// Global painting state shared by the hand menus, painters, haptics and brushes.
//
//   None   -> nothing is painted; touching painted surfaces just plays haptics
//             matched to what's under the finger (bumps over relief, smooth
//             over color, silence on bare surface).
//   Paint  -> paint as usual: right index = ActiveColor, left index = normal
//             map (ActiveTextureIndex).
//   Eraser -> touching painted areas removes them.

using UnityEngine;

namespace FingerPaint
{
    public enum PaintMode { None, Paint, Eraser }

    /// <summary>Vibrotactile character of a relief texture. Order-matched to the
    /// bootstrap's Relief Textures array.</summary>
    public enum ReliefFeel { Smooth, Rock, Blobs, Tiles, Waves }

    public static class PaintModeManager
    {
        public static PaintMode Mode { get; private set; } = PaintMode.Paint;
        public static Color ActiveColor { get; private set; } = new Color(0.9f, 0.2f, 0.25f, 1f);
        public static int ActiveTextureIndex { get; private set; }

        /// <summary>Stroke width in metres, driven by the right-wrist slider.
        /// 0 = not set yet (painters fall back to their configured width).
        /// Changed continuously while dragging, so no Changed event fires.</summary>
        public static float StrokeWidth { get; private set; }

        public static void SetStrokeWidth(float width) => StrokeWidth = Mathf.Max(width, 0.002f);

        /// <summary>Haptic profile per relief texture, set by the bootstrap.
        /// Index-matched to the Relief Textures array.</summary>
        public static ReliefFeel[] TextureFeels =
            { ReliefFeel.Smooth, ReliefFeel.Rock, ReliefFeel.Blobs, ReliefFeel.Tiles, ReliefFeel.Waves };

        public static ReliefFeel FeelOfTexture(int index)
        {
            if (TextureFeels == null || TextureFeels.Length == 0) return ReliefFeel.Blobs;
            return TextureFeels[Mathf.Clamp(index, 0, TextureFeels.Length - 1)];
        }

        /// <summary>Raised whenever mode/color/texture changes (menus re-highlight,
        /// brushes switch textures).</summary>
        public static event System.Action Changed;

        public static void SetMode(PaintMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            Changed?.Invoke();
        }

        public static void SetActiveColor(Color color)
        {
            if (ActiveColor == color) return;
            ActiveColor = color;
            Changed?.Invoke();
        }

        public static void SetActiveTextureIndex(int index)
        {
            if (ActiveTextureIndex == index) return;
            ActiveTextureIndex = index;
            Changed?.Invoke();
        }
    }
}
