// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using Quill.Parsing;

namespace Quill
{
    /// <summary>
    /// Base of every instantiated Quill element. Holds a bag of reactive properties (keyed by name),
    /// a parent/child tree, an optional id, signal handlers and declared functions. Concrete elements
    /// (<see cref="QuillItem"/>, <see cref="QuillRectangle"/>) register the properties they understand.
    /// </summary>
    public class QuillObject
    {
        public string TypeName;
        public string Id;
        public string OnProperty;   // from `NumberAnimation on phase { ... }`
        public QuillObject Parent;
        public readonly List<QuillObject> Children = new List<QuillObject>();

        /// <summary>
        /// Non-null on a component instance root: the id scope for that component's internals, so
        /// two instances of the same component don't collide on internal ids.
        /// </summary>
        public Dictionary<string, QuillObject> LocalIds;

        /// <summary>
        /// Compiled Quill signal handlers keyed by `on&lt;Signal&gt;` (e.g. "onClicked"). They receive the
        /// signal's arguments (null when it has none).
        /// </summary>
        public readonly Dictionary<string, System.Action<object[]>> Handlers
            = new Dictionary<string, System.Action<object[]>>();

        /// <summary>Parameter names of declared (and built-in) signals, keyed by bare signal name.</summary>
        public readonly Dictionary<string, string[]> SignalParams = new Dictionary<string, string[]>();

        /// <summary>`function name(...) { }` declarations, callable as `id.name(...)` or `name(...)` in scope.</summary>
        public readonly Dictionary<string, FunctionDecl> Functions = new Dictionary<string, FunctionDecl>();

        /// <summary>The list property this object was declared in (`states`, `transitions`), or null.</summary>
        public string AssignedTo;

        // C#-side subscribers, keyed the same way. Multiple listeners are multicast.
        private Dictionary<string, System.Action> _connections;
        private Dictionary<string, System.Action<object[]>> _argConnections;

        private readonly Dictionary<string, QuillProperty> _props = new Dictionary<string, QuillProperty>();
        private HashSet<string> _groups;   // "border" for "border.color", "a" and "a.b" for "a.b.c"

        /// <summary>Run the Quill handler and any C# subscribers for a signal (e.g. "onClicked").</summary>
        public void Emit(string handlerKey) => Emit(handlerKey, null);

        /// <summary>Run the Quill handler and C# subscribers for a signal, passing its arguments.</summary>
        public virtual void Emit(string handlerKey, object[] args)
        {
            if (Handlers.TryGetValue(handlerKey, out var h)) h?.Invoke(args);
            if (_connections != null && _connections.TryGetValue(handlerKey, out var c)) c?.Invoke();
            if (_argConnections != null && _argConnections.TryGetValue(handlerKey, out var ac)) ac?.Invoke(args);
        }

        /// <summary>Subscribe C# to a Quill signal by its bare name (e.g. "clicked", "toggled").</summary>
        public void Connect(string signal, System.Action callback)
        {
            string key = HandlerKey(signal);
            _connections ??= new Dictionary<string, System.Action>();
            _connections.TryGetValue(key, out var cur);
            _connections[key] = cur + callback;
        }

        /// <summary>Subscribe C# to a Quill signal and receive its arguments (e.g. `moved(real value)`).</summary>
        public void Connect(string signal, System.Action<object[]> callback)
        {
            string key = HandlerKey(signal);
            _argConnections ??= new Dictionary<string, System.Action<object[]>>();
            _argConnections.TryGetValue(key, out var cur);
            _argConnections[key] = cur + callback;
        }

        public void Disconnect(string signal, System.Action callback)
        {
            if (_connections == null) return;
            string key = HandlerKey(signal);
            if (_connections.TryGetValue(key, out var cur)) _connections[key] = cur - callback;
        }

        public void Disconnect(string signal, System.Action<object[]> callback)
        {
            if (_argConnections == null) return;
            string key = HandlerKey(signal);
            if (_argConnections.TryGetValue(key, out var cur)) _argConnections[key] = cur - callback;
        }

        /// <summary>"clicked" → "onClicked" (an "onX" key is returned unchanged).</summary>
        public static string HandlerKey(string signal)
        {
            if (string.IsNullOrEmpty(signal)) return signal;
            if (signal.Length > 2 && signal[0] == 'o' && signal[1] == 'n' && char.IsUpper(signal[2]))
                return signal;   // already an "onX" key
            return "on" + char.ToUpperInvariant(signal[0]) + signal.Substring(1);
        }

        /// <summary>
        /// Built-in methods callable from Quill (`anim.start()`, `timer.restart()`). Return true if
        /// <paramref name="name"/> was handled.
        /// </summary>
        public virtual bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            return false;
        }

        /// <summary>Get an existing property or create it on demand (for dynamic/declared props).</summary>
        public QuillProperty Property(string name)
        {
            if (!_props.TryGetValue(name, out var p))
            {
                p = new QuillProperty(this, name);
                _props[name] = p;
                RegisterGroups(name);
            }
            return p;
        }

        /// <summary>Make <paramref name="name"/> an alias of another property cell (`property alias`).</summary>
        public void AliasProperty(string name, QuillProperty target)
        {
            _props[name] = target;
            RegisterGroups(name);
        }

        private void RegisterGroups(string name)
        {
            int dot = name.IndexOf('.');
            if (dot < 0) return;
            _groups ??= new HashSet<string>();
            while (dot >= 0)
            {
                _groups.Add(name.Substring(0, dot));
                dot = name.IndexOf('.', dot + 1);
            }
        }

        public bool HasProperty(string name) => _props.ContainsKey(name);

        /// <summary>True if the object has dotted properties under <paramref name="prefix"/> (e.g. "border").</summary>
        public bool HasGroup(string prefix) => _groups != null && _groups.Contains(prefix);

        /// <summary>Property on this object only, or null.</summary>
        public QuillProperty FindProperty(string name)
            => _props.TryGetValue(name, out var p) ? p : null;

        /// <summary>Property on this object or any ancestor (lexical scope), or null.</summary>
        public QuillProperty FindPropertyInScope(string name)
        {
            for (var o = this; o != null; o = o.Parent)
                if (o._props.TryGetValue(name, out var p)) return p;
            return null;
        }

        public IEnumerable<QuillProperty> Properties => _props.Values;

        /// <summary>Property names, aliases included.</summary>
        public IEnumerable<string> PropertyNames => _props.Keys;

        public void AddChild(QuillObject child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public void InsertChild(int index, QuillObject child)
        {
            child.Parent = this;
            if (index < 0 || index > Children.Count) Children.Add(child);
            else Children.Insert(index, child);
        }

        // Convenience typed reads used by the renderer (raw, untracked).
        public float Num(string name, float fallback = 0f)
        {
            var p = FindProperty(name);
            return p != null ? QuillConvert.ToFloat(p.Raw) : fallback;
        }

        public bool Flag(string name, bool fallback = true)
        {
            var p = FindProperty(name);
            return p != null ? QuillConvert.ToBool(p.Raw) : fallback;
        }

        public string Str(string name, string fallback = "")
        {
            var p = FindProperty(name);
            return p != null ? QuillConvert.ToStr(p.Raw) : fallback;
        }
    }
}
