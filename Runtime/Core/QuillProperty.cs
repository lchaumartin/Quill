// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// A single reactive property. Holds a boxed value (double / bool / string / Color / QuillObject).
    /// Reading through <see cref="Get"/> registers a dependency on the binding currently being
    /// evaluated (see <see cref="Binding"/>). Writing notifies every binding that depends on it.
    ///
    /// This is the atom of Quill's reactivity: bindings observe properties, properties wake bindings.
    /// </summary>
    public sealed class QuillProperty
    {
        public string Name;
        public QuillObject Owner;

        private object _value;

        // The binding that currently *drives* this property (from `width: parent.width / 2`).
        // Cleared when the property is assigned a value imperatively.
        private Binding _driver;

        // Bindings that depend on (read) this property and must be invalidated when it changes.
        private readonly List<Binding> _subscribers = new List<Binding>();

        /// <summary>Fired after the value changes. Used by the engine / renderer to mark dirty.</summary>
        public event Action Changed;

        public QuillProperty(QuillObject owner, string name, object initial = null)
        {
            Owner = owner;
            Name = name;
            _value = initial;
        }

        /// <summary>Raw value without dependency tracking. For engine/renderer reads.</summary>
        public object Raw => _value;

        /// <summary>Tracked read. Call this from inside binding expressions.</summary>
        public object Get()
        {
            Binding.RegisterRead(this);
            return _value;
        }

        /// <summary>Imperative assignment. Removes any driving binding and notifies subscribers.</summary>
        public void SetValue(object v)
        {
            _driver = null;
            Assign(v);
        }

        /// <summary>Attach an expression that drives this property. Evaluated immediately.</summary>
        public void SetBinding(Binding b)
        {
            _driver = b;
            b.Target = this;
            b.Evaluate();
        }

        public bool HasBinding => _driver != null;

        /// <summary>Called by the driving binding when it recomputes a new value.</summary>
        internal void AssignFromBinding(object v) => Assign(v);

        private void Assign(object v)
        {
            if (ValuesEqual(_value, v)) return;
            _value = v;

            // Snapshot: subscribers may mutate the list while being invalidated.
            if (_subscribers.Count > 0)
            {
                var snapshot = _subscribers.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                    snapshot[i].Invalidate();
            }

            Changed?.Invoke();
        }

        internal void AddSubscriber(Binding b)
        {
            if (!_subscribers.Contains(b)) _subscribers.Add(b);
        }

        internal void RemoveSubscriber(Binding b)
        {
            _subscribers.Remove(b);
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }
    }
}
