// Keeps a hand-tracked SyntheticHand from visually sinking through a static
// surface collider (e.g. the desk). It mirrors what Meta's HandPokeLimiterVisual
// does -- pushing the whole hand back via SyntheticHand.LockWristPose so the
// fingertips rest ON the surface -- but detects the surface directly from a
// Collider instead of requiring a fully-configured PokeInteractable graph.
//
// Setup (one component per hand):
//   1. Add this component to each hand under OVRComprehensiveInteractionRig
//      (a good spot is the same GameObject that holds the SyntheticHand).
//   2. Synthetic Hand : drag that hand's SyntheticHand component.
//   3. Tracked Hand   : drag the upstream tracked hand feeding it (any IHand,
//      e.g. the OVRHand / Hand that the SyntheticHand modifies). This must be
//      the *tracked* hand, not the SyntheticHand, or you get a feedback loop.
//   4. Surface        : drag the DeskSurface's Collider.

using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;         // Interface attribute
using Oculus.Interaction.Input;   // IHand, SyntheticHand, HandJointId

namespace FingerPaint
{
    public class HandDeskCollisionLimiter : MonoBehaviour
    {
        [Tooltip("The rendered hand that gets pushed back. This is a SyntheticHand " +
                 "component inside the interaction rig.")]
        [SerializeField]
        private SyntheticHand _syntheticHand;

        [Tooltip("The tracked hand that drives the synthetic hand (any IHand). " +
                 "Must be the upstream tracked hand, NOT the synthetic hand.")]
        [SerializeField, Interface(typeof(IHand))]
        private Object _trackedHand;
        private IHand TrackedHand;

        [Tooltip("The surface the hand should not pass through, e.g. the desk BoxCollider. " +
                 "Its top face (bounds.max.y) is treated as the surface plane.")]
        [SerializeField]
        private Collider _surface;

        [Tooltip("Fingertip joints checked for penetration. The deepest one decides " +
                 "how far the hand is lifted.")]
        [SerializeField]
        private List<HandJointId> _fingertips = new List<HandJointId>
        {
            HandJointId.HandThumbTip,
            HandJointId.HandIndexTip,
            HandJointId.HandMiddleTip,
            HandJointId.HandRingTip,
            HandJointId.HandPinkyTip,
        };

        [Tooltip("Effective radius of a fingertip, so the tip rests slightly above the " +
                 "surface instead of exactly on it.")]
        [SerializeField]
        private float _fingerRadius = 0.008f;

        [Tooltip("How far past the surface edges (in metres) a fingertip still counts as " +
                 "being over the surface. Keeps the clamp from popping off at the rim.")]
        [SerializeField]
        private float _edgeTolerance = 0.02f;

        [Tooltip("Safety cap on how far the hand may be lifted, so bad tracking under the " +
                 "desk can't fling the hand upward.")]
        [SerializeField]
        private float _maxLift = 0.15f;

        private bool _isLocked;

        private void Awake()
        {
            TrackedHand = _trackedHand as IHand;
        }

        private void OnDisable()
        {
            if (_isLocked)
            {
                FreeHand();
            }
        }

        // LateUpdate so we run after the hand data has been sampled for the frame,
        // matching HandPokeLimiterVisual's timing.
        private void LateUpdate()
        {
            if (_syntheticHand == null || TrackedHand == null || _surface == null)
            {
                return;
            }

            if (!TrackedHand.IsTrackedDataValid ||
                !TrackedHand.GetRootPose(out Pose rootPose))
            {
                if (_isLocked) FreeHand();
                return;
            }

            Bounds bounds = _surface.bounds;
            float surfaceY = bounds.max.y + _fingerRadius;

            // Find the fingertip that is deepest below the surface plane while still
            // horizontally over the surface.
            float deepestPenetration = 0f;
            for (int i = 0; i < _fingertips.Count; i++)
            {
                if (!TrackedHand.GetJointPose(_fingertips[i], out Pose tip))
                {
                    continue;
                }

                if (!IsOverSurface(tip.position, bounds))
                {
                    continue;
                }

                float penetration = surfaceY - tip.position.y;
                if (penetration > deepestPenetration)
                {
                    deepestPenetration = penetration;
                }
            }

            if (deepestPenetration > 0f)
            {
                float lift = Mathf.Min(deepestPenetration, _maxLift);
                Pose target = new Pose(
                    rootPose.position + Vector3.up * lift,
                    rootPose.rotation);

                // worldPose:true because target is in world space; skipAnimation:true
                // to track the finger without lag.
                _syntheticHand.LockWristPose(target, 1f,
                    SyntheticHand.WristLockMode.Full, true, true);
                _syntheticHand.MarkInputDataRequiresUpdate();
                _isLocked = true;
            }
            else if (_isLocked)
            {
                FreeHand();
            }
        }

        private bool IsOverSurface(Vector3 worldPos, Bounds bounds)
        {
            return worldPos.x >= bounds.min.x - _edgeTolerance &&
                   worldPos.x <= bounds.max.x + _edgeTolerance &&
                   worldPos.z >= bounds.min.z - _edgeTolerance &&
                   worldPos.z <= bounds.max.z + _edgeTolerance;
        }

        private void FreeHand()
        {
            _syntheticHand.FreeWrist();
            _isLocked = false;
        }

        #region Inject
        public void InjectAll(SyntheticHand syntheticHand, IHand trackedHand, Collider surface)
        {
            _syntheticHand = syntheticHand;
            _trackedHand = trackedHand as Object;
            TrackedHand = trackedHand;
            _surface = surface;
        }
        #endregion
    }
}
