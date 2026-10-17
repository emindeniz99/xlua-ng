"""Split what xLua adds to a player into its parts, from size_report.py builds.

Reads a size_report.py work directory (projects/ and players/) and writes
WORK/size-breakdown.json and WORK/size-breakdown.md:

  native   libxlua.so per object file, from lld maps (--android-map) and the iOS
           linker map of UnityFramework (libxlua.a members), with gzip -9 of
           each part's bytes taken from the linked binary
  il2cpp   libil2cpp.so per xLua source file and per class library, from the
           unstripped libil2cpp.dbg.so of the variant and of the baseline and
           the generated C++ in Library/Bee/artifacts/Android/il2cppOutput
  metadata global-metadata.dat per section
  engine   libunity.so code that the variant keeps and the baseline does not,
           by library (libcurl, mbedtls, nghttp2, zlib, UnityWebRequest)
  resources the Lua files under Assets/XLua/Resources

The Android projects keep only their last build in Library; build the default
configuration last (size_report.py builds the AAB last, with the same
stripping and ABI) before running this.

An --android-map comes from linking the plugin again with the map, for example
  cmake -S build -B OUT -DLUA_VERSION=5.5.1 -DANDROID_ABI=arm64-v8a \\
    -DANDROID_PLATFORM=android-21 -DCMAKE_BUILD_TYPE=Release \\
    -DCMAKE_TOOLCHAIN_FILE=$ANDROID_NDK/build/cmake/android.toolchain.cmake \\
    -DCMAKE_SHARED_LINKER_FLAGS=-Wl,-Map=OUT/libxlua.map
"""
import argparse
import collections
import glob
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import zlib

repo = Path(__file__).resolve().parents[2]

LUA_FILES = {
    # Lua core
    "lapi": ("core", "C API (lua_push*, lua_to*, lua_call ...)"),
    "lcode": ("core", "compiler: bytecode generator"),
    "lctype": ("core", "character classes for the lexer"),
    "ldebug": ("core", "debug interface, error messages with variable names"),
    "ldo": ("core", "calls, stack growth, coroutines, protected calls, loading"),
    "ldump": ("core", "bytecode writer (string.dump)"),
    "lfunc": ("core", "closures and upvalues"),
    "lgc": ("core", "garbage collector"),
    "llex": ("core", "compiler: lexer"),
    "lmem": ("core", "allocation wrappers"),
    "lobject": ("core", "number and string conversions, tostring/format helpers"),
    "lopcodes": ("core", "opcode tables"),
    "lparser": ("core", "compiler: parser"),
    "lstate": ("core", "global state and threads"),
    "lstring": ("core", "string interning"),
    "ltable": ("core", "tables (array and hash parts)"),
    "ltm": ("core", "metamethods"),
    "lundump": ("core", "bytecode loader"),
    "lvm": ("core", "bytecode interpreter"),
    "lzio": ("core", "buffered input for the loader"),
    # standard libraries
    "lauxlib": ("library", "auxiliary library (luaL_*), used by every library and by xLua"),
    "lbaselib": ("library", "base library: print, pairs, pcall, load, tostring ..."),
    "lbitlib": ("library", "bit32; compiled out unless LUA_COMPAT_BITLIB"),
    "lcorolib": ("library", "coroutine library"),
    "ldblib": ("library", "debug library"),
    "linit": ("library", "luaL_openlibs: the list of libraries to open"),
    "liolib": ("library", "io library (files, popen)"),
    "lmathlib": ("library", "math library and its random generator"),
    "loadlib": ("library", "package library: require, searchers, C module loading"),
    "loslib": ("library", "os library (time, date, clock, getenv, remove ...)"),
    "lstrlib": ("library", "string library: find/match/gsub patterns, format, pack/unpack"),
    "ltablib": ("library", "table library: insert, remove, sort, concat, move"),
    "lutf8lib": ("library", "utf8 library"),
    # xLua's own C
    "xlua": ("bridge", "xLua bridge: object userdata, indexers, C# callbacks, struct packing, bypath access"),
    "i64lib": ("bridge", "64-bit integer helpers (uint64 library; int64 userdata on LuaJIT)"),
    "memory_leak_checker": ("bridge", "table sizes and references for General/LuaMemoryLeakChecker; Assets/XLua/Src does not call it"),
    # LuaSocket
    "luasocket": ("socket", "socket.core module entry (luaopen_socket_core)"),
    "auxiliar": ("socket", "class and userdata helpers"),
    "buffer": ("socket", "buffered send/receive"),
    "compat": ("socket", "Lua version compatibility shims"),
    "except": ("socket", "socket.try/protect error handling"),
    "inet": ("socket", "address resolution (getaddrinfo, toip, tohostname)"),
    "io": ("socket", "I/O error strings"),
    "mime": ("socket", "mime.core (base64, quoted-printable); xLua never registers it"),
    "options": ("socket", "setoption/getoption tables"),
    "select": ("socket", "socket.select"),
    "tcp": ("socket", "TCP sockets"),
    "timeout": ("socket", "timeouts, socket.gettime and socket.sleep"),
    "udp": ("socket", "UDP sockets"),
    "usocket": ("socket", "POSIX socket calls"),
}
GROUPS = {"core": "Lua core", "library": "Lua standard libraries", "bridge": "xLua bridge", "socket": "LuaSocket"}
LINKER = {
    ".rela.dyn": "dynamic relocations (pointers in function tables, PIC data)",
    ".rela.plt": "relocations for calls into libc, libm, libdl",
    ".plt": "call stubs for libc, libm, libdl",
    ".got": "global offset table",
    ".got.plt": "global offset table for the call stubs",
    ".dynsym": "dynamic symbol table: every exported API function and every import",
    ".dynstr": "names of the dynamic symbols",
    ".hash": "SysV hash table of the exports",
    ".gnu.hash": "GNU hash table of the exports",
    ".gnu.version": "symbol versions",
    ".gnu.version_r": "symbol versions needed",
    ".dynamic": "dynamic section (NEEDED libraries ...)",
    ".eh_frame_hdr": "index of the unwind tables",
    ".rodata": "merged string literals and constants (all files)",
    ".note.gnu.build-id": "build id",
}
LUAJIT_FAMILIES = [
    ("JIT compiler", r"lj_(asm|record|opt_\w+|snap|trace|ir|mcode|ffrecord|crecord|gdbjit|emit|target|iropt|jit)\b"),
    ("FFI", r"lj_(cparse|ctype|cconv|cdata|carith|ccall|ccallback|clib|cffi)\b|lib_ffi\b"),
    ("interpreter (generated by buildvm)", r"lj_(vm|dispatch|vmevent|vmmath)\b"),
    ("compiler: lexer, parser, bytecode", r"lj_(lex|parse|bcread|bcwrite|bc)\b"),
    ("standard libraries", r"lib_\w+|lj_lib\b"),
    ("runtime: GC, tables, strings, API, errors", r"lj_\w+"),
]


