// Drives one bHaptics TactGlove motor from fingertip contact.
//
// Texture feels (used by the LEFT hand while painting relief, and by BOTH hands
// in Feel mode when touching relief that was painted with that texture):
//   Smooth -> gentle continuous hum, no events.
//   Rock   -> irregular ticks: jittered spacing, intensity and duration.
//   Blobs  -> regular soft "pops" (round attack, medium density).
//   Tiles  -> long silent flats + one hard sharp click per grout line.
//   Waves  -> slow swells: intensity follows a sine across the surface,
//             direction-dependent (along crests = steady, across = pulsing).
//
// Styles per hand while painting: SmoothFlow (color hand), BumpGrid (relief
// hand -> plays the ACTIVE texture's feel). Feel mode reads the texture id
// painted at the touched point and plays THAT feel. Eraser mode: strong buzz
// while scrubbing paint.
//
// TactGlove motors: 0=thumb 1=index 2=middle 3=ring 4=pinky 5=wrist.

using UnityEngine;
using Bhaptics.SDK2;

namespace FingerPaint
{
    [RequireComponent(typeof(FingerTipContact))]
    public class FingerTipHaptic : MonoBehaviour
    {
        public enum Style { PulseOnTouch, SmoothFlow, BumpGrid }

        [System.Serializable]
        public struct PaintHapticSettings
        {
            [Tooltip("SmoothFlow: constant vibration intensity while painting (1-100).")]
            [Range(1, 100)] public int smoothIntensity;

            [Tooltip("Blobs feel: bump lines per metre. Higher = denser bump grid.")]
            public float bumpsPerMeter;
            [Tooltip("Soft hum between bumps while moving (0 = silent between bumps).")]
            [Range(0, 100)] public int bumpBaseIntensity;
            [Tooltip("Peak intensity when crossing a bump line (1-100).")]
            [Range(1, 100)] public int bumpPeakIntensity;
            [Tooltip("Peak pulse length in ms.")]
            public int bumpPeakDurationMs;
            [Tooltip("Below this fingertip speed (m/s) the finger counts as stopped " +
                     "-> no vibration.")]
            public float minSpeed;

            public static PaintHapticSettings Default => new PaintHapticSettings
            {
                smoothIntensity = 30,
                bumpsPerMeter = 60f,
                bumpBaseIntensity = 15,
                bumpPeakIntensity = 100,
                bumpPeakDurationMs = 45,
                minSpeed = 0.015f,
            };
        }

        [SerializeField] private Style _style = Style.PulseOnTouch;
        [SerializeField] private PaintHapticSettings _settings = PaintHapticSettings.Default;

        [Header("PulseOnTouch (legacy)")]
        [Range(1, 100)]
        [SerializeField] private int _intensity = 100;
        [SerializeField] private int _durationMillis = 100;
        [SerializeField] private bool _pulseWhileTouching = true;
        [SerializeField] private float _pulseInterval = 0.12f;

        private const float BaseRefreshInterval = 0.09f; // s, keeps "continuous" buzz alive

        private FingerTipContact _contact;
        private PositionType _position;
        private float _lastSendTime = -999f;
        private float _lastPeakTime = -999f;
        private float _lastPeakDuration;

        // movement / surface-grid state
        private Vector3 _lastPoint;
        private float _lastPointTime;
        private bool _hasLastPoint;
        private int _lastCellU, _lastCellV;

        private void Awake()
        {
            _contact = GetComponent<FingerTipContact>();
            _position = _contact.Side == GloveSide.Left ? PositionType.GloveL : PositionType.GloveR;
        }

        /// <summary>When true this finger only gives feedback in Feel mode --
        /// used for the non-index fingers, which never paint.</summary>
        public bool FeelModeOnly { get; set; }

        /// <summary>Set style + tuning from code (for tips spawned at runtime).</summary>
        public void Configure(Style style, PaintHapticSettings settings)
        {
            _style = style;
            _settings = settings;
        }

