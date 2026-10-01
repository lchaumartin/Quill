// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// The rectangle layer. All rectangles are packed into a float data texture (four RGBA32F texels
    /// per rect) and drawn by <c>Quill/Surface</c> in one draw call: a static mesh of one quad per
    /// rect, which the vertex shader places from the texture. Each pixel is shaded only by the rects
    /// that cover it (like any sprite batch), so GPU cost follows the area drawn, not rects × screen
    /// pixels. Per frame only the data texture is uploaded; the mesh changes only when the rect count
    /// does. No small fixed cap, so it scales to thousands of rects.
    ///
    /// A texture is used instead of a ComputeBuffer / StructuredBuffer because WebGL has no compute
    /// buffers at all, and many GLES3 mobile GPUs expose zero storage buffers to the fragment stage.
    /// <c>texelFetch</c> on a float texture works everywhere Unity 6 runs (shader target 3.5).
    /// </summary>
    internal sealed class QuillRectLayer
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RectGpu
        {
            public Vector4 bounds;       // xy = top-left px, zw = size px
            public Vector4 color;        // rgba fill
            public Vector4 prm;          // x = radius, y = opacity, z = border width, w = edge softness
            public Vector4 borderColor;  // rgba border
        }

        // Data texture layout: TexWidth texels per row, TexelsPerRect texels per rect.
        // Keep in sync with Quill/Surface (RectsPerRowShift).
        private const int TexelsPerRect = 4;
        private const int TexWidth = 1024;
        private const int RectsPerRow = TexWidth / TexelsPerRect; // 256

        // Safety ceiling so a runaway document can't allocate unbounded GPU memory.
        private const int MaxRects = 1 << 17; // 131072 → 512 rows

        private readonly Material _material;
        private Texture2D _data;
        private int _capacity;

        // One quad per rect: uv = (corner x, corner y, rect index, 0). Built for _capacity rects;
        // only the first `_drawn` quads are indexed.
        private Mesh _mesh;
        private int _meshCapacity = -1;
        private int _drawn = -1;
        private int[] _indices;

        private static readonly int IdRects = Shader.PropertyToID("_RectData");
        private static readonly int IdScreen = Shader.PropertyToID("_ScreenSize");

        public QuillRectLayer(Transform parent, int renderQueue)
        {
            var shader = Shader.Find("Quill/Surface");
            if (shader == null)
            {
                Debug.LogError("[Quill] Shader 'Quill/Surface' not found.");
                return;
            }
            if (!shader.isSupported)
                Debug.LogError("[Quill] Shader 'Quill/Surface' is not supported on this graphics API ("
                             + SystemInfo.graphicsDeviceType + ").");
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                Debug.LogError("[Quill] RGBAFloat textures are not supported on this device; rectangles won't render.");

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.renderQueue = renderQueue;

            QuillSurface.MakeLayer(parent, "Quill Rects", out var filter, out var renderer);
            _mesh = new Mesh { name = "Quill Rect Quads", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;
            renderer.sharedMaterial = _material;

            EnsureCapacity(RectsPerRow * 4);
            SetDrawnCount(0);
        }

        /// <param name="w">Surface width in Quill pixels.</param>
        /// <param name="h">Surface height in Quill pixels.</param>
        /// <param name="scale">Screen pixels per Quill pixel. Rects are uploaded in screen pixels so the
        /// shader's 1px anti-aliasing ramp stays one real pixel wide at any scale.</param>
        public void Render(List<QuillRectangle> rects, float w, float h, float scale)
        {
            if (_material == null) return;

            int count = Mathf.Min(rects.Count, MaxRects);
            EnsureCapacity(count);

            if (count > 0)
            {
                // Write straight into the texture's CPU copy — no intermediate managed array.
                NativeArray<RectGpu> cpu = _data.GetPixelData<RectGpu>(0);
                for (int i = 0; i < count; i++)
                {
                    var r = rects[i];
                    Color c = QuillConvert.ToColor(r.FindProperty("color")?.Raw);
                    Color bc = QuillConvert.ToColor(r.FindProperty("border.color")?.Raw);

                    cpu[i] = new RectGpu
                    {
                        bounds = new Vector4(r.AbsX(), r.AbsY(), r.Num("width"), r.Num("height")) * scale,
                        color = new Vector4(c.r, c.g, c.b, c.a),
                        prm = new Vector4(r.Num("radius") * scale, Mathf.Clamp01(r.EffectiveOpacity()),
                                          r.Num("border.width") * scale, Mathf.Max(0f, r.Num("softness")) * scale),
                        borderColor = new Vector4(bc.r, bc.g, bc.b, bc.a),
                    };
                }
                _data.Apply(false, false);
            }

            SetDrawnCount(count);
            _material.SetTexture(IdRects, _data);
            float sw = w * scale, sh = h * scale;
            _material.SetVector(IdScreen, new Vector4(sw, sh, 1f / sw, 1f / sh));
        }

        private void EnsureCapacity(int needed)
        {
            if (needed < 1) needed = 1;
            if (_data != null && _capacity >= needed) return;

            int rows = Mathf.Max(_capacity / RectsPerRow, 4);
            while (rows * RectsPerRow < needed) rows *= 2;
            rows = Mathf.Min(rows, MaxRects / RectsPerRow);

            if (_data != null) Object.Destroy(_data);
            _data = new Texture2D(TexWidth, rows, TextureFormat.RGBAFloat, false, true)
            {
                name = "Quill Rect Data",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            _capacity = rows * RectsPerRow;
        }

        /// <summary>Index the first <paramref name="count"/> quads (rebuilding the quad mesh if the capacity grew).</summary>
        private void SetDrawnCount(int count)
        {
            if (_mesh == null) return;
            if (_meshCapacity != _capacity) BuildQuads(_capacity);
            if (count == _drawn) return;
            _drawn = count;
            _mesh.SetIndices(_indices, 0, count * 6, MeshTopology.Triangles, 0, false);
        }

        private void BuildQuads(int quads)
        {
            var verts = new Vector3[quads * 4];   // unused by the shader; zero
            var uvs = new Vector4[quads * 4];
            _indices = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, t = q * 6;
                uvs[v]     = new Vector4(0f, 0f, q, 0f);
                uvs[v + 1] = new Vector4(1f, 0f, q, 0f);
                uvs[v + 2] = new Vector4(0f, 1f, q, 0f);
                uvs[v + 3] = new Vector4(1f, 1f, q, 0f);
                _indices[t] = v; _indices[t + 1] = v + 2; _indices[t + 2] = v + 1;
                _indices[t + 3] = v + 2; _indices[t + 4] = v + 3; _indices[t + 5] = v + 1;
            }

            _mesh.Clear();
            _mesh.indexFormat = quads * 4 > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                  : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.vertices = verts;
            _mesh.SetUVs(0, uvs);
            // Placement happens in the shader, so the CPU bounds must cover everything.
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            _meshCapacity = quads;
            _drawn = -1;
        }

        public void Dispose()
        {
            if (_data != null) Object.Destroy(_data);
            _data = null;
            if (_material != null) Object.Destroy(_material);
            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = null;
        }
    }
}