def tool(name):
    ndk = os.environ.get("ANDROID_NDK")
    found = glob.glob(os.path.join(ndk, "toolchains/llvm/prebuilt/*/bin", name)) if ndk else []
    return found[0] if found else name


def gz(data):
    return len(zlib.compress(data, 9)) if data else 0


# ---------------------------------------------------------------- native

class Image:
    """Reads bytes of a linked ELF64 or Mach-O 64 file by virtual address."""
    def __init__(self, path):
        self.data = Path(path).read_bytes()
        self.segments = []
        d = self.data
        if d[:4] == b"\x7fELF":
            phoff, = struct.unpack_from("<Q", d, 0x20)
            phentsize, phnum = struct.unpack_from("<HH", d, 0x36)
            for i in range(phnum):
                p_type, _, off, vaddr, _, filesz, _, _ = struct.unpack_from("<IIQQQQQQ", d, phoff + i * phentsize)
                if p_type == 1:
                    self.segments.append((vaddr, off, filesz))
        elif d[:4] == b"\xcf\xfa\xed\xfe":
            ncmds, = struct.unpack_from("<I", d, 16)
            o = 32
            for _ in range(ncmds):
                cmd, size = struct.unpack_from("<II", d, o)
                if cmd == 0x19:  # LC_SEGMENT_64
                    vmaddr, _, fileoff, filesize = struct.unpack_from("<QQQQ", d, o + 24)
                    self.segments.append((vmaddr, fileoff, filesize))
                o += size

    def read(self, vma, size):
        for vaddr, off, filesz in self.segments:
            if vaddr <= vma and vma + size <= vaddr + filesz:
                return self.data[off + vma - vaddr: off + vma - vaddr + size]
        return b""


def lld_map(so, path):
    """{part: [bytes, gzip]} for one Android library from its lld map."""
    image, parts, out = Image(so), collections.defaultdict(lambda: [0, bytearray()]), None
    for line in Path(path).read_text(errors="replace").splitlines():
        m = re.match(r"\s*([0-9a-f]+)\s+[0-9a-f]+\s+([0-9a-f]+)\s+\d+\s(\s*)(\S.*)$", line)
        if not m:
            continue
        vma, size, rest = int(m.group(1), 16), int(m.group(2), 16), m.group(4)
        if not m.group(3):
            out = rest
            continue
        src = re.match(r"(.*):\((\S+)\)$", rest)
        if not src or out.startswith((".bss", ".tbss", ".debug", ".comment", ".symtab", ".strtab", ".shstrtab")):
            continue
        name = src.group(1)
        if name == "<internal>":
            key = "linker:" + out
        else:
            member = re.search(r"\(([^()]+)\)$", name)
            key = (member.group(1) if member else name.rsplit("/", 1)[-1])
            key = re.sub(r"\.(c|cpp)?\.?o$", "", key)
            if member:
                key = name.rsplit("/", 1)[-1].split("(")[0] + ":" + key
        parts[key][0] += size
        parts[key][1] += image.read(vma, size)
    return {k: [v[0], gz(bytes(v[1]))] for k, v in parts.items()}


