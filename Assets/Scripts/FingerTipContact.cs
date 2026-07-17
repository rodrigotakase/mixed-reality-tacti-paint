// Single source of truth for "is this fingertip touching a surface, and where".
// Attach ONE per fingertip. Other components (FingerTipHaptic, FingerTipPainter)
// live on the same object and react to its events -- so contact is detected once
// and drives both vibration and painting.
//
// Detection defaults to a Physics.OverlapSphere at the tip because AutoHand hands
// are a single Rigidbody with a compound of child colliders: Unity would deliver
// OnTriggerEnter/OnCollisionEnter to the HAND, not the fingertip. OverlapSphere
// sidesteps that and doesn't disturb AutoHand's physics. Switch to PhysicsEvents
// only if the tip has its own Rigidbody.

using System;
using UnityEngine;

namespace FingerPaint
{
    public enum GloveSide { Left, Right }

    // Values match bHaptics glove motor indices exactly -- do not reorder.
    // Palm maps to motor 5 (the wrist motor on both TactGlove DK2 and DK3).
    public enum FingerId { Thumb = 0, Index = 1, Middle = 2, Ring = 3, Pinky = 4, Palm = 5 }

    public enum ContactDetectionMode { OverlapSphere, PhysicsEvents }

    public class FingerTipContact : MonoBehaviour
    {
        [Header("Finger identity (set once per tip)")]
        [SerializeField] private GloveSide _side = GloveSide.Right;
        [SerializeField] private FingerId _finger = FingerId.Index;

        [Header("Detection")]
        [SerializeField] private ContactDetectionMode _mode = ContactDetectionMode.OverlapSphere;
        [Tooltip("Only these layers count as paintable/touchable. Leave the hand's own layer OUT.")]
        [SerializeField] private LayerMask _contactMask = ~0;
        [Tooltip("Overlap sphere radius (OverlapSphere mode). 0 = auto from this " +
                 "object's collider, falling back to 1 cm.")]
        [SerializeField] private float _overlapRadius = 0f;
        [Tooltip("Only touch horizontal or vertical surfaces (desk tops, walls); " +
                 "slanted faces are ignored. Also prevents 'painting' in mid-air " +
                 "inside volume anchors -- contact requires an actual face nearby.")]
        [SerializeField] private bool _axisAlignedSurfacesOnly = true;

        /// <summary>Fired the first frame this tip starts touching a surface.</summary>
        public event Action<FingerTipContact> ContactStarted;
        /// <summary>Fired every physics step the tip stays in contact.</summary>
        public event Action<FingerTipContact> ContactStayed;
        /// <summary>Fired the frame the tip leaves the surface.</summary>
        public event Action<FingerTipContact> ContactEnded;

        public GloveSide Side => _side;
        public FingerId Finger => _finger;
        public bool IsTouching { get; private set; }
        /// <summary>World-space point on the touched surface (approx).</summary>
        public Vector3 ContactPoint { get; private set; }
        /// <summary>World-space surface normal at the contact (approx).</summary>
        public Vector3 ContactNormal { get; private set; }
        public Collider CurrentCollider { get; private set; }

        private Rigidbody _handBody;      // used to ignore the hand's own colliders
        private float _effectiveRadius;

        // Stroke plane lock: the surface plane captured at first contact. Every
        // later contact point in the same touch is projected onto it, so a stroke
        // on a (lumpy-scanned) flat table stays at ONE constant height instead of
        // following each mesh triangle's hit point.
        private Vector3 _planePoint;
        private Vector3 _planeNormal;
        private bool _planeLocked;
        private static readonly Collider[] _overlapBuffer = new Collider[16];

        // Probe directions used to recover a surface point/normal on non-convex
        // MeshColliders (which don't support Collider.ClosestPoint).
        private static readonly Vector3[] _probeDirs =
        {
            Vector3.down, Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        };

        private void Awake()
        {
            _handBody = GetComponentInParent<Rigidbody>();
            _effectiveRadius = ResolveRadius();
        }

        /// <summary>Set up this contact from code (for tips spawned at runtime).
        /// Call right after AddComponent, before adding painter/haptic.</summary>
        public void Configure(GloveSide side, FingerId finger, LayerMask contactMask, float overlapRadius)
        {
            _side = side;
            _finger = finger;
            _contactMask = contactMask;
            _overlapRadius = overlapRadius;
            _effectiveRadius = ResolveRadius();
        }

