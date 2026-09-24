// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Base of every instantiated Quill element. Holds a bag of reactive properties (keyed by name),
    /// a parent/child tree, and an optional id. Concrete elements (<see cref="QuillItem"/>,
    /// <see cref="QuillRectangle"/>) register the properties they understand.
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

        /// <summary>Compiled Quill signal handlers keyed by `on<Signal>` (e.g. "onClicked").</summary>
        public readonly Dictionary<string, System.Action> Handlers = new Dictionary<string, System.Action>();

        // C#-side subscribers, keyed the same way. Multiple listeners are multicast.
        private Dictionary<string, System.Action> _connections;

        private readonly Dictionary<string, QuillProperty> _props = new Dictionary<string, QuillProperty>();

        /// <summary>Run the Quill handler and any C# subscribers for a signal (e.g. "onClicked").</summary>
        public virtual void Emit(string handlerKey)
        {
            if (Handlers.TryGetValue(handlerKey, out var h)) h?.Invoke();
            if (_connections != null && _connections.TryGetValue(handlerKey, out var c)) c?.Invoke();
        }

        /// <summary>Subscribe C# to a Quill signal by its bare name (e.g. "clicked", "toggled").</summary>
        public void Connect(string signal, System.Action callback)
        {
            string key = HandlerKey(signal);
            _connections ??= new Dictionary<string, System.Action>();
            _connections.TryGetValue(key, out var cur);
            _connections[key] = cur + callback;
        }

        public void Disconnect(string signal, System.Action callback)
        {
            if (_connections == null) return;
            string key = HandlerKey(signal);
            if (_connections.TryGetValue(key, out var cur)) _connections[key] = cur - callback;
        }

        private static string HandlerKey(string signal)
        {
            if (string.IsNullOrEmpty(signal)) return signal;
            if (signal.Length > 2 && signal[0] == 'o' && signal[1] == 'n' && char.IsUpper(signal[2]))
                return signal;   // already an "onX" key
            return "on" + char.ToUpperInvariant(signal[0]) + signal.Substring(1);
        }

        /// <summary>Get an existing property or create it on demand (for dynamic/declared props).</summary>
        public QuillProperty Property(string name)
        {
            if (!_props.TryGetValue(name, out var p))
            {
                p = new QuillProperty(this, name);
                _props[name] = p;
            }
            return p;
        }

        public bool HasProperty(string name) => _props.ContainsKey(name);

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

        public void AddChild(QuillObject child)
        {
            child.Parent = this;
            Children.Add(child);
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
    }
}
