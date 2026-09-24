// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Builds one combined glyph mesh for all Text elements using Unity's dynamic font atlas, drawn
    /// with <c>Quill/Text</c>. Each Text is content-sized: its measured width/height are written back
    /// to its properties so position anchors (e.g. centring) work off the real text extent.
    /// </summary>
    internal sealed class QuillTextLayer
    {
        private readonly Font _font;
        private readonly Material _material;
        private readonly Mesh _mesh;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color32> _cols = new List<Color32>();
        private readonly List<int> _tris = new List<int>();

        private static readonly int IdMainTex = Shader.PropertyToID("_MainTex");

        public QuillTextLayer(Transform parent, int renderQueue)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);

            var shader = Shader.Find("Quill/Text");
            if (shader == null)
            {
                Debug.LogError("[Quill] Shader 'Quill/Text' not found.");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = renderQueue };

            QuillSurface.MakeLayer(parent, "Quill Text", out var filter, out var renderer);
            _mesh = new Mesh { name = "Quill Text Mesh" };
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;
            renderer.sharedMaterial = _material;
        }

        public void Render(List<QuillText> texts, float w, float h)
        {
            if (_font == null || _material == null || _mesh == null) return;

            _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();

            // Ensure every glyph we need is in the atlas before reading any metrics.
            for (int i = 0; i < texts.Count; i++)
            {
                string s = QuillConvert.ToStr(texts[i].FindProperty("text")?.Raw);
                if (string.IsNullOrEmpty(s)) continue;
                int size = Mathf.Max(1, Mathf.RoundToInt(texts[i].Num("fontSize", 16f)));
                _font.RequestCharactersInTexture(s, size, FontStyle.Normal);
            }

            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                string s = QuillConvert.ToStr(t.FindProperty("text")?.Raw);
                int size = Mathf.Max(1, Mathf.RoundToInt(t.Num("fontSize", 16f)));

                Color col = QuillConvert.ToColor(t.FindProperty("color")?.Raw);
                float op = Mathf.Clamp01(t.EffectiveOpacity());
                Color32 c32 = new Color(col.r, col.g, col.b, col.a * op);

                float penX = t.AbsX();
                float startX = penX;
                float ascent = size * 0.8f;            // approximation; good enough for layout
                float baseline = t.AbsY() + ascent;

                if (!string.IsNullOrEmpty(s))
                {
                    for (int ci = 0; ci < s.Length; ci++)
                    {
                        if (!_font.GetCharacterInfo(s[ci], out var info, size, FontStyle.Normal))
                            continue;

                        float left = penX + info.minX;
                        float right = penX + info.maxX;
                        float top = baseline - info.maxY;     // maxY is up from baseline
                        float bottom = baseline - info.minY;

                        int b = _verts.Count;
                        _verts.Add(QuillSurface.ToClip(left, top, w, h));      // TL
                        _verts.Add(QuillSurface.ToClip(right, top, w, h));     // TR
                        _verts.Add(QuillSurface.ToClip(right, bottom, w, h));  // BR
                        _verts.Add(QuillSurface.ToClip(left, bottom, w, h));   // BL

                        _uvs.Add(info.uvTopLeft);
                        _uvs.Add(info.uvTopRight);
                        _uvs.Add(info.uvBottomRight);
                        _uvs.Add(info.uvBottomLeft);

                        _cols.Add(c32); _cols.Add(c32); _cols.Add(c32); _cols.Add(c32);

                        _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
                        _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);

                        penX += info.advance;
                    }
                }

                // Content size -> feed anchors (1-frame latency is fine).
                t.Property("width").SetValue((double)(penX - startX));
                t.Property("height").SetValue((double)size);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);

            _material.SetTexture(IdMainTex, _font.material.mainTexture);
        }

        public void Dispose()
        {
            if (_material != null) Object.Destroy(_material);
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }
}
