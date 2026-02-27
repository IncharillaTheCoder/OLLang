using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ollang.Ast;
using Ollang.Lexer;

namespace Ollang.Parser
{
    public class ParserState
    {
        private readonly List<Token> _tokens;
        private int _pos = 0;

        public ParserState(List<Token> tokens) => _tokens = tokens;

        private Token? Current => _pos < _tokens.Count ? _tokens[_pos] : null;
        private Token? Peek(int offset) => (_pos + offset) < _tokens.Count ? _tokens[_pos + offset] : null;
        private void Advance() => _pos++;

        private bool IsType(Token? t)
        {
            if (t == null) return false;
            if (t.Type == "id") return true; 
            if (t.Type == "keyword")
                return t.Value == "bool" || t.Value == "num" || t.Value == "str" || t.Value == "void" ||
                       t.Value == "int" || t.Value == "float" || t.Value == "double" || t.Value == "string" ||
                       t.Value == "any" || t.Value == "object";
            return false;
        }

        private bool Match(string type, string? value = null)
        {
            if (Current == null || Current.Type != type) return false;
            if (value != null && Current.Value != value) return false;
            return true;
        }

        private Token Expect(string type, string? value = null)
        {
            if (!Match(type, value))
            {
                string got = Current == null ? "end of file" : $"'{Current.Value}'";
                string expected = value == null ? type : $"'{value}'";
                int line = Current?.Line ?? 0;
                int col = Current?.Column ?? 0;
                throw new Exception($"Parse error at {line}:{col}: Expected {expected}, but got {got}");
            }
            var token = Current!;
            Advance();
            return token;
        }

        private string ExpectId()
        {
            if (Match("id")) return Expect("id").Value;
            if (IsType(Current)) 
            {
                var val = Current!.Value;
                Advance();
                return val;
            }
            int line = Current?.Line ?? 0;
            int col = Current?.Column ?? 0;
            throw new Exception($"Parse error at {line}:{col}: Expected identifier, but got '{Current?.Value}'");
        }

        private T Mark<T>(T node, Token? t = null) where T : Node
        {
            var tok = t ?? Current;
            if (tok != null)
            {
                node.Line = tok.Line;
                node.Column = tok.Column;
            }
            return node;
        }

        public ProgramNode Parse()
        {
            var program = new ProgramNode();
            while (_pos < _tokens.Count)
            {
                if (Match("keyword", "import"))
                    program.Body.Add(ParseImport());
                else
                    program.Body.Add(ParseStatement());
            }
            return program;
        }

        private Node ParseImport()
        {
            Expect("keyword", "import");
            string importPath = Expect("str").Value;
            string? alias = null;
            
            if (Match("keyword", "as"))
            {
                Advance();
                alias = ExpectId();
            }
            
            if (Match("punc", ";")) Advance();

            return new ImportNode { Path = importPath, Alias = alias };
        }

        private Node ParseStatement()
        {
            if (Match("keyword", "var") || Match("keyword", "const"))
            {
                var startTok = Current;
                Advance();
                var node = ParseStatement();
                if (node is ExpressionStatementNode esn && esn.Expression is AssignmentNode assign)
                    return Mark(new DeclarationNode { Name = assign.Name, Value = assign.Value }, startTok);
                if (node is AssignmentNode directAssign)
                    return Mark(new DeclarationNode { Name = directAssign.Name, Value = directAssign.Value }, startTok);
                throw new Exception($"Parse error at {startTok?.Line}:{startTok?.Column}: Expected assignment after 'var'");
            }
            if (Match("keyword", "func") || (IsType(Current) && Peek(1)?.Type == "id" && Peek(2)?.Value == "(")) 
                return ParseFunction();
            if (Match("keyword", "if")) return ParseIf();
            if (Match("keyword", "while")) return ParseWhile();
            if (Match("keyword", "for")) return ParseFor();
            if (Match("keyword", "class")) return ParseClass();
            if (Match("keyword", "switch")) return ParseSwitch();
            if (Match("keyword", "do")) return ParseDoWhile();
            if (Match("keyword", "break"))
            {
                Advance();
                if (Match("punc", ";")) Advance();
                return new BreakNode();
            }
            if (Match("keyword", "continue"))
            {
                Advance();
                if (Match("punc", ";")) Advance();
                return new ContinueNode();
            }
            if (Match("keyword", "throw"))
            {
                Advance();
                var expr = ParseExpression();
                if (Match("punc", ";")) Advance();
                return new ThrowNode { Expression = expr };
            }
            if (Match("keyword", "try")) return ParseTry();
            if (Match("keyword", "return"))
            {
                var startTok = Current;
                Advance();
                var node = Mark(new ReturnNode { Expr = (Match("punc", ";") || Match("punc", "}")) ? null : ParseExpression() }, startTok);
                if (Match("punc", ";")) Advance();
                return node;
            }

            var startExprTok = Current;
            var exprNode = ParseExpression();
            if (Match("punc", ";")) Advance();
            return Mark(new ExpressionStatementNode { Expression = exprNode }, startExprTok);
        }

