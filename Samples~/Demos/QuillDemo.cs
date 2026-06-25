// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
using UnityEngine;

namespace Quill.Samples
{
    /// <summary>
    /// Minimal bootstrap: loads a Quill document and attaches a <see cref="QuillSurface"/>. The surface
    /// advances animations and bindings each frame, so the document animates itself (no C# needed).
    ///
    /// Usage: drop this on any GameObject in a scene that has a camera (e.g. the Main Camera), press
    /// Play. Optionally assign a <c>.ui</c> file to <see cref="QuillFile"/>; otherwise the built-in
    /// sample below is used.
    /// </summary>
    [AddComponentMenu("Quill/Quill Demo")]
    public sealed class QuillDemo : MonoBehaviour
    {
        [Tooltip("Optional .ui document. If empty, the built-in sample is used.")]
        public TextAsset QuillFile;

        [Tooltip("Extra reusable component .ui files to register as types (by file name). Any .ui "
               + "under a Resources/QuillControls folder is also registered automatically.")]
        public TextAsset[] Components;

        private QuillEngine _engine;
        private QuillSurface _surface;

        private void Start()
        {
            _engine = new QuillEngine();
            RegisterControls(_engine);

            string source = QuillFile != null ? QuillFile.text : DefaultUi;
            _engine.LoadFromSource(source);

            _surface = gameObject.GetComponent<QuillSurface>();
            if (_surface == null) _surface = gameObject.AddComponent<QuillSurface>();
            _surface.Engine = _engine;
            _surface.Visible = false;   // start hidden, like a closed menu (Esc toggles it)

            _engine.OnChanged("masterVol", "value", value => Debug.Log(value));
        }

        // Register every reusable component so documents can use `Button {}`, `Slider {}`, etc.
        private void RegisterControls(QuillEngine engine)
        {
            foreach (var asset in Resources.LoadAll<TextAsset>("QuillControls"))
                engine.RegisterComponent(asset.name, asset.text);

            if (Components != null)
                foreach (var asset in Components)
                    if (asset != null) engine.RegisterComponent(asset.name, asset.text);
        }

        private void Update()
        {
            // Toggle the menu on the Escape *edge* (not every frame it's held).
            if (TogglePressed())
                _surface.Toggle();

        }

        // Edge-triggered Escape, working with whichever input backend the project uses.
        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }




        private const string DefaultUi = @"
Rectangle {
    id: root
    color: ""#10141d""
    property real t: 0
    NumberAnimation on t { from: 0; to: 1; duration: 1500; loops: Animation.Infinite; easing.type: Easing.InOutSine }

    Text {
        text: ""Quill is running""
        color: ""#e8eef7""
        fontSize: 28
        anchors.centerIn: parent
        opacity: 0.5 + 0.5 * t
    }
}
";
    }
}