// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill.Parsing
{
    // ---- Document AST (structure) --------------------------------------------------------------

    /// <summary>A `Type { ... }` declaration.</summary>
    public sealed class ObjectNode
    {
        public string TypeName;
        public string Id;                       // from `id: something`
        public string OnProperty;               // from `NumberAnimation on phase { ... }`
        public List<string> Signals = new List<string>();  // from `signal clicked`
        public List<PropertyNode> Properties = new List<PropertyNode>();
        public List<ObjectNode> Children = new List<ObjectNode>();
        public int Line;
    }

    /// <summary>A `name: expr` assignment, optionally a `property type name: expr` declaration.</summary>
    public sealed class PropertyNode
    {
        public string Name;
        public ExprNode Value;
        public bool IsDeclaration;              // true for `property real foo: ...`
        public string DeclaredType;             // real/int/bool/string/color/var (when IsDeclaration)
        public List<HandlerStmt> Handler;       // non-null for signal handlers (`onClicked: ...`)
        public int Line;
    }

    /// <summary>A statement inside a signal handler body.</summary>
    public abstract class HandlerStmt { }

    /// <summary>`path[0].path[1]... = Value`.</summary>
    public sealed class AssignStmt : HandlerStmt
    {
        public string[] Path;
        public ExprNode Value;
    }

    /// <summary>`path()` — emits the signal named by the last path segment on the object before it.</summary>
    public sealed class CallStmt : HandlerStmt
    {
        public string[] Path;
    }

    // ---- Expression AST (evaluates against the reactive scope) ---------------------------------

    public abstract class ExprNode
    {
        public abstract object Eval(EvalContext ctx);
    }

    public sealed class NumberNode : ExprNode
    {
        public double Value;
        public override object Eval(EvalContext ctx) => Value;
    }

    public sealed class StringNode : ExprNode
    {
        public string Value;
        public override object Eval(EvalContext ctx) => Value;
    }

    public sealed class BoolNode : ExprNode
    {
        public bool Value;
        public override object Eval(EvalContext ctx) => Value;
    }

    /// <summary>A bare identifier: resolves to a local/ancestor property value, `parent`, or an id.</summary>
    public sealed class IdentifierNode : ExprNode
    {
        public string Name;
        public override object Eval(EvalContext ctx) => ctx.Resolve(Name);
    }

    /// <summary>`target.member` — reads a property off whatever object the target resolves to.</summary>
    public sealed class MemberNode : ExprNode
    {
        public ExprNode Target;
        public string Member;

        public override object Eval(EvalContext ctx)
        {
            var t = Target.Eval(ctx);
            if (t is QuillObject obj)
            {
                var p = obj.FindProperty(Member);
                return p?.Get();
            }
            return null;
        }
    }

    /// <summary>A function call such as `Math.abs(x)` or `Math.min(a, b)`.</summary>
    public sealed class CallNode : ExprNode
    {
        public string[] Name;            // e.g. ["Math", "abs"]
        public List<ExprNode> Args;
        public override object Eval(EvalContext ctx)
        {
            if (Name.Length == 2 && Name[0] == "Math")
                return MathBuiltins.Call(Name[1], Args, ctx);
            return null;   // unknown function
        }
    }

    /// <summary>`cond ? a : b`.</summary>
    public sealed class TernaryNode : ExprNode
    {
        public ExprNode Cond;
        public ExprNode WhenTrue;
        public ExprNode WhenFalse;
        public override object Eval(EvalContext ctx)
            => QuillConvert.ToBool(Cond.Eval(ctx)) ? WhenTrue.Eval(ctx) : WhenFalse.Eval(ctx);
    }

    public sealed class UnaryNode : ExprNode
    {
        public TokenType Op;
        public ExprNode Operand;
        public override object Eval(EvalContext ctx)
        {
            var v = Operand.Eval(ctx);
            if (Op == TokenType.Not) return !QuillConvert.ToBool(v);
            return Op == TokenType.Minus ? -QuillConvert.ToDouble(v) : v;
        }
    }

    public sealed class BinaryNode : ExprNode
    {
        public TokenType Op;
        public ExprNode Left;
        public ExprNode Right;

        public override object Eval(EvalContext ctx)
        {
            var l = Left.Eval(ctx);
            var r = Right.Eval(ctx);

            // Logical operators (no side effects in expressions, so eager eval is fine).
            if (Op == TokenType.And) return QuillConvert.ToBool(l) && QuillConvert.ToBool(r);
            if (Op == TokenType.Or) return QuillConvert.ToBool(l) || QuillConvert.ToBool(r);

            // `+` concatenates when either side is a string.
            if (Op == TokenType.Plus && (l is string || r is string))
                return QuillConvert.ToStr(l) + QuillConvert.ToStr(r);

            // String equality compares text, not numeric coercion (so "main" != "settings").
            if ((Op == TokenType.EqualEqual || Op == TokenType.NotEqual) && (l is string || r is string))
            {
                bool eq = QuillConvert.ToStr(l) == QuillConvert.ToStr(r);
                return Op == TokenType.EqualEqual ? eq : !eq;
            }

            double a = QuillConvert.ToDouble(l);
            double b = QuillConvert.ToDouble(r);
            switch (Op)
            {
                case TokenType.Plus: return a + b;
                case TokenType.Minus: return a - b;
                case TokenType.Star: return a * b;
                case TokenType.Slash: return b == 0 ? 0 : a / b;
                case TokenType.Percent: return b == 0 ? 0 : a % b;
                case TokenType.Less: return a < b;
                case TokenType.Greater: return a > b;
                case TokenType.LessEqual: return a <= b;
                case TokenType.GreaterEqual: return a >= b;
                case TokenType.EqualEqual: return a == b;
                case TokenType.NotEqual: return a != b;
                default: return null;
            }
        }
    }
}
