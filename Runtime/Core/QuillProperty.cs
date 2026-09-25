// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Intercepts writes to a property — how <c>Behavior on x { ... }</c> turns a new value into an
    /// animation toward it. Return true when the write was handled (the property is then driven by
    /// the interceptor through <see cref="QuillProperty.SetAnimated"/>).
    /// </summary>
    public interface IPropertyInterceptor
    {
        bool Intercept(QuillProperty property, object newValue);
    }

    /// <summary>
    /// A single reactive property. Holds a boxed value (double / bool / string / Color / list /
    /// QuillObject). Reading through <see cref="Get"/> registers a dependency on the binding currently
    /// being evaluated (see <see cref="Binding"/>). Writing notifies every binding that depends on it.
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

        /// <summary>A <c>Behavior</c> animating writes to this property, if any.</summary>
        public IPropertyInterceptor Interceptor;

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

        /// <summary>
        /// Imperative assignment (a handler's <c>x = 5</c>, <c>engine.SetValue</c>). Removes any driving
        /// binding and notifies subscribers. A <c>Behavior</c> on the property animates to the value.
        /// </summary>
        public void SetValue(object v)
        {
            DetachDriver();
            Write(v);
        }

        /// <summary>
        /// Write an animation frame: keeps the driving binding (it still provides targets) and bypasses
        /// any interceptor. Used by animations, behaviors and transitions.
        /// </summary>
        public void SetAnimated(object v) => Assign(v);

        /// <summary>Attach an expression that drives this property. Evaluated immediately.</summary>
        public void SetBinding(Binding b)
        {
            if (_driver != b) DetachDriver();
            _driver = b;
            b.Target = this;
            b.Evaluate();
        }

        /// <summary>Drop the driving binding (if any) without changing the value.</summary>
        public void ClearBinding() => DetachDriver();

        public bool HasBinding => _driver != null;

        /// <summary>The binding driving this property, or null. States use it to restore bindings.</summary>
        internal Binding Driver => _driver;

        // A replaced binding must also stop listening: otherwise its old dependencies keep
        // re-evaluating it and it overwrites the new value (e.g. a component's default binding
        // beating the use-site override, or a binding surviving an imperative assignment).
        private void DetachDriver()
        {
            _driver?.Detach();
            _driver = null;
        }

        /// <summary>Called by the driving binding when it recomputes a new value.</summary>
        internal void AssignFromBinding(object v) => Write(v);

        private void Write(object v)
        {
            if (Interceptor != null && Interceptor.Intercept(this, v)) return;
            Assign(v);
        }

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

        internal static bool ValuesEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a is List<object> la && b is List<object> lb)
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                    if (!ValuesEqual(la[i], lb[i])) return false;
                return true;
            }
            return a.Equals(b);
        }
    }
}
