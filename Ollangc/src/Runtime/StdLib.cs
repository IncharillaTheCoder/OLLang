using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ollang.Interpreter;
using Ollang.Values;
using Ollang.Utils;
using Ollang.Native;
using Ollang.Async;
using Ollang.Gui;

namespace Ollang.StdLib
{
    public static class StdRegistry
    {
        public static Dictionary<string, ICallable> Builtins { get; } = new();

        // Set true to enable system() calls
        public static bool AllowSystemCalls { get; set; } = false;

        private static string ValidatePath(string path)
        {
            string normalized = Path.GetFullPath(path);
            if (path.Contains("..") && !normalized.StartsWith(Environment.CurrentDirectory))
                throw new UnauthorizedAccessException($"Path traversal blocked: {path}");
            return normalized;
        }

        private static void RegisterMath(string name, Func<List<IValue>, IValue> fn)
        {
            var bfv = new BuiltinFunctionValue(name, (state, args) => fn(args));
            Builtins[name] = bfv;
            Builtins[$"Math.{name}"] = bfv;
        }

        static StdRegistry()
        {
            Builtins["println"] = new BuiltinFunctionValue("println", (state, args) => {
                Console.WriteLine(string.Join(" ", args.Select(a => a.ToString())));
                return new NullValue();
            });

            Builtins["print"] = new BuiltinFunctionValue("print", (state, args) => {
                Console.Write(string.Join(" ", args.Select(a => a.ToString())));
                return new NullValue();
            });

            Builtins["input"] = new BuiltinFunctionValue("input", (state, args) => {
                if (args.Count > 0) Console.Write(args[0].ToString());
                return new StringValue(Console.ReadLine() ?? "");
            });

            Builtins["len"] = new BuiltinFunctionValue("len", (state, args) => {
                if (args.Count == 0) return new NumberValue(0);
                var val = args[0];
                if (val is ArrayValue av) return new NumberValue(av.Elements.Count);
                if (val is StringValue sv) return new NumberValue(sv.Value.Length);
                if (val is DictValue dv) return new NumberValue(dv.Entries.Count);
                return new NumberValue(0);
            });

            Builtins["str"] = new BuiltinFunctionValue("str", (state, args) => new StringValue(args.Count > 0 ? args[0].ToString() : ""));
            Builtins["num"] = new BuiltinFunctionValue("num", (state, args) => new NumberValue(args.Count > 0 ? args[0].AsNumber() : 0));
            Builtins["bool"] = new BuiltinFunctionValue("bool", (state, args) => new BooleanValue(args.Count > 0 ? args[0].AsBool() : false));

            Builtins["Console.color"] = new BuiltinFunctionValue("Console.color", (state, args) => {
                if (args.Count > 0 && Enum.TryParse<ConsoleColor>(args[0].ToString(), true, out var color)) {
                    Console.ForegroundColor = color;
                }
                return new NullValue();
            });

            Builtins["Console.reset"] = new BuiltinFunctionValue("Console.reset", (state, args) => {
                Console.ResetColor();
                return new NullValue();
            });

            Builtins["Console.clear"] = new BuiltinFunctionValue("Console.clear", (state, args) => {
                try { Console.Clear(); } catch { }
                return new NullValue();
            });

            Builtins["Console.setCursor"] = new BuiltinFunctionValue("Console.setCursor", (state, args) => {
                try { Console.SetCursorPosition((int)args[0].AsNumber(), (int)args[1].AsNumber()); } catch { }
                return new NullValue();
            });

            Builtins["Console.setCursorVisible"] = new BuiltinFunctionValue("Console.setCursorVisible", (state, args) => {
                try { Console.CursorVisible = args[0].AsBool(); } catch { }
                return new NullValue();
            });

            Builtins["type"] = new BuiltinFunctionValue("type", (state, args) => {
                if (args.Count == 0) return new StringValue("null");
                var val = args[0];
                if (val is NumberValue) return new StringValue("number");
                if (val is StringValue) return new StringValue("string");
                if (val is BooleanValue) return new StringValue("boolean");
                if (val is ArrayValue) return new StringValue("array");
                if (val is DictValue) return new StringValue("dict");
                if (val is FunctionValue) return new StringValue("function");
                if (val is BuiltinFunctionValue) return new StringValue("builtin");
                if (val is PointerValue) return new StringValue("pointer");
                return new StringValue("null");
            });

            Builtins["sleep"] = new BuiltinFunctionValue("sleep", (state, args) => {
                if (args.Count > 0) System.Threading.Thread.Sleep((int)args[0].AsNumber());
                return new NullValue();
            });

            Builtins["time"] = new BuiltinFunctionValue("time", (state, args) => new NumberValue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            Builtins["clock"] = new BuiltinFunctionValue("clock", (state, args) => new NumberValue(Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds));
            Builtins["exit"] = new BuiltinFunctionValue("exit", (state, args) => {
                Environment.Exit(args.Count > 0 ? (int)args[0].AsNumber() : 0);
                return new NullValue();
            });

            Builtins["GC.Collect"] = new BuiltinFunctionValue("GC.Collect", (state, args) => {
                if (Ollang.VM.VirtualMachine.CurrentVM != null)
                {
                    Ollang.VM.VirtualMachine.CurrentVM.GC.Collect();
                }
                else
                {
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                }
                return new NullValue();
            });

            Builtins["GC.WaitForPendingFinalizers"] = new BuiltinFunctionValue("GC.WaitForPendingFinalizers", (state, args) => {
                System.GC.WaitForPendingFinalizers();
                return new NullValue();
            });

            Builtins["GC.WaitForFullGCApproach"] = new BuiltinFunctionValue("GC.WaitForFullGCApproach", (state, args) => {
                var status = System.GC.WaitForFullGCApproach(args.Count > 0 ? (int)args[0].AsNumber() : -1);
                return new StringValue(status.ToString());
            });

            Builtins["GC.WaitForFullGCComplete"] = new BuiltinFunctionValue("GC.WaitForFullGCComplete", (state, args) => {
                var status = System.GC.WaitForFullGCComplete(args.Count > 0 ? (int)args[0].AsNumber() : -1);
                return new StringValue(status.ToString());
            });

            Builtins["GC.CancelFullGCNotification"] = new BuiltinFunctionValue("GC.CancelFullGCNotification", (state, args) => {
                System.GC.CancelFullGCNotification();
                return new NullValue();
            });
            
            Builtins["GC.stats"] = new BuiltinFunctionValue("GC.stats", (state, args) => {
                var stats = new DictValue();
                if (Ollang.VM.VirtualMachine.CurrentVM != null)
                {
                    var gcStats = Ollang.VM.VirtualMachine.CurrentVM.GC.GetStats();
                    stats.Entries[new StringValue("allocatedBytes")] = new NumberValue(gcStats.TotalAllocated);
                    stats.Entries[new StringValue("allocationCount")] = new NumberValue(gcStats.AllocationCount);
                    stats.Entries[new StringValue("youngCount")] = new NumberValue(gcStats.YoungCount);
                    stats.Entries[new StringValue("oldCount")] = new NumberValue(gcStats.OldCount);
                    stats.Entries[new StringValue("minorCollections")] = new NumberValue(gcStats.MinorCollections);
                    stats.Entries[new StringValue("majorCollections")] = new NumberValue(gcStats.MajorCollections);
                    stats.Entries[new StringValue("lastCollectionMs")] = new NumberValue(gcStats.LastCollectionMs);
                    stats.Entries[new StringValue("totalCollectionMs")] = new NumberValue(gcStats.TotalCollectionMs);
                }
                return stats;
            });

            Builtins["GC.collectYoung"] = new BuiltinFunctionValue("GC.collectYoung", (state, args) => {
                Ollang.VM.VirtualMachine.CurrentVM?.GC.CollectYoung();
                return new NullValue();
            });

            // CLR Interop (Compiled only)
            Builtins["CLR.type"] = new BuiltinFunctionValue("CLR.type", (state, args) => {
                if (state != null) throw new Exception("CLR interop only available in compiled mode");
                string typeName = args[0].ToString();
                var type = Type.GetType(typeName);
                if (type == null) {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        type = asm.GetType(typeName);
                        if (type != null) break;
                    }
                }
                return type != null ? new NativeObjectValue(type) : new NullValue();
            });

            Builtins["CLR.create"] = new BuiltinFunctionValue("CLR.create", (state, args) => {
                if (state != null) throw new Exception("CLR interop only available in compiled mode");
                var typeVal = args[0];
                Type? type = null;
                if (typeVal is NativeObjectValue nov && nov.Value is Type t) type = t;
                else type = Type.GetType(typeVal.ToString());
                if (type == null) throw new Exception($"Type {typeVal} not found");

                var ctorArgs = args.Skip(1).Select(a => {
                    if (a is NumberValue nv) return (object)nv.Value;
                    if (a is StringValue sv1) return (object)sv1.Value;
                    if (a is BooleanValue bv1) return (object)bv1.Value;
                    if (a is NativeObjectValue nov2) return nov2.Value;
                    return (object)a;
                }).ToArray();

                var instance = Activator.CreateInstance(type, ctorArgs);
                return instance != null ? new NativeObjectValue(instance) : new NullValue();
            });

            Builtins["CLR.load"] = new BuiltinFunctionValue("CLR.load", (state, args) => {
                if (state != null) throw new Exception("CLR interop only available in compiled mode");
                return new NativeObjectValue(System.Reflection.Assembly.LoadFrom(args[0].ToString()));
            });

            // Math
            RegisterMath("abs", (args) => new NumberValue(Math.Abs(args[0].AsNumber())));
            RegisterMath("sqrt", (args) => new NumberValue(Math.Sqrt(args[0].AsNumber())));
            RegisterMath("sin", (args) => new NumberValue(Math.Sin(args[0].AsNumber())));
            RegisterMath("cos", (args) => new NumberValue(Math.Cos(args[0].AsNumber())));
            RegisterMath("tan", (args) => new NumberValue(Math.Tan(args[0].AsNumber())));
            RegisterMath("asin", (args) => new NumberValue(Math.Asin(args[0].AsNumber())));
            RegisterMath("acos", (args) => new NumberValue(Math.Acos(args[0].AsNumber())));
            RegisterMath("atan", (args) => new NumberValue(Math.Atan(args[0].AsNumber())));
            RegisterMath("atan2", (args) => new NumberValue(Math.Atan2(args[0].AsNumber(), args[1].AsNumber())));
            RegisterMath("pow", (args) => new NumberValue(Math.Pow(args[0].AsNumber(), args[1].AsNumber())));
            RegisterMath("log", (args) => new NumberValue(Math.Log(args[0].AsNumber())));
            RegisterMath("log10", (args) => new NumberValue(Math.Log10(args[0].AsNumber())));
            RegisterMath("exp", (args) => new NumberValue(Math.Exp(args[0].AsNumber())));
            RegisterMath("floor", (args) => new NumberValue(Math.Floor(args[0].AsNumber())));
            RegisterMath("ceil", (args) => new NumberValue(Math.Ceiling(args[0].AsNumber())));
            RegisterMath("round", (args) => new NumberValue(Math.Round(args[0].AsNumber())));
            RegisterMath("min", (args) => new NumberValue(args.Select(a => a.AsNumber()).Min()));
            RegisterMath("max", (args) => new NumberValue(args.Select(a => a.AsNumber()).Max()));
            
            var rng = new Random();
            RegisterMath("random", (args) => {
                if (args.Count == 0) return new NumberValue(rng.NextDouble());
                if (args.Count == 1) return new NumberValue(rng.Next((int)args[0].AsNumber()));
                return new NumberValue(rng.Next((int)args[0].AsNumber(), (int)args[1].AsNumber()));
            });

            // Array
            Builtins["array"] = new BuiltinFunctionValue("array", (state, args) => {
                var av = new ArrayValue();
                av.Elements.AddRange(args);
                return av;
            });
            Builtins["push"] = new BuiltinFunctionValue("push", (state, args) => {
                if (args[0] is ArrayValue av) for (int i = 1; i < args.Count; i++) av.Elements.Add(args[i]);
                return args[0];
            });
            Builtins["pop"] = new BuiltinFunctionValue("pop", (state, args) => {
                if (args[0] is ArrayValue av && av.Elements.Count > 0) {
                    var last = av.Elements[^1];
                    av.Elements.RemoveAt(av.Elements.Count - 1);
                    return last;
                }
                return new NullValue();
            });
            Builtins["shift"] = new BuiltinFunctionValue("shift", (state, args) => {
                if (args[0] is ArrayValue av && av.Elements.Count > 0) {
                    var first = av.Elements[0];
                    av.Elements.RemoveAt(0);
                    return first;
                }
                return new NullValue();
            });
            Builtins["unshift"] = new BuiltinFunctionValue("unshift", (state, args) => {
                if (args[0] is ArrayValue av) {
                    for (int i = args.Count - 1; i >= 1; i--) av.Elements.Insert(0, args[i]);
                }
                return args[0];
            });
            Builtins["slice"] = new BuiltinFunctionValue("slice", (state, args) => {
                if (args[0] is ArrayValue av) {
                    int start = (int)args[1].AsNumber();
                    int end = args.Count > 2 ? (int)args[2].AsNumber() : av.Elements.Count;
                    var nav = new ArrayValue();
                    nav.Elements.AddRange(av.Elements.Skip(start).Take(end - start));
                    return nav;
                }
                return new NullValue();
            });
            Builtins["sort"] = new BuiltinFunctionValue("sort", (state, args) => {
                if (args[0] is ArrayValue av) {
                    var sorted = av.Elements.OrderBy(e => e.ToString()).ToList();
                    var nav = new ArrayValue();
                    nav.Elements.AddRange(sorted);
                    return nav;
                }
                return args[0];
            });
            Builtins["concat"] = new BuiltinFunctionValue("concat", (state, args) => {
                var av = new ArrayValue();
                foreach (var arg in args)
                {
                    if (arg is ArrayValue other) av.Elements.AddRange(other.Elements);
                    else av.Elements.Add(arg);
                }
                return av;
            });
            Builtins["reverse"] = new BuiltinFunctionValue("reverse", (state, args) => {
                if (args[0] is ArrayValue av) {
                    var reversed = av.Elements.AsEnumerable().Reverse().ToList();
                    var nav = new ArrayValue();
                    nav.Elements.AddRange(reversed);
                    return nav;
                }
                return args[0];
            });

            Builtins["join"] = new BuiltinFunctionValue("join", (state, args) => {
                if (args.Count < 2 || !(args[0] is ArrayValue av)) return new StringValue("");
                string del = args[1].ToString();
                return new StringValue(string.Join(del, av.Elements.Select(e => e.ToString())));
            });

            Builtins["indexOf"] = new BuiltinFunctionValue("indexOf", (state, args) => {
                if (args[0] is ArrayValue av) return new NumberValue(av.Elements.IndexOf(args[1]));
                if (args[0] is StringValue sv) return new NumberValue(sv.Value.IndexOf(args[1].ToString()));
                return new NumberValue(-1);
            });

            Builtins["last"] = new BuiltinFunctionValue("last", (state, args) => {
                if (args[0] is ArrayValue av && av.Elements.Count > 0) return av.Elements[^1];
                if (args[0] is StringValue sv && sv.Value.Length > 0) return new StringValue(sv.Value[^1].ToString());
                return new NullValue();
            });

            // String
            Builtins["substring"] = new BuiltinFunctionValue("substring", (state, args) => {
                var s = args[0].ToString();
                int start = (int)args[1].AsNumber();
                int len = args.Count > 2 ? (int)args[2].AsNumber() : s.Length - start;
                
                if (start < 0) start = 0;
                if (start > s.Length) start = s.Length;
                if (len < 0) len = 0;
                if (start + len > s.Length) len = s.Length - start;

                return new StringValue(s.Substring(start, len));
            });
            Builtins["find"] = new BuiltinFunctionValue("find", (state, args) => {
                var s = args[0].ToString();
                var search = args[1].ToString();
                int start = args.Count > 2 ? (int)args[2].AsNumber() : 0;
                return new NumberValue(s.IndexOf(search, start));
            });
            Builtins["lower"] = new BuiltinFunctionValue("lower", (state, args) => new StringValue(args[0].ToString().ToLower()));
            Builtins["upper"] = new BuiltinFunctionValue("upper", (state, args) => new StringValue(args[0].ToString().ToUpper()));
            Builtins["trim"] = new BuiltinFunctionValue("trim", (state, args) => new StringValue(args[0].ToString().Trim()));
            Builtins["replace"] = new BuiltinFunctionValue("replace", (state, args) => new StringValue(args[0].ToString().Replace(args[1].ToString(), args[2].ToString())));
            Builtins["split"] = new BuiltinFunctionValue("split", (state, args) => {
                var parts = args[0].ToString().Split(new[] { args[1].ToString() }, StringSplitOptions.None);
                var av = new ArrayValue();
                av.Elements.AddRange(parts.Select(p => new StringValue(p)));
                return av;
            });

            // Dict
            Builtins["dict"] = new BuiltinFunctionValue("dict", (state, args) => new DictValue());
            Builtins["keys"] = new BuiltinFunctionValue("keys", (state, args) => {
                if (args[0] is DictValue dv) {
                    var av = new ArrayValue();
                    av.Elements.AddRange(dv.Entries.Keys);
                    return av;
                }
                return new ArrayValue();
            });
            Builtins["values"] = new BuiltinFunctionValue("values", (state, args) => {
                if (args[0] is DictValue dv) {
                    var av = new ArrayValue();
                    av.Elements.AddRange(dv.Entries.Values);
                    return av;
                }
                return new ArrayValue();
            });
            Builtins["has"] = new BuiltinFunctionValue("has", (state, args) => new BooleanValue(args[0] is DictValue dv && dv.Entries.ContainsKey(args[1])));
            Builtins["remove"] = new BuiltinFunctionValue("remove", (state, args) => {
                if (args[0] is DictValue dv && dv.Entries.Remove(args[1], out var val)) return val;
                return new NullValue();
            });
            Builtins["merge"] = new BuiltinFunctionValue("merge", (state, args) => {
                var nd = new DictValue();
                foreach (var arg in args)
                {
                    if (arg is DictValue other)
                        foreach (var kv in other.Entries) nd.Entries[kv.Key] = kv.Value;
                }
                return nd;
            });

            // File
            Builtins["File.read"] = new BuiltinFunctionValue("File.read", (state, args) => {
                if (args.Count == 0) throw new Exception("File.read requires a path argument");
                return new StringValue(File.ReadAllText(ValidatePath(args[0].ToString())));
            });
            Builtins["File.write"] = new BuiltinFunctionValue("File.write", (state, args) => {
                if (args.Count < 2) throw new Exception("File.write requires path and content arguments");
                File.WriteAllText(ValidatePath(args[0].ToString()), args[1].ToString()); return new NullValue();
            });
            Builtins["File.append"] = new BuiltinFunctionValue("File.append", (state, args) => {
                if (args.Count < 2) throw new Exception("File.append requires path and content arguments");
                File.AppendAllText(ValidatePath(args[0].ToString()), args[1].ToString()); return new NullValue();
            });
            Builtins["File.exists"] = new BuiltinFunctionValue("File.exists", (state, args) => {
                if (args.Count == 0) throw new Exception("File.exists requires a path argument");
                return new BooleanValue(File.Exists(ValidatePath(args[0].ToString())));
            });
            Builtins["File.delete"] = new BuiltinFunctionValue("File.delete", (state, args) => {
                if (args.Count == 0) throw new Exception("File.delete requires a path argument");
                File.Delete(ValidatePath(args[0].ToString())); return new NullValue();
            });

            // Directory
            Builtins["Dir.create"] = new BuiltinFunctionValue("Dir.create", (state, args) => { Directory.CreateDirectory(args[0].ToString()); return new NullValue(); });
            Builtins["Dir.exists"] = new BuiltinFunctionValue("Dir.exists", (state, args) => new BooleanValue(Directory.Exists(args[0].ToString())));
            Builtins["Dir.files"] = new BuiltinFunctionValue("Dir.files", (state, args) => {
                var files = Directory.GetFiles(args[0].ToString(), args.Count > 1 ? args[1].ToString() : "*");
                var av = new ArrayValue();
                av.Elements.AddRange(files.Select(f => new StringValue(f)));
                return av;
            });
            Builtins["Dir.dirs"] = new BuiltinFunctionValue("Dir.dirs", (state, args) => {
                var dirs = Directory.GetDirectories(args[0].ToString(), args.Count > 1 ? args[1].ToString() : "*");
                var av = new ArrayValue();
                av.Elements.AddRange(dirs.Select(d => new StringValue(d)));
                return av;
            });

            // JSON
            Builtins["JSON.stringify"] = new BuiltinFunctionValue("JSON.stringify", (state, args) => {
                return new StringValue(JsonSerializer.Serialize(ValueToObj(args[0])));
            });
            Builtins["JSON.parse"] = new BuiltinFunctionValue("JSON.parse", (state, args) => {
                return ObjToValue(JsonSerializer.Deserialize<object>(args[0].ToString()));
            });

            // OS
            Builtins["OS.name"] = new BuiltinFunctionValue("OS.name", (state, args) => new StringValue(Environment.MachineName));
            Builtins["OS.user"] = new BuiltinFunctionValue("OS.user", (state, args) => new StringValue(Environment.UserName));
            Builtins["OS.version"] = new BuiltinFunctionValue("OS.version", (state, args) => new StringValue(Environment.OSVersion.ToString()));
            Builtins["OS.platform"] = new BuiltinFunctionValue("OS.platform", (state, args) => new StringValue(RuntimeInformation.OSDescription));
            Builtins["OS.getEnv"] = new BuiltinFunctionValue("OS.getEnv", (state, args) => new StringValue(Sys.Env(args[0].ToString()) ?? ""));
            Builtins["OS.setEnv"] = new BuiltinFunctionValue("OS.setEnv", (state, args) => { Sys.SetEnv(args[0].ToString(), args[1].ToString()); return new NullValue(); });
            Builtins["OS.args"] = new BuiltinFunctionValue("OS.args", (state, args) => {
                var av = new ArrayValue();
                av.Elements.AddRange(Environment.GetCommandLineArgs().Select(a => new StringValue(a)));
                return av;
            });
            Builtins["system"] = new BuiltinFunctionValue("system", (state, args) => {
                if (!AllowSystemCalls)
                    throw new UnauthorizedAccessException("system() is disabled for security. Set StdRegistry.AllowSystemCalls = true to enable.");
                if (args.Count == 0) throw new Exception("system requires a command argument");
                var proc = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + args[0].ToString()) { UseShellExecute = false, RedirectStandardOutput = true });
                return new StringValue(proc?.StandardOutput.ReadToEnd() ?? "");
            });

