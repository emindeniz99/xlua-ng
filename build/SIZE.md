# Size breakdown

What each part of xlua-ng adds to a player, and what the part is for. The numbers
come from the 2026-10-16 player builds in "Player size" in `README.md`: Lua 5.5.1,
no generated code, Unity 6000.3.21f1, IL2CPP, ARM64, managed stripping Minimal
unless a column says Medium. `Test/MobileSmoke/size_breakdown.py` produced the
tables from those builds; see "Player size" in `Test/README.md` for how to run it.

Every figure is measured unless it is marked *estimate*. Bytes are the size in the
linked binary. "gzip -9" compresses each part's bytes on their own; it is close to
the part's share of the compressed APK, but the parts' sum is about 8% larger than
the whole library compresses to (183,238 against 169,029 for the Lua 5.5.1 library).

## Where the bytes go

| | Android APK, compressed / uncompressed | iOS |
| --- | --- | --- |
| native library | `libxlua.so` 169,426 / 418,272 | `libxlua.a` 288,515 linked into UnityFramework |
| IL2CPP code for the C# | `libil2cpp.so` +559,500 / +1,912,024 | `libGameAssembly.a` +1,408,823 |
| engine code kept for xLua | `libunity.so` +544,070 / +1,101,520 | `libiPhone-lib.a` +9,760 |
| IL2CPP metadata | `global-metadata.dat` +116,360 / +329,240 | +293,512 |
| Lua files in `Resources` | +6,965 / +19,524 | +19,008 (all of `Data` except metadata) |
| **package delta** | **+1,397,134** | **+737,445** zipped `.app`, +1,763,168 UnityFramework |

## Native library

Android arm64-v8a from the lld map of `libxlua.so` (NDK r30, Release). The shipped
library is the stripped one: Unity strips it when it packs the APK. iOS arm64 is the
part of `libxlua.a` the linker kept in UnityFramework (dead stripping removes what
nothing references, so `mime.c` and `memory_leak_checker.c` are gone there).

**Lua core** (`build/lua-5.5.1/src`)

| file | 5.5.1 Android | gzip -9 | 5.3.6 Android | 5.5.1 iOS | purpose |
| --- | --- | --- | --- | --- | --- |
| `lvm.c` | 25,192 | 9,897 | 15,604 | 24,164 | bytecode interpreter |
| `lparser.c` | 22,554 | 12,338 | 16,738 | 21,234 | compiler: parser |
| `lapi.c` | 21,421 | 8,409 | 19,473 | 17,618 | C API (`lua_push*`, `lua_to*`, `lua_call` ...) |
| `lcode.c` | 18,854 | 9,552 | 12,892 | 15,418 | compiler: bytecode generator |
| `lgc.c` | 15,227 | 7,296 | 9,052 | 13,231 | garbage collector |
| `llex.c` | 13,104 | 5,418 | 12,612 | 12,626 | compiler: lexer |
| `ltable.c` | 11,296 | 6,428 | 6,068 | 9,643 | tables (array and hash parts) |
| `ldebug.c` | 11,120 | 6,375 | 5,708 | 10,043 | debug interface, error messages with variable names |
| `ldo.c` | 10,252 | 5,872 | 7,512 | 9,038 | calls, stack growth, coroutines, protected calls, loading |
| `lobject.c` | 6,580 | 4,048 | 5,020 | 5,263 | number and string conversions |
| `lundump.c` | 5,896 | 2,907 | 3,080 | 5,855 | bytecode loader |
| `ltm.c` | 5,449 | 2,929 | 2,833 | 4,495 | metamethods |
| `ldump.c` | 4,680 | 2,083 | 2,472 | 4,444 | bytecode writer (`string.dump`) |
| `lstate.c` | 3,588 | 2,073 | 2,472 | 2,881 | global state and threads |
| `lstring.c` | 3,460 | 2,209 | 2,116 | 2,628 | string interning |
| `lfunc.c` | 2,468 | 1,493 | 1,276 | 1,999 | closures and upvalues |
| `lmem.c` | 1,496 | 871 | 584 | 1,117 | allocation wrappers |
| `lzio.c`, `lctype.c`, `lopcodes.c` | 1,138 | 708 | 768 | 878 | loader input buffer, character classes, opcode names |
| **total** | **183,775** | **90,906** | **126,280** | **162,575** | |

**Lua standard libraries**

| file | 5.5.1 Android | gzip -9 | 5.3.6 Android | 5.5.1 iOS | purpose |
| --- | --- | --- | --- | --- | --- |
| `lstrlib.c` | 21,547 | 10,662 | 18,503 | 20,151 | `string`: patterns, `format`, `pack`/`unpack` |
| `lauxlib.c` | 15,784 | 7,244 | 13,660 | 12,963 | auxiliary library (`luaL_*`), used by every library and by xLua |
| `liolib.c` | 9,220 | 3,938 | 9,036 | 8,359 | `io`: files, `popen` |
| `lbaselib.c` | 6,963 | 2,854 | 6,308 | 6,131 | base: `print`, `pairs`, `pcall`, `load`, `tostring` ... |
| `ldblib.c` | 6,348 | 2,963 | 6,052 | 5,801 | `debug` |
| `lmathlib.c` | 5,692 | 2,184 | 4,476 | 4,480 | `math` and its random generator |
| `loadlib.c` | 5,624 | 2,514 | 5,308 | 5,513 | `package`: `require`, searchers, C module loading |
| `ltablib.c` | 5,068 | 2,491 | 4,776 | 4,595 | `table`: `insert`, `remove`, `sort`, `concat`, `move` |
| `loslib.c` | 3,900 | 2,016 | 3,836 | 3,700 | `os`: `time`, `date`, `clock`, `getenv` ... (`os.execute` removed) |
| `lutf8lib.c` | 3,795 | 2,230 | 2,987 | 3,390 | `utf8` |
| `lcorolib.c` | 2,516 | 1,231 | 1,996 | 2,241 | `coroutine` |
| `linit.c` | 1,080 | 561 | 464 | 1,047 | `luaL_openlibs`: the list of libraries to open |
| **total** | **87,537** | **40,888** | **77,434** | **78,371** | |

5.3.6 compiles `lbitlib.c` (`bit32`) to almost nothing: it is off unless `LUA_COMPAT_BITLIB`.

**xLua's own C** (`build/`)

| file | 5.5.1 Android | gzip -9 | 5.3.6 Android | 5.5.1 iOS | purpose |
| --- | --- | --- | --- | --- | --- |
| `xlua.c` | 19,952 | 5,921 | 19,796 | 15,182 | the bridge: C# object userdata, class and object indexers, C# callbacks and errors, struct packing, `xlua_pgettable_bypath` ... |
| `memory_leak_checker.c` | 1,980 | 1,468 | 1,376 | - | table sizes and references for `General/LuaMemoryLeakChecker`; `Assets/XLua/Src` never calls it |
| `i64lib.c` | 1,388 | 749 | 1,388 | 847 | 64-bit integer helpers (the `uint64` library; on LuaJIT the int64 userdata, 8,888 bytes) |

