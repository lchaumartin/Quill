// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.IO;
using Quill.Parsing;

namespace Quill.LanguageServer
{
    /// <summary>An object of the document tree, with its parent (the AST has none).</summary>
    public sealed class Obj
    {
        public ObjectNode Node;
        public Obj Parent;
        public readonly List<Obj> Children = new List<Obj>();
        public string Type => Node.TypeName;
    }

    /// <summary>One .quill file: text, tokens, and its parse (tree or error).</summary>
    public sealed class QuillDoc
    {
        public string Path;          // local file path
        public string Uri;
        public string Text;
        public int Version;
        public bool Open;            // held by the editor (its text wins over the disk)

        public List<Token> Tokens;
        public ObjectNode Root;
        public Obj RootObj;
        public readonly List<Obj> Objects = new List<Obj>();
        public QuillParseException Error;
        public readonly Dictionary<string, Obj> Ids = new Dictionary<string, Obj>();
        private int[] _lineStarts;

        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public QuillDoc(string path, string uri, string text)
        {
            Path = path;
            Uri = uri;
            SetText(text);
        }

        public void SetText(string text)
        {
            Text = text ?? "";
            var starts = new List<int> { 0 };
            for (int i = 0; i < Text.Length; i++)
                if (Text[i] == '\n') starts.Add(i + 1);
            _lineStarts = starts.ToArray();

            // While the text doesn't parse (mid-edit), the last good tree stays available (Stale) so
            // completion and hover keep working; diagnostics only report the syntax error.
            var oldObjects = new List<Obj>(Objects);
            var oldIds = new Dictionary<string, Obj>(Ids);
            var oldRoot = RootObj;
            Objects.Clear();
            Ids.Clear();
            Root = null;
            RootObj = null;
            Error = null;
            Stale = false;
            try { Tokens = new Lexer(Text).Tokenize(); }
            catch (Exception) { Tokens = new List<Token> { new Token { Type = TokenType.EOF, Offset = Text.Length, End = Text.Length } }; }
            try
            {
                Root = new Parser(Tokens).ParseDocument();
                RootObj = Build(Root, null);
            }
            catch (QuillParseException e) { Error = e; }
            catch (Exception e) { Error = new QuillParseException(e.Message); }

            if (Error != null && oldRoot != null)
            {
                Objects.AddRange(oldObjects);
                foreach (var kv in oldIds) Ids[kv.Key] = kv.Value;
                RootObj = oldRoot;
                Stale = true;
            }
        }

        /// <summary>The tree is from an earlier version of the text (the current one doesn't parse).</summary>
        public bool Stale;

        private Obj Build(ObjectNode node, Obj parent)
        {
            var o = new Obj { Node = node, Parent = parent };
            Objects.Add(o);
            if (!string.IsNullOrEmpty(node.Id) && !Ids.ContainsKey(node.Id)) Ids[node.Id] = o;
            foreach (var c in node.Children) o.Children.Add(Build(c, o));
            return o;
        }

        // ---- Positions ----------------------------------------------------------------------------

        public (int line, int ch) PositionOf(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, Text.Length));
            int lo = 0, hi = _lineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
            }
            return (lo, offset - _lineStarts[lo]);
        }

        public int OffsetOf(int line, int ch)
        {
            if (line < 0) return 0;
            if (line >= _lineStarts.Length) return Text.Length;
            int lineEnd = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] - 1 : Text.Length;
            return Math.Max(_lineStarts[line], Math.Min(_lineStarts[line] + ch, lineEnd));
        }

        /// <summary>The innermost object whose body contains the offset.</summary>
        public Obj ObjectAt(int offset)
        {
            Obj best = null;
            foreach (var o in Objects)
                if (offset > o.Node.BodyStart && offset <= o.Node.BodyEnd)
                    if (best == null || o.Node.BodyStart > best.Node.BodyStart) best = o;
            return best;
        }

        /// <summary>Index of the token containing (or ending at) the offset, or -1.</summary>
        public int TokenIndexAt(int offset)
        {
            int endingHere = -1;
            for (int i = 0; i < Tokens.Count; i++)
            {
                var t = Tokens[i];
                if (t.Type == TokenType.EOF || t.Offset > offset) break;
                if (offset < t.End) return i;                 // inside (or at the start of) a token
                if (offset == t.End) endingHere = i;          // just after one: the cursor at a word's end
            }
            return endingHere;
        }

        /// <summary>Index of the last token that ends at or before the offset, or -1.</summary>
        public int TokenIndexBefore(int offset)
        {
            int best = -1;
            for (int i = 0; i < Tokens.Count; i++)
            {
                var t = Tokens[i];
                if (t.Type == TokenType.EOF) break;
                if (t.End <= offset) best = i; else break;
            }
            return best;
        }

        /// <summary>The leading comment block, without the licence header: a component's documentation.</summary>
        public string HeaderComment()
        {
            var lines = new List<string>();
            using (var r = new StringReader(Text))
            {
                string l;
                while ((l = r.ReadLine()) != null)
                {
                    var t = l.Trim();
                    if (!t.StartsWith("//")) break;
                    lines.Add(t.Length > 2 && t[2] == ' ' ? t.Substring(3) : t.Substring(2));
                }
            }
            // Drop the licence block (up to the first empty comment line) if it has one.
            int cut = 0;
            if (lines.Exists(x => x.Contains("Copyright")))
            {
                cut = lines.FindIndex(x => x.Trim().Length == 0);
                cut = cut < 0 ? lines.Count : cut + 1;
            }
            return string.Join("\n", lines.GetRange(cut, lines.Count - cut)).Trim();
        }

        /// <summary>The comment lines just above an offset's line (for property docs).</summary>
        public string CommentAbove(int offset)
        {
            var (line, _) = PositionOf(offset);
            var lines = new List<string>();
            for (int l = line - 1; l >= 0; l--)
            {
                int s = _lineStarts[l], e = l + 1 < _lineStarts.Length ? _lineStarts[l + 1] : Text.Length;
                var t = Text.Substring(s, e - s).Trim();
                if (!t.StartsWith("//")) break;
                lines.Insert(0, t.Substring(2).Trim());
            }
            return string.Join("\n", lines);
        }

        /// <summary>The trailing `// comment` on an offset's line, if any.</summary>
        public string TrailingComment(int offset)
        {
            var (line, _) = PositionOf(offset);
            int s = _lineStarts[line], e = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : Text.Length;
            var t = Text.Substring(s, e - s);
            int i = t.IndexOf("//", StringComparison.Ordinal);
            if (i < 0) return "";
            // Ignore `//` inside a string on that line (rare): count quotes before it.
            int quotes = 0;
            for (int k = 0; k < i; k++) if (t[k] == '"') quotes++;
            return quotes % 2 == 0 ? t.Substring(i + 2).Trim() : "";
        }

        public string Slice(int start, int end)
            => start < 0 || end < start ? "" : Text.Substring(start, Math.Min(end, Text.Length) - start);
    }
}
