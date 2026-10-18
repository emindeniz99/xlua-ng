#!/usr/bin/env python3
"""Check the presets (docs/design/presets.md) without building anything.

1. The <xlua-preset> blocks in the C# files match Tools/presets.py (--check).
2. CMake, configured for the host with each preset, sets the native options to the
   values of Tools/presets.py NATIVE: legacy with and without -DXLUA_PRESET, and
   minimal from -D and from the environment, on Lua 5.5.1 and LuaJIT; on macOS also
   minimal for iOS (device and simulator), which keeps the memory snapshot.
3. An option given with -D or in the environment wins over the preset.
4. core configures on Lua 5.5.1 and 5.4.9 with the values of NATIVE; core on
   LuaJIT and Lua 5.3, core with a part of the bridge or a library it cannot
   contain, an unknown preset and a build directory reconfigured with another
   preset stop the configuration with a message. XLUA_LUASCRIPT=ON from the
   environment adds the sandbox to legacy. The bytecode options: XLUA_BYTECODE_ONLY
   configures with the bridge presets on PUC Lua and is refused on LuaJIT and with
   LuaScript's sandbox; XLUA_TRUSTED_BYTECODE configures with the sandbox (core,
   XLUA_LUASCRIPT=ON) and is refused without it. XLUA_BUFFER (LuaScript's buffer
   library) configures with the sandbox, can be turned off in core, and is refused
   without the sandbox. XLUA_CHEAP_HARDEN is on in every preset and puts its flags
   on the right files (compile_commands.json); -D or the environment turns it off.
5. With the .NET SDK and Mono (as run_managed.py --source-gen uses them): the xLua
   assembly (General/vs2013/XLua.Mini.csproj) compiled deterministically with the
   preset define is byte for byte the assembly compiled with the defines it implies.
   --skip-assemblies leaves this out.

    python3 Test/Native/presets_check.py --output /tmp/presets-check
"""
import argparse
import json
import os
import xml.etree.ElementTree as ET
from pathlib import Path
import platform
import shutil
import subprocess
import sys

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools"))
import presets  # noqa: E402

failures = []


def expect(ok, what):
    print(("ok   " if ok else "FAIL ") + what)
    if not ok:
        failures.append(what)


def clean_env(extra=None):
    env = {k: v for k, v in os.environ.items() if not k.startswith("XLUA_")}
    env.update(extra or {})
    return env


def configure(out, name, args, env=None, fresh=True):
    build = out / name
    if fresh:
        shutil.rmtree(build, ignore_errors=True)
    host = ["-DBUILD_SILICON=ON"] if platform.system() == "Darwin" else []
    result = subprocess.run(["cmake", "-S", str(REPO / "build"), "-B", str(build)] + host + args,
                            env=clean_env(env), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=600)
    (out / (name + ".log")).write_text(result.stdout)
    cache = {}
    cache_file = build / "CMakeCache.txt"
    if result.returncode == 0 and cache_file.exists():
        for line in cache_file.read_text().splitlines():
            if line.startswith("XLUA_") and ":" in line and "=" in line:
                key, value = line.split("=", 1)
                cache[key.split(":", 1)[0]] = value
    return result, cache


def resolved(preset, luajit):
    """Tools/presets.py NATIVE for the host."""
    elf_shared = platform.system() == "Linux"
    want = {}
    for option, value in presets.NATIVE[preset].items():
        if option == "XLUA_LIBS_LUAJIT":
            continue
        if option == "XLUA_LIBS" and luajit and "XLUA_LIBS_LUAJIT" in presets.NATIVE[preset]:
            value = presets.NATIVE[preset]["XLUA_LIBS_LUAJIT"]
        if value == "platform":
            value = "ON"  # the memory snapshot of the desktop plugins the Editor loads
        elif value == "apple-static":
            value = "OFF"  # ON only for iOS, tvOS and visionOS static libraries
        elif value == "platform-all":
            value = "all"
        elif value == "platform-managed":
            value = "managed" if elf_shared else "all"
        if option == "XLUA_LEAK_CHECKER" and luajit:
            continue  # CMake turns it off for LuaJIT after the option
        want[option] = value if isinstance(value, str) else ";".join(value)
    return want


