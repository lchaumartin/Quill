// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// The no-code way to put a Quill UI on screen: assign a <c>.ui</c> document, pick a theme, press
    /// Play. It creates the <see cref="QuillEngine"/>, registers the theme's controls (and any extra
    /// components), loads the document and hands it to a <see cref="QuillSurface"/> on the same
    /// GameObject (added if missing). With no document assigned it shows the theme gallery — every
    /// control of the chosen theme — which is a quick way to preview a theme.
    ///
    /// From C#, use <see cref="Engine"/> after <c>Awake</c> to read values, connect signals or call
    /// functions (<c>doc.Engine.Connect("playBtn", "clicked", …)</c>), or <see cref="QuillBindings"/>
    /// to wire them in the Inspector.
    /// </summary>
    [AddComponentMenu("Quill/Quill Document")]
    [DisallowMultipleComponent]
    public sealed class QuillDocument : MonoBehaviour
    {
        [Tooltip("The .ui document to show. Leave empty to show the theme gallery.")]
        public TextAsset Document;

        [Tooltip("Theme folder under Resources/QuillThemes. Slate ships with Quill; import more from the package's Samples.")]
        public string Theme = QuillThemes.Default;

        [Tooltip("Extra reusable components (.ui files), registered by file name after the theme's — so a file named Button.ui replaces the theme's button.")]
        public TextAsset[] Components;

        [Tooltip("Start with the UI shown. Toggle it later with Surface.Visible (e.g. an in-game menu on Escape).")]
        public bool StartVisible = true;

        /// <summary>The engine running the document (null before Awake, or if loading failed).</summary>
        public QuillEngine Engine { get; private set; }

        /// <summary>The surface drawing it.</summary>
        public QuillSurface Surface { get; private set; }

        private void Awake()
        {
            Surface = GetComponent<QuillSurface>();
            if (Surface == null) Surface = gameObject.AddComponent<QuillSurface>();
            Load();
        }

        /// <summary>(Re)build the UI from <see cref="Document"/> and <see cref="Theme"/>. State in the document is reset.</summary>
        public void Load()
        {
            var doc = Document != null ? Document : Resources.Load<TextAsset>(QuillThemes.GalleryPath);
            if (doc == null)
            {
                Debug.LogError("[Quill] Quill Document: no document assigned, and the gallery was not found.", this);
                return;
            }

            var engine = new QuillEngine();
            QuillThemes.Apply(engine, Theme);
            if (Components != null)
                foreach (var asset in Components)
                    if (asset != null) engine.RegisterComponent(asset.name, asset.text);

            try { engine.LoadFromSource(doc.text); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Quill] '{doc.name}' failed to load: {e.Message}", this);
                return;
            }

            Engine = engine;
            Surface.Engine = engine;
            Surface.Visible = StartVisible;
        }

        /// <summary>Switch theme at runtime (rebuilds the document).</summary>
        public void SetTheme(string theme)
        {
            Theme = theme;
            if (Surface != null) Load();
        }
    }
}
