// The painting "strategy" seam. FingerTipPainter only ever talks to this
// abstraction, so you can swap how paint is applied without changing any
// fingertip code:
//
//   - LineRendererBrush  (now)   -> draws a line in the air along the stroke
//   - TextureBrush       (later) -> splats into a surface's albedo RenderTexture
//   - NormalMapBrush     (later) -> writes into a normal-map RenderTexture
//   - DecalBrush         (later) -> spawns URP decal projectors
//
// A stroke is opened per finger contact and fed points until the finger lifts,
// so multiple fingers can paint at once, each with its own IPaintStroke.

using UnityEngine;

namespace FingerPaint
{
    /// <summary>Describes one finger contact starting a stroke.</summary>
    public struct StrokeInfo
    {
        public FingerId finger;
        public Color color;
        public float width;          // metres
        public Vector3 startPoint;   // world space
        public Vector3 startNormal;  // world space
        public Collider surface;     // the collider being painted on
    }

    /// <summary>A single in-progress stroke. Returned by a brush; fed points until End().</summary>
    public interface IPaintStroke
    {
        void AddPoint(Vector3 worldPoint, Vector3 worldNormal);
        void End();
    }

    /// <summary>Base class for a painting technique. Implement BeginStroke.</summary>
    public abstract class FingerPaintBrush : MonoBehaviour
    {
        /// <summary>Open a new stroke. Return a handle the painter feeds points to.</summary>
        public abstract IPaintStroke BeginStroke(in StrokeInfo info);
    }
}
