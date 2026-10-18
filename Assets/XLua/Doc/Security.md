# Security and sandboxing

`LuaEnv` does not sandbox Lua code. A script that runs in a default `LuaEnv`
can do anything the game process can do. Treat every Lua file you load into a
`LuaEnv` as native code, especially hotfix patches downloaded at runtime. For
scripts you do not trust, use [`LuaScript`](LuaScript.md), which is a sandbox
with limits ([below](#luascript)).

## What a default LuaEnv exposes

- **All of .NET.** The `CS` global reaches any public C# type through
  reflection, for example `CS.System.IO.File` or
  `CS.System.Diagnostics.Process`. `xlua.private_accessible` also opens
  private members.
- **The Lua standard libraries.** `luaL_openlibs` opens `io`, `os`,
  `package`, `debug`, `load` and `dofile`. `io.popen` runs shell commands
  and `package.loadlib` loads native libraries on Windows, macOS, Linux and
  Android. xLua removes `os.execute`
  from all bundled PUC Lua versions, but that alone is not a sandbox. The
  minimal preset's library has no `io`, no `os` beyond `time`, `clock`,
  `date` and `difftime`, no `package.loadlib` or C modules and no `dofile` or
  `loadfile`, and `LuaOptions.Libraries` lets a `LuaEnv` leave libraries out
  ([XLua_API_EN.md](XLua_API_EN.md#luaenv-luaenvluaoptions-options)); while
  scripts reach `CS`, that is not a sandbox either.
- **Network sockets.** LuaSocket is compiled in by default and registers
  itself in `package.preload` as `socket` and `socket.core` (with `mime` and
  `mime.core`) on every platform except Switch and WebGL. Build the native
  library with `-DXLUA_LUASOCKET=OFF` to leave it out; `require "socket"` then
  fails ([build/README.md](../../../build/README.md#luasocket-and-mime)).
- **Binary chunks.** The loader accepts precompiled bytecode. PUC Lua does not
  verify bytecode, so a crafted binary chunk can corrupt memory.

## Protecting downloaded scripts

Load scripts only from sources you control. To detect tampering in transit,
sign scripts and wrap your loader with `SignatureLoader`; see
[Script signatures](signature.md). A signature proves who produced a file. It
does not limit what the file can do.

## Running less-trusted scripts

Lua has no built-in sandbox, but you can narrow what a chunk sees. Pass a
restricted environment table to `DoString` or `LoadString`. The chunk then
resolves globals only from that table:

```csharp
LuaTable sandbox = luaenv.NewTable();
foreach (var name in new[] { "assert", "error", "ipairs", "next", "pairs",
                             "pcall", "select", "tonumber", "tostring",
                             "type", "xpcall" })
{
    sandbox.Set(name, luaenv.Global.Get<object>(name));
}
sandbox.Set("math", luaenv.Global.Get<LuaTable>("math"));
sandbox.Set("string", luaenv.Global.Get<LuaTable>("string"));
sandbox.Set("table", luaenv.Global.Get<LuaTable>("table"));
// Expose only the game API you want scripts to use, as Lua functions built by
// trusted code that return plain Lua values: a C# object would give the
// script reflection through its GetType().
LuaTable api = (LuaTable)luaenv.DoString(@"
    local ModApi = CS.MyGame.ModApi
    return { score = function() return ModApi.Score() end }")[0];
sandbox.Set("api", api);

luaenv.DoString(untrustedSource, "mod", sandbox);
```

Keep these limits in mind:

- Do not put `CS`, `xlua`, `require`, `load`, `dofile`, `io`, `os`,
  `package`, `debug` or `socket` in the table. Any one of them escapes the
  sandbox.
- Pass source text, not bytecode. Reject chunks whose first byte is `0x1B`.
- Shared tables such as `string` are the real library tables. A script can
  change them for every other script in the same `LuaEnv`. Use a separate
  `LuaEnv` for untrusted code, or give it copies.
- An environment table does not limit CPU or memory. xLua has no built-in
  time limit, so an endless loop blocks the calling thread. You can watch
  memory with `LuaEnv.Memroy` (the property name keeps upstream's spelling).
- C# objects you expose keep their full API, and every object's
  `GetType()` leads to reflection over all of .NET (see below). Expose Lua
  functions that call C#, not C# objects.
- By default a script can read and change the metatables of C# objects. For
  example, `getmetatable(obj).__gc(obj)` releases the C# handle of an object
  Lua still uses. Define `XLUA_LOCK_METATABLES` to lock them; see
  [Macros](XLua_API_EN.md#xlua_lock_metatables). Leave out `debug` and
  `xlua` as well, because `debug.getmetatable`, `debug.setmetatable` and
  `xlua.metatable_operation` bypass the lock.

## Opt-in bridge hardening

These defines make the bridge check state that a script can reach before it
acts on it. They cost little (measured in
[bridge-audit.md](../../../docs/research/bridge-audit.md)) and change nothing
for correct scripts, except where noted. See
[Macros](XLua_API_EN.md#bridge-hardening-defines) for details.

| Define | What a script can no longer do |
|---|---|
| `XLUA_LOCK_METATABLES` | Read or replace bridge metatables with `getmetatable`/`setmetatable` |
| `XLUA_CHECK_OBJECTS` | Pass a foreign userdata (a file, a socket) as `self` and reach another C# object |
| `XLUA_CHECK_STRUCT_ACCESSORS` | Read or write a foreign userdata with the `xlua.genaccessor` accessors |
| `XLUA_RESTRICT_GETMETATABLE` | Reach `__gc` through `xlua.getmetatable` and release a live object |
| `XLUA_CHECK_OBJECT_CACHE` | Plant a value in the userdata cache (through `debug.getregistry`) that C# then returns for an object |
| `XLUA_LOCK_XLUA_TABLE` | Assign to `xlua` fields, or change how `xlua.hotfix` and the `CS` lookup work by replacing `xlua.access` or `rawset`. `rawset(xlua, ...)` still works, so leave `rawset` out of a sandbox |
| `XLUA_RESTRICT_PRIVATE_ACCESS` | Use `xlua.access` and `xlua.private_accessible` outside hotfix, or wrap non-public methods with `xlua.tofunction` and `xlua.get_generic_method` |
| `XLUA_NO_LOAD_ASSEMBLY` | Load assemblies with `xlua.load_assembly` |

None of them limits what `CS` reaches. A script that holds any C# object
can call its `GetType()` and use reflection to read private fields and call
any method of any type, for example
`obj:GetType():GetDeclaredField("secret"):GetValue(obj)`; `typeof(CS.Foo)`
does the same. A type allowlist is planned for the minimal preset. Until
then, give a sandbox no C# objects at all: expose Lua functions, defined in
trusted code, that call C# for it.

A sandbox built this way reduces risk. It is not a security boundary you
should rely on against a determined attacker. For that, run untrusted code in
a separate process.

## LuaScript

[`LuaScript`](LuaScript.md) is the sandbox of xlua-ng. Unlike everything
above, it is meant to run code you do not trust. It is a preview: what this
section promises covers what is implemented today (loading and calling, C#
functions registered for Lua, the `buffer` library and the limits); table
handles and C#-driven coroutines will come under the same terms.

**Scope.** `LuaScript` running Lua 5.4 or 5.5 source text, on the core
library (`XLUA_PRESET=core`) or on a bridge library built with
`XLUA_LUASCRIPT=ON` in a project that also uses `LuaEnv`. `LuaEnv` itself is
not a sandbox and is out of scope; see the sections above.

**We treat these as security bugs.** Please report them privately, not in a
public issue. A script loaded as text through `LuaScript`, under any
`LuaOptions`, that:

1. corrupts memory, crashes the process or triggers undefined behaviour in
   native code;
2. reads or writes files, the network, other processes or memory outside its
   Lua state;
3. calls a C# method the host did not register, or reaches a C# object;
4. makes the state hold more than `MemoryLimit` plus 16 KB, plus what one host
   callback allocates;
5. keeps running more than 4,096 instruction-equivalents past
   `InstructionLimit`, or past `TimeLimit` by more than the watchdog latency
   (a few milliseconds) and one of the C paths listed below;
6. makes a C# call that hit an instruction, time or interrupt limit return
   normally;
7. changes what another environment of the same `LuaScript` sees: the frozen
   base, another environment's tables, or the string metatable;
8. loads or produces bytecode.

**Known issues:** none open. Fixed on 2026-10-18 (each was a bug under the
list above):

- With an `InstructionLimit` set, `TimeLimit` and `Interrupt()` took effect
  up to about five seconds late, at the instruction budget's next internal
  check (item 5). They now stop a call within a few milliseconds, with or
  without an instruction limit
  ([core-stop-latency.md](../../../docs/research/core-stop-latency.md)).
- `Interrupt()` stopped a call only while the script had an instruction or
  time limit; it now stops any running call (unless the script turns that
  off with `LuaOptions.AllowInterrupt = false`).
- `table.insert`, `table.remove` and `table.sort` under a script's `__len`
  looped in C up to 2^63 times (2^31 for `sort`) without allocating, and no
  limit, the watchdog or `Interrupt()` could stop them (item 5); with C
  metamethods `table.concat` was bounded only by `MemoryLimit`, and on Lua
  5.4.9 `string.rep("", math.maxinteger)` looped the same way. They now run
  under the C budget, and `string.rep` of two empty strings returns at once
  ([build/LUA_PATCHES.md](../../../build/LUA_PATCHES.md)).
- A table operation from C# that failed (a nil or NaN key) inside a host
  function that the script called through `xpcall` ran the script's message
  handler; a handler that raised left the native frame of the host function
  pointing at a dead Lua frame until it returned (item 1; no memory error
  was observed). Table operations now run no Lua code.

**Not covered:**

- **No termination guarantee without limits.** With `InstructionLimit` and
  `TimeLimit` off, a script can loop forever.
- **Listed C paths.** `string.rep`, `string.format` and `utf8` run in C for
  a time proportional to their input or result, which `MemoryLimit` bounds.
  A full garbage collection takes time proportional to the heap.
- **Host functions.** What a registered C# function does with its arguments,
  including how long it runs and how much it allocates, is the host's
  responsibility. A host function is never interrupted; the call stops when
  it returns.
- **Side channels.** `tostring` of tables and functions shows addresses;
  timing is observable.
- **Other builds.** LuaJIT, Lua 5.3, modified builds, builds without the
  `lstrlib.c` and `ltablib.c` budget patch ([build/LUA_PATCHES.md](../../../build/LUA_PATCHES.md)),
  and `LuaEnv` in any preset.
- **Lua itself.** Bugs in PUC Lua are in scope for reports. The fix depends on
  upstream or on a local patch to the pinned version.

**Supported versions:** the current main branch until the first release of
xlua-ng, then the latest release.
