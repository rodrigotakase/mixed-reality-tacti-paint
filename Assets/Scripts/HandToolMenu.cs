// Wrist-button tool menu for one tracked hand, built entirely at runtime.
//
//  - A small button sits on the INNER wrist. Pressing it with the OPPOSITE
//    index finger toggles a panel above the palm with three mode buttons:
//    Feel (no painting, haptics only), Paint, Erase.
//  - In Paint mode a palette hovers over this hand's fingertips:
//    LEFT hand fingers show COLORS, RIGHT hand fingers show TEXTURES.
//    Touch a swatch with the opposite index finger to select it.
//  - Everything is only shown while this palm FACES THE USER; palm down or
//    toward a wall hides button, panel and palette.
//
// Created by FingerPaintMeshBootstrap for each hand.

using System.Collections.Generic;
using UnityEngine;
using Bhaptics.SDK2;

namespace FingerPaint
{
    /// <summary>All tweakable placement/size values for the wrist menu and the
    /// fingertip palettes. Edit on FingerPaintSetup (and preview in the editor
    /// via GameObject > FingerPaint > Hand Menu Preview).</summary>
    [System.Serializable]
    public class HandMenuLayout
    {
        [Header("Visibility / touch")]
        [Tooltip("How directly the palm must face the head (dot product) to show the menu.")]
        [Range(0f, 1f)] public float facingThreshold = 0.35f;
        [Tooltip("Touch distance (m) for pressing buttons/swatches with the opposite index.")]
        public float pressRange = 0.025f;

        [Header("Wrist button")]
        public float wristButtonSize = 0.025f;
        [Tooltip("Metres above the inner-wrist skin (along palm normal).")]
        public float wristButtonPalmOffset = 0.015f;
        [Tooltip("Metres from the wrist toward the forearm.")]
        public float wristButtonForearmOffset = 0.04f;
        public Color wristButtonColor = new Color(0.2f, 0.9f, 0.9f);

        [Header("Mode panel")]
        [Tooltip("Metres above the palm centre.")]
        public float panelPalmOffset = 0.08f;
        public float modeButtonSize = 0.04f;
        public float modeButtonSelectedSize = 0.05f;
        [Tooltip("Horizontal spacing between the three mode buttons.")]
        public float modeButtonSpacing = 0.05f;

        [Header("Palette swatches")]
        public float swatchSize = 0.022f;
        public float swatchSelectedSize = 0.03f;
        [Tooltip("Metres above each fingertip (along palm normal). Larger = floats " +
                 "further off the fingers, easier to touch with the other index.")]
        public float swatchPalmOffset = 0.035f;
        [Tooltip("Metres each swatch is pulled from the fingertip toward the palm " +
                 "centre, so swatches sit on the fingers rather than past the tips.")]
        public float swatchInwardOffset = 0.018f;

        [Header("Selection indicator (on the painting index finger)")]
        [Tooltip("Size of the sphere on the index fingertip showing the selected color/texture.")]
        public float indicatorSize = 0.014f;
        [Tooltip("Metres from the fingertip toward the back of the finger (nail side), " +
                 "so it doesn't sit inside the painting contact point.")]
        public float indicatorOffset = 0.014f;

        [Header("Labels")]
        [Tooltip("Multiplier for all button label font sizes.")]
        public float labelScale = 2f;

        [Header("Stroke width slider (right wrist)")]
        [Tooltip("Track length in metres.")]
        public float sliderTrackLength = 0.09f;
        [Tooltip("Track thickness in metres.")]
        public float sliderTrackThickness = 0.008f;
        public float sliderKnobSize = 0.022f;
        [Tooltip("Metres from the wrist toward the forearm (further than the menu button).")]
        public float sliderForearmOffset = 0.08f;
        public float minStrokeWidth = 0.02f;
        public float maxStrokeWidth = 0.15f;
    }

    /// <summary>Icon textures for the menu buttons (white PNGs with alpha).</summary>
    [System.Serializable]
    public class HandMenuIcons
    {
        public Texture feelHand;
        public Texture paintBrush;
        public Texture eraser;
        public Texture clearBin;
        public Texture menu;
        [Tooltip("Material asset for the icons (IconCutout). Using an asset keeps its " +
                 "transparent/alpha-clip shader variants in the build; runtime-created " +
                 "materials can get their variants stripped and render as squares.")]
        public Material iconMaterial;
    }

