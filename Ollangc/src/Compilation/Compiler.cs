using Ollang.Ast;
using Ollang.Bytecode;
using Ollang.Lexer;
using Ollang.Parser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpCode = Ollang.Bytecode.OpCode;

namespace Ollang.Compiler
{
    public class CompilationContext
    {
        public BytecodeAssembler Assembler { get; }
        public Dictionary<string, int> VariableScopes { get; set; }
        public List<string> CurrentFunctionParams { get; }
        public Stack<LoopContext> Loops { get; }
        public Stack<TryContext> TryBlocks { get; }
        public int TempVarCounter { get; set; }
        public int LabelCounter { get; set; }
        public CompilerOptions Options { get; }

        public CompilationContext(string moduleName, CompilerOptions options)
        {
            Assembler = new BytecodeAssembler(moduleName);
            VariableScopes = new Dictionary<string, int>();
            CurrentFunctionParams = new List<string>();
            Loops = new Stack<LoopContext>();
            TryBlocks = new Stack<TryContext>();
            TempVarCounter = 0;
            LabelCounter = 0;
            Options = options;
        }

        public string NewLabel(string prefix = "L") => $"{prefix}_{LabelCounter++}";

        public string NewTempVar() => $"__temp_{TempVarCounter++}";

        public void DefineVariable(string name)
        {
            if (!VariableScopes.ContainsKey(name))
                VariableScopes[name] = VariableScopes.Count;
        }

        public bool IsVariableDefined(string name) => VariableScopes.ContainsKey(name);

        public int GetVariableIndex(string name) => VariableScopes.TryGetValue(name, out int index) ? index : -1;
    }

    public class LoopContext
    {
        public string StartLabel { get; }
        public string EndLabel { get; }
        public string ContinueLabel { get; }

        public LoopContext(string start, string end, string cont)
        {
            StartLabel = start;
            EndLabel = end;
            ContinueLabel = cont;
        }
    }

    public class TryContext
    {
        public string CatchLabel { get; }
        public string FinallyLabel { get; }
        public string EndLabel { get; }
        public string ExceptionVar { get; }

        public TryContext(string catchLabel, string finallyLabel, string endLabel, string exceptionVar)
        {
            CatchLabel = catchLabel;
            FinallyLabel = finallyLabel;
            EndLabel = endLabel;
            ExceptionVar = exceptionVar;
        }
    }

    public class CompilerOptions
    {
        public bool Optimize { get; set; } = true;
        public bool DebugInfo { get; set; } = true;
        public bool StrictMode { get; set; } = false;
        public int OptimizationLevel { get; set; } = 1;
        public bool GenerateSourceMap { get; set; } = false;
    }

    public class BytecodeCompiler
    {
        private CompilationContext context;
        private readonly ParserState parser;
        private readonly CompilerOptions options;

        public BytecodeCompiler(ParserState parser, CompilerOptions? options = null)
        {
            this.parser = parser;
            this.options = options ?? new CompilerOptions();
            context = new CompilationContext("main", this.options);
        }

        public BytecodeModule Compile()
        {
            try
            {
                var ast = parser.Parse();
                CompileProgram(ast);
                var module = context.Assembler.GetModule();

                if (options.Optimize)
                {
                    var optimizer = new BytecodeOptimizer(options.OptimizationLevel);
                    optimizer.Optimize(module);
                }

                return module;
            }
            catch (Exception ex)
            {
                throw new CompilationException($"Compilation failed: {ex.Message}", ex);
            }
        }

        private void CompileProgram(ProgramNode program)
        {
            foreach (var stmt in program.Body)
                CompileStatement(stmt);
            context.Assembler.EmitReturnNull();
        }

