using System;
using System.Collections.Generic;
using System.Text;

namespace Ollang.Lexer
{
    public class Token
    {
        public string Type;
        public string Value;
        public int Line;
        public int Column;

        public Token(string type, string value, int line = 0, int column = 0)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }
    }

    public class LexerState
    {
        private readonly string _src;
        private int _pos = 0;
        private int _line = 1;
        private int _column = 1;

        private static readonly HashSet<string> _keywords = new HashSet<string>
        {
            "var", "func", "if", "else", "while", "return", "true", "false", "null",
            "for", "import", "in", "and", "or", "break", "continue", "try", "catch",
            "finally", "throw", "class", "self", "new", "switch", "case", "do",
            "static", "bool", "num", "str", "void", "int", "float", "double",
            "string", "any", "object", "as", "const", "default"
        };

        private static readonly string[] _multiOps =
        {
            "==", "!=", "<=", ">=",
            "&&", "||", "**", "<<", ">>", "^^"
        };

        private static readonly HashSet<char> _singleOpChars = new HashSet<char>
        {
            '+', '-', '*', '/', '%', '=', '<', '>', '!', '&', '|', '^', '~'
        };

        public LexerState(string source) => _src = source;

        private char Current => _pos < _src.Length ? _src[_pos] : '\0';
        private char Peek(int offset) => _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

        private void Advance(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                if (Current == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                    _column++;
                _pos++;
            }
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (_pos < _src.Length)
            {
                if (char.IsWhiteSpace(Current))
                {
                    Advance();
                    continue;
                }

                if (Current == '/' && Peek(1) == '/')
                {
                    while (Current != '\n' && Current != '\0') Advance();
                    continue;
                }

                if (Current == '/' && Peek(1) == '*')
                {
                    Advance(2);
                    while (!(_pos >= _src.Length || (Current == '*' && Peek(1) == '/')))
                        Advance();
                    if (_pos < _src.Length) Advance(2);
                    continue;
                }

                if (Current == '#')
                {
                    while (Current != '\n' && Current != '\0') Advance();
                    continue;
                }

                int startLine = _line;
                int startCol = _column;

                if (char.IsLetter(Current) || Current == '_')
                {
                    var sb = new StringBuilder();

                    while (char.IsLetterOrDigit(Current) || Current == '_')
                    {
                        sb.Append(Current);
                        Advance();
                    }

                    string value = sb.ToString();
                    tokens.Add(new Token(_keywords.Contains(value) ? "keyword" : "id", value, startLine, startCol));
                    continue;
                }

                if (char.IsDigit(Current))
                {
                    var sb = new StringBuilder();
                    bool hasDot = false;

                    if (Current == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
                    {
                        sb.Append(Current); Advance();
                        sb.Append(Current); Advance();
                        while (IsHexDigit(Current))
                        {
                            sb.Append(Current);
                            Advance();
                        }
                    }
                    else
                    {
                        while (char.IsDigit(Current) || (Current == '.' && !hasDot && char.IsDigit(Peek(1))))
                        {
                            if (Current == '.') hasDot = true;
                            sb.Append(Current);
                            Advance();
                        }
                    }

                    tokens.Add(new Token("num", sb.ToString(), startLine, startCol));
                    continue;
                }

                if (Current == '"')
                {
                    Advance();
                    var sb = new StringBuilder();

                    while (Current != '"' && Current != '\0')
                    {
                        if (Current == '\\')
                        {
                            Advance();
                            switch (Current)
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case '"': sb.Append('"'); break;
                                case '\\': sb.Append('\\'); break;
                                case '0': sb.Append('\0'); break;
                                default: sb.Append(Current); break;
                            }
                            Advance();
                        }
                        else
                        {
                            sb.Append(Current);
                            Advance();
                        }
                    }

                    if (Current == '"') Advance();
                    else throw new Exception($"Lexer error at {startLine}:{startCol}: Unterminated string literal");
                    tokens.Add(new Token("str", sb.ToString(), startLine, startCol));
                    continue;
                }

                if (Current == '\'')
                {
                    Advance();
                    var sb = new StringBuilder();
                    while (Current != '\'' && Current != '\0')
                    {
                        if (Current == '\\')
                        {
                            Advance();
                            switch (Current)
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case '\'': sb.Append('\''); break;
                                case '\\': sb.Append('\\'); break;
                                default: sb.Append(Current); break;
                            }
                            Advance();
                        }
                        else
                        {
                            sb.Append(Current);
                            Advance();
                        }
                    }
                    if (Current == '\'') Advance();
                    else throw new Exception($"Lexer error at {startLine}:{startCol}: Unterminated character literal");
                    tokens.Add(new Token("str", sb.ToString(), startLine, startCol));
                    continue;
                }

                bool matched = false;

                foreach (var op in _multiOps)
                {
                    bool match = true;
                    for (int i = 0; i < op.Length; i++)
                    {
                        if (Peek(i) != op[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        tokens.Add(new Token("op", op, startLine, startCol));
                        Advance(op.Length);
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    tokens.Add(new Token(
                        _singleOpChars.Contains(Current) ? "op" : "punc",
                        Current.ToString(),
                        startLine,
                        startCol
                    ));
                    Advance();
                }
            }

            return tokens;
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}