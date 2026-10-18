"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin.

Also checks that build/xlua_exports.txt, the export list of -DXLUA_EXPORTS=managed,
still matches LuaDLL.cs, and that each optional part of the library (the CMake
options XLUA_MEMORY_SNAPSHOT, XLUA_LEAK_CHECKER, XLUA_UINT64, XLUA_LUASOCKET,
XLUA_MIME and the libraries of XLUA_LIBS) is there with all of its exports or
with none. XLUA_EXPECT_<option> (XLUA_EXPECT_MEMORY_SNAPSHOT=OFF,
XLUA_EXPECT_LEAK_CHECKER=ON ...), XLUA_EXPECT_NATIVE_MODULES and
XLUA_EXPECT_LIBS say how the library was built; unset, either is fine. A bridge
library built with XLUA_LUASCRIPT=ON (it exports xlua_core_open) must also export
every function of build/xlua_core_exports.txt. XLUA_EXPECT_BYTECODE_ONLY=ON
(CMake XLUA_BYTECODE_ONLY) fails a library that contains Lua's lexer or parser,
OFF one without them.

--preset core checks a library built with XLUA_PRESET=core instead
(docs/design/core-luascript.md, section 6): it exports the functions of
build/xlua_core_exports.txt, and with XLUA_EXPORTS=managed nothing else (listed
with nm); it contains none of io, os, debug, package, LuaSocket, the bridge or
Lua's bytecode loader (which XLUA_EXPECT_TRUSTED_BYTECODE=ON, for CMake
XLUA_TRUSTED_BYTECODE, requires instead); with every export, its libraries are XLUA_EXPECT_LIBS
(default: coroutine, table, string, math and utf8).

    python3 Test/Native/check_exports.py [--preset core] LIB
"""
import argparse
import ctypes
from pathlib import Path
import platform
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import (CORE_EXPORTS, check_core_library, check_optional_exports,  # noqa: E402
                             check_parser, export_list_problem, managed_imports)


def exported_symbols(path):
    """The names a shared library exports, from nm; None without nm."""
    if platform.system() == "Darwin":
        args = ["nm", "-gUj", str(path)]
    else:
        args = ["nm", "-D", "--defined-only", "--format=posix", str(path)]
    try:
        out = subprocess.run(args, check=True, capture_output=True, text=True).stdout
    except (OSError, subprocess.CalledProcessError):
        return None
    names = set()
    for line in out.splitlines():
        name = line.split()[0] if line.split() else ""
        if platform.system() == "Darwin":
            name = name[1:] if name.startswith("_") else ""
        if name:
            names.add(name.split("@")[0])
    return names


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument("--preset", choices=["legacy", "minimal", "core"], default="legacy",
                    help="the preset the library was built with; only core changes the checks")
parser.add_argument("lib", type=Path)
args = parser.parse_args()
path = args.lib.resolve()
lib = ctypes.CDLL(str(path))

problem = export_list_problem()
assert not problem, problem
if args.preset == "core":
    problems, summary = check_core_library(lambda name: hasattr(lib, name), exported_symbols(path),
                                           path.read_bytes())
    assert not problems, problems
    print("XLUA_EXPORTS_PASS: core preset,", len(CORE_EXPORTS), "core exports,", summary)
    sys.exit(0)

assert not hasattr(lib, "xlua_core_run") or hasattr(lib, "luaopen_xlua"), \
    "a core library (XLUA_PRESET=core): use --preset core"
names = managed_imports()
count = len(names)
luajit = hasattr(lib, "luaJIT_setmode")
problems, absent, summary = check_optional_exports(lambda name: hasattr(lib, name), luajit)
problems += check_parser(path.read_bytes())
assert not problems, problems
print("XLUA_EXPORTS: optional parts:", summary)
missing = [name for name in names if not hasattr(lib, name) and name not in absent]
assert count > 0, "No managed imports found"
assert not missing, missing
if hasattr(lib, "xlua_core_open"):
    missing = [name for name in CORE_EXPORTS if not hasattr(lib, name)]
    assert not missing, ("XLUA_LUASCRIPT: core exports missing", missing)
    print("XLUA_EXPORTS: XLUA_LUASCRIPT, the", len(CORE_EXPORTS), "core exports as well")
print("XLUA_EXPORTS_PASS:", count - len(set(names) & absent), "of", count, "managed imports")
