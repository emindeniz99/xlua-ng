"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin."""
import ctypes
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import managed_imports

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
names = managed_imports()
count = len(names)
missing = [name for name in names if not hasattr(lib, name)]
assert count > 0, "No managed imports found"
assert not missing, missing
print("XLUA_EXPORTS_PASS:", count, "managed imports")