        private void CompileStatement(Node stmt)
        {
            if (stmt is ExpressionStatementNode esn)
            {
                CompileExpression(esn.Expression);
                context.Assembler.Emit(OpCode.POP);
            }
            else if (stmt is AssignmentNode an)
                CompileAssignment(an);
            else if (stmt is FunctionDefNode fdn)
                CompileFunctionDefinition(fdn);
            else if (stmt is IfNode ifn)
                CompileIfStatement(ifn);
            else if (stmt is WhileNode wn)
                CompileWhileStatement(wn);
            else if (stmt is ForNode fn)
                CompileForStatement(fn);
            else if (stmt is ReturnNode rn)
                CompileReturnStatement(rn);
            else if (stmt is BreakNode)
                CompileBreak();
            else if (stmt is ContinueNode)
                CompileContinue();
            else if (stmt is ThrowNode tn)
                CompileThrow(tn);
            else if (stmt is TryNode trn)
                CompileTry(trn);
            else if (stmt is SwitchNode swn)
                CompileSwitchStatement(swn);
            else if (stmt is DoWhileNode dwn)
                CompileDoWhileStatement(dwn);
            else if (stmt is ClassDefNode cdn)
                CompileClassDefinition(cdn);
            else if (stmt is ImportNode importNode)
                CompileImport(importNode);
            else if (stmt is DeclarationNode dn)
                CompileDeclaration(dn);
            else
                throw new CompilationException($"Unsupported statement type: {stmt.GetType().Name}");
        }

        private void CompileExpression(Node expr)
        {
            if (expr is NumberNode nn)
                CompileNumber(nn);
            else if (expr is StringNode sn)
                CompileString(sn);
            else if (expr is BooleanNode bn)
                CompileBoolean(bn);
            else if (expr is NullNode)
                CompileNull();
            else if (expr is IdentifierNode idn)
                CompileIdentifier(idn);
            else if (expr is BinaryOpNode bon)
                CompileBinaryOperation(bon);
            else if (expr is UnaryOpNode uon)
                CompileUnaryOperation(uon);
            else if (expr is CallNode cn)
                CompileCall(cn);
            else if (expr is ArrayNode arrn)
                CompileArray(arrn);
            else if (expr is DictNode dn)
                CompileDictionary(dn);
            else if (expr is IndexNode idxn)
                CompileIndex(idxn);
            else if (expr is AssignmentNode an)
                CompileAssignment(an);
            else if (expr is IndexAssignmentNode ian)
                CompileIndexAssignment(ian);
            else if (expr is FunctionDefNode fdn)
                CompileFunctionDefinition(fdn);
            else
                throw new CompilationException($"Unsupported expression type: {expr.GetType().Name}");
        }

        private void CompileNumber(NumberNode node) => context.Assembler.EmitPushNumber(node.Value);

        private void CompileString(StringNode node) => context.Assembler.EmitPushString(node.Value);

        private void CompileBoolean(BooleanNode node) => context.Assembler.EmitPushBool(node.Value);

        private void CompileNull() => context.Assembler.EmitPushNull();

