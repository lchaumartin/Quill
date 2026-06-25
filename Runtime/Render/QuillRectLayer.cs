// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// The full-screen SDF layer. All rectangles are uploaded into a single <see cref="ComputeBuffer"/>
    /// (a <c>StructuredBuffer</c> in the shader) and resolved per pixel by <c>Quill/Surface</c> in one
    /// draw — no per-element geometry, and no fixed element cap, so it scales to thousands of rects.
    /// </summary>
    internal sealed class QuillRectLayer
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RectGpu
        {
            public Vector4 bounds;       // xy = top-left px, zw = size px
            public Vector4 color;        // rgba fill
            public Vector4 prm;          // x = radius, y = opacity, z = border width
            public Vector4 borderColor;  // rgba border
        }
        private const int Stride = 64; // 4 * float4

        // Safety ceiling so a runaway document can't allocate unbounded GPU memory.
        private const int MaxRects = 1 << 17; // 131072

        private readonly Material _material;
        private RectGpu[] _cpu = new RectGpu[1024];
        private ComputeBuffer _buffer;
        private int _capacity;

        private static readonly int IdRects = Shader.PropertyToID("_Rects");
        private static readonly int IdCount = Shader.PropertyToID("_RectCount");
        private static readonly int IdScreen = Shader.PropertyToID("_ScreenSize");

        public QuillRectLayer(Transform parent, int renderQueue)
        {
            var shader = Shader.Find("Quill/Surface");
            if (shader == null)
            {
                Debug.LogError("[Quill] Shader 'Quill/Surface' not found.");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.renderQueue = renderQueue;

            QuillSurface.MakeLayer(parent, "Quill Rects", out var filter, out var renderer);
            filter.sharedMesh = BuildFullscreenQuad();
            renderer.sharedMaterial = _material;

            EnsureCapacity(_cpu.Length);
        }

        public void Render(List<QuillRectangle> rects, float w, float h)
        {
            if (_material == null) return;

            int count = Mathf.Min(rects.Count, MaxRects);
            EnsureCapacity(count);

            for (int i = 0; i < count; i++)
            {
                var r = rects[i];
                Color c = QuillConvert.ToColor(r.FindProperty("color")?.Raw);
                Color bc = QuillConvert.ToColor(r.FindProperty("border.color")?.Raw);

                _cpu[i] = new RectGpu
                {
                    bounds = new Vector4(r.AbsX(), r.AbsY(), r.Num("width"), r.Num("height")),
                    color = new Vector4(c.r, c.g, c.b, c.a),
                    prm = new Vector4(r.Num("radius"), Mathf.Clamp01(r.EffectiveOpacity()), r.Num("border.width"), 0f),
                    borderColor = new Vector4(bc.r, bc.g, bc.b, bc.a),
                };
            }

            if (count > 0 && _buffer != null)
                _buffer.SetData(_cpu, 0, 0, count);

            if (_buffer != null) _material.SetBuffer(IdRects, _buffer);
            _material.SetInt(IdCount, count);
            _material.SetVector(IdScreen, new Vector4(w, h, 1f / w, 1f / h));
        }

        private void EnsureCapacity(int needed)
        {
            if (needed < 1) needed = 1;
            if (_buffer != null && _capacity >= needed) return;

            int cap = Mathf.Max(_capacity, 1024);
            while (cap < needed) cap *= 2;
            cap = Mathf.Min(cap, MaxRects);

            _buffer?.Release();
            _buffer = new ComputeBuffer(cap, Stride, ComputeBufferType.Structured);
            _capacity = cap;
            if (_cpu.Length < cap) _cpu = new RectGpu[cap];
        }

        private static Mesh BuildFullscreenQuad()
        {
            var mesh = new Mesh { name = "Quill Fullscreen Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }

        public void Dispose()
        {
            _buffer?.Release();
            _buffer = null;
            if (_material != null) Object.Destroy(_material);
        }
    }
}
