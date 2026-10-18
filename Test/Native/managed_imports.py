"""List the native functions the managed sources import from the xLua plugin.

Shared by check_exports.py (host plugins) and check_webgl_exports.py (WebGL).
Also knows which exports belong to an optional part of the native library (a
CMake option, OPTION_EXPORTS, or a standard library of XLUA_LIBS, LIB_EXPORTS)
and what a build is expected to contain (the XLUA_EXPECT_* variables), so that
check_exports.py and check_android_elf.py are exact for every configuration.
"""
import os
from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[2]
PATTERN = r'\[DllImport\((.*?)\)\]\s*(?://[^\n]*\s*)*(?:\[[^\]]+\]\s*)*(?:public|internal) static extern [^;]+?\s+(\w+)\s*\('


def _condition(expression, defined, other):
    """Evaluate a C# #if expression of symbols, !, && and ||.

    Platform symbols (UNITY_*) are true only if in `defined`. Any other symbol,
    such as GEN_CODE_MINIMIZE, is a project setting and takes the value `other`.
    """
    def symbol(match):
        name = match.group(0)
        return str(name in defined if name.startswith("UNITY_") else other)

    text = re.sub(r"[A-Za-z_]\w*", symbol, expression)
    text = text.replace("!", " not ").replace("&&", " and ").replace("||", " or ")
    return bool(eval(text, {"__builtins__": {}}, {}))  # only True/False/and/or/not/parens remain


def _active_text(source, defined, other):
    """Source text with the lines of inactive #if branches blanked out."""
    out = []
    stack = []  # (parent_active, branch_taken, active)
    for line in source.splitlines(keepends=True):
        directive = re.match(r"\s*#\s*(if|elif|else|endif)\b(.*)", line)
        if directive:
            kind, rest = directive.group(1), directive.group(2).split("//")[0].strip()
            parent = stack[-1][2] if stack else True
            if kind == "if":
                value = parent and _condition(rest, defined, other)
                stack.append((parent, value, value))
            elif kind == "elif":
                p, taken, _ = stack.pop()
                value = p and not taken and _condition(rest, defined, other)
                stack.append((p, taken or value, value))
            elif kind == "else":
                p, taken, _ = stack.pop()
                stack.append((p, True, p and not taken))
            else:
                stack.pop()
            out.append("\n")
        else:
            out.append(line if all(s[2] for s in stack) else "\n")
    return "".join(out)


def managed_imports(defined=None):
    """Return the entry point names imported through LUADLL in LuaDLL.cs.

    With `defined` (a set of platform symbols such as {"UNITY_WEBGL"}) only
    imports compiled for that platform are returned. Project settings that are
    not platform symbols count as both on and off, so an import is returned if
    any setting compiles it. Without `defined` every import in the file is
    returned.
    """
    source = (REPO / "Assets/XLua/Src/LuaDLL.cs").read_text()
    texts = [source] if defined is None else [_active_text(source, defined, v) for v in (False, True)]
    names = []
    for text in texts:
        for attribute, method in re.findall(PATTERN, text, re.S):
            if not attribute.startswith("LUADLL"):
                continue
            entry = re.search(r'EntryPoint\s*=\s*"([^"]+)"', attribute)
            name = entry.group(1) if entry else method
            if name not in names:
                names.append(name)
    return names


# Exports of the optional parts of the native library, by the CMake option that
# builds them (build/CMakeLists.txt, build/README.md). A part is there with all
# of its exports or with none. Only XLUA_MEMORY_SNAPSHOT has managed imports; the
# others are exported with XLUA_EXPORTS=all only.
OPTION_EXPORTS = {
    "XLUA_MEMORY_SNAPSHOT": ["xlua_memory_snapshot", "xlua_memory_snapshot_free"],
    # Lua 5.3 to 5.5; never on LuaJIT.
    "XLUA_LEAK_CHECKER": ["xlua_report_table_size", "xlua_report_object_relationship",
                          "xlua_registry_pointer", "xlua_global_pointer"],
    # The uint64 table; luaopen_i64lib and the push and get functions always stay.
    "XLUA_UINT64": ["uint64_parse"],
    "XLUA_LUASOCKET": ["luaopen_socket_core"],
    "XLUA_MIME": ["luaopen_mime_core"],
}
# XLUA_LIBS: the luaopen_* function of each standard library, by runtime. LuaJIT's
# ffi is not here: other parts of libluajit.a pull lib_ffi.o in, so luaopen_ffi is
# exported whether or not XLUA_LIBS puts ffi into package.preload.
LIB_EXPORTS = {
    "lua": {name: "luaopen_" + name for name in
            ["package", "coroutine", "table", "io", "os", "string", "math", "utf8", "debug"]},
    "luajit": {name: "luaopen_" + name for name in
               ["package", "table", "io", "os", "string", "math", "debug", "bit", "jit"]},
}
# Exported with XLUA_EXPORTS=all on every runtime, never imported by LuaDLL.cs.
ALL_EXPORTS_MARKERS = ["lua_xmove", "luaL_checkstack"]

