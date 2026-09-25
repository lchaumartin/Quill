// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;

namespace Quill
{
    // Pointer input: hover, press/release/click, double-click, press-and-hold, drag.target, wheel.
    public sealed partial class QuillEngine
    {
        private const double DoubleClickSeconds = 0.4;
        private const double PressAndHoldSeconds = 0.8;

        private QuillMouseArea _hovered, _grabber, _lastClickArea;
        private bool _wasDown, _suppressClick;
        private double _ptrX = double.NaN, _ptrY = double.NaN;
        private double _lastClickTime = double.NegativeInfinity;

        private void ProcessPointer(double px, double py, bool down, double wheelX, double wheelY)
        {
            bool moved = !double.IsNaN(_ptrX) && (px != _ptrX || py != _ptrY);
            _ptrX = px; _ptrY = py;

            var hit = HitTest(px, py, null);

            // Keep area-local mouseX/mouseY fresh on whatever area receives events this frame
            // (the area under the pointer, and the press grabber if we're dragging).
            if (hit != null) SetLocalPointer(hit, px, py);
            if (_grabber != null && _grabber != hit) SetLocalPointer(_grabber, px, py);

            // Hover enter/exit.
            if (hit != _hovered)
            {
                if (_hovered != null)
                {
                    _hovered.Property("containsMouse").SetValue(false);
                    if (_hovered.HoverEnabled) _hovered.Emit("onExited");
                }
                _hovered = hit;
                if (_hovered != null)
                {
                    _hovered.Property("containsMouse").SetValue(true);
                    if (_hovered.HoverEnabled) _hovered.Emit("onEntered");
                }
            }

            // Press / release / click (click = press and release over the same area).
            if (down && !_wasDown)
            {
                _grabber = hit;
                _suppressClick = false;
                if (_grabber != null)
                {
                    var g = _grabber;
                    g.PressTime = Time;
                    g.HoldFired = false;
                    g.DragStartPx = px;
                    g.DragStartPy = py;
                    if (g.FindProperty("drag.target")?.Raw is QuillItem dragged)
                    {
                        g.DragStartX = QuillConvert.ToDouble(dragged.Property("x").Raw);
                        g.DragStartY = QuillConvert.ToDouble(dragged.Property("y").Raw);
                    }
                    g.Property("pressed").SetValue(true);
                    g.Emit("onPressed", MouseArgs(g, px, py));

                    // A second press soon after a click on the same area is a double-click.
                    if (g == _lastClickArea && Time - _lastClickTime <= DoubleClickSeconds
                        && g.Handlers.ContainsKey("onDoubleClicked"))
                    {
                        g.Emit("onDoubleClicked", MouseArgs(g, px, py));
                        _suppressClick = true;
                        _lastClickArea = null;
                    }
                }
            }
            else if (!down && _wasDown)
            {
                if (_grabber != null)
                {
                    var g = _grabber;
                    bool dragged = g.Flag("drag.active", false);
                    if (dragged) g.Property("drag.active").SetValue(false);
                    g.Property("pressed").SetValue(false);
                    g.Emit("onReleased", MouseArgs(g, px, py));
                    if (hit == g && !_suppressClick && !g.HoldFired && !dragged)
                    {
                        g.Emit("onClicked", MouseArgs(g, px, py));
                        _lastClickArea = g;
                        _lastClickTime = Time;
                    }
                    _grabber = null;
                }
            }

            if (down && _grabber != null)
            {
                if (moved) Drag(_grabber, px, py);

                // Press and hold: only when someone listens, so plain clicks are unaffected.
                var g = _grabber;
                if (g != null && !g.HoldFired && Time - g.PressTime >= PressAndHoldSeconds
                    && g.Handlers.ContainsKey("onPressAndHold") && !g.Flag("drag.active", false)
                    && Math.Abs(px - g.DragStartPx) + Math.Abs(py - g.DragStartPy) < 8)
                {
                    g.HoldFired = true;
                    g.Emit("onPressAndHold", MouseArgs(g, px, py));
                }
            }

            if (moved)
            {
                if (_grabber != null) _grabber.Emit("onPositionChanged", MouseArgs(_grabber, px, py));
                else if (_hovered != null && _hovered.HoverEnabled) _hovered.Emit("onPositionChanged", MouseArgs(_hovered, px, py));
            }

            // Wheel: the topmost area under the pointer that handles it.
            if (wheelX != 0 || wheelY != 0)
            {
                var w = HitTest(px, py, a => a.Handlers.ContainsKey("onWheel") || a.HasWheelListener);
                if (w != null)
                {
                    var wheel = new QuillObject { TypeName = "WheelEvent" };
                    var delta = new QuillObject { TypeName = "Point" };
                    delta.Property("x").SetValue(wheelX);
                    delta.Property("y").SetValue(wheelY);
                    wheel.Property("angleDelta").SetValue(delta);
                    wheel.Property("pixelDelta").SetValue(delta);
                    wheel.Property("x").SetValue(px - w.AbsX());
                    wheel.Property("y").SetValue(py - w.AbsY());
                    wheel.Property("accepted").SetValue(true);
                    w.Emit("onWheel", new object[] { wheel });
                }
            }

            _wasDown = down;
        }

