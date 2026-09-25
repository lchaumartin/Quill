// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quill.Parsing;

namespace Quill.LanguageServer
{
    /// <summary>A property, signal, function or method of a type, and where it is declared.</summary>
    public sealed class Member
    {
        public string Name, Kind, Type, Doc, Value;   // Kind: property | signal | function | method
        public string[] Params = new string[0];
        public bool ReadOnly;
        public QuillDoc Source;                         // null for built-ins
        public int Offset = -1, End = -1;
    }

    /// <summary>Everything a type offers: built-in schema and/or the API of its component file(s).</summary>
    public sealed class TypeApi
    {
        public string Name;
        public bool Known, Open;
        public ElementInfo Builtin;
        public readonly List<QuillDoc> Components = new List<QuillDoc>();
        public readonly Dictionary<string, Member> Props = new Dictionary<string, Member>();
        public readonly Dictionary<string, Member> Signals = new Dictionary<string, Member>();
        public readonly Dictionary<string, Member> Functions = new Dictionary<string, Member>();

        public string Doc
        {
            get
            {
                if (Builtin != null) return Builtin.Doc;
                var c = Components.FirstOrDefault();
                return c?.HeaderComment() ?? "";
            }
        }

        /// <summary>Property names are also groups (`anchors`, `font`, `border`…) for their dotted members.</summary>
        public bool HasGroup(string prefix) => Props.Keys.Any(k => k.StartsWith(prefix + ".", StringComparison.Ordinal));

        public Member Find(string name)
            => Props.TryGetValue(name, out var p) ? p
             : Functions.TryGetValue(name, out var f) ? f
             : Signals.TryGetValue(name, out var s) ? s : null;
    }

    /// <summary>
    /// Every .quill file under the workspace roots (Assets, embedded packages, the package cache), the
    /// ones open in the editor, and the type model built from them: built-in elements plus one
    /// component per file name.
    /// </summary>
    public sealed class Workspace
    {
        public readonly Dictionary<string, QuillDoc> Docs = new Dictionary<string, QuillDoc>(StringComparer.Ordinal);
        public readonly List<string> Roots = new List<string>();
        private readonly Dictionary<string, TypeApi> _apis = new Dictionary<string, TypeApi>();

        public static readonly string[] Extensions = { ".quill", ".ui" };

        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", "node_modules", "Temp", "Logs", "obj", "bin", "Build", "Builds", "UserSettings", "MemoryCaptures",
        };

        public static string Normalize(string path) => System.IO.Path.GetFullPath(path).Replace('\\', '/');

