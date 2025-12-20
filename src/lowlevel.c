#include "ollang_c.h"
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <psapi.h>

void* ollang_alloc(size_t size) {
    if (size == 0) return NULL;
    return malloc(size);
}

void ollang_free(void* ptr) {
    if (ptr) free(ptr);
}

void* ollang_alloc_executable(size_t size) {
    if (size == 0) return NULL;
    return VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
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

uint64_t ollang_syscall(uint64_t num, uint64_t a1, uint64_t a2, uint64_t a3,
    uint64_t a4, uint64_t a5, uint64_t a6) {
    switch (num) {
    case 0x001:
        return GetCurrentProcessId();
    case 0x002:
        return GetCurrentThreadId();
    case 0x003:
        Sleep((DWORD)a1);
        return 0;
    case 0x004:
        return GetTickCount64();
    case 0x005:
        return (uint64_t)GetModuleHandleA(NULL);
    case 0x006:
        return (uint64_t)VirtualAlloc((void*)a1, (SIZE_T)a2, MEM_COMMIT | MEM_RESERVE, (DWORD)a3);
    case 0x007:
        return VirtualFree((void*)a1, 0, MEM_RELEASE) ? 1 : 0;
    case 0x008:
        return (uint64_t)GetProcAddress(GetModuleHandleA(NULL), (const char*)a1);
    case 0x009:
        OutputDebugStringA((const char*)a1);
        return 0;
    case 0x00A:
        return (uint64_t)CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)a1, (void*)a2, 0, NULL);
    default:
        return 0xC0000000;
    }
}

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

size_t ollang_scan_memory(const void* start, size_t size, const uint8_t* pattern, size_t pattern_len) {
    if (!start || !pattern || pattern_len == 0 || size < pattern_len) return 0;
    const uint8_t* mem = (const uint8_t*)start;
    for (size_t i = 0; i + pattern_len <= size; i++) {
        if (memcmp(mem + i, pattern, pattern_len) == 0) {
            return (size_t)(mem + i);
        }
    }
    return 0;
}

HANDLE ollang_create_thread(void* start_address, void* parameter) {
    if (!start_address) return NULL;
    return CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)start_address, parameter, 0, NULL);
}

BOOL ollang_suspend_thread(HANDLE thread) {
    if (!thread) return FALSE;
    return SuspendThread(thread) != (DWORD)-1;
}

BOOL ollang_resume_thread(HANDLE thread) {
    if (!thread) return FALSE;
    return ResumeThread(thread) != (DWORD)-1;
}

DWORD ollang_get_process_id(void) {
    return GetCurrentProcessId();
}

DWORD ollang_get_thread_id(void) {
    return GetCurrentThreadId();
}

size_t ollang_get_module_base(const char* module_name) {
    if (!module_name) return 0;
    HMODULE module = GetModuleHandleA(module_name);
    return (size_t)module;
}

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

void* ollang_get_proc_address(const char* module, const char* function) {
    if (!module || !function) return NULL;
    HMODULE h = GetModuleHandleA(module);
    if (!h) h = LoadLibraryA(module);
    if (!h) return NULL;
    return (void*)GetProcAddress(h, function);
}

void ollang_sleep(uint32_t ms) {
    Sleep(ms);
}

uint64_t ollang_get_tick_count(void) {
    return GetTickCount64();
}

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
    return FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, err, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), buffer, (DWORD)size, NULL) != 0;
}