        private void CompileIdentifier(IdentifierNode node)
        {
            if (context.IsVariableDefined(node.Name))
            {
                int varIndex = context.GetVariableIndex(node.Name);
                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)varIndex);
            }
            else
            {
                context.Assembler.EmitLoadGlobal(node.Name);
            }
        }

        private void CompileAssignment(AssignmentNode node)
        {
            CompileExpression(node.Value);
            context.Assembler.Emit(OpCode.DUP);

            if (context.IsVariableDefined(node.Name))
            {
                int varIndex = context.GetVariableIndex(node.Name);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)varIndex);
            }
            else
            {
                context.Assembler.EmitStoreGlobal(node.Name);
            }
        }

        private void CompileDeclaration(DeclarationNode node)
        {
            CompileExpression(node.Value);

            bool isGlobal = context.Assembler.GetCurrentFunction().Name == "main";

            if (isGlobal)
            {
                context.Assembler.EmitStoreGlobal(node.Name);
            }
            else
            {
                context.DefineVariable(node.Name);
                int index = context.GetVariableIndex(node.Name);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)index);
            }
        }

        private void CompileBinaryOperation(BinaryOpNode node)
        {
            if (node.Op == "and" || node.Op == "or" || node.Op == "&&" || node.Op == "||")
            {
                CompileShortCircuitOperation(node);
                return;
            }

            CompileExpression(node.Left);
            CompileExpression(node.Right);

            switch (node.Op)
            {
                case "+":
                    context.Assembler.Emit(OpCode.ADD);
                    break;
                case "-":
                    context.Assembler.Emit(OpCode.SUB);
                    break;
                case "*":
                    context.Assembler.Emit(OpCode.MUL);
                    break;
                case "/":
                    context.Assembler.Emit(OpCode.DIV);
                    break;
                case "%":
                    context.Assembler.Emit(OpCode.MOD);
                    break;
                case "**":
                    context.Assembler.Emit(OpCode.POW);
                    break;
                case "==":
                    context.Assembler.Emit(OpCode.EQ);
                    break;
                case "!=":
                    context.Assembler.Emit(OpCode.NE);
                    break;
                case "<":
                    context.Assembler.Emit(OpCode.LT);
                    break;
                case ">":
                    context.Assembler.Emit(OpCode.GT);
                    break;
                case "<=":
                    context.Assembler.Emit(OpCode.LE);
                    break;
                case ">=":
                    context.Assembler.Emit(OpCode.GE);
                    break;
                case "<<":
                    context.Assembler.Emit(OpCode.SHL);
                    break;
                case ">>":
                    context.Assembler.Emit(OpCode.SHR);
                    break;
                case "&":
                    context.Assembler.Emit(OpCode.BAND);
                    break;
                case "|":
                    context.Assembler.Emit(OpCode.BOR);
                    break;
                case "^":
                    context.Assembler.Emit(OpCode.BXOR);
                    break;
                default:
                    throw new CompilationException($"Unknown binary operator: {node.Op}");
            }
        }

        private void CompileShortCircuitOperation(BinaryOpNode node)
        {
            string endLabel = context.NewLabel("sc_end");
            
            CompileExpression(node.Left);
            context.Assembler.Emit(OpCode.DUP);
            
            if (node.Op == "and" || node.Op == "&&")
            {
                context.Assembler.EmitJumpIfFalse(endLabel);
                context.Assembler.Emit(OpCode.POP);
                CompileExpression(node.Right);
            }
            else // or
            {
                context.Assembler.EmitJumpIfTrue(endLabel);
                context.Assembler.Emit(OpCode.POP);
                CompileExpression(node.Right);
            }
            
            context.Assembler.Label(endLabel);
        }

        private void CompileUnaryOperation(UnaryOpNode node)
        {
            CompileExpression(node.Operand);

            if (node.Op == "-")
                context.Assembler.Emit(OpCode.UNM);
            else if (node.Op == "!")
                context.Assembler.Emit(OpCode.NOT);
            else if (node.Op == "~")
                context.Assembler.Emit(OpCode.BNOT);
            else
            {
                throw new CompilationException($"Unknown unary operator: {node.Op}");
            }
        }

        private void CompileCall(CallNode node)
        {
            CompileExpression(node.Callee);

            foreach (var arg in node.Arguments)
                CompileExpression(arg);

            context.Assembler.Emit(OpCode.CALL, (byte)node.Arguments.Count);
        }

        private void CompileArray(ArrayNode node)
        {
            foreach (var element in node.Elements)
            {
                CompileExpression(element);
            }

            context.Assembler.Emit(OpCode.NEW_ARRAY, BitConverter.GetBytes(node.Elements.Count));
        }

        private void CompileDictionary(DictNode node)
        {
            context.Assembler.Emit(OpCode.NEW_DICT);

            foreach (var entry in node.Entries)
            {
                context.Assembler.Emit(OpCode.DUP);
                CompileExpression(entry.Key);
                CompileExpression(entry.Value);
                context.Assembler.Emit(OpCode.STORE_INDEX);
                context.Assembler.Emit(OpCode.POP);
            }
        }

        private void CompileIndex(IndexNode node)
        {
            CompileExpression(node.Target);
            CompileExpression(node.Index);
            context.Assembler.Emit(OpCode.LOAD_INDEX);
        }

        private void CompileIndexAssignment(IndexAssignmentNode node)
        {
            CompileExpression(node.Target);
            CompileExpression(node.Index);
            CompileExpression(node.Value);
            context.Assembler.Emit(OpCode.STORE_INDEX);
        }

        private void CompileFunctionDefinition(FunctionDefNode node)
        {
            var oldFunc = context.Assembler.GetCurrentFunction();
            context.Assembler.DefineFunction(node.Name, node.Params.Count);

            var oldScopes = context.VariableScopes;
            context.VariableScopes = new Dictionary<string, int>();

            try
            {
                for (int i = 0; i < node.Params.Count; i++)
                {
                    context.Assembler.AddLocal(node.Params[i]);
                    context.DefineVariable(node.Params[i]);
                }

                foreach (var stmt in node.Body)
                {
                    CompileStatement(stmt);
                }

                if (!(node.Body.LastOrDefault() is ReturnNode))
                {
                    context.Assembler.EmitReturnNull();
                }
            }
            finally
            {
                context.Assembler.RestoreLabelContext();
                context.Assembler.SetCurrentFunction(oldFunc);
                context.VariableScopes = oldScopes;
            }

            int nameIdx = context.Assembler.GetCurrentFunction().AddConstant(node.Name);
            context.Assembler.Emit(OpCode.FUNC, BitConverter.GetBytes(nameIdx));

            if (node.Name.StartsWith("$lambda_"))
            {
                return;
            }

            bool isGlobal = context.Assembler.GetCurrentFunction().Name == "main";
            if (isGlobal)
            {
                context.Assembler.EmitStoreGlobal(node.Name);
            }
            else
            {
                context.DefineVariable(node.Name);
                context.Assembler.AddLocal(node.Name);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)context.GetVariableIndex(node.Name));
            }
        }

        private void CompileClassDefinition(ClassDefNode node)
        {
            if (node.ParentName != null)
            {
                CompileExpression(new IdentifierNode(node.ParentName));
            }
            else
            {
                context.Assembler.Emit(OpCode.PUSH_NULL);
            }

            int classNameIdx = context.Assembler.GetCurrentFunction().AddConstant(node.Name);
            context.Assembler.Emit(OpCode.NEW_CLASS, BitConverter.GetBytes(classNameIdx));

            foreach (var method in node.Methods)
            {
                CompileMethodDefinition(method, node.Name);
                int methodNameIdx = context.Assembler.GetCurrentFunction().AddConstant(method.Name);
                context.Assembler.Emit(OpCode.METHOD, BitConverter.GetBytes(methodNameIdx));
            }

            bool isGlobal = context.Assembler.GetCurrentFunction().Name == "main";
            if (isGlobal)
            {
                context.Assembler.EmitStoreGlobal(node.Name);
            }
            else
            {
                context.DefineVariable(node.Name);
                context.Assembler.AddLocal(node.Name);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)context.GetVariableIndex(node.Name));
            }
        }

        private void CompileMethodDefinition(FunctionDefNode node, string className)
        {
            var oldFunc = context.Assembler.GetCurrentFunction();
            string internalName = $"{className}.{node.Name}";

            int arity = node.Params.Count + (node.IsStatic ? 0 : 1);
            var func = context.Assembler.DefineFunction(internalName, arity);
            func.IsStatic = node.IsStatic;

            var oldScopes = context.VariableScopes;
            context.VariableScopes = new Dictionary<string, int>();

            try
            {
                if (!node.IsStatic)
                {
                    context.Assembler.AddLocal("self");
                    context.DefineVariable("self");
                }

                for (int i = 0; i < node.Params.Count; i++)
                {
                    context.Assembler.AddLocal(node.Params[i]);
                    context.DefineVariable(node.Params[i]);
                }

                foreach (var stmt in node.Body)
                    CompileStatement(stmt);

                if (!(node.Body.LastOrDefault() is ReturnNode))
                    context.Assembler.EmitReturnNull();
            }
            finally
            {
                context.Assembler.RestoreLabelContext();
                context.Assembler.SetCurrentFunction(oldFunc);
                context.VariableScopes = oldScopes;
            }

            int internalNameIdx = context.Assembler.GetCurrentFunction().AddConstant(internalName);
            context.Assembler.Emit(OpCode.FUNC, BitConverter.GetBytes(internalNameIdx));
        }

        private void CompileIfStatement(IfNode node)
        {
            string elseLabel = context.NewLabel("else");
            string endLabel = context.NewLabel("endif");

            CompileExpression(node.Condition);
            context.Assembler.EmitJumpIfFalse(elseLabel);

            foreach (var stmt in node.Then)
                CompileStatement(stmt);

            if (node.Else.Count > 0)
                context.Assembler.EmitJump(endLabel);

            context.Assembler.Label(elseLabel);

            foreach (var stmt in node.Else)
                CompileStatement(stmt);

            if (node.Else.Count > 0)
                context.Assembler.Label(endLabel);
        }

        private void CompileWhileStatement(WhileNode node)
        {
            string startLabel = context.NewLabel("while_start");
            string endLabel = context.NewLabel("while_end");
            string continueLabel = context.NewLabel("while_continue");

            context.Loops.Push(new LoopContext(startLabel, endLabel, continueLabel));

            try
            {
                context.Assembler.Label(startLabel);
                CompileExpression(node.Condition);
                context.Assembler.EmitJumpIfFalse(endLabel);

                foreach (var stmt in node.Body)
                    CompileStatement(stmt);

                context.Assembler.Label(continueLabel);
                context.Assembler.EmitJump(startLabel);
                context.Assembler.Label(endLabel);
            }
            finally
            {
                context.Loops.Pop();
            }
        }

        private void CompileForStatement(ForNode node)
        {
            string startLabel = context.NewLabel("for_start");
            string endLabel = context.NewLabel("for_end");
            string continueLabel = context.NewLabel("for_continue");

            context.Loops.Push(new LoopContext(startLabel, endLabel, continueLabel));

            try
            {
                CompileExpression(node.Iterable);

                string iterableVar = context.NewTempVar();
                context.DefineVariable(iterableVar);
                context.Assembler.AddLocal(iterableVar);
                int iterableIdx = context.GetVariableIndex(iterableVar);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)iterableIdx);

                string indexVar = context.NewTempVar();
                context.DefineVariable(indexVar);
                context.Assembler.AddLocal(indexVar);
                int indexIdx = context.GetVariableIndex(indexVar);
                context.Assembler.EmitPushNumber(0);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)indexIdx);

                context.Assembler.Label(startLabel);

                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)iterableIdx);
                context.Assembler.Emit(OpCode.ARRAY_LEN);
                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)indexIdx);
                context.Assembler.Emit(OpCode.GT);

                context.Assembler.EmitJumpIfFalse(endLabel);

                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)iterableIdx);
                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)indexIdx);
                context.Assembler.Emit(OpCode.LOAD_INDEX);

                context.DefineVariable(node.IteratorName);
                context.Assembler.AddLocal(node.IteratorName);
                int iteratorIdx = context.GetVariableIndex(node.IteratorName);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)iteratorIdx);

                foreach (var stmt in node.Body)
                    CompileStatement(stmt);

                context.Assembler.Label(continueLabel);

                context.Assembler.Emit(OpCode.LOAD_LOCAL_N, (byte)indexIdx);
                context.Assembler.EmitPushNumber(1);
                context.Assembler.Emit(OpCode.ADD);
                context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)indexIdx);

                context.Assembler.EmitJump(startLabel);
                context.Assembler.Label(endLabel);
            }
            finally
            {
                context.Loops.Pop();
            }
        }

        private void CompileBreak()
        {
            if (context.Loops.Count == 0)
                throw new CompilationException("Cannot use 'break' outside of a loop");
            
            context.Assembler.EmitJump(context.Loops.Peek().EndLabel);
        }

        private void CompileContinue()
        {
            if (context.Loops.Count == 0)
                throw new CompilationException("Cannot use 'continue' outside of a loop");
            
            context.Assembler.EmitJump(context.Loops.Peek().ContinueLabel);
        }

        private void CompileThrow(ThrowNode node)
        {
            CompileExpression(node.Expression);
            context.Assembler.Emit(OpCode.THROW);
        }

        private void CompileTry(TryNode node)
        {
            string catchLabel = context.NewLabel("catch");
            string finallyLabel = context.NewLabel("finally");
            string endLabel = context.NewLabel("try_end");
            
            context.TryBlocks.Push(new TryContext(catchLabel, finallyLabel, endLabel, node.CatchVar ?? ""));
            
            context.Assembler.EmitTryBegin(catchLabel);
            
            foreach (var stmt in node.TryBody)
                CompileStatement(stmt);

            context.Assembler.Emit(OpCode.TRY_END);
            context.Assembler.EmitJump(finallyLabel);

            context.Assembler.Label(catchLabel);
            if (node.CatchBody.Count > 0)
            {
                if (node.CatchVar != null)
                {
                    context.DefineVariable(node.CatchVar);
                    context.Assembler.AddLocal(node.CatchVar);
                    context.Assembler.Emit(OpCode.STORE_LOCAL_N, (byte)context.GetVariableIndex(node.CatchVar));
                }
                else
                    context.Assembler.Emit(OpCode.POP);

                foreach (var stmt in node.CatchBody)
                    CompileStatement(stmt);
            }

            context.Assembler.Label(finallyLabel);
            foreach (var stmt in node.FinallyBody)
                CompileStatement(stmt);

            context.Assembler.Label(endLabel);
            context.TryBlocks.Pop();
        }

        private void CompileReturnStatement(ReturnNode node)
        {
            if (node.Expr != null)
            {
                CompileExpression(node.Expr);
                context.Assembler.Emit(OpCode.RETURN);
            }
            else
            {
                context.Assembler.Emit(OpCode.RETURN_NULL);
            }
        }

        private void CompileImport(ImportNode node)
        {
            int pathIdx = context.Assembler.GetCurrentFunction().AddConstant(node.Path);
            context.Assembler.Emit(OpCode.IMPORT, BitConverter.GetBytes(pathIdx));

            string targetName = node.Alias ?? Path.GetFileNameWithoutExtension(node.Path);
            context.Assembler.EmitStoreGlobal(targetName);
        }

        private void CompileSwitchStatement(SwitchNode node)
        {
            string endLabel = context.NewLabel("switch_end");
            string defaultLabel = context.NewLabel("switch_default");
            var caseLabels = new List<string>();

            CompileExpression(node.Expression);
            for (int i = 0; i < node.Cases.Count; i++)
            {
                var @case = node.Cases[i];
                string label = context.NewLabel($"case_{i}");
                caseLabels.Add(label);

                if (@case.Value == null)
                {
                    continue;
                }

                context.Assembler.Emit(OpCode.DUP);
                CompileExpression(@case.Value);
                context.Assembler.Emit(OpCode.EQ);
                context.Assembler.EmitJumpIfTrue(label);
            }

            bool hasDefault = node.Cases.Any(c => c.Value == null);
            if (hasDefault)
                context.Assembler.EmitJump(defaultLabel);
            else
                context.Assembler.EmitJump(endLabel);

            for (int i = 0; i < node.Cases.Count; i++)
            {
                var @case = node.Cases[i];

                if (@case.Value == null)
                    context.Assembler.Label(defaultLabel);
                
                context.Assembler.Label(caseLabels[i]);
                context.Assembler.Emit(OpCode.POP);

                foreach (var stmt in @case.Body)
                {
                    CompileStatement(stmt);
                }

                context.Assembler.EmitJump(endLabel);
            }

            context.Assembler.Label(endLabel);
        }

        private void CompileDoWhileStatement(DoWhileNode node)
        {
            string startLabel = context.NewLabel("dowhile_start");
            string endLabel = context.NewLabel("dowhile_end");
            string continueLabel = context.NewLabel("dowhile_continue");

            context.Loops.Push(new LoopContext(startLabel, endLabel, continueLabel));

            context.Assembler.Label(startLabel);

            foreach (var stmt in node.Body)
                CompileStatement(stmt);

            context.Assembler.Label(continueLabel);
            CompileExpression(node.Condition);
            context.Assembler.EmitJumpIfTrue(startLabel);

            context.Assembler.Label(endLabel);
            context.Loops.Pop();
        }
    }

    public class CompilationException : Exception
    {
        public CompilationException(string message) : base(message) { }
        public CompilationException(string message, Exception inner) : base(message, inner) { }
    }

    public static class CompilerDriver
    {
        public static BytecodeModule CompileSource(string source, CompilerOptions? options = null)
        {
            var lexer = new LexerState(source);
            var tokens = lexer.Tokenize();
            var parser = new ParserState(tokens);
            var compiler = new BytecodeCompiler(parser, options);
            return compiler.Compile();
        }

        public static BytecodeModule CompileFile(string filePath, CompilerOptions? options = null)
        {
            string source = File.ReadAllText(filePath);
            var module = CompileSource(source, options);
            module.FilePath = filePath;
            return module;
        }

        public static void CompileToFile(string sourceFile, string outputFile, CompilerOptions? options = null)
        {
            var module = CompileFile(sourceFile, options);
            module.SaveToFile(outputFile);
        }
    }
}