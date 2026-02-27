using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Ollang.Bytecode;
using Ollang.Values;
using Ollang.Ast;
using Ollang.Compiler;
using Ollang.Errors;
using Ollang.Lexer;
using Ollang.Parser;

namespace Ollang.VM
{
    public class VirtualMachine
    {
        private readonly BytecodeModule _module;
        private readonly IValue[] _stack = new IValue[1024 * 64];
        private int _sp = -1;
        private readonly Dictionary<string, IValue> _globals = new();
        private readonly CallFrame[] _frames = new CallFrame[1024];
        private int _fp = -1;
        private int _instructionCount = 0;
        private DateTime _startTime;
        private readonly Dictionary<string, int> _functionIndex = new();
        public bool DebugMode { get; set; } = false;

        internal static readonly NullValue CachedNull = new NullValue();
        internal static readonly BooleanValue CachedTrue = new BooleanValue(true);
        internal static readonly BooleanValue CachedFalse = new BooleanValue(false);

        private static readonly ConditionalWeakTable<BytecodeFunction, IValue[]> _constantCache = new();

        [ThreadStatic]
        public static VirtualMachine? CurrentVM;
        
        private static readonly Dictionary<string, DictValue> _moduleCache = new();

        public Ollang.GC.GarbageCollector GC { get; }

