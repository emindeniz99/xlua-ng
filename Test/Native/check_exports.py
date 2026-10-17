"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin.

Also checks that build/xlua_exports.txt, the export list of -DXLUA_EXPORTS=managed,
still matches LuaDLL.cs.
"""
import ctypes
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import export_list_problem, managed_imports

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
names = managed_imports()
count = len(names)
missing = [name for name in names if not hasattr(lib, name)]
# The memory snapshot walk is a CMake option (XLUA_MEMORY_SNAPSHOT, off by default for
# Android, OpenHarmony, UWP and Switch); its two exports are there together or not at all.
# XLUA_EXPECT_MEMORY_SNAPSHOT=ON or OFF says how the library was built; unset, either is fine.
snapshot = ["xlua_memory_snapshot", "xlua_memory_snapshot_free"]
expect = os.environ.get("XLUA_EXPECT_MEMORY_SNAPSHOT", "").upper()
if all(name in missing for name in snapshot):
    assert expect not in ("ON", "1", "TRUE", "YES"), "built without the memory snapshot: " + ", ".join(snapshot)
    missing = [name for name in missing if name not in snapshot]
    print("XLUA_EXPORTS: built without the memory snapshot (XLUA_MEMORY_SNAPSHOT=OFF)")
else:
    assert expect not in ("OFF", "0", "FALSE", "NO"), "built with the memory snapshot"
assert count > 0, "No managed imports found"
assert not missing, missing
problem = export_list_problem()
assert not problem, problem
print("XLUA_EXPORTS_PASS:", count, "managed imports")
