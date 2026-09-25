// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill.Parsing
{
    public sealed class QuillParseException : Exception
    {
        /// <summary>Where the error was found (the offending token), for editor tooling; -1 if unknown.</summary>
        public int Offset = -1, End = -1, Line, Column;

        public QuillParseException(string message) : base(message) { }

        internal QuillParseException(string message, Token at) : base(message)
        {
            Offset = at.Offset;
            End = Math.Max(at.End, at.Offset + 1);
            Line = at.Line;
            Column = at.Column;
        }
    }

    /// <summary>
    /// Recursive-descent parser producing an <see cref="ObjectNode"/> tree, with a Pratt
    /// (precedence-climbing) sub-parser for binding expressions and a small statement parser for
    /// signal handlers and functions.
    ///
    /// Grammar (subset):
    ///   document   := object
    ///   object     := Ident ['on' dotted] '{' member* '}'
    ///   member     := 'id' ':' Ident
    ///               | ['readonly'|'default'|'required'] 'property' type Ident [ ':' expr ]
    ///               | 'property' 'alias' Ident ':' dotted
    ///               | 'signal' Ident [ '(' [type] Ident (',' [type] Ident)* ')' ]
    ///               | 'function' Ident '(' params ')' block
    ///               | dotted ':' ( expr | handler | object | '[' object (',' object)* ']' )
    ///               | object
    ///   handler    := statement | block             (for `on&lt;Signal&gt;` names and ScriptAction.script)
    ///   statement  := block | if | for | while | var | return | break | continue
    ///               | lvalue ('=' | '+=' | '-=' | '*=' | '/=') expr | lvalue ('++' | '--') | ('++'|'--') lvalue
    ///               | call
    /// </summary>
    public sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _i;

        public Parser(List<Token> tokens) { _tokens = tokens; }

        public static ObjectNode Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens).ParseDocument();
        }

        private Token Cur => _tokens[_i];
        private int PrevEnd => _i > 0 ? _tokens[_i - 1].End : 0;
        private static string Describe(Token t) => t.Type == TokenType.EOF ? "end of file" : t.Text;
        private Token PeekTok(int o = 1) => _tokens[Math.Min(_i + o, _tokens.Count - 1)];
        private Token Advance() => _tokens[_i++];
        private bool Check(TokenType t) => Cur.Type == t;
        private bool CheckWord(string w) => Cur.Type == TokenType.Identifier && Cur.Text == w;

        private Token Expect(TokenType t, string what)
        {
            if (Cur.Type != t)
                throw new QuillParseException($"Expected {what} but found '{Describe(Cur)}' (line {Cur.Line})", Cur);
            return Advance();
        }

        public ObjectNode ParseDocument()
        {
            var root = ParseObject();
            if (!Check(TokenType.EOF))
                throw new QuillParseException($"Unexpected trailing '{Describe(Cur)}' (line {Cur.Line})", Cur);
            return root;
        }

        private string ParseDotted(string what)
        {
            string name = Expect(TokenType.Identifier, what).Text;
            while (Check(TokenType.Dot))
            {
                Advance();
                name += "." + Expect(TokenType.Identifier, what).Text;
            }
            return name;
        }

        private ObjectNode ParseObject()
        {
            var typeTok = Expect(TokenType.Identifier, "type name");
            var node = new ObjectNode { TypeName = typeTok.Text, Line = typeTok.Line, TypeOffset = typeTok.Offset };

            // Optional `on <property>` (e.g. `NumberAnimation on phase { ... }`, `Behavior on border.color`).
            if (CheckWord("on"))
            {
                Advance();
                node.OnProperty = ParseDotted("property after 'on'");
            }

            node.BodyStart = Expect(TokenType.LBrace, "'{'").Offset;

            while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
                ParseMember(node);

            node.BodyEnd = Expect(TokenType.RBrace, "'}'").Offset;
            return node;
        }

        // `Type {` or `Type on x {` — an object literal starts here.
        private bool AtObjectStart()
            => Check(TokenType.Identifier) && char.IsUpper(Cur.Text[0])
               && (PeekTok().Type == TokenType.LBrace
                   || (PeekTok().Type == TokenType.Identifier && PeekTok().Text == "on"));

        private void ParseMember(ObjectNode owner)
        {
            // Modifiers are accepted and ignored.
            while ((CheckWord("readonly") || CheckWord("default") || CheckWord("required"))
                   && PeekTok().Type == TokenType.Identifier)
                Advance();

            // `signal clicked` / `signal moved(real value, bool final)`
            if (CheckWord("signal") && PeekTok().Type == TokenType.Identifier)
            {
                Advance();
                var sig = Expect(TokenType.Identifier, "signal name");
                owner.Signals.Add(sig.Text);
                owner.SignalDecls.Add((sig.Text, sig.Offset, sig.End));
                var names = new List<string>();
                if (Check(TokenType.LParen))
                {
                    Advance();
                    while (!Check(TokenType.RParen) && !Check(TokenType.EOF))
                    {
                        // `type name` or just `name`.
                        var first = Expect(TokenType.Identifier, "signal parameter");
                        if (Check(TokenType.Identifier)) names.Add(Advance().Text);
                        else names.Add(first.Text);
                        if (Check(TokenType.Comma)) Advance();
                    }
                    Expect(TokenType.RParen, "')'");
                }
                owner.SignalParams[sig.Text] = names.ToArray();
                ConsumeOptionalSemicolon();
                return;
            }

            // `function name(a, b) { ... }`
            if (CheckWord("function") && PeekTok().Type == TokenType.Identifier)
            {
                Advance();
                var fname = Expect(TokenType.Identifier, "function name");
                var fn = new FunctionDecl { Name = fname.Text, Line = fname.Line, NameOffset = fname.Offset, NameEnd = fname.End };
                fn.Params = ParseParamList();
                fn.BodyStart = Cur.Offset;
                fn.Body = ParseBlockBody();
                fn.BodyEnd = PrevEnd;
                owner.Functions.Add(fn);
                return;
            }

            // `property <type> <name> [: expr]` / `property alias <name>: target.prop`
            if (CheckWord("property") && PeekTok().Type == TokenType.Identifier)
            {
                Advance(); // property
                var type = Expect(TokenType.Identifier, "property type");
                string typeName = type.Text;
                if (Check(TokenType.Less))            // list<Item>
                {
                    while (!Check(TokenType.Greater) && !Check(TokenType.EOF)) Advance();
                    Expect(TokenType.Greater, "'>'");
                    typeName = "var";
                }
                var name = Expect(TokenType.Identifier, "property name");
                var pn = new PropertyNode
                {
                    Name = name.Text,
                    IsDeclaration = true,
                    DeclaredType = typeName,
                    Line = name.Line,
                    NameOffset = name.Offset,
                    NameEnd = name.End
                };
                if (Check(TokenType.Colon))
                {
                    Advance();
                    pn.ValueStart = Cur.Offset;
                    if (AtObjectStart() || (Check(TokenType.LBracket) && ObjectListAhead()))
                    {
                        ParseObjectValue(owner, pn.Name);
                        pn.ValueEnd = PrevEnd;
                        owner.Properties.Add(pn);
                        return;
                    }
                    pn.Value = ParseExpression();
                    pn.ValueEnd = PrevEnd;
                }
                ConsumeOptionalSemicolon();
                owner.Properties.Add(pn);
                return;
            }

            // Must start with an identifier: `name:`, a dotted `group.member:` (anchors.left,
            // border.width, Component.onCompleted), or a nested `Type { }`.
            if (AtObjectStart())
            {
                owner.Children.Add(ParseObject());
                return;
            }

            var ident = Cur;
            string memberName = ParseDotted("member name");
            if (!Check(TokenType.Colon))
                throw new QuillParseException($"Unexpected '{Describe(Cur)}' after '{memberName}' (line {Cur.Line})", Cur);
            Advance(); // ':'

            if (memberName == "id")
            {
                var idTok = Expect(TokenType.Identifier, "id value");
                owner.Id = idTok.Text;
                owner.IdOffset = idTok.Offset;
                ConsumeOptionalSemicolon();
                return;
            }

            // Signal handler: `onClicked: stmt`, `onClicked: { ... }`, `Component.onCompleted: ...`,
            // and ScriptAction's `script:`.
            string last = memberName.Substring(memberName.LastIndexOf('.') + 1);
            int nameEnd = _tokens[_i - 2].End;   // just before the ':'
            int valueStart = Cur.Offset;
            if (IsHandlerName(last) || (owner.TypeName == "ScriptAction" && memberName == "script"))
            {
                var handler = new PropertyNode { Name = memberName, Line = ident.Line, NameOffset = ident.Offset, NameEnd = nameEnd, ValueStart = valueStart };
                handler.Handler = ParseHandler();
                handler.ValueEnd = PrevEnd;
                owner.Properties.Add(handler);
                return;
            }

            // Object-valued member: `transitions: Transition { }`, `states: [ State {}, State {} ]`.
            if (AtObjectStart() || (Check(TokenType.LBracket) && ObjectListAhead()))
            {
                ParseObjectValue(owner, memberName);
                return;
            }

            var pnode = new PropertyNode { Name = memberName, Line = ident.Line, NameOffset = ident.Offset, NameEnd = nameEnd, ValueStart = valueStart };
            pnode.Value = ParseExpression();
            pnode.ValueEnd = PrevEnd;
            ConsumeOptionalSemicolon();
            owner.Properties.Add(pnode);
        }

        // `[` followed by `Type {` — a list of objects rather than an array expression.
        private bool ObjectListAhead()
        {
            var a = PeekTok(1);
            var b = PeekTok(2);
            return a.Type == TokenType.Identifier && a.Text.Length > 0 && char.IsUpper(a.Text[0])
                   && (b.Type == TokenType.LBrace || (b.Type == TokenType.Identifier && b.Text == "on"));
        }

        private void ParseObjectValue(ObjectNode owner, string listName)
        {
            if (Check(TokenType.LBracket))
            {
                Advance();
                while (!Check(TokenType.RBracket) && !Check(TokenType.EOF))
                {
                    var child = ParseObject();
                    child.AssignedTo = listName;
                    owner.Children.Add(child);
                    if (Check(TokenType.Comma)) Advance();
                }
                Expect(TokenType.RBracket, "']'");
            }
            else
            {
                var child = ParseObject();
                child.AssignedTo = listName;
                owner.Children.Add(child);
            }
            ConsumeOptionalSemicolon();
        }

        private void ConsumeOptionalSemicolon()
        {
            if (Check(TokenType.Semicolon)) Advance();
        }

        private string[] ParseParamList()
        {
            Expect(TokenType.LParen, "'('");
            var names = new List<string>();
            while (!Check(TokenType.RParen) && !Check(TokenType.EOF))
            {
                names.Add(Expect(TokenType.Identifier, "parameter name").Text);
                if (Check(TokenType.Colon)) { Advance(); Expect(TokenType.Identifier, "parameter type"); } // `a: real`
                if (Check(TokenType.Comma)) Advance();
            }
            Expect(TokenType.RParen, "')'");
            if (Check(TokenType.Colon)) { Advance(); Expect(TokenType.Identifier, "return type"); }         // `): real`
            return names.ToArray();
        }

        // ---- Statements -------------------------------------------------------------------------

        private static bool IsHandlerName(string name)
            => name.Length > 2 && name[0] == 'o' && name[1] == 'n' && char.IsUpper(name[2]);

        private List<HandlerStmt> ParseHandler()
        {
            if (Check(TokenType.LBrace)) return ParseBlockBody();
            var list = new List<HandlerStmt> { ParseStatement() };
            ConsumeOptionalSemicolon();
            return list;
        }

        private List<HandlerStmt> ParseBlockBody()
        {
            Expect(TokenType.LBrace, "'{'");
            var list = new List<HandlerStmt>();
            while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
            {
                list.Add(ParseStatement());
                ConsumeOptionalSemicolon();
            }
            Expect(TokenType.RBrace, "'}'");
            return list;
        }

        private HandlerStmt ParseStatement()
        {
            int line = Cur.Line;

            if (Check(TokenType.LBrace))
                return new BlockStmt { Body = ParseBlockBody(), Line = line };

            if (Check(TokenType.Semicolon)) { Advance(); return new BlockStmt { Line = line }; }

            if (CheckWord("if"))
            {
                Advance();
                Expect(TokenType.LParen, "'(' after if");
                var cond = ParseExpression();
                Expect(TokenType.RParen, "')'");
                var then = ParseStatement();
                ConsumeOptionalSemicolon();
                HandlerStmt els = null;
                if (CheckWord("else"))
                {
                    Advance();
                    els = ParseStatement();
                }
                return new IfStmt { Cond = cond, Then = then, Else = els, Line = line };
            }

            if (CheckWord("for"))
            {
                Advance();
                Expect(TokenType.LParen, "'(' after for");
                HandlerStmt init = Check(TokenType.Semicolon) ? null : ParseStatement();
                Expect(TokenType.Semicolon, "';' in for");
                ExprNode cond = Check(TokenType.Semicolon) ? null : ParseExpression();
                Expect(TokenType.Semicolon, "';' in for");
                HandlerStmt step = Check(TokenType.RParen) ? null : ParseStatement();
                Expect(TokenType.RParen, "')'");
                return new ForStmt { Init = init, Cond = cond, Step = step, Body = ParseStatement(), Line = line };
            }

            if (CheckWord("while"))
            {
                Advance();
                Expect(TokenType.LParen, "'(' after while");
                var cond = ParseExpression();
                Expect(TokenType.RParen, "')'");
                return new WhileStmt { Cond = cond, Body = ParseStatement(), Line = line };
            }

            if ((CheckWord("var") || CheckWord("let") || CheckWord("const")) && PeekTok().Type == TokenType.Identifier)
            {
                Advance();
                var name = Expect(TokenType.Identifier, "variable name").Text;
                ExprNode value = null;
                if (Check(TokenType.Assign)) { Advance(); value = ParseExpression(); }
                return new VarStmt { Name = name, Value = value, Line = line };
            }

            if (CheckWord("return"))
            {
                Advance();
                ExprNode value = null;
                if (!Check(TokenType.Semicolon) && !Check(TokenType.RBrace) && !Check(TokenType.EOF)
                    && Cur.Line == line)
                    value = ParseExpression();
                return new ReturnStmt { Value = value, Line = line };
            }
            if (CheckWord("break")) { Advance(); return new BreakStmt { Line = line }; }
            if (CheckWord("continue")) { Advance(); return new ContinueStmt { Line = line }; }

            // Prefix ++x / --x
            if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            {
                var op = Advance().Type;
                return new AssignStmt { Target = CheckLValue(ParsePostfix()), Op = op, Line = line };
            }

            var expr = ParsePostfix();

            switch (Cur.Type)
            {
                case TokenType.Assign:
                case TokenType.PlusAssign:
                case TokenType.MinusAssign:
                case TokenType.StarAssign:
                case TokenType.SlashAssign:
                {
                    var op = Advance().Type;
                    return new AssignStmt { Target = CheckLValue(expr), Op = op, Value = ParseExpression(), Line = line };
                }
                case TokenType.PlusPlus:
                case TokenType.MinusMinus:
                {
                    var op = Advance().Type;
                    return new AssignStmt { Target = CheckLValue(expr), Op = op, Line = line };
                }
            }

            // Anything else is an expression statement; allow a trailing operator chain too
            // (e.g. `a && b()` is rare, but `foo()` is the common case).
            if (!(expr is CallNode))
                expr = ContinueExpression(expr);
            return new ExprStmt { Expr = expr, Line = line };
        }

        private ExprNode CheckLValue(ExprNode e)
        {
            if (e is IdentifierNode || e is MemberNode || e is IndexNode) return e;
            throw new QuillParseException($"Invalid assignment target (line {Cur.Line})", Cur);
        }

        // ---- Expression parsing (precedence climbing) ------------------------------------------

        private static int Precedence(TokenType t)
        {
            switch (t)
            {
                case TokenType.Star:
                case TokenType.Slash:
                case TokenType.Percent: return 8;
                case TokenType.Plus:
                case TokenType.Minus: return 7;
                case TokenType.Less:
                case TokenType.Greater:
                case TokenType.LessEqual:
                case TokenType.GreaterEqual: return 6;
                case TokenType.EqualEqual:
                case TokenType.NotEqual: return 5;
                case TokenType.BitAnd: return 4;
                case TokenType.BitOr: return 3;
                case TokenType.And: return 2;
                case TokenType.Or: return 1;
                default: return -1;
            }
        }

        public ExprNode ParseExpression() => ParseTernary(ParseBinary(0, ParseUnary()));

        // Finish an expression whose leftmost operand was already parsed (expression statements).
        private ExprNode ContinueExpression(ExprNode left) => ParseTernary(ParseBinary(0, left));

        private ExprNode ParseTernary(ExprNode cond)
        {
            if (Check(TokenType.Question))
            {
                Advance();
                var a = ParseExpression();
                Expect(TokenType.Colon, "':' in ternary");
                var b = ParseExpression();
                return new TernaryNode { Cond = cond, WhenTrue = a, WhenFalse = b };
            }
            return cond;
        }

        private ExprNode ParseBinary(int minPrec, ExprNode left)
        {
            while (true)
            {
                int prec = Precedence(Cur.Type);
                if (prec < minPrec || prec < 0) break;
                var op = Advance().Type;
                var right = ParseBinary(prec + 1, ParseUnary()); // left-associative
                left = new BinaryNode { Op = op, Left = left, Right = right };
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            if (Check(TokenType.Minus) || Check(TokenType.Not) || Check(TokenType.Plus))
            {
                var op = Advance().Type;
                return new UnaryNode { Op = op, Operand = ParseUnary() };
            }
            return ParsePostfix();
        }

        private ExprNode ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                if (Check(TokenType.Dot))
                {
                    Advance();
                    var member = Expect(TokenType.Identifier, "member name");
                    expr = new MemberNode { Target = expr, Member = member.Text };
                }
                else if (Check(TokenType.LBracket))
                {
                    Advance();
                    var index = ParseExpression();
                    Expect(TokenType.RBracket, "']'");
                    expr = new IndexNode { Target = expr, Index = index };
                }
                else if (Check(TokenType.LParen))
                {
                    // Call: `foo(a)`, `Math.min(a, b)`, `anim.start()`, `value.toFixed(2)`.
                    Advance();
                    var args = new List<ExprNode>();
                    if (!Check(TokenType.RParen))
                    {
                        args.Add(ParseExpression());
                        while (Check(TokenType.Comma)) { Advance(); args.Add(ParseExpression()); }
                    }
                    Expect(TokenType.RParen, "')'");

                    if (expr is IdentifierNode id) expr = new CallNode { Target = null, Name = id.Name, Args = args };
                    else if (expr is MemberNode m) expr = new CallNode { Target = m.Target, Name = m.Member, Args = args };
                    else throw new QuillParseException($"Call target must be a name (line {Cur.Line})", Cur);
                }
                else break;
            }
            return expr;
        }

        private ExprNode ParsePrimary()
        {
            switch (Cur.Type)
            {
                case TokenType.Number: return new NumberNode { Value = Advance().Number };
                case TokenType.String: return new StringNode { Value = Advance().Text };
                case TokenType.Bool: return new BoolNode { Value = Advance().Bool };
                case TokenType.Identifier:
                {
                    var t = Advance();
                    if (t.Text == "null" || t.Text == "undefined") return new NullNode();
                    return new IdentifierNode { Name = t.Text };
                }
                case TokenType.LParen:
                {
                    Advance();
                    var inner = ParseExpression();
                    Expect(TokenType.RParen, "')'");
                    return inner;
                }
                case TokenType.LBracket:
                {
                    Advance();
                    var arr = new ArrayNode();
                    while (!Check(TokenType.RBracket) && !Check(TokenType.EOF))
                    {
                        arr.Items.Add(ParseExpression());
                        if (Check(TokenType.Comma)) Advance();
                        else break;
                    }
                    Expect(TokenType.RBracket, "']'");
                    return arr;
                }
                default:
                    throw new QuillParseException($"Unexpected '{Describe(Cur)}' in expression (line {Cur.Line})", Cur);
            }
        }
    }
}
