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
        public string OnProperty;               // from `NumberAnimation on phase { ... }` / `Behavior on x { }`
        public string AssignedTo;               // `states: [ State {} ]` -> "states" (null for plain children)
        public List<string> Signals = new List<string>();  // from `signal clicked`
        public Dictionary<string, string[]> SignalParams = new Dictionary<string, string[]>();
        public List<PropertyNode> Properties = new List<PropertyNode>();
        public List<ObjectNode> Children = new List<ObjectNode>();
        public List<FunctionDecl> Functions = new List<FunctionDecl>();
        public int Line;
    }

    /// <summary>A `name: expr` assignment, optionally a `property type name: expr` declaration.</summary>
    public sealed class PropertyNode
    {
        public string Name;
        public ExprNode Value;
        public bool IsDeclaration;              // true for `property real foo: ...`
        public string DeclaredType;             // real/int/bool/string/color/var/alias (when IsDeclaration)
        public List<HandlerStmt> Handler;       // non-null for signal handlers (`onClicked: ...`)
        public int Line;

        public bool IsAlias => IsDeclaration && DeclaredType == "alias";
    }

    /// <summary>`function name(a, b) { ... }` declared on an object.</summary>
    public sealed class FunctionDecl
    {
        public string Name;
        public string[] Params;
        public List<HandlerStmt> Body;
    }

    // ---- Statements (signal handlers, functions, ScriptAction) ---------------------------------

    public abstract class HandlerStmt { public int Line; }

    /// <summary>
    /// `target = value`, `target += value` (and -= *= /=), `target++` / `target--`. The target is an
    /// identifier, a member chain (`panel.visible`, `border.color`) or an index (`list[i]`).
    /// </summary>
    public sealed class AssignStmt : HandlerStmt
    {
        public ExprNode Target;
        public TokenType Op = TokenType.Assign;
        public ExprNode Value;                  // null for ++ / --
    }

    /// <summary>An expression evaluated for its effect: a signal emit, a method or function call.</summary>
    public sealed class ExprStmt : HandlerStmt { public ExprNode Expr; }

    public sealed class BlockStmt : HandlerStmt { public List<HandlerStmt> Body = new List<HandlerStmt>(); }

    public sealed class IfStmt : HandlerStmt
    {
        public ExprNode Cond;
        public HandlerStmt Then;
        public HandlerStmt Else;
    }

    /// <summary>`var name = expr` (also `let` / `const`): a local of the running handler or function.</summary>
    public sealed class VarStmt : HandlerStmt
    {
        public string Name;
        public ExprNode Value;
    }

    public sealed class ForStmt : HandlerStmt
    {
        public HandlerStmt Init;
        public ExprNode Cond;
        public HandlerStmt Step;
        public HandlerStmt Body;
    }

    public sealed class WhileStmt : HandlerStmt
    {
        public ExprNode Cond;
        public HandlerStmt Body;
    }

    public sealed class ReturnStmt : HandlerStmt { public ExprNode Value; }
    public sealed class BreakStmt : HandlerStmt { }
    public sealed class ContinueStmt : HandlerStmt { }

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

    /// <summary>`null` / `undefined`.</summary>
    public sealed class NullNode : ExprNode
    {
        public override object Eval(EvalContext ctx) => null;
    }

    /// <summary>A bare identifier: a local, a property in lexical scope, `parent`, an id, or a group.</summary>
    public sealed class IdentifierNode : ExprNode
    {
        public string Name;
        public override object Eval(EvalContext ctx) => ctx.Resolve(Name);
    }

    /// <summary>
    /// `target.member` — a property of an object, a sub-property of a group (`border.color`,
    /// `drag.active`), a colour channel (`c.r`, `c.hsvHue`), or `length` of a list / string.
    /// </summary>
    public sealed class MemberNode : ExprNode
    {
        public ExprNode Target;
        public string Member;

        public override object Eval(EvalContext ctx) => Builtins.GetMember(Target.Eval(ctx), Member);
    }

    /// <summary>`target[index]` on a list or a string.</summary>
    public sealed class IndexNode : ExprNode
    {
        public ExprNode Target;
        public ExprNode Index;

        public override object Eval(EvalContext ctx) => Builtins.GetIndex(Target.Eval(ctx), Index.Eval(ctx));
    }

    /// <summary>`[a, b, c]` — a list value.</summary>
    public sealed class ArrayNode : ExprNode
    {
        public List<ExprNode> Items = new List<ExprNode>();

        public override object Eval(EvalContext ctx)
        {
            var list = new List<object>(Items.Count);
            for (int i = 0; i < Items.Count; i++) list.Add(Items[i].Eval(ctx));
            return list;
        }
    }

    /// <summary>
    /// A call. <see cref="Target"/> is null for a bare call (`foo(1)`: a function in scope or a global
    /// such as <c>parseInt</c>), else the receiver: <c>Math</c>/<c>Qt</c>/<c>console</c>, an object
    /// (function, built-in method such as <c>anim.start()</c>, or signal emit), or a plain value
    /// (<c>value.toFixed(2)</c>, <c>name.toUpperCase()</c>).
    /// </summary>
    public sealed class CallNode : ExprNode
    {
        public ExprNode Target;
        public string Name;
        public List<ExprNode> Args = new List<ExprNode>();

        public override object Eval(EvalContext ctx) => Builtins.Call(this, ctx);
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
            switch (Op)
            {
                case TokenType.Not: return !QuillConvert.ToBool(v);
                case TokenType.Minus: return -QuillConvert.ToDouble(v);
                case TokenType.Plus: return QuillConvert.ToDouble(v);
                default: return v;
            }
        }
    }

    public sealed class BinaryNode : ExprNode
    {
        public TokenType Op;
        public ExprNode Left;
        public ExprNode Right;

        public override object Eval(EvalContext ctx)
        {
            // && and || short-circuit and return an operand, as in JavaScript (`name || "none"`).
            if (Op == TokenType.And)
            {
                var l = Left.Eval(ctx);
                return QuillConvert.ToBool(l) ? Right.Eval(ctx) : l;
            }
            if (Op == TokenType.Or)
            {
                var l = Left.Eval(ctx);
                return QuillConvert.ToBool(l) ? l : Right.Eval(ctx);
            }
            return Apply(Op, Left.Eval(ctx), Right.Eval(ctx));
        }

        /// <summary>Apply a binary operator to two evaluated operands (also used by `+=` etc.).</summary>
        public static object Apply(TokenType op, object l, object r)
        {
            // `+` concatenates when either side is a string.
            if (op == TokenType.Plus && (l is string || r is string))
                return QuillConvert.ToStr(l) + QuillConvert.ToStr(r);

            if (op == TokenType.EqualEqual || op == TokenType.NotEqual)
            {
                bool eq = Builtins.LooseEquals(l, r);
                return op == TokenType.EqualEqual ? eq : !eq;
            }

            double a = QuillConvert.ToDouble(l);
            double b = QuillConvert.ToDouble(r);
            switch (op)
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
                case TokenType.BitAnd: return (double)((long)a & (long)b);
                case TokenType.BitOr: return (double)((long)a | (long)b);
                default: return null;
            }
        }
    }
}
