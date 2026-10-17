# Ecosystem

Tools and libraries that work well with xLua-NG, grouped by purpose. xLua-NG
embeds standard Lua (5.5, 5.4, 5.3 or LuaJIT 2.1), so most of the Lua
ecosystem applies. This page lists the ones that matter for a Unity project
and says what to watch for.

Links, licences and last-push dates were checked on 2026-10-17 with the GitHub
API. A project is listed with its last push only when it has been quiet for
more than a year. None of these projects is bundled with xLua-NG or endorsed
by it. The integration notes come from this repository's
[research notes](research/README.md) and guides and from each project's own
documentation, not from a test of every combination. The preset names
(`legacy`, `minimal`, `core`) refer to the [preset design](design/presets.md),
which is not implemented yet. The last section lists the projects that the
research reviewed, which is a different list from the tools recommended here.

## Editors, type checking and debugging

### lua-language-server (LuaLS)

- **Link:** <https://github.com/LuaLS/lua-language-server>
- **Licence:** MIT
- **What it is:** the language server behind the VS Code Lua extension and
  most other editors' Lua support. It understands LuaCATS annotations.
- **Fit:** xLua-NG generates LuaCATS definitions for the C# API your scripts
  call, so LuaLS completes `CS.*` names, shows signatures and reports wrong
  argument types. Run **XLua > Generate LuaCATS Definitions**.
- **Integration:** the generator writes a `.luarc.json` that points LuaLS at
  the generated `LuaTypes` folder. `Lua 5.5` as `runtime.version` needs LuaLS
  3.16 or later. See [LuaCATS definitions](../Assets/XLua/Doc/LuaCATS.md).

### EmmyLua

