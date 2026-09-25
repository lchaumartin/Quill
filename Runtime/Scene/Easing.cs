// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;

namespace Quill
{
    /// <summary>
    /// The QML easing curves: <c>Linear</c> plus <c>In</c>, <c>Out</c>, <c>InOut</c> and <c>OutIn</c>
    /// variants of <c>Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce</c>.
    /// <c>easing.amplitude</c> / <c>easing.period</c> shape Elastic (and Bounce's amplitude),
    /// <c>easing.overshoot</c> shapes Back.
    /// </summary>
    public static class Easing
    {
        public static readonly string[] Families =
            { "Quad", "Cubic", "Quart", "Quint", "Sine", "Expo", "Circ", "Back", "Elastic", "Bounce" };

        /// <summary>Every curve name, for the <c>Easing.*</c> built-in.</summary>
        public static string[] AllNames()
        {
            var names = new string[1 + Families.Length * 4];
            names[0] = "Linear";
            int n = 1;
            foreach (var f in Families)
            {
                names[n++] = "In" + f;
                names[n++] = "Out" + f;
                names[n++] = "InOut" + f;
                names[n++] = "OutIn" + f;
            }
            return names;
        }

        public static double Evaluate(string name, double t, double amplitude = 1.0, double period = 0.3, double overshoot = 1.70158)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (string.IsNullOrEmpty(name)) return t;

            // Accept "Easing.InOutQuad" or "InOutQuad".
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            if (name == "Linear") return t;

            string family;
            Func<double, double> ein;
            if (name.StartsWith("InOut", StringComparison.Ordinal)) family = name.Substring(5);
            else if (name.StartsWith("OutIn", StringComparison.Ordinal)) family = name.Substring(5);
            else if (name.StartsWith("Out", StringComparison.Ordinal)) family = name.Substring(3);
            else if (name.StartsWith("In", StringComparison.Ordinal)) family = name.Substring(2);
            else return t;

            ein = In(family, amplitude, period, overshoot);
            if (ein == null) return t;

            double EOut(double x) => 1 - ein(1 - x);

            if (name.StartsWith("InOut", StringComparison.Ordinal))
                return t < 0.5 ? ein(t * 2) / 2 : 1 - ein((1 - t) * 2) / 2;
            if (name.StartsWith("OutIn", StringComparison.Ordinal))
                return t < 0.5 ? EOut(t * 2) / 2 : 0.5 + ein(t * 2 - 1) / 2;
            if (name.StartsWith("Out", StringComparison.Ordinal))
                return EOut(t);
            return ein(t);
        }

        // The "In" form of each family; the others are derived from it.
        private static Func<double, double> In(string family, double amplitude, double period, double overshoot)
        {
            switch (family)
            {
                case "Quad": return t => t * t;
                case "Cubic": return t => t * t * t;
                case "Quart": return t => t * t * t * t;
                case "Quint": return t => t * t * t * t * t;
                case "Sine": return t => 1 - Math.Cos(t * Math.PI / 2);
                case "Expo": return t => t <= 0 ? 0 : Math.Pow(2, 10 * (t - 1)) - 0.001;
                case "Circ": return t => 1 - Math.Sqrt(Math.Max(0, 1 - t * t));
                case "Back": return t => t * t * ((overshoot + 1) * t - overshoot);
                case "Elastic":
                    return t =>
                    {
                        if (t <= 0) return 0;
                        if (t >= 1) return 1;
                        double p = period <= 0 ? 0.3 : period;
                        double a = amplitude;
                        double s;
                        if (a < 1) { a = 1; s = p / 4; }
                        else s = p / (2 * Math.PI) * Math.Asin(1 / a);
                        double u = t - 1;
                        return -(a * Math.Pow(2, 10 * u) * Math.Sin((u - s) * (2 * Math.PI) / p));
                    };
                case "Bounce": return t => 1 - BounceOut(1 - t, amplitude);
                default: return null;
            }
        }

        private static double BounceOut(double t, double a)
        {
            // Classic Penner bounce; `a` scales the rebounds (1 = standard).
            if (t < 1 / 2.75) return 7.5625 * t * t;
            double k;
            if (t < 2 / 2.75) { t -= 1.5 / 2.75; k = 7.5625 * t * t + 0.75; }
            else if (t < 2.5 / 2.75) { t -= 2.25 / 2.75; k = 7.5625 * t * t + 0.9375; }
            else { t -= 2.625 / 2.75; k = 7.5625 * t * t + 0.984375; }
            return 1 - (1 - k) * a;
        }
    }
}
