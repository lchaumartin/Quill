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

        private readonly Func<object> _expr;
        private readonly QuillEngine _engine;
        private readonly List<QuillProperty> _deps = new List<QuillProperty>();
        private bool _queued;
        private bool _detached;

        public Binding(QuillEngine engine, Func<object> expr)
        {
            _engine = engine;
            _expr = expr;
        }

        /// <summary>The expression this binding evaluates (states re-install it after a state ends).</summary>
        public Func<object> Expression => _expr;

        /// <summary>True once the binding was replaced; it never evaluates again.</summary>
        public bool IsDetached => _detached;

        // --- Automatic dependency tracking ------------------------------------------------------

        [ThreadStatic] private static Stack<Binding> _evalStack;

        internal static void RegisterRead(QuillProperty p)
        {
            var stack = _evalStack;
            if (stack == null || stack.Count == 0) return;
            var current = stack.Peek();
            if (!current._deps.Contains(p))
            {
                current._deps.Add(p);
                p.AddSubscriber(current);
            }
        }

        // --- Evaluation -------------------------------------------------------------------------

        public void Evaluate()
        {
            if (_detached) { _queued = false; return; }   // replaced while it sat in the dirty queue

            // Detach old dependencies; they are rebuilt fresh on every evaluation so the graph
            // always reflects the branches actually taken this time.
            for (int i = 0; i < _deps.Count; i++)
                _deps[i].RemoveSubscriber(this);
            _deps.Clear();

            _evalStack ??= new Stack<Binding>();
            _evalStack.Push(this);
            object result;
            try
            {
                result = _expr();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Quill] Binding for '{Target?.Name}' threw: {e.Message}");
                result = null;
            }
            finally
            {
                _evalStack.Pop();
            }

            _queued = false;
            Target?.AssignFromBinding(result);
        }

        /// <summary>A dependency changed: schedule a recompute on the engine's dirty queue.</summary>
        /// <summary>
        /// Stop driving the target for good: drop every dependency so the binding never
        /// re-evaluates. Called when the property gets a new binding or an imperative value.
        /// </summary>
        internal void Detach()
        {
            _detached = true;
            for (int i = 0; i < _deps.Count; i++)
                _deps[i].RemoveSubscriber(this);
            _deps.Clear();
            Target = null;
        }

        public void Invalidate()
        {
            if (_queued) return;
            _queued = true;
            _engine.Enqueue(this);
        }
    }
}
