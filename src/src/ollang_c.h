#ifndef OLLANG_C_H
#define OLLANG_C_H

#ifdef __cplusplus
extern "C" {
#endif

#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

	void* ollang_alloc(size_t size);
	void ollang_free(void* ptr);

	void* ollang_alloc_executable(size_t size);
	BOOL ollang_make_executable(void* ptr, size_t size);
	void ollang_free_executable(void* ptr);

	BOOL ollang_memcpy(void* dest, const void* src, size_t n);
	BOOL ollang_memset(void* ptr, int value, size_t n);

	BOOL ollang_read8(const void* ptr, uint8_t* out);
	BOOL ollang_read16(const void* ptr, uint16_t* out);
	BOOL ollang_read32(const void* ptr, uint32_t* out);
	BOOL ollang_read64(const void* ptr, uint64_t* out);

	BOOL ollang_write8(void* ptr, uint8_t value);
	BOOL ollang_write16(void* ptr, uint16_t value);
	BOOL ollang_write32(void* ptr, uint32_t value);
	BOOL ollang_write64(void* ptr, uint64_t value);

	DWORD ollang_find_process_id(const char* process_name);
	HANDLE ollang_open_process(DWORD pid, DWORD access);
	BOOL ollang_close_handle(HANDLE handle);

	BOOL ollang_read_process_memory(HANDLE process, const void* address, void* buffer, size_t size);
	BOOL ollang_write_process_memory(HANDLE process, void* address, const void* buffer, size_t size);
	void* ollang_alloc_external(HANDLE process, size_t size, uint32_t protection);
	BOOL ollang_free_external(HANDLE process, void* ptr);

	BOOL ollang_inject_dll(DWORD pid, const char* dll_path);

	size_t ollang_scan_memory(const void* start, size_t size, const uint8_t* pattern, size_t pattern_len);
	size_t ollang_scan_external(HANDLE process, const void* start, size_t size, const uint8_t* pattern, size_t pattern_len);

	size_t ollang_get_module_base(HANDLE process, const char* module_name);
	size_t ollang_get_module_size(HANDLE process, HMODULE module);

	HANDLE ollang_create_thread(void* start_address, void* parameter);
	HANDLE ollang_create_remote_thread(HANDLE process, void* start_address, void* parameter);
	BOOL ollang_suspend_thread(HANDLE thread);
	BOOL ollang_resume_thread(HANDLE thread);
	BOOL ollang_terminate_thread(HANDLE thread, DWORD exit_code);

	BOOL ollang_write_jmp(void* target, void* destination);
	BOOL ollang_write_call(void* target, void* destination);

	HWND ollang_find_window(const char* class_name, const char* window_name);
	BOOL ollang_get_window_text(HWND hwnd, char* buffer, int size);
	BOOL ollang_set_window_text(HWND hwnd, const char* text);
	DWORD ollang_get_window_process_id(HWND hwnd);

	BOOL ollang_send_key(int vk_code, BOOL pressed);
	BOOL ollang_send_mouse_click(int x, int y, BOOL right_button);

	BOOL ollang_get_system_info(SYSTEM_INFO* info);
	BOOL ollang_get_memory_info(size_t* total, size_t* used);

	BOOL ollang_is_debugger_present(void);
	BOOL ollang_output_debug_string(const char* str);
	BOOL ollang_get_last_error(char* buffer, size_t size);

	uint64_t ollang_syscall(uint64_t num, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6);

	void* ollang_virtual_alloc(size_t size, uint32_t protection);
	BOOL ollang_virtual_free(void* ptr);
	BOOL ollang_virtual_protect(void* ptr, size_t size, uint32_t new_protect, uint32_t* old_protect);

	DWORD ollang_get_process_id(void);
	DWORD ollang_get_thread_id(void);
	void ollang_sleep(uint32_t ms);
	uint64_t ollang_get_tick_count(void);

#ifdef __cplusplus
}
#endif

#endif
