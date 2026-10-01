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

        // Source positions (offsets into the text), for editor tooling.
        public int TypeOffset, BodyStart, BodyEnd;           // the type name, `{` and `}`
        public int IdOffset = -1;                            // the id value, -1 if none
        public List<(string name, int offset, int end)> SignalDecls = new List<(string, int, int)>();
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

        // Source positions, for editor tooling: the (dotted) name, and the value after the colon
        // (expression, handler, or object value); ValueStart is -1 for a declaration without a value.
        public int NameOffset, NameEnd, ValueStart = -1, ValueEnd = -1;

        public bool IsAlias => IsDeclaration && DeclaredType == "alias";
    }

    /// <summary>`function name(a, b) { ... }` declared on an object.</summary>
    public sealed class FunctionDecl
    {
        public string Name;
        public string[] Params;
        public List<HandlerStmt> Body;
        public int Line, NameOffset, NameEnd, BodyStart, BodyEnd;
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

        /// <summary>
        /// The value as a number, without boxing when the node is numeric (see <see cref="IsNumeric"/>).
        /// Same result as <c>QuillConvert.ToDouble(Eval(ctx))</c>.
        /// </summary>
        public virtual double EvalNumber(EvalContext ctx) => QuillConvert.ToDouble(Eval(ctx));

        /// <summary>
        /// True when this node always yields a number (literals, arithmetic, Math.*), so it can be
        /// evaluated through <see cref="EvalNumber"/> with no intermediate boxed values.
        /// </summary>
        public virtual bool IsNumeric => false;
    }

    public sealed class NumberNode : ExprNode
    {
        public double Value;
        private object _boxed;   // boxed once, on first use (Value is set by the parser before that)
        public override object Eval(EvalContext ctx) => _boxed ??= Value;
        public override double EvalNumber(EvalContext ctx) => Value;
        public override bool IsNumeric => true;
    }

    public sealed class StringNode : ExprNode
    {
        public string Value;
        public override object Eval(EvalContext ctx) => Value;
    }

    public sealed class BoolNode : ExprNode
    {
        public bool Value;
        public override object Eval(EvalContext ctx) => QuillConvert.Box(Value);
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
        public override double EvalNumber(EvalContext ctx) => ctx.ResolveNumber(Name);
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
        public override double EvalNumber(EvalContext ctx) => Builtins.GetMemberNumber(Target.Eval(ctx), Member);
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
    /// such as <c>parseInt</c>), else the receiver: <c>Math</c>/<c>Color</c>/<c>console</c>, an object
    /// (function, built-in method such as <c>anim.start()</c>, or signal emit), or a plain value
    /// (<c>value.toFixed(2)</c>, <c>name.toUpperCase()</c>).
    /// </summary>
    public sealed class CallNode : ExprNode
    {
        public ExprNode Target;
        public string Name;
        public List<ExprNode> Args = new List<ExprNode>();

        public override object Eval(EvalContext ctx)
        {
            // Allocation-free fast paths for Math.* and Color.rgba/hsva/hsla (the hot calls in
            // animated bindings); everything else goes through Builtins.Call.
            if (IsMath && NotShadowed(ctx, "Math") && MathBuiltins.TryCall(Name, this, ctx, out double m)) return m;
            if (IsColor && NotShadowed(ctx, "Color") && TryColor(ctx, out var c)) return c;
            return Builtins.Call(this, ctx);
        }

        public override double EvalNumber(EvalContext ctx)
        {
            if (IsMath && NotShadowed(ctx, "Math") && MathBuiltins.TryCall(Name, this, ctx, out double m)) return m;
            return QuillConvert.ToDouble(Eval(ctx));
        }

        public override bool IsNumeric => IsMath && MathBuiltins.IsNumeric(Name);

        private bool IsMath => Target is IdentifierNode id && id.Name == "Math";
        private bool IsColor => Target is IdentifierNode id && id.Name == "Color";

        private static bool NotShadowed(EvalContext ctx, string name)
            => ctx.Locals == null || !ctx.Locals.ContainsKey(name);

        /// <summary>Argument <paramref name="i"/> as a number, or <paramref name="fallback"/> if absent.</summary>
        internal double ArgNumber(EvalContext ctx, int i, double fallback = 0)
            => i < Args.Count ? Args[i].EvalNumber(ctx) : fallback;

        private bool TryColor(EvalContext ctx, out UnityEngine.Color c)
        {
            switch (Name)
            {
                case "rgba":
                    c = new UnityEngine.Color((float)ArgNumber(ctx, 0), (float)ArgNumber(ctx, 1),
                                              (float)ArgNumber(ctx, 2), (float)ArgNumber(ctx, 3, 1));
                    return true;
                case "hsva":
                    c = QuillColor.FromHsv(ArgNumber(ctx, 0), ArgNumber(ctx, 1), ArgNumber(ctx, 2), ArgNumber(ctx, 3, 1));
                    return true;
                case "hsla":
                    c = QuillColor.FromHsl(ArgNumber(ctx, 0), ArgNumber(ctx, 1), ArgNumber(ctx, 2), ArgNumber(ctx, 3, 1));
                    return true;
                default:
                    c = default;
                    return false;
            }
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
        public override double EvalNumber(EvalContext ctx)
            => QuillConvert.ToBool(Cond.Eval(ctx)) ? WhenTrue.EvalNumber(ctx) : WhenFalse.EvalNumber(ctx);
        public override bool IsNumeric => WhenTrue.IsNumeric && WhenFalse.IsNumeric;
    }

    public sealed class UnaryNode : ExprNode
    {
        public TokenType Op;
        public ExprNode Operand;
        public override object Eval(EvalContext ctx)
        {
            switch (Op)
            {
                case TokenType.Not: return QuillConvert.Box(!QuillConvert.ToBool(Operand.Eval(ctx)));
                case TokenType.Minus: return -Operand.EvalNumber(ctx);
                case TokenType.Plus: return Operand.EvalNumber(ctx);
                default: return Operand.Eval(ctx);
            }
        }

        public override double EvalNumber(EvalContext ctx)
        {
            switch (Op)
            {
                case TokenType.Minus: return -Operand.EvalNumber(ctx);
                case TokenType.Plus: return Operand.EvalNumber(ctx);
                default: return QuillConvert.ToDouble(Eval(ctx));
            }
        }

        public override bool IsNumeric => Op == TokenType.Minus || Op == TokenType.Plus;
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

            // Numbers in, number out: compute unboxed, box the result once.
            if (IsNumeric) return Arith(Op, Left.EvalNumber(ctx), Right.EvalNumber(ctx));

            if (Op == TokenType.Plus)
            {
                // A numeric side can't be a string, so only the other side decides concatenation.
                if (Left.IsNumeric)
                {
                    double a = Left.EvalNumber(ctx);
                    var r = Right.Eval(ctx);
                    return r is string rs ? QuillConvert.FormatNumber(a) + rs : (object)(a + QuillConvert.ToDouble(r));
                }
                if (Right.IsNumeric)
                {
                    var l = Left.Eval(ctx);
                    double b = Right.EvalNumber(ctx);
                    return l is string ls ? ls + QuillConvert.FormatNumber(b) : (object)(QuillConvert.ToDouble(l) + b);
                }
            }
            else if (IsComparison(Op) && Left.IsNumeric && Right.IsNumeric)
            {
                return QuillConvert.Box(Compare(Op, Left.EvalNumber(ctx), Right.EvalNumber(ctx)));
            }
            return Apply(Op, Left.Eval(ctx), Right.Eval(ctx));
        }

        public override double EvalNumber(EvalContext ctx)
        {
            if (IsNumeric) return Arith(Op, Left.EvalNumber(ctx), Right.EvalNumber(ctx));
            if (Op == TokenType.Plus)
            {
                // `index + 1` with an untyped side: add without boxing the sum (it may still turn out to
                // be a string concatenation, which is then converted like before).
                bool ln = Left.IsNumeric, rn = Right.IsNumeric;
                object l = null, r = null;
                double a = 0, b = 0;
                if (ln) a = Left.EvalNumber(ctx); else l = Left.Eval(ctx);
                if (rn) b = Right.EvalNumber(ctx); else r = Right.Eval(ctx);
                if ((!ln && l is string) || (!rn && r is string))
                    return QuillConvert.ToDouble((ln ? QuillConvert.FormatNumber(a) : QuillConvert.ToStr(l))
                                               + (rn ? QuillConvert.FormatNumber(b) : QuillConvert.ToStr(r)));
                return (ln ? a : QuillConvert.ToDouble(l)) + (rn ? b : QuillConvert.ToDouble(r));
            }
            return QuillConvert.ToDouble(Eval(ctx));
        }

        /// <summary>
        /// Arithmetic on numeric operands. `+` only counts when both sides are numeric (otherwise it
        /// may concatenate strings).
        /// </summary>
        public override bool IsNumeric
        {
            get
            {
                switch (Op)
                {
                    case TokenType.Minus: case TokenType.Star: case TokenType.Slash: case TokenType.Percent:
                    case TokenType.BitAnd: case TokenType.BitOr:
                        return true;
                    case TokenType.Plus:
                        return Left.IsNumeric && Right.IsNumeric;
                    default:
                        return false;
                }
            }
        }

        private static double Arith(TokenType op, double a, double b)
        {
            switch (op)
            {
                case TokenType.Plus: return a + b;
                case TokenType.Minus: return a - b;
                case TokenType.Star: return a * b;
                case TokenType.Slash: return b == 0 ? 0 : a / b;
                case TokenType.Percent: return b == 0 ? 0 : a % b;
                case TokenType.BitAnd: return (long)a & (long)b;
                case TokenType.BitOr: return (long)a | (long)b;
                default: return 0;
            }
        }

        private static bool IsComparison(TokenType op)
            => op == TokenType.Less || op == TokenType.Greater || op == TokenType.LessEqual || op == TokenType.GreaterEqual;

        private static bool Compare(TokenType op, double a, double b)
        {
            switch (op)
            {
                case TokenType.Less: return a < b;
                case TokenType.Greater: return a > b;
                case TokenType.LessEqual: return a <= b;
                default: return a >= b;
            }
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
                return QuillConvert.Box(op == TokenType.EqualEqual ? eq : !eq);
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
                case TokenType.Less: return QuillConvert.Box(a < b);
                case TokenType.Greater: return QuillConvert.Box(a > b);
                case TokenType.LessEqual: return QuillConvert.Box(a <= b);
                case TokenType.GreaterEqual: return QuillConvert.Box(a >= b);
                case TokenType.BitAnd: return (double)((long)a & (long)b);
                case TokenType.BitOr: return (double)((long)a | (long)b);
                default: return null;
            }
        }
    }
}
