# Core preset and the `LuaScript` API

Status: **design, written 2026-10-17; milestones 1 and 2 implemented on
2026-10-18 (sections 8.1 and 8.2), the rest not implemented**. It expands the
"Core preset", "Limits in every preset" and "Native layers" sections of
[presets.md](presets.md) into an API, a native ABI, a test plan and a work
breakdown. The research behind it is in [luau.md](../research/luau.md),
[puerts-js.md](../research/puerts-js.md),
[bridge-audit.md](../research/bridge-audit.md) and
[focus-creative-games.md](../research/focus-creative-games.md).

Decisions already made by the owner, and taken as given here:

- Lua 5.4 and 5.5 only.
- A separate `Core/` folder and asmdef that shares `LuaDLL.cs`.
- One UPM package with two asmdefs, `XLua.Core` and `XLua`; a define
  excludes the full assembly from core-only projects (2026-10-17).
- The class is `LuaScript`. Its options are `LuaOptions`, shared with `LuaEnv`.
- Tables are handles (`LuaTableRef`) with typed getters. There is no boxing
  converter as the main API. Source-generated `[LuaRecord]` types come later;
  an allocating `ToDictionary()` helper is optional.
- A script yields a request; C# resumes it from `IEnumerator` or `async`.
- No determinism mode, only `HashSeed`.
- `MemoryLimit`, `InstructionLimit` and a planned `TimeLimit`, in every preset.
- A step budget in `lstrlib.c` `match()` instead of disabling patterns.
- `xlua_core.c` holds the shared native layer; `xlua.c` builds on it.
- New native exports are allowed in the core variant.
- Text chunks only.
- Signature checking is optional; Ed25519 comes later.

File and line references are to this checkout (`5bc720d`).

## 1. Use cases and non-goals

**Use cases:**

- **Remote game logic:** reward formulas, quest conditions, A/B rules and
  event scripts downloaded at run time, which a new build would otherwise
  ship. Values go in, values come out.
- **Mods and user content:** code from players, run with hard memory and
  time limits so a bad mod cannot hang or crash the game.
- **Data as code:** configuration exported as Lua literals, for example
  luban's `lua-lua` output
  ([focus-creative-games.md](../research/focus-creative-games.md)), loaded
  without the C# bridge.
- **Server-side checks:** the same rule scripts run under .NET on a server
  (the `XLUA_GENERAL` build) to validate what clients report.
- **Small games or tools** that want Lua and a 250–300 KB library rather than
  the 1.36 MB the full bridge adds to an APK today (presets.md:147-149).

**Non-goals:**

- **No access to C# objects.** There is no `CS.*`, no ObjectTranslator, no
  userdata for C# objects and no hotfix. A script reaches only the functions
  the host registers. Projects that need the bridge use `LuaEnv`.
- **No bytecode.** Core loads text only. Precompiled chunks are refused at
  every entry point, so PUC Lua's unchecked bytecode loader is out of reach
  (bridge-audit.md:535-543).
- **No LuaJIT and no Lua 5.3.** LuaJIT's JIT is not allowed on iOS, 5.3 gets no
  fixes (presets.md:145-146), and core reads internal fields of the 5.4/5.5
  `lua_State` (section 3.5).
- **No determinism guarantee.** `HashSeed` fixes string hashing only. Address
  hashing of table keys, `math.random` seeding and `os.clock` stay outside it
  (section 3.10).
- **No preemption of C# code.** A limit stops Lua code. It does not stop a
  registered C# function that runs for a long time.
- **No multi-threaded state.** One `LuaScript` is used by one thread at a time
  (section 2.13).
- **No Luau.** A `core-luau` variant stays a later option
  ([luau.md](../research/luau.md), "Verdict").

## 2. C# API

### 2.1 Example

```csharp
using XLua;

using var lua = new LuaScript(new LuaOptions {
    MemoryLimit = 4 << 20,
    InstructionLimit = 10_000_000,
    TimeLimit = TimeSpan.FromMilliseconds(50),
    Libraries = LuaLibs.String | LuaLibs.Table | LuaLibs.Math,
});
lua.Register("log", (ref LuaCall c) => Debug.Log(c.GetString(0)));
lua.Load(remoteCode, "remote");                  // text only
long reward = lua.Call<long>("calcReward", level, "gold");

using LuaTableRef cfg = lua.Globals.GetTable("config");
double rate = cfg.GetDouble("dropRate");
```

`level` (an `int`) and `"gold"` convert to `LuaValue` through implicit
operators, so the call allocates nothing for numbers and booleans, and only
the UTF-8 encoding for strings, into a reused buffer.

### 2.2 Assemblies, namespaces and files

- **Folder and asmdef:** `Assets/XLua/Core/` with `XLua.Core.asmdef`,
  `allowUnsafeCode: true`, no references. The full runtime gets
  `XLua.asmdef` (none exists today; `find Assets -name '*.asmdef'` is empty)
  that references `XLua.Core`. Both ship in **one UPM package**; a project
  picks what it references (section 2.2.1).
- **Namespace:** `XLua`, so `LuaOptions` and `LuaException` are the same types
  for both APIs. Core type names do not collide with the bridge's `LuaTable`,
  `LuaFunction` and `LuaEnv`.
- **Types that move into Core:** `LuaDLL.cs`, and with it `LuaTypes`,
  `LuaGCOptions` and `LuaThreadStatus` (today in `ObjectTranslator.cs:55-89`),
  `LuaException` (`LuaException.cs:12-17`), and the part of `InternalGlobals`
  that `LuaDLL.cs` uses (`strBuff`, `LuaDLL.cs:277,312-322`).
- **How `LuaDLL.cs` is shared:** see open question 1. A Unity asmdef cannot
  compile one file into two assemblies, and a partial class cannot span two
  assemblies. A core-only plugin also cannot satisfy the bridge's
  `DllImport`s on iOS, where `__Internal` imports become link-time symbols.
  The proposal: `XLua.LuaDLL.Lua` moves to Core and keeps only the Lua C API
  and the `xlua_core_*` imports; the bridge imports (`xlua_pushcsobj`,
  `xlua_pack_*`, indexers, `luaopen_i64lib`) move to a new
  `XLua.LuaDLL.Bridge` class in the full assembly. (`luaopen_socket_core` is
  no longer a managed import: since 2026-10-18 `luaopen_xlua` registers
  LuaSocket in `package.preload` natively.)
- **Library name:** `xlua` for every variant (`LuaDLL.cs:39-43`). The core
  plugin is a variant of the same library with fewer exports, picked by
  `SetIncludeInBuildDelegate` (UNITY_SPIKE.md:315-362). The full plugin
  contains every core export, so `LuaScript` also works in `legacy` and
  `minimal`.

#### 2.2.1 One package, two assemblies

Decided by the owner on 2026-10-17: core and the full bridge ship in one UPM
package with two asmdefs, `XLua.Core` and `XLua`. A core-only project must
not pay for the bridge.

| Part | Core only (`XLUA_CORE_ONLY` defined) | Full (no define) |
|---|---|---|
| `XLua.Core` asmdef (`Assets/XLua/Core`) | compiled | compiled |
| `XLua` asmdef (`Assets/XLua/Src`) | not compiled: `"defineConstraints": ["!XLUA_CORE_ONLY"]` | compiled, references `XLua.Core` |
| `XLua.Editor` asmdef (`Assets/XLua/Src/Editor`: generator, hotfix, LuaCATS) | not compiled: same constraint | compiled |
| `XLua.Core.Editor` asmdef (plugin selector, core checks) | compiled | compiled |
| Native plugin per platform and ABI | the core variant | the `legacy` or `minimal` variant, which contains every core export |

- **Excluding the full assembly.** Define constraints work for managed
  assemblies, unlike native plugins, where they failed in the spike
  (UNITY_SPIKE.md:315-331). The plugin selector reads the Player Settings
  scripting defines, not `csc.rsp` (UNITY_SPIKE.md:333-337), so
  `XLUA_CORE_ONLY` goes into Player Settings, where both mechanisms see it.
- **Native variant.** The `PluginImporter.SetIncludeInBuildDelegate` selector
  from the spike (`Test/MobileSmoke/PluginVariantSelector.cs`,
  UNITY_SPIKE.md:333-339) moves into `XLua.Core.Editor`. It includes the core
  plugin when `XLUA_CORE_ONLY` is set, and otherwise the plugin that the
  preset defines choose (`legacy` by default, `minimal`, or the Development
  Build variant). Exactly one `xlua` library per platform and ABI reaches the
  player, because two plugins with the same name and ABI fail the build
  (UNITY_SPIKE.md:325-331).
- **Code that refers to the full API** (`LuaEnv`, `CS.*` bindings, generated
  wrappers) fails to compile under `XLUA_CORE_ONLY`. That is the intended
  signal. Core code never refers to the full assembly.
- **Unverified:** whether Unity's Minimal managed stripping already drops a
  package assembly that nothing references, without the define. The size
  test in section 6 answers it. If it does, the define still matters for the
  native plugin choice and for Editor compile time.

#### 2.2.2 Using both in one player

A game can use the full bridge for its own code and `LuaScript` for
sandboxed remote scripts, in the same player:

- **Setup:** no `XLUA_CORE_ONLY`. Both assemblies compile, the selector
  ships the full plugin, and `LuaScript` runs on it, because `xlua.c` builds
  on `xlua_core.c` and the full plugin exports every `xlua_core_*` function.
- **Separate states:** a `LuaEnv` and a `LuaScript` never share a
  `lua_State`, a registry or a handle. A `LuaScript` cannot reach `CS.*`,
  because its state never runs `luaopen_xlua` or creates an ObjectTranslator;
  the sandbox is built at run time by `xlua_core_open` with `sandbox = 1`.
- **Library choice:** the full plugin also contains `io`, `os`, `debug` and
  `package`. `LuaScript` does not open them (`LuaLibs` rejects them,
  section 2.14), so the script sees the same base and libraries as with the
  core plugin.
- **What differs from a core-only player:**
  - The `xlua_core_nobc.c` bytecode stubs are absent, because the bridge needs
    the real `lundump.c`. Text-only loading then rests on mode `"t"` at every
    entry point and in the wrapped `load` (section 3.9). The tests run the
    bytecode-refusal cases on both plugins.
  - The `lstrlib.c`/`ltablib.c` budget patch and the hooks header are in
    every preset, so the C budget is the same.
  - Size: the player pays for the full bridge once; `LuaScript` adds only its
    C# (section 6).
- **Limits on `LuaEnv`** come from the same `LuaOptions` and native layer,
  so a game can also cap its own trusted `LuaEnv`.
- **Threads:** a `LuaEnv` and any number of `LuaScript`s may live on
  different threads, each confined to its own (section 2.13).

