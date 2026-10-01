// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using Quill.Parsing;

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
                case "random": return Random01();
                // Not in JS Math, but handy: clamp(value, lo, hi).
                case "clamp": return Math.Min(Math.Max(A(0), A(1)), A(2));
                default:
                    UnityEngine.Debug.LogWarning($"[Quill] Unknown function 'Math.{fn}'.");
                    return 0.0;
            }
        }

        // Kept out of Call so that method has no engine call in it (and can run outside Unity, in tests).
        private static double Random01() => UnityEngine.Random.value;

        /// <summary>Functions with a fixed number of numeric arguments (see <see cref="TryCall"/>).</summary>
        private static int Arity(string fn)
        {
            switch (fn)
            {
                case "random": return 0;
                case "abs": case "sign": case "floor": case "ceil": case "round": case "trunc": case "sqrt":
                case "cbrt": case "exp": case "log": case "log2": case "log10": case "sin": case "cos":
                case "tan": case "asin": case "acos": case "atan":
                    return 1;
                case "pow": case "atan2": case "hypot": return 2;
                case "clamp": return 3;
                case "min": case "max": return int.MaxValue;   // any number
                default: return -1;
            }
        }

        /// <summary>True for the functions <see cref="TryCall"/> evaluates (all of them return a number).</summary>
        public static bool IsNumeric(string fn) => Arity(fn) >= 0;

        /// <summary>
        /// <c>Math.fn(args)</c> evaluated straight from the call's argument nodes, with no argument
        /// array and no boxing. Same results as <see cref="Call"/>; false (nothing evaluated) for
        /// unknown functions or extra arguments, which then take the general path.
        /// </summary>
        public static bool TryCall(string fn, CallNode n, EvalContext ctx, out double r)
        {
            int arity = Arity(fn);
            int count = n.Args.Count;
            if (arity < 0 || (arity != int.MaxValue && count > arity)) { r = 0; return false; }

            double a = n.ArgNumber(ctx, 0);
            switch (fn)
            {
                case "abs": r = Math.Abs(a); return true;
                case "sign": r = Math.Sign(a); return true;
                case "floor": r = Math.Floor(a); return true;
                case "ceil": r = Math.Ceiling(a); return true;
                case "round": r = Math.Round(a, MidpointRounding.AwayFromZero); return true;
                case "trunc": r = Math.Truncate(a); return true;
                case "sqrt": r = Math.Sqrt(a); return true;
                case "cbrt": r = Math.Sign(a) * Math.Pow(Math.Abs(a), 1.0 / 3.0); return true;
                case "exp": r = Math.Exp(a); return true;
                case "log": r = Math.Log(a); return true;
                case "log2": r = Math.Log(a, 2.0); return true;
                case "log10": r = Math.Log10(a); return true;
                case "sin": r = Math.Sin(a); return true;
                case "cos": r = Math.Cos(a); return true;
                case "tan": r = Math.Tan(a); return true;
                case "asin": r = Math.Asin(a); return true;
                case "acos": r = Math.Acos(a); return true;
                case "atan": r = Math.Atan(a); return true;
                case "random": r = Random01(); return true;
            }

            double b = n.ArgNumber(ctx, 1);
            switch (fn)
            {
                case "pow": r = Math.Pow(a, b); return true;
                case "atan2": r = Math.Atan2(a, b); return true;
                case "hypot": r = Math.Sqrt(a * a + b * b); return true;
                case "clamp": r = Math.Min(Math.Max(a, b), n.ArgNumber(ctx, 2)); return true;
                case "min":
                case "max":
                {
                    bool wantMin = fn == "min";
                    if (count == 0) { r = 0; return true; }
                    double acc = a;
                    if (count > 1) acc = wantMin ? Math.Min(acc, b) : Math.Max(acc, b);
                    for (int i = 2; i < count; i++)
                    {
                        double v = n.ArgNumber(ctx, i);
                        acc = wantMin ? Math.Min(acc, v) : Math.Max(acc, v);
                    }
                    r = acc;
                    return true;
                }
            }
            r = 0;
            return false;
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
