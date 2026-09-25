// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// The scope an expression evaluates against. `Self` is the object the binding (or handler, or
    /// function) belongs to; `Locals` holds handler/function locals and signal parameters. Name
    /// resolution mirrors QML: locals, own/ancestor properties, the `parent` keyword, component-local
    /// ids, global ids, then property groups (`border`, `drag`, `font`).
    /// </summary>
    public sealed class EvalContext
    {
        public QuillEngine Engine;
        public QuillObject Self;
        public Dictionary<string, object> Locals;

        public EvalContext(QuillEngine engine, QuillObject self)
        {
            Engine = engine;
            Self = self;
        }

        public object Resolve(string name)
        {
            if (Locals != null && Locals.TryGetValue(name, out var local))
                return local;

            if (name == "parent")
                return Self != null ? Self.Parent : null;

            // Own property, then walk up the parent chain (lexical scoping).
            var p = Self?.FindPropertyInScope(name);
            if (p != null) return p.Get();

            // Component-local ids first (the nearest enclosing component scope), then global ids.
            for (var o = Self; o != null; o = o.Parent)
                if (o.LocalIds != null && o.LocalIds.TryGetValue(name, out var id))
                    return id;

            var obj = Engine?.FindId(name);
            if (obj != null) return obj;

            // A property group of this object or an ancestor: `drag.active`, `border.color`.
            for (var o = Self; o != null; o = o.Parent)
                if (o.HasGroup(name)) return new QuillGroup(o, name);

            return null;
        }
    }

    /// <summary>
    /// A reference to a group of dotted properties on an object (`border` in `border.color`), so
    /// `rect.border.color` and `drag.active` read like QML grouped properties.
    /// </summary>
    public sealed class QuillGroup
    {
        public readonly QuillObject Owner;
        public readonly string Prefix;

        public QuillGroup(QuillObject owner, string prefix)
        {
            Owner = owner;
            Prefix = prefix;
        }

        public override bool Equals(object obj)
            => obj is QuillGroup g && ReferenceEquals(g.Owner, Owner) && g.Prefix == Prefix;

        public override int GetHashCode() => (Owner?.GetHashCode() ?? 0) ^ Prefix.GetHashCode();
    }
}
