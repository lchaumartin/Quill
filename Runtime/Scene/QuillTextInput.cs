// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Text;

namespace Quill
{
    /// <summary>
    /// Where copy/cut/paste go. Unity's system clipboard inside a QuillSurface; an in-memory buffer
    /// elsewhere (tests, tooling). Replace both delegates to route it somewhere else.
    /// </summary>
    public static class QuillClipboard
    {
        private static string s_Buffer = "";
        public static Func<string> Get = () => s_Buffer;
        public static Action<string> Set = s => s_Buffer = s ?? "";
    }

    /// <summary>
    /// `TextInput` — one line of editable text: a caret, selection (keyboard, mouse drag,
    /// double-click), clipboard, <c>maximumLength</c>, <c>readOnly</c> and password echo. Typing
    /// reaches it while it has active focus; pressing on it focuses it (<c>activeFocusOnPress</c>).
    /// Text wider than the item scrolls horizontally to keep the caret in view. Signals:
    /// <c>accepted</c> (Return / Enter), <c>editingFinished</c> (Return / Enter, or losing focus) and
    /// <c>textEdited</c> (the user changed the text). <c>hovered</c> (read-only) is true while the pointer
    /// is over it.
    ///
    /// The renderer lays the text out (it owns the font metrics) and writes back character positions,
    /// which this class uses to place the caret from the pointer. The caret and selection are two
    /// engine-made Rectangles inside it, under the text.
    /// </summary>
    public sealed class QuillTextInput : QuillItem
    {
        // Engine-made children.
        internal QuillRectangle SelectionRect, CaretRect;
        internal QuillMouseArea Area;

        // From the renderer's last layout: x of each character boundary in the display text, in
        // Quill px from the text's start (CharX.Length == display length + 1), the horizontal scroll
        // and where the text starts inside the item (alignment).
        internal float[] CharX;
        internal float ScrollX, TextX;

        // The fixed end of the selection; the caret is the moving end.
        internal int Anchor;
        // Engine time of the last edit or caret move: the caret shows solid for a moment after it.
        internal double LastEditTime;
        internal Binding ImplicitWidth, ImplicitHeight;

        // Renderer bookkeeping: font, size and vertical placement from the last layout.
        internal object RenderCache;
        internal float TextY, LineHeight;

        private bool _editing;     // our own writes to text / cursor / selection
        private bool _dragging;

        public bool HasExplicitWidth => ImplicitWidth == null || FindProperty("width")?.Driver != ImplicitWidth;

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("text").SetValue("");
            Property("color").SetValue("white");
            Property("selectionColor").SetValue("#3a86ff");
            Property("selectedTextColor").SetValue("white");
            Property("fontSize").SetValue(16.0);
            Property("font.bold").SetValue(false);
            Property("font.italic").SetValue(false);
            Property("font.family").SetValue("");
            Property("font.letterSpacing").SetValue(0.0);
            Property("horizontalAlignment").SetValue(1.0);   // Text.AlignLeft
            Property("verticalAlignment").SetValue(32.0);    // Text.AlignTop
            Property("cursorPosition").SetValue(0.0);
            Property("selectionStart").SetValue(0.0);
            Property("selectionEnd").SetValue(0.0);
            Property("selectedText").SetValue("");
            Property("displayText").SetValue("");
            Property("maximumLength").SetValue(32767.0);
            Property("echoMode").SetValue(0.0);              // TextInput.Normal
            Property("passwordCharacter").SetValue("•");
            Property("readOnly").SetValue(false);
            Property("activeFocusOnPress").SetValue(true);
            Property("cursorVisible").SetValue(false);
            Property("contentWidth").SetValue(0.0);
            Property("contentHeight").SetValue(0.0);
            Property("enabled").SetValue(true);
            Property("hovered").SetValue(false);
        }

        // ---- State ------------------------------------------------------------------------------

