"""LuaEnv on a bytecode-only native library (CMake XLUA_BYTECODE_ONLY; build/README.md,
"Bytecode-only builds").

The managed suite (run_managed.py) cannot run there: its C# tests and Lua files
are text. This runs Test/Native/BytecodeOnlyLuaEnv.cs instead, with the scripts of
Test/Native/bytecode_luaenv/ precompiled by --compiler-plugin (a library of the
same Lua version and bytecode format that has the compiler, such as the default
build): new LuaEnv() with the precompiled init_xlua, the CS.* bridge, pairs over
C# collections, a Lua function as a delegate, xlua.util, perf.profiler and
xlua.memory, LuaOptions.Libraries, and the define-dependent arguments of
init_xlua. Each mode compiles xLua (General/vs2013/XLua.Mini.csproj) with Mono's
mcs and the test with mcs or, with generated bindings, with the .NET SDK's csc
and the xLua source generator (source_gen_build.py):

  reflection    no generated code;
  source-gen    the source generator's wrapper and bridge for the test's
                [LuaCallCSharp] and [CSharpCallLua] types;
  source-gen-minimize  the same with GEN_CODE_MINIMIZE;
  locked        XLUA_LOCK_METATABLES, XLUA_LOCK_XLUA_TABLE and
                XLUA_RESTRICT_PRIVATE_ACCESS, which change init_xlua's arguments.

(gen-code and gen-minimize, the legacy generator's modes, went with it in
source generator milestone M11.)

    python3 Test/Native/bytecode_luaenv.py --plugin <bytecode-only libxlua> \\
        --compiler-plugin <default libxlua> --output /tmp/bytecode-luaenv

XLUA_EXPECT_BYTECODE_MODULES names the embedded modules the library was built with
(CMake XLUA_BYTECODE_UTIL, XLUA_BYTECODE_PROFILER, XLUA_BYTECODE_MEMORY), default
"util,profiler,memory"; require of the others must fail with the library's message.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Test/Native"))
import bytecode_only  # noqa: E402

MODES = {
    "reflection": ([], False),
    "source-gen": ([], True),
    "source-gen-minimize": (["GEN_CODE_MINIMIZE"], True),
    "locked": (["XLUA_LOCK_METATABLES", "XLUA_LOCK_XLUA_TABLE", "XLUA_RESTRICT_PRIVATE_ACCESS"], False),
}


def sources(name):
    project = REPO / "General/vs2013" / (name + ".csproj")
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--plugin", type=Path, required=True, help="the bytecode-only library")
    p.add_argument("--compiler-plugin", type=Path, required=True,
                   help="a library with the compiler, same Lua version and bytecode format")
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--mono", default="mono")
    p.add_argument("--compiler", default="mcs")
    p.add_argument("--mode", action="append", choices=sorted(MODES), help="default: all")
    a = p.parse_args()
    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)

    scripts = out / "scripts"
    scripts.mkdir(exist_ok=True)
    compile_ = bytecode_only.Compiler(a.compiler_plugin)
    try:
        for script in sorted((REPO / "Test/Native/bytecode_luaenv").glob("*.lua")):
            code = compile_(script.read_bytes(), b"@" + script.name.encode())
            (scripts / (script.stem + ".luac")).write_bytes(code)
    finally:
        compile_.close()

    compiler = [a.compiler]
    builder = None
    failed = []
    for mode in a.mode or list(MODES):
        defines, generated = MODES[mode]
        defines = ["XLUA_GENERAL", "XLUA_MEMORY_DIAGNOSTICS"] + defines
        folder = out / mode
        shutil.rmtree(folder, ignore_errors=True)
        folder.mkdir()
        flags = ["-unsafe"] + ["-define:" + d for d in defines]
        mini = folder / "XLua.Mini.dll"
        subprocess.run(compiler + ["-target:library"] + flags + ["-out:" + str(mini)] + sources("XLua.Mini"), check=True)
        test_sources = [str(REPO / "Test/Native/BytecodeOnlyLuaEnv.cs")]
        exe = folder / "BytecodeOnlyLuaEnv.exe"
        if generated:
            # The test's [LuaCallCSharp] and [CSharpCallLua] types get the source generator's bindings.
            if builder is None:
                import source_gen_build
                builder = source_gen_build.Builder(out / "source-gen-build", a.mono)
            builder.compile(exe, test_sources, refs=[mini], defines=defines + ["XLUA_BYTECODE_TEST_GEN"])
            if not list(builder.generated(exe).rglob("W_BytecodeOnlyTarget_*.g.cs")):
                raise SystemExit("the source generator wrote no wrapper for BytecodeOnlyTarget")
        else:
            subprocess.run(compiler + flags + ["-r:" + str(mini), "-out:" + str(exe)] + test_sources, check=True)
        shutil.copy2(a.plugin, folder / a.plugin.name)
        env = dict(os.environ)
        for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
            env[key] = str(folder) + os.pathsep + env.get(key, "")
        modules = os.environ.get("XLUA_EXPECT_BYTECODE_MODULES", "util,profiler,memory")
        result = subprocess.run([a.mono, str(exe), str(scripts), modules], cwd=folder, env=env, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True, errors="backslashreplace", timeout=600)
        (folder / "test.log").write_text(result.stdout)
        print("== " + mode)
        print(result.stdout)
        if result.returncode != 0 or "XLUA_BYTECODE_LUAENV_PASS" not in result.stdout:
            failed.append(mode)
    if failed:
        raise SystemExit("bytecode_luaenv: failed in " + ", ".join(failed))
    print("XLUA_BYTECODE_LUAENV_PASS: " + ", ".join(a.mode or list(MODES)))


if __name__ == "__main__":
    main()
