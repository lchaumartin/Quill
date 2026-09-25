// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Themes are folders of <c>.quill</c> files under <c>Resources/QuillThemes/&lt;Name&gt;</c>: one file per
    /// control (<c>Button.quill</c>, <c>Slider.quill</c>…) plus <c>Theme.quill</c>, the palette documents read as
    /// <c>Theme.accent</c>, <c>Theme.radius</c>… Every theme implements the same controls with the
    /// same properties and signals, so switching theme never means editing a document.
    ///
    /// <see cref="Default"/> (Slate) ships with the package; the others come as samples. A theme only
    /// needs to provide what it restyles: anything it leaves out falls back to Slate's version,
    /// drawn with the theme's palette.
    /// </summary>
    public static class QuillThemes
    {
        public const string Folder = "QuillThemes";
        public const string Default = "Slate";

        /// <summary>The gallery every theme is previewed in (shown by a Quill Document with no document).</summary>
        public const string GalleryPath = Folder + "/Gallery";

        /// <summary>
        /// Register a theme's controls and palette with <paramref name="engine"/> (the default theme
        /// first, then <paramref name="theme"/> on top). Call before <c>LoadFromSource</c>. Returns false
        /// (and applies the default) if no theme of that name is installed.
        /// </summary>
        public static bool Apply(QuillEngine engine, string theme)
        {
            foreach (var asset in Resources.LoadAll<TextAsset>(Folder + "/" + Default))
                Register(engine, asset);

            if (string.IsNullOrEmpty(theme) || theme == Default) return true;

            var assets = Resources.LoadAll<TextAsset>(Folder + "/" + theme);
            if (assets.Length == 0)
            {
                Debug.LogWarning($"[Quill] Theme '{theme}' not found (no Resources/{Folder}/{theme} folder) — using {Default}. "
                               + "Import it from the package's Samples in the Package Manager.");
                return false;
            }
            foreach (var asset in assets)
                Register(engine, asset);
            return true;
        }

        // Resources can't filter by extension, so a theme folder may also hold other text assets
        // (a readme, a licence). Only names that can be type names are components: letters, digits
        // and underscores, starting with an uppercase letter — `Button`, `FrostPane`, `Theme`.
        private static void Register(QuillEngine engine, TextAsset asset)
        {
            if (IsTypeName(asset.name)) engine.RegisterComponent(asset.name, asset.text);
        }

        public static bool IsTypeName(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }
    }
}
