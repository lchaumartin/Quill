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
        private readonly List<(QuillText text, Font font, TextCache cache)> _items = new List<(QuillText, Font, TextCache)>();
        private readonly GlyphAdvance _advance = new GlyphAdvance();

        /// <summary>
        /// What a Text displayed last frame and how it was laid out. Reused frame to frame until the
        /// text, its font settings or its box change, so a steady label costs no layout and no
        /// allocations. Kept on the element (<see cref="QuillText.RenderCache"/>).
        /// </summary>
        private sealed class TextCache
        {
            public object Raw;            // the `text` value it was built from
            public int Caps = -1;
            public string Display;
            public Font Font;
            public int Px;
            public FontStyle Style;
            public float Spacing, MaxW, Inv;
            public int Wrap, Elide;
            public bool LayoutValid;
            public int RequestedAt = -1;  // s_AtlasRebuilds when its glyphs were last requested
            public readonly TextLayout.Result Layout = new TextLayout.Result();
        }

        /// <summary>The advance function handed to TextLayout: one instance and one delegate, reused.</summary>
        private sealed class GlyphAdvance
        {
            public Font Font;
            public int Px;
            public FontStyle Style;
            public float Inv, Spacing;
            public readonly System.Func<char, float> Fn;
            public GlyphAdvance() { Fn = Measure; }
            private float Measure(char ch)
                => (Font.GetCharacterInfo(ch, out var info, Px, Style) ? info.advance * Inv : 0f) + Spacing;
        }

        // Dynamic font atlases can be rebuilt (and lose glyphs) when new characters are requested;
        // glyphs only need requesting again after a rebuild or when a label changes.
        private static int s_AtlasRebuilds;
        static QuillTextLayer() { Font.textureRebuilt += _ => s_AtlasRebuilds++; }

        private static readonly int IdMainTex = Shader.PropertyToID("_MainTex");

        public QuillTextLayer(Transform parent, int renderQueue)
        {
            _parent = parent;
            _renderQueue = renderQueue;
            _shader = Shader.Find("Quill/Text");
            if (_shader == null) Debug.LogError("[Quill] Shader 'Quill/Text' not found.");
        }

        private static FontStyle StyleOf(QuillItem t)
        {
            bool bold = t.Flag("font.bold", false), italic = t.Flag("font.italic", false);
            return bold && italic ? FontStyle.BoldAndItalic : bold ? FontStyle.Bold : italic ? FontStyle.Italic : FontStyle.Normal;
        }

        // The displayed string: `text` with `font.capitalization` applied.
        private static string ApplyCaps(string s, int caps)
        {
            if (string.IsNullOrEmpty(s)) return s;
            switch (caps)
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

        /// <param name="w">Surface width in Quill pixels.</param>
        /// <param name="h">Surface height in Quill pixels.</param>
        /// <param name="scale">Screen pixels per Quill pixel. Glyphs are rasterised at
        /// <c>fontSize * scale</c> so text stays sharp; layout and metrics stay in Quill pixels.</param>
        public void Render(List<QuillText> texts, float w, float h, float scale)
            => Render(texts, null, w, h, scale);

        /// <param name="inputs">TextInputs, already laid out by <see cref="LayoutInputs"/> this frame.</param>
        public void Render(List<QuillText> texts, List<QuillTextInput> inputs, float w, float h, float scale)
        {
            if (scale <= 0f) scale = 1f;
            float inv = 1f / scale;
            if (_shader == null) return;

            foreach (var b in _batches.Values)
            {
                b.Verts.Clear(); b.Uvs.Clear(); b.Cols.Clear(); b.Tris.Clear();
            }

            // Resolve fonts and displayed strings, and make sure every glyph we need is in its atlas
            // before reading any metrics (requesting can rebuild an atlas and invalidate earlier UVs,
            // so it all happens up front). Unchanged labels skip the request unless an atlas was
            // rebuilt since; a rebuild during this pass re-runs it so no label is left without glyphs.
            _items.Clear();
            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                var font = QuillFonts.Get(QuillConvert.ToStr(t.FindProperty("font.family")?.Raw));
                if (font == null) continue;
                var cache = t.RenderCache as TextCache;
                if (cache == null) t.RenderCache = cache = new TextCache();

                object raw = t.FindProperty("text")?.Raw;
                int caps = (int)t.Num("font.capitalization", 0f);
                if (caps != cache.Caps || !Equals(raw, cache.Raw))
                {
                    cache.Raw = raw;
                    cache.Caps = caps;
                    cache.Display = ApplyCaps(QuillConvert.ToStr(raw), caps);
                    cache.LayoutValid = false;
                    cache.RequestedAt = -1;
                }
                int px = Mathf.Max(1, Mathf.RoundToInt(t.Num("fontSize", 16f) * scale));
                var style = StyleOf(t);
                if (font != cache.Font || px != cache.Px || style != cache.Style)
                {
                    cache.Font = font;
                    cache.Px = px;
                    cache.Style = style;
                    cache.LayoutValid = false;
                    cache.RequestedAt = -1;
                }
                _items.Add((t, font, cache));
            }
            for (int pass = 0; pass < 3; pass++)
            {
                int stamp = s_AtlasRebuilds;
                if (inputs != null)   // few, and edited often: requested every pass
                    for (int i = 0; i < inputs.Count; i++)
                        if (inputs[i].RenderCache is InputLayout il && !string.IsNullOrEmpty(il.Display))
                            il.Font.RequestCharactersInTexture(il.Display, il.Px, il.Style);
                for (int i = 0; i < _items.Count; i++)
                {
                    var c = _items[i].cache;
                    if (c.RequestedAt == s_AtlasRebuilds || string.IsNullOrEmpty(c.Display)) continue;
                    c.Font.RequestCharactersInTexture(c.Display, c.Px, c.Style);
                    c.Font.RequestCharactersInTexture("…", c.Px, c.Style);   // for eliding
                    c.RequestedAt = s_AtlasRebuilds;
                }
                if (s_AtlasRebuilds == stamp) break;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                var (t, font, cache) = _items[i];
                string s = cache.Display;
                // `px` is the rasterised size in screen pixels; `size` is the same in Quill pixels.
                int px = cache.Px;
                float size = px * inv;
                var style = cache.Style;
                float spacing = t.Num("font.letterSpacing", 0f);

                Color col = QuillConvert.ToColor(t.FindProperty("color")?.Raw);
                float op = Mathf.Clamp01(t.EffectiveOpacity());
                Color32 c32 = new Color(col.r, col.g, col.b, col.a * op);

                float boxW = t.Num("width"), boxH = t.Num("height");
                float maxW = t.HasExplicitWidth ? boxW : float.PositiveInfinity;
                int wrap = (int)t.Num("wrapMode"), elide = (int)t.Num("elide");
                if (!cache.LayoutValid || maxW != cache.MaxW || wrap != cache.Wrap || elide != cache.Elide
                    || spacing != cache.Spacing || inv != cache.Inv)
                {
                    _advance.Font = font; _advance.Px = px; _advance.Style = style;
                    _advance.Inv = inv; _advance.Spacing = spacing;
                    TextLayout.Layout(s, maxW, wrap, elide, _advance.Fn, cache.Layout);
                    cache.MaxW = maxW; cache.Wrap = wrap; cache.Elide = elide;
                    cache.Spacing = spacing; cache.Inv = inv;
                    cache.LayoutValid = true;
                }
                var layout = cache.Layout;

                float lineH = size * Mathf.Max(0.1f, t.Num("lineHeight", 1f));
                float contentH = lineH * layout.Lines.Count;

                // Content size -> implicit width/height and anchors (1-frame latency is fine). Letter
                // spacing goes between characters, so the last one's is not part of the extent.
                t.Property("contentWidth").SetNumber(Mathf.Max(0f, layout.Width - (layout.Width > 0 ? spacing : 0f)));
                t.Property("contentHeight").SetNumber(contentH);
                t.Property("lineCount").SetNumber(layout.Lines.Count);

                if (string.IsNullOrEmpty(s)) continue;

                // Clipped entirely away (scrolled out of a Flickable): measured above, nothing to draw.
                bool clipped = t.Clipped;
                float cl = t.ClipL, ct = t.ClipT, cr = t.ClipR, cb = t.ClipB;
                if (clipped && (cr <= cl || cb <= ct)) continue;
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
                    // Lines wholly above or below the clip rectangle are skipped.
                    if (clipped)
                    {
                        float lineTop = y0 + li * lineH;
                        if (lineTop + lineH * 2f < ct) continue;
                        if (lineTop - lineH > cb) break;
                    }
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
                        if (!font.GetCharacterInfo(ls[ci], out var info, px, style))
                            continue;

                        // Glyph metrics are in screen pixels; convert to Quill pixels.
                        float left = penX + info.minX * inv;
                        float right = penX + info.maxX * inv;
                        float top = baseline - info.maxY * inv;     // maxY is up from baseline
                        float bottom = baseline - info.minY * inv;
                        if (clipped) EmitGlyph(batch, ref info, left, top, right, bottom, c32, w, h, cl, ct, cr, cb);
                        else EmitGlyph(batch, ref info, left, top, right, bottom, c32, w, h);

                        penX += info.advance * inv + spacing;
                    }
                }
            }

            if (inputs != null)
                for (int i = 0; i < inputs.Count; i++) EmitInput(inputs[i], w, h, inv);

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

        // ---- TextInput ---------------------------------------------------------------------------

        /// <summary>Per-TextInput layout from <see cref="LayoutInputs"/>, used again by <see cref="Render"/>.</summary>
        private sealed class InputLayout
        {
            public Font Font;
            public int Px;
            public FontStyle Style;
            public string Display;
            public float Size, Inv;
        }

        /// <summary>
        /// Lays out every TextInput: character positions, horizontal scroll to keep the caret in view,
        /// alignment, and the caret and selection rectangles. Runs before the rectangle layer renders,
        /// so those rectangles are placed this frame. <paramref name="time"/> is the engine time (caret blink).
        /// </summary>
        public void LayoutInputs(List<QuillTextInput> inputs, float scale, double time)
        {
            if (inputs == null || inputs.Count == 0) return;
            if (scale <= 0f) scale = 1f;
            float inv = 1f / scale;

            for (int k = 0; k < inputs.Count; k++)
            {
                var t = inputs[k];
                var font = QuillFonts.Get(QuillConvert.ToStr(t.FindProperty("font.family")?.Raw));
                if (font == null) continue;
                var lay = t.RenderCache as InputLayout;
                if (lay == null) t.RenderCache = lay = new InputLayout();

                string s = QuillConvert.ToStr(t.FindProperty("displayText")?.Raw);
                int px = Mathf.Max(1, Mathf.RoundToInt(t.Num("fontSize", 16f) * scale));
                var style = StyleOf(t);
                float spacing = t.Num("font.letterSpacing", 0f);
                lay.Font = font; lay.Px = px; lay.Style = style; lay.Display = s;
                lay.Inv = inv; lay.Size = px * inv;
                if (!string.IsNullOrEmpty(s)) font.RequestCharactersInTexture(s, px, style);

                // Character boundaries, from the start of the text.
                int n = s.Length;
                if (t.CharX == null || t.CharX.Length != n + 1) t.CharX = new float[n + 1];
                float x = 0f;
                for (int i = 0; i < n; i++)
                {
                    t.CharX[i] = x;
                    x += (font.GetCharacterInfo(s[i], out var info, px, style) ? info.advance * inv : 0f) + spacing;
                }
                float content = n > 0 ? x - spacing : 0f;
                t.CharX[n] = content;

                float lineH = lay.Size;
                t.LineHeight = lineH;
                t.Property("contentWidth").SetNumber(content);
                t.Property("contentHeight").SetNumber(lineH);

                float boxW = t.Num("width"), boxH = t.Num("height");
                float caretW = Mathf.Max(1f, Mathf.Round(scale)) * inv;
                int cursor = Mathf.Clamp(t.CursorPosition, 0, n);

                // Horizontal placement: aligned when it fits, else scrolled to keep the caret in view.
                if (!t.HasExplicitWidth || content + caretW <= boxW)
                {
                    t.ScrollX = 0f;
                    int hAlign = (int)t.Num("horizontalAlignment", 1f);
                    t.TextX = !t.HasExplicitWidth ? 0f
                        : (hAlign & 4) != 0 ? Mathf.Round((boxW - content) * 0.5f / inv) * inv   // AlignHCenter
                        : (hAlign & 2) != 0 ? boxW - content - caretW                           // AlignRight
                        : 0f;
                }
                else
                {
                    t.TextX = 0f;
                    float cx = t.CharX[cursor];
                    if (cx - t.ScrollX > boxW - caretW) t.ScrollX = cx - boxW + caretW;
                    if (cx - t.ScrollX < 0f) t.ScrollX = cx;
                    t.ScrollX = Mathf.Clamp(t.ScrollX, 0f, Mathf.Max(0f, content - boxW + caretW));
                }

                int vAlign = (int)t.Num("verticalAlignment", 32f);
                t.TextY = (vAlign & 128) != 0 ? Mathf.Round((boxH - lineH) * 0.5f / inv) * inv   // AlignVCenter
                        : (vAlign & 64) != 0 ? boxH - lineH                                     // AlignBottom
                        : 0f;

                bool focused = t.Flag("activeFocus", false);
                int s0 = Mathf.Clamp((int)t.Num("selectionStart"), 0, n), s1 = Mathf.Clamp((int)t.Num("selectionEnd"), 0, n);

                // Selection highlight.
                var sel = t.SelectionRect;
                if (sel != null)
                {
                    float x0 = 0f, x1 = 0f;
                    if (focused && s1 > s0)
                    {
                        x0 = Mathf.Clamp(t.TextX + t.CharX[s0] - t.ScrollX, 0f, boxW);
                        x1 = Mathf.Clamp(t.TextX + t.CharX[s1] - t.ScrollX, 0f, boxW);
                    }
                    Place(sel, x0, t.TextY, x1 - x0, lineH, t.FindProperty("selectionColor")?.Raw);
                }

                // Caret: solid for half a second after an edit or move, then blinking.
                var caret = t.CaretRect;
                if (caret != null)
                {
                    bool on = focused && !t.ReadOnly && ((time - t.LastEditTime) % 1.0) < 0.5;
                    float cx = Mathf.Clamp(t.TextX + t.CharX[cursor] - t.ScrollX, 0f, Mathf.Max(0f, boxW - caretW));
                    Place(caret, cx, t.TextY, on ? caretW : 0f, lineH, t.FindProperty("color")?.Raw);
                }
            }
        }

        private static void Place(QuillRectangle r, float x, float y, float w, float h, object color)
        {
            r.Property("x").SetNumber(x);
            r.Property("y").SetNumber(y);
            r.Property("width").SetNumber(w);
            r.Property("height").SetNumber(h);
            var c = r.Property("color");
            if (!Equals(c.Raw, color)) c.SetValue(color);
        }

        // Glyphs of one TextInput, cut to its width (and any clipping ancestor), the selected ones in
        // selectedTextColor. Characters scrolled past either edge show partly, as in any text field.
        private void EmitInput(QuillTextInput t, float w, float h, float inv)
        {
            if (!(t.RenderCache is InputLayout lay) || string.IsNullOrEmpty(lay.Display) || t.CharX == null) return;
            string s = lay.Display;
            if (t.CharX.Length != s.Length + 1) return;
            var batch = BatchFor(lay.Font);

            float op = Mathf.Clamp01(t.EffectiveOpacity());
            Color col = QuillConvert.ToColor(t.FindProperty("color")?.Raw);
            Color scol = QuillConvert.ToColor(t.FindProperty("selectedTextColor")?.Raw);
            Color32 c32 = new Color(col.r, col.g, col.b, col.a * op);
            Color32 s32 = new Color(scol.r, scol.g, scol.b, scol.a * op);
            bool focused = t.Flag("activeFocus", false);
            int s0 = (int)t.Num("selectionStart"), s1 = (int)t.Num("selectionEnd");

            // The input's own box horizontally (accents and descenders may overhang a tight height),
            // cut by its clipping ancestors.
            float ax = t.AbsX(), ay = t.AbsY();
            float cl = ax, ct = -1e7f, cr = ax + t.Num("width"), cb = 1e7f;
            if (t.Clipped)
            {
                cl = Mathf.Max(cl, t.ClipL); ct = Mathf.Max(ct, t.ClipT);
                cr = Mathf.Min(cr, t.ClipR); cb = Mathf.Min(cb, t.ClipB);
            }
            if (cr <= cl || cb <= ct) return;

            float x0 = ax + t.TextX - t.ScrollX, top = ay + t.TextY;
            float ascent = lay.Size * 0.8f;
            float baseline = top + ascent;

            for (int i = 0; i < s.Length; i++)
            {
                float penX = x0 + t.CharX[i];
                if (penX > cr) break;
                if (x0 + t.CharX[i + 1] < cl - lay.Size) continue;
                if (!lay.Font.GetCharacterInfo(s[i], out var info, lay.Px, lay.Style)) continue;

                float gl = penX + info.minX * lay.Inv, gr = penX + info.maxX * lay.Inv;
                float gt = baseline - info.maxY * lay.Inv, gb = baseline - info.minY * lay.Inv;
                var cc = focused && i >= s0 && i < s1 ? s32 : c32;
                EmitGlyph(batch, ref info, gl, gt, gr, gb, cc, w, h, cl, ct, cr, cb);
            }
        }

        // One glyph quad, unclipped.
        private static void EmitGlyph(Batch batch, ref CharacterInfo info, float left, float top, float right, float bottom,
                                      Color32 c, float w, float h)
        {
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

            batch.Cols.Add(c); batch.Cols.Add(c); batch.Cols.Add(c); batch.Cols.Add(c);

            batch.Tris.Add(b); batch.Tris.Add(b + 1); batch.Tris.Add(b + 2);
            batch.Tris.Add(b); batch.Tris.Add(b + 2); batch.Tris.Add(b + 3);
        }

        // One glyph quad cut to the clip rectangle (cl, ct)–(cr, cb). The atlas coordinates are
        // interpolated across the quad (glyphs may be stored flipped, so all four corners are used).
        private static void EmitGlyph(Batch batch, ref CharacterInfo info, float left, float top, float right, float bottom,
                                      Color32 c, float w, float h, float cl, float ct, float cr, float cb)
        {
            if (left >= cl && right <= cr && top >= ct && bottom <= cb)
            {
                EmitGlyph(batch, ref info, left, top, right, bottom, c, w, h);
                return;
            }
            float x0 = Mathf.Max(left, cl), x1 = Mathf.Min(right, cr);
            float y0 = Mathf.Max(top, ct), y1 = Mathf.Min(bottom, cb);
            if (x1 <= x0 || y1 <= y0) return;

            float gw = right - left, gh = bottom - top;
            float s0 = gw > 0f ? (x0 - left) / gw : 0f, s1 = gw > 0f ? (x1 - left) / gw : 1f;
            float t0 = gh > 0f ? (y0 - top) / gh : 0f, t1 = gh > 0f ? (y1 - top) / gh : 1f;
            Vector2 tl = info.uvTopLeft, tr = info.uvTopRight, br = info.uvBottomRight, bl = info.uvBottomLeft;
            Vector2 Uv(float u, float v) => Vector2.Lerp(Vector2.Lerp(tl, tr, u), Vector2.Lerp(bl, br, u), v);

            var verts = batch.Verts;
            int b = verts.Count;
            verts.Add(QuillSurface.ToClip(x0, y0, w, h));
            verts.Add(QuillSurface.ToClip(x1, y0, w, h));
            verts.Add(QuillSurface.ToClip(x1, y1, w, h));
            verts.Add(QuillSurface.ToClip(x0, y1, w, h));

            batch.Uvs.Add(Uv(s0, t0));
            batch.Uvs.Add(Uv(s1, t0));
            batch.Uvs.Add(Uv(s1, t1));
            batch.Uvs.Add(Uv(s0, t1));

            batch.Cols.Add(c); batch.Cols.Add(c); batch.Cols.Add(c); batch.Cols.Add(c);

            batch.Tris.Add(b); batch.Tris.Add(b + 1); batch.Tris.Add(b + 2);
            batch.Tris.Add(b); batch.Tris.Add(b + 2); batch.Tris.Add(b + 3);
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
