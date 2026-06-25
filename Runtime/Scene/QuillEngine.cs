// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
using System.Collections.Generic;
using Quill.Parsing;

namespace Quill
{
    /// <summary>
    /// Loads Quill text, instantiates the element tree, wires reactive bindings, and drives the
    /// dirty-binding queue. This is the public entry point most code uses.
    ///
    /// Instantiation is two-pass: first every object is created and every id registered, then
    /// bindings are attached. That ordering lets a binding reference an id declared later in the
    /// file without the resolution failing.
    /// </summary>
    public sealed class QuillEngine
    {
        public QuillItem Root { get; private set; }

        private readonly Dictionary<string, QuillObject> _ids = new Dictionary<string, QuillObject>();
        private readonly Dictionary<string, ObjectNode> _components = new Dictionary<string, ObjectNode>();
        private readonly List<(ObjectNode node, QuillItem obj)> _pairs = new List<(ObjectNode, QuillItem)>();
        private readonly List<QuillAnimation> _animations = new List<QuillAnimation>();
        private readonly List<QuillMouseArea> _mouseAreas = new List<QuillMouseArea>();
        private readonly Queue<Binding> _dirty = new Queue<Binding>();

        // Pointer state.
        private QuillMouseArea _hovered, _grabber;
        private bool _wasDown;
        private double _ptrX = double.NaN, _ptrY = double.NaN;

        public QuillObject FindId(string id)
            => _ids.TryGetValue(id, out var o) ? o : null;

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

        /// <summary>
        /// Register a reusable component type (one per .ui file). After this, the document can use
        /// <paramref name="typeName"/> like a built-in element (e.g. <c>Slider { value: 0.5 }</c>).
        /// Call before <see cref="LoadFromSource"/>.
        /// </summary>
        public void RegisterComponent(string typeName, string source)
        {
            try { _components[typeName] = Parser.Parse(source); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Component '{typeName}' failed to parse: {e.Message}");
            }
        }

        /// <summary>Parse + instantiate a Quill document from source text.</summary>
        public void LoadFromSource(string source)
        {
            _ids.Clear();
            _pairs.Clear();
            _animations.Clear();
            _mouseAreas.Clear();
            _hovered = _grabber = null;
            _wasDown = false;
            _dirty.Clear();

            RegisterBuiltins();   // Easing.* and Animation.Infinite must resolve during wiring

            var rootNode = Parser.Parse(source);
            Root = Instantiate(rootNode, null);
            SetupAnchorLines();   // anchor-line props must exist before bindings/resolution read them
            WireBindings();       // attach document bindings (incl. anchors.* inputs)
            SetupPositioners();   // Row/Column/Grid position their children
            ResolveAnchors();     // derive x/y/width/height from any declared anchors
            SetupHandlers();      // compile MouseArea signal handlers
            Flush();
        }

        /// <summary>Pseudo-objects so Quill can write `Easing.InOutQuad` and `Animation.Infinite`.</summary>
        private void RegisterBuiltins()
        {
            var easing = new QuillObject { TypeName = "Easing" };
            foreach (var name in new[]
            {
                "Linear", "InQuad", "OutQuad", "InOutQuad", "InCubic", "OutCubic",
                "InOutCubic", "InSine", "OutSine", "InOutSine", "OutBack"
            })
                easing.Property(name).SetValue(name);
            _ids["Easing"] = easing;

            var anim = new QuillObject { TypeName = "Animation" };
            anim.Property("Infinite").SetValue(-1.0);
            _ids["Animation"] = anim;

            // Math.* functions are handled by the expression evaluator; PI/E are constants.
            var math = new QuillObject { TypeName = "Math" };
            math.Property("PI").SetValue(System.Math.PI);
            math.Property("E").SetValue(System.Math.E);
            _ids["Math"] = math;
        }

        private void SetupPositioners()
        {
            foreach (var (_, obj) in _pairs)
                Positioners.Setup(this, obj);
        }

        // ---- Signal handlers (any object) ------------------------------------------------------

        private void SetupHandlers()
        {
            foreach (var (node, obj) in _pairs)
                foreach (var prop in node.Properties)
                    if (prop.Handler != null)
                        obj.Handlers[prop.Name] = CompileHandler(obj, prop.Handler);
        }

