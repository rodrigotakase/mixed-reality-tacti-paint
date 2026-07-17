// Keeps the FloatingMenu hovering in front of the user, always facing them, at
// a comfortable reach distance. Lazy-follow: the menu stays put while you look
// around near it, and glides to a new spot only when you turn far enough away
// (like the Quest system menus).
//
// Dismissing: touching the Dismiss button with EITHER index finger hides the
// whole menu (SetActive(false)) with a haptic click. The button is auto-found:
// a descendant whose name contains "dismiss", a descendant with a Collider, or
// the parent of a TMP text reading "Dismiss" -- or assign it explicitly.

using System.Collections.Generic;
using UnityEngine;
using Bhaptics.SDK2;
using TMPro;

namespace FingerPaint
{
    public class FloatingMenuController : MonoBehaviour
    {
        [Header("Follow")]
        [Tooltip("Metres in front of the head -- keep within arm's reach.")]
        [SerializeField] private float _distance = 0.45f;
        [Tooltip("Metres above (+) / below (-) eye level.")]
        [SerializeField] private float _heightOffset = -0.12f;
        [Tooltip("How quickly the menu glides to its target pose.")]
        [SerializeField] private float _followSpeed = 4f;
        [Tooltip("Degrees the user must look away before the menu repositions.")]
        [SerializeField] private float _repositionAngle = 30f;

        [Header("Dismiss")]
        [Tooltip("The button the index finger touches to hide the menu. " +
                 "Empty = auto-find (name containing 'dismiss', a collider, or TMP text).")]
        [SerializeField] private Transform _dismissButton;
        [Tooltip("Touch distance (m) for the index fingertip.")]
        [SerializeField] private float _touchRange = 0.035f;

        private Vector3 _targetPos;
        private bool _hasTarget;
        private Collider _dismissCollider;
        private Transform _leftIndexTip, _rightIndexTip;

        private void Start()
        {
            if (_dismissButton == null) _dismissButton = FindDismissButton();
            if (_dismissButton != null) _dismissCollider = _dismissButton.GetComponentInChildren<Collider>();
            if (_dismissButton == null)
            {
                Debug.LogWarning($"{name}: no dismiss button found -- the menu can't be dismissed by touch.", this);
            }
        }

        private void LateUpdate()
        {
            Camera head = Camera.main;
            if (head == null) return;

            // Desired spot: in front of the head on the horizontal plane (so the
            // menu doesn't dive when the user looks down at the desk).
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-4f ? head.transform.forward : fwd.normalized;
            Vector3 desired = head.transform.position + fwd * _distance + Vector3.up * _heightOffset;

            if (!_hasTarget)
            {
                _targetPos = desired;
                transform.position = desired;
                _hasTarget = true;
            }
            else
            {
                Vector3 toTarget = _targetPos - head.transform.position;
                toTarget.y = 0f;
                float angle = Vector3.Angle(fwd, toTarget);
                float dist = toTarget.magnitude;
                if (angle > _repositionAngle || dist < _distance * 0.6f || dist > _distance * 1.5f)
                {
                    _targetPos = desired;    // drifted too far -> glide to new spot
                }
                else
                {
                    _targetPos.y = desired.y; // track height continuously
                }
            }

            float k = Time.deltaTime * _followSpeed;
            transform.position = Vector3.Lerp(transform.position, _targetPos, k);

            // Yaw-only facing: rotate around Y toward the user but stay upright,
            // never pitching/rolling with head height.
            Vector3 flatDir = transform.position - head.transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(flatDir.normalized, Vector3.up), k * 1.5f);
            }

            CheckDismissTouch();
        }

        // ------------------------------------------------------------- dismiss

        private void CheckDismissTouch()
        {
            if (_dismissButton == null) return;

            if (_leftIndexTip == null || _rightIndexTip == null) FindIndexTips();

            if (TipTouches(_leftIndexTip, out _))
            {
                Dismiss(PositionType.GloveL);
            }
            else if (TipTouches(_rightIndexTip, out _))
            {
                Dismiss(PositionType.GloveR);
            }
        }

        private bool TipTouches(Transform tip, out float dist)
        {
            dist = float.MaxValue;
            if (tip == null) return false;
            Vector3 p = tip.position;
            Vector3 closest = _dismissCollider != null
                ? _dismissCollider.ClosestPoint(p)
                : _dismissButton.position;
            dist = Vector3.Distance(closest, p);
            return dist < _touchRange;
        }

        private bool _dismissing;

        private void Dismiss(PositionType glove)
        {
            if (_dismissing) return;
            _dismissing = true;
            // Crisp button click on the pressing index finger. Hide one frame
            // later so the haptic call is dispatched before deactivation.
            BhapticsLibrary.PlaySingleMotor(glove, (int)FingerId.Index, 100, 80);
            StartCoroutine(HideNextFrame());
        }

        private System.Collections.IEnumerator HideNextFrame()
        {
            yield return null;
            _dismissing = false;
            gameObject.SetActive(false);
        }

        /// <summary>Re-show the menu (e.g. from another script or UnityEvent).</summary>
        public void Show()
        {
            _hasTarget = false; // snap to a fresh spot in front of the user
            gameObject.SetActive(true);
        }

        // --------------------------------------------------------------- setup

        private Transform FindDismissButton()
        {
            // 1. Any descendant named *dismiss*.
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != transform && t.name.ToLowerInvariant().Contains("dismiss")) return t;
            }
            // 2. The parent of a TMP text reading "dismiss".
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.text.ToLowerInvariant().Contains("dismiss"))
                {
                    return text.transform.parent != null ? text.transform.parent : text.transform;
                }
            }
            // 3. Any descendant with a collider (assumed to be the button).
            Collider col = GetComponentInChildren<Collider>(true);
            return col != null ? col.transform : null;
        }

        private void FindIndexTips()
        {
            OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (OVRSkeleton skeleton in skeletons)
            {
                if (!skeleton.IsInitialized || skeleton.Bones == null || skeleton.Bones.Count == 0) continue;
                var type = skeleton.GetSkeletonType();
                bool isXR = type == OVRSkeleton.SkeletonType.XRHandLeft ||
                            type == OVRSkeleton.SkeletonType.XRHandRight;
                bool isLeft = type == OVRSkeleton.SkeletonType.HandLeft ||
                              type == OVRSkeleton.SkeletonType.XRHandLeft;
                bool isRight = type == OVRSkeleton.SkeletonType.HandRight ||
                               type == OVRSkeleton.SkeletonType.XRHandRight;
                if (!isLeft && !isRight) continue;

                OVRSkeleton.BoneId id = isXR
                    ? OVRSkeleton.BoneId.XRHand_IndexTip
                    : OVRSkeleton.BoneId.Hand_IndexTip;
                IList<OVRBone> bones = skeleton.Bones;
                for (int i = 0; i < bones.Count; i++)
                {
                    if (bones[i].Id != id) continue;
                    if (isLeft) _leftIndexTip = bones[i].Transform;
                    else _rightIndexTip = bones[i].Transform;
                    break;
                }
            }
        }
    }
}
