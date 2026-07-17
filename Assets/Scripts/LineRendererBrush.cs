// LineRenderer painting strategy: each finger contact spawns a LineRenderer that
// traces the stroke through the air. This is the "start simple" brush; swap it for
// a TextureBrush/NormalMapBrush later without touching FingerTipPainter.
//
// Put one of these in the scene and assign it to every FingerTipPainter's Brush.

using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    public class LineRendererBrush : FingerPaintBrush
    {
        [Tooltip("Material for painted lines. Leave empty to auto-create an unlit " +
                 "URP material (falls back to Sprites/Default).")]
        [SerializeField] private Material _lineMaterial;
        [Tooltip("Corner/cap smoothing. 0 keeps lines crisp and cheap.")]
        [SerializeField] private int _cornerCapVertices = 4;
        [Tooltip("Parent for spawned strokes. Empty = parent under this object.")]
        [SerializeField] private Transform _strokeParent;

        // Reused shader property ids.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public override IPaintStroke BeginStroke(in StrokeInfo info)
        {
            var go = new GameObject($"Stroke_{info.finger}");
            go.transform.SetParent(_strokeParent != null ? _strokeParent : transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCornerVertices = _cornerCapVertices;
            lr.numCapVertices = _cornerCapVertices;
            lr.startWidth = info.width;
            lr.endWidth = info.width;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            lr.material = ResolveMaterial(info.color);
            lr.startColor = info.color;
            lr.endColor = info.color;

            lr.positionCount = 1;
            lr.SetPosition(0, info.startPoint);

            return new LineStroke(lr);
        }

        // One material instance per stroke so each line keeps its own color even on
        // shaders that ignore LineRenderer vertex colors.
        private Material ResolveMaterial(Color color)
        {
            Material mat;
            if (_lineMaterial != null)
            {
                mat = new Material(_lineMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader);
            }

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
            return mat;
        }

        private class LineStroke : IPaintStroke
        {
            private readonly LineRenderer _lr;
            private readonly List<Vector3> _points = new List<Vector3>();

            public LineStroke(LineRenderer lr)
            {
                _lr = lr;
                _points.Add(lr.GetPosition(0));
            }

            public void AddPoint(Vector3 worldPoint, Vector3 worldNormal)
            {
                if (_lr == null) return;
                _points.Add(worldPoint);
                _lr.positionCount = _points.Count;
                _lr.SetPosition(_points.Count - 1, worldPoint);
            }

            public void End() { /* line stays in the scene */ }
        }
    }
}
