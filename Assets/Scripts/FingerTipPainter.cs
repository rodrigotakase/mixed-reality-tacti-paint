// Paints while this fingertip touches a surface, using whatever FingerPaintBrush
// is assigned. Reads contact + finger identity from the FingerTipContact on the
// same object. Each finger gets its own color (auto-assigned per finger unless you
// override it).

using UnityEngine;

namespace FingerPaint
{
    [RequireComponent(typeof(FingerTipContact))]
    public class FingerTipPainter : MonoBehaviour
    {
        [Tooltip("Painting strategy (LineRendererBrush now; TextureBrush etc. later). " +
                 "Assign the shared brush object in the scene.")]
        [SerializeField] private FingerPaintBrush _brush;

        [Tooltip("This finger's paint color. Alpha 0 = auto-pick from the finger id.")]
        [SerializeField] private Color _color = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Stroke width in metres.")]
        [SerializeField] private float _width = 0.005f;

        [Tooltip("Minimum distance (m) the tip must move before a new stroke point is " +
                 "added. Keeps strokes light.")]
        [SerializeField] private float _minPointDistance = 0.003f;

        private FingerTipContact _contact;
        private IPaintStroke _stroke;
        private Vector3 _lastPoint;

        // Distinct default per finger so each leaves a different color.
        private static readonly Color[] DefaultPalette =
        {
            new Color(0.90f, 0.20f, 0.20f), // Thumb  - red
            new Color(0.20f, 0.75f, 0.30f), // Index  - green
            new Color(0.25f, 0.50f, 0.95f), // Middle - blue
            new Color(0.95f, 0.80f, 0.20f), // Ring   - yellow
            new Color(0.80f, 0.30f, 0.85f), // Pinky  - magenta
        };

        private void Awake()
        {
            _contact = GetComponent<FingerTipContact>();
            if (_color.a <= 0f)
            {
                _color = PaletteColor(_contact.Finger);
            }
        }

        /// <summary>When true, strokes use PaintModeManager.ActiveColor (set from
        /// the hand-menu palette) instead of the local color field.</summary>
        public bool UseGlobalColor { get; set; }

        /// <summary>Set up this painter from code (for tips spawned at runtime).
        /// Pass color with alpha 0 (or omit) to keep the per-finger palette.</summary>
        public void Configure(FingerPaintBrush brush, float width, Color color = default)
        {
            _brush = brush;
            _width = width;
            _color = color.a > 0f ? color : PaletteColor(_contact.Finger);
        }

        // Palm (and any future ids) fall back to the last palette entry.
        private static Color PaletteColor(FingerId finger)
        {
            return DefaultPalette[Mathf.Min((int)finger, DefaultPalette.Length - 1)];
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
            // Close any open stroke if we get disabled mid-paint.
            OnContactEnded(_contact);
        }

        // Slider-driven width when set; the configured width otherwise.
        private float EffectiveWidth =>
            PaintModeManager.StrokeWidth > 0f ? PaintModeManager.StrokeWidth : _width;

        private void OnContactStarted(FingerTipContact c)
        {
            if (PaintModeManager.Mode == PaintMode.Eraser)
            {
                MeshStampBrush.EraseAt(c.ContactPoint, EffectiveWidth);
                _lastPoint = c.ContactPoint;
                return;
            }
            if (PaintModeManager.Mode != PaintMode.Paint) return; // None: haptics only

            if (_brush == null)
            {
                Debug.LogWarning($"{name}: no FingerPaintBrush assigned; nothing to paint with.", this);
                return;
            }

            var info = new StrokeInfo
            {
                finger = c.Finger,
                color = UseGlobalColor ? PaintModeManager.ActiveColor : _color,
                width = EffectiveWidth,
                startPoint = c.ContactPoint,
                startNormal = c.ContactNormal,
                surface = c.CurrentCollider,
            };
            _stroke = _brush.BeginStroke(info);
            _lastPoint = c.ContactPoint;
        }

        private void OnContactStayed(FingerTipContact c)
        {
            if (PaintModeManager.Mode == PaintMode.Eraser)
            {
                if ((c.ContactPoint - _lastPoint).sqrMagnitude >=
                    _minPointDistance * _minPointDistance)
                {
                    MeshStampBrush.EraseAt(c.ContactPoint, EffectiveWidth);
                    _lastPoint = c.ContactPoint;
                }
                return;
            }

            if (_stroke == null) return;
            if ((c.ContactPoint - _lastPoint).sqrMagnitude < _minPointDistance * _minPointDistance)
            {
                return;
            }
            _stroke.AddPoint(c.ContactPoint, c.ContactNormal);
            _lastPoint = c.ContactPoint;
        }

        private void OnContactEnded(FingerTipContact c)
        {
            if (_stroke == null) return;
            _stroke.End();
            _stroke = null;
        }
    }
}