Core files (C#): `LuaScript.cs`, `LuaOptions.cs`, `LuaValue.cs`,
`LuaTableRef.cs`, `LuaFunctionRef.cs`, `LuaBufferRef.cs`, `LuaCall.cs`,
`LuaCoroutine.cs`, `LuaCoroutineDrivers.cs`, `LuaExceptions.cs`,
`LuaWatchdog.cs`, `LuaDLL.cs`.

### 2.3 Lifecycle

```csharp
public sealed class LuaScript : IDisposable
{
    public LuaScript();                         // LuaOptions.ScriptDefaults (open question 2)
    public LuaScript(LuaOptions options);       // copies the options; later edits do not apply

    public bool IsDisposed { get; }
    public void Dispose();                      // closes the state; every handle becomes invalid
    ~LuaScript();                               // closes a leaked state; warns in development builds

    // limits, adjustable between calls
    public long MemoryLimit { get; set; }       // lowering below MemoryUsed makes the next growth fail
    public long InstructionLimit { get; set; }
    public TimeSpan TimeLimit { get; set; }

    // statistics, readable at any time from the owner thread
    public long MemoryUsed { get; }             // bytes the allocator holds for this state
    public long MemoryPeak { get; }
    public long LastInstructionCount { get; }   // VM instructions of the last top-level call
    public int LiveHandles { get; }             // handles not yet disposed, for leak checks

    public void Interrupt();                    // any thread: stops the running call (section 2.12)
    public void CollectGarbage();               // full collection
    public LuaTableRef Globals { get; }         // the default environment (section 4)
    public LuaTableRef NewEnvironment();        // a fresh _ENV over the frozen base
    public LuaModuleResolver ModuleResolver { get; set; }  // enables require (section 4)
}

public delegate bool LuaModuleResolver(string name, out string code, out string chunkName);
```

- **Construction** checks `xlua_core_version()` the way `LuaEnv` checks the
  bridge version (`LuaEnv.cs:62-70`), opens the state, the libraries and the
  sandbox in one native call (`xlua_core_open`, section 3.3), and pins a weak
  `GCHandle` to itself for callbacks.
- **The finalizer** may close the state: a finalized `LuaScript` is
  unreachable, so no other thread uses it. The callback handle is weak for
  that reason. Handles (`LuaTableRef` and the others) hold a strong reference,
  so a script with live handles is never finalized.
- **`Dispose` inside a callback or a coroutine driver** throws
  `InvalidOperationException`. The state is closed when the outermost call
  returns.

### 2.4 Values

```csharp
public enum LuaType : byte { Nil, Boolean, Integer, Number, String, Table, Function, Buffer, Other }

public readonly struct LuaValue : IEquatable<LuaValue>
{
    public static readonly LuaValue Nil;
    public LuaType Type { get; }
    public bool IsNil { get; }

    public static implicit operator LuaValue(bool v);
    public static implicit operator LuaValue(long v);     // int, short, byte widen to long
    public static implicit operator LuaValue(double v);   // float widens to double
    public static implicit operator LuaValue(string v);   // null becomes nil
    public static implicit operator LuaValue(LuaTableRef v);
    public static implicit operator LuaValue(LuaFunctionRef v);
    public static implicit operator LuaValue(LuaBufferRef v);
    public static LuaValue FromBytes(byte[] bytes);       // a Lua string with these bytes

    public bool AsBool();      // Lua truth: only nil and false are false
    public long AsLong();      // integer, or a float with an exact integer value; else LuaTypeException
    public double AsDouble();  // integer or float
    public string AsString();  // strings only; numbers are not converted
    public byte[] AsBytes();
    public LuaTableRef AsTable(); public LuaFunctionRef AsFunction(); public LuaBufferRef AsBuffer();
    public bool TryGetLong(out long v); public bool TryGetDouble(out double v);
    public bool TryGetString(out string v);
}
```

`LuaValue` is 24 bytes: a tag, a `long` or `double` payload and one object
field (a `string`, a `byte[]` or the owning `LuaScript` for handles). Strings
from Lua are decoded as UTF-8 with U+FFFD for invalid bytes, as the bridge
does (`LuaDLL.cs:263-282`).

**Integers stay lossless:** Lua 5.4/5.5 integers are 64-bit, so `long` maps
to `lua_Integer` directly and core needs none of `i64lib.c` (its 5.3+ branch is
a pass-through, `i64lib.c:482-505`). `ulong` is not a `LuaValue` type; values
above `long.MaxValue` are passed as their two's-complement `long`.

### 2.5 Load

```csharp
public void Load(string code, string chunkName);
public void Load(ReadOnlySpan<byte> utf8Code, string chunkName);
public void Load(string code, string chunkName, LuaTableRef env);
public T Load<T>(string code, string chunkName);                 // first result
public T Load<T>(string code, string chunkName, LuaTableRef env);
public int Load(string code, string chunkName, LuaTableRef env, Span<LuaValue> results);
public LuaFunctionRef Compile(string code, string chunkName, LuaTableRef env = default);
```

- **Text only:** every overload loads with mode `"t"`. A chunk that starts
  with the bytecode signature fails with `LuaSyntaxException`.
- **Chunk names** get an `@` prefix, so tracebacks read `remote:3:` rather
  than `[string "remote"]:3:` (backlog: "Always use `@<real path>` chunk
  names").
- **Environment:** `env` defaults to `Globals`. `Compile` binds the
  environment but does not run the chunk.
- **Limits** apply to the run of the chunk; compiling counts against
  `MemoryLimit` only.

### 2.6 Call

```csharp
public void Call(string function);
public void Call(string function, LuaValue a1);
public void Call(string function, LuaValue a1, LuaValue a2);
public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3);
public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4);
public void Call(string function, ReadOnlySpan<LuaValue> args);

public T Call<T>(string function);   // and the same five argument shapes
public T Call<T>(string function, LuaValue a1);
// ...
public T Call<T>(string function, ReadOnlySpan<LuaValue> args);
```

- **Lookup:** `function` is a global of `Globals` (raw get first, then the
  frozen base). Dotted names (`"quest.onStart"`) walk tables with raw gets.
  `LuaFunctionRef` and `LuaTableRef.Call...` have the same overloads for other
  environments and for functions held as values.
- **`T`** is one of `long`, `int`, `double`, `float`, `bool`, `string`,
  `byte[]`, `LuaValue`, `LuaTableRef`, `LuaFunctionRef`, `LuaBufferRef`. A
  static generic reader (`LuaRead<T>.Read`, a delegate chosen once per `T`)
  converts the result without boxing; IL2CPP sees every instantiation at the
  call site. Another `T` throws `NotSupportedException` at the first call.
  `int` and `float` check the range and throw `LuaTypeException` on overflow.
- **More than four arguments:** the span overload. `LuaValue` holds an object
  field, so the span cannot come from `stackalloc`; callers reuse an array.
- **One P/Invoke per call:** the arguments, the function selector, the limits
  and the result buffer travel in one `xlua_core_call` struct (section 3.3).
  The zlua comparison suggests transitions dominate call cost
  (focus-creative-games.md:84-88).

### 2.7 Multiple returns

```csharp
public int Call(string function, ReadOnlySpan<LuaValue> args, Span<LuaValue> results);
public (T1, T2) Call<T1, T2>(string function, ReadOnlySpan<LuaValue> args);
public (T1, T2, T3) Call<T1, T2, T3>(string function, ReadOnlySpan<LuaValue> args);
```

The span form fills `results` and returns how many values the function
returned. Values beyond `results.Length` are dropped; missing ones are `Nil`.
Table and function results arrive as owning handles that the caller disposes.

### 2.8 Register

```csharp
public delegate void LuaCallback(ref LuaCall call);

public void Register(string name, LuaCallback callback);          // into the frozen base
public void Register(LuaTableRef env, string name, LuaCallback callback);
public void Register<A1, R>(string name, Func<A1, R> fn);          // typed shorthands, 0–4 args
public void Register<A1>(string name, Action<A1> fn);
public void Unregister(string name);

public ref struct LuaCall
{
    public LuaScript Script { get; }
    public int ArgCount { get; }
    public LuaType ArgType(int i);

    public bool GetBool(int i);   public long GetLong(int i);   public double GetDouble(int i);
    public string GetString(int i);              // allocates the managed string
    public ReadOnlySpan<byte> GetUtf8(int i);    // no allocation; valid until the callback returns
    public bool ArgEquals(int i, string ascii);  // compares without decoding
    public LuaTableRef GetTable(int i);          // borrowed: valid until the callback returns
    public LuaFunctionRef GetFunction(int i);    // borrowed
    public LuaBufferRef GetBuffer(int i);        // borrowed
    public LuaValue Get(int i);
    public long OptLong(int i, long fallback);   // nil or none gives fallback; also OptDouble, OptString

    public void Return(bool v); public void Return(long v); public void Return(double v);
    public void Return(string v); public void Return(ReadOnlySpan<byte> utf8); public void Return(LuaValue v);
    public void ReturnNil();
    public void Yield();   // suspend the calling coroutine; the returned values are yielded
}
```

- **Arguments without allocation:** before C# runs, the native trampoline
  copies every argument into an array of 16-byte `xlua_value` records
  (section 3.7). Strings are pointer and length into the Lua string, which
  stays on the Lua stack during the call. `LuaCall` reads that array through a
  pointer, so reading an argument costs no P/Invoke and no allocation.
- **Wrong types:** `GetLong(0)` on a string throws `LuaTypeException`, which
  the trampoline turns into the usual Lua message
  `bad argument #1 to 'calcReward' (number expected, got string)`.
- **Returns without allocation:** `Return` writes `xlua_value` records into a
  native result array; strings are encoded into a pinned per-script scratch
  arena. The trampoline pushes them after C# has returned.
- **Borrowed handles** are tied to the call frame. Using one after the
  callback returns throws `ObjectDisposedException`. `Retain()` turns a
  borrowed handle into an owning one.
- **Typed shorthands** read arguments and write results through the same
  `LuaRead<T>`/`LuaWrite<T>` tables as `Call<T>`, so `Register<long, long>`
  boxes nothing.
- **Exceptions:** any exception thrown by the callback becomes a Lua error. If
  the script does not catch it, the C# entry call throws
  `LuaRuntimeException` with the original exception as `InnerException`.
- **Nested calls:** a callback may call `Call`, `Load` or table methods. Those
  run inside the outer call's limits (section 3.5).
- **`Yield()`** lets a registered function suspend the script, for example a
  `fetch(url)` that the coroutine driver completes. It is only valid when the
  script runs in a coroutine started by `Start`; elsewhere it throws.
- **Dotted names** (`Register("game.log", ...)`) create a library table in
  the base, copied into each environment like the standard libraries.

### 2.9 Tables: `LuaTableRef`

```csharp
public readonly struct LuaTableRef : IDisposable, IEquatable<LuaTableRef>
{
    public bool IsValid { get; }          // false after Dispose, after the frame ends (borrowed) or after the script is disposed
    public LuaScript Script { get; }

    // reads: raw (no metamethods), typed, keyed by string, integer, LuaKey or LuaValue
    public long GetLong(string key);      public long GetLong(long index);    public long GetLong(LuaKey key);
    public double GetDouble(string key);  public bool GetBool(string key);    public string GetString(string key);
    public byte[] GetBytes(string key);   public LuaValue Get(LuaValue key);
    public long GetLong(string key, long fallback);                          // and the other types
    public bool TryGetLong(string key, out long value);                      // and the other types
    public bool TryCopyBytes(string key, Span<byte> destination, out int written);
    public LuaType TypeOf(string key);
    public LuaTableRef GetTable(string key);       // a new owning handle
    public bool TryGetTable(string key, out LuaTableRef table);
    public LuaFunctionRef GetFunction(string key);

    // writes: raw
    public void Set(string key, LuaValue value);  public void Set(long index, LuaValue value);
    public void Set(LuaKey key, LuaValue value);  public void Set(LuaValue key, LuaValue value);
    public LuaTableRef SetNewTable(string key, int arrayCapacity = 0, int hashCapacity = 0);

    public long Length { get; }                   // raw length (rawlen)
    public LuaPairs Pairs();                      // struct enumerator
    public LuaIPairs IPairs();                    // 1..n until the first nil

    public LuaTableRef Retain();                  // an owning copy of this handle
    public Dictionary<string, object> ToDictionary(int maxDepth = 16);  // optional, allocates
    public void Dispose();
}

public readonly struct LuaKey { }                 // lua.Key("gold"): a pre-interned string key
public LuaTableRef LuaScript.NewTable(int arrayCapacity = 0, int hashCapacity = 0);
public LuaKey LuaScript.Key(string name);

public struct LuaPairs : IDisposable
{
    public LuaPair Current { get; }               // Key and Value as LuaValue; tables borrowed until MoveNext
    public bool MoveNext();
    public LuaPairs GetEnumerator();
}
```

- **Raw access only.** Getters and setters never run metamethods, so table
  access from C# cannot run script code, cannot yield and needs no time
  limit. It can still hit `MemoryLimit` on writes and string keys, and then
  throws `LuaMemoryLimitException`.
- **No boxing:** typed getters return the value through one P/Invoke that
  writes an `xlua_value`. `GetLong` on an integral float converts it;
  `GetLong` on a string throws `LuaTypeException` (no string-to-number
  coercion). Missing keys throw `KeyNotFoundException` for the plain getters,
  return the fallback for the fallback overloads and `false` for `TryGet`.
- **`LuaKey`** anchors one Lua string for the script's lifetime. Reads through
  a `LuaKey` skip UTF-8 encoding and the protected call; they are the fast path
  for hot fields.
- **Handles are structs.** A handle is the owning `LuaScript`, a slot number
  and a generation, so creating one allocates nothing on the managed heap.
  `Dispose` releases the registry slot. A copy of a disposed handle sees a
  newer generation and throws `ObjectDisposedException`; a second `Dispose` is
  a no-op. A struct has no finalizer: a forgotten handle keeps its table alive
  until the script is disposed. `LuaScript.LiveHandles` counts them; open
  question 6 asks whether a class with a finalizer is preferred.
- **Nesting:** `GetTable` returns a new owning handle, so
  `t.GetTable("a").GetTable("b")` leaks the middle one. Write
  `using var a = t.GetTable("a");`. Inside `Pairs()`, nested table values are
  borrowed until the next `MoveNext`.
- **Iteration** keeps its key on the native side (section 3.3), so `foreach`
  allocates nothing except the managed strings of keys the caller decodes.
  `Current.Key.Type` and `KeyEquals("x")` inspect keys without decoding. The
  Lua rule applies: assigning existing fields during traversal is fine,
  adding keys is not ("invalid key to 'next'" becomes `LuaRuntimeException`).
- **After the script is disposed:** every member throws
  `ObjectDisposedException` except `IsValid` (false) and `Dispose` (no-op).
- **`ToDictionary`** converts a tree into `Dictionary<string, object>` and
  `List<object>` with boxed numbers. It is a debugging and migration helper,
  not the main API. Cycles and depth beyond `maxDepth` throw.

`LuaFunctionRef` has the same handle shape plus the `Call` overloads of
section 2.6.

### 2.10 `byte[]` and buffers

- **`byte[]` as a Lua string:** `LuaValue.FromBytes`, `Return(ReadOnlySpan<byte>)`
  and `Set(key, LuaValue.FromBytes(b))` copy bytes into a Lua string, which
  may hold any byte. `GetBytes` copies out into a new array; `TryCopyBytes`
  and `GetUtf8` avoid the allocation.
- **`buffer` library** (`LuaLibs.Buffer`, opt-in): a fixed-size mutable byte
  array as a userdata, with Luau's API names
  ([luau.md](../research/luau.md), idea 5): `buffer.create`, `fromstring`,
  `tostring`, `len`, `readi8`/`readu8`/`readi16`/`readu16`/`readi32`/`readu32`/
  `readf32`/`readf64` and the `write` forms, `readstring`, `writestring`,
  `copy`, `fill`. Offsets are 0-based and little-endian, as in Luau; every
  access is bounds-checked. The metatable is locked.
- **C# side:**

  ```csharp
  public LuaBufferRef LuaScript.NewBuffer(int length);
  public LuaBufferRef LuaScript.NewBuffer(ReadOnlySpan<byte> contents);

  public readonly struct LuaBufferRef : IDisposable
  {
      public int Length { get; }
      public bool IsValid { get; }
      public void CopyTo(Span<byte> destination);  public void CopyFrom(ReadOnlySpan<byte> source, int offset = 0);
      public int ReadInt32(int offset);  public void WriteInt32(int offset, int value);  // and the other widths
      public Span<byte> AsSpan();   // over Lua's memory: valid while this handle is valid
      public void Dispose();
  }
  ```

  Lua's collector never moves objects and a buffer never resizes, so
  `AsSpan()` gives zero-copy access while the handle keeps the userdata
  alive. Using the span after `Dispose` is a host bug that the API cannot
  detect; the copy methods are the safe default.

### 2.11 Coroutines

```csharp
public LuaCoroutine LuaScript.Start(string function);    // runs to the first yield or the end
public LuaCoroutine LuaScript.Start(string function, LuaValue a1);  // up to 4, and a span overload
public LuaCoroutine LuaScript.Start(LuaFunctionRef function, ReadOnlySpan<LuaValue> args);

public enum LuaCoroutineStatus { Yielded, Finished, Faulted, Closed }

public sealed class LuaCoroutine : IDisposable
{
    public LuaCoroutineStatus Status { get; }
    public bool IsYielded { get; }
    public bool IsDone { get; }                   // Finished, Faulted or Closed
    public int ValueCount { get; }                // yielded values, or return values once finished
    public LuaValue this[int i] { get; }          // tables are borrowed until the next Resume
    public T Get<T>(int i);
    public void Resume();                         // and Resume(a1) ... Resume(a1..a4), Resume(span)
    public void Close();                          // runs pending to-be-closed variables
    public void Dispose();                        // Close and release the thread
}
```

Script side:

```lua
function main(level)
  local ok = coroutine.yield({ op = "wait", seconds = 1.5 })
  local data = coroutine.yield({ op = "fetch", key = "rewards" })
  return data.gold * level
end
```

- **Limits apply per `Start` or `Resume`:** each one is a top-level call with
  the full `InstructionLimit` and `TimeLimit`. `MemoryLimit` covers the whole
  state. Open question 5 asks about a cumulative budget per coroutine.
- **Errors:** a Lua error in the coroutine throws from `Start` or `Resume`
  and leaves the coroutine `Faulted`. A limit error does the same.
- **Yield from C#:** a registered function can call `LuaCall.Yield()`
  (section 2.8). Lua 5.4/5.5 allow yields across `pcall` and metamethods, so
  `coroutine.yield` works from nested Lua functions.

**Drivers** (in `LuaCoroutineDrivers.cs`, all on the owner thread):

```csharp
public sealed class LuaReply                    // values passed to the next Resume; reused per yield
{
    public void Set(LuaValue v); public void Set(LuaValue a, LuaValue b); public void Clear();
}

// Unity coroutine: StartCoroutine(co.Run(Handle))
public delegate IEnumerator LuaRequestHandler(LuaCoroutine co, LuaReply reply);
public IEnumerator LuaCoroutine.Run(LuaRequestHandler handler);

// async/await: continues on the SynchronizationContext captured at the call
public delegate ValueTask LuaAsyncRequestHandler(LuaCoroutine co, LuaReply reply, CancellationToken ct);
public Task LuaCoroutine.RunAsync(LuaAsyncRequestHandler handler, CancellationToken ct = default);

#if UNITY_2023_1_OR_NEWER
public delegate Awaitable LuaAwaitableRequestHandler(LuaCoroutine co, LuaReply reply, CancellationToken ct);
public Awaitable LuaCoroutine.RunAwaitable(LuaAwaitableRequestHandler handler, CancellationToken ct = default);
#endif
```

- The driver calls the handler for each yield, then resumes with the reply.
  It returns when the coroutine finishes; the return values stay readable
  through `co[i]`.
- **Thread:** `RunAsync` captures `SynchronizationContext.Current` and posts
  each `Resume` back to it, so a handler that uses `ConfigureAwait(false)`
  still resumes Lua on the owner thread. Without a context (plain .NET) it
  resumes on whatever thread completes the handler, and the thread check
  (section 2.13) then requires `CheckThread = false` and one thread at a time.
- **Cancellation:** `ct` closes the coroutine (running its `__close`
  handlers) and the task ends with `OperationCanceledException`.
- **UniTask:** no dependency. A UniTask user wraps `RunAsync` or writes the
  ten-line loop against `Resume` and `IsYielded`.

### 2.12 Errors and exceptions

```csharp
public class LuaException : Exception                // existing XLua.LuaException, moved to Core
{
    public LuaException(string message);             // kept for the bridge
    public string LuaStackTrace { get; }             // luaL_traceback output; null when none was taken
}
public class LuaRuntimeException : LuaException      // error() and runtime errors
{
    public LuaValue ErrorValue { get; }              // error({code = 5}) keeps the table (owning handle)
}                                                    // InnerException: C# exception from a callback
public sealed class LuaSyntaxException : LuaException { public string ChunkName { get; } }
public sealed class LuaTypeException : LuaException  { public LuaType Expected { get; } public LuaType Actual { get; } }

public enum LuaLimit { Memory, Instructions, Time, Interrupt }
public abstract class LuaLimitException : LuaException { public LuaLimit Limit { get; } }
public sealed class LuaMemoryLimitException : LuaLimitException
    { public long MemoryLimit { get; } public long MemoryUsed { get; } public bool HostOutOfMemory { get; } }
public sealed class LuaInstructionLimitException : LuaLimitException { public long InstructionLimit { get; } }
public sealed class LuaTimeLimitException : LuaLimitException
    { public TimeSpan TimeLimit { get; } public TimeSpan Elapsed { get; } }
public sealed class LuaInterruptedException : LuaLimitException { }
```

- **Tracebacks:** a C message handler runs `luaL_traceback` for runtime
  errors. It needs no `debug` library. The bridge's handler looks up the
  global `debug.traceback` instead (`xlua.c:707-715`), which fails when a
  script replaces `debug` or the library is missing.
- **Classification comes from the native state, not from message text.** The
  native entry returns a status that names the limit (section 3.5).
- **Instruction, time and interrupt limits are terminal for the call.** A
  script `pcall` can catch the first error, but the stop hook stays armed and
  raises again at the next instruction until the C# call returns
  (puerts-js.md:31-40).
- **The memory limit is a cap, not a stop.** A refused allocation raises
  Lua's normal memory error, which `pcall` can catch; the cap holds either
  way. When the error reaches C#, it becomes `LuaMemoryLimitException`
  (`HostOutOfMemory` when `malloc` itself failed). Open question 4 asks
  whether it should be terminal like the others.
- **After a limit error** the state is consistent (Lua guarantees that after
  any error) and the script stays usable. Script-level data may be half
  updated; hosts that run untrusted code should dispose it.
- **Host errors:** a disposed script throws `ObjectDisposedException`; use
  from another thread throws `InvalidOperationException`.

### 2.13 Threads

- **One owner thread.** A `LuaScript` is used from the thread that created it.
  With `CheckThread` (default on) every member except `Interrupt`,
  `IsDisposed` and the statistics compares `Environment.CurrentManagedThreadId`
  and throws on mismatch.
- **Several scripts, several threads:** independent `LuaScript` instances may
  run in parallel. Each has its own native state; the only process-wide
  native values are the dispatcher pointer, set once, and the watchdog.
- **Callbacks** run synchronously on the thread that made the call.
- **`Interrupt()`** is the one cross-thread operation. It stops the running
  call with `LuaInterruptedException`; with nothing running it does nothing.
- **WebGL** has no threads: no watchdog, and `TimeLimit` falls back to a
  clock check in the hook (section 3.6).

### 2.14 `LuaOptions`

```csharp
public sealed class LuaOptions
{
    public long MemoryLimit { get; set; }        // bytes; 0 = unlimited. Default 0
    public long InstructionLimit { get; set; }   // VM instructions per top-level call; 0 = unlimited. Default 0
    public TimeSpan TimeLimit { get; set; }      // wall clock per top-level call; Zero = unlimited. Default Zero
    public LuaLibs Libraries { get; set; }       // Default = the host's default set. Default LuaLibs.Default
    public uint? HashSeed { get; set; }          // null = random per state. Default null
    public bool CheckThread { get; set; }        // owner-thread check (section 2.13). Default true

    public static LuaOptions ScriptDefaults { get; }  // what new LuaScript() uses (open question 2)
}

[Flags]
public enum LuaLibs
{
    Default   = 0,       // LuaScript: Safe. LuaEnv: everything, as today
    Coroutine = 1 << 0,
    String    = 1 << 1,
    Table     = 1 << 2,
    Math      = 1 << 3,
    Utf8      = 1 << 4,
    Buffer    = 1 << 5,
    Io        = 1 << 6,  // LuaEnv only
    Os        = 1 << 7,  // LuaEnv only
    Debug     = 1 << 8,  // LuaEnv only
    Package   = 1 << 9,  // LuaEnv only
    Safe = Coroutine | String | Table | Math | Utf8,
}
```

- **The base library is always open**, shaped as in section 4 for
  `LuaScript`.
- **`LuaScript` rejects** `Io`, `Os`, `Debug` and `Package` with
  `ArgumentException`; the core plugin does not contain them.
- **`ScriptDefaults`** proposed: `MemoryLimit = 64 MiB`, `TimeLimit = 1 s`,
  the rest as above. `new LuaOptions()` stays unlimited, so `LuaEnv` keeps
  today's behaviour.
- **`LuaEnv(LuaOptions)`** is a new constructor; `LuaEnv()` keeps calling
  `luaL_newstate` (`LuaEnv.cs:81`) until the bridge moves onto
  `xlua_core_open`.

## 3. Native ABI: `xlua_core.c`

### 3.1 Layers

```text
xlua_core.c        allocator, entry protocol, hooks, watchdog interface, trampoline,
                   library selection, sandbox, handles, raw table access, primitive values, seed
xlua_core_buffer.c the buffer library (only with LuaLibs.Buffer support compiled in)
xlua_core_nobc.c   core builds only: luaU_undump and luaU_dump stubs that replace lundump.c and ldump.c
xlua.c             legacy and minimal: the C# bridge, built on xlua_core.c
```

**What moves from `xlua.c` into `xlua_core.c`:**

| Today | Where | In core |
|---|---|---|
| `xlua_gc` (GC options across 5.4/5.5) | `xlua.c:51-68` | moves unchanged; `LuaScript.CollectGarbage` uses it |
| `luaL_openlibs` shim for 5.5 | `xlua.c:70-76` | replaced by `xlua_core_openlibs(L, mask)`; the shim stays for the bridge |
| C function trampoline with an error flag | `xlua.c:795-831`, `874-887` | generalised into the dispatcher trampoline (section 3.7) |
| Wrapper caller for `GEN_CODE_MINIMIZE` | `xlua.c:833-868` | same pattern: one process-wide function pointer and an id upvalue |
| `push_helper` for protected helpers | `xlua.c:253-266` | moves; every raising core export uses it |
| Error handler `errorfunc` | `xlua.c:707-715` | replaced by a C handler on `luaL_traceback` |
| Profiler hook | `xlua.c:733-793` | stays in `xlua.c`, but registers through core's hook owner (section 3.5), so the profiler and the limits share one `lua_Hook` |

The `lstate.h` include that `xlua.c` already has for PUC Lua
(`xlua.c:23-27`) moves too: core reads `hookcount` and `basehookcount`
(`lstate.h:329-331` in 5.4, `305-307` in 5.5).

The CMake list called `XLUA_CORE` today (`build/CMakeLists.txt:126-133`) holds
the bridge files; it is renamed `XLUA_BRIDGE_SOURCES` to avoid confusion. A new
`XLUA_PRESET` option (`legacy`, `minimal`, `core`) selects files and libraries.

### 3.2 Shared types

```c
enum { XLUA_T_NIL, XLUA_T_BOOL, XLUA_T_INT, XLUA_T_NUM, XLUA_T_STR,
       XLUA_T_TABLE, XLUA_T_FUNC, XLUA_T_BUFFER, XLUA_T_OTHER };

typedef struct xlua_value {        /* 16 bytes, mirrored by a C# struct */
  int32_t type;                    /* XLUA_T_* */
  int32_t len;                     /* bytes, for strings and buffers */
  union { int64_t i; double n; const char *s; } u;   /* i also holds bool and handle ids */
} xlua_value;

enum {                             /* status of every core export */
  XLUA_OK = 0, XLUA_YIELD = 1, XLUA_ERRRUN = 2, XLUA_ERRSYNTAX = 3, XLUA_ERRMEM = 4,
  XLUA_ERRERR = 5, XLUA_LIMIT_INSTR = 10, XLUA_LIMIT_TIME = 11, XLUA_INTERRUPTED = 12,
  XLUA_ERRTYPE = 20, XLUA_ERRSTACK = 21, XLUA_ERRHANDLE = 22
};

typedef struct xlua_core_config {
  int64_t  memory_limit;           /* 0 = unlimited */
  uint32_t seed; int32_t has_seed;
  int32_t  libs;                   /* LuaLibs bits */
  int32_t  sandbox;                /* 1 = core shape (section 4); 0 = LuaEnv's plain globals */
} xlua_core_config;

typedef struct xlua_core_call {    /* one top-level or nested entry */
  int32_t kind;                    /* XLUA_CALL_GLOBAL, _REF, _CHUNK, _COMPILE */
  int32_t env;                     /* environment handle; 0 = default */
  int32_t fn;                      /* function handle for _REF */
  const char *name; int32_t name_len;   /* global name (dotted) or chunk name */
  const char *code; int32_t code_len;   /* chunk text */
  const xlua_value *args; int32_t nargs;
  xlua_value *results; int32_t results_cap; int32_t nresults;   /* out */
  int64_t instruction_limit; int64_t time_limit_ns;
  int64_t instructions;            /* out: used by this call */
  uint32_t serial;                 /* out: entry serial for the watchdog */
  const char *error; int32_t error_len;  /* out: message and traceback, valid until the next export call */
  int32_t error_ref;               /* out: handle of a non-string error value, or 0 */
} xlua_core_call;

typedef struct xlua_core_frame {   /* one C# callback */
  lua_State *L;                    /* the thread the call runs on */
  const xlua_value *args; int32_t nargs;
  xlua_value *results; int32_t nresults; int32_t results_cap;
  int32_t flags;                   /* out: XLUA_FRAME_ERROR, XLUA_FRAME_YIELD */
  int32_t id;                      /* frame serial, for borrowed handles */
} xlua_core_frame;

typedef void (*xlua_core_dispatch)(void *host, int32_t fn_id, xlua_core_frame *frame);
```

Handle arguments (`int32_t t` below) are positive for owning handles (slots
in a registry table) and negative for borrowed values on the current frame's
stack. Strings passed in from C# are pinned for the duration of the call.

### 3.3 Exports

Every export is in one of two classes:

- **non-raising:** it cannot call `lua_error` or allocate;
- **protected:** it runs its body under `lua_pcall` through `push_helper`, as
  `xlua_pgettable` does today (`xlua.c:268-281`), and returns a status.

C# never calls a raising Lua API directly, so no `longjmp` ever crosses a
managed frame.

| Export | Class | Purpose |
|---|---|---|
| `int xlua_core_version(void)` | non-raising | ABI version, checked by `LuaScript` |
| `void xlua_core_set_dispatcher(xlua_core_dispatch fn)` | non-raising | process-wide, set once from a static constructor |
| `lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status)` | protected inside | allocator, seed, libraries, sandbox; NULL on failure |
| `void xlua_core_close(lua_State *L)` | non-raising | `lua_close`, then frees the core state |
| `void *xlua_core_of(lua_State *L)` | non-raising | the `xlua_core_t *` behind any thread of the state |
| `void xlua_core_set_memory_limit(lua_State *L, int64_t bytes)` | non-raising | |
| `void xlua_core_stats(lua_State *L, xlua_core_stats *out)` | non-raising | used, peak, last instruction count |
| `void xlua_core_interrupt(void *xc, uint32_t serial, int32_t reason)` | any thread | watchdog and `Interrupt()` (section 3.6) |
| `int xlua_core_gc(lua_State *L, int what, int data)` | protected | today's `xlua_gc` |
| `int xlua_core_call(lua_State *L, xlua_core_call *c)` | protected entry | load, compile, call (section 3.5) |
| `int xlua_core_start(lua_State *L, xlua_core_call *c, int32_t *co)` | protected entry | new thread plus first resume |
| `int xlua_core_resume(lua_State *L, int32_t co, xlua_core_call *c)` | protected entry | |
| `int xlua_core_thread_status(lua_State *L, int32_t co)` | non-raising | |
| `int xlua_core_thread_close(lua_State *L, int32_t co, xlua_core_call *c)` | protected entry | `lua_closethread` (`lua.h:166` in 5.4.9 and 5.5.1) |
| `int xlua_core_newenv(lua_State *L, int32_t *env)` | protected | section 4 |
| `int xlua_core_register(lua_State *L, int32_t env, const char *name, int32_t len, int32_t fn_id)` | protected | creates the trampoline closure |
| `int xlua_core_key(lua_State *L, const char *s, int32_t len, int32_t *key)` | protected | `LuaKey` |
| `int xlua_core_ref(lua_State *L, int32_t borrowed, int32_t *ref)` | protected | borrowed to owning |
| `void xlua_core_unref(lua_State *L, int32_t ref)` | non-raising | |
| `int xlua_core_newtable(lua_State *L, int32_t narr, int32_t nrec, int32_t *ref)` | protected | |
| `int xlua_core_tgeti(lua_State *L, int32_t t, int64_t i, xlua_value *out)` | non-raising | `lua_rawgeti` |
| `int xlua_core_tgetk(lua_State *L, int32_t t, int32_t key, xlua_value *out)` | non-raising | read through a `LuaKey` |
| `int xlua_core_tgets(lua_State *L, int32_t t, const char *k, int32_t len, xlua_value *out)` | protected | pushes the key string |
| `int xlua_core_tget(lua_State *L, int32_t t, const xlua_value *key, xlua_value *out)` | protected | |
| `int xlua_core_tseti(...)`, `xlua_core_tsetk(...)`, `xlua_core_tsets(...)`, `xlua_core_tset(...)` | protected | raw writes |
| `int64_t xlua_core_tlen(lua_State *L, int32_t t)` | non-raising | `lua_rawlen` |
| `int xlua_core_iter_open(lua_State *L, int32_t t, int32_t *iter)` | protected | |
| `int xlua_core_iter_next(lua_State *L, int32_t iter, xlua_value *key, xlua_value *val)` | protected | 1 = pair, 0 = end |
| `void xlua_core_iter_close(lua_State *L, int32_t iter)` | non-raising | |
| `int xlua_core_newbuffer(lua_State *L, int32_t len, int32_t *ref, void **data)` | protected | |
| `void *xlua_core_tobuffer(lua_State *L, int32_t t, int32_t *len)` | non-raising | |
| `void xlua_core_frame_error(xlua_core_frame *f, const char *utf8, int32_t len)` | non-raising | callback error message |
| `int xlua_core_frame_reserve(xlua_core_frame *f, int32_t nresults)` | non-raising | more result slots than the inline 8 |

Out values stay valid until the next export call on the same state: strings
point into Lua strings that the export leaves on the stack of the main thread
(or the frame's thread), and the next call clears them.

### 3.4 Allocator

```c
typedef struct xlua_core_t {
  size_t used, peak, limit;        /* bytes; limit 0 = unlimited */
  int unprotected;                 /* > 0 while C# runs inside a trampoline */
  int refused, overdraft, host_oom;
  /* ... entry, hook and watchdog fields (section 3.5) */
} xlua_core_t;

static void *core_alloc(void *ud, void *ptr, size_t osize, size_t nsize);
```

- **`osize` is a type tag when `ptr` is NULL** (`lua.h:125`; `lua_newstate`
  passes `LUA_TTHREAD`, `lstate.c` in 5.5:344). The old size is therefore
  `ptr ? osize : 0`.
- **Free** (`nsize == 0`): `free`, subtract, return NULL.
- **Shrink** (`nsize <= old`): never fails. Lua assumes that. If `realloc`
  returns NULL on a shrink, the old block is returned.
- **Grow over the limit:** when `used - old + nsize > limit`, the allocator
  returns NULL and sets `refused`. Lua then runs an emergency full collection
  and asks again (`lmem.c:162-170` in 5.4); the second request may succeed. If
  it also fails, Lua raises its preallocated "not enough memory" error
  (`LUA_ERRMEM`, which calls no message handler and allocates nothing). The
  entry reports `XLUA_ERRMEM`; with `refused` set, C# throws
  `LuaMemoryLimitException`.
- **Never refuse under a managed frame.** While a C# callback runs
  (`unprotected > 0`), a refusal would `longjmp` through IL2CPP frames, which
  is undefined behaviour (zlua reports SEH crashes from exactly this,
  focus-creative-games.md:82-83). The allocator then grants the request,
  sets `overdraft`, and the trampoline raises the memory error after C# has
  returned. Every protected export and nested entry saves `unprotected`, sets
  it to 0 for its body and restores it, so script code run from inside a
  callback is capped again. The overdraft is bounded by what one host
  callback allocates directly.
- **Error reserve:** while the message handler builds a traceback, the limit
  is raised by 16 KB, so a runtime error near the limit still gets its
  traceback instead of "error in error handling".
- **Bridge note:** this rule matters most for `LuaEnv`. Generated wrappers
  call `lua_pushstring` and friends unprotected inside callbacks
  (`LuaDLL.cs:300-330`), so a `MemoryLimit` on `LuaEnv` without the
  "never refuse under a managed frame" rule would turn a limit into a crash.
- **Accounting cost:** one compare and two additions per allocation. The
  default allocator (`luaL_alloc`) is replaced in every preset that sets a
  limit; without a limit the same function runs with `limit == 0`.

### 3.5 Entry protocol and instruction hook

Every C#-to-Lua call goes through `xlua_core_call`, `_start`, `_resume` or
`_thread_close`. Each does:

1. **Begin.** If this is the outermost entry (`depth == 0`): store the
   instruction budget, the deadline (monotonic clock plus `time_limit_ns`),
   bump `serial`, set `running` to the entry thread, clear `stop`, and arm the
   budget hook on the entry thread when a budget is set. A nested entry
   (from a callback) keeps the outer budget, deadline and serial.
2. **Run** the body under `lua_pcall` with the C message handler.
3. **End.** At depth 0: disarm the hook on the entry thread, clear `running`
   and `stop` under the lock (section 3.6), record the instruction count, and
   map the result to a status. The limit that tripped is stored in
   `stop_reason`, so the status does not depend on the error message.

**The budget hook** (`LUA_MASKCOUNT`):

- `lua_sethook(L, core_hook, LUA_MASKCOUNT, slice)` with
  `slice = min(remaining, 2^30)`, because the count is an `int`
  (`lua.h:468` in 5.4).
- When it fires, it subtracts `slice` from `remaining`. At zero it sets
  `stop = XLUA_LIMIT_INSTR`, arms the stop hook and raises. Otherwise it
  re-arms with the next slice.
- **Per-instruction cost:** with any count hook set, `luaV_execute` takes the
  `trap` path and calls `luaG_traceexec` on every instruction (`lvm.c:1146-1148`
  and `ldebug.c:928-939` in 5.4; `lvm.c:1191-1193`, `ldebug.c:936-947` in 5.5).
  That is the cost of `InstructionLimit`, to be measured with
  `Test/Native/benchmark.py`. Without a budget no hook is armed and the cost
  is zero.

**The stop hook** is the same function armed like `lua.c`'s SIGINT handler
(`lua.c:63-79` in 5.4.9): all masks, count 1. On every event it raises the
error for `stop_reason` again. The messages ("instruction limit exceeded",
"time limit exceeded", "interrupted") are strings created at `xlua_core_open`
and anchored in the registry, so raising allocates nothing. Because the hook
stays armed until the entry ends, `pcall`, `xpcall` handlers written in Lua
and `__close` handlers all fail again, and the error reaches C#.

**Coroutines inherit hooks only when they are created** (`lstate.c:306-309`
in 5.4, `291-294` in 5.5). A script that spreads a loop over many coroutines
would get a fresh count in each. Core therefore owns every thread switch:

- `xlua_core_resume` and `_start` from C#;
- `coroutine.resume`, `coroutine.wrap` and `coroutine.close`, which core
  replaces with its own versions (based on `lcorolib.c`'s `auxresume`, with
  the Lua copyright notice kept). `create`, `yield`, `status`, `running` and
  `isyieldable` stay as Lua ships them.

At each switch from thread A to thread B core:

1. charges A's use, `A->basehookcount - A->hookcount`, to `remaining`;
2. sets `running = B`;
3. arms B with the remaining budget, or with the stop hook if `stop` is set;
4. after `lua_resume` returns, charges B's use and re-arms A the same way.

This makes the budget global across coroutines and the count deterministic
for the same code, input and seed.

**Hooks that never fire:**

- **Finalizers.** Lua runs `__gc` with hooks off (`lgc.c:920-929` in 5.4,
  `977-986` in 5.5), so an endless loop in `__gc` would escape every limit.
  Core's `setmetatable` refuses metatables with a `__gc` field (section 4).
  Lua marks an object for finalization only when its metatable already has
  `__gc` at `setmetatable` time, so a later `__gc` field does nothing.
- **C functions.** The count hook sees VM instructions only. Section 3.11
  covers the long C paths.

**One hook owner.** The profiler (`xlua.c:778-793`) and the limits both need
`lua_sethook`. In presets that have both, core installs the only `lua_Hook`
and calls the profiler's callback from it when the profiler is on.

### 3.6 Time limit and watchdog

- **C# watchdog:** one background thread per process (`LuaWatchdog`),
  started on first use. Each `LuaScript` with a time limit owns a slot holding
  its `xlua_core_t *`, the entry serial and a deadline in `Stopwatch` ticks.
- **Entry begin:** two volatile writes (serial, deadline). The watchdog is
  pulsed only when the new deadline is earlier than its next wake, which is
  rare when every call uses the same `TimeLimit`. **Entry end:** one volatile
  write clears the deadline.
- **Expiry:** the watchdog calls `xlua_core_interrupt(xc, serial, TIME)`.
- **Native side of the interrupt,** under a spinlock in `xlua_core_t`:
  1. if `serial` is not the current entry's serial, return (a stale
     deadline of a finished call);
  2. set `stop` and `stop_reason`;
  3. call `lua_sethook(running, core_hook, all masks, 1)`.

  `lua_sethook` is documented as safe from a signal (`ldebug.c:124-133` in
  5.4).
- **The lock** closes two races. The owner thread takes the same lock at
  every switch (setting `running` and arming the next thread) and at entry
  end:
  - the watchdog arming a thread that has just finished and been collected;
  - the owner thread overwriting the stop hook with a budget hook.

  The lock is uncontended except during an interrupt. On WebGL it compiles
  to nothing.
- **Lifetime:** `LuaScript.Dispose` removes the slot under the watchdog's
  lock before `xlua_core_close`, so the watchdog never sees a freed state.
- **Native deadline as well:** the entry also stores a native monotonic
  deadline:
  - `clock_gettime(CLOCK_MONOTONIC)` on POSIX, Android, iOS and OpenHarmony;
  - `QueryPerformanceCounter` on Windows;
  - `emscripten_get_now()` on WebGL.

  The budget hook and the C budget (section 3.11) check it, so time is
  enforced even when the watchdog is late or absent.
- **WebGL fallback:** with a `TimeLimit` and no threads, core arms a count
  hook every 10,000 instructions only to read the clock. This costs the
  per-instruction trap described above; open question 9.
- **Slack:**
  - the watchdog's wake latency, typically a few milliseconds;
  - the longest stretch of C code between budget checks: 4,096 pattern steps
    or 4,096 moved elements (section 3.11), or one GC step;
  - a registered C# function: core never preempts it. The stop hits when it
    returns.

### 3.7 Callback trampoline

```c
static int core_trampoline(lua_State *L) {
  xlua_core_t *xc = upvalue 1 (light userdata);  int32_t fn_id = upvalue 2;
  xlua_core_frame f;  xlua_value inline_args[8], inline_results[8];
  /* 1. read arguments: no allocation, no error */
  for each i: type via lua_type; integers via lua_isinteger + lua_tointeger;
              strings via lua_tolstring on LUA_TSTRING values only (numbers are never converted);
              tables, functions, buffers as borrowed stack indices
  /* 2. call C# */
  xc->unprotected++;  g_dispatch(xc->host, fn_id, &f);  xc->unprotected--;
  /* 3. now no managed frame is on the C stack: raising is safe */
  if (xc->overdraft) raise the memory error;
  if (f.flags & XLUA_FRAME_ERROR) { push the message; return lua_error(L); }
  luaL_checkstack(L, f.nresults, NULL);
  push each result (may raise LUA_ERRMEM: still no managed frame);
  if (f.flags & XLUA_FRAME_YIELD) return lua_yield(L, f.nresults);
  return f.nresults;
}
```

- **Same idea as today's bridge:** C# records the error and returns, and the
  C side raises it after C# has returned (`xlua.c:795-816` with
  `xlua_csharp_str_error`, `xlua.c:874-880`, and `LuaDLL.cs:83-87`). The core
  version also keeps every Lua API call that can raise out of the managed part
  of the callback.
- **The C# dispatcher** is one static method with
  `[MonoPInvokeCallback(typeof(xlua_core_dispatch))]`, as IL2CPP requires. It
  resolves the `LuaScript` from the weak `GCHandle` and the delegate from
  `fn_id`, and wraps everything in `try`/`catch (Exception)`. No exception
  leaves it.
- **Allowed inside a callback:** only exports marked non-raising or protected.
  Nested entries (`xlua_core_call` from a callback) use `lua_pcall`, so their
  errors return as a status. A Lua function called from C# inside a callback
  cannot yield: Lua reports "attempt to yield across a C-call boundary".
- **Stack room:** Lua guarantees `LUA_MINSTACK` slots when a C function
  starts (bridge-audit.md:545-548). Results beyond that are covered by
  `luaL_checkstack` after C# returns, where raising is allowed.

### 3.8 Library selection

- **Core does not compile** `liolib.c`, `loslib.c`, `ldblib.c`, `loadlib.c` or
  `linit.c`. `linit.c` references every library through its static table
  (`linit.c:28-40` in 5.5, `42-54` in 5.4), so linking it would pull them all
  in.
- **`xlua_core_openlibs(L, mask)`** calls `luaL_requiref` for base and each
  selected `luaopen_*`, with `#if` guards for the libraries a build contains.
  The bit order follows Lua 5.5's `LUA_*LIBK` constants (`lualib.h:17-54`)
  where they overlap, so the bridge build can pass the same mask to
  `luaL_openselectedlibs` (`linit.c:46-62`).
- **The full presets** compile everything and use the same function, so
  `LuaEnv(LuaOptions { Libraries = ... })` works there too.
- **Build-time selection (done 2026-10-18):** the CMake option `XLUA_LIBS`
  (`all` by default) leaves the unselected libraries and `linit.c` out and
  compiles `build/xlua_libs.c`, which defines `luaL_openlibs` (5.5:
  `luaL_openselectedlibs` with the `LUA_*LIBK` bits) over the libraries the
  build contains ([build/README.md](../../build/README.md#standard-libraries)).
  `xlua_core_openlibs(L, mask)` can use the same table; core builds would set
  `XLUA_LIBS` to the `Safe` set. The run-time mask is not implemented.

### 3.9 Text-only loading, `dofile` and `loadfile`

- **C# entries** call `luaL_loadbufferx(L, code, len, name, "t")`. The
  bridge's `xluaL_loadbuffer` passes no mode and accepts binary chunks
  (`xlua.c:244-247`).
- **Script `load`** is wrapped: the mode is forced to `"t"` (a script that
  asks for `"b"` gets an error), and the default environment is the calling
  environment (section 4).
- **`string.dump`** is removed from the `string` table copied into each
  environment.
- **`dofile` and `loadfile`** are removed from the base table after
  `luaopen_base` (`lbaselib.c:509,513` in 5.4.9; `512,516` in 5.5.1).
- **Defence in depth:** `xlua_core_nobc.c` replaces `lundump.c` and `ldump.c`
  in core builds.
  - `luaU_undump` raises "binary chunks are not supported". The signature
    differs by version: `lundump.h:29` in 5.4, `lundump.h:33` in 5.5. It is
    called from `ldo.c:1024` in 5.4 and `ldo.c:1146` in 5.5.
  - `luaU_dump` returns an error (`lapi.c:1118` in 5.4, `1156` in 5.5).

  This removes about 10.6 KB on Android 5.5.1 (`lundump.c` 5,896 and
  `ldump.c` 4,680 bytes, SIZE.md:46-47) without touching the Lua sources.

### 3.10 Hash seed on 5.4 and 5.5

- **Lua 5.5:** `lua_newstate` takes the seed (`lua.h:163`; stored at
  `lstate.c:359`). Core passes `cfg->seed` or `luaL_makeseed(NULL)`
  (`lauxlib.c:1187-1190`), as `luaL_newstate` does (`lauxlib.c:1198`).
- **Lua 5.4:** `lua_newstate` has no seed parameter (`lua.h:163`). It calls
  the `luai_makeseed(L)` macro (`lstate.c:388`), which is only defined when
  the build has not defined it (`lstate.c:58-79`). The core build defines it
  as `xlua_core_makeseed(L)`, which:
  - reads the core state from `G(L)->ud` (set at `lstate.c:384`, before the
    seed);
  - returns the configured seed, or the same time-and-address mix as
    upstream.

  The macro needs a prototype, so `luaconf.h.in` gets one guarded line that
  includes `xlua_luahooks.h` under `XLUA_LUA_HOOKS`, recorded in
  `LUA_PATCHES.md`. The value is therefore chosen at run time on 5.4 as well;
  presets.md:105-106 says it is set at build time, and this design corrects
  that.
- **What the seed fixes:**
  - string hashes, and with them `next`/`pairs` order for string and number
    keys given the same insertion history;
  - in 5.5 also the border that `#t` finds in a table with holes, because
    `hash_search` mixes in the seed (`ltable.c:1235-1241`).
- **What it does not fix:**
  - order for table, function or userdata keys, which hash by address;
  - `math.random`, which seeds itself: from the time in 5.4
    (`lmathlib.c:628-661`), from `luaL_makeseed` in 5.5 (`lmathlib.c:635,659`).
    Scripts call `math.randomseed`.

### 3.11 The C budget: `lstrlib.c` and `ltablib.c` patch

The hook cannot stop a long C function. Two kinds are unbounded by memory
alone:

- **Patterns:** `match()` recurses and backtracks.
  `("a"):rep(1e5):find(".-.-.-.-b")` runs for minutes. The existing
  "pattern too complex" exit (`lstrlib.c:570-572` in 5.4, `572-574` in 5.5)
  shows that raising from inside `match()` is safe.
  - Plain `find` and patterns without specials go through `lmemfind`
    (`lstrlib.c:673-690` in 5.4, `675-692` in 5.5), which is O(n·m) with
    `memchr` plus `memcmp`.
- **`table.move`:** `table.move({}, 1, 1 << 62, 2)` loops in C over nils that
  allocate nothing (`ltablib.c:115-140` in 5.4, `128-153` in 5.5). The only
  guard is "too many elements to move".

**Patch** (both versions, about 20 lines each, recorded in `LUA_PATCHES.md`):

```c
/* lstrlib.c */
#if !defined(luai_budget)
#define luai_budget(L,n)	((void)0)
#endif
#define BUDGET_TICK	4096
/* MatchState gets: unsigned int tick;  prepstate sets it to 0 */
/* at the top of match() and at its 'init:' label: */
if (l_unlikely(++ms->tick >= BUDGET_TICK)) { ms->tick = 0; luai_budget(ms->L, BUDGET_TICK); }
/* lmemfind gets an L parameter and charges l2 bytes per candidate, BUDGET_TICK at a time */

/* ltablib.c tmove: one check per BUDGET_TICK elements */
```

- **`luai_budget(L, n)`** is defined in `xlua_luahooks.h` as
  `xlua_core_budget(L, n)`:
  1. find the core state through `lua_getallocf(L, &ud)`; if the allocator
     is not core's, return;
  2. charge `n` to the instruction budget;
  3. check `stop` and the native deadline;
  4. raise with the prebuilt message when over.

  One call per 4,096 steps keeps the cost negligible. A Lua build without
  the hooks header compiles the macro to nothing.
- **Remaining C paths that the budget does not split:**
  - `table.sort`: O(n log n) with a randomised pivot;
  - `table.concat`, `string.rep`, `string.format` and `utf8.*`: linear in
    sizes that `MemoryLimit` bounds;
  - a full garbage collection: proportional to the heap, so to `MemoryLimit`.

  The security statement lists them.

### 3.12 Existing exports core reuses

Core imports no bridge export. From the current export set it reuses:

- **The Lua C API**, non-raising calls only:
  - `lua_gettop`, `lua_settop` (on the frame's thread, to drop out values);
  - `lua_type`, `lua_toboolean`, `lua_rawlen`;
  - `lua_checkstack` (`lapi.c:111-123`: it grows the stack with
    `raiseerror = 0`).

  Everything that can raise goes through the core exports above.
- **Bridge code that moves:** `xlua_gc` and the trampoline, wrapper-caller
  and `push_helper` patterns (section 3.1).
- **Not reused:**
  - `luaL_newstate` and `luaL_openlibs` (`LuaDLL.cs:93,102`): no allocator
    hook and no library choice;
  - `xluaL_loadbuffer`: accepts bytecode;
  - `errorfunc`: needs the global `debug`;
  - `lua_atpanic`: core sets a native panic that aborts with a message,
    because every entry is protected and a panic means a core bug;
  - `luaopen_i64lib` (`LuaEnv.cs:85`): 64-bit integers are native.

The export list of the core plugin is computed from `Assets/XLua/Core` with
`Test/Native/managed_imports.py`, as presets.md:57-60 plans for every preset.

## 4. Sandbox shape

Modelled on Luau's `luaL_sandbox` and `luaL_sandboxthread`
(luau.md:44-50), within what Lua 5.x allows. Lua 5.x has no read-only
tables, so the base is kept out of reach rather than marked read-only.

- **Frozen base `B`:** one table holding the base functions and registered
  host functions. No script ever gets a reference to it: `_G` is not in it,
  every environment's metatable is locked, and `load` never defaults to it.
  `LUA_RIDX_GLOBALS` points to `B`, but nothing a script can call loads a
  chunk with the registry globals.
- **Environment `E`:** a fresh table per environment
  (`Globals`, `NewEnvironment()`, each `require`d module). It has
  `setmetatable(E, { __index = B, __metatable = false })`, `E._G = E`, its
  own `load` closure bound to `E`, and shallow copies of the library tables.
  A script that writes `string.format = nil` changes only its own copy.
- **The string metatable** keeps `__index` pointing to the original `string`
  table and gets `__metatable = false`. `("x"):upper()` therefore uses the
  originals, and `getmetatable("")` returns `false`.
- **Per-environment copies** cost about 3–4 KB per environment (about 70
  library functions). The alternative, read-only proxy tables, costs nothing
  per environment but makes `next(string)` and `rawget(string, k)` see an
  empty table and adds a lookup per library access (open question 3).
- **`require`** exists only when `ModuleResolver` is set. It asks the
  resolver for text, loads it with mode `"t"` into a fresh environment, and
  caches the result per `LuaScript`. Circular requires get the usual Lua
  error.

**Base functions:**

| Kept as Lua ships them | Wrapped | Removed |
|---|---|---|
| `assert`, `error`, `getmetatable`, `ipairs`, `next`, `pairs`, `pcall`, `rawequal`, `rawget`, `rawlen`, `rawset`, `select`, `tonumber`, `tostring`, `type`, `xpcall`, `_VERSION` | `setmetatable` (refuses a metatable with `__gc`, section 3.5); `collectgarbage` (`"count"` only, as Luau, luau.md:211-216); `load` (mode `"t"`, environment defaults to the caller's) | `dofile`, `loadfile`, `print`, `warn` |

`print` is absent so output goes where the host decides
(`lua.Register("print", ...)`, open question 7). `warn` is a no-op without a
warning function, since `lua_newstate` sets none (`lstate.c:385-386` in 5.4),
so it is removed rather than left silent.

**Libraries:**

| Library | In core | Notes |
|---|---|---|
| `coroutine` | `LuaLibs.Coroutine` | `resume`, `wrap` and `close` replaced by core's switch-aware versions |
| `string` | `LuaLibs.String` | no `dump`; patterns under the C budget |
| `table` | `LuaLibs.Table` | `move` under the C budget |
| `math` | `LuaLibs.Math` | unchanged |
| `utf8` | `LuaLibs.Utf8` | unchanged |
| `buffer` | `LuaLibs.Buffer`, opt-in | section 2.10 |
| `io`, `os`, `debug`, `package` | not built | a host that wants `os.time` registers its own |

Allowed metatable features: `__index`, `__newindex`, arithmetic and
comparison events, `__call`, `__close`, `__mode` (weak tables), `__name`,
`__tostring`. Refused: `__gc`.

## 5. Security statement (draft)

To be published as a "LuaScript" section of
[Security.md](../../Assets/XLua/Doc/Security.md), modelled on Luau's
SECURITY.md (luau.md:79-84). Draft text:

> **Scope.** `LuaScript` runs Lua 5.4 or 5.5 source text that you do not
> trust, on the core plugin or on the full plugin of a project that also uses
> `LuaEnv`. `LuaEnv` itself is not a sandbox and is out of scope; see the
> sections above.
>
> **We treat these as security bugs.** Report them privately. A script loaded
> as text through `LuaScript`, under any `LuaOptions`, that:
>
> 1. corrupts memory, crashes the process or triggers undefined behaviour in
>    native code;
> 2. reads or writes files, the network, other processes or memory outside
>    its Lua state;
> 3. calls a C# method the host did not register, or reaches a C# object;
> 4. makes the state hold more than `MemoryLimit` plus 16 KB, plus what one
>    host callback allocates;
> 5. keeps running more than 4,096 instruction-equivalents past
>    `InstructionLimit`, or past `TimeLimit` by more than the watchdog
>    latency and one listed C path;
> 6. makes a C# call that hit an instruction, time or interrupt limit return
>    normally;
> 7. changes what another environment of the same `LuaScript` sees: the
>    frozen base, another environment's tables, or the string metatable;
> 8. loads or produces bytecode.
>
> **Not covered:**
>
> - **No termination guarantee without limits.** With `InstructionLimit` and
>   `TimeLimit` off, a script can loop forever.
> - **Listed C paths.** `table.sort`, `table.concat`, `string.rep`,
>   `string.format` and `utf8` run in C for a time proportional to their
>   input, which `MemoryLimit` bounds. A full garbage collection takes time
>   proportional to the heap.
> - **Host functions.** What a registered C# function does with its
>   arguments, including how long it runs and how much it allocates, is the
>   host's responsibility.
> - **Side channels.** `tostring` of tables shows addresses; timing is
>   observable.
> - **Other builds.** LuaJIT, Lua 5.3, modified builds, builds without the
>   `lstrlib.c` budget patch, and `LuaEnv` in any preset.
> - **Lua itself.** Bugs in PUC Lua are in scope for reports. The fix depends
>   on upstream or on a local patch to the pinned version.
>
> **Supported versions:** the latest release of xlua-ng.

## 6. Size budget and how to verify it

**Native, Android arm64**, from the per-file sizes in
[SIZE.md](../../build/SIZE.md) (Lua 5.5.1, current flags):

| Part | Stripped bytes | gzip -9 | Source |
|---|---|---|---|
| Lua core without `lundump.c` and `ldump.c` | 173,199 | 85,916 | SIZE.md:34-53 minus lines 46-47 |
| `lstrlib`, `lauxlib`, `lbaselib`, `lmathlib`, `ltablib`, `lutf8lib`, `lcorolib` | 61,365 | 28,896 | SIZE.md:57-69 |
| `xlua_core.c`, `xlua_core_nobc.c` | about 10,000 | about 4,000 | estimate: `xlua.c` is 19,952 (SIZE.md:78) and core has no indexers or struct packing |
| `xlua_core_buffer.c` (opt-in) | about 5,000 | about 2,000 | estimate |
| linker sections: relocations, `.rodata`, symbols for about 60 exports instead of 280, unwind index, PLT, headers | about 45,000 | about 15,000 | scaled from SIZE.md:101-112 |
| **total, current flags** | **about 290,000–295,000** | **about 134,000** | |
| **total with the FLAGS.md hybrid flags** | **about 240,000–250,000** | **about 115,000** | the full library saved 72,176 bytes, 17% (SIZE.md:267) |

**iOS arm64:** the same files in the UnityFramework link map add up to about
216 KB: Lua core 152,276, the seven libraries 53,951 (SIZE.md:34-69, iOS
column), and about 10 KB for core.

**Budget:** Android arm64 stripped `libxlua.so` (core, Lua 5.5.1, release
flags) at most **290 KB** and **125 KB gzip**; a CI check fails if a change
grows it by more than 2% without updating the budget.

**C#:** about 2,500 lines in `Assets/XLua/Core`.

- **Estimate:** 60–150 KB of IL2CPP code plus 20–40 KB of metadata. For
  comparison, the full runtime adds 1.4–1.9 MB of IL2CPP code
  (SIZE.md:173).
- **Budget:** the whole core preset adds at most **200 KB to an APK**
  (native compressed plus IL2CPP code and metadata), against 1.36 MB for
  full xlua-ng today.

**Verification:**

- **`size_report.py` variants** `core-5.5` and `core-5.4`, added to
  `Test/MobileSmoke/size_report.py`:
  - they copy the whole package (`Core`, `Src`, `Resources`, `Editor`, as a
    user installs it) and set `XLUA_CORE_ONLY` in Player Settings;
  - they take the plugin from `plugin_lua55_core` (or `_lua54_`), built by the
    `make_*` scripts with `-DXLUA_PRESET=core`;
  - they run a new `SizeProbeCore.cs` that does what `SizeProbe.cs` does
    without the bridge: a `LuaScript`, a chunk, one registered C# function
    called from Lua, one `Call` from C#, one table read, one coroutine
    resume.

  The report shows the delta against `baseline` and against `ng-5.5`, on
  Android and iOS; `core-5.5+srcplugin` measures the source-plugin route.
- **No bridge code in a core-only player.** A check on the `core-5.5` APK
  and `.app` (a new `core_only_check.py` next to `size_report.py`) fails if:
  - `ScriptingAssemblies.json` in the player data lists `XLua.dll`;
  - `global-metadata.dat` contains a type of the `XLua` assembly, such as
    `ObjectTranslator`, `LuaEnv` or `StaticLuaCallbacks`;
  - the stripped `libil2cpp.so` symbols or the iOS link map contain
    `XLua_ObjectTranslator` or other `XLua` assembly methods;
  - `libxlua.so` exports a bridge symbol (`xlua_pushcsobj`, `luaopen_xlua`).
- **The stripping question.** A third variant, `core-5.5+nodefine`, installs
  the whole package without `XLUA_CORE_ONLY`; its probe uses only
  `LuaScript`. The same check, run on that player, answers whether Unity's
  Minimal managed stripping drops the unreferenced `XLua` assembly by itself.
  Run it at stripping levels Minimal and High, on 2021.3 and 6000.3. Without
  the define the selector ships the full native plugin, so this variant
  measures the managed side only.
- **Both in one player.** A `mixed-5.5` variant runs `SizeProbe.cs` and
  `SizeProbeCore.cs` together, without the define. Its delta over `ng-5.5` is
  what `LuaScript` costs a game that already ships the bridge.
- **`size_breakdown.py`** on the lld map gives the per-file table above, so
  the estimates are replaced by measurements.
- **`check_exports.py --preset core`** checks that the plugin exports
  exactly what `managed_imports.py` finds in `Assets/XLua/Core`, and that
  none of `luaopen_io`, `luaopen_os`, `luaopen_debug`, `luaopen_package`,
  `luaU_undump` (the real one) or LuaSocket is linked.

## 7. Testing plan

Rules from AGENTS.md apply: one native-test or Unity process at a time,
every run with a timeout, and the process group killed after a timeout. A
hang is a finding.

### 7.1 Harness

- **Host suite:** `Test/Core/` holds the C# tests, compiled with `mcs`
  against `Assets/XLua/Core` and run under Mono and .NET by a new
  `Test/Native/run_core.py`. It takes `--plugin` like `run_managed.py`, has a
  hard per-run timeout that kills the child, and an `EXPECTED_TESTS` count.
- **C smoke:** `Test/Native/core_smoke.c` drives the `xlua_core_*` exports
  from C, like `plugin_smoke.c`, for cross-compiled targets
  (`ohos_check.sh`).
- **Fuzz:** `Test/Fuzz/fuzz_core.c` runs random chunks (reusing
  `fuzz_lua.c`'s corpus and `lua.dict`) under a 1 MB memory limit and a
  100,000-instruction limit. Its oracles:
  - no crash under ASan and UBSan;
  - `used <= limit + 16 KB` after every allocation;
  - every entry returns.

  A libFuzzer timeout is a limit bug.
- **Sanitizers:** `sanitize.sh` gets `VERSIONS=core-5.4,core-5.5`.

### 7.2 Unit tests per API

- **Lifecycle:** create and dispose; double dispose; finalizer closes a
  leaked script; version mismatch throws; `Dispose` inside a callback throws.
- **Load:**
  - text runs;
  - a `"\27Lua"` chunk throws `LuaSyntaxException`;
  - script `load(s, n, "b")` errors and `load(string.dump(f))` is impossible
    (`dump` is absent);
  - chunk names show as `name:line`;
  - `Compile` without running;
  - `Load<T>` returns the first result.
- **Call:**
  - each argument shape;
  - each `T`, with integer/float conversions and range errors;
  - dotted names;
  - missing function;
  - with `alloc_probe.py`-style counters: zero managed allocations for
    `Call<long>(name, long, double, bool)`.
- **Multiple returns:** fewer, equal and more results than the span; tuple
  forms.
- **Register:**
  - every getter, with wrong types giving the standard "bad argument"
    message;
  - `GetUtf8` and `ArgEquals` allocate nothing;
  - borrowed tables throw after return;
  - `Retain`;
  - `Return` of each type;
  - exceptions become Lua errors, with `InnerException` preserved;
  - nested `Call` inside a callback;
  - typed shorthands do not box;
  - `Unregister`;
  - dotted registration copied per environment;
  - 20 results (stack growth after return).
- **LuaTableRef:**
  - typed get and set by string, integer, `LuaKey` and `LuaValue`;
  - fallbacks and `TryGet`;
  - missing keys;
  - `Length` with and without holes;
  - `Pairs` over 10,000 keys with zero allocations;
  - adding a key during `Pairs` throws;
  - nesting and disposal;
  - generations: a copy of a disposed handle throws;
  - all handles invalid after script disposal;
  - `LiveHandles`;
  - `ToDictionary` on cycles and depth.
- **Buffer:**
  - the Luau function list with bounds errors;
  - little-endian layout;
  - `AsSpan` sees Lua writes;
  - `CopyFrom` is seen by Lua;
  - a locked metatable.
- **Sandbox:**
  - `io`, `os`, `debug`, `package`, `dofile`, `loadfile`, `print` and `warn`
    are absent;
  - `getmetatable("")` is `false`;
  - `_G == _ENV`;
  - an environment's `string.format = nil` does not reach another
    environment or `("x"):format`;
  - `load` without an environment writes into the caller's environment, not
    the base;
  - `setmetatable({}, {__gc = f})` errors;
  - `collectgarbage("collect")` errors and `"count"` works;
  - `require` with and without a resolver, and its caching.
- **Errors:**
  - the exception type per failure kind;
  - a traceback is present;
  - `error({code = 5})` keeps the table;
  - a C# exception round trip;
  - an error inside the message handler near the memory limit still gives a
    traceback (the reserve).
- **Both APIs in one process** (full plugin): a `LuaEnv` and two
  `LuaScript`s side by side; the scripts see no `CS`, `xlua`, `io`, `os`,
  `debug` or `package`; bytecode is refused on the full plugin as well
  (the stubs are absent there); limits on one state do not affect another;
  a `LuaEnv` callback that calls into a `LuaScript` works.
- **Threads:**
  - use from another thread throws with `CheckThread`;
  - two scripts on two threads run in parallel;
  - `Interrupt()` from another thread.
- **Seed:**
  - with `HashSeed = 42`, `pairs` order over string keys is the same across
    two states and two process runs;
  - different seeds give a different order (probabilistic, over 64 keys);
  - in 5.5, `#t` on a holey table is stable for a fixed seed.

### 7.3 Limit tests

**Memory:**

- a string growth loop, a table growth loop and `("x"):rep(1 << 30)`, each
  raising `LuaMemoryLimitException` with `MemoryUsed <= MemoryLimit + 16 KB`;
- a script that catches the memory error with `pcall`, frees memory and
  finishes normally;
- the emergency GC retry succeeding (garbage present) raises no error;
- a callback that allocates over the limit (overdraft) raises after it
  returns, without a crash, under the APICHECK build;
- lowering `MemoryLimit` below the current use;
- `MemoryPeak`.

**Instructions:**

- `while true do end`;
- a loop that swallows the error with `pcall` in a loop;
- an `xpcall` with a Lua handler;
- a `__close` handler that loops;
- a loop spread over 10,000 coroutines (`coroutine.wrap` per step), which
  must hit the global budget;
- an exact count: `LastInstructionCount` for a fixed function is identical
  across 100 runs and both seeds;
- a budget above 2^31 (slicing);
- a coroutine left suspended with a stale budget hook, then resumed with no
  limit, runs to the end.

**Time:**

- `while true do end` with only `TimeLimit` (watchdog path);
- the same with the WebGL fallback forced by a test define;
- a deadline that expires inside a coroutine started from Lua (the watchdog
  arms the running thread);
- the stale-serial race: back-to-back calls with tiny limits, 10,000
  iterations;
- `Interrupt()` with nothing running is a no-op;
- a long C# callback is not interrupted, but the script stops right after it
  returns.

**C budget:**

- `("a"):rep(1e5):find(".-.-.-.-b")`, `gsub` and `gmatch` with the same
  pattern;
- worst-case plain `find` (`("a"):rep(1e6):find(("a"):rep(5e5) .. "b", 1, true)`);
- `table.move({}, 1, 1 << 62, 2)`.

Each must stop within its limit, under the instruction limit and under the
time limit alone. Normal pattern results do not change: run the string
sections of the upstream Lua test suite (`pm.lua`, `strings.lua` from the
matching `lua-5.4.x-tests` and `lua-5.5.x-tests`, pinned and hashed per
AGENTS.md), with and without the patch.

**Cost:** `benchmark.py` with no limits, each limit alone and all three. The
no-limit numbers must equal today's within noise; the others are recorded in
`build/SIZE.md` or a new `build/LIMITS.md`.

### 7.4 Coroutine tests

- `Start`, `Resume`, `IsYielded`, `ValueCount` and values per yield;
- a yield from a nested Lua function and across `pcall`;
- `LuaCall.Yield()` from C#;
- `Yield()` outside a coroutine throws;
- resume values arrive as `coroutine.yield` results;
- an error in a coroutine makes it `Faulted`;
- `Resume` after finish throws;
- `Close` runs pending `__close` handlers;
- limits apply per resume;
- the `IEnumerator` driver, stepped by a test loop with no Unity;
- the `Task` driver with a test single-thread `SynchronizationContext`, a
  handler that uses `ConfigureAwait(false)`, and cancellation;
- `Awaitable` in the Unity editor suite only.

### 7.5 luban `lua-lua` fixture

- **Source:** luban v5.1.0 (`029f714`, focus-creative-games.md:8) and
  luban_examples' design tables.
- **Getting the output:** cloned repositories are untrusted (AGENTS.md:
  "read them; don't run their build scripts"), so the generated output is
  produced once on the owner's machine from the pinned release and committed
  under `Test/Core/Fixtures/luban-lua-lua/`. A `SOURCE.md` next to it gives
  the commit, the command line, the hashes and the licences. Open question 10.
- **The test:**
  - loads every table file through `LuaScript` with `MemoryLimit = 16 MB`
    and `InstructionLimit = 50,000,000`, using a `ModuleResolver` over the
    fixture folder;
  - checks record counts and a sample of fields against a JSON export of the
    same data, also committed;
  - reports `MemoryPeak`, `LastInstructionCount` and load time.
- **To check when the output is generated:** whether luban's Lua loader uses
  `setmetatable` with `__gc` or globals across files. Either would need a
  documented workaround.

### 7.6 Lua versions and CI

- **Versions:** every core test runs on Lua 5.4.9 and 5.5.1.
  - The patch and seed tests also run on the `legacy` build of 5.4 and 5.5,
    because the limits and the patch apply to every preset.
  - LuaJIT and 5.3 builds must fail to build `XLUA_PRESET=core` with a clear
    CMake message.
- **CI** (`native-build.yml`, manual while the repository is private):
  - a `preset: [legacy, core]` matrix axis for Lua 5.4 and 5.5 on Linux,
    macOS and Windows: build, `check_exports.py --preset core`,
    `run_core.py`, `core_smoke.c`;
  - the sanitizer job adds the core versions and 60 seconds of `fuzz_core`;
  - the size check runs on the Android build of the core plugin;
  - an editor-level check (no player build) imports the package into an
    empty project with `XLUA_CORE_ONLY` and confirms that `XLua.dll` and
    `XLua.Editor.dll` are not compiled and that a `LuaScript` sample builds;
  - `unity-matrix.yml` runs the core tests as an editor test assembly on
    2021.3 and 6000.3;
  - mobile `size_report.py` runs stay manual, with the owner's approval.

## 8. Work breakdown

In the order of presets.md:198-218: step 3 ("`LuaOptions` limits and
`xlua_core.c`") is milestones 1–4, step 5 ("Core preset and `LuaScript`") is
5–11. Sizes are working days for one agent, including tests.

| # | Milestone | Done when | Size |
|---|---|---|---|
| 0 | Owner answers the open questions | section 9 resolved in this file | 0.5 d |
| 1 | `xlua_core.c` split: move `xlua_gc`, `push_helper`, the trampoline pattern and the error handler; `XLUA_PRESET` in CMake; rename `XLUA_CORE` | the full suite passes unchanged in all three binding modes; sizes unchanged | 2 d |
| 2 | Allocator, `MemoryLimit`, the overdraft rule, `HashSeed` on 5.4 (luaconf hook) and 5.5; `LuaEnv(LuaOptions)` | memory and seed tests pass through `LuaEnv`; APICHECK run clean | 3 d |
| 3 | Entry protocol, budget hook, stop hook, switch accounting, `Interrupt`, watchdog, native deadline, WebGL fallback | instruction and time tests pass on 5.4 and 5.5; cost measured | 5 d |
| 4 | `lstrlib.c` and `ltablib.c` budget patch, `LUA_PATCHES.md`, upstream `pm.lua`/`strings.lua` | C budget tests pass; pattern results unchanged | 2 d |
| 5 | Core native sandbox: `xlua_core_openlibs`, frozen base, environments, wrapped `load`/`setmetatable`/`collectgarbage`/`coroutine`, `require`, `xlua_core_nobc.c` | `core_smoke.c` and the sandbox tests pass; `check_exports.py --preset core` | 3 d |
| 6 | C# `LuaScript`: the `XLua.Core` and `XLua` asmdefs with the `XLUA_CORE_ONLY` constraint, the plugin selector in `XLua.Core.Editor`, the `LuaDLL.cs` split, `LuaValue`, `Load`, `Call`, multiple returns, exceptions, thread check | API tests for these pass; allocation probe shows zero for primitive calls; the editor-level core-only check passes | 6 d |
| 7 | `Register`, dispatcher, `LuaCall`, typed shorthands, host `Yield` | Register tests pass under APICHECK and ASan | 3 d |
| 8 | `LuaTableRef`, `LuaKey`, iteration, `LuaFunctionRef`, `ToDictionary` | table tests pass; `Pairs` allocates nothing | 3 d |
| 9 | `LuaCoroutine` and the three drivers | coroutine tests pass; editor suite runs `Awaitable` | 3 d |
| 10 | `buffer` library and `LuaBufferRef` | buffer tests pass | 2 d |
| 11 | Fuzz target, luban fixture, CI wiring, `size_report.py` variants (`core`, `core+nodefine`, `mixed`), `core_only_check.py`, device size runs, Security.md section, user guide page | budgets met on Android and iOS; no `XLua` assembly code in a core-only player; stripping question answered in SIZE.md; docs published | 6 d |

Total: about 38 working days, roughly eight weeks for one agent. Milestones
2–4 also deliver the limits for `legacy` and `minimal`.

### 8.1 Milestone 1 as implemented (2026-10-18)

- **Files.** `build/xlua_core.c` holds, moved unchanged from `xlua.c`:
  `xlua_gc`; the C# function trampolines with the deferred error flag
  (`csharp_function_wrap`, `xlua_push_csharp_function`, `xlua_upvalueindex`,
  `xlua_csharp_str_error`, `xlua_csharp_error`); the `GEN_CODE_MINIMIZE`
  wrapper caller (`xlua_set_csharp_wrapper_caller`,
  `xlua_push_csharp_wrapper`); and the error handler (`errorfunc`,
  `get_error_func_ref`). `load_error_func` and `pcall_prepare` stay in
  `xlua.c`: they are bridge call helpers, not the handler.
- **`push_helper`** is a `static inline` function in the new internal header
  `build/xlua_core.h`, not a function in `xlua_core.c`. FLAGS.md ("`xlua.c` at
  `-O3`") found that `xlua_pgettable` loses speed on armeabi-v7a when
  `push_helper` is called out of line, so both files keep it inlined.
- **Profiler.** The trampolines report a C# function's return to the
  `xlua.sethook` profiler through `xlua_profiler_return` (the former
  `call_ret_hook`, still in `xlua.c`). It is declared `XLUA_INTERNAL` in
  `xlua_core.h`: hidden visibility on ELF and Mach-O, so the export list does
  not change. Milestone 3's hook owner replaces this link; until then a
  core-only build could not link the trampolines, which is one reason
  `XLUA_PRESET=core` is not accepted yet.
- **The error handler is moved, not replaced.** `errorfunc` still calls the
  global `debug.traceback`, so `LuaEnv`'s error messages are unchanged. The C
  handler on `luaL_traceback` (section 2.12) comes with the entry protocol
  (milestone 3), which is its first user.
- **CMake.** `XLUA_CORE` is now `XLUA_BRIDGE_SOURCES`; `XLUA_CORE_SOURCES`
  (`xlua_core.c`) and `XLUA_SOURCES` (what the preset compiles) are new, and
  the warning set, coverage instrumentation and size flags apply to
  `XLUA_SOURCES`. `XLUA_PRESET` takes `legacy`, `minimal` or `core`; only
  `legacy` configures. `core` with LuaJIT or Lua 5.3 fails with its own
  message (section 7.6); `minimal` and `core` otherwise fail with "not
  implemented yet".
- **Hot list.** `xlua_core.c` is in `XLUA_HOT_FILES` with `xlua.c`. The
  trampolines run on every Lua-to-C# call and were compiled at `-O3` as part
  of `xlua.c` since the FLAGS.md decision; in the list, every moved function
  compiles to the same size as before (llvm-nm on arm64-v8a and armeabi-v7a,
  Lua 5.5.1). At `-Oz` the file would save 160 bytes stripped on arm64-v8a
  and 16 on armeabi-v7a (Lua 5.5.1). Milestone 2 adds the allocator, which
  runs on every allocation like `lmem.c`, also in the hot list.
  `Test/Native/flag_sweep.py` uses the same pair (`XLUA_HOT`) in
  `bestnwc23-xluaO3`.
- **Sizes**, Android API 23 release build (NDK r30, default flags), stripped
  bytes before (4e76aee) and after:

  | Lua | ABI | before | after | gzip -9 before | after |
  |---|---|---:|---:|---:|---:|
  | 5.5.1 | arm64-v8a | 371,960 | 371,960 | 158,463 | 158,514 |
  | 5.5.1 | armeabi-v7a | 249,256 | 249,256 | 139,331 | 139,356 |
  | 5.4.9 | arm64-v8a | 355,264 | 355,280 | 149,925 | 149,975 |
  | 5.4.9 | armeabi-v7a | 239,552 | 239,552 | 132,682 | 132,690 |

### 8.2 Milestone 2 as implemented (2026-10-18)

- **Exports** (`build/xlua_core.h` holds the structs):
  - `int xlua_core_version(void)`: 1.
  - `lua_State *xlua_core_newstate(const xlua_core_config *cfg, int32_t *status)`
    in place of section 3.3's `xlua_core_open` for now: allocator and seed
    only, nothing opened, so `LuaEnv` sets the state up as it does after
    `luaL_newstate`. `xlua_core_open` (libraries, sandbox, host) will build
    on it in milestone 5. `xlua_core_config` already has the `libs` and
    `sandbox` fields of section 3.2; they must be 0.
  - `void xlua_core_close(lua_State *L)`, for any state.
  - `int xlua_core_set_memory_limit(lua_State *L, int64_t bytes)` and
    `int xlua_core_get_stats(lua_State *L, xlua_core_stats *out)` return 0 for
    a state without the core allocator. The stats function is not called
    `xlua_core_stats`, because C cannot have a function and a typedef of the
    same name. `xlua_core_stats` has used, peak, limit, refusals, overdrafts,
    host failures and an `instructions` field for milestone 3.
  - A new status `XLUA_ERRCONFIG = 23`: the build cannot honour an option
    (LuaJIT: no core allocator; Lua 5.3, or a 5.4 build without the
    `luaconf.h` hook: no seed).
- **Allocator** as section 3.4, with these differences:
  - **The core struct is allocated through the same limit**, before
    `lua_newstate`, and counted in `MemoryUsed`. A limit too small for a
    state then fails cleanly with `XLUA_ERRMEM`, and the branch is testable.
  - **Shrinking calls `realloc` as Lua's own `luaL_newstate` allocator
    does.** If it failed (no supported C library does), the block and the
    count stay as they were and Lua sees NULL. The section 3.4 rule ("return
    the old block") was not adopted because its branch cannot be reached by
    any test.
  - **Lua 5.3 gets the allocator too**, so `MemoryLimit` works on 5.3;
    `HashSeed` does not.
- **The overdraft rule for `LuaEnv`.** C# calls the raising Lua API
  directly at top level and inside callbacks, so "unprotected" is the
  default: the counter starts at 1 when no protected entry is active.
  - The protected entries are the exported `lua_pcall` (Lua 5.3+, which
    also serves `xlua_pgettable` and the other protected helpers) and
    `xluaL_loadbuffer`. They save the counter, the overdraft flag and the
    reserve, clear them for the body and restore them afterwards (inline,
    `xlua_core.h`).
  - The two C# function trampolines increment the counter around the call.
  - After the call, with the counter back at 0, an overdraft first runs
    `lua_gc(LUA_GCCOLLECT)`, as Lua retries after an emergency collection,
    and raises `LUA_ERRMEM` (`luaD_throw`) only if the state is still over
    the limit. Without the collection, garbage that one large C# result left
    over the limit failed every later C# call that allocated. The memory
    error wins over an error the C# function recorded; the error flag is
    reset either way.
  - A refusal inside Lua code that C# reached without an entry (`lua_gc`
    with Lua `__gc` handlers, raw API calls with metamethods) is granted:
    conservative, never a crash, but not capped.
- **Error reserve:** `errorfunc` (the bridge's handler, still on
  `debug.traceback`) raises the limit by 16 KB while it runs; a failing
  handler leaves the restore to the enclosing entry.
- **`HashSeed`:** 5.5 passes it to `lua_newstate`; 5.4 reads it in
  `xlua_core_makeseed` through `build/xlua_luahooks.h`, which the vendored
  `luaconf.h.in` includes under `XLUA_LUA_HOOKS` (`LUA_PATCHES.md`). Without
  a configured seed, 5.4 keeps upstream's time-and-address mix.
- **C#:** `LuaOptions` (`Assets/XLua/Src/LuaOptions.cs`, moves to Core in
  milestone 6) has only `MemoryLimit` and `HashSeed`; the instruction and
  time limits, `Libraries`, `CheckThread` and `ScriptDefaults` come with
  their milestones. `LuaEnv(LuaOptions)` sets the limit when its constructor
  has finished, so the bridge's own setup never fails on it; `MemoryLimit` (settable), `MemoryUsed` and `MemoryPeak` are on
  `LuaEnv`. A memory error reaches C# as `LuaException("not enough
  memory")`; `LuaMemoryLimitException` (section 2.12) needs the entry
  statuses of milestone 3.
- **Tests:** `check_core_state` and `check_core_host_oom` in
  `native_regression.py`, the five `CaseLuaOptions*` managed cases (all
  binding modes, Lua 5.3 to 5.5 and LuaJIT); the reserve case fails with a
  reserve of 0.
- **Sizes**, Android API 23 release build, Lua 5.5.1, main at bf0fb21 against
  this branch (milestones 1 and 2): arm64-v8a 372,216 to 375,128 bytes
  stripped (+2,912; gzip -9 158,587 to 159,782), armeabi-v7a 249,472 to
  251,232 (+1,760; gzip 139,383 to 140,470). Lua 5.4.9: arm64-v8a +3,024,
  armeabi-v7a +1,840. Most of it is the inline entry in `lua_pcall`, which
  every protected helper inlines (about 100 bytes each on arm64-v8a), the
  trampolines (192 to 332 and 240 to 364 bytes) and the allocator and
  `xlua_core_newstate` (about 500 bytes). Moving the entry out of line was
  measured and was larger. After merging main at ab4f498 (bridge
  hardening), against that main: Lua 5.5.1 arm64-v8a 374,768 to 377,696
  (+2,928; gzip -9 +1,165), armeabi-v7a 250,980 to 252,740 (+1,760; gzip
  +1,025); Lua 5.4.9 +3,008 and +1,824. `check_android_elf.py` passes on all
  four libraries (290 and 287 exports, the five new ones included).
- **Speed:** two rounds of `benchmark.py` (gencode and gencode-min, macOS
  arm64, Lua 5.5.1) showed no systematic difference for `new LuaEnv()`
  between main and the branch; the host spread (up to 2x between rounds on a
  shared machine) is far larger than one pointer compare per call. Not
  measured on a phone.

## 9. Open questions for the owner

1. **Sharing `LuaDLL.cs`.** An asmdef cannot compile one file into two
   assemblies, and a core-only plugin cannot link the bridge's `__Internal`
   imports on iOS. The proposal (section 2.2): `XLua.LuaDLL.Lua` moves to
   Core with the Lua C API and core imports only; the bridge imports move to
   `XLua.LuaDLL.Bridge`, and the generator and runtime switch to it.
   - **Alternative:** a separate class of core imports, with no change to the
     bridge but with duplicated declarations.
   - **Which one?**
2. **Defaults of `new LuaScript()`.** The proposal is 64 MiB and 1 s, with
   `new LuaOptions()` unlimited. Or should `LuaScript` require options
   explicitly?
3. **Library tables per environment:** shallow copies (3–4 KB each, normal
   `next`/`rawget`) or read-only proxies (no per-environment cost, one more
   lookup per access, empty to `next`)?
4. **Memory limit semantics:** a cap that `pcall` can catch (proposed), or
   terminal for the call like the other limits?
5. **Per-call or cumulative limits for coroutines:** each `Resume` gets the
   full `InstructionLimit` and `TimeLimit` (proposed). Is a lifetime budget
   per coroutine wanted as well?
6. **`LuaTableRef` as a struct** (no allocation, leaks until script disposal
   if not disposed) or a class with a finalizer that queues the unref (one
   allocation per handle)?
7. **`print`:** absent by default (proposed), or routed to a `LuaOptions`
   callback or `Debug.Log`?
8. **`__gc` ban in core.** It closes the "finalizers run without hooks" hole.
   Libraries that use `__gc` for cleanup won't work. Acceptable?
9. **`TimeLimit` on WebGL** costs the per-instruction hook. Accept that, or
   document `TimeLimit` as unsupported on WebGL and rely on
   `InstructionLimit` there?
10. **luban fixture:** commit the generated output and its JSON twin from a
    pinned luban run on the owner's machine, or generate it in CI from a
    pinned and hashed luban release?
11. **The luaconf hook line for 5.4** (for `HashSeed` and the C budget macro)
    is a new vendored change. Acceptable, or should 5.4 drop runtime
    `HashSeed`?
12. **If Minimal stripping already drops the unreferenced `XLua` assembly**
    (the `core-5.5+nodefine` run answers this), should `XLUA_CORE_ONLY`
    stay as the switch for the native plugin only, or should the selector
    infer core-only from the absence of a reference to `XLua`? The define is
    simpler and explicit; inference saves users a setting but needs an
    Editor scan of asmdef references.
13. **Signatures in core:** keep `SignatureLoader` (RSA) opt-in for core as
    presets.md:34 says, with a `Load(code, name, signature)` overload, or wait
    for Ed25519 and ship core without signature checking?

## Decisions (owner, 2026-10-17)

| Question | Decision |
|---|---|
| 1. Sharing `LuaDLL.cs` | The Lua C API moves to `XLua.Core`; bridge-only imports go to a separate class in `XLua`. |
| 2. Defaults | `new LuaScript()` starts with the `ScriptDefaults` limits (64 MiB, 1 s); callers raise or remove them explicitly. |
| 3. Library tables per environment | Shallow copies per environment. |
| 4. Memory limit | A cap that `pcall` can catch ("not enough memory"), as in standard Lua. |
| 5. Coroutine limits | Per `Resume`; a lifetime budget may be added later. |
| 6. `LuaTableRef` | A struct; disposing the script releases every handle, and `LiveHandles` reports leaks. |
| 7. `print` | Absent by default, with an optional `Print` callback. The upstream `print_*` C helpers stay exported only in the legacy preset; the computed export lists of minimal and core leave them out. |
| 8. `__gc` in core | Refused by core's `setmetatable` (one check). |
| 9. `TimeLimit` on WebGL | Supported through the count hook checking the clock every N instructions. |
| 10. luban fixture | A small generated sample (a few tables, tens of KB) committed with its regeneration script; no LFS. |
| 11. Lua 5.4 `luaconf.h.in` hook | Accepted, recorded in `build/LUA_PATCHES.md`. |
| 12. `XLUA_CORE_ONLY` | Decided after the stripping test. |
| 13. Signatures | The existing RSA `SignatureLoader` stays an opt-in flag; Ed25519 comes later. |
| `buffer` library | Part of the first core release (the owner wants it). It uses Luau's function names because they are well designed, not for compatibility. |
| Size trims in core | Core loads text only, so the bytecode loader and dumper (`lundump.c`, `ldump.c`, `string.dump`) are left out of the core native build, together with the `LUA_COMPAT_*` options. `utf8` and `coroutine` stay: they cost about 4 KB and 2.5 KB, and core's yield model needs coroutines. |
