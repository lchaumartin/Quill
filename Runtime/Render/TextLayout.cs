// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.Text;

namespace Quill
{
    /// <summary>
    /// Breaks a Text's string into lines: explicit line breaks, wrapping (word / anywhere / word
    /// with long-word breaking) within a width, and eliding with "…". Pure C# over a glyph-advance
    /// callback, so the renderer and tests share it.
    /// </summary>
    public static class TextLayout
    {
        // QML Text.WrapMode / Text.TextElideMode values.
        public const int NoWrap = 0, WordWrap = 1, WrapAnywhere = 3, Wrap = 4;
        public const int ElideNone = 0, ElideLeft = 1, ElideMiddle = 2, ElideRight = 3;

        public struct Line
        {
            public string Text;
            public float Width;
        }

        public sealed class Result
        {
            public readonly List<Line> Lines = new List<Line>();
            public float Width;   // widest line
        }

        /// <param name="maxWidth">Available width; <= 0 or infinity means unconstrained.</param>
        /// <param name="advance">Horizontal advance of one character, in pixels.</param>
        public static Result Layout(string text, float maxWidth, int wrapMode, int elide, Func<char, float> advance)
        {
            var result = new Result();
            text ??= string.Empty;
            bool limited = maxWidth > 0 && !float.IsInfinity(maxWidth);

            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (limited && wrapMode != NoWrap) Wrapped(raw, maxWidth, wrapMode, advance, result.Lines);
                else result.Lines.Add(new Line { Text = raw, Width = Measure(raw, advance) });
            }

            if (limited && elide != ElideNone)
                for (int i = 0; i < result.Lines.Count; i++)
                    if (result.Lines[i].Width > maxWidth)
                        result.Lines[i] = Elide(result.Lines[i].Text, maxWidth, elide, advance);

            foreach (var l in result.Lines) result.Width = Math.Max(result.Width, l.Width);
            return result;
        }

        public static float Measure(string s, Func<char, float> advance)
        {
            float w = 0;
            for (int i = 0; i < s.Length; i++) w += advance(s[i]);
            return w;
        }

        private static void Wrapped(string para, float max, int mode, Func<char, float> advance, List<Line> lines)
        {
            if (para.Length == 0) { lines.Add(new Line { Text = "", Width = 0 }); return; }

            var line = new StringBuilder();
            float lineW = 0;

            void Flush()
            {
                string t = line.ToString().TrimEnd(' ');
                lines.Add(new Line { Text = t, Width = Measure(t, advance) });
                line.Clear();
                lineW = 0;
            }

            if (mode == WrapAnywhere)
            {
                foreach (char c in para)
                {
                    float a = advance(c);
                    if (line.Length > 0 && lineW + a > max) Flush();
                    line.Append(c);
                    lineW += a;
                }
                Flush();
                return;
            }

            // Word wrap: words keep their trailing space; a line never starts with the space it broke on.
            int i = 0;
            while (i < para.Length)
            {
                int j = i;
                while (j < para.Length && para[j] != ' ') j++;
                while (j < para.Length && para[j] == ' ') j++;
                string word = para.Substring(i, j - i);
                string bare = word.TrimEnd(' ');
                float wordW = Measure(word, advance), bareW = Measure(bare, advance);

                if (line.Length > 0 && lineW + bareW > max) Flush();

                if (line.Length == 0 && bareW > max && mode == Wrap)
                {
                    // Too long for any line: break it anywhere.
                    foreach (char c in word)
                    {
                        float a = advance(c);
                        if (line.Length > 0 && lineW + a > max && c != ' ') Flush();
                        line.Append(c);
                        lineW += a;
                    }
                }
                else
                {
                    line.Append(word);
                    lineW += wordW;
                }
                i = j;
            }
            if (line.Length > 0) Flush();
        }

        private static Line Elide(string s, float max, int mode, Func<char, float> advance)
        {
            const string dots = "…";
            float dotsW = Measure(dots, advance);
            float room = max - dotsW;
            if (room <= 0) return new Line { Text = dots, Width = dotsW };

            if (mode == ElideLeft)
            {
                float w = 0; int k = s.Length;
                while (k > 0 && w + advance(s[k - 1]) <= room) { w += advance(s[k - 1]); k--; }
                string t = dots + s.Substring(k);
                return new Line { Text = t, Width = w + dotsW };
            }
            if (mode == ElideMiddle)
            {
                float w = 0; int a = 0, b = s.Length;
                bool left = true;
                while (a < b)
                {
                    char c = left ? s[a] : s[b - 1];
                    if (w + advance(c) > room) break;
                    w += advance(c);
                    if (left) a++; else b--;
                    left = !left;
                }
                string t = s.Substring(0, a) + dots + s.Substring(b);
                return new Line { Text = t, Width = w + dotsW };
            }
            {
                float w = 0; int k = 0;
                while (k < s.Length && w + advance(s[k]) <= room) { w += advance(s[k]); k++; }
                string t = s.Substring(0, k).TrimEnd(' ') + dots;
                return new Line { Text = t, Width = Measure(t, advance) };
            }
        }
    }
}
