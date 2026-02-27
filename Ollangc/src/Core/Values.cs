using System;
using System.Collections.Generic;
using System.Linq;
using Ollang.Ast;
using Ollang.Interpreter;
using System.Runtime.InteropServices;

namespace Ollang.Values
{
    public interface IValue
    {
        double AsNumber();
        string ToString();
        bool AsBool();
        IValue GetIndex(IValue index);
        void SetIndex(IValue index, IValue value);
    }

    public static class ValueExtensions
    {
        public static IValue GetOffset(this IValue val)
        {
            if (val is PointerValue pv) return new NumberValue(pv.Address.ToInt64());
            return new NumberValue(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(val));
        }
    }

    public interface ICallable : IValue
    {
        IValue Call(InterpreterState state, List<IValue> args);
    }

    public class NumberValue : IValue
    {
        public double Value { get; }
        public NumberValue(double value) => Value = value;
        public double AsNumber() => Value;
        public bool AsBool() => Value != 0;
        public override string ToString() => Value.ToString();
        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (key == "offset") return this.GetOffset();
            throw new Exception("Runtime error: Numbers cannot be indexed");
        }
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Numbers cannot be indexed");
        public override bool Equals(object? obj) => obj is NumberValue other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    public class StringValue : IValue
    {
        public string Value { get; }
        public StringValue(string value) => Value = value;
        public double AsNumber() => double.TryParse(Value, out double d) ? d : 0;
        public bool AsBool() => !string.IsNullOrEmpty(Value);
        public override string ToString() => Value;
        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (key == "offset") return this.GetOffset();
            if (key == "length") return new NumberValue(Value.Length);

            if (StdLib.StdRegistry.Builtins.TryGetValue(key, out var builtin))
            {
                string[] stringMethods = { "substring", "find", "lower", "upper", "trim", "replace", "split" };
                if (stringMethods.Contains(key)) return new BoundMethodValue(this, builtin);
            }

            double numIdx = index.AsNumber();
            if (index is NumberValue || (numIdx == 0 && index.ToString() == "0"))
            {
                int idx = (int)numIdx;
                if (idx < 0) idx = Value.Length + idx;
                if (idx >= 0 && idx < Value.Length)
                    return new StringValue(Value[idx].ToString());
                return new NullValue();
            }

            throw new Exception($"Member '{key}' not found on string");
        }
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Strings are immutable");
        public override bool Equals(object? obj) => obj is StringValue other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    public class BooleanValue : IValue
    {
        public bool Value { get; }
        public BooleanValue(bool value) => Value = value;
        public double AsNumber() => Value ? 1 : 0;
        public bool AsBool() => Value;
        public override string ToString() => Value.ToString().ToLower();
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Booleans cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Booleans cannot be indexed");
        public override bool Equals(object? obj) => obj is BooleanValue other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    public class NullValue : IValue
    {
        public double AsNumber() => 0;
        public bool AsBool() => false;
        public override string ToString() => "null";
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Null cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Null cannot be indexed");
        public override bool Equals(object? obj) => obj is NullValue;
        public override int GetHashCode() => 0;
    }

    public class ArrayValue : IValue
    {
        public List<IValue> Elements { get; } = new();
        public double AsNumber() => Elements.Count;
        public bool AsBool() => Elements.Count > 0;
        public override string ToString() => "[" + string.Join(", ", Elements.Select(e => e.ToString())) + "]";
        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (key == "offset") return this.GetOffset();
            if (key == "length") return new NumberValue(Elements.Count);

            if (StdLib.StdRegistry.Builtins.TryGetValue(key, out var builtin))
            {
                string[] arrayMethods = { "push", "pop", "shift", "unshift", "slice", "sort", "reverse", "join", "concat" };
                if (arrayMethods.Contains(key)) return new BoundMethodValue(this, builtin);
            }

            double numIdx = index.AsNumber();
            if (index is NumberValue || (numIdx == 0 && index.ToString() == "0"))
            {
                int idx = (int)numIdx;
                if (idx < 0) idx = Elements.Count + idx;
                if (idx >= 0 && idx < Elements.Count)
                    return Elements[idx];
                return new NullValue();
            }

            throw new Exception($"Member '{key}' not found on array");
        }
        public void SetIndex(IValue index, IValue value)
        {
            int idx = (int)index.AsNumber();
            if (idx >= 0 && idx < Elements.Count) Elements[idx] = value;
            else if (idx == Elements.Count) Elements.Add(value);
            else throw new Exception($"Index out of bounds: {idx}");
        }
    }

