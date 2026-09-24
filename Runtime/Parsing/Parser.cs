// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill.Parsing
{
    public sealed class QuillParseException : Exception
    {
        public QuillParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Recursive-descent parser producing an <see cref="ObjectNode"/> tree, with a Pratt
    /// (precedence-climbing) sub-parser for binding expressions.
    ///
    /// Grammar (subset):
    ///   document   := object
    ///   object     := Ident '{' member* '}'
    ///   member     := 'id' ':' Ident
    ///               | 'property' Ident Ident [ ':' expr ]
    ///               | Ident ':' expr
    ///               | object
    ///   expr       := precedence-climbing over + - * / comparisons, unary -, member access, calls of ()
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
        private Token Advance() => _tokens[_i++];
        private bool Check(TokenType t) => Cur.Type == t;

        private Token Expect(TokenType t, string what)
        {
            if (Cur.Type != t)
                throw new QuillParseException($"Expected {what} but found '{Cur.Text}' (line {Cur.Line})");
            return Advance();
        }

        public ObjectNode ParseDocument()
        {
            var root = ParseObject();
            if (!Check(TokenType.EOF))
                throw new QuillParseException($"Unexpected trailing '{Cur.Text}' (line {Cur.Line})");
            return root;
        }

        private ObjectNode ParseObject()
        {
            var typeTok = Expect(TokenType.Identifier, "type name");
            var node = new ObjectNode { TypeName = typeTok.Text, Line = typeTok.Line };

            // Optional `on <property>` (e.g. `NumberAnimation on phase { ... }`).
            if (Check(TokenType.Identifier) && Cur.Text == "on")
            {
                Advance();
                node.OnProperty = Expect(TokenType.Identifier, "property after 'on'").Text;
            }

            Expect(TokenType.LBrace, "'{'");

            while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
                ParseMember(node);

            Expect(TokenType.RBrace, "'}'");
            return node;
        }

        private void ParseMember(ObjectNode owner)
        {
            // `signal clicked` / `signal clicked()` — record the name; params are ignored.
            if (Check(TokenType.Identifier) && Cur.Text == "signal")
            {
                Advance();
                var sig = Expect(TokenType.Identifier, "signal name");
                owner.Signals.Add(sig.Text);
                if (Check(TokenType.LParen))
                {
                    while (!Check(TokenType.RParen) && !Check(TokenType.EOF)) Advance();
                    Expect(TokenType.RParen, "')'");
                }
                ConsumeOptionalSemicolon();
                return;
            }

            // `property <type> <name> [: expr]`
            if (Check(TokenType.Identifier) && Cur.Text == "property")
            {
                Advance(); // property
                var type = Expect(TokenType.Identifier, "property type");
                var name = Expect(TokenType.Identifier, "property name");
                var pn = new PropertyNode
                {
                    Name = name.Text,
                    IsDeclaration = true,
                    DeclaredType = type.Text,
                    Line = name.Line
                };
                if (Check(TokenType.Colon))
                {
                    Advance();
                    pn.Value = ParseExpression();
                }
                ConsumeOptionalSemicolon();
                owner.Properties.Add(pn);
                return;
            }

            // Must start with an identifier: either `name:`, a dotted `group.member: expr`
            // (e.g. anchors.left, border.width), or a nested `Type { }`.
            var ident = Expect(TokenType.Identifier, "member name");

            // Dotted left-hand side: collapse `anchors.left` into a single property name.
            if (Check(TokenType.Dot))
            {
                string name = ident.Text;
                while (Check(TokenType.Dot))
                {
                    Advance();
                    var seg = Expect(TokenType.Identifier, "member name");
                    name += "." + seg.Text;
                }
                Expect(TokenType.Colon, "':'");
                var dn = new PropertyNode { Name = name, Value = ParseExpression(), Line = ident.Line };
                ConsumeOptionalSemicolon();
                owner.Properties.Add(dn);
                return;
            }

            if (Check(TokenType.Colon))
            {
                Advance(); // ':'

                if (ident.Text == "id")
                {
                    var idTok = Expect(TokenType.Identifier, "id value");
                    owner.Id = idTok.Text;
                    ConsumeOptionalSemicolon();
                    return;
                }

                // Signal handler: `onClicked: a = b` or `onClicked: { a = b; c = d }`.
                if (IsHandlerName(ident.Text))
                {
                    owner.Properties.Add(new PropertyNode
                    {
                        Name = ident.Text,
                        Handler = ParseHandler(),
                        Line = ident.Line
                    });
                    return;
                }

                var pn = new PropertyNode { Name = ident.Text, Value = ParseExpression(), Line = ident.Line };
                ConsumeOptionalSemicolon();
                owner.Properties.Add(pn);
                return;
            }

            // Nested object: `Type { }` or `Type on prop { }`.
            if (Check(TokenType.LBrace) || (Check(TokenType.Identifier) && Cur.Text == "on"))
            {
                // Rewind one token so ParseObject re-reads the type name.
                _i--;
                owner.Children.Add(ParseObject());
                return;
            }

            throw new QuillParseException($"Unexpected '{Cur.Text}' after '{ident.Text}' (line {Cur.Line})");
        }

        private void ConsumeOptionalSemicolon()
        {
            if (Check(TokenType.Semicolon)) Advance();
        }

        // ---- Signal handlers (assignment statements) -------------------------------------------

        private static bool IsHandlerName(string name)
            => name.Length > 2 && name[0] == 'o' && name[1] == 'n' && char.IsUpper(name[2]);

        private List<HandlerStmt> ParseHandler()
        {
            var list = new List<HandlerStmt>();
            if (Check(TokenType.LBrace))
            {
                Advance();
                while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
                {
                    list.Add(ParseStatement());
                    ConsumeOptionalSemicolon();
                }
                Expect(TokenType.RBrace, "'}'");
            }
            else
            {
                list.Add(ParseStatement());
                ConsumeOptionalSemicolon();
            }
            return list;
        }

        // A handler statement is either `path = expr` (assignment) or `path()` (signal emit).
        private HandlerStmt ParseStatement()
        {
            var path = new List<string> { Expect(TokenType.Identifier, "statement target").Text };
            while (Check(TokenType.Dot))
            {
                Advance();
                path.Add(Expect(TokenType.Identifier, "member name").Text);
            }

            if (Check(TokenType.LParen))
            {
                Advance();
                Expect(TokenType.RParen, "')'");
                return new CallStmt { Path = path.ToArray() };
            }

            Expect(TokenType.Assign, "'='");
            return new AssignStmt { Path = path.ToArray(), Value = ParseExpression() };
        }

        // ---- Expression parsing (precedence climbing) ------------------------------------------

        private static int Precedence(TokenType t)
        {
            switch (t)
            {
                case TokenType.Star:
                case TokenType.Slash:
                case TokenType.Percent: return 6;
                case TokenType.Plus:
                case TokenType.Minus: return 5;
                case TokenType.Less:
                case TokenType.Greater:
                case TokenType.LessEqual:
                case TokenType.GreaterEqual: return 4;
                case TokenType.EqualEqual:
                case TokenType.NotEqual: return 3;
                case TokenType.And: return 2;
                case TokenType.Or: return 1;
                default: return -1;
            }
        }

        public ExprNode ParseExpression()
        {
            var cond = ParseBinary(0);
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

        private ExprNode ParseBinary(int minPrec)
        {
            var left = ParseUnary();
            while (true)
            {
                int prec = Precedence(Cur.Type);
                if (prec < minPrec || prec < 0) break;
                var op = Advance().Type;
                var right = ParseBinary(prec + 1); // left-associative
                left = new BinaryNode { Op = op, Left = left, Right = right };
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            if (Check(TokenType.Minus) || Check(TokenType.Not))
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
                else if (Check(TokenType.LParen))
                {
                    // Function call, e.g. Math.abs(x) or Math.min(a, b).
                    Advance();
                    var args = new List<ExprNode>();
                    if (!Check(TokenType.RParen))
                    {
                        args.Add(ParseExpression());
                        while (Check(TokenType.Comma)) { Advance(); args.Add(ParseExpression()); }
                    }
                    Expect(TokenType.RParen, "')'");
                    expr = new CallNode { Name = ExtractCallPath(expr), Args = args };
                }
                else break;
            }
            return expr;
        }

        // Flatten an identifier / member chain (the call target) into a dotted name path.
        private static string[] ExtractCallPath(ExprNode e)
        {
            var parts = new List<string>();
            while (e is MemberNode m) { parts.Add(m.Member); e = m.Target; }
            if (e is IdentifierNode id) { parts.Add(id.Name); parts.Reverse(); return parts.ToArray(); }
            throw new QuillParseException("call target must be a function name");
        }

        private ExprNode ParsePrimary()
        {
            switch (Cur.Type)
            {
                case TokenType.Number: return new NumberNode { Value = Advance().Number };
                case TokenType.String: return new StringNode { Value = Advance().Text };
                case TokenType.Bool: return new BoolNode { Value = Advance().Bool };
                case TokenType.Identifier: return new IdentifierNode { Name = Advance().Text };
                case TokenType.LParen:
                    Advance();
                    var inner = ParseExpression();
                    Expect(TokenType.RParen, "')'");
                    return inner;
                default:
                    throw new QuillParseException($"Unexpected '{Cur.Text}' in expression (line {Cur.Line})");
            }
        }
    }
}
