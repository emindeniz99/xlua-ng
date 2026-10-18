"""LuaEnv on a bytecode-only native library (CMake XLUA_BYTECODE_ONLY; build/README.md,
"Bytecode-only builds").

The managed suite (run_managed.py) cannot run there: its C# tests and Lua files
are text. This runs Test/Native/BytecodeOnlyLuaEnv.cs instead, with the scripts of
Test/Native/bytecode_luaenv/ precompiled by --compiler-plugin (a library of the
same Lua version and bytecode format that has the compiler, such as the default
build): new LuaEnv() with the precompiled init_xlua, the CS.* bridge, pairs over
C# collections, a Lua function as a delegate, xlua.util, perf.profiler and
xlua.memory, LuaOptions.Libraries, and the define-dependent arguments of
init_xlua. Each mode compiles xLua (General/vs2013/XLua.Mini.csproj) and the test
with Mono's mcs:

  reflection    no generated code;
  gen-code      with XLuaGenerate's wrappers (generated with --compiler-plugin,
                since XLuaGenerate runs Lua text);
  gen-minimize  the same with GEN_CODE_MINIMIZE;
  locked        XLUA_LOCK_METATABLES, XLUA_LOCK_XLUA_TABLE and
                XLUA_RESTRICT_PRIVATE_ACCESS, which change init_xlua's arguments.

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
    "gen-code": ([], True),
    "gen-minimize": (["GEN_CODE_MINIMIZE"], True),
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
            # XLuaGenerate runs Lua text, so it gets the library with the compiler.
            plain = folder / "plain.exe"
            subprocess.run(compiler + flags + ["-r:" + str(mini), "-out:" + str(plain)] + test_sources, check=True)
            tools = folder / "tools"
            subprocess.run([sys.executable, str(REPO / "Tools/build_tools.py"), "--output", str(tools),
                            "--compiler", a.compiler] + ["--define=" + d for d in defines[1:]], check=True)
            shutil.copy2(a.compiler_plugin, tools / a.plugin.name)
            env = dict(os.environ)
            for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
                env[key] = str(tools) + os.pathsep + env.get(key, "")
            gen = folder / "Gen"
            subprocess.run([a.mono, str(tools / "XLuaGenerate.exe"), str(plain), str(gen) + "/"],
                           cwd=tools, env=env, check=True, timeout=600)
            test_sources += sorted(str(f) for f in gen.glob("*.cs"))
            assert len(test_sources) > 1, "XLuaGenerate wrote no wrappers"
            flags.append("-define:XLUA_BYTECODE_TEST_GEN")
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
