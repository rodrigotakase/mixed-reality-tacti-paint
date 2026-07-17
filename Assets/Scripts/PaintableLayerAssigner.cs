// Assigns the "Paintable" layer to scanned room geometry at runtime.
//
// Scene meshes/anchors (MRUK global mesh, OVRSceneManager anchors, etc.) are
// spawned asynchronously AFTER the scene model loads, so their colliders don't
// exist when the scene starts -- you can't set the layer by hand in the editor.
// This scans for new colliders under a root and stamps them with Paintable, so
// FingerTipContact's Contact Mask (set to Paintable) picks them up.
//
// Works with any scene system: just point Scene Root at whatever the scanned
// geometry spawns under (the MRUK room, the OVRSceneManager object, or leave it
// empty to scan the whole scene).

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    public class PaintableLayerAssigner : MonoBehaviour
    {
        [Tooltip("Parent the scanned geometry spawns under (MRUK room / OVRSceneManager). " +
                 "Empty = scan the whole scene (heavier; may catch unrelated meshes).")]
        [SerializeField] private Transform _sceneRoot;

        [Tooltip("Layer to assign. Must exist in Tags & Layers.")]
        [SerializeField] private string _layerName = "Paintable";

        [Tooltip("Only stamp MeshColliders (the scanned surfaces), not box/capsule etc.")]
        [SerializeField] private bool _meshCollidersOnly = true;

        [Tooltip("Seconds between scans while waiting for geometry to spawn.")]
        [SerializeField] private float _scanInterval = 0.5f;

        [Tooltip("How long to keep scanning, in seconds. 0 = forever (catches meshes " +
                 "that keep refining as the room is re-scanned).")]
        [SerializeField] private float _scanDuration = 20f;

        private int _layer = -1;
        private readonly HashSet<Collider> _stamped = new HashSet<Collider>();

        private void Start()
        {
            _layer = LayerMask.NameToLayer(_layerName);
            if (_layer < 0)
            {
                Debug.LogError($"{name}: layer \"{_layerName}\" doesn't exist. " +
                               "Add it in Project Settings > Tags and Layers.", this);
                enabled = false;
                return;
            }
            StartCoroutine(ScanLoop());
        }

        private IEnumerator ScanLoop()
        {
            float elapsed = 0f;
            var wait = new WaitForSeconds(_scanInterval);
            while (_scanDuration <= 0f || elapsed < _scanDuration)
            {
                AssignNow();
                yield return wait;
                elapsed += _scanInterval;
            }
        }

        /// <summary>Stamp all matching colliders under the root right now. Safe to
        /// call manually (e.g. from a "rescan" button).</summary>
        public void AssignNow()
        {
            Collider[] colliders = _sceneRoot != null
                ? _sceneRoot.GetComponentsInChildren<Collider>(true)
                : FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            int newlyStamped = 0;
            foreach (var col in colliders)
            {
                if (_meshCollidersOnly && !(col is MeshCollider)) continue;
                if (!_stamped.Add(col)) continue;          // already handled

                col.gameObject.layer = _layer;
                newlyStamped++;
            }

            if (newlyStamped > 0)
            {
                Debug.Log($"{name}: assigned \"{_layerName}\" to {newlyStamped} scanned collider(s).");
            }
        }
    }
}
