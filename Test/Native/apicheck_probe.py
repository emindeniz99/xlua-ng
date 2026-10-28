"""Checks that a native plugin built with XLUA_APICHECK has Lua's C API checks.

A child process loads the plugin with ctypes and misuses the API once: on PUC
Lua lua_pushvalue(L, 100) on an empty stack, on LuaJIT lua_rawgeti on a
number. It must abort with the check's message. A second child makes a
correct call and must exit cleanly, so a plugin that cannot load does not
pass as an abort. A plugin without the checks fails: the misuse returns.

    python3 Test/Native/apicheck_probe.py [--luajit] PLUGIN

docs/research/apicheck-runs.md describes the checks; windows-host.yml runs
this for MSVC builds, where XLUA_APICHECK also sends a failed check to stderr
instead of a message box (build/xlua_apicheck_msvc.c).
"""
import argparse
import ctypes
import subprocess
import sys


def child(plugin, case, luajit):
    lib = ctypes.CDLL(plugin)
    lib.luaL_newstate.restype = ctypes.c_void_p
    lib.lua_pushvalue.argtypes = [ctypes.c_void_p, ctypes.c_int]
    lib.lua_pushnumber.argtypes = [ctypes.c_void_p, ctypes.c_double]
    lib.lua_settop.argtypes = [ctypes.c_void_p, ctypes.c_int]
    lib.lua_rawgeti.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int if luajit else ctypes.c_longlong]
    lib.lua_close.argtypes = [ctypes.c_void_p]
    L = lib.luaL_newstate()
    if not L:
        raise SystemExit("luaL_newstate failed")
    if case == "correct":
        lib.lua_pushnumber(L, 1.0)
        lib.lua_pushvalue(L, -1)
        lib.lua_settop(L, 0)
    elif luajit:
        lib.lua_pushnumber(L, 1.0)
        lib.lua_rawgeti(L, -1, 1)
    else:
        lib.lua_pushvalue(L, 100)
    lib.lua_close(L)
    print("returned")
    sys.stdout.flush()


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--luajit", action="store_true", help="the plugin is a LuaJIT build")
    p.add_argument("--child", choices=["correct", "misuse"], help=argparse.SUPPRESS)
    p.add_argument("plugin")
    a = p.parse_args()
    if a.child:
        child(a.plugin, a.child, a.luajit)
        return
    results = {}
    for case in ["correct", "misuse"]:
        cmd = [sys.executable, "-I", __file__, "--child", case] + (["--luajit"] if a.luajit else []) + [a.plugin]
        r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, errors="replace", timeout=60)
        results[case] = r
        print("%s: exit %d, stdout %r, stderr %r" % (case, r.returncode, r.stdout.strip(), r.stderr.strip()[:400]))
    correct, misuse = results["correct"], results["misuse"]
    if correct.returncode != 0 or "returned" not in correct.stdout:
        raise SystemExit("the correct call did not return cleanly")
    # PUC Lua: assert() ("Assertion failed: ..." from MSVC and macOS, "Assertion `...' failed." from glibc).
    # LuaJIT: "LuaJIT ASSERT lj_api.c:...".
    message = "LuaJIT ASSERT" if a.luajit else "ssertion"
    if misuse.returncode == 0 or "returned" in misuse.stdout or message not in misuse.stderr:
        raise SystemExit("the misuse did not fail the API check: the plugin has no XLUA_APICHECK checks")
    print("XLUA_APICHECK_PROBE_PASS: %s aborts on a misused C API call (exit %d)" % (a.plugin, misuse.returncode))


if __name__ == "__main__":
    main()