- **Links:** [emmylua-analyzer-rust](https://github.com/EmmyLuaLs/emmylua-analyzer-rust)
  (language server), [IntelliJ-EmmyLua](https://github.com/EmmyLua/IntelliJ-EmmyLua)
  (JetBrains plugin), [VSCode-EmmyLua](https://github.com/EmmyLua/VSCode-EmmyLua)
  (VS Code extension)
- **Licence:** MIT (analyzer), Apache-2.0 (IntelliJ plugin). The VS Code
  extension repository has no licence file.
- **What it is:** an alternative Lua language server plus debugger. The Rust
  analyzer supports Lua 5.1 to 5.5 and LuaJIT, reads both EmmyLua and LuaCATS
  annotations, and also reads a `.luarc.json`.
- **Fit:** it should read the generated LuaCATS files as well; the
  generated `.luarc.json` is one of the config files it searches for. This
  combination has not been tested here.
- **Integration:** EmmyLua's debugger needs the full `debug` library, and its
  attach mode needs the process to export the Lua API, which `legacy`
  does. The planned `core` preset has neither. See the debugger note in
  [bridge-audit.md](research/bridge-audit.md) and the exported-symbols row in
  [presets.md](design/presets.md).

### LuaPanda

- **Link:** <https://github.com/Tencent/LuaPanda>
- **Licence:** BSD-3-Clause for the source; the repository's `LICENSE.txt`
  also lists third-party components under other licences.
- **What it is:** a Lua debugger and code tool for VS Code, from Tencent. Its
  README says it supports Lua 5.1 to 5.4 and names xLua, SLua and
  slua-unreal as host frameworks.
- **Fit:** a debugger for the same teams that already use xLua.
- **Integration:** the debugger depends on LuaSocket. xLua-NG compiles
  LuaSocket in as `socket` on every platform except Switch and WebGL (see
  [Security](../Assets/XLua/Doc/Security.md)), but the planned `minimal` and
  `core` presets drop it outside development builds.
- **Activity:** last pushed 2024-03-11. It supports Lua up to 5.4, not 5.5.

### ZeroBrane Studio and MobDebug

- **Links:** [ZeroBraneStudio](https://github.com/pkulchenko/ZeroBraneStudio),
  [MobDebug](https://github.com/pkulchenko/MobDebug)
- **Licence:** MIT (both)
- **What they are:** a Lua IDE with a remote debugger (ZeroBrane Studio) and
  the remote-debugging module it uses (MobDebug). MobDebug supports Lua 5.1 to
  5.4 and LuaJIT 2.x. ZeroBrane Studio bundles it.
- **Fit:** the upstream xLua package shipped LuaSocket specifically so that
  ZeroBrane Studio could attach to a running game
  ([Native Lua extensions](../Assets/XLua/Doc/Add_Remove_Lua_Lib.md)).
- **Integration:** `require('mobdebug').start()` in the script needs
  `socket` (LuaSocket) and the `debug` library. Both are present in `legacy`.
  Set the loader's `filepath` to a real path so breakpoints map to files; see
  [the FAQ](../Assets/XLua/Doc/Faq_EN.md#how-should-customloader-set-filepath-for-debugging).
- **Activity:** MobDebug was last pushed 2023-10-17. It is a small, stable
  module, but it does not claim Lua 5.5.

## Linting and formatting

xLua scripts use two globals that no linter knows: `CS` and `xlua`. Declare
them in each tool's configuration, or every script reports undefined globals.

### luacheck

- **Link:** <https://github.com/lunarmodules/luacheck>
- **Licence:** MIT
- **What it is:** a static analyzer and linter for Lua. It finds undefined and
  unused variables, unreachable code and similar mistakes.
- **Fit:** the usual CI gate for Lua code. Its README lists support for Lua 5.1
  to 5.4 syntax and LuaJIT, not 5.5.
- **Integration:** add `CS` and `xlua` to `globals` in `.luacheckrc`.

### selene

- **Link:** <https://github.com/Kampfkarren/selene>
- **Licence:** MPL-2.0
- **What it is:** a linter written in Rust.
- **Fit:** a faster alternative to luacheck with no Lua runtime to install.
  Built-in standard libraries cover Lua 5.1 to 5.4 and Roblox, according to
  its [documentation](https://kampfkarren.github.io/selene/usage/std.html).
- **Integration:** write a small YAML standard-library file that extends
  `lua51`, `lua52`, `lua53` or `lua54` and declares `CS` and `xlua` as
  `any: true` globals. Set `lua_versions` for the syntax your scripts use.

### StyLua

- **Link:** <https://github.com/JohnnyMorganz/StyLua>
- **Licence:** MPL-2.0
- **What it is:** a deterministic Lua formatter.
- **Fit:** keeps script formatting out of code review. It handles Lua 5.1 to
  5.4, LuaJIT and Luau syntax; Lua 5.5 is not listed.
- **Integration:** the release binaries enable all syntax variants. If you
  build from source with Cargo, the default is Lua 5.1 only, so pass the
  features you need and select the syntax in `.stylua.toml`.

## Languages that compile to Lua

### Teal

- **Link:** <https://github.com/teal-language/tl>
- **Licence:** MIT
- **What it is:** a typed dialect of Lua. The compiler `tl` is a single Lua
  file.
- **Fit:** compile `.tl` files to `.lua` ahead of time and load the output
  through your normal loader. Nothing in xLua-NG needs to know about Teal.
- **Integration:** xLua-NG does not generate Teal declarations for `CS.*`.
  Declare the parts you use as records, or type them as `any`. To map errors
  back to `.tl` files, have your loader set a real `filepath` as described in
  [the FAQ](../Assets/XLua/Doc/Faq_EN.md#how-should-customloader-set-filepath-for-debugging).

### TypeScriptToLua

- **Link:** <https://github.com/TypeScriptToLua/TypeScriptToLua>
- **Licence:** MIT
- **What it is:** a TypeScript to Lua transpiler. Its `luaTarget` option
  takes `JIT`, `5.1` to `5.4` and `universal` (the default), according to its
  [configuration page](https://typescripttolua.github.io/docs/configuration).
- **Fit:** write scripts in TypeScript, ship the generated Lua. Match
  `luaTarget` to the Lua build you ship; its documented targets stop at 5.4.
- **Integration:** with `sourceMapTraceback` it overrides `debug.traceback` so
  stack traces point at the TypeScript. xLua-NG's error handler looks up the
  global `debug.traceback` ([core-luascript.md](design/core-luascript.md)),
  so the override reaches xLua-NG's error messages; a build without the `debug`
  library cannot use it. There are no `.d.ts` declarations for `CS.*`; you
  need to write them.

## Testing

### luaunit

- **Link:** <https://github.com/bluebird75/luaunit>
- **Licence:** BSD (see its `LICENSE.txt`)
- **What it is:** an xUnit-style unit-test framework for Lua. It is a single
  file with no dependencies.
- **Fit:** xLua-NG's own Lua test suite runs on luaunit 3.5, as an unmodified
  upstream file. See [Test/README.md](../Test/README.md).
- **Integration:** because it is one file, it loads through any `LuaEnv`
  loader. The suite wraps it in `luaunit_compat.lua` to keep the older
  assertion names; copy that approach if you have existing tests.

### busted

- **Link:** <https://github.com/lunarmodules/busted>
- **Licence:** MIT
- **What it is:** a behaviour-style Lua test framework with a command-line
  runner. It supports Lua 5.1 and later and LuaJIT 2.0 and later.
- **Fit:** it runs under a standalone Lua interpreter, so it suits pure-Lua
  logic that never touches `CS`. Tests that call C# have to run inside a
  `LuaEnv`, which is how the xLua-NG suite runs luaunit.

### LuaCov

- **Link:** <https://github.com/lunarmodules/luacov>
- **Licence:** MIT
- **What it is:** a coverage analyzer in pure Lua with no dependencies.
- **Fit:** measures which lines of your scripts the tests run, under the same
  standalone interpreter as busted.
- **Integration:** the script has to load the `luacov` module, and it relies
  on the `debug` library, which the planned `core` preset does not include.

## Data and serialization

For a library written in C, follow [Native Lua extensions](../Assets/XLua/Doc/Add_Remove_Lua_Lib.md):
compile it into the xLua plugin and register it with `AddBuildin`. The
research behind this section is in [tdr.md](research/tdr.md), which also
records why the bundled `tdr` library was removed.

### luban

- **Link:** <https://github.com/focus-creative-games/luban>
- **Licence:** MIT
- **What it is:** a game configuration pipeline. It reads Excel, JSON, XML,
  YAML and Lua sources and writes code plus data in binary, JSON, Lua and
  other formats, for many languages including Lua and C#.
- **Fit:** its README lists xLua among the supported hot-update schemes. It
  has two Lua outputs that matter here (details in
  [focus-creative-games.md](research/focus-creative-games.md)): `lua-lua`
  writes Lua tables with EmmyLua annotations and needs no C# bridge, and
  `lua-bin` reads binary data through a C# `ByteBuf` class by way of `CS.*`.
- **Integration:** `lua-bin` makes many Lua to C# calls, so generate xLua
  wrappers for the `ByteBuf` types. The upstream `lua-bin` example still uses
  the old `Bright.Serialization` namespace. Prefer `lua-lua` when you want
  the smallest bridge surface.

### lua-protobuf

- **Link:** <https://github.com/starwing/lua-protobuf>
- **Licence:** MIT
- **What it is:** a Lua module that works with Google Protocol Buffers:
  schemas, encode and decode.
- **Fit:** versioned, schema-driven messages that match the Protobuf
  ecosystem. [tdr.md](research/tdr.md) names it as the modern counterpart of
  TDR.
- **Integration:** a C library, so follow the native-extension guide above.

### sproto

- **Link:** <https://github.com/cloudwu/sproto>
- **Licence:** MIT
- **What it is:** a small, fast schema-based protocol library similar to
  Protocol Buffers.
- **Fit:** a lighter schema language, common in Chinese game servers (per
  [tdr.md](research/tdr.md)).
- **Integration:** a C library with a Lua binding; same native-extension steps.

### lua-rapidjson

- **Link:** <https://github.com/xpol/lua-rapidjson>
- **Licence:** MIT
- **What it is:** a JSON module for Lua built on RapidJSON.
- **Fit:** it is the worked example in
  [Native Lua extensions](../Assets/XLua/Doc/Add_Remove_Lua_Lib.md).
- **Integration:** C++, so the extension build needs a C++ toolchain.
- **Activity:** last pushed 2025-08-26.

### lua-cjson

- **Link:** <https://github.com/openresty/lua-cjson>
- **Licence:** MIT
- **What it is:** a fast JSON encoder and parser in C, maintained by OpenResty.
- **Fit:** the plain-C alternative to lua-rapidjson; no C++ needed.
- **Integration:** native-extension steps as above.

### `string.pack` and `string.unpack`

- **Link:** [Lua 5.4 manual, `string.pack`](https://www.lua.org/manual/5.4/manual.html#pdf-string.pack)
- **Licence:** part of Lua (MIT)
- **What it is:** built-in binary packing, available from Lua 5.3.
- **Fit:** simple binary layouts with no dependency. It covers most of what
  the removed TDR wrapper did and works on the Lua 5.3, 5.4 and 5.5 builds.
- **Integration:** LuaJIT has no `string.pack`; it has `string.buffer`
  instead. Code that must run on both needs a fallback.

## Libraries and package management

Pure-Lua libraries work in xLua-NG once your loader can find their files; see
[Loading Lua sources](../Assets/XLua/Doc/Lua_Loaders.md). Libraries with C
code need the native-extension steps described under
[Data and serialization](#data-and-serialization).

### LPeg

- **Link:** <https://www.inf.puc-rio.br/~roberto/lpeg/> (the author's page;
  the GitHub copy at <https://github.com/roberto-ieru/LPeg> is an irregular
  mirror)
- **Licence:** MIT-style, copyright Lua.org, PUC-Rio
- **What it is:** a pattern-matching library based on parsing expression
  grammars. Patterns are Lua values that you combine with operators.
- **Fit:** parsers for config files, dialogue scripts or small DSLs, where
  Lua patterns are too weak.
- **Integration:** a C library, so build it into the plugin as a native
  extension.

### Penlight

- **Link:** <https://github.com/lunarmodules/Penlight>
- **Licence:** MIT
- **What it is:** a set of pure-Lua modules for tables, lists, strings,
  functional helpers, class definitions and more.
- **Fit:** fills gaps in the standard library without any native code.
- **Integration:** its file and directory functions need LuaFileSystem, which
  xLua-NG does not bundle. Use the table, string and class modules and leave
  the file modules out.

### inspect.lua

- **Link:** <https://github.com/kikito/inspect.lua>
- **Licence:** MIT
- **What it is:** a pure-Lua function that renders a table as readable text.
- **Fit:** print-debugging for Lua tables in the Unity console.
- **Integration:** bridge objects appear as `<userdata>` entries.

### LuaRocks

- **Link:** <https://github.com/luarocks/luarocks>
- **Licence:** MIT
- **What it is:** the package manager for Lua.
- **Fit:** a way to fetch pure-Lua libraries and their sources, and the
  install path for luacheck and busted.
- **Integration:** it installs for a standalone Lua, not for Unity. Copy the
  files you need into your project and ship them through your loader.

## Hot update and code protection

### HybridCLR

- **Links:** <https://github.com/focus-creative-games/hybridclr>,
  [hybridclr_unity](https://github.com/focus-creative-games/hybridclr_unity)
- **Licence:** MIT for the core. Paid commercial editions exist.
- **What it is:** a modified IL2CPP that interprets C# assemblies you
  download, so you can hot-update C# code on every platform.
- **Fit:** it solves a different problem from xLua-NG's hotfix. xLua-NG patches
  methods in AOT code with Lua; HybridCLR ships new C# assemblies. The
  comparison table is in [hotfix.md](research/hotfix.md).
- **Integration:** keep xLua's core, the generated wrappers and the `LuaDLL`
  P/Invoke declarations in AOT assemblies, because HybridCLR v9 forbids
  `extern` methods in hot-update assemblies. Delegate bridges for
  `CSharpCallLua` should use AOT delegate types (unconfirmed). The Lua hotfix
  is redundant for code inside a hot-update assembly. HybridCLR replaces
  `libil2cpp`, which xLua-NG itself avoids (see the design rules in
  [AGENTS.md](../AGENTS.md)), and at the time of the research it rejected
  Unity 6000.6, which xLua-NG supports. Check its supported-version page
  before you pick an editor version.

### obfuz

- **Links:** <https://github.com/focus-creative-games/obfuz>, [xLua guide](https://www.obfuz.com/en/docs/manual/xlua/work-with-xlua)
- **Licence:** MIT
- **What it is:** an IL obfuscator for Unity from the HybridCLR authors. It
  runs after the build's script compilation and supports HybridCLR through
  `obfuz4hybridclr`.
- **Fit:** protects game code against decompilation.
- **Integration:** it breaks xLua unchanged. xLua keys its metatables on
  `Type.FullName`, which obfuz renames. Its xLua guide has you patch about ten
  `Type.FullName` call sites to use `ObfuscationTypeMapper`, and register the
  types you expose to Lua before creating a `LuaEnv`. Lua hotfix names such as
  `xlua.hotfix(CS.T, 'Method', ...)` also break unless the target carries
  `[ObfuzIgnore]`. xlua-ng injects hotfixes before obfuz runs, which is the
  required order. Details and the planned adapter are in
  [obfuscators.md](research/obfuscators.md) and
  [focus-creative-games.md](research/focus-creative-games.md).

## Profiling and memory

The research in [lua-memory.md](research/lua-memory.md) found no ready-made
tool that maps Lua userdata to the C# objects a bridge keeps alive. The tools
below show the Lua side only. xLua-NG's own checkers
(`build/memory_leak_checker.c` and `General/LuaMemoryLeakChecker`) have gaps
that the same note lists; the
[Performance analysis](../Assets/XLua/Doc/Performance_Analysis.md) guide covers
the built-in profiler.

### lua-snapshot

- **Link:** <https://github.com/cloudwu/lua-snapshot>
- **Licence:** MIT
- **What it is:** a C library that snapshots a Lua state to find memory leaks.
- **Fit:** the research picks its reachability walk as the algorithm to port
  for a future built-in snapshot. Its source is about 430 lines of C and uses
  only the public Lua API.
- **Integration:** the notes confirm Lua 5.1 to 5.4; 5.5 and LuaJIT are
  plausible but were not compiled.
- **Activity:** last pushed 2024-08-05.

### Miku-LuaProfiler

- **Link:** <https://github.com/leinlin/Miku-LuaProfiler> (also known as
  LuaProfiler-For-Unity)
- **Licence:** Apache-2.0
- **What it is:** a Unity editor profiler for Lua: CPU and GC cost per
  function, mark-and-diff of memory, and a view of destroyed Unity objects
  still held from Lua.
- **Fit:** the research recommends learning from its workflow.
- **Integration:** it is invasive. It rewrites Lua source at load, hooks native
  exports and `dlopen`, and takes over GC, so hooking `dlopen` can crash. Treat
  it as an editor and development tool.

### LuaMemorySnapshotDump

- **Link:** <https://github.com/yaukeywang/LuaMemorySnapshotDump>
- **Licence:** MIT
- **What it is:** a pure Lua memory snapshot dump with reference chains and
  counts, and a query for a single object.
- **Fit:** useful on any Lua build because it uses only `debug.*`. The notes
  call it heavy: it allocates in the heap it measures.
- **Activity:** last pushed 2024-06-06.

## Suggest a project

Open an issue or a pull request. A project needs a verified link and licence,
and a sentence on how it fits xLua-NG.
## Projects we studied

These are the projects that xLua-NG's [research notes](research/README.md)
reviewed. They are references, not endorsements, and none is a dependency of
xLua-NG. Each line says what the note took from the project or decided about
it; the linked note has the evidence, the date and the confidence of each
claim.

| Project | What we took or decided | Note |
| --- | --- | --- |
| [Tencent/xLua](https://github.com/Tencent/xLua) (upstream) | xLua-NG contains every upstream commit up to 59bf426 (2025-11-21). Upstream issues, pull requests and forks were checked against it, and the bridge got a hardening review. | [UPSTREAM.md](../UPSTREAM.md), [bridge-audit.md](research/bridge-audit.md) |
| [PuerTS](https://github.com/Tencent/puerts), Lua and JS backends | Same author as xLua; its Lua backend is Lua 5.4.8 and still carries xLua's licence header. Copy: wrappers deduplicated by signature with a per-member reflection fallback, one marshalling model for generator and reflection, a watchdog, a debugger only in development builds, unique export names per plugin variant. Avoid: generated C++ against libil2cpp internals and addons that are enabled by default. | [puerts.md](research/puerts.md), [puerts-js.md](research/puerts-js.md) |
| [zlua](https://github.com/focus-creative-games/zlua) and [ZenTS](https://github.com/focus-creative-games/zen-ts) | zlua compiles a patched Lua into libil2cpp. Copy: one canonical reduced marshal signature as the dedup key, tests on every binding path, a log when a member falls back to reflection. Avoid: patching libil2cpp or the Lua VM and unpinned source downloads. ZenTS's `.d.ts` generator over the bound type set is the model for type definitions. | [focus-creative-games.md](research/focus-creative-games.md) |
| [HybridCLR](https://github.com/focus-creative-games/hybridclr) | Decided to keep the Lua hotfix, which patches AOT code without forking libil2cpp and works on Unity 6000.6, and to write an integration guide. | [hotfix.md](research/hotfix.md) |
| [ILRuntime](https://github.com/Ourpalm/ILRuntime) | A pure C# IL interpreter. Its use of `IPostBuildPlayerScriptDLLs` confirms that hook is the mainstream one for hotfix injection. | [hotfix.md](research/hotfix.md) |
| [InjectFix](https://github.com/Tencent/InjectFix) | Compared in the hotfix table: Cecil entry checks that redirect to its own IL VM; the note records broken async and failures in 2022.3 player builds. | [hotfix.md](research/hotfix.md) |
| [Luau](https://github.com/luau-lang/luau), with [NuLua](https://github.com/nuskey8/NuLua), [luau-dotnet](https://github.com/nuskey8/luau-dotnet) and [Luau-Unity](https://github.com/Quantum-Lion-Labs/Luau-Unity) | Verdict: not recommended as another Lua version under the full bridge (an estimated 4 to 8 weeks, and native size roughly doubles); plausible later as an opt-in backend of the core preset. Ideas to copy into the Lua 5.4/5.5 build: a step budget for pattern matching, a time limit, Luau's sandbox shape, a `buffer` library. None of the three C# and Unity bindings has an xLua-scale bridge, hotfix or a mature mobile story. | [luau.md](research/luau.md) |
| [tolua#](https://github.com/topameng/tolua) and [SLua](https://github.com/pangweiwei/slua) | Both generate static wrappers from Editor menus. tolua registers types by runtime `Type.Name`, so renaming breaks it. No leak tooling was found for either. zlua offers migration adaptors for xLua, tolua and SLua. | [binding-generation.md](research/binding-generation.md), [obfuscators.md](research/obfuscators.md) |
| [MoonSharp](https://github.com/moonsharp-devs/moonsharp) and [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) | Pure-C# Lua interpreters. MoonSharp uses reflection descriptors plus a "hardwire" generator for AOT; Lua-CSharp uses a source generator with `partial` classes and no reflection fallback. xLua-NG decided on non-partial per-assembly classes with an opt-in reflection fallback. | [binding-generation.md](research/binding-generation.md) |
| [lua-snapshot](https://github.com/cloudwu/lua-snapshot), [Miku-LuaProfiler](https://github.com/leinlin/Miku-LuaProfiler) and [LuaMemorySnapshotDump](https://github.com/yaukeywang/LuaMemorySnapshotDump) | Port lua-snapshot's reachability walk; learn the workflow of Miku-LuaProfiler and the output format of LuaMemorySnapshotDump; build the C# cross-reference layer ourselves. | [lua-memory.md](research/lua-memory.md) |
| [obfuz](https://github.com/focus-creative-games/obfuz), [Beebyte](https://assetstore.unity.com/packages/tools/utilities/obfuscator-48919), [GuardingPear Obfuscator](https://docs.guardingpearsoftware.com/manual/Obfuscator/Attributes.html), [ConfuserEx2](https://github.com/mkaring/ConfuserEx) and [Obfuscar](https://github.com/obfuscar/obfuscar) | Mapped where xLua breaks under renaming and sorted tools into recommended support tiers. obfuz would get a runtime mapper and a generated rule file; the others would get emitted `ObfuscationAttribute` plus exclusion guides. Running Beebyte after injection failed in [xLua#1122](https://github.com/Tencent/xLua/issues/1122), open when the note was written. | [obfuscators.md](research/obfuscators.md), [focus-creative-games.md](research/focus-creative-games.md) |
| [sproto](https://github.com/cloudwu/sproto) | Listed as a lightweight schema-based alternative to the removed TDR wrapper. | [tdr.md](research/tdr.md) |
| [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) and [MemoryPack](https://github.com/Cysharp/MemoryPack) | Evidence that Roslyn incremental source generators work in Unity: both ship them and require Unity 2022.3.12f1 or later. xLua-NG's generator mode targets 2021.3 and later after its own compatibility spike. | [binding-generation.md](research/binding-generation.md), [SOURCEGEN_SPIKE.md](../build/SOURCEGEN_SPIKE.md) |
| Tencent TDR ([tsf4g](https://github.com/tencentyun/tsf4g), the closest public code, which has no licence file) | The `libtdrlua` module was never published, so the `tdr` wrapper was removed from every preset. Replaced by a `string.pack` example and a pointer to lua-protobuf. | [tdr.md](research/tdr.md) |
