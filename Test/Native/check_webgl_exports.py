"""Verify that the Unity WebGL object file defines every function C# imports.

    python3 Test/Native/check_webgl_exports.py nm-output.txt

The argument is the output of `llvm-nm --defined-only` for the object or archive
built from Assets/Plugins/WebGL/xlua_webgl.cpp. On WebGL the managed sources
compile with UNITY_WEBGL defined and without UNITY_EDITOR, so imports under
`#if !UNITY_WEBGL` would not be required (there are none since LuaSocket
registers itself from native code).
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import OPTION_EXPORTS, managed_imports

defined = set()
for line in Path(sys.argv[1]).read_text().splitlines():
    parts = line.split()
    # "<address> <type> <name>"; uppercase types are external symbols
    if len(parts) == 3 and parts[1] in "TDBRW":
        defined.add(parts[2])
names = managed_imports({"UNITY_WEBGL"})
assert names, "No managed imports found"
skipped = sorted(set(managed_imports()) - set(names))
# LuaScript's sandbox is not in xlua_webgl.cpp yet: its imports compile only with the
# scripting define XLUA_LUASCRIPT or XLUA_CORE_ONLY, and must be there all or not at all.
sandbox = OPTION_EXPORTS["XLUA_LUASCRIPT"]
have = [name for name in sandbox if name in defined]
assert not have or len(have) == len(sandbox), ("only part of the LuaScript sandbox", have)
if not have:
    names = [name for name in names if name not in sandbox]
missing = [name for name in names if name not in defined]
assert not missing, missing
print("XLUA_WEBGL_EXPORTS_PASS:", len(names), "managed imports defined;",
      "not imported on WebGL:", ", ".join(skipped) or "none")
