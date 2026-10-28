# Native plugin compatibility

The managed sources require native bridge version **110**. The repository
does not ship prebuilt plugins: the upstream binaries were version 105 and
failed the version check, so they were removed. Build the plugin from this
directory, or download the `xlua-lua-<version>-<platform>` artifact from the
`native-build.yml` workflow, and copy it into the matching `Assets/Plugins`
directory. The committed `.meta` files there carry the Unity import settings.

Version 106 adds `xlua_gc`, a fixed-signature wrapper around `lua_gc`.
Lua 5.4 declares `lua_gc` as variadic. Managed callers must use the wrapper
so GC parameters follow the native calling convention on Apple ARM64.
The same wrapper supports Lua 5.3 and LuaJIT.

## macOS ARM64 source builds

Keep output directories outside the checkout, for example on an external SSD:

```sh
cmake -S build -B /path/to/builds/lua54 -DBUILD_SILICON=ON -DLUA_VERSION=5.4.9 -DCMAKE_BUILD_TYPE=Release
cmake --build /path/to/builds/lua54 --parallel 4
```

Select `-DLUA_VERSION=5.3.6` and a separate output directory to build Lua 5.3.
The default CMake build selects Lua 5.5.1. Select `-DLUA_VERSION=5.4.9`
for Lua 5.4. Scripts named `lua53` and the
legacy UWP scripts explicitly select 5.3.6. Use a fresh output directory
when changing runtime versions. Bytecode and scripts that rely on
a particular Lua version require an explicit runtime selection.
`LUAC_COMPATIBLE_FORMAT` is a Lua 5.3-only extension; CMake rejects it for
Lua 5.4 or 5.5 instead of silently ignoring the option.

## macOS and iOS deployment targets

A Mach-O library records the oldest OS it runs on (`minos` in its
`LC_BUILD_VERSION` load command), and the loader refuses it on anything older.
Without a deployment target, Xcode and clang use the OS of the build machine,
so until 2026-10-18 the macOS plugins built on macOS 27 needed macOS 27.

