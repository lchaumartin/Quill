// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// `Flickable` — a viewport onto content larger than itself. The items declared inside it go into
    /// its <c>contentItem</c>, which moves by <c>contentX</c> / <c>contentY</c>. It scrolls by dragging
    /// (taking the drag from the buttons it contains once the pointer has clearly moved), by flicking
    /// (momentum with <c>flickDeceleration</c>), by the mouse wheel, and by the keyboard / gamepad:
    /// moving focus to an item inside scrolls it into view, and a focused Flickable scrolls with the
    /// arrow keys. It clips its content by default (<c>clip: true</c>).
    ///
    /// <c>contentWidth</c> / <c>contentHeight</c> follow the content's extent unless the document sets
    /// them. Read-only state: <c>moving</c>, <c>flicking</c>, <c>dragging</c>, <c>atXBeginning</c>,
    /// <c>atXEnd</c>, <c>atYBeginning</c>, <c>atYEnd</c>, <c>horizontalVelocity</c>, <c>verticalVelocity</c>,
    /// <c>visibleArea.xPosition / widthRatio / yPosition / heightRatio</c> (for scroll bars).
    /// </summary>
    public sealed class QuillFlickable : QuillItem
    {
        /// <summary>Holds the declared children; moves opposite to contentX / contentY.</summary>
        public QuillItem ContentItem { get; private set; }

        // Content size: true while the engine maintains it from the content's extent.
        internal bool AutoWidth, AutoHeight;
        internal bool Wired;

        // Motion (content px, px / s). Velocity is the rate contentX / contentY change.
        internal double VelX, VelY;
        internal bool Flicking, Dragging, Moving;
        internal bool HasTarget;              // smooth scrolling (wheel, keys, scroll into view)
        internal double TargetX, TargetY;

        // Drag: where it started (pointer and content) and recent samples for the release velocity.
        internal double DragPx, DragPy, DragCX, DragCY;
        internal readonly List<(double t, double x, double y)> Samples = new List<(double, double, double)>();

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("clip").SetValue(true);
            Property("contentWidth").SetValue(-1.0);       // -1: follow the content's extent
            Property("contentHeight").SetValue(-1.0);
            Property("contentX").SetValue(0.0);
            Property("contentY").SetValue(0.0);
            Property("interactive").SetValue(true);
            Property("flickableDirection").SetValue(0.0);  // Flickable.AutoFlickDirection
            Property("boundsBehavior").SetValue(3.0);      // Flickable.DragAndOvershootBounds
            Property("flickDeceleration").SetValue(1500.0);
            Property("maximumFlickVelocity").SetValue(2500.0);
            Property("moving").SetValue(false);
            Property("flicking").SetValue(false);
            Property("dragging").SetValue(false);
            Property("horizontalVelocity").SetValue(0.0);
            Property("verticalVelocity").SetValue(0.0);
            Property("atXBeginning").SetValue(true);
            Property("atXEnd").SetValue(true);
            Property("atYBeginning").SetValue(true);
            Property("atYEnd").SetValue(true);
            Property("visibleArea.xPosition").SetValue(0.0);
            Property("visibleArea.widthRatio").SetValue(1.0);
            Property("visibleArea.yPosition").SetValue(0.0);
            Property("visibleArea.heightRatio").SetValue(1.0);

            ContentItem = QuillTypeRegistry.Create("Item");
            AddChild(ContentItem);
            Property("contentItem").SetValue(ContentItem);
        }

        // Items (and Repeaters, whose instances join their parent) live in the content item; animations,
        // Behaviors, states and timers stay on the Flickable itself.
        internal override QuillItem ChildHost(QuillObject child)
            => child is QuillNonVisual && !(child is QuillRepeater) ? this : ContentItem;

        internal double ContentW => Math.Max(Number("contentWidth"), 0);
        internal double ContentH => Math.Max(Number("contentHeight"), 0);
        internal double MaxX => Math.Max(0, ContentW - Number("width"));
        internal double MaxY => Math.Max(0, ContentH - Number("height"));

        private double Number(string name) => FindProperty(name)?.Number ?? 0;

        /// <summary><c>flick(xVelocity, yVelocity)</c>, <c>cancelFlick()</c>, <c>returnToBounds()</c>.</summary>
        public override bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            return engine.CallFlickableMethod(this, name, args);
        }

        /// <summary>Which axes scroll: flickableDirection, or (Auto) those where the content is larger.</summary>
        internal void Axes(out bool x, out bool y)
        {
            int dir = (int)Number("flickableDirection");
            if (dir == 1) { x = true; y = false; }
            else if (dir == 2) { x = false; y = true; }
            else if (dir == 3) { x = y = true; }
            else { x = ContentW > Number("width") + 0.5; y = ContentH > Number("height") + 0.5; }   // Auto
        }
    }

    // Flickable wiring and motion.
    public sealed partial class QuillEngine
    {
        private const double FlickStealDistance = 8;     // px the pointer moves before a Flickable takes the drag
        private const double WheelStep = 72;             // px per wheel notch (120 units)
        private const double KeyStep = 48;               // px per arrow key on a focused Flickable
        private const double MinFlickVelocity = 60;      // px / s: slower releases just stop
        private const double SmoothRate = 18;            // smooth scrolling: 1/s, exponential approach
        private const double ReturnRate = 12;            // overshoot returning to the bounds

        private readonly List<QuillFlickable> _flickables = new List<QuillFlickable>();
        private QuillFlickable _flickDrag;                       // the Flickable dragging now
        private readonly List<QuillFlickable> _flickChain = new List<QuillFlickable>(); // under the press, innermost first
        private double _flickPressX, _flickPressY;

        private void SetupFlickable(QuillFlickable f)
        {
            var content = f.ContentItem;
            content.Engine = this;
            if (f.Wired) return;
            f.Wired = true;

            var cw = f.Property("contentWidth");
            var ch = f.Property("contentHeight");
            f.AutoWidth = !cw.HasBinding && cw.Number < 0;
            f.AutoHeight = !ch.HasBinding && ch.Number < 0;

            // The content item moves opposite to the scroll position and is the content's size (the
            // Flickable's own size while that follows the content, so `width: parent.width` inside a
            // vertical list means the viewport's width).
            var cx = f.Property("contentX");
            var cy = f.Property("contentY");
            content.Property("x").SetBinding(Binding.Numeric(this, () => -cx.GetNumber()));
            content.Property("y").SetBinding(Binding.Numeric(this, () => -cy.GetNumber()));
            var fw = f.Property("width");
            var fh = f.Property("height");
            content.Property("width").SetBinding(Binding.Numeric(this, () => f.AutoWidth ? fw.GetNumber() : Math.Max(0, cw.GetNumber())));
            content.Property("height").SetBinding(Binding.Numeric(this, () => f.AutoHeight ? fh.GetNumber() : Math.Max(0, ch.GetNumber())));

            // Read-only state for bindings (scroll bars, fades at the ends).
            f.Property("atXBeginning").SetBinding(new Binding(this, () => QuillConvert.Box(cx.GetNumber() <= 0.5)));
            f.Property("atYBeginning").SetBinding(new Binding(this, () => QuillConvert.Box(cy.GetNumber() <= 0.5)));
            f.Property("atXEnd").SetBinding(new Binding(this, () => QuillConvert.Box(cx.GetNumber() >= Math.Max(0, cw.GetNumber() - fw.GetNumber()) - 0.5)));
            f.Property("atYEnd").SetBinding(new Binding(this, () => QuillConvert.Box(cy.GetNumber() >= Math.Max(0, ch.GetNumber() - fh.GetNumber()) - 0.5)));
            f.Property("visibleArea.xPosition").SetBinding(Binding.Numeric(this, () => cw.GetNumber() > 0 ? cx.GetNumber() / cw.GetNumber() : 0));
            f.Property("visibleArea.yPosition").SetBinding(Binding.Numeric(this, () => ch.GetNumber() > 0 ? cy.GetNumber() / ch.GetNumber() : 0));
            f.Property("visibleArea.widthRatio").SetBinding(Binding.Numeric(this, () => cw.GetNumber() > 0 ? Math.Min(1, fw.GetNumber() / cw.GetNumber()) : 1));
            f.Property("visibleArea.heightRatio").SetBinding(Binding.Numeric(this, () => ch.GetNumber() > 0 ? Math.Min(1, fh.GetNumber() / ch.GetNumber()) : 1));

            _flickables.Add(f);
            UpdateContentSize(f);
        }

        // contentWidth / contentHeight from the content's extent, unless the document sets them.
        private static void UpdateContentSize(QuillFlickable f)
        {
            if (!f.AutoWidth && !f.AutoHeight) return;
            double w = 0, h = 0;
            var kids = f.ContentItem.Children;
            for (int i = 0; i < kids.Count; i++)
            {
                if (!(kids[i] is QuillItem k) || k is QuillNonVisual || !k.Flag("visible")) continue;
                w = Math.Max(w, k.Num("x") + k.Num("width"));
                h = Math.Max(h, k.Num("y") + k.Num("height"));
            }
            if (f.AutoWidth) f.Property("contentWidth").SetNumber(w);
            if (f.AutoHeight) f.Property("contentHeight").SetNumber(h);
        }

        // ---- Per frame -----------------------------------------------------------------------------

        private void TickFlickables(double dt)
        {
            for (int i = _flickables.Count - 1; i >= 0; i--)
            {
                var f = _flickables[i];
                if (!IsAttached(f)) { _flickables.RemoveAt(i); continue; }
                UpdateContentSize(f);
                if (!f.Dragging) Animate(f, dt);
                UpdateMotionState(f);
            }
        }

        private bool IsAttached(QuillObject o)
        {
            while (o.Parent != null) o = o.Parent;
            return o == Root;
        }

        private void Animate(QuillFlickable f, double dt)
        {
            if (dt <= 0) return;
            double x = f.Property("contentX").Number, y = f.Property("contentY").Number;
            double maxX = f.MaxX, maxY = f.MaxY;
            bool overshoot = ((int)f.Num("boundsBehavior", 3) & 2) != 0;
            double decel = Math.Max(1, f.Num("flickDeceleration", 1500));

            if (f.HasTarget)
            {
                double tx = Clamp(f.TargetX, 0, maxX), ty = Clamp(f.TargetY, 0, maxY);
                double k = 1 - Math.Exp(-dt * SmoothRate);
                x += (tx - x) * k;
                y += (ty - y) * k;
                if (Math.Abs(tx - x) < 0.5 && Math.Abs(ty - y) < 0.5) { x = tx; y = ty; f.HasTarget = false; }
            }
            else if (f.Flicking)
            {
                x = Integrate(x, ref f.VelX, 0, maxX, decel, overshoot, dt);
                y = Integrate(y, ref f.VelY, 0, maxY, decel, overshoot, dt);
                if (f.VelX == 0 && f.VelY == 0) f.Flicking = false;
            }

            // Past the bounds and not being pushed further: return.
            if (!f.Flicking && !f.HasTarget)
            {
                x = ReturnToBounds(x, 0, maxX, dt);
                y = ReturnToBounds(y, 0, maxY, dt);
            }

            WriteContent(f.Property("contentX"), x);
            WriteContent(f.Property("contentY"), y);
        }

        // One axis of a flick: move, decelerate, and either stop at a bound or overshoot it and turn back.
        private static double Integrate(double pos, ref double vel, double min, double max, double decel, bool overshoot, double dt)
        {
            if (vel == 0) return pos;
            bool outside = pos < min || pos > max;
            // Overshooting brakes hard, so the content only travels a little past the end.
            double d = outside ? decel * 10 : decel;
            pos += vel * dt;
            double dv = d * dt;
            vel = Math.Abs(vel) <= dv ? 0 : vel - Math.Sign(vel) * dv;

            if (pos < min || pos > max)
            {
                if (!overshoot) { pos = Clamp(pos, min, max); vel = 0; }
                else if ((pos < min && vel > 0) || (pos > max && vel < 0)) vel = 0;   // already turning back
            }
            return pos;
        }

        private static double ReturnToBounds(double pos, double min, double max, double dt)
        {
            double target = Clamp(pos, min, max);
            if (target == pos) return pos;
            double next = pos + (target - pos) * (1 - Math.Exp(-dt * ReturnRate));
            return Math.Abs(target - next) < 0.5 ? target : next;
        }

        // Interaction writes the scroll position like an assignment (replacing a binding, as in QML),
        // but only when it actually changes.
        private static void WriteContent(QuillProperty p, double v)
        {
            if (p.Number != v) p.SetNumber(v);
        }

        private void UpdateMotionState(QuillFlickable f)
        {
            bool outside = OutOfBounds(f);
            bool moving = f.Dragging || f.Flicking || f.HasTarget || outside;
            SetState(f, "dragging", f.Dragging);
            bool wasFlicking = QuillConvert.ToBool(f.Property("flicking").Raw);
            if (f.Flicking != wasFlicking)
            {
                f.Property("flicking").SetValue(QuillConvert.Box(f.Flicking));
                f.Emit(f.Flicking ? "onFlickStarted" : "onFlickEnded");
            }
            if (moving != f.Moving)
            {
                f.Moving = moving;
                f.Property("moving").SetValue(QuillConvert.Box(moving));
                f.Emit(moving ? "onMovementStarted" : "onMovementEnded");
            }
            f.Property("horizontalVelocity").SetNumber(f.Flicking ? f.VelX : 0);
            f.Property("verticalVelocity").SetNumber(f.Flicking ? f.VelY : 0);
        }

        private static void SetState(QuillItem f, string name, bool v)
        {
            var p = f.Property(name);
            if (!(p.Raw is bool b) || b != v) p.SetValue(QuillConvert.Box(v));
        }

        private static bool OutOfBounds(QuillFlickable f)
        {
            double x = f.Property("contentX").Number, y = f.Property("contentY").Number;
            return x < -0.25 || y < -0.25 || x > f.MaxX + 0.25 || y > f.MaxY + 0.25;
        }

        // ---- Pointer -------------------------------------------------------------------------------

        /// <summary>
        /// Flickables under the pointer that may take a drag starting here, innermost first: those
        /// containing the point (within their clips) and either containing the pressed area or drawn
        /// above it.
        /// </summary>
        private void FindFlickChain(double px, double py, QuillMouseArea hit, List<QuillFlickable> chain)
        {
            chain.Clear();
            if (_flickables.Count == 0 || Root == null) return;
            int order = 0, hitOrder = -1;
            QuillFlickable top = null;
            int topOrder = -1;
            ScanFlickables(Root, px, py, hit, ref order, ref hitOrder, ref top, ref topOrder);
            if (top == null) return;
            if (hit != null && hitOrder > topOrder && !IsAncestor(top, hit))
            {
                // The pressed area is drawn above the innermost Flickable: only Flickables that contain it.
                for (QuillObject o = hit.Parent; o != null; o = o.Parent)
                    if (o is QuillFlickable f && f.Flag("interactive") && Contains(f, px, py)) chain.Add(f);
                return;
            }
            for (QuillObject o = top; o != null; o = o.Parent)
                if (o is QuillFlickable f && f.Flag("interactive") && Contains(f, px, py)) chain.Add(f);
        }

        private static void ScanFlickables(QuillObject o, double px, double py, QuillMouseArea hit,
                                           ref int order, ref int hitOrder, ref QuillFlickable top, ref int topOrder)
        {
            if (o is QuillNonVisual) return;
            int mine = order++;
            if (ReferenceEquals(o, hit)) hitOrder = mine;
            if (o is QuillItem item)
            {
                if (!item.Flag("visible")) return;
                bool inside = Contains(item, px, py);
                if (item.ClipsChildren && !inside) return;
                if (o is QuillFlickable f && inside && f.Flag("interactive")) { top = f; topOrder = mine; }
            }
            for (int i = 0; i < o.Children.Count; i++)
                ScanFlickables(o.Children[i], px, py, hit, ref order, ref hitOrder, ref top, ref topOrder);
        }

        /// <summary>A press: note the Flickables under it; a press on a moving one stops it (and is used up).</summary>
        private bool FlickPress(double px, double py, QuillMouseArea hit)
        {
            _flickDrag = null;
            FindFlickChain(px, py, hit, _flickChain);
            _flickPressX = px; _flickPressY = py;
            bool stopped = false;
            foreach (var f in _flickChain)
            {
                if (f.Flicking && Math.Abs(f.VelX) + Math.Abs(f.VelY) > 150) stopped = true;
                f.Flicking = false; f.VelX = f.VelY = 0; f.HasTarget = false;
            }
            return stopped;
        }

        /// <summary>
        /// The pointer moved while down. Returns true when a Flickable took the drag this move (the
        /// grabbing MouseArea must then be cancelled).
        /// </summary>
        private bool FlickMove(double px, double py, QuillMouseArea grabber)
        {
            if (_flickDrag != null)
            {
                DragTo(_flickDrag, px, py);
                return false;
            }
            if (_flickChain.Count == 0) return false;
            if (grabber != null && (grabber.Flag("preventStealing", false) || grabber.Flag("drag.active", false))) return false;

            double dx = px - _flickPressX, dy = py - _flickPressY;
            foreach (var f in _flickChain)
            {
                f.Axes(out bool ax, out bool ay);
                bool takeY = ay && Math.Abs(dy) > FlickStealDistance && Math.Abs(dy) > Math.Abs(dx);
                bool takeX = ax && Math.Abs(dx) > FlickStealDistance && Math.Abs(dx) >= Math.Abs(dy);
                if (!takeX && !takeY) continue;

                _flickDrag = f;
                f.Dragging = true;
                f.Flicking = false; f.HasTarget = false;
                f.DragPx = px; f.DragPy = py;   // start from here: no jump by the steal distance
                f.DragCX = f.Property("contentX").Number;
                f.DragCY = f.Property("contentY").Number;
                f.Samples.Clear();
                f.Samples.Add((Time, f.DragCX, f.DragCY));
                UpdateMotionState(f);
                return true;
            }
            return false;
        }

        private void DragTo(QuillFlickable f, double px, double py)
        {
            f.Axes(out bool ax, out bool ay);
            bool over = ((int)f.Num("boundsBehavior", 3) & 1) != 0;
            double x = f.DragCX, y = f.DragCY;
            if (ax) x = DragAxis(f.DragCX - (px - f.DragPx), f.MaxX, over);
            if (ay) y = DragAxis(f.DragCY - (py - f.DragPy), f.MaxY, over);
            if (ax) WriteContent(f.Property("contentX"), x);
            if (ay) WriteContent(f.Property("contentY"), y);
            f.Samples.Add((Time, x, y));
            if (f.Samples.Count > 8) f.Samples.RemoveAt(0);
        }

        // Past a bound the content follows the pointer at half speed (rubber band), or stops there.
        private static double DragAxis(double v, double max, bool over)
        {
            if (v < 0) return over ? v * 0.5 : 0;
            if (v > max) return over ? max + (v - max) * 0.5 : max;
            return v;
        }

        /// <summary>The pointer went up: a drag that was moving fast enough becomes a flick.</summary>
        private void FlickRelease()
        {
            var f = _flickDrag;
            _flickDrag = null;
            _flickChain.Clear();
            if (f == null) return;
            f.Dragging = false;

            // Velocity over the last ~100 ms of the drag.
            var s = f.Samples;
            double vx = 0, vy = 0;
            if (s.Count >= 2)
            {
                var last = s[s.Count - 1];
                int k = s.Count - 2;
                while (k > 0 && last.t - s[k].t < 0.1) k--;
                double span = last.t - s[k].t;
                if (span > 1e-4 && Time - last.t < 0.1)
                {
                    vx = (last.x - s[k].x) / span;
                    vy = (last.y - s[k].y) / span;
                }
            }
            double vmax = Math.Max(1, f.Num("maximumFlickVelocity", 2500));
            f.Axes(out bool ax, out bool ay);
            f.VelX = ax ? Clamp(vx, -vmax, vmax) : 0;
            f.VelY = ay ? Clamp(vy, -vmax, vmax) : 0;
            if (Math.Abs(f.VelX) < MinFlickVelocity) f.VelX = 0;
            if (Math.Abs(f.VelY) < MinFlickVelocity) f.VelY = 0;
            f.Flicking = f.VelX != 0 || f.VelY != 0;
            UpdateMotionState(f);
        }

        private readonly List<QuillFlickable> _wheelChain = new List<QuillFlickable>();

        /// <summary>
        /// The wheel. A MouseArea handling <c>onWheel</c> inside the innermost Flickable under the pointer
        /// (or with none) gets it first; otherwise, or if it leaves <c>wheel.accepted</c> false, the
        /// innermost Flickable that can still scroll that way scrolls.
        /// </summary>
        private void DispatchWheel(double px, double py, double wheelX, double wheelY)
        {
            var w = HitTest(px, py, a => a.Handlers.ContainsKey("onWheel") || a.HasWheelListener);
            FindFlickChain(px, py, w, _wheelChain);
            bool areaFirst = w != null && (_wheelChain.Count == 0 || IsAncestor(_wheelChain[0], w));
            if (areaFirst && EmitWheel(w, px, py, wheelX, wheelY)) { _wheelChain.Clear(); return; }

            bool scrolled = false;
            foreach (var f in _wheelChain)
            {
                f.Axes(out bool ax, out bool ay);
                double dx = -wheelX / 120.0 * WheelStep, dy = -wheelY / 120.0 * WheelStep;
                if (!ay && ax && dx == 0) { dx = dy; dy = 0; }   // a horizontal-only list scrolls with the wheel too
                if (!ax) dx = 0;
                if (!ay) dy = 0;
                if ((dx != 0 || dy != 0) && ScrollBy(f, dx, dy)) { scrolled = true; break; }
            }
            _wheelChain.Clear();
            if (!scrolled && !areaFirst && w != null) EmitWheel(w, px, py, wheelX, wheelY);
        }

        // Returns whether the handler accepted the event (the default).
        private static bool EmitWheel(QuillMouseArea w, double px, double py, double wheelX, double wheelY)
        {
            var wheel = new QuillObject { TypeName = "WheelEvent" };
            var delta = new QuillObject { TypeName = "Point" };
            delta.Property("x").SetValue(wheelX);
            delta.Property("y").SetValue(wheelY);
            wheel.Property("angleDelta").SetValue(delta);
            wheel.Property("pixelDelta").SetValue(delta);
            wheel.Property("x").SetValue(px - w.AbsX());
            wheel.Property("y").SetValue(py - w.AbsY());
            wheel.Property("accepted").SetValue(QuillConvert.True);
            w.Emit("onWheel", new object[] { wheel });
            return QuillConvert.ToBool(wheel.FindProperty("accepted")?.Raw);
        }

        // Smoothly scroll by (dx, dy) from wherever the scroll is heading. False if already at that end.
        private static bool ScrollBy(QuillFlickable f, double dx, double dy)
        {
            double fromX = f.HasTarget ? f.TargetX : f.Property("contentX").Number;
            double fromY = f.HasTarget ? f.TargetY : f.Property("contentY").Number;
            double tx = Clamp(fromX + dx, 0, f.MaxX), ty = Clamp(fromY + dy, 0, f.MaxY);
            if (Math.Abs(tx - fromX) < 0.01 && Math.Abs(ty - fromY) < 0.01) return false;
            f.Flicking = false; f.VelX = f.VelY = 0;
            f.TargetX = tx; f.TargetY = ty; f.HasTarget = true;
            return true;
        }

        // ---- Keyboard and focus --------------------------------------------------------------------

        /// <summary>
        /// Keys on a focused Flickable: arrows scroll (passing on at the ends, so navigation continues),
        /// Page Up / Down and Home / End scroll by a page or to the ends. Page keys also reach a
        /// Flickable from any item focused inside it.
        /// </summary>
        private bool FlickKey(QuillFlickable f, int key, bool focused)
        {
            f.Axes(out bool ax, out bool ay);
            double page = Math.Max(KeyStep, (ay ? f.Num("height") : f.Num("width")) * 0.9);
            double dx = 0, dy = 0;
            switch (key)
            {
                case QuillKeys.Key_Up: if (!focused) return false; dy = -KeyStep; break;
                case QuillKeys.Key_Down: if (!focused) return false; dy = KeyStep; break;
                case QuillKeys.Key_Left: if (!focused) return false; dx = -KeyStep; break;
                case QuillKeys.Key_Right: if (!focused) return false; dx = KeyStep; break;
                case QuillKeys.Key_PageUp: if (ay) dy = -page; else dx = -page; break;
                case QuillKeys.Key_PageDown: if (ay) dy = page; else dx = page; break;
                case QuillKeys.Key_Home: if (!focused) return false; dx = -1e9; dy = -1e9; break;
                case QuillKeys.Key_End: if (!focused) return false; dx = ay ? 0 : 1e9; dy = ay ? 1e9 : 0; break;
                default: return false;
            }
            if (!ax) dx = 0;
            if (!ay) dy = 0;
            return (dx != 0 || dy != 0) && ScrollBy(f, dx, dy);
        }

        /// <summary>Scroll every Flickable around <paramref name="item"/> so it shows (with a little margin).</summary>
        private void EnsureVisible(QuillItem item)
        {
            const double margin = 8;
            for (QuillObject o = item.Parent; o != null; o = o.Parent)
            {
                if (!(o is QuillFlickable f)) continue;
                f.Axes(out bool ax, out bool ay);
                // The item's rectangle in content coordinates.
                double ix = item.AbsX() - f.ContentItem.AbsX(), iy = item.AbsY() - f.ContentItem.AbsY();
                double iw = item.Num("width"), ih = item.Num("height");
                double cx = f.HasTarget ? f.TargetX : f.Property("contentX").Number;
                double cy = f.HasTarget ? f.TargetY : f.Property("contentY").Number;
                double vw = f.Num("width"), vh = f.Num("height");
                double tx = cx, ty = cy;
                if (ax)
                {
                    if (ix - margin < cx || iw + 2 * margin > vw) tx = ix - margin;
                    else if (ix + iw + margin > cx + vw) tx = ix + iw + margin - vw;
                }
                if (ay)
                {
                    if (iy - margin < cy || ih + 2 * margin > vh) ty = iy - margin;
                    else if (iy + ih + margin > cy + vh) ty = iy + ih + margin - vh;
                }
                ScrollBy(f, tx - cx, ty - cy);
            }
        }

        // ---- Methods -------------------------------------------------------------------------------

        internal bool CallFlickableMethod(QuillFlickable f, string name, object[] args)
        {
            double A(int i) => args != null && args.Length > i ? QuillConvert.ToDouble(args[i]) : 0;
            switch (name)
            {
                case "flick":
                {
                    // QML's sign: a positive velocity moves the content towards its start (contentY decreases).
                    double vmax = Math.Max(1, f.Num("maximumFlickVelocity", 2500));
                    f.Axes(out bool ax, out bool ay);
                    f.HasTarget = false;
                    f.VelX = ax ? -Clamp(A(0), -vmax, vmax) : 0;
                    f.VelY = ay ? -Clamp(A(1), -vmax, vmax) : 0;
                    f.Flicking = f.VelX != 0 || f.VelY != 0;
                    UpdateMotionState(f);
                    return true;
                }
                case "cancelFlick":
                    f.Flicking = false; f.VelX = f.VelY = 0; f.HasTarget = false;
                    UpdateMotionState(f);
                    return true;
                case "returnToBounds":
                    f.Flicking = false; f.VelX = f.VelY = 0;
                    f.TargetX = Clamp(f.Property("contentX").Number, 0, f.MaxX);
                    f.TargetY = Clamp(f.Property("contentY").Number, 0, f.MaxY);
                    f.HasTarget = true;
                    return true;
            }
            return false;
        }
    }
}
