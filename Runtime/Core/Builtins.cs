// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Quill.Parsing;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// The runtime library behind expressions: member and index access on every value type, calls
    /// (<c>Math.*</c>, <c>Qt.*</c>, <c>console.*</c>, globals such as <c>parseInt</c>, object methods
    /// and functions, value methods such as <c>toFixed</c>), and JavaScript-style equality.
    /// </summary>
    public static class Builtins
    {
        // ---- Member / index access ----------------------------------------------------------------

        public static object GetMember(object target, string member)
        {
            switch (target)
            {
                case null:
                    return null;

                case QuillObject obj:
                {
                    var p = obj.FindProperty(member);
                    if (p != null) return p.Get();
                    if (obj.HasGroup(member)) return new QuillGroup(obj, member);
                    return null;
                }

                case QuillGroup g:
                {
                    string full = g.Prefix + "." + member;
                    var p = g.Owner.FindProperty(full);
                    if (p != null) return p.Get();
                    if (g.Owner.HasGroup(full)) return new QuillGroup(g.Owner, full);
                    return null;
                }

                case List<object> list:
                    return member == "length" ? (object)(double)list.Count : null;

                case string s:
                    if (member == "length") return (double)s.Length;
                    if (QuillColor.IsChannelName(member) && QuillConvert.TryParseColor(s, out var parsed))
                        return QuillColor.Channel(parsed, member);
                    return null;

                case Color c:
                    return QuillColor.Channel(c, member);

                default:
                    return null;
            }
        }

        public static object GetIndex(object target, object index)
        {
            int i = (int)Math.Floor(QuillConvert.ToDouble(index));
            switch (target)
            {
                case List<object> list: return i >= 0 && i < list.Count ? list[i] : null;
                case string s: return i >= 0 && i < s.Length ? s[i].ToString() : null;
                case QuillObject obj when index is string name: return obj.FindProperty(name)?.Get();
                default: return null;
            }
        }

        /// <summary>JavaScript-flavoured `==`: text compares as text, colours by value, lists by content.</summary>
        public static bool LooseEquals(object l, object r)
        {
            if (l == null && r == null) return true;

            if (l is Color || r is Color)
            {
                if ((l is Color || l is string) && (r is Color || r is string))
                    return QuillConvert.ToColor(l) == QuillConvert.ToColor(r);
                return false;
            }
            if (l is string || r is string)
                return QuillConvert.ToStr(l) == QuillConvert.ToStr(r);
            if (l is QuillObject || r is QuillObject || l is QuillGroup || r is QuillGroup)
                return Equals(l, r);
            if (l is List<object> || r is List<object>)
                return QuillProperty.ValuesEqual(l, r);
            return QuillConvert.ToDouble(l) == QuillConvert.ToDouble(r);
        }

        // ---- Calls ----------------------------------------------------------------------------

        public static object Call(CallNode n, EvalContext ctx)
        {
            var args = new object[n.Args.Count];
            for (int i = 0; i < args.Length; i++) args[i] = n.Args[i].Eval(ctx);

            if (n.Target == null)
            {
                // A function declared in scope (this object or an ancestor), else a global.
                if (ctx.Engine != null && ctx.Engine.TryCallScopedFunction(ctx.Self, n.Name, args, out var result))
                    return result;
                return Global(n.Name, args);
            }

            if (n.Target is IdentifierNode id && (ctx.Locals == null || !ctx.Locals.ContainsKey(id.Name)))
            {
                switch (id.Name)
                {
                    case "Math": return MathBuiltins.Call(n.Name, args);
                    case "Qt": return Qt(n.Name, args);
                    case "console": Console(n.Name, args); return null;
                }
            }

            var target = n.Target.Eval(ctx);
            if (target is QuillObject obj)
                return ctx.Engine != null ? ctx.Engine.CallMethod(obj, n.Name, args) : null;
            return ValueMethod(target, n.Name, args);
        }

        private static object Arg(object[] a, int i) => i < a.Length ? a[i] : null;
        private static double Num(object[] a, int i, double fallback = 0)
            => i < a.Length && a[i] != null ? QuillConvert.ToDouble(a[i]) : fallback;

        // ---- Globals ------------------------------------------------------------------------------

        public static object Global(string name, object[] a)
        {
            switch (name)
            {
                case "parseInt":
                {
                    string s = QuillConvert.ToStr(Arg(a, 0)).Trim();
                    int radix = (int)Num(a, 1, 10);
                    try
                    {
                        if (radix == 16 && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                        int end = 0;
                        if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
                        while (end < s.Length && Uri.IsHexDigit(s[end]) && (radix == 16 || char.IsDigit(s[end]))) end++;
                        s = s.Substring(0, end);
                        if (s.Length == 0 || s == "-" || s == "+") return double.NaN;
                        return (double)Convert.ToInt64(s, radix);
                    }
                    catch { return double.NaN; }
                }
                case "parseFloat":
                case "Number":
                {
                    if (name == "Number" && !(Arg(a, 0) is string)) return QuillConvert.ToDouble(Arg(a, 0));
                    return double.TryParse(QuillConvert.ToStr(Arg(a, 0)).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
                }
                case "String": return QuillConvert.ToStr(Arg(a, 0));
                case "Boolean": return QuillConvert.ToBool(Arg(a, 0));
                case "isNaN": return double.IsNaN(QuillConvert.ToDouble(Arg(a, 0)));
                case "isFinite":
                {
                    double d = QuillConvert.ToDouble(Arg(a, 0));
                    return !double.IsNaN(d) && !double.IsInfinity(d);
                }
                case "qsTr": return QuillConvert.ToStr(Arg(a, 0));   // no translation layer: identity
                default:
                    Debug.LogWarning($"[Quill] Unknown function '{name}'.");
                    return null;
            }
        }

        private static void Console(string level, object[] a)
        {
            var sb = new StringBuilder("[Quill] ");
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuillConvert.ToStr(a[i]));
            }
            switch (level)
            {
                case "warn": Debug.LogWarning(sb.ToString()); break;
                case "error": Debug.LogError(sb.ToString()); break;
                default: Debug.Log(sb.ToString()); break;
            }
        }

        // ---- Qt.* (colours) -------------------------------------------------------------------------

        public static object Qt(string name, object[] a)
        {
            switch (name)
            {
                case "rgba": return new Color((float)Num(a, 0), (float)Num(a, 1), (float)Num(a, 2), (float)Num(a, 3, 1));
                case "hsva": return QuillColor.FromHsv(Num(a, 0), Num(a, 1), Num(a, 2), Num(a, 3, 1));
                case "hsla": return QuillColor.FromHsl(Num(a, 0), Num(a, 1), Num(a, 2), Num(a, 3, 1));
                case "color": return QuillConvert.ToColor(Arg(a, 0));
                case "lighter": return QuillColor.Lighter(QuillConvert.ToColor(Arg(a, 0)), Num(a, 1, 1.5));
                case "darker": return QuillColor.Darker(QuillConvert.ToColor(Arg(a, 0)), Num(a, 1, 2.0));
                case "tint": return QuillColor.Tint(QuillConvert.ToColor(Arg(a, 0)), QuillConvert.ToColor(Arg(a, 1)));
                case "alpha":
                {
                    var c = QuillConvert.ToColor(Arg(a, 0));
                    c.a = (float)Math.Max(0, Math.Min(1, Num(a, 1, 1)));
                    return c;
                }
                case "colorEqual": return QuillConvert.ToColor(Arg(a, 0)) == QuillConvert.ToColor(Arg(a, 1));
                default:
                    Debug.LogWarning($"[Quill] Unknown function 'Qt.{name}'.");
                    return null;
            }
        }

        // ---- Methods on plain values --------------------------------------------------------------

        public static object ValueMethod(object target, string name, object[] a)
        {
            switch (target)
            {
                case double d: return NumberMethod(d, name, a);
                case bool b: return name == "toString" ? (b ? "true" : "false") : null;
                case string s: return StringMethod(s, name, a);
                case List<object> list: return ListMethod(list, name, a);
                case Color c: return name == "toString" ? QuillColor.Format(c) : null;
                case null:
                    Debug.LogWarning($"[Quill] Cannot call '{name}' on null.");
                    return null;
                default: return null;
            }
        }

        private static object NumberMethod(double d, string name, object[] a)
        {
            switch (name)
            {
                case "toFixed":
                {
                    int digits = Math.Max(0, Math.Min(20, (int)Num(a, 0)));
                    return d.ToString("F" + digits, CultureInfo.InvariantCulture);
                }
                case "toPrecision":
                {
                    if (a.Length == 0) return QuillConvert.FormatNumber(d);
                    int p = Math.Max(1, Math.Min(21, (int)Num(a, 0)));
                    return d.ToString("G" + p, CultureInfo.InvariantCulture);
                }
                case "toString":
                {
                    int radix = (int)Num(a, 0, 10);
                    if (radix == 10 || radix < 2 || radix > 36) return QuillConvert.FormatNumber(d);
                    return ToRadix((long)d, radix);
                }
                default:
                    Debug.LogWarning($"[Quill] Unknown number method '{name}'.");
                    return null;
            }
        }

        private static string ToRadix(long v, int radix)
        {
            if (v == 0) return "0";
            bool neg = v < 0;
            v = Math.Abs(v);
            var sb = new StringBuilder();
            while (v > 0) { sb.Insert(0, "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(v % radix)]); v /= radix; }
            return neg ? "-" + sb : sb.ToString();
        }

        private static int SliceIndex(double i, int len)
        {
            int k = (int)i;
            if (k < 0) k = Math.Max(0, len + k);
            return Math.Min(k, len);
        }

        private static object StringMethod(string s, string name, object[] a)
        {
            switch (name)
            {
                case "toString": return s;
                case "toUpperCase": return s.ToUpperInvariant();
                case "toLowerCase": return s.ToLowerInvariant();
                case "trim": return s.Trim();
                case "charAt": { int i = (int)Num(a, 0); return i >= 0 && i < s.Length ? s[i].ToString() : ""; }
                case "indexOf": return (double)s.IndexOf(QuillConvert.ToStr(Arg(a, 0)), StringComparison.Ordinal);
                case "lastIndexOf": return (double)s.LastIndexOf(QuillConvert.ToStr(Arg(a, 0)), StringComparison.Ordinal);
                case "includes": return s.IndexOf(QuillConvert.ToStr(Arg(a, 0)), StringComparison.Ordinal) >= 0;
                case "startsWith": return s.StartsWith(QuillConvert.ToStr(Arg(a, 0)), StringComparison.Ordinal);
                case "endsWith": return s.EndsWith(QuillConvert.ToStr(Arg(a, 0)), StringComparison.Ordinal);
                case "repeat": { int n = Math.Max(0, (int)Num(a, 0)); var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(s); return sb.ToString(); }
                case "slice":
                {
                    int start = SliceIndex(Num(a, 0), s.Length);
                    int end = a.Length > 1 ? SliceIndex(Num(a, 1), s.Length) : s.Length;
                    return end > start ? s.Substring(start, end - start) : "";
                }
                case "substring":
                {
                    int start = Math.Max(0, Math.Min(s.Length, (int)Num(a, 0)));
                    int end = a.Length > 1 ? Math.Max(0, Math.Min(s.Length, (int)Num(a, 1))) : s.Length;
                    if (end < start) { int t = start; start = end; end = t; }
                    return s.Substring(start, end - start);
                }
                case "split":
                {
                    string sep = QuillConvert.ToStr(Arg(a, 0));
                    var list = new List<object>();
                    if (sep.Length == 0) { foreach (char ch in s) list.Add(ch.ToString()); return list; }
                    foreach (var part in s.Split(new[] { sep }, StringSplitOptions.None)) list.Add(part);
                    return list;
                }
                case "replace":
                {
                    string find = QuillConvert.ToStr(Arg(a, 0));
                    int at = find.Length == 0 ? -1 : s.IndexOf(find, StringComparison.Ordinal);
                    return at < 0 ? s : s.Substring(0, at) + QuillConvert.ToStr(Arg(a, 1)) + s.Substring(at + find.Length);
                }
                case "replaceAll":
                {
                    string find = QuillConvert.ToStr(Arg(a, 0));
                    return find.Length == 0 ? s : s.Replace(find, QuillConvert.ToStr(Arg(a, 1)));
                }
                case "padStart":
                case "padEnd":
                {
                    int width = (int)Num(a, 0);
                    string pad = a.Length > 1 ? QuillConvert.ToStr(a[1]) : " ";
                    if (pad.Length == 0 || s.Length >= width) return s;
                    var sb = new StringBuilder();
                    while (sb.Length < width - s.Length) sb.Append(pad);
                    string fill = sb.ToString(0, width - s.Length);
                    return name == "padStart" ? fill + s : s + fill;
                }
                case "arg":
                {
                    // Qt's "%1 of %2".arg(a): replace the lowest-numbered %N marker.
                    int lowest = int.MaxValue;
                    for (int i = 0; i + 1 < s.Length; i++)
                        if (s[i] == '%' && char.IsDigit(s[i + 1])) lowest = Math.Min(lowest, s[i + 1] - '0');
                    return lowest == int.MaxValue ? s : s.Replace("%" + lowest, QuillConvert.ToStr(Arg(a, 0)));
                }
                default:
                    Debug.LogWarning($"[Quill] Unknown string method '{name}'.");
                    return null;
            }
        }

        private static object ListMethod(List<object> list, string name, object[] a)
        {
            switch (name)
            {
                case "indexOf":
                    for (int i = 0; i < list.Count; i++) if (LooseEquals(list[i], Arg(a, 0))) return (double)i;
                    return -1.0;
                case "includes":
                    for (int i = 0; i < list.Count; i++) if (LooseEquals(list[i], Arg(a, 0))) return true;
                    return false;
                case "join":
                {
                    string sep = a.Length > 0 ? QuillConvert.ToStr(a[0]) : ",";
                    var parts = new string[list.Count];
                    for (int i = 0; i < list.Count; i++) parts[i] = QuillConvert.ToStr(list[i]);
                    return string.Join(sep, parts);
                }
                case "slice":
                {
                    int start = SliceIndex(Num(a, 0), list.Count);
                    int end = a.Length > 1 ? SliceIndex(Num(a, 1), list.Count) : list.Count;
                    return end > start ? list.GetRange(start, end - start) : new List<object>();
                }
                case "concat":
                {
                    var copy = new List<object>(list);
                    foreach (var x in a)
                        if (x is List<object> more) copy.AddRange(more); else copy.Add(x);
                    return copy;
                }
                case "toString": return QuillConvert.ToStr(list);
                default:
                    Debug.LogWarning($"[Quill] Unknown list method '{name}' (lists are values: build a new one instead of mutating).");
                    return null;
            }
        }
    }
}
