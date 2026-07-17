// Blob-stamp painting brush (no LineRenderer). Each stroke lays down round,
// surface-aligned blobs that merge into a smear you can extend in ANY direction
// on the plane -- like finger painting. All blobs of one kind append into a
// single growing Mesh (one per color / relief kind), so draw calls stay flat.
//
// Modes:
//   Color        -> opaque colored paint. Every stamp also registers its area
//                   AND COLOR in a shared coverage grid.
//   NormalRelief -> applies a normal map to what you touch:
//                     - over painted color -> stamps opaque paint of THE SAME
//                       color carrying the normal map, so the existing paint
//                       visibly gains bumps (not a separate overlay film);
//                     - over bare surface  -> paints WHITE with that normal map.
//
// The normal map comes from the texture assigned in the Inspector / Setup
// (e.g. normalMap1); if none is given, a procedural noise map is generated.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FingerPaint
{
    public class MeshStampBrush : FingerPaintBrush
    {
        public enum StampMode { Color, NormalRelief }

        [Header("Mode")]
        [SerializeField] private StampMode _mode = StampMode.Color;

        [Header("Material")]
        [Tooltip("Base material to clone. Empty = auto-create from URP Lit.")]
        [SerializeField] private Material _baseMaterial;
        [Tooltip("Normal map used by NormalRelief mode. Empty = procedural noise.")]
        [SerializeField] private Texture _normalMap;

        [Header("Blob shape")]
        [Tooltip("Vertices around each blob disc.")]
        [Range(6, 32)]
        [SerializeField] private int _segments = 16;
        [Tooltip("Metres the paint floats above the surface to avoid z-fighting.")]
        [SerializeField] private float _surfaceOffset = 0.003f;
        [Tooltip("New blob is stamped after moving this fraction of the blob radius. " +
                 "Lower = smoother smear, more triangles.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _stampSpacing = 0.35f;

        [Header("Normal relief mode")]
        [Tooltip("Strength of the bump.")]
        [SerializeField] private float _bumpStrength = 1.5f;
        [Tooltip("Bump pattern repeats per metre.")]
        [SerializeField] private float _bumpTiling = 40f;
        [Tooltip("Relief only paints WHITE when no existing color paint is within " +
                 "this distance (metres). Near a painted area it snaps to that " +
                 "area's color instead, so edges don't get white blobs overlapping " +
                 "the color.")]
        [SerializeField] private float _bareGuardDistance = 0.04f;

        // ---- shared coverage grid: which COLOR was painted where? ------------
        private const float CoverageCell = 0.025f;
        private static readonly Dictionary<Vector3Int, Color> PaintedCells =
            new Dictionary<Vector3Int, Color>();
        private static readonly Dictionary<Vector3Int, int> ReliefCells =
            new Dictionary<Vector3Int, int>(); // cell -> texture id painted there

        // Discrete paint layers: paint directly on the surface is layer 1; paint
        // of a DIFFERENT kind over existing paint goes one layer higher (+0.5mm),
        // so it renders on top. Same-kind paint reuses the existing layer, which
        // keeps a whole smear on a flat surface at ONE constant height instead of
        // creeping upward per stamp.
        private const float LayerStep = 0.0005f;
        private const int MaxLayer = 20;
        private static readonly Dictionary<Vector3Int, (int layer, int surfaceId)> LayerCells =
            new Dictionary<Vector3Int, (int, int)>();
        private static int _nextSurfaceId = 1;

        public static void ClearCoverage()
        {
            PaintedCells.Clear();
            ReliefCells.Clear();
            LayerCells.Clear();
            _nextSurfaceId = 1;
        }

        private static Vector3Int Cell(Vector3 p) => new Vector3Int(
            Mathf.RoundToInt(p.x / CoverageCell),
            Mathf.RoundToInt(p.y / CoverageCell),
            Mathf.RoundToInt(p.z / CoverageCell));

        private static void ForEachCell(Vector3 point, float radius, System.Action<Vector3Int> action)
        {
            int r = Mathf.CeilToInt(radius / CoverageCell);
            Vector3Int c = Cell(point);
            for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                    for (int z = -r; z <= r; z++)
                        action(new Vector3Int(c.x + x, c.y + y, c.z + z));
        }

        private static bool TryGetPaintedColor(Vector3 point, out Color color) =>
            PaintedCells.TryGetValue(Cell(point), out color);

        /// <summary>Is there color paint at this world point? (used by No-mode haptics)</summary>
        public static bool HasColorAt(Vector3 point) => PaintedCells.ContainsKey(Cell(point));

        /// <summary>Is there normal-map paint at this world point?</summary>
        public static bool HasReliefAt(Vector3 point) => ReliefCells.ContainsKey(Cell(point));

        /// <summary>Which relief texture was painted at this point (Feel mode
        /// plays that texture's haptic profile).</summary>
        public static bool TryGetReliefTextureAt(Vector3 point, out int texId) =>
            ReliefCells.TryGetValue(Cell(point), out texId);

        // All live brushes, so the eraser can reach every painted surface.
        private static readonly List<MeshStampBrush> Instances = new List<MeshStampBrush>();
        private void OnEnable() => Instances.Add(this);
        private void OnDisable() => Instances.Remove(this);

        /// <summary>Deletes ALL paint from every brush (the menu's Clear tool).</summary>
        public static void ClearAllPaint()
        {
            foreach (MeshStampBrush brush in Instances)
            {
                foreach (PaintSurface s in brush._surfaces.Values)
                {
                    s.verts.Clear();
                    s.normals.Clear();
                    s.tangents.Clear();
                    s.uvs.Clear();
                    s.tris.Clear();
                    s.blobs.Clear();
                    s.mesh.Clear();
                    s.dirty = false;
                }
                brush._dirty.Clear();
            }
            ClearCoverage();
        }

        /// <summary>Erases every paint blob whose center is within radius of the
        /// point, across all brushes. Returns true if anything was removed.</summary>
        public static bool EraseAt(Vector3 point, float radius)
        {
            bool erased = false;
            foreach (MeshStampBrush brush in Instances)
            {
                foreach (PaintSurface s in brush._surfaces.Values)
                {
                    foreach (BlobRec blob in s.blobs)
                    {
                        if (blob.erased) continue;
                        float reach = radius + blob.radius;
                        if ((blob.center - point).sqrMagnitude > reach * reach) continue;

                        blob.erased = true;
                        // Degenerate the blob's triangles (all indices 0 renders nothing).
                        for (int t = 0; t < blob.triCount; t++) s.tris[blob.triStart + t] = 0;
                        // Forget its coverage so painting/haptics treat it as bare.
                        ForEachCell(blob.center, blob.radius, c =>
                        {
                            PaintedCells.Remove(c);
                            ReliefCells.Remove(c);
                            LayerCells.Remove(c);
                        });
                        brush.MarkDirty(s);
                        erased = true;
                    }
                }
            }
            return erased;
        }

        // Searches for painted color within maxDist of the point and returns the
        // NEAREST one -- used by relief at color edges, so a blob just outside the
        // recorded cells still counts as "on the color" instead of going white.
        private static bool TryGetPaintedColorNear(Vector3 point, float maxDist, out Color color)
        {
            if (PaintedCells.TryGetValue(Cell(point), out color)) return true;

            int r = Mathf.CeilToInt(maxDist / CoverageCell);
            Vector3Int c = Cell(point);
            float bestSqr = float.MaxValue;
            bool found = false;
            for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                    for (int z = -r; z <= r; z++)
                    {
                        var cell = new Vector3Int(c.x + x, c.y + y, c.z + z);
                        if (!PaintedCells.TryGetValue(cell, out Color candidate)) continue;
                        Vector3 cellCenter = new Vector3(cell.x, cell.y, cell.z) * CoverageCell;
                        float sqr = (cellCenter - point).sqrMagnitude;
                        if (sqr < bestSqr && sqr <= maxDist * maxDist)
                        {
                            bestSqr = sqr;
                            color = candidate;
                            found = true;
                        }
                    }
            return found;
        }

        private static bool IsReliefPainted(Vector3 point) => ReliefCells.ContainsKey(Cell(point));

        // Surfaces are keyed by (color, has normal map, texture id): flat purple,
        // bumpy purple, and bumpy purple with a different texture are all
        // different meshes/materials.
        private readonly Dictionary<(Color color, bool bumped, int texId), PaintSurface> _surfaces =
            new Dictionary<(Color, bool, int), PaintSurface>();

        // Selected normal map for NEW bumped stamps (switched from the hand menu).
        // A NULL texture means "smooth": relief painting then lays FLAT paint and
        // strips the normal mapping from whatever it touches.
        private int _activeTexId;
        private Texture _activeNormalMap;
        private bool _activeIsSmooth;
        private Texture[] _normalMaps; // full palette, so existing paint keeps ITS texture

        /// <summary>Switch the normal map used by subsequent bumped stamps.
        /// Pass null for the smooth (bump-removing) brush.</summary>
        public void SetActiveNormalMap(int texId, Texture texture)
        {
            _activeTexId = texId;
            _activeNormalMap = texture;
            _activeIsSmooth = texture == null;
        }

        /// <summary>Provide the whole texture palette, so coloring OVER an area
        /// keeps the texture that was painted there (not the active one).</summary>
        public void SetNormalMaps(Texture[] textures) => _normalMaps = textures;

        private Texture TextureForId(int texId)
        {
            if (_normalMaps != null && texId >= 0 && texId < _normalMaps.Length && _normalMaps[texId] != null)
            {
                return _normalMaps[texId];
            }
            if (_activeNormalMap != null) return _activeNormalMap;
            return _normalMap != null ? _normalMap : GetProceduralBump();
        }
        private readonly List<PaintSurface> _dirty = new List<PaintSurface>();
        private Texture2D _proceduralBump;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // ---------------------------------------------------------------- API

        /// <summary>Configure from code (for brushes created at runtime). Call
        /// before the first stroke.</summary>
        public void Setup(StampMode mode, Material baseMaterial = null,
            float surfaceOffset = -1f, Texture normalMap = null, float bumpTiling = -1f)
        {
            _mode = mode;
            _baseMaterial = baseMaterial;
            if (surfaceOffset >= 0f) _surfaceOffset = surfaceOffset;
            if (normalMap != null) _normalMap = normalMap;
            if (bumpTiling > 0f) _bumpTiling = bumpTiling;
        }

        public override IPaintStroke BeginStroke(in StrokeInfo info)
        {
            var stroke = new BlobStroke(this, info.color, Mathf.Max(info.width * 0.5f, 0.003f));
            stroke.AddPoint(info.startPoint, info.startNormal);
            return stroke;
        }

        // ------------------------------------------------------------ stamping

        private void Stamp(Color color, Vector3 point, Vector3 normal, float radius)
        {
            PaintSurface surface;
            if (_mode == StampMode.Color)
            {
                // Over an area that already has relief: keep the bumps and color
                // them -- stamp this color WITH the texture THAT IS ALREADY THERE
                // (not the currently selected one). Else flat color.
                bool bumped = TryGetReliefTextureAt(point, out int existingTex);
                surface = GetSurface(color, bumped, bumped ? existingTex : 0);
                ForEachCell(point, radius, c => PaintedCells[c] = color);
            }
            else if (_activeIsSmooth)
            {
                // Smooth selected (empty texture slot): flatten. Lay FLAT paint in
                // the local color (white on bare surface) and strip the relief
                // registration, so the area loses its normal mapping -- visually
                // and for Feel-mode haptics.
                float guard = Mathf.Max(_bareGuardDistance, radius);
                Color key = TryGetPaintedColorNear(point, guard, out Color painted)
                    ? painted : Color.white;
                surface = GetSurface(key, false, 0);
                ForEachCell(point, radius, c =>
                {
                    ReliefCells.Remove(c);
                    PaintedCells[c] = key;
                });
            }
            else
            {
                // Over (or NEAR) existing color: stamp THE SAME color with the
                // normal map, so the paint itself gains bumps. White only when no
                // color is anywhere within the guard distance -- prevents white
                // blobs from spawning at color edges and overlapping the color.
                float guard = Mathf.Max(_bareGuardDistance, radius);
                Color key = TryGetPaintedColorNear(point, guard, out Color painted)
                    ? painted : Color.white;
                surface = GetSurface(key, true, _activeTexId);
                int texId = _activeTexId;
                ForEachCell(point, radius, c => ReliefCells[c] = texId);
            }

            // Pick the paint layer: reuse the height when stamping over the SAME
            // surface (constant Y within a smear); go one layer up only when
            // covering a different paint kind.
            int maxLayer = 0, maxSurf = 0;
            ForEachCell(point, radius, c =>
            {
                if (LayerCells.TryGetValue(c, out var e) && e.layer > maxLayer)
                {
                    maxLayer = e.layer;
                    maxSurf = e.surfaceId;
                }
            });
            int layer = maxLayer == 0 ? 1
                : maxSurf == surface.id ? maxLayer
                : Mathf.Min(maxLayer + 1, MaxLayer);
            ForEachCell(point, radius, c =>
            {
                if (!LayerCells.TryGetValue(c, out var e) || e.layer < layer)
                {
                    LayerCells[c] = (layer, surface.id);
                }
            });

            AddBlob(surface, point, normal, radius, layer);
        }

        private void AddBlob(PaintSurface s, Vector3 point, Vector3 normal, float radius, int layer)
        {
            // Tangent basis derived only from the normal, so every blob on the
            // same plane shares it -> UVs (and the bump pattern) line up
            // seamlessly across overlapping blobs.
            Vector3 tangent = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            // w=+1: Unity's binormal = cross(normal, tangent) * w, which must match
            // our v axis (bitangent = N x T) or the bump lighting reads inverted.
            var tangent4 = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
            float uvScale = _bumpTiling / 40f;

            // Height = base offset + this stamp's paint layer. Same layer =>
            // exactly the same height above the surface, no per-stamp creep.
            Vector3 center = point + normal * (_surfaceOffset + layer * LayerStep);

            int baseIndex = s.verts.Count;
            s.verts.Add(center);
            s.normals.Add(normal);
            s.tangents.Add(tangent4);
            s.uvs.Add(PlanarUv(center, tangent, bitangent, uvScale));

            for (int i = 0; i < _segments; i++)
            {
                float a = i * Mathf.PI * 2f / _segments;
                Vector3 v = center + (tangent * Mathf.Cos(a) + bitangent * Mathf.Sin(a)) * radius;
                s.verts.Add(v);
                s.normals.Add(normal);
                s.tangents.Add(tangent4);
                s.uvs.Add(PlanarUv(v, tangent, bitangent, uvScale));
            }

            // Winding (center, i, i+1) makes the front face point along the
            // surface normal (ring runs tangent->bitangent, and N x T = B).
            int triStart = s.tris.Count;
            for (int i = 0; i < _segments; i++)
            {
                s.tris.Add(baseIndex);
                s.tris.Add(baseIndex + 1 + i);
                s.tris.Add(baseIndex + 1 + (i + 1) % _segments);
            }

            s.blobs.Add(new BlobRec
            {
                center = point,
                radius = radius,
                triStart = triStart,
                triCount = _segments * 3,
            });

            MarkDirty(s);
        }

        private void MarkDirty(PaintSurface s)
        {
            if (!s.dirty)
            {
                s.dirty = true;
                _dirty.Add(s);
            }
        }

        private static Vector2 PlanarUv(Vector3 worldPos, Vector3 tangent, Vector3 bitangent, float scale)
        {
            return new Vector2(Vector3.Dot(worldPos, tangent), Vector3.Dot(worldPos, bitangent)) * scale;
        }

        // ------------------------------------------------------------ surfaces

        private class BlobRec
        {
            public Vector3 center;
            public float radius;
            public int triStart;
            public int triCount;
            public bool erased;
        }

        private class PaintSurface
        {
            public Mesh mesh;
            public int id;   // unique across all brushes, for the layer grid
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> tris = new List<int>();
            public readonly List<BlobRec> blobs = new List<BlobRec>();
            public bool dirty;
        }

        private PaintSurface GetSurface(Color color, bool bumped, int texId)
        {
            if (!bumped) texId = 0;
            var key = (color, bumped, texId);
            if (_surfaces.TryGetValue(key, out PaintSurface s)) return s;

            s = new PaintSurface();
            s.id = _nextSurfaceId++;
            string suffix = bumped ? $"_bump{texId}" : "";
            s.mesh = new Mesh { name = $"PaintMesh_{color}{suffix}" };
            s.mesh.indexFormat = IndexFormat.UInt32;
            s.mesh.MarkDynamic();

            var go = new GameObject($"Paint_{ColorUtility.ToHtmlStringRGBA(color)}{suffix}");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = CreateMaterial(color, bumped, texId);

            _surfaces.Add(key, s);
            return s;
        }

        private Material CreateMaterial(Color color, bool bumped, int texId)
        {
            Material mat;
            if (_baseMaterial != null)
            {
                mat = new Material(_baseMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader);
            }

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);

            if (!bumped)
            {
                if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, 0.35f);
                return mat;
            }

            // Bumped paint: opaque color carrying the normal map.
            if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, 0.6f);
            if (mat.HasProperty(BumpMapId))
            {
                mat.SetTexture(BumpMapId, TextureForId(texId));
                mat.SetFloat(BumpScaleId, _bumpStrength);
                mat.EnableKeyword("_NORMALMAP");
            }
            return mat;
        }

        // Fallback tangent-space normal map (multi-octave noise). Encodes X in
        // both R and A so it unpacks correctly on both RGB and AG shader paths.
        private Texture2D GetProceduralBump()
        {
            if (_proceduralBump != null) return _proceduralBump;

            const int size = 256;
            const float noiseScale = 12f;
            var heights = new float[size + 1, size + 1];
            for (int y = 0; y <= size; y++)
                for (int x = 0; x <= size; x++)
                    heights[x, y] =
                        Mathf.PerlinNoise(x * noiseScale / size, y * noiseScale / size) +
                        0.5f * Mathf.PerlinNoise(x * noiseScale * 2f / size + 37f, y * noiseScale * 2f / size + 11f);

            _proceduralBump = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = heights[x + 1, y] - heights[x, y];
                    float dy = heights[x, y + 1] - heights[x, y];
                    Vector3 n = new Vector3(-dx, -dy, 0.35f).normalized;
                    byte r = (byte)((n.x * 0.5f + 0.5f) * 255f);
                    byte g = (byte)((n.y * 0.5f + 0.5f) * 255f);
                    byte b = (byte)((n.z * 0.5f + 0.5f) * 255f);
                    pixels[y * size + x] = new Color32(r, g, b, r);
                }
            }
            _proceduralBump.SetPixels32(pixels);
            _proceduralBump.wrapMode = TextureWrapMode.Repeat;
            _proceduralBump.Apply(true);
            return _proceduralBump;
        }

        // Batch mesh uploads once per frame instead of per stamp.
        private void LateUpdate()
        {
            if (_dirty.Count == 0) return;
            foreach (PaintSurface s in _dirty)
            {
                s.mesh.SetVertices(s.verts);
                s.mesh.SetNormals(s.normals);
                s.mesh.SetTangents(s.tangents);
                s.mesh.SetUVs(0, s.uvs);
                s.mesh.SetTriangles(s.tris, 0, false);
                s.mesh.RecalculateBounds();
                s.dirty = false;
            }
            _dirty.Clear();
        }

        // ------------------------------------------------------------- stroke

        private class BlobStroke : IPaintStroke
        {
            private readonly MeshStampBrush _brush;
            private readonly Color _color;
            private readonly float _radius;
            private Vector3 _lastStamp;
            private bool _hasStamp;

            public BlobStroke(MeshStampBrush brush, Color color, float radius)
            {
                _brush = brush;
                _color = color;
                _radius = radius;
            }

            public void AddPoint(Vector3 point, Vector3 normal)
            {
                if (_hasStamp &&
                    (point - _lastStamp).sqrMagnitude <
                    _radius * _radius * _brush._stampSpacing * _brush._stampSpacing)
                {
                    return;
                }
                _brush.Stamp(_color, point, normal, _radius);
                _lastStamp = point;
                _hasStamp = true;
            }

            public void End() { /* paint stays */ }
        }
    }
}
