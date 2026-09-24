// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Quill
{
    /// <summary>
    /// Inspector-friendly bridge between a Quill document and the scene: wire Quill signals and property
    /// changes to <see cref="UnityEvent"/>s without writing C#. Put it on the same GameObject as the
    /// surface (it finds the engine automatically) and fill in the two lists.
    ///
    ///   Signals:  id "playBtn", signal "clicked"   -> On Emit ()        e.g. SceneLoader.Load
    ///   Values:   id "masterVol", property "value"  -> On Float (float)  e.g. AudioMixer.SetVolume
    ///
    /// Ids are document-level (use-site <c>id</c>s); component internals stay private.
    /// </summary>
    [AddComponentMenu("Quill/Quill Bindings")]
    public sealed class QuillBindings : MonoBehaviour
    {
        [Serializable] public class FloatEvent : UnityEvent<float> { }
        [Serializable] public class BoolEvent : UnityEvent<bool> { }
        [Serializable] public class StringEvent : UnityEvent<string> { }

        [System.Serializable]
        public sealed class SignalHook
        {
            [Tooltip("Document-level id of the element, e.g. playBtn")] public string id;
            [Tooltip("Bare signal name, e.g. clicked / toggled / moved")] public string signal = "clicked";
            public UnityEvent onEmit;
        }

        [System.Serializable]
        public sealed class ValueHook
        {
            [Tooltip("Document-level id, e.g. masterVol")] public string id;
            [Tooltip("Property name, e.g. value / checked")] public string property = "value";
            [Tooltip("Fire once with the current value when wiring.")] public bool emitOnStart = true;
            public FloatEvent onFloat;
            public BoolEvent onBool;
            public StringEvent onString;
        }

        [Tooltip("The surface whose engine to bind to. Auto-found on this GameObject if left empty.")]
        public QuillSurface Surface;

        public List<SignalHook> signals = new List<SignalHook>();
        public List<ValueHook> values = new List<ValueHook>();

        private QuillEngine _wired;

        private void Reset() => Surface = GetComponent<QuillSurface>();

        private void Update()
        {
            if (Surface == null) Surface = GetComponent<QuillSurface>();
            var engine = Surface != null ? Surface.Engine : null;

            // Wire once the engine is live, and re-wire if the document was reloaded (new engine/tree).
            if (engine != null && engine.Root != null && engine != _wired)
            {
                _wired = engine;
                Wire(engine);
            }
        }

        private void Wire(QuillEngine engine)
        {
            foreach (var s in signals)
            {
                if (string.IsNullOrEmpty(s.id) || string.IsNullOrEmpty(s.signal)) continue;
                var hook = s;
                engine.Connect(hook.id, hook.signal, () => hook.onEmit?.Invoke());
            }

            foreach (var v in values)
            {
                if (string.IsNullOrEmpty(v.id) || string.IsNullOrEmpty(v.property)) continue;
                var hook = v;
                engine.OnChanged(hook.id, hook.property, raw => Dispatch(hook, raw));
                if (hook.emitOnStart) Dispatch(hook, engine.GetValue(hook.id, hook.property));
            }
        }

        private static void Dispatch(ValueHook hook, object raw)
        {
            hook.onFloat?.Invoke((float)QuillConvert.ToDouble(raw));
            hook.onBool?.Invoke(QuillConvert.ToBool(raw));
            hook.onString?.Invoke(QuillConvert.ToStr(raw));
        }
    }
}