        private float ResolveRadius()
        {
            if (_overlapRadius > 0f) return _overlapRadius;

            if (TryGetComponent(out Collider col))
            {
                Vector3 e = col.bounds.extents;
                float r = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
                if (r > 0f) return r;
            }
            return 0.01f;
        }

        private void FixedUpdate()
        {
            if (_mode != ContactDetectionMode.OverlapSphere) return;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _effectiveRadius, _overlapBuffer,
                _contactMask, QueryTriggerInteraction.Ignore);

            // A candidate collider only counts as touched if an actual FACE of it
            // is within the contact radius (being inside a volume anchor with no
            // face nearby is NOT contact) and the face orientation is allowed.
            //
            // The desk area usually has TWO overlapping paintable colliders (the
            // TABLE anchor box and the lumpy scanned mesh) at slightly different
            // heights. Two rules keep the paint at ONE consistent height:
            //  1. While already touching, STAY on the same collider (a stroke can
            //     never hop surfaces mid-way).
            //  2. When (re)starting contact, prefer primitive/convex colliders
            //     (clean flat anchor faces) over non-convex scanned meshes.
            bool touching = false;

            if (IsTouching && CurrentCollider != null &&
                TryFindSurface(CurrentCollider, out Vector3 lockedPoint, out Vector3 lockedNormal, out _))
            {
                ContactPoint = lockedPoint;
                ContactNormal = lockedNormal;
                touching = true;
            }
            else
            {
                // Prefer the SMALLEST qualifying collider: a desk/wall anchor is
                // furniture-sized while the scanned room mesh spans the whole
                // room, so anchors (clean flat faces) always win over the lumpy
                // global mesh. Distance breaks ties between similar-size options.
                float bestDist = float.MaxValue;
                float bestVolume = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    Collider candidate = _overlapBuffer[i];
                    if (!IsExternal(candidate)) continue;
                    if (!TryFindSurface(candidate, out Vector3 point, out Vector3 normal, out float dist))
                    {
                        continue;
                    }

                    Vector3 size = candidate.bounds.size;
                    float volume = size.x * size.y * size.z;
                    bool smaller = volume < bestVolume * 0.9f;
                    bool similar = volume < bestVolume * 1.1f;
                    if (!smaller && !(similar && dist < bestDist)) continue;

                    bestDist = dist;
                    bestVolume = volume;
                    CurrentCollider = candidate;
                    ContactPoint = point;
                    ContactNormal = normal;
                    touching = true;
                }
            }

