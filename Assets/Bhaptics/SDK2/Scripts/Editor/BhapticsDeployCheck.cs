using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Bhaptics.SDK2.Scripts.Editor
{
    public static class BhapticsDeployCheck
    {
        // EditorPrefs key holding the deploy version the user chose to stop being notified about.
        public const string DismissVersionKey = "bHaptics.DeployDismissedVersion";

        // Published from the background fetch to the main-thread UI poller (DrainResult).
        private static volatile bool _resultReady;
        private static bool _showPopup;
        private static int _latest;
        private static int _embedded;

        public static void WarnIfStale(string phase)
        {
            var settings = BhapticsSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.AppId) || string.IsNullOrEmpty(settings.ApiKey))
            {
                return;
            }

            var appId = settings.AppId;
            var apiKey = settings.ApiKey;
            var embeddedVersion = settings.LastDeployVersion;

            // Run the blocking native HTTP query off the main thread; result only feeds a log.
            Task.Run(() =>
            {
                try
                {
                    var json = BhapticsEditorUtils.EditorGetSettings(appId, apiKey, embeddedVersion, out int code);
                    if (code != 0)
                    {
                        return;
                    }

                    var message = DeployHttpMessage.CreateFromJSON(json).message;
                    var latest = message != null ? message.version : -1;
                    Debug.LogWarning(string.Format(
                        "[bHaptics] {0}: New haptics (v{1}) have been deployed; this build bundles v{2} as the offline fallback. " +
                        "Online users get the latest automatically; offline users get the bundled v{2}. " +
                        "(Online/offline here means whether the bHaptics server is reachable at runtime - it can be blocked by a firewall or a closed lab/enterprise network even with internet access.) " +
                        "Press 'Refresh' in the bHaptics Settings window and commit before shipping to bundle the latest.",
                        phase, latest, embeddedVersion));
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }

        // Editor-load check: fetch off the main thread, then show a popup on the main thread if stale.
        public static void CheckStaleAsync()
        {
            var settings = BhapticsSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.AppId) || string.IsNullOrEmpty(settings.ApiKey))
            {
                return;
            }

            var appId = settings.AppId;
            var apiKey = settings.ApiKey;
            var embeddedVersion = settings.LastDeployVersion;

            _resultReady = false;
            _showPopup = false;
            EditorApplication.update += DrainResult;

            Task.Run(() =>
            {
                try
                {
                    var json = BhapticsEditorUtils.EditorGetSettings(appId, apiKey, embeddedVersion, out int code);
                    if (code == 0)
                    {
                        var message = DeployHttpMessage.CreateFromJSON(json).message;
                        _latest = message != null ? message.version : -1;
                        _embedded = embeddedVersion;
                        _showPopup = true;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    _resultReady = true;
                }
            });
        }

        // Runs on the main thread (EditorApplication.update); marshals the popup back from the worker.
        private static void DrainResult()
        {
            if (!_resultReady)
            {
                return;
            }

            EditorApplication.update -= DrainResult;
            if (_showPopup && EditorPrefs.GetInt(DismissVersionKey, 0) < _latest)
            {
                BhapticsDeployPopup.Show(_latest, _embedded);
            }
        }

        // Refreshes the embedded deploy in place (same effect as 'Refresh' in the settings window):
        // updates DefaultDeploy, the event list, and the generated BhapticsEvent.cs, then saves.
        public static bool UpdateEmbeddedDeploy(out int version, out string error)
        {
            version = -1;
            var settings = BhapticsSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.AppId) || string.IsNullOrEmpty(settings.ApiKey))
            {
                error = "AppId/ApiKey is not set.";
                return false;
            }

            var appId = settings.AppId;
            var apiKey = settings.ApiKey;

            var json = BhapticsEditorUtils.EditorGetSettings(appId, apiKey, -1, out int code);
            if (code != 0)
            {
                error = BhapticsHelpers.ErrorCodeToMessage(code);
                return false;
            }

            var events = BhapticsEditorUtils.EditorGetEventList(appId, apiKey, -1, out code);
            if (code != 0)
            {
                error = BhapticsHelpers.ErrorCodeToMessage(code);
                return false;
            }

            try
            {
                var message = DeployHttpMessage.CreateFromJSON(json).message;
                if (message == null || message.version <= 0)
                {
                    error = "Not Valid format";
                    return false;
                }

                settings.AppName = message.name;
                settings.LastDeployVersion = message.version;
                settings.DefaultDeploy = json;

                var eventNames = new string[events.Count];
                var eventDataArr = new MappingMetaData[events.Count];
                for (var i = 0; i < events.Count; i++)
                {
                    eventNames[i] = events[i].key;
                    eventDataArr[i] = events[i];
                }
                settings.EventData = eventDataArr;

                BhapticsEventGenerator.CreateEventCsFile("BhapticsEvent", eventNames);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                version = message.version;
                error = null;
                return true;
            }
            catch (System.Exception e)
            {
                error = "Exception: " + e.Message;
                return false;
            }
        }
    }

    public class BhapticsBuildDeployCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (Application.isBatchMode)
            {
                return;
            }
            BhapticsDeployCheck.WarnIfStale("Build");
        }
    }

    [InitializeOnLoad]
    public static class BhapticsEditorDeployCheck
    {
        static BhapticsEditorDeployCheck()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || SessionState.GetBool("bHaptics.DeployChecked", false))
                {
                    return;
                }
                SessionState.SetBool("bHaptics.DeployChecked", true);
                BhapticsDeployCheck.CheckStaleAsync();
            };
        }
    }

    public class BhapticsDeployPopup : EditorWindow
    {
        private int _latest;
        private int _embedded;
        private Texture2D _logo;
        private bool _dontShowAgain;
        private string _status;

        public static void Show(int latest, int embedded)
        {
            var window = CreateInstance<BhapticsDeployPopup>();
            window.titleContent = new GUIContent("bHaptics");
            window._latest = latest;
            window._embedded = embedded;

            var size = new Vector2(480f, 360f);
            window.minSize = size;
            window.maxSize = size;
            var res = Screen.currentResolution;
            window.position = new Rect((res.width - size.x) * 0.5f, (res.height - size.y) * 0.5f, size.x, size.y);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            // bHaptics brand mark instead of Unity's default dialog icon; pick by editor skin.
            var key = EditorGUIUtility.isProSkin ? "Primary_white" : "Primary_black";
            _logo = Resources.Load<Texture2D>(key);
        }

        private void OnDestroy()
        {
            if (_dontShowAgain)
            {
                EditorPrefs.SetInt(BhapticsDeployCheck.DismissVersionKey, _latest);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16f);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(16f);
                    if (_logo != null)
                    {
                        var rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
                        GUI.DrawTexture(rect, _logo, ScaleMode.ScaleToFit);
                    }
                    GUILayout.Space(12f);

                    EditorGUILayout.LabelField(
                        string.Format("New haptics (v{0}) have been deployed.", _latest),
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        "When your users are online, the latest version is used automatically.",
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(
                        string.Format("When your users are offline, the version bundled in this build (v{0}) is used as a fallback.", _embedded),
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(
                        "\"Online/offline\" here means whether the bHaptics server is reachable at runtime - " +
                        "it can be blocked by a firewall or a closed lab/enterprise network even with internet access.",
                        EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(4f);
                    EditorGUILayout.LabelField(
                        string.Format("Click Update to bundle the latest (v{0}) as the fallback before shipping.", _latest),
                        EditorStyles.wordWrappedLabel);

                    GUILayout.Space(8f);
                    _dontShowAgain = EditorGUILayout.ToggleLeft(
                        string.Format("Don't show this again for v{0}", _latest), _dontShowAgain);

                    if (!string.IsNullOrEmpty(_status))
                    {
                        EditorGUILayout.HelpBox(_status, MessageType.Error);
                    }

                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Later", GUILayout.Width(80f)))
                        {
                            Close();
                        }
                        if (GUILayout.Button("Update", GUILayout.Width(120f)))
                        {
                            if (BhapticsDeployCheck.UpdateEmbeddedDeploy(out int version, out string error))
                            {
                                Debug.Log(string.Format(
                                    "[bHaptics] Embedded deploy updated to v{0}. Commit the change to ship it.", version));
                                Close();
                            }
                            else
                            {
                                _status = "Update failed: " + error;
                                Debug.LogWarning("[bHaptics] Deploy update failed: " + error);
                            }
                        }
                    }
                    GUILayout.Space(16f);
                }
                GUILayout.Space(16f);
            }
        }
    }
}
