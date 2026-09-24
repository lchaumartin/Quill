// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Row / Column / Grid layout, expressed through the reactive binding system. A positioner
    /// installs bindings on each child's x/y so positions recompute automatically when a child's
    /// size or the spacing changes, and binds its own width/height to its content extent (unless the
    /// document already set/anchored them).
    /// </summary>
    public static class Positioners
    {
        public static void Setup(QuillEngine engine, QuillItem item)
        {
            switch (item.TypeName)
            {
                case "Row": Row(engine, item); break;
                case "Column": Column(engine, item); break;
                case "Grid": Grid(engine, item); break;
            }
        }

        // Children that actually participate in layout (skip animations etc.).
        private static List<QuillItem> Kids(QuillItem p)
        {
            var list = new List<QuillItem>();
            for (int i = 0; i < p.Children.Count; i++)
                if (p.Children[i] is QuillItem k && !(p.Children[i] is QuillAnimation))
                    list.Add(k);
            return list;
        }

        private static double Spacing(QuillItem p) => QuillConvert.ToDouble(p.Property("spacing").Get());
        private static double W(QuillItem k) => QuillConvert.ToDouble(k.Property("width").Get());
        private static double H(QuillItem k) => QuillConvert.ToDouble(k.Property("height").Get());

        private static void BindIfFree(QuillEngine e, QuillProperty prop, System.Func<object> fn)
        {
            if (!prop.HasBinding) prop.SetBinding(new Binding(e, fn));
        }

        private static void Row(QuillEngine engine, QuillItem p)
        {
            var kids = Kids(p);
            for (int i = 0; i < kids.Count; i++)
            {
                int idx = i;
                kids[idx].Property("x").SetBinding(new Binding(engine, () =>
                {
                    double sp = Spacing(p), acc = 0;
                    for (int j = 0; j < idx; j++) acc += W(kids[j]) + sp;
                    return acc;
                }));
            }

            BindIfFree(engine, p.Property("width"), () =>
            {
                double sp = Spacing(p), acc = 0;
                for (int j = 0; j < kids.Count; j++) acc += W(kids[j]);
                return acc + sp * System.Math.Max(0, kids.Count - 1);
            });
            BindIfFree(engine, p.Property("height"), () =>
            {
                double m = 0;
                for (int j = 0; j < kids.Count; j++) m = System.Math.Max(m, H(kids[j]));
                return m;
            });
        }

        private static void Column(QuillEngine engine, QuillItem p)
        {
            var kids = Kids(p);
            for (int i = 0; i < kids.Count; i++)
            {
                int idx = i;
                kids[idx].Property("y").SetBinding(new Binding(engine, () =>
                {
                    double sp = Spacing(p), acc = 0;
                    for (int j = 0; j < idx; j++) acc += H(kids[j]) + sp;
                    return acc;
                }));
            }

            BindIfFree(engine, p.Property("height"), () =>
            {
                double sp = Spacing(p), acc = 0;
                for (int j = 0; j < kids.Count; j++) acc += H(kids[j]);
                return acc + sp * System.Math.Max(0, kids.Count - 1);
            });
            BindIfFree(engine, p.Property("width"), () =>
            {
                double m = 0;
                for (int j = 0; j < kids.Count; j++) m = System.Math.Max(m, W(kids[j]));
                return m;
            });
        }

        private static void Grid(QuillEngine engine, QuillItem p)
        {
            var kids = Kids(p);
            int cols = System.Math.Max(1, (int)QuillConvert.ToDouble(p.Property("columns").Raw));

            double ColSpacing() => p.HasProperty("columnSpacing")
                ? QuillConvert.ToDouble(p.Property("columnSpacing").Get()) : Spacing(p);
            double RowSpacing() => p.HasProperty("rowSpacing")
                ? QuillConvert.ToDouble(p.Property("rowSpacing").Get()) : Spacing(p);

            // Max width of column c / max height of row r (reactive reads).
            double ColW(int c)
            {
                double m = 0;
                for (int j = c; j < kids.Count; j += cols) m = System.Math.Max(m, W(kids[j]));
                return m;
            }
            double RowH(int r)
            {
                double m = 0;
                for (int j = r * cols; j < (r + 1) * cols && j < kids.Count; j++) m = System.Math.Max(m, H(kids[j]));
                return m;
            }

            for (int i = 0; i < kids.Count; i++)
            {
                int col = i % cols, row = i / cols;
                kids[i].Property("x").SetBinding(new Binding(engine, () =>
                {
                    double acc = 0, sp = ColSpacing();
                    for (int c = 0; c < col; c++) acc += ColW(c) + sp;
                    return acc;
                }));
                kids[i].Property("y").SetBinding(new Binding(engine, () =>
                {
                    double acc = 0, sp = RowSpacing();
                    for (int r = 0; r < row; r++) acc += RowH(r) + sp;
                    return acc;
                }));
            }

            int numRows = (kids.Count + cols - 1) / cols;
            int numCols = System.Math.Min(cols, System.Math.Max(1, kids.Count));

            BindIfFree(engine, p.Property("width"), () =>
            {
                double acc = 0, sp = ColSpacing();
                for (int c = 0; c < numCols; c++) acc += ColW(c);
                return acc + sp * System.Math.Max(0, numCols - 1);
            });
            BindIfFree(engine, p.Property("height"), () =>
            {
                double acc = 0, sp = RowSpacing();
                for (int r = 0; r < numRows; r++) acc += RowH(r);
                return acc + sp * System.Math.Max(0, numRows - 1);
            });
        }
    }
}
