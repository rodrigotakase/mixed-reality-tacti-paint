// Edit-mode preview of the wrist menu + fingertip palettes, so the layout can
// be tweaked visually without deploying to the headset.
//
// Create via GameObject > FingerPaint > Hand Menu Preview. The preview reads
// the HandMenuLayout (and palettes) LIVE from the FingerPaintSetup bootstrap in
// the scene -- tweak the values on FingerPaintSetup's "Menu Layout" section and
// the preview rebuilds itself. What you see is exactly what runs on device.
//
// The preview object's transform is the fake hand: position = wrist,
// blue axis (forward) = finger direction, green axis (up) = palm normal.
// Preview children are never saved into the scene or builds.

using UnityEngine;

namespace FingerPaint
{
    [ExecuteAlways]
    public class HandMenuPreview : MonoBehaviour
    {
        [Tooltip("Which hand to preview (left shows color swatches, right shows textures).")]
        [SerializeField] private GloveSide _side = GloveSide.Left;
        [Tooltip("Show the mode panel as if the wrist button had been pressed.")]
        [SerializeField] private bool _menuOpen = true;
        [Tooltip("Bootstrap to read layout/palettes from. Empty = auto-find in scene.")]
        [SerializeField] private FingerPaintMeshBootstrap _source;

        private Transform _root;
        private string _builtState = "";

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying)
            {
                Clear();
                return;
            }

            if (_source == null)
            {
                _source = FindFirstObjectByType<FingerPaintMeshBootstrap>();
                if (_source == null) return;
            }

            // Rebuild whenever anything that affects the look changes.
            string state = JsonUtility.ToJson(_source.MenuLayout)
                           + (int)_side + _menuOpen
                           + (_source.PaletteColors?.Length ?? 0)
                           + (_source.ReliefTextures?.Length ?? 0)
                           + transform.position + transform.rotation.eulerAngles;
            if (state == _builtState) return;
            _builtState = state;

            Clear();
            Build();
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            if (_root != null)
            {
                DestroyImmediate(_root.gameObject);
                _root = null;
                _builtState = "";
            }
        }

        private void Build()
        {
            HandMenuLayout layout = _source.MenuLayout;
            var rootGo = new GameObject("Preview (not saved)");
            rootGo.hideFlags = HideFlags.DontSave;
            _root = rootGo.transform;
            _root.SetParent(transform, false);

            // Fake hand frame from this object's transform.
            Vector3 wrist = transform.position;
            Vector3 fingerDir = transform.forward;
            Vector3 palmOut = transform.up;
            Vector3 rowDir = transform.right;
            Vector3 middleProx = wrist + fingerDir * 0.09f;
            Vector3 palmCenter = Vector3.Lerp(wrist, middleProx, 0.55f);
            Quaternion facing = Quaternion.LookRotation(-palmOut, fingerDir);

            // Palm reference plate so proportions are readable.
            GameObject palm = HandToolMenu.CreateDisc("PalmReference", 1f,
                new Color(0.25f, 0.28f, 0.32f), _root);
            palm.transform.SetPositionAndRotation(palmCenter, facing);
            palm.transform.localScale = new Vector3(0.08f, 0.11f, 1f);

            HandMenuIcons icons = _source.MenuIcons ?? new HandMenuIcons();

            // Wrist button (left hand only; right has just the slider).
            if (_side == GloveSide.Left)
            {
                GameObject wb = HandToolMenu.CreateButton("WristButton",
                    layout.wristButtonSize, layout.wristButtonColor, icons.menu, "Menu",
                    0.034f * layout.labelScale, _root, icons.iconMaterial);
                wb.transform.SetPositionAndRotation(
                    wrist + palmOut * layout.wristButtonPalmOffset - fingerDir * layout.wristButtonForearmOffset,
                    facing);
            }

            // Mode panel.
            if (_menuOpen && _side == GloveSide.Left)
            {
                string[] labels = { "Feel", "Paint", "Erase", "Clear" };
                Texture[] modeIcons = { icons.feelHand, icons.paintBrush, icons.eraser, icons.clearBin };
                Color[] tints =
                {
                    new Color(0.55f, 0.6f, 0.65f), new Color(0.25f, 0.6f, 0.95f),
                    new Color(0.95f, 0.45f, 0.35f), new Color(0.45f, 0.16f, 0.18f),
                };
                Vector3 panelCenter = palmCenter + palmOut * layout.panelPalmOffset;
                for (int i = 0; i < 4; i++)
                {
                    GameObject b = HandToolMenu.CreateButton($"Mode_{labels[i]}",
                        i == 1 ? layout.modeButtonSelectedSize : layout.modeButtonSize,
                        tints[i], modeIcons[i], labels[i], 0.036f * layout.labelScale, _root, icons.iconMaterial);
                    b.transform.SetPositionAndRotation(
                        panelCenter + rowDir * ((i - 1.5f) * layout.modeButtonSpacing), facing);
                }
            }

            // Stroke-width slider (right wrist only).
            if (_side == GloveSide.Right)
            {
                Vector3 sliderCenter = wrist + palmOut * layout.wristButtonPalmOffset
                                       - fingerDir * layout.sliderForearmOffset;
                GameObject track = HandToolMenu.CreateRect("WidthSliderTrack",
                    new Color(0.35f, 0.38f, 0.42f), _root);
                track.transform.SetPositionAndRotation(sliderCenter, facing);
                track.transform.localScale = new Vector3(layout.sliderTrackLength, layout.sliderTrackThickness, 1f);
                GameObject knob = HandToolMenu.CreateDisc("WidthSliderKnob",
                    layout.sliderKnobSize, Color.white, _root);
                knob.transform.SetPositionAndRotation(sliderCenter, facing);
                HandToolMenu.AddLabel(knob.transform, "25mm", 0.014f * layout.labelScale);
            }

            // Palette swatches over fake fingertips.
            int texCount = _source.ReliefTextures?.Length ?? 0;
            int count = _side == GloveSide.Left
                ? Mathf.Min(_source.PaletteColors?.Length ?? 0, 5)
                : (texCount > 0 ? 5 : 0);
            for (int i = 0; i < count; i++)
            {
                // Fan of fingertip positions: thumb..pinky across the row axis.
                float spread = (i - 2f) * 0.022f;
                Vector3 tipPos = wrist + fingerDir * (0.16f - Mathf.Abs(i - 2f) * 0.012f)
                                 + rowDir * spread * 2f;

                GameObject sw = _side == GloveSide.Left
                    ? HandToolMenu.CreateDisc($"Swatch_{i}", layout.swatchSize, _source.PaletteColors[i], _root)
                    : HandToolMenu.CreateBumpSphere($"Swatch_{i}", layout.swatchSize,
                        _source.ReliefTextures[i % texCount], _root);
                Vector3 inward = (palmCenter - tipPos).normalized * layout.swatchInwardOffset;
                sw.transform.SetPositionAndRotation(
                    tipPos + inward + palmOut * layout.swatchPalmOffset, facing);
            }
        }
#endif
    }
}
