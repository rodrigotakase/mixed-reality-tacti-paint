// Adds "GameObject > FingerPaint > Hand Menu Preview" to create an edit-mode
// preview of the wrist menu + palettes (see HandMenuPreview).

using UnityEditor;
using UnityEngine;

namespace FingerPaint.EditorTools
{
    public static class HandMenuPreviewMenu
    {
        [MenuItem("GameObject/FingerPaint/Hand Menu Preview", false, 10)]
        public static void CreatePreview()
        {
            var go = new GameObject("HandMenuPreview");
            go.AddComponent<HandMenuPreview>();

            // Put it in front of the scene camera so it's immediately visible.
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                go.transform.position = view.pivot;
                go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Create Hand Menu Preview");
        }
    }
}