        public void AddRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            root = Normalize(root);
            if (Roots.Contains(root)) return;
            Roots.Add(root);
            Scan(root, 0);
            Invalidate();
        }

        private void Scan(string dir, int depth)
        {
            if (depth > 24) return;
            string name = System.IO.Path.GetFileName(dir);
            if (depth > 0 && SkipDirs.Contains(name)) return;
            // Unity's Library holds the package cache (installed packages) and nothing else of use.
            if (depth > 0 && name.Equals("Library", StringComparison.OrdinalIgnoreCase))
            {
                var cache = System.IO.Path.Combine(dir, "PackageCache");
                if (Directory.Exists(cache)) Scan(cache, depth + 1);
                return;
            }
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (IsQuillFile(f)) LoadFromDisk(f);
                foreach (var d in Directory.EnumerateDirectories(dir))
                    Scan(d, depth + 1);
            }
            catch (Exception) { /* unreadable folder: skip */ }
        }

        public static bool IsQuillFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path);
            if (ext.Equals(".quill", StringComparison.OrdinalIgnoreCase)) return true;
            // Legacy .ui: only if it doesn't look like Qt Designer XML.
            if (ext.Equals(".ui", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var r = new StreamReader(path);
                    var head = new char[64];
                    int n = r.Read(head, 0, head.Length);
                    return !new string(head, 0, n).TrimStart().StartsWith("<");
                }
                catch { return false; }
            }
            return false;
        }

        private void LoadFromDisk(string path)
        {
            path = Normalize(path);
            if (Docs.TryGetValue(path, out var existing) && existing.Open) return;
            try { Docs[path] = new QuillDoc(path, PathToUri(path), File.ReadAllText(path)); }
            catch (Exception) { }
        }

        // ---- Editor-driven changes ----------------------------------------------------------------

        public QuillDoc Open(string uri, string text, int version)
        {
            var path = Normalize(UriToPath(uri));
            if (!Docs.TryGetValue(path, out var d)) Docs[path] = d = new QuillDoc(path, uri, text);
            else { d.Uri = uri; d.SetText(text); }
            d.Open = true;
            d.Version = version;
            Invalidate();
            return d;
        }

        public QuillDoc Change(string uri, string text, int version)
        {
            var d = Get(uri);
            if (d == null) return Open(uri, text, version);
            d.SetText(text);
            d.Version = version;
            Invalidate();
            return d;
        }

        public void Close(string uri)
        {
            var d = Get(uri);
            if (d == null) return;
            d.Open = false;
            if (File.Exists(d.Path)) LoadFromDiskForce(d.Path);
            else Docs.Remove(d.Path);
            Invalidate();
        }

        public void FileChanged(string uri, bool deleted)
        {
            var path = Normalize(UriToPath(uri));
            if (Docs.TryGetValue(path, out var d) && d.Open) return;
            if (deleted) Docs.Remove(path);
            else if (IsQuillFile(path)) LoadFromDiskForce(path);
            Invalidate();
        }

        private void LoadFromDiskForce(string path)
        {
            try { Docs[path] = new QuillDoc(path, PathToUri(path), File.ReadAllText(path)); }
            catch (Exception) { Docs.Remove(path); }
        }

        public QuillDoc Get(string uri)
        {
            if (uri == null) return null;
            var path = Normalize(UriToPath(uri));
            return Docs.TryGetValue(path, out var d) ? d : null;
        }

        public void Invalidate() => _apis.Clear();

        // ---- Types -------------------------------------------------------------------------------

        /// <summary>Component files defining a type, the most relevant first (same folder, then the built-in theme).</summary>
        public List<QuillDoc> ComponentFiles(string type, QuillDoc near = null)
        {
            var list = Docs.Values.Where(d => d.Name == type).ToList();
            string nearDir = near != null ? System.IO.Path.GetDirectoryName(near.Path) : null;
            return list.OrderBy(d => nearDir != null && System.IO.Path.GetDirectoryName(d.Path) == nearDir ? 0
                                    : d.Path.Contains("/QuillThemes/Slate/") ? 1 : 2)
                       .ThenBy(d => d.Path, StringComparer.Ordinal).ToList();
        }

        public IEnumerable<string> ComponentNames()
            => Docs.Values.Select(d => d.Name).Where(QuillThemesNames.IsTypeName).Distinct();

        public TypeApi Api(string type) => Api(type, new HashSet<string>());

        private TypeApi Api(string type, HashSet<string> visiting)
        {
            if (type == null) return new TypeApi { Name = "?" };
            if (_apis.TryGetValue(type, out var cached)) return cached;
            var api = new TypeApi { Name = type };
            if (!visiting.Add(type)) return api;   // a cycle: stop

            if (Schema.Elements.TryGetValue(type, out var b))
            {
                api.Known = true;
                api.Builtin = b;
                api.Open = b.Open;
                foreach (var p in b.Props.Values)
                    api.Props[p.Name] = new Member { Name = p.Name, Kind = "property", Type = p.Type, Doc = p.Doc, ReadOnly = p.ReadOnly };
                foreach (var s in b.Signals.Values)
                    api.Signals[s.Name] = new Member { Name = s.Name, Kind = "signal", Doc = s.Doc, Params = s.Params };
                foreach (var m in b.Methods.Values)
                    api.Functions[m.Name] = new Member { Name = m.Name, Kind = "method", Type = m.Signature, Doc = m.Doc };
            }
            else
            {
                // Every file of that name (themes each have one): their APIs are merged, so a property
                // valid in any theme is known. The preferred file (Slate) wins for docs and locations.
                var files = ComponentFiles(type).Where(d => d.Root != null).ToList();
                // Open (any property accepted) only if every variant is: Frost's glass Button is a
                // ShaderEffect underneath, but a Button is still a Button.
                api.Open = files.Count > 0;
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    var f = files[i];
                    api.Known = true;
                    api.Components.Insert(0, f);
                    var baseApi = Api(f.Root.TypeName, visiting);
                    api.Open &= baseApi.Open;
                    foreach (var kv in baseApi.Props) api.Props[kv.Key] = kv.Value;
                    foreach (var kv in baseApi.Signals) api.Signals[kv.Key] = kv.Value;
                    foreach (var kv in baseApi.Functions) api.Functions[kv.Key] = kv.Value;
                    AddDeclared(api, f, f.Root);
                }
                if (!api.Known) api.Open = true;   // unknown type: don't cascade warnings
            }
            visiting.Remove(type);
            _apis[type] = api;
            return api;
        }

        /// <summary>An object's own declarations (properties, signals, functions) as members.</summary>
        public static void AddDeclared(TypeApi api, QuillDoc doc, ObjectNode node)
        {
            foreach (var p in node.Properties)
            {
                if (!p.IsDeclaration) continue;
                string doc1 = doc.TrailingComment(p.NameOffset);
                if (doc1.Length == 0) doc1 = doc.CommentAbove(p.NameOffset);
                api.Props[p.Name] = new Member
                {
                    Name = p.Name, Kind = "property", Type = p.DeclaredType, Doc = doc1, Source = doc,
                    Offset = p.NameOffset, End = p.NameEnd,
                    Value = p.ValueStart >= 0 ? doc.Slice(p.ValueStart, p.ValueEnd).Trim() : null,
                };
            }
            foreach (var (name, offset, end) in node.SignalDecls)
            {
                node.SignalParams.TryGetValue(name, out var ps);
                api.Signals[name] = new Member { Name = name, Kind = "signal", Params = ps ?? new string[0], Source = doc, Offset = offset, End = end, Doc = doc.TrailingComment(offset) };
            }
            foreach (var f in node.Functions)
                api.Functions[f.Name] = new Member { Name = f.Name, Kind = "function", Params = f.Params, Source = doc, Offset = f.NameOffset, End = f.NameEnd, Doc = doc.CommentAbove(f.NameOffset) };
        }

        /// <summary>The API of one object: its type's, plus what it declares itself.</summary>
        public TypeApi ObjectApi(QuillDoc doc, Obj o)
        {
            var baseApi = Api(o.Type);
            var hasOwn = o.Node.Properties.Any(p => p.IsDeclaration) || o.Node.SignalDecls.Count > 0 || o.Node.Functions.Count > 0;
            if (!hasOwn) return baseApi;
            var api = new TypeApi { Name = baseApi.Name, Known = baseApi.Known, Open = baseApi.Open, Builtin = baseApi.Builtin };
            api.Components.AddRange(baseApi.Components);
            foreach (var kv in baseApi.Props) api.Props[kv.Key] = kv.Value;
            foreach (var kv in baseApi.Signals) api.Signals[kv.Key] = kv.Value;
            foreach (var kv in baseApi.Functions) api.Functions[kv.Key] = kv.Value;
            AddDeclared(api, doc, o.Node);
            return api;
        }

        // ---- URIs --------------------------------------------------------------------------------

        public static string UriToPath(string uri)
        {
            if (uri == null) return null;
            if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return uri;
            var u = new Uri(uri);
            var p = Uri.UnescapeDataString(u.AbsolutePath);
            // file:///c%3A/x on Windows arrives as "/c:/x".
            if (p.Length > 2 && p[0] == '/' && p[2] == ':') p = p.Substring(1);
            return p;
        }

        public static string PathToUri(string path)
        {
            path = path.Replace('\\', '/');
            if (!path.StartsWith("/")) path = "/" + path;   // C:/x -> /C:/x
            return "file://" + string.Join("/", path.Split('/').Select(seg => Uri.EscapeDataString(seg).Replace("%3A", ":")));
        }
    }

    /// <summary>Component file names become type names only if they look like one (matches the runtime's QuillThemes).</summary>
    public static class QuillThemesNames
    {
        public static bool IsTypeName(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }
    }
}
