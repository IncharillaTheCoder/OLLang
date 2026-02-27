using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using Ollang.Bytecode;

namespace Ollang.Compiler
{
    public class NativeCompiler
    {
        const uint IMAGE_BASE = 0x400000;
        const uint SECTION_RVA = 0x1000;
        const uint ORIGIN = IMAGE_BASE + SECTION_RVA; // 0x401000

        int _importDirOffset;
        int _importDirSize;

        public string AsmOutput { get; private set; } = "";
        public int ImportDirOffset => _importDirOffset;
        public int ImportDirSize => _importDirSize;

        public static (string asm, int importOff, int importSize) Compile(BytecodeModule module)
        {
            var nc = new NativeCompiler();
            nc.Generate(module);
            return (nc.AsmOutput, nc._importDirOffset, nc._importDirSize);
        }

        void Generate(BytecodeModule module)
        {
            var sb = new StringBuilder();
            var strings = new Dictionary<string, string>(); // value -> label
            int strIdx = 0;

            void CollectStrings(BytecodeFunction fn)
            {
                foreach (var c in fn.Constants)
                {
                    if (c is string s && !strings.ContainsKey(s))
                        strings[s] = $"str_{strIdx++}";
                }
                foreach (var nested in fn.NestedFunctions)
                    CollectStrings(nested);
            }
            CollectStrings(module.MainFunction);
            foreach (var fn in module.Functions)
                CollectStrings(fn);

            sb.AppendLine("bits 64");
            sb.AppendLine($"org 0x{ORIGIN:X}");
            sb.AppendLine();

            sb.AppendLine("_start:");
            sb.AppendLine("  sub rsp, 40");
            sb.AppendLine();
            EmitFunction(module.MainFunction, strings, sb, module);

            sb.AppendLine("  add rsp, 40");
            sb.AppendLine("  xor ecx, ecx");
            sb.AppendLine("  call [rel _imp_ExitProcess]");
            sb.AppendLine();

            foreach (var fn in module.Functions)
            {
                sb.AppendLine($"_fn_{SanitizeName(fn.Name)}:");
                sb.AppendLine("  push rbp");
                sb.AppendLine("  mov rbp, rsp");
                sb.AppendLine($"  sub rsp, {Math.Max(40, (fn.MaxLocals + 1) * 8)}");
                EmitFunction(fn, strings, sb, module);
                sb.AppendLine($"  add rsp, {Math.Max(40, (fn.MaxLocals + 1) * 8)}");
                sb.AppendLine("  pop rbp");
                sb.AppendLine("  ret");
                sb.AppendLine();
            }

            sb.AppendLine("; --- Data ---");
            sb.AppendLine("fmt_int: db '%lld', 10, 0");
            sb.AppendLine("fmt_str: db '%s', 10, 0");
            sb.AppendLine("fmt_float: db '%f', 10, 0");
            sb.AppendLine("fmt_true: db 'true', 10, 0");
            sb.AppendLine("fmt_false: db 'false', 10, 0");
            sb.AppendLine("fmt_null: db 'null', 10, 0");
            sb.AppendLine();

            foreach (var kv in strings)
                sb.AppendLine($"{kv.Value}: db {NasmStringLiteral(kv.Key)}, 0");

            sb.AppendLine();
            sb.AppendLine("align 8");
            sb.AppendLine();

            sb.AppendLine("; --- IAT ---");
            sb.AppendLine("ilt_kernel32:");
            sb.AppendLine($"_imp_ExitProcess: dq hint_ExitProcess - 0x{IMAGE_BASE:X}");
            sb.AppendLine("  dq 0");
            sb.AppendLine("ilt_msvcrt:");
            sb.AppendLine($"_imp_printf: dq hint_printf - 0x{IMAGE_BASE:X}");
            sb.AppendLine("  dq 0");
            sb.AppendLine();

            sb.AppendLine("; --- Import Directory ---");
            sb.AppendLine("import_dir:");

            sb.AppendLine($"  dd ilt_kernel32 - 0x{IMAGE_BASE:X}");
            sb.AppendLine("  dd 0");
            sb.AppendLine("  dd 0");
            sb.AppendLine($"  dd name_kernel32 - 0x{IMAGE_BASE:X}");
            sb.AppendLine($"  dd ilt_kernel32 - 0x{IMAGE_BASE:X}");

            sb.AppendLine($"  dd ilt_msvcrt - 0x{IMAGE_BASE:X}");
            sb.AppendLine("  dd 0");
            sb.AppendLine("  dd 0");
            sb.AppendLine($"  dd name_msvcrt - 0x{IMAGE_BASE:X}");
            sb.AppendLine($"  dd ilt_msvcrt - 0x{IMAGE_BASE:X}");

            sb.AppendLine("  times 20 db 0");
            sb.AppendLine("import_dir_end:");
            sb.AppendLine();
            sb.AppendLine("hint_ExitProcess:");
            sb.AppendLine("  dw 0");
            sb.AppendLine("  db 'ExitProcess', 0");
            sb.AppendLine("hint_printf:");
            sb.AppendLine("  dw 0");
            sb.AppendLine("  db 'printf', 0");
            sb.AppendLine();

            sb.AppendLine("name_kernel32: db 'kernel32.dll', 0");
            sb.AppendLine("name_msvcrt: db 'msvcrt.dll', 0");
            sb.AppendLine();

            AsmOutput = sb.ToString();

            _importDirOffset = -1; // signal to C++ to scan
            _importDirSize = 60;   // 3 entries * 20 bytes = fixed
        }

