"""Verify that managed LuaDLL imports resolve in a host-native xLua plugin."""
from pathlib import Path
import re
import sys

REPO = Path(__file__).resolve().parents[2]


def managed_imports():
    """Names of the native functions Assets/XLua/Src/LuaDLL.cs imports from the xLua plugin."""
    source = (REPO / "Assets/XLua/Src/LuaDLL.cs").read_text()
    pattern = r'\[DllImport\((.*?)\)\]\s*(?:\[[^\]]+\]\s*)*public static extern [^;]+?\s+(\w+)\s*\('
    names = []
    for attribute, method in re.findall(pattern, source, re.S):
        if not attribute.startswith("LUADLL"):
            continue
        entry = re.search(r'EntryPoint\s*=\s*"([^"]+)"', attribute)
        names.append(entry.group(1) if entry else method)
    assert names, "No managed imports found"
    return names


def main():
    import ctypes

    lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
    names = managed_imports()
    missing = [name for name in names if not hasattr(lib, name)]
    assert not missing, missing
    print("XLUA_EXPORTS_PASS:", len(names), "managed imports")


if __name__ == "__main__":
    main()