        private Node ParseClass()
        {
            Advance(); // class
            string name = ExpectId();
            string? parent = null;
            if (Match("punc", ":") || Match("punc", "<"))
            {
                Advance();
                parent = ExpectId();
            }

            Expect("punc", "{");
            var methods = new List<FunctionDefNode>();
            while (!Match("punc", "}"))
            {
                bool isStatic = false;
                if (Match("keyword", "static"))
                {
                    Advance();
                    isStatic = true;
                }

                string? returnType = "any";
                if (IsType(Current) && Peek(1)?.Type == "id" && Peek(2)?.Value == "(")
                {
                    returnType = Current.Value;
                    Advance();
                }
                else if (Match("keyword", "func"))
                {
                    Advance();
                }

                if (Match("id"))
                {
                    string methName = Expect("id").Value;

                    Expect("punc", "(");
                    var parameters = new List<string>();
                    if (!Match("punc", ")"))
                    {
                        parameters.Add(Expect("id").Value);
                        while (Match("punc", ","))
                        {
                            Advance();
                            parameters.Add(Expect("id").Value);
                        }
                    }
                    Expect("punc", ")");
                    var body = ParseBlock();
                    methods.Add(new FunctionDefNode { Name = methName, ReturnType = returnType, Params = parameters, Body = body, IsStatic = isStatic });
                }
                else if (Match("punc", ";"))
                {
                    Advance();
                }
                else
                {
                    throw new Exception($"Parse error at {Current?.Line}:{Current?.Column}: Unexpected token in class body: '{Current?.Value}'");
                }
            }
            Expect("punc", "}");
            return new ClassDefNode { Name = name, ParentName = parent, Methods = methods };
        }

        private Node ParseTry()
        {
            Advance(); // try
            var tryBody = ParseBlock();
            string? catchVar = null;
            var catchBody = new List<Node>();
            var finallyBody = new List<Node>();

            if (Match("keyword", "catch"))
            {
                Advance();
                if (Match("punc", "("))
                {
                    Advance();
                    catchVar = Expect("id").Value;
                    Expect("punc", ")");
                }
                catchBody = ParseBlock();
            }

            if (Match("keyword", "finally"))
            {
                Advance();
                finallyBody = ParseBlock();
            }

            return new TryNode
            {
                TryBody = tryBody,
                CatchVar = catchVar,
                CatchBody = catchBody,
                FinallyBody = finallyBody
            };
        }

        private Node ParseFunction()
        {
            string? returnType = "any";
            if (IsType(Current) && Peek(1)?.Type == "id" && Peek(2)?.Value == "(")
            {
                returnType = Current!.Value;
                Advance(); // type
            }
            else
            {
                Expect("keyword", "func");
            }

            string name = ExpectId();
            Expect("punc", "(");
            var parameters = new List<string>();
            if (!Match("punc", ")"))
            {
                parameters.Add(ExpectId());
                while (Match("punc", ","))
                {
                    Advance();
                    parameters.Add(ExpectId());
                }
            }
            Expect("punc", ")");
            return new FunctionDefNode
            {
                Name = name,
                ReturnType = returnType,
                Params = parameters,
                Body = ParseBlock()
            };
        }

        private List<Node> ParseBlock()
        {
            Expect("punc", "{");
            var body = new List<Node>();
            while (!Match("punc", "}"))
            {
                if (Current == null) throw new Exception("Parse error: Expected '}' but reached end of file");
                body.Add(ParseStatement());
            }
            Expect("punc", "}");
            return body;
        }

