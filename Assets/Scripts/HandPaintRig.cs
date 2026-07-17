// Index-finger painting rig for one tracked OVR hand: a single contact point on
// the index fingertip carrying FingerTipContact + FingerTipPainter +
// FingerTipHaptic. Haptics fire the glove's index motor.
//
// Supports both classic (Hand_*) and OpenXR (XRHand_*) skeletons.
// Added at runtime by FingerPaintMeshBootstrap.

using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    public class HandPaintRig : MonoBehaviour
    {
        [SerializeField] private OVRSkeleton _skeleton;
        [SerializeField] private GloveSide _side = GloveSide.Right;
        [SerializeField] private FingerPaintBrush _brush;
        [SerializeField] private LayerMask _contactMask = ~0;
        [Tooltip("Contact detection radius around the fingertip, metres.")]
        [SerializeField] private float _contactRadius = 0.02f;
        [Tooltip("Stroke width in metres.")]
        [SerializeField] private float _strokeWidth = 0.02f;
        [Tooltip("Paint color (alpha 0 = palette fallback). Ignored by relief brushes.")]
        [SerializeField] private Color _color = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Haptic feel for this hand (SmoothFlow for color, BumpGrid for normal map).")]
        [SerializeField] private FingerTipHaptic.Style _hapticStyle = FingerTipHaptic.Style.SmoothFlow;
        [SerializeField] private FingerTipHaptic.PaintHapticSettings _hapticSettings =
            FingerTipHaptic.PaintHapticSettings.Default;

        private Transform _tip;      // contact object, follows the index tip bone
        private Transform _tipBone;
        private bool _built;

        public void Init(OVRSkeleton skeleton, GloveSide side, FingerPaintBrush brush,
            LayerMask contactMask, float contactRadius, float strokeWidth, Color color,
            FingerTipHaptic.Style hapticStyle, FingerTipHaptic.PaintHapticSettings hapticSettings)
        {
            _skeleton = skeleton;
            _side = side;
            _brush = brush;
            _contactMask = contactMask;
            _contactRadius = contactRadius;
            _strokeWidth = strokeWidth;
            _color = color;
            _hapticStyle = hapticStyle;
            _hapticSettings = hapticSettings;
        }

        private void Update()
        {
            if (_skeleton == null || !_skeleton.IsInitialized ||
                _skeleton.Bones == null || _skeleton.Bones.Count == 0)
            {
                return;
            }

            if (!_built) Build();
            if (_tip != null && _tipBone != null)
            {
                _tip.SetPositionAndRotation(_tipBone.position, _tipBone.rotation);
            }
            for (int i = 0; i < _feelTips.Count; i++)
            {
                _feelTips[i].tip.SetPositionAndRotation(
                    _feelTips[i].bone.position, _feelTips[i].bone.rotation);
            }
        }

        // Non-index fingers: contact + Feel-mode-only haptics (they never paint).
        private readonly System.Collections.Generic.List<(Transform tip, Transform bone)> _feelTips =
            new System.Collections.Generic.List<(Transform, Transform)>();

        private void Build()
        {
            // Classic (Hand_*) and OpenXR (XRHand_*) bone ids share one numeric
            // space (e.g. Hand_IndexTip == 20 == XRHand_RingTip), so the enum
            // family MUST be chosen from the skeleton type -- probing both in
            // order grabs the wrong finger on XR skeletons.
            var type = _skeleton.GetSkeletonType();
            bool isXR = type == OVRSkeleton.SkeletonType.XRHandLeft ||
                        type == OVRSkeleton.SkeletonType.XRHandRight;
            _tipBone = FindBone(isXR
                ? OVRSkeleton.BoneId.XRHand_IndexTip
                : OVRSkeleton.BoneId.Hand_IndexTip);
            if (_tipBone == null)
            {
                Debug.LogWarning($"{name}: no index tip bone found on {_side} hand.", this);
                enabled = false;
                return;
            }

            var tip = new GameObject($"IndexPaint_{_side}");
            tip.transform.SetParent(transform, false);
            _tip = tip.transform;

            var contact = tip.AddComponent<FingerTipContact>();
            contact.Configure(_side, FingerId.Index, _contactMask, _contactRadius);
            var painter = tip.AddComponent<FingerTipPainter>();
            painter.Configure(_brush, _strokeWidth, _color);
            // The color hand follows the menu palette selection.
            painter.UseGlobalColor = _side == GloveSide.Right;
            tip.AddComponent<FingerTipHaptic>().Configure(_hapticStyle, _hapticSettings);

            // The other four fingers feel (Feel mode only), but never paint.
            var feelFingers = new (FingerId finger, OVRSkeleton.BoneId classic, OVRSkeleton.BoneId xr)[]
            {
                (FingerId.Thumb, OVRSkeleton.BoneId.Hand_ThumbTip, OVRSkeleton.BoneId.XRHand_ThumbTip),
                (FingerId.Middle, OVRSkeleton.BoneId.Hand_MiddleTip, OVRSkeleton.BoneId.XRHand_MiddleTip),
                (FingerId.Ring, OVRSkeleton.BoneId.Hand_RingTip, OVRSkeleton.BoneId.XRHand_RingTip),
                (FingerId.Pinky, OVRSkeleton.BoneId.Hand_PinkyTip, OVRSkeleton.BoneId.XRHand_LittleTip),
            };
            foreach (var (finger, classic, xr) in feelFingers)
            {
                Transform bone = FindBone(isXR ? xr : classic);
                if (bone == null) continue;

                var feelTip = new GameObject($"FeelTip_{_side}_{finger}");
                feelTip.transform.SetParent(transform, false);
                var feelContact = feelTip.AddComponent<FingerTipContact>();
                feelContact.Configure(_side, finger, _contactMask, _contactRadius);
                var haptic = feelTip.AddComponent<FingerTipHaptic>();
                haptic.Configure(_hapticStyle, _hapticSettings);
                haptic.FeelModeOnly = true;
                _feelTips.Add((feelTip.transform, bone));
            }

            _built = true;
            Debug.Log($"{name}: paint rig built for {_side} hand (index paints, all fingers feel).");
        }

        private Transform FindBone(OVRSkeleton.BoneId id)
        {
            IList<OVRBone> bones = _skeleton.Bones;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].Id == id) return bones[i].Transform;
            }
            return null;
        }
    }
}
