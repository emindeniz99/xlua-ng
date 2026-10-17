# Security and sandboxing

xLua does not sandbox Lua code. A script that runs in a default `LuaEnv` can
do anything the game process can do. Treat every Lua file you load as native
code, especially hotfix patches downloaded at runtime.

## What a default LuaEnv exposes

- **All of .NET.** The `CS` global reaches any public C# type through
  reflection, for example `CS.System.IO.File` or
  `CS.System.Diagnostics.Process`. `xlua.private_accessible` also opens
  private members.
- **The Lua standard libraries.** `luaL_openlibs` opens `io`, `os`,
  `package`, `debug`, `load` and `dofile`. `io.popen` runs shell commands
  and `package.loadlib` loads native libraries on Windows, macOS, Linux and
  Android. xLua removes `os.execute`
  from all bundled PUC Lua versions, but that alone is not a sandbox.
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
