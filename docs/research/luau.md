# Luau as input for xlua-ng

Researched 2026-10-17 from [luau-lang/luau](https://github.com/luau-lang/luau)
HEAD `9d7748a4` (2026-10-16, release 0.743) and
[luau-lang/rfcs](https://github.com/luau-lang/rfcs) `5fcbb5a2`. The VM,
compiler and CodeGen were built and measured; no Luau binary was run, so
speed is not measured. Confidence: **[C]** confirmed in source or primary
docs, **[S]** secondary, **[U]** unconfirmed.

## What Luau is

**Base:** Luau is derived from Lua 5.1 with a rewritten interpreter. It keeps
5.1's pseudo-indices and `getfenv`/`setfenv`. **[C]**

**Added:**

- `continue` and compound assignment;
- string interpolation, `if` expressions and generalized iteration;
- `const` and type annotations;
- a `buffer` type;
- a native allocation-free `vector` (3×float by default). **[C]**

**Rejected or removed:**

- from Lua 5.2–5.4: `goto`, `__gc`, `<close>`, `_ENV`, bitwise operators
  (`bit32` instead) and tail calls;
- from Lua 5.1: `io`, `package`, `dofile`/`loadfile`, `load`, `string.dump`
  and bytecode in `loadstring`. **[C]**

**Integers:**

- `number` is a double, and `lua_Integer` is a 32-bit `int` (`lua.h:122-125`).
- A separate 64-bit `integer` type exists in the RFC `type-long-integer.md`
  (2026-02) and in the VM (`LUA_TINTEGER`, `lua_pushinteger64`).
  - It has no arithmetic operators; scripts write `integer.add(a, b)`.
  - It is behind feature flags that are off by default.
  - luau.org/compatibility still lists 64-bit integers as rejected. **[C]**

**Compatibility:** Lua 5.3+ code (bitwise operators, integers, `goto`, `_ENV`,
`load`) does not run unchanged. **[C]**

## Sandbox and limits

**Sandbox setup:**

- `luaL_sandbox` (`VM/src/linit.cpp:66-111`) makes every library table, the
  string metatable and `_G` read-only, and turns on *safeenv*.
- `luaL_sandboxthread` (`:113-128`) gives each script a fresh globals table
  whose `__index` is the frozen `_G`.
- `lua_setreadonly` blocks every write, including `rawset`. **[C]**

**Libraries:**

- `os` has only `clock`, `date`, `difftime` and `time`.
- `debug` has only `info` and `traceback`.
- No `io`, `package`, `load` or `require` in the VM; the host provides them.
- There is no `lua_sethook`. **[C]**

**Interrupt callback:**

- `lua_callbacks(L)->interrupt` runs at calls, returns and loop back-edges
  (`VM/src/lvmexecute.cpp`) and at GC steps.
- **It also runs at every recursion step of pattern matching**
  (`VM/src/lstrlib.cpp:431-444`).
- It may be set from another thread (`lua.h:612-613`). Limits are therefore
  based on time, not on instruction counts.
- Other long C functions such as `table.concat`, `string.rep` and
  `table.sort` do not call it. **[C]**

**Memory:**

- the usual `lua_Alloc`;
- per-thread memory categories (`lua_setmemcat`, 256 categories);
- a caged heap (`lua_setmemorycage`). **[C]**

**Finalizers:** there is no `__gc`. Destructors are per userdata tag
(`lua_setuserdatadtor`), and scripts cannot call them. **[C]**

**Security statement** (SECURITY.md):

- source code cannot cause memory-safety errors;
- there is no termination guarantee;
- bytecode not produced by the Luau compiler is unsupported, so it must be
  signed. **[C]**

**Roblox:**

- one sandboxed thread per script over frozen builtins;
- a watchdog interrupt with a 10 s timeout in Studio;
- memory categories per script;
- `__gc` was dropped partly for thread-identity reasons. **[C/S]**

## Performance and CodeGen

**Interpreter:**

- computed-goto dispatch and inline caches;
- "imports" for globals and fastcalls for builtins;
- a multi-pass compiler.

The vendor claims it is faster than Lua 5.4 and sometimes matches the LuaJIT
interpreter. No independent numbers were found. **[C/U]**

**CodeGen is a JIT:**

- It writes machine code into `MAP_JIT` memory and makes it executable
  (`CodeGen/src/CodeAllocator.cpp:86-128`), so it is unusable on iOS App
  Store builds.
- It is optional: a separate library, compiled only for modules marked
  `--!native` or `@native`. **[C]**

## Embedding

**Loading code:** there is no `lua_load`. The flow is
`luau_compile(source) → bytecode`, then `luau_load`. The compiler needs C++
exceptions; the VM can use longjmp with `LUAU_EXTERN_C=ON`. **[C]**

**C API:**

- userdata tags 0–127 with per-tag metatables and destructors;
- `lua_ref` instead of `luaL_ref`;
- `lua_pushcclosurek` takes a debug name;
- no `lua_rotate`, `lua_copy`, `lua_newuserdatauv`, hooks or `lua_getstack`.
  **[C]**

**What the xlua-ng bridge would need:**

- 15 of the 50 Lua API functions that `LuaDLL.cs` imports are missing in
  Luau and need shims.
- `xlua.c` uses `luaL_loadbuffer`, `lua_newuserdatauv`, `lua_sethook` (the
  profiler), `lua_rotate`, `lua_copy` and `lua_pcallk`.
- ObjectTranslator's `__gc` release (`ObjectTranslator.cs:749`) would become
  a tag destructor that queues the index for C#.
- Unity `Vector3` could map to the native `vector` with no allocation. **[C]**

## Binary size (measured, Android arm64, NDK r30, API 23, 16 KB pages, `c++_static`)

| Stripped bytes (gzip -9) | VM + stdlib | VM + compiler | VM + compiler + CodeGen |
|---|---|---|---|
| `-Os` | 369,808 (144,715) | 907,664 (346,269) | 1,414,088 (557,277) |
| `-O2` | 390,192 (153,778) | 955,744 (378,112) | 1,501,416 (610,709) |
| `-Os`, VM with `-fno-exceptions -fno-unwind-tables` | 320,656 (129,224) | – (the compiler needs exceptions) | – |

For comparison, xlua-ng's `libxlua.so` with Lua 5.5.1 is 418,272 bytes
(169,029 gzip). That figure includes Lua's compiler, LuaSocket and `xlua.c`.

Breakdown of the Luau numbers:

- **VM core:** 186 KB, about the size of Lua 5.5's 184 KB core. Lua's core
  includes its compiler (about 54 KB), Luau's does not.
- **Luau compiler:** about 441 KB, roughly 8× Lua's parser.
- **CodeGen:** another 468 KB.
- **C++ runtime on Android:** 30–55 KB for libunwind, libc++abi and libc++,
  plus 40–115 KB of exception tables. **[C]**

## Platforms and ecosystem

- **Licence:** MIT.
- **Code:** a C++11 VM and a C++17 compiler.
- **Releases:** about weekly, with no LTS and roughly 80 feature flags in
  flight, all off by default.
- **Users:** Roblox; Alan Wake 2 (Remedy Northlight); Farming Simulator 2025;
  Second Life; Warframe. **[C/S]**
- **WebGL:** upstream builds with emscripten and `-fexceptions`. Whether
  Unity's WebGL exception setting allows the compiler's throws is untested.
  **[U]**
- **C# and Unity bindings:** NuLua (Lua 5.1–5.5, LuaJIT, Luau; desktop
  only), luau-dotnet (archived 2026-06) and Luau-Unity (a preview with
  hostile-script defaults; Windows and Android only). None has an xLua-scale
  bridge, hotfix or a mature mobile story. **[C]**

## Verdict

**Luau as another Lua version under the full bridge: not recommended.** The
estimate is 4–8 weeks plus continuous upkeep:

- port `xlua.c`;
- write shims;
- write a Luau branch of `init_xlua`;
- give up the profiler;
- design a new int64 story.

Existing 5.3+ scripts would break, and the native size roughly doubles with
the compiler on the device.

**Luau as an opt-in backend of the core preset: plausible later.** The
estimate is 1.5–3 weeks. It would bring a built-in sandbox, an interrupt that
also covers patterns, and Roblox-scale hardening. Blockers:

- **Size:** loading text needs the compiler, about 908 KB against the planned
  250–290 KB. A VM-only build would need signed, server-compiled bytecode.
- **Integers:** `long` values are awkward.
- **Limits:** no deterministic instruction limit.
- **C++ runtime:** exceptions are required, a risk on WebGL.
- **Upkeep:** weekly flag churn.
- **Portability:** scripts written for Luau don't run in the full preset's
  Lua 5.4/5.5, and the reverse.

Not a v1 target. If remote untrusted logic becomes a priority, spike a
`core-luau` plugin variant and measure it on the device first.

## Ideas to copy into the Lua 5.4/5.5 build

1. **Patch the pattern hang.** Have `lstrlib.c` `match()` check a budget or
   the hook at each step, as Luau's `lstrlib.cpp:431-444` does. This removes
   the documented `string.find`/`gsub` limit of the planned `LuaOptions`. It
   is small, isolated and the most valuable item on this list.
2. **Add a `TimeLimit`** next to `InstructionLimit`: wall-clock time, checked
   in the count hook. Allow a watchdog thread to arm the hook; Lua documents
   `lua_sethook` as signal-safe.
3. **Give `LuaScript` Luau's sandbox shape.**
   - Freeze a base environment once, and give each script a fresh `_ENV`
     whose `__index` is the frozen base.
   - Use proxy library tables and `__metatable` locks, because Lua 5.x has no
     read-only flag.
   - Allow only `collectgarbage("count")`.
4. **Make finalizers unreachable from scripts.** xlua-ng's bridge metatables
   expose `__gc` and set no `__metatable` (none in `Assets/XLua/Src` or
   `build/xlua.c`). Any script can therefore call `getmetatable(obj).__gc(obj)`
   and release a live C# handle early. Set `__metatable` on bridge metatables
   in the minimal and core presets. This is a real finding; it is tracked in
   [ROADMAP.md](../../ROADMAP.md).
5. **A `buffer` library on userdata** for core's `byte[]` values, using
   Luau's names (`create`, `len`, `readu8` … `writef64`, `readstring`,
   `copy`, `fill`). It avoids string copies and keeps scripts portable to
   Luau.
6. **A security statement for `LuaScript`**, modelled on Luau's SECURITY.md.
7. **Lower priority:**
   - one state per script for memory attribution;
   - a tag byte in the bridge userdata header for fast type checks;
   - a `.d.luau` definitions emitter, only if a Luau backend lands.

**Not worth copying:** CodeGen, feature flags as a release mechanism, and
integers without operators.