The memory snapshot walk (`xlua_memory_snapshot`, added to `memory_leak_checker.c` on
2026-10-18, after the measurements above) is the CMake option `XLUA_MEMORY_SNAPSHOT`,
off by default for Android, OpenHarmony, UWP and Switch: the default stripped Android
arm64-v8a `libxlua.so` is 377,256 bytes, as on main (c067a2b). With
`-DXLUA_MEMORY_SNAPSHOT=ON` it is 384,856 bytes, 7,600 more (4,056 more gzip -9; NDK 27.1,
Release, Lua 5.5.1, API 23, the default `XLUA_SIZE_FLAGS`; 8,848 with the earlier `-O3`
build). It adds 7,912 bytes to the macOS arm64 `__text`, where it is on by default. Its
object file has 8,537 bytes of text against 1,135 for the leak checker alone. Only the
Editor and Development Builds call it, so a statically linked release player (iOS, WebGL,
the source plugin) strips it.

No other C library is bundled: no lpeg, rapidjson or protobuf.

**LuaSocket** (`build/luasocket`, 48,158 bytes Android, 20,652 gzip; 31,540 iOS). In
these builds `LuaEnv` registered `socket` and `socket.core` as built-in modules on
every platform except WebGL and Switch. Since 2026-10-18 the native library puts
them, and `mime.core` and `mime`, into `package.preload` itself, and CMake's
`XLUA_LUASOCKET` and `XLUA_MIME` leave them out; see
[without LuaSocket](#without-luasocket-or-mime) for the measured saving. Nothing
in `Assets/XLua` requires it; `Add_Remove_Lua_Lib.md` says it is there for
ZeroBrane Studio's remote debugger, and any Lua code can open TCP and UDP sockets
with it (see `Assets/XLua/Doc/Security.md`).

| file | Android | gzip -9 | iOS | purpose |
| --- | --- | --- | --- | --- |
| `options.c` | 11,096 | 2,737 | 6,605 | `setoption`/`getoption` tables |
| `mime.c` | 6,727 | 3,028 | - | `mime.core` (base64, quoted-printable): compiled and exported, never registered in these builds (registered since 2026-10-18) |
| `udp.c`, `tcp.c` | 10,896 | 4,230 | 9,073 | UDP and TCP socket objects |
| `inet.c`, `usocket.c` | 9,603 | 4,862 | 7,981 | address resolution and the POSIX socket calls |
| `select.c`, `buffer.c`, `timeout.c` | 6,156 | 3,726 | 5,158 | `socket.select`, buffered I/O, timeouts, `gettime`/`sleep` |
| `auxiliar.c`, `except.c`, `luasocket.c`, `io.c` | 3,680 | 2,069 | 2,723 | class helpers, `try`/`protect`, module entry, error strings |

**Produced by the linker** (Android 5.5.1)

| part | bytes | gzip -9 | purpose |
| --- | --- | --- | --- |
| `.rela.dyn` | 18,624 | 3,865 | dynamic relocations: pointers in the `luaL_Reg` tables and other data |
| `.rodata` (merged) | 11,188 | 5,118 | string literals and constants merged from every file |
| `.dynsym`, `.dynstr`, `.hash`, `.gnu.hash`, `.gnu.version*` | 21,110 | 7,213 | the dynamic symbol table: 280 exported functions (all of the Lua C API, `luaL_*`, `luaopen_*`, xLua's `xlua_*`) and 121 imports |
| `.eh_frame_hdr` | 7,964 | 4,120 | index of the unwind tables (the unwind entries are counted in each file) |
| `.plt`, `.rela.plt`, `.got`, `.got.plt` | 12,784 | 2,030 | calls into libc, libm and libdl |
| other | 740 | | `.dynamic`, build id, `crtbegin_so.o` |
| ELF headers, page alignment | 3,072 | | |
| **stripped library** | **418,272** | **169,029** | |

Dependencies: `libc.so`, `libm.so` and `libdl.so` are shared system libraries and
add nothing to the APK. Nothing else is linked statically except the NDK's
`crtbegin_so.o` (288 bytes).

**LuaJIT 2.1** (Android arm64-v8a, 706,712 bytes stripped, 338,010 gzip)

| part | bytes | gzip -9 |
| --- | --- | --- |
| JIT compiler (`lj_asm`, `lj_record`, `lj_opt_*`, `lj_snap`, `lj_trace`, `lj_ir` ...) | 208,293 | 116,134 |
| runtime: GC, tables, strings, API, errors, allocator | 115,114 | 65,821 |
| FFI (`lj_cparse`, `lj_ctype`, `lj_cconv`, `lj_ccall`, `lib_ffi` ...) | 62,897 | 37,544 |
| standard libraries (`lib_*`) | 62,728 | 34,784 |
| compiler: lexer, parser, bytecode reader and writer | 53,696 | 29,918 |
| LuaSocket | 48,770 | 20,966 |
| xLua bridge (`xlua.c`, `i64lib.c` with the int64 userdata) | 27,612 | 8,841 |
| interpreter (`lj_vm`, assembly generated by `buildvm`) | 23,696 | 12,140 |
| `libunwind.a` from the NDK, linked statically: the unwinder LuaJIT raises errors with | 19,584 | 10,475 |
| linker output and ELF headers | 81,051 | 26,505 |

## IL2CPP code

The symbols of the unstripped `libil2cpp.dbg.so` that the xlua-ng player adds or
grows over the baseline, by owner. Methods of compiler-generated classes (lambdas,
iterators) count for the class they are written after in the generated C++, which is
the class that declares them. iOS uses the `libGameAssembly.a` symbols in the
UnityFramework linker map with the same owners. The last column is the same pair of
projects built with Medium stripping.

| owner | purpose | Android | iOS | Android, Medium |
| --- | --- | --- | --- | --- |
| `Utils.cs` | wrappers for C# types without generated code (`ReflectionWrap`), member lookup, extension methods, type loading | 39,856 | 47,376 | 36,728 |
| `StaticLuaCallbacks.cs` | the C functions Lua calls: object and class `__index`/`__newindex`, arrays, delegates, loaders, `CS`, `typeof`, `cast`, `import_type`, `print` | 31,236 | 35,700 | 31,236 |
| `ObjectTranslator.cs` | C# value to Lua value and back: object cache and references, push/get per type, delegate and interface bridges, type registration | 29,220 | 33,016 | 27,648 |
| `LuaDLL.cs` | P/Invoke declarations of the native API, `StackToString`, UTF-8 string decoding | 26,460 | 12,932 | 14,284 |
| `ObjectCasters.cs` | check and cast functions from Lua values to C# parameter types | 21,808 | 28,924 | 21,292 |
| `MethodWarpsCache.cs` | overload resolution and call wrappers for reflected methods, constructors and events | 15,176 | 16,124 | 13,592 |
| `LuaEnv.cs` | the Lua state: creation, the `init_xlua` script, searchers, `DoString`, GC control, `Tick`, `Dispose` | 8,232 | 10,296 | 6,224 |
| `TemplateEngine.cs` | the Lua template engine, opened as the `template` library in every `LuaEnv`; the code generator uses it | 7,244 | 8,536 | 6,612 |
| `CopyByValue.cs` | packing value types into Lua userdata (`[GCOptimize]` structs, `decimal`) | 2,856 | 3,104 | 488 |
| `GenAttributes.cs` | `[LuaCallCSharp]`, `[CSharpCallLua]`, `[Hotfix]` and the other configuration attributes | 2,432 | 2,456 | - |
| `InternalGlobals.cs` | shared static tables: operator names, extension methods, array accessors | 2,396 | 2,592 | 1,988 |
| `DelegateBridge.cs` | Lua functions as C# delegates, hotfix bridges | 2,136 | 2,452 | 1,356 |
| `LuaTable.cs`, `LuaFunction.cs`, `LuaBase.cs` | C# handles to Lua tables and functions, reference counting and `Dispose` | 4,340 | 5,596 | 2,264 |
| `ObjectPool.cs`, `ObjectTranslatorPool.cs` | slot array of the C# objects Lua references; `lua_State` to translator lookup | 2,744 | 3,180 | 2,576 |
| `TypeExtensions.cs` | `Type` helpers | 1,644 | 1,696 | 1,604 |
| `SignatureLoader.cs` | loader that checks RSA/SHA-256 signatures on Lua files | 1,424 | 1,636 | - |
| `RawObject.cs` | wrappers that pass a value as one exact C# type | 892 | 980 | - |
| `LuaException.cs` | the exception for Lua errors | 104 | 108 | 104 |
| generic instances of xLua methods | `ObjectTranslator` (11,856), `LuaTable` (10,452), `DelegateBridge` (8,520), `LuaFunction`, `LuaEnv`, `RawObject` | 38,140 | 38,484 | 21,280 |
| **xLua's own C#** | | **238,340** | **255,188** | **189,276** |
| class library: collections and arrays | `List`, `Dictionary`, `HashSet`, sorting and comparers instantiated for xLua's types | 208,376 | 250,452 | 168,264 |
| class library: cryptography | `System.Security.Cryptography`: `SignatureLoader`'s RSA and SHA-256, and through `CryptoConfig` every other algorithm (AES, DES, RC2, MD5, RIPEMD-160 ...) | 164,212 | 198,868 | - |
| class library: regular expressions | `System.Text.RegularExpressions`. `TemplateEngine` parses templates with a `Regex`, but that accounts for only 10,512 bytes at Medium; other class library code reaches the rest (not traced) | 103,156 | 114,980 | 102,060 |
| class library: streams, tasks, async | `FileStream`/`MemoryStream` and their async state machines | 71,996 | 82,304 | 84 |
| class library: LINQ | used by `Utils`, `ObjectTranslator`, `TypeExtensions` and `TemplateEngine` | 57,016 | 66,576 | 56,496 |
| class library: `System.Uri` and UnityWebRequest | `WWW` in `LoadFromStreamingAssetsPath` (Android only) | 36,688 | 16,604 | 103,396 |
| class library: other | delegates, other generic instances, other mscorlib, `UnityEngine.CoreModule` | 76,188 | 72,644 | 71,240 |
| IL2CPP tables and data | method pointer tables, invokers, generic method tables, string literals | 325,803 | 350,123 | 351,286 |
| not in a symbol | relocations (`.rela.dyn` +347,232), unwind tables (`.eh_frame` +200,312), alignment | 629,341 | | 435,854 |
| **total** | stripped `libil2cpp.so`; `libGameAssembly.a` in the linker map | **1,912,024** | **1,408,823** | **1,478,864** |

`CodeEmit.cs` is not in the player: it compiles only in the Editor and in
`XLUA_GENERAL` builds. The editor code (the generator, LuaCATS, Validate Setup,
`Hotfix.cs`) is under `Editor` folders and is not in the player either.

Minimal stripping keeps every method of Assembly-CSharp, so everything above
ships even if nothing calls it. With Medium, the linker removes what no kept
method references: `SignatureLoader` and the whole cryptography stack go, the
stream and task code goes, and `LuaDLL` loses half its wrappers. What stays is
reachable from `new LuaEnv()`, which registers every callback in
`StaticLuaCallbacks`, `ObjectTranslator` and `Utils` and opens the `template`
library.

Reached only through reflection, where the linker sees no call:

- In reflection mode (no generated code), every C# type, method, field and
  property that Lua uses goes through `Utils.ReflectionWrap`. The linker cannot
  see those uses, so Medium and High strip anything that C# does not call
  itself; keep such members with `link.xml` or `[Preserve]` (this probe marks
  `SizeProbeApi` `[Preserve]`). Minimal strips only the class libraries and
  UnityEngine, so it keeps user code.
- XLua/Generate Code writes `Assets/XLua/Gen/link.xml` with `preserve="all"` for
  every `[LuaCallCSharp]` type. That is why generated code for `GameObject` and
  `Transform` also keeps 36 KB more of `libunity.so`: all their members stay.
- The generated `XLua_Gen_Initer_Register__` runs from the static field
  `ObjectTranslator.s_gen_reg_dumb_obj`; `ObjectTranslator.getDelegate` finds the
  generated `__Gen_Delegate_Imp*` methods with `GetMethods`, but the generated
  `GetDelegateByType` also calls them directly.

## IL2CPP metadata

`global-metadata.dat` (version 39) grows by 329,240 bytes:

| section | delta | what it holds |
| --- | --- | --- |
| methods | +72,576 | one record per method |
| string | +44,272 | type, method, field and parameter names |
| fieldAndParameterDefaultValueData | +39,840 | default values and constant data |
| parameters | +31,800 | one record per parameter |
| stringLiteralData | +31,651 | C# string literals, including the `init_xlua` Lua source in `LuaEnv` |
| typeDefinitions | +27,816 | one record per type |
| attributeData | +25,124 | custom attributes (`[MonoPInvokeCallback]` on every callback ...) |
| vtableMethods | +14,984 | virtual method tables |
| fields | +13,860 | one record per field |
| other sections | +27,317 | properties, literal index, generic parameters, interfaces ... |

## Engine code

On Android, `StaticLuaCallbacks.LoadFromStreamingAssetsPath` reads
StreamingAssets with `UnityEngine.WWW`. That keeps UnityWebRequest in
`libunity.so`: 544,070 bytes compressed, 1,101,520 uncompressed. The symbols the
xlua-ng player adds to the unstripped `libunity.so`:

| part | symbol bytes | purpose |
| --- | --- | --- |
| mbedtls and PSA crypto | 213,557 | TLS for HTTPS |
| libcurl | 157,834 | the HTTP client behind UnityWebRequest |
| nghttp2 | 53,808 | HTTP/2, with its Huffman tables |
| UnityWebRequest, handlers, `HeaderHelper` | 18,380 | the engine side of `UnityWebRequest` and `WWW` |
| zlib | 10,972 | content decoding |
| other | 14,346 | `GameObject.GetComponents`, `Resources.Load` bindings ... |

Symbols cover 468,897 of the 1,101,520 bytes; relocations, unwind tables and
string data make up the rest. Removing the `WWW` path removes all of the parts
above (the measured saving is in "Size reductions"). iOS does not use `WWW`, and
its engine code grows by only 9,760 bytes. A project that already uses
UnityWebRequest pays this anyway.

## Resources

`Assets/XLua/Resources` is in every build, because Unity ships everything under a
`Resources` folder whether or not it is loaded.

| file | bytes | gzip -9 | purpose |
| --- | --- | --- | --- |
| `xlua/util.lua.txt` | 6,232 | 2,008 | `xlua.util`: `async_to_sync`, `coroutine_call`, `cs_generator`, `loadpackage`, `auto_id_map`, `hotfix_ex`, `bind`, `createdelegate`, `state` |
| `perf/profiler.lua.txt` | 4,477 | 1,538 | `perf.profiler`: per-function call counts and times on `debug.sethook` |
| `tdr/tdr.lua.txt` | 3,280 | 1,046 | `tdr`: wrapper for Tencent's TDR protocol; it requires the native `libtdrlua` module, which xLua does not build |
| `perf/memory.lua.txt` | 948 | 541 | `perf.memory`: `snapshot()` and `total()` only raise "use memory leak checker instead!" |

The APK grows by 6,965 bytes compressed for these files (19,524 uncompressed with
the resource container).

Since 2026-10-17 `Assets/XLua/Resources` is gone: `tdr` and `perf.memory` were
removed, and `xlua.util` and `perf.profiler` are string constants in
`EmbeddedModules.cs`. The two modules now ship in IL2CPP metadata as string
literals instead of in the resource container, roughly the same bytes, and the
`XLUA_NO_UTIL` and `XLUA_NO_PROFILER` defines leave them out (the profiler stays
in Development Builds). Not re-measured.

## Size reductions

Measured in scratch builds. The `SignatureLoader` and `template` rows are available as
the `XLUA_NO_SIGNATURE_LOADER` and `XLUA_NO_TEMPLATE` defines since 2026-10-17; the
others are not applied. The 2026-10-18 figures for the defines are release ARM64 APKs from
Unity 6000.3.21f1 at Minimal stripping (`size_report.py`, "Player size" in
`Test/MobileSmoke/README.md`).

| change | measured saving | cost |
| --- | --- | --- |
| Drop the Android `WWW` path of `LoadFromStreamingAssetsPath` | 569,044 bytes APK | `require` from StreamingAssets stops working on Android; upstream marks that loader obsolete |
| Leave `SignatureLoader.cs` out of the player (now `XLUA_NO_SIGNATURE_LOADER`) | 239,384 bytes APK at Minimal (`libil2cpp.so` -184,591 compressed / -551,064, metadata -54,744 / -137,936); nothing at Medium, which already strips it. The define, measured on 2026-10-18: 244,272 bytes at Minimal | projects that sign their Lua need to add it back |
| Export only the 117 functions `LuaDLL.cs` imports (a linker version script) | Lua 5.5.1: 418,272 to 392,552 bytes (-25,720), gzip -8,782; Lua 5.3.6: 344,072 to 320,240 | native C modules loaded at run time could no longer link against the Lua API; `General/LuaMemoryLeakChecker` needs its functions added |
| Build plugins with `-Os` | 25,324 bytes APK (see `README.md`) | VM 14% slower on a Cortex-A73 (`FLAGS.md`) |
| `-O3` for the interpreter's hot files, `-Oz` without the outliner for the rest, exports trimmed (`bestnw21` in `FLAGS.md`) | Lua 5.5.1 arm64-v8a: 24,511 bytes APK, 72,176 stripped; 26,946 and 90,432 at API 23 with packed relocations | compiling Lua source 6 to 8% slower; the cost of the version script above |
| Open the `template` library only in the Editor (`XLUA_NO_TEMPLATE` also leaves `TemplateEngine.cs` out of players, which this row did not measure) | nothing at Minimal (-68 bytes); 16,552 bytes APK at Medium. The define with `TemplateEngine.cs`, measured on 2026-10-18: 14,928 bytes at Minimal | Lua code in players can no longer call `template.compile` |
| Leave `mime.c` out of the build (now `-DXLUA_MIME=OFF`) | 6,727 bytes Android (*estimate* from the map; iOS strips it already); measured in [without LuaSocket](#without-luasocket-or-mime) | `require "mime"`, which works since 2026-10-18 |
| Leave LuaSocket out (now `-DXLUA_LUASOCKET=OFF`) | see [without LuaSocket](#without-luasocket-or-mime) | `require "socket"`, and with it the ZeroBrane and other socket debuggers |

`Test/Native/managed_imports.py`, which `check_exports.py`,
`check_android_elf.py` and `check_webgl_exports.py` use, finds 108 of the 117
imports: an import whose `[DllImport]` line ends with a comment is skipped, along
with the declaration after it. The nine it misses are `luaL_openlibs`,
`luaopen_xlua`, `gen_obj_indexer`, `gen_obj_newindexer`, `gen_cls_indexer`,
`gen_cls_newindexer`, `get_error_func_ref`, `load_error_func` and
`xlua_csharp_error`. They are all defined in the libraries measured here.

## Without LuaSocket or mime

Measured 2026-10-18 on the native library alone, not in a player: NDK r30, Lua
5.5.1, the release build (`XLUA_SIZE_FLAGS`, API 23), stripped with
`llvm-strip --strip-unneeded` as Unity packs it. "APK" is that file compressed
with zip deflate level 6, as `FLAGS.md` measures it. `main` is the library before
the change (4e76aee), where C# registered LuaSocket and `mime.c` was compiled but
never registered. `on` is the default now: LuaSocket and mime register
themselves in `package.preload`.

| ABI, exports | build | stripped | APK (deflate) |
| --- | --- | ---: | ---: |
| arm64-v8a, `all` | main | 372,560 | 159,184 |
| | on (default) | 372,816 (+256) | 159,299 (+115) |
| | `-DXLUA_MIME=OFF` | 366,424 (-6,136) | 156,668 (-2,516) |
| | `-DXLUA_LUASOCKET=OFF` | 327,976 (-44,584) | 141,552 (-17,632) |
| armeabi-v7a, `all` | main | 250,012 | 139,688 |
| | on (default) | 250,228 (+216) | 139,747 (+59) |
| | `-DXLUA_MIME=OFF` | 245,704 (-4,308) | 137,385 (-2,303) |
| | `-DXLUA_LUASOCKET=OFF` | 220,468 (-29,544) | 124,345 (-15,343) |
| arm64-v8a, `managed` | main | 349,840 | 151,807 |
| | on | 356,288 (+6,448) | 154,436 (+2,629) |
| | `-DXLUA_MIME=OFF` | 349,928 (+88) | 151,836 (+29) |
| | `-DXLUA_LUASOCKET=OFF` | 311,544 (-38,296) | 136,635 (-15,172) |
| armeabi-v7a, `managed` | main | 233,612 | 133,246 |
| | on | 238,172 (+4,560) | 135,620 (+2,374) |
| | `-DXLUA_MIME=OFF` | 233,680 (+68) | 133,301 (+55) |
| | `-DXLUA_LUASOCKET=OFF` | 208,492 (-25,120) | 120,193 (-13,053) |
| LuaJIT arm64-v8a, `all` | on (default) | 670,368 | 332,281 |
| | `XLUA_LUASOCKET=OFF` | 625,208 (-45,160) | 314,066 (-18,215) |

The registration itself (`preload_native_modules` in `xlua.c`) costs 68 to 120
bytes (the `XLUA_MIME=OFF` rows against `main` with `managed` exports and on iOS,
where nothing else changes). With `XLUA_EXPORTS=all` mime was already
in the library, exported but unused, so registering it costs nothing more; with
`XLUA_EXPORTS=managed` and on iOS, where the linker dropped the unreferenced
`mime.c`, registering it adds it back.

iOS arm64 (Lua 5.5.1, `make_ios_lua55.sh`, Xcode 27.1 beta): a program that calls
`luaL_newstate` and `luaopen_xlua`, linked against `libxlua.a` with
`-dead_strip`, the way IL2CPP's `__Internal` calls pull the library in. For
`main` the program also calls `luaopen_socket_core`, as main's C# did.

| build | `__text` | executable |
| --- | ---: | ---: |
| main | 258,996 | 368,992 |
| on (default) | 264,504 (+5,508, mime) | 386,000 (+17,008; Mach-O segments grow in 16 KB pages) |
| `XLUA_MIME=OFF` | 259,116 (+120) | 369,000 (+8) |
| `XLUA_LUASOCKET=OFF` | 230,860 (-28,136) | 327,920 (-41,072) |

Not measured: a Unity player (APK or IPA) with these libraries; the Android
`XLUA_EXPORTS=all` rows match the release scripts' output byte for byte
(`make_android_lua55.sh` with `XLUA_LUASOCKET` in the environment).

## Leak checker, uint64 table and standard libraries

Measured 2026-10-18 like [without LuaSocket](#without-luasocket-or-mime): the
native library alone, NDK r30, Lua 5.5.1, the release build (`XLUA_SIZE_FLAGS`,
API 23, `XLUA_MEMORY_SNAPSHOT` off as on Android by default), stripped with
`llvm-strip --strip-unneeded`, "APK" the file compressed with zip deflate level
6. The default library is the size of main (5cbefa9): 378,296 bytes stripped on
arm64-v8a and 253,496 on armeabi-v7a (deflate within 2 bytes; the build ID
differs). "Minimal libraries" is `XLUA_LIBS=package,coroutine,table,string,math,debug`
(no `io`, `os` or `utf8`); "reduced" adds `XLUA_LEAK_CHECKER=OFF`,
`XLUA_UINT64=OFF` and `XLUA_LUASOCKET=OFF`.

| exports | build | arm64-v8a stripped | APK | armeabi-v7a stripped | APK |
| --- | --- | ---: | ---: | ---: | ---: |
| `all` | default | 378,296 | 161,077 | 253,496 | 141,328 |
| | `XLUA_LEAK_CHECKER=OFF` | 376,904 (-1,392) | 160,355 (-722) | 252,552 (-944) | 140,641 (-687) |
| | `XLUA_UINT64=OFF` | 376,840 (-1,456) | 160,604 (-473) | 252,552 (-944) | 140,874 (-454) |
| | without `io` | 368,952 (-9,344) | 157,358 (-3,719) | 247,492 (-6,004) | 138,042 (-3,286) |
| | without `os` | 373,448 (-4,848) | 159,147 (-1,930) | 250,088 (-3,408) | 139,449 (-1,879) |
| | without `utf8` | 375,128 (-3,168) | 159,545 (-1,532) | 251,072 (-2,424) | 139,761 (-1,567) |
| | without `coroutine` | 375,480 (-2,816) | 160,083 (-994) | 251,768 (-1,728) | 140,534 (-794) |
| | minimal libraries | 360,704 (-17,592) | 153,719 (-7,358) | 241,536 (-11,960) | 134,571 (-6,757) |
| | reduced | 312,936 (-65,360) | 134,692 (-26,385) | 209,800 (-43,696) | 118,159 (-23,169) |
| `managed` | default | 361,736 | 156,174 | 241,440 | 137,131 |
| | `XLUA_LEAK_CHECKER=OFF` | 361,736 (0) | 156,174 (0) | 241,440 (0) | 137,131 (0) |
| | `XLUA_UINT64=OFF` | 360,328 (-1,408) | 155,708 (-466) | 240,528 (-912) | 136,750 (-381) |
| | minimal libraries | 343,568 (-18,168) | 148,640 (-7,534) | 229,256 (-12,184) | 130,131 (-7,000) |
| | reduced | 297,152 (-64,584) | 130,029 (-26,145) | 198,532 (-42,908) | 114,298 (-22,833) |

- **The leak checker** costs only where it is exported: with
  `XLUA_EXPORTS=managed` nothing references it and `--gc-sections` already
  drops it, and on iOS the linker dead-strips it (0 bytes below).
- **The uint64 table** is the five `uint64` functions and their registration;
  the push and get functions stay.
- **A library** saves about its file in the table at the top (`liolib.c`
  9,220 bytes against 9,344 without `io`), plus its strings and relocations;
  `xlua_libs.c` takes the place of `linit.c`.
- **"Reduced" is about the sum of its parts:** 65.4 KB on arm64-v8a against
  65.0 KB for the rows above plus LuaSocket's 44.6 KB from
  [without LuaSocket](#without-luasocket-or-mime).

LuaJIT 2.1, arm64-v8a, `XLUA_EXPORTS=all` (`make_android_luajit.sh` with the
options in the environment):

| build | stripped | APK |
| --- | ---: | ---: |
| default | 674,160 | 333,486 |
| `XLUA_LIBS=package,table,string,math,debug,jit` (no `io`, `os`, `bit`; `ffi` not preloaded) | 659,912 (-14,248) | 327,579 (-5,907) |
| the same and `XLUA_LUASOCKET=OFF` | 614,712 (-59,448) | 309,490 (-23,996) |

`lib_ffi.o` stays in the LuaJIT library either way: other parts of LuaJIT
reference it.

iOS arm64 (Lua 5.5.1, `make_ios_lua55.sh` with the options in the environment,
Xcode 27.1 beta): `__text` of a program that calls `luaL_newstate`,
`luaopen_xlua`, `luaopen_i64lib` and `lua_close`, linked against `libxlua.a`
with `-dead_strip`, as the earlier iOS measurements.

| build | `__text` | executable |
| --- | ---: | ---: |
| default | 264,172 | 387,944 |
| `XLUA_LEAK_CHECKER=OFF` | 264,172 (0) | 387,944 |
| `XLUA_UINT64=OFF` | 263,424 (-748) | 387,712 |
| minimal libraries | 249,640 (-14,532) | 368,288 |
| reduced (also `XLUA_MEMORY_SNAPSHOT=OFF`) | 215,144 (-49,028) | 309,832 |

Not measured: a Unity player with these libraries, x86 and x86_64, Lua 5.3 and
5.4, and the speed. The options leave code out; the code that stays is
compiled as before, except `luaL_openlibs`, which runs once per state.

## Presets

Measured 2026-10-18 like [the leak checker section](#leak-checker-uint64-table-and-standard-libraries):
the native library alone, NDK r30, API 23, the release scripts
(`build/make_android_lua55.sh`, `make_android_luajit.sh`; `minimal` is
`XLUA_VARIANT=minimal XLUA_PRESET=minimal` with the same script), stripped with
`llvm-strip --strip-unneeded`, "APK" the file compressed with zip deflate level 6.
The minimal preset's native options are in
[presets.md](../docs/design/presets.md#the-minimal-column-as-implemented): no
LuaSocket, mime, leak checker, memory snapshot or `io`, and the `managed` export
list.

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | legacy (main and this branch) | 383,368 | 163,547 |
| | minimal | 312,440 (-70,928) | 137,097 (-26,450) |
| | minimal with `XLUA_EXPORTS=all` | 327,768 (-55,600) | 141,388 (-22,159) |
| 5.5.1 armeabi-v7a | legacy | 256,980 | 143,511 |
| | minimal | 209,180 (-47,800) | 120,603 (-22,908) |
| | minimal with `XLUA_EXPORTS=all` | 220,252 (-36,728) | 124,168 (-19,343) |
| LuaJIT arm64-v8a | legacy | 674,432 | 333,509 |
| | minimal | 609,040 (-65,392) | 308,471 (-25,038) |

Since the [library parts](#parts-of-os-package-and-the-base-library) the minimal
library is 308,080 bytes stripped on 5.5.1 arm64-v8a (135,426 of APK), 206,504
on armeabi-v7a (119,262) and 609,600 on LuaJIT arm64-v8a (308,694).

- **Legacy is unchanged:** the stripped libraries of main (0435c75) and of this
  branch are equal byte for byte once `.note.gnu.build-id` is removed
  (`llvm-objcopy --remove-section`), on both ABIs. The unstripped files differ in
  their debug information, which records the source directory.
- **The sum of the parts:** the "reduced" row of the section above (-65,360 on
  arm64-v8a, every export) also leaves out `os`, `utf8` and the uint64 table
  (4,848, 3,168 and 1,456 bytes); with those back it would be -55,888, close to
  minimal with every export (-55,600). The `managed` export list saves another
  15,328 bytes stripped (4,291 of APK).
- **iOS arm64** (Lua 5.5.1, `make_ios_lua55.sh`, Xcode 27.1 beta, the probe of the
  section above with `-dead_strip`): `__text` 267,996 for legacy (264,172 in the
  default row above, before the core preset's milestones 3 and 4 added the
  limits) and 226,124 (-41,872) for minimal; the executable 388,608 and
  328,528. The archive is 626,736 and 486,616 bytes.
- **C#,** before the player run below: the per-define figures of
  [size reductions](#size-reductions) and `Test/MobileSmoke/README.md` (Unity
  6000.3.21f1, Minimal stripping, release ARM64 APK) add up to -262,952 bytes for
  `XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and
  `XLUA_NO_PROFILER` together (measured as a set), plus -569,044 for
  `XLUA_NO_STREAMING_ASSETS_LOADER` in a project that does not use
  `UnityWebRequest` otherwise.

### Players

Measured 2026-10-18 with `size_report.py run --configs minimal` (main at
`35165d02` plus the harness change that lets the plugin selector choose):
Unity 6000.3.21f1, release IL2CPP players of the size probe (`SizeProbe.cs`: a
`LuaEnv`, a chunk, one C# call each way), Minimal managed stripping, Lua 5.5.1
plugins from `build/make_android_lua55.sh` and `make_ios_lua55.sh` with and
without `XLUA_VARIANT=minimal XLUA_PRESET=minimal`. `ng-5.5+minimal` has both
libraries in the project and `XLUA_PRESET_MINIMAL` in the Player Settings
defines; the selector put the minimal one into the player (the build log says
so, the packed `libxlua.so` and the iOS link map have no `luaopen_io` or
`luaopen_socket_core`, and the Xcode project has only
`Libraries/Plugins/iOS/xlua-variants/minimal/libxlua.a`).

Android, ARM64 APK; component cells compressed in the APK / uncompressed:

| | APK | over baseline | `libxlua.so` | `libil2cpp.so` delta | `libunity.so` delta | `global-metadata.dat` delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 12,279,060 | | | | | |
| legacy (`ng-5.5`) | 13,690,038 | +1,410,978 | 163,688 / 383,672 | +574,612 / +1,923,184 | +544,071 / +1,101,520 | +126,950 / +359,096 |
| minimal | 12,719,518 | +440,458 | 137,273 / 312,744 | +260,605 / +948,712 | +4,445 / +10,720 | +36,477 / +114,940 |
| minimal - legacy | -970,520 | -68.8% | -26,415 / -70,928 | -314,007 / -974,472 | -539,626 / -1,090,800 | -90,473 / -244,156 |

iOS, ARM64, exported and compiled with `xcodebuild -sdk iphoneos
CODE_SIGNING_ALLOWED=NO DEPLOYMENT_POSTPROCESSING=YES` (stripped as in an
archive, not signed); "IPA" is the `.app` zipped with zip's default deflate,
the stand-in `size_report.py` uses for an IPA. Link map columns are live bytes
in `UnityFramework`:

| | `.app` zip | over baseline | `UnityFramework` delta | `libxlua.a` (link map) | `libGameAssembly.a` delta | `global-metadata.dat` delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 6,628,030 | | | | | |
| legacy (`ng-5.5`) | 7,393,553 | +765,523 | +1,830,128 | 303,477 | +1,447,808 | +323,296 |
| minimal | 7,022,263 | +394,233 | +1,037,416 | 256,908 | +719,020 | +115,056 |
| minimal - legacy | -371,290 | -48.5% | -792,712 | -46,569 | -728,788 | -208,240 |

- **Most of the saving is C#,** not the native library: on Android
  `libil2cpp.so` and metadata give 404,480 compressed bytes and `libunity.so`
  539,626, against 26,415 for `libxlua.so`. The `libunity.so` part is
  `XLUA_NO_STREAMING_ASSETS_LOADER`: without xLua's `UnityWebRequest` use,
  engine code stripping drops the module, as the `+uwr` rows of the default
  configuration showed. A project that uses `UnityWebRequest` itself keeps it;
  its saving would be about 430 KB on Android (an estimate: the total without
  the `libunity.so` part; not built).
- **iOS** keeps the web request code in `libiPhone-lib.a` either way (+9,760
  and +8,963 over the baseline), so its saving is the C# and the library.
- The ARM64 IL2CPP C# of the hardening defines and `XLUA_NO_LOAD_ASSEMBLY` is
  in these numbers but not split out.

Not measured: x86 and x86_64, Lua 5.3, 5.4 and LuaJIT players, armeabi-v7a,
Medium or High stripping, WebGL (whose plugin source does not go through the
preset), a signed IPA from an archive export, and the speed of the minimal C#
defines together (each one's cost is in
[bridge-hardening.md](../docs/design/bridge-hardening.md)).

### The core preset

Measured 2026-10-18 after milestone 5 of
[core-luascript.md](../docs/design/core-luascript.md) (section 8.4), the same
way: `XLUA_VARIANT=core XLUA_PRESET=core build/make_android_lua55.sh` (and
`lua54`), against legacy of the same tree. The core library has no bridge, no
LuaSocket, no `io`, `os`, `debug` or `package`, no bytecode loader or dumper,
and exports the 20 functions of `build/xlua_core_exports.txt`.

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | legacy | 383,672 | 163,690 |
| | core | 253,792 (-129,880) | 115,246 (-48,444) |
| 5.5.1 armeabi-v7a | legacy | 257,156 | 143,599 |
| | core | 171,456 (-85,700) | 101,693 (-41,906) |
| 5.4.9 arm64-v8a | legacy | 367,136 | 155,227 |
| | core | 239,096 (-128,040) | 107,872 (-47,355) |
| 5.4.9 armeabi-v7a | legacy | 247,484 | 137,017 |
| | core | 163,408 (-84,076) | 96,463 (-40,554) |

- **Budget:** core-luascript.md section 6 allows 290 KB stripped and 125 KB
  compressed for Lua 5.5.1 arm64-v8a; this is 253,792 and 115,246, before the
  exports of milestones 6 to 10 (calls, registration, tables, the `buffer`
  library).
- **The new files:** `xlua_core_sandbox.c` is 7,170 bytes of `.text` in its
  object on arm64-v8a (5,174 on armeabi-v7a) and `xlua_core_nobc.c` 152 (96);
  their functions in the linked library add up to 6,836 (4,770). The core
  build of `xlua_core.c` is 1,500 bytes of `.text` (5.5.1, arm64-v8a).
- **Legacy and minimal are unchanged:** after the change their stripped
  libraries (Lua 5.5.1 and 5.4.9 legacy, 5.5.1 minimal, both ABIs) equal
  main's (bacd3a8) once `.note.gnu.build-id` is removed. `XLUA_LUASCRIPT=ON`
  would add the sandbox there (about 7 KB of code by the figures above; not
  measured in an Android bridge library); it is off.

**After milestone 6** (2026-10-18, `xlua_core_call` replacing `xlua_core_run`;
section 8.5), arm64-v8a, the same build, against main `35165d0`; "APK" here is
Info-ZIP's deflate level 6 of the stripped file, which comes out a few hundred
bytes below the table above:

| Lua | build | main stripped | M6 stripped | main APK | M6 APK |
| --- | --- | ---: | ---: | ---: | ---: |
| 5.5.1 | core | 253,792 | 255,512 (+1,720) | 114,891 | 115,846 (+955) |
| 5.4.9 | core | 239,096 | 240,784 (+1,688) | 107,356 | 108,398 (+1,042) |
| 5.5.1 | legacy | 383,672 | 383,672 | 162,481 | 162,479 |
| 5.4.9 | legacy | 367,136 | 367,088 (-48) | 154,172 | 154,141 |

- 1,620 of the 1,720 bytes are `.text`: `xlua_core_call` with its dotted
  lookup and value conversion, against `xlua_core_run`. The core library
  exports 19 functions (`xlua_core_call` for `xlua_core_run`, no
  `lua_rawlen`).
- Exporting every import of `Assets/XLua/Core` instead (49 functions, the
  Lua C API that the bridge uses included) measured 260,608 bytes stripped
  and 117,655 compressed: 5,096 and 1,809 more, mostly `.dynsym`, `.dynstr`,
  `.gnu.hash` and the PLT and GOT entries of calls that became
  interposable. The short list is kept (core-luascript.md, question 17).
- Legacy does not compile the sandbox; 5.5.1 is the same size, 5.4.9 has 8
  bytes less `.text` and 48 bytes less padding, with the same 290 exports.
- **Managed** (IL of the asmdef layout for an Android IL2CPP player, Unity
  6000.3.26f1, `asmdef_compile_check.py`): main's `XLua.dll` is 232,960
  bytes; now `XLua.dll` is 227,328 and `XLua.Core.dll` 14,848 (+9,216 in
  all: the second assembly's metadata), and `XLua.Core.dll` with
  `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` is 37,376 (LuaScript, LuaValue and
  their exceptions: +22,528). A core-only project compiles `XLua.Core.dll`
  alone. IL2CPP code and metadata are measured with milestone 11.

## Parts of os, package and the base library

Measured 2026-10-18 like [the presets](#presets): NDK r30, API 23, the release
scripts, stripped with `llvm-strip --strip-unneeded`, "APK" zip deflate level 6.
"Before" is main (35165d02) built with `XLUA_VARIANT=minimal XLUA_PRESET=minimal
build/make_android_lua55.sh`, "after" this branch the same way, where the
minimal preset also sets `XLUA_OS_FULL`, `XLUA_LOADLIB` and `XLUA_DOFILE` to
`OFF` ([build/README.md](README.md#parts-of-os-package-and-the-base-library)).
The single-option rows are this branch's minimal library with the other two
options back `ON`.

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | minimal before | 312,744 | 137,271 |
| | minimal after | 308,080 (-4,664) | 135,426 (-1,845) |
| | only `XLUA_OS_FULL=OFF` | 310,736 (-2,008) | 136,464 (-807) |
| | only `XLUA_LOADLIB=OFF` | 310,728 (-2,016) | 136,398 (-873) |
| | only `XLUA_DOFILE=OFF` | 312,088 (-656) | 137,001 (-270) |
| 5.5.1 armeabi-v7a | minimal before | 209,356 | 120,716 |
| | minimal after | 206,504 (-2,852) | 119,262 (-1,454) |
| | only `XLUA_OS_FULL=OFF` | 208,120 (-1,236) | 120,112 (-604) |
| | only `XLUA_LOADLIB=OFF` | 208,092 (-1,264) | 120,116 (-600) |
| | only `XLUA_DOFILE=OFF` | 209,020 (-336) | 120,530 (-186) |
| LuaJIT arm64-v8a | minimal before | 609,040 | 308,472 |
| | minimal after | 609,600 (+560) | 308,694 (+222) |

- **Legacy is unchanged:** the stripped legacy libraries (Lua 5.5.1 on both
  ABIs, LuaJIT arm64-v8a) equal main's byte for byte once
  `.note.gnu.build-id` is removed, and so does this branch's minimal library
  with the three options `ON`. On the macOS host every object file of the
  legacy Lua 5.3.6, 5.4.9 and 5.5.1 builds equals main's.
- **What goes:** the six `os` functions (and the `exit`, `remove`, `rename`, `mkstemp` and
  `setlocale` imports; `getenv` stays for `LUA_PATH`), `package.loadlib` with the C searchers and Lua's
  `dlopen` code (`dlopen`, `dlsym`, `dlclose` and `dlerror` imports), and
  `dofile`/`loadfile`. `luaL_loadfilex` stays: the Lua file searcher uses it.
  The NDK still lists `libdl.so` as needed.
- **LuaJIT** grows: `libluajit.a` is not patched, so `xlua_libs.c` removes the
  fields at run time and the code stays.
- **Run time:** `LuaOptions.Libraries` and the reduced `debug` of
  `XLUA_REDUCED_DEBUG` are C#: no native size change; a `LuaEnv` that removes
  a library runs one small Lua chunk at construction.

Not measured: iOS, x86 and x86_64, Lua 5.3 and 5.4 on Android, a player.

## Generated bindings: Generate Code, GCM and the source generator

Measured 2026-10-18 for source generator milestone M6
([source-generator.md](../docs/design/source-generator.md#m6-as-implemented-2026-10-18)):
`Test/MobileSmoke/size_report.py run --platforms android --configs
sg-minimal,sg-high`, release ARM64 APKs from Unity 6000.3.21f1, Lua 5.5.1
plugin from `build/make_android_lua55.sh` (NDK r30) at `4ab2c296`, C# at
`4ab2c296` (the source generator variant at `225fc8e0`, which only adds a
`#pragma` to the generated registration). Every
variant binds the same configuration: `SizeGenConfig.cs` (the probe class,
`Vector3`, `GameObject`, `Transform`, `Debug`, `Func<int, int>` and
`Action<string>`) for Generate Code, and the same types as assembly attributes
(`SizeSgConfig.cs`) for the source generator. `+pkg` installs xLua as the UPM
package (`Tools/make_upm_package.py`, a `file:` tarball), so xLua compiles into
`XLua.dll`; without it xLua is in Assembly-CSharp. APK bytes, and the
`libil2cpp.so` delta over the baseline compressed in the APK / uncompressed:

| variant | Minimal: APK delta | Minimal: `libil2cpp.so` | High: APK delta | High: `libil2cpp.so` |
| --- | ---: | ---: | ---: | ---: |
| package, reflection only (`ng-5.5+pkg`) | +1,410,682 | +573,882 / +1,925,432 | +1,269,278 | +478,831 / +1,643,752 |
| Assets layout, Generate Code (`ng-5.5+gen`) | +1,638,654 | +745,575 / +2,612,016 | +1,521,034 | +671,508 / +2,401,344 |
| Assets layout, GCM (`ng-5.5+gen+gcm`) | +1,606,146 | +711,447 / +2,496,616 | +1,485,130 | +634,196 / +2,288,120 |
| package, Generate Code (`ng-5.5+pkg+gen`) | +1,639,710 | +744,977 / +2,614,336 | +1,521,482 | +671,378 / +2,405,928 |
| package, GCM (`ng-5.5+pkg+gen+gcm`) | +1,608,362 | +711,749 / +2,499,248 | +1,484,402 | +632,861 / +2,290,456 |
| package, source generator (`ng-5.5+pkg+sg`) | +1,619,530 | +735,240 / +2,562,016 | +1,500,490 | +658,709 / +2,354,408 |

Baselines (no xLua): 12,279,052 bytes with Minimal, 11,526,124 with High.
`libxlua.so` is 163,689 / 383,672 in every variant. The stripped managed
assemblies (`Library/Bee/.../ManagedStripped`), Assembly-CSharp + XLua:

| variant | Minimal | High |
| --- | ---: | ---: |
| package, reflection only | 5,632 + 221,696 | 4,096 + 150,528 |
| package, Generate Code | 87,040 + 221,696 | 84,480 + 161,280 |
| package, GCM | 78,848 + 222,720 | 76,288 + 162,304 |
| package, source generator | 77,824 + 237,568 | 75,264 + 177,152 |

- **The source generator against Generate Code:** 20,180 bytes smaller APK
  with Minimal, 20,992 with High (`libil2cpp.so` -52 KB uncompressed with
  both), and `libunity.so` keeps 12 KB less engine code (+1,167,744 against
  +1,179,984 uncompressed), probably because it does not write Generate
  Code's `link.xml` with `preserve="all"` for each `[LuaCallCSharp]` type
  (*not traced*).
- **GCM against the source generator:** GCM's APK is 11,168 bytes smaller with
  Minimal and 16,088 with High (`libil2cpp.so` 63 KB and 64 KB smaller
  uncompressed). Against plain Generate Code GCM saves 31,348 (Minimal) and
  37,080 (High), with about 115 KB less `libil2cpp.so`; the reverse P/Invoke
  wrappers of the `[MonoPInvokeCallback]` methods it does not have are the
  likely cause (*not traced*). With five wrapped types this is a small
  configuration; the difference should grow with the number of wrapped
  members.
- **XLua.dll is 16 KB larger with the source generator** (237,568 against
  221,696 with Minimal): XLua's own default `[GCOptimize]` pushers (M4), which
  Generate Code puts into Assembly-CSharp only when it is run.
- **The package layout costs nothing measurable:** with Generate Code and
  with GCM the package APKs are within 2.3 KB of the Assets layout APKs (the
  Lua files and metadata of a second assembly).
- **High stripping does little for generated bindings** (about 118 KB less
  APK, against 141 KB for the reflection-only player).

Not measured: iOS, WebGL, larger configurations (such as the editor suite's
or a game's), and Medium stripping.
