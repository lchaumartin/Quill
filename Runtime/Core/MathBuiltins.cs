// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;

namespace Quill
{
    /// <summary>
    /// JavaScript-style <c>Math.*</c> functions usable from Quill expressions, e.g.
    /// <c>width: Math.max(40, parent.width * 0.2)</c> or <c>x: Math.abs(value)</c>.
    /// Constants <c>Math.PI</c> / <c>Math.E</c> are registered as a pseudo-object by the engine.
    /// </summary>
    public static class MathBuiltins
    {
        public static object Call(string fn, object[] args)
        {
            double A(int i) => i < args.Length ? QuillConvert.ToDouble(args[i]) : 0.0;

            switch (fn)
            {
                case "abs": return Math.Abs(A(0));
                case "sign": return (double)Math.Sign(A(0));
                case "floor": return Math.Floor(A(0));
                case "ceil": return Math.Ceiling(A(0));
                case "round": return Math.Round(A(0), MidpointRounding.AwayFromZero);
                case "trunc": return Math.Truncate(A(0));
                case "sqrt": return Math.Sqrt(A(0));
                case "cbrt": { double x = A(0); return Math.Sign(x) * Math.Pow(Math.Abs(x), 1.0 / 3.0); }
                case "pow": return Math.Pow(A(0), A(1));
                case "exp": return Math.Exp(A(0));
                case "log": return Math.Log(A(0));
                case "log2": return Math.Log(A(0), 2.0);
                case "log10": return Math.Log10(A(0));
                case "sin": return Math.Sin(A(0));
                case "cos": return Math.Cos(A(0));
                case "tan": return Math.Tan(A(0));
                case "asin": return Math.Asin(A(0));
                case "acos": return Math.Acos(A(0));
                case "atan": return Math.Atan(A(0));
                case "atan2": return Math.Atan2(A(0), A(1));
                case "hypot": return Math.Sqrt(A(0) * A(0) + A(1) * A(1));
                case "min": return MinMax(args, true);
                case "max": return MinMax(args, false);
                case "random": return (double)UnityEngine.Random.value;
                // Not in JS Math, but handy: clamp(value, lo, hi).
                case "clamp": return Math.Min(Math.Max(A(0), A(1)), A(2));
                default:
                    UnityEngine.Debug.LogWarning($"[Quill] Unknown function 'Math.{fn}'.");
                    return 0.0;
            }
        }

        private static double MinMax(object[] args, bool wantMin)
        {
            if (args.Length == 0) return 0.0;
            double acc = QuillConvert.ToDouble(args[0]);
            for (int i = 1; i < args.Length; i++)
            {
                double v = QuillConvert.ToDouble(args[i]);
                acc = wantMin ? Math.Min(acc, v) : Math.Max(acc, v);
            }
            return acc;
        }
    }
}
