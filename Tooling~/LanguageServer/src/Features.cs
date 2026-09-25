// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quill.Parsing;

namespace Quill.LanguageServer
{
    public sealed class CompletionItem
    {
        public string Label, Detail, Doc, Insert, SortText;
        public int Kind;          // LSP CompletionItemKind
        public bool Snippet;
    }

    public sealed class Location
    {
        public QuillDoc Doc;
        public int Start, End;
    }

    public sealed class Symbol
    {
        public string Name, Detail;
        public int Kind, Start, End, SelStart, SelEnd;
        public readonly List<Symbol> Children = new List<Symbol>();
    }

    /// <summary>Completion, hover, go-to-definition, outline and folding for one document.</summary>
    public sealed class Features
    {
        // LSP CompletionItemKind / SymbolKind values.
        private const int KMethod = 2, KFunction = 3, KVariable = 6, KClass = 7, KProperty = 10, KEnum = 13, KKeyword = 14,
                          KSnippet = 15, KColor = 16, KReference = 18, KEnumMember = 20, KEvent = 23;
        private const int SClass = 5, SProperty = 7, SFunction = 12, SObject = 19, SEvent = 24;

        private readonly Workspace _ws;
        private readonly QuillDoc _doc;
        private readonly Analysis _an;

        public Features(Workspace ws, QuillDoc doc)
        {
            _ws = ws;
            _doc = doc;
            _an = new Analysis(ws, doc);
        }

        // ---- Context -----------------------------------------------------------------------------

        private enum Ctx { Member, Value, Code, PropertyType, OnProperty, None }

        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// Where the cursor is: at the start of a member in an object body, in a value / code, or after
        /// `property` / `on`. Token-based, so it works while the file doesn't parse.
        /// </summary>
        private Ctx ContextAt(int offset, int wordStart, out string ownerType)
        {
            ownerType = null;
            var t = _doc.Tokens;
            var stack = new List<(char kind, int tokenIndex)>();   // 'o' object body, 'c' code block, '(' '['
            int last = -1;
            for (int i = 0; i < t.Count && t[i].Type != TokenType.EOF && t[i].End <= wordStart; i++)
            {
                last = i;
                switch (t[i].Type)
                {
                    case TokenType.LBrace:
                    {
                        bool obj = i > 0 && t[i - 1].Type == TokenType.Identifier
                                   && (char.IsUpper(t[i - 1].Text[0]) && (i < 2 || t[i - 2].Type != TokenType.Dot)
                                       || (i >= 2 && t[i - 2].Type == TokenType.Identifier && t[i - 2].Text == "on")
                                       || (i >= 3 && t[i - 2].Type == TokenType.Dot));
                        if (obj && i >= 2 && t[i - 2].Type == TokenType.Colon && !char.IsUpper(t[i - 1].Text[0])) obj = false;
                        stack.Add((obj ? 'o' : 'c', i));
                        break;
                    }
                    case TokenType.LParen: stack.Add(('(', i)); break;
                    case TokenType.LBracket: stack.Add(('[', i)); break;
                    case TokenType.RBrace:
                    case TokenType.RParen:
                    case TokenType.RBracket:
                        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                        break;
                }
            }
            if (stack.Count == 0) return last < 0 ? Ctx.Member : Ctx.None;   // an empty file: offer root types
            var top = stack[stack.Count - 1];
            if (top.kind != 'o') return Ctx.Code;

            // The object's type: the identifier before `{` (or before `on x {`).
            int bi = top.tokenIndex;
            for (int k = bi - 1; k >= 0; k--)
                if (t[k].Type == TokenType.Identifier && char.IsUpper(t[k].Text[0]) && (k == 0 || t[k - 1].Type != TokenType.Dot))
                {
                    ownerType = t[k].Text;
                    break;
                }

            // What precedes the word on its line decides member vs value.
            int lineStart = _doc.Text.LastIndexOf('\n', Math.Max(0, wordStart - 1)) + 1;
            if (wordStart == 0) lineStart = 0;
            string before = _doc.Text.Substring(lineStart, Math.Max(0, wordStart - lineStart));
            string trimmed = before.Trim();
            // Only a dotted prefix (`anchors.`) before the word still counts as the member name.
            string lead = trimmed.TrimEnd('.');
            if (trimmed.Length == 0 || (trimmed.EndsWith(".") && lead.All(c => IsIdent(c) || c == '.'))) return Ctx.Member;
            if (trimmed.EndsWith("{") || trimmed.EndsWith(";") || trimmed.EndsWith("}")) return Ctx.Member;
            var words = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0 && words[words.Length - 1] == "property") return Ctx.PropertyType;
            if (words.Length > 0 && words[words.Length - 1] == "on") return Ctx.OnProperty;
            if (words.Length > 0 && (words[0] == "signal" || words[0] == "function" || words[words.Length - 1] == "property")) return Ctx.None;
            if (words.Length >= 2 && words[words.Length - 2] == "property") return Ctx.None;   // naming a new property
            if (trimmed.Contains(':') || trimmed.StartsWith("?") || trimmed.StartsWith("&&") || trimmed.StartsWith("||")
                || trimmed.StartsWith("+") || trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("("))
                return Ctx.Value;
            if (words.Length > 0 && (words[0] == "readonly" || words[0] == "default" || words[0] == "required")) return Ctx.Member;
            return Ctx.Value;
        }