        private System.Action CompileHandler(QuillObject owner, List<HandlerStmt> stmts)
        {
            return () =>
            {
                var ctx = new EvalContext(this, owner);
                for (int i = 0; i < stmts.Count; i++)
                {
                    switch (stmts[i])
                    {
                        case AssignStmt a:
                            var target = ResolveLValue(owner, a.Path);
                            if (target != null) target.SetValue(a.Value.Eval(ctx));
                            break;
                        case CallStmt c:
                            EmitSignal(owner, c.Path);
                            break;
                    }
                }
            };
        }

        // `parent.clicked()` -> emit "clicked" on the object the path resolves to (before the call).
        private void EmitSignal(QuillObject self, string[] path)
        {
            string sig = path[path.Length - 1];
            QuillObject target = self;
            if (path.Length > 1)
            {
                target = ResolveObject(self, path[0]);
                for (int i = 1; i < path.Length - 1 && target != null; i++)
                    target = target.FindProperty(path[i])?.Raw as QuillObject;
            }
            target?.Emit("on" + char.ToUpperInvariant(sig[0]) + sig.Substring(1));
        }

        // Resolve the property an assignment writes to: `name`, `id.name`, or `parent.name`.
        private QuillProperty ResolveLValue(QuillObject self, string[] path)
        {
            if (path.Length == 1)
                return self.FindPropertyInScope(path[0]) ?? self.Property(path[0]);

            QuillObject obj = ResolveObject(self, path[0]);
            for (int i = 1; i < path.Length - 1 && obj != null; i++)
                obj = obj.FindProperty(path[i])?.Raw as QuillObject;

            return obj?.Property(path[path.Length - 1]);
        }

        // Resolve the first segment of a path to an object: `parent`, a local id, or a global id.
        private QuillObject ResolveObject(QuillObject self, string name)
        {
            if (name == "parent") return self.Parent;
            for (var o = self; o != null; o = o.Parent)
                if (o.LocalIds != null && o.LocalIds.TryGetValue(name, out var local))
                    return local;
            return FindId(name);
        }

        private void SetupAnchorLines()
        {
            // Parent-first (the order items were added to _pairs) so a child's lines can read its
            // parent's lines as it is created.
            foreach (var (_, obj) in _pairs)
                Anchors.SetupLines(this, obj);
        }

        private void ResolveAnchors()
        {
            foreach (var (_, obj) in _pairs)
                Anchors.Resolve(this, obj);
        }

        private QuillItem Instantiate(ObjectNode node, QuillItem parent)
        {
            // A registered component type expands into its own tree.
            if (_components.ContainsKey(node.TypeName))
                return InstantiateComponent(node, parent);

            var obj = QuillTypeRegistry.Create(node.TypeName);
            obj.OnProperty = node.OnProperty;

            parent?.AddChild(obj);            // attach first so id registration sees the scope
            RegisterIdAndCollect(node, obj);
            DeclareProperties(node, obj);
            _pairs.Add((node, obj));
            InstantiateChildren(node, obj);
            return obj;
        }

        /// <summary>
        /// Instantiate a component instance: build the component's internal tree in its own id scope,
        /// then apply the use-site's id, property overrides, and extra children onto the root.
        /// </summary>
        private QuillItem InstantiateComponent(ObjectNode useSite, QuillItem parent)
        {
            var compNode = _components[useSite.TypeName];

            var root = QuillTypeRegistry.Create(compNode.TypeName);
            root.LocalIds = new Dictionary<string, QuillObject>();   // component-private id scope
            root.OnProperty = useSite.OnProperty;
            if (root is QuillAnimation a2) _animations.Add(a2);
            if (root is QuillMouseArea m2) _mouseAreas.Add(m2);

            parent?.AddChild(root);            // attach first so the use-site id resolves to the outer scope

            // The component's own root id lives in its local scope; the use-site id in the outer scope.
            if (!string.IsNullOrEmpty(compNode.Id)) root.LocalIds[compNode.Id] = root;
            root.Id = useSite.Id;
            if (!string.IsNullOrEmpty(useSite.Id)) RegisterId(root, useSite.Id);

            // Declared properties from the component (its public API) and any added at the use-site.
            DeclareProperties(compNode, root);
            DeclareProperties(useSite, root);

            // Component internals wire first (defaults), then use-site overrides win.
            _pairs.Add((compNode, root));
            _pairs.Add((useSite, root));

            InstantiateChildren(compNode, root);   // internal tree (ids -> root.LocalIds)
            InstantiateChildren(useSite, root);    // children added at the use-site
            return root;
        }

