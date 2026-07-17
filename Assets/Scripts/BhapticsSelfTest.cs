// Diagnostic: logs bHaptics connection state and buzzes the gloves on a loop,
// completely independent of AutoHand / FingerTipContact. Attach to any GameObject
// in the scene (alongside the [bhaptics] prefab).
//
// Read the output with:   adb logcat -s Unity
// Look for lines tagged [SelfTest].
//
// It tells you WHICH layer is failing:
//   - IsInitialized False      -> the [bhaptics] prefab isn't in the scene / didn't run
//   - IsBhapticsAvailable False-> SDK ran but no glove/Player reachable
//   - device list empty        -> nothing paired to this headset
//   - device Connected False   -> paired but not currently connected (still on PC?)
//   - all True but no buzz      -> hardware/motor issue
// If the gloves buzz here but NOT when you touch the desk -> the problem is the
// contact system (mask/radius/layers), not bHaptics.

using System.Collections;
using System.Text;
using UnityEngine;
using Bhaptics.SDK2;

namespace FingerPaint
{
    public class BhapticsSelfTest : MonoBehaviour
    {
        [Tooltip("Seconds to wait after start before the first test (lets the SDK init).")]
        [SerializeField] private float _startDelay = 2f;
        [Tooltip("Seconds between test cycles. Set 0 to test only once.")]
        [SerializeField] private float _interval = 4f;
        [Tooltip("Motor intensity used for the test buzz (1-100).")]
        [Range(1, 100)]
        [SerializeField] private int _intensity = 100;
        [Tooltip("Buzz length per finger, ms.")]
        [SerializeField] private int _durationMillis = 300;

        private void Start()
        {
            StartCoroutine(TestLoop());
        }

        private IEnumerator TestLoop()
        {
            yield return new WaitForSeconds(_startDelay);

            do
            {
                LogStatus();
                yield return PulseGlove(PositionType.GloveL);
                yield return PulseGlove(PositionType.GloveR);

                // PingAll makes every CONNECTED bHaptics device buzz briefly -- the
                // definitive "is the hardware reachable" check.
                Debug.Log("[SelfTest] PingAll() -> connected devices should buzz now.");
                BhapticsLibrary.PingAll();

                if (_interval > 0f) yield return new WaitForSeconds(_interval);
            }
            while (_interval > 0f);
        }

        private void LogStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SelfTest] ---- bHaptics status ----");
            sb.AppendLine($"[SelfTest] BhapticsSDK2.IsInitialized = {BhapticsSDK2.IsInitialized}");
            sb.AppendLine($"[SelfTest] IsBhapticsAvailable      = {BhapticsLibrary.IsBhapticsAvailable(false)}");
            sb.AppendLine($"[SelfTest] IsConnect(GloveL)        = {BhapticsLibrary.IsConnect(PositionType.GloveL)}");
            sb.AppendLine($"[SelfTest] IsConnect(GloveR)        = {BhapticsLibrary.IsConnect(PositionType.GloveR)}");

            var devices = BhapticsLibrary.GetDevices();
            if (devices == null || devices.Count == 0)
            {
                sb.AppendLine("[SelfTest] GetDevices() = EMPTY (nothing paired to this headset)");
            }
            else
            {
                sb.AppendLine($"[SelfTest] GetDevices() = {devices.Count} device(s):");
                foreach (var d in devices)
                {
                    sb.AppendLine($"[SelfTest]   - {d.DeviceName} pos={d.Position} " +
                                  $"paired={d.IsPaired} connected={d.IsConnected} battery={d.Battery}");
                }
            }
            Debug.Log(sb.ToString());
        }

        // Buzz thumb..pinky in sequence on one glove.
        private IEnumerator PulseGlove(PositionType glove)
        {
            Debug.Log($"[SelfTest] Pulsing {glove} motors 0-4 (thumb->pinky).");
            for (int motor = 0; motor < 5; motor++)
            {
                int result = BhapticsLibrary.PlaySingleMotor(glove, motor, _intensity, _durationMillis);
                Debug.Log($"[SelfTest]   {glove} motor {motor} -> PlaySingleMotor returned {result}");
                yield return new WaitForSeconds(_durationMillis / 1000f + 0.05f);
            }
        }
    }
}