        /// <summary>`a.b.` before the word: the identifiers of the chain (empty if none).</summary>
        private List<string> ChainBefore(int wordStart)
        {
            var chain = new List<string>();
            int i = wordStart - 1;
            while (i >= 0 && _doc.Text[i] == '.')
            {
                int e = i;
                i--;
                while (i >= 0 && char.IsWhiteSpace(_doc.Text[i]) && _doc.Text[i] != '\n') i--;
                int s = i;
                while (s >= 0 && IsIdent(_doc.Text[s])) s--;
                if (s == i) break;
                chain.Insert(0, _doc.Text.Substring(s + 1, i - s));
                i = s;
            }
            return chain;
        }

        private Obj OwnerAt(int offset) => _doc.RootObj != null ? _doc.ObjectAt(offset) : null;

        // ---- Completion --------------------------------------------------------------------------

        public List<CompletionItem> Complete(int offset)
        {
            int ws = offset;
            while (ws > 0 && IsIdent(_doc.Text[ws - 1])) ws--;
            var chain = ChainBefore(ws);
            var ctx = ContextAt(offset, ws, out var ownerType);
            var owner = OwnerAt(offset);
            if (owner == null && ownerType != null)
                owner = new Obj { Node = new ObjectNode { TypeName = ownerType } };
            var items = new List<CompletionItem>();

            switch (ctx)
            {
                case Ctx.PropertyType:
                    foreach (var ty in Schema.PropertyTypes)
                        items.Add(new CompletionItem { Label = ty, Kind = KKeyword, Detail = "property type" });
                    break;

                case Ctx.OnProperty:
                    if (owner?.Parent != null || owner != null)
                    {
                        // `Behavior on |` sits in the object being animated's body.
                        foreach (var m in ApiFor(owner).Props.Values.Where(p => !p.ReadOnly))
                            items.Add(PropItem(m, insert: m.Name));
                    }
                    break;

                case Ctx.Member:
                    if (chain.Count > 0) MemberGroupItems(owner, string.Join(".", chain), items);
                    else MemberItems(owner, items);
                    break;

                case Ctx.Value:
                case Ctx.Code:
                    if (chain.Count > 0) ChainItems(chain, owner, offset, items);
                    else ScopeItems(owner, offset, ctx == Ctx.Code, items);
                    break;
            }
            return items;
        }

        private TypeApi ApiFor(Obj o)
            => o == null ? new TypeApi() : o.Node.Properties.Count > 0 || o.Node.Functions.Count > 0 ? _ws.ObjectApi(_doc, o) : _ws.Api(o.Type);

