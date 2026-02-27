using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Ollang.Packager
{
    public enum BinaryFormat
    {
        PE32,      // 32-bit Windows
        PE64,      // 64-bit Windows
        Raw        // Raw bytecode
    }

    public class ExecutableBuilder
    {
        private byte[] bytecode;
        private BinaryFormat format;
        private string outputPath;
        private bool compressBytecode;
        private bool embedRuntime;
        private bool singleExecutable;
        private bool hiddenWindow;
        private byte[]? iconData;
        private string? versionInfo;
        private byte[]? manifestData;
        private Dictionary<string, byte[]> packedFiles = new();

        public ExecutableBuilder(byte[] bytecode, BinaryFormat format = BinaryFormat.PE64)
        {
            this.bytecode = bytecode;
            this.format = format;
            this.compressBytecode = format != BinaryFormat.Raw;
            this.embedRuntime = true;
            this.singleExecutable = false;
            this.hiddenWindow = false;
        }

        public void SetOutputPath(string path) => outputPath = path;

        public void SetCompression(bool compress) => compressBytecode = compress;

        public void SetEmbedRuntime(bool embed) => embedRuntime = embed;

        public void SetSingleExecutable(bool single) => singleExecutable = single;
        
        public void SetHidden(bool hidden) => hiddenWindow = hidden;

        public void SetIcon(byte[] iconData) => this.iconData = iconData;

        public void SetVersionInfo(string version) => versionInfo = version;

        public void SetManifest(byte[] manifest) => manifestData = manifest;

        public void PackDirectory(string virtualPath, string physicalPath)
        {
            if (!Directory.Exists(physicalPath)) return;
            foreach (var file in Directory.GetFiles(physicalPath, "*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(physicalPath, file);
                string vPath = Path.Combine(virtualPath, relPath).Replace("\\", "/");
                packedFiles[vPath] = File.ReadAllBytes(file);
            }
        }

        public void Build()
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new InvalidOperationException("Output path not set");

            byte[] processedBytecode = compressBytecode ? CompressBytecode(bytecode) : bytecode;
            
            switch (format)
            {
                case BinaryFormat.PE32:
                case BinaryFormat.PE64:
                    BuildPE(processedBytecode);
                    break;
                case BinaryFormat.Raw:
                    BuildRaw(processedBytecode);
                    break;
                default:
                    throw new NotSupportedException($"Format {format} not supported");
            }

            Console.WriteLine($"Executable built: {outputPath}");
        }

        private byte[] CompressBytecode(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }



        private void BuildPE(byte[] processedBytecode)
        {
            if (singleExecutable)
                BuildPESingleExec(processedBytecode);
            else
                BuildPEWithExternalDLL(processedBytecode);
        }

        private void BuildPEWithExternalDLL(byte[] processedBytecode)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string selfPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;

            string sourceExe = Path.Combine(baseDir, "Ollangc.exe");
            if (!File.Exists(sourceExe)) sourceExe = selfPath;

            string sourceDll = Path.Combine(Path.GetDirectoryName(sourceExe)!, "Ollangc.dll");
            string sourceConfig = Path.ChangeExtension(sourceExe, ".runtimeconfig.json");

            File.Copy(sourceExe, outputPath, true);
            string outDir = Path.GetDirectoryName(outputPath)!;
            
            if (File.Exists(sourceDll))
            {
                string targetDll = Path.Combine(outDir, "Ollangc.dll");
                if (Path.GetFullPath(sourceDll).ToLower() != Path.GetFullPath(targetDll).ToLower())
                    File.Copy(sourceDll, targetDll, true);
            }

            if (File.Exists(sourceConfig))
            {
                string targetConfig = Path.Combine(outDir, "Ollangc.runtimeconfig.json");
                if (Path.GetFullPath(sourceConfig).ToLower() != Path.GetFullPath(targetConfig).ToLower())
                    File.Copy(sourceConfig, targetConfig, true);
            }

            AppendBytecode(outputPath, processedBytecode);
        }

        private void AppendBytecode(string path, byte[] data)
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(fs);
            
            long startPos = fs.Position;
            
            // Write packed files
            writer.Write(packedFiles.Count);
            foreach (var kvp in packedFiles)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value.Length);
                writer.Write(kvp.Value);
            }
            
            long endPos = fs.Position;
            int packedBlockLen = (int)(endPos - startPos);

            writer.Write(data);
            writer.Write((byte)(hiddenWindow ? 1 : 0));
            writer.Write(data.Length);
            writer.Write(packedBlockLen);
            writer.Write(Encoding.ASCII.GetBytes("OLLANG_BUNDLE"));
        }
        private void BuildPESingleExec(byte[] processedBytecode)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string[] templatePaths = {
                Path.Combine(baseDir, "template_sf", "Ollangc.exe"),
                Path.Combine(baseDir, "Ollangc.exe")
            };

            string? template = templatePaths.FirstOrDefault(File.Exists);

            if (template == null)
            {
                Console.WriteLine("[Warning] Single-file template not found. Falling back to multi-file logic.");
                BuildPEWithExternalDLL(processedBytecode);
                return;
            }
            File.Copy(template, outputPath, true);
            AppendBytecode(outputPath, processedBytecode);
            Console.WriteLine($"[Info] Standalone executable created using template: {template}");
        }

        private void BuildRaw(byte[] processedBytecode) => File.WriteAllBytes(outputPath, processedBytecode);


    }

    public static class Packager
    {
        public static void CreateExecutable(string bytecodeFile, string outputExe, BinaryFormat format = BinaryFormat.PE64, bool compress = true, bool singleExec = false)
        {
            if (!File.Exists(bytecodeFile))
                throw new FileNotFoundException($"Bytecode file not found: {bytecodeFile}");

            byte[] bytecode = File.ReadAllBytes(bytecodeFile);

            var builder = new ExecutableBuilder(bytecode, format);
            builder.SetOutputPath(outputExe);
            builder.SetCompression(compress);
            builder.SetSingleExecutable(singleExec);

            builder.Build();
        }

        public static void CreateWindowsExecutable(string bytecodeFile, string outputExe, bool singleExec = false, bool x64 = true) =>
            CreateExecutable(bytecodeFile, outputExe, x64 ? BinaryFormat.PE64 : BinaryFormat.PE32, singleExec: singleExec);

        public static void CreateCrossPlatformPackage(string bytecodeFile, string outputDir, bool singleExec = false)
        {
            string baseName = Path.GetFileNameWithoutExtension(bytecodeFile);
            CreateExecutable(bytecodeFile, Path.Combine(outputDir, $"{baseName}.exe"), BinaryFormat.PE64, singleExec: singleExec);
        }

        public static byte[] CreateSelfExtractingArchive(string bytecodeFile, params string[] resourceFiles)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(Encoding.ASCII.GetBytes("OLLANG_SFX"));
                writer.Write((byte)1); // Version

                byte[] bytecode = File.ReadAllBytes(bytecodeFile);
                writer.Write(bytecode.Length);
                writer.Write(bytecode);

                writer.Write(resourceFiles.Length);
                foreach (var file in resourceFiles)
                {
                    if (File.Exists(file))
                    {
                        string name = Path.GetFileName(file);
                        byte[] data = File.ReadAllBytes(file);

                        writer.Write(name);
                        writer.Write(data.Length);
                        writer.Write(data);
                    }
                }

                return ms.ToArray();
            }
        }
    }
}