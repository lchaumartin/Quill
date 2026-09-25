// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Quill.Editor
{
    /// <summary>
    /// Inspector for <see cref="QuillDocument"/>: the theme is picked from the themes installed in the
    /// project (every <c>Resources/QuillThemes/&lt;Name&gt;/Theme.quill</c>), and in Play mode changing it
    /// rebuilds the UI live.
    /// </summary>
    [CustomEditor(typeof(QuillDocument))]
    public sealed class QuillDocumentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(QuillDocument.Document)));
            ThemePopup(serializedObject.FindProperty(nameof(QuillDocument.Theme)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(QuillDocument.Components)), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(QuillDocument.StartVisible)));

            if (serializedObject.ApplyModifiedProperties() && Application.isPlaying)
                foreach (var t in targets)
                    if (t is QuillDocument doc && doc.isActiveAndEnabled) doc.Load();

            if (((QuillDocument)target).Document == null)
                EditorGUILayout.HelpBox("No document: the theme gallery is shown. Assign a .quill file to show your own UI.", MessageType.Info);
        }

        private static void ThemePopup(SerializedProperty prop)
        {
            var themes = InstalledThemes();
            if (!themes.Contains(prop.stringValue)) themes.Add(prop.stringValue);

            int index = themes.IndexOf(prop.stringValue);
            var label = new GUIContent("Theme", "Themes installed in this project (Resources/QuillThemes/<Name>). Import more from Quill's Samples in the Package Manager.");
            var options = new GUIContent[themes.Count];
            for (int i = 0; i < themes.Count; i++)
                options[i] = new GUIContent(themes[i] == QuillThemes.Default ? themes[i] + " (built-in)" : themes[i]);

            int picked = EditorGUILayout.Popup(label, index, options);
            if (picked != index) prop.stringValue = themes[picked];
        }

        /// <summary>Names of the theme folders in the project and packages, built-in first.</summary>
        public static List<string> InstalledThemes()
        {
            var names = new List<string> { QuillThemes.Default };
            string marker = "/Resources/" + QuillThemes.Folder + "/";
            foreach (var guid in AssetDatabase.FindAssets(QuillEngine.ThemeName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + QuillEngine.ThemeName + ".quill") && !path.EndsWith("/" + QuillEngine.ThemeName + ".ui")) continue;
                int i = path.IndexOf(marker, System.StringComparison.Ordinal);
                if (i < 0) continue;
                string rest = path.Substring(i + marker.Length);          // "<Name>/Theme.quill"
                int slash = rest.IndexOf('/');
                if (slash <= 0) continue;
                string name = rest.Substring(0, slash);
                if (!names.Contains(name)) names.Add(name);
            }
            names.Sort(1, names.Count - 1, System.StringComparer.Ordinal);
            return names;
        }

        [MenuItem("GameObject/Quill/Quill Document", false, 10)]
        private static void Create(MenuCommand command)
        {
            var go = new GameObject("Quill Document");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.AddComponent<QuillDocument>();
            Undo.RegisterCreatedObjectUndo(go, "Create Quill Document");
            Selection.activeObject = go;
        }
    }
}
