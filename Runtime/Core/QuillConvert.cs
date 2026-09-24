// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Globalization;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Conversions between the dynamic boxed values used by the binding system and the concrete
    /// types the renderer needs. Quill is loosely typed at the expression level, so everything is a
    /// double / bool / string / Color until it is consumed.
    /// </summary>
    public static class QuillConvert
    {
        public static double ToDouble(object v)
        {
            switch (v)
            {
                case null: return 0;
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case bool b: return b ? 1 : 0;
                case string s:
                    return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0;
                default: return 0;
            }
        }

        public static float ToFloat(object v) => (float)ToDouble(v);

        public static bool ToBool(object v)
        {
            switch (v)
            {
                case null: return false;
                case bool b: return b;
                case double d: return d != 0;
                case string s: return !string.IsNullOrEmpty(s) && s != "false";
                default: return true;
            }
        }

        public static string ToStr(object v)
        {
            if (v == null) return string.Empty;
            if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            return v.ToString();
        }

        public static Color ToColor(object v)
        {
            if (v is Color c) return c;
            if (v is string s && TryParseColor(s, out var parsed)) return parsed;
            return Color.magenta; // visible "unset/error" sentinel
        }

        public static bool TryParseColor(string s, out Color color)
        {
            color = Color.magenta;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();

            if (s == "transparent") { color = new Color(0, 0, 0, 0); return true; }

            // Unity understands "#RRGGBB", "#RRGGBBAA" and a set of HTML names ("red", "cyan"...).
            if (ColorUtility.TryParseHtmlString(s, out color)) return true;

            // Quill also accepts "#AARRGGBB". Translate to Unity's RGBA ordering.
            if (s.Length == 9 && s[0] == '#'
                && uint.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                float a = ((argb >> 24) & 0xFF) / 255f;
                float r = ((argb >> 16) & 0xFF) / 255f;
                float g = ((argb >> 8) & 0xFF) / 255f;
                float b = (argb & 0xFF) / 255f;
                color = new Color(r, g, b, a);
                return true;
            }
            return false;
        }
    }
}
