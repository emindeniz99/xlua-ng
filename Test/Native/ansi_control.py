"""Control runs for Tencent/xLua#1081 on the .NET Framework (windows-host.yml).

The managed suite passes on the .NET Framework with the UTF-8 imports of
XLUA_GENERAL. This shows that it would not pass without them, so that the run
really exercised the ANSI marshalling: for each file with such imports, it
turns them off (`#if XLUA_GENERAL` becomes a symbol nobody defines, the code
before the fixes), runs the first pass of run_managed.py --runtime netfx, and
restores the file. Each variant must fail its own case and pass the other's:

- Assets/XLua/Src/LuaDLLBridge.cs (chunk names, paths, error messages):
  CaseUtf8StringParameters fails, CaseUtf8TypeNames passes.
- Assets/XLua/Core/LuaDLL.cs (luaL_newmetatable): CaseUtf8TypeNames does not
  pass (it fails, or a Lua API check aborts the run with XLUA_APICHECK),
  CaseUtf8StringParameters passes.
- NATIVE_LUA_PUSHSTRING: the suite compiled with that define, with only the
  UTF-8 lua_pushstring import of LuaDLLBridge.cs off (the `#if XLUA_GENERAL //
  NATIVE_LUA_PUSHSTRING ...` line). C# strings then reach Lua in ANSI:
  CaseUtf8StringParameters does not pass (SetInPath's value), and the same
  import pushes member and metatable names, so other UTF-8 cases fail too.

    python Test/Native/ansi_control.py --compiler CSC.EXE --plugin xlua.dll --output DIR

Windows with the .NET Framework only; the ANSI code page must not be UTF-8.
"""
import argparse
from pathlib import Path
import re
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
STRINGS = "CMyTestCaseLuaCallCSUpstream.CaseUtf8StringParameters"
TYPES = "CMyTestCaseLuaCallCSUpstream.CaseUtf8TypeNames"
# Every UTF-8 import block of a file, or the NATIVE_LUA_PUSHSTRING one alone.
ALL_BLOCKS = rb"^#if XLUA_GENERAL(\r?)$"
PUSHSTRING_BLOCK = rb"^#if XLUA_GENERAL // NATIVE_LUA_PUSHSTRING[^\r\n]*?(\r?)$"
# (name, file, the lines turned off, defines, the case that must not pass, the case that must pass)
VARIANTS = [("LuaDLLBridge", "Assets/XLua/Src/LuaDLLBridge.cs", ALL_BLOCKS, [], STRINGS, TYPES),
            ("LuaDLL", "Assets/XLua/Core/LuaDLL.cs", ALL_BLOCKS, [], TYPES, STRINGS),
            ("NativeLuaPushString", "Assets/XLua/Src/LuaDLLBridge.cs", PUSHSTRING_BLOCK, ["NATIVE_LUA_PUSHSTRING"], STRINGS, None)]


def results(log):
    """The last status of each case in the log: Ok, FAIL or ERROR."""
    return dict(re.findall(r"^\s+(\S+) \.\.\. (Ok|FAIL|ERROR)\s*$", log, re.M))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--compiler", required=True, help="Roslyn csc.exe, as for run_managed.py --runtime netfx")
    p.add_argument("--plugin", required=True)
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    errors = []
    for name, rel, pattern, defines, must_fail, must_pass in VARIANTS:
        source = repo / rel
        original = source.read_bytes()
        changed, n = re.subn(pattern, rb"#if XLUA_GENERAL_UTF8_IMPORTS_OFF\1", original, flags=re.M)
        if n == 0:
            raise SystemExit("%s: no line matches %r" % (rel, pattern))
        out = a.output / name
        try:
            source.write_bytes(changed)
            run = subprocess.run([sys.executable, str(repo / "Test/Native/run_managed.py"), "--runtime", "netfx",
                                  "--compiler", a.compiler, "--plugin", a.plugin, "--output", str(out)]
                                 + ["--define=" + d for d in defines],
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace", timeout=900)
        finally:
            source.write_bytes(original)
        (a.output / (name + ".out")).write_text(run.stdout, encoding="utf-8")
        log_path = out / "managed-suite.log"
        log = log_path.read_text(encoding="utf-8", errors="replace") if log_path.is_file() else ""
        status = results(log)
        failed = sorted(k for k, v in status.items() if v != "Ok")
        aborts = sorted(set(re.findall(r"^(?:Assertion failed:.*|LuaJIT ASSERT.*)$", log, re.M)))
        print("%s: %s with %d UTF-8 import blocks off%s: run_managed.py exit %d"
              % (name, rel, n, "".join(", -define:" + d for d in defines), run.returncode))
        print("  failed: " + (", ".join(failed) or "none"))
        for line in aborts:
            print("  abort: " + line.strip())
        if status.get(must_fail) == "Ok":
            errors.append("%s: %s passed with ANSI imports" % (name, must_fail))
        if must_fail not in status and not aborts:
            errors.append("%s: %s did not run, and nothing aborted" % (name, must_fail))
        if must_pass and status.get(must_pass) != "Ok" and must_pass in status:
            errors.append("%s: %s did not pass (%s)" % (name, must_pass, status.get(must_pass)))
    if errors:
        raise SystemExit("\n".join(errors))
    print("XLUA_ANSI_CONTROL_PASS: each UTF-8 case fails with its imports in ANSI on the .NET Framework")


if __name__ == "__main__":
    main()