        void EmitFunction(BytecodeFunction fn, Dictionary<string, string> strings, StringBuilder sb, BytecodeModule module)
        {
            for (int i = 0; i < fn.Instructions.Count; i++)
            {
                var inst = fn.Instructions[i];
                sb.AppendLine($"  ; {inst.OpCode}");

                switch (inst.OpCode)
                {
                    case OpCode.PUSH_CONST_IDX:
                    case OpCode.PUSH_NUM_CONST:
                        if (inst.OperandInt < fn.Constants.Count)
                        {
                            var val = fn.Constants[inst.OperandInt];
                            if (val is double d)
                                sb.AppendLine($"  push {(long)d}");
                            else if (val is long l)
                                sb.AppendLine($"  push {l}");
                            else if (val is string s && strings.ContainsKey(s))
                                sb.AppendLine($"  lea rax, [rel {strings[s]}]\n  push rax");
                            else
                                sb.AppendLine("  push 0");
                        }
                        break;

                    case OpCode.PUSH_STR_CONST:
                        if (inst.OperandInt < fn.Constants.Count)
                        {
                            var val = fn.Constants[inst.OperandInt] as string;
                            if (val != null && strings.ContainsKey(val))
                                sb.AppendLine($"  lea rax, [rel {strings[val]}]\n  push rax");
                            else
                                sb.AppendLine("  push 0");
                        }
                        break;

                    case OpCode.PUSH_NULL:
                        sb.AppendLine("  push 0");
                        break;
                    case OpCode.PUSH_TRUE:
                        sb.AppendLine("  push 1");
                        break;
                    case OpCode.PUSH_FALSE:
                        sb.AppendLine("  push 0");
                        break;

                    case OpCode.LOAD_LOCAL:
                        sb.AppendLine($"  mov rax, [rbp - {(inst.OperandInt + 1) * 8}]");
                        sb.AppendLine("  push rax");
                        break;
                    case OpCode.STORE_LOCAL:
                        sb.AppendLine("  pop rax");
                        sb.AppendLine($"  mov [rbp - {(inst.OperandInt + 1) * 8}], rax");
                        break;

                    case OpCode.ADD:
                        sb.AppendLine("  pop rbx\n  pop rax\n  add rax, rbx\n  push rax");
                        break;
                    case OpCode.SUB:
                        sb.AppendLine("  pop rbx\n  pop rax\n  sub rax, rbx\n  push rax");
                        break;
                    case OpCode.MUL:
                        sb.AppendLine("  pop rbx\n  pop rax\n  imul rax, rbx\n  push rax");
                        break;
                    case OpCode.DIV:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cqo\n  idiv rbx\n  push rax");
                        break;
                    case OpCode.MOD:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cqo\n  idiv rbx\n  push rdx");
                        break;

                    case OpCode.EQ:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  sete al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.NE:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  setne al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.LT:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  setl al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.GT:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  setg al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.LE:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  setle al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.GE:
                        sb.AppendLine("  pop rbx\n  pop rax\n  cmp rax, rbx\n  setge al\n  movzx rax, al\n  push rax");
                        break;

                    case OpCode.NOT:
                        sb.AppendLine("  pop rax\n  test rax, rax\n  sete al\n  movzx rax, al\n  push rax");
                        break;
                    case OpCode.UNM:
                        sb.AppendLine("  pop rax\n  neg rax\n  push rax");
                        break;

                    case OpCode.JMP:
                        sb.AppendLine($"  jmp .L{inst.OperandInt}");
                        break;
                    case OpCode.JMP_T:
                        sb.AppendLine($"  pop rax\n  test rax, rax\n  jnz .L{inst.OperandInt}");
                        break;
                    case OpCode.JMP_F:
                        sb.AppendLine($"  pop rax\n  test rax, rax\n  jz .L{inst.OperandInt}");
                        break;

                    case OpCode.CALL_BUILTIN:
                        if (inst.OperandInt < fn.Constants.Count)
                        {
                            var name = fn.Constants[inst.OperandInt] as string;
                            if (name == "println" || name == "print")
                            {
                                sb.AppendLine("  pop rdx");
                                sb.AppendLine("  lea rcx, [rel fmt_int]");
                                sb.AppendLine("  call [rel _imp_printf]");
                            }
                        }
                        break;

                    case OpCode.CALL:
                        if (inst.OperandInt < module.Functions.Count)
                        {
                            var target = module.Functions[inst.OperandInt];
                            sb.AppendLine($"  call _fn_{SanitizeName(target.Name)}");
                        }
                        break;

                    case OpCode.RETURN:
                        sb.AppendLine("  pop rax");
                        break;
                    case OpCode.RETURN_NULL:
                        sb.AppendLine("  xor eax, eax");
                        break;

                    case OpCode.POP:
                        sb.AppendLine("  pop rax");
                        break;
                    case OpCode.DUP:
                        sb.AppendLine("  pop rax\n  push rax\n  push rax");
                        break;

                    case OpCode.CONCAT:
                        sb.AppendLine("  ; TODO: string concat (needs runtime)");
                        sb.AppendLine("  pop rbx\n  pop rax\n  push rax");
                        break;

                    case OpCode.TO_STR:
                        sb.AppendLine("  ; TODO: to_str conversion");
                        break;

                    case OpCode.HALT:
                        sb.AppendLine("  xor ecx, ecx\n  call [rel _imp_ExitProcess]");
                        break;

                    case OpCode.NOP:
                        sb.AppendLine("  nop");
                        break;

                    default:
                        sb.AppendLine($"  ; [STUB] {inst.OpCode}");
                        break;
                }
            }
        }

        static string SanitizeName(string name) =>
            name.Replace(".", "_").Replace("-", "_").Replace(" ", "_");

        static string NasmStringLiteral(string s)
        {
            if (s.Length == 0) return "''";
            var sb = new StringBuilder();
            bool inStr = false;
            foreach (char c in s)
            {
                if (c >= 32 && c < 127 && c != '\'')
                {
                    if (!inStr) { if (sb.Length > 0) sb.Append(", "); sb.Append('\''); inStr = true; }
                    sb.Append(c);
                }
                else
                {
                    if (inStr) { sb.Append('\''); inStr = false; }
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append((int)c);
                }
            }
            if (inStr) sb.Append('\'');
            return sb.ToString();
        }
    }
}
