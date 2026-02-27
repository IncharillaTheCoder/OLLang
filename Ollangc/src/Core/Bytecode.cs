using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Ollang.Bytecode
{
    public enum OpCode : byte
    {
        // Stack operations (0x00-0x0F)
        NOP = 0x00,
        PUSH_NULL = 0x01,
        PUSH_TRUE = 0x02,
        PUSH_FALSE = 0x03,
        PUSH_NUM_CONST = 0x04,
        PUSH_STR_CONST = 0x05,
        PUSH_BOOL_CONST = 0x06,
        PUSH_CONST_IDX = 0x07,
        POP = 0x08,
        DUP = 0x09,
        DUP_N = 0x0A,
        SWAP = 0x0B,
        SWAP_N = 0x0C,
        ROT = 0x0D,
        OVER = 0x0E,
        PICK = 0x0F,

        // Variables and memory (0x10-0x1F)
        LOAD_LOCAL = 0x10,
        STORE_LOCAL = 0x11,
        LOAD_LOCAL_N = 0x12,
        STORE_LOCAL_N = 0x13,
        LOAD_GLOBAL = 0x14,
        STORE_GLOBAL = 0x15,
        LOAD_UPVALUE = 0x16,
        STORE_UPVALUE = 0x17,
        CLOSE_UPVALUE = 0x18,
        LOAD_FIELD = 0x19,
        STORE_FIELD = 0x1A,
        LOAD_INDEX = 0x1B,
        STORE_INDEX = 0x1C,
        NEW_SLOT = 0x1D,
        DELETE_SLOT = 0x1E,
        GET_META = 0x1F,

        // Arithmetic operations (0x20-0x2F)
        ADD = 0x20,
        SUB = 0x21,
        MUL = 0x22,
        DIV = 0x23,
        MOD = 0x24,
        POW = 0x25,
        UNM = 0x26,
        FLOOR = 0x27,
        CEIL = 0x28,
        ROUND = 0x29,
        ABS = 0x2A,
        SQRT = 0x2B,
        LOG = 0x2C,
        LOG10 = 0x2D,
        EXP = 0x2E,
        SIN = 0x2F,
        COS = 0x30,
        TAN = 0x31,
        ASIN = 0x32,
        ACOS = 0x33,
        ATAN = 0x34,
        ATAN2 = 0x35,
        RAND = 0x36,
        SRAND = 0x37,

        // Bitwise operations (0x38-0x3F)
        BAND = 0x38,
        BOR = 0x39,
        BXOR = 0x3A,
        BNOT = 0x3B,
        SHL = 0x3C,
        SHR = 0x3D,
        USHR = 0x3E,
        ROL = 0x3F,
        ROR = 0x40,

        // Comparison operations (0x41-0x4F)
        EQ = 0x41,
        NE = 0x42,
        LT = 0x43,
        LE = 0x44,
        GT = 0x45,
        GE = 0x46,
        CMP = 0x47,
        TYPEOF = 0x48,
        INSTANCEOF = 0x49,
        IN = 0x4A,
        IS_NULL = 0x4B,
        IS_NAN = 0x4C,
        IS_FINITE = 0x4D,
        IS_INT = 0x4E,
        IS_STR = 0x4F,

        // Logical operations (0x50-0x5F)
        AND = 0x50,
        OR = 0x51,
        NOT = 0x52,
        BOOL = 0x53,
        COALESCE = 0x54,
        TERNARY = 0x55,

        // String operations (0x60-0x6F)
        CONCAT = 0x60,
        STR_LEN = 0x61,
        SUBSTR = 0x62,
        FIND = 0x63,
        RFIND = 0x64,
        LOWER = 0x65,
        UPPER = 0x66,
        TRIM = 0x67,
        LTRIM = 0x68,
        RTRIM = 0x69,
        REPLACE = 0x6A,
        SPLIT = 0x6B,
        JOIN = 0x6C,
        FORMAT = 0x6D,
        ESCAPE = 0x6E,
        UNESCAPE = 0x6F,

        // Array operations (0x70-0x7F)
        NEW_ARRAY = 0x70,
        NEW_ARRAY_WITH = 0x71,
        ARRAY_LEN = 0x72,
        ARRAY_PUSH = 0x73,
        ARRAY_POP = 0x74,
        ARRAY_SHIFT = 0x75,
        ARRAY_UNSHIFT = 0x76,
        ARRAY_SLICE = 0x77,
        ARRAY_SPLICE = 0x78,
        ARRAY_CONCAT = 0x79,
        ARRAY_REVERSE = 0x7A,
        ARRAY_SORT = 0x7B,
        ARRAY_MAP = 0x7C,
        ARRAY_FILTER = 0x7D,
        ARRAY_REDUCE = 0x7E,
        ARRAY_FOREACH = 0x7F,

        // Dictionary operations (0x80-0x8F)
        NEW_DICT = 0x80,
        NEW_DICT_WITH = 0x81,
        DICT_LEN = 0x82,
        DICT_KEYS = 0x83,
        DICT_VALUES = 0x84,
        DICT_HAS = 0x85,
        DICT_MERGE = 0x86,
        DICT_REMOVE = 0x87,
        DICT_CLEAR = 0x88,

        // Type conversions (0x90-0x9F)
        TO_NUM = 0x90,
        TO_STR = 0x91,
        TO_BOOL = 0x92,
        TO_INT = 0x93,
        TO_FLOAT = 0x94,
        TO_HEX = 0x95,
        TO_BASE64 = 0x96,
        FROM_BASE64 = 0x97,
        PARSE_JSON = 0x98,
        STRINGIFY = 0x99,
        CLONE = 0x9A,
        DEEP_CLONE = 0x9B,

        // Control flow (0xA0-0xAF)
        JMP = 0xA0,
        JMP_T = 0xA1,
        JMP_F = 0xA2,
        JMP_NULL = 0xA3,
        JMP_NN = 0xA4,
        JMP_EQ = 0xA5,
        JMP_NE = 0xA6,
        JMP_LT = 0xA7,
        JMP_LE = 0xA8,
        JMP_GT = 0xA9,
        JMP_GE = 0xAA,
        SWITCH = 0xAB,
        SWITCH_RANGE = 0xAC,
        SWITCH_STR = 0xAD,
        TRY_BEGIN = 0xAE,
        TRY_END = 0xAF,
        THROW = 0xB1,
        RETHROW = 0xB2,

        // Function operations (0xB3-0xBF)
        CALL = 0xB3,
        CALL_METHOD = 0xB4,
        CALL_TAIL = 0xB5,
        CALL_VARARG = 0xB6,
        CALL_BUILTIN = 0xB7,
        RETURN = 0xB8,
        RETURN_NULL = 0xB9,
        YIELD = 0xBA,
        RESUME = 0xBB,
        SPAWN = 0xBC,

        // Function creation (0xC0-0xCF)
        CLOSURE = 0xC0,
        CLOSURE_VARARG = 0xC1,
        FUNC = 0xC2,
        LAMBDA = 0xC3,
        METHOD = 0xC4,
        GETTER = 0xC5,
        SETTER = 0xC6,
        BIND = 0xC7,
        APPLY = 0xC8,
        CALL_CTOR = 0xC9,
        SUPER = 0xCA,

        // Class and object operations (0xD0-0xDF)
        NEW_CLASS = 0xD0,
        NEW_OBJECT = 0xD1,
        INSTANCE = 0xD2,
        INHERIT = 0xD3,
        MIXIN = 0xD4,
        GET_PROTO = 0xD5,
        SET_PROTO = 0xD6,
        GET_SLOT = 0xD7,
        SET_SLOT = 0xD8,
        DEF_SLOT = 0xD9,

        // Module and scope operations (0xE0-0xEF)
        IMPORT = 0xE0,
        EXPORT = 0xE1,
        REQUIRE = 0xE2,
        MODULE = 0xE3,
        SCOPE_BEGIN = 0xE4,
        SCOPE_END = 0xE5,
        WITH_BEGIN = 0xE6,
        WITH_END = 0xE7,
        USE_STRICT = 0xE8,

        // Memory and native operations (0xF0-0xFF)
        ALLOC = 0xF0,
        FREE = 0xF1,
        REALLOC = 0xF2,
        MEMCPY = 0xF3,
        MEMMOVE = 0xF4,
        MEMSET = 0xF5,
        MEMCMP = 0xF6,
        READ8 = 0xF7,
        READ16 = 0xF8,
        READ32 = 0xF9,
        READ64 = 0xFA,
        WRITE8 = 0xFB,
        WRITE16 = 0xFC,
        WRITE32 = 0xFD,
        WRITE64 = 0xFE,
        HALT = 0x56,
        NATIVE = 0xFF
    }

    public class BytecodeInstruction
    {
        public OpCode OpCode { get; set; }
        public byte[] Operands { get; set; } = Array.Empty<byte>();
        public int LineNumber { get; set; }
        public int Column { get; set; }

        private int? _operandInt;
        public int OperandInt
        {
            get
            {
                if (!_operandInt.HasValue)
                {
                    if (Operands.Length >= 4) _operandInt = BitConverter.ToInt32(Operands, 0);
                    else if (Operands.Length > 0) _operandInt = (int)Operands[0];
                    else _operandInt = 0;
                }
                return _operandInt.Value;
            }
        }
        public BytecodeInstruction(OpCode op, params byte[] operands)
        {
            OpCode = op;
            Operands = operands ?? Array.Empty<byte>();
            LineNumber = 0;
            Column = 0;
        }

        public BytecodeInstruction(OpCode op, int line, int col, params byte[] operands)
        {
            OpCode = op;
            Operands = operands ?? Array.Empty<byte>();
            LineNumber = line;
            Column = col;
        }

        public int GetSize()
        {
            return 1 + Operands.Length; // OpCode + operands
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{OpCode.ToString().PadRight(20)}");

            if (Operands.Length > 0)
            {
                sb.Append("[");
                for (int i = 0; i < Operands.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"0x{Operands[i]:X2}");
                }
                sb.Append("]");
            }

            if (LineNumber > 0)
            {
                sb.Append($" (line {LineNumber}:{Column})");
            }

            return sb.ToString();
        }
    }

    public class BytecodeFunction
    {
        public string Name { get; set; }
        public int Arity { get; set; }
        public bool IsVararg { get; set; }
        public bool IsStatic { get; set; }
        public List<BytecodeInstruction> Instructions { get; set; }
        public List<object?> Constants { get; set; }
        public List<string> Locals { get; set; }
        public List<string> Upvalues { get; set; }
        public List<BytecodeFunction> NestedFunctions { get; set; }
        public int MaxStackSize { get; set; }
        public int MaxLocals { get; set; }

        public BytecodeFunction(string name)
        {
            Name = name;
            Instructions = new List<BytecodeInstruction>();
            Constants = new List<object?>();
            Locals = new List<string>();
            Upvalues = new List<string>();
            NestedFunctions = new List<BytecodeFunction>();
            MaxStackSize = 0;
            MaxLocals = 0;
        }

        public int AddConstant(object? value)
        {
            Constants.Add(value);
            return Constants.Count - 1;
        }

        public int AddLocal(string name)
        {
            Locals.Add(name);
            return Locals.Count - 1;
        }

        public void AddInstruction(OpCode op, params byte[] operands)
        {
            Instructions.Add(new BytecodeInstruction(op, operands));
        }

        public void AddInstruction(OpCode op, int line, int col, params byte[] operands)
        {
            Instructions.Add(new BytecodeInstruction(op, line, col, operands));
        }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Header: "FUNC" + version
                writer.Write(Encoding.ASCII.GetBytes("FUNC"));
                writer.Write((byte)1); // Version

                // Function info
                writer.Write(Name ?? "");
                writer.Write(Arity);
                writer.Write(IsVararg);
                writer.Write(IsStatic);
                writer.Write(MaxStackSize);
                writer.Write(MaxLocals);

                // Constants
                writer.Write(Constants.Count);
                foreach (var constant in Constants)
                {
                    WriteConstant(writer, constant);
                }

                // Locals
                writer.Write(Locals.Count);
                foreach (var local in Locals)
                {
                    writer.Write(local);
                }

                // Upvalues
                writer.Write(Upvalues.Count);
                foreach (var upvalue in Upvalues)
                {
                    writer.Write(upvalue);
                }

                // Instructions
                writer.Write(Instructions.Count);
                foreach (var instr in Instructions)
                {
                    writer.Write((byte)instr.OpCode);
                    writer.Write((byte)instr.Operands.Length);
                    writer.Write(instr.Operands);
                    writer.Write(instr.LineNumber);
                    writer.Write(instr.Column);
                }

                // Nested functions
                writer.Write(NestedFunctions.Count);
                foreach (var nested in NestedFunctions)
                {
                    var nestedData = nested.Serialize();
                    writer.Write(nestedData.Length);
                    writer.Write(nestedData);
                }

                return ms.ToArray();
            }
        }

        public static BytecodeFunction Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (magic != "FUNC") throw new InvalidDataException("Invalid function format");

                var version = reader.ReadByte();
                if (version != 1) throw new InvalidDataException($"Unsupported version: {version}");

                var func = new BytecodeFunction(reader.ReadString())
                {
                    Arity = reader.ReadInt32(),
                    IsVararg = reader.ReadBoolean(),
                    IsStatic = reader.ReadBoolean(),
                    MaxStackSize = reader.ReadInt32(),
                    MaxLocals = reader.ReadInt32()
                };

                // Constants
                int constCount = reader.ReadInt32();
                for (int i = 0; i < constCount; i++)
                {
                    func.Constants.Add(ReadConstant(reader));
                }

                // Locals
                int localCount = reader.ReadInt32();
                for (int i = 0; i < localCount; i++)
                {
                    func.Locals.Add(reader.ReadString());
                }

                // Upvalues
                int upvalueCount = reader.ReadInt32();
                for (int i = 0; i < upvalueCount; i++)
                {
                    func.Upvalues.Add(reader.ReadString());
                }

                // Instructions
                int instrCount = reader.ReadInt32();
                for (int i = 0; i < instrCount; i++)
                {
                    var op = (OpCode)reader.ReadByte();
                    int operandCount = reader.ReadByte();
                    var operands = reader.ReadBytes(operandCount);
                    var line = reader.ReadInt32();
                    var col = reader.ReadInt32();

                    func.Instructions.Add(new BytecodeInstruction(op, line, col, operands));
                }

                // Nested functions
                int nestedCount = reader.ReadInt32();
                for (int i = 0; i < nestedCount; i++)
                {
                    int nestedSize = reader.ReadInt32();
                    var nestedData = reader.ReadBytes(nestedSize);
                    func.NestedFunctions.Add(Deserialize(nestedData));
                }

                return func;
            }
        }

        public static void WriteConstant(BinaryWriter writer, object? constant)
        {
            if (constant == null)
            {
                writer.Write((byte)0);
            }
            else if (constant is double d)
            {
                writer.Write((byte)1);
                writer.Write(d);
            }
            else if (constant is long l)
            {
                writer.Write((byte)2);
                writer.Write(l);
            }
            else if (constant is string s)
            {
                writer.Write((byte)3);
                writer.Write(s);
            }
            else if (constant is bool b)
            {
                writer.Write((byte)4);
                writer.Write(b);
            }
            else if (constant is byte[] bytes)
            {
                writer.Write((byte)5);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
            else if (constant is object[] array)
            {
                writer.Write((byte)6);
                writer.Write(array.Length);
                foreach (var item in array)
                {
                    WriteConstant(writer, item);
                }
            }
            else if (constant is Dictionary<string, object> dict)
            {
                writer.Write((byte)7);
                writer.Write(dict.Count);
                foreach (var kvp in dict)
                {
                    writer.Write(kvp.Key);
                    WriteConstant(writer, kvp.Value);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported constant type: {constant.GetType()}");
            }
        }

        public static object? ReadConstant(BinaryReader reader)
        {
            byte? type = reader.ReadByte();
            return type switch
            {
                0 => null,
                1 => reader.ReadDouble(),
                2 => reader.ReadInt64(),
                3 => reader.ReadString(),
                4 => reader.ReadBoolean(),
                5 => reader.ReadBytes(reader.ReadInt32()),
                6 => ReadArrayConstant(reader),
                7 => ReadDictConstant(reader),
                _ => throw new InvalidDataException($"Unknown constant type: {type}")
            };
        }

        private static object?[] ReadArrayConstant(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var array = new object?[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = ReadConstant(reader);
            }
            return array;
        }

        private static Dictionary<string, object?> ReadDictConstant(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                object value = ReadConstant(reader);
                dict[key] = value;
            }
            return dict;
        }
    }

    public class BytecodeModule
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public BytecodeFunction MainFunction { get; set; }
        public List<BytecodeFunction> Functions { get; set; }
        public Dictionary<string, object?> Globals { get; set; }
        public List<string> Exports { get; set; }
        public List<string> Imports { get; set; }
        public Dictionary<string, string> Dependencies { get; set; }
        public Dictionary<string, byte[]> VirtualFiles { get; set; }
        public byte[] CustomData { get; set; }

        public BytecodeModule(string name)
        {
            Name = name;
            Functions = new List<BytecodeFunction>();
            Globals = new Dictionary<string, object?>();
            Exports = new List<string>();
            Imports = new List<string>();
            Dependencies = new Dictionary<string, string>();
            VirtualFiles = new Dictionary<string, byte[]>();
            CustomData = Array.Empty<byte>();
            FilePath = "";
            MainFunction = new BytecodeFunction("main");
        }

        public BytecodeFunction AddFunction(string name, int arity = 0, bool isVararg = false)
        {
            var func = new BytecodeFunction(name)
            {
                Arity = arity,
                IsVararg = isVararg
            };
            Functions.Add(func);
            return func;
        }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Header: "OLLANG" + version
                writer.Write(Encoding.ASCII.GetBytes("OLLANG"));
                writer.Write((byte)1); // Major version
                writer.Write((byte)0); // Minor version
                writer.Write((byte)0); // Patch version
                writer.Write((byte)0); // Flags

                // Module info
                writer.Write(Name ?? "");
                writer.Write(FilePath ?? "");

                // Globals
                writer.Write(Globals.Count);
                foreach (var kvp in Globals)
                {
                    writer.Write(kvp.Key);
                    BytecodeFunction.WriteConstant(writer, kvp.Value);
                }

                // Exports
                writer.Write(Exports.Count);
                foreach (var export in Exports)
                {
                    writer.Write(export);
                }

                // Imports
                writer.Write(Imports.Count);
                foreach (var import in Imports)
                {
                    writer.Write(import);
                }

                // Dependencies
                writer.Write(Dependencies.Count);
                foreach (var dep in Dependencies)
                {
                    writer.Write(dep.Key);
                    writer.Write(dep.Value);
                }

                // Main function
                var mainData = MainFunction.Serialize();
                writer.Write(mainData.Length);
                writer.Write(mainData);

                // Functions
                writer.Write(Functions.Count);
                foreach (var func in Functions)
                {
                    var funcData = func.Serialize();
                    writer.Write(funcData.Length);
                    writer.Write(funcData);
                }

                // Custom data
                writer.Write(CustomData.Length);
                writer.Write(CustomData);

                // Virtual Files
                writer.Write(VirtualFiles.Count);
                foreach (var kvp in VirtualFiles)
                {
                    writer.Write(kvp.Key);
                    writer.Write(kvp.Value.Length);
                    writer.Write(kvp.Value);
                }

                return ms.ToArray();
            }
        }

        public static BytecodeModule Deserialize(byte[] data)
        {
            // Handle GZip compression if present
            if (data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B)
            {
                using (var ms = new MemoryStream(data))
                using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    gzip.CopyTo(outMs);
                    data = outMs.ToArray();
                }
            }

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                var magic = Encoding.ASCII.GetString(reader.ReadBytes(6));
                if (magic != "OLLANG") throw new InvalidDataException("Invalid bytecode format");

                var major = reader.ReadByte();
                var minor = reader.ReadByte();
                var patch = reader.ReadByte();
                var flags = reader.ReadByte();

                if (major != 1) throw new InvalidDataException($"Unsupported major version: {major}");

                var module = new BytecodeModule(reader.ReadString())
                {
                    FilePath = reader.ReadString()
                };

                // Globals
                int globalCount = reader.ReadInt32();
                for (int i = 0; i < globalCount; i++)
                {
                    string key = reader.ReadString();
                    object? value = BytecodeFunction.ReadConstant(reader);
                    module.Globals[key] = value;
                }

                // Exports
                int exportCount = reader.ReadInt32();
                for (int i = 0; i < exportCount; i++)
                {
                    module.Exports.Add(reader.ReadString());
                }

                // Imports
                int importCount = reader.ReadInt32();
                for (int i = 0; i < importCount; i++)
                {
                    module.Imports.Add(reader.ReadString());
                }

                // Dependencies
                int depCount = reader.ReadInt32();
                for (int i = 0; i < depCount; i++)
                {
                    string key = reader.ReadString();
                    string value = reader.ReadString();
                    module.Dependencies[key] = value;
                }

                // Main function
                int mainSize = reader.ReadInt32();
                var mainData = reader.ReadBytes(mainSize);
                module.MainFunction = BytecodeFunction.Deserialize(mainData);

                // Functions
                int funcCount = reader.ReadInt32();
                for (int i = 0; i < funcCount; i++)
                {
                    int funcSize = reader.ReadInt32();
                    var funcData = reader.ReadBytes(funcSize);
                    module.Functions.Add(BytecodeFunction.Deserialize(funcData));
                }

                // Custom data
                int customSize = reader.ReadInt32();
                module.CustomData = reader.ReadBytes(customSize);

                // Virtual Files
                int virtualFileCount = reader.ReadInt32();
                for (int i = 0; i < virtualFileCount; i++)
                {
                    string fileName = reader.ReadString();
                    int fileLen = reader.ReadInt32();
                    byte[] fileData = reader.ReadBytes(fileLen);
                    module.VirtualFiles[fileName] = fileData;
                }

                return module;
            }
        }

        public void SaveToFile(string path)
        {
            var data = Serialize();
            File.WriteAllBytes(path, data);
        }

        public static BytecodeModule LoadFromFile(string path)
        {
            var data = File.ReadAllBytes(path);
            return Deserialize(data);
        }
    }

    public class BytecodeAssembler
    {
        private BytecodeModule module;
        private BytecodeFunction currentFunction;
        private Dictionary<string, int> labels = new Dictionary<string, int>();
        private Dictionary<string, List<int>> labelReferences = new Dictionary<string, List<int>>();
        private readonly Stack<(Dictionary<string, int> labels, Dictionary<string, List<int>> refs)> _labelStack = new();
        private readonly Dictionary<(BytecodeFunction func, object key), int> _constantIndex = new();

        public BytecodeAssembler(string moduleName)
        {
            module = new BytecodeModule(moduleName);
            currentFunction = module.MainFunction;
        }

        public BytecodeFunction GetCurrentFunction() => currentFunction;

        public void SetCurrentFunction(BytecodeFunction func)
        {
            currentFunction = func ?? module.MainFunction;
        }

        public void SetCurrentFunction(string name)
        {
            currentFunction = module.Functions.FirstOrDefault(f => f.Name == name) ?? module.MainFunction;
        }

        public BytecodeFunction DefineFunction(string name, int arity = 0, bool vararg = false)
        {
            _labelStack.Push((labels, labelReferences));
            labels = new Dictionary<string, int>();
            labelReferences = new Dictionary<string, List<int>>();

            var func = module.AddFunction(name, arity, vararg);
            currentFunction = func;
            return func;
        }

        public void RestoreLabelContext()
        {
            if (_labelStack.Count > 0)
            {
                var (savedLabels, savedRefs) = _labelStack.Pop();
                labels = savedLabels;
                labelReferences = savedRefs;
            }
        }

        public int AddLocal(string name) => currentFunction.AddLocal(name);

        public void Label(string name)
        {
            labels[name] = currentFunction.Instructions.Count;

            if (labelReferences.TryGetValue(name, out var references))
            {
                foreach (var instrIndex in references)
                {
                    var instr = currentFunction.Instructions[instrIndex];
                    int offset = currentFunction.Instructions.Count - instrIndex - 1;
                    instr.Operands = BitConverter.GetBytes(offset);
                }
                labelReferences.Remove(name);
            }
        }

        public void Emit(OpCode op, params byte[] operands)
        {
            currentFunction.AddInstruction(op, 0, 0, operands);
        }

        public void Emit(OpCode op, int line, int col, params byte[] operands)
        {
            currentFunction.AddInstruction(op, line, col, operands);
        }

        private int AddOrReuseConstant(object? value)
        {
            if (value != null)
            {
                var key = (currentFunction, value);
                if (_constantIndex.TryGetValue(key, out int existing))
                    return existing;
                int idx = currentFunction.AddConstant(value);
                _constantIndex[key] = idx;
                return idx;
            }
            return currentFunction.AddConstant(value);
        }

        public void EmitPushNumber(double value)
        {
            int constIndex = AddOrReuseConstant(value);
            Emit(OpCode.PUSH_CONST_IDX, BitConverter.GetBytes(constIndex));
        }

        public void EmitPushString(string value)
        {
            int constIndex = AddOrReuseConstant(value);
            Emit(OpCode.PUSH_CONST_IDX, BitConverter.GetBytes(constIndex));
        }

        public void EmitPushBool(bool value)
        {
            Emit(value ? OpCode.PUSH_TRUE : OpCode.PUSH_FALSE);
        }

        public void EmitPushNull()
        {
            Emit(OpCode.PUSH_NULL);
        }

        public void EmitLoadLocal(string name)
        {
            int index = currentFunction.Locals.IndexOf(name);
            if (index == -1)
            {
                index = currentFunction.AddLocal(name);
            }
            Emit(OpCode.LOAD_LOCAL_N, (byte)index);
        }

        public void EmitStoreLocal(string name)
        {
            int index = currentFunction.Locals.IndexOf(name);
            if (index == -1)
            {
                index = currentFunction.AddLocal(name);
            }
            Emit(OpCode.STORE_LOCAL_N, (byte)index);
        }

        public void EmitLoadGlobal(string name)
        {
            int idx = AddOrReuseConstant(name);
            Emit(OpCode.LOAD_GLOBAL, BitConverter.GetBytes(idx));
        }

        public void EmitStoreGlobal(string name)
        {
            int idx = AddOrReuseConstant(name);
            Emit(OpCode.STORE_GLOBAL, BitConverter.GetBytes(idx));
        }

        public void EmitJump(string label)
        {
            if (labels.TryGetValue(label, out int target))
            {
                int offset = target - currentFunction.Instructions.Count - 1;
                Emit(OpCode.JMP, BitConverter.GetBytes(offset));
            }
            else
            {
                if (!labelReferences.ContainsKey(label))
                    labelReferences[label] = new List<int>();
                labelReferences[label].Add(currentFunction.Instructions.Count);

                Emit(OpCode.JMP, new byte[4]);
            }
        }

        public void EmitJumpIfTrue(string label)
        {
            if (labels.TryGetValue(label, out int target))
            {
                int offset = target - currentFunction.Instructions.Count - 1;
                Emit(OpCode.JMP_T, BitConverter.GetBytes(offset));
            }
            else
            {
                if (!labelReferences.ContainsKey(label))
                    labelReferences[label] = new List<int>();
                labelReferences[label].Add(currentFunction.Instructions.Count);
                Emit(OpCode.JMP_T, new byte[4]);
            }
        }

        public void EmitJumpIfFalse(string label)
        {
            if (labels.TryGetValue(label, out int target))
            {
                int offset = target - currentFunction.Instructions.Count - 1;
                Emit(OpCode.JMP_F, BitConverter.GetBytes(offset));
            }
            else
            {
                if (!labelReferences.ContainsKey(label))
                    labelReferences[label] = new List<int>();
                labelReferences[label].Add(currentFunction.Instructions.Count);
                Emit(OpCode.JMP_F, new byte[4]);
            }
        }

        public void EmitCall(string functionName, int argCount)
        {
            EmitPushString(functionName);
            Emit(OpCode.CALL_BUILTIN, (byte)argCount);
        }

        public void EmitTryBegin(string catchLabel)
        {
            if (labels.TryGetValue(catchLabel, out int target))
            {
                int offset = target - currentFunction.Instructions.Count - 1;
                Emit(OpCode.TRY_BEGIN, BitConverter.GetBytes(offset));
            }
            else
            {
                if (!labelReferences.ContainsKey(catchLabel))
                    labelReferences[catchLabel] = new List<int>();
                labelReferences[catchLabel].Add(currentFunction.Instructions.Count);
                Emit(OpCode.TRY_BEGIN, new byte[4]);
            }
        }

        public void EmitReturn()
        {
            Emit(OpCode.RETURN);
        }

        public void EmitReturnNull()
        {
            Emit(OpCode.RETURN_NULL);
        }

        public void CalculateStackUsage()
        {
            foreach (var func in module.Functions.Concat(new[] { module.MainFunction }))
            {
                int stackSize = 0;
                int maxStack = 0;
                int localCount = func.Locals.Count;

                foreach (var instr in func.Instructions)
                {
                    switch (instr.OpCode)
                    {
                        case OpCode.PUSH_NULL:
                        case OpCode.PUSH_TRUE:
                        case OpCode.PUSH_FALSE:
                        case OpCode.PUSH_NUM_CONST:
                        case OpCode.PUSH_STR_CONST:
                        case OpCode.PUSH_BOOL_CONST:
                        case OpCode.PUSH_CONST_IDX:
                        case OpCode.LOAD_LOCAL:
                        case OpCode.LOAD_LOCAL_N:
                        case OpCode.LOAD_GLOBAL:
                        case OpCode.LOAD_UPVALUE:
                        case OpCode.LOAD_FIELD:
                        case OpCode.LOAD_INDEX:
                            stackSize++;
                            if (instr.OpCode == OpCode.LOAD_LOCAL_N && instr.Operands.Length > 0)
                            {
                                int lIdx = instr.Operands[0];
                                if (lIdx + 1 > localCount) localCount = lIdx + 1;
                            }
                            break;

                        case OpCode.POP:
                        case OpCode.STORE_LOCAL:
                        case OpCode.STORE_LOCAL_N:
                        case OpCode.STORE_GLOBAL:
                        case OpCode.STORE_UPVALUE:
                        case OpCode.STORE_FIELD:
                        case OpCode.STORE_INDEX:
                            stackSize--;
                            if (instr.OpCode == OpCode.STORE_LOCAL_N && instr.Operands.Length > 0)
                            {
                                int lIdx = instr.Operands[0];
                                if (lIdx + 1 > localCount) localCount = lIdx + 1;
                            }
                            break;

                        case OpCode.DUP:
                            stackSize++;
                            break;

                        case OpCode.SWAP:
                            break;

                        case OpCode.ADD:
                        case OpCode.SUB:
                        case OpCode.MUL:
                        case OpCode.DIV:
                        case OpCode.MOD:
                        case OpCode.POW:
                        case OpCode.BAND:
                        case OpCode.BOR:
                        case OpCode.BXOR:
                        case OpCode.SHL:
                        case OpCode.SHR:
                        case OpCode.EQ:
                        case OpCode.NE:
                        case OpCode.LT:
                        case OpCode.LE:
                        case OpCode.GT:
                        case OpCode.GE:
                            stackSize--;
                            break;

                        case OpCode.UNM:
                        case OpCode.BNOT:
                        case OpCode.NOT:
                            break;

                        case OpCode.CALL:
                        case OpCode.CALL_BUILTIN:
                            if (instr.Operands.Length > 0)
                            {
                                int argCount = instr.Operands[0];
                                stackSize -= argCount;
                                stackSize++;
                            }
                            break;

                        case OpCode.RETURN:
                        case OpCode.RETURN_NULL:
                            stackSize = 0;
                            break;

                        case OpCode.NEW_ARRAY:
                            if (instr.Operands.Length > 0)
                            {
                                int elemCount = BitConverter.ToInt32(instr.Operands, 0);
                                stackSize -= elemCount;
                                stackSize++;
                            }
                            break;
                        default:
                            break;
                    }

                    if (stackSize > maxStack) maxStack = stackSize;
                    if (stackSize < 0) stackSize = 0;
                }

                func.MaxStackSize = maxStack;
                func.MaxLocals = localCount;
            }
        }

        public BytecodeModule GetModule()
        {
            CalculateStackUsage();
            return module;
        }
    }

    public class BytecodeDisassembler
    {
        public static string Disassemble(BytecodeModule module)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Module: {module.Name}");
            sb.AppendLine($"File: {module.FilePath}");
            sb.AppendLine();

            if (module.Globals.Count > 0)
            {
                sb.AppendLine("Globals:");
                foreach (var kvp in module.Globals)
                {
                    sb.AppendLine($"  {kvp.Key} = {kvp.Value}");
                }
                sb.AppendLine();
            }

            if (module.Exports.Count > 0)
            {
                sb.AppendLine("Exports:");
                foreach (var export in module.Exports)
                {
                    sb.AppendLine($"  {export}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("Main function:");
            DisassembleFunction(module.MainFunction, sb, 1);
            sb.AppendLine();
            if (module.Functions.Count > 0)
            {
                sb.AppendLine("Functions:");
                foreach (var func in module.Functions)
                {
                    sb.AppendLine($"Function: {func.Name} (arity={func.Arity}, vararg={func.IsVararg})");
                    DisassembleFunction(func, sb, 1);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static void DisassembleFunction(BytecodeFunction func, StringBuilder sb, int indent)
        {
            string indentStr = new string(' ', indent * 2);

            if (func.Constants.Count > 0)
            {
                sb.AppendLine($"{indentStr}Constants:");
                for (int i = 0; i < func.Constants.Count; i++)
                {
                    sb.AppendLine($"{indentStr}  [{i}] = {func.Constants[i]}");
                }
            }

            if (func.Locals.Count > 0)
            {
                sb.AppendLine($"{indentStr}Locals:");
                for (int i = 0; i < func.Locals.Count; i++)
                {
                    sb.AppendLine($"{indentStr}  [{i}] = {func.Locals[i]}");
                }
            }

            sb.AppendLine($"{indentStr}Instructions (max stack: {func.MaxStackSize}, max locals: {func.MaxLocals}):");
            for (int i = 0; i < func.Instructions.Count; i++)
            {
                var instr = func.Instructions[i];
                sb.Append($"{indentStr}  {i:D4}: {instr.OpCode.ToString().PadRight(20)}");

                if (instr.Operands.Length > 0)
                {
                    sb.Append(" [");
                    for (int j = 0; j < instr.Operands.Length; j++)
                    {
                        if (j > 0) sb.Append(", ");

                        if ((instr.OpCode == OpCode.PUSH_CONST_IDX || 
                             instr.OpCode == OpCode.LOAD_GLOBAL || 
                             instr.OpCode == OpCode.STORE_GLOBAL || 
                             instr.OpCode == OpCode.FUNC || 
                             instr.OpCode == OpCode.METHOD) && instr.Operands.Length >= 4)
                        {
                            int constIdx = instr.OperandInt;
                            if (constIdx >= 0 && constIdx < func.Constants.Count)
                            {
                                sb.Append($"const[{constIdx}]={func.Constants[constIdx]}");
                            }
                            else
                            {
                                sb.Append($"0x{instr.Operands[j]:X2}");
                            }
                        }
                        else if ((instr.OpCode == OpCode.LOAD_LOCAL_N || instr.OpCode == OpCode.STORE_LOCAL_N) &&
                                 instr.Operands.Length > 0)
                        {
                            int localIdx = instr.Operands[0];
                            if (localIdx >= 0 && localIdx < func.Locals.Count)
                            {
                                sb.Append($"local[{localIdx}]={func.Locals[localIdx]}");
                            }
                            else
                            {
                                sb.Append($"0x{instr.Operands[j]:X2}");
                            }
                        }
                        else if ((instr.OpCode == OpCode.JMP || instr.OpCode == OpCode.JMP_T || instr.OpCode == OpCode.JMP_F) &&
                                 instr.Operands.Length >= 4)
                        {
                            int offset = BitConverter.ToInt32(instr.Operands, 0);
                            int target = i + 1 + offset;
                            sb.Append($"-> {target:D4}");
                        }
                        else
                        {
                            sb.Append($"0x{instr.Operands[j]:X2}");
                        }
                    }
                    sb.Append("]");
                }

                if (instr.LineNumber > 0)
                {
                    sb.Append($" (line {instr.LineNumber}:{instr.Column})");
                }

                sb.AppendLine();
            }

            // Nested functions
            if (func.NestedFunctions.Count > 0)
            {
                sb.AppendLine($"{indentStr}Nested functions:");
                foreach (var nested in func.NestedFunctions)
                {
                    sb.AppendLine($"{indentStr}  Function: {nested.Name}");
                    DisassembleFunction(nested, sb, indent + 2);
                }
            }
        }

        public static void DisassembleToFile(BytecodeModule module, string outputPath)
        {
            string disassembly = Disassemble(module);
            File.WriteAllText(outputPath, disassembly);
        }
    }

    public class BytecodeDecompiler
    {
        public static string Decompile(BytecodeModule module)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// Decompiled OLL code from {module.Name}");
            sb.AppendLine();

            DecompileFunction(module.MainFunction, sb, 0);

            foreach (var func in module.Functions)
            {
                sb.AppendLine();
                DecompileFunction(func, sb, 0);
            }

            return sb.ToString();
        }

        private static void DecompileFunction(BytecodeFunction func, StringBuilder sb, int indent)
        {
            string indentStr = new string(' ', indent * 2);
            bool isMain = func.Name == "main";

            if (!isMain)
            {
                sb.Append($"{indentStr}func {func.Name}(");
                // Parameters are usually the first few locals
                var paramList = new List<string>();
                for (int i = 0; i < func.Arity; i++)
                {
                    paramList.Add(i < func.Locals.Count ? func.Locals[i] : $"arg_{i}");
                }
                sb.Append(string.Join(", ", paramList));
                sb.AppendLine(") {");
                indent += 2;
                indentStr = new string(' ', indent * 2);
            }

            var stack = new Stack<string>();
            for (int i = 0; i < func.Instructions.Count; i++)
            {
                var instr = func.Instructions[i];
                switch (instr.OpCode)
                {
                    case OpCode.PUSH_NULL: stack.Push("null"); break;
                    case OpCode.PUSH_TRUE: stack.Push("true"); break;
                    case OpCode.PUSH_FALSE: stack.Push("false"); break;
                    case OpCode.PUSH_CONST_IDX:
                        int idx = BitConverter.ToInt32(instr.Operands, 0);
                        if (idx >= 0 && idx < func.Constants.Count)
                        {
                            var val = func.Constants[idx];
                            stack.Push(val is string ? $"\"{val}\"" : val.ToString());
                        }
                        else stack.Push($"const_{idx}");
                        break;
                    case OpCode.LOAD_GLOBAL:
                        stack.Push(Encoding.UTF8.GetString(instr.Operands));
                        break;
                    case OpCode.LOAD_LOCAL_N:
                        int lIdx = instr.Operands[0];
                        stack.Push(lIdx < func.Locals.Count ? func.Locals[lIdx] : $"local_{lIdx}");
                        break;
                    case OpCode.STORE_GLOBAL:
                        string gName = Encoding.UTF8.GetString(instr.Operands);
                        if (stack.Count > 0) sb.AppendLine($"{indentStr}{gName} = {stack.Pop()}");
                        break;
                    case OpCode.STORE_LOCAL_N:
                        int slIdx = instr.Operands[0];
                        string slName = slIdx < func.Locals.Count ? func.Locals[slIdx] : $"local_{slIdx}";
                        if (stack.Count > 0) sb.AppendLine($"{indentStr}{slName} = {stack.Pop()}");
                        break;
                    case OpCode.CALL_BUILTIN:
                    case OpCode.CALL:
                        int argCount = instr.Operands[0];
                        var args = new List<string>();
                        for (int j = 0; j < argCount; j++) if (stack.Count > 0) args.Insert(0, stack.Pop());
                        string callee = stack.Count > 0 ? stack.Pop() : "unknown";
                        stack.Push($"{callee}({string.Join(", ", args)})");
                        break;
                    case OpCode.POP:
                        if (stack.Count > 0)
                        {
                            string expr = stack.Pop();
                            if (!string.IsNullOrEmpty(expr)) sb.AppendLine($"{indentStr}{expr}");
                        }
                        break;
                    case OpCode.ADD: 
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} + {b})"); }
                        break;
                    case OpCode.SUB: 
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} - {b})"); }
                        break;
                    case OpCode.MUL: 
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} * {b})"); }
                        break;
                    case OpCode.DIV: 
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} / {b})"); }
                        break;
                    case OpCode.MOD:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} % {b})"); }
                        break;
                    case OpCode.POW:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} ** {b})"); }
                        break;
                    case OpCode.EQ:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} == {b})"); }
                        break;
                    case OpCode.NE:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} != {b})"); }
                        break;
                    case OpCode.LT:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} < {b})"); }
                        break;
                    case OpCode.GT:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} > {b})"); }
                        break;
                    case OpCode.LE:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} <= {b})"); }
                        break;
                    case OpCode.GE:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} >= {b})"); }
                        break;
                    case OpCode.AND:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} and {b})"); }
                        break;
                    case OpCode.OR:
                        if (stack.Count >= 2) { string b = stack.Pop(); string a = stack.Pop(); stack.Push($"({a} or {b})"); }
                        break;
                    case OpCode.NOT:
                        if (stack.Count >= 1) { string a = stack.Pop(); stack.Push($"(not {a})"); }
                        break;
                    case OpCode.RETURN:
                        if (stack.Count > 0) sb.AppendLine($"{indentStr}return {stack.Pop()}");
                        break;
                    case OpCode.RETURN_NULL:
                        sb.AppendLine($"{indentStr}return");
                        break;
                    case OpCode.NEW_ARRAY:
                        int count = BitConverter.ToInt32(instr.Operands, 0);
                        var elems = new List<string>();
                        for (int j = 0; j < count; j++) if (stack.Count > 0) elems.Insert(0, stack.Pop());
                        stack.Push($"[{string.Join(", ", elems)}]");
                        break;
                }
            }

            if (!isMain)
            {
                sb.AppendLine("}");
            }
        }

        public static void DecompileToFile(BytecodeModule module, string outputPath)
        {
            File.WriteAllText(outputPath, Decompile(module));
        }
    }
}