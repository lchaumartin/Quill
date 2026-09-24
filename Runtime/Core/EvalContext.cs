// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
namespace Quill
{
    /// <summary>
    /// The scope an expression evaluates against. `Self` is the object the binding belongs to;
    /// name resolution mirrors Quill: own/ancestor properties, the `parent` keyword, then ids.
    /// </summary>
    public sealed class EvalContext
    {
        public QuillEngine Engine;
        public QuillObject Self;

        public EvalContext(QuillEngine engine, QuillObject self)
        {
            Engine = engine;
            Self = self;
        }

        public object Resolve(string name)
        {
            if (name == "parent")
                return Self != null ? Self.Parent : null;

            // Own property, then walk up the parent chain (lexical scoping).
            var p = Self?.FindPropertyInScope(name);
            if (p != null) return p.Get();

            // Component-local ids first (the nearest enclosing component scope), then global ids.
            for (var o = Self; o != null; o = o.Parent)
                if (o.LocalIds != null && o.LocalIds.TryGetValue(name, out var local))
                    return local;

            var obj = Engine?.FindId(name);
            if (obj != null) return obj;

            return null;
        }
    }
}