        private void MemberItems(Obj owner, List<CompletionItem> items)
        {
            // Element types: a nested object.
            foreach (var e in Schema.Elements.Values)
                items.Add(new CompletionItem { Label = e.Name, Kind = KClass, Detail = "element", Doc = e.Doc, Insert = e.Name + " {\n\t$0\n}", Snippet = true, SortText = "2" + e.Name });
            foreach (var c in _ws.ComponentNames().Where(n => n != "Theme" && !Schema.Elements.ContainsKey(n)))
            {
                var f = _ws.ComponentFiles(c, _doc).FirstOrDefault();
                items.Add(new CompletionItem { Label = c, Kind = KClass, Detail = "component · " + ShortPath(f), Doc = f?.HeaderComment(), Insert = c + " {\n\t$0\n}", Snippet = true, SortText = "2" + c });
            }

            // Keywords.
            items.Add(new CompletionItem { Label = "id", Kind = KKeyword, Insert = "id: ", Detail = "this object's id", SortText = "0id" });
            items.Add(new CompletionItem { Label = "property", Kind = KKeyword, Insert = "property ${1|real,int,bool,string,color,var,alias|} ${2:name}: $0", Snippet = true, Detail = "declare a property", SortText = "0property" });
            items.Add(new CompletionItem { Label = "signal", Kind = KKeyword, Insert = "signal ${1:name}", Snippet = true, Detail = "declare a signal", SortText = "0signal" });
            items.Add(new CompletionItem { Label = "function", Kind = KKeyword, Insert = "function ${1:name}(${2}) {\n\t$0\n}", Snippet = true, Detail = "declare a function", SortText = "0function" });
            items.Add(new CompletionItem { Label = "Behavior on", Kind = KSnippet, Insert = "Behavior on ${1:property} { ${2:NumberAnimation} { duration: ${3:200} } }", Snippet = true, Detail = "animate every change of a property", SortText = "1Behavior" });
            items.Add(new CompletionItem { Label = "Component.onCompleted", Kind = KEvent, Insert = "Component.onCompleted: ", Detail = "runs once the tree is built", SortText = "1Component" });

            if (owner == null) return;
            var api = ApiFor(owner);
            foreach (var m in api.Props.Values.Where(p => !p.ReadOnly))
                items.Add(PropItem(m, insert: m.Name + ": "));
            foreach (var s in api.Signals.Values)
            {
                string h = "on" + char.ToUpperInvariant(s.Name[0]) + s.Name.Substring(1);
                items.Add(new CompletionItem
                {
                    Label = h, Kind = KEvent, Insert = h + ": ", SortText = "1" + h,
                    Detail = "signal " + s.Name + (s.Params.Length > 0 ? "(" + string.Join(", ", s.Params) + ")" : ""),
                    Doc = s.Doc,
                });
            }
        }

        // `anchors.` / `font.` / `Component.` at a member position.
        private void MemberGroupItems(Obj owner, string prefix, List<CompletionItem> items)
        {
            if (prefix == "Component")
            {
                items.Add(new CompletionItem { Label = "onCompleted", Kind = KEvent, Insert = "onCompleted: ", Detail = "runs once the tree is built" });
                return;
            }
            if (owner == null) return;
            var api = ApiFor(owner);
            foreach (var m in api.Props.Values.Where(p => p.Name.StartsWith(prefix + ".", StringComparison.Ordinal) && !p.ReadOnly))
            {
                string rest = m.Name.Substring(prefix.Length + 1);
                items.Add(PropItem(m, label: rest, insert: rest + ": "));
            }
        }