    public class HandToolMenu : MonoBehaviour
    {
        private const float PressDebounce = 0.35f;

        [SerializeField] private HandMenuLayout _layout = new HandMenuLayout();
        [SerializeField] private HandMenuIcons _icons = new HandMenuIcons();

        private GloveSide _side;
        private OVRSkeleton _skeleton;
        private OVRSkeleton _oppositeSkeleton;
        private Color[] _paletteColors;
        private Texture[] _paletteTextures;

        private bool _menuOpen;
        private float _lastPressTime;

        // bones
        private Transform _wrist, _middleProx, _thumbProx;
        private Transform _oppositeIndexTip;
        private readonly List<Transform> _fingerTips = new List<Transform>();

        // visuals
        private Transform _wristButton;
        private Transform _panel;
        private readonly List<Transform> _modeButtons = new List<Transform>();
        private readonly List<Renderer> _modeRenderers = new List<Renderer>();
        private readonly List<Transform> _swatches = new List<Transform>();
        private readonly List<Renderer> _swatchRenderers = new List<Renderer>();
        private bool _built;

        private Transform _clearButton;

        // selection indicator on this hand's index fingertip
        private Transform _selectionIndicator;
        private Renderer _indicatorRenderer;
        private Transform _ownIndexTip;

        // stroke width slider (right hand only)
        private Transform _sliderTrack;
        private Transform _sliderKnob;
        private TextMesh _sliderLabel;
        private bool _sliderDragging;
        private int _lastSliderDetent = -1;

        private static readonly PaintMode[] Modes = { PaintMode.None, PaintMode.Paint, PaintMode.Eraser };
        private static readonly string[] ModeLabels = { "Feel", "Paint", "Erase" };
        private static readonly Color[] ModeTints =
        {
            new Color(0.55f, 0.6f, 0.65f), new Color(0.25f, 0.6f, 0.95f), new Color(0.95f, 0.45f, 0.35f),
        };

        public void Init(GloveSide side, OVRSkeleton skeleton, OVRSkeleton oppositeSkeleton,
            Color[] paletteColors, Texture[] paletteTextures, HandMenuLayout layout = null,
            HandMenuIcons icons = null)
        {
            _side = side;
            _skeleton = skeleton;
            _oppositeSkeleton = oppositeSkeleton;
            _paletteColors = paletteColors;
            _paletteTextures = paletteTextures;
            if (layout != null) _layout = layout;
            if (icons != null) _icons = icons;
        }

        private void OnEnable() => PaintModeManager.Changed += RefreshHighlights;
        private void OnDisable() => PaintModeManager.Changed -= RefreshHighlights;