EXPORT_LIST = REPO / "build/xlua_exports.txt"
EXPORT_HEADER = """\
# Functions that LuaDLL.cs imports from the native plugin, one per line, on any
# platform. With -DXLUA_EXPORTS=managed, build/CMakeLists.txt exports only
# these and XLUA_EXPORTS_EXTRA. A second column names the CMake option without
# which the function is not built; CMake leaves it out then. Generated by
#   python3 Test/Native/managed_imports.py --write
# check_exports.py fails when this file and LuaDLL.cs disagree.
"""


def option_of(name):
    """The CMake option that builds an export, or None."""
    for option, names in OPTION_EXPORTS.items():
        if name in names:
            return option
    return None


def export_list_text():
    lines = []
    for name in sorted(managed_imports()):
        option = option_of(name)
        lines.append(name + (" " + option if option else "") + "\n")
    return EXPORT_HEADER + "".join(lines)


def _flag(text):
    text = (text or "").strip().upper()
    if text in ("ON", "1", "TRUE", "YES"):
        return True
    if text in ("OFF", "0", "FALSE", "NO"):
        return False
    return None


def expected_options(environ=None):
    """{option: True, False or None} from XLUA_EXPECT_<option without XLUA_> (for
    example XLUA_EXPECT_LEAK_CHECKER=OFF); None when the variable is not set.
    XLUA_EXPECT_NATIVE_MODULES ("socket,mime", "socket" or "") also sets the
    LuaSocket and mime options."""
    environ = os.environ if environ is None else environ
    out = {option: _flag(environ.get("XLUA_EXPECT_" + option[len("XLUA_"):])) for option in OPTION_EXPORTS}
    modules = environ.get("XLUA_EXPECT_NATIVE_MODULES")
    if modules is not None:
        names = {n.strip() for n in modules.split(",") if n.strip()}
        out["XLUA_LUASOCKET"] = "socket" in names
        out["XLUA_MIME"] = "mime" in names
    return out


def expected_libs(runtime, environ=None):
    """The standard libraries XLUA_EXPECT_LIBS names for runtime ("lua" or
    "luajit"), as a set, or None when it is not set. Same syntax as the CMake
    option XLUA_LIBS: "all", or names separated by commas or semicolons; names of
    the other runtime are ignored, as CMake ignores them."""
    environ = os.environ if environ is None else environ
    text = environ.get("XLUA_EXPECT_LIBS")
    if text is None or not text.strip():
        return None
    known = set(LIB_EXPORTS[runtime]) | ({"ffi"} if runtime == "luajit" else set())
    if text.strip().lower() == "all":
        return known
    names = {n.strip().lower() for n in re.split(r"[,;]", text) if n.strip()}
    return names & known


def check_optional_exports(exported, luajit, environ=None):
    """Compare the optional parts of a library with what the XLUA_EXPECT_*
    variables say. exported(name) tells whether the library exports a function.

    Returns (problems, absent, summary): problems is a list of messages, absent
    the names of parts that are not built (to leave out of the required managed
    imports), summary one line for the log."""
    expect = expected_options(environ)
    all_exports = any(exported(name) for name in ALL_EXPORTS_MARKERS)
    problems, absent, parts = [], set(), []
    for option, names in OPTION_EXPORTS.items():
        have = [n for n in names if exported(n)]
        managed = all(n in managed_imports() for n in names)
        wanted = expect[option]
        if option == "XLUA_LEAK_CHECKER" and luajit:
            wanted = False if wanted is None else wanted
            if wanted:
                problems.append("XLUA_EXPECT_LEAK_CHECKER=ON, but LuaJIT has no leak checker")
        if have and len(have) != len(names):
            problems.append("%s: only part of its exports: %s" % (option, ", ".join(have)))
        built = bool(have)
        if not managed and not all_exports:
            parts.append(option + " ?")  # XLUA_EXPORTS=managed hides it either way
            if not built and wanted is not False:
                absent.update(names)
            continue
        if wanted is True and not built:
            problems.append("%s expected ON, but the library has none of %s" % (option, ", ".join(names)))
        if wanted is False and built:
            problems.append("%s expected OFF, but the library has %s" % (option, ", ".join(have)))
        if not built:
            absent.update(names)
        parts.append(option + (" on" if built else " off"))
    runtime = "luajit" if luajit else "lua"
    libs = expected_libs(runtime, environ)
    if all_exports:
        have_libs = {lib for lib, fn in LIB_EXPORTS[runtime].items() if exported(fn)}
        if libs is not None and have_libs != libs - {"ffi"}:
            problems.append("XLUA_EXPECT_LIBS: expected %s, the library has %s"
                            % (",".join(sorted(libs)) or "none", ",".join(sorted(have_libs)) or "none"))
        parts.append("libs " + (",".join(sorted(have_libs)) or "none"))
    return problems, absent, ", ".join(parts)


def export_list_problem():
    """None when build/xlua_exports.txt matches LuaDLL.cs, else what is wrong."""
    if not EXPORT_LIST.exists() or EXPORT_LIST.read_text() != export_list_text():
        return "build/xlua_exports.txt does not match LuaDLL.cs; run python3 Test/Native/managed_imports.py --write"
    return None


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["--write"]:
        EXPORT_LIST.write_text(export_list_text())
        print("wrote build/xlua_exports.txt")
    elif sys.argv[1:] == ["--check"]:
        problem = export_list_problem()
        print(problem or "build/xlua_exports.txt matches LuaDLL.cs")
        sys.exit(1 if problem else 0)
    else:
        print("\n".join(managed_imports()))
