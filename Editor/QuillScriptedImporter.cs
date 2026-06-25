// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Quill.Editor
{
    /// <summary>
    /// Imports `.ui` files as <see cref="TextAsset"/>s so they can be referenced from the inspector
    /// and loaded at runtime via <c>Resources.Load</c>/asset references. As a convenience it also
    /// parses the file on import and surfaces any syntax error in the console.
    /// </summary>
    [ScriptedImporter(1, "ui")]
    public sealed class QuillScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);

            var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);

            // Compile-check so authors see errors at import time, not only at runtime. This must
            // never throw out of import: any failure is surfaced as a warning, nothing more.
            try
            {
                Parsing.Parser.Parse(text);
            }
            catch (System.Exception e)
            {
                ctx.LogImportWarning($"Quill parse error: {e.Message}");
            }
        }
    }
}
