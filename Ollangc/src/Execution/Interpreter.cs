using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Ollang.Values;

namespace Ollang.Interpreter
{
    public class InterpreterState
    {
        private readonly Dictionary<string, IValue>[] _scopes = new Dictionary<string, IValue>[1024];
        private int _scopeIdx = -1;
        
        public bool IsReturning = false;
        public bool IsBreaking = false;
        public bool IsContinuing = false;

        public InterpreterState()
        {
            PushScope();
            StdLib.StdRegistry.Register(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushScope() => _scopes[++_scopeIdx] = new Dictionary<string, IValue>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopScope()
        {
            if (_scopeIdx <= 0) throw new Exception("Runtime error: Cannot pop global scope");
            _scopes[_scopeIdx--] = null!;
        }

        public IValue GetVar(string name)
        {
            for (int i = _scopeIdx; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out var value)) 
                    return value;
            }

            throw new Exception($"Runtime error: Variable '{name}' is not defined");
        }

        public void SetVar(string name, IValue value)
        {
            if (value == null) throw new Exception($"Runtime error: Cannot assign null to variable '{name}'");

            for (int i = _scopeIdx; i >= 0; i--)
            {
                if (_scopes[i].ContainsKey(name))
                {
                    _scopes[i][name] = value;
                    return;
                }
            }

            throw new Exception($"Runtime error: Variable '{name}' is not defined. Use 'var' for first assignment.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DefineVar(string name, IValue value)
        {
            if (value == null) throw new Exception($"Runtime error: Cannot assign null to variable '{name}'");
            _scopes[_scopeIdx][name] = value;
        }

        public void Run(Ast.ProgramNode ast)
        {
            try
            {
                ast.Eval(this);
            }
            catch (Exception ex) when (!ex.Message.StartsWith("Runtime error") && !ex.Message.StartsWith("Parse error"))
            {
                throw new Exception($"Runtime error: {ex.Message}");
            }
        }
    }
}