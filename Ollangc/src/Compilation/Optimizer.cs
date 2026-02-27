using System;
using System.Collections.Generic;
using System.Linq;
using Ollang.Bytecode;

namespace Ollang.Compiler
{
    public class BytecodeOptimizer
    {
        private readonly int _level;

        public BytecodeOptimizer(int optimizationLevel = 1) => _level = optimizationLevel;

        public void Optimize(BytecodeModule module)
        {
            OptimizeFunction(module.MainFunction);
            foreach (var func in module.Functions)
                OptimizeFunction(func);
        }

        private void OptimizeFunction(BytecodeFunction func)
        {
            if (_level <= 0) return;

            bool changed = true;
            int passes = 0;
            while (changed && passes < 10)
            {
                changed = false;
                changed |= RemoveDeadStores(func);
                changed |= FoldConstants(func);
                changed |= RemoveRedundantOps(func);
                if (_level >= 2)
                {
                    changed |= OptimizeJumps(func);
                    changed |= StrengthReduce(func);
                }
                passes++;
            }
        }

        private bool RemoveRedundantOps(BytecodeFunction func)
        {
            var instrs = func.Instructions;
            bool changed = false;

            for (int i = instrs.Count - 2; i >= 0; i--)
            {
                if (instrs[i + 1].OpCode == OpCode.POP && IsPush(instrs[i].OpCode))
                {
                    instrs.RemoveAt(i + 1);
                    instrs.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (instrs[i].OpCode == OpCode.DUP && instrs[i + 1].OpCode == OpCode.POP)
                {
                    instrs.RemoveAt(i + 1);
                    instrs.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (instrs[i].OpCode == OpCode.STORE_LOCAL_N && instrs[i + 1].OpCode == OpCode.LOAD_LOCAL_N)
                {
                    if (instrs[i].Operands.Length > 0 && instrs[i + 1].Operands.Length > 0 &&
                        instrs[i].Operands[0] == instrs[i + 1].Operands[0])
                    {
                        instrs[i] = new BytecodeInstruction(OpCode.DUP);
                        instrs[i + 1] = new BytecodeInstruction(OpCode.STORE_LOCAL_N, instrs[i + 1].Operands[0]);
                        changed = true;
                    }
                }

                if (instrs[i].OpCode == OpCode.NOT && instrs[i + 1].OpCode == OpCode.NOT)
                {
                    instrs.RemoveAt(i + 1);
                    instrs.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (instrs[i].OpCode == OpCode.UNM && instrs[i + 1].OpCode == OpCode.UNM)
                {
                    instrs.RemoveAt(i + 1);
                    instrs.RemoveAt(i);
                    changed = true;
                    continue;
                }
            }
            return changed;
        }

        private bool FoldConstants(BytecodeFunction func)
        {
            var instrs = func.Instructions;
            bool changed = false;

            for (int i = instrs.Count - 3; i >= 0; i--)
            {
                if (i + 2 >= instrs.Count) continue;

                if (instrs[i].OpCode == OpCode.PUSH_CONST_IDX &&
                    instrs[i + 1].OpCode == OpCode.PUSH_CONST_IDX &&
                    IsArithmeticOp(instrs[i + 2].OpCode))
                {
                    var aConst = func.Constants[instrs[i].OperandInt];
                    var bConst = func.Constants[instrs[i + 1].OperandInt];

                    if (aConst is double a && bConst is double b)
                    {
                        double? result = EvalConstOp(instrs[i + 2].OpCode, a, b);
                        if (result.HasValue && !double.IsNaN(result.Value) && !double.IsInfinity(result.Value))
                        {
                            int constIdx = func.AddConstant(result.Value);
                            instrs[i] = new BytecodeInstruction(OpCode.PUSH_CONST_IDX, BitConverter.GetBytes(constIdx));
                            instrs.RemoveAt(i + 2);
                            instrs.RemoveAt(i + 1);
                            changed = true;
                        }
                    }
                }
            }
            return changed;
        }

        private bool RemoveDeadStores(BytecodeFunction func)
        {
            var instrs = func.Instructions;
            var usedLocals = new HashSet<int>();
            var storePositions = new List<(int pos, int localIdx)>();

            for (int i = 0; i < instrs.Count; i++)
            {
                if (instrs[i].OpCode == OpCode.LOAD_LOCAL_N && instrs[i].Operands.Length > 0)
                    usedLocals.Add(instrs[i].Operands[0]);
                if (instrs[i].OpCode == OpCode.STORE_LOCAL_N && instrs[i].Operands.Length > 0)
                    storePositions.Add((i, instrs[i].Operands[0]));
            }

            bool changed = false;
            for (int i = storePositions.Count - 1; i >= 0; i--)
            {
                var (pos, localIdx) = storePositions[i];
                if (!usedLocals.Contains(localIdx) && localIdx >= func.Arity)
                {
                    string localName = localIdx < func.Locals.Count ? func.Locals[localIdx] : "";
                    if (localName.StartsWith("__temp_"))
                    {
                        instrs[pos] = new BytecodeInstruction(OpCode.POP);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private bool OptimizeJumps(BytecodeFunction func)
        {
            var instrs = func.Instructions;
            bool changed = false;

            for (int i = 0; i < instrs.Count; i++)
            {
                if (instrs[i].OpCode == OpCode.JMP)
                {
                    int target = i + 1 + instrs[i].OperandInt;
                    if (target >= 0 && target < instrs.Count && instrs[target].OpCode == OpCode.JMP)
                    {
                        int chainTarget = target + 1 + instrs[target].OperandInt;
                        int newOffset = chainTarget - i - 1;
                        instrs[i] = new BytecodeInstruction(OpCode.JMP, BitConverter.GetBytes(newOffset));
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private bool StrengthReduce(BytecodeFunction func)
        {
            var instrs = func.Instructions;
            bool changed = false;

            for (int i = instrs.Count - 2; i >= 0; i--)
            {
                if (i + 1 >= instrs.Count) continue;

                if (instrs[i].OpCode == OpCode.PUSH_CONST_IDX && instrs[i + 1].OpCode == OpCode.MUL)
                {
                    var val = func.Constants[instrs[i].OperandInt];
                    if (val is double d && d == 2.0)
                    {
                        instrs[i] = new BytecodeInstruction(OpCode.DUP);
                        instrs[i + 1] = new BytecodeInstruction(OpCode.ADD);
                        changed = true;
                    }
                }

                if (instrs[i].OpCode == OpCode.PUSH_CONST_IDX && instrs[i + 1].OpCode == OpCode.POW)
                {
                    var val = func.Constants[instrs[i].OperandInt];
                    if (val is double d && d == 2.0)
                    {
                        instrs[i] = new BytecodeInstruction(OpCode.DUP);
                        instrs[i + 1] = new BytecodeInstruction(OpCode.MUL);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private static bool IsPush(OpCode op) =>
            op == OpCode.PUSH_NULL || op == OpCode.PUSH_TRUE || op == OpCode.PUSH_FALSE ||
            op == OpCode.PUSH_CONST_IDX || op == OpCode.PUSH_NUM_CONST ||
            op == OpCode.PUSH_STR_CONST || op == OpCode.PUSH_BOOL_CONST;

        private static bool IsArithmeticOp(OpCode op) =>
            op == OpCode.ADD || op == OpCode.SUB || op == OpCode.MUL ||
            op == OpCode.DIV || op == OpCode.MOD || op == OpCode.POW;

        private static double? EvalConstOp(OpCode op, double a, double b)
        {
            return op switch
            {
                OpCode.ADD => a + b,
                OpCode.SUB => a - b,
                OpCode.MUL => a * b,
                OpCode.DIV when b != 0 => a / b,
                OpCode.MOD when b != 0 => a % b,
                OpCode.POW => Math.Pow(a, b),
                _ => null
            };
        }
    }
}