        private void OnEnable()
        {
            _contact.ContactStarted += OnContactStarted;
            _contact.ContactStayed += OnContactStayed;
            _contact.ContactEnded += OnContactEnded;
        }

        private void OnDisable()
        {
            _contact.ContactStarted -= OnContactStarted;
            _contact.ContactStayed -= OnContactStayed;
            _contact.ContactEnded -= OnContactEnded;
        }

        private void OnContactStarted(FingerTipContact c)
        {
            _hasLastPoint = false;
            if (_style == Style.PulseOnTouch && PaintModeManager.Mode == PaintMode.Paint)
            {
                Send(_intensity, _durationMillis);
            }
        }

        private void OnContactEnded(FingerTipContact c)
        {
            _hasLastPoint = false;
        }

        private void OnContactStayed(FingerTipContact c)
        {
            if (FeelModeOnly && PaintModeManager.Mode != PaintMode.None) return;

            // Global rule: haptics only while the fingertip is MOVING on the
            // surface. A resting finger gets no feedback in any mode or style.
            if (!UpdateMotion(c)) return;

            switch (PaintModeManager.Mode)
            {
                case PaintMode.None:
                    // Feel what's painted here: the exact texture's profile over
                    // relief, smooth hum over color, silence on bare surface.
                    if (MeshStampBrush.TryGetReliefTextureAt(c.ContactPoint, out int texId))
                    {
                        UpdateTextureFeel(c, PaintModeManager.FeelOfTexture(texId));
                    }
                    else if (MeshStampBrush.HasColorAt(c.ContactPoint))
                    {
                        SmoothRefresh(_settings.smoothIntensity);
                    }
                    break;

                case PaintMode.Eraser:
                    if (MeshStampBrush.HasColorAt(c.ContactPoint) ||
                        MeshStampBrush.HasReliefAt(c.ContactPoint))
                    {
                        SmoothRefresh(70);
                    }
                    break;

                default: // Paint
                    switch (_style)
                    {
                        case Style.PulseOnTouch:
                            if (_pulseWhileTouching && Time.time - _lastSendTime >= _pulseInterval)
                            {
                                Send(_intensity, _durationMillis);
                            }
                            break;

                        case Style.SmoothFlow:
                            SmoothRefresh(_settings.smoothIntensity);
                            break;

                        case Style.BumpGrid:
                            // Relief hand: play the feel of the texture being painted.
                            UpdateTextureFeel(c,
                                PaintModeManager.FeelOfTexture(PaintModeManager.ActiveTextureIndex));
                            break;
                    }
                    break;
            }
        }

        // ------------------------------------------------------ texture feels

        // Tracks fingertip motion on the surface. Returns true only while the
        // finger moves faster than minSpeed -- ALL haptic output is gated on it.
        private bool UpdateMotion(FingerTipContact c)
        {
            Vector3 point = c.ContactPoint;
            float now = Time.time;

            if (!_hasLastPoint)
            {
                _hasLastPoint = true;
                _lastPoint = point;
                _lastPointTime = now;
                _lastCellU = int.MinValue;
                _lastCellV = int.MinValue;
                return false;
            }

            float dt = now - _lastPointTime;
            if (dt <= 0f) return false;
            float speed = (point - _lastPoint).magnitude / dt;
            _lastPoint = point;
            _lastPointTime = now;
            return speed >= _settings.minSpeed;
        }

