using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace Ollang.Native
{
    public static unsafe class Memory
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)] public static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr baseAddr, byte[] buf, int size, out int read);
        [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr baseAddr, byte[] buf, int size, out int written);
        [DllImport("kernel32.dll")] public static extern bool VirtualProtectEx(IntPtr h, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);
        [DllImport("kernel32.dll")] public static extern int VirtualQueryEx(IntPtr h, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        public const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        public static IntPtr GetModuleBase(int pid, string moduleName)
        {
            using (var proc = Process.GetProcessById(pid))
            {
                foreach (ProcessModule mod in proc.Modules)
                    if (mod.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        return mod.BaseAddress;
            }
            return IntPtr.Zero;
        }

        public static T Read<T>(IntPtr h, IntPtr address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buf = new byte[size];
            ReadProcessMemory(h, address, buf, size, out _);
            fixed (byte* p = buf) return Marshal.PtrToStructure<T>((IntPtr)p);
        }

        public static bool Write<T>(IntPtr h, IntPtr address, T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buf = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(value, ptr, true);
            Marshal.Copy(ptr, buf, 0, size);
            Marshal.FreeHGlobal(ptr);
            return WriteProcessMemory(h, address, buf, size, out _);
        }

        public static IntPtr FollowPointerChain(IntPtr h, IntPtr baseAddr, int[] offsets)
        {
            IntPtr current = baseAddr;
            foreach (int offset in offsets)
                current = (IntPtr)(Read<long>(h, current) + offset);

            return current;
        }

        public static bool Patch(IntPtr h, IntPtr address, byte[] data)
        {
            VirtualProtectEx(h, address, (UIntPtr)data.Length, PAGE_EXECUTE_READWRITE, out uint old);
            bool success = WriteProcessMemory(h, address, data, data.Length, out _);
            VirtualProtectEx(h, address, (UIntPtr)data.Length, old, out _);
            return success;
        }

        public static bool Nop(IntPtr h, IntPtr address, int length)
        {
            byte[] nops = new byte[length];
            for (int i = 0; i < length; i++) nops[i] = 0x90;
            return Patch(h, address, nops);
        }

        public static IntPtr AllocateMemory(IntPtr h, uint size) => VirtualAllocEx(h, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

        public static IntPtr Scan(IntPtr h, string pattern)
        {
            var parts = pattern.Split(' ');
            byte?[] patternBytes = parts.Select(p => p == "?" ? (byte?)null : Convert.ToByte(p, 16)).ToArray();

            long address = 0;
            while (VirtualQueryEx(h, (IntPtr)address, out MEMORY_BASIC_INFORMATION mbi, (uint)sizeof(MEMORY_BASIC_INFORMATION)) != 0)
            {
                if (mbi.State == MEM_COMMIT && (mbi.Protect & 0x100) == 0 && (mbi.Protect & 0x01) == 0)
                {
                    byte[] buffer = new byte[(int)mbi.RegionSize];
                    if (ReadProcessMemory(h, mbi.BaseAddress, buffer, buffer.Length, out int read))
                    {
                        for (int i = 0; i < read - patternBytes.Length; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < patternBytes.Length; j++)
                            {
                                if (patternBytes[j].HasValue && patternBytes[j].Value != buffer[i + j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match) return (IntPtr)((long)mbi.BaseAddress + i);
                        }
                    }
                }
                address = (long)mbi.BaseAddress + (long)mbi.RegionSize;
                if (address >= 0x7FFFFFFFFFFF) break;
            }
            return IntPtr.Zero;
        }
    }
}