        private static object[] MouseArgs(QuillMouseArea a, double px, double py)
        {
            var m = new QuillObject { TypeName = "MouseEvent" };
            m.Property("x").SetValue(px - a.AbsX());
            m.Property("y").SetValue(py - a.AbsY());
            m.Property("button").SetValue(1.0);    // Qt.LeftButton
            m.Property("buttons").SetValue(1.0);
            m.Property("accepted").SetValue(true);
            m.Property("wasHeld").SetValue(a.HoldFired);
            return new object[] { m };
        }

        private static void Drag(QuillMouseArea g, double px, double py)
        {
            if (!(g.FindProperty("drag.target")?.Raw is QuillItem target)) return;

            double dx = px - g.DragStartPx, dy = py - g.DragStartPy;
            if (!g.Flag("drag.active", false))
            {
                if (Math.Sqrt(dx * dx + dy * dy) < g.Num("drag.threshold", 4)) return;
                g.Property("drag.active").SetValue(true);
            }

            int axis = (int)g.Num("drag.axis", 3);
            if ((axis & 1) != 0)
                target.Property("x").SetValue(Clamp(g.DragStartX + dx, g.Num("drag.minimumX", -1e9f), g.Num("drag.maximumX", 1e9f)));
            if ((axis & 2) != 0)
                target.Property("y").SetValue(Clamp(g.DragStartY + dy, g.Num("drag.minimumY", -1e9f), g.Num("drag.maximumY", 1e9f)));
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        private static void SetLocalPointer(QuillMouseArea a, double px, double py)
        {
            a.Property("mouseX").SetValue(px - a.AbsX());
            a.Property("mouseY").SetValue(py - a.AbsY());
        }

        // Topmost enabled, visible MouseArea containing the point (later in tree order = on top).
        private QuillMouseArea HitTest(double px, double py, Func<QuillMouseArea, bool> filter)
            => Root == null ? null : HitRecursive(Root, px, py, filter);

        private static QuillMouseArea HitRecursive(QuillObject o, double px, double py, Func<QuillMouseArea, bool> filter)
        {
            if (o is QuillNonVisual) return null;
            if (o is QuillItem item && !item.Flag("visible")) return null;

            for (int i = o.Children.Count - 1; i >= 0; i--)
            {
                var r = HitRecursive(o.Children[i], px, py, filter);
                if (r != null) return r;
            }

            if (o is QuillMouseArea a && a.Enabled && (filter == null || filter(a)))
            {
                float ax = a.AbsX(), ay = a.AbsY(), aw = a.Num("width"), ah = a.Num("height");
                if (px >= ax && px <= ax + aw && py >= ay && py <= ay + ah) return a;
            }
            return null;
        }
    }
}
