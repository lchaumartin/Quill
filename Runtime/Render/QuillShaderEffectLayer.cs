// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Draws each <see cref="QuillShaderEffect"/> as a clip-space quad using the Unity shader named by
    /// its <c>shader</c> property. Custom Quill properties are forwarded as uniforms of the same name;
    /// standard uniforms (_Rect, _ScreenSize, _Opacity) are always set; for time, shaders read Unity's
    /// built-in <c>_Time</c> (<c>_Time.y</c> = seconds since level load). Materials are pooled
    /// and rebuilt only when an effect's shader name changes.
    /// </summary>
    internal sealed class QuillShaderEffectLayer
    {
        private readonly Transform _parent;
        private readonly int _renderQueue;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>Queues reserved for effects above the base; later effects share the last one.</summary>
        public const int MaxQueueSteps = 96;
        private readonly List<string> _shaderNames = new List<string>();

        private readonly Vector3[] _v = new Vector3[4];
        private static readonly Vector2[] Uv =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        private static readonly int[] Tris = { 0, 1, 2, 0, 2, 3 };

        private static readonly int IdRect = Shader.PropertyToID("_Rect");
        private static readonly int IdScreen = Shader.PropertyToID("_ScreenSize");
        private static readonly int IdOpacity = Shader.PropertyToID("_Opacity");

        // Built-in item properties that must NOT be forwarded as shader uniforms.
        private static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "x", "y", "z", "width", "height", "visible", "opacity", "enabled", "index", "shader",
            "left", "right", "top", "bottom", "horizontalCenter", "verticalCenter", "state"
        };

        public QuillShaderEffectLayer(Transform parent, int renderQueue)
        {
            _parent = parent;
            _renderQueue = renderQueue;
        }

        public void Render(List<QuillShaderEffect> effects, float w, float h)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var fx = effects[i];
                string shaderName = QuillConvert.ToStr(fx.FindProperty("shader")?.Raw);
                EnsureSlot(i, shaderName);

                var mat = _materials[i];
                if (mat == null) { _pool[i].SetActive(false); continue; }

                float x = fx.AbsX(), y = fx.AbsY(), fw = fx.Num("width"), fh = fx.Num("height");

                _v[0] = QuillSurface.ToClip(x, y, w, h);
                _v[1] = QuillSurface.ToClip(x + fw, y, w, h);
                _v[2] = QuillSurface.ToClip(x + fw, y + fh, w, h);
                _v[3] = QuillSurface.ToClip(x, y + fh, w, h);

                var mesh = _meshes[i];
                mesh.Clear();
                mesh.vertices = _v;
                mesh.uv = Uv;
                mesh.triangles = Tris;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);

                // Standard uniforms.
                mat.SetVector(IdRect, new Vector4(x, y, fw, fh));
                mat.SetVector(IdScreen, new Vector4(w, h, 1f / w, 1f / h));
                mat.SetFloat(IdOpacity, Mathf.Clamp01(fx.EffectiveOpacity()));

                // Forward custom properties as same-named uniforms.
                foreach (var prop in fx.Properties)
                {
                    if (Reserved.Contains(prop.Name) || prop.Name.IndexOf('.') >= 0) continue;
                    ForwardUniform(mat, prop.Name, prop.Raw);
                }

                // One render queue step per effect keeps overlapping effects in tree order (a picker
                // on a glass panel draws over the panel), all between the images and the text. Not
                // sortingOrder: that outranks the render queue, so effects would cover the text.
                mat.renderQueue = _renderQueue + Mathf.Min(i, MaxQueueSteps - 1);
                _pool[i].SetActive(true);
            }

            for (int i = effects.Count; i < _pool.Count; i++)
                _pool[i].SetActive(false);
        }

        private static void ForwardUniform(Material mat, string name, object value)
        {
            switch (value)
            {
                case double d: mat.SetFloat(name, (float)d); break;
                case bool b: mat.SetFloat(name, b ? 1f : 0f); break;
                case Color c: mat.SetColor(name, c); break;
                case string s:
                    if (QuillConvert.TryParseColor(s, out var col)) mat.SetColor(name, col);
                    break;
            }
        }

        private void EnsureSlot(int index, string shaderName)
        {
            while (_pool.Count <= index)
            {
                QuillSurface.MakeLayer(_parent, "Quill ShaderEffect", out var filter, out var renderer);
                var mesh = new Mesh { name = "Quill Effect Quad" };
                filter.sharedMesh = mesh;
                _pool.Add(renderer.gameObject);
                _meshes.Add(mesh);
                _materials.Add(null);
                _shaderNames.Add(null);
            }

            if (_shaderNames[index] != shaderName)
            {
                if (_materials[index] != null) Object.Destroy(_materials[index]);

                Material mat = null;
                if (!string.IsNullOrEmpty(shaderName))
                {
                    var shader = Shader.Find(shaderName);
                    if (shader != null)
                        mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = _renderQueue };
                    else
                        Debug.LogWarning($"[Quill] ShaderEffect shader '{shaderName}' not found.");
                }

                _materials[index] = mat;
                _shaderNames[index] = shaderName;
                _pool[index].GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        public void Dispose()
        {
            foreach (var m in _materials) if (m != null) Object.Destroy(m);
            foreach (var m in _meshes) if (m != null) Object.Destroy(m);
            foreach (var go in _pool) if (go != null) Object.Destroy(go);
        }
    }
}
