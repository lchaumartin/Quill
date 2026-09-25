// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using Quill.Parsing;

namespace Quill
{
    /// <summary>
    /// Runs statements: signal handlers, functions and <c>ScriptAction</c> scripts. Assignments go
    /// through <see cref="QuillProperty.SetValue"/>, so — as in QML — assigning breaks a binding and
    /// a <c>Behavior</c> animates the change.
    /// </summary>
    public static class Interpreter
    {
        public enum Flow { Normal, Return, Break, Continue }

        private const int LoopLimit = 100000;

        /// <summary>Run a statement list. Returns the value of a `return`, else null.</summary>
        public static object Run(List<HandlerStmt> body, EvalContext ctx)
        {
            ctx.Locals ??= new Dictionary<string, object>();
            ExecList(body, ctx, out var value);
            return value;
        }

        private static Flow ExecList(List<HandlerStmt> body, EvalContext ctx, out object value)
        {
            value = null;
            for (int i = 0; i < body.Count; i++)
            {
                var flow = Exec(body[i], ctx, out value);
                if (flow != Flow.Normal) return flow;
            }
            return Flow.Normal;
        }

        private static Flow Exec(HandlerStmt stmt, EvalContext ctx, out object value)
        {
            value = null;
            switch (stmt)
            {
                case null:
                    return Flow.Normal;

                case AssignStmt a:
                    Assign(a, ctx);
                    return Flow.Normal;

                case ExprStmt e:
                    e.Expr.Eval(ctx);
                    return Flow.Normal;

                case BlockStmt b:
                    return ExecList(b.Body, ctx, out value);

                case IfStmt f:
                    if (QuillConvert.ToBool(f.Cond.Eval(ctx))) return Exec(f.Then, ctx, out value);
                    return f.Else != null ? Exec(f.Else, ctx, out value) : Flow.Normal;

                case VarStmt v:
                    ctx.Locals[v.Name] = v.Value?.Eval(ctx);
                    return Flow.Normal;

                case ReturnStmt r:
                    value = r.Value?.Eval(ctx);
                    return Flow.Return;

                case BreakStmt _: return Flow.Break;
                case ContinueStmt _: return Flow.Continue;

                case WhileStmt w:
                {
                    int guard = 0;
                    while (QuillConvert.ToBool(w.Cond.Eval(ctx)))
                    {
                        if (++guard > LoopLimit) { LoopWarning(stmt); break; }
                        var flow = Exec(w.Body, ctx, out value);
                        if (flow == Flow.Break) break;
                        if (flow == Flow.Return) return flow;
                    }
                    value = null;
                    return Flow.Normal;
                }

                case ForStmt fr:
                {
                    Exec(fr.Init, ctx, out _);
                    int guard = 0;
                    while (fr.Cond == null || QuillConvert.ToBool(fr.Cond.Eval(ctx)))
                    {
                        if (++guard > LoopLimit) { LoopWarning(stmt); break; }
                        var flow = Exec(fr.Body, ctx, out value);
                        if (flow == Flow.Break) break;
                        if (flow == Flow.Return) return flow;
                        Exec(fr.Step, ctx, out _);
                    }
                    value = null;
                    return Flow.Normal;
                }

                default:
                    return Flow.Normal;
            }
        }

        private static void LoopWarning(HandlerStmt s)
            => UnityEngine.Debug.LogWarning($"[Quill] Loop at line {s.Line} exceeded {LoopLimit} iterations; stopped.");

        // ---- Assignment ---------------------------------------------------------------------------

        private static void Assign(AssignStmt a, EvalContext ctx)
        {
            object Current() => Read(a.Target, ctx);

            object value;
            switch (a.Op)
            {
                case TokenType.Assign: value = a.Value.Eval(ctx); break;
                case TokenType.PlusAssign: value = BinaryNode.Apply(TokenType.Plus, Current(), a.Value.Eval(ctx)); break;
                case TokenType.MinusAssign: value = BinaryNode.Apply(TokenType.Minus, Current(), a.Value.Eval(ctx)); break;
                case TokenType.StarAssign: value = BinaryNode.Apply(TokenType.Star, Current(), a.Value.Eval(ctx)); break;
                case TokenType.SlashAssign: value = BinaryNode.Apply(TokenType.Slash, Current(), a.Value.Eval(ctx)); break;
                case TokenType.PlusPlus: value = QuillConvert.ToDouble(Current()) + 1; break;
                case TokenType.MinusMinus: value = QuillConvert.ToDouble(Current()) - 1; break;
                default: return;
            }
            Write(a.Target, value, ctx);
        }

        private static object Read(ExprNode target, EvalContext ctx) => target.Eval(ctx);

        /// <summary>Store <paramref name="value"/> into an assignable expression.</summary>
        public static void Write(ExprNode target, object value, EvalContext ctx)
        {
            switch (target)
            {
                case IdentifierNode id:
                {
                    if (ctx.Locals != null && ctx.Locals.ContainsKey(id.Name)) { ctx.Locals[id.Name] = value; return; }
                    var self = ctx.Self;
                    if (self == null) return;
                    (self.FindPropertyInScope(id.Name) ?? self.Property(id.Name)).SetValue(value);
                    return;
                }

                case MemberNode m:
                {
                    var prop = PropertyOf(m.Target.Eval(ctx), m.Member);
                    if (prop != null) prop.SetValue(value);
                    else UnityEngine.Debug.LogWarning($"[Quill] Cannot assign '{m.Member}': the target is not an object.");
                    return;
                }

                case IndexNode ix:
                {
                    // Lists are values: copy, change, and store the copy back so bindings see it.
                    var container = ix.Target.Eval(ctx);
                    if (container is List<object> list)
                    {
                        int i = (int)QuillConvert.ToDouble(ix.Index.Eval(ctx));
                        if (i < 0) return;
                        var copy = new List<object>(list);
                        while (copy.Count <= i) copy.Add(null);
                        copy[i] = value;
                        Write(ix.Target, copy, ctx);
                    }
                    else if (container is QuillObject obj)
                    {
                        obj.Property(QuillConvert.ToStr(ix.Index.Eval(ctx))).SetValue(value);
                    }
                    return;
                }
            }
        }

        /// <summary>The property named <paramref name="member"/> on an object or a property group.</summary>
        public static QuillProperty PropertyOf(object target, string member)
        {
            switch (target)
            {
                case QuillObject obj: return obj.Property(member);
                case QuillGroup g: return g.Owner.Property(g.Prefix + "." + member);
                default: return null;
            }
        }
    }
}