        private List<Node> ParseStatementOrBlock()
        {
            if (Match("punc", "{")) return ParseBlock();
            return new List<Node> { ParseStatement() };
        }

        private Node ParseIf()
        {
            Advance(); // if
            Expect("punc", "(");
            var condition = ParseExpression();
            Expect("punc", ")");
            var then = ParseStatementOrBlock();
            var @else = new List<Node>();
            if (Match("keyword", "else"))
            {
                Advance();
                @else = Match("keyword", "if") ? new List<Node> { ParseIf() } : ParseStatementOrBlock();
            }
            return new IfNode { Condition = condition, Then = then, Else = @else };
        }

        private Node ParseWhile()
        {
            Advance(); // while
            Expect("punc", "(");
            var condition = ParseExpression();
            Expect("punc", ")");
            return new WhileNode { Condition = condition, Body = ParseStatementOrBlock() };
        }

        private Node ParseFor()
        {
            Advance(); // for
            Expect("punc", "(");
            string iterator = ExpectId();
            Expect("keyword", "in");
            var iterable = ParseExpression();
            Expect("punc", ")");
            return new ForNode { IteratorName = iterator, Iterable = iterable, Body = ParseStatementOrBlock() };
        }

        private Node ParseSwitch()
        {
            Advance(); // switch
            Expect("punc", "(");
            var expr = ParseExpression();
            Expect("punc", ")");

            Expect("punc", "{");
            var node = new SwitchNode { Expression = expr };

            while (!Match("punc", "}"))
            {
                if (Match("keyword", "case"))
                {
                    Advance();
                    var caseValue = ParseExpression();
                    if (Match("punc", ":")) Advance();
                    var body = new List<Node>();
                    while (!Match("keyword", "case") && !Match("keyword", "default") && !Match("punc", "}"))
                    {
                        body.Add(ParseStatement());
                    }
                    node.Cases.Add(new SwitchCase { Value = caseValue, Body = body });
                }
                else if (Match("keyword", "default"))
                {
                    Advance();
                    if (Match("punc", ":")) Advance();
                    var body = new List<Node>();
                    while (!Match("keyword", "case") && !Match("keyword", "default") && !Match("punc", "}"))
                    {
                        body.Add(ParseStatement());
                    }
                    node.Cases.Add(new SwitchCase { Value = null, Body = body });
                }
                else if (Match("punc", ";"))
                {
                    Advance();
                }
                else
                {
                    throw new Exception($"Parse error at {Current?.Line}:{Current?.Column}: Unexpected token in switch body: '{Current?.Value}'");
                }
            }
            Expect("punc", "}");
            return node;
        }

        private Node ParseDoWhile()
        {
            Advance(); // do
            var body = ParseBlock();
            Expect("keyword", "while");
            Expect("punc", "(");
            var condition = ParseExpression();
            Expect("punc", ")");
            if (Match("punc", ";")) Advance();

            return new DoWhileNode { Condition = condition, Body = body };
        }

        private Node ParseExpression()
        {
            var left = ParseBinary(0);

            if (Match("op", "="))
            {
                Advance(); // Consume '='
                var right = ParseExpression();

                if (left is IdentifierNode id)
                    return new AssignmentNode { Name = id.Name, Value = right };

                if (left is IndexNode idx)
                    return new IndexAssignmentNode { Target = idx.Target, Index = idx.Index, Value = right };

                throw new Exception("Invalid assignment target");
            }
            return left;
        }

        private Node ParseBinary(int precedence)
        {
            var left = ParsePrimary();
            while (true)
            {
                var opToken = Current;
                if (opToken == null || (opToken.Type != "op" && opToken.Type != "keyword")) break;
                
                int p = GetPrecedence(opToken.Value);
                if (p <= precedence) break;
                
                Advance();
                left = new BinaryOpNode
                {
                    Left = left,
                    Op = opToken.Value,
                    Right = ParseBinary(p)
                };
            }
            return left;
        }

