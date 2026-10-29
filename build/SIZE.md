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

## Stripped Android libraries

What `build/make_android_*.sh` copy into `Plugins/Android/libs/<abi>/` since
2026-10-18 (`llvm-strip --strip-unneeded`) against the linked library they
keep in `symbols/` ([README.md](README.md#stripped-libraries-and-symbols)).
NDK r30, API 23, legacy preset, sources of 2026-10-18 (`f0c6deda`); deflate is
zlib level 9 without header, close to what an APK stores. LuaJIT was built
for the 64-bit ABIs only (its 32-bit ABIs need gcc-multilib, which the macOS
host lacks). Bytes:

| runtime | ABI | linked | stripped | linked deflate | stripped deflate |
| --- | --- | ---: | ---: | ---: | ---: |
| Lua 5.3.6 | arm64-v8a | 1,638,552 | 314,792 | 520,862 | 133,126 |
| Lua 5.3.6 | armeabi-v7a | 1,125,952 | 208,500 | 455,344 | 115,325 |
| Lua 5.3.6 | x86 | 1,197,040 | 320,660 | 501,954 | 145,035 |
| Lua 5.3.6 | x86_64 | 1,555,296 | 306,280 | 525,774 | 137,507 |
| Lua 5.4.9 | arm64-v8a | 1,975,456 | 367,504 | 625,724 | 154,942 |
| Lua 5.4.9 | armeabi-v7a | 1,386,760 | 247,724 | 562,018 | 137,118 |
| Lua 5.4.9 | x86 | 1,459,624 | 380,628 | 612,269 | 170,308 |
| Lua 5.4.9 | x86_64 | 1,859,720 | 354,344 | 627,779 | 160,765 |
| Lua 5.5.1 | arm64-v8a | 2,066,128 | 384,040 | 655,626 | 163,519 |
| Lua 5.5.1 | armeabi-v7a | 1,437,032 | 257,396 | 585,475 | 143,651 |
| Lua 5.5.1 | x86 | 1,513,800 | 395,616 | 638,611 | 178,738 |
| Lua 5.5.1 | x86_64 | 1,928,360 | 369,400 | 653,754 | 168,311 |
| LuaJIT | arm64-v8a | 1,111,368 | 674,416 | 452,880 | 332,811 |
| LuaJIT | x86_64 | 1,119,120 | 727,664 | 463,304 | 348,002 |

The stripped file is what Unity 6000.x packs from the linked one anyway; in a
Unity 2021.3 APK, which packs plugins as they are, the arm64-v8a library goes
from about 655 KB to 164 KB compressed. A minimal-preset variant
(`XLUA_VARIANT=minimal XLUA_PRESET=minimal`, Lua 5.5.1, arm64-v8a) is 1,744,560
linked and 308,464 stripped.

A release ARM64 APK from Unity 2021.3.45f2 (`size_report.py run --platforms
android --variants ng-5.5 --configs default`, Minimal stripping, 2026-10-18)
is 9,642,938 bytes with the linked library (`XLUA_ANDROID_STRIP=OFF`;
`libxlua.so` 2,066,128, 663,305 compressed) and 9,143,534 with the stripped
one (384,040, 163,900 compressed): 499,404 bytes less. A Unity 6000.3.21f1
APK stores the stripped file as it is (384,040, 163,900 compressed in the
hotfix IL2CPP player of `hotfix_il2cpp.py`).

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
  Since 2026-10-18 the minimal preset keeps the memory snapshot walk in Apple
  static libraries (an iOS Development player did not link without it;
  [README.md](README.md#memory-snapshot)): the minimal archive is 493,832 bytes
  instead of 479,688 built from the same tree without it. Release players are
  unchanged: nothing references `memory_leak_checker.o`, and the link map of a
  minimal release simulator player has none of it.
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

### Reflection off in the minimal preset

Measured 2026-10-19 with `size_report.py run --configs minimal --variants
baseline,ng-5.5,ng-5.5+at-9c166e9d,ng-5.5+minimal,ng-5.5+minimal+at-9c166e9d`:
the same setup as [Players](#players) above (Unity 6000.3.21f1, release IL2CPP,
ARM64, Minimal stripping, the size probe, Lua 5.5.1 plugins with the minimal
variant), main at `9c166e9d` (`+at-9c166e9d`) against the change that turns
off the reflection fallback and CodeEmit in the minimal preset
(`XLUA_NO_REFLECTION`, [presets.md](../docs/design/presets.md#reflection-fallback-and-codeemit-as-implemented)).
Component cells are compressed in the APK / uncompressed; deltas over the
baseline (12,279,056):

| variant | APK | over baseline | `libil2cpp.so` | `global-metadata.dat` |
| --- | ---: | ---: | ---: | ---: |
| legacy, main | 13,728,202 | +1,449,146 | +604,919 / +2,039,248 | +133,980 / +383,544 |
| legacy, this change | 13,728,314 | +1,449,258 | +604,919 / +2,039,248 | +134,084 / +384,016 |
| minimal, main | 12,756,302 | +477,246 | +291,262 / +1,067,144 | +43,618 / +138,700 |
| minimal, this change | 12,712,918 | +433,862 | +248,944 / +881,952 | +42,551 / +135,828 |
| minimal: change | -43,384 | -9.1% | -42,318 / -185,192 | -1,067 / -2,872 |

- **Where it comes from:** the reflection wrappers (`Utils.ReflectionWrap`,
  `makeReflectionWrap`, `ReflectionMembers`, the indexer and enum helpers,
  `MakePrivateAccessible`) are not compiled; Minimal stripping keeps the user
  assemblies whole, so leaving the code out is what removes it. The
  metatables for types without bindings (`Utils.UnboundWrap`) and the C#
  `pairs` are what the change adds. `libxlua.so` (136,139 / 309,216) and
  `libunity.so` do not change.
- **Legacy** differs by 112 bytes of APK: `libil2cpp.so` is the same, and the
  metadata holds `xlua.util`'s text, which changed.
- **Host:** `XLua.Mini.dll` (`XLUA_GENERAL`, which also had CodeEmit) with
  `XLUA_PRESET_MINIMAL`: 262,144 to 217,600 bytes (-44,544).

Not measured: iOS, WebGL, Medium or High stripping, a player with the source
generator (the `+pkg+sg` variants take no preset variant), and the run-time
cost.

**Measured again on 2026-10-28** with `size_report.py run --configs minimal`
(the same setup, Android only), main at `d37515c2` with the Lua 5.5.1
plugins rebuilt from it: after overload selection by best fit, the bridge
checks by build type (off in these release players), the #999
`ObjectDisposedException` and the reproducible native builds. Deltas over the
baseline (12,279,056):

| variant | APK | over baseline | `libxlua.so` | `libil2cpp.so` | `global-metadata.dat` |
| --- | ---: | ---: | ---: | ---: | ---: |
| legacy | 13,736,466 | +1,457,410 | 164,800 / 385,720 | +611,506 / +2,061,512 | +135,245 / +387,576 |
| minimal | 12,722,674 | +443,618 | 136,412 / 310,016 | +256,975 / +902,784 | +44,008 / +139,040 |
| minimal - legacy | -1,013,792 | -69.6% | -28,388 / -75,704 | -354,531 / -1,158,728 | -91,237 / -248,536 |

Against "this change" above, minimal grew by 9,756 bytes of APK
(`libil2cpp.so` +8,031 compressed, metadata +1,457, `libxlua.so` +273) and
legacy by 8,152; not split by change. `libunity.so` is unchanged (+4,444 /
+10,720 for minimal). A first run on main at `b3adc8e8`, before the
reproducible builds, gave the same numbers within 16 bytes.

**The minimal player as shipped, 2026-10-28.** The `minimal` row above has
no reflection fallback and no generated bindings, so its size probe cannot
run: on the `xlua16k` emulator the player raised `cannot use
SizeProbeApi.Add: SizeProbeApi has no generated bindings ...`. Two working
alternatives, measured with `size_report.py run --configs strict` (new
variants, the same setup, main at `aadd1d87`, whose runtime C# and native code equal
`d37515c2`'s, with the same plugins): `+reflection` adds `XLUA_REFLECTION`,
and `+strict` installs the source generator in the Assets layout and binds
what the probe calls (`[assembly: LuaCallCSharp(typeof(SizeProbeApi))]`), in
the GCM mode the preset implies. Both probes printed `XLUA_SIZE_PROBE: Lua
5.5 42` on the emulator. Deltas over the baseline (12,279,048):

| variant | APK | over baseline | `libil2cpp.so` | `global-metadata.dat` | stripped `XLua.dll` | stripped `Assembly-CSharp.dll` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| minimal, nothing bound (does not run) | 12,722,698 | +443,650 | +256,980 / +902,784 | +44,008 / +139,040 | 196,096 | 5,632 |
| minimal with `XLUA_REFLECTION` | 12,769,018 | +489,970 | +302,051 / +1,089,632 | +45,274 / +142,200 | 202,240 | 5,632 |
| strict minimal (source generator) | 12,778,210 | +499,162 | +307,828 / +1,156,008 | +48,540 / +150,400 | 212,480 | 7,680 |

`libxlua.so` (136,412 / 310,016) and `libunity.so` are the same in all
three. The unbound row matches the table above within 32 bytes.

- **Strict costs 9,192 bytes more than keeping reflection** for this one
  bound method, and 55,512 more than the unbound row. Most of it is what the
  generator writes into `XLua.dll` itself, which it compiles in a generator
  project whatever the project binds (`XLua.dll` before stripping: 339,456
  bytes without the generator, 360,448 with it; after stripping +16,384 over
  the unbound row, +10,240 over reflection). `Assembly-CSharp.dll` grows by
  2,048 with `SizeProbeApi`'s GCM wrapper and its registration. The
  reflection wrappers that `XLUA_NO_REFLECTION` leaves out (about 43 KB,
  [above](#reflection-off-in-the-minimal-preset)) are still out in the
  strict row.
- So a project that binds a handful of methods pays about the same either
  way; the generator's fixed part is split in the next section.

Not measured: iOS, High stripping, a project with more bindings (the
`+pkg+sg` rows of [Generated bindings](#generated-bindings-generate-code-gcm-and-the-source-generator)
are the legacy preset).

#### The generator's code in `XLua.dll` (2026-10-28)

The owner's question of 2026-10-28 (Q18): where the fixed cost of the
source generator in `XLua.dll` comes from, and how to shrink it without
changing behaviour. The change is described in
[source-generator.md](../docs/design/source-generator.md#xluadlls-own-bindings-2026-10-28).

**What the generator writes into `XLua.dll`.** The analyzer sits next to
`XLua.asmdef`, so it also runs in `XLua.dll` and binds xLua's own
configuration. xLua has no `[CSharpCallLua]` and no `[LuaCallCSharp]`
types, so the unit holds only the eight UnityEngine GCOptimize defaults of
`GenAttributes.cs` (`Vector2`, `Vector3`, `Vector4`, `Color`, `Quaternion`,
`Ray`, `Ray2D`, `Bounds`): `Structs` (push, get and update per type),
`PackUnpack`, the registration (`Bindings.Register` with the eight name
table entries) and the manifest (Editor and host builds only). Its IL is
7,268 bytes before the change and 7,193 after (`ikdasm` of `XLua.dll`
compiled by `editor_compile_check.py`, `player-android-il2cpp-minimal-source-gen`,
6000.3.21f1's Roslyn).

**Where the bytes went.** Symbols of `libil2cpp.dbg.so` that the strict
player adds over the unbound minimal one (`llvm-nm -S` of both, each
symbol attributed by its definition in `il2cppOutput/cpp`), uncompressed
bytes:

| part | before | after |
| --- | ---: | ---: |
| delegate types instantiated per struct type (`Invoke` with its seven variants, constructor, `BeginInvoke`) | 57,324 | 5,140 |
| generic registration code per struct type: `AssemblyBindings.Struct<T>`, `ArrayAccessor<T>`, `ObjectTranslator.RegisterPushAndGetAndUpdate<T>` and their closure classes | 26,448 | 0 |
| data: method pointer, generic method and metadata tables | 35,398 | 13,341 |
| generated `Structs` | 13,644 | 20,032 |
| generated registration (`Bindings.Register` and its lambdas) | 8,508 | 1,904 |
| generated `PackUnpack` | 7,732 | 7,732 |
| `T[]` interface code for `Color`, `Vector2`, `Ray` and `Ray2D` arrays | 8,096 | 8,096 |
| reflection invokers (`Il2CppInvokerTable`) | 4,364 | 3,668 |
| other | 6,116 | 5,828 |
| `Assembly-CSharp` (`SizeProbeApi`'s wrapper and registration) | 1,100 | 1,100 |
| total | 168,730 | 66,841 |

Before the change each of the eight types made IL2CPP compile nine
delegate types (`Action<ObjectTranslator, IntPtr, T>`, `StructGetter<T>`,
`Action<ObjectTranslator, IntPtr, int, T>`, `Action<IntPtr, T>`,
`ObjectTranslator.GetFunc<T>`, `Action<IntPtr, int, T>`,
`Func<IntPtr, int, T>`, `ArrayElementPusher<T>`, `ArrayElementSetter<T>`)
and six generic methods with their closures: two thirds of the cost. Now
each type has one generated `Ops_` method that the runtime's non-generic
closures call, and only `Action<IntPtr, T>` and `Func<IntPtr, int, T>`
remain (the typed paths of `PushByType` and `Get<T>`, which do not box).
The `T[]` code comes from the array accessors (`typeof(T[])`, `(T[])`)
and stays; `Vector3[]`, `Vector4[]`, `Quaternion[]` and `Bounds[]` already
have it in the unbound player.

**Sizes.** `size_report.py run --configs strict,sg-minimal` (the variants
of the [strict table](#reflection-off-in-the-minimal-preset) and
`ng-5.5+pkg+sg`, the legacy preset with the generator), Unity 6000.3.21f1,
Android ARM64 IL2CPP release, Minimal stripping, the Lua 5.5.1 plugins of
the table above. Before: main at `3042ef63` (this table's rows grew since
`aadd1d87` with the merges in between); after: the same with this change.
APK bytes over the baseline of the same run (12,279,052 to 12,279,060):

| variant | before | after | change |
| --- | ---: | ---: | ---: |
| minimal, nothing bound (does not run) | +451,070 | +447,602 | -3,468 |
| minimal with `XLUA_REFLECTION` | +497,642 | +491,874 | -5,768 |
| strict minimal (source generator) | +505,750 | +474,990 | **-30,760** |
| legacy with the source generator (`ng-5.5+pkg+sg`) | +1,679,538 | +1,646,306 | **-33,232** |

The components after the change, compressed in the APK / uncompressed
(before in brackets):

| variant | `libil2cpp.so` | `global-metadata.dat` | stripped `XLua.dll` |
| --- | ---: | ---: | ---: |
| minimal, nothing bound | +260,156 / +892,352 (+262,880 / +918,712) | +44,812 / +139,944 (+45,550 / +142,056) | 200,704 (201,216) |
| minimal with `XLUA_REFLECTION` | +303,231 / +1,078,880 (+308,398 / +1,105,536) | +46,000 / +142,712 (+46,601 / +144,840) | 205,824 (206,848) |
| strict minimal | +283,551 / +993,456 (+313,072 / +1,173,360) | +48,655 / +149,340 (+49,890 / +153,424) | 212,992 (218,112) |
| `ng-5.5+pkg+sg` | +749,828 / +2,544,224 (+782,090 / +2,722,544) | +155,185 / +453,188 (+156,154 / +457,180) | 276,480 (281,088) |

- **Strict minimal** now costs 27,388 bytes over the unbound player
  (54,680 before) and is 16,884 bytes smaller than keeping reflection (it
  was 8,108 larger). `libxlua.so`, `libunity.so` and `Assembly-CSharp.dll`
  are unchanged.
- **The players without the generator** got smaller too: binding API
  version 2 removed the generic `Struct<T>`, `Caster<T>` and
  `ArrayAccessor<T>` and their delegate types, which Minimal stripping kept
  in `XLua.dll` (IL2CPP compiled them once, fully shared); the non-generic
  forms that replace them cost less. With version 1's generic forms kept
  next to the new ones (an intermediate build), the unbound player was
  3,716 bytes larger than before instead of 3,468 smaller.
- **Steps** (strict APK over the unbound player of the "before" run, one
  build each): the non-generic registration with one method per delegate
  +33,148 (from +54,680); shared error messages +32,732; one `Ops_` method
  per type +31,096; the final build's own unbound player is the reference
  of the table above.

Not measured: iOS, High or Medium stripping (where unused binding API code
is stripped anyway), WebGL, a project with many GCOptimize types or
`[LuaCallCSharp]` enums (which get the same per-type saving), and the
run-time cost (one more delegate call on the boxed push, get and update of
a struct; none on the array accessors and the typed paths).

**Measured again in the final verification round (2026-10-28).**
`size_report.py run --configs strict --variants
baseline,ng-5.5,ng-5.5+minimal,ng-5.5+minimal+reflection,ng-5.5+minimal+strict`:
the same setup (Unity 6000.3.21f1, Android ARM64 IL2CPP release, Minimal
stripping), the C# of main at `fe4f71dc`, the Lua 5.5.1 plugins built from
`a2f8ad12` (the merge in between changed only the Lua 5.4 branch of
`xlua_gc`). After the day's runtime changes (overload scoring, the owner
thread, binding-mode parity, the struct, enum and `Nullable` rules, the
registry checks, native bridge version 112). The strict, reflection and
plain legacy probes printed `XLUA_SIZE_PROBE: Lua 5.5 42` on the `xlua16k`
emulator. Deltas over the baseline (12,279,060); components compressed in
the APK / uncompressed:

| variant | APK | over baseline | change from "after" above | `libxlua.so` | `libil2cpp.so` | `global-metadata.dat` | stripped `XLua.dll` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| legacy (`ng-5.5`) | 13,745,798 | +1,466,738 | +9,328 over `d37515c2` | 165,597 / 388,136 | +619,224 / +2,068,472 | +136,071 / +391,368 | 266,752 |
| minimal, nothing bound (does not run) | 12,731,870 | +452,810 | +5,208 | 137,270 / 312,176 | +262,998 / +909,544 | +46,317 / +143,720 | 205,312 |
| minimal with `XLUA_REFLECTION` | 12,778,266 | +499,206 | +7,332 | 137,270 / 312,176 | +308,351 / +1,096,616 | +47,369 / +146,012 | 209,920 |
| strict minimal (source generator) | 12,760,054 | +480,994 | +6,004 | 137,270 / 312,176 | +287,436 / +1,012,336 | +49,919 / +153,264 | 218,624 |

- Strict minimal still costs less than keeping reflection (18,212 bytes)
  and 28,184 more than the unbound player.
- The rows grew by 5 to 9 KB: `libil2cpp.so` +2,842 to +5,120 compressed,
  the metadata +1,264 to +1,505, `libxlua.so` +797 compressed for legacy
  and +858 for minimal (bridge version 112: struct bounds, protected
  helpers, `set_limits`; 388,136 bytes against 385,720 at `d37515c2` for
  legacy). Not split by change.
- `libunity.so` is unchanged; `Assembly-CSharp.dll` too (5,632, and 7,680
  with the strict probe's wrapper).

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
- After merging main `4eac4ff4` (the bytecode options, whose `xlua_core_run_mode`
  had added 488 bytes to every core library, and the load mode now in
  `xlua_core_call`): core 5.5.1 is 255,720 bytes stripped and 116,214
  compressed against main's 254,280 and 115,376 (+1,440 and +838), core 5.4.9
  240,992 and 108,534 against 239,584 and 107,764 (+1,408 and +770); legacy as
  above.
- **Managed** (IL of the asmdef layout for an Android IL2CPP player, Unity
  6000.3.26f1, `asmdef_compile_check.py`): main's `XLua.dll` is 232,960
  bytes; now `XLua.dll` is 227,328 and `XLua.Core.dll` 14,848 (+9,216 in
  all: the second assembly's metadata), and `XLua.Core.dll` with
  `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` is 37,376 (LuaScript, LuaValue and
  their exceptions: +22,528). A core-only project compiles `XLua.Core.dll`
  alone. IL2CPP code and metadata are measured with milestone 11.

**After milestone 7** (2026-10-18, `LuaScript.Register`, section 8.6),
arm64-v8a, the same build and zip, against main `65f7ea17`:

| Lua | build | main stripped | M7 stripped | main APK | M7 APK |
| --- | --- | ---: | ---: | ---: | ---: |
| 5.5.1 | core | 255,720 | 260,488 (+4,768) | 116,214 | 118,476 (+2,262) |
| 5.4.9 | core | 240,992 | 245,824 (+4,832) | 108,533 | 110,890 (+2,357) |
| 5.5.1 | legacy | 383,672 | 383,640 (-32) | 162,481 | 162,475 |
| 5.4.9 | legacy | 367,088 | 367,136 (+48) | 154,139 | 154,182 |

- `.text` grows by 4,012 bytes (5.5.1): the trampoline 1,108 (`-O3`,
  `xlua_core.c` is a hot file), `register_body` with the environment update
  1,136, `raise_frame_error` 340, `xlua_core_register` 304,
  `xlua_core_check_stop` 148, `push_weak_keys` 100, and `xlua_core_call` 176
  more (the host function's thread and the host error flag). The rest is the
  two new exports' symbols, unwind tables and constants. Still within the
  290 KB and 125 KB budget of core-luascript.md section 6.
- Legacy does not compile the sandbox or the trampoline; the few bytes come
  from the larger `xlua_core_t` (three fields for host functions).
- **With milestone 10** (merged with main `4b0aa92d`, the buffer library, and
  buffer arguments and results of host functions): core 5.5.1 268,488 bytes
  stripped and 121,532 compressed against that main's 263,544 and 119,272
  (+4,944 and +2,260), core 5.4.9 253,808 and 114,158 against 248,832 and
  111,829. 21.5 KB and 3.5 KB of section 6's budget are left for milestones
  8, 9 and 11.
- **Managed:** `XLua.Core.dll` with `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` is
  56,320 bytes (+18,944 for `LuaCall.cs`: `LuaCall`, the dispatcher, the
  arena and the typed shorthands), the same for Unity 2021.3.45f2 and
  6000.3.26f1; without the define it stays 14,848, as the frame struct and
  the dispatcher delegate compile only with it.

**After milestone 8** (2026-10-18, table and function handles, section 8.7 of
core-luascript.md), arm64-v8a, the same build and zip, main `1ff0609d` against
the branch merged with main `c74f4bbc` (no native change in between):

| Lua | build | main stripped | M8 stripped | main zip | M8 zip |
| --- | --- | ---: | ---: | ---: | ---: |
| 5.5.1 | core | 268,488 | 270,008 (+1,520) | 121,531 | 122,301 (+770) |
| 5.4.9 | core | 253,808 | 255,296 (+1,488) | 114,158 | 115,000 (+842) |
| 5.5.1 | legacy | 383,640 | 383,672 (+32) | 162,476 | 162,464 |
| 5.4.9 | legacy | 367,136 | 367,136 | 154,179 | 154,179 |

- **Where it goes** (5.5.1, `llvm-nm` of the unstripped library, functions):
  `handle_body` 1,500 and `xlua_core_handle` 400 (the one handle export),
  `to_value` 348, `xlua_core_push_ref` 236, `xlua_core_push_value` 232 and
  `type_of` 188 (the value conversion that `xlua_core_call`, the handle
  export and the trampoline now share), `xlua_core_new_buffer` 112, and 64 to
  84 more in `call_msgh` (the error value), `xlua_core_register` (a table as
  the target), `register_body` and `open_body`: +3,243. Removed or smaller:
  `xlua_core_newbuffer` 456, `call_body` 316, `push_handle` 188, the
  trampoline 168 (its result switch), `xlua_core_tobuffer` 128,
  `newbuffer_body` 116, `xlua_core_push_buffer_ref` 100, `xlua_core_newenv`
  and `newenv_body` 152, the profiler's paths in `core_hook` and
  `arm_thread` 64: -1,700. Net +1,543 bytes of functions; two exports fewer.
- **Budget:** 290 KB and 125 KB (section 6); 20.0 KB and 2.7 KB are left for
  milestones 9 and 11 (5.5.1).
- **Legacy** does not compile the sandbox: its functions are the same byte for
  byte (`llvm-nm`); the 32 bytes of 5.5.1 are section padding, which moved by
  the same amount between earlier merges.
- **Managed:** `XLua.Core.dll` with `XLUA_CORE_ONLY` (and with
  `XLUA_LUASCRIPT`), compiled by `asmdef_compile_check.py` against Unity
  6000.3.26f1: 65,536 bytes before, 92,672 after (+27,136). Its IL code grows
  from 19,771 to about 32,300 bytes (`monodis`): `LuaScript` +6,660 (the
  handle slots, iteration, `ToDictionary`, the table operations and the
  environment overloads), `LuaTableRef` 2,224, `LuaFunctionRef` 1,001,
  `LuaPair` 614, `LuaCall` +366, `LuaValue` +396, `LuaPairs` 304; the rest of
  the file is metadata. A first version with a typed getter for every key and
  type combination and the full `Call` set on `LuaTableRef` was 93,696 bytes
  and instantiated a generic reader per type; the named getters now share one
  non-generic path. IL2CPP code was not measured (no player build in this
  milestone; question 44 of core-luascript.md).

**After milestone 9** (2026-10-18, coroutines, section 8.8 of
core-luascript.md), arm64-v8a, the same build and zip, main `98b3bab5` against
the branch:

| Lua | build | main stripped | M9 stripped | main zip | M9 zip |
| --- | --- | ---: | ---: | ---: | ---: |
| 5.5.1 | core | 270,008 | 271,320 (+1,312) | 122,301 | 122,992 (+691) |
| 5.4.9 | core | 255,296 | 256,608 (+1,312) | 114,999 | 115,666 (+667) |
| 5.5.1 | legacy | 383,672 | 383,672 | | 162,470 |
| 5.4.9 | legacy | 367,136 | 367,136 | | 154,179 |

Merged with main `446dea2b` (the on-time stops of section 8.11), against that
main: core 5.5.1 270,024 to 271,336 (+1,312) and 122,319 to 122,998 (+679);
legacy 5.5.1 383,688 on both.

- **Where it goes** (5.5.1, `llvm-nm`, functions): `call_body` +580 (the
  coroutine step of `xlua_core_call` is inlined into it), `describe_error` 328
  and `call_error` 148 (moved out of `call_msgh`, -248), `co_status` 224,
  `xlua_core_switch` 92 (formerly the static `switch_thread`, -92),
  `xlua_core_call` +40, `to_value` +28, `handle_body` +20, the trampoline +16:
  +1,132 bytes of functions. No export was added (21).
- **Budget:** 290 KB and 125 KB (section 6); 18.7 KB and 2,002 bytes are left
  for milestone 11 (5.5.1, merged).
- **Managed:** `XLua.Core.dll` with `XLUA_CORE_ONLY`, compiled against Unity
  6000.3.26f1: 92,672 bytes before, 104,960 after (+12,288). The drivers
  (`LuaCoroutineDrivers.cs`, with `RunAwaitable`) are about 5.6 KB of it
  (99,328 without the file; the define `XLUA_NO_COROUTINE_DRIVERS` leaves them
  out). The design's `LuaCoroutine.Get<T>` with a reader table per type was
  another 2.5 KB (107,520) and was left out.

#### The buffer library

Milestone 10 (2026-10-18, core-luascript.md section 8.9): the `buffer`
library and its C exports (`xlua_core_buffer.c`, CMake `XLUA_BUFFER`, on in
core), with the buffer cases of `xlua_core_call` and `xlua_core_unref` in the
sandbox. Android API 23, NDK r30, release, default flags, stripped, Info-ZIP
deflate 6 of the stripped file; main `65f7ea17` against this branch, and the
branch with `XLUA_BUFFER=OFF`:

| Lua, ABI | main stripped | buffer | `XLUA_BUFFER=OFF` | main zip | buffer | `OFF` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 5.5.1 arm64-v8a | 255,720 | 263,544 (+7,824) | 255,928 (+208) | 116,214 | 119,272 (+3,058) | 116,310 (+96) |
| 5.5.1 armeabi-v7a | 172,980 | 177,884 (+4,904) | 173,124 (+144) | 102,466 | 105,248 (+2,782) | 102,528 (+62) |
| 5.4.9 arm64-v8a | 240,992 | 248,832 (+7,840) | 241,200 (+208) | 108,534 | 111,829 (+3,295) | 108,680 (+146) |
| 5.4.9 armeabi-v7a | 164,948 | 169,900 (+4,952) | 165,092 (+144) | 97,224 | 100,008 (+2,784) | 97,286 (+62) |
| 5.5.1 arm64-v8a, legacy | 383,672 | 383,672 | 383,672 | 162,481 | 162,479 | 162,480 |

- **Budget:** core 5.5.1 arm64-v8a is 263,544 bytes stripped and 119,272
  compressed, within section 6's 290 KB and 125 KB; 26.5 KB and 5.7 KB are
  left for milestones 7 to 9.
- **Where it goes** (arm64-v8a object, `-Oz`): 4,680 bytes of functions, of
  which the 16 `read`/`write` entry points are 448 and their two shared
  bodies 640, `xlua_core_newbuffer` 456 (the inlined entry protocol),
  `copy`, `fill`, `writestring`, `readstring` and `fromstring` 1,256; 520
  bytes of messages and names, 400 of the function table (with its
  relocations), and about 1.3 KB of unwind data. The design's estimate was
  about 5 KB.
- **`XLUA_BUFFER=OFF`** keeps the two exports as stubs and the buffer cases
  of the sandbox out: 208 bytes over main (144 on armeabi-v7a).
- **Legacy** does not compile the sandbox or the buffer and is unchanged (the
  zip differs by the build ID).
- **`buffer.isbuffer`, `readi64` and `writei64`** (2026-10-18, core-luascript.md
  section 8.13), main `4449c12b` against this branch, both with
  `XLUA_CHEAP_HARDEN=OFF`: core 5.5.1 271,704 to 272,088 bytes stripped
  (+384) and 123,211 to 123,316 compressed (+105) on arm64-v8a, +184 and +97
  on armeabi-v7a; core 5.4.9 +368 and +123, +184 and +69. The section's
  estimate was about 0.1 KB for the 64-bit pair and nothing for `isbuffer`;
  each new function costs an entry in the function table with its
  relocation and its unwind data besides its few instructions.

### LuaScript in players

Measured 2026-10-18 for milestone 11 of
[core-luascript.md](../docs/design/core-luascript.md) (section 8.10), main at
`1ff0609d` (core milestones 6, 7 and 10) plus the harness of this
measurement: `size_report.py run --configs core,core-high`, Unity
6000.3.21f1, release ARM64 IL2CPP APKs, Lua 5.5.1, NDK r30, API 23. Every
xLua variant installs the UPM package (`Tools/make_upm_package.py`) with one
Android library as its plugin:

- `ng-5.5+pkg`: the legacy library (383,640 bytes stripped), `SizeProbe.cs`
  (a `LuaEnv`, a chunk, one C# call each way);
- `core-5.5`: the core library (`XLUA_PRESET=core`, 268,488 stripped,
  121,529 zip -6), `XLUA_CORE_ONLY` in the Player Settings, and
  `SizeProbeCore.cs`: a `LuaScript` with memory and instruction limits and the
  `buffer` library, one `Register<long, long, long>`, a chunk, `Call<long>`
  twice, a `NewBuffer` passed to Lua, `Load<string>`;
- `core-5.5+nodefine`: the same with `XLUA_LUASCRIPT` instead of
  `XLUA_CORE_ONLY`, so the `XLua` assembly is compiled into the player and
  nothing references it;
- `mixed-5.5`: the legacy library built with `XLUA_LUASCRIPT=ON
  XLUA_BUFFER=ON` (405,816 stripped), `XLUA_LUASCRIPT`, and both probes in one
  player.

APK bytes; component cells are compressed in the APK / uncompressed, as
deltas over the baseline (an empty project with the same probe class):

| Minimal stripping | APK | over baseline | `libxlua.so` | `libil2cpp.so` | `global-metadata.dat` | `libunity.so` | assets |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 12,279,052 | | | | | | |
| legacy (`ng-5.5+pkg`) | 13,726,794 | +1,447,742 | 163,693 / 383,640 | +607,305 / +2,077,528 | +130,884 / +372,044 | +544,069 / +1,101,520 | +1,661 / +4,545 |
| `core-5.5` | 12,502,582 | **+223,530** | 121,686 / 268,488 | +84,624 / +347,584 | +16,554 / +47,620 | 0 / 0 | +538 / +1,427 |
| `core-5.5+nodefine` | 12,503,870 | +224,818 | 121,686 / 268,488 | +84,625 / +347,584 | +16,554 / +47,620 | -1 / 0 | +1,833 / +5,137 |
| `mixed-5.5` | 13,814,374 | +1,535,322 | 173,011 / 405,816 | +676,165 / +2,349,008 | +140,066 / +404,256 | +544,062 / +1,101,520 | +1,892 / +5,297 |

| High stripping | APK | over baseline | `libxlua.so` | `libil2cpp.so` | `global-metadata.dat` | `libunity.so` | assets |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 11,526,128 | | | | | | |
| legacy (`ng-5.5+pkg`) | 12,830,998 | +1,304,870 | 163,693 / 383,640 | +512,218 / +1,778,432 | +82,841 / +265,472 | +544,333 / +1,101,488 | +1,660 / +4,545 |
| `core-5.5` | 11,719,930 | **+193,802** | 121,686 / 268,488 | +61,329 / +238,728 | +10,128 / +28,124 | -3 / 0 | +539 / +1,427 |
| `core-5.5+nodefine` | 11,721,218 | +195,090 | 121,686 / 268,488 | +61,327 / +238,728 | +10,128 / +28,124 | 0 / 0 | +1,832 / +5,137 |
| `mixed-5.5` | 12,894,346 | +1,368,218 | 173,011 / 405,816 | +558,262 / +1,957,296 | +90,596 / +287,380 | +544,333 / +1,101,488 | +1,891 / +5,297 |

- **Core-only against legacy:** -1,224,212 bytes of APK with Minimal
  stripping, -1,111,068 with High. Most of it is the bridge's IL2CPP code and
  metadata and the engine code (`libunity.so`, the `UnityWebRequest` module
  that `LuaEnv`'s StreamingAssets loader keeps); a core-only player keeps no
  engine code for xLua.
- **What LuaScript costs a game that ships `LuaEnv`** (`mixed-5.5` minus
  legacy): +87,580 bytes with Minimal stripping (`libxlua.so` +9,318 for the
  sandbox and the buffer library, `libil2cpp.so` +68,860, metadata +9,182) and
  +63,348 with High (+9,318, +46,044, +7,755).
- **The stripping question** (section 6 of core-luascript.md): Unity's
  managed stripping drops the unreferenced `XLua` assembly by itself, at
  Minimal as well as High. `core-5.5+nodefine` has the same `libil2cpp.so`
  (within 2 bytes) and the same `global-metadata.dat` as `core-5.5`, its
  stripped assemblies (`Library/Bee/artifacts/Android/ManagedStripped`) have
  `XLua.Core.dll` only, and the metadata names no `XLua` type
  (`core_only_player_check.py`). What stays is a name: `ScriptingAssemblies.json`
  lists `XLua.dll`, and `globalgamemanagers` grows by about 1 KB (its script
  list); together +1,288 bytes of APK. Without `XLUA_CORE_ONLY` the plugin
  selector ships the default library, so a project that drops the define but
  keeps the bridge library in its default slot pays for that library; these
  players used the core library as the package's only plugin.
- **Unity 2021.3.45f2** (Minimal stripping, `baseline`, `core-5.5` and
  `core-5.5+nodefine`, `size_report.py --work .../size-2021`) gives the same
  answer: `libil2cpp.so` +94,163 and +94,162 compressed (+336,088
  uncompressed both), the same metadata (+14,359 / +52,096), stripped
  assemblies `XLua.Core.dll` only (58,880 bytes), +640 bytes of APK for the
  names. Its APKs are not comparable with 6000.3's: 2021.3 packs `libxlua.so`
  unstripped (1,562,632 bytes, 514,272 compressed; core-only +623,846 over its
  baseline of 7,872,684), as the smoke README notes for the legacy library.
- **Budget** (section 6: the core preset adds at most 200 KB to an APK): over
  with Minimal stripping, Unity's default for IL2CPP, by 23,530 bytes (11.8%),
  within with High. The native library is within its own budget (121,686
  against 125 KB); the IL2CPP code is the larger part: 84,624 compressed and
  347,584 uncompressed, against the estimate of 60 to 150 KB of code and 20 to
  40 KB of metadata (47,620 uncompressed here). The stripped `XLua.Core.dll`
  is 62,464 bytes with Minimal and 40,960 with High. Milestones 8 and 9 (table
  handles, coroutines) are still to come.
- **`core_only_player_check.py`** passes on both `core-5.5` players: no
  `XLua.dll` in `ScriptingAssemblies.json`, no `ObjectTranslator`, `LuaEnv`,
  `StaticLuaCallbacks` or `XLua.dll` in the metadata, no bridge export in
  `libxlua.so`, no `XLua.dll` among the stripped assemblies.

Not measured here: WebGL (no sandbox in the WebGL plugin), Lua 5.4.9
(`core-5.4`), the source-plugin route (`core-5.5+srcplugin`), High stripping
and the legacy and mixed rows on Unity 2021.3, and the per-file IL2CPP
breakdown (`size_breakdown.py`). iOS is measured
[below](#the-core-only-player-on-ios).

#### Trimming the core-only player

Measured 2026-10-18 for question 34 of
[core-luascript.md](../docs/design/core-luascript.md) (section 8.15), from
main `ed602f71` (milestones 8 and 9 merged): `size_report.py run --configs
core` and `core-high`, variants `baseline` and `core-5.5`, Unity
6000.3.21f1, release ARM64 IL2CPP APKs, the core library of main (Lua
5.5.1, 271,336 bytes stripped, 123,163 in the APK). APK bytes over the
empty project; the IL2CPP and metadata cells are compressed in the APK /
uncompressed:

| Step | Minimal | `libil2cpp.so` | `global-metadata.dat` | High | `libil2cpp.so` | `global-metadata.dat` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| main `ed602f71` | +322,494 | +170,552 / +687,536 | +27,923 / +77,916 | +210,938 | +74,631 / +290,256 | +12,289 / +34,380 |
| no value-type collections or struct arrays | +294,554 | +143,424 / +539,000 | +27,102 / +75,444 | +201,826 | +65,295 / +236,072 | +12,511 / +35,020 |
| conversions through `TypedReference` | +273,370 | +123,616 / +439,872 | +25,740 / +71,000 | +187,542 | +52,226 / +166,112 | +11,306 / +31,172 |
| drivers without `async` methods | +267,930 | +118,791 / +412,752 | +25,119 / +69,448 | +187,558 | +52,225 / +166,112 | +11,306 / +31,172 |
| get-only exception properties (this branch) | **+267,066** | +118,159 / +412,048 | +24,880 / +68,876 | **+187,494** | +52,419 / +165,456 | +11,048 / +30,352 |

After merging main `8ca4c14e` (its core library is 272,584 bytes stripped,
123,783 in the APK: the sandbox fixes and `buffer.readi64`), with that
library in both players: main +323,082 at Minimal and +212,042 at High, this
branch +268,262 (-54,820) and +188,834 (-23,208).

The size probe (`SizeProbeCore.cs`) uses a script, `Register`, `Load`,
`Call` and a buffer. Before milestones 8 and 9 the same player was +223,530
and +193,802 (above): the table handles and coroutines had added 99 KB at
Minimal stripping, where every method of `XLua.Core` is compiled, and 17 KB
at High. `XLua.Core.dll` (core-only, Unity 6000.3.26f1 reference
assemblies, `asmdef_compile_check.py`) is 99,328 bytes of IL, from 104,960.

- **What the bytes are.** `llvm-nm` on IL2CPP's `libil2cpp.sym.so` and
  `llvm-readelf` on the APK's library (main, Minimal): of the +281,392 bytes
  of uncompressed sections, `.text` was +53,560, `.eh_frame` and its index
  +118,724, `.rodata` +39,960, `.data` and `.data.rel.ro` +61,360. The cost
  scales with the number of methods (about 2,900 new functions, each with an
  unwind entry, method table slots and relocations), not with their size.
  Minimal stripping compiles every method of `XLua.Core`; each value-type
  instantiation of a class-library generic brings the whole generic class
  (`List<LuaTableRef>` and `List<KeyValuePair<object, object>>` in
  `ToDictionary` about 41 methods each, `Dictionary<IntPtr, TimeSpan>` in the
  watchdog about 40, with their enumerators and comparers); every array of a
  struct (`HandleSlot[]`, `IterState[]`, `xlua_value[]`, `LuaValue[]`) brings
  IL2CPP's `IList<T>` implementation, its enumerators and comparers; every
  generic delegate instantiation its `Invoke`, `BeginInvoke` and `EndInvoke`.
- **The changes** (all keep the API; core-luascript.md 8.15): parallel arrays
  of primitives, classes instead of struct slots and native memory for value
  buffers (-27,940 / -9,112); the result, argument and table readers convert
  through `__makeref`/`__refvalue` in non-generic switches instead of 35
  reader and writer delegate types (-21,184 / -14,284); `RunAsync` and
  `RunAwaitable` continue from awaiters instead of being `async` methods
  (-5,440 at Minimal); get-only exception properties (-864).
- **Optional parts** (Minimal stripping, against the drivers row; with High
  stripping they cost nothing unless called):

  | Left out | APK bytes |
  | --- | ---: |
  | `XLUA_NO_COROUTINE_DRIVERS` | -24,112 |
  | of it `Run` (the iterator's `IEnumerator.Reset` keeps every `Reset` of the class libraries) | -10,896 |
  | of it `RunAsync` (the TPL) | -9,656 |
  | of it `RunAwaitable` | -4,312 |
  | `XLUA_NO_LUASCRIPT_EXTRAS`: `Register` with three or four arguments | -5,816 |
  | the tuple calls | -3,804 |
  | `ToDictionary` | -3,424 |
  | both defines (`IPairs` included) | -38,464 |

  The driver numbers are against an empty project. With a probe that has a
  coroutine (`yield return`) and an `async Task` method of its own in both
  the baseline and the core player (12,652 bytes more baseline APK), the
  core player is +259,490 and without the drivers +243,622: the drivers
  cost 15,868 bytes there.
- **With the defines and without unwind tables** (the same tree, both
  defines in every `XLua.Core` file; the last row with the core library of
  the next item):

  | Variant | Minimal | `libil2cpp.so` | High |
  | --- | ---: | ---: | ---: |
  | this branch | +267,066 | +118,159 / +412,048 | +187,494 |
  | `XLUA_NO_COROUTINE_DRIVERS` and `XLUA_NO_LUASCRIPT_EXTRAS` | +228,934 | +84,817 / +285,992 | +187,430 |
  | the same, core library without unwind tables (110,625 in the APK) | +216,398 | +84,817 / +285,992 | +174,894 |
- **Native: no unwind tables.** The core library built with
  `-fno-asynchronous-unwind-tables -fno-unwind-tables` (CMake
  `CMAKE_C_FLAGS`, NDK r30) is 234,920 bytes stripped and 110,537 compressed
  (zip -6), against 271,336 and 122,997: `.eh_frame` (31,072) and its index
  (5,468) go. Lua raises errors with `longjmp`, no C# exception crosses the
  library, and the library is C, so nothing in it needs them at run time;
  crash reports then lose the unwinding through `libxlua.so` frames. Not
  applied (an owner question).
- **Native: the five Lua C API exports** that `CORE_LUA_API` keeps
  (`lua_checkstack`, `lua_gettop`, `lua_settop`, `lua_toboolean`,
  `lua_type`; `LuaScript` imports none of them): 472 bytes stripped and 209
  compressed. Not applied.

#### The core-only player on iOS

Measured 2026-10-19 for milestone 11 of
[core-luascript.md](../docs/design/core-luascript.md) (section 8.16), main at
`58bd6940` (the trimmed core-only player of the section above):
`size_report.py run --platforms ios --configs core,core-high --variants
baseline,ng-5.5+pkg,core-5.5`, Unity 6000.3.21f1, release ARM64 IL2CPP
exports for iOS 15 with the device SDK (no Simulator), Lua 5.5.1, the probes
of the Android table (`SizeProbe.cs` for legacy, `SizeProbeCore.cs` for the
core-only player), Minimal and High managed stripping. The legacy and core
libraries came from `build/make_ios_lua55.sh` with `XLUA_IOS_SDK=device` (no
`XLUA_VARIANT` for legacy; `XLUA_VARIANT=core XLUA_PRESET=core` for core;
629,944 and 432,280 bytes as archives). Each export was compiled with Xcode
27.1 (iPhoneOS 27.1 SDK) as in [Players](#players): `xcodebuild -sdk iphoneos
CODE_SIGNING_ALLOWED=NO LD_GENERATE_MAP_FILE=YES DEPLOYMENT_POSTPROCESSING=YES`,
stripped as in an archive and not signed. "`.app` zip" is the `.app` zipped
with zip's default deflate, the stand-in `size_report.py` uses for an IPA
(a signed IPA from an archive export was not built). Link map columns are live
bytes in `UnityFramework`; deltas are over the empty project with the same
stripping level:

| Minimal stripping | `.app` zip | over baseline | `UnityFramework` delta | `libxlua.a` (link map) | `libGameAssembly.a` delta | `global-metadata.dat` delta | `libiPhone-lib.a` delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 6,628,030 | | | | | | |
| legacy (`ng-5.5+pkg`) | 7,418,957 | +790,927 | +1,912,608 | 304,933 | +1,525,443 | +347,676 | +9,760 |
| `core-5.5` | 6,870,273 | **+242,243** | +570,056 | 236,768 | +293,975 | +69,220 | +144 |

| High stripping | `.app` zip | over baseline | `UnityFramework` delta | `libxlua.a` (link map) | `libGameAssembly.a` delta | `global-metadata.dat` delta | `libiPhone-lib.a` delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 5,911,657 | | | | | | |
| legacy (`ng-5.5+pkg`) | 6,485,450 | +573,793 | +1,484,800 | 304,933 | +1,114,523 | +199,164 | +9,776 |
| `core-5.5` | 6,085,170 | **+173,513** | +404,808 | 236,768 | +125,392 | +30,652 | +144 |

- **Against Android** (APK, main `8ca4c14e`, after the trimming: +268,262 at
  Minimal and +188,834 at High; not re-measured on `58bd6940`): iOS is 26,019 bytes smaller at Minimal
  (+242,243) and 15,321 smaller at High (+173,513), a different package
  format and a different compiler, so it is a comparison of sizes, not of
  causes. **Against the budget** (question 73, answered 2026-10-19: about
  270 KB at Minimal stripping, 200 KB at High): Minimal is 27,757 bytes under
  270,000 (Android: 1,738 under), High is 26,487 under 200,000 (Android: 11,166
  under); against the old 200 KB figure for both, Minimal is 42,243 bytes
  (21%) over, as on Android. The libraries behind it:

  | | iOS (link map, live) | Android (stripped, in the APK) |
  | --- | ---: | ---: |
  | core native library | 236,768 | 271,336 / about 123,000 compressed |
  | IL2CPP code, Minimal | +293,975 | +118,159 compressed / +412,048 |
  | IL2CPP code, High | +125,392 | +52,419 compressed / +165,456 |
  | metadata, Minimal | +69,220 | +24,880 compressed / +68,876 |
  | metadata, High | +30,652 | +11,048 compressed / +30,352 |

  The metadata is the same file on both (within 400 bytes). The native
  library is 34,568 bytes smaller on iOS (live bytes of the archive's objects
  after `-dead_strip`, against a stripped ELF shared library with its own
  sections and symbol tables). The iOS cells are uncompressed bytes in the
  linked binary, the Android cells compressed in the APK and uncompressed.
- **Core-only against legacy:** -548,684 bytes of `.app` zip with Minimal
  stripping (-69.4%) and -400,280 with High (-69.8%); `UnityFramework` is
  1,342,552 and 1,079,992 bytes smaller. On Android the same comparison was
  -1,224,212 and -1,111,068 of APK; iOS gains less mainly because it keeps the web
  request code in `libiPhone-lib.a` with or without xLua, so the engine code
  that legacy keeps for xLua is 9,760 bytes there (the core-only player 144),
  against 544,069 bytes of `libunity.so` in the Android APK.
- **Where the bytes are** (`size -m` on `UnityFramework`, core-only against
  baseline, Minimal / High): `__text` +267,704 / +239,356 (the native library
  and the C++ of `libGameAssembly.a` outside IL2CPP's own section), the
  `il2cpp` section +184,472 / +87,504, `__DATA_CONST,__const` +23,336 /
  +9,064, `__TEXT,__const` +21,885 / +8,573, `__DATA,__data` +22,104 /
  +8,480, `__cstring` +7,897 / +7,903, `__unwind_info` +5,880 / +3,568. The
  sections add up to +537,518 and +366,760, the live bytes of the link map
  (+537,459 and +366,710); the file grows 570,056 and 404,808, the rest
  being page alignment and the load commands. **iOS has no `.eh_frame`:** its
  compact unwind data is 5,880 bytes at Minimal, where Android's `libil2cpp.so`
  carried 118,724 bytes of `.eh_frame` before the trimming and the core
  library 36,540 more. A core library built without unwind tables
  (SIZE.md, "Native: no unwind tables" above) would save nothing here.
- **Stripped assemblies** (`Library/Bee/artifacts/iOS/ManagedStripped`):
  `XLua.Core.dll` 92,672 bytes at Minimal and 48,640 at High (the legacy
  package player: `XLua.dll` 249,344 and 158,208 plus `XLua.Core.dll` 15,872
  and 10,240). `core_only_player_check.py` passes on both `core-5.5` players
  with `--link-map` (and `--project` for the High one): only `XLua.Core.dll` in
  `ScriptingAssemblies.json`, no bridge name in the metadata, no bridge symbol
  in the link map.
- **Fixes to the harness on the way:** the `ios` configurations had `core`
  (Minimal) only, so `core-high` is new for iOS; `prepare()` copied
  `HotfixFlags.cpp` to `Assets/Plugins/iOS` for the `+pkg` variants, which have
  no such folder (the package carries the file) and stopped with
  `FileNotFoundError`; and the `--one-unity` check matched another run's own
  command line (the finding of section 8.15), so two runs waited for each
  other (fixed on main in parallel, with `pgrep -x Unity`).

Not measured: Lua 5.4.9, a signed IPA from an archive export (it adds a code
signature and may compress differently from the zip), Medium stripping, Unity
2021.3, the optional-parts defines (`XLUA_NO_COROUTINE_DRIVERS`,
`XLUA_NO_LUASCRIPT_EXTRAS`), and a phone.

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

## Bytecode options

Measured 2026-10-18 like [presets](#presets): the native library alone, NDK
r30, API 23, the release scripts (`build/make_android_lua55.sh`, `lua54`,
`lua53`) with the options in the environment, stripped with `llvm-strip
--strip-unneeded`, "APK" the stripped file compressed with raw deflate level 6
(zlib). "main" is the library before the change (82c570f9; for minimal
10666945, which added the minimal preset's `os`, `package` and `dofile` items),
measured again after merging main. The options are in
[README.md](README.md#bytecode-options).

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | legacy (main and this branch) | 383,672 | 163,688 |
| | legacy, `XLUA_BYTECODE_ONLY` | 326,984 (-56,688) | 136,413 (-27,275) |
| | minimal (main 10666945 and this branch) | 308,080 | 135,424 |
| | minimal, `XLUA_BYTECODE_ONLY` | 251,376 (-56,704) | 108,230 (-27,194) |
| 5.5.1 armeabi-v7a | legacy | 257,156 | 143,598 |
| | legacy, `XLUA_BYTECODE_ONLY` | 219,964 (-37,192) | 119,986 (-23,612) |
| | minimal | 206,504 | 119,263 |
| | minimal, `XLUA_BYTECODE_ONLY` | 169,280 (-37,224) | 95,764 (-23,499) |
| 5.4.9 arm64-v8a | legacy | 367,136 | 155,227 |
| | legacy, `XLUA_BYTECODE_ONLY` | 314,992 (-52,144) | 130,530 (-24,697) |
| 5.4.9 armeabi-v7a | legacy | 247,484 | 137,016 |
| | legacy, `XLUA_BYTECODE_ONLY` | 212,948 (-34,536) | 115,213 (-21,803) |
| 5.3.6 arm64-v8a | legacy | 314,792 | 133,468 |
| | legacy, `XLUA_BYTECODE_ONLY` | 270,816 (-43,976) | 112,497 (-20,971) |
| 5.3.6 armeabi-v7a | legacy | 208,532 | 115,390 |
| | legacy, `XLUA_BYTECODE_ONLY` | 179,888 (-28,644) | 97,513 (-17,877) |

LuaEnv's precompiled built-in chunks ([README.md](README.md#luaenv-on-a-bytecode-only-library)),
measured 2026-10-18 the same way with CMake directly (`-DANDROID_PLATFORM=android-23
-DCMAKE_BUILD_TYPE=Release`, the NDK's toolchain file). "main" is cdf3b3f3, where
a bytecode-only library has no chunks and `LuaEnv` cannot start. The chunks keep
their debug information; legacy has all six (14,449 bytes of bytecode on Lua
5.5.1, of which 3,940 are debug information), minimal the three setup chunks
(5,496 bytes).

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | legacy (main; this branch the same once the build ID is removed) | 383,688 | 163,696 |
| | legacy, `XLUA_BYTECODE_ONLY`, main | 327,000 | 136,414 |
| | legacy, `XLUA_BYTECODE_ONLY`, with the chunks | 342,280 (+15,280; -41,408 against legacy) | 144,310 (+7,896; -19,386) |
| | minimal, `XLUA_BYTECODE_ONLY`, main | 251,408 | 108,238 |
| | minimal, `XLUA_BYTECODE_ONLY`, with the chunks | 258,000 (+6,592) | 111,898 (+3,660) |
| 5.5.1 armeabi-v7a | legacy (main; this branch the same) | 257,172 | 143,610 |
| | legacy, `XLUA_BYTECODE_ONLY`, main | 219,980 | 119,937 |
| | legacy, `XLUA_BYTECODE_ONLY`, with the chunks | 234,884 (+14,904; -22,288 against legacy) | 127,719 (+7,782; -15,891) |
| 5.3.6 armeabi-v7a | legacy, `XLUA_BYTECODE_ONLY` | refused: needs `LUAC_COMPATIBLE_FORMAT=ON` | |
| | legacy, `XLUA_BYTECODE_ONLY`, `LUAC_COMPATIBLE_FORMAT` | 202,712 | 106,493 |

Measured again after merging main f0c6deda (5.5.1 arm64-v8a): legacy 384,040
(the same as main's once the build ID is removed), `XLUA_BYTECODE_ONLY` 327,352
on main and 342,632 with the chunks: the same +15,280.

The core preset with `XLUA_TRUSTED_BYTECODE` (`XLUA_VARIANT=core
XLUA_PRESET=core`, managed exports):

| Lua, ABI | build | stripped | APK |
| --- | --- | ---: | ---: |
| 5.5.1 arm64-v8a | core, main | 253,792 | 115,246 |
| | core, this branch | 254,280 (+488) | 115,504 (+258) |
| | core, `XLUA_TRUSTED_BYTECODE` | 260,760 (+6,480) | 118,414 (+2,910) |
| 5.5.1 armeabi-v7a | core, main | 171,456 | 101,694 |
| | core, this branch | 171,828 (+372) | 101,878 (+184) |
| | core, `XLUA_TRUSTED_BYTECODE` | 176,256 (+4,428) | 104,443 (+2,565) |
| 5.4.9 arm64-v8a | core, main | 239,096 | 107,871 |
| | core, this branch | 239,584 (+488) | 108,183 (+312) |
| | core, `XLUA_TRUSTED_BYTECODE` | 244,400 (+4,816) | 110,375 (+2,192) |
| 5.4.9 armeabi-v7a | core, main | 163,408 | 96,465 |
| | core, this branch | 163,764 (+356) | 96,683 (+218) |
| | core, `XLUA_TRUSTED_BYTECODE` | 166,896 (+3,132) | 98,492 (+1,809) |

- **The compiler** (`lparser.c`, `lcode.c`, `llex.c`) is 52 to 57 KB of a
  64-bit library on Lua 5.4 and 5.5 (the backlog estimated 54 KB), 44 KB on
  5.3, and 16 to 20% of the library's compressed size. The stub that replaces
  it is a few dozen bytes; the bytecode-only `lua_setfenv` adds a little code
  to `xlua.c`. The savings of legacy and minimal are about the same, because the
  compiler does not depend on the other options.
- **Trusted bytecode** adds `lundump.c` and its messages: 6.5 KB stripped on
  5.5.1 and 4.8 KB on 5.4.9 (arm64-v8a). Every core library grows by the export
  `xlua_core_run_mode` and its refusals (488 bytes), with or without the option
  (measured before milestone 6, whose `xlua_core_call` carries the mode instead).
- **`LUA_COMPAT_APIINTCASTS`:** with it gone, the legacy and minimal libraries of
  the table above (Lua 5.3.6, 5.4.9 and 5.5.1, both ABIs) are main's byte for
  byte once `.note.gnu.build-id` is removed; the definition only declared
  macros that nothing used.

Not measured: iOS, x86 and x86_64, a Unity player, and the speed of loading
(a bytecode-only library loads only precompiled chunks, which skip the
compiler anyway).

## Cheap hardening

Measured 2026-10-18 for the owner's decision to turn the cheap hardening set
on in every preset (CMake `XLUA_CHEAP_HARDEN`, [README.md](README.md#warnings-hardening-and-test-options);
[FLAGS.md](FLAGS.md#the-cheap-set-as-applied-2026-10-18)):
`-fno-delete-null-pointer-checks` for every C file, `-ftrivial-auto-var-init=zero`
for xLua's own files. NDK r30, API 23, the release build (`XLUA_SIZE_FLAGS`),
stripped with `llvm-strip --strip-unneeded`, Info-ZIP deflate 6 of the stripped
file, the same tree (main `4449c12b` merged) with the option `OFF` and `ON`:

| Preset, Lua, ABI | stripped, off | on | APK, off | on |
|---|---:|---:|---:|---:|
| legacy 5.5.1 arm64-v8a | 384,040 | 384,392 (+352) | 162,636 | 162,763 (+127) |
| legacy 5.5.1 armeabi-v7a | 257,396 | 257,748 (+352) | 143,135 | 143,505 (+370) |
| minimal 5.5.1 arm64-v8a | 308,464 | 308,784 (+320) | 135,291 | 135,388 (+97) |
| minimal 5.5.1 armeabi-v7a | 206,744 | 207,064 (+320) | 119,020 | 119,329 (+309) |
| core 5.5.1 arm64-v8a | 272,088 | 272,584 (+496) | 123,316 | 123,616 (+300) |
| core 5.5.1 armeabi-v7a | 183,704 | 184,216 (+512) | 109,006 | 109,464 (+458) |
| core 5.4.9 arm64-v8a | 257,360 | 257,808 (+448) | 116,015 | 116,196 (+181) |
| core 5.4.9 armeabi-v7a | 175,688 | 176,120 (+432) | 103,725 | 104,023 (+298) |

- **Off is main:** the legacy libraries with `XLUA_CHEAP_HARDEN=OFF` equal
  main's byte for byte on both ABIs once `.note.gnu.build-id` is removed.
- **Against FLAGS.md's measurement** (+272 and +336 bytes stripped, +176 and
  +244 of APK for legacy on 2026-10-17): the zeroed locals now also reach
  `xlua_core.c`, which holds the C# function trampolines since the split,
  and the core preset's sandbox, limits and buffer files; core grows the most
  (+496 bytes stripped on 5.5.1 arm64-v8a).
- **Core's budget** (core-luascript.md section 6: 290 KB and 125 KB on
  arm64-v8a, Lua 5.5.1): 272,584 and 123,616 bytes with the option and the
  buffer additions of the same day, so 17.4 KB and 1.4 KB are left.
- Not measured: iOS, the desktops and LuaJIT (whose `libluajit.a` the option
  does not reach; only xLua's files and the null-check flag on the CMake
  sources change there).

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

Not measured here: iOS, WebGL and Medium stripping. A game-sized configuration
follows.

### A game-sized configuration (2026-10-18)

Measured for the GCM question (M6 question 2 of
[source-generator.md](../docs/design/source-generator.md#m6-as-implemented-2026-10-18)):
`Test/MobileSmoke/size_report.py run --platforms android --configs
game-minimal,game-medium,game-high`, release ARM64 APKs from Unity 6000.3.21f1,
xLua from the UPM package (`+pkg`), Lua 5.5.1 plugin from
`build/make_android_lua55.sh` (NDK r30) and C# at `df205401` plus the `+game`
variant. The configuration is `Test/MobileSmoke/SizeGameGenConfig.cs` (removed
with Generate Code in source generator M11; its attribute form
`SizeGameSgConfig.cs` stays):
`ExampleConfig.cs` with its LINQ queries switched on (the sample of
`Test/Native/migration_check.py` without its Hotfix list), that is every exported
class and struct of `UnityEngine.CoreModule` and `UnityEngine.UI` in the
`UnityEngine` and `UnityEngine.UI` namespaces, the project's own types and the
delegates they use, with `ExampleConfig.cs`'s `BlackList` and filter, plus 43
members and two types that exist only in the Editor's assemblies (Generate Code
reflects on those, and its wrappers for them do not compile in a player). Generate
Code and GCM read those lists; the source generator reads
`SizeGameSgConfig.cs`, the output of **XLua > Migrate Configuration to
Attributes** for them in the same project: 264 wrapped types, 26 delegate
types, 62 `BlackList` entries. Every `+game` project, the baseline too, has
`com.unity.ugui`, and the probe times six Lua calls into the wrapped types
(`XLUA_SIZE_GAME`; the run-time results, host and emulator, are in
[source-generator.md](../docs/design/source-generator.md#gcm-with-a-game-sized-configuration-2026-10-18)).

APK delta over the baseline, and `libil2cpp.so` and `global-metadata.dat`
deltas compressed in the APK / uncompressed:

| bindings (package layout) | stripping | APK delta | `libil2cpp.so` | `global-metadata.dat` |
| --- | --- | ---: | ---: | ---: |
| Generate Code | Minimal | +6,517,306 | +4,028,719 / +15,574,504 | +611,516 / +2,180,512 |
| `GEN_CODE_MINIMIZE` | Minimal | +5,825,054 | +3,307,312 / +12,847,160 | +640,687 / +2,373,208 |
| source generator | Minimal | +6,297,634 | +3,887,944 / +14,969,816 | +577,891 / +2,018,836 |
| Generate Code | Medium | +6,295,094 | +3,872,067 / +15,084,888 | +545,872 / +1,967,068 |
| `GEN_CODE_MINIMIZE` | Medium | +5,602,606 | +3,151,888 / +12,377,616 | +573,578 / +2,176,108 |
| source generator | Medium | +6,051,494 | +3,705,023 / +14,388,752 | +514,353 / +1,848,760 |
| Generate Code | High | +6,309,866 | +3,909,875 / +15,231,168 | +521,993 / +1,882,924 |
| `GEN_CODE_MINIMIZE` | High | +5,624,514 | +3,194,089 / +12,524,152 | +552,442 / +2,091,940 |
| source generator | High | +6,070,914 | +3,745,566 / +14,523,048 | +492,775 / +1,772,924 |

Baselines: 12,287,148 bytes (Minimal), 12,065,812 (Medium), 11,534,212 (High).
`libxlua.so` is 163,697 / 383,688 in every variant. `libunity.so` is +1,698,978
/ +3,758,240 with both legacy modes and 32 KB / 92 KB less with the source
generator (Minimal; the same with Medium and High).

The generated assembly (Assembly-CSharp; xLua is in `XLua.dll`), as the player
build compiles it (`Library/Bee/PlayerScriptAssemblies`) and after Minimal and
High stripping, and the generated source:

| bindings | IL | stripped, Minimal / High | `XLua.dll` stripped, Minimal | generated source |
| --- | ---: | ---: | ---: | --- |
| Generate Code | 1,432,064 | 1,429,504 / 1,393,152 | 228,864 | 269 files, 5,397,004 bytes, 130,751 lines |
| `GEN_CODE_MINIMIZE` | 1,293,312 | 1,292,800 / 1,261,056 | 230,400 | 269 files, 4,085,854 bytes, 105,767 lines |
| source generator | 1,505,792 | 1,504,256 / 1,458,176 | 245,248 | 268 player files, 5,817,558 bytes; 271 with the Editor-only manifest, link.xml and Obfuz rules, 6,301,551 bytes, about 104,000 lines |

- **GCM against the source generator:** GCM's APK is 472,580 bytes smaller
  with Minimal (7.5% of the source generator's delta), 448,888 with Medium and
  446,400 with High; `libil2cpp.so` is 2.1 MB smaller uncompressed (580 KB in
  the APK). The source generator's metadata is 60 KB smaller (compressed) and
  its `libunity.so` 33 KB smaller. With five wrapped types the difference was
  11 to 16 KB (above), so it grows with the number of wrapped members, as
  expected.
- **Where GCM saves:** each wrapper of Generate Code and of the source
  generator is a `[MonoPInvokeCallback]` method that looks up its
  `ObjectTranslator`, reads `lua_gettop` and catches exceptions itself. IL2CPP
  gives every such method a reverse P/Invoke wrapper: 4,625 in the Generate
  Code and the source generator players, 34 in the GCM player (counted in
  `Library/Bee/artifacts/Android/il2cppOutput/cpp`). GCM's wrappers are plain
  methods that the one native trampoline calls with the translator and the
  stack top, and that one entry catches exceptions. The C++ IL2CPP writes for
  Assembly-CSharp is 38.8 MB with Generate Code, 39.0 MB with the source
  generator and 25.4 MB with GCM (Medium builds).
- **Where the source generator saves:** it preserves the members it wraps one
  by one instead of Generate Code's `preserve="all"` per wrapped type, so
  IL2CPP compiles less engine code: the C++ of `UnityEngine.CoreModule` is
  15.5 MB against 17.3 MB, the generic instantiations 45.8 MB against 48.4 MB.
  That is the 220 to 240 KB it is below Generate Code in the APK, although
  its own wrappers are as large.
- **A GCM mode in the source generator** should therefore save about what GCM
  saves over Generate Code (692 KB APK, 2.7 MB `libil2cpp.so` uncompressed) and
  end about 200 KB below legacy GCM (*estimate* at the time; measured below:
  225 to 248 KB).
- **Build times** (the build launch of `SmokeBuild.BuildAndroid`; the
  machine ran other agents' builds, so they are rough): Minimal, the first
  build of each project, Generate Code 224 s, GCM 91 s, source generator 119 s;
  Medium and High, which reuse the project's Library, 55 to 79 s each. Generate
  Code itself takes 2 to 3 s (GCM 2 s); its launch, which imports and compiles
  the new project first, 44 s (GCM 20 s). Compiling the Editor's Assembly-CSharp again with
  its own compiler command (the median of five warm runs, as
  `Test/UnityMatrix/compile_time.py` replays it): Generate Code 1.9 s, GCM
  0.9 s, source generator (generator included) 1.0 s.

Not measured: iOS, WebGL and a phone. The source generator's GCM mode, built
after this measurement, follows.

#### The source generator's GCM mode (2026-10-18)

The same configuration and method with the new variant
`ng-5.5+pkg+sg+gcm+game` (the source generator compiled with
`GEN_CODE_MINIMIZE`; [source-generator.md](../docs/design/source-generator.md#gcm-mode-as-implemented-2026-10-18)),
every variant rebuilt in one run: `size_report.py run --platforms android
--configs game-minimal,game-high`, Unity 6000.3.21f1, release ARM64 APKs, the
package layout, Lua 5.5.1 plugin from `build/make_android_lua55.sh` (NDK r30)
and C# at `4f114134`. APK delta over the baseline (12,287,144 bytes with
Minimal, 11,534,216 with High), and deltas compressed in the APK / uncompressed:

| bindings (package layout) | stripping | APK delta | `libil2cpp.so` | `libunity.so` | `global-metadata.dat` |
| --- | --- | ---: | ---: | ---: | ---: |
| Generate Code | Minimal | +6,521,206 | +4,030,078 / +15,576,704 | +1,698,979 / +3,758,240 | +613,821 / +2,189,456 |
| `GEN_CODE_MINIMIZE` | Minimal | +5,828,158 | +3,308,015 / +12,849,040 | +1,698,993 / +3,758,240 | +642,774 / +2,382,156 |
| source generator | Minimal | +6,301,626 | +3,889,781 / +14,971,888 | +1,666,273 / +3,666,304 | +579,836 / +2,027,780 |
| source generator, GCM | Minimal | **+5,602,662** | +3,172,062 / +12,262,440 | +1,666,273 / +3,666,304 | +598,595 / +2,111,352 |
| Generate Code | High | +6,312,062 | +3,911,779 / +15,233,216 | +1,699,900 / +3,758,016 | +522,056 / +1,883,196 |
| `GEN_CODE_MINIMIZE` | High | +5,625,970 | +3,195,110 / +12,526,008 | +1,699,898 / +3,758,016 | +552,588 / +2,092,208 |
| source generator | High | +6,073,266 | +3,747,569 / +14,525,160 | +1,667,041 / +3,666,000 | +492,915 / +1,773,184 |
| source generator, GCM | High | **+5,377,678** | +3,032,055 / +11,814,824 | +1,667,042 / +3,666,000 | +512,851 / +1,856,488 |

`libxlua.so` is 163,902 / 384,040 in every variant. The rows other than the
new one are within 4 KB of the measurement above (C# at `df205401`).

| bindings | reverse P/Invoke wrappers | C++ of Assembly-CSharp (`du`, High) | Assembly-CSharp stripped, High | `XLua.dll` stripped, High |
| --- | ---: | ---: | ---: | ---: |
| Generate Code | 4,625 | 37,904 KB | - | - |
| `GEN_CODE_MINIMIZE` | 34 | 24,828 KB | 1,261,056 | 168,448 |
| source generator | 4,625 | 38,176 KB | 1,458,176 | 183,296 |
| source generator, GCM | 34 | 24,628 KB | 1,204,736 | 184,320 |

- **Against legacy GCM:** the source generator's GCM player is 225,496 bytes
  smaller with Minimal (3.9% of legacy GCM's delta) and 248,292 with High
  (4.4%), close to the estimate above (about 200 KB). `libil2cpp.so` is
  136 KB / 163 KB smaller compressed, `libunity.so` 33 KB (the members
  preserved one by one), the metadata 40 KB / 44 KB.
- **Against the plain source generator:** 698,964 (Minimal) and 695,588
  (High) bytes smaller, the same saving as legacy GCM over Generate Code
  (693,048 and 686,092 here). The reverse P/Invoke wrappers fall to the 34 of
  legacy GCM and `libil2cpp.so` loses 2.7 MB uncompressed; the metadata grows
  by 19 to 20 KB compressed.
- **The Editor's compiled Assembly-CSharp** (`Library/ScriptAssemblies`,
  with the Editor-only manifest, link.xml and obfuz constants): 2,408,448
  bytes plain, 2,152,960 in GCM mode (Generate Code 1,432,576, legacy GCM
  1,293,824).
- **Build times** (the build launch; the machine ran other agents' work):
  Minimal 89 s (GCM mode) and 102 s (plain), High 60 s and 75 s.

Not measured: iOS, WebGL, Medium stripping and a phone.

## What link.xml preservation costs (2026-10-28)

The backlog item "Preserve only used members instead of `preserve="all"`"
([source-generator.md](../docs/design/source-generator.md#linkxml-entries-audited-2026-10-28)
has the audit of every link.xml entry xLua writes). Measured with
`Test/MobileSmoke/size_report.py run --platforms android --configs
linkxml-minimal,linkxml-high` (new configurations): Unity 6000.3.21f1, release
ARM64 IL2CPP APKs, the package layout, C# at `32f0748d` (the harness change
only adds an Editor script to the measured projects), the Lua 5.5.1 plugin of
the 2026-10-28 builds (`libxlua.so` 164,888 / 385,592 in every variant,
sha256 `2e2677ca...`). Every variant has the game-sized configuration's 264
types (`SizeGameSgConfig.cs`, [above](#a-game-sized-configuration-2026-10-18)).
The `+link*` variants add one more link.xml through
`Test/MobileSmoke/SizeLinkXml.cs` (an `IUnityLinkerProcessor`) for those types:

- **`+linkall`:** `<type fullname="T" preserve="all"/>`. That is what the
  source generator writes for a `[ReflectionUse]` type, and what Generate
  Code (`LinkXmlGen`, removed in M11) wrote for every `[LuaCallCSharp]` and
  `[ReflectionUse]` type.
- **`+linkpublic`:** the public members T declares, by name. That is all the
  reflection fallback reaches without `xlua.private_accessible`.
- **`+linktype`:** `preserve="nothing"`, the type alone. This is a lower bound,
  not a working option.

Without `+sg`, the 264 types are `[ReflectionUse]` types and nothing is
generated: a reflection-mode project. Deltas over the baseline (12,287,144
bytes with Minimal, 11,534,212 with High), compressed in the APK /
uncompressed:

| variant | stripping | APK delta | `libil2cpp.so` | `libunity.so` | `global-metadata.dat` |
| --- | --- | ---: | ---: | ---: | ---: |
| source generator, as it is | Minimal | +6,381,626 | +3,965,818 / +15,133,264 | +1,666,271 / +3,666,304 | +582,816 / +2,036,720 |
| source generator + `preserve="all"` per wrapped type | Minimal | +6,644,434 | +4,155,990 / +15,976,544 | +1,699,413 / +3,758,944 | +622,315 / +2,186,252 |
| reflection, `preserve="all"` | Minimal | +4,860,882 | +2,489,061 / +9,605,904 | +1,699,114 / +3,758,560 | +506,183 / +1,669,880 |
| reflection, public members | Minimal | +4,767,474 | +2,421,419 / +9,285,424 | +1,684,778 / +3,713,648 | +494,755 / +1,632,896 |
| reflection, type only | Minimal | +3,234,482 | +1,623,690 / +5,906,600 | +1,134,680 / +2,375,136 | +309,596 / +961,320 |
| source generator, as it is | High | +6,165,582 | +3,834,568 / +14,700,960 | +1,667,040 / +3,666,000 | +497,252 / +1,787,368 |
| source generator + `preserve="all"` per wrapped type | High | +6,496,750 | +4,096,559 / +15,793,800 | +1,699,912 / +3,758,640 | +533,563 / +1,917,724 |
| reflection, `preserve="all"` | High | +4,698,062 | +2,412,645 / +9,371,208 | +1,699,900 / +3,758,256 | +418,988 / +1,404,924 |
| reflection, public members | High | +4,540,970 | +2,282,125 / +8,825,888 | +1,685,422 / +3,713,280 | +406,897 / +1,368,116 |
| reflection, type only | High | +2,994,514 | +1,448,741 / +5,373,352 | +1,135,142 / +2,374,816 | +244,110 / +782,620 |

Stripped `UnityEngine.CoreModule.dll` (`ManagedStripped`), Minimal / High:
baseline 220,672 / 200,704; source generator 1,051,136 / 893,440, with
`preserve="all"` 1,217,024 / 1,081,856; reflection with `preserve="all"`
1,217,024 / 1,081,344, public members 1,181,696 / 1,051,648, type only
359,936 / 323,072.

- **What the source generator saves by not preserving wrapped types whole**
  (its M5 change against Generate Code's `link.xml`): 262,808 bytes of APK
  with Minimal and 331,168 with High (4.0% and 5.1% of the `+linkall`
  delta). Of that, `libil2cpp.so` is 190 KB / 262 KB compressed (843 KB /
  1.09 MB uncompressed), `libunity.so` 33 KB, and the metadata 36 to 39 KB.
  The 33 KB of `libunity.so` is the "not traced" engine code difference of
  the [earlier measurements](#generated-bindings-generate-code-gcm-and-the-source-generator)
  (12 KB with five types, 33 KB with the game configuration). That difference
  is now traced to `preserve="all"`.
- **The generator's own link.xml for this configuration** has two entries,
  the `[DoNotGen]` methods `GameObject.SendMessage` and `BroadcastMessage`
  (`link-sourcegen.xml` next to each player, the same file in every `+sg`
  player). Wrapped members need none.
- **`[ReflectionUse]` with public members instead of `preserve="all"`**
  saves 93,408 bytes with Minimal and 157,092 with High (1.9% and 3.3% of the
  `preserve="all"` delta), most of it `libil2cpp.so` (68 KB / 131 KB
  compressed). `libunity.so` saves 14 KB. These 264 engine types are a large
  `[ReflectionUse]` list; a project with a handful of its own types saves
  next to nothing with Minimal, which keeps user assemblies whole.
- **Type only** would save another 1.5 MB, but Lua reaching those types
  through reflection then fails: the members are gone. It only bounds what
  the members cost.
- **The generated bindings cost more than reflection here** (6.38 MB against
  4.86 MB with Minimal) because each of the 264 types gets a wrapper. The
  [GCM mode](#the-source-generators-gcm-mode-2026-10-18) is about 700 KB
  smaller, so it is still above reflection.
- **Build times:** 54 to 139 s per build launch; the projects reuse their
  Library between Minimal and High.

Not measured: iOS, WebGL, Medium stripping, a smaller `[ReflectionUse]` list,
and players run on a device (these size builds are not run).

## Legacy keeps `[LuaCallCSharp]` types whole (2026-10-29)

Since owner decision Q75 of 2026-10-29 the source generator writes
`preserve="all"` for every `[LuaCallCSharp]` type in the legacy preset and in
minimal with `HOTFIX_ENABLE`; strict minimal keeps the narrow entries
([source-generator.md](../docs/design/source-generator.md#luacallcsharp-types-kept-whole-where-reflection-reaches-them-2026-10-29)).
What legacy now pays, measured on the game configuration of the section
above: `size_report.py run --platforms android --configs linkxml-minimal
--variants baseline+game,ng-5.5+pkg+sg+game`, once from main at `fd4a43ce`
(before) and once with the change (after), Unity 6000.3.21f1, release ARM64
IL2CPP APKs, Minimal stripping, the package layout, the Lua 5.5.1 plugin of
2026-10-28 (`libxlua.so` 165,593 / 388,136, sha256 `cb2109d0...`, 705 bytes
more in the APK than the plugin of the section above). Compressed in the APK
/ uncompressed:

| variant | APK | delta over the baseline | `libil2cpp.so` delta | `libunity.so` delta | `global-metadata.dat` delta |
| --- | ---: | ---: | ---: | ---: | ---: |
| baseline+game, before | 12,287,144 | +0 | | | |
| baseline+game, after | 12,287,152 | +0 | | | |
| `ng-5.5+pkg+sg+game`, before (narrow) | 18,670,602 | +6,383,458 | +3,966,965 / +15,134,512 | +1,666,270 / +3,666,304 | +582,800 / +2,036,892 |
| `ng-5.5+pkg+sg+game`, after (`preserve="all"`) | 18,934,578 | +6,647,426 | +4,158,275 / +15,977,680 | +1,699,412 / +3,758,944 | +622,322 / +2,186,424 |
| **after - before** | **+263,976** | **+263,968** | **+191,310 / +843,168** | **+33,142 / +92,640** | **+39,522 / +149,532** |

- **Legacy pays 263,976 bytes of APK** (1.4% of the APK, 4.1% of what xLua
  and the 264 wrapped types add to it), as the `+linkall` variant of the section above
  predicted (262,808). The two baselines differ by 8 bytes, which is the
  noise of the APK container.
- **The generator's link.xml** (`link-sourcegen.xml` next to each player):
  before, one type, `UnityEngine.GameObject` with the `[DoNotGen]` methods
  `SendMessage` and `BroadcastMessage`; after, 264 types with
  `preserve="all"` and no member entries (`GameObject`'s two methods are
  covered by its `preserve="all"`). The stripped `UnityEngine.CoreModule.dll`
  grows from 1,051,136 to 1,217,024 bytes, the size of the `+linkall` player
  above; `Assembly-CSharp.dll`, `XLua.dll` and `UnityEngine.UI.dll` keep their
  sizes (Minimal stripping keeps those assemblies whole).
- **Strict minimal** writes the "before" entries, so its size does not
  change; neither does minimal with `XLUA_REFLECTION` alone. Minimal with
  `HOTFIX_ENABLE` pays as legacy does, on top of the hotfix cost below. Not
  measured here.
- **High stripping** was not measured again: the `+linkall` variant of the
  section above gives 331,168 bytes for it.

Not measured: High and Medium stripping, iOS, WebGL, a project's own types
(Minimal stripping keeps them whole anyway).

## Public generic methods in strict minimal (2026-10-29)

What it costs to add link.xml entries for the public generic methods of
`[LuaCallCSharp]` types in strict minimal. `xlua.get_generic_method` finds
these methods by name, and IL2CPP strips them otherwise. The change was
measured but not committed, and it waits for the owner
([source-generator.md](../docs/design/source-generator.md#the-obfuz-rules-and-public-generic-methods-2026-10-29)).

The measurement:
- `size_report.py run --platforms android --configs strict --variants
  baseline,ng-5.5+minimal+strict,baseline+game,ng-5.5+minimal+strict+game`;
- once from `16a528ea` (main at `d3997971` merged) with the harness change of the same day
  (before), and once with the generator change (after). Both baselines are
  the "after" players;
- Unity 6000.3.21f1, release ARM64 IL2CPP APKs, Minimal stripping;
- the Lua 5.5.1 minimal plugin of 2026-10-28 (`libxlua.so` 137,270 /
  312,176).

`ng-5.5+minimal+strict+game` is new: the game configuration of
`SizeGameSgConfig.cs` (264 engine types), bound by the generator in strict
minimal. The `+minimal` variants now apply their defines in a launch of
their own. Before that, the generator's link.xml came from Editor
assemblies compiled without `XLUA_PRESET_MINIMAL`, which since Q75 meant the
legacy `preserve="all"` for every bound type (Test/README.md, "Player
size"). Compressed in the APK / uncompressed:

| variant | APK | delta over its baseline | `libil2cpp.so` delta | `libunity.so` delta | `global-metadata.dat` delta |
| --- | ---: | ---: | ---: | ---: | ---: |
| baseline | 12,279,056 | | | | |
| `ng-5.5+minimal+strict`, before | 12,761,698 | +482,642 | +289,042 / +1,014,360 | +4,446 / +10,720 | +50,040 / +153,496 |
| `ng-5.5+minimal+strict`, after | 12,761,702 | +482,646 | +289,049 / +1,014,360 | +4,445 / +10,720 | +50,040 / +153,496 |
| baseline+game | 12,287,140 | | | | |
| `ng-5.5+minimal+strict+game`, before | 16,946,354 | +4,659,214 | +2,865,801 / +11,179,616 | +1,142,018 / +2,601,968 | +512,358 / +1,872,708 |
| `ng-5.5+minimal+strict+game`, after | 17,034,170 | +4,747,030 | +2,933,848 / +11,470,960 | +1,150,749 / +2,622,080 | +523,412 / +1,912,080 |
| **game, after - before** | **+87,816** | | **+68,047 / +291,344** | **+8,731 / +20,112** | **+11,054 / +39,372** |

- **The size probe** binds one type without generic methods, so it gets no
  link.xml and does not change (4 bytes of container noise).
- **The game configuration:** link.xml (`link-sourcegen.xml`) grows from one
  type to 24. Before, it held only `GameObject` with the `[DoNotGen]`
  methods `SendMessage` and `BroadcastMessage`. After, it adds 72 method
  names, such as:
  - the `GetComponent` family of `GameObject` and `Component`, and
    `AddComponent`;
  - `Object`'s `Instantiate`, `InstantiateAsync` and `Find*ObjectsByType`;
  - `Resources.Load` and `LoadAll`, and `ScriptableObject.CreateInstance`;
  - the `Mesh` setters;
  - the `GetPixelData` and `SetPixelData` methods of the texture types.

  The probe calls none of them. A game that calls them from C# has them
  already and pays less.
- The strict minimal game player is 4.7 MB over its baseline; legacy's
  `ng-5.5+pkg+sg+game` is 6.6 MB over it, in the package layout with plain
  wrappers (the section above).

Not measured: High and Medium stripping, iOS, WebGL.

## Hotfix (`HOTFIX_ENABLE`, 2026-10-28)

`Test/MobileSmoke/size_report.py run --platforms android --configs hotfix`,
release ARM64 APKs from Unity 6000.3.21f1, Minimal stripping, xLua from the
UPM package with the source generator (`+pkg+sg`), Lua 5.5.1 plugin from
`build/make_android_lua55.sh` (NDK r30), C# at `a4750b51` (the
`ObjectTranslatorPool` change included). `+hotfix` adds `HOTFIX_ENABLE` and
the injector; `+hftargets` adds 50 `[Hotfix]` classes of ten methods each
(a constructor, a property, eight methods of eight signatures), not
injected without `+hotfix`; `+intkey` makes them
`[Hotfix(HotfixFlag.IntKey)]`. Every build that had `[Hotfix]` types and
the define logged `inject finish!` and no IntKey fallback.

APK delta over the baseline (12,279,060 bytes), and deltas compressed in the
APK / uncompressed:

| variant | APK delta | `libil2cpp.so` | `global-metadata.dat` |
| --- | ---: | ---: | ---: |
| `ng-5.5+pkg+sg` | +1,655,970 | +757,577 / +2,571,400 | +156,315 / +456,852 |
| `+hotfix` (the define alone) | +1,669,098 | +770,370 / +2,603,136 | +156,644 / +457,484 |
| `+hftargets` (500 methods, not injected) | +1,677,814 | +771,207 / +2,645,552 | +164,458 / +488,996 |
| `+hftargets+hotfix` (field mode) | +1,712,126 | +801,495 / +2,774,360 | +168,486 / +504,428 |
| `+hftargets+hotfix+intkey` | +1,709,962 | +803,050 / +2,768,752 | +164,765 / +489,760 |

`libxlua.so` is 165,593 / 388,136 and `libunity.so` +574,559 / +1,167,744
in every xLua variant.

- **The define alone:** +13,128 bytes, of which `libil2cpp.so` +12,793
  (+31,736 uncompressed): the hotfix half of `DelegateBridge`, `xlua.hotfix`
  and the locks of `THREAD_SAFE`, which the define also turns on.
- **Injection, 500 methods:** the injected variants minus `+hftargets`, minus
  the define. Field mode: +21,184 bytes, 42 per method (194 bytes of
  uncompressed code and 30 of metadata, the static field). IntKey: +19,020
  bytes, 38 per method (183 bytes of code, no metadata). IntKey saves the
  metadata, not the code.
- **Not injected:** the 500 methods and their source-generated
  `HotfixBridges` together cost +21,844 bytes; `HotfixBridges` is generated
  whether or not `HOTFIX_ENABLE` is defined.
- **`HotfixBridges` alone** (2026-10-28, estimated, not built with Unity):
  `HotfixSizeTargets.cs` compiled as a library with csc `-optimize+` and the
  source generator, without `HOTFIX_ENABLE`: the class has 13 methods (10
  bridges, the IntKey slots' `Get` and `Set`, a static constructor) and 867
  bytes of IL; the DLL is 3,072 bytes smaller without it (33,280 against
  30,208). The strict minimal and legacy configurations have no `[Hotfix]`
  types and no `HotfixBridges`. Skipping it without the define is an owner
  question
  ([hotfix.md](../docs/research/hotfix.md#hotfixbridges-without-hotfix_enable-2026-10-28)).

Run-time results of the `+hfbench` players are in
[hotfix.md](../docs/research/hotfix.md#measured-cost-2026-10-28). Not
measured: iOS, WebGL, Medium and High stripping, and a game's own types.
