// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Quill.Parsing
{
    public enum TokenType
    {
        Identifier, Number, String, Bool,
        LBrace, RBrace, LParen, RParen,
        Colon, Dot, Comma, Semicolon,
        Plus, Minus, Star, Slash, Percent,
        Less, Greater, LessEqual, GreaterEqual, EqualEqual, NotEqual,
        And, Or, Not,
        Assign, Question,
        EOF
    }

    public struct Token
    {
        public TokenType Type;
        public string Text;
        public double Number;
        public bool Bool;
        public int Line;

        public override string ToString() => $"{Type}:'{Text}'@{Line}";
    }

    /// <summary>
    /// Hand-written tokenizer for the Quill subset. Skips comments (// and /* */) and `import` lines.
    /// </summary>
    public sealed class Lexer
    {
        private readonly string _src;
        private int _pos;
        private int _line = 1;

        public Lexer(string src) { _src = src ?? string.Empty; }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            Token t;
            do
            {
                t = Next();
                tokens.Add(t);
            } while (t.Type != TokenType.EOF);
            return tokens;
        }

        private char Cur => _pos < _src.Length ? _src[_pos] : '\0';
        private char Peek(int o = 1) => _pos + o < _src.Length ? _src[_pos + o] : '\0';

        private Token Next()
        {
            SkipTrivia();
            if (_pos >= _src.Length)
                return new Token { Type = TokenType.EOF, Line = _line };

            char c = Cur;

            if (char.IsLetter(c) || c == '_') return ReadIdentifier();
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek()))) return ReadNumber();
            if (c == '"' || c == '\'') return ReadString(c);

            return ReadSymbol();
        }

        private void SkipTrivia()
        {
            while (_pos < _src.Length)
            {
                char c = Cur;
                if (c == '\n') { _line++; _pos++; }
                else if (char.IsWhiteSpace(c)) { _pos++; }
                else if (c == '/' && Peek() == '/')
                {
                    while (_pos < _src.Length && Cur != '\n') _pos++;
                }
                else if (c == '/' && Peek() == '*')
                {
                    _pos += 2;
                    while (_pos < _src.Length && !(Cur == '*' && Peek() == '/'))
                    {
                        if (Cur == '\n') _line++;
                        _pos++;
                    }
                    _pos += 2;
                }
                else break;
            }
        }

        private Token ReadIdentifier()
        {
            int start = _pos;
            while (_pos < _src.Length && (char.IsLetterOrDigit(Cur) || Cur == '_')) _pos++;
            string text = _src.Substring(start, _pos - start);

            // `import ...` statements run to end of line and are ignored for this subset.
            if (text == "import")
            {
                while (_pos < _src.Length && Cur != '\n') _pos++;
                return Next();
            }

            if (text == "true") return new Token { Type = TokenType.Bool, Text = text, Bool = true, Line = _line };
            if (text == "false") return new Token { Type = TokenType.Bool, Text = text, Bool = false, Line = _line };

            return new Token { Type = TokenType.Identifier, Text = text, Line = _line };
        }

        private Token ReadNumber()
        {
            int start = _pos;
            while (_pos < _src.Length && (char.IsDigit(Cur) || Cur == '.')) _pos++;
            string text = _src.Substring(start, _pos - start);
            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n);
            return new Token { Type = TokenType.Number, Text = text, Number = n, Line = _line };
        }

        private Token ReadString(char quote)
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (_pos < _src.Length && Cur != quote)
            {
                if (Cur == '\\' && _pos + 1 < _src.Length)
                {
                    _pos++;
                    switch (Cur)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(Cur); break;
                    }
                    _pos++;
                }
                else
                {
                    if (Cur == '\n') _line++;
                    sb.Append(Cur);
                    _pos++;
                }
            }
            _pos++; // closing quote
            return new Token { Type = TokenType.String, Text = sb.ToString(), Line = _line };
        }

        private Token ReadSymbol()
        {
            char c = Cur;
            char n = Peek();
            int line = _line;

            Token Two(TokenType tt) { _pos += 2; return new Token { Type = tt, Text = $"{c}{n}", Line = line }; }
            Token One(TokenType tt) { _pos += 1; return new Token { Type = tt, Text = c.ToString(), Line = line }; }

            switch (c)
            {
                case '{': return One(TokenType.LBrace);
                case '}': return One(TokenType.RBrace);
                case '(': return One(TokenType.LParen);
                case ')': return One(TokenType.RParen);
                case ':': return One(TokenType.Colon);
                case '.': return One(TokenType.Dot);
                case ',': return One(TokenType.Comma);
                case ';': return One(TokenType.Semicolon);
                case '+': return One(TokenType.Plus);
                case '-': return One(TokenType.Minus);
                case '*': return One(TokenType.Star);
                case '/': return One(TokenType.Slash);
                case '%': return One(TokenType.Percent);
                case '<': return n == '=' ? Two(TokenType.LessEqual) : One(TokenType.Less);
                case '>': return n == '=' ? Two(TokenType.GreaterEqual) : One(TokenType.Greater);
                case '=': return n == '=' ? Two(TokenType.EqualEqual) : One(TokenType.Assign);
                case '!': return n == '=' ? Two(TokenType.NotEqual) : One(TokenType.Not);
                case '&': return n == '&' ? Two(TokenType.And) : One(TokenType.Identifier);
                case '|': return n == '|' ? Two(TokenType.Or) : One(TokenType.Identifier);
                case '?': return One(TokenType.Question);
                default:
                    _pos++; // unknown char: skip to stay robust
                    return Next();
            }
        }
    }
}
