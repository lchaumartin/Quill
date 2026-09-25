// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Quill.Editor
{
    /// <summary>
    /// Makes .quill files first-class in the external code editor (Rider, Visual Studio, VS Code…):
    ///   • double-clicking one in the Project window (or a Quill error in the Console) opens it there,
    ///     like a script, instead of the OS default app;
    ///   • "quill" is added to Project Settings ▸ Editor ▸ Additional extensions to include, so the
    ///     generated solution lists .quill files and the IDE searches them.
    /// The IDE-side language support (highlighting, completion, diagnostics) is the Quill extension for
    /// each editor — see the package's Tooling~ folder.
    /// </summary>
    [InitializeOnLoad]
    internal static class QuillCodeEditor
    {
        private static readonly string[] Extensions = { ".quill", ".ui" };

        static QuillCodeEditor()
        {
            // Deferred: settings aren't safe to touch during domain reload itself.
            EditorApplication.delayCall += IncludeInGeneratedProjects;
        }

        private static void IncludeInGeneratedProjects()
        {
            var exts = EditorSettings.projectGenerationUserExtensions ?? new string[0];
            if (exts.Any(e => string.Equals(e?.Trim().TrimStart('.'), "quill", StringComparison.OrdinalIgnoreCase))) return;
            EditorSettings.projectGenerationUserExtensions = exts.Concat(new[] { "quill" }).ToArray();
        }

        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618   // int instance ids are obsolete from Unity 6.3 (EntityId) but work everywhere
            string path = AssetDatabase.GetAssetPath(instanceId);
#pragma warning restore CS0618
            if (string.IsNullOrEmpty(path) || !Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                return false;

            var editor = CodeEditor.CurrentEditor;
            if (editor == null) return false;   // no external editor set: let Unity open it
            return editor.OpenProject(Path.GetFullPath(path), Math.Max(1, line), 1);
        }
    }
}