            // Utils
            Builtins["Utils.b64Encode"] = new BuiltinFunctionValue("Utils.b64Encode", (state, args) => new StringValue(Sys.B64E(args[0].ToString())));
            Builtins["Utils.b64Decode"] = new BuiltinFunctionValue("Utils.b64Decode", (state, args) => new StringValue(Sys.B64D(args[0].ToString())));
            Builtins["Utils.hexEncode"] = new BuiltinFunctionValue("Utils.hexEncode", (state, args) => new StringValue(Sys.HexE(args[0].ToString())));
            Builtins["Utils.hexDecode"] = new BuiltinFunctionValue("Utils.hexDecode", (state, args) => new StringValue(Sys.HexD(args[0].ToString())));
            Builtins["Utils.sha256"] = new BuiltinFunctionValue("Utils.sha256", (state, args) => new StringValue(Sys.HashSHA256(args[0].ToString())));
            Builtins["Utils.md5"] = new BuiltinFunctionValue("Utils.md5", (state, args) => new StringValue(Sys.HashMD5(args[0].ToString())));
            Builtins["Utils.uuid"] = new BuiltinFunctionValue("Utils.uuid", (state, args) => new StringValue(Sys.UUID()));
            Builtins["Utils.timestamp"] = new BuiltinFunctionValue("Utils.timestamp", (state, args) => new NumberValue(Sys.Timestamp()));
            Builtins["Utils.memoryUsage"] = new BuiltinFunctionValue("Utils.memoryUsage", (state, args) => new NumberValue(Sys.MemoryUsage()));
            Builtins["Utils.workingDir"] = new BuiltinFunctionValue("Utils.workingDir", (state, args) => new StringValue(Sys.WorkingDir()));