        private void InstantiateChildren(ObjectNode node, QuillItem obj)
        {
            foreach (var child in node.Children)
            {
                if (child.TypeName == "Repeater")
                    ExpandRepeater(child, obj);
                else
                    Instantiate(child, obj);
            }
        }

        private void RegisterIdAndCollect(ObjectNode node, QuillItem obj)
        {
            obj.Id = node.Id;
            if (!string.IsNullOrEmpty(node.Id)) RegisterId(obj, node.Id);
            if (obj is QuillAnimation anim) _animations.Add(anim);
            if (obj is QuillMouseArea area) _mouseAreas.Add(area);
        }

        // Register an id in the nearest enclosing component scope, or globally if there is none.
        private void RegisterId(QuillObject obj, string id)
        {
            for (var o = obj.Parent; o != null; o = o.Parent)
                if (o.LocalIds != null) { o.LocalIds[id] = obj; return; }
            _ids[id] = obj;
        }

        // Create declared properties up-front with type defaults, so scope resolution sees them
        // before any binding runs.
        private static void DeclareProperties(ObjectNode node, QuillItem obj)
        {
            foreach (var prop in node.Properties)
                if (prop.IsDeclaration)
                    obj.Property(prop.Name).SetValue(DefaultForType(prop.DeclaredType));
        }

        /// <summary>
        /// Repeater: instantiate its delegate `model` times as children of the Repeater's parent,
        /// injecting a per-instance <c>index</c>. The generated items are ordinary scene items, so
        /// they participate in anchors, positioners, animations, and rendering like anything else.
        /// </summary>
        private void ExpandRepeater(ObjectNode rep, QuillItem parent)
        {
            ObjectNode delegateNode = rep.Children.Count > 0 ? rep.Children[0] : null;
            if (delegateNode == null) return;

            int count = EvalModelCount(rep);
            for (int i = 0; i < count; i++)
            {
                var inst = Instantiate(delegateNode, parent);
                inst.Property("index").SetValue((double)i);
            }
        }

        private int EvalModelCount(ObjectNode rep)
        {
            PropertyNode model = null;
            foreach (var p in rep.Properties)
                if (p.Name == "model") { model = p; break; }
            if (model?.Value == null) return 0;

            try
            {
                int n = (int)System.Math.Round(ConstEval(model.Value));
                return n < 0 ? 0 : n;
            }
            catch
            {
                UnityEngine.Debug.LogWarning(
                    "[Quill] Repeater 'model' must be a constant expression for now (e.g. model: 500).");
                return 0;
            }
        }

        // Evaluate a constant numeric expression (no identifiers) for Repeater models.
        private static double ConstEval(ExprNode n)
        {
            switch (n)
            {
                case NumberNode num: return num.Value;
                case UnaryNode u: return -ConstEval(u.Operand);
                case BinaryNode b:
                    double l = ConstEval(b.Left), r = ConstEval(b.Right);
                    switch (b.Op)
                    {
                        case TokenType.Plus: return l + r;
                        case TokenType.Minus: return l - r;
                        case TokenType.Star: return l * r;
                        case TokenType.Slash: return r == 0 ? 0 : l / r;
                        case TokenType.Percent: return r == 0 ? 0 : l % r;
                        default: throw new System.InvalidOperationException();
                    }
                default: throw new System.InvalidOperationException();
            }
        }

        private void WireBindings()
        {
            foreach (var (node, obj) in _pairs)
            {
                foreach (var prop in node.Properties)
                {
                    if (prop.Value == null) continue; // declaration with no initializer

                    var ctx = new EvalContext(this, obj);
                    var ast = prop.Value;
                    var binding = new Binding(this, () => ast.Eval(ctx));
                    obj.Property(prop.Name).SetBinding(binding);
                }
            }
        }