        private void ChainItems(List<string> chain, Obj owner, int offset, List<CompletionItem> items)
        {
            if (chain.Count != 1) return;   // deeper chains: values of unknown type
            string head = chain[0];
            if (Schema.Enums.TryGetValue(head, out var members))
            {
                foreach (var kv in members)
                {
                    bool fn = kv.Value.StartsWith("`");
                    items.Add(new CompletionItem { Label = kv.Key, Kind = fn ? KFunction : KEnumMember, Detail = head + "." + kv.Key, Doc = kv.Value });
                }
                return;
            }
            var span = _an.SpanAt(offset);
            var r = owner != null ? _an.Resolve(head, owner, span) : null;
            if (head == "Theme") r = new Resolution { Kind = "global", Name = "Theme" };
            var api = _an.ApiOf(r, owner);
            if (api == null) return;
            foreach (var m in api.Props.Values)
            {
                if (m.Name.Contains('.')) continue;
                var it = PropItem(m, insert: m.Name);
                if (head == "Theme" && m.Value != null)
                {
                    it.Detail = (m.Type ?? "") + " = " + m.Value;
                    if (m.Type == "color") { it.Kind = KColor; it.Doc = m.Value.Trim('"'); }
                }
                items.Add(it);
            }
            foreach (var g in api.Props.Keys.Where(k => k.Contains('.')).Select(k => k.Substring(0, k.IndexOf('.'))).Distinct())
                items.Add(new CompletionItem { Label = g, Kind = KProperty, Detail = "property group" });
            foreach (var f in api.Functions.Values)
                items.Add(new CompletionItem { Label = f.Name, Kind = f.Kind == "method" ? KMethod : KFunction, Detail = f.Type ?? f.Name + "(" + string.Join(", ", f.Params) + ")", Doc = f.Doc, Insert = f.Name + "($0)", Snippet = true });
            foreach (var s in api.Signals.Values)
                items.Add(new CompletionItem { Label = s.Name, Kind = KEvent, Detail = "signal — call to emit", Doc = s.Doc, Insert = s.Name + "($0)", Snippet = true });
        }

        private void ScopeItems(Obj owner, int offset, bool code, List<CompletionItem> items)
        {
            var span = _an.SpanAt(offset);
            var seen = new HashSet<string>();
            void Add(CompletionItem it) { if (seen.Add(it.Label)) items.Add(it); }

            if (span != null)
                foreach (var l in span.Locals) Add(new CompletionItem { Label = l, Kind = KVariable, Detail = "local", SortText = "0" + l });
            foreach (var kv in _doc.Ids)
                Add(new CompletionItem { Label = kv.Key, Kind = KReference, Detail = "id · " + kv.Value.Type, SortText = "1" + kv.Key });
            int depth = 0;
            for (var o = owner; o != null; o = o.Parent, depth++)
            {
                var api = ApiFor(o);
                foreach (var m in api.Props.Values.Where(p => !p.Name.Contains('.')))
                    Add(PropItem(m, insert: m.Name, sort: (depth == 0 ? "2" : "3") + m.Name));
                foreach (var f in api.Functions.Values.Where(f => f.Kind == "function"))
                    Add(new CompletionItem { Label = f.Name, Kind = KFunction, Detail = f.Name + "(" + string.Join(", ", f.Params) + ")", Doc = f.Doc, Insert = f.Name + "($0)", Snippet = true, SortText = "2" + f.Name });
            }
            foreach (var kv in Schema.Globals)
                Add(new CompletionItem { Label = kv.Key, Kind = char.IsUpper(kv.Key[0]) ? KEnum : KVariable, Detail = "global", Doc = kv.Value, SortText = "4" + kv.Key });
            foreach (var e in Schema.Enums.Keys)
                Add(new CompletionItem { Label = e, Kind = KEnum, Detail = "global", Doc = Schema.Globals.TryGetValue(e, out var d) ? d : null, SortText = "4" + e });
            foreach (var k in new[] { "true", "false", "null" })
                Add(new CompletionItem { Label = k, Kind = KKeyword, SortText = "5" + k });
            if (code)
                foreach (var k in new[] { "if", "else", "for", "while", "var", "let", "const", "return", "break", "continue" })
                    Add(new CompletionItem { Label = k, Kind = KKeyword, SortText = "5" + k });
        }

        private static CompletionItem PropItem(Member m, string label = null, string insert = null, string sort = null)
            => new CompletionItem
            {
                Label = label ?? m.Name,
                Kind = m.Kind == "signal" ? KEvent : m.Kind == "function" || m.Kind == "method" ? KFunction : KProperty,
                Detail = (m.ReadOnly ? "readonly " : "") + (m.Type ?? "") + (m.Source != null ? " · " + m.Source.Name : ""),
                Doc = m.Doc,
                Insert = insert,
                SortText = sort ?? "1" + (label ?? m.Name),
            };

        private string ShortPath(QuillDoc d)
        {
            if (d == null) return "";
            int i = d.Path.IndexOf("/Resources/", StringComparison.Ordinal);
            return i >= 0 ? d.Path.Substring(i + 1) : System.IO.Path.GetFileName(d.Path);
        }

