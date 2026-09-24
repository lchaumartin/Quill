// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Renders a <see cref="QuillEngine"/>'s element tree. The tree is split into three layers, each
    /// drawn with its own shader and a fixed render-queue so they composite in a stable order:
    ///
    ///   1. Rectangles  — one full-screen SDF pass (rounded boxes + borders).   queue 4000
    ///   2. Images      — one textured quad per Image element.                   queue 4001
    ///   3. Text        — one combined glyph mesh per font.                      queue 4002
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

        [Tooltip("When true, the root item's width/height are driven by the surface pixel size each "
               + "frame, so `parent.width` / `parent.height` track the window.")]
        public bool RootFillsSurface = true;

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
            _effects = new QuillShaderEffectLayer(t, 4001);
            _text = new QuillTextLayer(t, 4002);

            _layerRoot.SetActive(_visible);
        }

        private void LateUpdate()
        {
            if (!_visible || Engine == null || Engine.Root == null) return;

            float w = Mathf.Max(1, Screen.width);
            float h = Mathf.Max(1, Screen.height);

            if (RootFillsSurface)
            {
                Engine.Root.Property("width").SetValue((double)w);
                Engine.Root.Property("height").SetValue((double)h);
            }

            // Read the pointer in surface pixels (top-left origin) and tick the engine.
            ReadPointer(out float px, out float py, out bool down);
            Engine.Update(Time.deltaTime, px, py, down);

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

            _rects.Render(_rectList, w, h);
            _images.Render(_imageList, w, h);
            _effects.Render(_effectList, w, h);
            _text.Render(_textList, w, h);
        }

        // Reads the mouse from whichever input backend is enabled. Y is flipped to top-left origin.
        private static void ReadPointer(out float px, out float py, out bool down)
        {
            float mx = 0, my = 0; down = false;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                mx = p.x; my = p.y;
                down = mouse.leftButton.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            var p = Input.mousePosition;
            mx = p.x; my = p.y;
            down = Input.GetMouseButton(0);
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
