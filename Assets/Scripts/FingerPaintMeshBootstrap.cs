// One-component setup for the FingerPaintMesh scene:
//
//  1. Spawns an INVISIBLE but COLLIDABLE version of the MRUK Global Mesh (the
//     depth-scanned room): an EffectMesh with no material (=> no renderer is
//     created) and Colliders on, stamped with the Paintable layer.
//  2. Finds the two tracked OVR hands (OVRSkeleton) and builds INDEX-FINGER
//     paint rigs -- only the index fingertip of each hand paints.
//  3. RIGHT index paints one color; LEFT index applies the assigned normal map
//     to what it touches -- a transparent bump overlay on top of existing
//     color, or WHITE paint carrying the normal map on bare surfaces. Both use
//     MeshStampBrush blob stamping (realtime mesh generation).
//
// Add this to one GameObject in the scene. Requires: MRUK in the scene, hand
// tracking enabled, and a "Paintable" layer in Tags & Layers.

using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FingerPaint
{
    public class FingerPaintMeshBootstrap : MonoBehaviour
    {
        [Header("Layers")]
        [SerializeField] private string _paintableLayerName = "Paintable";

        [Header("Painting")]
        [Tooltip("Stroke width in metres.")]
        [SerializeField] private float _strokeWidth = 0.025f;
        [Tooltip("Fingertip contact detection radius in metres. Keep small (~0.02) " +
                 "so only a deliberate fingertip touch paints -- large values make " +
                 "the whole hand near a surface trigger the index tip.")]
        [SerializeField] private float _contactRadius = 0.02f;
        [Tooltip("The single color the RIGHT index finger paints with.")]
        [SerializeField] private Color _rightHandColor = new Color(0.9f, 0.2f, 0.25f, 1f);
        [Tooltip("Normal map the LEFT index finger applies (e.g. normalMap1). " +
                 "Empty = procedural noise bump.")]
        [SerializeField] private Texture _reliefNormalMap;

        [Header("Hand menu palettes")]
        [Tooltip("Colors shown on the LEFT hand fingertips in Paint mode. Touch one " +
                 "with the right index finger to select the painting color.")]
        [SerializeField] private Color[] _paletteColors =
        {
            new Color(0.9f, 0.2f, 0.25f), new Color(0.95f, 0.75f, 0.2f), new Color(0.25f, 0.75f, 0.3f),
            new Color(0.25f, 0.5f, 0.95f), new Color(0.46f, 0.2f, 0.9f),
        };
        [Tooltip("Normal-map textures shown on the RIGHT hand fingertips in Paint " +
                 "mode. Touch one with the left index finger to select. Empty = uses " +
                 "the single Relief Normal Map.")]
        [SerializeField] private Texture[] _reliefTextures;
        [Tooltip("Haptic character of each relief texture, index-matched to Relief " +
                 "Textures (used while painting it and when feeling it in Feel mode).")]
        [SerializeField] private ReliefFeel[] _textureFeels =
        {
            ReliefFeel.Smooth, ReliefFeel.Rock, ReliefFeel.Blobs, ReliefFeel.Tiles, ReliefFeel.Waves,
        };
        [Tooltip("Placement/sizes of the wrist menu and palettes. Preview & tweak in " +
                 "the editor via GameObject > FingerPaint > Hand Menu Preview.")]
        [SerializeField] private HandMenuLayout _menuLayout = new HandMenuLayout();
        [Tooltip("White icon textures for the menu buttons (hand, brush, eraser, bin, menu).")]
        [SerializeField] private HandMenuIcons _menuIcons = new HandMenuIcons();

        // Read by the editor preview tool.
        public HandMenuLayout MenuLayout => _menuLayout;
        public HandMenuIcons MenuIcons => _menuIcons;
        public Color[] PaletteColors => _paletteColors;
        public Texture[] ReliefTextures => _reliefTextures;
        [Tooltip("Normal map repeats per metre of painted surface. Higher = finer, " +
                 "denser bump pattern.")]
        [SerializeField] private float _reliefTiling = 40f;

        [Header("Haptics")]
        [Tooltip("Tuning for both hands' glove feedback. smoothIntensity = right " +
                 "hand's continuous buzz while painting color. bumpsPerMeter / " +
                 "bumpBaseIntensity / bumpPeakIntensity = left hand's virtual bump " +
                 "grid while painting normal map (denser grid = more bumps felt).")]
        [SerializeField] private FingerTipHaptic.PaintHapticSettings _hapticSettings =
            FingerTipHaptic.PaintHapticSettings.Default;

        [Header("Hand visuals")]
        [Tooltip("Tint for the tracked hand meshes. The default hand material isn't " +
                 "URP-compatible (renders black), so it's replaced at runtime with a " +
                 "translucent material in this color, similar to the Quest system hands.")]
        [SerializeField] private Color _handTint = new Color(0.85f, 0.9f, 1f, 0.25f);

        [Header("Debug")]
        [Tooltip("Show the scanned room mesh as a translucent green surface, so you " +
                 "can see where the paintable surface actually is (scan offset, missing " +
                 "scan, etc). Turn off for the real experience.")]
        [SerializeField] private bool _debugShowRoomMesh = true;

        [Header("Optional material overrides (else auto-created from URP Lit)")]
        [SerializeField] private Material _colorPaintMaterial;
        [SerializeField] private Material _reliefPaintMaterial;

        private int _paintableLayer = -1;
        private MeshStampBrush _colorBrush;   // right hand
        private MeshStampBrush _reliefBrush;  // left hand
        private EffectMesh _roomEffectMesh;
        private bool _handsReady;
        private FloatingMenuController _floatingMenu;
        private bool _hmdWasRemoved;

        private void Start()
        {
            _paintableLayer = LayerMask.NameToLayer(_paintableLayerName);
            if (_paintableLayer < 0)
            {
                Debug.LogError($"{name}: layer \"{_paintableLayerName}\" doesn't exist " +
                               "(Project Settings > Tags and Layers).", this);
                enabled = false;
                return;
            }

            if (_reliefTextures == null || _reliefTextures.Length == 0)
            {
                _reliefTextures = new[] { _reliefNormalMap };
            }
            PaintModeManager.SetActiveColor(_rightHandColor);
            PaintModeManager.SetStrokeWidth(_strokeWidth);
            if (_textureFeels != null && _textureFeels.Length > 0)
            {
                PaintModeManager.TextureFeels = _textureFeels;
            }
            PaintModeManager.Changed += ApplyGlobalSelection;

            // Multimodal: hands + controllers tracked simultaneously, so the
            // Logitech MX Ink stylus can be used alongside hand tracking.
            if (OVRManager.instance != null)
            {
                OVRManager.instance.SimultaneousHandsAndControllersEnabled = true;
            }
            if (!OVRPlugin.SetSimultaneousHandsAndControllersEnabled(true))
            {
                Debug.LogWarning($"{name}: could not enable simultaneous hands + controllers.");
            }

            CreatePaintSurfaceLoader();
            CreateBrushes();
            ApplyGlobalSelection(); // sync brushes with texture slot 0 (may be empty = smooth)
            StartCoroutine(ApplyHandMaterials());

            // New-user handoff: when the headset comes off and is put back on,
            // wipe the painting and restart the intro (menu + video).
            _floatingMenu = FindFirstObjectByType<FloatingMenuController>(FindObjectsInactive.Include);
            OVRManager.HMDUnmounted += OnHmdUnmounted;
            OVRManager.HMDMounted += OnHmdMounted;

            if (MRUK.Instance != null)
            {
                MRUK.Instance.RegisterSceneLoadedCallback(LogSceneStatus);
            }
            else
            {
                Debug.LogError($"{name}: no MRUK in the scene -- the room mesh can never load.", this);
            }
        }

        // Replaces the hand mesh material (BasicHandMaterial is built-in-RP only
        // and renders black in URP) with a translucent Quest-style material.
        // OVRMeshRenderer creates/reassigns the SkinnedMeshRenderer material during
        // its own (late, tracking-dependent) initialization, so a single early
        // swap can get overwritten -- keep re-applying for the first seconds.
        private System.Collections.IEnumerator ApplyHandMaterials()
        {
            Material mat = CreateHandMaterial();
            float deadline = Time.time + 15f;
            var wait = new WaitForSeconds(0.5f);

            while (Time.time < deadline)
            {
                OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (OVRSkeleton skeleton in skeletons)
                {
                    foreach (var r in skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (r.sharedMaterial != mat)
                        {
                            r.sharedMaterial = mat;
                            Debug.Log($"{name}: applied translucent hand material to '{r.gameObject.name}'.");
                        }
                    }
                }
                yield return wait;
            }
        }

        private Material CreateHandMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _handTint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
            return mat;
        }

        // Prints exactly what MRUK loaded, so "toggle shows nothing" can be told
        // apart from "the scan never loaded".
        private void LogSceneStatus()
        {
            var rooms = MRUK.Instance.Rooms;
            Debug.Log($"{name}: MRUK scene loaded. Rooms: {rooms.Count}.");
            foreach (var room in rooms)
            {
                Debug.Log($"{name}:   room '{room.name}' anchors={room.Anchors.Count} " +
                          $"globalMesh={(room.GlobalMeshAnchor != null ? "YES" : "NO -- re-run Space Setup with mesh capture!")}");
                foreach (var anchor in room.Anchors)
                {
                    Debug.Log($"{name}:     anchor '{anchor.name}' label={anchor.Label}");
                }
            }
        }

        // EffectMesh quirk we rely on: with MeshMaterial == null it creates NO
        // MeshRenderer at all (fully invisible) but still generates MeshColliders
        // when Colliders is true. It also handles waiting for the MRUK scene load
        // by itself, so this works whether the room is loaded yet or not.
        private void CreatePaintSurfaceLoader()
        {
            var go = new GameObject("PaintableGlobalMesh");
            go.transform.SetParent(transform, false);
            _roomEffectMesh = go.AddComponent<EffectMesh>();
            _roomEffectMesh.SpawnOnStart = MRUK.RoomFilter.AllRooms;
            // Not just GLOBAL_MESH: Space Setup often stores furniture as classified
            // anchors (e.g. the desk as a TABLE cuboid) that are NOT part of the
            // global mesh -- include them so they're paintable too.
            _roomEffectMesh.Labels = MRUKAnchor.SceneLabels.GLOBAL_MESH
                                     | MRUKAnchor.SceneLabels.TABLE
                                     | MRUKAnchor.SceneLabels.FLOOR
                                     | MRUKAnchor.SceneLabels.WALL_FACE
                                     | MRUKAnchor.SceneLabels.CEILING
                                     | MRUKAnchor.SceneLabels.COUCH
                                     | MRUKAnchor.SceneLabels.STORAGE
                                     | MRUKAnchor.SceneLabels.BED
                                     | MRUKAnchor.SceneLabels.OTHER;
            _roomEffectMesh.Colliders = true;
            _roomEffectMesh.Layer = _paintableLayer;
            // Always assign the debug material so visibility can be toggled at
            // runtime; HideMesh alone controls whether it renders.
            _roomEffectMesh.MeshMaterial = CreateDebugMeshMaterial();
            _roomEffectMesh.HideMesh = !_debugShowRoomMesh;
        }

        private void OnDestroy()
        {
            PaintModeManager.Changed -= ApplyGlobalSelection;
            OVRManager.HMDUnmounted -= OnHmdUnmounted;
            OVRManager.HMDMounted -= OnHmdMounted;
        }

        private void OnHmdUnmounted()
        {
            _hmdWasRemoved = true;
        }

        private void OnHmdMounted()
        {
            if (!_hmdWasRemoved) return; // first mount at app start: nothing to reset
            _hmdWasRemoved = false;
            ResetForNewUser();
        }

        /// <summary>Fresh start for the next person wearing the headset: all paint
        /// removed, defaults restored, floating intro menu shown, video from 0.</summary>
        public void ResetForNewUser()
        {
            MeshStampBrush.ClearAllPaint();
            PaintModeManager.SetMode(PaintMode.Paint);
            PaintModeManager.SetActiveColor(_rightHandColor);
            PaintModeManager.SetActiveTextureIndex(0);
            PaintModeManager.SetStrokeWidth(_strokeWidth);

            if (_floatingMenu != null)
            {
                _floatingMenu.Show();
                var video = _floatingMenu.GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true);
                if (video != null)
                {
                    video.Stop();
                    video.Play();
                }
            }
            Debug.Log($"{name}: reset for a new user (paint cleared, intro restarted).");
        }

        // Pushes the menu's texture selection into both brushes (both can create
        // bumped surfaces, so both need the active normal map).
        private void ApplyGlobalSelection()
        {
            int i = Mathf.Clamp(PaintModeManager.ActiveTextureIndex, 0, _reliefTextures.Length - 1);
            Texture tex = _reliefTextures[i];
            if (_colorBrush != null) _colorBrush.SetActiveNormalMap(i, tex);
            if (_reliefBrush != null) _reliefBrush.SetActiveNormalMap(i, tex);
        }

        /// <summary>Show/hide the scanned room mesh. Also bound to the A / X
        /// controller buttons for on-device alignment checks.</summary>
        public void ToggleRoomMeshVisible()
        {
            if (_roomEffectMesh == null) return;
            _roomEffectMesh.HideMesh = !_roomEffectMesh.HideMesh;
            Debug.Log($"{name}: room mesh {(_roomEffectMesh.HideMesh ? "hidden" : "VISIBLE")}.");
        }

        // Translucent green so the scanned surface is visible through passthrough
        // without hiding the real room.
        private static Material CreateDebugMeshMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.1f, 1f, 0.3f, 0.25f));
            return mat;
        }

        private void CreateBrushes()
        {
            MeshStampBrush.ClearCoverage();
            // Both brushes share the same base offset: stamp ordering (newer paint
            // on top) is handled by the per-stamp layer epsilon inside the brush.
            // Both get the normal map -- the color brush needs it too, to keep the
            // bumps when coloring over relief-painted areas.
            _colorBrush = CreateBrush("ColorBrush (right index)",
                MeshStampBrush.StampMode.Color, _colorPaintMaterial);
            _reliefBrush = CreateBrush("NormalReliefBrush (left index)",
                MeshStampBrush.StampMode.NormalRelief, _reliefPaintMaterial);
            _colorBrush.SetNormalMaps(_reliefTextures);
            _reliefBrush.SetNormalMaps(_reliefTextures);
        }

        private MeshStampBrush CreateBrush(string label, MeshStampBrush.StampMode mode,
            Material baseMat)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var brush = go.AddComponent<MeshStampBrush>();
            brush.Setup(mode, baseMat, 0.003f, _reliefNormalMap, _reliefTiling);
            return brush;
        }

        // Skeletons initialize asynchronously once tracking starts -- keep looking
        // until both hands are rigged.
        private void Update()
        {
            // A (right controller) or X (left controller) toggles the scanned room
            // mesh so its alignment with the real room can be checked on device.
            // The controller must be polled EXPLICITLY: parameterless GetDown only
            // reads the "active" controller, which is Hands while hand tracking
            // runs, so button presses would be ignored.
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch) ||
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
            {
                ToggleRoomMeshVisible();
            }

            if (_handsReady) return;

            OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            int rigged = 0;
            foreach (OVRSkeleton skeleton in skeletons)
            {
                var type = skeleton.GetSkeletonType();
                bool isLeft = type == OVRSkeleton.SkeletonType.HandLeft ||
                              type == OVRSkeleton.SkeletonType.XRHandLeft;
                bool isRight = type == OVRSkeleton.SkeletonType.HandRight ||
                               type == OVRSkeleton.SkeletonType.XRHandRight;
                if (!isLeft && !isRight) continue;

                rigged++;
                if (skeleton.GetComponent<HandPaintRig>() != null) continue;

                var rig = skeleton.gameObject.AddComponent<HandPaintRig>();
                rig.Init(
                    skeleton,
                    isLeft ? GloveSide.Left : GloveSide.Right,
                    isLeft ? _reliefBrush : _colorBrush,
                    1 << _paintableLayer,
                    _contactRadius,
                    _strokeWidth,
                    isLeft ? default : _rightHandColor,
                    isLeft ? FingerTipHaptic.Style.BumpGrid : FingerTipHaptic.Style.SmoothFlow,
                    _hapticSettings);
                Debug.Log($"{name}: added index-finger paint rig to {(isLeft ? "LEFT" : "RIGHT")} hand " +
                          $"({(isLeft ? "normal map" : "color")} painting).");
            }

            if (rigged >= 2)
            {
                _handsReady = true;
                CreateHandMenus(skeletons);
            }
        }

        private void CreateHandMenus(OVRSkeleton[] skeletons)
        {
            OVRSkeleton left = null, right = null;
            foreach (OVRSkeleton s in skeletons)
            {
                var t = s.GetSkeletonType();
                if (t == OVRSkeleton.SkeletonType.HandLeft || t == OVRSkeleton.SkeletonType.XRHandLeft) left = s;
                if (t == OVRSkeleton.SkeletonType.HandRight || t == OVRSkeleton.SkeletonType.XRHandRight) right = s;
            }
            if (left == null || right == null) return;

            if (left.GetComponent<HandToolMenu>() == null)
            {
                left.gameObject.AddComponent<HandToolMenu>()
                    .Init(GloveSide.Left, left, right, _paletteColors, _reliefTextures, _menuLayout, _menuIcons);
            }
            if (right.GetComponent<HandToolMenu>() == null)
            {
                right.gameObject.AddComponent<HandToolMenu>()
                    .Init(GloveSide.Right, right, left, _paletteColors, _reliefTextures, _menuLayout, _menuIcons);
            }
            Debug.Log($"{name}: hand menus created on both wrists.");
        }
    }
}
