// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Reads the keyboard, typed text and the gamepad for a <see cref="QuillSurface"/> and feeds them
    /// to its engine as key events (<see cref="QuillEngine.KeyPress"/>, <see cref="QuillEngine.KeyRelease"/>,
    /// <see cref="QuillEngine.InputText"/>), with key repeat. Uses the Input System when it is enabled,
    /// otherwise the legacy Input Manager (keyboard only).
    ///
    /// The gamepad drives the UI as keys, so documents handle one set of events: D-pad / left stick →
    /// arrows, South (A / Cross) → Return, East (B / Circle) → Escape, left / right shoulder → Backtab /
    /// Tab, Start → Menu. Key events from the gamepad carry <c>event.gamepad: true</c>.
    /// </summary>
    internal sealed class QuillKeyInput
    {
        // Key repeat: the last pressed key repeats while held (navigation, deleting, moving the caret).
        private const float KeyRepeatDelay = 0.45f, KeyRepeatInterval = 0.035f;
        private const float PadRepeatDelay = 0.4f, PadRepeatInterval = 0.12f;
        private const float StickPress = 0.6f, StickRelease = 0.35f;

        private int _repeatKey;
        private bool _repeatPad;
        private float _nextRepeat;
        private readonly List<char> _typed = new List<char>();
        private readonly System.Text.StringBuilder _text = new System.Text.StringBuilder();
        private int _stickDir;   // 0 none, else the arrow key the left stick is held towards

        public void Enable()
        {
#if ENABLE_INPUT_SYSTEM
            HookKeyboard();
#endif
        }

        public void Disable()
        {
#if ENABLE_INPUT_SYSTEM
            UnhookKeyboard();
#endif
            _repeatKey = 0;
        }

        public void Poll(QuillEngine engine, bool keyboard, bool gamepad)
        {
            float now = Time.unscaledTime;
#if ENABLE_INPUT_SYSTEM
            HookKeyboard();   // the keyboard device can change (reconnects, first use)
            if (keyboard) PollKeyboard(engine, now); else _typed.Clear();
            if (gamepad) PollGamepad(engine, now);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (keyboard) PollLegacyKeyboard(engine, now);
#endif
            // Auto-repeat the held key.
            if (_repeatKey != 0 && now >= _nextRepeat)
            {
                engine.KeyPress(_repeatKey, engine.KeyboardModifiers, autoRepeat: true, gamepad: _repeatPad);
                _nextRepeat = now + (_repeatPad ? PadRepeatInterval : KeyRepeatInterval);
            }
        }

        private void Press(QuillEngine engine, int key, bool pad, float now)
        {
            engine.KeyPress(key, engine.KeyboardModifiers, gamepad: pad);
            _repeatKey = key;
            _repeatPad = pad;
            _nextRepeat = now + (pad ? PadRepeatDelay : KeyRepeatDelay);
        }

        private void Release(QuillEngine engine, int key, bool pad)
        {
            engine.KeyRelease(key, engine.KeyboardModifiers, pad);
            if (_repeatKey == key) _repeatKey = 0;
        }

        // Keys that repeat while held: navigation and editing. Not the activating keys (holding Space
        // or A on a button clicks it once).
        private static bool Repeats(int key)
            => key != QuillKeys.Key_Escape && key != QuillKeys.Key_Return && key != QuillKeys.Key_Enter
               && key != QuillKeys.Key_Menu && key != QuillKeys.Key_Space;

#if ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.Keyboard _hooked;

        private void HookKeyboard()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == _hooked) return;
            UnhookKeyboard();
            _hooked = kb;
            if (kb != null) kb.onTextInput += OnTextInput;
        }

        private void UnhookKeyboard()
        {
            if (_hooked != null) _hooked.onTextInput -= OnTextInput;
            _hooked = null;
        }

        private void OnTextInput(char c) => _typed.Add(c);

        private static readonly (UnityEngine.InputSystem.Key key, int code)[] s_Keys = BuildKeyTable();

        private static (UnityEngine.InputSystem.Key, int)[] BuildKeyTable()
        {
            var list = new List<(UnityEngine.InputSystem.Key, int)>
            {
                (UnityEngine.InputSystem.Key.Escape, QuillKeys.Key_Escape),
                (UnityEngine.InputSystem.Key.Tab, QuillKeys.Key_Tab),
                (UnityEngine.InputSystem.Key.Backspace, QuillKeys.Key_Backspace),
                (UnityEngine.InputSystem.Key.Enter, QuillKeys.Key_Return),
                (UnityEngine.InputSystem.Key.NumpadEnter, QuillKeys.Key_Enter),
                (UnityEngine.InputSystem.Key.Insert, QuillKeys.Key_Insert),
                (UnityEngine.InputSystem.Key.Delete, QuillKeys.Key_Delete),
                (UnityEngine.InputSystem.Key.Home, QuillKeys.Key_Home),
                (UnityEngine.InputSystem.Key.End, QuillKeys.Key_End),
                (UnityEngine.InputSystem.Key.LeftArrow, QuillKeys.Key_Left),
                (UnityEngine.InputSystem.Key.UpArrow, QuillKeys.Key_Up),
                (UnityEngine.InputSystem.Key.RightArrow, QuillKeys.Key_Right),
                (UnityEngine.InputSystem.Key.DownArrow, QuillKeys.Key_Down),
                (UnityEngine.InputSystem.Key.PageUp, QuillKeys.Key_PageUp),
                (UnityEngine.InputSystem.Key.PageDown, QuillKeys.Key_PageDown),
                (UnityEngine.InputSystem.Key.Space, QuillKeys.Key_Space),
                (UnityEngine.InputSystem.Key.ContextMenu, QuillKeys.Key_Menu),
            };
            for (int i = 0; i < 12; i++) list.Add((UnityEngine.InputSystem.Key.F1 + i, QuillKeys.Key_F1 + i));
            for (int i = 0; i < 10; i++) list.Add((UnityEngine.InputSystem.Key.Digit0 + i, QuillKeys.Key_0 + i));
            for (int i = 0; i < 26; i++) list.Add((UnityEngine.InputSystem.Key.A + i, QuillKeys.Key_A + i));
            return list.ToArray();
        }

        private void PollKeyboard(QuillEngine engine, float now)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { _typed.Clear(); return; }

            // Qt's convention: on macOS, Command is the "Control" modifier (shortcuts) and Control is Meta.
            bool mac = Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
            int mods = 0;
            if (kb.shiftKey.isPressed) mods |= QuillKeys.ShiftModifier;
            if (kb.altKey.isPressed) mods |= QuillKeys.AltModifier;
            bool ctrl = kb.ctrlKey.isPressed;
            bool meta = kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed;
            if (mac) { if (meta) mods |= QuillKeys.ControlModifier; if (ctrl) mods |= QuillKeys.MetaModifier; }
            else { if (ctrl) mods |= QuillKeys.ControlModifier; if (meta) mods |= QuillKeys.MetaModifier; }
            engine.KeyboardModifiers = mods;

            foreach (var (key, code) in s_Keys)
            {
                var k = kb[key];
                if (k.wasPressedThisFrame)
                {
                    if (Repeats(code)) Press(engine, code, false, now);
                    else engine.KeyPress(code, mods);
                }
                else if (k.wasReleasedThisFrame) Release(engine, code, false);
            }

            // Typed characters (after the key presses that produced them, as in Qt).
            if (_typed.Count > 0)
            {
                _text.Clear();
                foreach (char c in _typed) if (c >= 0x20 && c != 0x7f) _text.Append(c);
                _typed.Clear();
                // Ctrl / Cmd shortcuts don't type.
                if (_text.Length > 0 && (mods & (QuillKeys.ControlModifier | QuillKeys.MetaModifier)) == 0)
                    engine.InputText(_text.ToString());
            }
        }

        private void PollGamepad(QuillEngine engine, float now)
        {
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return;

            Button(engine, pad.dpad.up, QuillKeys.Key_Up, now);
            Button(engine, pad.dpad.down, QuillKeys.Key_Down, now);
            Button(engine, pad.dpad.left, QuillKeys.Key_Left, now);
            Button(engine, pad.dpad.right, QuillKeys.Key_Right, now);
            Button(engine, pad.buttonSouth, QuillKeys.Key_Return, now);
            Button(engine, pad.buttonEast, QuillKeys.Key_Escape, now);
            Button(engine, pad.leftShoulder, QuillKeys.Key_Backtab, now);
            Button(engine, pad.rightShoulder, QuillKeys.Key_Tab, now);
            Button(engine, pad.startButton, QuillKeys.Key_Menu, now);

            // Left stick as a D-pad, with hysteresis so it doesn't chatter at the threshold.
            var v = pad.leftStick.ReadValue();
            int dir = 0;
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y);
            float threshold = _stickDir != 0 ? StickRelease : StickPress;
            if (Mathf.Max(ax, ay) >= threshold)
                dir = ax > ay ? (v.x > 0 ? QuillKeys.Key_Right : QuillKeys.Key_Left)
                              : (v.y > 0 ? QuillKeys.Key_Up : QuillKeys.Key_Down);
            if (dir != _stickDir)
            {
                if (_stickDir != 0) Release(engine, _stickDir, true);
                if (dir != 0) Press(engine, dir, true, now);
                _stickDir = dir;
            }
        }

        private void Button(QuillEngine engine, UnityEngine.InputSystem.Controls.ButtonControl b, int key, float now)
        {
            if (b.wasPressedThisFrame)
            {
                if (Repeats(key)) Press(engine, key, true, now);
                else engine.KeyPress(key, 0, gamepad: true);
            }
            else if (b.wasReleasedThisFrame) Release(engine, key, true);
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        private static readonly (KeyCode key, int code)[] s_Keys = BuildKeyTable();

        private static (KeyCode, int)[] BuildKeyTable()
        {
            var list = new List<(KeyCode, int)>
            {
                (KeyCode.Escape, QuillKeys.Key_Escape), (KeyCode.Tab, QuillKeys.Key_Tab),
                (KeyCode.Backspace, QuillKeys.Key_Backspace), (KeyCode.Return, QuillKeys.Key_Return),
                (KeyCode.KeypadEnter, QuillKeys.Key_Enter), (KeyCode.Insert, QuillKeys.Key_Insert),
                (KeyCode.Delete, QuillKeys.Key_Delete), (KeyCode.Home, QuillKeys.Key_Home),
                (KeyCode.End, QuillKeys.Key_End), (KeyCode.LeftArrow, QuillKeys.Key_Left),
                (KeyCode.UpArrow, QuillKeys.Key_Up), (KeyCode.RightArrow, QuillKeys.Key_Right),
                (KeyCode.DownArrow, QuillKeys.Key_Down), (KeyCode.PageUp, QuillKeys.Key_PageUp),
                (KeyCode.PageDown, QuillKeys.Key_PageDown), (KeyCode.Space, QuillKeys.Key_Space),
                (KeyCode.Menu, QuillKeys.Key_Menu),
            };
            for (int i = 0; i < 12; i++) list.Add((KeyCode.F1 + i, QuillKeys.Key_F1 + i));
            for (int i = 0; i < 10; i++) list.Add((KeyCode.Alpha0 + i, QuillKeys.Key_0 + i));
            for (int i = 0; i < 26; i++) list.Add((KeyCode.A + i, QuillKeys.Key_A + i));
            return list.ToArray();
        }

        private void PollLegacyKeyboard(QuillEngine engine, float now)
        {
            bool mac = Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
            int mods = 0;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods |= QuillKeys.ShiftModifier;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mods |= QuillKeys.AltModifier;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool meta = Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (mac) { if (meta) mods |= QuillKeys.ControlModifier; if (ctrl) mods |= QuillKeys.MetaModifier; }
            else { if (ctrl) mods |= QuillKeys.ControlModifier; if (meta) mods |= QuillKeys.MetaModifier; }
            engine.KeyboardModifiers = mods;

            foreach (var (key, code) in s_Keys)
            {
                if (Input.GetKeyDown(key))
                {
                    if (Repeats(code)) Press(engine, code, false, now);
                    else engine.KeyPress(code, mods);
                }
                else if (Input.GetKeyUp(key)) Release(engine, code, false);
            }

            string typed = Input.inputString;
            if (!string.IsNullOrEmpty(typed) && (mods & (QuillKeys.ControlModifier | QuillKeys.MetaModifier)) == 0)
            {
                _text.Clear();
                foreach (char c in typed) if (c >= 0x20 && c != 0x7f) _text.Append(c);
                if (_text.Length > 0) engine.InputText(_text.ToString());
            }
        }
#endif
    }
}
