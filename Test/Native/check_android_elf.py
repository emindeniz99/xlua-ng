"""Verify Android libxlua.so files with the NDK's llvm-readelf.

For every library: ELF class and machine match the ABI, every PT_LOAD segment is aligned
to at least 16 KB (Android 15+ 16 KB-page devices), and the dynamic symbol table defines
every function the managed side imports (parsed from Assets/XLua/Core and LuaDLLBridge.cs by managed_imports.py).
The summary shows the AArch64 feature property (BTI, PAC) when the library has one;
--expect-branch-protection fails an arm64-v8a library without both, as a build with
-DXLUA_HARDEN=ON must have them. A LuaJIT library needs only BTI: LuaJIT's interpreter
supports PAC only on arm64e (luajit-2.1/src/lj_arch.h), so its object carries a
BTI-only note and the linker drops PAC from the library's note, although the C code
still signs its return addresses.

--managed-exports fails a library that exports anything besides the managed imports
(build/xlua_exports.txt), as a build with -DXLUA_EXPORTS=managed must; --extra names
the XLUA_EXPORTS_EXTRA functions it may export as well.

The optional parts of the library (CMake XLUA_MEMORY_SNAPSHOT, XLUA_LEAK_CHECKER,
XLUA_UINT64, XLUA_LUASOCKET, XLUA_MIME, XLUA_LIBS) must be there with all of their
exports or with none, and as XLUA_EXPECT_<option>, XLUA_EXPECT_NATIVE_MODULES and
XLUA_EXPECT_LIBS say when they are set (managed_imports.check_optional_exports). The
managed imports of a part that is not built are not required. XLUA_EXPECT_LOADLIB=OFF
(CMake XLUA_LOADLIB) requires that a PUC Lua library does not import dlopen, ON that
it does (managed_imports.check_loadlib_imports). XLUA_EXPECT_BYTECODE_ONLY=ON
(CMake XLUA_BYTECODE_ONLY) fails a library that contains Lua's lexer or parser or lacks
LuaEnv's precompiled built-in chunks, OFF one without the parser or with the chunks.

--preset core checks a library built with XLUA_PRESET=core instead of the managed imports:
its exports are exactly build/xlua_core_exports.txt (with XLUA_EXPORTS=managed, the core
preset's default on Android), and it contains none of io, os, debug, package, LuaSocket,
the bridge or Lua's bytecode loader (managed_imports.check_core_library; with
XLUA_EXPECT_TRUSTED_BYTECODE=ON, for CMake XLUA_TRUSTED_BYTECODE, the loader must be there).

usage: check_android_elf.py [--readelf PATH] [--expect-branch-protection] [--managed-exports [--extra NAME,...]] [--preset core] LIB...
The ABI is the name of the library's parent directory (.../libs/<abi>/libxlua.so).
The readelf defaults to llvm-readelf from ANDROID_NDK, ANDROID_NDK_HOME or ANDROID_NDK_ROOT.
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import (CORE_EXPORTS, check_core_library, check_loadlib_imports,  # noqa: E402
                             check_optional_exports, check_parser, managed_imports)

MIN_ALIGN = 0x4000
EXPECTED = {  # ABI -> (ELF class, readelf "Machine" text)
    "armeabi-v7a": ("ELF32", "ARM"),
    "arm64-v8a": ("ELF64", "AArch64"),
    "x86": ("ELF32", "Intel 80386"),
    "x86_64": ("ELF64", "Advanced Micro Devices X86-64"),
}


def find_readelf(explicit):
    if explicit:
        return explicit
    for var in ("ANDROID_NDK", "ANDROID_NDK_HOME", "ANDROID_NDK_ROOT"):
        root = os.environ.get(var)
        if root:
            found = sorted(Path(root).glob("toolchains/llvm/prebuilt/*/bin/llvm-readelf"))
            if found:
                return str(found[0])
    sys.exit("llvm-readelf not found: set ANDROID_NDK or pass --readelf")


def readelf(tool, *args):
    return subprocess.run([tool, "--wide", *args], check=True, capture_output=True, text=True).stdout


def check(tool, lib, required, expect_branch_protection=False, managed_exports=None, core=False):
    abi = lib.parent.name
    problems = []
    if abi not in EXPECTED:
        return [f"parent directory {abi!r} is not an Android ABI name"], ""
    elf_class, machine = EXPECTED[abi]

    header = readelf(tool, "-h", str(lib))
    got_class = re.search(r"^\s*Class:\s*(\S+)", header, re.M).group(1)
    got_machine = re.search(r"^\s*Machine:\s*(.+?)\s*$", header, re.M).group(1)
    got_type = re.search(r"^\s*Type:\s*(\S+)", header, re.M).group(1)
    if got_class != elf_class or got_machine != machine:
        problems.append(f"expected {elf_class} {machine}, got {got_class} {got_machine}")
    if got_type != "DYN":
        problems.append(f"expected a shared object (DYN), got {got_type}")

    aligns = [int(m.group(1), 16) for m in re.finditer(r"^\s*LOAD\b.*\s(0x[0-9a-fA-F]+)\s*$", readelf(tool, "-l", str(lib)), re.M)]
    if not aligns:
        problems.append("no PT_LOAD segments found")
    elif min(aligns) < MIN_ALIGN:
        problems.append("PT_LOAD alignment " + ", ".join(hex(a) for a in aligns) + " is below 0x4000")

    defined = set()
    exported = set()
    imported = set()
    luajit = False
    for line in readelf(tool, "--dyn-syms", str(lib)).splitlines():
        cols = line.split()
        # Num: Value Size Type Bind Vis Ndx Name
        if len(cols) >= 8 and cols[0].endswith(":") and cols[3] == "FUNC" and cols[4] in ("GLOBAL", "WEAK") and cols[6] != "UND":
            defined.add(cols[7].split("@")[0])
        if len(cols) >= 8 and cols[0].endswith(":") and cols[4] in ("GLOBAL", "WEAK") and cols[6] != "UND":
            exported.add(cols[7].split("@")[0])
        if len(cols) >= 8 and cols[0].endswith(":") and cols[6] == "UND":
            imported.add(cols[7].split("@")[0])
        if len(cols) >= 8 and cols[6] != "UND" and cols[7].split("@")[0] == "luaJIT_setmode":
            luajit = True
    if core:
        optional_problems, optional_summary = check_core_library(lambda name: name in exported, exported, lib.read_bytes())
        absent = set()
    else:
        optional_problems, absent, optional_summary = check_optional_exports(lambda name: name in exported, luajit)
        # LuaJIT's lib_package.c always imports dlopen; with managed exports luaJIT_setmode is
        # not exported, so look for LuaJIT's version string as well.
        loadlib_problems, loadlib_summary = check_loadlib_imports(imported, luajit or b"LuaJIT 2." in lib.read_bytes())
        optional_problems += loadlib_problems
        if loadlib_summary:
            optional_summary += ", " + loadlib_summary
        optional_problems += check_parser(lib.read_bytes())
    problems.extend(optional_problems)
    missing = sorted(set(required) - defined - absent)
    if missing:
        problems.append(f"{len(missing)} managed imports not exported: {', '.join(missing[:10])}")
    if managed_exports is not None:
        extra = sorted(exported - managed_exports)
        if extra:
            problems.append(f"{len(extra)} exports besides the managed imports: {', '.join(extra[:10])}")

    # A GNU property note marks the whole library BTI- and PAC-enabled only when every
    # object file linked into it was built with -mbranch-protection.
    feature = re.search(r"aarch64 feature: (.+?)\s*$", readelf(tool, "-n", str(lib)), re.M)
    features = {f.strip() for f in feature.group(1).split(",")} if feature else set()
    wanted = {"BTI"} if luajit else {"BTI", "PAC"}
    if expect_branch_protection and abi == "arm64-v8a" and not wanted <= features:
        problems.append("no aarch64 %s property note (-mbranch-protection=standard%s)"
                        % (" and ".join(sorted(wanted)), ", LUAJIT_ENABLE_CET_BR" if luajit else ""))

    needed = re.findall(r"\(NEEDED\)\s+Shared library: \[(.+?)\]", readelf(tool, "-d", str(lib)))
    summary = f"{abi}: {got_class} {got_machine}, PT_LOAD align {', '.join(hex(a) for a in aligns)}, NEEDED {' '.join(needed)}"
    if features:
        summary += f", aarch64 feature {', '.join(sorted(features))}"
    if luajit:
        summary += ", LuaJIT"
    summary += f", {len(exported)} exports, optional parts: {optional_summary}"
    return problems, summary


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--readelf")
    parser.add_argument("--expect-branch-protection", action="store_true",
                        help="fail arm64-v8a libraries without the BTI and PAC property note")
    parser.add_argument("--managed-exports", action="store_true",
                        help="fail libraries that export more than the managed imports (XLUA_EXPORTS=managed)")
    parser.add_argument("--extra", default="", help="with --managed-exports: comma-separated XLUA_EXPORTS_EXTRA names")
    parser.add_argument("--preset", choices=["legacy", "minimal", "core"], default="legacy",
                        help="core: check the core preset's exports instead of the managed imports")
    parser.add_argument("libs", nargs="+", type=Path)
    args = parser.parse_args()
    tool = find_readelf(args.readelf)
    core = args.preset == "core"
    required = sorted(CORE_EXPORTS) if core else sorted(set(managed_imports({"UNITY_ANDROID"})))
    allowed = set(managed_imports()) | {n for n in args.extra.split(",") if n} if args.managed_exports and not core else None
    failed = False
    for lib in args.libs:
        problems, summary = check(tool, lib.resolve(), required, args.expect_branch_protection, allowed, core)
        status = "FAIL" if problems else "PASS"
        failed |= bool(problems)
        print(f"{status} {lib}: {summary} ({len(required)} {'core exports' if core else 'managed imports'})")
        for problem in problems:
            print("  -", problem)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
