"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin.

Also checks that build/xlua_exports.txt, the export list of -DXLUA_EXPORTS=managed,
still matches LuaDLL.cs, and that each optional part of the library (the CMake
options XLUA_MEMORY_SNAPSHOT, XLUA_LEAK_CHECKER, XLUA_UINT64, XLUA_LUASOCKET,
XLUA_MIME and the libraries of XLUA_LIBS) is there with all of its exports or
with none. XLUA_EXPECT_<option> (XLUA_EXPECT_MEMORY_SNAPSHOT=OFF,
XLUA_EXPECT_LEAK_CHECKER=ON ...), XLUA_EXPECT_NATIVE_MODULES and
XLUA_EXPECT_LIBS say how the library was built; unset, either is fine.
"""
import ctypes
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import check_optional_exports, export_list_problem, managed_imports

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
names = managed_imports()
count = len(names)
luajit = hasattr(lib, "luaJIT_setmode")
problems, absent, summary = check_optional_exports(lambda name: hasattr(lib, name), luajit)
assert not problems, problems
print("XLUA_EXPORTS: optional parts:", summary)
missing = [name for name in names if not hasattr(lib, name) and name not in absent]
assert count > 0, "No managed imports found"
assert not missing, missing
problem = export_list_problem()
assert not problem, problem
print("XLUA_EXPORTS_PASS:", count - len(set(names) & absent), "of", count, "managed imports")
