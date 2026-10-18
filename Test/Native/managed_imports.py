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


# The files with the native imports: the XLua.Core assembly's (the Lua C API, the
# xlua_core_* layer and LuaScript, docs/design/core-luascript.md section 2.2) and
# the bridge's (XLua.LuaDLL.Bridge).
CORE_DIR = REPO / "Assets/XLua/Core"
BRIDGE_FILES = [REPO / "Assets/XLua/Src/LuaDLLBridge.cs"]


def core_files():
    """The C# files of the XLua.Core runtime assembly (Assets/XLua/Core without Editor)."""
    return sorted(f for f in CORE_DIR.rglob("*.cs") if "Editor" not in f.relative_to(CORE_DIR).parts)


def managed_imports(defined=None, files=None):
    """Return the entry point names imported through LUADLL by xLua's managed
    sources: the XLua.Core files and the bridge's LuaDLLBridge.cs, or `files`.

    With `defined` (a set of platform symbols such as {"UNITY_WEBGL"}) only
    imports compiled for that platform are returned. Project settings that are
    not platform symbols count as both on and off, so an import is returned if
    any setting compiles it. Without `defined` every import in the files is
    returned.
    """
    texts = []
    for path in (core_files() + BRIDGE_FILES if files is None else files):
        source = path.read_text()
        texts += [source] if defined is None else [_active_text(source, defined, v) for v in (False, True)]
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
    # LuaScript's sandbox (build/xlua_core_sandbox.c): always in the core preset, in
    # legacy and minimal with XLUA_LUASCRIPT=ON. Its C# imports compile only with the
    # scripting define XLUA_LUASCRIPT or XLUA_CORE_ONLY (Assets/XLua/Core/LuaDLL.cs).
    "XLUA_LUASCRIPT": ["xlua_core_open", "xlua_core_call", "xlua_core_newenv", "xlua_core_unref",
                       "xlua_core_set_resolver", "xlua_core_set_require",
                       # build/xlua_core_buffer.c: always with the sandbox, also with XLUA_BUFFER=OFF.
                       "xlua_core_newbuffer", "xlua_core_tobuffer"],
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
# Exported with XLUA_EXPORTS=all on every runtime, never imported by the managed sources.
ALL_EXPORTS_MARKERS = ["lua_xmove", "luaL_checkstack"]

EXPORT_LIST = REPO / "build/xlua_exports.txt"
EXPORT_HEADER = """\
# Functions that the managed sources (Assets/XLua/Core and the bridge's
# Assets/XLua/Src/LuaDLLBridge.cs) import from the native plugin, one per line, on any
# platform. With -DXLUA_EXPORTS=managed, build/CMakeLists.txt exports only
# these and XLUA_EXPORTS_EXTRA. A second column names the CMake option without
# which the function is not built; CMake leaves it out then. Generated by
#   python3 Test/Native/managed_imports.py --write
# check_exports.py fails when this file and the sources disagree.
"""


# The core preset (XLUA_PRESET=core, docs/design/core-luascript.md): what LuaScript
# needs of the XLua.Core assembly's imports (Assets/XLua/Core). Those are the
# xlua_core_* layer, xlua_gc and the non-raising Lua C API functions of section
# 3.12; the rest of Core's Lua C API imports (lua_pushnumber, luaL_ref ...) serve
# the bridge in the full presets. A core library still contains them (lapi.c,
# lauxlib.c), so an iOS or WebGL player links, but with XLUA_EXPORTS=managed it
# does not export them: a core-only player never calls them, and IL2CPP, Mono
# and .NET resolve an import when it is first called. Exporting every Core
# import cost 5,096 bytes stripped on Android arm64 (Lua 5.5.1: 260,608 against
# 255,512), mostly symbol tables and PLT entries. build/xlua_core_exports.txt is written from it, and
# with XLUA_EXPORTS=managed the core library exports exactly these.
CORE_LUA_API = ["lua_gettop", "lua_settop", "lua_type", "lua_toboolean", "lua_checkstack"]
CORE_EXPORTS = [n for n in managed_imports(files=core_files())
                if (n.startswith("xlua_core_") and n != "xlua_core_newstate") or n == "xlua_gc" or n in CORE_LUA_API]
