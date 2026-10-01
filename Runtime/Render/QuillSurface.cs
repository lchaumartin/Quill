// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Renders a <see cref="QuillEngine"/>'s element tree. The tree is split into three layers, each
    /// drawn with its own shader and a fixed render-queue so they composite in a stable order:
    ///
    ///   1. Rectangles  — one draw call of SDF quads (rounded boxes + borders). queue 4000
    ///   2. Images      — one textured quad per Image element.                   queue 4001
    ///   3. Effects     — one quad per ShaderEffect, one queue each, tree order. queue 4002–4097
    ///   4. Text        — one combined glyph mesh per font.                      queue 4100
    ///
    /// Each frame the surface makes the root fill it, flushes pending bindings, collects the visible
    /// items in tree order, and hands each layer its slice.
    ///
    /// Known limitation (prototype): ordering between layers is fixed (text over images over rects),
    /// not strictly interleaved by tree order. That matches the common case (labels/icons sit on top
    /// of their backing panels). A future unified per-quad path would remove it.
    /// </summary>
    [AddComponentMenu("Quill/Quill Surface")]
    public sealed class QuillSurface : MonoBehaviour
    {
        // Keep in sync with the rectangle cap used by the rect layer.
        public const int MaxRects = 256;

        public QuillEngine Engine;

        [Tooltip("When true, the root item's width/height are driven by the surface size (in Quill pixels) "
               + "each frame, so `parent.width` / `parent.height` track the window.")]
        public bool RootFillsSurface = true;

        /// <summary>How Quill pixels (the units documents are written in) map to screen pixels.</summary>
        public enum Scaling
        {
            /// <summary>One Quill pixel is <see cref="ScaleFactor"/> screen pixels, whatever the resolution.</summary>
            ConstantPixelSize,
            /// <summary>The UI is sized against <see cref="ReferenceResolution"/> and scales with the screen.</summary>
            ScaleWithScreenSize,
        }

        /// <summary>How <see cref="Scaling.ScaleWithScreenSize"/> fits the reference resolution to the screen.</summary>
        public enum ScreenMatch
        {
            /// <summary>Blend between matching the width (0) and the height (1), see <see cref="MatchWidthOrHeight"/>.</summary>
            MatchWidthOrHeight,
            /// <summary>The whole reference area always fits: the surface is never smaller than it.</summary>
            Expand,
            /// <summary>The reference area always covers the screen: the surface is never larger than it.</summary>
            Shrink,
        }

        [Header("Scaling")]
        [Tooltip("ScaleWithScreenSize keeps the UI the same size relative to the screen at any resolution or "
               + "pixel density (e.g. a Retina browser canvas). ConstantPixelSize uses a fixed factor.")]
        public Scaling ScalingMode = Scaling.ScaleWithScreenSize;

        [Tooltip("ConstantPixelSize: screen pixels per Quill pixel.")]
        [Min(0.01f)] public float ScaleFactor = 1f;

        [Tooltip("ScaleWithScreenSize: the resolution the documents are designed for.")]
        public Vector2 ReferenceResolution = new Vector2(1280f, 720f);

        [Tooltip("ScaleWithScreenSize: how the reference resolution is fitted to the screen.")]
        public ScreenMatch Match = ScreenMatch.Expand;

        [Tooltip("MatchWidthOrHeight: 0 scales with the screen width, 1 with the height, in between blends.")]
        [Range(0f, 1f)] public float MatchWidthOrHeight = 0.5f;

        /// <summary>Screen pixels per Quill pixel, as used for the last frame.</summary>
        public float Scale { get; private set; } = 1f;

        /// <summary>The surface size in Quill pixels (what the root item fills).</summary>
        public Vector2 Size => new Vector2(Mathf.Max(1, Screen.width) / Scale, Mathf.Max(1, Screen.height) / Scale);

        /// <summary>Screen pixels per Quill pixel for a screen of <paramref name="screenW"/> × <paramref name="screenH"/>.</summary>
        public float ComputeScale(float screenW, float screenH)
        {
            if (ScalingMode == Scaling.ConstantPixelSize) return Mathf.Max(0.01f, ScaleFactor);

            float rw = Mathf.Max(1f, ReferenceResolution.x), rh = Mathf.Max(1f, ReferenceResolution.y);
            float sx = screenW / rw, sy = screenH / rh;
            float scale;
            switch (Match)
            {
                case ScreenMatch.Expand: scale = Mathf.Min(sx, sy); break;
                case ScreenMatch.Shrink: scale = Mathf.Max(sx, sy); break;
                default:
                    // Blend in log space, like CanvasScaler: halfway between 2x and 0.5x is 1x.
                    scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(sx, 2f), Mathf.Log(sy, 2f), MatchWidthOrHeight));
                    break;
            }
            return Mathf.Max(0.01f, scale);
        }

        // Profiler markers, so Quill's per-frame cost shows up by phase in the Unity Profiler and can be
        // read with a ProfilerRecorder (the benchmark does). Quill.Surface encloses the other five.
        private static readonly ProfilerMarker s_SurfaceMarker = new ProfilerMarker("Quill.Surface");
        private static readonly ProfilerMarker s_TickMarker = new ProfilerMarker("Quill.Tick");
        private static readonly ProfilerMarker s_CollectMarker = new ProfilerMarker("Quill.Collect");
        private static readonly ProfilerMarker s_RectsMarker = new ProfilerMarker("Quill.Render.Rects");
        private static readonly ProfilerMarker s_LayersMarker = new ProfilerMarker("Quill.Render.ImagesEffects");
        private static readonly ProfilerMarker s_TextMarker = new ProfilerMarker("Quill.Render.Text");

        private QuillRectLayer _rects;
        private QuillImageLayer _images;
        private QuillShaderEffectLayer _effects;
        private QuillTextLayer _text;

        private readonly List<QuillItem> _visuals = new List<QuillItem>(MaxRects);
        private readonly List<QuillRectangle> _rectList = new List<QuillRectangle>();
        private readonly List<QuillImage> _imageList = new List<QuillImage>();
        private readonly List<QuillShaderEffect> _effectList = new List<QuillShaderEffect>();
        private readonly List<QuillText> _textList = new List<QuillText>();

        private GameObject _layerRoot;
        private bool _visible = true;

        /// <summary>
        /// Show/hide the whole surface (like an in-game menu). Hiding deactivates every rendered
        /// layer so nothing is drawn, and pauses ticking so animations and input stop; showing
        /// resumes from the current state. Toggle this rather than the component's <c>enabled</c>
        /// flag — disabling the component alone leaves the last frame frozen on screen.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (_layerRoot != null) _layerRoot.SetActive(value);
            }
        }

        public void Toggle() => Visible = !Visible;
        public void Show() => Visible = true;
        public void Hide() => Visible = false;

        private void Awake()
        {
            // All rendered layers live under one child so the surface can be shown/hidden at once.
            _layerRoot = new GameObject("Quill Layers") { hideFlags = HideFlags.DontSave };
            _layerRoot.transform.SetParent(transform, false);
            var t = _layerRoot.transform;

            _rects = new QuillRectLayer(t, 4000);
            _images = new QuillImageLayer(t, 4001);
            _effects = new QuillShaderEffectLayer(t, 4002);
            _text = new QuillTextLayer(t, 4100);

            _layerRoot.SetActive(_visible);
        }

        private void LateUpdate()
        {
            if (!_visible || Engine == null || Engine.Root == null) return;
            using var surfaceScope = s_SurfaceMarker.Auto();

            // Screen pixels, and the same area in Quill pixels (the units documents are written in).
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            Scale = ComputeScale(sw, sh);
            float w = sw / Scale;
            float h = sh / Scale;

            using (s_TickMarker.Auto())
            {
                if (RootFillsSurface)
                {
                    Engine.Root.Property("width").SetNumber(w);
                    Engine.Root.Property("height").SetNumber(h);
                }

                // Read the pointer in surface pixels (top-left origin) and tick the engine.
                ReadPointer(out float px, out float py, out bool down, out float wheelX, out float wheelY);
                Engine.Update(Time.deltaTime, px / Scale, py / Scale, down, wheelX, wheelY);
            }

            using (s_CollectMarker.Auto())
            {
                Engine.CollectVisuals(_visuals);
                _rectList.Clear(); _imageList.Clear(); _effectList.Clear(); _textList.Clear();
                for (int i = 0; i < _visuals.Count; i++)
                {
                    switch (_visuals[i])
                    {
                        case QuillShaderEffect fx: _effectList.Add(fx); break;
                        case QuillRectangle r: _rectList.Add(r); break;
                        case QuillImage img: _imageList.Add(img); break;
                        case QuillText t: _textList.Add(t); break;
                    }
                }
            }

            // Layers take the surface size in Quill pixels; rects and text also get the scale so they
            // rasterise at full screen resolution (crisp edges and glyphs at any scale).
            using (s_RectsMarker.Auto()) _rects.Render(_rectList, w, h, Scale);
            using (s_LayersMarker.Auto())
            {
                _images.Render(_imageList, w, h);
                _effects.Render(_effectList, w, h);
            }
            using (s_TextMarker.Auto()) _text.Render(_textList, w, h, Scale);
        }

        /// <summary>Wheel units per notch handed to Quill (angle-delta convention: 120 per notch).</summary>
        private const float WheelNotch = 120f;

        // Reads the mouse in screen pixels from whichever input backend is enabled. Y is flipped to
        // top-left origin. The caller converts to Quill pixels.
        // The wheel is reported in angle-delta units (120 per notch, +y = away from the user).
        private static void ReadPointer(out float px, out float py, out bool down, out float wheelX, out float wheelY)
        {
            float mx = 0, my = 0; down = false;
            wheelX = 0; wheelY = 0;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                mx = p.x; my = p.y;
                down = mouse.leftButton.isPressed;
                // With the Input System's default (uniform) scroll behaviour one notch reads as 1;
                // older settings report raw platform units (±120 on Windows) — pass those through.
                var sc = mouse.scroll.ReadValue();
                float k = Mathf.Abs(sc.x) > 10f || Mathf.Abs(sc.y) > 10f ? 1f : WheelNotch;
                wheelX = sc.x * k;
                wheelY = sc.y * k;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            var p = Input.mousePosition;
            mx = p.x; my = p.y;
            down = Input.GetMouseButton(0);
            var sc = Input.mouseScrollDelta;
            wheelX = sc.x * WheelNotch;
            wheelY = sc.y * WheelNotch;
#endif
            px = mx;
            py = Mathf.Max(1, Screen.height) - my;
        }

        private void OnDestroy()
        {
            _rects?.Dispose();
            _images?.Dispose();
            _effects?.Dispose();
            _text?.Dispose();
        }

        // ---- Shared helpers --------------------------------------------------------------------

        /// <summary>Pixel (top-left origin, y-down) → clip space (-1..1, y-up).</summary>
        internal static Vector3 ToClip(float px, float py, float w, float h)
            => new Vector3(px / w * 2f - 1f, 1f - py / h * 2f, 0f);

        internal static GameObject MakeLayer(Transform parent, string name, out MeshFilter filter, out MeshRenderer renderer)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go;
        }
    }
}
