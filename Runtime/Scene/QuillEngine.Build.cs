// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using Quill.Parsing;

namespace Quill
{
    // Instantiation and the build pipeline. The same pipeline runs for the whole document at load
    // and for each batch of Repeater delegates created later, so runtime-created items get anchors,
    // bindings, handlers, behaviors, states and animations exactly like the rest.
    public sealed partial class QuillEngine
    {
        private int _buildDepth;
        private readonly HashSet<(QuillObject, string)> _changeSubscriptions = new HashSet<(QuillObject, string)>();

        private void ResetState()
        {
            _ids.Clear();
            _idScope = null;
            _pairs.Clear();
            _dirty.Clear();
            _hovered = _grabber = null;
            _wasDown = false;
            _ptrX = _ptrY = double.NaN;
            _lastClickArea = null;
            _changeSubscriptions.Clear();
            _positioners.Clear();
            ResetAnimationState();
            Time = 0;
        }

        // ---- Instantiation -----------------------------------------------------------------------

        /// <param name="ownScope">
        /// Give the new item its own id scope (Repeater delegates: each instance's ids are private to
        /// it, so `id: area` inside a delegate resolves to that instance's area).
        /// </param>
        private QuillItem Instantiate(ObjectNode node, QuillItem parent, int insertIndex, bool ownScope = false)
        {
            // A registered component type expands into its own tree.
            if (_components.ContainsKey(node.TypeName))
            {
                var root = InstantiateComponent(node, parent, insertIndex, registerUseSiteId: !ownScope, useSiteInOwnScope: ownScope);
                if (ownScope && !string.IsNullOrEmpty(node.Id)) root.LocalIds[node.Id] = root;
                return root;
            }

            var obj = QuillTypeRegistry.Create(node.TypeName);
            obj.OnProperty = node.OnProperty;
            obj.AssignedTo = node.AssignedTo;

            parent?.InsertChild(insertIndex, obj);
            obj.Id = node.Id;
            var outer = _idScope;
            if (ownScope)
            {
                obj.LocalIds = new Dictionary<string, QuillObject>();
                if (!string.IsNullOrEmpty(node.Id)) obj.LocalIds[node.Id] = obj;
                _idScope = obj.LocalIds;             // the instance's ids are private to it
            }
            else if (!string.IsNullOrEmpty(node.Id)) RegisterId(obj, node.Id);
            ApplyDeclarations(node, obj);
            _pairs.Add((node, obj));

            // A Repeater keeps its delegate as a template; instances are built by the pipeline.
            if (obj is QuillRepeater rep)
            {
                rep.Delegate = node.Children.Count > 0 ? node.Children[0] : null;
                _idScope = outer;
                return obj;
            }

            InstantiateChildren(node, obj);
            _idScope = outer;
            return obj;
        }

        /// <summary>
        /// Instantiate a component instance: build the component's internal tree in its own id scope,
        /// then apply the use-site's id, declarations, property overrides, and extra children onto
        /// the root. A component whose root is itself a component extends it (one merged id scope).
        /// </summary>
        /// <param name="useSiteInOwnScope">
        /// The use-site's id and children belong to the new instance's own scope instead of the
        /// enclosing one: a Repeater delegate (private per instance), or a component whose root is
        /// another component (one merged scope).
        /// </param>
        private QuillItem InstantiateComponent(ObjectNode useSite, QuillItem parent, int insertIndex, bool registerUseSiteId,
                                               bool useSiteInOwnScope = false)
        {
            var compNode = _components[useSite.TypeName];
            var outer = _idScope;   // where the use-site was written
            QuillItem root;

            if (_components.ContainsKey(compNode.TypeName) && compNode.TypeName != useSite.TypeName)
            {
                root = InstantiateComponent(compNode, parent, insertIndex, registerUseSiteId: false, useSiteInOwnScope: true);
                if (!string.IsNullOrEmpty(compNode.Id)) root.LocalIds[compNode.Id] = root;
            }
            else
            {
                root = QuillTypeRegistry.Create(compNode.TypeName);
                root.LocalIds = new Dictionary<string, QuillObject>();   // component-private id scope
                root.OnProperty = compNode.OnProperty;
                parent?.InsertChild(insertIndex, root);

                // The component's own root id lives in its local scope; the use-site id in the outer scope.
                if (!string.IsNullOrEmpty(compNode.Id)) root.LocalIds[compNode.Id] = root;
                ApplyDeclarations(compNode, root);
                _pairs.Add((compNode, root));             // component internals wire first (defaults)
                _idScope = root.LocalIds;
                InstantiateChildren(compNode, root);      // internal tree (ids -> root.LocalIds)
                _idScope = outer;
            }

            root.Id = useSite.Id;
            root.AssignedTo = useSite.AssignedTo;
            if (useSite.OnProperty != null) root.OnProperty = useSite.OnProperty;
            if (registerUseSiteId && !string.IsNullOrEmpty(useSite.Id)) RegisterId(root, useSite.Id);

            // Use-site declarations and overrides come after the whole internal tree, so they win —
            // including over internal bindings reached through a `property alias`.
            ApplyDeclarations(useSite, root);
            _pairs.Add((useSite, root));

            // Children added at the use-site were written in the enclosing scope, so their ids land
            // there (as in QML: a Panel's content can be reached from the rest of the document).
            _idScope = useSiteInOwnScope ? root.LocalIds : outer;
            InstantiateChildren(useSite, root);
            _idScope = outer;
            return root;
        }

