"""Verify that the Unity WebGL object file defines every function C# imports.

    python3 Test/Native/check_webgl_exports.py nm-output.txt

The argument is the output of `llvm-nm --defined-only` for the object or archive
built from Assets/Plugins/WebGL/xlua_webgl.cpp. On WebGL the managed sources
compile with UNITY_WEBGL defined and without UNITY_EDITOR, so imports under
`#if !UNITY_WEBGL` (LuaSocket) are not required.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import managed_imports

defined = set()
for line in Path(sys.argv[1]).read_text().splitlines():
    parts = line.split()
    # "<address> <type> <name>"; uppercase types are external symbols
    if len(parts) == 3 and parts[1] in "TDBRW":
        defined.add(parts[2])
names = managed_imports({"UNITY_WEBGL"})
assert names, "No managed imports found"
skipped = sorted(set(managed_imports()) - set(names))
missing = [name for name in names if name not in defined]
assert not missing, missing
print("XLUA_WEBGL_EXPORTS_PASS:", len(names), "managed imports defined;",
      "not imported on WebGL:", ", ".join(skipped) or "none")