        private void LateUpdate()
        {
            if (_skeleton == null || !_skeleton.IsInitialized ||
                _skeleton.Bones == null || _skeleton.Bones.Count == 0)
            {
                return;
            }
            if (!_built) Build();
            if (!_built) return;

            Camera head = Camera.main;
            if (head == null) return;

            Vector3 wrist = _wrist.position;
            Vector3 middleVec = _middleProx.position - wrist;
            Vector3 thumbVec = _thumbProx.position - wrist;
            Vector3 palmOut = (_side == GloveSide.Right
                ? Vector3.Cross(middleVec, thumbVec)
                : Vector3.Cross(thumbVec, middleVec)).normalized;
            Vector3 palmCenter = Vector3.Lerp(wrist, _middleProx.position, 0.55f);
            Vector3 fingerDir = middleVec.normalized;
            Vector3 toHead = (head.transform.position - palmCenter).normalized;

            bool facing = Vector3.Dot(palmOut, toHead) > _layout.facingThreshold;

            // --- visibility ---------------------------------------------------
            if (_wristButton != null) _wristButton.gameObject.SetActive(facing);
            if (_panel != null) _panel.gameObject.SetActive(facing && _menuOpen);
            if (_sliderTrack != null)
            {
                _sliderTrack.gameObject.SetActive(facing);
                _sliderKnob.gameObject.SetActive(facing);
            }
            bool paletteVisible = facing && PaintModeManager.Mode == PaintMode.Paint;
            for (int i = 0; i < _swatches.Count; i++)
            {
                _swatches[i].gameObject.SetActive(paletteVisible && i < _fingerTips.Count);
            }

            // Selection indicator rides the index fingertip whenever Paint mode is
            // on -- but hides while THIS hand is showing its palette (palm facing
            // the user), so it doesn't clutter the swatches.
            if (_selectionIndicator != null)
            {
                bool showIndicator = PaintModeManager.Mode == PaintMode.Paint &&
                                     _ownIndexTip != null && !paletteVisible;
                _selectionIndicator.gameObject.SetActive(showIndicator);
                if (showIndicator)
                {
                    _selectionIndicator.position =
                        _ownIndexTip.position - palmOut * _layout.indicatorOffset;
                }
            }

            if (!facing) return;

            // --- placement ----------------------------------------------------
            Quaternion faceHead = Quaternion.LookRotation(palmCenter - head.transform.position);

            if (_wristButton != null)
            {
                _wristButton.SetPositionAndRotation(
                    wrist + palmOut * _layout.wristButtonPalmOffset - fingerDir * _layout.wristButtonForearmOffset, faceHead);
            }

            if (_sliderTrack != null)
            {
                UpdateSlider(wrist, palmOut, fingerDir, faceHead);
            }

            if (_menuOpen && _panel != null)
            {
                Vector3 panelCenter = palmCenter + palmOut * _layout.panelPalmOffset;
                Vector3 rowDir = Vector3.Cross(Vector3.up, toHead).normalized;
                _panel.SetPositionAndRotation(panelCenter, faceHead);
                for (int i = 0; i < _modeButtons.Count; i++)
                {
                    _modeButtons[i].position = panelCenter + rowDir * ((i - 1.5f) * _layout.modeButtonSpacing);
                    _modeButtons[i].rotation = faceHead;
                }
                _clearButton.position = panelCenter + rowDir * (1.5f * _layout.modeButtonSpacing);
                _clearButton.rotation = faceHead;
            }

            if (paletteVisible)
            {
                for (int i = 0; i < _swatches.Count && i < _fingerTips.Count; i++)
                {
                    Vector3 tip = _fingerTips[i].position;
                    Vector3 inward = (palmCenter - tip).normalized * _layout.swatchInwardOffset;
                    _swatches[i].SetPositionAndRotation(
                        tip + inward + palmOut * _layout.swatchPalmOffset, faceHead);
                }
            }

            // --- interaction (opposite index finger) ---------------------------
            if (_oppositeIndexTip == null)
            {
                _oppositeIndexTip = FindBone(_oppositeSkeleton, OVRSkeleton.BoneId.Hand_IndexTip,
                    OVRSkeleton.BoneId.XRHand_IndexTip);
                if (_oppositeIndexTip == null) return;
            }
            if (Time.time - _lastPressTime < PressDebounce) return;
            Vector3 pressTip = _oppositeIndexTip.position;

            if (_wristButton != null &&
                (pressTip - _wristButton.position).sqrMagnitude < _layout.pressRange * _layout.pressRange)
            {
                _menuOpen = !_menuOpen;
                _lastPressTime = Time.time;
                // Toggle feedback on THIS hand's wrist (the one wearing the button).
                PositionType ownGlove = _side == GloveSide.Left ? PositionType.GloveL : PositionType.GloveR;
                BhapticsLibrary.PlaySingleMotor(ownGlove, (int)FingerId.Palm, 90, 70);
                return;
            }

            if (_menuOpen && _panel != null && _panel.gameObject.activeSelf)
            {
                for (int i = 0; i < _modeButtons.Count; i++)
                {
                    if ((pressTip - _modeButtons[i].position).sqrMagnitude < _layout.pressRange * _layout.pressRange)
                    {
                        PaintModeManager.SetMode(Modes[i]);
                        Click();
                        return;
                    }
                }
                if ((pressTip - _clearButton.position).sqrMagnitude < _layout.pressRange * _layout.pressRange)
                {
                    MeshStampBrush.ClearAllPaint();
                    Click();
                    Debug.Log($"{name}: cleared all paint.");
                    return;
                }
            }

            if (paletteVisible)
            {
                for (int i = 0; i < _swatches.Count && i < _fingerTips.Count; i++)
                {
                    if (!_swatches[i].gameObject.activeSelf) continue;
                    if ((pressTip - _swatches[i].position).sqrMagnitude < _layout.pressRange * _layout.pressRange)
                    {
                        if (_side == GloveSide.Left) PaintModeManager.SetActiveColor(_paletteColors[i]);
                        else PaintModeManager.SetActiveTextureIndex(i % _paletteTextures.Length);
                        Click(); // the selecting (opposite) index finger
                        // ...and the finger wearing the selected swatch on THIS hand.
                        PositionType ownGlove = _side == GloveSide.Left ? PositionType.GloveL : PositionType.GloveR;
                        BhapticsLibrary.PlaySingleMotor(ownGlove, i, 85, 90);
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------- build

        private void Build()
        {
            _wrist = FindBone(_skeleton, OVRSkeleton.BoneId.Hand_WristRoot, OVRSkeleton.BoneId.XRHand_Wrist);
            _middleProx = FindBone(_skeleton, OVRSkeleton.BoneId.Hand_Middle1, OVRSkeleton.BoneId.XRHand_MiddleProximal);
            _thumbProx = FindBone(_skeleton, OVRSkeleton.BoneId.Hand_Thumb1, OVRSkeleton.BoneId.XRHand_ThumbProximal);
            if (_wrist == null || _middleProx == null || _thumbProx == null) return;

            var tipIds = new[]
            {
                (OVRSkeleton.BoneId.Hand_ThumbTip, OVRSkeleton.BoneId.XRHand_ThumbTip),
                (OVRSkeleton.BoneId.Hand_IndexTip, OVRSkeleton.BoneId.XRHand_IndexTip),
                (OVRSkeleton.BoneId.Hand_MiddleTip, OVRSkeleton.BoneId.XRHand_MiddleTip),
                (OVRSkeleton.BoneId.Hand_RingTip, OVRSkeleton.BoneId.XRHand_RingTip),
                (OVRSkeleton.BoneId.Hand_PinkyTip, OVRSkeleton.BoneId.XRHand_LittleTip),
            };
            _fingerTips.Clear();
            foreach (var (classic, xr) in tipIds)
            {
                Transform t = FindBone(_skeleton, classic, xr);
                if (t != null) _fingerTips.Add(t);
            }

            // The menu button + mode panel live on the LEFT wrist only; the right
            // wrist carries just the stroke-width slider.
            if (_side == GloveSide.Left)
            {
                _wristButton = CreateButton("WristButton", _layout.wristButtonSize,
                    _layout.wristButtonColor, _icons.menu, "Menu",
                    0.034f * _layout.labelScale, transform, _icons.iconMaterial).transform;

                _panel = new GameObject("ModePanel").transform;
                _panel.SetParent(transform, false);
                Texture[] modeIcons = { _icons.feelHand, _icons.paintBrush, _icons.eraser };
                for (int i = 0; i < Modes.Length; i++)
                {
                    GameObject b = CreateButton($"Mode_{ModeLabels[i]}", _layout.modeButtonSize,
                        ModeTints[i], modeIcons[i], ModeLabels[i],
                        0.036f * _layout.labelScale, _panel, _icons.iconMaterial);
                    _modeButtons.Add(b.transform);
                    _modeRenderers.Add(b.GetComponent<Renderer>());
                }
                GameObject clear = CreateButton("Mode_Clear", _layout.modeButtonSize,
                    new Color(0.45f, 0.16f, 0.18f), _icons.clearBin, "Clear",
                    0.036f * _layout.labelScale, _panel, _icons.iconMaterial);
                _clearButton = clear.transform;
            }

            // Palette swatches: colors (left hand) or textures (right hand). The
            // right hand always fills all five fingers; with fewer textures than
            // fingers the textures repeat.
            int count = _side == GloveSide.Left
                ? Mathf.Min(_paletteColors.Length, 5)
                : (_paletteTextures.Length > 0 ? 5 : 0);
            for (int i = 0; i < count; i++)
            {
                GameObject sw = _side == GloveSide.Left
                    ? CreateDisc($"Swatch_{i}", _layout.swatchSize, _paletteColors[i], transform)
                    : CreateBumpSphere($"Swatch_{i}", _layout.swatchSize,
                        _paletteTextures[i % _paletteTextures.Length], transform);
                _swatches.Add(sw.transform);
                _swatchRenderers.Add(sw.GetComponent<Renderer>());
            }

            _ownIndexTip = FindBone(_skeleton, OVRSkeleton.BoneId.Hand_IndexTip,
                OVRSkeleton.BoneId.XRHand_IndexTip);
            if (_ownIndexTip != null)
            {
                // RIGHT index paints color -> tinted sphere. LEFT index paints the
                // normal map -> bump sphere. Shows the current selection right on
                // the finger that paints with it.
                GameObject ind = CreateBumpSphere($"SelectionIndicator_{_side}",
                    _layout.indicatorSize,
                    _side == GloveSide.Left && _paletteTextures.Length > 0 ? _paletteTextures[0] : null,
                    transform);
                _selectionIndicator = ind.transform;
                _indicatorRenderer = ind.GetComponent<Renderer>();
            }

            if (_side == GloveSide.Right)
            {
                _sliderTrack = CreateRect("WidthSliderTrack", new Color(0.35f, 0.38f, 0.42f), transform).transform;
                _sliderKnob = CreateDisc("WidthSliderKnob", _layout.sliderKnobSize, Color.white, transform).transform;
                AddLabel(_sliderKnob, "", 0.014f * _layout.labelScale);
                _sliderLabel = _sliderKnob.GetComponentInChildren<TextMesh>();
            }

            _built = true;
            RefreshHighlights();
            Debug.Log($"{name}: hand menu built for {_side} hand.");
        }

        private static Mesh _discMesh;

        // Unit-diameter circle in the XY plane (scale = final diameter), UV-mapped
        // 0..1 so texture swatches show their image.
        private static Mesh GetDiscMesh()
        {
            if (_discMesh != null) return _discMesh;

            const int segments = 32;
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float x = Mathf.Cos(a) * 0.5f, y = Mathf.Sin(a) * 0.5f;
                verts[i + 1] = new Vector3(x, y, 0f);
                uvs[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % segments;
                tris[i * 3 + 2] = 1 + i;
            }
            _discMesh = new Mesh { name = "MenuDisc", vertices = verts, uv = uvs, triangles = tris };
            _discMesh.RecalculateNormals();
            _discMesh.RecalculateBounds();
            return _discMesh;
        }

        // Circle-stroke (ring) outline + white icon in the middle + label underneath.
        internal static GameObject CreateButton(string name, float size, Color tint,
            Texture icon, string label, float labelHeight, Transform parent,
            Material iconMaterialTemplate = null)
        {
            GameObject go = CreateRing(name, size, tint, parent);
            if (icon != null)
            {
                var iconGo = CreateRect($"{name}_Icon", Color.white, go.transform);
                iconGo.transform.localScale = Vector3.one * 0.66f;
                iconGo.transform.localPosition = new Vector3(0f, 0f, -0.03f);

                // Prefer the IconCutout material ASSET: its transparent + alpha-clip
                // shader variants are guaranteed to exist in device builds. A
                // runtime-created material can get those variants stripped, which
                // renders the icon as an opaque/ghost square.
                Material mat;
                if (iconMaterialTemplate != null)
                {
                    mat = new Material(iconMaterialTemplate);
                    iconGo.GetComponent<Renderer>().material = mat;
                }
                else
                {
                    mat = iconGo.GetComponent<Renderer>().material;
                    MakeTransparent(mat);
                    if (mat.HasProperty("_AlphaClip"))
                    {
                        mat.SetFloat("_AlphaClip", 1f);
                        mat.SetFloat("_Cutoff", 0.35f);
                        mat.EnableKeyword("_ALPHATEST_ON");
                    }
                }

                Texture cleanIcon = EnsureUsableAlpha(icon);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", cleanIcon);
                else mat.mainTexture = cleanIcon;
            }
            // Label sits just below the ring edge (parent-local units: radius = 0.5).
            AddLabel(go.transform, label, labelHeight, -0.6f);
            return go;
        }

        // If an icon's alpha channel is unusable (fully/near opaque), rebuild it
        // with alpha = luminance -- these are white glyphs, so brightness IS the
        // shape. Requires the texture import to have Read/Write enabled.
        private static readonly Dictionary<Texture, Texture> _cleanIcons = new Dictionary<Texture, Texture>();

        internal static Texture EnsureUsableAlpha(Texture icon)
        {
            if (_cleanIcons.TryGetValue(icon, out Texture cached)) return cached;

            Texture result = icon;
            if (icon is Texture2D tex2d && tex2d.isReadable)
            {
                Color32[] px = tex2d.GetPixels32();
                byte minA = 255;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < minA) minA = px[i].a;
                }
                if (minA > 200) // no meaningful alpha -> derive from brightness
                {
                    for (int i = 0; i < px.Length; i++)
                    {
                        px[i].a = (byte)((px[i].r + px[i].g + px[i].b) / 3);
                    }
                    var clean = new Texture2D(tex2d.width, tex2d.height, TextureFormat.RGBA32, true);
                    clean.SetPixels32(px);
                    clean.Apply(true);
                    result = clean;
                }
            }
            _cleanIcons.Add(icon, result);
            return result;
        }

        internal static void MakeTransparent(Material mat)
        {
            if (!mat.HasProperty("_Surface")) return;
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // Lit sphere carrying the normal map, so the swatch shows the actual bumps.
        internal static GameObject CreateBumpSphere(string name, float size, Texture bump, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.55f);
            if (bump != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bump);
                mat.SetTextureScale("_BumpMap", new Vector2(2f, 2f));
                mat.SetFloat("_BumpScale", 1.5f);
                mat.EnableKeyword("_NORMALMAP");
            }
            var renderer = go.GetComponent<Renderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Mesh _ringMesh;

        // Unit-diameter ring (circle stroke) in the XY plane.
        private static Mesh GetRingMesh()
        {
            if (_ringMesh != null) return _ringMesh;

            const int segments = 48;
            const float outer = 0.5f, inner = 0.42f;
            var verts = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * outer, sn * outer, 0f);
                verts[i * 2 + 1] = new Vector3(c * inner, sn * inner, 0f);
                uvs[i * 2] = new Vector2(c * 0.5f + 0.5f, sn * 0.5f + 0.5f);
                uvs[i * 2 + 1] = uvs[i * 2];

                int n = (i + 1) % segments;
                tris[i * 6] = i * 2;
                tris[i * 6 + 1] = i * 2 + 1;
                tris[i * 6 + 2] = n * 2;
                tris[i * 6 + 3] = n * 2;
                tris[i * 6 + 4] = i * 2 + 1;
                tris[i * 6 + 5] = n * 2 + 1;
            }
            _ringMesh = new Mesh { name = "MenuRing", vertices = verts, uv = uvs, triangles = tris };
            _ringMesh.RecalculateNormals();
            _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        internal static GameObject CreateRing(string label, float size, Color color, Transform parent)
        {
            GameObject go = CreateDisc(label, size, color, parent);
            go.GetComponent<MeshFilter>().sharedMesh = GetRingMesh();
            return go;
        }

        private static Mesh _rectMesh;

        // Unit 1x1 rectangle in the XY plane (scale = width/height).
        private static Mesh GetRectMesh()
        {
            if (_rectMesh != null) return _rectMesh;
            _rectMesh = new Mesh
            {
                name = "MenuRect",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f),
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
            _rectMesh.RecalculateNormals();
            _rectMesh.RecalculateBounds();
            return _rectMesh;
        }

        internal static GameObject CreateRect(string label, Color color, Transform parent)
        {
            GameObject go = CreateDisc(label, 1f, color, parent);
            go.GetComponent<MeshFilter>().sharedMesh = GetRectMesh();
            return go;
        }

        internal static GameObject CreateDisc(string label, float size, Color color, Transform parent)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = GetDiscMesh();
            var renderer = go.AddComponent<MeshRenderer>();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0); // double-sided
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        internal static void AddLabel(Transform parent, string text, float height, float yOffset = 0f)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, -0.01f);
            go.transform.localScale = Vector3.one * 0.06f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = height * 6f;
            tm.fontSize = 48;
            tm.color = Color.white;