            // Path
            Builtins["Path.join"] = new BuiltinFunctionValue("Path.join", (state, args) => new StringValue(Sys.PathJoin(args[0].ToString(), args[1].ToString())));
            Builtins["Path.dir"] = new BuiltinFunctionValue("Path.dir", (state, args) => new StringValue(Sys.PathDir(args[0].ToString()) ?? ""));
            Builtins["Path.base"] = new BuiltinFunctionValue("Path.base", (state, args) => new StringValue(Sys.PathBase(args[0].ToString())));
            Builtins["Path.ext"] = new BuiltinFunctionValue("Path.ext", (state, args) => new StringValue(Sys.PathExt(args[0].ToString())));
            
            // File Size
            Builtins["File.size"] = new BuiltinFunctionValue("File.size", (state, args) => new NumberValue(Sys.FileSize(args[0].ToString())));

            // Web
            Builtins["Web.get"] = new BuiltinFunctionValue("Web.get", (state, args) => new StringValue(Sys.Get(args[0].ToString())));
            Builtins["Web.post"] = new BuiltinFunctionValue("Web.post", (state, args) => new StringValue(Sys.Post(args[0].ToString(), args[1].ToString())));
            Builtins["Web.fetch"] = new BuiltinFunctionValue("Web.fetch", (state, args) => {
                string url = args[0].ToString();
                string method = args.Count > 1 ? args[1].ToString() : "GET";
                Dictionary<string, string>? headers = null;
                if (args.Count > 2 && args[2] is DictValue dv)
                {
                    headers = dv.Entries.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToString());
                }
                string? body = args.Count > 3 ? args[3].ToString() : null;
                return new StringValue(Sys.HttpFetch(url, method, headers, body));
            });

            // Native
            Builtins["alloc"] = new BuiltinFunctionValue("alloc", (state, args) => {
                int size = (int)args[0].AsNumber();
                if (Ollang.VM.VirtualMachine.CurrentVM != null)
                {
                    return new PointerValue(Ollang.VM.VirtualMachine.CurrentVM.GC.Allocate(size));
                }
                return new PointerValue(Marshal.AllocHGlobal(size));
            });
            Builtins["free"] = new BuiltinFunctionValue("free", (state, args) => { 
                if (args[0] is PointerValue pv) 
                {
                     if (Ollang.VM.VirtualMachine.CurrentVM != null)
                     {
                         Ollang.VM.VirtualMachine.CurrentVM.GC.Free(pv.Address);
                     }
                     else
                     {
                         Marshal.FreeHGlobal(pv.Address); 
                     }
                }
                return new NullValue(); 
            });
            
            Builtins["read8"] = new BuiltinFunctionValue("read8", (state, args) => new NumberValue(Marshal.ReadByte(((PointerValue)args[0]).Address)));
            Builtins["read16"] = new BuiltinFunctionValue("read16", (state, args) => new NumberValue(Marshal.ReadInt16(((PointerValue)args[0]).Address)));
            Builtins["read32"] = new BuiltinFunctionValue("read32", (state, args) => new NumberValue(Marshal.ReadInt32(((PointerValue)args[0]).Address)));
            Builtins["read64"] = new BuiltinFunctionValue("read64", (state, args) => new NumberValue(Marshal.ReadInt64(((PointerValue)args[0]).Address)));
            
            Builtins["write8"] = new BuiltinFunctionValue("write8", (state, args) => { Marshal.WriteByte(((PointerValue)args[0]).Address, (byte)args[1].AsNumber()); return new NullValue(); });
            Builtins["write16"] = new BuiltinFunctionValue("write16", (state, args) => { Marshal.WriteInt16(((PointerValue)args[0]).Address, (short)args[1].AsNumber()); return new NullValue(); });
            Builtins["write32"] = new BuiltinFunctionValue("write32", (state, args) => { Marshal.WriteInt32(((PointerValue)args[0]).Address, (int)args[1].AsNumber()); return new NullValue(); });
            Builtins["write64"] = new BuiltinFunctionValue("write64", (state, args) => { Marshal.WriteInt64(((PointerValue)args[0]).Address, (long)args[1].AsNumber()); return new NullValue(); });

            Builtins["ptrToStr"] = new BuiltinFunctionValue("ptrToStr", (state, args) => {
                int len = args.Count > 1 ? (int)args[1].AsNumber() : -1;
                string? str = len == -1 ? Marshal.PtrToStringAnsi(((PointerValue)args[0]).Address) : Marshal.PtrToStringAnsi(((PointerValue)args[0]).Address, len);
                return str != null ? new StringValue(str) : new NullValue();
            });
            Builtins["strToPtr"] = new BuiltinFunctionValue("strToPtr", (state, args) => new PointerValue(Marshal.StringToHGlobalAnsi(args[0].ToString())));

            Builtins["memcpy"] = new BuiltinFunctionValue("memcpy", (state, args) => {
                IntPtr dest = ((PointerValue)args[0]).Address;
                IntPtr src = ((PointerValue)args[1]).Address;
                int size = (int)args[2].AsNumber();
                unsafe { Buffer.MemoryCopy(src.ToPointer(), dest.ToPointer(), size, size); }
                return args[0];
            });

            Builtins["memset"] = new BuiltinFunctionValue("memset", (state, args) => {
                IntPtr dest = ((PointerValue)args[0]).Address;
                byte val = (byte)args[1].AsNumber();
                int size = (int)args[2].AsNumber();
                unsafe {
                    byte* p = (byte*)dest.ToPointer();
                    for (int i = 0; i < size; i++) p[i] = val;
                }
                return args[0];
            });

            // Memory Namespace (Low Level)
            Builtins["Memory.openProcess"] = new BuiltinFunctionValue("Memory.openProcess", (state, args) => new PointerValue(Memory.OpenProcess((uint)args[0].AsNumber(), args[1].AsBool(), (int)args[2].AsNumber())));
            Builtins["Memory.closeHandle"] = new BuiltinFunctionValue("Memory.closeHandle", (state, args) => new BooleanValue(Memory.CloseHandle(((PointerValue)args[0]).Address)));
            Builtins["Memory.read"] = new BuiltinFunctionValue("Memory.read", (state, args) => {
                IntPtr h = ((PointerValue)args[0]).Address;
                IntPtr addr = ((PointerValue)args[1]).Address;
                int size = (int)args[2].AsNumber();
                byte[] buf = new byte[size];
                Memory.ReadProcessMemory(h, addr, buf, size, out _);
                var av = new ArrayValue();
                foreach (var b in buf) av.Elements.Add(new NumberValue(b));
                return av;
            });
            Builtins["Memory.write"] = new BuiltinFunctionValue("Memory.write", (state, args) => {
                IntPtr h = ((PointerValue)args[0]).Address;
                IntPtr addr = ((PointerValue)args[1]).Address;
                if (args[2] is ArrayValue av) {
                    byte[] data = av.Elements.Select(e => (byte)e.AsNumber()).ToArray();
                    return new BooleanValue(Memory.WriteProcessMemory(h, addr, data, data.Length, out _));
                }
                return new BooleanValue(false);
            });
            Builtins["Memory.protect"] = new BuiltinFunctionValue("Memory.protect", (state, args) => {
                Memory.VirtualProtectEx(((PointerValue)args[0]).Address, ((PointerValue)args[1]).Address, (UIntPtr)args[2].AsNumber(), (uint)args[3].AsNumber(), out uint old);
                return new NumberValue(old);
            });
            Builtins["Memory.allocEx"] = new BuiltinFunctionValue("Memory.allocEx", (state, args) => new PointerValue(Memory.VirtualAllocEx(((PointerValue)args[0]).Address, ((PointerValue)args[1]).Address, (uint)args[2].AsNumber(), (uint)args[3].AsNumber(), (uint)args[4].AsNumber())));
            Builtins["Memory.getModule"] = new BuiltinFunctionValue("Memory.getModule", (state, args) => new PointerValue(Memory.GetModuleBase((int)args[0].AsNumber(), args[1].ToString())));
            Builtins["Memory.scan"] = new BuiltinFunctionValue("Memory.scan", (state, args) => new PointerValue(Memory.Scan(((PointerValue)args[0]).Address, args[1].ToString())));
            Builtins["Memory.patch"] = new BuiltinFunctionValue("Memory.patch", (state, args) => {
                if (args[2] is ArrayValue av) {
                    byte[] data = av.Elements.Select(e => (byte)e.AsNumber()).ToArray();
                    return new BooleanValue(Memory.Patch(((PointerValue)args[0]).Address, ((PointerValue)args[1]).Address, data));
                }
                return new BooleanValue(false);
            });
            Builtins["Memory.nop"] = new BuiltinFunctionValue("Memory.nop", (state, args) => new BooleanValue(Memory.Nop(((PointerValue)args[0]).Address, ((PointerValue)args[1]).Address, (int)args[2].AsNumber())));

            // DLL
            Builtins["DLL.load"] = new BuiltinFunctionValue("DLL.load", (state, args) => new PointerValue(Memory.LoadLibrary(args[0].ToString())));
            Builtins["DLL.proc"] = new BuiltinFunctionValue("DLL.proc", (state, args) => new PointerValue(Memory.GetProcAddress(((PointerValue)args[0]).Address, args[1].ToString())));
            Builtins["DLL.free"] = new BuiltinFunctionValue("DLL.free", (state, args) => new BooleanValue(Memory.FreeLibrary(((PointerValue)args[0]).Address)));
            Builtins["DLL.call"] = new BuiltinFunctionValue("DLL.call", (state, args) => {
                var ptr = ((PointerValue)args[0]).Address;
                var callArgs = args.Skip(1).Select(a => {
                    if (a is NumberValue nv) return (object)(long)nv.Value;
                    if (a is StringValue sv) return (object)sv.Value;
                    if (a is BooleanValue bv) return (object)(bv.Value ? 1L : 0L);
                    if (a is PointerValue pv) return (object)pv.Address;
                    return (object)0L;
                }).ToArray();

                try {
                    Delegate? del = null;
                    int count = callArgs.Length;

                    switch(count)
                    {
                        case 0: del = Marshal.GetDelegateForFunctionPointer<Call0>(ptr); break;
                        case 1: del = Marshal.GetDelegateForFunctionPointer<Call1>(ptr); break;
                        case 2: del = Marshal.GetDelegateForFunctionPointer<Call2>(ptr); break;
                        case 3: del = Marshal.GetDelegateForFunctionPointer<Call3>(ptr); break;
                        case 4: del = Marshal.GetDelegateForFunctionPointer<Call4>(ptr); break;
                        case 5: del = Marshal.GetDelegateForFunctionPointer<Call5>(ptr); break;
                        case 6: del = Marshal.GetDelegateForFunctionPointer<Call6>(ptr); break;
                        case 7: del = Marshal.GetDelegateForFunctionPointer<Call7>(ptr); break;
                        case 8: del = Marshal.GetDelegateForFunctionPointer<Call8>(ptr); break;
                        default:
                            if (count <= 16)
                            {
                                 Type[] argTypes = Enumerable.Repeat(typeof(long), count).ToArray();
                                 var delegateType = GetDelegateType(argTypes);
                                 del = Marshal.GetDelegateForFunctionPointer(ptr, delegateType);
                            }
                            else throw new Exception("DLL calls currently support max 16 arguments");
                            break;
                    }

                    var result = del.DynamicInvoke(callArgs);
                    return new NumberValue(Convert.ToDouble(result));
                } catch (Exception ex) {
                    throw new Exception($"DLL Error: {ex.Message}");
                }
            });

            // String extras
            Builtins["startsWith"] = new BuiltinFunctionValue("startsWith", (state, args) =>
                new BooleanValue(args[0].ToString().StartsWith(args[1].ToString())));
            Builtins["endsWith"] = new BuiltinFunctionValue("endsWith", (state, args) =>
                new BooleanValue(args[0].ToString().EndsWith(args[1].ToString())));
            Builtins["repeat"] = new BuiltinFunctionValue("repeat", (state, args) => {
                string s = args[0].ToString();
                int count = (int)args[1].AsNumber();
                return new StringValue(string.Concat(Enumerable.Repeat(s, Math.Max(0, count))));
            });
            Builtins["padLeft"] = new BuiltinFunctionValue("padLeft", (state, args) => {
                string s = args[0].ToString();
                int width = (int)args[1].AsNumber();
                char pad = args.Count > 2 ? args[2].ToString()[0] : ' ';
                return new StringValue(s.PadLeft(width, pad));
            });
            Builtins["padRight"] = new BuiltinFunctionValue("padRight", (state, args) => {
                string s = args[0].ToString();
                int width = (int)args[1].AsNumber();
                char pad = args.Count > 2 ? args[2].ToString()[0] : ' ';
                return new StringValue(s.PadRight(width, pad));
            });
            Builtins["contains"] = new BuiltinFunctionValue("contains", (state, args) => {
                if (args[0] is ArrayValue av) return new BooleanValue(av.Elements.Any(e => e.Equals(args[1])));
                return new BooleanValue(args[0].ToString().Contains(args[1].ToString()));
            });
            Builtins["charCodeAt"] = new BuiltinFunctionValue("charCodeAt", (state, args) => {
                string s = args[0].ToString();
                int idx = (int)args[1].AsNumber();
                return idx >= 0 && idx < s.Length ? new NumberValue(s[idx]) : new NullValue();
            });
            Builtins["fromCharCode"] = new BuiltinFunctionValue("fromCharCode", (state, args) =>
                new StringValue(((char)(int)args[0].AsNumber()).ToString()));
            Builtins["trimStart"] = new BuiltinFunctionValue("trimStart", (state, args) =>
                new StringValue(args[0].ToString().TrimStart()));
            Builtins["trimEnd"] = new BuiltinFunctionValue("trimEnd", (state, args) =>
                new StringValue(args[0].ToString().TrimEnd()));

            // Array extras (higher-order via VM callbacks)
            Builtins["map"] = new BuiltinFunctionValue("map", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    var result = new ArrayValue();
                    foreach (var elem in av.Elements) {
                        var callState = state ?? new InterpreterState();
                        result.Elements.Add(fn.Call(callState, new List<IValue> { elem }));
                    }
                    return result;
                }
                return new ArrayValue();
            });
            Builtins["filter"] = new BuiltinFunctionValue("filter", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    var result = new ArrayValue();
                    foreach (var elem in av.Elements) {
                        var callState = state ?? new InterpreterState();
                        if (fn.Call(callState, new List<IValue> { elem }).AsBool())
                            result.Elements.Add(elem);
                    }
                    return result;
                }
                return new ArrayValue();
            });
            Builtins["reduce"] = new BuiltinFunctionValue("reduce", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    IValue acc = args.Count > 2 ? args[2] : (av.Elements.Count > 0 ? av.Elements[0] : new NullValue());
                    int start = args.Count > 2 ? 0 : 1;
                    for (int i = start; i < av.Elements.Count; i++) {
                        var callState = state ?? new InterpreterState();
                        acc = fn.Call(callState, new List<IValue> { acc, av.Elements[i] });
                    }
                    return acc;
                }
                return new NullValue();
            });
            Builtins["find"] = new BuiltinFunctionValue("find", (state, args) => {
                if (args[0] is StringValue) {
                    // String find (already exists, but this handles the 2-arg form)
                    return new NumberValue(args[0].ToString().IndexOf(args[1].ToString()));
                }
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    foreach (var elem in av.Elements) {
                        var callState = state ?? new InterpreterState();
                        if (fn.Call(callState, new List<IValue> { elem }).AsBool())
                            return elem;
                    }
                }
                return new NullValue();
            });
            Builtins["every"] = new BuiltinFunctionValue("every", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    foreach (var elem in av.Elements) {
                        var callState = state ?? new InterpreterState();
                        if (!fn.Call(callState, new List<IValue> { elem }).AsBool()) return new BooleanValue(false);
                    }
                    return new BooleanValue(true);
                }
                return new BooleanValue(false);
            });
            Builtins["some"] = new BuiltinFunctionValue("some", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    foreach (var elem in av.Elements) {
                        var callState = state ?? new InterpreterState();
                        if (fn.Call(callState, new List<IValue> { elem }).AsBool()) return new BooleanValue(true);
                    }
                }
                return new BooleanValue(false);
            });
            Builtins["flat"] = new BuiltinFunctionValue("flat", (state, args) => {
                var result = new ArrayValue();
                void Flatten(IValue val) {
                    if (val is ArrayValue inner) foreach (var e in inner.Elements) Flatten(e);
                    else result.Elements.Add(val);
                }
                if (args[0] is ArrayValue av) Flatten(av);
                return result;
            });
            Builtins["fill"] = new BuiltinFunctionValue("fill", (state, args) => {
                int count = (int)args[0].AsNumber();
                IValue value = args.Count > 1 ? args[1] : new NullValue();
                var result = new ArrayValue();
                for (int i = 0; i < count; i++) result.Elements.Add(value);
                return result;
            });
            Builtins["includes"] = new BuiltinFunctionValue("includes", (state, args) => {
                if (args[0] is ArrayValue av) return new BooleanValue(av.Elements.Any(e => e.Equals(args[1])));
                return new BooleanValue(args[0].ToString().Contains(args[1].ToString()));
            });
            Builtins["forEach"] = new BuiltinFunctionValue("forEach", (state, args) => {
                if (args[0] is ArrayValue av && args[1] is ICallable fn) {
                    for (int i = 0; i < av.Elements.Count; i++) {
                        var callState = state ?? new InterpreterState();
                        fn.Call(callState, new List<IValue> { av.Elements[i], new NumberValue(i) });
                    }
                }
                return new NullValue();
            });

            // Math extras
            RegisterMath("clamp", (args) => {
                double val = args[0].AsNumber(), lo = args[1].AsNumber(), hi = args[2].AsNumber();
                return new NumberValue(Math.Max(lo, Math.Min(hi, val)));
            });
            RegisterMath("lerp", (args) => {
                double a = args[0].AsNumber(), b = args[1].AsNumber(), t = args[2].AsNumber();
                return new NumberValue(a + (b - a) * t);
            });
            RegisterMath("sign", (args) => new NumberValue(Math.Sign(args[0].AsNumber())));
            RegisterMath("truncate", (args) => new NumberValue(Math.Truncate(args[0].AsNumber())));

            // Concurrency
            Builtins["channel"] = new BuiltinFunctionValue("channel", (state, args) => {
                int capacity = args.Count > 0 ? (int)args[0].AsNumber() : 0;
                return new ChannelValue(new Channel(capacity));
            });
            Builtins["send"] = new BuiltinFunctionValue("send", (state, args) => {
                if (args[0] is ChannelValue cv) { cv.Channel.Send(args[1]); return new BooleanValue(true); }
                throw new Exception("send requires a channel as first argument");
            });
            Builtins["receive"] = new BuiltinFunctionValue("receive", (state, args) => {
                if (args[0] is ChannelValue cv) return cv.Channel.Receive();
                throw new Exception("receive requires a channel as first argument");
            });
            Builtins["tryReceive"] = new BuiltinFunctionValue("tryReceive", (state, args) => {
                if (args[0] is ChannelValue cv) {
                    int timeout = args.Count > 1 ? (int)args[1].AsNumber() : 0;
                    return cv.Channel.TryReceive(timeout) ?? new NullValue();
                }
                throw new Exception("tryReceive requires a channel as first argument");
            });
            Builtins["closeChannel"] = new BuiltinFunctionValue("closeChannel", (state, args) => {
                if (args[0] is ChannelValue cv) cv.Channel.Close();
                return new NullValue();
            });
            Builtins["mutex"] = new BuiltinFunctionValue("mutex", (state, args) => new MutexValue());
            Builtins["lock"] = new BuiltinFunctionValue("lock", (state, args) => {
                if (args[0] is MutexValue mv) mv.Lock();
                return new NullValue();
            });
            Builtins["unlock"] = new BuiltinFunctionValue("unlock", (state, args) => {
                if (args[0] is MutexValue mv) mv.Unlock();
                return new NullValue();
            });
            Builtins["waitgroup"] = new BuiltinFunctionValue("waitgroup", (state, args) =>
                new WaitGroupValue((int)args[0].AsNumber()));
            Builtins["wgDone"] = new BuiltinFunctionValue("wgDone", (state, args) => {
                if (args[0] is WaitGroupValue wg) wg.Done();
                return new NullValue();
            });
            Builtins["wgWait"] = new BuiltinFunctionValue("wgWait", (state, args) => {
                if (args[0] is WaitGroupValue wg) wg.Wait();
                return new NullValue();
            });

            // Stopwatch / Timer
            var stopwatches = new Dictionary<string, System.Diagnostics.Stopwatch>();
            Builtins["Stopwatch.start"] = new BuiltinFunctionValue("Stopwatch.start", (state, args) => {
                string name = args.Count > 0 ? args[0].ToString() : "default";
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                stopwatches[name] = sw;
                return new StringValue(name);
            });
            Builtins["Stopwatch.elapsed"] = new BuiltinFunctionValue("Stopwatch.elapsed", (state, args) => {
                string name = args.Count > 0 ? args[0].ToString() : "default";
                if (stopwatches.TryGetValue(name, out var sw)) return new NumberValue(sw.Elapsed.TotalMilliseconds);
                return new NumberValue(0);
            });
            Builtins["Stopwatch.stop"] = new BuiltinFunctionValue("Stopwatch.stop", (state, args) => {
                string name = args.Count > 0 ? args[0].ToString() : "default";
                if (stopwatches.TryGetValue(name, out var sw)) { sw.Stop(); return new NumberValue(sw.Elapsed.TotalMilliseconds); }
                return new NumberValue(0);
            });
            Builtins["Stopwatch.reset"] = new BuiltinFunctionValue("Stopwatch.reset", (state, args) => {
                string name = args.Count > 0 ? args[0].ToString() : "default";
                stopwatches.Remove(name);
                return new NullValue();
            });

            // Assert (developer tooling)
            Builtins["assert"] = new BuiltinFunctionValue("assert", (state, args) => {
                if (args.Count == 0 || !args[0].AsBool()) {
                    string msg = args.Count > 1 ? args[1].ToString() : "Assertion failed";
                    throw new Exception($"AssertionError: {msg}");
                }
                return new BooleanValue(true);
            });

            // Debug
            Builtins["debug"] = new BuiltinFunctionValue("debug", (state, args) => {
                var vm = Ollang.VM.VirtualMachine.CurrentVM;
                if (vm == null) return new NullValue();
                string cmd = args.Count > 0 ? args[0].ToString() : "stack";
                return cmd switch {
                    "stack" => new StringValue(vm.DumpStack()),
                    "locals" => new StringValue(vm.DumpLocals()),
                    "globals" => new StringValue(vm.DumpGlobals()),
                    _ => new StringValue(vm.DumpStack())
                };
            });

            GuiRuntime.RegisterBuiltins(Builtins);
        }

        // Concrete Delegates for P/Invoke
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call0();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call1(long a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call2(long a, long b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call3(long a, long b, long c);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call4(long a, long b, long c, long d);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call5(long a, long b, long c, long d, long e);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call6(long a, long b, long c, long d, long e, long f);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call7(long a, long b, long c, long d, long e, long f, long g);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call8(long a, long b, long c, long d, long e, long f, long g, long h);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call9(long a, long b, long c, long d, long e, long f, long g, long h, long i);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call10(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call11(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call12(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k, long l);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call13(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k, long l, long m);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call14(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k, long l, long m, long n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call15(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k, long l, long m, long n, long o);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate long Call16(long a, long b, long c, long d, long e, long f, long g, long h, long i, long j, long k, long l, long m, long n, long o, long p);

        private static Type GetDelegateType(Type[] argTypes)
        {
            var types = new List<Type>(argTypes);
            types.Add(typeof(long)); // Return type
            return System.Linq.Expressions.Expression.GetDelegateType(types.ToArray());
        }

        private static object? ValueToObj(IValue val)
        {
            if (val is NumberValue nv) return nv.Value;
            if (val is StringValue sv) return sv.Value;
            if (val is BooleanValue bv) return bv.Value;
            if (val is ArrayValue av) return av.Elements.Select(ValueToObj).ToList();
            if (val is DictValue dv) return dv.Entries.ToDictionary(kv => kv.Key.ToString(), kv => ValueToObj(kv.Value));
            return null;
        }

        private static IValue ObjToValue(object? obj)
        {
            if (obj == null) return new NullValue();
            if (obj is double d) return new NumberValue(d);
            if (obj is int i) return new NumberValue(i);
            if (obj is long l) return new NumberValue(l);
            if (obj is string s) return new StringValue(s);
            if (obj is bool b) return new BooleanValue(b);
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number) return new NumberValue(je.GetDouble());
                if (je.ValueKind == JsonValueKind.String) return new StringValue(je.GetString() ?? "");
                if (je.ValueKind == JsonValueKind.True) return new BooleanValue(true);
                if (je.ValueKind == JsonValueKind.False) return new BooleanValue(false);
                if (je.ValueKind == JsonValueKind.Array)
                {
                    var av = new ArrayValue();
                    foreach (var item in je.EnumerateArray()) av.Elements.Add(ObjToValue(item));
                    return av;
                }
                if (je.ValueKind == JsonValueKind.Object)
                {
                    var dv = new DictValue();
                    foreach (var prop in je.EnumerateObject()) dv.Entries[new StringValue(prop.Name)] = ObjToValue(prop.Value);
                    return dv;
                }
            }
            return new NullValue();
        }

        public static void Register(InterpreterState state)
        {
            var namespaces = new Dictionary<string, DictValue>();

            foreach (var kvp in Builtins)
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
                        state.DefineVar(nsName, nsDict);
                    }
                    nsDict.Entries[new StringValue(funcName)] = kvp.Value;
                }
                else
                {
                    state.DefineVar(kvp.Key, kvp.Value);
                }
            }
            
            // Register namespaces
            state.DefineVar("Console", namespaces.ContainsKey("Console") ? namespaces["Console"] : new DictValue());
            state.DefineVar("File", namespaces.ContainsKey("File") ? namespaces["File"] : new DictValue());
            state.DefineVar("Dir", namespaces.ContainsKey("Dir") ? namespaces["Dir"] : new DictValue());
            state.DefineVar("GC", namespaces.ContainsKey("GC") ? namespaces["GC"] : new DictValue());
            state.DefineVar("OS", namespaces.ContainsKey("OS") ? namespaces["OS"] : new DictValue());
            state.DefineVar("JSON", namespaces.ContainsKey("JSON") ? namespaces["JSON"] : new DictValue());
            state.DefineVar("Memory", namespaces.ContainsKey("Memory") ? namespaces["Memory"] : new DictValue());
            state.DefineVar("CLR", namespaces.ContainsKey("CLR") ? namespaces["CLR"] : new DictValue());
            state.DefineVar("Stopwatch", namespaces.ContainsKey("Stopwatch") ? namespaces["Stopwatch"] : new DictValue());
            state.DefineVar("Gui", namespaces.ContainsKey("Gui") ? namespaces["Gui"] : new DictValue());
            
            if (!namespaces.TryGetValue("Math", out var mathDict))
            {
                mathDict = new DictValue();
                state.DefineVar("Math", mathDict);
            }
            mathDict.Entries[new StringValue("pi")] = new NumberValue(Math.PI);
            mathDict.Entries[new StringValue("e")] = new NumberValue(Math.E);

            if (!namespaces.TryGetValue("Memory", out var memoryDict))
            {
                memoryDict = new DictValue();
                state.DefineVar("Memory", memoryDict);
            }
            memoryDict.Entries[new StringValue("PROCESS_ALL_ACCESS")] = new NumberValue(Memory.PROCESS_ALL_ACCESS);
            memoryDict.Entries[new StringValue("MEM_COMMIT")] = new NumberValue(Memory.MEM_COMMIT);
            memoryDict.Entries[new StringValue("MEM_RESERVE")] = new NumberValue(Memory.MEM_RESERVE);
            memoryDict.Entries[new StringValue("PAGE_READWRITE")] = new NumberValue(Memory.PAGE_READWRITE);
            memoryDict.Entries[new StringValue("PAGE_EXECUTE_READWRITE")] = new NumberValue(Memory.PAGE_EXECUTE_READWRITE);
        }
    }
}