        private static object DefaultForType(string declaredType)
        {
            switch (declaredType)
            {
                case "real":
                case "double":
                case "int": return 0.0;
                case "bool": return false;
                case "string": return "";
                case "color": return "white";
                default: return null; // var / unknown
            }
        }

        // ---- Dirty-binding queue ---------------------------------------------------------------

        internal void Enqueue(Binding b) => _dirty.Enqueue(b);

        /// <summary>
        /// Per-frame tick: process the pointer (hover/press/click), advance animations, then recompute
        /// bindings. <paramref name="px"/>/<paramref name="py"/> are in surface pixels (top-left origin).
        /// </summary>
        public void Update(double dtSeconds, double px, double py, bool pointerDown)
        {
            ProcessPointer(px, py, pointerDown);
            for (int i = 0; i < _animations.Count; i++)
                Animations.Advance(_animations[i], dtSeconds);
            Flush();
        }

        /// <summary>Animation-only tick (no pointer), for headless/non-interactive use.</summary>
        public void Update(double dtSeconds)
        {
            for (int i = 0; i < _animations.Count; i++)
                Animations.Advance(_animations[i], dtSeconds);
            Flush();
        }

        // ---- Pointer state machine -------------------------------------------------------------

        private void ProcessPointer(double px, double py, bool down)
        {
            bool moved = !double.IsNaN(_ptrX) && (px != _ptrX || py != _ptrY);
            _ptrX = px; _ptrY = py;

            var hit = HitTest(px, py);

            // Keep area-local mouseX/mouseY fresh on whatever area receives events this frame
            // (the area under the pointer, and the press grabber if we're dragging).
            if (hit != null) SetLocalPointer(hit, px, py);
            if (_grabber != null && _grabber != hit) SetLocalPointer(_grabber, px, py);

            // Hover enter/exit.
            if (hit != _hovered)
            {
                if (_hovered != null)
                {
                    _hovered.Property("containsMouse").SetValue(false);
                    if (_hovered.HoverEnabled) _hovered.Emit("onExited");
                }
                _hovered = hit;
                if (_hovered != null)
                {
                    _hovered.Property("containsMouse").SetValue(true);
                    if (_hovered.HoverEnabled) _hovered.Emit("onEntered");
                }
            }

            // Press / release / click (click = press and release over the same area).
            if (down && !_wasDown)
            {
                _grabber = hit;
                if (_grabber != null)
                {
                    _grabber.Property("pressed").SetValue(true);
                    _grabber.Emit("onPressed");
                }
            }
            else if (!down && _wasDown)
            {
                if (_grabber != null)
                {
                    _grabber.Property("pressed").SetValue(false);
                    _grabber.Emit("onReleased");
                    if (hit == _grabber) _grabber.Emit("onClicked");
                    _grabber = null;
                }
            }

            if (moved)
            {
                if (_grabber != null) _grabber.Emit("onPositionChanged");
                else if (_hovered != null && _hovered.HoverEnabled) _hovered.Emit("onPositionChanged");
            }

            _wasDown = down;
        }

        private static void SetLocalPointer(QuillMouseArea a, double px, double py)
        {
            a.Property("mouseX").SetValue(px - a.AbsX());
            a.Property("mouseY").SetValue(py - a.AbsY());
        }

        // Topmost enabled, visible MouseArea containing the point (later in tree order = on top).
        private QuillMouseArea HitTest(double px, double py)
        {
            for (int i = _mouseAreas.Count - 1; i >= 0; i--)
            {
                var a = _mouseAreas[i];
                if (!a.Enabled || !a.EffectiveVisible()) continue;
                float ax = a.AbsX(), ay = a.AbsY(), aw = a.Num("width"), ah = a.Num("height");
                if (px >= ax && px <= ax + aw && py >= ay && py <= ay + ah) return a;
            }
            return null;
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
            if (obj is QuillItem item && item.EffectiveVisible())
                output.Add(item);

            // Parents are added before children, so children composite on top — matching the tree.
            for (int i = 0; i < obj.Children.Count; i++)
                CollectRecursive(obj.Children[i], output);
        }
    }
}