            // Runtime-created TextMesh has no font -> assign the built-in one,
            // or the label renders nothing.
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { /* older Unity name */ }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }
        }

        // Track along the wrist (thumb->pinky direction), knob draggable with the
        // opposite index finger. Maps to PaintModeManager.StrokeWidth.
        private void UpdateSlider(Vector3 wrist, Vector3 palmOut, Vector3 fingerDir, Quaternion faceHead)
        {
            Vector3 axis = Vector3.Cross(palmOut, fingerDir).normalized;
            Vector3 center = wrist + palmOut * _layout.wristButtonPalmOffset
                             - fingerDir * _layout.sliderForearmOffset;

            // Orient the track so its long (local X) axis IS the drag axis, so
            // the bar and the knob path always line up.
            Quaternion trackRot = Quaternion.LookRotation(-palmOut, Vector3.Cross(-palmOut, axis));
            _sliderTrack.SetPositionAndRotation(center, trackRot);
            _sliderTrack.localScale = new Vector3(_layout.sliderTrackLength, _layout.sliderTrackThickness, 1f);

            float width = PaintModeManager.StrokeWidth > 0f
                ? PaintModeManager.StrokeWidth : _layout.minStrokeWidth;
            float t = Mathf.InverseLerp(_layout.minStrokeWidth, _layout.maxStrokeWidth, width);

            // Dragging: no debounce, continuous while the fingertip stays close.
            if (_oppositeIndexTip != null)
            {
                Vector3 tip = _oppositeIndexTip.position;
                float grabRange = _sliderDragging ? _layout.pressRange * 2.5f : _layout.pressRange;
                Vector3 knobPos = center + axis * ((t - 0.5f) * _layout.sliderTrackLength);
                if ((tip - knobPos).sqrMagnitude < grabRange * grabRange)
                {
                    _sliderDragging = true;
                    t = Mathf.Clamp01(Vector3.Dot(tip - center, axis) / _layout.sliderTrackLength + 0.5f);
                    PaintModeManager.SetStrokeWidth(
                        Mathf.Lerp(_layout.minStrokeWidth, _layout.maxStrokeWidth, t));

                    // Detent ticks on the dragging finger: one soft click per 1/12
                    // of the track, stronger at both ends.
                    int detent = Mathf.RoundToInt(t * 12f);
                    if (detent != _lastSliderDetent)
                    {
                        _lastSliderDetent = detent;
                        bool endStop = detent == 0 || detent == 12;
                        // Detents on THIS hand's wrist (the one wearing the slider).
                        PositionType sliderGlove = _side == GloveSide.Left
                            ? PositionType.GloveL : PositionType.GloveR;
                        BhapticsLibrary.PlaySingleMotor(sliderGlove, (int)FingerId.Palm,
                            endStop ? 100 : 50, endStop ? 70 : 30);
                    }
                }
                else
                {
                    _sliderDragging = false;
                }
            }

            _sliderKnob.SetPositionAndRotation(
                center + axis * ((t - 0.5f) * _layout.sliderTrackLength), faceHead);
            if (_sliderLabel != null)
            {
                float shown = PaintModeManager.StrokeWidth > 0f
                    ? PaintModeManager.StrokeWidth : _layout.minStrokeWidth;
                _sliderLabel.text = $"{shown * 1000f:0}mm";
            }
        }

        private void Click()
        {
            _lastPressTime = Time.time;
            // Buzz the pressing (opposite) index finger.
            PositionType glove = _side == GloveSide.Left ? PositionType.GloveR : PositionType.GloveL;
            BhapticsLibrary.PlaySingleMotor(glove, (int)FingerId.Index, 80, 60);
        }

        private void RefreshHighlights()
        {
            for (int i = 0; i < _modeRenderers.Count; i++)
            {
                bool selected = PaintModeManager.Mode == Modes[i];
                _modeButtons[i].localScale = Vector3.one * (selected ? _layout.modeButtonSelectedSize : _layout.modeButtonSize);
                var mat = _modeRenderers[i].material;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", selected ? ModeTints[i] : ModeTints[i] * 0.45f);
                }
            }
            for (int i = 0; i < _swatchRenderers.Count; i++)
            {
                bool selected = _side == GloveSide.Left
                    ? PaintModeManager.ActiveColor == _paletteColors[i]
                    : PaintModeManager.ActiveTextureIndex == i % Mathf.Max(_paletteTextures.Length, 1);
                _swatches[i].localScale = Vector3.one * (selected ? _layout.swatchSelectedSize : _layout.swatchSize);
            }

            if (_indicatorRenderer != null)
            {
                Material mat = _indicatorRenderer.material;
                if (_side == GloveSide.Right)
                {
                    // Right hand paints color.
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", PaintModeManager.ActiveColor);
                }
                else if (_paletteTextures.Length > 0 && mat.HasProperty("_BumpMap"))
                {
                    // Left hand paints the selected normal map.
                    mat.SetTexture("_BumpMap",
                        _paletteTextures[PaintModeManager.ActiveTextureIndex % _paletteTextures.Length]);
                }
            }
        }

        private static Transform FindBone(OVRSkeleton skeleton,
            OVRSkeleton.BoneId classicId, OVRSkeleton.BoneId xrId)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return null;
            var type = skeleton.GetSkeletonType();
            bool isXR = type == OVRSkeleton.SkeletonType.XRHandLeft ||
                        type == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId id = isXR ? xrId : classicId;
            IList<OVRBone> bones = skeleton.Bones;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].Id == id) return bones[i].Transform;
            }
            return null;
        }
    }
}