        private void InstantiateChildren(ObjectNode node, QuillItem obj)
        {
            foreach (var child in node.Children)
                Instantiate(child, obj, -1);
        }

        // The id scope objects being instantiated register into: a component's private scope while
        // its internals are built, a Repeater delegate's own scope, or null for the document.
        private Dictionary<string, QuillObject> _idScope;

        private void RegisterId(QuillObject obj, string id)
        {
            if (_idScope != null) _idScope[id] = obj;
            else _ids[id] = obj;
        }

        // Declared properties (type defaults, so scope resolution sees them before any binding runs),
        // functions and signals.
        private static void ApplyDeclarations(ObjectNode node, QuillItem obj)
        {
            foreach (var prop in node.Properties)
                if (prop.IsDeclaration)
                {
                    if (prop.IsAlias) obj.Property(prop.Name);   // placeholder until the alias resolves
                    else obj.Property(prop.Name).SetValue(DefaultForType(prop.DeclaredType));
                }

            foreach (var fn in node.Functions) obj.Functions[fn.Name] = fn;
            foreach (var sig in node.Signals)
                obj.SignalParams[sig] = node.SignalParams.TryGetValue(sig, out var ps) ? ps : new string[0];
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
                default: return null; // var / list / unknown
            }
        }

        // ---- Pipeline ---------------------------------------------------------------------------

        /// <summary>Wire everything instantiated since <paramref name="from"/> in <see cref="_pairs"/>.</summary>
        private void BuildRange(int from)
        {
            _buildDepth++;
            try
            {
                var pairs = _pairs.GetRange(from, _pairs.Count - from);
                var objs = new List<QuillItem>(pairs.Count);
                var seen = new HashSet<QuillItem>();
                foreach (var (_, obj) in pairs)
                    if (seen.Add(obj)) objs.Add(obj);

                ResolveAliases(pairs);

                // Anchor-line props must exist before bindings/resolution read them. Parent-first.
                foreach (var obj in objs)
                    if (!(obj is QuillNonVisual)) Anchors.SetupLines(this, obj);

                WireBindings(pairs);        // attach document bindings (incl. anchors.* inputs)

                foreach (var obj in objs)   // Row/Column/Grid/Flow position their children
                    if (obj is QuillPositioner pos) Positioners.Setup(this, pos);

                foreach (var obj in objs)   // derive x/y/width/height from any declared anchors
                    if (!(obj is QuillNonVisual)) Anchors.Resolve(this, obj);

                foreach (var obj in objs)   // Text takes its content size unless sized by the document
                    if (obj is QuillText text) SetupText(text);

                var changeHandlers = SetupHandlers(pairs);   // compile signal handlers
                Flush();

                // on<Property>Changed handlers listen only once the initial values have settled —
                // as in QML, building the tree doesn't count as a change.
                foreach (var (owner, key, propName) in changeHandlers)
                    owner.Property(propName).Changed += () => owner.Emit(key);

                foreach (var obj in objs)   // build Repeater delegates (recursively runs this pipeline)
                    if (obj is QuillRepeater rep) SetupRepeater(rep);

                foreach (var obj in objs)   // after initial values settle: Behaviors, states, animations
                {
                    switch (obj)
                    {
                        case QuillBehavior b: InstallBehavior(b); break;
                        case QuillAnimation a: SetupAnimation(a); break;
                        case QuillTimer t: SetupTimer(t); break;
                    }
                }
                foreach (var obj in objs) SetupStates(obj);
                Flush();

                // Component.onCompleted — children before their parents.
                for (int i = objs.Count - 1; i >= 0; i--)
                    if (objs[i].Handlers.ContainsKey("Component.onCompleted"))
                        objs[i].Emit("Component.onCompleted");
                Flush();
            }
            finally
            {
                _buildDepth--;
                if (_buildDepth == 0) _pairs.Clear();
            }
        }

