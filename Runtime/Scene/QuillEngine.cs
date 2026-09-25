// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using Quill.Parsing;

namespace Quill
{
    /// <summary>
    /// Loads Quill text, instantiates the element tree, wires reactive bindings, and drives the
    /// dirty-binding queue, animations, timers and input. This is the public entry point most code
    /// uses.
    ///
    /// Instantiation is two-pass: first every object is created and every id registered, then
    /// bindings are attached. That ordering lets a binding reference an id declared later in the
    /// file without the resolution failing. The same pipeline builds Repeater delegates created at
    /// runtime (see QuillEngine.Build.cs).
    /// </summary>
    public sealed partial class QuillEngine
    {
        public QuillItem Root { get; private set; }

        private readonly Dictionary<string, QuillObject> _ids = new Dictionary<string, QuillObject>();
        private readonly Dictionary<string, ObjectNode> _components = new Dictionary<string, ObjectNode>();
        private readonly List<(ObjectNode node, QuillItem obj)> _pairs = new List<(ObjectNode, QuillItem)>();
        private readonly Queue<Binding> _dirty = new Queue<Binding>();

        /// <summary>Seconds since the document was loaded (engine time; drives timers and double-clicks).</summary>
        public double Time { get; private set; }

        public QuillObject FindId(string id)
            => id != null && _ids.TryGetValue(id, out var o) ? o : null;

        // ---- C# interop: read/write values, subscribe to changes and signals ------------------

        /// <summary>Current value of <c>id.property</c> (untracked), or null if not found.</summary>
        public object GetValue(string id, string property)
            => FindId(id)?.FindProperty(property)?.Raw;

        public double GetNumber(string id, string property, double fallback = 0)
        {
            var p = FindId(id)?.FindProperty(property);
            return p != null ? QuillConvert.ToDouble(p.Raw) : fallback;
        }

        public bool GetBool(string id, string property, bool fallback = false)
        {
            var p = FindId(id)?.FindProperty(property);
            return p != null ? QuillConvert.ToBool(p.Raw) : fallback;
        }

        public string GetString(string id, string property)
            => QuillConvert.ToStr(FindId(id)?.FindProperty(property)?.Raw);

        /// <summary>Set <c>id.property</c> from C# (clears any binding on it, like an assignment).</summary>
        public void SetValue(string id, string property, object value)
            => FindId(id)?.Property(property).SetValue(value);

        /// <summary>Call back whenever <c>id.property</c> changes. The arg is the new raw value.</summary>
        public void OnChanged(string id, string property, System.Action<object> callback)
        {
            var obj = FindId(id);
            if (obj == null)
            {
                UnityEngine.Debug.LogWarning($"[Quill] OnChanged: no element with id '{id}' (call after LoadFromSource).");
                return;
            }
            // FindProperty (not Property) so a misspelled name warns instead of silently never firing.
            var p = obj.FindProperty(property);
            if (p == null)
            {
                UnityEngine.Debug.LogWarning($"[Quill] OnChanged: '{id}' has no property '{property}'.");
                return;
            }
            p.Changed += () => callback(p.Raw);
        }

        /// <summary>Subscribe C# to a Quill signal on a document-level id, e.g. Connect("playBtn","clicked", ...).</summary>
        public void Connect(string id, string signal, System.Action callback)
        {
            var obj = FindId(id);
            if (obj == null)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Connect: no element with id '{id}' (call after LoadFromSource).");
                return;
            }
            obj.Connect(signal, callback);
        }

