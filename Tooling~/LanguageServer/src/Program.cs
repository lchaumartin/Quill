// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quill.LanguageServer
{
    /// <summary>
    /// Language Server Protocol over stdio (JSON-RPC with Content-Length framing). One process per
    /// editor window; the same server backs VS Code, Rider (via LSP4IJ) and Visual Studio.
    /// </summary>
    public static class Program
    {
        public const string Version = "0.1.0";

        private static Stream _out;
        private static readonly Workspace Ws = new Workspace();
        private static bool _shutdown;
        private static bool _canRegisterWatchers;

        public static int Main(string[] args)
        {
            if (args.Contains("--version")) { Console.WriteLine(Version); return 0; }
            if (args.Length >= 2 && args[0] == "--check") return Check(args.Skip(1).ToArray());
            if (args.Contains("--schema-check")) return SchemaCheck();

            var input = Console.OpenStandardInput();
            _out = Console.OpenStandardOutput();
            while (true)
            {
                JsonObject msg;
                try { msg = Read(input); }
                catch (Exception e) { Log("read failed: " + e.Message); return 1; }
                if (msg == null) return _shutdown ? 0 : 1;
                try { Handle(msg); }
                catch (Exception e)
                {
                    Log("handler failed: " + e);
                    if (msg["id"] != null)
                        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = msg["id"]?.DeepClone(), ["error"] = new JsonObject { ["code"] = -32603, ["message"] = e.Message } });
                }
                if (_exit) return _shutdown ? 0 : 1;
            }
        }

        private static bool _exit;

        /// <summary>`--check <folder or files>`: print diagnostics like a compiler (CI / quick checks).</summary>
        private static int Check(string[] paths)
        {
            int problems = 0;
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) Ws.AddRoot(p);
                else if (File.Exists(p)) Ws.AddRoot(Path.GetDirectoryName(Path.GetFullPath(p)));
            }
            foreach (var d in Ws.Docs.Values.OrderBy(d => d.Path, StringComparer.Ordinal))
            {
                if (paths.Any(File.Exists) && !paths.Any(p => Workspace.Normalize(p) == d.Path)) continue;
                foreach (var diag in new Analysis(Ws, d).Diagnose())
                {
                    var (l, c) = d.PositionOf(diag.Start);
                    Console.WriteLine($"{d.Path}({l + 1},{c + 1}): {(diag.Severity == 1 ? "error" : diag.Severity == 2 ? "warning" : "info")} {diag.Code}: {diag.Message}");
                    if (diag.Severity <= 2) problems++;
                }
            }
            Console.WriteLine($"{Ws.Docs.Count} file(s), {problems} problem(s).");
            return problems == 0 ? 0 : 2;
        }

        /// <summary>Every element the engine registers is described, with every property it seeds.</summary>
        private static int SchemaCheck()
        {
            int bad = 0;
            foreach (var name in QuillTypeRegistry.Names)
            {
                if (!Schema.Elements.TryGetValue(name, out var info)) { Console.WriteLine($"missing element {name}"); bad++; continue; }
                var item = QuillTypeRegistry.Create(name);
                foreach (var p in item.Properties)
                    if (!info.Props.ContainsKey(p.Name)) { Console.WriteLine($"{name}.{p.Name} not in schema"); bad++; }
            }
            Console.WriteLine(bad == 0 ? "schema ok" : $"{bad} schema problem(s)");
            return bad == 0 ? 0 : 1;
        }

        // ---- Transport ----------------------------------------------------------------------------

        private static JsonObject Read(Stream s)
        {
            int length = -1;
            while (true)
            {
                var line = ReadLine(s);
                if (line == null) return null;
                if (line.Length == 0) break;
                int i = line.IndexOf(':');
                if (i > 0 && line.Substring(0, i).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(line.Substring(i + 1).Trim());
            }
            if (length < 0) return null;
            var buf = new byte[length];
            int read = 0;
            while (read < length)
            {
                int n = s.Read(buf, read, length - read);
                if (n <= 0) return null;
                read += n;
            }
            return JsonNode.Parse(buf) as JsonObject;
        }

        private static string ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (b == '\n') return sb.ToString().TrimEnd('\r');
                sb.Append((char)b);
            }
        }

        private static readonly object SendLock = new object();

        private static void Send(JsonObject msg)
        {
            var body = Encoding.UTF8.GetBytes(msg.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            lock (SendLock)
            {
                _out.Write(header, 0, header.Length);
                _out.Write(body, 0, body.Length);
                _out.Flush();
            }
        }

        private static void Reply(JsonNode id, JsonNode result)
            => Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

        private static void Notify(string method, JsonNode prms)
            => Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = prms });

        private static void Log(string text) => Console.Error.WriteLine("[quill-ls] " + text);

        // ---- Dispatch ----------------------------------------------------------------------------

        private static void Handle(JsonObject msg)
        {
            string method = msg["method"]?.GetValue<string>();
            var id = msg["id"];
            var p = msg["params"] as JsonObject;
            if (method == null) return;   // a response to one of our requests

            switch (method)
            {
                case "initialize": Reply(id, Initialize(p)); break;
                case "initialized": RegisterWatchers(); break;
                case "shutdown": _shutdown = true; Reply(id, null); break;
                case "exit": _exit = true; break;

                case "textDocument/didOpen":
                {
                    var td = p["textDocument"];
                    var uri = td["uri"].GetValue<string>();
                    EnsureProjectRoot(uri);
                    Ws.Open(uri, td["text"].GetValue<string>(), td["version"]?.GetValue<int>() ?? 0);
                    PublishAll();
                    break;
                }
                case "textDocument/didChange":
                {
                    var td = p["textDocument"];
                    var changes = p["contentChanges"] as JsonArray;
                    var text = changes?.LastOrDefault()?["text"]?.GetValue<string>();
                    if (text != null) Ws.Change(td["uri"].GetValue<string>(), text, td["version"]?.GetValue<int>() ?? 0);
                    PublishAll();
                    break;
                }
                case "textDocument/didClose":
                {
                    var uri = p["textDocument"]["uri"].GetValue<string>();
                    Ws.Close(uri);
                    Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() });
                    PublishAll();
                    break;
                }
                case "textDocument/didSave": break;
                case "workspace/didChangeWatchedFiles":
                {
                    foreach (var ch in (p["changes"] as JsonArray) ?? new JsonArray())
                        Ws.FileChanged(ch["uri"].GetValue<string>(), ch["type"]?.GetValue<int>() == 3);
                    PublishAll();
                    break;
                }
                case "workspace/didChangeWorkspaceFolders":
                {
                    foreach (var f in (p["event"]?["added"] as JsonArray) ?? new JsonArray())
                        Ws.AddRoot(Workspace.UriToPath(f["uri"].GetValue<string>()));
                    PublishAll();
                    break;
                }

                case "textDocument/completion": Reply(id, Completion(p)); break;
                case "textDocument/hover": Reply(id, Hover(p)); break;
                case "textDocument/definition": Reply(id, Definition(p)); break;
                case "textDocument/documentSymbol": Reply(id, Symbols(p)); break;
                case "textDocument/foldingRange": Reply(id, Folding(p)); break;

                default:
                    if (id != null)
                        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found: " + method } });
                    break;
            }
        }

        private static JsonObject Initialize(JsonObject p)
        {
            if (p?["workspaceFolders"] is JsonArray folders)
                foreach (var f in folders) Ws.AddRoot(Workspace.UriToPath(f["uri"].GetValue<string>()));
            else if (p?["rootUri"] != null && p["rootUri"].GetValueKind() == JsonValueKind.String)
                Ws.AddRoot(Workspace.UriToPath(p["rootUri"].GetValue<string>()));
            else if (p?["rootPath"] != null && p["rootPath"].GetValueKind() == JsonValueKind.String)
                Ws.AddRoot(p["rootPath"].GetValue<string>());

            _canRegisterWatchers = p?["capabilities"]?["workspace"]?["didChangeWatchedFiles"]?["dynamicRegistration"]?.GetValue<bool>() == true;
            Log($"initialized with {Ws.Roots.Count} root(s), {Ws.Docs.Count} .quill file(s)");

            return new JsonObject
            {
                ["capabilities"] = new JsonObject
                {
                    ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1, ["save"] = false },
                    ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(".") },
                    ["hoverProvider"] = true,
                    ["definitionProvider"] = true,
                    ["documentSymbolProvider"] = true,
                    ["foldingRangeProvider"] = true,
                    ["workspace"] = new JsonObject
                    {
                        ["workspaceFolders"] = new JsonObject { ["supported"] = true, ["changeNotifications"] = true },
                    },
                },
                ["serverInfo"] = new JsonObject { ["name"] = "quill-language-server", ["version"] = Version },
            };
        }

        private static void RegisterWatchers()
        {
            if (!_canRegisterWatchers) return;
            Send(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "register-watchers",
                ["method"] = "client/registerCapability",
                ["params"] = new JsonObject
                {
                    ["registrations"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "quill-files",
                        ["method"] = "workspace/didChangeWatchedFiles",
                        ["registerOptions"] = new JsonObject
                        {
                            ["watchers"] = new JsonArray(new JsonObject { ["globPattern"] = "**/*.quill" }, new JsonObject { ["globPattern"] = "**/*.ui" }),
                        },
                    }),
                },
            });
        }

        /// <summary>A file opened outside any root: index its Unity project (the folder with Assets/ and ProjectSettings/).</summary>
        private static void EnsureProjectRoot(string uri)
        {
            var path = Workspace.Normalize(Workspace.UriToPath(uri));
            if (Ws.Roots.Any(r => path.StartsWith(r + "/", StringComparison.Ordinal))) return;
            var dir = Path.GetDirectoryName(path);
            for (var d = dir; d != null; d = Path.GetDirectoryName(d))
                if (Directory.Exists(Path.Combine(d, "Assets")) && Directory.Exists(Path.Combine(d, "ProjectSettings")))
                {
                    Ws.AddRoot(d);
                    return;
                }
            // Not in a Unity project: at least its own folder.
            if (dir != null) Ws.AddRoot(dir);
        }

        // ---- Features ----------------------------------------------------------------------------

        private static void PublishAll()
        {
            foreach (var d in Ws.Docs.Values.Where(d => d.Open).ToList())
            {
                var arr = new JsonArray();
                foreach (var diag in new Analysis(Ws, d).Diagnose())
                    arr.Add(new JsonObject
                    {
                        ["range"] = Range(d, diag.Start, diag.End),
                        ["severity"] = diag.Severity,
                        ["code"] = diag.Code,
                        ["source"] = "quill",
                        ["message"] = diag.Message,
                    });
                Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = d.Uri, ["version"] = d.Version, ["diagnostics"] = arr });
            }
        }

        private static (QuillDoc doc, int offset) At(JsonObject p)
        {
            var doc = Ws.Get(p["textDocument"]["uri"].GetValue<string>());
            if (doc == null) return (null, 0);
            int line = p["position"]?["line"]?.GetValue<int>() ?? 0;
            int ch = p["position"]?["character"]?.GetValue<int>() ?? 0;
            return (doc, doc.OffsetOf(line, ch));
        }

        private static JsonNode Completion(JsonObject p)
        {
            var (doc, off) = At(p);
            var arr = new JsonArray();
            if (doc == null) return arr;
            foreach (var it in new Features(Ws, ForCompletion(doc, off)).Complete(off))
            {
                var o = new JsonObject { ["label"] = it.Label, ["kind"] = it.Kind };
                if (it.Detail != null) o["detail"] = it.Detail;
                if (!string.IsNullOrEmpty(it.Doc)) o["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = it.Doc };
                if (it.Insert != null) o["insertText"] = it.Insert;
                if (it.Snippet) o["insertTextFormat"] = 2;
                if (it.SortText != null) o["sortText"] = it.SortText;
                arr.Add(o);
            }
            return new JsonObject { ["isIncomplete"] = false, ["items"] = arr };
        }

        /// <summary>
        /// Mid-edit the text rarely parses (`text: volume.`, a half-typed member). Try completing a
        /// placeholder at the cursor so the tree is current; otherwise the document's last good tree.
        /// </summary>
        private static QuillDoc ForCompletion(QuillDoc doc, int offset)
        {
            if (doc.Error == null) return doc;
            foreach (var patch in new[] { "__qc", "__qc: 0", "__qc {}", "__qc)" })
            {
                var d = new QuillDoc(doc.Path, doc.Uri, doc.Text.Insert(offset, patch));
                if (d.Error == null) return d;
            }
            return doc;
        }

        private static JsonNode Hover(JsonObject p)
        {
            var (doc, off) = At(p);
            if (doc == null) return null;
            var h = new Features(Ws, doc).Hover(off);
            if (h == null) return null;
            return new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = h.Value.markdown },
                ["range"] = Range(doc, h.Value.start, h.Value.end),
            };
        }

        private static JsonNode Definition(JsonObject p)
        {
            var (doc, off) = At(p);
            var arr = new JsonArray();
            if (doc == null) return arr;
            foreach (var l in new Features(Ws, doc).Definition(off))
                arr.Add(new JsonObject { ["uri"] = l.Doc.Uri, ["range"] = Range(l.Doc, l.Start, l.End) });
            return arr;
        }

        private static JsonNode Symbols(JsonObject p)
        {
            var doc = Ws.Get(p["textDocument"]["uri"].GetValue<string>());
            var arr = new JsonArray();
            if (doc == null) return arr;
            foreach (var s in new Features(Ws, doc).Symbols()) arr.Add(SymbolJson(doc, s));
            return arr;
        }

        private static JsonObject SymbolJson(QuillDoc doc, Symbol s)
        {
            var o = new JsonObject
            {
                ["name"] = s.Name,
                ["kind"] = s.Kind,
                ["range"] = Range(doc, s.Start, Math.Max(s.Start, s.End)),
                ["selectionRange"] = Range(doc, s.SelStart, Math.Max(s.SelStart, s.SelEnd)),
            };
            if (!string.IsNullOrEmpty(s.Detail)) o["detail"] = s.Detail;
            if (s.Children.Count > 0)
            {
                var ch = new JsonArray();
                foreach (var c in s.Children) ch.Add(SymbolJson(doc, c));
                o["children"] = ch;
            }
            return o;
        }

        private static JsonNode Folding(JsonObject p)
        {
            var doc = Ws.Get(p["textDocument"]["uri"].GetValue<string>());
            var arr = new JsonArray();
            if (doc == null) return arr;
            foreach (var (a, b, kind) in new Features(Ws, doc).Folding())
            {
                var o = new JsonObject { ["startLine"] = a, ["endLine"] = b };
                if (kind != null) o["kind"] = kind;
                arr.Add(o);
            }
            return arr;
        }

        private static JsonObject Range(QuillDoc d, int start, int end)
        {
            var (l1, c1) = d.PositionOf(start);
            var (l2, c2) = d.PositionOf(end);
            return new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = l1, ["character"] = c1 },
                ["end"] = new JsonObject { ["line"] = l2, ["character"] = c2 },
            };
        }
    }
}
