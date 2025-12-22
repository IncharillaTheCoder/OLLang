#include "ollang_c.h"
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <winternl.h>
#include <tchar.h>

// Basic memory operations
void* ollang_alloc(size_t size) {
    if (size == 0) return NULL;
    return malloc(size);
}

void ollang_free(void* ptr) {
    if (ptr) free(ptr);
}

void* ollang_alloc_executable(size_t size) {
    if (size == 0) return NULL;
    return VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
}

BOOL ollang_make_executable(void* ptr, size_t size) {
    if (!ptr || size == 0) return FALSE;
    DWORD old;
    return VirtualProtect(ptr, size, PAGE_EXECUTE_READ, &old);
}

void ollang_free_executable(void* ptr) {
    if (ptr) VirtualFree(ptr, 0, MEM_RELEASE);
}

BOOL ollang_memcpy(void* dest, const void* src, size_t n) {
    if (!dest || !src || n == 0) return FALSE;
    memcpy(dest, src, n);
    return TRUE;
}

BOOL ollang_memset(void* ptr, int value, size_t n) {
    if (!ptr || n == 0) return FALSE;
    memset(ptr, value, n);
    return TRUE;
}

// Memory reading/writing
BOOL ollang_read8(const void* ptr, uint8_t* out) {
    if (!ptr || !out) return FALSE;
    memcpy(out, ptr, sizeof(uint8_t));
    return TRUE;
}

BOOL ollang_read16(const void* ptr, uint16_t* out) {
    if (!ptr || !out) return FALSE;
    memcpy(out, ptr, sizeof(uint16_t));
    return TRUE;
}

BOOL ollang_read32(const void* ptr, uint32_t* out) {
    if (!ptr || !out) return FALSE;
    memcpy(out, ptr, sizeof(uint32_t));
    return TRUE;
}

BOOL ollang_read64(const void* ptr, uint64_t* out) {
    if (!ptr || !out) return FALSE;
    memcpy(out, ptr, sizeof(uint64_t));
    return TRUE;
}

BOOL ollang_write8(void* ptr, uint8_t value) {
    if (!ptr) return FALSE;
    memcpy(ptr, &value, sizeof(uint8_t));
    return TRUE;
}

BOOL ollang_write16(void* ptr, uint16_t value) {
    if (!ptr) return FALSE;
    memcpy(ptr, &value, sizeof(uint16_t));
    return TRUE;
}

BOOL ollang_write32(void* ptr, uint32_t value) {
    if (!ptr) return FALSE;
    memcpy(ptr, &value, sizeof(uint32_t));
    return TRUE;
}

BOOL ollang_write64(void* ptr, uint64_t value) {
    if (!ptr) return FALSE;
    memcpy(ptr, &value, sizeof(uint64_t));
    return TRUE;
}

// PROCESS MANIPULATION

unsigned long ollang_find_process_id(const char* process_name) {
    HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnapshot == INVALID_HANDLE_VALUE) return 0;

    PROCESSENTRY32 pe;
    pe.dwSize = sizeof(PROCESSENTRY32);

    unsigned long pid = 0;
    if (Process32First(hSnapshot, &pe)) {
        do {
            if (_stricmp((const char*)pe.szExeFile, process_name) == 0) {
                pid = pe.th32ProcessID;
                break;
            }
        } while (Process32Next(hSnapshot, &pe));
    }

    CloseHandle(hSnapshot);
    return pid;
}

void* ollang_open_process(unsigned long pid, unsigned long access) {
    return OpenProcess(access, FALSE, pid);
}

int ollang_close_handle(void* handle) {
    return CloseHandle(handle) ? 1 : 0;
}

// EXTERNAL PROCESS MEMORY

BOOL ollang_read_process_memory(HANDLE process, const void* address, void* buffer, size_t size) {
    if (!process || !address || !buffer || size == 0) return FALSE;
    SIZE_T read;
    return ReadProcessMemory(process, address, buffer, size, &read) && read == size;
}

BOOL ollang_write_process_memory(HANDLE process, void* address, const void* buffer, size_t size) {
    if (!process || !address || !buffer || size == 0) return FALSE;
    SIZE_T written;
    return WriteProcessMemory(process, address, buffer, size, &written) && written == size;
}

void* ollang_alloc_external(HANDLE process, size_t size, uint32_t protection) {
    if (!process || size == 0) return NULL;
    return VirtualAllocEx(process, NULL, size, MEM_COMMIT | MEM_RESERVE, protection);
}

