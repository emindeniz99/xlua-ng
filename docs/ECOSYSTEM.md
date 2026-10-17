# Ecosystem

Tools and libraries that work well with xLua-NG, grouped by purpose. xLua-NG
embeds standard Lua (5.5, 5.4, 5.3 or LuaJIT 2.1), so most of the Lua
ecosystem applies. This page lists the ones that matter for a Unity project
and says what to watch for.

Links, licences and last-push dates were checked on 2026-10-17 with the GitHub
API. A project is listed with its last push only when it has been quiet for
more than a year. None of these projects is bundled with xLua-NG or endorsed
by it, and the integration notes come from this repository's
[research notes](research/README.md) and guides, not from a test of every
combination. The preset names (`legacy`, `minimal`, `core`) refer to the
[preset design](design/presets.md), which is not implemented yet.

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