            if (touching)
            {
                // First frame of a touch: capture the surface plane. On a box
                // collider (scene anchors like the table) use the box's OWN face
                // plane -- identical for every stroke on that face, even if the
                // anchor is slightly tilted. On meshes, use the first hit.
                if (!_planeLocked)
                {
                    if (CurrentCollider is BoxCollider box)
                    {
                        LockToBoxFace(box);
                    }
                    else
                    {
                        _planePoint = ContactPoint;
                        _planeNormal = ContactNormal;
                    }
                    _planeLocked = true;
                }

                // Flatten every contact (including the first) onto the locked
                // plane -- constant stroke height, stable orientation.
                ContactPoint -= _planeNormal *
                    Vector3.Dot(ContactPoint - _planePoint, _planeNormal);
                ContactNormal = _planeNormal;

                Report(started: !IsTouching);
                IsTouching = true;
            }
            else if (IsTouching)
            {
                IsTouching = false;
                CurrentCollider = null;
                _planeLocked = false;
                ContactEnded?.Invoke(this);
            }
            else
            {
                _planeLocked = false;
            }
        }

        #region Physics-event mode
        private void OnTriggerEnter(Collider other) => HandlePhysicsContact(other, entering: true);
        private void OnTriggerStay(Collider other) => HandlePhysicsContact(other, entering: false);
        private void OnTriggerExit(Collider other) => HandlePhysicsExit(other);
        private void OnCollisionEnter(Collision c) => HandlePhysicsContact(c.collider, entering: true);
        private void OnCollisionStay(Collision c) => HandlePhysicsContact(c.collider, entering: false);
        private void OnCollisionExit(Collision c) => HandlePhysicsExit(c.collider);

        private void HandlePhysicsContact(Collider other, bool entering)
        {
            if (_mode != ContactDetectionMode.PhysicsEvents || !IsExternal(other)) return;
            if (!TryFindSurface(other, out Vector3 point, out Vector3 normal, out _)) return;
            CurrentCollider = other;
            ContactPoint = point;
            ContactNormal = normal;
            Report(started: entering || !IsTouching);
            IsTouching = true;
        }

        private void HandlePhysicsExit(Collider other)
        {
            if (_mode != ContactDetectionMode.PhysicsEvents || other != CurrentCollider) return;
            IsTouching = false;
            CurrentCollider = null;
            ContactEnded?.Invoke(this);
        }
        #endregion

        // Finds the nearest actual FACE of the collider via outside-in raycasts
        // along the world axes. Works uniformly for boxes/volumes (rays start
        // outside, so faces are hit even when the tip is INSIDE the volume) and
        // for the non-convex room mesh (which doesn't support ClosestPoint).
        //
        // Returns true only when a face is within the contact radius AND its
        // orientation passes the horizontal/vertical filter. Being deep inside a
        // volume anchor finds no nearby face -> no contact -> no mid-air painting.
        private bool TryFindSurface(Collider other, out Vector3 point, out Vector3 normal, out float distance)
        {
            Vector3 tip = transform.position;
            float range = Mathf.Max(_effectiveRadius * 4f, 0.05f);

            float best = float.MaxValue;
            point = tip;
            normal = Vector3.up;

            for (int i = 0; i < _probeDirs.Length; i++)
            {
                Vector3 dir = _probeDirs[i];
                var ray = new Ray(tip - dir * range, dir);
                if (!other.Raycast(ray, out RaycastHit hit, range * 2f)) continue;
                if (!IsAllowedOrientation(hit.normal)) continue;

                float dist = Vector3.Distance(hit.point, tip);
                if (dist < best)
                {
                    best = dist;
                    point = hit.point;
                    normal = hit.normal;
                }
            }

            distance = best;
            if (best <= _effectiveRadius)
            {
                if (_axisAlignedSurfacesOnly) normal = SnapNormal(normal);
                return true;
            }
            return false;
        }

        // Locks the stroke plane to the touched FACE of a box collider: the same
        // face always yields the same plane, so all strokes on the table top are
        // at exactly the same height -- regardless of where they start or small
        // anchor tilt.
        private void LockToBoxFace(BoxCollider box)
        {
            Transform bt = box.transform;
            Vector3 localNormal = bt.InverseTransformDirection(ContactNormal);

            int axis = 0;
            float ax = Mathf.Abs(localNormal.x), ay = Mathf.Abs(localNormal.y), az = Mathf.Abs(localNormal.z);
            if (ay >= ax && ay >= az) axis = 1;
            else if (az >= ax && az >= ay) axis = 2;
            float sign = Mathf.Sign(localNormal[axis]);

            Vector3 faceLocalNormal = Vector3.zero;
            faceLocalNormal[axis] = sign;
            Vector3 faceCenterLocal = box.center +
                Vector3.Scale(box.size * 0.5f, faceLocalNormal);

            _planePoint = bt.TransformPoint(faceCenterLocal);
            _planeNormal = bt.TransformDirection(faceLocalNormal).normalized;
        }

        // Horizontal surfaces have a near-vertical normal (|y| high); vertical
        // surfaces a near-horizontal one (|y| low). Slanted faces are rejected.
        private bool IsAllowedOrientation(Vector3 n)
        {
            if (!_axisAlignedSurfacesOnly) return true;
            float y = Mathf.Abs(n.y);
            return y > 0.75f || y < 0.25f;
        }

        // The scanned mesh's triangle normals wobble a few degrees even on flat
        // surfaces, which tilts the paint. Snap: near-vertical normals become
        // exactly up/down (flat paint), near-horizontal ones become exactly
        // horizontal (upright paint on walls).
        private static Vector3 SnapNormal(Vector3 n)
        {
            if (Mathf.Abs(n.y) > 0.75f)
            {
                return n.y > 0f ? Vector3.up : Vector3.down;
            }
            var flat = new Vector3(n.x, 0f, n.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.up;
        }

        private void Report(bool started)
        {
            if (started) ContactStarted?.Invoke(this);
            ContactStayed?.Invoke(this);
        }

        private bool IsExternal(Collider other)
        {
            if (other == null) return false;
            if (_handBody != null && other.attachedRigidbody == _handBody) return false;
            return (_contactMask.value & (1 << other.gameObject.layer)) != 0;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsTouching ? Color.green : Color.cyan;
            float r = Application.isPlaying ? _effectiveRadius : ResolveRadius();
            Gizmos.DrawWireSphere(transform.position, r);
        }
    }
}
