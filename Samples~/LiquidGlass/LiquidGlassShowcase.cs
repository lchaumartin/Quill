// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Quill.Samples
{
    /// <summary>
    /// Bootstrap for the Liquid Glass sample. Loads <c>LiquidGlassShowcase.ui</c> and the glass
    /// components, attaches a <see cref="QuillSurface"/>, and plays the app side of the document:
    /// the clock, a small music player, and the controls wired to the 3D backdrop and audio.
    ///
    /// On URP it also turns on the camera's Opaque Texture (a per-camera override), which
    /// <c>Quill/Effect/LiquidGlass</c> needs to see the scene — no pipeline-asset change required.
    /// Esc shows / hides the interface; Tab opens the glass tuning panel.
    /// </summary>
    [AddComponentMenu("Quill/Samples/Liquid Glass Showcase")]
    public sealed class LiquidGlassShowcase : MonoBehaviour
    {
        private const string DocumentPath = "QuillLiquidGlass/LiquidGlassShowcase";
        private const string ComponentsPath = "QuillLiquidGlass/Components";

        [Tooltip("Optional .ui document. Defaults to Resources/" + DocumentPath + ".ui.")]
        public TextAsset Document;

        [Tooltip("Backdrop the brightness / Focus / Night Shift controls drive. Found automatically if empty.")]
        public LiquidGlassBackdrop Backdrop;

        [Tooltip("How fast hover / press / tint transitions settle (1/s).")]
        public float TransitionSpeed = 14f;

        [Tooltip("Lift any Application.targetFrameRate cap on start. In the Editor the cap is global and "
               + "outlives Play mode, so a scene that set 30 fps earlier keeps throttling this one.")]
        public bool UncapFrameRate = true;

        private struct Track
        {
            public string Title, Artist;
            public float Length;   // seconds
            public Track(string title, string artist, float length) { Title = title; Artist = artist; Length = length; }
        }

        private static readonly Track[] Tracks =
        {
            new Track("Harbour Lights", "The Tidewater Band", 217f),
            new Track("Northside Static", "Kilnamanagh", 194f),
            new Track("Glass & Salt", "Mira Vale", 243f),
        };

        private QuillEngine _engine;
        private QuillSurface _surface;
        private QuillTweens _tweens;

        // Colour of the resting glass tint; the "Tint" slider sets its alpha.
        private static readonly Color RestTintRgb = new Color(0x0a / 255f, 0x0f / 255f, 0x1e / 255f, 1f);

        private int _track;
        private float _elapsed = 38f;
        private int _shownSecond = -1;
        private string _shownClock;

        private void Start()
        {
            if (UncapFrameRate && Application.targetFrameRate > 0)
            {
                Debug.Log($"[Quill] Liquid Glass sample: Application.targetFrameRate was {Application.targetFrameRate} "
                        + "(set by another script or an earlier Play session) — lifting the cap.");
                Application.targetFrameRate = -1;
            }

            RequestOpaqueTexture(Camera.main);
            if (Backdrop == null) Backdrop = FindAnyObjectByType<LiquidGlassBackdrop>();

            var doc = Document != null ? Document : Resources.Load<TextAsset>(DocumentPath);
            if (doc == null)
            {
                Debug.LogError($"[Quill] Liquid Glass sample: document 'Resources/{DocumentPath}.ui' not found.");
                enabled = false;
                return;
            }

            _engine = new QuillEngine();
            foreach (var asset in Resources.LoadAll<TextAsset>("QuillControls"))
                _engine.RegisterComponent(asset.name, asset.text);
            foreach (var asset in Resources.LoadAll<TextAsset>(ComponentsPath))
                _engine.RegisterComponent(asset.name, asset.text);

            _engine.LoadFromSource(doc.text);
            _tweens = new QuillTweens(_engine.Root);

            _engine.OnChanged("player", "track", OnTrackChanged);
            _engine.OnChanged("glassStyle", "tint", ApplyGlassTint);
            ApplyGlassTint(_engine.GetValue("glassStyle", "tint"));
            ApplyTrack();
            UpdateClock();

            _surface = GetComponent<QuillSurface>();
            if (_surface == null) _surface = gameObject.AddComponent<QuillSurface>();
            _surface.Engine = _engine;
        }

        private void Update()
        {
            if (_engine == null) return;

            if (KeyPressed(Key.Escape)) _surface.Toggle();
            if (!_surface.Visible) return;
            if (KeyPressed(Key.Tab)) _engine.SetValue("tuning", "open", !_engine.GetBool("tuning", "open"));

            float dt = Time.unscaledDeltaTime;
            _tweens.Step(dt, TransitionSpeed);

            UpdateClock();
            UpdatePlayer(dt);
            DriveScene();
        }

        // ---- Document -> scene --------------------------------------------------------------------

        private void DriveScene()
        {
            if (Backdrop != null)
            {
                Backdrop.Exposure = Mathf.Lerp(0.3f, 1.25f, (float)_engine.GetNumber("brightness", "value", 0.7));
                Backdrop.Calm = _engine.GetBool("focus", "checked") ? 1f : 0f;
                Backdrop.Warmth = _engine.GetBool("nightShift", "checked") ? 1f : 0f;
            }

            AudioListener.volume = _engine.GetBool("sound", "checked", true)
                ? (float)_engine.GetNumber("volume", "value", 0.5)
                : 0f;
        }

        // ---- Glass tuning ---------------------------------------------------------------------------

        // Quill expressions can't build a colour from a number, so the "Tint" slider's alpha becomes
        // glassStyle.restTint here. GlassPane picks it up (and eases to it) like any other tint.
        private void ApplyGlassTint(object raw)
        {
            var c = RestTintRgb;
            c.a = Mathf.Clamp01((float)QuillConvert.ToDouble(raw));
            _engine.SetValue("glassStyle", "restTint", c);
        }

        // ---- Clock ----------------------------------------------------------------------------------

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string clock = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (clock == _shownClock) return;
            _shownClock = clock;

            _engine.SetValue("root", "clock", clock);
            _engine.SetValue("root", "date", now.ToString("dddd d MMMM", CultureInfo.InvariantCulture));
        }

        // ---- Music player ---------------------------------------------------------------------------

        private void UpdatePlayer(float dt)
        {
            if (!_engine.GetBool("player", "playing")) return;

            _elapsed += dt;
            if (_elapsed >= Tracks[_track].Length)
            {
                _engine.SetValue("player", "track", (double)(_track + 1));   // -> OnTrackChanged
                return;
            }

            int second = Mathf.FloorToInt(_elapsed);
            if (second != _shownSecond) PushProgress();
        }

        private void OnTrackChanged(object raw)
        {
            int n = Tracks.Length;
            int index = (int)Math.Round(QuillConvert.ToDouble(raw));
            int wrapped = ((index % n) + n) % n;

            if (wrapped != index)
            {
                // Normalise the Quill-side value; this re-enters with the wrapped index.
                _engine.SetValue("player", "track", (double)wrapped);
                return;
            }

            _track = wrapped;
            _elapsed = 0f;
            ApplyTrack();
        }

        private void ApplyTrack()
        {
            var t = Tracks[_track];
            _engine.SetValue("player", "title", t.Title);
            _engine.SetValue("player", "artist", t.Artist);
            PushProgress();
        }

        private void PushProgress()
        {
            var t = Tracks[_track];
            _shownSecond = Mathf.FloorToInt(_elapsed);
            _engine.SetValue("player", "progress", (double)Mathf.Clamp01(_elapsed / t.Length));
            _engine.SetValue("player", "elapsedText", FormatTime(_elapsed));
            _engine.SetValue("player", "remainingText", "-" + FormatTime(t.Length - _elapsed));
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // ---- Setup helpers --------------------------------------------------------------------------

        /// <summary>
        /// URP only: ask this camera for the Opaque Texture (UniversalAdditionalCameraData
        /// .requiresColorOption = On). Done by reflection so the sample compiles without URP.
        /// </summary>
        private static void RequestOpaqueTexture(Camera cam)
        {
            if (cam == null || GraphicsSettings.currentRenderPipeline == null) return;

            var type = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) return;

            var data = cam.GetComponent(type);
            if (data == null) data = cam.gameObject.AddComponent(type);

            var prop = type.GetProperty("requiresColorOption");
            if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) return;
            try { prop.SetValue(data, Enum.Parse(prop.PropertyType, "On")); }
            catch (Exception e) { Debug.LogWarning($"[Quill] Could not enable the camera Opaque Texture: {e.Message}"); }
        }

        private enum Key { Escape, Tab }

        // Edge-triggered key press, working with whichever input backend the project uses.
        private static bool KeyPressed(Key key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            return (key == Key.Tab ? kb.tabKey : kb.escapeKey).wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key == Key.Tab ? KeyCode.Tab : KeyCode.Escape);
#else
            return false;
#endif
        }
    }
}
