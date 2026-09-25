// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Builds the glyph meshes for all Text elements using Unity's dynamic font atlases, drawn with
    /// <c>Quill/Text</c>: one combined mesh per font in use (<c>font.family</c>). Lines come from
    /// <see cref="TextLayout"/> (line breaks, wrapping, eliding) and are aligned inside the item's box.
    /// The measured size is written back to <c>contentWidth</c> / <c>contentHeight</c> /
    /// <c>lineCount</c>; a Text the document doesn't size takes that as its width/height (see
    /// QuillEngine.SetupText), so anchors see the real extent.
    /// </summary>
    internal sealed class QuillTextLayer
    {
        // One mesh + material + renderer per font.
        private sealed class Batch
        {
            public Font Font;
            public Material Material;
            public Mesh Mesh;
            public GameObject Go;
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color32> Cols = new List<Color32>();
            public readonly List<int> Tris = new List<int>();
        }

        private readonly Transform _parent;
        private readonly int _renderQueue;
        private readonly Shader _shader;
        private readonly Dictionary<Font, Batch> _batches = new Dictionary<Font, Batch>();
        private readonly List<(QuillText text, Font font)> _items = new List<(QuillText, Font)>();

        private static readonly int IdMainTex = Shader.PropertyToID("_MainTex");

        public QuillTextLayer(Transform parent, int renderQueue)
        {
            _parent = parent;
            _renderQueue = renderQueue;
            _shader = Shader.Find("Quill/Text");
            if (_shader == null) Debug.LogError("[Quill] Shader 'Quill/Text' not found.");
        }

        private static FontStyle StyleOf(QuillText t)
        {
            bool bold = t.Flag("font.bold", false), italic = t.Flag("font.italic", false);
            return bold && italic ? FontStyle.BoldAndItalic : bold ? FontStyle.Bold : italic ? FontStyle.Italic : FontStyle.Normal;
        }

        // The displayed string: `text` with `font.capitalization` applied.
        private static string TextOf(QuillText t)
        {
            string s = QuillConvert.ToStr(t.FindProperty("text")?.Raw);
            if (string.IsNullOrEmpty(s)) return s;
            switch ((int)t.Num("font.capitalization", 0f))
            {
                case 1: case 3: return s.ToUpperInvariant();   // AllUppercase; SmallCaps approximated
                case 2: return s.ToLowerInvariant();
                case 4:
                {
                    var chars = s.ToCharArray();
                    for (int i = 0; i < chars.Length; i++)
                        if (i == 0 || char.IsWhiteSpace(chars[i - 1])) chars[i] = char.ToUpperInvariant(chars[i]);
                    return new string(chars);
                }
                default: return s;
            }
        }

        private Batch BatchFor(Font font)
        {
            if (_batches.TryGetValue(font, out var b)) return b;
            b = new Batch { Font = font };
            b.Material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = _renderQueue };
            b.Go = QuillSurface.MakeLayer(_parent, "Quill Text (" + font.name + ")", out var filter, out var renderer);
            b.Mesh = new Mesh { name = "Quill Text Mesh" };
            b.Mesh.MarkDynamic();
            filter.sharedMesh = b.Mesh;
            renderer.sharedMaterial = b.Material;
            _batches[font] = b;
            return b;
        }

        public void Render(List<QuillText> texts, float w, float h)
        {
            if (_shader == null) return;

            foreach (var b in _batches.Values)
            {
                b.Verts.Clear(); b.Uvs.Clear(); b.Cols.Clear(); b.Tris.Clear();
            }

            // Resolve fonts, and make sure every glyph we need is in its atlas before reading any
            // metrics (requesting can rebuild an atlas and invalidate earlier UVs, so it all happens
            // up front).
            _items.Clear();
            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                var font = QuillFonts.Get(QuillConvert.ToStr(t.FindProperty("font.family")?.Raw));
                if (font == null) continue;
                _items.Add((t, font));
                string s = TextOf(t);
                if (string.IsNullOrEmpty(s)) continue;
                int size = Mathf.Max(1, Mathf.RoundToInt(t.Num("fontSize", 16f)));
                font.RequestCharactersInTexture(s + "…", size, StyleOf(t));
            }

            for (int i = 0; i < _items.Count; i++)
            {
                var (t, font) = _items[i];
                string s = TextOf(t);
                int size = Mathf.Max(1, Mathf.RoundToInt(t.Num("fontSize", 16f)));
                var style = StyleOf(t);
                float spacing = t.Num("font.letterSpacing", 0f);

                Color col = QuillConvert.ToColor(t.FindProperty("color")?.Raw);
                float op = Mathf.Clamp01(t.EffectiveOpacity());
                Color32 c32 = new Color(col.r, col.g, col.b, col.a * op);

                float boxW = t.Num("width"), boxH = t.Num("height");
                float maxW = t.HasExplicitWidth ? boxW : float.PositiveInfinity;
                var layout = TextLayout.Layout(s, maxW, (int)t.Num("wrapMode"), (int)t.Num("elide"),
                    ch => (font.GetCharacterInfo(ch, out var info, size, style) ? info.advance : 0f) + spacing);

                float lineH = size * Mathf.Max(0.1f, t.Num("lineHeight", 1f));
                float contentH = lineH * layout.Lines.Count;

                // Content size -> implicit width/height and anchors (1-frame latency is fine). Letter
                // spacing goes between characters, so the last one's is not part of the extent.
                t.Property("contentWidth").SetValue((double)Mathf.Max(0f, layout.Width - (layout.Width > 0 ? spacing : 0f)));
                t.Property("contentHeight").SetValue((double)contentH);
                t.Property("lineCount").SetValue((double)layout.Lines.Count);

                if (string.IsNullOrEmpty(s)) continue;
                var batch = BatchFor(font);

                int hAlign = (int)t.Num("horizontalAlignment", 1f);
                int vAlign = (int)t.Num("verticalAlignment", 32f);
                float x0 = t.AbsX(), y0 = t.AbsY();
                if (t.HasExplicitHeight)
                {
                    if ((vAlign & 64) != 0) y0 += boxH - contentH;              // AlignBottom
                    else if ((vAlign & 128) != 0) y0 += (boxH - contentH) * 0.5f; // AlignVCenter
                }

                float ascent = size * 0.8f;            // approximation; good enough for layout
                for (int li = 0; li < layout.Lines.Count; li++)
                {
                    var line = layout.Lines[li];
                    float lineW = line.Width - (line.Text.Length > 0 ? spacing : 0f);
                    float penX = x0;
                    if (t.HasExplicitWidth)
                    {
                        if ((hAlign & 2) != 0) penX += boxW - lineW;               // AlignRight
                        else if ((hAlign & 4) != 0) penX += (boxW - lineW) * 0.5f; // AlignHCenter
                    }
                    float baseline = y0 + li * lineH + (lineH - size) * 0.5f + ascent;

                    string ls = line.Text;
                    for (int ci = 0; ci < ls.Length; ci++)
                    {
                        if (!font.GetCharacterInfo(ls[ci], out var info, size, style))
                            continue;

                        float left = penX + info.minX;
                        float right = penX + info.maxX;
                        float top = baseline - info.maxY;     // maxY is up from baseline
                        float bottom = baseline - info.minY;

                        var verts = batch.Verts;
                        int b = verts.Count;
                        verts.Add(QuillSurface.ToClip(left, top, w, h));      // TL
                        verts.Add(QuillSurface.ToClip(right, top, w, h));     // TR
                        verts.Add(QuillSurface.ToClip(right, bottom, w, h));  // BR
                        verts.Add(QuillSurface.ToClip(left, bottom, w, h));   // BL

                        batch.Uvs.Add(info.uvTopLeft);
                        batch.Uvs.Add(info.uvTopRight);
                        batch.Uvs.Add(info.uvBottomRight);
                        batch.Uvs.Add(info.uvBottomLeft);

                        batch.Cols.Add(c32); batch.Cols.Add(c32); batch.Cols.Add(c32); batch.Cols.Add(c32);

                        batch.Tris.Add(b); batch.Tris.Add(b + 1); batch.Tris.Add(b + 2);
                        batch.Tris.Add(b); batch.Tris.Add(b + 2); batch.Tris.Add(b + 3);

                        penX += info.advance + spacing;
                    }
                }
            }

            foreach (var b in _batches.Values)
            {
                b.Mesh.Clear();
                b.Mesh.SetVertices(b.Verts);
                b.Mesh.SetUVs(0, b.Uvs);
                b.Mesh.SetColors(b.Cols);
                b.Mesh.SetTriangles(b.Tris, 0);
                b.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
                b.Material.SetTexture(IdMainTex, b.Font.material.mainTexture);
            }
        }

        public void Dispose()
        {
            foreach (var b in _batches.Values)
            {
                if (b.Material != null) Object.Destroy(b.Material);
                if (b.Mesh != null) Object.Destroy(b.Mesh);
            }
            _batches.Clear();
        }
    }

    /// <summary>
    /// Resolves a Text's <c>font.family</c> to a Unity <see cref="Font"/>, cached by name:
    ///   ""                     → Unity's built-in font (LegacyRuntime)
    ///   "Fonts/MyFont"         → a font asset under any Resources folder, if one exists at that path
    ///   "Georgia, serif"       → installed fonts, first found wins (the rest are fallbacks)
    /// Installed fonts vary by platform, so themes list a few names and ship-ready projects put a
    /// font in Resources and name that instead.
    /// </summary>
    public static class QuillFonts
    {
        private static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();
        private static Font _default;

        public static Font Default
        {
            get
            {
                if (_default == null)
                {
                    _default = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_default == null) _default = Font.CreateDynamicFontFromOSFont("Arial", 16);
                }
                return _default;
            }
        }

        public static Font Get(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return Default;
            if (_cache.TryGetValue(family, out var cached) && cached != null) return cached;

            Font font = Resources.Load<Font>(family.Trim());
            if (font == null)
            {
                var names = family.Split(',');
                for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
                font = Font.CreateDynamicFontFromOSFont(names, 16);
                if (font != null) font.hideFlags = HideFlags.DontSave;
            }
            if (font == null) font = Default;
            _cache[family] = font;
            return font;
        }
    }
}
