"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin."""
import ctypes
from pathlib import Path
import re
import sys

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
repo = Path(__file__).resolve().parents[2]
source = (repo / "Assets/XLua/Src/LuaDLL.cs").read_text()
pattern = r'\[DllImport\((.*?)\)\]\s*(?:\[[^\]]+\]\s*)*public static extern [^;]+?\s+(\w+)\s*\('
missing = []
count = 0
for attribute, method in re.findall(pattern, source, re.S):
    if not attribute.startswith("LUADLL"):
        continue
    entry = re.search(r'EntryPoint\s*=\s*"([^"]+)"', attribute)
    name = entry.group(1) if entry else method
    count += 1
    if not hasattr(lib, name):
        missing.append(name)
assert count > 0, "No managed imports found"
assert not missing, missing
print("XLUA_EXPORTS_PASS:", count, "managed imports")
