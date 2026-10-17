"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin.

Also checks that build/xlua_exports.txt, the export list of -DXLUA_EXPORTS=managed,
still matches LuaDLL.cs.
"""
import ctypes
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import export_list_problem, managed_imports

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
names = managed_imports()
count = len(names)
missing = [name for name in names if not hasattr(lib, name)]
assert count > 0, "No managed imports found"
assert not missing, missing
problem = export_list_problem()
assert not problem, problem
print("XLUA_EXPORTS_PASS:", count, "managed imports")
