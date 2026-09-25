// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Globalization;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Colour maths behind <c>Qt.rgba/hsva/hsla/lighter/darker/tint</c>, colour channel access in
    /// expressions (<c>c.hsvHue</c>, <c>c.a</c>) and colour interpolation in animations. All channels
    /// are 0..1, hue included, as in QML. Pure C# (no engine calls) so it runs anywhere.
    /// </summary>
    public static class QuillColor
    {
        /// <summary>HSV (all 0..1) to RGB. Hue wraps around (1.25 and -0.75 are both 0.25).</summary>
        public static Color FromHsv(double h, double s, double v, double a = 1)
        {
            h -= Math.Floor(h);
            s = Clamp01(s); v = Clamp01(v);
            double c = v * s;
            double hp = h * 6.0;
            double x = c * (1 - Math.Abs(hp % 2 - 1));
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; }
            else if (hp < 2) { r = x; g = c; }
            else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; }
            else if (hp < 5) { r = x; b = c; }
            else { r = c; b = x; }
            double m = v - c;
            return new Color((float)(r + m), (float)(g + m), (float)(b + m), (float)Clamp01(a));
        }

        /// <summary>RGB to HSV. Hue is -1 for achromatic colours (greys), as in QML.</summary>
        public static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.r, g = c.g, b = c.b;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            h = d <= 1e-9 ? -1 : Hue(r, g, b, max, d);
        }

        public static Color FromHsl(double h, double s, double l, double a = 1)
        {
            h -= Math.Floor(h);
            s = Clamp01(s); l = Clamp01(l);
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double v = l + c / 2;
            double sv = v <= 0 ? 0 : c / v;
            return FromHsv(h, sv, v, a);
        }

        /// <summary>RGB to HSL. Hue is -1 for achromatic colours (greys), as in QML.</summary>
        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.r, g = c.g, b = c.b;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            l = (max + min) / 2;
            s = d <= 1e-9 ? 0 : d / (1 - Math.Abs(2 * l - 1));
            h = d <= 1e-9 ? -1 : Hue(r, g, b, max, d);
        }

        private static double Hue(double r, double g, double b, double max, double d)
        {
            double h;
            if (max == r) h = ((g - b) / d) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            return h < 0 ? h + 1 : h;
        }

        /// <summary>QML <c>Qt.lighter</c>: scale HSV value by <paramref name="factor"/>, spilling into saturation.</summary>
        public static Color Lighter(Color c, double factor = 1.5)
        {
            if (factor <= 0) return c;
            if (factor < 1) return Darker(c, 1 / factor);
            ToHsv(c, out var h, out var s, out var v);
            v *= factor;
            if (v > 1) { s = Math.Max(0, s - (v - 1)); v = 1; }
            return FromHsv(h, s, v, c.a);
        }

        /// <summary>QML <c>Qt.darker</c>: divide HSV value by <paramref name="factor"/>.</summary>
        public static Color Darker(Color c, double factor = 2.0)
        {
            if (factor <= 0) return c;
            if (factor < 1) return Lighter(c, 1 / factor);
            ToHsv(c, out var h, out var s, out var v);
            return FromHsv(h, s, v / factor, c.a);
        }

        /// <summary>QML <c>Qt.tint</c>: <paramref name="tint"/> composited over <paramref name="baseColor"/> by its alpha.</summary>
        public static Color Tint(Color baseColor, Color tint)
        {
            float a = tint.a, inv = 1f - a;
            return new Color(tint.r * a + baseColor.r * inv,
                             tint.g * a + baseColor.g * inv,
                             tint.b * a + baseColor.b * inv,
                             a + inv * baseColor.a);
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            float k = (float)t;
            return new Color(a.r + (b.r - a.r) * k, a.g + (b.g - a.g) * k, a.b + (b.b - a.b) * k, a.a + (b.a - a.a) * k);
        }

        /// <summary>
        /// "#rrggbb" when opaque, else "#rrggbbaa" — the order Quill parses back, so a formatted
        /// colour round-trips through a string property.
        /// </summary>
        public static string Format(Color c)
        {
            int r = To255(c.r), g = To255(c.g), b = To255(c.b), a = To255(c.a);
            string s = "#" + r.ToString("x2", CultureInfo.InvariantCulture)
                           + g.ToString("x2", CultureInfo.InvariantCulture)
                           + b.ToString("x2", CultureInfo.InvariantCulture);
            return a == 255 ? s : s + a.ToString("x2", CultureInfo.InvariantCulture);
        }

        /// <summary>A channel of a colour by its QML name, or null if <paramref name="name"/> isn't one.</summary>
        public static object Channel(Color c, string name)
        {
            double h, s, v;
            switch (name)
            {
                case "r": return (double)c.r;
                case "g": return (double)c.g;
                case "b": return (double)c.b;
                case "a": return (double)c.a;
                case "hsvHue": ToHsv(c, out h, out s, out v); return h;
                case "hsvSaturation": ToHsv(c, out h, out s, out v); return s;
                case "hsvValue": ToHsv(c, out h, out s, out v); return v;
                case "hslHue": ToHsl(c, out h, out s, out v); return h;
                case "hslSaturation": ToHsl(c, out h, out s, out v); return s;
                case "hslLightness": ToHsl(c, out h, out s, out v); return v;
                case "valid": return true;
                default: return null;
            }
        }

        public static bool IsChannelName(string name)
        {
            switch (name)
            {
                case "r": case "g": case "b": case "a":
                case "hsvHue": case "hsvSaturation": case "hsvValue":
                case "hslHue": case "hslSaturation": case "hslLightness":
                case "valid":
                    return true;
                default:
                    return false;
            }
        }

        private static int To255(float x) => (int)Math.Round(Clamp01(x) * 255.0);
        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
