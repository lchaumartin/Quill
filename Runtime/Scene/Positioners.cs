// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Row / Column / Grid / Flow layout, expressed through the reactive binding system. Each
    /// positioner owns one hidden <c>__layout</c> binding that solves the whole layout in a single
    /// pass (reading every child's size and visibility, the spacing and padding), and each child's
    /// x/y is a tiny binding that picks its slot from it — so a size change costs O(children), and
    /// children added or removed at runtime (Repeater) just join the solve.
    ///
    /// Hidden children (`visible: false`) take no space. The positioner sizes itself to its content
    /// unless the document sets its width/height; a Flow with a set width wraps its children.
    /// Padding: <c>padding</c> or per side <c>leftPadding/rightPadding/topPadding/bottomPadding</c>.
    /// </summary>
    public static class Positioners
    {
        internal sealed class Info
        {
            public bool Initialized, OwnsWidth, OwnsHeight;
            public readonly HashSet<QuillItem> Laid = new HashSet<QuillItem>();
        }

        internal sealed class Layout
        {
            public readonly Dictionary<QuillItem, (double x, double y)> Pos = new Dictionary<QuillItem, (double, double)>();
            public double Width, Height;
        }

        public static void Setup(QuillEngine engine, QuillPositioner p)
        {
            var info = engine.PositionerInfo(p);
            var layout = p.Property("__layout");

            if (!info.Initialized)
            {
                info.Initialized = true;
                p.Property("__rev").SetValue(0.0);
                info.OwnsWidth = !p.Property("width").HasBinding;
                info.OwnsHeight = !p.Property("height").HasBinding;

                layout.SetBinding(new Binding(engine, () => Solve(p, info)));
                if (info.OwnsWidth)
                    p.Property("width").SetBinding(new Binding(engine, () => (layout.Get() as Layout)?.Width ?? 0.0));
                if (info.OwnsHeight)
                    p.Property("height").SetBinding(new Binding(engine, () => (layout.Get() as Layout)?.Height ?? 0.0));
            }

            bool setX = p.TypeName != "Column", setY = p.TypeName != "Row";
            foreach (var kid in Kids(p))
            {
                if (!info.Laid.Add(kid)) continue;
                var k = kid;
                if (setX) k.Property("x").SetBinding(new Binding(engine, () => Pick(layout.Get(), k, true)));
                if (setY) k.Property("y").SetBinding(new Binding(engine, () => Pick(layout.Get(), k, false)));
            }
        }

        /// <summary>Children were added or removed at runtime: re-solve and lay out the newcomers.</summary>
        public static void ChildrenChanged(QuillEngine engine, QuillPositioner p)
        {
            var info = engine.PositionerInfo(p);
            info.Laid.RemoveWhere(k => k.Parent != p);
            var rev = p.Property("__rev");
            rev.SetValue(QuillConvert.ToDouble(rev.Raw) + 1);
            Setup(engine, p);
        }

        private static object Pick(object layout, QuillItem kid, bool x)
        {
            if (layout is Layout l && l.Pos.TryGetValue(kid, out var pos)) return x ? pos.x : pos.y;
            return kid.Property(x ? "x" : "y").Raw;   // hidden child: stay where it is
        }

        // Children that take part in layout (not animations, timers, states, repeaters…).
        private static List<QuillItem> Kids(QuillItem p)
        {
            var list = new List<QuillItem>();
            for (int i = 0; i < p.Children.Count; i++)
                if (p.Children[i] is QuillItem k && !(k is QuillNonVisual))
                    list.Add(k);
            return list;
        }

        private static double Get(QuillItem o, string name) => QuillConvert.ToDouble(o.Property(name).Get());

        private static double Pad(QuillItem p, string side)
            => p.HasProperty(side) ? Get(p, side) : Get(p, "padding");

        private static Layout Solve(QuillPositioner p, Info info)
        {
            p.Property("__rev").Get();   // re-solve when children are added/removed

            var result = new Layout();
            var vis = new List<QuillItem>();
            foreach (var k in Kids(p))
                if (QuillConvert.ToBool(k.Property("visible").Get())) vis.Add(k);

            double sp = Get(p, "spacing");
            double lp = Pad(p, "leftPadding"), rp = Pad(p, "rightPadding");
            double tp = Pad(p, "topPadding"), bp = Pad(p, "bottomPadding");

            switch (p.TypeName)
            {
                case "Row":
                {
                    double x = lp, maxH = 0;
                    for (int i = 0; i < vis.Count; i++)
                    {
                        if (i > 0) x += sp;
                        result.Pos[vis[i]] = (x, 0);
                        x += Get(vis[i], "width");
                        maxH = System.Math.Max(maxH, Get(vis[i], "height"));
                    }
                    result.Width = x + rp;
                    result.Height = maxH + tp + bp;
                    break;
                }

                case "Column":
                {
                    double y = tp, maxW = 0;
                    for (int i = 0; i < vis.Count; i++)
                    {
                        if (i > 0) y += sp;
                        result.Pos[vis[i]] = (0, y);
                        y += Get(vis[i], "height");
                        maxW = System.Math.Max(maxW, Get(vis[i], "width"));
                    }
                    result.Height = y + bp;
                    result.Width = maxW + lp + rp;
                    break;
                }

                case "Grid":
                {
                    int cols = System.Math.Max(1, (int)Get(p, "columns"));
                    double colSp = p.HasProperty("columnSpacing") ? Get(p, "columnSpacing") : sp;
                    double rowSp = p.HasProperty("rowSpacing") ? Get(p, "rowSpacing") : sp;
                    int n = vis.Count;
                    int numCols = System.Math.Min(cols, System.Math.Max(1, n));
                    int numRows = (n + cols - 1) / cols;
                    var colW = new double[numCols];
                    var rowH = new double[System.Math.Max(1, numRows)];
                    for (int i = 0; i < n; i++)
                    {
                        colW[i % cols] = System.Math.Max(colW[i % cols], Get(vis[i], "width"));
                        rowH[i / cols] = System.Math.Max(rowH[i / cols], Get(vis[i], "height"));
                    }
                    var colX = new double[numCols];
                    var rowY = new double[System.Math.Max(1, numRows)];
                    double acc = lp;
                    for (int c = 0; c < numCols; c++) { colX[c] = acc; acc += colW[c] + colSp; }
                    result.Width = (n == 0 ? lp : acc - colSp) + rp;
                    acc = tp;
                    for (int r = 0; r < numRows; r++) { rowY[r] = acc; acc += rowH[r] + rowSp; }
                    result.Height = (numRows == 0 ? tp : acc - rowSp) + bp;
                    for (int i = 0; i < n; i++) result.Pos[vis[i]] = (colX[i % cols], rowY[i / cols]);
                    break;
                }

                case "Flow":
                {
                    double limit = info.OwnsWidth ? double.PositiveInfinity : Get(p, "width") - lp - rp;
                    double x = lp, y = tp, lineH = 0, maxRight = lp;
                    for (int i = 0; i < vis.Count; i++)
                    {
                        double w = Get(vis[i], "width"), h = Get(vis[i], "height");
                        if (x > lp && x - lp + w > limit)
                        {
                            y += lineH + sp;
                            x = lp;
                            lineH = 0;
                        }
                        result.Pos[vis[i]] = (x, y);
                        maxRight = System.Math.Max(maxRight, x + w);
                        x += w + sp;
                        lineH = System.Math.Max(lineH, h);
                    }
                    result.Width = maxRight + rp;
                    result.Height = y + lineH + bp;
                    break;
                }
            }
            return result;
        }
    }
}
