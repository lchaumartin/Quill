// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Quill.Parsing;

namespace Quill.LanguageServer
{
    public sealed class Diagnostic
    {
        public int Start, End, Severity;   // 1 error, 2 warning, 3 information, 4 hint
        public string Message, Code;
    }

    /// <summary>What a name in an expression refers to.</summary>
    public sealed class Resolution
    {
        public string Kind;       // local | member | id | global | enum
        public Member Member;     // for member
        public Obj Obj;           // for id (the object), member (the object it was found on)
        public string Name;
    }

    /// <summary>A value, handler or function body: where it is, which object it belongs to, its locals.</summary>
    public sealed class CodeSpan
    {
        public Obj Owner;
        public int Start, End;
        public HashSet<string> Locals = new HashSet<string>();
    }

    /// <summary>Name resolution and diagnostics over one document, against the workspace's type model.</summary>
    public sealed class Analysis
    {
        private readonly Workspace _ws;
        private readonly QuillDoc _doc;

        public Analysis(Workspace ws, QuillDoc doc)
        {
            _ws = ws;
            _doc = doc;
        }

        // ---- Code spans ---------------------------------------------------------------------------

        /// <summary>Every expression / handler / function body in the document, with its locals.</summary>
        public List<CodeSpan> Spans()
        {
            var list = new List<CodeSpan>();
            foreach (var o in _doc.Objects)
            {
                var api = _ws.ObjectApi(_doc, o);
                foreach (var p in o.Node.Properties)
                {
                    if (p.ValueStart < 0 || p.Value == null && p.Handler == null) continue;
                    var span = new CodeSpan { Owner = o, Start = p.ValueStart, End = p.ValueEnd };
                    if (p.Handler != null)
                    {
                        var sig = SignalForHandler(api, p.Name);
                        if (sig != null) foreach (var n in sig.Params) span.Locals.Add(n);
                    }
                    AddDeclaredLocals(span);
                    list.Add(span);
                }
                foreach (var f in o.Node.Functions)
                {
                    var span = new CodeSpan { Owner = o, Start = f.BodyStart, End = f.BodyEnd };
                    foreach (var n in f.Params) span.Locals.Add(n);
                    AddDeclaredLocals(span);
                    list.Add(span);
                }
            }
            return list;
        }