def ios_map(link_map, binary, archive="libxlua.a"):
    """{object: [bytes, gzip]} for the members of one static library in a linker map."""
    image, files, parts, section = Image(binary), {}, collections.defaultdict(lambda: [0, bytearray()]), None
    for line in Path(link_map).read_text(errors="replace").splitlines():
        if line.startswith("# ") and line.endswith(":"):
            section = line
            continue
        if section == "# Object files:":
            m = re.match(r"\[\s*(\d+)\]\s+.*/" + re.escape(archive) + r"\((.*)\.o\)$", line)
            if m:
                files[m.group(1)] = m.group(2)
        elif section == "# Symbols:":
            m = re.match(r"0x([0-9A-F]+)\s+0x([0-9A-F]+)\s+\[\s*(\d+)\]", line)
            if m and m.group(3) in files:
                p = parts[files[m.group(3)]]
                p[0] += int(m.group(2), 16)
                p[1] += image.read(int(m.group(1), 16), int(m.group(2), 16))
    return {k: [v[0], gz(bytes(v[1]))] for k, v in parts.items()}


def elf_info(so):
    needed = re.findall(r"\(NEEDED\)\s+Shared library: \[(.*?)\]",
                        subprocess.run([tool("llvm-readelf"), "-d", so], capture_output=True, text=True).stdout)
    syms = subprocess.run([tool("llvm-nm"), "-D", so], capture_output=True, text=True).stdout.splitlines()
    return {"needed": needed, "imports": sum(1 for s in syms if s.split()[-2:-1] == ["U"]),
            "exports": sum(1 for s in syms if len(s.split()) == 3 and s.split()[1] in "TDBR")}


def stripped_size(so, work):
    copy = Path(work) / "tmp" / ("strip-" + Path(so).name)
    copy.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([tool("llvm-strip"), "--strip-unneeded", "-o", str(copy), so], check=True)
    return copy.stat().st_size


# ---------------------------------------------------------------- IL2CPP

def nm_sizes(path, demangle=False):
    out = subprocess.run([tool("llvm-nm"), "-S", "--defined-only"] + (["-C"] if demangle else []) + [str(path)],
                         capture_output=True, text=True).stdout
    sizes = collections.Counter()
    for line in out.splitlines():
        f = line.split(maxsplit=3)
        if len(f) == 4 and re.fullmatch(r"[0-9a-f]+", f[1]):
            sizes[f[3]] += int(f[1], 16)
    return sizes


def xlua_types():
    """Type name -> xLua source file, for every type declared under Assets/XLua/Src (not Editor)."""
    types = {}
    for f in sorted((repo / "Assets/XLua/Src").glob("*.cs")) + [repo / "Assets/XLua/Src/TemplateEngine/TemplateEngine.cs"]:
        text = re.sub(r"//[^\n]*", "", f.read_text(errors="replace"))
        for m in re.finditer(r"\b(?:class|struct|interface|enum)\s+(\w+)|\bdelegate\s+[\w<>\[\],\s]+?\s(\w+)\s*(?:<[^>]*>)?\s*\(", text):
            types.setdefault(m.group(1) or m.group(2), f.name)
    types.update({"SizeProbeApi": "SizeProbe.cs (the probe)", "XluaSmoke": "SizeProbe.cs (the probe)",
                  "UnitySourceGeneratedAssemblyMonoScriptTypes": "Unity generated (Assembly-CSharp)"})
    return types


DEFINITION = re.compile(r"^[A-Za-z][^\n;{}]*?\b(\w+) \([^;{}]*\)\s*\n\{", re.M)


def cpp_definitions(cpp_dir):
    """Symbol -> generated C++ file stem, and the order of definitions in Assembly-CSharp*.cpp."""
    owner, order = {}, []
    for f in sorted(Path(cpp_dir).glob("*.cpp")):
        stem = re.sub(r"(__\d+)?$", "", f.stem)
        names = DEFINITION.findall(f.read_text(errors="replace"))
        for n in names:
            owner.setdefault(n, stem)
        if stem == "Assembly-CSharp":
            order += names
    return owner, order