        // ---- Hover & definition ------------------------------------------------------------------

        /// <summary>What's under the cursor, as markdown, with the range it covers.</summary>
        public (string markdown, int start, int end)? Hover(int offset)
        {
            var target = Target(offset);
            if (target == null) return null;
            return (target.Value.markdown, target.Value.start, target.Value.end);
        }

        public List<Location> Definition(int offset)
        {
            var target = Target(offset);
            return target?.locations ?? new List<Location>();
        }

        private (string markdown, int start, int end, List<Location> locations)? Target(int offset)
        {
            int ti = _doc.TokenIndexAt(offset);
            if (ti < 0) return null;
            var t = _doc.Tokens;
            var tok = t[ti];
            if (tok.Type != TokenType.Identifier) return null;
            var locs = new List<Location>();

            // 1. An element type.
            var typeObj = _doc.Objects.FirstOrDefault(o => o.Node.TypeOffset == tok.Offset);
            if (typeObj != null || (char.IsUpper(tok.Text[0]) && ti + 1 < t.Count && (t[ti + 1].Type == TokenType.LBrace || (t[ti + 1].Type == TokenType.Identifier && t[ti + 1].Text == "on"))))
                return TypeTarget(tok.Text, tok.Offset, tok.End);

            // 2. An id declaration.
            var idObj = _doc.Objects.FirstOrDefault(o => o.Node.IdOffset == tok.Offset);
            if (idObj != null)
                return ($"**id** `{idObj.Node.Id}` — {TypeLink(idObj.Type)}", tok.Offset, tok.End, new List<Location> { new Location { Doc = _doc, Start = tok.Offset, End = tok.End } });

            // 3. A member name at the start of `name: value` (possibly dotted).
            foreach (var o in _doc.Objects)
                foreach (var p in o.Node.Properties)
                    if (offset >= p.NameOffset && offset <= p.NameEnd && p.NameEnd > p.NameOffset)
                    {
                        var api = _ws.ObjectApi(_doc, o);
                        Member m;
                        if (p.Handler != null && !p.Name.Contains('.'))
                        {
                            m = Analysis.SignalForHandler(api, p.Name);
                            if (m == null) return null;
                            return (SignalMarkdown(m, api.Name, handler: p.Name), p.NameOffset, p.NameEnd, Where(m));
                        }
                        if (!api.Props.TryGetValue(p.Name, out m)) return null;
                        return (MemberMarkdown(m, api.Name), p.NameOffset, p.NameEnd, Where(m));
                    }

            // 4. A name in an expression: `a`, or `a.b` (hovering either part).
            var owner = OwnerAt(offset);
            if (owner == null) return null;
            var span = _an.SpanAt(offset);
            bool isMember = ti > 0 && t[ti - 1].Type == TokenType.Dot;
            if (!isMember)
            {
                var r = _an.Resolve(tok.Text, owner, span);
                if (r == null) return null;
                switch (r.Kind)
                {
                    case "local": return ($"`{tok.Text}` — local", tok.Offset, tok.End, locs);
                    case "id":
                        return ($"**id** `{r.Name}` — {TypeLink(r.Obj.Type)}", tok.Offset, tok.End,
                                new List<Location> { new Location { Doc = _doc, Start = r.Obj.Node.IdOffset, End = r.Obj.Node.IdOffset + r.Name.Length } });
                    case "member":
                        if (r.Member?.Kind == "group") return ($"`{r.Name}` — property group", tok.Offset, tok.End, locs);
                        return (r.Member.Kind == "signal" ? SignalMarkdown(r.Member, _ws.ObjectApi(_doc, r.Obj).Name) : MemberMarkdown(r.Member, _ws.ObjectApi(_doc, r.Obj).Name), tok.Offset, tok.End, Where(r.Member));
                    case "enum":
                    case "global":
                        if (r.Name == "Theme")
                            return ("**Theme** — " + Schema.Globals["Theme"], tok.Offset, tok.End,
                                    _ws.ComponentFiles("Theme", _doc).Where(d => d.Root != null).Select(d => new Location { Doc = d, Start = d.Root.TypeOffset, End = d.Root.TypeOffset + d.Root.TypeName.Length }).ToList());
                        return ($"**{r.Name}** — " + (Schema.Globals.TryGetValue(r.Name, out var g) ? g : "global"), tok.Offset, tok.End, locs);
                }
                return null;
            }

            // A member: resolve the identifier before the dot.
            if (ti < 2 || t[ti - 2].Type != TokenType.Identifier || (ti >= 3 && t[ti - 3].Type == TokenType.Dot)) return null;
            var head = t[ti - 2].Text;
            if (Schema.Enums.TryGetValue(head, out var em))
                return em.TryGetValue(tok.Text, out var ed) ? ($"**{head}.{tok.Text}**" + (ed.Length > 0 ? " — " + ed : ""), tok.Offset, tok.End, locs) : ((string, int, int, List<Location>)?)null;
            var hr = _an.Resolve(head, owner, span);
            var hapi = _an.ApiOf(hr, owner);
            if (hapi == null) return null;
            var mem = hapi.Find(tok.Text);
            if (mem == null) return null;
            if (head == "Theme")
            {
                var allLocs = new List<Location>();
                var lines = new List<string>();
                foreach (var d in _ws.ComponentFiles("Theme", _doc).Where(d => d.Root != null))
                {
                    var p = d.Root.Properties.FirstOrDefault(x => x.IsDeclaration && x.Name == tok.Text);
                    if (p == null) continue;
                    allLocs.Add(new Location { Doc = d, Start = p.NameOffset, End = p.NameEnd });
                    lines.Add($"| {ThemeName(d)} | `{d.Slice(p.ValueStart, p.ValueEnd).Trim()}` |");
                }
                var md = $"**Theme.{tok.Text}** : {mem.Type}" + (string.IsNullOrEmpty(mem.Doc) ? "" : " — " + mem.Doc);
                if (lines.Count > 0) md += "\n\n| Theme | Value |\n|---|---|\n" + string.Join("\n", lines);
                return (md, tok.Offset, tok.End, allLocs);
            }
            return (mem.Kind == "signal" ? SignalMarkdown(mem, hapi.Name) : MemberMarkdown(mem, hapi.Name), tok.Offset, tok.End, Where(mem));
        }

