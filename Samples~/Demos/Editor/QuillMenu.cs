// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
using Quill.Samples;
using UnityEditor;
using UnityEngine;

namespace Quill.Editor
{
    /// <summary>One-click scene setup so you can see the system running without manual wiring.</summary>
    public static class QuillMenu
    {
        [MenuItem("GameObject/Quill/Demo Surface", false, 10)]
        public static void CreateDemoSurface()
        {
            var go = new GameObject("Quill Demo");
            go.AddComponent<QuillDemo>();   // QuillSurface is added automatically at Play time.

            Undo.RegisterCreatedObjectUndo(go, "Create Quill Demo");
            Selection.activeObject = go;

            if (Camera.main == null)
                Debug.LogWarning("[Quill] No camera tagged MainCamera in the scene — add one so the surface is rendered.");
        }
    }
}