        // `var x`, `let x`, `const x` (and `for (var i …)`) inside a span.
        private void AddDeclaredLocals(CodeSpan span)
        {
            var t = _doc.Tokens;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i].Offset < span.Start) continue;
                if (t[i].Offset >= span.End) break;
                if (t[i].Type == TokenType.Identifier && (t[i].Text == "var" || t[i].Text == "let" || t[i].Text == "const")
                    && t[i + 1].Type == TokenType.Identifier)
                    span.Locals.Add(t[i + 1].Text);
            }
        }

        public CodeSpan SpanAt(int offset)
            => Spans().Where(s => offset >= s.Start && offset <= s.End).OrderBy(s => s.End - s.Start).FirstOrDefault();

        /// <summary>The signal an `onSomething` handler name refers to, if any.</summary>
        public static Member SignalForHandler(TypeApi api, string handlerName)
        {
            string last = handlerName.Substring(handlerName.LastIndexOf('.') + 1);
            if (last.Length < 3 || !last.StartsWith("on")) return null;
            string sig = char.ToLowerInvariant(last[2]) + last.Substring(3);
            return api.Signals.TryGetValue(sig, out var s) ? s : null;
        }

        // ---- Name resolution ---------------------------------------------------------------------

        public Resolution Resolve(string name, Obj at, CodeSpan span)
        {
            if (span != null && span.Locals.Contains(name)) return new Resolution { Kind = "local", Name = name };
            if (name == "parent") return new Resolution { Kind = "global", Name = name };
            for (var o = at; o != null; o = o.Parent)
            {
                var api = _ws.ObjectApi(_doc, o);
                var m = api.Find(name);
                if (m != null) return new Resolution { Kind = "member", Member = m, Obj = o, Name = name };
                if (api.HasGroup(name)) return new Resolution { Kind = "member", Obj = o, Name = name, Member = new Member { Name = name, Kind = "group" } };
            }
            if (_doc.Ids.TryGetValue(name, out var idObj)) return new Resolution { Kind = "id", Obj = idObj, Name = name };
            if (Schema.Enums.ContainsKey(name)) return new Resolution { Kind = "enum", Name = name };
            if (Schema.Globals.ContainsKey(name)) return new Resolution { Kind = "global", Name = name };
            return null;
        }

        /// <summary>The API behind a resolved name, when it names an object (an id, `parent`, `Theme`).</summary>
        public TypeApi ApiOf(Resolution r, Obj at)
        {
            if (r == null) return null;
            if (r.Kind == "id") return _ws.ObjectApi(_doc, r.Obj);
            if (r.Kind == "global" && r.Name == "parent") return at?.Parent != null ? _ws.ObjectApi(_doc, at.Parent) : null;
            if (r.Kind == "global" && r.Name == "Theme") return _ws.Api("Theme");
            return null;
        }

        // ---- Diagnostics -------------------------------------------------------------------------

        private bool? _isComponent;

        /// <summary>This file is used as a component by another file.</summary>
        public bool IsComponent => _isComponent ??= _ws.Docs.Values.Any(d => d != _doc && d.Objects.Any(o => o.Type == _doc.Name));

        public List<Diagnostic> Diagnose()
        {
            var list = DiagnoseUnsorted();
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        private List<Diagnostic> DiagnoseUnsorted()
        {
            var list = new List<Diagnostic>();
            if (_doc.Error != null)
            {
                int s = _doc.Error.Offset >= 0 ? _doc.Error.Offset : 0;
                int e = _doc.Error.End > s ? _doc.Error.End : s + 1;
                list.Add(new Diagnostic { Start = s, End = e, Severity = 1, Message = CleanMessage(_doc.Error.Message), Code = "syntax" });
                return list;
            }

            var seenIds = new Dictionary<string, int>();
            foreach (var o in _doc.Objects)
            {
                var n = o.Node;
                var api = _ws.ObjectApi(_doc, o);

                if (!api.Known)
                    list.Add(new Diagnostic
                    {
                        Start = n.TypeOffset, End = n.TypeOffset + n.TypeName.Length, Severity = 1, Code = "unknown-type",
                        Message = $"Unknown element '{n.TypeName}': not a built-in element, and no {n.TypeName}.quill component in the project."
                                  + Suggest(n.TypeName, Schema.Elements.Keys.Concat(_ws.ComponentNames())),
                    });

                if (!string.IsNullOrEmpty(n.Id) && n.IdOffset >= 0)
                {
                    if (seenIds.ContainsKey(n.Id))
                        list.Add(new Diagnostic { Start = n.IdOffset, End = n.IdOffset + n.Id.Length, Severity = 2, Code = "duplicate-id", Message = $"Duplicate id '{n.Id}'." });
                    else seenIds[n.Id] = n.IdOffset;
                }

                // `Behavior on prop` / `NumberAnimation on prop`: the parent must have that property.
                if (n.OnProperty != null && o.Parent != null)
                {
                    var papi = _ws.ObjectApi(_doc, o.Parent);
                    if (papi.Known && !papi.Open && !papi.Props.ContainsKey(n.OnProperty))
                    {
                        int at = _doc.Text.IndexOf(n.OnProperty, n.TypeOffset, Math.Max(0, n.BodyStart - n.TypeOffset), StringComparison.Ordinal);
                        if (at >= 0)
                            list.Add(new Diagnostic { Start = at, End = at + n.OnProperty.Length, Severity = 2, Code = "unknown-property", Message = $"{o.Parent.Type} has no property '{n.OnProperty}'." + Suggest(n.OnProperty, papi.Props.Keys) });
                    }
                }

                foreach (var p in n.Properties)
                {
                    if (p.IsDeclaration)
                    {
                        if (!Schema.PropertyTypes.Contains(p.DeclaredType) && !_ws.Api(p.DeclaredType).Known)
                        {
                            int at = _doc.Text.LastIndexOf(p.DeclaredType, p.NameOffset, StringComparison.Ordinal);
                            if (at >= 0)
                                list.Add(new Diagnostic { Start = at, End = at + p.DeclaredType.Length, Severity = 2, Code = "unknown-type", Message = $"Unknown property type '{p.DeclaredType}' (use real, int, bool, string, color, var or alias)." });
                        }
                        continue;
                    }
                    if (!api.Known) continue;

                    if (p.Handler != null)
                    {
                        if (p.Name == "Component.onCompleted" || (o.Type == "ScriptAction" && p.Name == "script")) continue;
                        if (p.Name.Contains('.')) continue;
                        if (SignalForHandler(api, p.Name) != null) continue;
                        // on<Property>Changed
                        if (p.Name.EndsWith("Changed", StringComparison.Ordinal) && p.Name.Length > "onChanged".Length)
                        {
                            string prop = char.ToLowerInvariant(p.Name[2]) + p.Name.Substring(3, p.Name.Length - 3 - "Changed".Length);
                            if (api.Props.ContainsKey(prop)) continue;
                        }
                        list.Add(new Diagnostic
                        {
                            Start = p.NameOffset, End = p.NameEnd, Severity = 2, Code = "unknown-signal",
                            Message = $"{o.Type} has no signal '{char.ToLowerInvariant(p.Name[2]) + p.Name.Substring(3)}' for '{p.Name}'."
                                      + Suggest(p.Name, api.Signals.Keys.Select(k => "on" + char.ToUpperInvariant(k[0]) + k.Substring(1))),
                        });
                        continue;
                    }

                    if (api.Open) continue;
                    if (api.Props.ContainsKey(p.Name)) continue;
                    list.Add(new Diagnostic
                    {
                        Start = p.NameOffset, End = p.NameEnd, Severity = 2, Code = "unknown-property",
                        Message = UnknownPropertyMessage(o.Type, p.Name, api),
                    });
                }
            }

            foreach (var span in Spans())
                CheckNames(span, list);
            return list;
        }

        private static string UnknownPropertyMessage(string type, string name, TypeApi api)
        {
            string msg = $"{type} has no property '{name}'.";
            var near = Closest(name, api.Props.Keys);
            if (near != null) msg += $" Did you mean '{near}'?";
            else if (!name.Contains('.')) msg += $" Declare it with 'property <type> {name}'.";
            return msg;
        }

        private void CheckNames(CodeSpan span, List<Diagnostic> list)
        {
            var t = _doc.Tokens;
            for (int i = 0; i < t.Count; i++)
            {
                var tok = t[i];
                if (tok.Type == TokenType.EOF || tok.Offset >= span.End) break;
                if (tok.Offset < span.Start || tok.Type != TokenType.Identifier) continue;
                if (i > 0 && t[i - 1].Type == TokenType.Dot) continue;               // a member: checked with its target
                if (Schema.Keywords.Contains(tok.Text)) continue;
                if (i + 1 < t.Count && t[i + 1].Type == TokenType.Identifier && (tok.Text == "var" || tok.Text == "let")) continue;

                var r = Resolve(tok.Text, span.Owner, span);
                if (r == null)
                {
                    var near = Closest(tok.Text, VisibleNames(span.Owner, span));
                    // Inside a component, a bare name may be an id of the document using it (resolved at
                    // runtime): only a note there, unless it's clearly a typo of something in scope.
                    bool component = near == null && IsComponent;
                    list.Add(new Diagnostic
                    {
                        Start = tok.Offset, End = tok.End, Severity = component ? 3 : 2, Code = "unknown-name",
                        Message = $"Unknown name '{tok.Text}'." + (near != null ? $" Did you mean '{near}'?"
                                  : component ? " Fine if it's an id in the documents that use this component." : ""),
                    });
                    continue;
                }

                // One level of members: `id.prop`, `parent.width`, `Theme.accent`, `Easing.OutCubic`.
                if (i + 2 < t.Count && t[i + 1].Type == TokenType.Dot && t[i + 2].Type == TokenType.Identifier && t[i + 2].Offset < span.End)
                {
                    var member = t[i + 2];
                    if (r.Kind == "enum")
                    {
                        if (!Schema.Enums[r.Name].ContainsKey(member.Text))
                            list.Add(new Diagnostic { Start = member.Offset, End = member.End, Severity = 2, Code = "unknown-member", Message = $"'{r.Name}' has no member '{member.Text}'." + Suggest(member.Text, Schema.Enums[r.Name].Keys) });
                        continue;
                    }
                    var api = ApiOf(r, span.Owner);
                    if (api == null || !api.Known || api.Open) continue;
                    if (api.Find(member.Text) != null || api.HasGroup(member.Text)) continue;
                    var names = api.Props.Keys.Concat(api.Functions.Keys).Concat(api.Signals.Keys);
                    string what = r.Kind == "id" ? $"'{r.Name}' ({api.Name})" : $"'{r.Name}'";
                    list.Add(new Diagnostic { Start = member.Offset, End = member.End, Severity = 2, Code = "unknown-member", Message = $"{what} has no property '{member.Text}'." + Suggest(member.Text, names) });
                }
            }
        }

        private static string Suggest(string name, IEnumerable<string> candidates)
        {
            var near = Closest(name, candidates);
            return near != null ? $" Did you mean '{near}'?" : "";
        }

        /// <summary>Every name an expression at this object could use.</summary>
        public IEnumerable<string> VisibleNames(Obj at, CodeSpan span)
        {
            var names = new HashSet<string>();
            if (span != null) names.UnionWith(span.Locals);
            for (var o = at; o != null; o = o.Parent)
            {
                var api = _ws.ObjectApi(_doc, o);
                names.UnionWith(api.Props.Keys.Where(k => !k.Contains('.')));
                names.UnionWith(api.Props.Keys.Where(k => k.Contains('.')).Select(k => k.Substring(0, k.IndexOf('.'))));
                names.UnionWith(api.Functions.Keys);
                names.UnionWith(api.Signals.Keys);
            }
            names.UnionWith(_doc.Ids.Keys);
            names.UnionWith(Schema.Globals.Keys);
            names.UnionWith(Schema.Enums.Keys);
            return names;
        }

        private static string CleanMessage(string m)
        {
            // "… (line 12)" is redundant in an editor.
            int i = m.LastIndexOf(" (line ", StringComparison.Ordinal);
            return i > 0 ? m.Substring(0, i) : m;
        }

        /// <summary>The closest candidate by edit distance (typos), or null if none is close.</summary>
        public static string Closest(string name, IEnumerable<string> candidates)
        {
            string best = null;
            int bestD = int.MaxValue;
            foreach (var c in candidates)
            {
                if (c == name) continue;
                int d = Distance(name.ToLowerInvariant(), c.ToLowerInvariant());
                if (d < bestD) { bestD = d; best = c; }
            }
            int limit = Math.Max(1, name.Length / 3);
            return bestD <= limit ? best : null;
        }

        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
