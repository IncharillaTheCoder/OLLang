using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ollang.Bytecode;
using Ollang.Compiler;
using Ollang.Packager;
using Ollang.VM;

namespace Ollang
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        [DllImport("OllangNativeDLL.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern bool CompileNative(
            [MarshalAs(UnmanagedType.LPStr)] string nasmPath,
            [MarshalAs(UnmanagedType.LPStr)] string asmCode,
            [MarshalAs(UnmanagedType.LPStr)] string outPath,
            int importDirFileOff,
            int importDirSize,
            StringBuilder errorMsg,
            int maxErrorLen);

        static void HideConsole()
        {
            try
            {
                var handle = GetConsoleWindow();
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_HIDE);
                }
            }
            catch { }
        }

        static int Main(string[] args)
        {
            if (args.Contains("--hidden"))
            {
                HideConsole();
            }

            if (CheckForEmbeddedBytecode(out byte[]? bytecode, out bool hidden))
            {
                if (hidden) HideConsole();
                if (bytecode != null)
                {
                    return RunEmbeddedBytecode(bytecode, args);
                }
            }

            try
            {
                if (args.Length == 0)
                {
                    PrintHelp();
                    return 1;
                }

                string command = args[0].ToLower();

                switch (command)
                {
                    case "compile":
                        return HandleCompile(args);

                    case "build":
                        return HandleBuild(args);

                    case "run":
                        return HandleRun(args);

                    case "disasm":
                        return HandleDisassemble(args);

                    case "test":
                        return HandleTest(args);

                    case "package":
                        return HandlePackage(args);

                    case "decompile":
                        return HandleDecompile(args);

                    case "interpret":
                        return HandleInterpret(args);

                    case "version":
                        PrintVersion();
                        return 0;

                    case "help":
                        PrintHelp();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                return 1;
            }
        }

        static int HandleCompile(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: ollang compile <source.oll> <output.ollbc> [options]");
                Console.Error.WriteLine("Options:");
                Console.Error.WriteLine("  --optimize          Enable optimizations");
                Console.Error.WriteLine("  --no-debug          Disable debug info");
                Console.Error.WriteLine("  --strict            Enable strict mode");
                return 1;
            }

            string sourceFile = args[1];
            string outputFile = args[2];

            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine($"Source file not found: {sourceFile}");
                return 1;
            }

            var options = new CompilerOptions
            {
                Optimize = args.Contains("--optimize"),
                DebugInfo = !args.Contains("--no-debug"),
                StrictMode = args.Contains("--strict")
            };

            Console.WriteLine($"Compiling {sourceFile}...");

            var module = CompilerDriver.CompileFile(sourceFile, options);
            module.SaveToFile(outputFile);

            Console.WriteLine($"Bytecode written to {outputFile}");
            Console.WriteLine($"  Functions: {module.Functions.Count + 1}");
            Console.WriteLine($"  Constants: {module.MainFunction.Constants.Count}");

            return 0;
        }

        static int HandleBuild(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: ollang build <source.oll> <output.exe> [options]");
                return 1;
            }

            string sourceFile = args[1];
            string outputFile = args[2];

            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine($"Source file not found: {sourceFile}");
                return 1;
            }

            BinaryFormat format = BinaryFormat.PE64;
            if (args.Contains("--win32")) format = BinaryFormat.PE32;
            if (args.Contains("--raw")) format = BinaryFormat.Raw;

            bool compress = !args.Contains("--no-compress");
            bool singleExec = args.Contains("--single-exec");
            bool hidden = args.Contains("--hidden");

            if (args.Contains("--native"))
            {
                Console.WriteLine($"Compiling {sourceFile} into Native x64 Assembly...");
                var nativeMod = CompilerDriver.CompileFile(sourceFile);
                var (asmString, importOff, importSize) = NativeCompiler.Compile(nativeMod);
                
                Console.WriteLine("Passing assembly to NASM + C/C++ DLL...");
                
                string nasmPath = @"C:\nasm\nasm.exe";
                // Allow override via --nasm-path=...
                foreach (var a in args)
                {
                    if (a.StartsWith("--nasm-path="))
                        nasmPath = a.Substring(12);
                }
                
                StringBuilder errorMsg = new StringBuilder(4096);
                bool success = false;
                try {
                    success = CompileNative(nasmPath, asmString, outputFile, importOff, importSize, errorMsg, errorMsg.Capacity);
                } catch (Exception nativeEx) {
                    Console.Error.WriteLine($"Failed to invoke OllangNativeDLL.dll: {nativeEx.Message}");
                    Console.Error.WriteLine("Make sure OllangNativeDLL.dll is compiled and in the same directory.");
                    return 1;
                }
                
                if (success) {
                    Console.WriteLine($"Successfully built native executable: {outputFile}");
                    
                    // Clean up .lib and .exp if they exist (User request: make them not needed)
                    string libFile = Path.ChangeExtension(outputFile, ".lib");
                    string expFile = Path.ChangeExtension(outputFile, ".exp");
                    if (File.Exists(libFile)) try { File.Delete(libFile); } catch {}
                    if (File.Exists(expFile)) try { File.Delete(expFile); } catch {}
                    
                    return 0;
                } else {
                    Console.Error.WriteLine($"Native compilation failed: {errorMsg}");
                    return 1;
                }
            }

            Console.WriteLine($"Compiling {sourceFile}...");
            var module = CompilerDriver.CompileFile(sourceFile);
            byte[] bytecode = module.Serialize();

            Console.WriteLine($"Creating executable...");
            
            // Pack stdlib if found in execution directory
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string stdlibPath = Path.Combine(exeDir, "stdlib");
            if (Directory.Exists(stdlibPath))
            {
                Console.WriteLine("Packing stdlib into bytecode module...");
                foreach (var file in Directory.GetFiles(stdlibPath, "*.oll", SearchOption.AllDirectories))
                {
                    string relPath = Path.GetRelativePath(stdlibPath, file).Replace("\\", "/");
                    module.VirtualFiles[relPath] = File.ReadAllBytes(file);
                }
            }

            var builder = new ExecutableBuilder(module.Serialize(), format);
            builder.SetOutputPath(outputFile);
            builder.SetCompression(compress);
            builder.SetSingleExecutable(singleExec);
            builder.SetHidden(hidden);
            builder.Build();

            return 0;
        }

        static int HandleRun(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ollang run <bytecode.ollbc> [args...]");
                return 1;
            }

            string bytecodeFile = args[1];

            if (!File.Exists(bytecodeFile))
            {
                Console.Error.WriteLine($"Bytecode file not found: {bytecodeFile}");
                return 1;
            }

            Console.WriteLine($"Running {bytecodeFile}...");

            var module = BytecodeModule.LoadFromFile(bytecodeFile);
            var vm = new VirtualMachine(module);

            if (args.Length > 2)
            {
                var argsArray = new Ollang.Values.ArrayValue();
                for (int i = 2; i < args.Length; i++)
                {
                    argsArray.Elements.Add(new Ollang.Values.StringValue(args[i]));
                }
                vm.SetGlobal("args", argsArray);
            }

            var result = vm.Run();

            Console.WriteLine($"Program finished with result: {result}");
            Console.WriteLine($"  Instructions executed: {vm.GetInstructionCount()}");
            Console.WriteLine($"  Execution time: {vm.GetExecutionTime():F3}s");

            return 0;
        }

        static int HandleDisassemble(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ollang disasm <bytecode.ollbc> [output.txt]");
                return 1;
            }

            string bytecodeFile = args[1];
            string outputFile = args.Length > 2 ? args[2] : Path.ChangeExtension(bytecodeFile, ".disasm.txt");

            if (!File.Exists(bytecodeFile))
            {
                Console.Error.WriteLine($"Bytecode file not found: {bytecodeFile}");
                return 1;
            }

            Console.WriteLine($"Disassembling {bytecodeFile}...");

            var module = BytecodeModule.LoadFromFile(bytecodeFile);
            BytecodeDisassembler.DisassembleToFile(module, outputFile);

            Console.WriteLine($"Disassembly written to {outputFile}");

            return 0;
        }

        static int HandleDecompile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ollang decompile <bytecode.ollbc> [output.oll]");
                return 1;
            }

            string bytecodeFile = args[1];
            string outputFile = args.Length > 2 ? args[2] : Path.ChangeExtension(bytecodeFile, ".decompiled.oll");

            if (!File.Exists(bytecodeFile))
            {
                Console.Error.WriteLine($"Bytecode file not found: {bytecodeFile}");
                return 1;
            }

            Console.WriteLine($"Decompiling {bytecodeFile}...");

            var module = BytecodeModule.LoadFromFile(bytecodeFile);
            BytecodeDecompiler.DecompileToFile(module, outputFile);

            Console.WriteLine($"Decompiled code written to {outputFile}");

            return 0;
        }

        static int HandleTest(string[] args)
        {
            Console.WriteLine("Running compiler test...");
            string testCode = @"func test() {
            return 42;
        }
        
        var result = test();
        println(""Test result: "" + result);
        var x = 10;
        var y = 20;
        println(""x + y = "" + (x + y));
        var arr = [1, 2, 3, 4, 5];
        println(""Array: "" + arr);
        println(""Array length: "" + len(arr));
    ";

            try
            {
                var lexer = new Lexer.LexerState(testCode);
                var tokens = lexer.Tokenize();
                var parser = new Parser.ParserState(tokens);
                var ast = parser.Parse();

                var interpreter = new Interpreter.InterpreterState();
                interpreter.Run(ast);

                Console.WriteLine("Test passed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                return 1;
            }
            return 0;
        }

        static int HandlePackage(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ollang package <source.oll> [output_dir]");
                return 1;
            }

            string sourceFile = args[1];
            string outputDir = args.Length > 2 ? args[2] : "./dist";

            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine($"Source file not found: {sourceFile}");
                return 1;
            }

            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"Creating Windows package for {sourceFile}...");
            Packager.Packager.CreateCrossPlatformPackage(sourceFile, outputDir);

            return 0;
        }

        static int HandleInterpret(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ollang interpret <source.oll>");
                return 1;
            }

            string sourceFile = args[1];
            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine($"Source file not found: {sourceFile}");
                return 1;
            }

            try
            {
                string source = File.ReadAllText(sourceFile);
                var lexer = new Lexer.LexerState(source);
                var tokens = lexer.Tokenize();
                var parser = new Parser.ParserState(tokens);
                var ast = parser.Parse();

                var interpreter = new Interpreter.InterpreterState();
                interpreter.Run(ast);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            return 0;
        }

        static void PrintHelp()
        {
            Console.WriteLine("OLLang Compiler - Version 1.0");
            Console.WriteLine("A complete compiler system for the OLLang programming language");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  compile <src> <out>         Compile to bytecode");
            Console.WriteLine("  build <src> <out>           Create executable");
            Console.WriteLine("  run <bc> [args...]          Run bytecode");
            Console.WriteLine("  interpret <src>             Interpret source directly");
            Console.WriteLine("  disasm <bc> [out.txt]       Disassemble bytecode");
            Console.WriteLine("  decompile <bc> [out.oll]    Decompile bytecode back to OLL");
            Console.WriteLine("  test                        Run compiler test");
            Console.WriteLine("  package <src> [out_dir]     Create Windows package");
            Console.WriteLine("  version                     Show version information");
            Console.WriteLine("  help                        Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  ollang compile program.oll program.ollbc");
            Console.WriteLine("  ollang build program.oll program.exe");
            Console.WriteLine("  ollang build program.oll program.exe --single-exec");
            Console.WriteLine("  ollang run program.ollbc");
            Console.WriteLine("  ollang package program.oll ./dist");
            Console.WriteLine();
            Console.WriteLine("Build Options:");
            Console.WriteLine("  --native            Compile to pure native x64 binary via C/C++ DLL");
            Console.WriteLine("  --windows           Build for Windows (default)");
            Console.WriteLine("  --win32             Build for Windows x86");
            Console.WriteLine("  --win64             Build for Windows x64 (default)");
            Console.WriteLine("  --raw               Output raw bytecode");
            Console.WriteLine("  --no-compress       Disable bytecode compression");
            Console.WriteLine("  --single-exec       Create standalone executable (bundles DLL)");
            Console.WriteLine("  --hidden            Hide console window on startup");
            Console.WriteLine();
        }

        static void PrintVersion()
        {
            Console.WriteLine($"OLLang Compiler Version {Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine("Ollang Project");
            Console.WriteLine("Complete compiler system with VM and executable packager");
        }

        static bool CheckForEmbeddedBytecode(out byte[]? bytecode, out bool hidden)
        {
            bytecode = null;
            hidden = false;
            try
            {
                string? selfPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(selfPath) || !File.Exists(selfPath)) return false;

                using (var fs = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 26) return false;

                    fs.Seek(-13, SeekOrigin.End);
                    byte[] markerBytes = new byte[13];
                    int read = fs.Read(markerBytes, 0, 13);
                    if (read != 13) return false;

                    string marker = Encoding.ASCII.GetString(markerBytes);
                    if (marker == "OLLANG_BUNDLE")
                    {
                        fs.Seek(-18, SeekOrigin.End);
                        using (var reader = new BinaryReader(fs, Encoding.ASCII, true))
                        {
                            hidden = reader.ReadByte() == 1;
                            int length = reader.ReadInt32();
                            if (length > 0 && length < fs.Length - 21)
                            {
                                fs.Seek(-18 - length, SeekOrigin.End);
                                bytecode = new byte[length];
                                int bytesRead = fs.Read(bytecode, 0, length);
                                return bytesRead == length;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        static int RunEmbeddedBytecode(byte[] bytecode, string[] args)
        {
            try
            {
                var module = BytecodeModule.Deserialize(bytecode);
                if (module == null) throw new Exception("Failed to deserialize embedded bytecode.");
                
                var vm = new VirtualMachine(module);
                var argsArray = new Ollang.Values.ArrayValue();
                if (args != null)
                {
                    foreach (var arg in args) 
                        argsArray.Elements.Add(new Ollang.Values.StringValue(arg));
                }
                vm.SetGlobal("args", argsArray);
                vm.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.Message}");
                return 1;
            }
        }

        private static bool ExtractAndLoadEmbeddedDLL()
        {
            try
            {
                string? selfPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(selfPath) || !File.Exists(selfPath))
                    return false;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possibleDllNames = { "Ollangc.dll", Path.ChangeExtension(Path.GetFileName(selfPath), ".dll") };
                
                foreach (var dllName in possibleDllNames)
                {
                    string externalDllPath = Path.Combine(baseDir, dllName);
                    if (File.Exists(externalDllPath))
                    {
                        try
                        {
                            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Ollangc"))
                                return true;
                            Assembly.LoadFrom(externalDllPath);
                            return true;
                        }
                        catch { }
                    }
                }

                using (var fs = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 50) return false;

                    byte[] buffer = new byte[1024];
                    fs.Seek(-Math.Min(1024, fs.Length), SeekOrigin.End);
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    string tail = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    int dllMarkerPos = tail.LastIndexOf("OLLANG_DLL_EMBED");
                    if (dllMarkerPos == -1) return false;

                    long fileEnd = fs.Length;
                    long markerPos = fileEnd - (bytesRead - dllMarkerPos);

                    fs.Seek(markerPos - 4, SeekOrigin.Begin);
                    using (var reader = new BinaryReader(fs, Encoding.ASCII, true))
                    {
                        int dllSize = reader.ReadInt32();
                        if (dllSize <= 0 || dllSize > 100_000_000)
                            return false;


                        fs.Seek(markerPos - 4 - dllSize, SeekOrigin.Begin);
                        byte[] dllData = new byte[dllSize];
                        if (fs.Read(dllData, 0, dllSize) != dllSize)
                            return false;

                        string tempDir = Path.Combine(Path.GetTempPath(), "ollang_rt");
                        Directory.CreateDirectory(tempDir);
                        string dllPath = Path.Combine(tempDir, "Ollangc.dll");
                        
                        if (!File.Exists(dllPath) || new FileInfo(dllPath).Length != dllSize)
                            File.WriteAllBytes(dllPath, dllData);

                        try
                        {
                            Assembly.LoadFile(dllPath);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Warning] Failed to load embedded DLL: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Warning] DLL extraction issue: {ex.Message}");
                return false;
            }
        }

        static byte[] Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var output = new MemoryStream())
            using (var gs = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            {
                gs.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}