# xlua_core_newstate is LuaEnv(LuaOptions)'s entry: a core library has it, exported only with XLUA_EXPORTS=all.
# What a core library must not contain (core-luascript.md, section 6,
# "Verification"): the libraries LuaScript never opens, LuaSocket, the bridge,
# and the parts of the bridge build that core leaves out.
CORE_FORBIDDEN = [
    "luaopen_io", "luaopen_os", "luaopen_debug", "luaopen_package", "luaL_openlibs",
    "luaL_openselectedlibs", "luaopen_socket_core", "luaopen_mime_core", "luaopen_xlua",
    "luaopen_i64lib", "xlua_pushcsobj", "xlua_push_csharp_function", "errorfunc",
    "xlua_memory_snapshot", "xlua_report_table_size",
]
# Text of lundump.c (Lua 5.4 and 5.5) that a core library cannot contain: its
# loader is replaced by xlua_core_nobc.c, which has none of these messages
# (unless CMake XLUA_TRUSTED_BYTECODE keeps it: XLUA_EXPECT_TRUSTED_BYTECODE=ON).
CORE_FORBIDDEN_TEXT = [b"truncated chunk", b"bad binary format"]

CORE_EXPORT_LIST = REPO / "build/xlua_core_exports.txt"
CORE_EXPORT_HEADER = """\
# Functions that the core preset's library (XLUA_PRESET=core) exports with
# -DXLUA_EXPORTS=managed: what LuaScript needs of the imports of Assets/XLua/Core,
# CORE_EXPORTS in Test/Native/managed_imports.py. A bridge preset with
# XLUA_LUASCRIPT=ON exports these as well. Generated by
#   python3 Test/Native/managed_imports.py --write
"""


def core_export_list_text():
    return CORE_EXPORT_HEADER + "".join(name + "\n" for name in sorted(CORE_EXPORTS))


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


def check_loadlib_imports(imports, luajit, environ=None):
    """CMake XLUA_LOADLIB: package.loadlib and Lua's C searchers are the only users
    of dlopen in a PUC Lua library, so with XLUA_EXPECT_LOADLIB=OFF the library
    must not import it, and with ON it must (on the POSIX platforms these checks
    run on). imports is the set of undefined symbol names, or None when they could
    not be listed. LuaJIT's lib_package.c always imports it. Returns (problems, summary)."""
    environ = os.environ if environ is None else environ
    if imports is None or luajit:
        return [], ""
    have = "dlopen" in imports
    wanted = _flag(environ.get("XLUA_EXPECT_LOADLIB"))
    problems = []
    if wanted is False and have:
        problems.append("XLUA_EXPECT_LOADLIB=OFF, but the library imports dlopen")
    if wanted is True and not have:
        problems.append("XLUA_EXPECT_LOADLIB=ON, but the library does not import dlopen")
    return problems, "dlopen " + ("imported" if have else "not imported")