        private void ResolveAliases(List<(ObjectNode node, QuillItem obj)> pairs)
        {
            // Deepest first, so an alias to another component's alias sees the resolved cell.
            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                var (node, obj) = pairs[i];
                foreach (var prop in node.Properties)
                {
                    if (!prop.IsAlias || prop.Value == null) continue;
                    var path = FlattenPath(prop.Value);
                    if (path == null)
                    {
                        UnityEngine.Debug.LogWarning($"[Quill] property alias '{prop.Name}' must name id.property (line {prop.Line}).");
                        continue;
                    }

                    QuillObject target = ResolveObject(obj, path[0]);
                    if (target == null)
                    {
                        UnityEngine.Debug.LogWarning($"[Quill] property alias '{prop.Name}': no id '{path[0]}' in scope (line {prop.Line}).");
                        continue;
                    }
                    if (path.Count == 1) { obj.Property(prop.Name).SetValue(target); continue; }

                    int k = 1;
                    while (k < path.Count - 1 && target.FindProperty(path[k])?.Raw is QuillObject next) { target = next; k++; }
                    string rest = string.Join(".", path.GetRange(k, path.Count - k));
                    obj.AliasProperty(prop.Name, target.Property(rest));
                }
            }
        }

        private static List<string> FlattenPath(ExprNode e)
        {
            var parts = new List<string>();
            while (e is MemberNode m) { parts.Add(m.Member); e = m.Target; }
            if (!(e is IdentifierNode id)) return null;
            parts.Add(id.Name);
            parts.Reverse();
            return parts;
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

        private void WireBindings(List<(ObjectNode node, QuillItem obj)> pairs)
        {
            foreach (var (node, obj) in pairs)
            {
                foreach (var prop in node.Properties)
                {
                    if (prop.Value == null || prop.IsAlias || prop.Handler != null) continue;

                    var ctx = new EvalContext(this, obj);
                    var ast = prop.Value;
                    var binding = new Binding(this, () => ast.Eval(ctx));
                    obj.Property(prop.Name).SetBinding(binding);
                }
            }
        }

        private List<(QuillItem owner, string key, string prop)> SetupHandlers(List<(ObjectNode node, QuillItem obj)> pairs)
        {
            var changeHandlers = new List<(QuillItem, string, string)>();
            foreach (var (node, obj) in pairs)
            {
                foreach (var prop in node.Properties)
                {
                    if (prop.Handler == null) continue;
                    string key = prop.Name;
                    var owner = obj;
                    var body = prop.Handler;
                    System.Action<object[]> handler = args => RunHandler(owner, key, body, args);

                    // Internal and use-site handlers for the same signal both run, as in QML.
                    obj.Handlers[key] = obj.Handlers.TryGetValue(key, out var existing) ? existing + handler : handler;

                    // on<Property>Changed: fire when the property changes (unless it is a declared signal).
                    if (key.Length > 9 && key.StartsWith("on") && key.EndsWith("Changed") && char.IsUpper(key[2]))
                    {
                        string propName = char.ToLowerInvariant(key[2]) + key.Substring(3, key.Length - 10);
                        string sigName = char.ToLowerInvariant(key[2]) + key.Substring(3);
                        if (!obj.SignalParams.ContainsKey(sigName) && obj.HasProperty(propName)
                            && _changeSubscriptions.Add((obj, key)))
                        {
                            changeHandlers.Add((obj, key, propName));
                        }
                    }
                }
            }
            return changeHandlers;
        }

        private void SetupText(QuillText t)
        {
            // `font.pixelSize` / `font.pointSize` feed fontSize unless the document set fontSize itself.
            var size = t.Property("fontSize");
            if (!size.HasBinding)
            {
                if (t.HasProperty("font.pixelSize"))
                    size.SetBinding(new Binding(this, () => t.Property("font.pixelSize").Get()));
                else if (t.HasProperty("font.pointSize"))
                    size.SetBinding(new Binding(this, () => QuillConvert.ToDouble(t.Property("font.pointSize").Get()) * 4.0 / 3.0));
            }

            // Implicit size: the measured content, unless width/height are set or anchored.
            var w = t.Property("width");
            if (!w.HasBinding) w.SetBinding(t.ImplicitWidth = new Binding(this, () => t.Property("contentWidth").Get()));
            var h = t.Property("height");
            if (!h.HasBinding) h.SetBinding(t.ImplicitHeight = new Binding(this, () => t.Property("contentHeight").Get()));
        }

        // ---- Repeater ---------------------------------------------------------------------------

        private void SetupRepeater(QuillRepeater rep)
        {
            if (!rep.Wired)
            {
                rep.Wired = true;
                rep.Property("model").Changed += () => UpdateRepeater(rep);
            }
            UpdateRepeater(rep);
        }

        private void UpdateRepeater(QuillRepeater rep)
        {
            if (rep.Delegate == null || !(rep.Parent is QuillItem parent)) return;

            object model = rep.Property("model").Raw;
            var list = model as List<object>;
            int count;
            if (list != null) count = list.Count;
            else if (model is double || model is bool) count = (int)System.Math.Max(0, System.Math.Round(QuillConvert.ToDouble(model)));
            else count = 0;

            bool changed = false;

            // Existing instances just take their new data.
            if (list != null)
                for (int i = 0; i < rep.Instances.Count && i < count; i++)
                    rep.Instances[i].Property("modelData").SetValue(list[i]);

            while (rep.Instances.Count > count)
            {
                var last = rep.Instances[rep.Instances.Count - 1];
                rep.Instances.RemoveAt(rep.Instances.Count - 1);
                Destroy(last);
                changed = true;
            }

            if (rep.Instances.Count < count)
            {
                int insertAt = parent.Children.IndexOf(rep) + 1 + rep.Instances.Count;
                int start = _pairs.Count;
                _buildDepth++;   // keep _pairs alive while we append
                try
                {
                    for (int i = rep.Instances.Count; i < count; i++)
                    {
                        var inst = Instantiate(rep.Delegate, parent, insertAt++, ownScope: true);
                        inst.Property("index").SetValue((double)i);
                        if (list != null) inst.Property("modelData").SetValue(list[i]);
                        rep.Instances.Add(inst);
                    }
                }
                finally { _buildDepth--; }
                BuildRange(start);
                changed = true;
            }

            rep.Property("count").SetValue((double)count);
            if (changed) ChildrenChanged(parent);
        }

        /// <summary>Tear down an item and its subtree: bindings, input, animations, timers, ids.</summary>
        private void Destroy(QuillItem item)
        {
            DestroyRecursive(item);
            if (item.Parent != null)
            {
                item.Parent.Children.Remove(item);
                item.Parent = null;
            }
        }

        private void DestroyRecursive(QuillObject o)
        {
            for (int i = o.Children.Count - 1; i >= 0; i--) DestroyRecursive(o.Children[i]);

            foreach (var p in o.Properties)
            {
                p.ClearBinding();
                p.Interceptor = null;
            }

            if (ReferenceEquals(o, _hovered)) _hovered = null;
            if (ReferenceEquals(o, _grabber)) _grabber = null;
            if (ReferenceEquals(o, _lastClickArea)) _lastClickArea = null;

            switch (o)
            {
                case QuillAnimation a: StopAnimation(a, emit: false); break;
                case QuillTimer t: _activeTimers.Remove(t); break;
                case QuillBehavior b: RemoveBehavior(b); break;
                case QuillRepeater r: r.Instances.Clear(); break;
                case QuillPositioner pos: _positioners.Remove(pos); break;
            }
            RemoveStates(o);

            if (!string.IsNullOrEmpty(o.Id))
            {
                if (_ids.TryGetValue(o.Id, out var g) && ReferenceEquals(g, o)) _ids.Remove(o.Id);
                for (var p = o.Parent; p != null; p = p.Parent)
                    if (p.LocalIds != null && p.LocalIds.TryGetValue(o.Id, out var l) && ReferenceEquals(l, o))
                        p.LocalIds.Remove(o.Id);
            }
        }

        /// <summary>A parent's child list changed at runtime (Repeater): re-run its layout.</summary>
        private void ChildrenChanged(QuillItem parent)
        {
            if (parent is QuillPositioner pos) Positioners.ChildrenChanged(this, pos);
        }
    }
}