| platform | target | set by |
| --- | --- | --- |
| macOS bundle (x86_64 and arm64) and arm64 dylib, Lua 5.x and LuaJIT | 12.0 | `build/CMakeLists.txt` (`CMAKE_OSX_DEPLOYMENT_TARGET`), the `make_osx_*.sh` scripts (`MACOSX_DEPLOYMENT_TARGET`, also for `libluajit.a`) |
| iOS arm64 | 15.0 | the `make_ios_*.sh` scripts (`IOS_DEPLOYMENT_TARGET`, passed to `cmake/ios.toolchain.cmake` and to LuaJIT's `-miphoneos-version-min`) |

Why these numbers:

- The supported Unity versions run players on macOS 10.13 (2021.3), 10.14
  (2022.3), 11 (6000.0) and 12 (6000.3), and on iOS 12 (2021.3, 2022.3 before
  2022.3.72f1), 13 (6000.0) and 15 (6000.3), from Unity's
  [system requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html).
- The lowest targets the toolchains build: Xcode 27's macOS 27 SDK accepts
  macOS 12.0 and later and its iOS 27.1 SDK iOS 15.0 and later
  (`MinimumDeploymentTarget` in each SDK's `SDKSettings.json`); with a lower
  macOS target the Xcode generator, which the scripts use, fails with "No
  CMAKE_CXX_COMPILER could be found". Xcode 26 (the `macos-26` CI runner)
  goes down to macOS 11 and iOS 15
  ([Apple](https://developer.apple.com/support/xcode/)).
- So 12.0 and 15.0 are the lowest targets both Xcode versions build. An app
  for an older macOS needs a plugin built with an older Xcode:
  `MACOSX_DEPLOYMENT_TARGET=11.0 build/make_osx_lua55.sh` with Xcode 26, or
  10.13 with Xcode 16. App Store uploads need the Xcode 26 SDK or later,
  which already means iOS 15. A Ninja build with Xcode 27's command-line
  clang accepts a lower macOS target (it raises arm64 to 11.0, the first
  macOS on Apple silicon), but the SDK does not support it and such a
  plugin was not tried on an older macOS.

The macOS scripts pass `MACOSX_DEPLOYMENT_TARGET` (default 12.0) to CMake,
and a CMake build of its own on a Mac (`cmake -S build -B ...` without a
toolchain file, as CI does) gets 12.0 unless `-DCMAKE_OSX_DEPLOYMENT_TARGET`
or the environment variable says otherwise. Check a library with:

```sh
python3 Test/Native/check_apple_minos.py --platform macos plugin_lua55/Plugins/arm64/libxlua.dylib plugin_lua55/Plugins/xlua.bundle/Contents/MacOS/xlua
python3 Test/Native/check_apple_minos.py --platform ios plugin_lua55/Plugins/iOS/libxlua.a
```

It reads `otool -l` for every architecture of a universal file and every
object of a static library, and fails when one needs more than macOS 12.0 or
iOS 15.0 (`--macos`, `--ios` change the limits). `vtool -show-build <file>`
shows the same for a linked image. There are no tvOS or visionOS scripts;
the toolchain file builds them (`PLATFORM=TVOS`, `VISIONOS`), and the check
knows their limits (tvOS 15.0, visionOS 1.0).

All macOS scripts take `XLUA_BUILD_ROOT` for the build directory and
`plugin_lua*/` (default: this directory), like the iOS and Android scripts.
`make_osx_silicon_luajit.sh` builds in `build_lj_osx_silicon`, so it no longer
shares a CMake cache with `make_osx_luajit.sh`.

## Native layers and presets

xLua's own C code has two layers ([core-luascript.md](../docs/design/core-luascript.md),
section 3.1):

- `xlua_core.c` (with the internal header `xlua_core.h`): the layer every
  preset builds. It holds the GC option wrapper (`xlua_gc`), the C# function
  trampolines with their deferred error, the error handler of the C# side's
  protected calls, and the states of `LuaEnv(LuaOptions)`: a counting
  allocator with the memory limit, and the hash seed. For Lua 5.4 the seed
  goes through `xlua_luahooks.h`, which the vendored `luaconf.h.in` (5.4) and
  `luaconf.h` (5.5) include when `XLUA_LUA_HOOKS` is defined (CMake defines it
  for libxlua; [LUA_PATCHES.md](LUA_PATCHES.md)).
- `xlua_core_limits.c`: the instruction and time limits (the count hook, the
  stop hook, the coroutine switches, `Interrupt`, the watchdog entry point and
  the C budget that the patched `lstrlib.c` and `ltablib.c` call through the
  same header). A file of its own so that the size flags compile it at `-Oz`;
  it runs per call, slice or switch, not per instruction.
- `xlua.c`, `i64lib.c` and `memory_leak_checker.c`: the C# bridge, built on
  top of it.
- `xlua_libs.c`: compiled in place of Lua's `linit.c` only when
  [`XLUA_LIBS`](#standard-libraries) leaves libraries out, in the bridge presets,
  and on LuaJIT also when one of the [library parts](#parts-of-os-package-and-the-base-library)
  is off.
- `xlua_core_sandbox.c`: LuaScript's native sandbox (`xlua_core_open`,
  environments, the wrapped `load`, `setmetatable` and `collectgarbage`,
  `require` through a host resolver; core-luascript.md, section 8.4), and
  handles to tables, functions and buffers (`xlua_core_handle`, section 8.7). The
  core preset always builds it; the bridge presets with `XLUA_LUASCRIPT=ON`
  (Lua 5.4 and 5.5; off by default, decided 2026-10-18).
- `xlua_core_buffer.c`: LuaScript's `buffer` library (`LuaLibs.Buffer`,
  core-luascript.md section 2.10), whose buffers `xlua_core_handle` creates and
  reads for C#. Built with the sandbox; the library itself only with
  [`XLUA_BUFFER`](#luascripts-buffer-library), without it those operations
  report that the build has no buffer.
- `xlua_core_nobc.c`: the core preset only, in place of Lua's `lundump.c` and
  `ldump.c`: stubs that refuse binary chunks, so a core library contains no
  bytecode loader. With [`XLUA_TRUSTED_BYTECODE`](#bytecode-options) it
  replaces only `ldump.c`.
- `xlua_noparser.c`: with [`XLUA_BYTECODE_ONLY`](#bytecode-options), in place
  of Lua's `lparser.c`, `lcode.c` and `llex.c`: a stub that refuses text chunks.
- `xlua_chunkc.c`: with `XLUA_BYTECODE_ONLY`, a host program, not part of the
  library: it compiles LuaEnv's built-in chunks from the C# sources into
  `xlua_builtin_chunks.h`, which `xlua.c` includes
  ([LuaEnv on a bytecode-only library](#luaenv-on-a-bytecode-only-library)).

Anything that compiles the sources without this CMake file (a source plugin,
another build system) must compile `xlua_core.c` as well. The CMake option
`XLUA_PRESET` selects the file set of a preset and the defaults of the native
options ([presets](#presets) below).

### Presets

`XLUA_PRESET` is `legacy` (the default), `minimal` or `core`
([presets.md](../docs/design/presets.md)). Like the other options it is read from
the environment too: `XLUA_PRESET=minimal build/make_android_lua55.sh`.

- **`legacy`** keeps the defaults the options had before presets existed; the
  library is unchanged.
- **`minimal`** changes the defaults to the minimal column
  (`build/xlua_presets.cmake`): `XLUA_LUASOCKET`, `XLUA_MIME`,
  `XLUA_LEAK_CHECKER` and `XLUA_MEMORY_SNAPSHOT` off (the snapshot stays on
  for the iOS, tvOS and visionOS static libraries, see
  [Memory snapshot](#memory-snapshot)), `XLUA_LIBS` without `io`
  (`package;coroutine;table;os;string;math;utf8;debug`, LuaJIT
  `package;table;os;string;math;debug;bit;jit;ffi`), `XLUA_OS_FULL`,
  `XLUA_LOADLIB` and `XLUA_DOFILE` off (`os` with `time`, `clock`, `date` and
  `difftime` only; no `package.loadlib`, C searchers or `dlopen`; no `dofile`
  or `loadfile`), and `XLUA_EXPORTS=managed`
  on Android, OpenHarmony and Linux (`all` for static, Mach-O and Windows
  libraries). `XLUA_UINT64`, `XLUA_HARDEN`, `XLUA_CHEAP_HARDEN`, `XLUA_ICF` and `XLUA_SIZE_FLAGS`
  keep their legacy defaults. Sizes are in [SIZE.md](SIZE.md#presets).
- **`core`** builds LuaScript's native layer without the C# bridge: `xlua_core.c`
  (with `XLUA_CORE_ONLY`, without the bridge's trampolines and error handler),
  `xlua_core_limits.c`, `xlua_core_sandbox.c`, `xlua_core_buffer.c` and `xlua_core_nobc.c`, and Lua
  without `linit.c`, `lundump.c` and `ldump.c`. Its defaults: `XLUA_LIBS=coroutine;table;string;math;utf8`,
  [`XLUA_BUFFER=ON`](#luascripts-buffer-library),
  `XLUA_EXPORTS=managed` on Android, OpenHarmony and Linux (the list is
  `xlua_core_exports.txt`), every bridge part off. Turning on `XLUA_LUASOCKET`,
  `XLUA_MIME`, `XLUA_LEAK_CHECKER`, `XLUA_UINT64` or `XLUA_MEMORY_SNAPSHOT`, or
  putting `io`, `os`, `debug` or `package` into `XLUA_LIBS`, stops the
  configuration, and so do LuaJIT and Lua 5.3. `XLUA_VARIANT=core
  XLUA_PRESET=core build/make_android_lua55.sh` builds the `core` variant (also
  `lua54`); the C# that uses it (`LuaScript`) does not exist yet. Sizes are in
  [SIZE.md](SIZE.md#the-core-preset). [`XLUA_TRUSTED_BYTECODE=ON`](#bytecode-options)
  adds Lua's bytecode loader back for the host's own precompiled chunks.

A preset only sets defaults. An option given with `-D`, already in the cache or
set in the environment wins, so a project changes one part of a preset:
`XLUA_PRESET=minimal XLUA_LUASOCKET=ON build/make_ios_lua55.sh`, or
`-DXLUA_PRESET=minimal -DXLUA_EXPORTS=all` for a project that loads its own
native Lua modules. Because cached options keep their values, a build directory
stays with the preset it was first configured with: CMake stops when
`-DXLUA_PRESET` or the environment asks for another one. The `make_*` scripts that
support variants build a preset other than legacy in build directories of their
own (suffix `.preset-<name>`), and `XLUA_VARIANT=minimal` puts the library where
the [plugin selector](#plugin-variants) finds it:

```sh
XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_android_lua55.sh
XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_ios_lua55.sh
```

The C# side is the scripting define `XLUA_PRESET_MINIMAL` ([MIGRATING.md](../docs/MIGRATING.md#presets)).
`Test/Native/presets_check.py` checks the defaults of each preset against
`Tools/presets.py`, the overrides and the refusals; `check_exports.py --preset
core` and `check_android_elf.py --preset core` check a core library's exports; `Tools/presets.py
--expect-env minimal` prints the `XLUA_EXPECT_*` variables of a minimal library
for `check_exports.py`, `native_regression.py` and `check_android_elf.py`, and
`run_managed.py --define XLUA_PRESET_MINIMAL` and `sanitize.sh` with
`XLUA_PRESET=minimal` set them themselves.

## Warnings, hardening and test options

xLua's own C files (`xlua_core.c`, `xlua_libs.c`, `xlua.c`, `i64lib.c`,
`memory_leak_checker.c`) always compile with a warning set; the vendored Lua, LuaJIT and LuaSocket sources do
not, and their include directories are `SYSTEM` directories so that their
headers do not report warnings in xLua's files. The set and its reasons are in
[docs/research/c17-setup.md](../docs/research/c17-setup.md):

- **GCC and Clang:** `-Wall -Wextra -Wpedantic -Wformat=2 -Wshadow -Wundef
  -Wstrict-prototypes -Wold-style-definition -Wcast-qual -Wwrite-strings
  -Wcast-align -Wconversion -Wno-sign-conversion`, plus `-Wvla
  -Wimplicit-fallthrough -Wdouble-promotion -Wnull-dereference -Wc++-compat`
  where the compiler knows them. `-Werror=return-type`,
  `implicit-function-declaration`, `int-conversion` and
  `incompatible-pointer-types` are always errors. `-Werror=date-time` applies
  to every file of the library ([reproducible builds](#reproducible-builds)).
- **MSVC:** `/W4 /utf-8`, with C4013, C4024, C4047, C4133 and C4716 as errors.

CMake options (all `OFF` by default except `XLUA_CHEAP_HARDEN`):

| Option | Effect |
|---|---|
| `XLUA_WERROR` | Warnings in xLua's own files become errors (`-Werror`, `/WX`). CI turns it on. |
| `XLUA_CHEAP_HARDEN` | `ON` in every preset (since 2026-10-18): the cheap hardening set, see below. |
| `XLUA_HARDEN` | Hardening for the whole library, see below. |
| `XLUA_APICHECK` | Lua's C API argument checks (`LUA_USE_APICHECK`, with `NDEBUG` removed so that `assert` stays on). For test builds; `sanitize.sh` and `coverage.sh` use it. LuaJIT compiles its checks into `libluajit.a`: also build it with `XCFLAGS=-DLUA_USE_APICHECK`. With `USING_LUAJIT`, configure stops when `LUAJIT_STATIC_LIB` has no `lj_assert_fail`, that is, was made without the checks. With MSVC it also adds `xlua_apicheck_msvc.c`, which sends a failed check to stderr instead of a message box (the static C runtime does not know the host is a console program); with `msvcbuild_mt.bat`, set `CL=/DLUA_USE_APICHECK` for `lua51.lib`. `Test/Native/apicheck_probe.py` checks that a build has the checks. |
| `XLUA_SANITIZE`, `XLUA_COVERAGE` | Sanitizer and coverage test builds, see [Test/README.md](../Test/README.md). |

Size options ([size flags](#android-size-flags) below):

| Option | Default | Effect |
|---|---|---|
| `XLUA_SIZE_FLAGS` | `ON` for Android and OpenHarmony, `OFF` elsewhere | `-O3` for the files in `XLUA_HOT_FILES`, `-Oz` for the rest (with Clang outside Apple also `-mllvm -enable-machine-outliner=never`), identical code folding, and Android's packed relocations at API 23 to 27. Debug builds keep `-O0`. Ignored for MSVC. |
| `XLUA_HOT_FILES` | 18 Lua files, `xlua.c` and `xlua_core.c` | The file names `XLUA_SIZE_FLAGS` compiles at `-O3` (advanced). |
| `XLUA_ICF` | `ON` | With `XLUA_SIZE_FLAGS`: `-Wl,--icf=all` where the linker supports it (lld, gold). |
| `XLUA_EXPORTS` | `all` (`minimal` preset: `managed` for ELF shared libraries; also read from the environment) | `all` exports every non-static function, as upstream. `managed` compiles with `-fvisibility=hidden` and links with a version script that exports only the functions `LuaDLL.cs` imports (`build/xlua_exports.txt`) that this build contains ([export list](#the-export-list-follows-the-options)); ELF shared libraries only (Android, OpenHarmony, Linux). |
| `XLUA_EXPORTS_EXTRA` | empty | With `XLUA_EXPORTS=managed`: more functions to export, for native plugins that call them. |

`XLUA_WERROR`, `XLUA_CHEAP_HARDEN` and `XLUA_HARDEN` take their default from an environment
variable of the same name, so the `make_*` scripts need no change:
`XLUA_HARDEN=ON build/make_android_lua55.sh`. An explicit `-D` or an existing
CMake cache entry wins.

`XLUA_CHEAP_HARDEN` (the owner's decision of 2026-10-18, measured in
[FLAGS.md](FLAGS.md#a-cheaper-hardening-split)) adds, where the compiler
accepts them:

| Flag | Where |
|---|---|
| `-fno-delete-null-pointer-checks` | every C file of the library (the vendored Lua and LuaSocket sources too), GCC and Clang |
| `-ftrivial-auto-var-init=zero` | xLua's own files only (`XLUA_SOURCES`: `xlua.c`, `i64lib.c`, `memory_leak_checker.c`, `xlua_libs.c`, `xlua_noparser.c`, `xlua_core*.c`), GCC 12+ and Clang 16+, not with `XLUA_SANITIZE=memory` |

It costs 0.3 to 0.5 KB stripped and 0.1 to 0.5 KB of APK on Android
([SIZE.md](SIZE.md#cheap-hardening)) and no measurable speed. MSVC has neither
flag and gets nothing; with `XLUA_HARDEN` the full set below applies instead.
Not covered: LuaJIT's `libluajit.a` (its own Makefile; `XLUA_HARDEN` reaches
it), the host program `xlua_chunkc` of bytecode-only builds, and the WebGL and
Android source plugins, which Unity compiles with its own flags.
`-DXLUA_CHEAP_HARDEN=OFF` gives the library of before, byte for byte. Leave it
off for valgrind runs of xLua's own files, for the same reason as below.

`XLUA_HARDEN` adds, where the compiler accepts them (`check_c_compiler_flag`):

| Flag | Where |
|---|---|
| `-fstack-protector-strong` | GCC and Clang; the NDK already adds it |
| `-fno-delete-null-pointer-checks` | GCC and Clang |
| `-ftrivial-auto-var-init=zero` | GCC and Clang, not with `XLUA_SANITIZE=memory`. Leave `XLUA_HARDEN` off for valgrind runs: zeroed locals hide the reads valgrind looks for |
| `-mbranch-protection=standard` (PAC and BTI) | arm64 Android, OpenHarmony and Linux |
| `-fcf-protection`, `-fstack-clash-protection` | Linux x86_64 |
| `-D_FORTIFY_SOURCE=2` | Non-Debug builds without `XLUA_SANITIZE`, unless the toolchain already sets a level (the NDK sets 2, Ubuntu's GCC 3) |
| `/guard:cf` (compiler and linker) | MSVC |

LuaJIT's own Makefile builds `libluajit.a` with `-U_FORTIFY_SOURCE
-fno-stack-protector` (`luajit-2.1/src/Makefile`), which no CMake option
reaches. With `XLUA_HARDEN` set in the environment, the LuaJIT scripts
(`make_android_luajit.sh`, `make_osx_luajit.sh`, `make_osx_silicon_luajit.sh`,
`make_ios_luajit.sh`, `make_linux64_luajit.sh`, `make_linux32_luajit.sh`) pass
the same flags as `TARGET_CFLAGS`, which the Makefile puts after its own
(`luajit_harden.sh`): `-fstack-protector-strong`,
`-fno-delete-null-pointer-checks`, `-ftrivial-auto-var-init=zero` and
`-D_FORTIFY_SOURCE=2`; on arm64 Android and Linux also
`-mbranch-protection=standard -DLUAJIT_ENABLE_CET_BR`, which puts BTI landing
pads into LuaJIT's interpreter and JIT code (`luajit-2.1/src/lj_arch.h`); on
Linux x86_64 `-fcf-protection -fstack-clash-protection
-DLUAJIT_ENABLE_CET_BR` (LuaJIT notes that IBT slows its interpreter on
every x64 CPU; not built or measured here).

`python3 Test/Native/check_android_elf.py --expect-branch-protection` checks
that an arm64-v8a library carries the BTI and PAC property note. The linker
writes that note only when every object has it. A LuaJIT library carries BTI
only: LuaJIT's interpreter signs return addresses only on arm64e, so its
object's note has no PAC bit, although the C code is still built with PAC.
The check accepts BTI alone for LuaJIT. The size and speed cost is in
[FLAGS.md](FLAGS.md#xlua_harden), together with a cheaper set measured for
the default (`-fno-delete-null-pointer-checks` everywhere and zeroed locals in
xLua's own three files), which is not applied.

## Reproducible builds

The same sources and options give the same bytes in any checkout and any
build directory (since 2026-10-28). `build/CMakeLists.txt` adds, for every
target and every C file, vendored ones included:

| Flag | Compilers | Why |
|---|---|---|
| `-ffile-prefix-map=<build/ of the checkout>=build` and `-ffile-prefix-map=<build directory>=.` | GCC 8+, Clang 10+ (older ones: `-fdebug-prefix-map` and `-fmacro-prefix-map`) | Debug information, `__FILE__` and assertion messages name `build/lua-5.5.1/src/lvm.c` instead of the absolute path. The build directory's mapping comes last, so it wins when the directory lies inside `build/`, as the `make_*` scripts put it. |
| `-Werror=date-time` | GCC, Clang | `__DATE__`, `__TIME__` and `__TIMESTAMP__` stop the build. No file uses them. |
| `-Wl,-oso_prefix,<build directory>/` | Apple's linker | Relative object paths in the OSO entries that a library with debug information carries for `dsymutil`. |
| `/Brepro` (compiler and linker), `/pathmap:<dir>=build` | MSVC, where `cl.exe` and `link.exe` take them | No time stamps in objects and DLLs; mapped paths. Not built or checked yet. |

A debugger finds the sources from the repository root (`build/...`); the
build directory itself is `.` (LLDB: `settings set target.source-map build
/path/to/xlua-ng/build`). Release builds for Linux and the Apple platforms
carry no debug information, so their bytes did not change; Android's do,
because the NDK compiles with `-g`.

`python3 Test/Native/repro_check.py --work DIR [-- CMAKE_ARGS]` copies
`build/` (and `Assets/XLua/Src`) to two checkouts whose paths differ in name
and length, builds the same configuration in each (one build directory inside
`build/`, one outside the checkout) and compares the libraries' SHA-256; with
`--script make_android_lua55.sh` it runs a release script in each copy and
compares every file of the `plugin_*` folder it writes. On a difference it
lists the object files that differ and the GNU build IDs. Each configure,
build or script run has a timeout (`--timeout`, default 1800 s) and is killed
with its process group.

Measured on 2026-10-28 (Apple clang 21 on macOS 27 arm64 with Xcode 27.1 and
NDK r30 30.0.16248370; GCC 15.2 and Clang 21.1.8 in the arm64
`xlua-analysis:26.04` container), all for Lua 5.5.1 unless noted, two
checkouts each:

| Build | Before | After |
|---|---|---|
| `make_android_lua55.sh`, four ABIs, stripped plugins and `symbols/` | all 8 files differ (208 of 212 objects) | same bytes (`arm64-v8a` `5472d106...`) |
| `make_android_lua55.sh` with `XLUA_VARIANT=minimal XLUA_PRESET=minimal`, and with `core` | not run | same bytes |
| `make_osx_lua55.sh` (Xcode, universal bundle), `make_osx_silicon_lua55.sh`, `make_ios_lua55.sh` (device and `XLUA_IOS_SDK=simulator`) | same bytes in the earlier check ([release.md](../docs/design/release.md#reproducibility)) | same bytes |
| macOS arm64 dylib, Ninja, `RelWithDebInfo` | differs (objects and dylib) | objects the same; the dylib differs only by the object times in its OSO entries, and is the same with `ZERO_AR_DATE=1` in the environment |
| Linux, GCC, `Release`; `make_linux_lua55.sh` | same bytes (`2ed2e3d9...`, unchanged by the flags) | same bytes (`2ed2e3d9...`) |
| Linux, GCC and Clang, `RelWithDebInfo` | differs (all 51 objects with GCC, 50 with Clang; different GNU build IDs) | same bytes |
| Linux, GCC: `Debug` with `XLUA_APICHECK`, Lua 5.3.6, `XLUA_BYTECODE_ONLY`; `make_linux64_luajit.sh` | not run | same bytes |

Apple's linker writes each object file's modification time into the OSO
entries of a library linked with debug information; `ZERO_AR_DATE=1` in the
environment makes it write 0. The release scripts build Apple plugins without
debug information, so they do not need it. **Owner decision (2026-10-28):**
`ZERO_AR_DATE` stays documentation only. No script or CMake option sets it;
whoever needs identical Apple builds with debug information sets
`ZERO_AR_DATE=1` in the environment. LuaJIT's `libluajit.a` comes from
its own Makefile, which compiles without `-g` and with relative paths, so the
LuaJIT plugin was the same without any change there.

`publish.yml`'s `reproducibility-linux` and `reproducibility-apple` jobs
([release.md](../docs/design/release.md#reproducibility)) rebuild the release
plugins on a second runner and compare them with
`.github/scripts/compare-builds.sh`; they need no change. The same checkout
path on both runners is no longer required, and the Android build ID that
differed between build directories no longer does. `compare-builds.sh` was run
locally on two `make_osx_silicon_lua55.sh` checkouts (same, exit 0); the jobs
themselves have not run on hosted runners yet. Windows (MSVC) is not checked.

## LuaSocket and mime

LuaSocket's C core (`build/luasocket`) is compiled into the library by default,
and `luaopen_xlua` puts its loaders into `package.preload` as `socket.core` and
`socket` (the same module under both names; the LuaSocket Lua helpers such as
`socket.http` are not shipped), and `mime.c` as `mime.core` and `mime`. The
managed code imports nothing for them: `LuaEnv` registers nothing, and the same
C# works with any of these builds.

| Option | Default | Effect |
|---|---|---|
| `XLUA_LUASOCKET` | `ON` | `OFF` leaves LuaSocket and `mime.c` out of the library; `require "socket"` then fails with `module 'socket' not found`. |
| `XLUA_MIME` | `ON` | With `XLUA_LUASOCKET`: `OFF` leaves only `mime.c` out. |

Both take their default from an environment variable of the same name, like
`XLUA_HARDEN`: `XLUA_LUASOCKET=OFF build/make_android_lua55.sh` or
`XLUA_MIME=OFF build/make_ios_lua55.sh`. LuaSocket is 44 KB stripped and
18 KB of APK on arm64-v8a, 30 KB and 15 KB on armeabi-v7a, and 28 KB of code
on iOS; mime 6 KB and 2.5 KB ([SIZE.md](SIZE.md#without-luasocket-or-mime)).
The registration works the same in a shared library and in the static library
that iOS links: it is a call from `luaopen_xlua`, which the C# side always
calls, so no constructor is needed and the linker keeps LuaSocket through that
reference. The Switch build and the WebGL source build never compile
LuaSocket, so nothing is registered there.

## Memory snapshot

`XLUA_MEMORY_SNAPSHOT` builds the memory snapshot walk (`xlua_memory_snapshot`
in `memory_leak_checker.c`) that `LuaEnv.TakeMemorySnapshot()` and the
**XLua/Lua Memory** window use. Only the Editor and Development Builds compile
that C# API ([Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md#memory-snapshots)).

| Platform | Default | Why |
|---|---|---|
| macOS, Windows, Linux | `ON` | the Editor loads these plugins |
| iOS, tvOS, visionOS | `ON`, also in the minimal preset | a static library: release players drop the walk because nothing calls it, and a Development player does not link without it (below) |
| Android, OpenHarmony, UWP | `OFF` | the shared library ships in every player: about 7.5 KB stripped on arm64-v8a ([SIZE.md](SIZE.md)) |
| Switch | `OFF` | |

The WebGL build (`xlua_webgl.cpp`) and the Android source plugin do not go
through CMake and compile the walk; release players strip it. Like
`XLUA_HARDEN`, the option takes its default from an environment variable of
the same name, so a development Android library is
`XLUA_MEMORY_SNAPSHOT=ON build/make_android_lua55.sh`. Without the walk the
library also leaves the two functions out of the `XLUA_EXPORTS=managed`
version script, and `TakeMemorySnapshot` throws `NotSupportedException`
with these instructions. Outside CMake, `-DXLUA_NO_MEMORY_SNAPSHOT` leaves
the walk out.

A static library cannot leave the walk out on its own: IL2CPP links every
`__Internal` import that the player's C# keeps, and the Editor and
Development Builds keep `xlua_memory_snapshot` and
`xlua_memory_snapshot_free`. An iOS Development player with a library built
with `XLUA_MEMORY_SNAPSHOT=OFF` therefore stops in the Xcode link with
"Undefined symbols: _xlua_memory_snapshot" (found on 2026-10-18 with the
minimal preset's library, which had the option off; the preset keeps it on
for Apple static libraries since). Android loads the functions lazily and
throws `NotSupportedException` instead.

## Leak checker, uint64 table and standard libraries

Three more parts of the library are CMake options. Each takes its default from
an environment variable of the same name, like `XLUA_LUASOCKET`, so the
`make_*` scripts need no change (`XLUA_LEAK_CHECKER=OFF
build/make_android_lua55.sh`); an explicit `-D` or an existing cache entry
wins. The defaults are today's library, the `legacy` preset.

| Option | Default | Effect |
|---|---|---|
| `XLUA_LEAK_CHECKER` | `ON` | `OFF` leaves the internal-header leak checker of `memory_leak_checker.c` out: `xlua_report_table_size`, `xlua_report_object_relationship`, `xlua_registry_pointer` and `xlua_global_pointer`, which only `General/LuaMemoryLeakChecker` imports. Lua 5.3 to 5.5; LuaJIT never had it. The memory snapshot walk (`XLUA_MEMORY_SNAPSHOT`) does not depend on it. |
| `XLUA_UINT64` | `ON` | `OFF` leaves the `uint64` table out (`uint64.tostring`, `compare`, `divide`, `remainder`, `parse`; the export `uint64_parse`); `luaopen_i64lib` then sets nothing. Lua 5.3 to 5.5 only: CMake refuses it for LuaJIT. |
| `XLUA_LIBS` | `all` | The Lua standard libraries the library contains and `luaL_openlibs` opens, see [below](#standard-libraries). |
| `XLUA_OS_FULL`, `XLUA_LOADLIB`, `XLUA_DOFILE` | `ON` | `OFF` leaves a part of `os`, `package` or the base library out, see [below](#parts-of-os-package-and-the-base-library). |

What they save is in [SIZE.md](SIZE.md#leak-checker-uint64-table-and-standard-libraries):
on Android arm64-v8a with Lua 5.5.1, 1.4 KB stripped for each of the first
two, and 17.6 KB stripped (7.4 KB of APK) for a library without `io`, `os`
and `utf8`.

### The uint64 table and 64-bit integers

64-bit integers stay lossless with or without the table. On Lua 5.3 and later
a C# `long` or `ulong` is a Lua integer, and `lua_pushint64`,
`lua_pushuint64`, `lua_toint64`, `lua_touint64`, `lua_isint64` and
`lua_isuint64`, which `LuaDLL.cs` imports, stay in `i64lib.c` either way. The
table only gives Lua code an unsigned view of those 64 bits: without it a
`ulong` of 2^63 or more reads as a negative integer (`UInt64.MaxValue` is
`-1`), still compares and divides as a signed one, and `math.ult` and
`string.format("%d")` are what is left; the value goes back to C# unchanged.
The C# side does not need the table: `LuaEnv`'s `XLUA_LOCK_METATABLES` code
and `LuaCATSGenerator` check for it first.

LuaJIT keeps the table (`XLUA_UINT64=OFF` stops the configuration). There a
64-bit integer is the int64 userdata of `i64lib.c`, and the table is the only
way to print it unsigned, to compare or divide it unsigned, to parse one from
a string and to use the bitwise, shift and floor division helpers; `LuaEnv`
also reaches the userdata's metatable through `uint64.parse` to lock it under
`XLUA_LOCK_METATABLES`. The table is 1.4 KB of the 8.9 KB `i64lib.c` there.

### Standard libraries

`XLUA_LIBS` is `all` or a list of library names, separated by semicolons on
the command line (`-DXLUA_LIBS="package;coroutine;table;string;math;debug"`)
and by commas or semicolons in the environment
(`XLUA_LIBS=package,coroutine,table,string,math,debug`). Names are case
insensitive; `base` is always opened and may be listed.

| Runtime | Names |
|---|---|
| Lua 5.3, 5.4, 5.5 | `package`, `coroutine`, `table`, `io`, `os`, `string`, `math`, `utf8`, `debug` |
| LuaJIT | `package`, `table`, `io`, `os`, `string`, `math`, `debug`, `bit`, `jit`, `ffi` |

- **A list leaves the other libraries out of the library.** CMake drops
  their source files and Lua's `linit.c`, which references every library
  through its table, and compiles `xlua_libs.c` instead. That file defines
  `luaL_openlibs` (Lua 5.3, 5.4, LuaJIT) or `luaL_openselectedlibs` (Lua 5.5,
  with the `LUA_*LIBK` bits of `lualib.h`; bits of libraries the build does
  not have are ignored) for the libraries in the list, in `linit.c`'s order.
  `luaopen_xlua` calls `luaL_openlibs` as before, so the C# side is the same.
  The left-out libraries' `luaopen_*` functions are not in the library, and a
  left-out library is a `nil` global.
- **With `all`, or a list naming every library,** CMake builds `linit.c` as
  before and not `xlua_libs.c`: the default library is unchanged byte for
  byte in size.
- **The `legacy` preset needs `package` and `debug`.** `LuaEnv` adds its
  searchers to `package.searchers`, and its error handler calls
  `debug.traceback`. CMake stops with a message when either is missing.
  `xlua.util` uses `coroutine`, `debug`, `string` and `table`; the profiler
  `debug`, `os`, `string` and `table`.
- **LuaJIT** links `libluajit.a`, so the list decides what is opened and what
  the linker pulls in: `io`, `os`, `bit` and `jit` drop out, while
  `lib_ffi.o` stays because other parts of LuaJIT reference it (without
  `ffi` in the list it is just not in `package.preload`). `coroutine` is part
  of LuaJIT's base library and `utf8` does not exist there; CMake ignores
  those two names for LuaJIT, and `bit`, `jit` and `ffi` for Lua 5.x, so one
  list serves every runtime. Scripts usually test `jit ~= nil` to detect
  LuaJIT, xLua's own test suites too; leaving `jit` out breaks that test.
  `make_ios_luajit.sh` merges `libluajit.a` into `libxlua.a`; with a list it
  removes LuaJIT's `lib_init.o` from the copy it merges, because both that
  object and `xlua_libs.o` define `luaL_openlibs`. Other LuaJIT builds link
  the static library after `xlua_libs.o`, so the linker does not take
  `lib_init.o`.
- **Not part of this option:** parts of a library ([below](#parts-of-os-package-and-the-base-library))
  and the run-time selection `LuaOptions.Libraries`, which a `LuaEnv` applies
  in C# within what the library contains
  ([XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#luaenv-luaenvluaoptions-options)).

### Parts of `os`, `package` and the base library

Three options leave out the functions of a library that reach the file
system, the process or native code, for the `minimal` preset. Each is `ON` by
default (and in `legacy` and `core`), `OFF` in `minimal`, and read from the
environment like the others (`XLUA_OS_FULL=OFF build/make_android_lua55.sh`).

| Option | `OFF` leaves out | Stays |
|---|---|---|
| `XLUA_OS_FULL` | `os.exit`, `getenv`, `remove`, `rename`, `setlocale`, `tmpname` | `os.time`, `clock`, `date`, `difftime` |
| `XLUA_LOADLIB` | `package.loadlib`, the two C searchers (`package.cpath` is still set, nothing reads it) and `dlopen`; the library is not linked with `libdl` | `require` with `package.preload`, the Lua searcher (`package.path`) and xLua's loaders |
| `XLUA_DOFILE` | `dofile`, `loadfile` | `load`, `require` |

- **PUC Lua:** CMake defines `XLUA_OS_TIME_ONLY`, `XLUA_NO_LOADLIB` and
  `XLUA_NO_DOFILE` for the `xlua` target, and guards in the vendored
  `loslib.c`, `loadlib.c` and `lbaselib.c` ([LUA_PATCHES.md](LUA_PATCHES.md))
  leave the functions and their table entries out, so their code is not in
  the library. `XLUA_NO_LOADLIB` also undefines `LUA_USE_DLOPEN` and
  `LUA_DL_DLL` in `loadlib.c`, so Lua's stubs replace `dlopen` and
  `LoadLibrary`. The stand-alone test interpreter is not affected. On Android
  the NDK still records `libdl.so` as needed; the library imports nothing
  from it.
- **LuaJIT:** its libraries are in `libluajit.a`, which is not patched, so
  `xlua_libs.c` removes the same fields when it opens the libraries (and
  `os.execute`, which LuaJIT still has, and the C loaders of
  `package.loaders`); the code and `dlopen` stay.
- **Sizes** (Android arm64-v8a, Lua 5.5.1, the minimal preset,
  [SIZE.md](SIZE.md#parts-of-os-package-and-the-base-library)): 2,008 bytes
  stripped for `XLUA_OS_FULL`, 2,016 for `XLUA_LOADLIB`, 656 for
  `XLUA_DOFILE`, 4,664 (1,845 of APK) together.
- **Checks:** `native_regression.py` (`library_parts`) and the managed case
  `CaseLibraryPartsFollowTheBuild` require each part whole or absent, as
  `XLUA_EXPECT_OS_FULL`, `XLUA_EXPECT_LOADLIB` and `XLUA_EXPECT_DOFILE` say;
  `check_exports.py` and `check_android_elf.py` require that a PUC Lua library
  with `XLUA_EXPECT_LOADLIB=OFF` imports no `dlopen`.

The WebGL build (`xlua_webgl.cpp`) and the Android source plugin do not go
through CMake: they keep every part. Outside CMake, `-DXLUA_NO_LEAK_CHECKER`
and `-DXLUA_NO_UINT64` leave the leak checker and the table out,
`-DXLUA_OS_TIME_ONLY`, `-DXLUA_NO_LOADLIB` and `-DXLUA_NO_DOFILE` the parts of
the libraries above; library selection needs `xlua_libs.c` in place of
`linit.c` and the `XLUA_LIB_<NAME>` defines of the libraries to keep.

### The export list follows the options

`build/xlua_exports.txt`, the export list of `XLUA_EXPORTS=managed`, names
for each function the CMake option without which it is not built (a second
column, today only `XLUA_MEMORY_SNAPSHOT` for `xlua_memory_snapshot` and
`xlua_memory_snapshot_free`). CMake leaves such a function out of the version
script when the option is off, so the list is exactly the managed imports the
build contains. `Test/Native/managed_imports.py --write` regenerates the file
from the imports of `Assets/XLua/Core` and `Assets/XLua/Src/LuaDLLBridge.cs`
(`LuaDLL.cs` before 2026-10-18) and its table `OPTION_EXPORTS`, which names
the parts with managed imports (`XLUA_MEMORY_SNAPSHOT`, and `XLUA_LUASCRIPT`
for LuaScript's sandbox) and also lists the exports of the parts that the C#
does not import (the leak checker,
`uint64_parse`, `luaopen_socket_core`, `luaopen_mime_core`) and, in
`LIB_EXPORTS`, each standard library's `luaopen_*`. With `XLUA_EXPORTS=all`
those disappear with their part.

`check_exports.py` and `check_android_elf.py` check every part: all of its
exports or none, and, when `XLUA_EXPECT_<OPTION>` (`XLUA_EXPECT_LEAK_CHECKER`,
`XLUA_EXPECT_UINT64`, `XLUA_EXPECT_MEMORY_SNAPSHOT`, set to `ON` or `OFF`),
`XLUA_EXPECT_NATIVE_MODULES` or `XLUA_EXPECT_LIBS` (`all` or a list, like
`XLUA_LIBS`) is set, the parts that variable names. A managed import of a part
that is not built is not required. The managed list does not change with
`XLUA_LEAK_CHECKER`, `XLUA_UINT64`, `XLUA_LUASOCKET` or `XLUA_LIBS`, because
the C# imports nothing of them. The core preset uses its own list,
`build/xlua_core_exports.txt` (`CORE_EXPORTS` in `managed_imports.py`, which
`--write` also writes: the `xlua_core_*` functions LuaScript calls, `xlua_gc`
and five non-raising Lua API functions; the rest of the Lua C API that
`Assets/XLua/Core/LuaDLL.cs` declares serves the bridge and is not exported),
and a bridge library with `XLUA_LUASCRIPT=ON` exports both lists. The presets will differ further through the C#
defines that remove imports (for example the `print_*` helpers that core and
minimal leave out, [core-luascript.md](../docs/design/core-luascript.md),
decision 7).

Validation (2026-10-18):

- **macOS arm64:** `check_exports.py`, `native_regression.py` and
  `profiler_smoke.py` with the expectations set, for each option off on its
  own on Lua 5.3.6, 5.4.9 and 5.5.1, and for the default and the most reduced
  library (`XLUA_LIBS=package,coroutine,table,string,math,debug`, no leak
  checker, uint64 table, LuaSocket or snapshot) on Lua 5.5.1 and LuaJIT
  (LuaJIT with `jit` and the table). The managed suite in all three binding
  modes on Lua 5.5.1 and LuaJIT, default and reduced (the reduced managed
  runs keep `io` and `os` for the test harness).
- **Linux arm64** (`xlua-analysis:26.04`): `sanitize.sh` with ASan, UBSan
  and the C API checks, with `XLUA_LEAK_CHECKER=OFF`, `XLUA_UINT64=OFF`,
  `XLUA_LUASOCKET=OFF` and `XLUA_LIBS=package,coroutine,table,io,os,string,math,debug,jit`,
  on Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT.
- **Android** (NDK r30, Lua 5.5.1 arm64-v8a and armeabi-v7a, LuaJIT
  arm64-v8a): every configuration of [SIZE.md](SIZE.md#leak-checker-uint64-table-and-standard-libraries)
  built, and `check_android_elf.py` passes with the expectations, with
  `--managed-exports` for the `managed` builds. `check_android_elf.py` from
  before this change failed the default Android library, because it required
  the memory snapshot exports that Android leaves out.
- **iOS:** the libraries of SIZE.md built and linked into a probe; the
  `make_ios_luajit.sh` archive with a reduced list has no `lib_init.o`.
- **Not run:** a Unity Editor or player with these libraries, Windows,
  Linux x86_64, OpenHarmony and WebGL builds.

## Bytecode options

Two CMake options, decided on 2026-10-17
([backlog](../docs/design/backlog.md)). Both are off in every preset, and
each takes its default from the environment variable of the same name
(`XLUA_BYTECODE_ONLY=ON build/make_android_lua55.sh`).

| Option | Default | Effect |
|---|---|---|
| `XLUA_BYTECODE_ONLY` | `OFF` | `ON` leaves Lua's compiler out (`lparser.c`, `lcode.c`, `llex.c`); only precompiled chunks load. PUC Lua 5.3 to 5.5 with the bridge (`legacy`, `minimal`). |
| `XLUA_TRUSTED_BYTECODE` | `OFF` | `ON` lets LuaScript's host entry `xlua_core_call` (its `mode` field) load the host's own precompiled chunks; the core preset then contains Lua's `lundump.c` again. Needs the sandbox (`XLUA_PRESET=core` or `XLUA_LUASCRIPT=ON`). |

### LuaScript's buffer library

`XLUA_BUFFER` (milestone 10 of [core-luascript.md](../docs/design/core-luascript.md),
section 8.9) builds the `buffer` library of `xlua_core_buffer.c`: a fixed-size
byte array with Luau's function names (`buffer.create`, `readu32`, `writef64`,
`copy`, `fill` ...). It is `ON` in the core preset and `OFF` in legacy and
minimal, which may turn it on together with `XLUA_LUASCRIPT=ON`; without the
sandbox CMake refuses it. Like the other options it takes its default from the
environment (`XLUA_BUFFER=OFF XLUA_PRESET=core XLUA_VARIANT=core
build/make_android_lua55.sh`). A script gets the library only when
`LuaOptions.Libraries` names `LuaLibs.Buffer`; `LuaLibs.Default` and
`LuaLibs.Safe` leave it out. Its two C exports are there with the sandbox
whatever the option, so the export list does not depend on it. It costs about
7.8 KB stripped and 3 KB compressed on Android arm64-v8a ([SIZE.md](SIZE.md#the-buffer-library)).

### Bytecode-only builds

For projects that precompile every script, hotfix scripts included, with the
`luac` of the same Lua version (`build/luac`; Lua 5.3's format also depends on
`sizeof(int)` and `sizeof(size_t)`, see
[compatible_bytecode.md](../Assets/XLua/Doc/compatible_bytecode.md); 5.4's and
5.5's are the same on every little-endian target with 64-bit integers).

- **Left out:** `lparser.c`, `lcode.c` and `llex.c`. `build/xlua_noparser.c`
  defines the two functions the rest of Lua calls: `luaY_parser` (from
  `ldo.c`'s `f_parser`, for a text chunk that the load mode lets through) and
  `luaX_init` (from `lstate.c`, once per state). `lctype.c`, `lzio.c`,
  `lopcodes.c` and the dumper stay, because the number parser, the bytecode
  loader, the debug library and `string.dump` use them.
- **Text chunks** fail with `LUA_ERRSYNTAX` and `<chunk name>: text chunks are
  not supported in this build (bytecode only)`, wherever they come from:
  `luaL_loadbuffer` and `xluaL_loadbuffer` (`LuaEnv.DoString`, `LoadString`,
  the loaders), `load`, `loadfile`, `dofile` and `require`'s file searcher. A
  text chunk loaded with mode `"b"` keeps Lua's own "attempt to load a text
  chunk (mode is 'b')".
- **`lua_setfenv`** (`LuaEnv.LoadString` and `DoString` with an environment,
  `LuaFunction.SetEnv`) gave the function a fresh `_ENV` upvalue taken from an
  empty text chunk. In this build it takes it from a copy of the function, a
  stripped dump loaded again, under `lua_pcall`; the result is the same
  ([xlua.c](xlua.c), `setfenv_copy`).
- **`LuaEnv`'s own Lua source** is in the library, precompiled: see
  [below](#luaenv-on-a-bytecode-only-library).
- **Refused:** LuaJIT, whose parser (`lj_parse.c`) is compiled into
  `libluajit.a` by LuaJIT's own Makefile (leaving it out needs a build without
  the amalgamation that replaces `lj_parse.o`, or a source patch; `lj_lex.c`
  must stay, because `lj_bcread.c` reads bytecode through the lexer state);
  and LuaScript's sandbox (`XLUA_PRESET=core`, `XLUA_LUASCRIPT=ON`), which
  loads text.
- **Size** (Android, Lua 5.5.1, [SIZE.md](SIZE.md#bytecode-options)): 56.7 KB
  stripped and 27.3 KB of APK less on arm64-v8a, 37.2 KB and 23.6 KB on
  armeabi-v7a, without LuaEnv's chunks; with them (the default), 41.4 KB and
  19.4 KB less on arm64-v8a, 22.3 KB and 15.9 KB on armeabi-v7a.

#### LuaEnv on a bytecode-only library

`LuaEnv` runs Lua source of its own: `init_xlua` (the `CS` table, `typeof`,
`xlua.hotfix` ...), the removal of libraries for `LuaOptions.Libraries`, the
`pairs` helper of C# collections, and the embedded modules `xlua.util`,
`perf.profiler` and `xlua.memory`. Decided 2026-10-18 by the owner: a
bytecode-only library carries them precompiled, for its own Lua version only,
compiled at build time; nothing generated is committed and the C# side needs no
build step.

- **One source:** each chunk is a C# verbatim string between marker comments,
  `// <xlua-chunk name="init_xlua" chunkname="Init">` and `// </xlua-chunk>`
  (`LuaEnv.cs`, `ObjectTranslator.cs`, `EmbeddedModules.cs`). C# loads the
  text as before. `init_xlua` takes `LOCKED`, `LOCK_XLUA` and `HOTFIX` as
  arguments instead of text in front of it, so it has one form whatever the
  defines.
- **The build:** with `XLUA_BYTECODE_ONLY=ON`, `build/xlua_chunkc.c`, a host
  program built from the same vendored Lua as the library (as `luac` is), reads
  the markers of every `.cs` file in `Assets/XLua/Src`, compiles each chunk with
  its C# chunk name and writes `xlua_builtin_chunks.h` into the build directory;
  `xlua.c` includes it. The header changes when a C# file does. The program is a
  target of the library's own project when it can run on the build machine. For
  another system (Android, iOS, OpenHarmony, UWP, Windows ARM64 on x64) CMake
  configures and builds `build/cmake/xlua_chunkc` at configure time with the
  host's compiler (the toolchain's and Xcode's variables removed), passing
  `LUA_VERSION`, `LUAC_COMPATIBLE_FORMAT` and `LUA_IDSIZE`. CMake needs nothing
  new: no Python, no prebuilt `luac`.
- **The format:** Lua 5.4's and 5.5's bytecode is the same on every target
  (little-endian; 32-bit `int` and `Instruction`; 64-bit `lua_Integer` and
  `lua_Number`; `checkHeader` in `lundump.c` has no `size_t`). Lua 5.3's
  header also has `sizeof(size_t)`, so a 32-bit target compiled on a 64-bit host
  (armeabi-v7a, x86, OpenHarmony arm32, UWP ARM and Win32 cross builds) is
  refused unless `LUAC_COMPATIBLE_FORMAT=ON`; a native 32-bit build
  (`make_linux32_lua53.sh`, `make_win32_lua53.bat`) builds the program for the
  target and is not affected. `xlua_builtin_chunks.h` records the sizes it was
  compiled with, and `xlua.c` stops the build when they differ from the
  target's. LuaJIT stays refused.
- **At run time:** `luaopen_xlua` puts a loader into the registry
  (`xlua.builtin_chunk`); without the option nothing is registered, and the
  export list is the same in every build. `LuaEnv.LoadBuiltinChunk` uses the
  loader when it is there, with mode `"b"`, and the text otherwise. It passes
  a hash of its own text (FNV-1a without carriage returns, so CRLF and LF
  checkouts agree): a library built from other C# sources fails with "the
  precompiled chunk 'init_xlua' of this bytecode-only library was built from
  other C# sources than the ones running" instead of running other code.
- **The embedded modules** each have an option, which takes its default from
  the environment variable of the same name:

  | Option | Default | Module |
  |---|---|---|
  | `XLUA_BYTECODE_UTIL` | `ON` in legacy, `OFF` in minimal (C# `XLUA_NO_UTIL`) | `xlua.util` |
  | `XLUA_BYTECODE_PROFILER` | `ON` in legacy, `OFF` in minimal (C# `XLUA_NO_PROFILER`) | `perf.profiler` |
  | `XLUA_BYTECODE_MEMORY` | `XLUA_MEMORY_SNAPSHOT` | `xlua.memory` (Development Builds, `XLUA_MEMORY_DIAGNOSTICS`) |

  `require` of a module the library leaves out fails with "this bytecode-only
  library has no precompiled chunk 'xlua.util'". The options exist only with
  `XLUA_BYTECODE_ONLY`. `XLUA_CHUNKC` (a prebuilt host program) and
  `XLUA_CHUNK_SOURCES_DIR` (the C# folder) are advanced options.
- **Debug information** stays in the chunks (about 3.9 KB of the 14.4 KB on
  Lua 5.5.1), so errors and tracebacks name `Init` or `@xlua/util.lua` and the
  line, as with the text (kept by the owner's decision of 2026-10-18).
- **Not available:** the template engine (`template.compile`,
  `LuaTemplate.Compile`) composes Lua text at run time and fails with the
  text-chunk message; the Editor's code generator uses it with the Editor's own
  library.

### Trusted bytecode in LuaScript

LuaScript loads text (core-luascript.md, section 3.9). With
`XLUA_TRUSTED_BYTECODE=ON` a host may also run its own precompiled chunks
through `xlua_core_call` (`build/xlua_core.h`) with `kind = XLUA_CALL_CHUNK` and
the `mode` field (`xlua_core_run_mode` before milestone 6 of the core preset):

- **`mode`:** 0 or `XLUA_CORE_MODE_TEXT` (1), `XLUA_CORE_MODE_BINARY` (2) or
  both, Lua's modes `"t"`, `"b"` and `"bt"`. Without the option a mode with
  `XLUA_CORE_MODE_BINARY` returns `XLUA_ERRCONFIG` with "binary chunks are not
  supported in this build (XLUA_TRUSTED_BYTECODE is off)" in `error` before
  anything runs, and a mode with another bit "invalid mode".
- **The chunk** runs as a text chunk does: in the environment of `env`, with
  the state's limits. A precompiled function without upvalues runs too.
- **What stays:** scripts' `load` and `require` load text only, and
  `string.dump` stays out (`xlua_core_nobc.c` still replaces the dumper).
- **Only for the host's own chunks:** Lua does not verify bytecode, and a
  crafted chunk can corrupt memory. Never pass remote code with the binary
  mode; the security statement of
  [core-luascript.md](../docs/design/core-luascript.md), section 5, does not
  cover it.
- **Size** (Android, [SIZE.md](SIZE.md#bytecode-options)): the core library
  grows by 6.5 KB stripped and 2.9 KB of APK (Lua 5.5.1, arm64-v8a); the mode
  check is part of every core library.

`check_exports.py --preset core` and `check_android_elf.py --preset core` fail
a core library that contains the bytecode loader unless
`XLUA_EXPECT_TRUSTED_BYTECODE=ON` says it was built with the option.

### `LUA_COMPAT_APIINTCASTS`

LuaSocket's sources were compiled with `LUA_COMPAT_APIINTCASTS`, which defines
the macros `lua_pushunsigned`, `lua_tounsigned`, `luaL_checkint`,
`luaL_checklong` and their relatives. Nothing in LuaSocket, `xlua.c`,
`i64lib.c`, the other C files or `LuaDLL.cs` uses them (checked 2026-10-18),
so the definition is gone in every preset. It only defines macros: the Android
libraries (Lua 5.3.6, 5.4.9 and 5.5.1, legacy and minimal) are byte for byte
the same as before once the build ID is removed. No preset defines another
`LUA_COMPAT_*` option: `LUA_COMPAT_5_2` (5.3) and `LUA_COMPAT_5_3` (5.4) are
not defined, and Lua 5.5's `LUA_COMPAT_GLOBAL` is upstream's default (an open
question for core, [core-luascript.md](../docs/design/core-luascript.md),
section 8.4).

### Validation (2026-10-18)

- **macOS arm64:** `native_regression.py` on bytecode-only Lua 5.3.6 (also
  with `LUAC_COMPATIBLE_FORMAT`), 5.4.9 and 5.5.1, with
  `XLUA_EXPECT_BYTECODE_ONLY=ON` and `XLUA_COMPILER_LIB`; `check_exports.py`
  with the expectation; the default libraries with
  `XLUA_EXPECT_BYTECODE_ONLY=OFF`. Core 5.4.9 and 5.5.1 with and without
  `XLUA_TRUSTED_BYTECODE`, and legacy 5.5.1 with `XLUA_LUASCRIPT=ON` and the
  option: `native_regression.py`, `core_smoke.c` and `check_exports.py`.
  `presets_check.py`. The managed suite in all three binding modes on the
  default Lua 5.5.1 library (441 tests, on the tree merged with main); on the bytecode-only library it stops
  in `new LuaEnv()` with the message above. Lua's own suites (5.4.9 and 5.5.1,
  all modes) with `XLUA_BYTECODE_ONLY=ON` in the environment, whose
  interpreter keeps the compiler.
- **Linux arm64** (`xlua-analysis:26.04`): `sanitize.sh` with
  `VERSIONS="bytecode-5.3.6 bytecode-5.4.9 bytecode-5.5.1 core-trusted-5.4.9
  core-trusted-5.5.1 core-5.5.1"`, with `XLUA_PRESET=minimal
  VERSIONS=bytecode-5.5.1`, and with `XLUA_LUASCRIPT=ON XLUA_TRUSTED_BYTECODE=ON
  VERSIONS=5.5.1` (the managed suite included).
- **Android** (NDK r30): every library of [SIZE.md](SIZE.md#bytecode-options)
  built, and `check_android_elf.py` passes with the expectations.
- **Not run:** a Unity Editor or player, iOS, Windows, LuaJIT (refused), and
  Lua's own test suites on a bytecode-only library (they compile text).

LuaEnv's built-in chunks (2026-10-18):

- **macOS arm64:** `native_regression.py` (with the built-in chunk checks) and
  `check_exports.py` on bytecode-only Lua 5.5.1, 5.4.9, 5.3.6 (also with
  `LUAC_COMPATIBLE_FORMAT`) and minimal 5.5.1, built with `XLUA_WERROR=ON`, and
  on the default libraries with `XLUA_EXPECT_BYTECODE_ONLY=OFF`;
  `bytecode_luaenv.py` in its four modes on 5.5.1 and 5.4.9, reflection and
  locked on 5.3.6, reflection and locked on minimal 5.5.1 (modules absent); the
  managed suite in all three binding modes on the default 5.5.1 library (458
  tests); `presets_check.py`; `make_osx_silicon_lua55.sh` (Xcode, the host
  program as a target) with `XLUA_BYTECODE_ONLY=ON`.
- **Linux arm64** (`xlua-analysis:26.04`): `sanitize.sh` with
  `VERSIONS="bytecode-5.5.1 bytecode-5.4.9"`, and with `XLUA_PRESET=minimal
  VERSIONS=bytecode-5.5.1`, `bytecode_luaenv.py` included.
- **Android** (NDK r30, cross build: the host program built at configure time):
  5.5.1 arm64-v8a and armeabi-v7a, legacy and minimal, and 5.3.6 armeabi-v7a
  with `LUAC_COMPATIBLE_FORMAT` built; `check_android_elf.py` with the
  expectations; the legacy libraries equal main's once the build ID is removed;
  5.3.6 armeabi-v7a without `LUAC_COMPATIBLE_FORMAT` refused.
- **iOS** (`make_ios_lua55.sh`, Xcode): device and simulator libraries built,
  with the chunks and without the parser's objects.
- **Not run:** the libraries on an Android device or emulator or in an iOS
  player (no 32-bit runtime loaded the chunks; for 32-bit targets only the
  compile-time size checks of `xlua.c` ran), Windows, UWP, OpenHarmony, and a
  Unity Editor or player.

## Plugin variants

A Unity project can keep more than one build of the native library per
platform and CPU, for example the default library and a development one with
`XLUA_MEMORY_SNAPSHOT=ON`, or one without LuaSocket, and let the build pick
one. Unity ignores Define Constraints on native plugins and fails a build with
two libraries of the same name and CPU
([platform-levels.md](../docs/research/platform-levels.md#selecting-native-plugin-variants-in-unity)),
so `Assets/XLua/Core/Editor/PluginVariants.cs` decides with
`PluginImporter.SetIncludeInBuildDelegate`.

- **Layout.** A variant is the default plugin's path with
  `xlua-variants/<name>/` inserted, normally right after the platform folder.
  Names are lower-case letters, digits and underscores.

  ```text
  Assets/Plugins/Android/libs/arm64-v8a/libxlua.so                             default
  Assets/Plugins/Android/xlua-variants/development/libs/arm64-v8a/libxlua.so   development
  Assets/Plugins/iOS/libxlua.a                                                 default
  Assets/Plugins/iOS/xlua-variants/nosocket/libxlua.a                          nosocket
  ```

- **Choice.** For each group (the default plugin and its variants) the
  build takes the first of: the variant that a Player Settings scripting
  define `XLUA_PLUGIN_<NAME>` names (`XLUA_PLUGIN_NOSOCKET`; at most one such
  define), `core` when `XLUA_CORE_ONLY` is defined, `development` for a
  Development Build, `minimal` when the preset define `XLUA_PRESET_MINIMAL` is
  set, and the default plugin. A minimal project's Development Build therefore
  takes `development` where it exists; `XLUA_PLUGIN_MINIMAL` makes it take
  `minimal` as well. An unknown preset define (`XLUA_PRESET_CORE`) or two of
  them stop the build, and a preset define without any `minimal` plugin gives
  a warning: the default (legacy) plugins, which the minimal C# also runs on,
  are used. A group without the wanted
  variant uses its default plugin, so variants are needed only where they
  differ. The defines are read from Player Settings, not from `csc.rsp`. Two
  `XLUA_PLUGIN_` defines stop the build; a define that no plugin has gives a
  warning.
- **Import settings.** Only the default plugin may be enabled for the
  Editor, which loads it. **XLua/Plugin Variants/Copy Import Settings** gives
  each variant the platforms and CPU of its default plugin and turns the
  Editor off for it; copying the default's `.meta` file with a new `guid`
  does the same. **XLua/Plugin Variants/Show Selection** logs what the next
  build of the active target takes.
- **Building variants.** `XLUA_VARIANT=<name>` makes the Android scripts
  (`make_android_*.sh`) and the Lua 5.x iOS scripts (`make_ios_lua5*.sh`) build in
  their own build directories and copy the library to
  `Plugins/Android/xlua-variants/<name>/libs/<abi>/` or
  `Plugins/iOS/xlua-variants/<name>/`:

  ```sh
  XLUA_VARIANT=development XLUA_MEMORY_SNAPSHOT=ON build/make_android_lua55.sh
  XLUA_VARIANT=nosocket XLUA_LUASOCKET=OFF build/make_ios_lua55.sh
  ```

  The other scripts have no variant support; copy their output by hand.
- **Checks.** `Test/Native/plugin_variants_check.py` tests the selection
  rules with Mono, and `Test/Native/editor_compile_check.py` compiles the
  Editor part against Unity 2021.3 to 6000.6. Android and iOS Simulator
  players from Unity 6000.3.21f1 ran with the selector's choice on 2026-10-18
  (`Test/MobileSmoke/README.md`); the mechanism itself was measured in
  [UNITY_SPIKE.md](UNITY_SPIKE.md#choosing-a-prebuilt-plugin-at-build-time).
- **iOS Simulator.** The variants `simulator` and `<name>_simulator` are the
  simulator builds of the default plugin and of variant `<name>`; a build with
  Target SDK = Simulator SDK prefers them ([iOS Simulator](#ios-simulator)).
- **In the UPM package:** `Tools/make_upm_package.py` includes variants (since
  2026-10-18) with the `.meta` that Copy Import Settings would write: the
  default plugin's, with the Editor off and a GUID derived from the package
  path. A read-only package cannot run that menu, so the settings ship ready.
- **In releases:** since 2026-10-19 `publish.yml` builds Lua 5.5.1's
  `minimal` and `core` variants for Android and iOS (and their simulator
  builds), and the release package must contain them
  (`make_upm_package.py --release`, `RELEASE_PLUGINS`;
  [release.md](../docs/design/release.md)).
- **Not yet:** `minimal` plugins from `native-build.yml`, and variants of the
  desktop plugins (their scripts have no `XLUA_VARIANT`).

## Unity WebGL

WebGL has no prebuilt plugin and no CMake step. Unity compiles
`Assets/Plugins/WebGL/xlua_webgl.cpp` with Emscripten, and that one file
includes the sources of the default runtime: the Lua 5.5.1 core and standard
libraries from `build/lua-5.5.1/src`, plus `xlua_core.c`, `xlua.c`,
`i64lib.c` and `memory_leak_checker.c` from this directory. This is the same list as
`build/CMakeLists.txt`, without LuaSocket: `XLUA_LUASOCKET` is not defined,
so `require "socket"` fails with "module not found".
There is no separate WebGL copy of Lua or of the bridge, so bridge version 110
and every new export come with the other platforms. WebGL supports Lua 5.5.1
only; Lua 5.3 and 5.4 are not available there.

- Unity gives plugin sources no include paths. The wrapper therefore includes
  every file by a path relative to itself and defines `XLUA_AMALGAMATION`, which
  tells the bridge sources that the Lua headers are already included.
  `build/lua-5.5.1/src/luaconf.h` is a plain header with `LUA_IDSIZE` 120 as
  the default; CMake passes `-DLUA_IDSIZE` instead of generating the header
  from a template (Lua 5.3 and 5.4 still use `luaconf.h.in`).
- The sources are C and the wrapper is C++, so they are included inside
  `extern "C"` and keep their C symbol names, which `DllImport("__Internal")`
  needs. `LUA_USE_LONGJMP` keeps Lua errors on `longjmp` as on other platforms.
  C++ exceptions would require "Enable Exceptions" in the player settings.
- Check the result without Unity: compile the wrapper with the Emscripten
  version Unity uses (2.0.19 for 2021.3, 3.1.8 for 2022.3, 3.1.38 for 2023.2
  up to 6000.4, 4.0.19 from 6000.5; all four compile it without warnings),
  run `llvm-nm --defined-only` on the object file and pass the output to
  `python3 Test/Native/check_webgl_exports.py nm.txt`. It checks every function
  that `LuaDLL.cs` imports when `UNITY_WEBGL` is defined. See `LUA55.md` for the
  last validation. `python3 Test/Native/webgl_compile_check.py --output DIR
  [--package]` does both with an installed editor's own Emscripten
  (`PlaybackEngines/WebGLSupport/BuildTools/Emscripten`) and Unity 6000.6's
  flags, for the Assets copy and, with `--package`, the UPM package's.
  `--emscripten` also takes the `install/` folder of an official Emscripten
  release archive (what emsdk installs): `native-build.yml`'s `webgl` job
  downloads 4.0.20, the version of Unity 6000.6's WebGL module, checks its
  SHA-256 and runs `--package` with it (since 2026-10-18).
- A system header that declares C++ templates must not be included inside
  the `extern "C"` block: `xlua_core_limits.c` includes `<emscripten.h>`, so
  the wrapper includes it first. That include came with the core time limits
  on 2026-10-18, and until the wrapper took it out of the block the same day,
  every Unity WebGL build failed with "templates must have C++ linkage".
- WebAssembly64: Unity 6.6 builds Memory64 when the Maximum Memory Size is
  above 4096 MB. Pointers and `size_t` are then 64-bit and `int` stays 32-bit,
  and the linker matches each IL2CPP import with its C definition by
  WebAssembly signature. `python3 Test/Native/check_pinvoke_abi.py` checks
  that every import has the C function's signature; the bridge has no other
  32-bit assumption. The audit is in
  [32-bit targets](../docs/research/32-bit-targets.md#webassembly64-audit).

## Regression coverage

The unit suite includes `testGcPauseRoundTrip` and `testGcStepmulRoundTrip`.
They set GC parameters through C#, read them back through Lua, and check that
C# getters preserve the settings. Follow `Test/README.md` for the Unity scene
runner, or the standalone project list in `General/vs2013/XLuaUnitTest.csproj`.

## iOS device library

Run `build/make_ios_lua54.sh` (or `make_ios_lua53.sh`, `make_ios_lua55.sh`)
with `XLUA_BUILD_ROOT` pointing to an external build directory. The scripts,
`make_ios_luajit.sh` included, default to iOS 15.0; override
`IOS_DEPLOYMENT_TARGET` when your Xcode SDK and consuming Unity player support
another minimum ([deployment targets](#macos-and-ios-deployment-targets)).
Bitcode is disabled. CMake preserves the toolchain's deployment target
instead of forcing iOS 7.0.

Validation: the Lua 5.4.9 ARM64 device archive builds with Xcode 27.1 and the
iOS 27.1 SDK, targeting iOS 15.0. This confirms native compilation, not
Unity/IL2CPP linking or device execution.

## iOS Simulator

A Unity iOS player built with Player Settings > Target SDK = Simulator SDK
links the plugin into a simulator binary, and the device `libxlua.a` (arm64,
platform `IOS`) does not link there. The simulator library is arm64 too, so
`lipo` cannot put both into one file, and Unity has no import setting that
ties a native iOS library to one SDK. `XLUA_IOS_SDK=simulator` makes
`make_ios_lua53.sh`, `make_ios_lua54.sh` and `make_ios_lua55.sh` build for
the arm64 simulator (the toolchain's `SIMULATORARM64`, iOS 15.0) and copy the
library as a [plugin variant](#plugin-variants):

```sh
XLUA_IOS_SDK=simulator build/make_ios_lua55.sh                          # xlua-variants/simulator/
XLUA_IOS_SDK=simulator XLUA_VARIANT=minimal XLUA_PRESET=minimal \
    build/make_ios_lua55.sh                                              # xlua-variants/minimal_simulator/
```

`PluginVariants.cs` takes the simulator form of every name it would choose
for a device build (`minimal_simulator`, `development_simulator`,
`simulator`) when the active target is iOS with the Simulator SDK, then the
device order; a device build never takes a simulator variant. The library
stays out of device players. Since 2026-10-18 (an owner decision)
`native-build.yml` and `publish.yml` build it next to the device library for
Lua 5.3.6, 5.4.9 and 5.5.1 (`XLUA_IOS_SDK=simulator`), so their artifacts and
the release archives have `Plugins/iOS/xlua-variants/simulator/libxlua.a`, and
`Tools/make_upm_package.py` puts it into the package with the device
library's import settings and the Editor off. Without a simulator variant a
simulator build warns and links the device library, which fails as before.
`check_apple_minos.py --platform iossimulator` checks the result; both
workflows run it.

Unity 6000.x builds arm64 simulator players (`SmokeBuild.cs` sets
`PlayerSettings.iOS.simulatorSdkArchitecture` to ARM64; the default is
x86_64). Unity 2021.3 builds x86_64 simulator players only (its trampoline has
`libiPhone-lib-amd64-il2cpp.dylib` and no arm64 simulator library), and the
iOS 27.1 simulator runtime is arm64 only (its `dyld_sim`), so 2021.3 players
cannot run in it. LuaJIT (`make_ios_luajit.sh`) has no simulator build.

Lua 5.5.1, Xcode 27.1, 2026-10-18 (bytes): the device and simulator
archives are 627,592 and 629,384 (legacy), 493,832 and 495,264 (minimal),
426,080 and 427,496 (core).

## Android libraries

Every Android runtime builds with one NDK: **r30 (30.0.16248370), the current
LTS**, using the NDK's own clang and `build/cmake/android.toolchain.cmake`.
Set `ANDROID_NDK` (or `ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT`) to the NDK
directory and `XLUA_BUILD_ROOT` to an external output directory, then run one
of:

- `build/make_android_lua53.sh`, `make_android_lua54.sh`, `make_android_lua55.sh`
- `build/make_android_luajit.sh`

Each script builds armeabi-v7a, arm64-v8a, x86, and x86_64 at API 23 (the
lowest level xlua-ng supports; CMake warns below it) and copies `libxlua.so`
to `plugin_<runtime>/Plugins/Android/libs/<abi>/`. Override `ANDROID_ABIS`
with a space-separated list, `ANDROID_API_LEVEL` for another minimum, or
`XLUA_JOBS` for the parallelism. A failed configure or build stops the script
before it copies that ABI's plugin. The shared logic is in
`build/android_common.sh`.

### Stripped libraries and symbols

Since 2026-10-18 (an owner decision) the scripts put a stripped `libxlua.so`
into the plugin folder and keep the linked one for crash symbolication:

```text
plugin_lua55/Plugins/Android/libs/<abi>/libxlua.so     llvm-strip --strip-unneeded (what ships)
plugin_lua55/symbols/Android/libs/<abi>/libxlua.so     the linked library, with .symtab and DWARF
```

Variants keep their path below both folders
(`symbols/Android/xlua-variants/<name>/libs/<abi>/`). The NDK's
`llvm-strip --strip-unneeded` removes the static symbol table and the debug
sections and keeps `.dynsym`, so every export stays
(`Test/Native/check_android_elf.py` passes on the stripped files); the script
stops when the stripped file's GNU build ID differs from the linked one's,
because `ndk-stack`, the Play Console and Unity's symbol upload match symbols
by that ID. `XLUA_ANDROID_STRIP=OFF` copies the linked library into the plugin
folder as before and writes no `symbols/`. `symbols/` is in `.gitignore` (as
every built plugin), is not part of the UPM package, and CI uploads it on its
own: `native-build.yml` as `xlua-lua-<version>-android-symbols`, `publish.yml`
as the `android-symbols-<runtime>` artifacts and the release asset
`android-symbols_<tag>.tgz`.

Why: Unity 2021.3 copies Android plugins into the APK as they are, so a
2021.3 release APK carried the linked library (1,985,360 bytes, 640,441
compressed), where Unity 6000.x strips it while packing (372,560 and 159,185;
`Test/MobileSmoke/README.md`). Unity 6000.x strips the stripped file again
without a change. Sizes, NDK r30, API 23, 2026-10-18 (all ABIs in
[SIZE.md](SIZE.md#stripped-android-libraries)), bytes:

| runtime | ABI | linked | stripped | linked deflate | stripped deflate |
| --- | --- | ---: | ---: | ---: | ---: |
| Lua 5.5.1 | arm64-v8a | 2,066,128 | 384,040 | 655,626 | 163,519 |
| Lua 5.5.1 | armeabi-v7a | 1,437,032 | 257,396 | 585,475 | 143,651 |
| Lua 5.4.9 | arm64-v8a | 1,975,456 | 367,504 | 625,724 | 154,942 |
| Lua 5.3.6 | arm64-v8a | 1,638,552 | 314,792 | 520,862 | 133,126 |
| LuaJIT | arm64-v8a | 1,111,368 | 674,416 | 452,880 | 332,811 |

`llvm-strip --strip-all` would save another 600 bytes on 5.5.1 arm64-v8a
(383,440).

A release ARM64 APK from Unity 2021.3.45f2 (`size_report.py run --platforms
android --variants ng-5.5 --configs default`, Minimal stripping, 2026-10-18)
is 9,642,938 bytes with the linked library (`XLUA_ANDROID_STRIP=OFF`;
`libxlua.so` 2,066,128, 663,305 compressed) and 9,143,534 with the stripped
one (384,040, 163,900 compressed): 499,404 bytes less. A Unity 6000.3.21f1
APK stores the stripped file as it is (384,040, 163,900 compressed in the
hotfix IL2CPP player of `hotfix_il2cpp.py`).

### Android size flags

Android builds use the size flags measured in [FLAGS.md](FLAGS.md)
(`XLUA_SIZE_FLAGS`, on by default for Android and OpenHarmony):

- `-O3` for the 18 Lua files the device profile ranks hot (the VM, calls,
  tables, GC, strings, the C API, closures, numbers, string patterns and
  formatting, `luaL_Buffer`, allocation, metamethods, `table.sort`, the
  compiler and the bytecode loader) and for `xlua.c` and `xlua_core.c`
  (which holds the C# function trampolines that were part of `xlua.c` when
  this was measured); `-Oz` without the machine outliner for everything else.
- `-Wl,--icf=all` (`XLUA_ICF`): 1.3 to 2.3 KB stripped, 0.3 to 0.5 KB in the
  APK. Folded functions share an address. lld folds pairs of C API functions
  that compile to the same code (`lua_pushint64`/`lua_pushuint64`,
  `lua_isint64`/`lua_isuint64`, `lua_toint64`/`lua_touint64`, some
  `xlua_pack_*`/`xlua_unpack_*`), the signed and unsigned struct field
  setters (closures with an upvalue, so Lua still sees distinct functions),
  and functions that only return 0: LuaSocket's `auxiliar_open`,
  `buffer_open` and `do_nothing` and xLua's `nop` (on LuaJIT also the
  `lua_isinteger` stand-in). Of those, only `nop` (the third result of
  `xlua.genaccessor`) and `do_nothing` (an upvalue of the function
  `socket.newtry()` returns, reachable through `debug.getupvalue`) are Lua
  values, and they now compare equal. The list is from `--print-icf-sections`
  for Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT.
- `-Wl,--pack-dyn-relocs=android` (Android's packed relocations) at API 23 to
  27. From API 28 the NDK already packs relative relocations as RELR, which
  is smaller. API 23 also gives a GNU-only hash table (an NDK default).

Lua 5.5.1, stripped as Unity packs it, against the `-O3` build at API 21
that the scripts produced before:

| ABI | build | stripped | APK (deflate) |
| --- | --- | ---: | ---: |
| arm64-v8a | `-O3`, API 21 | 420,800 | 170,264 |
| arm64-v8a | size flags, API 23 (default) | 372,560 (-48,240) | 159,184 (-11,080) |
| arm64-v8a | the same with `XLUA_EXPORTS=managed` | 349,840 (-70,960) | 151,805 (-18,459) |
| armeabi-v7a | `-O3`, API 21 | 274,944 | 149,510 |
| armeabi-v7a | size flags, API 23 (default) | 250,012 (-24,932) | 139,689 (-9,821) |
| armeabi-v7a | the same with `XLUA_EXPORTS=managed` | 233,612 (-41,332) | 133,247 (-16,263) |

The speed is the same as `-O3` within the layout noise on the test phone
([FLAGS.md](FLAGS.md#applied-to-the-release-build)).

`XLUA_EXPORTS=managed` is the export list of the planned `minimal` and `core`
presets ([presets.md](../docs/design/presets.md)). It stays off by default
because the `legacy` preset exports the whole Lua API for native plugins that
extend xLua: `native_regression.py check_exported_helpers` calls such exports,
and `General/LuaMemoryLeakChecker` imports functions that the C# does
not. With `XLUA_EXPORTS=managed`, `python3 Test/Native/check_android_elf.py
--managed-exports` checks that a library exports nothing else, and
`python3 Test/Native/managed_imports.py --write` regenerates
`build/xlua_exports.txt` after the imports change (`check_exports.py` fails
until it is regenerated).

OpenHarmony uses the same options (its SDK has the same clang and lld), but
no packed relocations; its size and speed were not measured. iOS, macOS,
Windows and Linux keep their previous flags. The split was measured there on
2026-10-18 (iOS -29.5 KB of linked code, macOS -33 KB per slice, Linux -36
to -49 KB of `.text` with GCC; no geomean speed cost on macOS arm64) and
waits for the owner's decision; the Xcode generator of the Apple scripts
cannot build it yet
([FLAGS.md](FLAGS.md#the-release-split-on-ios-and-the-desktops)).

LuaJIT follows the Android instructions in `luajit-2.1/doc/install.html`: the
NDK clang builds `libluajit.a` (`BUILDMODE=static`) from a per-ABI copy of the
source tree, and CMake links it with `-DUSING_LUAJIT=ON`. The 32-bit ABIs need
a 32-bit host compiler (`gcc-multilib` on Debian/Ubuntu); the NDK is Linux or
macOS only. The removed `make_android_lua53_arm64.sh` and
`make_android_luajit_arm64.sh` are covered by the scripts above.

Check the result with `python3 Test/Native/check_android_elf.py
<libxlua.so files>`: it uses the NDK's `llvm-readelf` to verify the machine
for the ABI, that every ELF LOAD segment is aligned to at least 16 KB, and that
the dynamic symbol table defines every function the C# imports.

Validation (2026-10-15, NDK r30 in a linux/amd64 container): Lua 5.3.6, 5.4.9,
5.5.1 and LuaJIT build for all four ABIs, and every library passes the check
above (108 managed imports, 16 KB alignment). The container run did not
execute the libraries; Android IL2CPP player runs are recorded in
`Test/MobileSmoke/README.md`. The iOS device archive also links into
a native ARM64 smoke executable; that executable has not run on a device.

## Windows libraries

| Script | Output (`Plugins/...`) |
| --- | --- |
| `make_win_lua54.bat`, `make_win_lua55.bat` | `x86_64/xlua.dll` and `x86/xlua.dll` |
| `make_win64_lua53.bat`, `make_win32_lua53.bat` | `x86_64/xlua.dll`, `x86/xlua.dll` |
| `make_win_arm64_lua53.bat`, `make_win_arm64_lua54.bat`, `make_win_arm64_lua55.bat` | `arm64/xlua.dll` |
| `make_win64_luajit.bat`, `make_win64_luajit_gc64.bat`, `make_win32_luajit.bat` | `x86_64/xlua.dll`, `x86/xlua.dll` |
| `make_win_arm64_luajit.bat` | `arm64/xlua.dll` |

Windows ARM64 players exist from Unity 2023.1 ("ARM 64-bit" in the Windows
build settings). The ARM64 scripts use the CMake Visual Studio generator with
`-A ARM64`, so they build on Windows on Arm and also cross-compile on an x64
machine; Visual Studio needs the "MSVC ARM64 build tools" component either
way. `make_win_arm64_luajit.bat` builds LuaJIT with `msvcbuild_mt.bat` in an
ARM64 developer prompt: it uses the prompt it is started from, or opens
`vcvarsall.bat arm64` (on ARM64) or `x64_arm64` (on x64) itself. Unlike the
older scripts, the ARM64 ones stop at the first failed step and skip `pause`
when `CI` is set.

`Assets/Plugins/arm64/xlua.dll.meta` enables the DLL for Standalone Win64 with
CPU ARM64 and for the Windows Editor on ARM64. `Plugins/arm64` is the folder
Unity's import heuristic maps to ARM64, and it already holds the macOS
`libxlua.dylib`. `Plugins/x86_64/xlua.dll.meta` sets CPU x86_64 for Win64
(it was AnyCPU, which also matches an ARM64 player).

CI: `native-build.yml` builds Lua 5.3.6, 5.4.9 and 5.5.1 (and LuaJIT with the
5.5.1 leg) on the `windows-11-arm` runner and runs `check_exports.py`,
`profiler_smoke.py` and `native_regression.py` against them when the runner
and its Python are ARM64. `build.yml` and `publish.yml` run the four
`make_win_arm64_*.bat` scripts there. Not verified yet: no workflow has run
with these jobs, and no Unity player or Editor has loaded the ARM64 DLL.

### LuaJIT GC64 on x64

LuaJIT 2.1 builds x64 in GC64 mode unless `LUAJIT_DISABLE_GC64` is defined
(`luajit-2.1/src/lj_arch.h:216`); ARM64 is always GC64 (`lj_arch.h:303`).
`msvcbuild_mt.bat nogc64` defines it, so the two x64 scripts build different
LuaJIT libraries:

- `make_win64_luajit.bat` (used by CI): non-GC64. GC references are 32-bit,
  so the Lua heap must fit in the low 2 GB (`lj_alloc.c:105`), as in upstream
  xLua's Windows x64 plugin.
- `make_win64_luajit_gc64.bat`: GC64, like every other 64-bit LuaJIT plugin
  (Linux, macOS, iOS, Android arm64-v8a and x86_64, Windows ARM64). It still
  uses the Visual Studio 2017 generator.

The bridge itself does not depend on the mode. Until 2026-10-28 it did:
`xlua_gl` read `lua_State` through LuaJIT's `lj_obj.h`, which `xlua.c` was
compiled with in GC64 mode whatever the library's mode, so with a non-GC64
library it got a different value from call to call and the first `LuaEnv`
failed (`NullReferenceException` in `ImportType`; windows-host.yml run
40642149114). It now takes the registry's address (`lua_topointer`), about
2.5 ns more per call on LuaJIT (Apple M2 Pro). `xlua_libs.c` still includes
`lj_arch.h`, for `LJ_HASFFI` only. The CMake `GC64` option, which
defined `LUAJIT_ENABLE_GC64` for `xlua.c`, was removed: nothing in LuaJIT 2.1
or the bridge reads that macro. Bytecode depends on the mode: a GC64 runtime
rejects non-GC64 bytecode and the reverse (`lj_bcread.c:403`, the
`BCDUMP_F_FR2` flag). 32-bit LuaJIT and `make_win64_luajit.bat` share the
non-GC64 format; all other 64-bit builds use GC64. `luajit -b -W` writes
non-GC64 and `-b -X` GC64 bytecode from any LuaJIT.

## Player size (2026-10-16)

Measured with `Test/MobileSmoke/size_report.py` (see "Player size" in
`Test/README.md`) at commit d905ff1, against upstream Tencent/xLua 59bf426
with its prebuilt plugins (Lua 5.3.5, bridge 105). Every variant is the same
empty project and scene; the xLua ones create a `LuaEnv`, run a chunk, call a
C# static method from Lua and a Lua function from C#, without generated code
unless marked `+gen` (XLua/Generate Code for the probe class, `Vector3`,
`GameObject`, `Transform`, `Debug` and two delegates). The plugins were built
from this checkout: Android with NDK r30 (30.0.16248370), iOS with Xcode 27.1
beta 1 (27A9269). Players are release IL2CPP builds with Unity's defaults
(managed stripping Minimal, engine code stripping on). Android and iOS used
Unity 6000.3.21f1, WebGL 6000.6.4f1: the installed 6000.3.26f1 and 6000.6.6f1
have no Android, iOS or WebGL module. Android is an ARM64 APK at minimum API
25. iOS is the exported Xcode project built unsigned for `generic/platform=iOS`
with archive-style stripping; the zipped `.app` stands in for the IPA. The
players were built, not run.

What xLua adds, in bytes over the baseline:

| | upstream | xlua-ng 5.5.1 | 5.4.9 | 5.3.6 | LuaJIT | upstream +gen | 5.5.1 +gen |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Android APK, ARM64, Minimal | 1,361,602 | 1,397,134 | 1,387,970 | 1,365,570 | 1,566,270 | 1,558,794 | 1,598,514 |
| Android APK, ARM64, Medium | 1,171,154 | 1,201,674 | 1,192,510 | 1,170,110 | 1,370,810 | | |
| Android APK, ARMv7 + ARM64 | 2,499,878 | 2,552,854 | | | | | |
| Android AAB, ARM64 | 1,362,274 | 1,397,790 | | | | | |
| iOS `.app` zipped | 699,494 | 737,445 | 734,422 | 713,562 | | 870,050 | 919,630 |
| iOS UnityFramework | 1,661,128 | 1,763,168 | 1,756,184 | 1,704,360 | | 2,171,968 | 2,290,424 |
| WebGL, Brotli | 316,246 | 335,189 | | | | | |
| WebGL, uncompressed | 1,316,746 | 1,413,167 | | | | | |

The baseline APK is 12,279,056 bytes, the zipped `.app` 6,628,025 and the
Brotli WebGL build 3,161,385 (13,531,094 uncompressed). Where the Android
ARM64 APK delta goes (compressed in the APK / uncompressed):

| | upstream | xlua-ng 5.5.1 | 5.5.1 +gen |
| --- | --- | --- | --- |
| `lib/arm64-v8a/libxlua.so` | 150,152 / 413,416 | 169,426 / 418,272 | 169,426 / 418,272 |
| `libil2cpp.so` | +544,237 / +1,870,928 | +559,500 / +1,912,024 | +704,856 / +2,465,776 |
| `libunity.so` | +544,070 / +1,101,520 | +544,070 / +1,101,520 | +579,966 / +1,179,984 |
| `global-metadata.dat` | +115,259 / +326,540 | +116,360 / +329,240 | +136,027 / +404,840 |
| other assets (`Resources/*.lua.txt`) | +7,067 / +19,368 | +6,965 / +19,524 | +7,425 / +21,060 |

On iOS the linker map gives `libxlua.a` 227,689 bytes in UnityFramework for
upstream, 288,515 for 5.5.1, 274,921 for 5.4.9 and 227,158 for 5.3.6;
`libGameAssembly.a` (the IL2CPP code) grows by 1,364,790 (upstream) and
1,408,823 (xlua-ng). The WebGL `.wasm` grows by 219,142 (upstream) and 238,184
(xlua-ng) bytes Brotli, the `.data` file by about 97,000 in both.

Native plugins as published and as Unity packs them (Unity strips the Android
library, so the stripped size is the shipped one; iOS archives are arm64
slices, `strip -S`):

| plugin | bytes | stripped | stripped, gzip -9 |
| --- | --- | --- | --- |
| upstream 5.3.5 arm64-v8a | 993,608 | 413,416 | 149,733 |
| upstream 5.3.5 armeabi-v7a | 546,108 | 387,384 | 144,777 |
| 5.5.1 arm64-v8a | 2,158,192 | 418,272 | 169,029 |
| 5.5.1 armeabi-v7a | 1,478,156 | 273,144 | 148,483 |
| 5.4.9 arm64-v8a | 2,048,400 | 399,624 | 159,786 |
| 5.3.6 arm64-v8a | 1,710,952 | 344,072 | 137,490 |
| LuaJIT arm64-v8a | 1,163,688 | 706,712 | 338,011 |
| upstream 5.3.5 iOS (with bitcode) | 1,357,856 | 478,872 | 163,924 |
| 5.5.1 iOS | 588,744 | 568,488 | 197,935 |
| 5.4.9 iOS | 563,528 | 544,064 | 188,019 |
| 5.3.6 iOS | 495,440 | 476,016 | 162,106 |

What drives the numbers:

- The native library is the smaller part. Of the 1.36 MB upstream adds to the
  APK, `libxlua.so` is 150 KB. The rest is IL2CPP code for xLua's C#
  (`libil2cpp.so`, 544 KB compressed), its metadata (115 KB) and 544 KB of
  engine code: on Android, `StaticLuaCallbacks.LoadFromStreamingAssetsPath`
  uses `UnityEngine.WWW`, so engine code stripping keeps the UnityWebRequest
  modules in `libunity.so`. iOS has no such reference (`libiPhone-lib.a`
  +9.7 KB), which is why the iOS delta is about half the Android one. A project
  that already uses UnityWebRequest pays the engine part anyway. The
  `XLUA_NO_STREAMING_ASSETS_LOADER` define removes that reference; see below.
- xlua-ng over upstream: +35,532 bytes in the ARM64 APK, +37,951 in the zipped
  `.app`, +18,943 in the Brotli WebGL build. On Android 19 KB of that is
  `libxlua.so` and 15 KB is IL2CPP code. The stripped Assembly-CSharp grows
  from 139,264 to 145,408 bytes (`StackToString`, the UTF-8 decoding in
  `DecodeLuaString`, the `xlua_gc` wrapper and the reworked `SignatureLoader`);
  Minimal stripping keeps every method of Assembly-CSharp. The editor-only
  code (generator, LuaCATS, Validate Setup) is under `Editor` folders and is
  not in the player assembly.
- Lua version: the arm64 library is 344 KB (5.3.6), 400 KB (5.4.9) and 418 KB
  (5.5.1) stripped. From the lld map, the Lua core (VM, parser, GC) is 126 KB
  in 5.3.6 and 184 KB in 5.5.1, the standard libraries 77 KB and 88 KB;
  LuaSocket is 48 KB and the bridge (`xlua.c`, `i64lib.c`,
  `memory_leak_checker.c`) 23 KB in every version. Upstream's 5.3.5 library
  (built with GCC 4.9 and clang 5) is 69 KB larger than our 5.3.6 and about
  the size of our 5.5.1; on armeabi-v7a our 5.5.1 is 114 KB smaller than
  upstream's 5.3.5 (Thumb-2), though both compress to about 145 KB.
- LuaJIT adds 288 KB stripped over 5.5.1 (707 KB): the interpreter is
  assembly from `buildvm` plus the JIT compiler.
- Debug info: our Android libraries keep DWARF (the NDK adds `-g`), which is
  1.7 MB of the 2.16 MB 5.5.1 file. Unity strips it when it packs the APK, so
  it costs nothing in the player, only in the artifact.
- `-O3`: CMake's Release flags for the NDK and Xcode builds are `-O3`.
  `-ffunction-sections`, `-fdata-sections` and `--gc-sections` are already on
  (NDK defaults); adding them changes nothing.
- Generated code for the six configured types adds about 200 KB to the APK in
  both upstream and xlua-ng (IL2CPP code for the wrappers, their metadata and
  36 KB of engine code that `Transform` and `GameObject` reach).
- Medium stripping removes about 190 KB of xLua's IL2CPP cost; the engine part
  stays.

`XLUA_NO_STREAMING_ASSETS_LOADER` (2026-10-17) leaves out the StreamingAssets
searcher and its `WWW` code; see [Loading Lua sources](../Assets/XLua/Doc/Lua_Loaders.md#xlua_no_streaming_assets_loader).
Measured with `size_report.py run --platforms android --configs default` and the
`+nosa` and `+uwr` variants, Unity 6000.3.21f1, ARM64 APK, Minimal stripping,
Lua 5.5.1 (bytes; components compressed in the APK / uncompressed):

| variant | APK | APK delta | libunity.so delta | libil2cpp.so delta | global-metadata delta |
| --- | --- | --- | --- | --- | --- |
| baseline | 12,279,056 | | | | |
| ng-5.5 | 13,676,194 | +1,397,138 | +544,069 / +1,101,520 | +559,500 / +1,912,024 | +116,360 / +329,240 |
| ng-5.5+nosa | 13,106,230 | +827,174 | +4,445 / +10,720 | +536,595 / +1,828,296 | +108,927 / +292,812 |
| baseline+uwr | 12,938,608 | | | | |
| ng-5.5+uwr | 13,676,338 | +737,730 | +7,362 / +16,896 | +457,110 / +1,566,872 | +96,064 / +270,048 |
| ng-5.5+nosa+uwr | 13,661,222 | +722,614 | +3,773 / +10,688 | +448,055 / +1,525,528 | +93,588 / +255,876 |

The `+uwr` rows are deltas against `baseline+uwr`, whose probe calls
`UnityWebRequest.Get` once. The define saves 569,964 bytes in a project that
does not use UnityWebRequest and 15,116 bytes in one that does (IL2CPP code
9,055, metadata 2,476, `libunity.so` 3,589). Using UnityWebRequest at all costs
the baseline 659,552 bytes.

Size reductions measured in scratch builds (none of them is applied to the
release build scripts or the managed sources, except the first, which is the
opt-in define above):

| change | measured saving | cost |
| --- | --- | --- |
| Drop the Android `WWW` path of `LoadFromStreamingAssetsPath` (now `XLUA_NO_STREAMING_ASSETS_LOADER`) | 569,044 bytes APK (libunity.so -539,717 compressed / -1,089,920 uncompressed, libil2cpp.so -22,081, metadata -7,244) | `require` of `.lua` files from StreamingAssets on Android stops working; upstream already marks that loader obsolete. Nothing if the project uses UnityWebRequest |
| Build plugins with `-Os` (`CMAKE_BUILD_TYPE=MinSizeRel`) | Android arm64: 66,680 bytes in `libxlua.so`, 25,324 bytes APK; iOS: archive `__TEXT` 294,891 to 221,772 | slower Lua VM; not benchmarked |
| `-O2` instead of `-O3` | Android arm64: 21,552 bytes stripped, 6.8 KB gzip | not benchmarked |
| Build without LuaSocket (now `-DXLUA_LUASOCKET=OFF`, [above](#luasocket-and-mime)) | about 48 KB uncompressed native code per ABI | removes `socket.core` |
| `--icf=all` | 1,168 bytes | none |
| Leave `perf/` and `tdr/` out of `Resources` (done 2026-10-17: `tdr` and `perf.memory` removed, the profiler embedded behind `XLUA_NO_PROFILER`) | 8,705 bytes uncompressed, about 3 KB compressed (estimated, not built) | profiler and TDR helpers |

Not verified: the players were not launched, so the probe's run-time result
was not checked; no AAB was split into device APKs; Unity 6000.3.26f1 and
6000.6.6f1 were not measured.

[Size breakdown](SIZE.md) splits these numbers by source file, C# class,
class library, metadata section, engine library and resource file, and lists
further measured reductions (leaving `SignatureLoader` out of the player saves
239,384 bytes at Minimal stripping).
[Compiler and linker options](FLAGS.md) measures `-O2`, `-Os`, `-Oz`, per-file
optimisation levels, LTO, the machine outliner and linker options for size and
for speed on an Android phone.

A project setting outside xLua: Player Settings > IL2CPP Code Generation
**Faster (smaller) builds** (full generic sharing) cut what xLua adds to the
Android ARM64 APK by 116,412 bytes (1,397,146 to 1,280,734) and the empty
project by 375,832 bytes, almost all in class library generics; the run time
cost was not measured ([UNITY_SPIKE.md](UNITY_SPIKE.md#q2-il2cpp-generic-sharing),
[FAQ](../Assets/XLua/Doc/Faq_EN.md#how-do-i-make-an-il2cpp-player-smaller)).

## Lua 5.3.6 upgrade validation

The Lua 5.3 vendor tree comes from the official 5.3.6 archive, SHA256
`fc5fd69bb8736323f026672b1b7235da613d7177e72558893a0bdcd320466d60`.
The commit sequence restores unmodified 5.3.5, upgrades to unmodified 5.3.6,
then reapplies xLua integration separately.

Retained patches cover UWP restrictions, Android decimal parsing,
`os.execute` removal, configurable `LUA_IDSIZE`, the existing `log2`
fallback, and optional `LUAC_COMPATIBLE_FORMAT` bytecode. The upstream
5.3.6 `io.popen` mode validation and bytecode loader GC fixes are preserved.

Validation on 2026-10-07:

- macOS ARM64 native plugin and both unit-suite modes: 275 tests per mode.
- Unity 6000.3.21f1 Editor: Lua 5.3 loading, managed GC settings,
  coroutine, full GC, and disposal smoke checks passed.
- iOS ARM64 native archive with Xcode 27.1, deployment target 15.0.
- Android NDK 27.1, API 21 (since rebuilt with r30): armeabi-v7a, arm64-v8a, x86, and x86_64;
  bridge exports and at least 16 KB ELF LOAD alignment verified.
- Standard and custom-format `lua`/`luac`: source and bytecode execution of
  `Test/Native/lua53_regression.lua`, including GC during incremental
  loading of long strings and nested functions. Cross-format loads reject
  incompatible bytecode as expected.

For the Lua/luac tools, configure `build/luac` in separate external output
folders. `-DLUA_VERSION` selects 5.3.6, 5.4.9 or 5.5.1 (default 5.5.1);
`-DLUAC_COMPATIBLE_FORMAT=ON` is accepted only with 5.3.6.
Run each resulting `lua` executable on the regression file, then use its
matching `luac -o` to compile the file and execute the generated bytecode.
These host tests do not establish compatibility between 32-bit and 64-bit
runtimes; that remains a separate device/runtime check.

On 2026-10-08, Lua 5.3.6 also passed the Unity 6000.3.21f1 ARM64 IL2CPP smoke checks on
iPhone 14 Pro: GC setters/getters, coroutine, full GC, and disposal.
Android and UWP player execution remain unverified.

See [the vendor patch audit](LUA_PATCHES.md) for the historical evidence,
retained behavior, and outstanding platform checks.

See [Lua 5.5 integration](LUA55.md) for runtime changes and validation.