def check_core_library(exported, exported_names, data, environ=None):
    """Check a library built with XLUA_PRESET=core (core-luascript.md, section 6):
    it exports every CORE_EXPORTS function; none of CORE_FORBIDDEN; with managed
    exports (no ALL_EXPORTS_MARKERS) exactly CORE_EXPORTS, when exported_names
    (the set of exported names) is known; the libraries XLUA_EXPECT_LIBS names
    (default: LuaScript's safe set) when everything is exported; and no text of
    the bytecode loader in data (the library's bytes), unless XLUA_EXPECT_TRUSTED_BYTECODE=ON
    (CMake XLUA_TRUSTED_BYTECODE), which requires it.

    Returns (problems, summary)."""
    environ = os.environ if environ is None else environ
    problems = []
    missing = [n for n in CORE_EXPORTS if not exported(n)]
    if missing:
        problems.append("core exports missing: " + ", ".join(missing))
    forbidden = [n for n in CORE_FORBIDDEN if exported(n)]
    if forbidden:
        problems.append("a core library exports " + ", ".join(forbidden))
    text = [t.decode() for t in CORE_FORBIDDEN_TEXT if t in data]
    if environ.get("XLUA_EXPECT_TRUSTED_BYTECODE", "").upper() == "ON":
        # CMake XLUA_TRUSTED_BYTECODE: Lua's loader is back for the host's own chunks.
        if len(text) != len(CORE_FORBIDDEN_TEXT):
            problems.append("XLUA_EXPECT_TRUSTED_BYTECODE=ON, but the library has no bytecode loader")
    elif text:
        problems.append("a core library contains the bytecode loader (%s)" % ", ".join(text))
    all_exports = any(exported(name) for name in ALL_EXPORTS_MARKERS)
    if all_exports:
        libs = expected_libs("lua", {"XLUA_EXPECT_LIBS": environ.get("XLUA_EXPECT_LIBS") or
                                     "coroutine,table,string,math,utf8"})
        have = {lib for lib, fn in LIB_EXPORTS["lua"].items() if exported(fn)}
        # Every import of Assets/XLua/Core must exist in a core library, exported or
        # not: IL2CPP links __Internal imports by name on iOS and WebGL.
        absent = [n for n in managed_imports(files=core_files()) if not exported(n)]
        if absent:
            problems.append("Assets/XLua/Core imports functions a core library lacks: " + ", ".join(absent))
        if have != libs:
            problems.append("libraries: expected %s, the library has %s"
                            % (",".join(sorted(libs)) or "none", ",".join(sorted(have)) or "none"))
        summary = "all exports, libs " + (",".join(sorted(have)) or "none")
    elif exported_names is not None:
        extra = sorted(set(exported_names) - set(CORE_EXPORTS))
        if extra:
            problems.append("%d exports besides the core list: %s" % (len(extra), ", ".join(extra[:10])))
        summary = "managed exports, %d" % len(exported_names)
    else:
        summary = "managed exports (not enumerated)"
    return problems, summary


# Messages of Lua's lexer and parser (llex.c, lparser.c in 5.3 to 5.5), which a
# library built with XLUA_BYTECODE_ONLY does not contain.
PARSER_TEXT = [b"malformed number", b"function arguments expected"]


def check_parser(data, environ=None):
    """Problems with the compiler in data (a library's bytes) against
    XLUA_EXPECT_BYTECODE_ONLY: ON, Lua's lexer and parser must be left out;
    OFF, they must be there (PUC Lua's messages); unset, either is fine."""
    environ = os.environ if environ is None else environ
    expect = environ.get("XLUA_EXPECT_BYTECODE_ONLY", "").upper()
    found = [t.decode() for t in PARSER_TEXT if t in data]
    if expect == "ON" and found:
        return ["XLUA_EXPECT_BYTECODE_ONLY=ON, but the library contains Lua's parser (%s)" % ", ".join(found)]
    if expect == "OFF" and len(found) != len(PARSER_TEXT):
        return ["XLUA_EXPECT_BYTECODE_ONLY=OFF, but the library has no Lua parser"]
    return []


def export_list_problem():
    """None when build/xlua_exports.txt matches the managed imports and
    build/xlua_core_exports.txt those of Assets/XLua/Core, else what is wrong."""
    if not EXPORT_LIST.exists() or EXPORT_LIST.read_text() != export_list_text():
        return "build/xlua_exports.txt does not match the managed imports; run python3 Test/Native/managed_imports.py --write"
    if not CORE_EXPORT_LIST.exists() or CORE_EXPORT_LIST.read_text() != core_export_list_text():
        return "build/xlua_core_exports.txt does not match Assets/XLua/Core; run python3 Test/Native/managed_imports.py --write"
    return None


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["--write"]:
        EXPORT_LIST.write_text(export_list_text())
        CORE_EXPORT_LIST.write_text(core_export_list_text())
        print("wrote build/xlua_exports.txt and build/xlua_core_exports.txt")
    elif sys.argv[1:] == ["--check"]:
        problem = export_list_problem()
        print(problem or "build/xlua_exports.txt and build/xlua_core_exports.txt match the managed imports")
        sys.exit(1 if problem else 0)
    else:
        print("\n".join(managed_imports()))