def compare(cache, want, what):
    wrong = {k: (cache.get(k), v) for k, v in want.items() if cache.get(k) != v}
    expect(not wrong, what + ("" if not wrong else ": " + ", ".join("%s=%s, expected %s" % (k, a, b) for k, (a, b) in wrong.items())))


def compare_assemblies(out, mono):
    """Step 5: XLua.Mini.dll with XLUA_PRESET_<NAME> against the expanded defines."""
    import build_source_generator
    dotnet = build_source_generator.dotnet_path()
    cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
    found = shutil.which(mono)
    api = Path(found).resolve().parents[1] / "lib/mono/4.5" if found else None
    if not cscs or not api or not (api / "mscorlib.dll").is_file():
        expect(False, "assembly comparison: needs the .NET SDK's csc.dll and Mono's lib/mono/4.5 (or --skip-assemblies)")
        return
    project = REPO / "General/vs2013/XLua.Mini.csproj"
    sources = [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
               for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll"]]

    def build(name, defines):
        # The same file name in each folder: it is the assembly name.
        folder = out / ("assembly-" + name)
        folder.mkdir(exist_ok=True)
        dll = folder / "XLua.Mini.dll"
        rsp = folder / "XLua.Mini.rsp"
        lines = ["-nologo", "-nostdlib", "-unsafe", "-deterministic", "-target:library",
                 "-out:" + str(dll), "-pathmap:" + str(REPO) + "=/xlua", "-define:" + ";".join(["XLUA_GENERAL"] + defines)] \
            + ["-r:" + str(r) for r in refs] + sources
        rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
        result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True, timeout=600)
        (folder / "csc.log").write_text(result.stdout)
        return dll.read_bytes() if result.returncode == 0 else None

    for preset, entries in presets.CSHARP.items():
        with_define = build(preset + "-define", [presets.define(preset)])
        expanded = build(preset + "-expanded", [symbol for symbol, _ in entries])
        legacy = build("legacy", [])
        expect(with_define is not None and expanded is not None and legacy is not None,
               "XLua.Mini compiles with %s, with its defines and without" % presets.define(preset))
        expect(with_define is not None and with_define == expanded,
               "XLua.Mini with %s is byte for byte XLua.Mini with %s" % (presets.define(preset), ", ".join(s for s, _ in entries)))
        expect(with_define is not None and legacy is not None and with_define != legacy,
               "XLua.Mini with %s differs from legacy" % presets.define(preset))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--mono", default="mono")
    p.add_argument("--skip-assemblies", action="store_true", help="leave out step 5 (no .NET SDK or Mono)")
    a = p.parse_args()
    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)

    check = subprocess.run([sys.executable, str(REPO / "Tools/presets.py"), "--check"])
    expect(check.returncode == 0, "Tools/presets.py --check: the C# preset blocks are current")

    luajit = ["-DUSING_LUAJIT=ON", "-DLUAJIT_STATIC_LIB=" + str(out / "libluajit.a")]
    result, default = configure(out, "default", [])
    expect(result.returncode == 0, "configure without a preset")
    compare(default, resolved("legacy", False), "no preset: legacy defaults")
    expect(default.get("XLUA_PRESET") == "legacy", "no preset: XLUA_PRESET=legacy")
    result, legacy = configure(out, "legacy", ["-DXLUA_PRESET=legacy"])
    expect(result.returncode == 0 and legacy == default, "-DXLUA_PRESET=legacy: the same cache as no preset")

    for runtime, extra in (("Lua 5.5.1", []), ("LuaJIT", luajit)):
        is_jit = bool(extra)
        result, cache = configure(out, "minimal" + ("-luajit" if is_jit else ""), ["-DXLUA_PRESET=minimal"] + extra)
        expect(result.returncode == 0, "configure minimal, " + runtime)
        compare(cache, resolved("minimal", is_jit), "minimal, " + runtime)

    # The iOS static libraries keep the memory snapshot in minimal: a Development player's C# imports it
    # and IL2CPP links every __Internal import (an iOS Development player did not link without it).
    if platform.system() == "Darwin" and shutil.which("xcodebuild"):
        for sdk in ("OS64", "SIMULATORARM64"):
            result, cache = configure(out, "minimal-ios-" + sdk.lower(), [
                "-DXLUA_PRESET=minimal", "-DCMAKE_TOOLCHAIN_FILE=" + str(REPO / "build/cmake/ios.toolchain.cmake"),
                "-DPLATFORM=" + sdk, "-DDEPLOYMENT_TARGET=15.0", "-DENABLE_BITCODE=OFF", "-G", "Xcode"])
            expect(result.returncode == 0 and cache.get("XLUA_MEMORY_SNAPSHOT") == "ON" and cache.get("XLUA_LUASOCKET") == "OFF",
                   "minimal for iOS (%s): the memory snapshot stays on, LuaSocket off" % sdk)

    result, from_env = configure(out, "minimal-env", [], {"XLUA_PRESET": "minimal"})
    expect(result.returncode == 0 and from_env.get("XLUA_PRESET") == "minimal", "XLUA_PRESET=minimal from the environment")
    compare(from_env, resolved("minimal", False), "minimal from the environment")

    result, cache = configure(out, "minimal-override", ["-DXLUA_PRESET=minimal", "-DXLUA_LIBS=all", "-DXLUA_EXPORTS=all"],
                              {"XLUA_LUASOCKET": "ON", "XLUA_LEAK_CHECKER": "1", "XLUA_LOADLIB": "ON"})
    expect(result.returncode == 0 and cache.get("XLUA_LUASOCKET") == "ON" and cache.get("XLUA_LEAK_CHECKER") == "ON"
           and cache.get("XLUA_LIBS") == "all" and cache.get("XLUA_EXPORTS") == "all"
           and cache.get("XLUA_LOADLIB") == "ON" and cache.get("XLUA_OS_FULL") == "OFF"
           and cache.get("XLUA_MIME") == "OFF", "minimal with options from -D and the environment: those win, the rest stays")
    result, cache = configure(out, "legacy-override", ["-DXLUA_DOFILE=OFF"],
                              {"XLUA_MEMORY_SNAPSHOT": "OFF", "XLUA_MIME": "OFF", "XLUA_OS_FULL": "OFF"})
    expect(result.returncode == 0 and cache.get("XLUA_MEMORY_SNAPSHOT") == "OFF" and cache.get("XLUA_MIME") == "OFF"
           and cache.get("XLUA_OS_FULL") == "OFF" and cache.get("XLUA_DOFILE") == "OFF" and cache.get("XLUA_LOADLIB") == "ON",
           "legacy with options from -D and the environment")

    # The cheap hardening set (owner, 2026-10-18): on in every preset, off from -D or the
    # environment. compile_commands.json shows where each flag goes: the null-check flag on
    # every C file, the zeroed locals on xLua's own files only.
    null_flag, zero_flag = "-fno-delete-null-pointer-checks", "-ftrivial-auto-var-init=zero"
    for name, args, env, on in [
            ("cheap-legacy", [], None, True),
            ("cheap-minimal-env", [], {"XLUA_PRESET": "minimal"}, True),
            ("cheap-core", ["-DXLUA_PRESET=core"], None, True),
            ("cheap-off", ["-DXLUA_CHEAP_HARDEN=OFF"], None, False),
            ("cheap-off-env", ["-DXLUA_PRESET=core"], {"XLUA_CHEAP_HARDEN": "OFF"}, False)]:
        result, cache = configure(out, name, args + ["-DCMAKE_EXPORT_COMPILE_COMMANDS=ON"], env)
        commands = {}
        db = out / name / "compile_commands.json"
        if result.returncode == 0 and db.exists():
            for entry in json.loads(db.read_text()):
                commands[Path(entry["file"]).name] = entry.get("command") or " ".join(entry.get("arguments", []))
        own = "xlua_core.c" if "core" in name else "xlua.c"
        flags_ok = (own in commands and "lapi.c" in commands
                    and (null_flag in commands[own]) == on and (null_flag in commands["lapi.c"]) == on
                    and (zero_flag in commands[own]) == on and zero_flag not in commands["lapi.c"])
        expect(result.returncode == 0 and cache.get("XLUA_CHEAP_HARDEN") == ("ON" if on else "OFF")
               and ("Cheap hardening: " + ("ON" if on else "OFF")) in result.stdout and flags_ok,
               "XLUA_CHEAP_HARDEN %s: %s (%s and lapi.c flags)" % ("ON" if on else "OFF", name, own))

    for version in ("5.5.1", "5.4.9"):
        result, cache = configure(out, "core-" + version, ["-DXLUA_PRESET=core", "-DLUA_VERSION=" + version])
        expect(result.returncode == 0, "configure core, Lua " + version)
        compare(cache, resolved("core", False), "core, Lua " + version)
    for name, args, env, message in [
            ("core-luajit", ["-DXLUA_PRESET=core"] + luajit, None, "XLUA_PRESET=core needs Lua 5.4 or 5.5"),
            ("core-lua53", ["-DXLUA_PRESET=core", "-DLUA_VERSION=5.3.6"], None, "XLUA_PRESET=core needs Lua 5.4 or 5.5"),
            ("core-socket", ["-DXLUA_PRESET=core"], {"XLUA_LUASOCKET": "ON"}, "XLUA_LUASOCKET must be OFF"),
            ("core-snapshot", ["-DXLUA_PRESET=core", "-DXLUA_MEMORY_SNAPSHOT=ON"], None, "XLUA_MEMORY_SNAPSHOT must be OFF"),
            ("core-io", ["-DXLUA_PRESET=core", "-DXLUA_LIBS=string;io"], None, "the core preset cannot contain io"),
            ("legacy-script-53", ["-DLUA_VERSION=5.3.6", "-DXLUA_LUASCRIPT=ON"], None, "XLUA_LUASCRIPT needs Lua 5.4 or 5.5"),
            # The buffer library (milestone 10) needs the sandbox.
            ("buffer-legacy", ["-DXLUA_BUFFER=ON"], None, "XLUA_BUFFER needs LuaScript's sandbox"),
            ("buffer-minimal", ["-DXLUA_PRESET=minimal"], {"XLUA_BUFFER": "ON"}, "XLUA_BUFFER needs LuaScript's sandbox"),
            # The bytecode options (build/README.md, "Bytecode options").
            ("bytecode-only-luajit", luajit, {"XLUA_BYTECODE_ONLY": "ON"}, "XLUA_BYTECODE_ONLY is not available on LuaJIT"),
            ("bytecode-only-core", ["-DXLUA_PRESET=core", "-DXLUA_BYTECODE_ONLY=ON"], None,
             "XLUA_BYTECODE_ONLY cannot be combined with LuaScript's sandbox"),
            ("bytecode-only-script", ["-DXLUA_LUASCRIPT=ON", "-DXLUA_BYTECODE_ONLY=ON"], None,
             "XLUA_BYTECODE_ONLY cannot be combined with LuaScript's sandbox"),
            ("trusted-legacy", ["-DXLUA_TRUSTED_BYTECODE=ON"], None, "XLUA_TRUSTED_BYTECODE needs LuaScript's sandbox"),
            ("trusted-minimal", ["-DXLUA_PRESET=minimal"], {"XLUA_TRUSTED_BYTECODE": "ON"},
             "XLUA_TRUSTED_BYTECODE needs LuaScript's sandbox")]:
        result, _ = configure(out, name, args, env)
        expect(result.returncode != 0 and message in result.stdout, "refused: " + name)
    result, cache = configure(out, "legacy-script", [], {"XLUA_LUASCRIPT": "ON"})
    expect(result.returncode == 0 and cache.get("XLUA_LUASCRIPT") == "ON", "legacy with XLUA_LUASCRIPT=ON from the environment")
    result, cache = configure(out, "legacy-script-buffer", ["-DXLUA_LUASCRIPT=ON"], {"XLUA_BUFFER": "ON"})
    expect(result.returncode == 0 and cache.get("XLUA_BUFFER") == "ON" and "buffer library: ON" in result.stdout,
           "legacy with XLUA_LUASCRIPT=ON and XLUA_BUFFER=ON from the environment")
    result, cache = configure(out, "core-nobuffer", ["-DXLUA_PRESET=core", "-DXLUA_BUFFER=OFF"])
    expect(result.returncode == 0 and cache.get("XLUA_BUFFER") == "OFF" and "buffer library: OFF" in result.stdout,
           "core with XLUA_BUFFER=OFF")
    for name, args, env, option in [
            ("bytecode-only-legacy", [], {"XLUA_BYTECODE_ONLY": "ON"}, "XLUA_BYTECODE_ONLY"),
            ("bytecode-only-minimal-53", ["-DXLUA_PRESET=minimal", "-DLUA_VERSION=5.3.6", "-DXLUA_BYTECODE_ONLY=ON"], None,
             "XLUA_BYTECODE_ONLY"),
            ("trusted-core", ["-DXLUA_PRESET=core"], {"XLUA_TRUSTED_BYTECODE": "1"}, "XLUA_TRUSTED_BYTECODE"),
            ("trusted-script", ["-DXLUA_LUASCRIPT=ON", "-DXLUA_TRUSTED_BYTECODE=ON"], None, "XLUA_TRUSTED_BYTECODE")]:
        result, cache = configure(out, name, args, env)
        status = "Bytecode only: %s, trusted bytecode: %s" % (
            "ON" if option == "XLUA_BYTECODE_ONLY" else "OFF", "ON" if option == "XLUA_TRUSTED_BYTECODE" else "OFF")
        expect(result.returncode == 0 and cache.get(option) == "ON" and status in result.stdout,
               "%s=ON (from %s): %s" % (option, "the environment" if env else "-D", name))
    # The precompiled modules of a bytecode-only library follow the preset's C# (xlua.util and
    # perf.profiler in legacy, not in minimal) and xlua.memory the memory snapshot; options exist only
    # with XLUA_BYTECODE_ONLY, so the other caches stay as they were.
    for name, args, env, want in [
            ("bytecode-modules-legacy", ["-DXLUA_BYTECODE_ONLY=ON"], None,
             {"XLUA_BYTECODE_UTIL": "ON", "XLUA_BYTECODE_PROFILER": "ON", "XLUA_BYTECODE_MEMORY": "ON"}),
            ("bytecode-modules-minimal", ["-DXLUA_PRESET=minimal", "-DXLUA_BYTECODE_ONLY=ON"], None,
             {"XLUA_BYTECODE_UTIL": "OFF", "XLUA_BYTECODE_PROFILER": "OFF", "XLUA_BYTECODE_MEMORY": "OFF"}),
            ("bytecode-modules-env", ["-DXLUA_PRESET=minimal", "-DXLUA_BYTECODE_ONLY=ON"], {"XLUA_BYTECODE_UTIL": "ON"},
             {"XLUA_BYTECODE_UTIL": "ON", "XLUA_BYTECODE_PROFILER": "OFF"}),
            ("bytecode-modules-off", [], None, {"XLUA_BYTECODE_UTIL": None, "XLUA_CHUNKC": None})]:
        result, cache = configure(out, name, args, env)
        got = {k: cache.get(k) for k in want}
        expect(result.returncode == 0 and got == want, "precompiled modules, %s: %s" % (name, got))
    result, _ = configure(out, "unknown", ["-DXLUA_PRESET=small"])
    expect(result.returncode != 0 and "XLUA_PRESET must be legacy, minimal or core" in result.stdout, "an unknown preset is refused")
    result, _ = configure(out, "default", ["-DXLUA_PRESET=minimal"], fresh=False)
    expect(result.returncode != 0 and "Use a fresh build directory" in result.stdout,
           "reconfiguring a legacy directory with -DXLUA_PRESET=minimal is refused")
    result, _ = configure(out, "legacy", [], {"XLUA_PRESET": "minimal"}, fresh=False)
    expect(result.returncode != 0 and "The environment asks for XLUA_PRESET=minimal" in result.stdout,
           "reconfiguring a legacy directory with XLUA_PRESET=minimal in the environment is refused")
    result, again = configure(out, "minimal-env", [], fresh=False)
    expect(result.returncode == 0 and again.get("XLUA_PRESET") == "minimal",
           "reconfiguring a minimal directory without the variable keeps minimal")

    if not a.skip_assemblies:
        compare_assemblies(out, a.mono)

    print("XLUA_PRESETS_CHECK_PASS" if not failures else "%d failures" % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
