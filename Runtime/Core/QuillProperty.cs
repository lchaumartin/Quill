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

        // The value. Numbers are kept unboxed in _number (with _value caching their box, made only
        // when something asks for the object form), so writing and reading numbers allocates nothing.
        private object _value;
        private double _number;
        private bool _isNumber;

        // The binding that currently *drives* this property (from `width: parent.width / 2`).
        // Cleared when the property is assigned a value imperatively.
        private Binding _driver;

        // Bindings that depend on this property, in subscription order (created on first use: most
        // properties are never read by a binding). A set mirrors it once it grows, for O(1) lookups.
        private List<Binding> _subscribers;
        private HashSet<Binding> _subscriberSet;
        private const int SetThreshold = 8;

        /// <summary>Fired after the value changes. Used by the engine / renderer to mark dirty.</summary>
        public event Action Changed;

        /// <summary>A <c>Behavior</c> animating writes to this property, if any.</summary>
        public IPropertyInterceptor Interceptor;

        public QuillProperty(QuillObject owner, string name, object initial = null)
        {
            Owner = owner;
            Name = name;
            if (initial is double d) { _isNumber = true; _number = d; }
            _value = initial;
        }

        /// <summary>Raw value without dependency tracking. For engine/renderer reads.</summary>
        public object Raw => _isNumber ? (_value ??= _number) : _value;

        /// <summary>The value as a number, untracked and without boxing (<c>QuillConvert.ToDouble(Raw)</c>).</summary>
        public double Number => _isNumber ? _number : QuillConvert.ToDouble(_value);

        /// <summary>Tracked read. Call this from inside binding expressions.</summary>
        public object Get()
        {
            Binding.RegisterRead(this);
            return Raw;
        }

        /// <summary>Tracked read of the value as a number, without boxing.</summary>
        public double GetNumber()
        {
            Binding.RegisterRead(this);
            return _isNumber ? _number : QuillConvert.ToDouble(_value);
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

        /// <summary>A numeric binding's new value: no box unless a Behavior intercepts the write.</summary>
        internal void AssignNumberFromBinding(double v)
        {
            if (Interceptor != null) Write(v);
            else AssignNumber(v, null);
        }

        private void Write(object v)
        {
            if (Interceptor != null && Interceptor.Intercept(this, v)) return;
            Assign(v);
        }

        private void Assign(object v)
        {
            if (v is double d) { AssignNumber(d, v); return; }
            // A number never equals a non-number (as with boxed doubles before).
            if (!_isNumber && ValuesEqual(_value, v)) return;
            _isNumber = false;
            _value = v;
            Notify();
        }

        /// <param name="box">The value already boxed, if the caller has it (reused as the box).</param>
        private void AssignNumber(double v, object box)
        {
            if (_isNumber && _number.Equals(v)) return;   // Equals: NaN equals NaN, as before
            _isNumber = true;
            _number = v;
            _value = box;
            Notify();
        }

        private void Notify()
        {
            // Invalidate only queues a binding (it never touches subscriber lists), so no snapshot
            // copy is needed.
            if (_subscribers != null)
                for (int i = 0; i < _subscribers.Count; i++)
                    _subscribers[i].Invalidate();

            Changed?.Invoke();
        }

        internal void AddSubscriber(Binding b)
        {
            if (_subscriberSet != null) { if (!_subscriberSet.Add(b)) return; }
            else if (_subscribers != null && _subscribers.Contains(b)) return;

            (_subscribers ??= new List<Binding>(2)).Add(b);
            if (_subscriberSet == null && _subscribers.Count > SetThreshold)
                _subscriberSet = new HashSet<Binding>(_subscribers);
        }

        internal void RemoveSubscriber(Binding b)
        {
            if (_subscribers == null) return;
            if (_subscriberSet != null && !_subscriberSet.Remove(b)) return;
            _subscribers.Remove(b);
        }

        /// <summary>
        /// Write a number from engine code (renderer metrics, surface size) without allocating when
        /// it is unchanged. Same semantics as <see cref="SetValue"/> otherwise.
        /// </summary>
        public void SetNumber(double v)
        {
            DetachDriver();
            if (Interceptor != null) Write(v);
            else AssignNumber(v, null);
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
