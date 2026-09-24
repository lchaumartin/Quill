// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;

namespace Quill
{
    /// <summary>
    /// Drives <see cref="QuillAnimation"/> elements: each frame it advances elapsed time, applies an
    /// easing curve, and writes the interpolated value onto the target property. The target is either
    /// <c>parent.&lt;onProperty&gt;</c> (from `NumberAnimation on x`) or an explicit
    /// <c>target</c> + <c>property</c>.
    /// </summary>
    public static class Animations
    {
        /// <param name="dtSeconds">Frame delta time in seconds.</param>
        public static void Advance(QuillAnimation a, double dtSeconds)
        {
            if (!QuillConvert.ToBool(a.Property("running").Raw)) return;

            var target = ResolveTarget(a, out string propName);
            if (target == null || string.IsNullOrEmpty(propName)) return;

            double duration = QuillConvert.ToDouble(a.Property("duration").Raw);
            if (duration <= 0) duration = 1;
            int loops = (int)QuillConvert.ToDouble(a.Property("loops").Raw);

            a.Elapsed += dtSeconds * 1000.0;

            double t = a.Elapsed / duration;
            bool finishedCycle = t >= 1.0;
            if (t < 0) t = 0; else if (t > 1) t = 1;

            double from = QuillConvert.ToDouble(a.Property("from").Raw);
            double to = QuillConvert.ToDouble(a.Property("to").Raw);
            double eased = Ease(QuillConvert.ToStr(a.Property("easing.type").Raw), t);
            double value = from + (to - from) * eased;

            target.Property(propName).SetValue(value);

            if (finishedCycle)
            {
                a.LoopsDone++;
                bool infinite = loops < 0;
                if (!infinite && a.LoopsDone >= loops)
                    a.Property("running").SetValue(false);   // settle at `to`
                else
                    a.Elapsed -= duration;                   // next cycle (keep remainder)
            }
        }

        private static QuillObject ResolveTarget(QuillAnimation a, out string propName)
        {
            if (!string.IsNullOrEmpty(a.OnProperty) && a.Parent != null)
            {
                propName = a.OnProperty;
                return a.Parent;
            }
            propName = QuillConvert.ToStr(a.Property("property").Raw);
            return a.Property("target").Raw as QuillObject;
        }

        // ---- Easing curves ---------------------------------------------------------------------

        private const double Pi = Math.PI;

        public static double Ease(string name, double t)
        {
            if (string.IsNullOrEmpty(name)) return t;
            // Accept "Easing.InOutQuad" or "InOutQuad".
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);

            switch (name)
            {
                case "Linear": return t;
                case "InQuad": return t * t;
                case "OutQuad": return t * (2 - t);
                case "InOutQuad": return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case "InCubic": return t * t * t;
                case "OutCubic": return 1 + Math.Pow(t - 1, 3);
                case "InOutCubic": return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                case "InSine": return 1 - Math.Cos(t * Pi / 2);
                case "OutSine": return Math.Sin(t * Pi / 2);
                case "InOutSine": return -(Math.Cos(Pi * t) - 1) / 2;
                case "OutBack":
                    const double c1 = 1.70158, c3 = c1 + 1;
                    return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
                default: return t; // unknown -> linear
            }
        }
    }
}