CS_FILES = {
    "Utils.cs": "wrappers for C# types without generated code (ReflectionWrap), member lookup, extension methods, type loading",
    "StaticLuaCallbacks.cs": "C functions Lua calls: object and class __index/__newindex, arrays, delegates, loaders, CS/typeof/cast/import_type, print",
    "ObjectTranslator.cs": "C# value <-> Lua value: object cache and references, push/get per type, delegate and interface bridges, type registration",
    "LuaDLL.cs": "P/Invoke declarations of the native API (XLua.LuaDLL.Lua), StackToString, UTF-8 string decoding",
    "ObjectCasters.cs": "per-type check and cast functions from Lua values to C# parameters",
    "MethodWarpsCache.cs": "overload resolution and call wrappers for reflected methods, constructors, events",
    "LuaEnv.cs": "the Lua state: creation, init_xlua script, searchers, DoString, GC control, Tick, Dispose",
    "TemplateEngine.cs": "Lua template engine (the 'template' library every LuaEnv opens unless XLUA_NO_TEMPLATE); the code generator uses it",
    "EmbeddedModules.cs": "xlua.util and perf.profiler as string constants (StaticLuaCallbacks.LoadFromEmbedded loads them)",
    "CopyByValue.cs": "pack/unpack of value types into Lua userdata ([GCOptimize] structs, decimal)",
    "GenAttributes.cs": "configuration attributes ([LuaCallCSharp], [CSharpCallLua], [Hotfix] ...) and SysGenConfig",
    "InternalGlobals.cs": "shared static tables: operator names, extension methods, array accessors",
    "DelegateBridge.cs": "Lua functions as C# delegates; hotfix delegate bridges",
    "LuaTable.cs": "C# handle to a Lua table (Get/Set/ForEach/Cast)",
    "LuaFunction.cs": "C# handle to a Lua function (Call, Func, Action, Cast)",
    "LuaBase.cs": "reference counting and Dispose for LuaTable and LuaFunction",
    "ObjectPool.cs": "slot array holding the C# objects Lua references",
    "TypeExtensions.cs": "Type helpers shared by every platform",
    "SignatureLoader.cs": "loader that checks RSA/SHA-256 signatures on Lua files",
    "ObjectTranslatorPool.cs": "lua_State -> ObjectTranslator lookup",
    "RawObject.cs": "wrappers that pass a Lua value as one exact C# type (xlua.cast helpers)",
    "LuaException.cs": "exception type for Lua errors",
    "SizeProbe.cs (the probe)": "the measuring script itself",
}

# Class library code by the C++ type name a symbol starts with (fullmatch).
BCL_FAMILIES = [
    ("cryptography (in xLua only SignatureLoader uses System.Security.Cryptography)",
     r".*(Crypt|RSA|Aes|Rijndael|RC2|DES|SHA\d*|MD5|RIPEMD|HMAC|DSA|KeyPair|Csp|Symmetric|HashAlgorithm|PKCS1|ASN1|Kernel|ModulusRing|BigInteger|Oid|KeySizes|KeyBuilder|MACAlgorithm|Mono_Math|Prime|X509).*"),
    ("regular expressions", r"Regex.*|Match|Group|GroupCollection|Capture|CaptureCollection|MatchCollection"),
    ("System.Uri", r"Uri.*|IPv[46]AddressHelper|DomainNameHelper|UncNameHelper|IriHelper"),
    ("LINQ (used by Utils, ObjectTranslator, TypeExtensions)", r"Enumerable|Lookup_2|Grouping|.*Iterator.*|OrderedEnumerable.*|EnumerableSorter.*|Set_1|Buffer_1"),
    ("streams, tasks and async state machines", r".*Task.*|.*Async.*|.*Awaiter.*|.*Stream|.*Promise.*|.*ThreadPool.*"),
    ("collections and arrays (List, Dictionary, HashSet, sorting, comparers)",
     r"Dictionary_2|List_1|HashSet_1|Queue_1|Stack_1|.*Enumerator.*|ArraySortHelper_\d|.*Comparer.*|KeyValuePair_2|Array|ArraySegment_1|ReadOnlySpan_1|Span_1|Memory_1|ReadOnlyMemory_1|ValueListBuilder_1|CollectionBase|SortedList|Hashtable"),
    ("delegates (Func, Action ...)", r"Func_\d|Action_\d|Action|Func|Predicate_1|Comparison_1|Converter_2"),
]
TYPE_NAME = re.compile(r"(U3C\w+?U3E\w*?__\d+(?:_\d+)?|[A-Za-z0-9]+?(?:_\d+)?)_")


def classify(a, b, owner, order):
    """Attribute the symbols the variant (a) adds or grows over the baseline (b)."""
    types = xlua_types()
    names = sorted(types, key=len, reverse=True)

    def type_of(sym):
        for n in names:
            if sym.startswith(n + "_") or sym == n:
                return n
        return None

    # Compiler-generated types (<>c, display classes, iterators) are written after their declaring type.
    assembly_csharp, current = {}, None
    for sym in order:
        t = type_of(sym)
        if t and not t.startswith("U3C"):
            current = t
        assembly_csharp[sym] = t if t and not sym.startswith("U3C") else current
    by_file, bcl, tables = collections.Counter(), collections.defaultdict(collections.Counter), collections.Counter()
    for sym in set(a) | set(b):
        d = a.get(sym, 0) - b.get(sym, 0)
        if not d:
            continue
        where = owner.get(sym)
        if where == "Assembly-CSharp":
            t = assembly_csharp.get(sym)
            by_file[types.get(t, "Assembly-CSharp (other)")] += d
        elif where:
            t = type_of(sym.replace("_gshared", ""))
            if where in ("Generics", "GenericMethods") and t and not t.startswith("U3C"):
                by_file[types[t] + " (generic instances)"] += d
                continue
            m = TYPE_NAME.match(sym)
            name = m.group(1) if m else sym
            fam = next((f for f, rx in BCL_FAMILIES if re.fullmatch(rx, name)), None)
            if fam is None:
                fam = "other " + ("generic instances" if where in ("Generics", "GenericMethods") else where)
            bcl[fam][sym] += d
        else:
            tables[sym] += d
    return {"files": dict(by_file.most_common()),
            "bcl": {k: {"bytes": sum(v.values()), "top": [s for s, _ in v.most_common(4)]}
                    for k, v in sorted(bcl.items(), key=lambda kv: -sum(kv[1].values()))},
            "tables": sum(tables.values()), "tables_top": [s for s, _ in tables.most_common(6)]}