        /// <summary>Subscribe C# to a Quill signal and receive its arguments, e.g. `moved(real value)`.</summary>
        public void Connect(string id, string signal, System.Action<object[]> callback)
        {
            var obj = FindId(id);
            if (obj == null)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Connect: no element with id '{id}' (call after LoadFromSource).");
                return;
            }
            obj.Connect(signal, callback);
        }

        /// <summary>Call a Quill <c>function</c> (or built-in method) on a document-level id from C#.</summary>
        public object Invoke(string id, string function, params object[] args)
        {
            var obj = FindId(id);
            if (obj == null)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Invoke: no element with id '{id}'.");
                return null;
            }
            var result = CallMethod(obj, function, args ?? new object[0]);
            Flush();
            return result;
        }

        /// <summary>
        /// Register a reusable component type (one per .quill file). After this, the document can use
        /// <paramref name="typeName"/> like a built-in element (e.g. <c>Slider { value: 0.5 }</c>).
        /// Call before <see cref="LoadFromSource"/>.
        /// </summary>
        public void RegisterComponent(string typeName, string source)
        {
            if (typeName == ThemeName)
            {
                SetTheme(source);
                return;
            }
            try { _components[typeName] = Parser.Parse(source); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Component '{typeName}' failed to parse: {e.Message}");
            }
        }

        /// <summary>The global the theme palette is exposed as (<c>Theme.accent</c>, <c>Theme.radius</c>…).</summary>
        public const string ThemeName = "Theme";

        private ObjectNode _themeNode;

        /// <summary>
        /// Set the theme palette: a small document whose root declares the theme's properties
        /// (<c>QtObject { property color accent: "#3a86ff" … }</c>). Every document and component can
        /// read them through the global <c>Theme</c> — <c>color: Theme.accent</c> — and they stay
        /// reactive, so palette properties may bind to each other. A file named <c>Theme.quill</c> passed to
        /// <see cref="RegisterComponent"/> lands here, so registering a theme folder sets both its
        /// controls and its palette. Call before <see cref="LoadFromSource"/>.
        /// </summary>
        public void SetTheme(string source)
        {
            try { _themeNode = source != null ? Parser.Parse(source) : null; }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Theme failed to parse: {e.Message}");
            }
        }

        /// <summary>Parse + instantiate a Quill document from source text.</summary>
        public void LoadFromSource(string source)
        {
            ResetState();
            RegisterBuiltins();   // Easing.*, Animation.Infinite, Text.*, Qt.* must resolve during wiring

            // The theme palette: built like any object, but outside the tree (never drawn), and
            // registered as a global so every scope — components included — resolves `Theme`.
            if (_themeNode != null)
                _ids[ThemeName] = Instantiate(_themeNode, null, -1);

            var rootNode = Parser.Parse(source);
            Root = Instantiate(rootNode, null, -1);
            BuildRange(0);
        }

        /// <summary>Pseudo-objects for enums and constants: `Easing.InOutQuad`, `Text.AlignHCenter`, …</summary>
        private void RegisterBuiltins()
        {
            QuillObject MakeEnum(string name, params (string, object)[] values)
            {
                var o = new QuillObject { TypeName = name };
                foreach (var (k, v) in values) o.Property(k).SetValue(v);
                _ids[name] = o;
                return o;
            }

            var easing = new QuillObject { TypeName = "Easing" };
            foreach (var name in Easing.AllNames()) easing.Property(name).SetValue(name);
            _ids["Easing"] = easing;

            MakeEnum("Animation", ("Infinite", -1.0));
            // Math.* functions are handled by the expression evaluator; PI/E are constants.
            MakeEnum("Math", ("PI", System.Math.PI), ("E", System.Math.E), ("SQRT2", System.Math.Sqrt(2)),
                         ("LN2", System.Math.Log(2)), ("LN10", System.Math.Log(10)));

            var align = new (string, object)[]
            {
                ("AlignLeft", 1.0), ("AlignRight", 2.0), ("AlignHCenter", 4.0), ("AlignJustify", 8.0),
                ("AlignTop", 32.0), ("AlignBottom", 64.0), ("AlignVCenter", 128.0), ("AlignCenter", 132.0),
            };
            var text = MakeEnum("Text", align);
            foreach (var (k, v) in new (string, object)[]
            {
                ("NoWrap", 0.0), ("WordWrap", 1.0), ("WrapAnywhere", 3.0), ("Wrap", 4.0),
                ("ElideNone", 0.0), ("ElideLeft", 1.0), ("ElideMiddle", 2.0), ("ElideRight", 3.0),
            })
                text.Property(k).SetValue(v);

            var qt = MakeEnum("Qt", align);
            foreach (var (k, v) in new (string, object)[]
            {
                ("LeftButton", 1.0), ("RightButton", 2.0), ("MiddleButton", 4.0),
                ("Horizontal", 1.0), ("Vertical", 2.0),
            })
                qt.Property(k).SetValue(v);

            MakeEnum("Drag", ("XAxis", 1.0), ("YAxis", 2.0), ("XAndYAxis", 3.0));
            MakeEnum("Font", ("MixedCase", 0.0), ("AllUppercase", 1.0), ("AllLowercase", 2.0),
                             ("SmallCaps", 3.0), ("Capitalize", 4.0));
        }

        // ---- Functions, methods, signals -------------------------------------------------------

        private int _callDepth;

        /// <summary>
        /// `obj.name(args)`: a declared <c>function</c>, else a built-in method (<c>anim.start()</c>),
        /// else a signal emit (<c>control.clicked()</c>, <c>slider.moved(0.5)</c>).
        /// </summary>
        public object CallMethod(QuillObject obj, string name, object[] args)
        {
            if (obj == null) return null;
            if (obj.Functions.TryGetValue(name, out var fn)) return InvokeFunction(obj, fn, args);
            if (obj.TryInvokeMethod(this, name, args, out var result)) return result;
            obj.Emit(QuillObject.HandlerKey(name), args);
            return null;
        }

        /// <summary>A bare `name(args)`: the nearest function or signal of that name in lexical scope.</summary>
        public bool TryCallScopedFunction(QuillObject self, string name, object[] args, out object result)
        {
            for (var o = self; o != null; o = o.Parent)
            {
                if (o.Functions.TryGetValue(name, out var fn))
                {
                    result = InvokeFunction(o, fn, args);
                    return true;
                }
                if (o.SignalParams.ContainsKey(name))
                {
                    o.Emit(QuillObject.HandlerKey(name), args);
                    result = null;
                    return true;
                }
            }
            result = null;
            return false;
        }

        private object InvokeFunction(QuillObject owner, FunctionDecl fn, object[] args)
        {
            if (_callDepth > 64)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Function '{fn.Name}' recursed too deeply; stopped.");
                return null;
            }
            var ctx = new EvalContext(this, owner) { Locals = new Dictionary<string, object>() };
            for (int i = 0; i < fn.Params.Length; i++)
                ctx.Locals[fn.Params[i]] = args != null && i < args.Length ? args[i] : null;
            _callDepth++;
            try { return Interpreter.Run(fn.Body, ctx); }
            finally { _callDepth--; }
        }

        /// <summary>Run a signal handler with its parameters bound as locals.</summary>
        private void RunHandler(QuillObject owner, string key, List<HandlerStmt> body, object[] args)
        {
            var ctx = new EvalContext(this, owner) { Locals = new Dictionary<string, object>() };
            if (args != null && args.Length > 0 && key.Length > 2 && key.StartsWith("on"))
            {
                string sig = char.ToLowerInvariant(key[2]) + key.Substring(3);
                if (owner.SignalParams.TryGetValue(sig, out var names))
                    for (int i = 0; i < names.Length && i < args.Length; i++) ctx.Locals[names[i]] = args[i];
            }
            try { Interpreter.Run(body, ctx); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Handler '{key}' on {owner.TypeName}{(owner.Id != null ? " '" + owner.Id + "'" : "")} threw: {e.Message}");
            }
        }

        /// <summary>Run a ScriptAction's `script`.</summary>
        internal void RunScript(QuillAnimation el) => el.Emit("script");

        // ---- Dirty-binding queue ---------------------------------------------------------------

        internal void Enqueue(Binding b) => _dirty.Enqueue(b);

        /// <summary>
        /// Per-frame tick: process the pointer (hover/press/click/drag/wheel), advance timers and
        /// animations, then recompute bindings. <paramref name="px"/>/<paramref name="py"/> are in
        /// surface pixels (top-left origin).
        /// </summary>
        public void Update(double dtSeconds, double px, double py, bool pointerDown)
            => Update(dtSeconds, px, py, pointerDown, 0, 0);

        /// <summary>
        /// As <see cref="Update(double,double,double,bool)"/>, plus wheel input in QML angle-delta units
        /// (120 per notch; positive y = away from the user).
        /// </summary>
        public void Update(double dtSeconds, double px, double py, bool pointerDown, double wheelX, double wheelY)
        {
            Time += dtSeconds;
            ProcessPointer(px, py, pointerDown, wheelX, wheelY);
            Flush();
            AdvanceTime(dtSeconds);
            Flush();
        }

        /// <summary>Animation-only tick (no pointer), for headless/non-interactive use.</summary>
        public void Update(double dtSeconds)
        {
            Time += dtSeconds;
            AdvanceTime(dtSeconds);
            Flush();
        }

        /// <summary>Recompute every invalidated binding. Call once per frame (and after load).</summary>
        public void Flush()
        {
            int guard = 0;
            while (_dirty.Count > 0)
            {
                if (++guard > 200000)
                {
                    UnityEngine.Debug.LogWarning("[Quill] Binding flush exceeded guard — possible cyclic binding.");
                    _dirty.Clear();
                    break;
                }
                _dirty.Dequeue().Evaluate();
            }
        }

        // ---- Renderer interface ----------------------------------------------------------------

        /// <summary>
        /// Depth-first list of every visible item in back-to-front draw order. The renderer
        /// dispatches each to the right layer (rectangle / text / image) by type.
        /// </summary>
        public void CollectVisuals(List<QuillItem> output)
        {
            output.Clear();
            if (Root != null) CollectRecursive(Root, output);
        }

        private static void CollectRecursive(QuillObject obj, List<QuillItem> output)
        {
            if (obj is QuillNonVisual) return;
            if (obj is QuillItem item)
            {
                if (!item.Flag("visible")) return;   // a hidden item hides its subtree
                output.Add(item);
            }

            // Parents are added before children, so children composite on top — matching the tree.
            for (int i = 0; i < obj.Children.Count; i++)
                CollectRecursive(obj.Children[i], output);
        }
    }
}