        private void UpdateTextureFeel(FingerTipContact c, ReliefFeel feel)
        {
            Vector3 point = c.ContactPoint;
            Vector3 normal = c.ContactNormal;

            // Same surface-plane basis the paint UVs use, so the felt pattern is
            // anchored to the surface, not the hand.
            Vector3 tangent = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            float u = Vector3.Dot(point, tangent);
            float v = Vector3.Dot(point, bitangent);

            // Design rule learned from Tiles (the one that feels best): CONTRAST.
            // Dead silence between events, sharp distinct peaks at events. No
            // background hum anywhere -- hum blurs everything into "generic
            // vibration". The feels differ by event density, regularity,
            // sharpness and direction.
            switch (feel)
            {
                case ReliefFeel.Smooth:
                    // The one intentional exception: a barely-there whisper.
                    SmoothRefresh(8);
                    break;

                case ReliefFeel.Rock:
                    // Dense IRREGULAR crackle: short hard ticks at ~1.4cm with
                    // hash-jittered strength/length. Silence in between.
                    CellTicks(u, v, 70f, jittered: true,
                        peak: 0, durationMs: 0, baseline: 0);
                    break;

                case ReliefFeel.Blobs:
                    // Regular round pops: clearly separated pop..pop..pop.
                    CellTicks(u, v, _settings.bumpsPerMeter, jittered: false,
                        peak: 85, durationMs: 40, baseline: 0);
                    break;

                case ReliefFeel.Tiles:
                    // Sparse grout lines: dead-flat silence, then one hard click.
                    CellTicks(u, v, 12f, jittered: false,
                        peak: 100, durationMs: 30, baseline: 0);
                    break;

                case ReliefFeel.Waves:
                    // MANY close ripples: parallel crest LINES at 30/metre --
                    // crossing them gives a fast tick-tick-tick rhythm, softer
                    // and rounder than tiles' clicks; moving along a crest is
                    // quieter. (1D lines, so it's also direction-dependent.)
                    CellTicks(u, 0f, 30f, jittered: false,
                        peak: 60, durationMs: 35, baseline: 0);
                    break;
            }
        }

        // Surface-anchored cell grid: fires a peak when the fingertip crosses into
        // a new cell, with an optional hum between peaks. Jittered mode derives
        // intensity/duration from a per-cell hash, so rock feels irregular but
        // deterministic (the same spot always feels the same).
        private void CellTicks(float u, float v, float cellsPerMeter, bool jittered,
            int peak, int durationMs, int baseline)
        {
            int cellU = Mathf.FloorToInt(u * cellsPerMeter);
            int cellV = Mathf.FloorToInt(v * cellsPerMeter);
            bool crossed = cellU != _lastCellU || cellV != _lastCellV;
            bool hadCell = _lastCellU != int.MinValue;
            _lastCellU = cellU;
            _lastCellV = cellV;

            if (crossed && hadCell)
            {
                int hitIntensity = peak;
                int hitDuration = durationMs;
                if (jittered)
                {
                    int h = (cellU * 73856093) ^ (cellV * 19349663);
                    h &= 0x7fffffff;
                    hitIntensity = 30 + h % 71;         // 30..100: wide contrast
                    hitDuration = 12 + (h >> 8) % 22;   // 12..33 ms: short, crisp
                }
                Send(hitIntensity, hitDuration);
                _lastPeakTime = Time.time;
                _lastPeakDuration = hitDuration / 1000f;
                return;
            }

            bool peakActive = Time.time - _lastPeakTime < _lastPeakDuration;
            if (!peakActive && baseline > 0)
            {
                SmoothRefresh(baseline);
            }
        }

        // Refreshes a short buzz slightly longer than the interval, so
        // back-to-back sends overlap into one continuous vibration.
        private void SmoothRefresh(int intensity)
        {
            if (Time.time - _lastSendTime >= BaseRefreshInterval)
            {
                Send(intensity, Mathf.RoundToInt(BaseRefreshInterval * 1000f) + 40);
            }
        }

        private void Send(int intensity, int durationMillis)
        {
            _lastSendTime = Time.time;
            BhapticsLibrary.PlaySingleMotor(
                _position, (int)_contact.Finger, Mathf.Clamp(intensity, 1, 100), durationMillis);
        }

        /// <summary>Fires this fingertip's glove motor once (legacy hook).</summary>
        public void PlayHaptic() => Send(_intensity, _durationMillis);
    }
}