        public VirtualMachine(BytecodeModule module)
        {
            GC = new Ollang.GC.GarbageCollector(this);
            _module = module;
            foreach (var kvp in module.Globals)
                _globals[kvp.Key] = WrapValue(kvp.Value);
            for (int i = 0; i < module.Functions.Count; i++)
            {
                var func = module.Functions[i];
                _globals[func.Name] = new VMFunctionValue(func);
                _functionIndex[func.Name] = i;
            }

            RegisterStdLib();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Push(IValue val) => _stack[++_sp] = val;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IValue Pop() => _stack[_sp--];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IValue Peek() => _stack[_sp];

        private IValue[] GetWrappedConstants(BytecodeFunction func)
        {
            if (_constantCache.TryGetValue(func, out var wrapped)) return wrapped;
            var newWrapped = new IValue[func.Constants.Count];
            for (int i = 0; i < func.Constants.Count; i++)
                newWrapped[i] = WrapValue(func.Constants[i]);
            _constantCache.AddOrUpdate(func, newWrapped);
            return newWrapped;
        }

        public IValue[] GetStack() {
            var res = new IValue[_sp + 1];
            Array.Copy(_stack, res, _sp + 1);
            return res;
        }

        public List<CallFrame> GetFrames() {
            var res = new List<CallFrame>();
            for (int i = 0; i <= _fp; i++) res.Add(_frames[i]);
            return res;
        }

        private void RegisterStdLib()
        {
             var builtinFuncs = Ollang.StdLib.StdRegistry.Builtins;
             var namespaces = new Dictionary<string, DictValue>();
             
             foreach (var kvp in builtinFuncs)
             {
                 if (kvp.Key.Contains("."))
                 {
                     var parts = kvp.Key.Split('.');
                     var nsName = parts[0];
                     var funcName = parts[1];
                     
                     if (!namespaces.TryGetValue(nsName, out var nsDict))
                     {
                         nsDict = new DictValue();
                         namespaces[nsName] = nsDict;
                         _globals[nsName] = nsDict;
                     }
                     nsDict.Entries[new StringValue(funcName)] = kvp.Value;
                 }
                 else
                     _globals[kvp.Key] = kvp.Value;
             }

             if (!_globals.ContainsKey("Math")) _globals["Math"] = new DictValue();
             if (_globals["Math"] is DictValue mathDict)
             {
                 mathDict.Entries[new StringValue("pi")] = new NumberValue(Math.PI);
                 mathDict.Entries[new StringValue("e")] = new NumberValue(Math.E);
             }

             if (!_globals.ContainsKey("Memory")) _globals["Memory"] = new DictValue();
             if (_globals["Memory"] is DictValue memDict)
             {
                 memDict.Entries[new StringValue("PROCESS_ALL_ACCESS")] = new NumberValue(0x001F0FFF);
                 memDict.Entries[new StringValue("MEM_COMMIT")] = new NumberValue(0x1000);
                 memDict.Entries[new StringValue("MEM_RESERVE")] = new NumberValue(0x2000);
                 memDict.Entries[new StringValue("PAGE_READWRITE")] = new NumberValue(0x04);
                 memDict.Entries[new StringValue("PAGE_EXECUTE_READWRITE")] = new NumberValue(0x40);
             }
        }

        public IValue WrapValue(object? value)
        {
            if (value == null) return CachedNull;
            if (value is double d) return new NumberValue(d);
            if (value is long l) return new NumberValue(l);
            if (value is string s) return new StringValue(s);
            if (value is bool b) return b ? CachedTrue : CachedFalse;
            if (value is IValue iv) return iv;
            if (value is object[] arr)
            {
                var av = new ArrayValue();
                foreach (var item in arr) av.Elements.Add(WrapValue(item));
                return av;
            }
            return CachedNull;
        }

        public void SetGlobal(string name, IValue value) => _globals[name] = value;
        public Dictionary<string, IValue> GetGlobals() => _globals;

        public IValue Run()
        {
            _startTime = DateTime.Now;
            var oldVM = CurrentVM;
            CurrentVM = this;
            try
            {
                var mainFrame = new CallFrame(_module.MainFunction);
                mainFrame.WrappedConstants = GetWrappedConstants(_module.MainFunction);
                _fp++;
                _frames[_fp] = mainFrame;
                return ExecuteUntil(0);
            }
            finally
            {
                CurrentVM = oldVM;
            }
        }

        public IValue CallInternal(VMFunctionValue vmf, List<IValue> args)
        {
            var oldVM = CurrentVM;
            CurrentVM = this;
            try
            {
                var newFrame = new CallFrame(vmf.Function);
                newFrame.WrappedConstants = GetWrappedConstants(vmf.Function);
                for (int i = 0; i < args.Count && i < newFrame.Locals.Length; i++)
                    newFrame.Locals[i] = args[i];

                int targetCount = _fp + 1;
                _fp++;
                _frames[_fp] = newFrame;
                return ExecuteUntil(targetCount);
            }
            finally
            {
                CurrentVM = oldVM;
            }
        }

        public void ExecuteFunction(BytecodeFunction func, List<IValue> args)
        {
            CurrentVM = this;
            try
            {
                var newFrame = new CallFrame(func);
                newFrame.WrappedConstants = GetWrappedConstants(func);
                for (int i = 0; i < args.Count && i < newFrame.Locals.Length; i++)
                    newFrame.Locals[i] = args[i];
                
                _fp++;
                _frames[_fp] = newFrame;
                ExecuteUntil(0);
            }
            finally
            {
                CurrentVM = null;
            }
        }

        private IValue ExecuteUntil(int targetFrameCount)
        {
            while (_fp >= targetFrameCount)
            {
                var frame = _frames[_fp];
                var instrs = frame.Function.Instructions;
                
                if (frame.IP >= instrs.Count)
                {
                    _fp--;
                    continue;
                }

                try
                {
                    while (frame.IP < instrs.Count)
                    {
                        var instr = instrs[frame.IP++];
                        _instructionCount++;

                        switch (instr.OpCode)
                        {
                            case OpCode.NOP: break;
                            case OpCode.PUSH_NULL: _stack[++_sp] = CachedNull; break;
                            case OpCode.PUSH_TRUE: _stack[++_sp] = CachedTrue; break;
                            case OpCode.PUSH_FALSE: _stack[++_sp] = CachedFalse; break;
                            case OpCode.PUSH_CONST_IDX:
                                _stack[++_sp] = frame.WrappedConstants[instr.OperandInt];
                                break;

                            case OpCode.POP: if (_sp >= 0) _sp--; break;
                            case OpCode.DUP: { var v = _stack[_sp]; _stack[++_sp] = v; break; }
                            
                            case OpCode.LOAD_LOCAL_N:
                                _stack[++_sp] = frame.Locals[instr.Operands[0]];
                                break;
                            case OpCode.STORE_LOCAL_N:
                                frame.Locals[instr.Operands[0]] = _stack[_sp--];
                                break;
                                
                            case OpCode.LOAD_GLOBAL:
                                {
                                    var gNameVal = frame.Function.Constants[instr.OperandInt];
                                    if (gNameVal is string gName)
                                    {
                                        if (_globals.TryGetValue(gName, out var gVal)) _stack[++_sp] = gVal;
                                        else throw new Exception($"Runtime error: Global '{gName}' not found");
                                    }
                                }
                                break;
                            case OpCode.STORE_GLOBAL:
                                {
                                    var gNameVal = frame.Function.Constants[instr.OperandInt];
                                    if (gNameVal is string gName)
                                    {
                                        _globals[gName] = _stack[_sp--];
                                    }
                                }
                                break;

                            case OpCode.ADD:
                                {
                                    var b = _stack[_sp--];
                                    var a = _stack[_sp--];
                                    if (a is NumberValue nv1 && b is NumberValue nv2) _stack[++_sp] = new NumberValue(nv1.Value + nv2.Value);
                                    else if (a is StringValue || b is StringValue) _stack[++_sp] = new StringValue(a.ToString() + b.ToString());
                                    else if (a is PointerValue pv_add && b is NumberValue nv_add) _stack[++_sp] = new PointerValue(pv_add.Address + (int)nv_add.Value);
                                    else _stack[++_sp] = new NumberValue(a.AsNumber() + b.AsNumber());
                                }
                                break;
                            case OpCode.SUB:
                                {
                                    var b = _stack[_sp--];
                                    var a = _stack[_sp--];
                                    if (a is NumberValue nv1 && b is NumberValue nv2) _stack[++_sp] = new NumberValue(nv1.Value - nv2.Value);
                                    else if (a is PointerValue pv_sub && b is NumberValue nv_sub) _stack[++_sp] = new PointerValue(pv_sub.Address - (int)nv_sub.Value);
                                    else _stack[++_sp] = new NumberValue(a.AsNumber() - b.AsNumber());
                                }
                                break;
                            case OpCode.MUL:
                                {
                                    var b = _stack[_sp--];
                                    var a = _stack[_sp--];
                                    _stack[++_sp] = new NumberValue(a.AsNumber() * b.AsNumber());
                                }
                                break;
                            case OpCode.DIV:
                                {
                                    var b = _stack[_sp--];
                                    var a = _stack[_sp--];
                                    _stack[++_sp] = new NumberValue(a.AsNumber() / b.AsNumber());
                                }
                                break;
                            case OpCode.LT:
                                {
                                    var b = _stack[_sp--].AsNumber();
                                    var a = _stack[_sp--].AsNumber();
                                    _stack[++_sp] = new BooleanValue(a < b);
                                }
                                break;
                            case OpCode.GT:
                                {
                                    var b = _stack[_sp--].AsNumber();
                                    var a = _stack[_sp--].AsNumber();
                                    _stack[++_sp] = new BooleanValue(a > b);
                                }
                                break;
                            case OpCode.EQ:
                                {
                                    var b = _stack[_sp--];
                                    var a = _stack[_sp--];
                                    _stack[++_sp] = new BooleanValue(a.Equals(b));
                                }
                                break;
                            case OpCode.JMP:
                                frame.IP += instr.OperandInt;
                                break;
                            case OpCode.JMP_F:
                                if (!_stack[_sp--].AsBool()) frame.IP += instr.OperandInt;
                                break;
                            case OpCode.JMP_T:
                                if (_stack[_sp--].AsBool()) frame.IP += instr.OperandInt;
                                break;

                            case OpCode.CALL:
                                {
                                    int argCount = instr.Operands[0];
                                    var args = new IValue[argCount];
                                    for (int i = argCount - 1; i >= 0; i--) args[i] = _stack[_sp--];
                                    
                                    var callee = _stack[_sp--];
                                    IValue? boundSelf = null;
                                    if (callee is BoundMethodValue bmw)
                                    {
                                        boundSelf = bmw.Self;
                                        callee = bmw.Target;
                                    }

                                    if (callee is VMFunctionValue vmf)
                                    {
                                        var newFrame = new CallFrame(vmf.Function);
                                        newFrame.WrappedConstants = GetWrappedConstants(vmf.Function);
                                        int localIdx = 0;
                                        if (boundSelf != null) newFrame.Locals[localIdx++] = boundSelf;

                                        for (int i = 0; i < argCount; i++)
                                        {
                                            if (localIdx < newFrame.Locals.Length) 
                                                newFrame.Locals[localIdx++] = args[i];
                                        }
                                        _fp++;
                                        _frames[_fp] = newFrame;
                                        goto frame_switch;
                                    }
                                    else if (callee is ICallable callable)
                                    {
                                         var argList = new List<IValue>();
                                         if (boundSelf != null) argList.Add(boundSelf);
                                         for (int i = 0; i < argCount; i++) argList.Add(args[i]);
                                         _stack[++_sp] = callable.Call(new Ollang.Interpreter.InterpreterState(), argList);
                                    }
                                    else throw new Exception($"Runtime error: Attempt to call non-callable: {callee}");
                                }
                                break;

                            case OpCode.RETURN:
                            case OpCode.RETURN_NULL:
                                if (instr.OpCode == OpCode.RETURN_NULL) _stack[++_sp] = CachedNull;
                                _fp--;
                                goto frame_switch;

                            default:
                                ExecuteInstructionSlow(instr, frame);
                                if (_fp < targetFrameCount || (_fp >= 0 && _frames[_fp] != frame)) goto frame_switch;
                                break;
                        }
                    }
                    frame_switch: ;
                }
                catch (Exception ex)
                {
                    if (ex is OllangException) throw;
                    if (!HandleException(ex))
                    {
                        var ollangEx = WrapException(ex, frame);
                        Console.Error.WriteLine(ollangEx.FormatError());
                        throw ollangEx;
                    }
                }
            }

            return _sp >= 0 ? _stack[_sp--] : CachedNull;
        }

        private void ExecuteInstructionSlow(BytecodeInstruction instr, CallFrame frame)
        {
            switch (instr.OpCode)
            {
                case OpCode.MOD:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue(a.AsNumber() % b.AsNumber()));
                    }
                    break;
                case OpCode.POW:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue(Math.Pow(a.AsNumber(), b.AsNumber())));
                    }
                    break;
                case OpCode.UNM: Push(new NumberValue(-Pop().AsNumber())); break;
                case OpCode.NOT: Push(new BooleanValue(!Pop().AsBool())); break;
                case OpCode.BNOT: Push(new NumberValue(~(long)Pop().AsNumber())); break;
                case OpCode.BAND:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue((long)a.AsNumber() & (long)b.AsNumber()));
                    }
                    break;
                case OpCode.BOR:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue((long)a.AsNumber() | (long)b.AsNumber()));
                    }
                    break;
                case OpCode.BXOR:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue((long)a.AsNumber() ^ (long)b.AsNumber()));
                    }
                    break;
                case OpCode.SHL:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue((long)a.AsNumber() << (int)b.AsNumber()));
                    }
                    break;
                case OpCode.SHR:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new NumberValue((long)a.AsNumber() >> (int)b.AsNumber()));
                    }
                    break;
                case OpCode.NE:
                    {
                        var b = Pop();
                        var a = Pop();
                        Push(new BooleanValue(!a.Equals(b)));
                    }
                    break;
                case OpCode.LE:
                    {
                        var b = Pop().AsNumber();
                        var a = Pop().AsNumber();
                        Push(new BooleanValue(a <= b));
                    }
                    break;
                case OpCode.GE:
                    {
                        var b = Pop().AsNumber();
                        var a = Pop().AsNumber();
                        Push(new BooleanValue(a >= b));
                    }
                    break;
                case OpCode.CALL_BUILTIN:
                    {
                        string name = (string)frame.Function.Constants[instr.OperandInt];
                        int argCount = instr.Operands.Length > 0 ? instr.Operands[0] : 0;
                        if (_globals.TryGetValue(name, out var val) && val is ICallable callable)
                        {
                            var argList = new List<IValue>();
                            var tempArgs = new IValue[argCount];
                            for (int i = argCount - 1; i >= 0; i--) tempArgs[i] = Pop();
                            for (int i = 0; i < argCount; i++) argList.Add(tempArgs[i]);
                            Push(callable.Call(null!, argList));
                        }
                        else throw new Exception($"Runtime error: Builtin function '{name}' not found");
                    }
                    break;
                case OpCode.IMPORT:
                    {
                        string path = (string)frame.Function.Constants[instr.OperandInt];
                        string? resolved = ResolveImportPath(path);
                        if (resolved == null) throw new Exception($"Import error: Could not resolve module '{path}'");
                        if (_moduleCache.TryGetValue(resolved, out var cachedModule)) Push(cachedModule);
                        else
                        {
                            string source = resolved.StartsWith("virtual://") 
                                ? Encoding.UTF8.GetString(_module.VirtualFiles[resolved.Substring(10)]) 
                                : File.ReadAllText(resolved);
                            var lexer = new LexerState(source);
                            var tokens = lexer.Tokenize();
                            var parser = new ParserState(tokens);
                            var compiler = new BytecodeCompiler(parser);
                            var module = compiler.Compile();
                            var vm = new VirtualMachine(module);
                            vm.Run();
                            var exports = new DictValue();
                            foreach (var kvp in vm.GetGlobals())
                            {
                                exports.Entries[new StringValue(kvp.Key)] = kvp.Value;
                                if (!_globals.ContainsKey(kvp.Key)) _globals[kvp.Key] = kvp.Value;
                            }
                            _moduleCache[resolved] = exports;
                            Push(exports);
                        }
                    }
                    break;
                case OpCode.SPAWN:
                    {
                        var funcVal = Pop();
                        var arg = Pop();
                        if (funcVal is VMFunctionValue vmf)
                        {
                            var threadGlobals = new Dictionary<string, IValue>(_globals);
                            var t = new System.Threading.Thread(() => {
                                try {
                                    var newVm = new VirtualMachine(_module);
                                    foreach(var kvp in threadGlobals) newVm.SetGlobal(kvp.Key, kvp.Value);
                                    newVm.ExecuteFunction(vmf.Function, new List<IValue> { arg });
                                }
                                catch (Exception ex) { Console.WriteLine($"Thread Error: {ex.Message}"); }
                            });
                            t.IsBackground = true;
                            t.Start();
                            Push(new BooleanValue(true));
                        }
                        else throw new Exception("Runtime error: spawn requires a function");
                    }
                    break;
                case OpCode.FUNC:
                    {
                        string name = (string)frame.Function.Constants[instr.OperandInt];
                        if (_functionIndex.TryGetValue(name, out int funcIdx))
                        {
                            Push(new VMFunctionValue(_module.Functions[funcIdx]));
                        }
                        else
                        {
                            throw new Exception($"Runtime error: Function '{name}' not found");
                        }
                    }
                    break;
                case OpCode.NEW_ARRAY:
                    {
                        int count = instr.OperandInt;
                        var array = new ArrayValue();
                        var elements = new IValue[count];
                        for (int i = count - 1; i >= 0; i--) elements[i] = Pop();
                        array.Elements.AddRange(elements);
                        Push(array);
                    }
                    break;
                case OpCode.NEW_DICT: Push(new DictValue()); break;
                case OpCode.ARRAY_LEN:
                    {
                        var target = Pop();
                        if (target is ArrayValue arrLen) Push(new NumberValue(arrLen.Elements.Count));
                        else Push(new NumberValue(target.AsNumber()));
                    }
                    break;
                case OpCode.LOAD_INDEX:
                    {
                        var index = Pop();
                        var target = Pop();
                        if (index.ToString() == "offset") Push(target.GetOffset());
                        else Push(target.GetIndex(index));
                    }
                    break;
                case OpCode.STORE_INDEX:
                    {
                        var value = Pop();
                        var index = Pop();
                        var target = Pop();
                        target.SetIndex(index, value);
                        Push(value);
                    }
                    break;
                case OpCode.TRY_BEGIN:
                    frame.TryStack.Push(new ExceptionHandler { CatchIP = frame.IP + instr.OperandInt, StackDepth = _sp + 1 });
                    break;
                case OpCode.TRY_END: if (frame.TryStack.Count > 0) frame.TryStack.Pop(); break;
                case OpCode.THROW: throw new Exception(Pop().ToString());
                case OpCode.NEW_CLASS:
                    {
                        string name = (string)frame.Function.Constants[instr.OperandInt];
                        var parentVal = Pop();
                        ClassValue? parent = parentVal is ClassValue cv ? cv : null;
                        var @class = new ClassValue(name, parent);
                        Push(@class);
                    }
                    break;
                case OpCode.METHOD:
                    {
                        string name = (string)frame.Function.Constants[instr.OperandInt];
                        var method = (ICallable)Pop();
                        var @class = (ClassValue)Peek();
                        @class.Methods[name] = method;
                    }
                    break;
                case OpCode.INSTANCE:
                case OpCode.NEW_OBJECT:
                    {
                        var @class = (ClassValue)Pop();
                        Push(new InstanceValue(@class));
                    }
                    break;
            }
        }

        private bool HandleException(Exception ex)
        {
            while (_fp >= 0)
            {
                var frame = _frames[_fp];
                while (frame.TryStack.Count > 0)
                {
                    var handler = frame.TryStack.Pop();
                    if (handler.CatchIP >= 0)
                    {
                        frame.IP = handler.CatchIP;
                        while (_sp + 1 > handler.StackDepth) _sp--;
                        Push(new StringValue(ex.Message));
                        return true;
                    }
                }
                _fp--;
            }
            return false;
        }

        private string? ResolveImportPath(string path)
        {
            // Check Virtual Files first (packed into executable)
            if (_module.VirtualFiles.ContainsKey(path)) return $"virtual://{path}";
            string pathWithExt = path.EndsWith(".oll") ? path : path + ".oll";
            if (_module.VirtualFiles.ContainsKey(pathWithExt)) return $"virtual://{pathWithExt}";

            if (File.Exists(path)) return Path.GetFullPath(path);
            if (!path.EndsWith(".oll") && File.Exists(path + ".oll")) return Path.GetFullPath(path + ".oll");
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var stdLibPaths = new List<string> { 
                Path.Combine(exeDir, "stdlib"), 
                Path.Combine(Directory.GetCurrentDirectory(), "stdlib"),
                Path.Combine(exeDir, "..", "..", "..", "stdlib")
            };
            foreach (var stdPath in stdLibPaths)
            {
                if (!Directory.Exists(stdPath)) continue;
                string fullPath = Path.Combine(stdPath, path);
                if (File.Exists(fullPath)) return Path.GetFullPath(fullPath);
                if (!path.EndsWith(".oll") && File.Exists(fullPath + ".oll")) return Path.GetFullPath(fullPath + ".oll");
            }
            return null;
        }

        public long GetInstructionCount() => _instructionCount;
        public double GetExecutionTime() => (DateTime.Now - _startTime).TotalSeconds;

        private OllangException WrapException(Exception ex, CallFrame currentFrame)
        {
            var instr = currentFrame.IP > 0 ? currentFrame.Function.Instructions[currentFrame.IP - 1] : null;
            var ollangEx = new OllangException(
                "RuntimeError", ex.Message, ex,
                instr?.LineNumber ?? 0, instr?.Column ?? 0
            );

            for (int i = _fp; i >= 0; i--)
            {
                var f = _frames[i];
                if (f == null) continue;
                var ip = f.IP > 0 ? f.IP - 1 : 0;
                var ins = ip < f.Function.Instructions.Count ? f.Function.Instructions[ip] : null;
                ollangEx.CallStack.Add(new StackTraceEntry(
                    f.Function.Name, ip,
                    ins?.LineNumber ?? 0, ins?.Column ?? 0
                ));
            }

            ollangEx.Hint = ErrorHints.GetHint(ex.Message);
            return ollangEx;
        }

        public string DumpStack()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- Stack (depth: {_sp + 1}) ---");
            for (int i = _sp; i >= Math.Max(0, _sp - 20); i--)
                sb.AppendLine($"  [{i}] {_stack[i]?.ToString() ?? "null"} ({_stack[i]?.GetType().Name})");
            return sb.ToString();
        }

        public string DumpLocals()
        {
            if (_fp < 0) return "No active frame.";
            var frame = _frames[_fp];
            var sb = new StringBuilder();
            sb.AppendLine($"--- Locals ({frame.Function.Name}) ---");
            for (int i = 0; i < frame.Locals.Length && i < frame.Function.Locals.Count; i++)
                sb.AppendLine($"  {frame.Function.Locals[i]} = {frame.Locals[i]}");
            return sb.ToString();
        }

        public string DumpGlobals()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- Globals ({_globals.Count}) ---");
            foreach (var kvp in _globals.OrderBy(g => g.Key))
                if (!(kvp.Value is ICallable))
                    sb.AppendLine($"  {kvp.Key} = {kvp.Value}");
            return sb.ToString();
        }

        public List<StackTraceEntry> GetCallStack()
        {
            var stack = new List<StackTraceEntry>();
            for (int i = _fp; i >= 0; i--)
            {
                var f = _frames[i];
                if (f == null) continue;
                var ip = f.IP > 0 ? f.IP - 1 : 0;
                var ins = ip < f.Function.Instructions.Count ? f.Function.Instructions[ip] : null;
                stack.Add(new StackTraceEntry(f.Function.Name, ip, ins?.LineNumber ?? 0, ins?.Column ?? 0));
            }
            return stack;
        }
    }

    public class CallFrame
    {
        public BytecodeFunction Function { get; }
        public int IP { get; set; } = 0;
        public IValue[] Locals { get; }
        public IValue[] WrappedConstants { get; set; } = Array.Empty<IValue>();
        public Stack<ExceptionHandler> TryStack { get; } = new();

        public CallFrame(BytecodeFunction function)
        {
            Function = function;
            Locals = new IValue[Math.Max(Math.Max(function.MaxLocals, function.Locals.Count), function.Arity + 1)];
            for (int i = 0; i < Locals.Length; i++) Locals[i] = VirtualMachine.CachedNull;
        }
    }

    public class ExceptionHandler
    {
        public int CatchIP { get; set; }
        public int StackDepth { get; set; }
    }
}