def il2cpp(work, variant, baseline):
    proj = Path(work) / "projects"
    out_dir = "Library/Bee/artifacts/Android/il2cppOutput"
    dbg = lambda p: next((proj / p / out_dir / "build").glob("*/libil2cpp.dbg.so"))
    stripped = lambda p: next((proj / p / out_dir / "build").glob("*/libil2cpp.so"))
    owner, order = cpp_definitions(proj / variant / out_dir / "cpp")
    r = classify(nm_sizes(dbg(variant)), nm_sizes(dbg(baseline)), owner, order)

    def sections(so):
        """Sizes of the sections that take space in the file (no NOBITS, no non-alloc)."""
        text = subprocess.run([tool("llvm-readelf"), "-S", "-W", str(so)], capture_output=True, text=True).stdout
        found = {}
        for m in re.finditer(r"\]\s+(\S+)\s+(\S+)\s+[0-9a-f]+\s+[0-9a-f]+\s+([0-9a-f]+)\s+\S+\s+(\S*)", text):
            if "A" in m.group(4) and m.group(2) != "NOBITS":
                found[m.group(1)] = int(m.group(3), 16)
        return found
    sa, sb = sections(stripped(variant)), sections(stripped(baseline))
    r["sections"] = {k: sa.get(k, 0) - sb.get(k, 0) for k in sorted(set(sa) | set(sb)) if sa.get(k, 0) != sb.get(k, 0)}
    r["stripped_delta"] = stripped(variant).stat().st_size - stripped(baseline).stat().st_size
    return r, owner, order


def link_map_symbols(path, archive):
    """{symbol: bytes} for the members of one static library in an Apple linker map."""
    files, sizes, section = set(), collections.Counter(), None
    for line in Path(path).read_text(errors="replace").splitlines():
        if line.startswith("# ") and line.endswith(":"):
            section = line
        elif section == "# Object files:":
            m = re.match(r"\[\s*(\d+)\]\s+.*/" + re.escape(archive) + r"\(", line)
            if m:
                files.add(m.group(1))
        elif section == "# Symbols:":
            m = re.match(r"0x[0-9A-F]+\s+0x([0-9A-F]+)\s+\[\s*(\d+)\]\s+_?(.*)$", line)
            if m and m.group(2) in files:
                sizes[m.group(3)] += int(m.group(1), 16)
    return sizes


def il2cpp_ios(work, variant, baseline, owner, order):
    """The same split for UnityFramework's libGameAssembly.a, from the iOS linker maps."""
    lm = lambda v: Path(work) / "players/ios/default" / v / "UnityFramework-LinkMap-arm64.txt"
    a, b = link_map_symbols(lm(variant), "libGameAssembly.a"), link_map_symbols(lm(baseline), "libGameAssembly.a")
    r = classify(a, b, owner, order)
    r["total_delta"] = sum(a.values()) - sum(b.values())
    return r


METADATA = ("stringLiteral stringLiteralData string events properties methods parameterDefaultValues "
            "fieldDefaultValues fieldAndParameterDefaultValueData fieldMarshaledSizes parameters fields "
            "genericParameters genericParameterConstraints genericContainers nestedTypes interfaces vtableMethods "
            "interfaceOffsets typeDefinitions images assemblies fieldRefs referencedAssemblies attributeData "
            "attributeDataRange unresolvedIndirectCallParameterTypes unresolvedIndirectCallParameterRanges "
            "windowsRuntimeTypeNames windowsRuntimeStrings exportedTypeDefinitions").split()


