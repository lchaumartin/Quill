// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// A reactive binding: an expression whose result drives a target property.
    ///
    /// Dependencies are captured automatically. While <see cref="Evaluate"/> runs, this binding is
    /// pushed onto a thread-static stack; any <see cref="QuillProperty.Get"/> call during evaluation
    /// records a dependency edge. When any dependency changes, the binding is invalidated and queued
    /// for re-evaluation by the engine — exactly how Quill keeps the UI in sync with its model.
    /// </summary>
    public sealed class Binding
    {
        public QuillProperty Target;

        private Func<object> _expr;
        private readonly Func<double> _number;   // set for numeric bindings: evaluated without boxing
        private readonly QuillEngine _engine;

        // Dependencies of the last evaluation, and a spare list the next evaluation fills. Most
        // re-evaluations read exactly the same properties, so subscriptions are only touched for the
        // difference (a property with thousands of subscribers, like a shared clock, is never
        // rescanned).
        private List<QuillProperty> _deps = new List<QuillProperty>(4);
        private List<QuillProperty> _prevDeps = new List<QuillProperty>(4);
        private bool _queued;
        private bool _detached;

        public Binding(QuillEngine engine, Func<object> expr)
        {
            _engine = engine;
            _expr = expr;
        }

        private Binding(QuillEngine engine, Func<double> number)
        {
            _engine = engine;
            _number = number;
        }

        /// <summary>
        /// A binding whose expression always yields a number. It is evaluated and stored unboxed, so
        /// re-evaluating it allocates nothing.
        /// </summary>
        public static Binding Numeric(QuillEngine engine, Func<double> expr) => new Binding(engine, expr);

        /// <summary>The expression, as a boxed-result function (saved and restored by states).</summary>
        public Func<object> Expression
        {
            get
            {
                if (_expr == null && _number != null)
                {
                    var number = _number;
                    _expr = () => number();
                }
                return _expr;
            }
        }

        public bool IsDetached => _detached;

        // ---- Dependency capture ------------------------------------------------------------------

        [ThreadStatic] private static Stack<Binding> _evalStack;

        /// <summary>Called by <see cref="QuillProperty.Get"/>. Records a dependency on the current binding.</summary>
        internal static void RegisterRead(QuillProperty p)
        {
            var stack = _evalStack;
            if (stack == null || stack.Count == 0) return;
            var current = stack.Peek();
            if (current._deps.Contains(p)) return;
            current._deps.Add(p);
            // Subscribe right away (not after evaluation) so a change later in this same evaluation
            // still re-queues the binding. Already subscribed if it was a dependency last time.
            if (!current._prevDeps.Contains(p)) p.AddSubscriber(current);
        }

        // ---- Evaluation --------------------------------------------------------------------------

        /// <summary>Re-run the expression, re-capturing dependencies, and push the result to the target.</summary>
        public void Evaluate()
        {
            if (_detached) { _queued = false; return; }   // replaced while it sat in the dirty queue

            // Last evaluation's dependencies become the reference; collect this one's afresh.
            var prev = _deps;
            _deps = _prevDeps;
            _prevDeps = prev;
            _deps.Clear();

            _evalStack ??= new Stack<Binding>();
            _evalStack.Push(this);
            object result = null;
            double number = 0;
            bool failed = false;
            try
            {
                if (_number != null) number = _number();
                else result = _expr();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Binding for '{Target?.Name}' threw: {e.Message}");
                failed = true;
            }
            finally
            {
                _evalStack.Pop();
            }

            // Unsubscribe from what this evaluation no longer reads.
            for (int i = 0; i < _prevDeps.Count; i++)
                if (!_deps.Contains(_prevDeps[i])) _prevDeps[i].RemoveSubscriber(this);
            _prevDeps.Clear();

            _queued = false;
            var target = Target;
            if (target == null) return;
            if (failed) { target.AssignFromBinding(null); return; }
            if (_number == null) { target.AssignFromBinding(result); return; }

            // Numeric: stored unboxed (boxed only if a Behavior intercepts the write).
            target.AssignNumberFromBinding(number);
        }

        internal void Detach()
        {
            _detached = true;
            for (int i = 0; i < _deps.Count; i++)
                _deps[i].RemoveSubscriber(this);
            _deps.Clear();
            Target = null;
        }

        /// <summary>Mark dirty; the engine re-evaluates queued bindings once per frame (or on demand).</summary>
        public void Invalidate()
        {
            if (_queued) return;
            _queued = true;
            _engine.Enqueue(this);
        }
    }
}