        public string Text => QuillConvert.ToStr(FindProperty("text")?.Raw);
        public int CursorPosition => (int)Num("cursorPosition");
        public bool ReadOnly => Flag("readOnly", false);
        private bool HasSelection => Anchor != CursorPosition;
        private int SelStart => Math.Min(Anchor, CursorPosition);
        private int SelEnd => Math.Max(Anchor, CursorPosition);
        private bool Hidden => (int)Num("echoMode") != 0;   // Password / NoEcho: no copying out

        /// <summary>Set text, caret and anchor together (clamped), updating every derived property.</summary>
        internal void SetState(QuillEngine engine, string text, int cursor, int anchor, bool userEdit)
        {
            text ??= "";
            int max = Math.Max(0, (int)Num("maximumLength", 32767));
            if (text.Length > max) text = text.Substring(0, max);
            cursor = Math.Max(0, Math.Min(cursor, text.Length));
            anchor = Math.Max(0, Math.Min(anchor, text.Length));
            bool textChanged = text != Text;

            _editing = true;
            try
            {
                if (textChanged) Property("text").SetValue(text);
                Anchor = anchor;
                Property("cursorPosition").SetNumber(cursor);
                Property("selectionStart").SetNumber(Math.Min(cursor, anchor));
                Property("selectionEnd").SetNumber(Math.Max(cursor, anchor));
                UpdateSelectedText();
                if (textChanged) UpdateDisplayText();
            }
            finally { _editing = false; }

            if (engine != null) LastEditTime = engine.Time;
            if (userEdit && textChanged) Emit("onTextEdited");
        }

        // The text changed from outside (binding, assignment, C#): keep caret and selection valid.
        internal void OnTextChanged(QuillEngine engine)
        {
            if (_editing) return;
            int len = Text.Length;
            int max = Math.Max(0, (int)Num("maximumLength", 32767));
            if (len > max) { SetState(engine, Text, CursorPosition, Anchor, false); return; }
            _editing = true;
            try
            {
                Anchor = Math.Min(Anchor, len);
                Property("cursorPosition").SetNumber(Math.Min(CursorPosition, len));
                Property("selectionStart").SetNumber(Math.Min(Anchor, CursorPosition));
                Property("selectionEnd").SetNumber(Math.Max(Anchor, CursorPosition));
                UpdateSelectedText();
                UpdateDisplayText();
            }
            finally { _editing = false; }
        }

        // cursorPosition assigned from outside: move the caret, dropping the selection.
        internal void OnCursorAssigned(QuillEngine engine)
        {
            if (_editing) return;
            int c = CursorPosition;
            SetState(engine, Text, c, c, false);
        }

        internal void UpdateDisplayText()
        {
            string t = Text;
            string shown;
            switch ((int)Num("echoMode"))
            {
                case 1: shown = ""; break;                                   // NoEcho
                case 2: case 3:                                              // Password (and …OnEdit)
                {
                    string pc = QuillConvert.ToStr(FindProperty("passwordCharacter")?.Raw);
                    shown = new string(string.IsNullOrEmpty(pc) ? '•' : pc[0], t.Length);
                    break;
                }
                default: shown = t; break;
            }
            Property("displayText").SetValue(shown);
        }

        private void UpdateSelectedText()
        {
            string t = Text;
            int s = Math.Min(SelStart, t.Length), e = Math.Min(SelEnd, t.Length);
            Property("selectedText").SetValue(e > s && !Hidden ? t.Substring(s, e - s) : "");
        }

        // ---- Editing ----------------------------------------------------------------------------

        /// <summary>Type text at the caret, replacing the selection. Control characters are dropped.</summary>
        internal void Insert(QuillEngine engine, string s, bool userEdit)
        {
            if (ReadOnly || string.IsNullOrEmpty(s)) return;
            var clean = new StringBuilder(s.Length);
            foreach (char c in s)
                if (c >= 0x20 && c != 0x7f) clean.Append(c);
            if (clean.Length == 0) return;

            string t = Text;
            int max = Math.Max(0, (int)Num("maximumLength", 32767));
            int room = max - (t.Length - (SelEnd - SelStart));
            if (room <= 0) return;
            string add = clean.Length > room ? clean.ToString(0, room) : clean.ToString();
            int at = SelStart;
            string next = t.Substring(0, at) + add + t.Substring(SelEnd);
            SetState(engine, next, at + add.Length, at + add.Length, userEdit);
        }