        private int GetPrecedence(string op)
        {
            switch (op)
            {
                case "or": case "||": return 1;
                case "and": case "&&": return 2;
                case "==": case "!=": return 3;
                case "<": case ">": case "<=": case ">=": return 4;
                case "|": return 5;
                case "^": return 6;
                case "&": return 7;
                case "<<": case ">>": return 8;
                case "+": case "-": return 9;
                case "*": case "/": case "%": return 10;
                case "**": case "^^": return 11;
                default: return 0;
            }
        }

        private Node ParsePrimary()
        {
            Node node;
            if (Match("num"))
            {
                var t = Expect("num");
                if (!double.TryParse(t.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    throw new Exception($"Parse error at {t.Line}:{t.Column}: Invalid number format: '{t.Value}'");
                node = Mark(new NumberNode(result), t);
            }
            else if (Match("str")) { var t = Expect("str"); node = Mark(new StringNode(t.Value), t); }
            else if (Match("keyword", "true")) { var t = Current; Advance(); node = Mark(new BooleanNode(true), t); }
            else if (Match("keyword", "false")) { var t = Current; Advance(); node = Mark(new BooleanNode(false), t); }
            else if (Match("keyword", "null")) { var t = Current; Advance(); node = Mark(new NullNode(), t); }
            else if (Match("keyword", "self")) { var t = Current; Advance(); node = Mark(new IdentifierNode("self"), t); }
            else if (Match("keyword", "new")) { Advance(); node = ParsePrimary(); }
            else if (Match("id") || IsType(Current))
            {
                node = Mark(new IdentifierNode(Current!.Value));
                Advance();
            }
            else if (Match("punc", "("))
            {
                Advance();
                node = ParseExpression();
                Expect("punc", ")");
            }
            else if (Match("punc", "["))
            {
                Advance();
                var arr = new ArrayNode();
                if (!Match("punc", "]"))
                {
                    arr.Elements.Add(ParseExpression());
                    while (Match("punc", ","))
                    {
                        Advance();
                        arr.Elements.Add(ParseExpression());
                    }
                }
                Expect("punc", "]");
                node = arr;
            }
            else if (Match("punc", "{"))
            {
                Advance();
                var dict = new DictNode();
                if (!Match("punc", "}"))
                {
                    do
                    {
                        var key = ParseExpression();
                        Expect("punc", ":");
                        var val = ParseExpression();
                        dict.Entries[key] = val;
                        if (!Match("punc", ",")) break;
                        Advance();
                    } while (!Match("punc", "}"));
                }
                Expect("punc", "}");
                node = dict;
            }
            else if (Match("op", "-") || Match("op", "!") || Match("op", "~"))
            {
                string op = Current!.Value;
                Advance();
                node = new UnaryOpNode { Op = op, Operand = ParsePrimary() };
            }
            else if (Match("keyword", "func"))
            {
                Advance(); // func
                Expect("punc", "(");
                var parameters = new List<string>();
                if (!Match("punc", ")"))
                {
                    parameters.Add(ExpectId());
                    while (Match("punc", ","))
                    {
                        Advance();
                        parameters.Add(ExpectId());
                    }
                }
                Expect("punc", ")");
                var body = ParseBlock();
                node = new FunctionDefNode
                {
                    Name = $"$lambda_{_pos}",
                    Params = parameters,
                    Body = body
                };
            }
            else
            {
                string got = Current == null ? "end of file" : $"'{Current.Value}'";
                throw new Exception($"Unexpected token: {got}");
            }

            while (true)
            {
                if (Match("punc", "("))
                {
                    Advance();
                    var args = new List<Node>();
                    if (!Match("punc", ")"))
                    {
                        args.Add(ParseExpression());
                        while (Match("punc", ","))
                        {
                            Advance();
                            args.Add(ParseExpression());
                        }
                    }
                    Expect("punc", ")");
                    node = new CallNode { Callee = node, Arguments = args };
                }
                else if (Match("punc", "["))
                {
                    Advance();
                    var index = ParseExpression();
                    Expect("punc", "]");
                    node = new IndexNode { Target = node, Index = index };
                }
                else if (Match("punc", "."))
                {
                    Advance();
                    string field = ExpectId();
                    node = new IndexNode { Target = node, Index = new StringNode(field) };
                }
                else break;
            }

            return node;
        }
    }
}