def metadata_sections(path):
    d = Path(path).read_bytes()
    version, first = struct.unpack_from("<I", d, 4)[0], struct.unpack_from("<I", d, 8)[0]
    if version < 38:  # (offset, size) pairs before v38, (offset, size, count) triples after
        return version, {METADATA[i]: struct.unpack_from("<II", d, 8 + 8 * i)[1] for i in range((first - 8) // 8)}
    return version, {METADATA[i]: struct.unpack_from("<III", d, 8 + 12 * i)[1] for i in range((first - 8) // 12)}


def metadata(work, variant, baseline):
    path = lambda p: Path(work) / "projects" / p / "Library/Bee/artifacts/Android/il2cppOutput/data/Metadata/global-metadata.dat"
    va, a = metadata_sections(path(variant))
    _, b = metadata_sections(path(baseline))
    return {"version": va, "sections": {k: a[k] - b.get(k, 0) for k in a if a[k] != b.get(k, 0)},
            "delta": path(variant).stat().st_size - path(baseline).stat().st_size}


ENGINE = [("libcurl (HTTP client)", r"^(Curl|curl)_?|Curl"),
          ("mbedtls and PSA crypto (TLS)", r"mbedtls|psa_|unitytls|^tls|x509"),
          ("nghttp2 (HTTP/2)", r"nghttp2|huff_"),
          ("zlib (content decoding)", r"unity_z_|inflate|deflate"),
          ("UnityWebRequest and handlers", r"UnityWebRequest|WebRequest|DownloadHandler|UploadHandler|CertificateHandler|Transport|Curl|HeaderHelper|WWW|Http")]


def engine(work, variant, baseline):
    path = lambda p: Path(work) / "projects" / p / "Library/Bee/artifacts/Android/libunity/arm64-v8a/unstripped/libunity.so"
    a, b = nm_sizes(path(variant), True), nm_sizes(path(baseline), True)
    groups = collections.defaultdict(collections.Counter)
    for sym in set(a) | set(b):
        d = a.get(sym, 0) - b.get(sym, 0)
        if d:
            groups[next((g for g, rx in ENGINE if re.search(rx, sym)), "other engine code")][sym] += d
    return {k: {"bytes": sum(v.values()), "top": [s[:60] for s, _ in v.most_common(3)]}
            for k, v in sorted(groups.items(), key=lambda kv: -sum(kv[1].values()))}


RESOURCES = {
    "xlua/util.lua.txt": "xlua.util: async_to_sync, coroutine_call, cs_generator, loadpackage, auto_id_map, hotfix_ex, bind, createdelegate, state",
    "perf/memory.lua.txt": "perf.memory: snapshot() and total() only raise 'use memory leak checker instead!'",
    "perf/profiler.lua.txt": "perf.profiler: per-function call counts and times on debug.sethook",
    "tdr/tdr.lua.txt": "tdr: wrapper for Tencent's TDR protocol; needs the native libtdrlua module, which xLua does not build",
}


def resources():
    # Empty since 2026-10-17: tdr and perf.memory were removed, xlua.util and perf.profiler moved
    # into EmbeddedModules.cs (string literals in the IL2CPP metadata).
    root = repo / "Assets/XLua/Resources"
    if not root.is_dir():
        return {}
    return {str(f.relative_to(root)): {"bytes": f.stat().st_size, "gzip": gz(f.read_bytes()),
                                        "purpose": RESOURCES.get(str(f.relative_to(root)), "")}
            for f in sorted(root.rglob("*.txt"))}


# ---------------------------------------------------------------- report

def fmt(n):
    return "-" if n is None else "{:,}".format(n)


def sfmt(n):
    return "-" if n is None else "{:+,}".format(n)


def markdown(r):
    out = []
    main = r["native"]["android"].get("5.5.1", {})
    cmp_cols = [k for k in r["native"]["android"] if k not in ("5.5.1", "luajit")]
    ios = r["native"].get("ios", {})
    out.append("### Native library\n")
    out.append("Bytes of each part in the linked library (code, constants, data and its unwind entries) and gzip -9 of "
               "those bytes alone. Android arm64-v8a from the lld map; iOS arm64 from the linker map of UnityFramework "
               "(dead-stripped, as linked).\n")
    head = ["part", "5.5.1 Android bytes", "gzip -9"] + [v + " Android bytes" for v in cmp_cols] + ["5.5.1 iOS bytes", "purpose"]
    for group, title in GROUPS.items():
        rows = [(f, LUA_FILES[f][1]) for f in LUA_FILES if LUA_FILES[f][0] == group]
        rows.sort(key=lambda fp: -(main.get(fp[0]) or [0])[0])
        out.append("**%s**\n" % title)
        out.append("| " + " | ".join(head) + " |")
        out.append("|" + " --- |" * len(head))
        tot = [0, 0, collections.Counter(), 0]
        for f, purpose in rows:
            m = main.get(f)
            others = [r["native"]["android"][v].get(f) for v in cmp_cols]
            i = ios.get("5.5.1", {}).get(f)
            if not m and not any(others):
                continue
            out.append("| `%s.c` | %s | %s | %s | %s | %s |" % (f, fmt(m and m[0]), fmt(m and m[1]),
                       " | ".join(fmt(o and o[0]) for o in others), fmt(i and i[0]), purpose))
            tot[0] += m[0] if m else 0
            tot[1] += m[1] if m else 0
            for v, o in zip(cmp_cols, others):
                tot[2][v] += o[0] if o else 0
            tot[3] += i[0] if i else 0
        out.append("| total | %s | %s | %s | %s | |" % (fmt(tot[0]), fmt(tot[1]), " | ".join(fmt(tot[2][v]) for v in cmp_cols), fmt(tot[3])))
        out.append("")
    out.append("**Linker output and other objects (Android 5.5.1)**\n")
    out.append("| part | bytes | gzip -9 | purpose |")
    out.append("| --- | --- | --- | --- |")
    known = set(LUA_FILES)
    rest = sorted(((k, v) for k, v in main.items() if k not in known), key=lambda kv: -kv[1][0])
    for k, v in rest:
        purpose = LINKER.get(k.split(":", 1)[1], "") if k.startswith("linker:") else ("C runtime start file" if "crt" in k else "")
        out.append("| %s | %s | %s | %s |" % (k.replace("linker:", ""), fmt(v[0]), fmt(v[1]), purpose))
    info = r["native"]["android_info"].get("5.5.1", {})
    out.append("| ELF headers and page alignment | %s | | |" % fmt(info.get("stripped", 0) - sum(v[0] for v in main.values())))
    out.append("| **stripped library (as shipped)** | **%s** | %s | |" % (fmt(info.get("stripped")), fmt(info.get("stripped_gzip"))))
    out.append("")
    out.append("Dynamic dependencies: %s; %d imported and %d exported symbols.\n" % (
        ", ".join(info.get("needed", [])), info.get("imports", 0), info.get("exports", 0)))
    jit = r["native"]["android"].get("luajit")
    if jit:
        fams = collections.defaultdict(lambda: [0, 0])
        for k, v in jit.items():
            name = k.split(":", 1)[1] if k.startswith("libluajit.a:") else None
            if name:
                fam = next(f for f, rx in LUAJIT_FAMILIES if re.fullmatch(rx, name))
            elif k in LUA_FILES:
                fam = GROUPS[LUA_FILES[k][0]]
            elif k.startswith("libunwind"):
                fam = "libunwind (static, NDK): C++ unwinder LuaJIT uses for errors"
            else:
                fam = "linker output, ELF headers"
            fams[fam][0] += v[0]
            fams[fam][1] += v[1]
        out.append("**LuaJIT 2.1 (Android arm64-v8a)**\n")
        out.append("| part | bytes | gzip -9 |")
        out.append("| --- | --- | --- |")
        for f, v in sorted(fams.items(), key=lambda kv: -kv[1][0]):
            out.append("| %s | %s | %s |" % (f, fmt(v[0]), fmt(v[1])))
        ji = r["native"]["android_info"]["luajit"]
        out.append("| **stripped library** | **%s** | %s |" % (fmt(ji["stripped"]), fmt(ji["stripped_gzip"])))
        out.append("")
    il = r.get("il2cpp")
    if il:
        out.append("### IL2CPP code (Android arm64, %s against %s)\n" % (r["variant"], r["baseline"]))
        empty = {"files": {}, "bcl": {}, "tables": None}
        ios, med = r.get("il2cpp_ios") or empty, r.get("il2cpp_medium") or empty
        out.append("Function and data symbols that the variant adds or grows, by owner: Android from "
                   "`libil2cpp.dbg.so`, iOS from the `libGameAssembly.a` symbols in the UnityFramework linker map "
                   "(owners looked up in the Android IL2CPP output). Minimal stripping unless marked Medium.\n")
        out.append("| owner | purpose | Android | iOS | Android, Medium |")
        out.append("| --- | --- | --- | --- | --- |")
        for k in sorted(set(il["files"]) | set(ios["files"]) | set(med["files"]), key=lambda k: -il["files"].get(k, 0)):
            out.append("| %s | %s | %s | %s | %s |" % (k, CS_FILES.get(k.split(" (")[0], "") if "(generic" not in k else
                       "instances of its generic methods (Get<T>, Push<T> ...)", fmt(il["files"].get(k)),
                       fmt(ios["files"].get(k)), fmt(med["files"].get(k))))
        for k in sorted(set(il["bcl"]) | set(ios["bcl"]) | set(med["bcl"]), key=lambda k: -il["bcl"].get(k, {"bytes": 0})["bytes"]):
            v = il["bcl"].get(k) or ios["bcl"].get(k) or med["bcl"][k]
            out.append("| class library: %s | e.g. `%s` | %s | %s | %s |" % (k, "`, `".join(s[:40] for s in v["top"][:2]),
                       fmt(il["bcl"].get(k, {}).get("bytes")), fmt(ios["bcl"].get(k, {}).get("bytes")),
                       fmt(med["bcl"].get(k, {}).get("bytes"))))
        out.append("| IL2CPP tables and data | method pointers, invokers, generic tables, string literals | %s | %s | %s |" % (
            fmt(il["tables"]), fmt(ios["tables"]), fmt(med["tables"])))
        rest = lambda x: x["stripped_delta"] - sum(x["files"].values()) - sum(v["bytes"] for v in x["bcl"].values()) - x["tables"]
        out.append("| not in symbols | relocations, unwind tables, alignment | %s | | %s |" % (
            fmt(rest(il)), fmt(rest(med)) if med is not empty else "-"))
        out.append("| **total** | stripped libil2cpp.so; libGameAssembly.a in the map | **%s** | **%s** | **%s** |" % (
            fmt(il["stripped_delta"]), fmt(ios.get("total_delta")), fmt(med.get("stripped_delta"))))
        out.append("")
        out.append("Section deltas of the stripped libil2cpp.so: " + ", ".join(
            "`%s` %s" % (k, sfmt(v)) for k, v in sorted(il["sections"].items(), key=lambda kv: -abs(kv[1]))[:8]) + ".\n")
    md = r.get("metadata")
    if md:
        out.append("### global-metadata.dat (version %d)\n" % md["version"])
        out.append("| section | delta |")
        out.append("| --- | --- |")
        for k, v in sorted(md["sections"].items(), key=lambda kv: -kv[1]):
            if v >= 1000:
                out.append("| %s | %s |" % (k, sfmt(v)))
        out.append("| **file delta** | **%s** |" % sfmt(md["delta"]))
        out.append("")
    en = r.get("engine")
    if en:
        out.append("### Engine code in libunity.so (Android arm64)\n")
        out.append("Symbols of the unstripped libunity.so the variant keeps and the baseline does not.\n")
        out.append("| part | bytes | e.g. |")
        out.append("| --- | --- | --- |")
        for k, v in en.items():
            out.append("| %s | %s | `%s` |" % (k, fmt(v["bytes"]), "`, `".join(v["top"][:2])))
        out.append("")
    out.append("### Resources\n")
    out.append("| file | bytes | gzip -9 | purpose |")
    out.append("| --- | --- | --- | --- |")
    for k, v in r["resources"].items():
        out.append("| `%s` | %s | %s | %s |" % (k, fmt(v["bytes"]), fmt(v["gzip"]), v["purpose"]))
    return "\n".join(out) + "\n"


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--work", required=True, type=Path, help="size_report.py work directory")
    p.add_argument("--android-map", action="append", default=[], metavar="LABEL=SO:MAP",
                   help="an Android libxlua.so and its lld map; labels 5.5.1 (main column), luajit, others compared")
    p.add_argument("--ios-variant", action="append", default=[], metavar="LABEL=VARIANT",
                   help="iOS player under WORK/players/ios/default whose link map to split, e.g. 5.5.1=ng-5.5")
    p.add_argument("--variant", default="android-ng-5.5", help="Android project for the IL2CPP, metadata and engine split")
    p.add_argument("--baseline", default="android-baseline")
    p.add_argument("--ios-il2cpp", default="ng-5.5:baseline", metavar="VARIANT:BASELINE",
                   help="iOS players under WORK/players/ios/default to split libGameAssembly.a for ('' to skip)")
    p.add_argument("--medium", metavar="VARIANT:BASELINE",
                   help="Android projects (relative to WORK/projects) last built with Medium stripping, for a third column")
    a = p.parse_args()
    work = a.work.resolve()
    r = {"variant": a.variant, "baseline": a.baseline, "native": {"android": {}, "android_info": {}, "ios": {}}}
    for spec in a.android_map:
        label, _, paths = spec.partition("=")
        so, _, mp = paths.rpartition(":")
        r["native"]["android"][label] = lld_map(so, mp)
        size = stripped_size(so, work)
        info = elf_info(so)
        info.update(stripped=size, stripped_gzip=gz((work / "tmp" / ("strip-" + Path(so).name)).read_bytes()))
        r["native"]["android_info"][label] = info
    for spec in a.ios_variant:
        label, _, variant = spec.partition("=")
        d = work / "players/ios/default" / variant
        binary = next(d.glob("*.app/Frameworks/UnityFramework.framework/UnityFramework"))
        r["native"]["ios"][label] = ios_map(d / "UnityFramework-LinkMap-arm64.txt", binary)
    r["il2cpp"], owner, order = il2cpp(work, a.variant, a.baseline)
    if a.ios_il2cpp:
        r["il2cpp_ios"] = il2cpp_ios(work, *a.ios_il2cpp.split(":"), owner, order)
    if a.medium:
        r["il2cpp_medium"] = il2cpp(work, *a.medium.split(":"))[0]
    r["metadata"] = metadata(work, a.variant, a.baseline)
    r["engine"] = engine(work, a.variant, a.baseline)
    r["resources"] = resources()
    (work / "size-breakdown.json").write_text(json.dumps(r, indent=1) + "\n")
    text = markdown(r)
    (work / "size-breakdown.md").write_text(text)
    print(text)
    print("Wrote", work / "size-breakdown.json", "and", work / "size-breakdown.md")


if __name__ == "__main__":
    main()