    public class DictValue : IValue
    {
        public Dictionary<IValue, IValue> Entries { get; } = new();
        public double AsNumber() => Entries.Count;
        public bool AsBool() => Entries.Count > 0;
        public override string ToString() => "{" + string.Join(", ", Entries.Select(kv => $"{kv.Key}: {kv.Value}")) + "}";
        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (key == "offset") return this.GetOffset();
            if (key == "length") return new NumberValue(Entries.Count);
            if (key == "keys") { var av = new ArrayValue(); av.Elements.AddRange(Entries.Keys); return av; }
            if (key == "values") { var av = new ArrayValue(); av.Elements.AddRange(Entries.Values); return av; }

            if (Entries.TryGetValue(index, out var val)) return val;
            return new NullValue();
        }
        public void SetIndex(IValue index, IValue value) => Entries[index] = value;
    }

    public class PointerValue : IValue
    {
        public IntPtr Address { get; }
        public PointerValue(IntPtr address) => Address = address;
        public double AsNumber() => (double)Address.ToInt64();
        public bool AsBool() => Address != IntPtr.Zero;
        public override string ToString() => $"0x{Address.ToInt64():X}";
        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (key == "offset") return this.GetOffset();

            long offset = (long)index.AsNumber();
            return new NumberValue(Marshal.ReadByte(Address + (int)offset));
        }
        public void SetIndex(IValue index, IValue value)
        {
            long offset = (long)index.AsNumber();
            Marshal.WriteByte(Address + (int)offset, (byte)value.AsNumber());
        }
        public override bool Equals(object? obj) => obj is PointerValue other && Address == other.Address;
        public override int GetHashCode() => Address.GetHashCode();
    }

    public class FunctionValue : ICallable
    {
        public string Name { get; }
        public List<string> Params { get; }
        public List<Node> Body { get; }
        public bool IsStatic { get; set; } = false;

        public FunctionValue(string name, List<string> parameters, List<Node> body)
        {
            Name = name;
            Params = parameters;
            Body = body;
        }

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<function {Name}>";
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Functions cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Functions cannot be indexed");

        public IValue Call(InterpreterState state, List<IValue> args)
        {
            state.PushScope();
            for (int i = 0; i < Params.Count; i++)
                state.DefineVar(Params[i], i < args.Count ? args[i] : new NullValue());

            IValue result = new NullValue();
            foreach (var node in Body)
            {
                var val = node.Eval(state);
                if (state.IsReturning)
                {
                    result = val;
                    break;
                }
                result = val;
            }

            state.PopScope();
            state.IsReturning = false;
            return result;
        }
    }

    public class BuiltinFunctionValue : ICallable
    {
        public string Name { get; }
        public Func<InterpreterState, List<IValue>, IValue> Action { get; }

        public BuiltinFunctionValue(string name, Func<InterpreterState, List<IValue>, IValue> action)
        {
            Name = name;
            Action = action;
        }

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<builtin {Name}>";
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Functions cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Functions cannot be indexed");

        public IValue Call(InterpreterState state, List<IValue> args) => Action(state, args);
    }

    public class VMFunctionValue : IValue, ICallable
    {
        public Ollang.Bytecode.BytecodeFunction Function { get; }
        public VMFunctionValue(Ollang.Bytecode.BytecodeFunction function) => Function = function;

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<func {Function.Name}>";
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Functions cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Functions cannot be indexed");

        public IValue Call(InterpreterState state, List<IValue> args)
        {
            if (Ollang.VM.VirtualMachine.CurrentVM != null)
                return Ollang.VM.VirtualMachine.CurrentVM.CallInternal(this, args);
            throw new Exception("Runtime error: Cannot call VM function from interpreter");
        }
    }
    public class ClassValue : ICallable
    {
        public string Name { get; }
        public Dictionary<string, ICallable> Methods { get; } = new();
        public ClassValue? Parent { get; }

        public ClassValue(string name, ClassValue? parent = null)
        {
            Name = name;
            Parent = parent;
        }

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<class {Name}>";
        public IValue GetIndex(IValue index) => Methods.TryGetValue(index.ToString(), out var m) ? (IValue)m : (Parent?.GetIndex(index) ?? new NullValue());
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Class methods are immutable");

        public IValue Call(InterpreterState state, List<IValue> args)
        {
            var instance = new InstanceValue(this);
            var init = GetIndex(new StringValue("constructor"));
            if (init is NullValue) init = GetIndex(new StringValue("init"));
            
            if (init is ICallable callable)
            {
                var boundInit = new BoundMethodValue(instance, callable);
                boundInit.Call(state, args);
            }
            return instance;
        }
    }

    public class InstanceValue : IValue
    {
        public ClassValue Class { get; }
        public Dictionary<string, IValue> Fields { get; } = new();

        public InstanceValue(ClassValue @class) => Class = @class;

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<instance of {Class.Name}>";

        public IValue GetIndex(IValue index)
        {
            string key = index.ToString();
            if (Fields.TryGetValue(key, out var val)) return val;
            
            var member = Class.GetIndex(index);
            if (member is ICallable callable)
            {
                if (member is VMFunctionValue vmf && vmf.Function.IsStatic) return vmf;
                if (member is FunctionValue fv && fv.IsStatic) return fv;
                return new BoundMethodValue(this, callable);
            }
            return member;
        }

        public void SetIndex(IValue index, IValue value) => Fields[index.ToString()] = value;
    }

    public class BoundMethodValue : ICallable
    {
        public IValue Self { get; }
        public ICallable Target { get; }

        public BoundMethodValue(IValue self, ICallable target)
        {
            Self = self;
            Target = target;
        }

        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<method bound to {Self}>";
        public IValue GetIndex(IValue index) => throw new Exception("Runtime error: Methods cannot be indexed");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Runtime error: Methods cannot be indexed");

        public IValue Call(InterpreterState state, List<IValue> args)
        {
            if (Target is FunctionValue fv)
            {
                if (state == null) throw new Exception("Runtime error: Cannot call AST function from VM directly");
                state.PushScope();
                state.DefineVar("self", Self);
                for (int i = 0; i < fv.Params.Count; i++)
                    state.DefineVar(fv.Params[i], i < args.Count ? args[i] : new NullValue());

                IValue result = new NullValue();
                foreach (var node in fv.Body)
                {
                    result = node.Eval(state);
                    if (state.IsReturning) break;
                }
                state.PopScope();
                state.IsReturning = false;
                return result;
            }

            var newArgs = new List<IValue> { Self };
            newArgs.AddRange(args);
            return Target.Call(state, newArgs);
        }
    }

    public class NativeObjectValue : IValue
    {
        public object Value { get; }
        public NativeObjectValue(object value) => Value = value;
        public double AsNumber() => Value is IConvertible c ? Convert.ToDouble(c) : 0;
        public bool AsBool() => Value != null;
        public override string ToString() => Value?.ToString() ?? "null";
        public IValue GetIndex(IValue index)
        {
            try {
                var type = Value as Type ?? Value.GetType();
                var name = index.ToString();
                
                if (index is NumberValue || int.TryParse(name, out _)) {
                     var indexer = type.GetProperty("Item");
                     if (indexer != null) {
                        try { return WrapNative(indexer.GetValue(Value, new object[] { (int)index.AsNumber() })); } catch {}
                     }
                }

                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
                if (prop != null) return WrapNative(prop.GetValue(Value is Type ? null : Value));
                
                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
                if (field != null) return WrapNative(field.GetValue(Value is Type ? null : Value));

                var methods = type.GetMethods().Where(m => m.Name == name).ToArray();
                if (methods.Length > 0) return new NativeMethodValue(Value, name);

                return new NullValue();
            } catch { return new NullValue(); }
        }

        public void SetIndex(IValue index, IValue value)
        {
            try {
                var type = Value as Type ?? Value.GetType();
                var name = index.ToString();
                object? val = Unwrapped(value);

                if (index is NumberValue || int.TryParse(name, out _)) {
                    var indexer = type.GetProperty("Item");
                    if (indexer != null && indexer.CanWrite) {
                        try { indexer.SetValue(Value, Convert.ChangeType(val, indexer.PropertyType), new object[] { (int)index.AsNumber() }); return; } catch {}
                    }
                }

                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
                if (prop != null && prop.CanWrite) {
                    prop.SetValue(Value is Type ? null : Value, Convert.ChangeType(val, prop.PropertyType));
                    return;
                }

                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
                if (field != null) {
                    field.SetValue(Value is Type ? null : Value, Convert.ChangeType(val, field.FieldType));
                }
            } catch {}
        }

        private IValue WrapNative(object? obj) {
            if (obj == null) return new NullValue();
            if (obj is double d) return new NumberValue(d);
            if (obj is int i) return new NumberValue(i);
            if (obj is long l) return new NumberValue(l);
            if (obj is string s) return new StringValue(s);
            if (obj is bool b) return new BooleanValue(b);
            return new NativeObjectValue(obj);
        }

        private object? Unwrapped(IValue val) {
            if (val is NumberValue nv) return nv.Value;
            if (val is StringValue sv) return sv.Value;
            if (val is BooleanValue bv) return bv.Value;
            if (val is NullValue) return null;
            if (val is NativeObjectValue nov) return nov.Value;
            return val;
        }
    }

    public class NativeMethodValue : ICallable
    {
        public object Target { get; }
        public string MethodName { get; }
        public NativeMethodValue(object target, string methodName) { Target = target; MethodName = methodName; }
        public double AsNumber() => 0;
        public bool AsBool() => true;
        public override string ToString() => $"<native method {MethodName}>";
        public IValue GetIndex(IValue index) => new NullValue();
        public void SetIndex(IValue index, IValue value) {}
        public IValue Call(InterpreterState state, List<IValue> args) {
            var type = Target as Type ?? Target.GetType();
            var unwrappedArgs = args.Select(a => {
                if (a is NumberValue nv) return (object)nv.Value;
                if (a is StringValue sv) return (object)sv.Value;
                if (a is BooleanValue bv) return (object)bv.Value;
                if (a is NativeObjectValue nov) return nov.Value;
                if (a is NullValue) return null;
                return a;
            }).ToArray();

            var method = type.GetMethod(MethodName, unwrappedArgs.Select(a => a?.GetType() ?? typeof(object)).ToArray());
            if (method == null) {
                method = type.GetMethods().FirstOrDefault(m => m.Name == MethodName && m.GetParameters().Length == args.Count);
            }

            if (method != null) {
                var params_info = method.GetParameters();
                var finalArgs = new object?[params_info.Length];
                for(int i=0; i<params_info.Length; i++) {
                    if (i < unwrappedArgs.Length)
                        finalArgs[i] = unwrappedArgs[i] == null ? null : Convert.ChangeType(unwrappedArgs[i], params_info[i].ParameterType);
                    else
                        finalArgs[i] = params_info[i].DefaultValue;
                }
                var result = method.Invoke(Target is Type ? null : Target, finalArgs);
                return WrapNative(result);
            }
            throw new Exception($"Native method {MethodName} not found or arity mismatch");
        }

        private IValue WrapNative(object? obj) {
            if (obj == null) return new NullValue();
            if (obj is double d) return new NumberValue(d);
            if (obj is int i) return new NumberValue(i);
            if (obj is long l) return new NumberValue(l);
            if (obj is string s) return new StringValue(s);
            if (obj is bool b) return new BooleanValue(b);
            if (obj is IValue iv) return iv;
            return new NativeObjectValue(obj);
        }
    }
}
