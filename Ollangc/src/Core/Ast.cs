using System;
using System.Collections.Generic;
using Ollang.Interpreter;
using Ollang.Values;

namespace Ollang.Ast
{
    public abstract class Node
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public abstract IValue Eval(InterpreterState state);

        protected Exception Error(string message)
        {
            return new Exception($"Runtime error at {Line}:{Column}: {message}");
        }
    }

    public class ProgramNode : Node
    {
        public List<Node> Body { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            IValue lastValue = new NullValue();
            foreach (var node in Body)
            {
                lastValue = node.Eval(state);
                if (state.IsReturning || state.IsBreaking || state.IsContinuing) return lastValue;
            }
            return lastValue;
        }
    }

    public class ImportNode : Node
    {
        public string Path { get; set; } = "";
        public string? Alias { get; set; }
        public override IValue Eval(InterpreterState state) => throw Error("Imports are only supported in compiled mode");
    }

    public class NumberNode : Node
    {
        public double Value { get; }
        public NumberNode(double value) => Value = value;
        public override IValue Eval(InterpreterState state) => new NumberValue(Value);
    }

    public class StringNode : Node
    {
        public string Value { get; }
        public StringNode(string value) => Value = value;
        public override IValue Eval(InterpreterState state) => new StringValue(Value);
    }

    public class BooleanNode : Node
    {
        public bool Value { get; }
        public BooleanNode(bool value) => Value = value;
        public override IValue Eval(InterpreterState state) => new BooleanValue(Value);
    }

    public class NullNode : Node
    {
        public override IValue Eval(InterpreterState state) => new NullValue();
    }

    public class IdentifierNode : Node
    {
        public string Name { get; }
        public IdentifierNode(string name) => Name = name;
        public override IValue Eval(InterpreterState state) => state.GetVar(Name);
    }

    public class AssignmentNode : Node
    {
        public string Name { get; set; } = "";
        public Node Value { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var val = Value.Eval(state);
            state.SetVar(Name, val);
            return val;
        }
    }

    public class DeclarationNode : Node
    {
        public string Name { get; set; } = "";
        public Node Value { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var val = Value.Eval(state);
            state.DefineVar(Name, val);
            return val;
        }
    }

    public class IndexAssignmentNode : Node
    {
        public Node Target { get; set; } = null!;
        public Node Index { get; set; } = null!;
        public Node Value { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var targetVal = Target.Eval(state);
            var indexVal = Index.Eval(state);
            var val = Value.Eval(state);
            
            targetVal.SetIndex(indexVal, val);
            return val;
        }
    }

    public class BinaryOpNode : Node
    {
        public Node Left { get; set; } = null!;
        public string Op { get; set; } = "";
        public Node Right { get; set; } = null!;

        public override IValue Eval(InterpreterState state)
        {
            var leftVal = Left.Eval(state);

            if (Op == "and" || Op == "&&")
            {
                if (!leftVal.AsBool()) return leftVal;
                return Right.Eval(state);
            }
            if (Op == "or" || Op == "||")
            {
                if (leftVal.AsBool()) return leftVal;
                return Right.Eval(state);
            }

            var rightVal = Right.Eval(state);

            switch (Op)
            {
                case "==": return new BooleanValue(leftVal.Equals(rightVal));
                case "!=": return new BooleanValue(!leftVal.Equals(rightVal));
                case "+":
                    if (leftVal is PointerValue pv_add && rightVal is NumberValue nv_add)
                        return new PointerValue(pv_add.Address + (int)nv_add.Value);
                    if (leftVal is NumberValue nv_add2 && rightVal is PointerValue pv_add2)
                        return new PointerValue(pv_add2.Address + (int)nv_add2.Value);
                    if (leftVal is StringValue || rightVal is StringValue)
                        return new StringValue(leftVal.ToString() + rightVal.ToString());
                    return new NumberValue(leftVal.AsNumber() + rightVal.AsNumber());
                case "-":
                    if (leftVal is PointerValue pv_sub && rightVal is NumberValue nv_sub)
                        return new PointerValue(pv_sub.Address - (int)nv_sub.Value);
                    if (leftVal is PointerValue pv_l && rightVal is PointerValue pv_r)
                        return new NumberValue((double)(pv_l.Address.ToInt64() - pv_r.Address.ToInt64()));
                    return new NumberValue(leftVal.AsNumber() - rightVal.AsNumber());
                case "*": return new NumberValue(leftVal.AsNumber() * rightVal.AsNumber());
                case "/": return new NumberValue(leftVal.AsNumber() / rightVal.AsNumber());
                case "%": return new NumberValue(leftVal.AsNumber() % rightVal.AsNumber());
                case "**":
                case "^^": return new NumberValue(Math.Pow(leftVal.AsNumber(), rightVal.AsNumber()));
                case "<": return new BooleanValue(leftVal.AsNumber() < rightVal.AsNumber());
                case ">": return new BooleanValue(leftVal.AsNumber() > rightVal.AsNumber());
                case "<=": return new BooleanValue(leftVal.AsNumber() <= rightVal.AsNumber());
                case ">=": return new BooleanValue(leftVal.AsNumber() >= rightVal.AsNumber());
                case "&": return new NumberValue((long)leftVal.AsNumber() & (long)rightVal.AsNumber());
                case "|": return new NumberValue((long)leftVal.AsNumber() | (long)rightVal.AsNumber());
                case "^": return new NumberValue((long)leftVal.AsNumber() ^ (long)rightVal.AsNumber());
                case "<<": return new NumberValue((long)leftVal.AsNumber() << (int)rightVal.AsNumber());
                case ">>": return new NumberValue((long)leftVal.AsNumber() >> (int)rightVal.AsNumber());
                default: throw Error($"Unknown operator '{Op}'");
            }
        }
    }

    public class UnaryOpNode : Node
    {
        public string Op { get; set; } = "";
        public Node Operand { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var val = Operand.Eval(state);
            switch (Op)
            {
                case "-": return new NumberValue(-val.AsNumber());
                case "!": return new BooleanValue(!val.AsBool());
                case "~": return new NumberValue(~(long)val.AsNumber());
                default: throw Error($"Unknown unary operator '{Op}'");
            }
        }
    }

    public class CallNode : Node
    {
        public Node Callee { get; set; } = null!;
        public List<Node> Arguments { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            var calleeVal = Callee.Eval(state);
            if (calleeVal is ICallable callable)
            {
                var args = new List<IValue>();
                foreach (var arg in Arguments) args.Add(arg.Eval(state));
                return callable.Call(state, args);
            }
            throw Error("Target is not callable");
        }
    }

    public class ArrayNode : Node
    {
        public List<Node> Elements { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            var array = new ArrayValue();
            foreach (var elem in Elements) array.Elements.Add(elem.Eval(state));
            return array;
        }
    }

    public class DictNode : Node
    {
        public Dictionary<Node, Node> Entries { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            var dict = new DictValue();
            foreach (var kvp in Entries)
            {
                dict.Entries[kvp.Key.Eval(state)] = kvp.Value.Eval(state);
            }
            return dict;
        }
    }

    public class IndexNode : Node
    {
        public Node Target { get; set; } = null!;
        public Node Index { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var targetVal = Target.Eval(state);
            var indexVal = Index.Eval(state);
            
            if (indexVal.ToString() == "offset")
                return targetVal.GetOffset();

            return targetVal.GetIndex(indexVal);
        }
    }

    public class FunctionDefNode : Node
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "any";
        public List<string> Attributes { get; set; } = new();
        public List<string> Params { get; set; } = new();
        public List<Node> Body { get; set; } = new();
        public bool IsStatic { get; set; } = false;
        public override IValue Eval(InterpreterState state)
        {
            var func = new FunctionValue(Name, Params, Body);
            state.DefineVar(Name, func);
            return func;
        }
    }

    public class IfNode : Node
    {
        public Node Condition { get; set; } = null!;
        public List<Node> Then { get; set; } = new();
        public List<Node> Else { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            IValue res = new NullValue();
            if (Condition.Eval(state).AsBool())
            {
                foreach (var node in Then)
                {
                    res = node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) return res;
                }
            }
            else
            {
                foreach (var node in Else)
                {
                    res = node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) return res;
                }
            }
            return res;
        }
    }

    public class WhileNode : Node
    {
        public Node Condition { get; set; } = null!;
        public List<Node> Body { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            IValue res = new NullValue();
            while (Condition.Eval(state).AsBool())
            {
                foreach (var node in Body)
                {
                    res = node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                }
                if (state.IsReturning) return res;
                if (state.IsBreaking)
                {
                    state.IsBreaking = false;
                    return res;
                }
                state.IsContinuing = false;
            }
            return res;
        }
    }

    public class DoWhileNode : Node
    {
        public Node Condition { get; set; } = null!;
        public List<Node> Body { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            IValue res = new NullValue();
            do
            {
                foreach (var node in Body)
                {
                    res = node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                }
                if (state.IsReturning) return res;
                if (state.IsBreaking)
                {
                    state.IsBreaking = false;
                    return res;
                }
                state.IsContinuing = false;
            } while (Condition.Eval(state).AsBool());
            return res;
        }
    }

    public class ForNode : Node
    {
        public string IteratorName { get; set; } = "";
        public Node Iterable { get; set; } = null!;
        public List<Node> Body { get; set; } = new();
        public override IValue Eval(InterpreterState state)
        {
            IValue res = new NullValue();
            var iterVal = Iterable.Eval(state);
            if (iterVal is ArrayValue array)
            {
                foreach (var item in array.Elements)
                {
                    state.DefineVar(IteratorName, item);
                    foreach (var node in Body)
                    {
                        res = node.Eval(state);
                        if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                    }
                    if (state.IsReturning) return res;
                    if (state.IsBreaking)
                    {
                        state.IsBreaking = false;
                        return res;
                    }
                    state.IsContinuing = false;
                }
            }
            else if (iterVal is DictValue dict)
            {
                foreach (var key in dict.Entries.Keys)
                {
                    state.DefineVar(IteratorName, key);
                    foreach (var node in Body)
                    {
                        res = node.Eval(state);
                        if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                    }
                    if (state.IsReturning) return res;
                    if (state.IsBreaking)
                    {
                        state.IsBreaking = false;
                        return res;
                    }
                    state.IsContinuing = false;
                }
            }
            else throw Error("Iteration only supported for arrays and dicts");
            
            state.IsBreaking = false;
            return res;
        }
    }

    public class ReturnNode : Node
    {
        public Node? Expr { get; set; }
        public override IValue Eval(InterpreterState state)
        {
            var val = Expr?.Eval(state) ?? new NullValue();
            state.IsReturning = true;
            return val;
        }
    }

    public class BreakNode : Node
    {
        public override IValue Eval(InterpreterState state)
        {
            state.IsBreaking = true;
            return new NullValue();
        }
    }

    public class ContinueNode : Node
    {
        public override IValue Eval(InterpreterState state)
        {
            state.IsContinuing = true;
            return new NullValue();
        }
    }

    public class ThrowNode : Node
    {
        public Node Expression { get; set; } = null!;
        public override IValue Eval(InterpreterState state)
        {
            var val = Expression.Eval(state);
            throw new Exception("OLLang error: " + val.ToString());
        }
    }

    public class TryNode : Node
    {
        public List<Node> TryBody { get; set; } = new();
        public string? CatchVar { get; set; }
        public List<Node> CatchBody { get; set; } = new();
        public List<Node> FinallyBody { get; set; } = new();

        public override IValue Eval(InterpreterState state)
        {
            try
            {
                foreach (var node in TryBody)
                {
                    node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                }
            }
            catch (Exception ex) when (CatchBody.Count > 0)
            {
                state.PushScope();
                if (CatchVar != null) state.DefineVar(CatchVar, new StringValue(ex.Message));
                foreach (var node in CatchBody)
                {
                    node.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) break;
                }
                state.PopScope();
            }
            finally
            {
                if (FinallyBody.Count > 0)
                {
                    foreach (var node in FinallyBody)
                    {
                        node.Eval(state);
                    }
                }
            }
            return new NullValue();
        }
    }

    public class ClassDefNode : Node
    {
        public string Name { get; set; } = "";
        public string? ParentName { get; set; }
        public List<FunctionDefNode> Methods { get; set; } = new();

        public override IValue Eval(InterpreterState state)
        {
            ClassValue? parent = null;
            if (ParentName != null)
            {
                var p = state.GetVar(ParentName);
                if (p is ClassValue cv) parent = cv;
                else throw Error($"'{ParentName}' is not a class");
            }

            var @class = new ClassValue(Name, parent);
            foreach (var method in Methods)
            {
                var fv = new FunctionValue(method.Name, method.Params, method.Body);
                fv.IsStatic = method.IsStatic;
                @class.Methods[method.Name] = fv;
            }

            state.DefineVar(Name, @class);
            return @class;
        }
    }

    public class SwitchCase
    {
        public Node? Value { get; set; } // null means default 
        public List<Node> Body { get; set; } = new();
    }

    public class SwitchNode : Node
    {
        public Node Expression { get; set; } = null!;
        public List<SwitchCase> Cases { get; set; } = new();

        public override IValue Eval(InterpreterState state)
        {
            var val = Expression.Eval(state);
            IValue res = new NullValue();
            
            bool matched = false;
            SwitchCase? defaultCase = null;

            foreach (var @case in Cases)
            {
                if (@case.Value == null)
                {
                    defaultCase = @case;
                    continue;
                }

                var caseVal = @case.Value.Eval(state);
                if (val.Equals(caseVal))
                {
                    matched = true;
                    foreach (var stmt in @case.Body)
                    {
                        res = stmt.Eval(state);
                        if (state.IsReturning || state.IsBreaking || state.IsContinuing) return res;
                    }
                    break;
                }
            }

            if (!matched && defaultCase != null)
            {
                foreach (var stmt in defaultCase.Body)
                {
                    res = stmt.Eval(state);
                    if (state.IsReturning || state.IsBreaking || state.IsContinuing) return res;
                }
            }

            return res;
        }
    }

    public class ExpressionStatementNode : Node
    {
        public Node Expression { get; set; } = null!;
        public override IValue Eval(InterpreterState state) => Expression.Eval(state);
    }
}