// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    // Keyboard focus, key events and focus navigation (keyboard and gamepad).
    //
    // One item at a time has active focus. Key events go to it first and bubble up its parents until
    // something accepts them; at each level, as in QML, the specific `Keys.on<Key>Pressed` handler
    // runs first (event already accepted), then `Keys.onPressed` (accept it to stop it), then the
    // item's own handling (TextInput editing), then `KeyNavigation`. What nobody accepts moves focus:
    // Tab / Backtab through items with `activeFocusOnTab`, in tree order, and arrows spatially to the
    // nearest such item in that direction — what a gamepad's D-pad needs.
    public sealed partial class QuillEngine
    {
        /// <summary>Why focus moved. Keyboard focus shows a focus ring (<c>visualFocus</c>); the others don't.</summary>
        public enum FocusReason { Other, Mouse, Keyboard }

        /// <summary>The item that receives key events, or null.</summary>
        public QuillItem ActiveFocusItem { get; private set; }

        /// <summary>Raised after active focus moves (the new item, or null).</summary>
        public event Action<QuillItem> ActiveFocusChanged;

        /// <summary>Arrow keys (and the gamepad D-pad) move focus to the nearest focusable item in that direction.</summary>
        public bool SpatialNavigation = true;

        /// <summary>Modifier keys currently held (<see cref="QuillKeys"/> flags). Set by the input source.</summary>
        public int KeyboardModifiers;

        /// <summary>Give <paramref name="item"/> active focus, as its <c>forceActiveFocus()</c> does.</summary>
        public void ForceActiveFocus(QuillItem item) => SetActiveFocus(item, FocusReason.Other);

        /// <summary>Remove active focus from whichever item has it.</summary>
        public void ClearFocus() => SetActiveFocus(null, FocusReason.Other);

        internal void SetActiveFocus(QuillItem item, FocusReason reason)
        {
            if (item is QuillNonVisual) item = null;
            if (item == ActiveFocusItem)
            {
                if (item != null && reason != FocusReason.Other) SetFlag(item, "visualFocus", reason == FocusReason.Keyboard);
                return;
            }

            var old = ActiveFocusItem;
            ActiveFocusItem = item;
            if (old != null)
            {
                SetFlag(old, "visualFocus", false);
                SetFlag(old, "activeFocus", false);
                SetFlag(old, "focus", false);
                (old as QuillTextInput)?.FocusLost(this);
            }
            // A handler reacting to the loss may already have moved focus somewhere else.
            if (ActiveFocusItem != item) return;
            if (item != null)
            {
                // Each write can run handlers that move focus on (a TextField handing it to its
                // TextInput): stop as soon as this item no longer has it.
                SetFlag(item, "focus", true);
                if (ActiveFocusItem != item) return;
                SetFlag(item, "visualFocus", reason == FocusReason.Keyboard);
                if (ActiveFocusItem != item) return;
                SetFlag(item, "activeFocus", true);
                if (ActiveFocusItem != item) return;
                (item as QuillTextInput)?.FocusGained(this);
            }
            // Keyboard / gamepad (or code) moving focus into a Flickable scrolls the item into view.
            if (item != null && reason != FocusReason.Mouse) EnsureVisible(item);
            if (ActiveFocusItem == item) ActiveFocusChanged?.Invoke(item);
        }

        private static void SetFlag(QuillItem item, string name, bool value)
        {
            var p = item.Property(name);
            if (!(p.Raw is bool b) || b != value) p.SetValue(QuillConvert.Box(value));
        }

        /// <summary>Hooks a property as it is created: <c>focus</c> moves focus when assigned.</summary>
        internal void OnPropertyCreated(QuillObject owner, string name, QuillProperty p)
        {
            if (name == "focus" && owner is QuillItem item && !(owner is QuillNonVisual))
                p.Changed += () => FocusPropertyChanged(item);
        }

        // `focus: true` (declared or assigned) takes focus; `focus = false` on the focused item drops it.
        // Writes made by SetActiveFocus itself agree with the current state and are ignored here.
        private void FocusPropertyChanged(QuillItem item)
        {
            bool f = item.Flag("focus", false);
            if (f && item != ActiveFocusItem) SetActiveFocus(item, FocusReason.Other);
            else if (!f && item == ActiveFocusItem) SetActiveFocus(null, FocusReason.Other);
        }

        // Focus is lost when its item is hidden, disabled or removed from the tree.
        private void ValidateFocus()
        {
            var f = ActiveFocusItem;
            if (f != null && !CanHoldFocus(f)) SetActiveFocus(null, FocusReason.Other);
        }

        private bool CanHoldFocus(QuillItem item)
        {
            QuillObject o = item;
            for (; o != null; o = o.Parent)
            {
                if (o is QuillItem it)
                {
                    if (!it.Flag("visible")) return false;
                    var en = it.FindProperty("enabled");
                    if (en != null && !QuillConvert.ToBool(en.Raw)) return false;
                }
                if (o == Root) return true;
            }
            return false;   // detached
        }

        private static bool WantsTabFocus(QuillItem item)
            => !(item is QuillNonVisual) && item.FindProperty("activeFocusOnTab") is QuillProperty p && QuillConvert.ToBool(p.Raw);

        // ---- Key events -------------------------------------------------------------------------

        /// <summary>
        /// A key went down (or auto-repeated). Returns true if something handled it — a Keys handler, a
        /// TextInput, KeyNavigation, or focus navigation. <paramref name="text"/> is what the key types
        /// (defaults to letters/digits/space); typed text itself arrives through <see cref="InputText"/>.
        /// </summary>
        public bool KeyPress(int key, int modifiers = 0, bool autoRepeat = false, string text = null, bool gamepad = false)
        {
            if (Root == null) return false;
            if (key == QuillKeys.Key_Tab && (modifiers & QuillKeys.ShiftModifier) != 0) key = QuillKeys.Key_Backtab;
            ValidateFocus();

            var ev = KeyEvent(key, modifiers, text ?? QuillKeys.TextOf(key, modifiers), autoRepeat, gamepad);
            var args = new object[] { ev };
            string specific = QuillKeys.SpecificHandler(key);
            var focus = ActiveFocusItem;

            for (QuillObject o = (QuillObject)focus ?? Root; o != null; o = o.Parent)
            {
                if (specific != null && o.Handlers.ContainsKey(specific))
                {
                    ev.Property("accepted").SetValue(QuillConvert.True);
                    o.Emit(specific, args);
                    if (Accepted(ev)) { Flush(); return true; }
                }
                if (o.Handlers.ContainsKey("Keys.onPressed"))
                {
                    ev.Property("accepted").SetValue(QuillConvert.False);
                    o.Emit("Keys.onPressed", args);
                    if (Accepted(ev)) { Flush(); return true; }
                }
                // Focus may have moved in a handler: stop delivering to the old chain.
                if (ActiveFocusItem != focus) { Flush(); return true; }

                if (o == focus && focus is QuillTextInput input && input.HandleKey(this, key, modifiers))
                {
                    Flush();
                    return true;
                }
                if (o is QuillFlickable flick && FlickKey(flick, key, o == focus)) { Flush(); return true; }
                if (o is QuillItem item && KeyNavigate(item, key)) { Flush(); return true; }
                if (o == Root) break;
            }

            bool moved = DefaultNavigation(key, modifiers);
            Flush();
            return moved;
        }

        /// <summary>A key went up: <c>Keys.onReleased</c>, bubbling like presses.</summary>
        public bool KeyRelease(int key, int modifiers = 0, bool gamepad = false)
        {
            if (Root == null) return false;
            if (key == QuillKeys.Key_Tab && (modifiers & QuillKeys.ShiftModifier) != 0) key = QuillKeys.Key_Backtab;
            var ev = KeyEvent(key, modifiers, QuillKeys.TextOf(key, modifiers), false, gamepad);
            var args = new object[] { ev };
            for (QuillObject o = (QuillObject)ActiveFocusItem ?? Root; o != null; o = o.Parent)
            {
                if (o.Handlers.ContainsKey("Keys.onReleased"))
                {
                    ev.Property("accepted").SetValue(QuillConvert.False);
                    o.Emit("Keys.onReleased", args);
                    if (Accepted(ev)) { Flush(); return true; }
                }
                if (o == Root) break;
            }
            Flush();
            return false;
        }

        /// <summary>Typed text (one or more characters) for the focused TextInput.</summary>
        public void InputText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            ValidateFocus();
            if (ActiveFocusItem is QuillTextInput input) input.Insert(this, text, userEdit: true);
            Flush();
        }

        private static QuillObject KeyEvent(int key, int modifiers, string text, bool autoRepeat, bool gamepad)
        {
            var ev = new QuillObject { TypeName = "KeyEvent" };
            ev.Property("key").SetValue((double)key);
            ev.Property("modifiers").SetValue((double)modifiers);
            ev.Property("text").SetValue(text ?? "");
            ev.Property("isAutoRepeat").SetValue(QuillConvert.Box(autoRepeat));
            ev.Property("gamepad").SetValue(QuillConvert.Box(gamepad));
            ev.Property("accepted").SetValue(QuillConvert.False);
            return ev;
        }

        private static bool Accepted(QuillObject ev) => QuillConvert.ToBool(ev.FindProperty("accepted")?.Raw);

        // ---- Navigation -------------------------------------------------------------------------

        private bool KeyNavigate(QuillItem item, int key)
        {
            string name;
            switch (key)
            {
                case QuillKeys.Key_Tab: name = "KeyNavigation.tab"; break;
                case QuillKeys.Key_Backtab: name = "KeyNavigation.backtab"; break;
                case QuillKeys.Key_Up: name = "KeyNavigation.up"; break;
                case QuillKeys.Key_Down: name = "KeyNavigation.down"; break;
                case QuillKeys.Key_Left: name = "KeyNavigation.left"; break;
                case QuillKeys.Key_Right: name = "KeyNavigation.right"; break;
                default: return false;
            }
            if (!(item.FindProperty(name)?.Raw is QuillItem target) || !CanHoldFocus(target)) return false;
            SetActiveFocus(target, FocusReason.Keyboard);
            return true;
        }

        private bool DefaultNavigation(int key, int modifiers)
        {
            if ((modifiers & (QuillKeys.ControlModifier | QuillKeys.AltModifier | QuillKeys.MetaModifier)) != 0) return false;
            switch (key)
            {
                case QuillKeys.Key_Tab: return FocusInTabOrder(+1);
                case QuillKeys.Key_Backtab: return FocusInTabOrder(-1);
                case QuillKeys.Key_Left: return SpatialNavigation && FocusInDirection(-1, 0);
                case QuillKeys.Key_Right: return SpatialNavigation && FocusInDirection(+1, 0);
                case QuillKeys.Key_Up: return SpatialNavigation && FocusInDirection(0, -1);
                case QuillKeys.Key_Down: return SpatialNavigation && FocusInDirection(0, +1);
                default: return false;
            }
        }

        private readonly List<QuillItem> _focusables = new List<QuillItem>();

        // Items that take focus from Tab and arrows, in tree (document) order.
        private List<QuillItem> CollectFocusables()
        {
            _focusables.Clear();
            if (Root != null) CollectFocusables(Root);
            return _focusables;
        }

        private void CollectFocusables(QuillObject o)
        {
            if (o is QuillNonVisual) return;
            if (o is QuillItem item)
            {
                if (!item.Flag("visible")) return;
                var en = item.FindProperty("enabled");
                if (en != null && !QuillConvert.ToBool(en.Raw)) return;
                if (WantsTabFocus(item)) _focusables.Add(item);
            }
            for (int i = 0; i < o.Children.Count; i++) CollectFocusables(o.Children[i]);
        }

        /// <summary>
        /// The tab stop that owns the focused item: itself, or its nearest ancestor that takes tab
        /// focus (a TextField's inner TextInput belongs to the TextField). Navigation starts from it.
        /// </summary>
        private QuillItem FocusOwner(List<QuillItem> focusables)
        {
            for (QuillObject o = ActiveFocusItem; o != null; o = o.Parent)
                if (o is QuillItem it && focusables.Contains(it)) return it;
            return null;
        }

        private bool FocusInTabOrder(int step)
        {
            var list = CollectFocusables();
            if (list.Count == 0) return false;
            int i = list.IndexOf(FocusOwner(list));
            int next = i < 0 ? (step > 0 ? 0 : list.Count - 1) : (i + step + list.Count) % list.Count;
            SetActiveFocus(list[next], FocusReason.Keyboard);
            return true;
        }

        // The nearest tab stop in a direction, scored on the gap along it plus a heavier penalty for
        // sideways offset, so the item straight ahead wins over a closer one off to the side.
        // Inside a Flickable, the other stops in the same Flickable come first, scrolled out of view or
        // not (that is how a list is walked); beyond it only stops that are on screen count. Positions
        // are where things settle once any scrolling under way finishes.
        private bool FocusInDirection(int dx, int dy)
        {
            var list = CollectFocusables();
            if (list.Count == 0) return false;
            var from = FocusOwner(list);
            if (from == null)
            {
                SetActiveFocus(list[0], FocusReason.Keyboard);   // first press focuses something
                return true;
            }

            QuillFlickable scope = null;
            for (QuillObject o = from.Parent; o != null && scope == null; o = o.Parent) scope = o as QuillFlickable;
            var best = scope != null ? BestInDirection(list, from, dx, dy, scope) : null;
            if (best == null) best = BestInDirection(list, from, dx, dy, null);
            if (best == null) return false;
            SetActiveFocus(best, FocusReason.Keyboard);
            return true;
        }

        // within != null: only stops inside that Flickable. Otherwise only stops not scrolled out of view.
        private QuillItem BestInDirection(List<QuillItem> list, QuillItem from, int dx, int dy, QuillFlickable within)
        {
            SettledRect(from, out double fx0, out double fy0, out double fx1, out double fy1);
            double fcx = (fx0 + fx1) * 0.5, fcy = (fy0 + fy1) * 0.5;
            QuillItem best = null;
            double bestScore = double.MaxValue;
            foreach (var c in list)
            {
                if (c == from || IsAncestor(c, from) || IsAncestor(from, c)) continue;
                if (within != null ? !IsAncestor(within, c) : !OnScreen(c)) continue;
                SettledRect(c, out double cx0, out double cy0, out double cx1, out double cy1);
                double ccx = (cx0 + cx1) * 0.5, ccy = (cy0 + cy1) * 0.5;

                double along, gap, side;
                if (dx != 0)
                {
                    along = (ccx - fcx) * dx;
                    gap = dx > 0 ? cx0 - fx1 : fx0 - cx1;
                    side = Math.Max(0, Math.Max(cy0 - fy1, fy0 - cy1));   // 0 when the rows overlap
                    side += Math.Abs(ccy - fcy) * 0.1;
                }
                else
                {
                    along = (ccy - fcy) * dy;
                    gap = dy > 0 ? cy0 - fy1 : fy0 - cy1;
                    side = Math.Max(0, Math.Max(cx0 - fx1, fx0 - cx1));
                    side += Math.Abs(ccx - fcx) * 0.1;
                }
                if (along <= 0.5) continue;   // not in that direction
                double score = Math.Max(0, gap) + side * 3;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        // An item's rectangle once the Flickables around it reach the scroll positions they're heading to.
        private static void SettledRect(QuillItem it, out double x0, out double y0, out double x1, out double y1)
        {
            Rect(it, out x0, out y0, out x1, out y1);
            for (QuillObject o = it.Parent; o != null; o = o.Parent)
            {
                if (!(o is QuillFlickable f) || !f.HasTarget) continue;
                double ox = f.Property("contentX").Number - Math.Max(0, Math.Min(f.TargetX, f.MaxX));
                double oy = f.Property("contentY").Number - Math.Max(0, Math.Min(f.TargetY, f.MaxY));
                x0 += ox; x1 += ox; y0 += oy; y1 += oy;
            }
        }

        // Not entirely cut away by a clipping ancestor (scrolled out of a Flickable).
        private static bool OnScreen(QuillItem it)
        {
            SettledRect(it, out double x0, out double y0, out double x1, out double y1);
            for (QuillObject o = it.Parent; o != null; o = o.Parent)
            {
                if (!(o is QuillItem a) || !a.ClipsChildren) continue;
                SettledRect(a, out double ax0, out double ay0, out double ax1, out double ay1);
                if (x1 <= ax0 || x0 >= ax1 || y1 <= ay0 || y0 >= ay1) return false;
            }
            return true;
        }

        private static void Rect(QuillItem it, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = it.AbsX(); y0 = it.AbsY();
            x1 = x0 + it.Num("width"); y1 = y0 + it.Num("height");
        }

        private static bool IsAncestor(QuillObject ancestor, QuillObject o)
        {
            for (var p = o.Parent; p != null; p = p.Parent) if (p == ancestor) return true;
            return false;
        }

        // A press anywhere hides the keyboard focus ring; a press outside a focused TextInput ends editing.
        private void FocusOnPointerPress(QuillMouseArea pressed)
        {
            var f = ActiveFocusItem;
            if (f == null) return;
            // "Inside" is the whole tab stop: a press on a TextField's padding keeps editing.
            QuillObject owner = f;
            for (QuillObject o = f; o != null; o = o.Parent)
                if (o is QuillItem it && WantsTabFocus(it)) { owner = it; break; }
            if (f is QuillTextInput && (pressed == null || (pressed != owner && !IsAncestor(owner, pressed))))
            {
                SetActiveFocus(null, FocusReason.Mouse);
                return;
            }
            SetFlag(f, "visualFocus", false);
        }
    }
}
