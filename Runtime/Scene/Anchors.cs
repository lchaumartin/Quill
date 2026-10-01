// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Quill-style anchors, expressed entirely through the reactive binding system.
    ///
    /// Every item exposes six *anchor-line* properties in absolute surface coordinates:
    /// <c>left, right, top, bottom, horizontalCenter, verticalCenter</c>. Anchoring is then just a
    /// binding onto another item's line, e.g. <c>anchors.left: bar.right</c>. Because anchor lines
    /// are absolute, anchoring to a sibling and anchoring to <c>parent</c> use the exact same math —
    /// no special cases.
    ///
    /// The inputs (<c>anchors.left</c>, <c>anchors.fill</c>, margins, …) are ordinary properties set
    /// by the document. <see cref="Resolve"/> reads which ones are present and installs the
    /// corresponding bindings on x / y / width / height, so layout reacts to anything those inputs
    /// depend on (window size, sibling geometry, model values).
    /// </summary>
    public static class Anchors
    {
        // ---- Anchor-line properties (absolute coords) ------------------------------------------

        public static void SetupLines(QuillEngine engine, QuillItem it)
        {
            var parent = it.Parent as QuillItem;

            var x = it.Property("x");
            var y = it.Property("y");
            var w = it.Property("width");
            var h = it.Property("height");

            var left = it.Property("left");
            var right = it.Property("right");
            var top = it.Property("top");
            var bottom = it.Property("bottom");
            var hCenter = it.Property("horizontalCenter");
            var vCenter = it.Property("verticalCenter");

            var pLeft = parent?.Property("left");
            var pTop = parent?.Property("top");

            left.SetBinding(Binding.Numeric(engine,
                () => (pLeft != null ? pLeft.GetNumber() : 0.0) + x.GetNumber()));
            top.SetBinding(Binding.Numeric(engine,
                () => (pTop != null ? pTop.GetNumber() : 0.0) + y.GetNumber()));
            right.SetBinding(Binding.Numeric(engine,
                () => left.GetNumber() + w.GetNumber()));
            bottom.SetBinding(Binding.Numeric(engine,
                () => top.GetNumber() + h.GetNumber()));
            hCenter.SetBinding(Binding.Numeric(engine,
                () => left.GetNumber() + w.GetNumber() * 0.5));
            vCenter.SetBinding(Binding.Numeric(engine,
                () => top.GetNumber() + h.GetNumber() * 0.5));
        }

        // ---- Resolve x/y/width/height from the declared anchors --------------------------------

        private static readonly string[] InputNames =
        {
            "fill", "centerIn",
            "left", "right", "top", "bottom", "horizontalCenter", "verticalCenter"
        };

        public static void Resolve(QuillEngine engine, QuillItem it)
        {
            bool any = false;
            for (int i = 0; i < InputNames.Length; i++)
                if (it.HasProperty("anchors." + InputNames[i])) { any = true; break; }
            if (!any) return;

            var ctx = new Ctx(it);
            ResolveHorizontal(engine, ctx);
            ResolveVertical(engine, ctx);
        }

        private static void ResolveHorizontal(QuillEngine engine, Ctx c)
        {
            var it = c.It;
            var px = it.Property("x");
            var pw = it.Property("width");

            if (c.Has("fill"))
            {
                px.SetBinding(Binding.Numeric(engine, () => c.Line(c.Obj("fill"), "left") + c.LM() - c.PLeft()));
                pw.SetBinding(Binding.Numeric(engine, () =>
                    c.Line(c.Obj("fill"), "right") - c.Line(c.Obj("fill"), "left") - c.LM() - c.RM()));
                return;
            }
            if (c.Has("centerIn"))
            {
                px.SetBinding(Binding.Numeric(engine, () =>
                    c.Line(c.Obj("centerIn"), "horizontalCenter") + c.HCO() - c.W() * 0.5 - c.PLeft()));
                return;
            }

            bool L = c.Has("left"), R = c.Has("right"), HC = c.Has("horizontalCenter");
            if (L && R)
            {
                px.SetBinding(Binding.Numeric(engine, () => c.Val("left") + c.LM() - c.PLeft()));
                pw.SetBinding(Binding.Numeric(engine, () => (c.Val("right") - c.RM()) - (c.Val("left") + c.LM())));
            }
            else if (L)
            {
                px.SetBinding(Binding.Numeric(engine, () => c.Val("left") + c.LM() - c.PLeft()));
            }
            else if (R)
            {
                px.SetBinding(Binding.Numeric(engine, () => c.Val("right") - c.RM() - c.W() - c.PLeft()));
            }
            else if (HC)
            {
                px.SetBinding(Binding.Numeric(engine, () => c.Val("horizontalCenter") + c.HCO() - c.W() * 0.5 - c.PLeft()));
            }
        }

        private static void ResolveVertical(QuillEngine engine, Ctx c)
        {
            var it = c.It;
            var py = it.Property("y");
            var ph = it.Property("height");

            if (c.Has("fill"))
            {
                py.SetBinding(Binding.Numeric(engine, () => c.Line(c.Obj("fill"), "top") + c.TM() - c.PTop()));
                ph.SetBinding(Binding.Numeric(engine, () =>
                    c.Line(c.Obj("fill"), "bottom") - c.Line(c.Obj("fill"), "top") - c.TM() - c.BM()));
                return;
            }
            if (c.Has("centerIn"))
            {
                py.SetBinding(Binding.Numeric(engine, () =>
                    c.Line(c.Obj("centerIn"), "verticalCenter") + c.VCO() - c.H() * 0.5 - c.PTop()));
                return;
            }

            bool T = c.Has("top"), B = c.Has("bottom"), VC = c.Has("verticalCenter");
            if (T && B)
            {
                py.SetBinding(Binding.Numeric(engine, () => c.Val("top") + c.TM() - c.PTop()));
                ph.SetBinding(Binding.Numeric(engine, () => (c.Val("bottom") - c.BM()) - (c.Val("top") + c.TM())));
            }
            else if (T)
            {
                py.SetBinding(Binding.Numeric(engine, () => c.Val("top") + c.TM() - c.PTop()));
            }
            else if (B)
            {
                py.SetBinding(Binding.Numeric(engine, () => c.Val("bottom") - c.BM() - c.H() - c.PTop()));
            }
            else if (VC)
            {
                py.SetBinding(Binding.Numeric(engine, () => c.Val("verticalCenter") + c.VCO() - c.H() * 0.5 - c.PTop()));
            }
        }

        /// <summary>
        /// Tracked accessors for an item's anchor inputs. Every read goes through
        /// <see cref="QuillProperty.Get"/> so the enclosing binding subscribes to it.
        /// </summary>
        private sealed class Ctx
        {
            public readonly QuillItem It;
            private readonly QuillItem _parent;

            public Ctx(QuillItem it) { It = it; _parent = it.Parent as QuillItem; }

            public bool Has(string n) => It.HasProperty(Full(n));
            public double Val(string n) => It.Property(Full(n)).GetNumber();
            public QuillItem Obj(string n) => Has(n) ? It.Property(Full(n)).Get() as QuillItem : null;
            public double Line(QuillItem t, string line) => t == null ? 0.0 : t.Property(line).GetNumber();

            public double W() => It.Property("width").GetNumber();
            public double H() => It.Property("height").GetNumber();
            public double PLeft() => _parent == null ? 0.0 : _parent.Property("left").GetNumber();
            public double PTop() => _parent == null ? 0.0 : _parent.Property("top").GetNumber();

            // "anchors." + n, built once per name (these run on every anchor re-evaluation).
            private static readonly Dictionary<string, string> s_Full = new Dictionary<string, string>();
            private static string Full(string n)
            {
                if (!s_Full.TryGetValue(n, out var f)) s_Full[n] = f = "anchors." + n;
                return f;
            }

            public double LM() => Margin("leftMargin");
            public double RM() => Margin("rightMargin");
            public double TM() => Margin("topMargin");
            public double BM() => Margin("bottomMargin");
            public double HCO() => Has("horizontalCenterOffset") ? Val("horizontalCenterOffset") : 0.0;
            public double VCO() => Has("verticalCenterOffset") ? Val("verticalCenterOffset") : 0.0;

            private double Margin(string side)
                => Has(side) ? Val(side) : Has("margins") ? Val("margins") : 0.0;
        }
    }
}