        private static string ThemeName(QuillDoc d)
        {
            var dir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(d.Path));
            return dir ?? d.Name;
        }

        private (string, int, int, List<Location>)? TypeTarget(string type, int start, int end)
        {
            var api = _ws.Api(type);
            var locs = new List<Location>();
            var sb = new StringBuilder();
            if (api.Builtin != null)
            {
                sb.Append($"**{type}** — built-in element\n\n{api.Builtin.Doc}");
            }
            else if (api.Components.Count > 0)
            {
                var files = _ws.ComponentFiles(type, _doc).Where(d => d.Root != null).ToList();
                var main = files[0];
                sb.Append($"**{type}** — component `{ShortPath(main)}`");
                if (files.Count > 1) sb.Append($" (+{files.Count - 1} more: " + string.Join(", ", files.Skip(1).Select(ThemeName)) + ")");
                var header = main.HeaderComment();
                if (header.Length > 0) sb.Append("\n\n```\n" + header + "\n```");
                foreach (var f in files) locs.Add(new Location { Doc = f, Start = f.Root.TypeOffset, End = f.Root.TypeOffset + f.Root.TypeName.Length });
            }
            else return ($"**{type}** — unknown element", start, end, locs);
            return (sb.ToString(), start, end, locs);
        }

        private string TypeLink(string type) => _ws.Api(type).Builtin != null ? $"`{type}` (built-in)" : $"`{type}`";

        private static string MemberMarkdown(Member m, string owner)
        {
            var sb = new StringBuilder();
            string kind = m.Kind == "function" ? "function" : m.Kind == "method" ? "method" : "property";
            if (m.Kind == "function")
                sb.Append($"**{m.Name}**({string.Join(", ", m.Params)}) — function of `{owner}`");
            else if (m.Kind == "method")
                sb.Append($"**{m.Type ?? m.Name}** — method of `{owner}`");
            else
                sb.Append($"**{m.Name}** : {m.Type}{(m.ReadOnly ? " (read-only)" : "")} — {kind} of `{owner}`");
            if (m.Value != null) sb.Append($"\n\ndefault: `{m.Value}`");
            if (!string.IsNullOrEmpty(m.Doc)) sb.Append("\n\n" + m.Doc);
            return sb.ToString();
        }

        private static string SignalMarkdown(Member m, string owner, string handler = null)
        {
            var sb = new StringBuilder($"**signal {m.Name}**({string.Join(", ", m.Params)}) — `{owner}`");
            if (handler != null && m.Params.Length > 0)
                sb.Append($"\n\nIn `{handler}`, " + string.Join(", ", m.Params.Select(p => $"`{p}`")) + (m.Params.Length == 1 ? " is" : " are") + " available.");
            if (!string.IsNullOrEmpty(m.Doc)) sb.Append("\n\n" + m.Doc);
            return sb.ToString();
        }

        private static List<Location> Where(Member m)
            => m?.Source != null && m.Offset >= 0 ? new List<Location> { new Location { Doc = m.Source, Start = m.Offset, End = m.End } } : new List<Location>();

        // ---- Outline & folding -------------------------------------------------------------------

        public List<Symbol> Symbols()
        {
            var list = new List<Symbol>();
            if (_doc.RootObj != null) list.Add(SymbolOf(_doc.RootObj));
            return list;
        }

        private Symbol SymbolOf(Obj o)
        {
            var n = o.Node;
            var s = new Symbol
            {
                Name = n.TypeName + (n.Id != null ? " #" + n.Id : ""),
                Detail = n.OnProperty != null ? "on " + n.OnProperty : n.AssignedTo,
                Kind = o.Parent == null ? SClass : SObject,
                Start = n.TypeOffset, End = n.BodyEnd + 1, SelStart = n.TypeOffset, SelEnd = n.TypeOffset + n.TypeName.Length,
            };
            foreach (var p in n.Properties.Where(p => p.IsDeclaration))
                s.Children.Add(new Symbol { Name = p.Name, Detail = p.DeclaredType, Kind = SProperty, Start = p.NameOffset, End = Math.Max(p.NameEnd, p.ValueEnd), SelStart = p.NameOffset, SelEnd = p.NameEnd });
            foreach (var (name, off, end) in n.SignalDecls)
                s.Children.Add(new Symbol { Name = name, Detail = "signal", Kind = SEvent, Start = off, End = end, SelStart = off, SelEnd = end });
            foreach (var f in n.Functions)
                s.Children.Add(new Symbol { Name = f.Name + "(" + string.Join(", ", f.Params) + ")", Kind = SFunction, Start = f.NameOffset, End = Math.Max(f.NameEnd, f.BodyEnd), SelStart = f.NameOffset, SelEnd = f.NameEnd });
            foreach (var c in o.Children) s.Children.Add(SymbolOf(c));
            return s;
        }

        /// <summary>Foldable ranges as (startLine, endLine, kind).</summary>
        public List<(int, int, string)> Folding()
        {
            var list = new List<(int, int, string)>();
            // Braces (objects, handlers, functions), from the tokens so it works while the file doesn't parse.
            var stack = new Stack<int>();
            foreach (var tok in _doc.Tokens)
            {
                if (tok.Type == TokenType.LBrace || tok.Type == TokenType.LBracket) stack.Push(tok.Offset);
                else if ((tok.Type == TokenType.RBrace || tok.Type == TokenType.RBracket) && stack.Count > 0)
                {
                    int a = _doc.PositionOf(stack.Pop()).line, b = _doc.PositionOf(tok.Offset).line;
                    if (b > a) list.Add((a, b - 1, null));
                }
            }
            // Runs of // comment lines.
            var lines = _doc.Text.Split('\n');
            int start = -1;
            for (int i = 0; i <= lines.Length; i++)
            {
                bool c = i < lines.Length && lines[i].TrimStart().StartsWith("//");
                if (c && start < 0) start = i;
                if (!c && start >= 0) { if (i - 1 > start) list.Add((start, i - 1, "comment")); start = -1; }
            }
            return list;
        }
    }
}
