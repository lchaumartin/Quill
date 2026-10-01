// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Draws each Image element as its own textured clip-space quad. Renderers are pooled and reused
    /// across frames; textures are loaded from <c>Resources</c> by the element's <c>source</c> path
    /// and cached.
    /// </summary>
    internal sealed class QuillImageLayer
    {
        private readonly Transform _parent;
        private readonly int _renderQueue;
        private readonly Shader _shader;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

        private static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int IdColor = Shader.PropertyToID("_Color");
        private static readonly int IdOpacity = Shader.PropertyToID("_Opacity");

        private readonly Vector3[] _v = new Vector3[4];
        private readonly Vector2[] _uv = new Vector2[4];
        private static readonly int[] Tris = { 0, 1, 2, 0, 2, 3 };

        public QuillImageLayer(Transform parent, int renderQueue)
        {
            _parent = parent;
            _renderQueue = renderQueue;
            _shader = Shader.Find("Quill/Image");
            if (_shader == null) Debug.LogError("[Quill] Shader 'Quill/Image' not found.");
        }

        public void Render(List<QuillImage> images, float w, float h)
        {
            if (_shader == null) return;

            int i = 0;
            for (int n = 0; n < images.Count; n++)
            {
                var img = images[n];

                float x = img.AbsX(), y = img.AbsY();
                float iw = img.Num("width"), ih = img.Num("height");

                var tex = LoadTexture(QuillConvert.ToStr(img.FindProperty("source")?.Raw));
                if (iw <= 0f && tex != null) iw = tex.width;
                if (ih <= 0f && tex != null) ih = tex.height;

                // Cut to the clip rectangle (texture coordinates follow); skip if nothing shows.
                float x0 = x, y0 = y, x1 = x + iw, y1 = y + ih;
                if (!QuillSurface.ClipQuad(img, ref x0, ref y0, ref x1, ref y1, out float s0, out float t0, out float s1, out float t1))
                    continue;
                EnsurePool(i);

                // Quad corners: TL, TR, BR, BL (texture v runs bottom to top).
                _v[0] = QuillSurface.ToClip(x0, y0, w, h);
                _v[1] = QuillSurface.ToClip(x1, y0, w, h);
                _v[2] = QuillSurface.ToClip(x1, y1, w, h);
                _v[3] = QuillSurface.ToClip(x0, y1, w, h);
                _uv[0] = new Vector2(s0, 1f - t0);
                _uv[1] = new Vector2(s1, 1f - t0);
                _uv[2] = new Vector2(s1, 1f - t1);
                _uv[3] = new Vector2(s0, 1f - t1);

                var mesh = _meshes[i];
                mesh.Clear();
                mesh.vertices = _v;
                mesh.uv = _uv;
                mesh.triangles = Tris;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);

                var mat = _materials[i];
                mat.SetTexture(IdMainTex, tex != null ? tex : Texture2D.whiteTexture);
                mat.SetColor(IdColor, QuillConvert.ToColor(img.FindProperty("color")?.Raw));
                mat.SetFloat(IdOpacity, Mathf.Clamp01(img.EffectiveOpacity()));

                _pool[i].SetActive(true);
                i++;
            }

            for (int k = i; k < _pool.Count; k++)
                _pool[k].SetActive(false);
        }

        private void EnsurePool(int index)
        {
            while (_pool.Count <= index)
            {
                var go = QuillSurface.MakeLayer(_parent, "Quill Image", out var filter, out var renderer);
                var mat = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = _renderQueue };
                var mesh = new Mesh { name = "Quill Image Quad" };
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = mat;

                _pool.Add(go);
                _materials.Add(mat);
                _meshes.Add(mesh);
            }
        }

        private Texture2D LoadTexture(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (_textureCache.TryGetValue(source, out var cached)) return cached;

            var tex = Resources.Load<Texture2D>(source);
            if (tex == null)
                Debug.LogWarning($"[Quill] Image source '{source}' not found under a Resources folder.");
            _textureCache[source] = tex;
            return tex;
        }

        public void Dispose()
        {
            foreach (var m in _materials) if (m != null) Object.Destroy(m);
            foreach (var m in _meshes) if (m != null) Object.Destroy(m);
            foreach (var go in _pool) if (go != null) Object.Destroy(go);
        }
    }
}
