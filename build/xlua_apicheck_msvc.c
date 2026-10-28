/*
 * XLUA_APICHECK test builds with MSVC only (build/CMakeLists.txt).
 *
 * Lua's API checks are assert() (PUC Lua) or lj_assert_fail, which calls
 * abort() (LuaJIT). xlua.dll links the C runtime statically (/MT), and that
 * copy of the runtime never learns that the host is a console program: it
 * shows a failed assert, and abort()'s message, in a message box, which
 * blocks an unattended test run until its timeout. This initializer sends
 * both to stderr and turns off the Windows Error Reporting dialog, so a
 * failed check ends the process at once with the message in the log.
 */
#if defined(_MSC_VER)
#include <stdlib.h>

static void __cdecl xlua_apicheck_report_to_stderr(void)
{
	_set_error_mode(_OUT_TO_STDERR);
	_set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
}

/* The C runtime runs the functions in .CRT$XCU when it starts up the DLL;
 * /include keeps the linker from dropping the unreferenced pointer. */
#pragma section(".CRT$XCU", read)
__declspec(allocate(".CRT$XCU")) void (__cdecl *xlua_apicheck_init)(void) = xlua_apicheck_report_to_stderr;
#if defined(_M_IX86)
#pragma comment(linker, "/include:_xlua_apicheck_init")
#else
#pragma comment(linker, "/include:xlua_apicheck_init")
#endif
#endif