        private void DeleteRange(QuillEngine engine, int from, int to)
        {
            if (ReadOnly) return;
            string t = Text;
            from = Math.Max(0, Math.Min(from, t.Length));
            to = Math.Max(from, Math.Min(to, t.Length));
            if (to == from) return;
            SetState(engine, t.Substring(0, from) + t.Substring(to), from, from, true);
        }

        private void MoveCaret(QuillEngine engine, int to, bool extend)
        {
            SetState(engine, Text, to, extend ? Anchor : to, false);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private int PrevWord(int i)
        {
            string t = Text;
            while (i > 0 && !IsWordChar(t[i - 1])) i--;
            while (i > 0 && IsWordChar(t[i - 1])) i--;
            return i;
        }

        private int NextWord(int i)
        {
            string t = Text;
            while (i < t.Length && !IsWordChar(t[i])) i++;
            while (i < t.Length && IsWordChar(t[i])) i++;
            return i;
        }

        /// <summary>
        /// The TextInput's own key handling (after Keys handlers, which can override it). True when the
        /// key was used; unused keys (Up/Down, Tab, Escape…) carry on to navigation and the parents.
        /// </summary>
        internal bool HandleKey(QuillEngine engine, int key, int modifiers)
        {
            bool shift = (modifiers & QuillKeys.ShiftModifier) != 0;
            bool ctrl = (modifiers & QuillKeys.ControlModifier) != 0;      // Ctrl, or Cmd on macOS
            bool word = ctrl || (modifiers & QuillKeys.AltModifier) != 0;  // Ctrl / Option word jumps
            int cur = CursorPosition;
            switch (key)
            {
                case QuillKeys.Key_Left:
                    if (HasSelection && !shift) MoveCaret(engine, SelStart, false);
                    else MoveCaret(engine, word ? PrevWord(cur) : Math.Max(0, cur - 1), shift);
                    return true;
                case QuillKeys.Key_Right:
                    if (HasSelection && !shift) MoveCaret(engine, SelEnd, false);
                    else MoveCaret(engine, word ? NextWord(cur) : Math.Min(Text.Length, cur + 1), shift);
                    return true;
                case QuillKeys.Key_Home:
                    MoveCaret(engine, 0, shift);
                    return true;
                case QuillKeys.Key_End:
                    MoveCaret(engine, Text.Length, shift);
                    return true;
                case QuillKeys.Key_Backspace:
                    if (HasSelection) DeleteRange(engine, SelStart, SelEnd);
                    else if (cur > 0) DeleteRange(engine, word ? PrevWord(cur) : cur - 1, cur);
                    return true;
                case QuillKeys.Key_Delete:
                    if (HasSelection) DeleteRange(engine, SelStart, SelEnd);
                    else if (cur < Text.Length) DeleteRange(engine, cur, word ? NextWord(cur) : cur + 1);
                    return true;
                case QuillKeys.Key_Return:
                case QuillKeys.Key_Enter:
                    Emit("onAccepted");
                    Emit("onEditingFinished");
                    return true;
            }

            if (ctrl && !shift)
            {
                switch (key)
                {
                    case QuillKeys.Key_A: SetState(engine, Text, Text.Length, 0, false); return true;
                    case QuillKeys.Key_C: Copy(); return true;
                    case QuillKeys.Key_X: Cut(engine); return true;
                    case QuillKeys.Key_V: Paste(engine); return true;
                }
            }

            // Keys that type something are the TextInput's even though the text arrives separately,
            // so typing a letter never also triggers a parent's shortcut or navigation.
            if ((modifiers & (QuillKeys.ControlModifier | QuillKeys.AltModifier | QuillKeys.MetaModifier)) == 0
                && (key == QuillKeys.Key_Space || (key >= QuillKeys.Key_0 && key <= QuillKeys.Key_0 + 9)
                    || (key >= QuillKeys.Key_A && key < QuillKeys.Key_A + 26)))
                return true;
            return false;
        }

        private void Copy()
        {
            if (HasSelection && !Hidden) QuillClipboard.Set(Text.Substring(SelStart, SelEnd - SelStart));
        }

        private void Cut(QuillEngine engine)
        {
            if (!HasSelection || Hidden || ReadOnly) return;
            Copy();
            DeleteRange(engine, SelStart, SelEnd);
        }

        private void Paste(QuillEngine engine)
        {
            string s = QuillClipboard.Get();
            if (!string.IsNullOrEmpty(s)) Insert(engine, s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '), true);
        }

        // ---- Focus & pointer --------------------------------------------------------------------

        internal void FocusGained(QuillEngine engine)
        {
            LastEditTime = engine.Time;
            Property("cursorVisible").SetValue(QuillConvert.Box(!ReadOnly));
        }

        internal void FocusLost(QuillEngine engine)
        {
            _dragging = false;
            Property("cursorVisible").SetValue(QuillConvert.False);
            if (HasSelection) SetState(engine, Text, CursorPosition, CursorPosition, false);
            Emit("onEditingFinished");
        }

        /// <summary>The caret position nearest to <paramref name="localX"/> (px from the item's left).</summary>
        public int PositionAt(double localX)
        {
            int n = Text.Length;
            var xs = CharX;
            if (xs == null || xs.Length != n + 1)
            {
                // Not laid out yet (or outside Unity): assume an average glyph width.
                double w = Math.Max(1, Num("fontSize", 16) * 0.55);
                return Math.Max(0, Math.Min(n, (int)Math.Round(localX / w)));
            }
            double x = localX - TextX + ScrollX;
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                double d = Math.Abs(xs[i] - x);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        internal void PointerPressed(QuillEngine engine)
        {
            if (Flag("activeFocusOnPress", true)) engine.SetActiveFocus(this, QuillEngine.FocusReason.Mouse);
            int pos = PositionAt(Area.Num("mouseX"));
            bool extend = (engine.KeyboardModifiers & QuillKeys.ShiftModifier) != 0;
            MoveCaret(engine, pos, extend);
            _dragging = true;
        }

        internal void PointerMoved(QuillEngine engine)
        {
            if (!_dragging || !Area.Flag("pressed", false)) return;
            MoveCaret(engine, PositionAt(Area.Num("mouseX")), true);
        }

        internal void PointerReleased() => _dragging = false;

        internal void PointerDoubleClicked(QuillEngine engine)
        {
            int pos = PositionAt(Area.Num("mouseX"));
            string t = Text;
            if (Hidden) { SetState(engine, t, t.Length, 0, false); return; }   // whole field, like select-all
            int s = pos, e = pos;
            while (s > 0 && IsWordChar(t[s - 1])) s--;
            while (e < t.Length && IsWordChar(t[e])) e++;
            SetState(engine, t, e, s, false);
        }

        // ---- Methods (callable from documents) --------------------------------------------------

        public override bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            double A(int i) => args != null && i < args.Length ? QuillConvert.ToDouble(args[i]) : 0;
            string t = Text;
            switch (name)
            {
                case "selectAll": SetState(engine, t, t.Length, 0, false); return true;
                case "select": SetState(engine, t, (int)A(1), (int)A(0), false); return true;
                case "deselect": SetState(engine, t, CursorPosition, CursorPosition, false); return true;
                case "clear": SetState(engine, "", 0, 0, false); return true;
                case "copy": Copy(); return true;
                case "cut": Cut(engine); return true;
                case "paste": Paste(engine); return true;
                case "insert":
                {
                    int at = Math.Max(0, Math.Min((int)A(0), t.Length));
                    string s = args != null && args.Length > 1 ? QuillConvert.ToStr(args[1]) : "";
                    SetState(engine, t.Insert(at, s), CursorPosition >= at ? CursorPosition + s.Length : CursorPosition,
                             Anchor >= at ? Anchor + s.Length : Anchor, false);
                    return true;
                }
                case "remove":
                {
                    int a = Math.Max(0, Math.Min((int)A(0), t.Length)), b = Math.Max(a, Math.Min((int)A(1), t.Length));
                    int Shift(int p) => p <= a ? p : p >= b ? p - (b - a) : a;
                    SetState(engine, t.Remove(a, b - a), Shift(CursorPosition), Shift(Anchor), false);
                    return true;
                }
                case "positionAt":
                    result = (double)PositionAt(A(0));
                    return true;
                default: return false;
            }
        }
    }

    // TextInput wiring in the build pipeline.
    public sealed partial class QuillEngine
    {
        private void SetupTextInput(QuillTextInput t)
        {
            // `font.pixelSize` / `font.pointSize` feed fontSize, as for Text.
            var size = t.Property("fontSize");
            if (!size.HasBinding)
            {
                if (t.HasProperty("font.pixelSize"))
                    size.SetBinding(new Binding(this, () => t.Property("font.pixelSize").Get()));
                else if (t.HasProperty("font.pointSize"))
                    size.SetBinding(Binding.Numeric(this, () => t.Property("font.pointSize").GetNumber() * 4.0 / 3.0));
            }

            // Implicit size: the measured content (renderer), unless the document sizes it.
            var w = t.Property("width");
            if (!w.HasBinding) w.SetBinding(t.ImplicitWidth = Binding.Numeric(this, () => t.Property("contentWidth").GetNumber()));
            var h = t.Property("height");
            if (!h.HasBinding) h.SetBinding(t.ImplicitHeight = Binding.Numeric(this, () => t.Property("contentHeight").GetNumber()));

            // Selection and caret: Rectangles under the text, placed by the renderer's layout pass.
            QuillRectangle MakeRect()
            {
                var r = (QuillRectangle)QuillTypeRegistry.Create("Rectangle");
                r.Engine = this;
                r.Property("width").SetNumber(0);
                t.AddChild(r);
                return r;
            }
            t.SelectionRect = MakeRect();
            t.CaretRect = MakeRect();

            // Pointer: an engine-made MouseArea covering the input.
            var area = (QuillMouseArea)QuillTypeRegistry.Create("MouseArea");
            area.Engine = this;
            t.AddChild(area);
            area.Property("width").SetBinding(Binding.Numeric(this, () => t.Property("width").GetNumber()));
            area.Property("height").SetBinding(Binding.Numeric(this, () => t.Property("height").GetNumber()));
            area.Property("enabled").SetBinding(new Binding(this, () => t.Property("enabled").Get()));
            area.Handlers["onDoubleClicked"] = null;   // so a second click becomes a double-click
            area.Pressed += () => t.PointerPressed(this);
            area.PositionChanged += () => t.PointerMoved(this);
            area.Released += () => t.PointerReleased();
            area.Canceled += () => t.PointerReleased();   // a Flickable took the drag
            area.DoubleClicked += () => t.PointerDoubleClicked(this);
            var contains = area.Property("containsMouse");
            contains.Changed += () => t.Property("hovered").SetValue(contains.Raw);
            t.Area = area;

            t.Property("text").Changed += () => t.OnTextChanged(this);
            t.Property("echoMode").Changed += () => t.UpdateDisplayText();
            t.Property("passwordCharacter").Changed += () => t.UpdateDisplayText();
            t.Property("cursorPosition").Changed += () => t.OnCursorAssigned(this);
            t.Property("readOnly").Changed += () =>
                t.Property("cursorVisible").SetValue(QuillConvert.Box(ActiveFocusItem == t && !t.ReadOnly));
            t.OnTextChanged(this);
            t.Anchor = t.CursorPosition;
        }
    }
}