BOOL ollang_free_external(HANDLE process, void* ptr) {
    if (!process || !ptr) return FALSE;
    return VirtualFreeEx(process, ptr, 0, MEM_RELEASE);
}

// DLL INJECTION

BOOL ollang_inject_dll(DWORD pid, const char* dll_path) {
    HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
    if (!hProcess) return FALSE;

    size_t path_len = strlen(dll_path) + 1;
    void* remote_mem = VirtualAllocEx(hProcess, NULL, path_len, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote_mem) {
        CloseHandle(hProcess);
        return FALSE;
    }

    if (!WriteProcessMemory(hProcess, remote_mem, dll_path, path_len, NULL)) {
        VirtualFreeEx(hProcess, remote_mem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    HMODULE hKernel32 = GetModuleHandleA("kernel32.dll");
    LPTHREAD_START_ROUTINE load_library = (LPTHREAD_START_ROUTINE)GetProcAddress(hKernel32, "LoadLibraryA");

    HANDLE hThread = CreateRemoteThread(hProcess, NULL, 0, load_library, remote_mem, 0, NULL);
    if (!hThread) {
        VirtualFreeEx(hProcess, remote_mem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    WaitForSingleObject(hThread, INFINITE);

    DWORD exit_code;
    GetExitCodeThread(hThread, &exit_code);

    CloseHandle(hThread);
    VirtualFreeEx(hProcess, remote_mem, 0, MEM_RELEASE);
    CloseHandle(hProcess);

    return exit_code != 0;
}

// PATTERN SCANNING

size_t ollang_scan_memory(const void* start, size_t size, const uint8_t* pattern, size_t pattern_len) {
    if (!start || !pattern || pattern_len == 0 || size < pattern_len) return 0;

    const uint8_t* mem = (const uint8_t*)start;

    for (size_t i = 0; i + pattern_len <= size; i++) {
        BOOL match = TRUE;
        for (size_t j = 0; j < pattern_len; j++) {
            if (pattern[j] != 0x00 && mem[i + j] != pattern[j]) {
                match = FALSE;
                break;
            }
        }
        if (match) {
            return (size_t)(mem + i);
        }
    }
    return 0;
}

size_t ollang_scan_external(HANDLE process, const void* start, size_t size, const uint8_t* pattern, size_t pattern_len) {
    if (!process || !start || !pattern || pattern_len == 0 || size < pattern_len) return 0;

    uint8_t* buffer = (uint8_t*)malloc(size);
    if (!buffer) return 0;

    SIZE_T read;
    if (!ReadProcessMemory(process, start, buffer, size, &read) || read != size) {
        free(buffer);
        return 0;
    }

    size_t result = ollang_scan_memory(buffer, size, pattern, pattern_len);
    free(buffer);

    if (result) {
        return (size_t)start + (result - (size_t)buffer);
    }
    return 0;
}

// MODULE FUNCTIONS

size_t ollang_get_module_base(HANDLE process, const char* module_name) {
    if (!process) return 0;

    HMODULE modules[1024];
    DWORD needed;

    if (!EnumProcessModules(process, modules, sizeof(modules), &needed)) {
        return 0;
    }

    DWORD count = needed / sizeof(HMODULE);
    char module_path[MAX_PATH];

    for (DWORD i = 0; i < count; i++) {
        if (GetModuleFileNameExA(process, modules[i], module_path, MAX_PATH)) {
            char* base_name = strrchr(module_path, '\\');
            if (base_name) {
                base_name++;
                if (_stricmp(base_name, module_name) == 0) {
                    return (size_t)modules[i];
                }
            }
        }
    }

    return 0;
}

size_t ollang_get_module_size(HANDLE process, HMODULE module) {
    if (!process || !module) return 0;

    MODULEINFO info;
    if (GetModuleInformation(process, module, &info, sizeof(info))) {
        return info.SizeOfImage;
    }
    return 0;
}

// THREAD MANIPULATION

HANDLE ollang_create_thread(void* start_address, void* parameter) {
    if (!start_address) return NULL;
    return CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)start_address, parameter, 0, NULL);
}

HANDLE ollang_create_remote_thread(HANDLE process, void* start_address, void* parameter) {
    if (!process || !start_address) return NULL;
    return CreateRemoteThread(process, NULL, 0, (LPTHREAD_START_ROUTINE)start_address, parameter, 0, NULL);
}

BOOL ollang_suspend_thread(HANDLE thread) {
    if (!thread) return FALSE;
    return SuspendThread(thread) != (DWORD)-1;
}

BOOL ollang_resume_thread(HANDLE thread) {
    if (!thread) return FALSE;
    return ResumeThread(thread) != (DWORD)-1;
}

BOOL ollang_terminate_thread_safe(HANDLE thread, DWORD timeout_ms, DWORD* pExitCode) {
    if (!thread) return FALSE;

    DWORD result = WaitForSingleObject(thread, timeout_ms);
    if (result == WAIT_OBJECT_0) {
        if (pExitCode) {
            GetExitCodeThread(thread, pExitCode);
        }
        CloseHandle(thread);
        return TRUE;
    }
    else if (result == WAIT_TIMEOUT) {
        BOOL terminated = TerminateThread(thread, 0);
        if (terminated && pExitCode) {
            *pExitCode = 0;
        }
        CloseHandle(thread);
        return terminated;
    }

    return FALSE;
}

// HOOKING

BOOL ollang_write_jmp(void* target, void* destination) {
    if (!target || !destination) return FALSE;

    uint8_t jmp_code[5] = { 0xE9, 0x00, 0x00, 0x00, 0x00 };
    uintptr_t relative = (uintptr_t)destination - (uintptr_t)target - 5;
    memcpy(&jmp_code[1], &relative, 4);

    DWORD old_protect;
    if (!VirtualProtect(target, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return FALSE;
    }

    memcpy(target, jmp_code, 5);
    VirtualProtect(target, 5, old_protect, &old_protect);

    return TRUE;
}

BOOL ollang_write_call(void* target, void* destination) {
    if (!target || !destination) return FALSE;

    uint8_t call_code[5] = { 0xE8, 0x00, 0x00, 0x00, 0x00 };
    uintptr_t relative = (uintptr_t)destination - (uintptr_t)target - 5;
    memcpy(&call_code[1], &relative, 4);

    DWORD old_protect;
    if (!VirtualProtect(target, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return FALSE;
    }

    memcpy(target, call_code, 5);
    VirtualProtect(target, 5, old_protect, &old_protect);

    return TRUE;
}

// WINDOW FUNCTIONS

HWND ollang_find_window(const char* class_name, const char* window_name) {
    return FindWindowA(class_name, window_name);
}

BOOL ollang_get_window_text(HWND hwnd, char* buffer, int size) {
    if (!hwnd || !buffer || size <= 0) return FALSE;
    return GetWindowTextA(hwnd, buffer, size) > 0;
}

BOOL ollang_set_window_text(HWND hwnd, const char* text) {
    if (!hwnd || !text) return FALSE;
    return SetWindowTextA(hwnd, text);
}

DWORD ollang_get_window_process_id(HWND hwnd) {
    if (!hwnd) return 0;
    DWORD pid;
    GetWindowThreadProcessId(hwnd, &pid);
    return pid;
}

// KEYBOARD/MOUSE INPUT

BOOL ollang_send_key(int vk_code, BOOL pressed) {
    INPUT input = { 0 };
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = vk_code;
    input.ki.dwFlags = pressed ? 0 : KEYEVENTF_KEYUP;

    return SendInput(1, &input, sizeof(INPUT)) == 1;
}

BOOL ollang_send_mouse_click(int x, int y, BOOL right_button) {
    INPUT inputs[3] = { 0 };

    // Move mouse
    inputs[0].type = INPUT_MOUSE;
    inputs[0].mi.dx = x * (65535 / GetSystemMetrics(SM_CXSCREEN));
    inputs[0].mi.dy = y * (65535 / GetSystemMetrics(SM_CYSCREEN));
    inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

    // Mouse down
    inputs[1].type = INPUT_MOUSE;
    inputs[1].mi.dwFlags = right_button ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;

    // Mouse up
    inputs[2].type = INPUT_MOUSE;
    inputs[2].mi.dwFlags = right_button ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

    return SendInput(3, inputs, sizeof(INPUT)) == 3;
}

// SYSTEM INFORMATION

BOOL ollang_get_system_info(SYSTEM_INFO* info) {
    if (!info) return FALSE;
    GetSystemInfo(info);
    return TRUE;
}

BOOL ollang_get_memory_info(size_t* total, size_t* used) {
    MEMORYSTATUSEX m;
    m.dwLength = sizeof(m);
    if (!GlobalMemoryStatusEx(&m)) return FALSE;
    if (total) *total = (size_t)m.ullTotalPhys;
    if (used) *used = (size_t)(m.ullTotalPhys - m.ullAvailPhys);
    return TRUE;
}

// DEBUGGING

BOOL ollang_is_debugger_present(void) {
    return IsDebuggerPresent();
}

BOOL ollang_output_debug_string(const char* str) {
    if (!str) return FALSE;
    OutputDebugStringA(str);
    return TRUE;
}

BOOL ollang_get_last_error(char* buffer, size_t size) {
    if (!buffer || size == 0) return FALSE;
    DWORD err = GetLastError();
    return FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, err,
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), buffer, (DWORD)size, NULL) != 0;
}

// SYSCALL EXTENSIONS

uint64_t ollang_syscall(uint64_t num, uint64_t a1, uint64_t a2, uint64_t a3,
    uint64_t a4, uint64_t a5, uint64_t a6) {
    switch (num) {
        // Basic system info
    case 0x001: return GetCurrentProcessId();
    case 0x002: return GetCurrentThreadId();
    case 0x003: Sleep((DWORD)a1); return 0;
    case 0x004: return GetTickCount64();
    case 0x005: return (uint64_t)GetModuleHandleA(NULL);

        // Memory operations
    case 0x006: return (uint64_t)VirtualAlloc((void*)a1, (SIZE_T)a2, MEM_COMMIT | MEM_RESERVE, (DWORD)a3);
    case 0x007: return VirtualFree((void*)a1, 0, MEM_RELEASE) ? 1 : 0;

        // Process/Module operations
    case 0x008: return (uint64_t)GetProcAddress(GetModuleHandleA(NULL), (const char*)a1);
    case 0x009: return ollang_find_process_id((const char*)a1);
    case 0x00A: return (uint64_t)ollang_open_process((DWORD)a1, (DWORD)a2);

        // External memory operations
    case 0x00B: {
        HANDLE hProcess = (HANDLE)a1;
        void* addr = (void*)a2;
        void* buffer = (void*)a3;
        size_t size = (size_t)a4;
        return ollang_read_process_memory(hProcess, addr, buffer, size) ? 1 : 0;
    }
    case 0x00C: {
        HANDLE hProcess = (HANDLE)a1;
        void* addr = (void*)a2;
        void* buffer = (void*)a3;
        size_t size = (size_t)a4;
        return ollang_write_process_memory(hProcess, addr, buffer, size) ? 1 : 0;
    }

              // DLL Injection
    case 0x00D: return ollang_inject_dll((DWORD)a1, (const char*)a2) ? 1 : 0;

        // Pattern scanning
    case 0x00E: {
        HANDLE hProcess = (HANDLE)a1;
        void* start = (void*)a2;
        size_t size = (size_t)a3;
        uint8_t* pattern = (uint8_t*)a4;
        size_t pattern_len = (size_t)a5;
        return ollang_scan_external(hProcess, start, size, pattern, pattern_len);
    }

              // Window operations
    case 0x00F: return (uint64_t)ollang_find_window((const char*)a1, (const char*)a2);
    case 0x010: return ollang_get_window_process_id((HWND)a1);

        // Input simulation
    case 0x011: return ollang_send_key((int)a1, (BOOL)a2) ? 1 : 0;
    case 0x012: return ollang_send_mouse_click((int)a1, (int)a2, (BOOL)a3) ? 1 : 0;

        // Hooking
    case 0x013: return ollang_write_jmp((void*)a1, (void*)a2) ? 1 : 0;
    case 0x014: return ollang_write_call((void*)a1, (void*)a2) ? 1 : 0;

    default: return 0xC0000000;
    }
}

// VIRTUAL MEMORY OPERATIONS

void* ollang_virtual_alloc(size_t size, uint32_t protection) {
    if (size == 0) return NULL;
    return VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, protection);
}

BOOL ollang_virtual_free(void* ptr) {
    if (!ptr) return FALSE;
    return VirtualFree(ptr, 0, MEM_RELEASE);
}

BOOL ollang_virtual_protect(void* ptr, size_t size, uint32_t new_protect, uint32_t* old_protect) {
    if (!ptr || size == 0 || !old_protect) return FALSE;
    return VirtualProtect(ptr, size, new_protect, (DWORD*)old_protect);
}

DWORD ollang_get_process_id(void) {
    return GetCurrentProcessId();
}

DWORD ollang_get_thread_id(void) {
    return GetCurrentThreadId();
}

void ollang_sleep(uint32_t ms) {
    Sleep(ms);
}

uint64_t ollang_get_tick_count(void) {
    return GetTickCount64();
}