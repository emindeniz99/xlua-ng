# Core preset and the `LuaScript` API

Status: **design, written 2026-10-17; milestones 1 to 10 implemented
on 2026-10-18 (sections 8.1 to 8.9: the native layer, the limits, the
native sandbox of the core preset, the C# `LuaScript` with `Load`, `Call` and
the `XLua.Core` assembly, `Register` with `LuaCall`, table and function
handles (section 8.7), coroutines with `LuaCoroutine` and its drivers
(section 8.8), and the `buffer` library with `LuaBufferRef`), and the
first part of milestone 11 (section 8.10: the size variants and the stripping
test, a core-only player, the luban fixture, the user guide and the security
statement); the rest of 11 (CI, iOS, the release checks) not implemented**. It expands the
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
- Signature checking is optional (the RSA `SignatureLoader`, or a project's own C# loader); no native signature code.

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
  `slice = min(remaining, 2^20)` (the count is an `int`, `lua.h:468` in 5.4,
  so 2^30 would fit; 2^20 since 2026-10-18 bounds how late any stop can be,
  section 8.11).
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
  5.4), that is, on the hooked thread itself. From another thread it is
  not enough on its own: the running thread can see the frames' `trap`
  flag before the hook mask and drop the stop (found 2026-10-18,
  section 8.3). The watchdog therefore stores the mask, fences, then sets
  the traps, and arms again on every poll until the hook has fired. While
  an instruction limit runs, the running thread's per-instruction
  decrement of `hookcount` overwrites the remote `hookcount = 1`, so the
  arming also sets line events, whose mask only the arming thread writes
  (section 8.11).
- **The lock** closes two races. The owner thread takes the same lock at
  every switch (setting `running` and arming the next thread) and at entry
  end:
  - the watchdog arming a thread that has just finished and been collected;
  - the owner thread overwriting the stop hook with a budget hook;
  - the owner thread freeing a frame that the watchdog's `lua_sethook` is
    walking: the allocator frees frame-sized blocks under the lock while a
    limited call runs (added 2026-10-18, section 8.3).

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
  `XLUA_LIBS` to the `Safe` set. The run-time mask for `LuaEnv` is done
  (2026-10-18, `LuaOptions.Libraries` with these `LuaLibs` bits plus `Bit`, `Jit`
  and `Ffi`): it is applied in C# after `luaopen_xlua`, not through
  `xlua_core_openlibs`, so the legacy native library is unchanged
  ([presets.md](presets.md#library-parts-and-the-run-time-mask)).

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
- **Trusted bytecode (opt-in, 2026-10-18):** with CMake
  `XLUA_TRUSTED_BYTECODE` the host may run its own precompiled chunks through
  `xlua_core_call` (`XLUA_CALL_CHUNK`) with `mode = XLUA_CORE_MODE_BINARY`
  (`xlua_core_run_mode` until milestone 6 replaced it), and the core library
  keeps `lundump.c` (the dumper stub stays). Scripts' `load` and `require` stay
  text only. The security statement (section 5) covers text only; a chunk
  loaded in the binary mode is the host's responsibility
  ([build/README.md](../../build/README.md#trusted-bytecode-in-luascript)).
  LuaScript's C# has no binary `Load` yet (question 21).

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
- **Loops whose length `__len` chooses** (added 2026-10-18, section 8.12):
  `table.insert`, `table.remove`, `table.concat` and `table.sort` take `#t`
  from `__len`, which a script sets, so they loop up to 2^63 times (2^31
  for `sort`) over nils or C metamethods. Lua 5.4.9's `string.rep("", n)`
  makes n - 1 empty copies.

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

/* ltablib.c tmove: one check per BUDGET_TICK elements; since section 8.12
   also tinsert, tremove, tconcat and sort's partition (budget_step) */
/* lstrlib.c of 5.4.9 (section 8.12): str_rep returns "" for two empty strings, as 5.5 does */
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
- **Remaining C paths that the budget does not split** (`table.sort` and
  `table.concat` moved under the budget on 2026-10-18, section 8.12):
  - `string.rep`, `string.format` and `utf8.*`: linear in sizes that
    `MemoryLimit` bounds (`string.rep` allocates its result before it
    copies);
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
| `table` | `LuaLibs.Table` | `move`, `insert`, `remove`, `concat` and `sort` under the C budget |
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
> - **Listed C paths.** `string.rep`, `string.format` and `utf8` run in C
>   for a time proportional to their input or result, which `MemoryLimit`
>   bounds. A full garbage collection takes time proportional to the heap.
> - **Host functions.** What a registered C# function does with its
>   arguments, including how long it runs and how much it allocates, is the
>   host's responsibility.
> - **Side channels.** `tostring` of tables shows addresses; timing is
>   observable.
> - **Other builds.** LuaJIT, Lua 5.3, modified builds, builds without the
>   `lstrlib.c` and `ltablib.c` budget patch, and `LuaEnv` in any preset.
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

### 8.3 Milestones 3 and 4 as implemented (2026-10-18)

- **Files.** `build/xlua_core_limits.c` holds the limits, so that the size
  flags compile it at `-Oz`: it runs per call, slice or coroutine switch, not
  per instruction. `xlua_core.h` has the new state fields and the outermost
  entry (`depth`, `xlua_core_begin`, `xlua_core_end`).
- **Exports** (core ABI version 2): `xlua_core_set_limits(L, instructions,
  time_ns, flags)`, `xlua_core_interrupt(L)` and `xlua_core_watch(L)`;
  `xlua_core_stats` gains `stop_reason`, `instruction_limit` and
  `time_limit_ns`, and `instructions` is filled. The flags are
  `XLUA_LIMITS_CLOCK_CHECK` (no watchdog: WebGL, or the test switch
  `LuaWatchdog.ForceClockCheck`) and `XLUA_LIMITS_SMALL_SLICES` (tests:
  1,000-instruction slices, to exercise the slicing without running 2^30
  instructions). `xlua_core_interrupt` takes the state instead of
  section 3.6's `(xc, serial, reason)`: the native side keeps the deadline,
  so neither the watchdog nor `Interrupt` needs a serial; both act under the
  state's spinlock only while an outermost call is active.
- **Entry protocol for `LuaEnv`.** The outermost protected entry is the
  exported `lua_pcall` (Lua 5.3+), which every C# entry into Lua uses
  (`DoString`, `LuaFunction`, delegates, `xlua_pgettable` and the other
  protected helpers). `xluaL_loadbuffer` stays a memory entry only. Nested
  entries keep the outer budget and deadline.
- **Budget and slices** as section 3.5: the budget goes to threads in count
  slices of at most 2^30; at every switch and at the end the unused rest goes
  back, so the count is exact and global across coroutines. `coroutine.resume`
  and `wrap` are Lua's own `lcorolib.c` code (with its notice) around
  `lua_resume`; `coroutine.close` wraps Lua's function and switches only for
  a suspended or dead coroutine, the cases where it runs code. `luaopen_xlua`
  installs them for core states.
- **The stop hook counts instructions only** (count 1), not every event as
  section 3.5 proposed: with call events the stop hit `errorfunc`'s own call
  and every message became "error in error handling". Lua code still cannot
  run one instruction after a stop. Raising from the hook first turns hooks
  back on (`L->allowhook`): Lua clears it while a hook runs, and an `xpcall`
  handler that loops ran forever without them (found by the native test).
- **Watchdog** (`LuaWatchdog.cs`): one background thread polls
  `xlua_core_watch` for every state with a time limit, every quarter of the
  shortest limit (1 to 20 ms); the design's pulsed sleep needs the C# side to
  know when a call begins, which `LuaEnv`'s raw `lua_pcall` entries do not
  tell it. `Dispose` removes the state first and closes it under the
  `Interrupt` lock. The native deadline is checked in the hook (at slice ends
  and in the WebGL clock check) and in the C budget.
- **Profiler** (`xlua.sethook`): on a core state it goes through the same
  hook (call and return events), and its return report pauses the slice
  while the profiler's Lua function runs.
- **C budget** (milestone 4) as section 3.11, with `lmemfind` charging the
  bytes it compares and `tmove` both loops; recorded in `LUA_PATCHES.md`.
  Lua 5.5's `luaconf.h` got the same hook include as 5.4 (by a relative path
  for WebGL, which now defines `XLUA_LUA_HOOKS` too). The official 5.4.9 and
  5.5.1 suites pass on the patched trees (basic, complete, internal), and
  `pm.lua`, `strings.lua` and `sort.lua` also pass inside libxlua with an
  instruction limit active.
- **C#:** `LuaOptions.InstructionLimit`, `TimeLimit`; `LuaEnv.InstructionLimit`,
  `TimeLimit`, `LastInstructionCount`, `Interrupt()`; `LuaLimit`,
  `LuaLimitException` and the three limit exceptions of section 2.12
  (without `Elapsed`). `ThrowExceptionFromError` classifies by the native
  stop reason. Lua 5.4 and 5.5 only.
- **With `XLUA_LIBS`** (merged 2026-10-18): without `coroutine`, CMake
  defines `XLUA_NO_COROUTINE_LIB` for `xlua_core_limits.c`, which then
  installs no coroutine switches; the C budget goes with `lstrlib.c` and
  `ltablib.c` when `string` or `table` is left out. Both reduced sets build
  with `-Werror` and stop runaway loops (host smoke, Lua 5.5.1).
- **Tests:** `check_core_limits` in `native_regression.py` (the section 7.3
  instruction, time and C budget cases from C, plus the switch edges), the
  five `CaseLuaOptions*` limit cases of the managed suite (every binding
  mode), and `lua_tests.py` on the patched trees. `coverage.sh` reaches 100%
  of lines, functions and branches of `xlua_core_limits.c` (142 branches),
  `xlua_core.c` and `xlua.c` on Lua 5.4.9 and 5.5.1. The spinlock's retry
  branch is covered by a watchdog thread that polls without sleeping, so it
  depends on the two threads meeting; every run so far did.
- **Watchdog hang** (found and fixed 2026-10-18;
  [watchdog-lost-stop.md](../research/watchdog-lost-stop.md)). A host run
  of the managed suite hung for 28 minutes in `CaseLuaOptionsTimeLimit`.
  - *Cause:* the watchdog armed the stop hook with `lua_sethook` from its own
    thread. On AArch64 the running thread could see a frame's `trap` flag
    before `hookmask`; `luaG_traceexec` then cleared the flag as a spurious
    trap, and `while true do end` never read the mask again. The watchdog
    armed only once, so the call ran forever. About one call in 300 to
    2,000 lost its stop on macOS arm64, Lua 5.4.9 and 5.5.1, with and
    without the bridge hardening defines.
  - *Fix:* `arm_stop_remote` stores the mask, fences, then lets
    `lua_sethook` set the traps; `stop_pending` makes every watchdog poll
    (and a further `Interrupt`) arm again until the hook has fired.
  - *Second race:* ThreadSanitizer then showed the walk over the running
    thread's frames (`settraps`) reading a frame that `luaE_shrinkCI` had
    just freed after a caught error. The allocator now frees frame-sized
    blocks under the core lock while a limited call runs
    (`xlua_core_free_frame`).
  - *Still open:* the walk can read a frame that is being reused or
    published (TSan reports the races; no run has shown an effect). See
    open question 14.
  - *Tests:* `check_core_watchdog_race` (3,000 calls under a 1 ms limit with
    a sleeping watchdog; failed 6 of 6 runs before the fix, each within 355
    calls) and a 400-call loop in `CaseLuaOptionsTimeLimit` under a 5 ms
    limit. Before the fix the managed suite hung in that loop on both Lua
    versions, and 11 of 12 standalone runs of it hung within 400 calls.
    `coverage.sh` stays at 100% of lines, functions and branches
    (`xlua_core_limits.c` 148 branches) and `sanitize.sh` passes, both on
    Lua 5.4.9 and 5.5.1. `benchmark.py` with limits shows no change beyond
    noise; the plugin grows by 80 bytes (macOS arm64, 5.5.1).
- **Not done:** `LuaScript`'s `xlua_core_call` entries (milestone 6), the
  instruction count without a limit, `LuaMemoryLimitException`, and the
  `__gc` ban (core sandbox, milestone 5): finalizers run without hooks, so a
  `LuaEnv` script can still loop in `__gc`.
- **Sizes**, Android API 23 release build, Lua 5.5.1, main c067a2b against
  this branch: arm64-v8a 377,696 to 382,768 bytes stripped (+5,072; gzip -9
  160,372 to 162,874), armeabi-v7a 252,740 to 256,224 (+3,484; gzip 140,955
  to 143,165). Lua 5.4.9: arm64-v8a +5,120, armeabi-v7a +3,468.
  `check_android_elf.py` passes on all four (293 and 290 exports). Before the
  separate `-Oz` file and the inline atomics the arm64-v8a growth was
  +7,880; out-of-line atomics alone linked about 830 bytes of compiler-rt CPU
  detection.
- **Cost** (`benchmark.py`, gencode, macOS arm64, Lua 5.5.1, minimum of 7
  runs; `XLUA_BENCH_LIMITS` in `Benchmark.cs`):

  | case | main | branch, `new LuaEnv()` | `LuaOptions()` | instruction limit | time limit | both and memory |
  |---|---:|---:|---:|---:|---:|---:|
  | `lua.function_call` (ns per Lua call) | 11.0 | 11.1 | 11.0 | 28.4 | 11.2 | 29.0 |
  | `lua.table_array_read` | 7.7 | 7.5 | 7.5 | 17.3 | 7.6 | 18.0 |
  | `lua.int_math` | 10.8 | 10.8 | 10.8 | 10.7 | 10.7 | 10.5 |
  | `lua_cs.static_call` (Lua to C#) | 51.7 | 51.4 | 48.6 | 91.4 | 50.9 | 91.8 |
  | `cs_lua.delegate_int` (C# to Lua) | 66.6 | 66.6 | 66.1 | 83.0 | 90.2 | 98.8 |
  | `table.get_string_key` | 244.3 | 246.5 | 245.0 | 257.2 | 277.8 | 278.8 |

  Without limits the branch is as fast as main (a second round agrees within
  the noise). An instruction limit makes Lua code take the per-instruction
  hook path: 1 to 2.6 times slower depending on the code, plus about 40 ns
  per Lua-to-C# call. Each outermost C#-to-Lua call costs about 15 ns with
  an instruction limit (lock, hook arming) and about 25 ns with a time limit
  (one `clock_gettime`).
- **Phone** (2026-10-18, moto g play 2024, big cores, five interleaved
  rounds of `Test/Native/flag_sweep.py bench`, the release build at API 23;
  [build/FLAGS.md](../../build/FLAGS.md#core-limits-on-the-protected-call-path)):
  with limits built in, `lua_pcall` is no longer inlined into
  `xlua_pgettable` and the other protected helpers (on armeabi-v7a they now
  tail-call it through a long-branch thunk). Against main before the merge
  (`a84e037`), main is 0.2% (armeabi-v7a) and 0.3% (arm64-v8a) slower in the
  geomean, inside the layout noise, and `xlua_pgettable` 2.1% and 1.3%
  (219.0 against 214.3 ns on armeabi-v7a). Through the same library:

  | per outermost C-to-Lua call, ns | ABI | `luaL_newstate` | core state | instruction limit | time limit |
  |---|---|---:|---:|---:|---:|
  | `c_c_calls_lua` | armeabi-v7a | 156.7 | 162.2 | 300.2 | 635.6 |
  | | arm64-v8a | 129.4 | 133.6 | 246.7 | 601.4 |
  | `c_table_pgettable` | armeabi-v7a | 219.0 | 226.0 | 317.9 | 689.0 |
  | | arm64-v8a | 207.2 | 215.5 | 274.0 | 664.7 |

  A core state without limits is 1.2 and 1.5% slower in the geomean (the
  allocator's accounting). An instruction limit makes pure Lua 2 to 3.4
  times slower, as on macOS, and adds about 90 ns (armeabi-v7a) and 60 ns
  (arm64-v8a) per outermost call. A time limit adds about 470 ns per
  outermost call on both ABIs, against 25 ns on macOS: the one clock read in
  `xlua_core_begin` is that expensive on this phone (whether it is a system
  call was not checked). Lua code under a time limit runs at full speed.
  **Question for the owner:** a `TimeLimit` on a state that C# calls into
  per frame and object pays this on every call; reading the clock only in
  the hook and in the watchdog (a deadline set lazily, or a coarse
  `CLOCK_MONOTONIC_COARSE` read where the kernel offers it) would remove it,
  at the cost of a less exact deadline. Not changed here.

### 8.4 Milestone 5 as implemented (2026-10-18)

- **Files.** `build/xlua_core_sandbox.c` holds the sandbox and its exports;
  `build/xlua_core_nobc.c` replaces `lundump.c` and `ldump.c` in core builds.
  The sandbox is not in `xlua_core.c` as section 3.1 drew it: as a file of
  its own it can stay out of the bridge presets, whose libraries are then
  unchanged byte for byte, and it compiles at `-Oz` with the other cold files.
- **`XLUA_PRESET=core` configures** (Lua 5.4 and 5.5): `xlua_core.c` with
  `XLUA_CORE_ONLY` (no C# function trampolines, wrapper caller or
  `errorfunc`, which need `xlua.c`), `xlua_core_limits.c`, the two new files,
  and Lua without `linit.c`, `lundump.c`, `ldump.c` and the `io`, `os`,
  `debug` and `package` libraries. No `xlua.c`, `i64lib.c`,
  `memory_leak_checker.c`, LuaSocket, `xlua_libs.c` or libdl. The preset's
  defaults are `XLUA_LIBS=coroutine;table;string;math;utf8` and
  `XLUA_EXPORTS=managed` on ELF; CMake refuses `XLUA_LUASOCKET`, `XLUA_MIME`,
  `XLUA_LEAK_CHECKER`, `XLUA_UINT64` and `XLUA_MEMORY_SNAPSHOT` when they are
  turned on, and an `XLUA_LIBS` with `io`, `os`, `debug` or `package`.
- **`XLUA_LUASCRIPT`** (new CMake option, also from the environment) builds
  the same sandbox into `legacy` and `minimal`, for LuaScript on the full
  plugin (section 2.2.2). It is off there for now (provisional), so those
  libraries do not grow before LuaScript's C# exists; the sandbox is about
  7 KB of code on arm64-v8a (not measured in a bridge library). Lua 5.3 and
  LuaJIT refuse it.
- **Exports** (`build/xlua_core_exports.txt`, the core preset's managed list):
  - `lua_State *xlua_core_open(const xlua_core_config *cfg, void *host,
    int32_t *status)`: `xlua_core_newstate`'s allocator and seed, then the
    libraries and the sandbox. `cfg->libs` takes the `LuaLibs` bits of
    section 2.14 (`XLUA_LUALIBS_*` in `xlua_core.h`), not Lua 5.5's
    `LUA_*LIBK` order that section 3.8 suggests: C# passes `LuaOptions.Libraries`
    unchanged, and `Buffer` has no `LIBK`. 0 is every safe library the build
    contains. `io`, `os`, `debug`, `package`, `buffer` (milestone 10) and
    libraries the build lacks give `XLUA_ERRCONFIG`. `cfg->sandbox` must be
    1; LuaEnv's plain globals (0) are not implemented. A failed open frees
    everything and gives `XLUA_ERRMEM`.
  - `int xlua_core_newenv(lua_State *L, int32_t *env)` and
    `int xlua_core_unref(lua_State *L, int32_t env)`: environment handles,
    registry references. `xlua_core_unref` releases only environments (it
    checks the metatable, so a wrong number cannot free a registry slot such
    as `LUA_RIDX_MAINTHREAD`); milestone 8 extends it to the other handles.
    Handle 0 is the default environment (`Globals`) and is never released.
  - `int xlua_core_run(lua_State *L, int32_t env, const char *code, int32_t
    code_len, const char *name, int32_t name_len, const char **out,
    int32_t *out_len)`: loads text as chunk `"@" + name` into an
    environment and runs it as one entry with the state's limits, with a C
    message handler on `luaL_traceback` (lua.c's `msghandler`, under the
    error reserve). `*out` is the first result through `luaL_tolstring`, or
    the message, valid until the next `xlua_core_run`; it is anchored in the
    registry rather than left on the stack, so a nested call from a callback
    leaves the caller's stack alone. The status is `XLUA_OK`,
    `XLUA_ERRSYNTAX`, `XLUA_ERRRUN`, `XLUA_ERRMEM`, `XLUA_ERRERR`,
    `XLUA_LIMIT_*`, `XLUA_INTERRUPTED` (from the stop reason), or
    `XLUA_ERRHANDLE` and `XLUA_ERRCONFIG`. This is the entry of the native
    tests and the smoke test; milestone 6's `xlua_core_call` (typed arguments
    and results, section 3.2) replaces it for `LuaScript`.
  - `void xlua_core_set_resolver(xlua_core_resolver fn)` and
    `int xlua_core_set_require(lua_State *L, int32_t enabled)`: the
    process-wide resolver of `require`, like the dispatcher of milestone 7,
    and `require` in or out of the base per state (`ModuleResolver` set or
    not). The resolver gets `xlua_core_open`'s host pointer and the name, and
    returns 1 with the text and an optional chunk name (`xlua_core_source`),
    0 for "not found" or -1 with a message. It runs as C# code: the allocator
    does not refuse meanwhile, and an overdraft raises Lua's memory error once
    it returns, as the trampolines do (section 3.4).
  - Also in the core list: `xlua_core_version`, `xlua_core_close`, the memory
    and limit exports of milestones 2 to 4, `xlua_gc`, and `lua_gettop`,
    `lua_settop`, `lua_type`, `lua_toboolean`, `lua_rawlen` and
    `lua_checkstack` (section 3.12). `xlua_core_newstate` is exported only
    with `XLUA_EXPORTS=all`. The list is `CORE_EXPORTS` in
    `Test/Native/managed_imports.py` until `Assets/XLua/Core` exists.
  - The core ABI version stays 2: no struct that `LuaEnv` mirrors changed,
    and `LuaEnv` checks for exactly 2. `xlua_core_config.libs` and `sandbox`
    are now used by `xlua_core_open`; `xlua_core_newstate` still ignores them.
- **The sandbox** as section 4, with these details:
  - `LUA_RIDX_GLOBALS` and `package.loaded._G` point to the base `B`, a new
    table with the kept base functions (copied from Lua's global table, which
    is then garbage), the wrapped `setmetatable` and `collectgarbage`, and
    `require` when enabled; `load` is not in it.
  - The original library tables live in the registry; `string.dump` is
    removed from the original too, since `("").dump` reaches it through the
    string metatable. One shared metatable `{ __index = B, __metatable =
    false }` serves every environment.
  - `setmetatable` is Lua's code (lbaselib.c) plus a raw check for any
    non-nil `__gc` field; `__gc = false` is refused as well.
  - `collectgarbage` takes `"count"` only; no argument means `"collect"` and
    is refused with Lua's "bad argument" message.
  - `load` is a closure per environment (Lua 5.4's `luaB_load` with its
    reader and reserved slot, which 5.5's `lua_load` also accepts). It always
    passes mode `"t"`; a mode without `t` returns `nil` and "attempt to load
    a binary chunk (mode is 'b'): text chunks only". An explicit `nil`
    environment stays `nil`, as in Lua.
  - `require(name)`: a cached value, or the resolver's text loaded with mode
    `"t"` in a fresh environment and called with `(name, "@chunkname")`,
    whose result (or `true`) is cached per state. A module found again while
    it loads gives "loop requiring module 'x'" (Lua 5.4 would recurse until
    the C stack overflows). A failed load or run is not cached, so a later
    `require` asks the resolver again, as in Lua 5.4; memory errors keep
    their status.
  - **`xlua_core_nobc.c`:** `luaU_undump` raises "binary chunks are not
    supported" with `LUA_ERRSYNTAX`; `luaU_dump` returns 1. Only a C caller
    that passes mode `"b"` (or 5.5's `"B"`, which skips the mode check)
    reaches them.
  - **Not done:** the native panic of section 3.12. Lua aborts on an
    unprotected error without one; a message would need a test-only way to
    raise outside an entry, which the core exports do not offer. Registered
    host functions and dotted library tables in the base are milestone 7.
- **`LUA_COMPAT_*`:** nothing to remove. CMake never defines
  `LUA_COMPAT_5_3`, and 5.5's `LUA_COMPAT_MATHLIB` is commented out upstream,
  in every preset. Lua 5.5's `LUA_COMPAT_GLOBAL` (on upstream, keeps `global`
  usable as a name) stays on: turning it off makes `global` a reserved word,
  which breaks scripts and data files that use it as a key (question below).
- **Tests:**
  - `Test/Native/core_sandbox_cases.txt`: 78 cases and 13 modules, one or
    more per rule of section 4 and 3.9 (section 7.2's sandbox list, plus
    limits, errors and `require`).
  - `Test/Native/core_smoke.c` (core exports only, so it runs against the
    managed export list and on cross-compiled targets) and
    `native_regression.py` through `Test/Native/core_regression.py` run the
    cases and the ABI edges: configuration and memory errors of
    `xlua_core_open` (from the core struct up), library subsets, the seed,
    handles, `xlua_gc`, `Interrupt`, the watchdog entry and nested calls from
    inside a call, `require` without a resolver or switched off, and the
    overdraft around the resolver. With the Lua API exported they also call
    the bytecode stubs through `lua_load` and `lua_dump`, and states without
    the sandbox. A bridge library built with `XLUA_LUASCRIPT=ON` runs them
    after the bridge checks, on Lua's real bytecode loader.
  - `check_exports.py --preset core` and `check_android_elf.py --preset
    core` (section 6): the core exports, exactly those with managed exports,
    none of `io`, `os`, `debug`, `package`, LuaSocket or the bridge, and none
    of `lundump.c`'s messages in the file. `presets_check.py` configures core
    and its refusals.
  - `Test/Fuzz/fuzz_core.c`: chunks in a fresh sandbox with a 1 MB memory
    limit and a 100,000-instruction limit (section 7.1); `require` loads
    the module name as text.
  - `sanitize.sh`, `coverage.sh` and `analyze.sh` take `core-5.4.9` and
    `core-5.5.1`.
- **Results** (macOS arm64 host; Linux arm64 container `xlua-analysis:26.04`
  for the sanitizers, coverage, analysis and fuzzing):
  - `core_smoke.c` and `native_regression.py` pass on core 5.4.9 and 5.5.1
    (macOS: every export; Linux: every export and managed exports, under ASan,
    UBSan and the C API checks). The legacy 5.5.1 library with
    `XLUA_LUASCRIPT=ON` passes `native_regression.py` and `core_smoke.c`.
  - Coverage of the core build: 100% of lines, functions and branches of
    `xlua_core_sandbox.c` (118 branches), `xlua_core_nobc.c` and `xlua_core.c`
    (46 and 58 branches on 5.4.9 and 5.5.1). `xlua_core_limits.c` reaches 68%
    of its branches there; its 100% is measured through the bridge.
  - `analyze.sh` is clean on the core configurations and on 5.4.9 and 5.5.1
    with `XLUA_LUASCRIPT=ON`; `fuzz_core` ran 180 s on each version without a
    finding.
  - Unchanged elsewhere: the managed suite (427 tests, all three binding
    modes) passes on legacy and minimal, Lua 5.5.1 and LuaJIT (macOS arm64),
    and under the sanitizers on legacy 5.5.1 with `XLUA_LUASCRIPT=ON` (Linux);
    `presets_check.py` passes; Lua's own 5.4.9 and 5.5.1 suites pass (basic,
    complete, internal), which run the interpreter that the sandbox does not
    touch. All after merging main at bacd3a8 (the watchdog fixes).
- **Questions for the owner:**
  1. `XLUA_LUASCRIPT` in `legacy` and `minimal`: off (provisional, as now) until
     LuaScript ships, or on, so one full plugin serves both APIs (section
     2.2.2) at about 7 KB?
  2. Lua 5.5's `LUA_COMPAT_GLOBAL` in core: keep upstream's default (on, as
     now), or turn it off as the size decision says for `LUA_COMPAT_*`, which
     makes `global` a reserved word for scripts and data files?
  3. `require` passes the module two arguments (name and `"@chunkname"`) and
     returns one value; Lua 5.4 returns the loader data as a second value.
     Enough?
  4. A failed module is not cached (Lua 5.4's behaviour), so a host resolver
     is asked again on every `require` of a broken module. Cache the failure
     instead (Lua 5.1's "loop or previous error")?
- **Sizes**, Android API 23 release build (NDK r30), stripped, "APK" zip
  deflate level 6, against legacy of the same tree:

  | Lua | ABI | core stripped | legacy | core APK | legacy |
  |---|---|---:|---:|---:|---:|
  | 5.5.1 | arm64-v8a | 253,792 | 383,672 | 115,246 | 163,690 |
  | 5.5.1 | armeabi-v7a | 171,456 | 257,156 | 101,693 | 143,599 |
  | 5.4.9 | arm64-v8a | 239,096 | 367,136 | 107,872 | 155,227 |
  | 5.4.9 | armeabi-v7a | 163,408 | 247,484 | 96,463 | 137,017 |

  Within section 6's budget (290 KB stripped and 125 KB compressed on
  arm64-v8a, Lua 5.5.1), before any of `LuaScript`'s later exports. The two
  new files are about 6.8 KB of functions in the arm64-v8a library (7.2 KB
  of `.text` for `xlua_core_sandbox.c` before the linker drops unused
  sections, 152 bytes for the stubs). `check_android_elf.py --preset core`
  passes on all four (20 exports). The legacy and minimal libraries
  (Lua 5.5.1 and 5.4.9, both ABIs) equal main's byte for byte once the build
  ID is removed.

### 8.5 Milestone 6 as implemented (2026-10-18)

- **Files.** `Assets/XLua/Core` is the `XLua.Core` assembly: `LuaDLL.cs`
  (the Lua C API and core imports), `LuaTypes.cs` (`LuaTypes`,
  `LuaGCOptions`, `LuaThreadStatus`, `LuaIndexes` and
  `MonoPInvokeCallbackAttribute`, moved from `ObjectTranslator.cs`),
  `LuaOptions.cs`, `LuaException.cs`, `LuaWatchdog.cs`, `LuaValue.cs`,
  `LuaScript.cs`, `AssemblyInfo.cs` and `Editor/PluginVariants.cs`. The moved
  files keep their `.meta` GUIDs. `Assets/XLua/Src/LuaDLLBridge.cs` holds the
  bridge's imports. The opt-in asmdefs are `Assets/XLua/Asmdef~/Core/XLua.Core.asmdef`
  (no references, unsafe code, no define constraint) and
  `Core/Editor/XLua.Core.Editor.asmdef` (Editor only, references
  `XLua.Core`); `XLua.asmdef` references `XLua.Core`, `XLua.Editor.asmdef`
  `XLua` and `XLua.Core`, and both keep `!XLUA_CORE_ONLY`. In the Assets
  layout Core compiles into Assembly-CSharp with the rest, as before.
  `Tools/make_upm_package.py` puts Core into the package's `Core` folder
  with both asmdefs.
- **The `LuaDLL.cs` split** (owner decision 1):
  - `XLua.LuaDLL.Lua` (Core) has the imports that every native library
    defines: Lua's own `lapi.c` and `lauxlib.c` functions, which the core
    library contains too, the `xlua_core_*` layer and `xlua_gc`, and the C#
    helpers built only on them (`lua_pop`, `lua_tostring`, `lua_tobytes` ...).
    Everything that `xlua.c` or `i64lib.c` defines moves to
    `XLua.LuaDLL.Bridge` (XLua), including the shims for what Lua 5.2+ has as
    macros (`lua_pcall`, `lua_insert`, `lua_remove`, `lua_replace`,
    `lua_tonumber`, `lua_setfenv`, `luaL_openlibs`) and `xlua_get_lib_version`.
  - **`Bridge` derives from `Lua`,** so `XLua.LuaDLL.Bridge` reaches every
    import. The runtime, the templates, the source generator and the tests
    change only their alias (`using LuaAPI = XLua.LuaDLL.Bridge;`); CodeEmit
    looks its methods up with `FlattenHierarchy`. `const LUADLL` is
    `protected` in `Lua`, shared by both. Design question 15 below.
  - `InternalGlobals.strBuff` is `CoreGlobals.strBuff` in Core, and
    `InternalGlobals.LUA_REGISTRYINDEX` is gone: `LuaIndexes` (Core) holds the
    value, and `Binding.cs` reads it there. Core grants the `XLua` assembly
    its internals (`InternalsVisibleTo("XLua")`) for `LuaIndexes`,
    `LuaWatchdog` and `LuaOptions.Clone`.
- **`LuaScript` compiles only with the scripting define `XLUA_LUASCRIPT` or
  `XLUA_CORE_ONLY`** (`LuaScript`, `LuaValue`, `LuaType`, the four
  LuaScript-only exceptions and the sandbox imports). Two reasons, neither in
  the design: at Unity's Minimal stripping (the default for IL2CPP) every
  method of user assemblies is kept, so the sandbox imports would reach every
  iOS and WebGL player and fail its link on a legacy library built without
  `XLUA_LUASCRIPT=ON` (section 8.4's provisional default); and every player
  would carry LuaScript's IL. A project that wants both APIs defines
  `XLUA_LUASCRIPT` and builds the plugin with `XLUA_LUASCRIPT=ON`; a
  core-only project defines `XLUA_CORE_ONLY`. Design question 16.
- **Native: `xlua_core_call`** (`build/xlua_core_sandbox.c`) replaces
  `xlua_core_run`; the core ABI version is 3 and `LuaEnv` checks it.
  - The struct is `xlua_core_call_t` (C cannot have a function and a
    typedef of the same name). Its 64-bit field comes first, then the
    pointers, then the 32-bit fields, so every offset is the same where
    `int64_t` is 4-byte aligned (i386). `xlua_value` is 16 bytes on every ABI.
  - **Kinds:** `XLUA_CALL_GLOBAL` (a dotted name, raw gets, the first name in
    the environment and then in the base) and `XLUA_CALL_CHUNK` (text as
    `"@" + name`). `XLUA_CALL_REF` and `XLUA_CALL_COMPILE` need function
    handles and return `XLUA_ERRCONFIG` until milestone 8. A missing name
    gives Lua's own message for the expression, "attempt to call a nil value
    (global 'f')" or "(field 'g')", and an intermediate value that is not a
    table "attempt to index a number value (global 'n')".
  - **`mode`** (the struct's last field, which section 3.2 did not have):
    the load mode of `XLUA_CALL_CHUNK` as `xlua_core_run_mode` took it before
    (merged from main on 2026-10-18): 0 or `XLUA_CORE_MODE_TEXT` text,
    `XLUA_CORE_MODE_BINARY` the host's precompiled chunks (only with CMake
    `XLUA_TRUSTED_BYTECODE`, else `XLUA_ERRCONFIG` before anything runs), or
    both. 0 being text keeps a zeroed struct a text load. `xlua_core_run` and
    `xlua_core_run_mode` are gone, their tests use `xlua_core_call`
    (question 21).
  - **Not in the struct, unlike section 3.2:** the limits (they stay the
    state's, set by `xlua_core_set_limits` when a property changes, so a call
    is still one P/Invoke) and the watchdog serial (milestone 3's watchdog
    does not use one). `instructions` reports the count of an outermost call
    with an instruction limit, else 0.
  - **Results:** nil, booleans, integers, floats and strings by value;
    tables, functions and other values by type only (handles: milestone 8).
    `nresults` is the function's count, also beyond `results_cap`. String
    results are anchored in a registry table, released by the next call only
    after its arguments are pushed, so a result can be passed straight back.
  - **Errors:** the message handler keeps the message (non-string errors
    described as `lua.c` does) and stores `luaL_traceback` in the registry, so
    `error` and `traceback` are apart; the traceback is reported for runtime
    and limit errors only. `XLUA_ERRTYPE` (an argument type that cannot be
    passed) and `XLUA_ERRSTACK` (too many arguments) are used.
  - The core export list is no longer every import of Core: it is the
    `xlua_core_*` functions LuaScript needs, `xlua_gc` and the five
    non-raising functions of section 3.12 (19 exports;
    `Test/Native/managed_imports.py`, `CORE_EXPORTS`). The other Lua C API
    imports of `LuaDLL.Lua` serve the bridge; a core library defines them
    (`check_exports.py --preset core` checks that every Core import exists
    in a library with every export, for the iOS and WebGL link), but does not
    export them, since IL2CPP, Mono and .NET resolve an import when it is first
    called. Exporting all of them cost 5,096 bytes stripped on arm64-v8a
    (Lua 5.5.1), mostly symbol tables and PLT entries.
- **`LuaScript`** (sections 2.3 to 2.7, 2.12 to 2.14):
  - **Done:** both constructors (`ScriptDefaults`: 64 MiB and 1 s), the core
    version check, a `NotSupportedException` with the build hint when the
    library has no sandbox (`EntryPointNotFoundException` on Mono and .NET;
    an iOS link error instead, hence the define above), `Dispose` (refused
    while a call of the script runs) and the finalizer, `IsDisposed`,
    `MemoryLimit`, `InstructionLimit`, `TimeLimit` (with the watchdog or the
    clock check), `MemoryUsed`, `MemoryPeak`, `LastInstructionCount`,
    `Interrupt` (any thread), `CollectGarbage`, `ModuleResolver` (with a weak
    `GCHandle` as the host pointer and the module text pinned until the next
    module), `Load` (string, UTF-8 span, `Load<T>`, results span), `Call`
    and `Call<T>` with 0 to 4 arguments and a span, the span and tuple
    multiple results, and the owner-thread check.
  - **One P/Invoke per call:** the function name, the code and the string
    arguments are encoded into one reused UTF-8 array, pinned for the call;
    up to 16 arguments and results are `xlua_value`s on the stack, more use a
    reused array. `Call<T>` reads through `LuaRead<T>.Read`, a delegate
    chosen once per `T`.
  - **`LuaValue`:** as section 2.4, but with an implicit operator for every
    integral type (sbyte to ulong) and float, not only `long` and `double`:
    with those two only, a `ulong` would convert through `double` and lose
    bits, and adding `ulong` alone made every `int` argument ambiguous. A
    `ulong` is its two's-complement `long`, as the section says. Equality
    follows Lua (1 equals 1.0; a string equals its UTF-8 bytes).
  - **Readers:** `long`, `int`, `double`, `float`, `bool`, `string`, `byte[]`,
    `LuaValue`; nil reads as null for `string` and `byte[]`, as false for
    `bool`, and fails for the numbers.
  - **Exceptions:** `LuaRuntimeException` (with `LuaStackTrace`, a new
    property of `LuaException`; `ToString` adds it), `LuaSyntaxException`
    (`ChunkName`), `LuaTypeException`, `LuaMemoryLimitException` (a
    `LuaLimitException` with `LuaLimit.Memory`, which is added after the
    existing values so they keep their numbers). `LuaEnv` is unchanged: its
    memory errors are still `LuaException("not enough memory")`.
  - **`LuaOptions`:** `Libraries`, `CheckThread` (default true) and
    `ScriptDefaults` (a new object on every read). `Libraries` and `LuaLibs`
    came from main at the same time (the minimal preset's library selection for
    `LuaEnv`); LuaScript reads them with `Default` as `Safe` and refuses io, os,
    debug, package and LuaJIT's bit, jit and ffi. `LuaEnv` has no thread check.
  - **Not done (later milestones):** `Register` and `LuaCall` (7);
    `Globals`, `NewEnvironment`, the `env` overloads, `Compile`, handles,
    `LiveHandles`, `ErrorValue` and table and function arguments (8);
    coroutines (9); the buffer (10).
- **Tests** (macOS arm64; the sanitizers in the Linux arm64 container
  `xlua-analysis:26.04`):
  - `Test/Core/CoreTests.cs`, compiled from `Assets/XLua/Core` alone by
    `Test/Native/run_core.py` with the .NET SDK's Roslyn and run on Mono
    6.14.1 and .NET 10: 27 tests (lifecycle, options, libraries, finalizer,
    `LuaValue`, `Load`, syntax errors and chunk names, the argument shapes,
    every result type and conversion, lookups, multiple results, runtime
    errors, the instruction, time, interrupt and memory limits, the thread
    check, two scripts on two threads, the seed, `require`, `Dispose` inside
    a call and a nested call). They pass on the core library, Lua 5.4.9 and
    5.5.1, on both runtimes. `run_core.py --bridge` compiles them with the
    whole runtime and adds a test of `LuaEnv` and two `LuaScript`s in one
    process (no `CS` or `xlua` in the scripts, bytecode refused on the full
    library, limits per state, a `LuaEnv` callback calling into a
    `LuaScript`): 28 tests pass on legacy 5.5.1 with `XLUA_LUASCRIPT=ON`, on
    both runtimes.
  - **Allocation probe:** 70,000 calls with primitive arguments and results
    (`Call<double>`, `Call<long>` with four arguments, a string argument, a
    span of four, a results span, `Call` and `Load<long>`) allocate 0 bytes
    on Mono and on .NET (`GC.GetAllocatedBytesForCurrentThread`, which the
    test first checks counts); 1,000 `Call<string>` allocate only the result
    strings.
  - **Editor-level core-only check:** `asmdef_compile_check.py --core-only`
    defines `XLUA_CORE_ONLY`, evaluates the define constraints (XLua and
    XLua.Editor must be left out, XLua.Core must stay) and compiles XLua.Core,
    XLua.Core.Editor and `Test/Core/UnitySample/LuaScriptSample.cs` against
    XLua.Core alone, for the Editor and an IL2CPP player; `--luascript`
    compiles the sample next to the bridge. Both pass against Unity 2021.3.45f2
    and 6000.3.26f1, as do the default, `--source-gen` and `--hotfix` runs.
    This compiles with the editors' reference assemblies.
    `Test/UnityMatrix/core_only_check.py` then runs the check in a real
    editor: the UPM package with the core library as the Editor plugin, an
    empty project, `XLUA_CORE_ONLY` set in Player Settings by a first batch
    launch, and a second launch that compiles the sample and an Editor probe.
    Unity 6000.3.26f1 and 2021.3.45f2 compiled `XLua.Core`, `XLua.Core.Editor`,
    `Assembly-CSharp` and `Assembly-CSharp-Editor` and neither `XLua` nor
    `XLua.Editor`; the probe found only the two Core assemblies loaded, the
    plugin selector preferring the core variant, and a `LuaScript` call on
    the core library returning its result (Lua 5.5).
  - `core_smoke.c` gained the `xlua_core_call` checks (typed values, 64-bit
    integers, embedded NULs, more results than room, lookups, errors and
    tracebacks, argument errors, other kinds, anchoring across a full
    collection and release on the next call, a result passed back, the
    instruction count, a memory error). It and `native_regression.py` pass on
    core 5.4.9 and 5.5.1 and on legacy 5.5.1 with `XLUA_LUASCRIPT=ON`.
  - **Sanitizers:** `sanitize.sh` with `VERSIONS="core-5.4.9 core-5.5.1"`
    (ASan, UBSan, the Lua API checks) passes: `check_exports.py --preset
    core`, `native_regression.py` and `core_smoke.c` with every export, and
    the exact export check and `core_smoke.c` with the 19 managed exports.
  - **Unchanged elsewhere:** the managed suite (448 tests on the tree merged with main 99d366a4) passes in every
    mode (`--gen-code --gen-code-minimize --gen-assembly --source-gen
    --source-gen-fallback`) on legacy 5.5.1 and LuaJIT; `native_regression.py`
    on legacy 5.5.1, LuaJIT and minimal 5.5.1; `check_exports.py`,
    `check_android_elf.py`, `check_pinvoke_abi.py` (138 imports),
    `plugin_variants_check.py`, `presets_check.py`, `hotfix_check.py`,
    `luacats_check.py`, `migration_check.py` and `editor_compile_check.py`
    (7 editors from 2021.3.45f2 to 6000.6.6f1, 59 configurations including
    the four new LuaScript ones: 405 ok and the 7 C# 12 configurations skipped
    as before; one run of `player-ios-minimal-hotfix` on 6000.3.21f1 crashed
    inside Mono's csc, a segmentation fault in Roslyn's object pool, and
    passed when run again).
  - **Finding:** Linux Mono 6.14.1 (the container's) declares
    `ReadOnlySpan<T>`'s `ref readonly` members without the `modreq` that the
    macOS build has; Roslyn refuses to compile against it ("not supported by
    the language") and code compiled against the macOS assemblies fails there
    with `MissingMethodException`. So the C# tests do not run under the
    sanitizers; the native paths they drive are the ones `core_smoke.c` runs
    there. Unity compiles against its own reference assemblies, which have the
    `modreq` (the asmdef and editor checks pass).
- **Sizes**, Android API 23 release build (NDK r30), arm64-v8a, stripped,
  "APK" zip deflate level 6, main (`35165d0`) against this branch:

  | Lua | preset | main stripped | branch | main APK | branch |
  |---|---|---:|---:|---:|---:|
  | 5.5.1 | core | 253,792 | 255,512 | 114,891 | 115,846 |
  | 5.4.9 | core | 239,096 | 240,784 | 107,356 | 108,398 |
  | 5.5.1 | legacy | 383,672 | 383,672 | 162,481 | 162,479 |
  | 5.4.9 | legacy | 367,136 | 367,088 | 154,172 | 154,141 |

  Core grows by 1,720 bytes (5.5.1; 1,620 of them `.text`, `xlua_core_call`
  with its lookup and value conversion against `xlua_core_run`) and stays
  within section 6's budget (290 KB, 125 KB compressed). The legacy
  libraries do not compile the sandbox; 5.4.9 differs by 8 bytes of `.text`
  and 48 bytes of padding, with the same 290 exports.
  After merging main `4eac4ff4` (bytecode options; `xlua_core_call` took over
  `xlua_core_run_mode`'s load mode): core 5.5.1 255,720 bytes stripped and
  116,214 compressed against that main's 254,280 and 115,376 (+1,440, +838),
  5.4.9 240,992 and 108,534 against 239,584 and 107,764 (+1,408, +770).
  **Managed** (IL, the asmdef layout compiled for an Android IL2CPP player
  against Unity 6000.3.26f1, `asmdef_compile_check.py`): main's `XLua.dll`
  232,960 bytes; this branch `XLua.dll` 227,328 plus `XLua.Core.dll` 14,848
  (+9,216, the second assembly's own metadata), `XLua.Core.dll` 37,376 with
  `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` (LuaScript, LuaValue and the
  exceptions: +22,528), and a core-only project carries `XLua.Core.dll` only.
  IL2CPP sizes are milestone 11's (`size_report.py`).
- **Not done:** a player build of a core-only project; the WebGL plugin (`xlua_webgl.cpp` does not include the
  sandbox, so LuaScript on WebGL waits; `check_webgl_exports.py` accepts the
  sandbox all or not at all); `Test/MobileSmoke` probes for LuaScript
  (milestone 11).
- **Questions for the owner:** 15 to 21 in section 9.

### 8.6 Milestone 7 as implemented (2026-10-18)

- **Files.** `Assets/XLua/Core/LuaCall.cs` holds `LuaCallback`, `LuaCall`,
  and the part of `LuaScript` (a `partial` class now) for host functions:
  `Register`, `Unregister`, the typed shorthands and the dispatcher. The
  trampoline is in `build/xlua_core.c` under `XLUA_SANDBOX`, not in
  `xlua_core_sandbox.c`: it runs on every call from Lua to C#, and
  `xlua_core.c` is in `XLUA_HOT_FILES` (`-O3`). Registration and the
  environment bookkeeping are in `xlua_core_sandbox.c`; the stop check after a
  host function is in `xlua_core_limits.c`.
- **ABI (core version 4).** `LuaScript` and `LuaEnv` check for 4.
  - `xlua_core_frame` is section 3.2's frame with an out field `status` in
    place of `flags` (`XLUA_FRAME_RETURN`, `_YIELD`, `_ERROR`,
    `_ERROR_WHERE`, `_ARGERROR`, `_TYPEERROR`, `_MEMERR`), the error as
    `error`, `error_len` and `error_arg`, and `yieldable`. Pointers come
    first, then eight 32-bit fields.
  - `xlua_core_call_t` gains an out field `flags` at the end:
    `XLUA_CALLF_HOST_ERROR` (below).
  - **New exports:** `xlua_core_set_dispatcher(fn)` and
    `xlua_core_register(L, env, name, len, fn_id, &old_id)`; `fn_id` 0 removes
    the name, so `Unregister` needs no export of its own. The core list has 21
    exports.
  - **Not adopted:** section 3.3's `xlua_core_frame_error` and
    `xlua_core_frame_reserve`. C# writes the error message, string results
    and results beyond the trampoline's eight into native memory of its own
    (below) and points the frame at it, so the error path and long result
    lists need no P/Invoke.
- **The trampoline** (section 3.7): arguments become `xlua_value` records
  before C# runs, eight on the C stack and more in a userdata (a memory error
  can still be raised then). Strings are `LUA_TSTRING` values only; tables,
  functions and other values carry their stack index in `u.i` for
  milestone 8's borrowed handles. Then, with `unprotected` raised around the
  dispatcher, C# runs. After it returns, in this order: the stop of a limit
  that tripped meanwhile (below), the memory error of an overdraft (a full
  collection first, as section 8.2), the error C# reported, then
  `luaL_checkstack` and the results, or `lua_yield`. Upvalue 1 is the
  function's id; the host pointer is `xlua_core_open`'s, kept in
  `xlua_core_t`.
- **The dispatcher** is one static method with `[MonoPInvokeCallback]`, its
  delegate in a static field, installed once under a lock by the first
  `Register` of the process. It catches every exception (a second `try`
  covers a failure to report one, which then raises a fixed message), so
  none crosses a Lua frame. It finds the script through a thread-static
  "active script" that every entry sets (`LuaScript.Raw`), checked against
  the host pointer, with the weak `GCHandle` of section 2.3 as the fallback:
  on Mono the handle's `Target` alone cost about 28 ns per call (Mono
  6.14.1, macOS arm64: an empty host function took 91 ns with it and 64 ns
  through a static field). The `try` block has no `finally`, which cost time
  on Mono too; together the two changes took a one-argument host function
  from 106 to 50 ns on Mono.
- **Ids.** A host function's id indexes the script's callback array; ids
  are reused. `Unregister` and a second `Register` of a name set the old
  closure's id upvalue to 0, so a closure that a script kept fails with
  "attempt to call a host function that was unregistered" instead of
  calling whatever function gets the id next.
- **Native memory for results.** Each script keeps an arena
  (`Marshal.AllocHGlobal`) for string results, error messages and result
  lists longer than eight. It is a stack: a host function writes above the
  top, gives its part back when it returns, and the trampoline pushes the
  values before any C# runs again, so nested host functions never see each
  other's results. Results hold arena offsets until the host function
  returns, then pointers, so the arena may grow (move) meanwhile. Results
  beyond eight go to a managed overflow array first, then into the arena.
  An arena above 64 KiB is freed at the next outermost host function.
- **Names.** `"name"` goes into the frozen base; `"library.name"` into a
  library table kept with the standard libraries (and in `package.loaded`, so
  messages name `game.log`) that every environment copies. One dot only; an
  empty part, more dots or `env != 0` give `XLUA_ERRCONFIG`, and C# throws
  `ArgumentException` first. `Register(LuaTableRef env, ...)` waits for
  milestone 8's handles. Section 2.8 does not say what happens to
  environments that exist already (`Globals` always does): two weak tables
  record every environment and every library copy, and a registration sets
  the key in each environment's copy that still has nil or the old function
  there, and copies a library it has just created into environments without
  one. A value the script put under that key, a library table the script
  replaced, and a library the script removed are left alone.
- **`LuaCall`** as section 2.8, with these details:
  - indices are 0-based; a missing argument reads as `Nil` in `ArgType`,
    `Get` and the `Opt` getters, and `GetBool` is Lua's truth (missing is
    false);
  - `GetString` and `GetUtf8` take strings only (numbers are not converted,
    as for `Call<T>`); `ArgEquals` compares bytes, encoding only text that
    is not ASCII;
  - a wrong type throws `LuaTypeException` that knows its frame and
    argument; the dispatcher turns it into `luaL_typeerror` or
    `luaL_argerror` after C# returned, so the message is Lua's own:
    `bad argument #1 to 'calcReward' (number expected, got string)`, with
    "no value" for an argument not passed and Lua's method adjustment;
  - `Return(LuaValue)` of a table or function and `GetTable`,
    `GetFunction`, `GetBuffer`, `Retain`: milestones 8 and 10;
  - new: `CanYield`.
- **Exceptions to Lua errors.** A `LuaException` the host throws becomes its
  message at the caller's position, as `luaL_error` would place it (none when
  `pcall` called the function directly); any other exception becomes
  `"TypeName: message"` at the position; a nested call's
  `LuaRuntimeException`, `LuaSyntaxException` or limit exception keeps its
  message, which has its own position; a nested `LuaMemoryLimitException`
  becomes Lua's memory error, so the outer call throws
  `LuaMemoryLimitException` and a script's `pcall` sees "not enough memory".
  **`InnerException`:** the trampoline keeps the error string it raised in a
  registry slot; when the error that reaches `xlua_core_call` is that string
  (`lua_rawequal`), the call sets `XLUA_CALLF_HOST_ERROR` and C# attaches the
  host function's exception. An error the script rethrows unchanged keeps
  it; one it changes does not. The slot is cleared by each outermost call.
- **Limits** (section 3.5): a host function is never preempted. After it
  returns, the trampoline raises the stop of an instruction, time or
  interrupt limit that tripped meanwhile (the watchdog or `Interrupt` set it,
  or a nested call that the host function caught), so the call cannot
  return normally even when no Lua instruction follows (a registered
  function called straight from C#). Without a watchdog (WebGL's clock check)
  the deadline is read there. This takes the core lock once per host
  function call while a limit is configured (within the noise on macOS
  arm64: 38.6 against 39.1 ns per call on .NET, 50.5 against 53.3 on Mono,
  with a time limit only).
- **Threads.** A nested call from a host function runs on the host
  function's thread (`xlua_core_t.callback_L`), as do `xlua_core_newenv`,
  `_register` and `_set_require` from inside one. Found by review before any
  test: on the main thread, a host function in a coroutine that called back
  into the script ran Lua code on a thread whose count hook the switch
  accounting had taken away, so `InstructionLimit` and `TimeLimit` did not
  stop it. `TestRegisterNestedCalls` covers it. The owner-thread check
  applies to `Register` and `Unregister`; a host function runs on the
  thread of the call that reached it.
- **`Yield()`** works in any coroutine that can yield, Lua's own
  `coroutine.resume` and `wrap` today and milestone 9's `Start` later, not
  only under `Start` as section 2.8 says: the trampoline yields the results
  after C# has returned, and the values of the next resume become the
  results of the call in Lua. Elsewhere it throws `InvalidOperationException`
  (a Lua error with that inner exception).
- **Typed shorthands:** `Action` and `Func` with 0 to 4 arguments; the types
  are `long`, `int`, `double`, `float`, `bool`, `string`, `byte[]` and
  `LuaValue`, read and written through one delegate per type chosen once
  (`ArgOf<T>`, `ResultOf<T>`, as `LuaRead<T>`); another type throws
  `NotSupportedException` at `Register`. The thunks are private generic
  classes, so IL2CPP sees each instantiation at the `Register<...>` call
  site.
- **Memory** (section 3.4): while C# runs, the allocator grants instead of
  refusing; the trampoline raises the memory error once C# has returned and
  a collection did not bring the state back under the limit. Results are
  pushed after C# has returned, with refusals on again, so a result over the
  limit is an ordinary memory error. The C# API itself does not allocate in
  Lua outside a protected entry; the overdraft path is tested from C
  (`core_smoke.c`, `lua_checkstack` inside a host function).
- **Tests:**
  - `Test/Core/RegisterTests.cs` (15 tests, `CoreTests` is `partial`):
    every getter and result type, the argument error messages (also past 8
    arguments and through `pcall`), 20 and 3,000 results, nested results and
    arena growth, exceptions and inner exceptions, nested calls (in a
    coroutine, under a limit, a yield across C#), `Unregister`, replacing and
    id reuse, dotted names across environments and in `string`, every typed
    shorthand, `Yield`, the limits (a host function past the time limit,
    `pcall` around it, a caught nested limit, `Interrupt` inside, the clock
    check, an exact instruction count), memory (a result over the limit, a
    nested memory error), the thread check and `Dispose` inside a host
    function, the allocation probe and the cost; `TestRegisterBuffers`
    came with the merge of milestone 10 (below). `run_core.py` expects 42
    tests before that merge, 51 after it (one more with `--bridge`).
  - **Allocation probe:** 80,000 host function calls with primitive
    arguments and results (`LuaCallback` with `GetLong`, `GetDouble`,
    `GetBool`, `GetUtf8`, `ArgEquals`, `OptLong`, string and span results,
    12 results, `Register<long, long, long>` and `Register<double, bool,
    double>`) allocate 0 bytes on Mono and on .NET.
  - `core_smoke.c` `check_host`: a C dispatcher with every frame status,
    argument types and stack indices, 9 and 10 arguments, 24 results from
    the host's memory, invalid statuses, counts and result types, the host
    error flag, yields, nested calls and their thread, the frame serial,
    registration inside a host function, unregistering and replacing, every
    branch of the environment update, `Interrupt` and the clock check after a
    host function, the overdraft and memory errors of the registration.
  - `Test/Fuzz/fuzz_core.c`: bit 4 of the first byte registers five host
    functions through a C dispatcher; `corpus/core/host.lua` calls them.
- **Results** (macOS arm64 host; the Linux arm64 container
  `xlua-analysis:26.04` for `sanitize.sh` and the fuzzer):
  - `run_core.py`: 42 tests pass on Mono 6.14.1 and .NET 10 against the core
    library, Lua 5.5.1 and 5.4.9, with and without the Lua API checks
    (`XLUA_APICHECK`); `--bridge` passes 43 on legacy 5.5.1 with
    `XLUA_LUASCRIPT=ON`.
  - **Under ASan:** the same 42 tests pass on Mono against core libraries
    built with ASan, UBSan and the API checks (5.5.1 and 5.4.9), with the
    ASan runtime inserted into Mono (Homebrew's Mono is ad-hoc signed, so
    `DYLD_INSERT_LIBRARIES` applies). In the Linux container the C# tests
    still do not compile (section 8.5's finding), so there `sanitize.sh`
    with `core-5.4.9 core-5.5.1` covers the native paths: `core_smoke.c`
    with the host function checks, `native_regression.py` and the export
    checks with every export and with the 21 managed exports, all of which
    pass (ASan, UBSan, `XLUA_APICHECK`, Lua 5.4.9 and 5.5.1).
  - `core_smoke.c` passes on core 5.5.1 and 5.4.9 (also with ASan and UBSan
    on macOS and with the API checks) and on legacy 5.5.1 with
    `XLUA_LUASCRIPT=ON`; `fuzz_core` ran 180 s on each version without a
    finding (228,358 inputs on 5.5.1).
  - **Unchanged elsewhere:** `native_regression.py` on core 5.5.1 and 5.4.9,
    legacy 5.5.1 with and without `XLUA_LUASCRIPT`, minimal 5.5.1 and
    LuaJIT; `check_exports.py` on all of them (core: 21 exports), and
    `check_android_elf.py` (`--preset core`: 21 exports) on the Android
    core and legacy libraries of both versions;
    `check_pinvoke_abi.py` (140 imports); the managed suite (448 tests in
    every mode: `--gen-code --gen-code-minimize --gen-assembly --source-gen
    --source-gen-fallback`) on legacy 5.5.1; `presets_check.py`;
    `editor_compile_check.py` (7 editors, 60 configurations: 413 ok, the 7
    C# 12 configurations skipped as before); `asmdef_compile_check.py`
    default, `--core-only`, `--luascript`, `--source-gen` and `--hotfix`
    against Unity 2021.3.45f2 and 6000.3.26f1 (the sample now registers a
    `LuaCallback` and a `Register<long, long, long>`, compiled for the Editor
    and an IL2CPP player).
  - **In a real editor:** `Test/UnityMatrix/core_only_check.py` on Unity
    6000.3.26f1 with the core 5.5.1 library passes; its probe now also
    registers a `LuaCallback`, a `Register<string, string>` and a throwing
    host function, and checks their results and the error in the editor's
    Mono. 2021.3 was not run (one editor at a time on this machine, and the
    Unity matrix agent kept one busy).
  - **After merging main `4b0aa92d`** (milestone 10): the core and API-check
    libraries pass `run_core.py` with 51 tests on Mono and .NET (5.5.1 and
    5.4.9), `--bridge` 52 on legacy 5.5.1 with `XLUA_LUASCRIPT=ON` (no buffer
    library there, so the buffer tests check the refusal); the ASan Mono run
    51; `core_smoke.c` (131 cases) on every build, also under ASan;
    `sanitize.sh` for both core versions; `fuzz_core` 180 s on each version;
    `native_regression.py`, `check_exports.py` (23 core exports), the managed
    suite (451 tests in every mode), `presets_check.py`,
    `editor_compile_check.py` (413 ok, 7 skipped) and the editor check above.
    After merging main `ad843da9` as well: `webgl_compile_check.py --package`
    (Unity 6000.6.4f1's Emscripten; `xlua_core_limits.c` changed),
    `run_core.py` (51, `--bridge` 52), the managed suite (451, every mode),
    `check_pinvoke_abi.py` (142 imports) and `asmdef_compile_check.py`
    (default, `--core-only`, `--luascript`, `--source-gen`) pass.
- **Cost** (`TestCallbackCost`, macOS arm64, Lua 5.5.1, no limits, the
  minimum of 7 runs of 1,000,000 calls, three rounds; a Lua loop that adds
  the result of a one-argument function):

  | per call, ns | Lua function | `LuaCallback` | `Register<long, long>` |
  | --- | ---: | ---: | ---: |
  | .NET 10 | 10.2 | 38.6 to 39.5 | 43.1 to 44.9 |
  | Mono 6.14.1 | 9.8 to 10.6 | 51.4 to 51.5 | 58.4 to 59.2 |

  A host function costs about 29 ns more than a Lua function on .NET and 41
  ns on Mono; the typed shorthand adds 4 to 8 ns (two delegate calls).
  `LuaEnv`'s bridge on the same machine and build (`benchmark.py`, Mono,
  legacy 5.5.1): `lua_cs.static_call` (two arguments, generated wrappers)
  51.6 ns, 485.9 ns through reflection. A time limit adds no measurable
  cost; an instruction limit adds the per-instruction hook to the Lua part
  (about 15 ns of this loop) and nothing per host function beyond it.
- **Sizes** (Android API 23 release build, NDK r30, arm64-v8a, stripped, zip
  deflate level 6; [build/SIZE.md](../../build/SIZE.md#the-core-preset)):
  core 5.5.1 255,720 to 260,488 bytes (+4,768) and 116,214 to 118,476
  compressed (+2,262); core 5.4.9 240,992 to 245,824 (+4,832), 108,533 to
  110,890. After merging milestone 10 (main `4b0aa92d`): core 5.5.1 268,488
  and 121,532 against that main's 263,544 and 119,272 (+4,944, +2,260), 5.4.9
  253,808 and 114,158. Within section 6's budget (290 KB, 125 KB), with 3.5
  KB of the compressed budget left. Legacy changes by
  -32 and +48 bytes (the larger `xlua_core_t`). **Managed:** `XLua.Core.dll`
  with `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` 37,376 to 56,320 bytes of IL
  (+18,944); without the define unchanged at 14,848.
- **Buffers** (added when milestone 10 was merged, main `4b0aa92d`): a buffer
  argument is `LuaType.Buffer` with its bytes and length in the frame record
  (`xlua_core_buffer_at`, so `xlua_core.c` now compiles with `XLUA_BUFFER`
  too); `LuaCall.GetBufferSpan(i)` is a `Span<byte>` over it, valid until the
  host function returns, and writes are seen by the script. A buffer result
  is a `LuaBufferRef` of the same script (`Return(LuaBufferRef)`,
  `Return(LuaValue)`, `Register<LuaBufferRef>`): the trampoline pushes the
  buffer itself (`xlua_core_push_buffer_ref`), and the handle stays the
  host's. Section 2.8's borrowed `GetBuffer` handle needs milestone 8's
  borrowed handles. `TestRegisterBuffers` and `core_smoke.c`'s
  `check_host_buffer` cover both directions and an invalid handle.
- **Not done:** `Register(LuaTableRef env, ...)`, table and function
  arguments and results, borrowed handles (`GetTable`, `GetFunction`,
  `GetBuffer`) and `Retain` (milestone 8; arguments carry their stack index
  for them); C#-driven coroutines (milestone 9, `Yield`
  already works under Lua's own); a phone measurement of the cost; the WebGL
  plugin (still without the sandbox, section 8.5); IL2CPP code size
  (milestone 11).
- **Questions for the owner:** 28 to 33 in section 9.

### 8.7 Milestone 8 as implemented (2026-10-18)

- **Files.** `Assets/XLua/Core/LuaTableRef.cs` holds `LuaTableRef`, `LuaKey`,
  `LuaPairs`, `LuaPair` and the part of `LuaScript` that keeps every handle
  (tables, functions and buffers) in one slot array, with `Globals`,
  `NewTable`, `NewEnvironment`, `Key`, iteration and `ToDictionary`.
  `LuaFunctionRef.cs` holds `LuaFunctionRef` and `Compile`. `LuaBufferRef.cs`
  now uses the shared slots. Native: `build/xlua_core_sandbox.c` (the handle
  export, handle arguments and results, the error value), small changes in
  `xlua_core.c` (the trampoline pushes results through the sandbox's
  `xlua_core_push_value`) and `xlua_core_buffer.c`.
- **ABI (core version 5).** No struct changed, but the meaning of results did
  (a table or function result is now an owning handle the caller releases),
  and three exports went, so `LuaScript` and `LuaEnv` check for 5.
  - **One export for every handle operation,** `xlua_core_handle(L, op,
    xlua_value v[4])`, in place of section 3.3's `xlua_core_key`, `_ref`,
    `_newtable`, `_tget*`, `_tset*`, `_tlen` and `_iter_*`, and of the
    milestone 5 and 10 exports `xlua_core_newenv`, `xlua_core_newbuffer` and
    `xlua_core_tobuffer`: `GET`, `SET`, `LEN`, `NEXT`, `INEXT`, `NEWTABLE`,
    `NEWENV`, `REF`, `KEY`, `EQUAL`, `NEWBUFFER`, `BUFFER`
    (`XLUA_HANDLE_*` in `xlua_core.h`). Each export costs its symbol, its
    entry protocol and its argument checks; the core list has 21 exports
    (23 before). Question 39.
  - **Handles** are `int32_t` (`xlua_value`, `xlua_core.h`): 0 the default
    environment, a registry reference above `LUA_RIDX_LAST` (owning), and
    below 0 the key (`-2r`) or the value (`-(2r + 1)`) of iterator `r`
    (borrowed until its next step). A string can travel as a `LuaKey`
    handle: `XLUA_T_STR` with `len` -1. `xlua_core_unref` releases any
    registry reference to a table, function or userdata (environments,
    buffers, iterators) and refuses Lua's own slots, free slots and keys.
  - **Every operation is protected** by `luaD_rawrunprotected` (`luaD_pcall` since section 8.12; no C function
    to push, unlike `lua_pcall`), with refusals of the memory limit on also
    inside a host function (`unprotected` is saved and cleared as an entry
    does), and runs on the running host function's thread. It runs no Lua
    code (raw access only), so it begins no limited entry. A Lua error
    (a nil or NaN key, "invalid key to 'next'", "table overflow") comes back
    as `XLUA_ERRRUN` with the message in `v[2]`. Section 2.9's non-raising
    fast path for `LuaKey` and integer reads was not built: the protection
    is one `setjmp` and a pushed key may allocate. Question 41.
  - **`xlua_core_call`:** `XLUA_CALL_REF` calls a function handle;
    `XLUA_CALL_COMPILE` loads the chunk without running it and returns the
    function as an owning handle. `env` is any table handle (0 the default
    environment), no longer only an environment of `NEWENV` (question 43).
    Tables, functions and buffers in arguments and results are handles;
    other values (coroutines, light userdata) carry their type only and
    cannot be passed. A table or function error value is kept by the message
    handler and comes back as an owning handle in `error_ref`
    (`XLUA_CALLF_ERROR_TABLE`, `_FUNC`).
  - `xlua_core_register` with `env` != 0 sets the key `name` (dots included)
    of that table.
- **Iteration** keeps its key on the native side, as section 2.9 says: an
  iterator is a two-slot table in the registry that anchors the key and the
  value of the current step, so string pointers stay valid until the next
  step and table values are borrowed handles of the iterator. `NEXT` with
  `v[3].len` 1 (re)starts an iterator, so a `foreach` costs one P/Invoke per
  pair and none to open; C# keeps finished iterators for the next `Pairs`,
  which therefore allocates nothing, native or managed, after the first.
  `IPairs` runs on the same iterators (`INEXT`). An iteration that is not
  finished or disposed counts in `LiveHandles`.
- **Borrowed handles of host functions** (`LuaCall.GetTable`, `GetFunction`,
  `GetBuffer`, and `Get` of those types) are registry references made by
  `REF` from the argument's stack index (one P/Invoke), not section 3.2's
  negative stack indices: a stack index means another value as soon as a
  nested host function runs on the same thread, and a borrowed handle can
  reach code that runs there. C# invalidates them when the host function
  returns and releases their references at the next host function or after
  the outermost call, because a host function may return its argument as its
  result and the trampoline pushes results after C# returned. Question 40.
- **C#** as section 2.9, with these details:
  - one slot array for every kind of handle (a handle is the script, a slot
    and a generation, as milestone 10's buffers were); `LiveHandles` counts
    owning tables, functions and buffers and unfinished iterations, not
    borrowed handles, `Globals` or keys;
  - `Globals` is slot 0 and never disposed; `default(LuaTableRef)` where an
    environment is taken means `Globals`; `Register(LuaTableRef env, ...)`
    sets the key in that table (for `Globals` the table itself, so
    `rawget(_G, name)` sees it, where `Register(name)` writes the frozen base);
    `Unregister(LuaTableRef env, name)` is new;
  - **getters:** the named ones of section 2.9 (`GetLong` by string, index
    and `LuaKey`, `GetDouble`, `GetBool`, `GetString`, `GetBytes`, the
    fallback and `TryGet` forms, `TypeOf`, `GetTable`, `TryGetTable`,
    `GetFunction`, `TryCopyBytes`) read through one non-generic path, and
    generic `Get<T>`, `Get<T>(key, fallback)` and `TryGet<T>` take every
    result type of `Call<T>`, so that IL2CPP generates code per type only for
    what a project calls (question 44). Missing keys throw
    `KeyNotFoundException`, except `Get(LuaValue key)`, which returns nil as
    Lua's `rawget` does (question 47); `TryGet` is false also for a value
    that does not convert (question 46);
  - **setters:** `Set` by string, index, `LuaKey` and `LuaValue`; a nil or
    NaN key is Lua's error as a `LuaRuntimeException`; `SetNewTable`;
  - `LuaTableRef.Call` has the span forms only (five overloads, section 2.6
    asks for the full set): a `LuaFunctionRef` from `GetFunction` has them
    all (question 44);
  - `LuaPair` has `Key`, `Value` (decoding strings allocates), `KeyType`,
    `ValueType`, `KeyEquals`, `KeyUtf8`, `ValueUtf8` and `Deconstruct`; a
    pair of an earlier step throws `InvalidOperationException`;
  - `LuaValue` converts from `LuaTableRef` and `LuaFunctionRef`, has
    `AsTable` and `AsFunction`, compares handles as the same value (native
    `rawequal`), and is `IDisposable`: `Dispose` releases an owning handle it
    holds (question 49);
  - `LuaRuntimeException.ErrorValue`: the table or function a script raised,
    as an owning handle the exception holds; otherwise the message
    (question 42);
  - `ToDictionary(maxDepth = 16)`: `Dictionary<string, object>` at the top,
    `List<object>` for nested sequences, boxed `long`, `double`, `bool`,
    strings, buffers as `byte[]`, functions and other values as their type
    name, number and boolean keys as text; a cycle, a deeper table or a
    table key throws `InvalidOperationException` (question 45).
- **Size trims** (section 6's budget): the profiler's paths in the hook are
  left out of core builds (`XLUA_CORE_ONLY` now reaches `xlua_core_limits.c`;
  the profiler is `xlua.c`'s); the three exports folded into
  `xlua_core_handle`; `xlua_core_call`'s argument and result conversion and
  the trampoline's result push share `xlua_core_push_value` and `to_value`.
- **Tests:**
  - `Test/Core/TableTests.cs` (14 tests; `run_core.py` expects 65): reads by
    every key form and type, raw access past `__index`/`__newindex`, writes,
    `Length`, `Pairs` and `IPairs` (Lua's order, borrowed values, `Retain`,
    stale pairs, nested and manual iteration, assignment during traversal,
    "invalid key to 'next'", table keys), handle lifetimes (generations,
    copies, double `Dispose`, slot reuse, another script's handles, 1,000 and
    10,000 handles, script disposal), `ToDictionary`, `LuaKey`,
    `LuaFunctionRef`, environments (`Globals`, `NewEnvironment`, `Compile`,
    a plain table, `Register` and `Unregister` in a table), `ErrorValue`,
    borrowed handles of host functions (returned as results, nested, typed
    shorthands with handles, 100 per call), memory and thread checks, and
    the cost.
  - **Allocation probe:** `Pairs` over 10,000 keys with numeric reads and
    `KeyEquals`, a `Pairs` with borrowed table values, and 6,000 typed reads
    and writes by string and `LuaKey` allocate 0 bytes on Mono and .NET.
  - `core_smoke.c` `check_tables`: every operation of `xlua_core_handle`,
    their errors, handles in `xlua_core_call`, `CALL_REF` and `_COMPILE`,
    the error value, environments as plain tables, registration into a
    table, `NEXT`/`INEXT` with borrowed handles and the rehash error,
    borrowed arguments (a C host function), release rules, and `SET` past
    the memory limit; the milestone 5 to 10 checks use the handle export.
    `core_regression.py` and `fuzz_core.c` (table results walked with
    `NEXT`/`INEXT`, `GET`/`SET`, handles passed back, the error value; three
    new seeds) likewise.
- **Results** (macOS arm64 host; the Linux arm64 container
  `xlua-analysis:26.04` for `sanitize.sh` and the fuzzer; after merging main
  `c74f4bbc`, which brought `LubanFixtureTests.cs`):
  - `run_core.py`: 66 tests pass on Mono 6.14.1 and .NET 10 against the core
    library (Lua 5.5.1 and 5.4.9, with every export and with the managed
    ones), the core library with `XLUA_BUFFER=OFF`, and the API-check
    builds (`XLUA_APICHECK`, 5.5.1 and 5.4.9); `--bridge` passes 67 on legacy
    5.5.1 with `XLUA_LUASCRIPT=ON`, with and without `XLUA_BUFFER`.
  - **Under ASan:** the same 66 tests on Mono against the core libraries built
    with ASan, UBSan and the API checks (5.5.1 and 5.4.9), with the ASan
    runtime inserted into Homebrew's Mono, pass, as does `core_smoke.c`
    built with the sanitizers.
  - `core_smoke.c` and `native_regression.py` pass on every build above and
    on legacy and minimal 5.5.1; `check_exports.py` (core: 21 exports),
    `check_android_elf.py` (`--preset core`: 21 exports; legacy 290 and 293)
    and `check_pinvoke_abi.py` (139 imports) pass.
  - `sanitize.sh` with `VERSIONS="core-5.4.9 core-5.5.1"` and `XLUA_WERROR=ON`
    (ASan, UBSan, the Lua API checks; `check_exports.py`, `native_regression.py`
    and `core_smoke.c` with every export and with the 21 managed ones)
    pass on both versions. Its first run found a test bug: `core_smoke.c`'s
    bad-type host result had no handle set, and the zero that Linux left in it
    is the default environment, a valid table.
  - `fuzz_core` ran 180 s on each version without a finding (257,558 runs on
    5.5.1, 298,026 on 5.4.9).
  - **Unchanged elsewhere:** the managed suite (451 tests in every mode:
    `--gen-code --gen-code-minimize --gen-assembly --source-gen
    --source-gen-fallback`) on legacy 5.5.1; `presets_check.py`;
    `webgl_compile_check.py --package` (Unity 6000.6.4f1's Emscripten);
    `asmdef_compile_check.py` default, `--core-only`, `--luascript` and
    `--source-gen` against Unity 6000.3.26f1, and `--core-only` and
    `--luascript` against 2021.3.45f2 (the sample now reads a table, iterates
    it and calls a compiled chunk); `editor_compile_check.py` (7 editors, 60 configurations: 413 ok, the 7 C# 12
    configurations skipped as before);
    `Tools/dependencies.py --check`; the site's link check.
  - **In a real editor:** `Test/UnityMatrix/core_only_check.py` on Unity
    6000.3.26f1 with the core 5.5.1 library passes (its probe's 30 checks;
    only `XLua.Core` and `XLua.Core.Editor` compiled). 2021.3 was not run
    (one editor at a time on this machine).
- **Cost** (`TestTableCost`, macOS arm64, Lua 5.5.1, no limits, the minimum
  of 7 runs, ns per operation; the P/Invoke and the native checks are most of
  it):

  | | `GetLong(string)` | `GetLong(LuaKey)` | `TypeOf(long)` | `Set(LuaKey)` | `Pairs`, per pair | `LuaFunctionRef.Call<long>` | `Call<long>(name)` |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
  | .NET 10 | 83 to 86 | 73 | 71 | 60 to 63 | 94 to 95 | 137 to 139 | 156 to 160 |
  | Mono 6.14.1 | 187 to 189 | 114 to 122 | 116 to 119 | 105 to 107 | 136 to 137 | 288 | 289 to 298 |

  A string key costs its UTF-8 encoding (10 ns on .NET, 70 on Mono), which a
  `LuaKey` saves. Host functions are unchanged by the shared result push:
  `LuaCallback` 39.6 to 39.7 ns on .NET and 50.2 to 54.5 on Mono (milestone
  7: 38.6 to 39.5 and 51.4 to 51.5).
- **Sizes** ([build/SIZE.md](../../build/SIZE.md#the-core-preset)): core
  arm64-v8a, Lua 5.5.1, 268,488 to 270,008 bytes stripped (+1,520) and
  121,531 to 122,301 zip -6 (+770); 5.4.9 253,808 to 255,296 and 114,158 to
  115,000. Within section 6's budget (290 KB, 125 KB) with 2.7 KB of the
  compressed budget left. Legacy unchanged. **Managed:** `XLua.Core.dll`
  (core-only, Unity 6000.3.26f1 reference assemblies) 65,536 to 92,672 bytes;
  IL2CPP code not measured (question 44).
- **Not done:** coroutines (milestone 9: `LuaCoroutine`, and a coroutine as a
  handle); a userdata or coroutine `LuaValue` stays type-only; a phone
  measurement of the cost; IL2CPP code size of the new API (a
  `size_report.py` player run); the WebGL plugin (still no sandbox, question
  20); the design's `LuaTableRef.Call` overloads beyond the span forms.
- **Questions for the owner:** 39 to 49 in section 9.

### 8.8 Milestone 9 as implemented (2026-10-18)

- **Files.** `Assets/XLua/Core/LuaCoroutine.cs` holds `LuaCoroutine`,
  `LuaCoroutineStatus` and the part of `LuaScript` that starts coroutines and
  keeps their values; `LuaCoroutineDrivers.cs` holds `LuaReply`, the three
  handler delegates and the drivers. Native: `build/xlua_core_sandbox.c`
  (`xlua_core_call`'s coroutine kinds, the coroutine status, thread handles),
  and two small changes elsewhere: `xlua_core_limits.c` exports its thread
  switch as `xlua_core_switch` (it was `switch_thread`, static and only built
  with the coroutine library; the watchdog and hook code is untouched), and
  the trampoline in `xlua_core.c` reads a thread argument as `XLUA_T_THREAD`.
- **ABI (core version 6).** No struct changed and no export was added
  (section 3.3's `xlua_core_start`, `_resume`, `_thread_status` and
  `_thread_close` are kinds of `xlua_core_call` instead, as milestone 8 folded
  the table exports into `xlua_core_handle`; question 60). The version moved
  because results changed meaning, as in milestone 8:
  - `XLUA_T_THREAD` (9) is a new value type: a coroutine is an owning handle
    in `u.i`, as tables are, with its status in `len` (`XLUA_CO_SUSPENDED` 0,
    `_FINISHED` 1, `_FAILED` 2, `_RUNNING` 4, which is also Lua's "normal"; the
    main thread is always running). Before, it was `XLUA_T_OTHER` without a
    handle. `xlua_core_unref` releases it, `XLUA_HANDLE_REF` takes it, and it
    can be passed back as an argument, a table value or a host function result.
  - **`XLUA_CALL_START`** (8) is a flag on `XLUA_CALL_GLOBAL` or `_REF`: the
    function is looked up as for a call and run with the arguments as a new
    thread; the thread's owning handle comes back in `fn` (0 if none was made),
    also when the start fails, and the caller releases it.
  - **`XLUA_CALL_RESUME`** (4) and **`XLUA_CALL_CLOSE`** (5) take a coroutine
    handle in `fn`. A resume moves the arguments to the thread and runs
    `lua_resume`; a close runs `lua_closethread`, which runs the pending
    to-be-closed variables. A step that yielded returns `XLUA_YIELD` with the
    yielded values as results (they are moved off the thread, so the thread's
    stack is clean for the next resume); one that returned, `XLUA_OK`.
    `XLUA_CALLF_DEAD` says the coroutine is dead after the step. A coroutine
    that is running or normal is refused before anything runs
    (`XLUA_ERRHANDLE`, "cannot resume non-suspended coroutine", "cannot close
    a running coroutine"), and so is a resume of a dead one ("cannot resume
    dead coroutine", with `XLUA_CALLF_DEAD`), with Lua's own messages.
  - **Errors:** `lua_resume` has no message handler, so the error is described
    after it returns (the shared `describe_error`, formerly the body of
    `call_msgh`): the message, `ErrorValue` for a table or function, and the
    traceback of the coroutine's own stack, which Lua leaves in place after an
    error ("[C]: in function 'error'" or "in global 'error'", then the body,
    which Lua names `function <chunk:line>` because nothing called it by
    name). A copy of the error stays on the dead thread, so a later close
    passes it to the `__close` handlers, as `coroutine.wrap` does.
- **Entries and limits** (section 3.5; owner decision 5): every step is an
  outermost entry of its own, so it gets the full instruction budget and
  deadline, and `LastInstructionCount` is the step's. The entry begins on the
  calling thread; `xlua_core_switch` moves the budget, the watchdog's target
  (`running`) and a stop to the coroutine before `lua_resume` and back after
  it, exactly as the script-side `coroutine.resume` does. So the watchdog and
  `Interrupt` stop the coroutine's code, coroutines inside a step share its
  budget, a step nested in a host function shares the outer call's, and a
  coroutine left with a stale count hook disarms it on its next resume.
  Nothing in the hook or watchdog changed. A lifetime budget per coroutine is
  not built (decision 5: may come later; question 67).
- **`LuaCoroutine`** as section 2.11, with these details:
  - a class, as the design has it, over the handle slots of milestone 8: one
    object per handle (`LuaValue.AsCoroutine()` returns the same one), counted
    by `LiveHandles`, invalid after the script's `Dispose`. No finalizer, as
    for the other handles.
  - `LuaCoroutineStatus` gained **`Running`** (4) after the design's four
    values, for a coroutine that runs now (a host function it called looks at
    it) or that a script is running; it cannot be resumed, closed or, if
    `Start` made it, disposed.
  - **Values:** `ValueCount` and `co[i]` (nil beyond the count) hold the last
    step's values as `LuaValue`s, converted when the step returns: primitives
    without allocation, strings decoded then, tables, functions, buffers and
    coroutines as handles borrowed from the coroutine until its next step
    (`Retain` keeps one; the release waits until the step's arguments are
    pushed, so `co.Resume(co[0])` works, and inside a host function until the
    trampoline has pushed its results). At most `MaxValues` (64) values are
    kept (question 65).
  - **Not built: `Get<T>(i)`** of section 2.11. `co[i].AsLong()` and the other
    `As...` methods do the same; a generic reader table for `LuaValue` cost
    about 2.5 KB of IL (question 61).
  - **Status** is what C# last saw. A script may resume a coroutine it can
    reach (a script-made one, or `coroutine.running()` of a started one);
    `Resume` then finds the real state natively and throws
    `InvalidOperationException` for a dead or running one, and a dead one
    becomes `Finished` (question 66).
  - **Errors:** an error or a limit throws from `Start` or `Resume` with the
    coroutine's traceback and makes it `Faulted`. **A failed `Start` returns
    no coroutine:** it is closed (its pending `__close` handlers run with the
    error) and released before the exception leaves (question 69).
  - **`Close`** runs the pending to-be-closed variables of a suspended or
    faulted coroutine as one entry with the limits and makes it `Closed`; an
    error of a handler throws, except a runtime error when closing a faulted
    coroutine, which is its own error again (`lua_closethread` returns it); a
    limit still throws. **`Dispose`** closes a coroutine of `Start` (quietly:
    `LuaException`s are dropped) and releases the handle; a coroutine a script
    made is only released, since the script may still use it (question 63).
  - **Script-made coroutines** are `LuaCoroutine`s as well, which section 8.7
    left for this milestone: `Call<LuaCoroutine>`, `Get<LuaCoroutine>` of a
    table, `LuaValue.AsCoroutine()` (owning from results, borrowed in `Pairs`),
    `LuaCall.GetCoroutine(i)` (borrowed) and `LuaType.Coroutine` (question
    60). A not-yet-started one has the status `Yielded`, and its first
    `Resume` passes the function's arguments.
  - **Start:** `Start(name)` with 0 to 4 arguments and a span, and
    `Start(LuaFunctionRef, span)`, as section 2.11.
  - **Owner thread:** `Start`, `Resume`, `Close`, `Dispose`, `Retain` and
    the handles among the values check it; the status and `co[i]` of a
    primitive value read managed fields only.
- **Host `Yield`** (milestone 7) works in a coroutine of `Start` without any
  change: the trampoline's `lua_yield` ends the step with `XLUA_YIELD`, the
  host function's results are the yielded values, and the next resume's values
  become its results in Lua. `CanYield` is true there.
- **Drivers** as section 2.11, all resuming through `LuaReply.ResumeWith`
  (one `Resume` with the reply's values as a span):
  - `Run(handler)`: an iterator; for each yield it steps the handler's
    enumerator to its end and yields each of its values on (so under Unity's
    `StartCoroutine` a handler may `yield return new WaitForSeconds(...)`),
    then resumes. An error throws from `MoveNext`.
  - `RunAsync(handler, ct)`: an `async Task` awaiting a `ValueTask` handler.
    The default `await` continues on the `SynchronizationContext` it started on,
    which is what section 2.11 asks; a handler's `ConfigureAwait(false)` does
    not leak. Cancellation is checked before each request and after each
    handler; then the coroutine is closed and the task ends cancelled.
  - `RunAwaitable(handler, ct)` under `UNITY_2023_1_OR_NEWER`: an `async
    Awaitable` method; Awaitable has no context, so after a handler that left
    the main thread it awaits `Awaitable.MainThreadAsync()`.
  - **The reply does not own handles:** a table the handler creates for the
    reply stays the handler's to dispose (question 64).
  - The scripting define `XLUA_NO_COROUTINE_DRIVERS` leaves the drivers file
    out (question 62).
- **Tests:**
  - `Test/Core/CoroutineTests.cs` (13 tests; `run_core.py` expects 79):
    steps and values (start arguments, resume values, spans, `MaxValues`, a
    function handle, a function without yields), yields across `pcall`, a
    metamethod and nested functions, the host `Yield`, a coroutine that looks
    at itself while it runs, errors (message, traceback, `ErrorValue`, host
    exceptions, failed starts and their `__close`), `Close` and `Dispose`
    (`__close` once, errors, a looping `__close` at the limit, a faulted
    coroutine, a script's coroutine left alive), handles (borrowed values,
    `Retain`, script-made coroutines, a coroutine the script finished behind
    C#'s back, the main thread, `Pairs`, table reads, host function arguments,
    another script's coroutine, script disposal), the limits per step
    (instructions: twenty steps under a limit their sum exceeds, the exact
    count per step, coroutines inside a step, a stale hook; time: five 60 ms
    steps under 200 ms, the watchdog; `Interrupt`; memory, also caught by
    `pcall` inside), steps nested in host functions under the outer budget,
    the thread check, the `IEnumerator` driver stepped by a loop, the `Task`
    driver with a single-thread `SynchronizationContext` (a
    `ConfigureAwait(false)` handler, a synchronous one, cancellation, a script
    error, no context), the allocation probe and the cost. Tests of milestones
    6 to 8 that expected a coroutine to be `Other` without a handle now expect
    a handle.
  - **Allocation probe:** 30,000 resumes with primitive arguments and results
    (two values in, three out, a span) allocate 0 bytes on Mono and .NET.
    Reported: `Start` and `Dispose` 184 to 192 bytes (the object and its value
    array); a run of ten yields with `Run` 168 to 176 bytes, with `RunAsync`
    and handlers that complete at once 104 to 138 bytes (the state machine,
    the reply); a handler that really awaits adds what `async` costs.
  - `core_smoke.c` `check_coroutines` and `core_regression.py`
    `core_coroutines`: every kind and refusal, `XLUA_YIELD` and the dead flag,
    thread handles and their statuses (suspended, finished, failed, running,
    the main thread), `coroutine.status` of a handle passed back, errors with
    the coroutine's traceback and the error value, `__close` (also with a
    failed coroutine's error), the host yield, the limits per step with small
    slices, a resume nested in a host function under the outer budget and of
    the coroutine that runs it, the memory limit.
  - `Test/Fuzz/fuzz_core.c`: a coroutine result gets up to three steps from C
    (bit 6 makes the third a close), and bit 3 starts `f` as a coroutine;
    three seeds (`co_handles.lua`, `co_close.lua`, `co_host.lua`).
  - **Unity:** `Test/UnityMatrix/core_only_check.py`'s probe drives the three
    drivers from `EditorApplication.update` in a real editor: `Run` stepped once
    per update, `RunAsync` on Unity's `SynchronizationContext` with a handler
    that leaves the main thread, and on 2023.1 and later `RunAwaitable` with
    `AwaitableCompletionSource`s that later updates complete, a cancellation,
    a script error, and `Awaitable.NextFrameAsync` in a handler (reported:
    it completes in edit mode, batch mode included). `LuaScriptSample.cs` uses `Run` and, under the define,
    `RunAwaitable`, so `asmdef_compile_check.py` and `editor_compile_check.py`
    compile both with and without Awaitable.
- **Results** (macOS arm64 host, before merging main `446dea2b`; the run on the
  merged tree is below):
  - `run_core.py`: 79 tests pass on Mono 6.14.1 and .NET 10 against the core
    library (Lua 5.5.1 and 5.4.9, with every export and with the managed
    ones, and with `XLUA_BUFFER=OFF`) and the API-check builds; `--bridge`
    80 on legacy 5.5.1 with `XLUA_LUASCRIPT=ON`, with and without
    `XLUA_BUFFER`. The same 79 on Mono against the ASan, UBSan and API-check
    core libraries.
  - `core_smoke.c` (also built with ASan and UBSan) and `native_regression.py`
    on every build; `check_exports.py` on every preset (core: 21 exports),
    `check_android_elf.py` (core 21 exports, legacy 290 and 293),
    `check_pinvoke_abi.py` (139 imports), `presets_check.py`, the managed suite
    (451 tests in every mode on legacy 5.5.1), `webgl_compile_check.py
    --package`, `asmdef_compile_check.py --core-only` and `--luascript`
    against Unity 2021.3.45f2 and 6000.3.26f1.
  - **In real editors** (`core_only_check.py`, the core 5.5.1 library): Unity
    6000.3.26f1 runs the `IEnumerator`, `Task` and `Awaitable` drivers
    (completion sources, the cancellation, the script error and
    `NextFrameAsync`); 2021.3.45f2 compiles without `Awaitable` and runs the
    other two.
  - **After merging main `446dea2b`** (section 8.11's on-time stops and
    `XLUA_LIMITS_INTERRUPT`): `run_core.py` 82 tests (with
    `TestCoroutineStopsOnTime`, below) on Mono and .NET against the core
    library (5.5.1 and 5.4.9, every export and the managed ones, `XLUA_BUFFER=OFF`)
    and the API-check builds, 84 with `--bridge` on legacy 5.5.1 with
    `XLUA_LUASCRIPT=ON` (with and without `XLUA_BUFFER`), 82 under ASan on Mono
    (5.5.1 and 5.4.9); `core_smoke.c` and `native_regression.py` (with
    `core_stop_latency`) on every build; `check_exports.py`, `check_android_elf.py`,
    `check_pinvoke_abi.py` (139 imports), `presets_check.py`, the managed suite
    (451 tests in every mode), `editor_compile_check.py` (7 editors, 60
    configurations: 413 ok, the 7 C# 12 ones skipped as before),
    `webgl_compile_check.py --package`, `asmdef_compile_check.py` (`--core-only`
    and `--luascript` on 2021.3.45f2 and 6000.3.26f1, `--source-gen` on 6000.3.26f1);
    `sanitize.sh` with `core-5.4.9 core-5.5.1` in `xlua-analysis:26.04` (ASan,
    UBSan, the API checks, `XLUA_WERROR=ON`); `fuzz_core` 180 s on each version
    without a finding (218,901 runs on 5.5.1, 234,256 on 5.4.9);
    `core_only_check.py` on Unity 6000.3.26f1 and 6000.6.6f1, the three drivers.
  - **`TestCoroutineStopsOnTime`** (added after the merge): a 50 ms time limit
    and `Interrupt` (also on a script without limits) stop a step within
    section 8.11's 100 ms slack, with and without an instruction limit, also
    in a coroutine the step runs in Lua. One run of main's
    `TestTimeLimitIsOnTimeWithAnInstructionLimit` (not a coroutine test) was
    123 ms late on .NET while the sanitizer container was building (34% of the
    memory free); it passed when run again on the idle machine.
  - **After merging main `6b37d258`** as well (the pre-public audit: runtime,
    generator and tools, no core file): `run_core.py` 82 and `--bridge` 84 on
    Mono and .NET, the managed suite (458 tests in every mode),
    `check_pinvoke_abi.py`, `asmdef_compile_check.py` `--core-only` and
    `--luascript` (6000.3.26f1) and `editor_compile_check.py` (413 ok, 7
    skipped) pass.
  - **Finding (main's code, not reproduced):** in one `--bridge` Mono run, while
    other sessions loaded the machine (load average about 100), section 8.11's
    `TestInterruptIsOnTime` saw an `Interrupt` of `while true do end` (no
    instruction limit, a 30 s time limit) arrive about 30 s late: the call
    threw `LuaInterruptedException`, so the stop was set, but the stop hook
    fired only when the deadline passed, although the watchdog arms again on
    every poll. No coroutine is involved, and this branch changes nothing on
    that path. Three runs each of the test against main `446dea2b`'s own tree
    and library and against this branch passed (load about 10), as did the
    full runs. Section 8.11's owner should look at it.
  - **Not run:** a player build with coroutines (Android, iOS Simulator), a
    phone, Windows and Linux hosts (the C# tests do not run in the Linux
    container, section 8.5), coverage.
- **Cost** (`TestCoroutineCost`, macOS arm64, Lua 5.5.1, no limits, the minimum
  of 7 runs of 200,000 steps; a resume with one integer and a yield of one):

  | ns per resume and yield | C# `Resume` | Lua `coroutine.wrap` |
  | --- | ---: | ---: |
  | .NET 10 | 237 to 238 (244 after the merge) | 74 to 79 |
  | Mono 6.14.1 | 412 to 416 (424 after the merge) | 75 to 77 |

  A step costs one `xlua_core_call` (a protected entry, the lookup of the
  handle, `lua_resume`, whose yield is a `longjmp` in Lua 5.4 and 5.5) plus
  the conversion of the values, about 80 ns over `LuaFunctionRef.Call<long>`
  on .NET (section 8.7: 137 to 139). After the merge every step is an entry
  with `XLUA_LIMITS_INTERRUPT` (section 8.11), which adds a few ns.
- **Sizes** (Android API 23 release build, NDK r30, arm64-v8a, stripped, zip
  deflate 6; [build/SIZE.md](../../build/SIZE.md#the-core-preset)): core 5.5.1
  270,008 to 271,320 bytes (+1,312) and 122,301 to 122,992 compressed (+691)
  against main `98b3bab5`; 5.4.9 255,296 to 256,608 and 114,999 to 115,666.
  Merged with main `446dea2b` (270,024 and 122,319): 271,336 (+1,312) and
  122,998 (+679); 5.4.9 256,624 and 115,675. Within section 6's budget
  (290 KB and 125 KB) with 2,002 bytes of the compressed budget left. The
  functions grow by 1,132 bytes: `call_body` +580 (the step is inlined into
  it), `describe_error` 328 and `call_error` 148 (out of `call_msgh`, -248),
  `co_status` 224, `xlua_core_switch` 92 (was `switch_thread`), the rest in
  `xlua_core_call`, `to_value`, `handle_body` and the trampoline. Legacy has
  the same size as main's (383,672 and 367,136; merged 383,688 and 367,152,
  5.5.1 the same as main `446dea2b`'s; 5.4.9 not measured on that main). **Managed:**
  `XLua.Core.dll` (core-only, Unity 6000.3.26f1 reference assemblies) 92,672
  to 104,960 bytes (+12,288; main `446dea2b` is 92,672 too): the drivers about 5,600 of it (99,328 without
  `LuaCoroutineDrivers.cs`), `LuaCoroutine` and the changes for coroutine
  handles the rest; with `Get<T>` and its reader table it was 107,520. IL2CPP
  code not measured (question 34).
- **Not done:** a lifetime budget per coroutine (decision 5); `Get<T>`; a
  phone measurement of the cost; IL2CPP code size (a `size_report.py` player
  run; `SizeProbeCore.cs` does not start a coroutine yet); the WebGL plugin
  (still no sandbox, question 20); an on-device coroutine check (the iOS
  simulator smoke of main `bebb8faa` could run one).
- **Questions for the owner:** 60 to 69 in section 9.

### 8.9 Milestone 10 as implemented (2026-10-18)

- **Files.** `build/xlua_core_buffer.c` holds the `buffer` library and two C
  exports; `Assets/XLua/Core/LuaBufferRef.cs` holds `LuaBufferRef` and the
  part of `LuaScript` (now a `partial` class) that creates, adopts and
  releases buffer handles. Edits elsewhere are small and marked: the library
  entry, the default library set, buffer results and arguments and
  `xlua_core_unref` in `xlua_core_sandbox.c`; buffer arguments and results
  in `LuaScript.cs` (`Raw`, `Read<T>`, `Fill` and the tuple reads); the
  buffer case of `LuaValue`; two imports in `LuaDLL.cs`.
- **CMake `XLUA_BUFFER`** (new; from the environment as well): `ON` in the
  core preset, `OFF` in legacy and minimal, which may turn it on together with
  `XLUA_LUASCRIPT=ON`; without the sandbox CMake refuses it. `OFF` leaves the
  library out. The C exports are in the file either way and then report
  `XLUA_ERRCONFIG`, so the export list (`XLUA_LUASCRIPT`'s part of
  `managed_imports.py`) does not depend on the option. The decision for
  legacy and minimal follows question 1 of section 8.4: the sandbox is opt-in
  there, so the buffer is too (question 22).
- **Opening it:** `LuaLibs.Buffer` (bit 5, `XLUA_LUALIBS_BUFFER`) in
  `LuaOptions.Libraries`. It is not in `LuaLibs.Safe`, and `Libraries = 0`
  (`Default`) opens the safe set without it: section 2.10 calls the library
  opt-in. A build without the library refuses the bit (`XLUA_ERRCONFIG`,
  `NotSupportedException`). Each environment gets a shallow copy of the
  library table like the other libraries.
- **The library**, written from Luau's API description (no Luau code):
  `create`, `fromstring`, `tostring`, `len`, `readi8`/`readu8`/`readi16`/
  `readu16`/`readi32`/`readu32`/`readf32`/`readf64`, the eight `write` forms,
  `readstring`, `writestring`, `copy` and `fill`. `readbits` and `writebits`
  are not in section 2.10's list and are not implemented (question 23).
  - **Storage:** a full userdata (`lua_newuserdatauv` with no user values) of
    at most 1 GiB (`XLUA_BUFFER_MAX`, Luau's limit), allocated through the
    state's allocator, so `MemoryLimit` counts it and a refusal is the usual
    catchable "not enough memory". `create` zero-fills.
  - **Offsets** are 0-based integers and every access is bounds-checked with
    unsigned arithmetic, so no offset or count overflows: "bad argument #2 to
    'readu32' (buffer access out of bounds (offset 13 and 4 bytes in a buffer
    of 16 bytes))". Counts must not be negative; `writestring`'s count is at
    most the string length. `copy`'s source offset defaults to 0 and its count
    to the rest of the source; `fill`'s count to the rest of the buffer.
    Overlapping copies behave as through a temporary buffer.
  - **Byte order:** little-endian, any alignment. Loads and stores are byte
    by byte, which compilers merge, so the host's byte order and alignment
    rules do not matter.
  - **Integers stay lossless:** reads return Lua integers (`readu32` up to
    4294967295, `readi32` down to -2147483648). Writes take a Lua integer
    through `luaL_checkinteger` and keep its low 8, 16 or 32 bits, as Luau
    does (`writei8(b, 0, 0xabcd)` stores 0xcd). **Deviation from Luau:** a
    float without an exact integer value (1.5, NaN, infinity) is refused with
    Lua's "number has no integer representation", as every Lua 5.4 library
    function does, where Luau truncates (question 24). Integral floats and
    numeric strings are accepted, as `luaL_checkinteger` accepts them.
  - **Floats:** `writef32` rounds to the nearest float; values from FLT_MAX
    plus half an ulp up become an infinity, as IEEE 754 rounds, computed
    without the out-of-range conversion that C leaves undefined. NaN stays
    NaN (its payload may change, as Luau documents).
  - **The metatable** is locked (`__metatable = false`, so `getmetatable(b)`
    is `false`) and has `__name = "buffer"`, so `tostring(b)` is
    `buffer: 0x...` and errors say "attempt to index a buffer value" and
    "buffer expected, got table". It has no `__gc` (nothing to finalize), no
    `__index` and no `__len`. `type(b)` is `"userdata"`, not Luau's
    `"buffer"`: Lua 5.x has no way to add a type (question 25).
  - **The C budget (section 3.11):** operations on 1 KiB or more charge one
    instruction-equivalent per 32 bytes, `fill`, `copy`, `writestring`,
    `fromstring` and `create` in chunks of 128 KiB, each charged before it
    runs, so `InstructionLimit`, `TimeLimit` and `Interrupt` stop a large
    operation within 128 KiB. `tostring` and `readstring` copy in one
    `lua_pushlstring` and are charged in advance. Smaller operations cost
    nothing beyond their VM instructions. A 1 MiB fill costs 32,768. The rate
    is a choice (memset and memmove move about 32 bytes in the time of one
    VM instruction on the test machines); question 26.
- **Native ABI:** `XLUA_CORE_VERSION` stays 3: no struct changed.
  - `int xlua_core_newbuffer(lua_State *L, int32_t len, int32_t *ref, void
    **data)` (protected, memory only) creates a zero-filled buffer and an
    owning handle (a registry reference) and returns its bytes. `XLUA_ERRCONFIG`
    for a length outside 0 to 1 GiB or a state without `LuaLibs.Buffer`,
    `XLUA_ERRMEM` for the memory limit. It is not charged to the C budget.
  - `void *xlua_core_tobuffer(lua_State *L, int32_t ref, int32_t *len)`
    (non-raising): the bytes and length behind an owning handle, else NULL.
  - **Results:** a buffer result of `xlua_core_call` (within `results_cap`) is
    `XLUA_T_BUFFER` with `len` its length and `u.i` a new owning handle, which
    the caller releases; before, a buffer would have been `XLUA_T_OTHER`.
  - **Arguments:** `XLUA_T_BUFFER` with `u.i` an owning handle pushes that
    buffer; anything else gives `XLUA_ERRHANDLE` ("argument 1 is not a valid
    buffer handle").
  - **`xlua_core_unref`** also releases buffer handles (it checks the value's
    metatable, so it never frees another registry slot).
  - `call_body` now makes sure of three free stack slots before converting
    the results (the string results' anchors needed one already).
- **C#:**
  - `LuaScript.NewBuffer(int length)`, `NewBuffer(ReadOnlySpan<byte>)` and
    `LuaScript.MaxBufferLength`; `LuaScript.LiveHandles` (section 2.3) counts
    buffer handles until milestone 8 adds tables and functions.
  - `LuaBufferRef` as section 2.10, with these details: `Script`, `IsValid`,
    `Length`, `AsSpan()`, `CopyTo(Span<byte>)`, `CopyTo(int offset,
    Span<byte>)`, `CopyFrom(ReadOnlySpan<byte>, int offset = 0)`,
    `ToArray()`, `Dispose()`, and reads and writes named as .NET's
    `BinaryReader` names them: `ReadSByte`, `ReadByte`, `ReadInt16`,
    `ReadUInt16`, `ReadInt32`, `ReadUInt32`, `ReadSingle`, `ReadDouble` and
    the `Write` forms, little-endian, bounds-checked
    (`ArgumentOutOfRangeException`). They read the buffer's memory directly
    (the pointer is cached at adoption: a buffer never moves or resizes), so
    they cost no P/Invoke and allocate nothing.
  - **Handles** are a script, a slot and a generation, as section 2.9 plans
    for tables: a per-script slot array with a free list holds the registry
    reference, the pointer and the length; `Dispose` releases the reference
    and bumps the generation, so copies and a second `Dispose` are harmless.
    After the script is disposed every handle is invalid and `Dispose` does
    nothing. Milestone 8 can extend the table to other handles. Equality is
    the same buffer (two handles to one buffer are equal).
  - **Lifetime of `AsSpan()`:** valid while the handle is valid; the span
    cannot detect a later `Dispose`, as section 2.10 says. The accessors check
    the owner thread (`CheckThread`) like the rest of `LuaScript`.
  - **In calls:** a `LuaBufferRef` converts to `LuaValue` (`LuaType.Buffer`,
    the script in the object field, slot and generation in the payload;
    `AsBuffer()` returns the handle) and passes as an argument; a buffer of
    another script is an `ArgumentException`, a disposed one
    `ObjectDisposedException`. `Call<LuaBufferRef>` and `Call<LuaValue>`
    adopt a buffer result as an owning handle that the caller disposes, as do
    the span and tuple forms; nil reads as `default(LuaBufferRef)`. Every other
    `T` releases the native handle before it converts or throws, and a tuple
    read that fails releases the rest and disposes what it had read, so a
    failed read never leaks a buffer.
  - **`byte[]`:** `LuaValue.FromBytes` (milestone 6) already passes bytes as a
    Lua string, and `Call<byte[]>` reads one. `Return(ReadOnlySpan<byte>)` is
    milestone 7's, `TryCopyBytes` milestone 8's.
- **Tests:**
  - `Test/Native/core_sandbox_cases.txt`: 53 new cases (131 in all): one
    that the default state has no `buffer`, and 52 under a new directive,
    `--- libs: buffer`, which runs a case in a second state with the library
    and skips it in a build without it: the function list and
    per-environment copies, creation and its limits, `fromstring`/`tostring`
    with every byte, argument types, little-endian integers and floats,
    unaligned access, signed and unsigned reads and their extremes, the low
    bits of integer writes, the refused fractions, NaN and infinities, NaN,
    infinities and negative zero in floats, `writef32`'s rounding at FLT_MAX,
    the midpoint and the smallest denormal, the bounds of every accessor at
    every edge (also `math.maxinteger` and `math.mininteger` offsets),
    `readstring`/`writestring`, `copy` with its defaults, overlap in both
    directions and a 300 KB overlapping copy, `fill`, the locked metatable,
    identity, the memory limit (caught and not), and `fill`, `copy` (inside
    `pcall`) and `tostring` stopped by the instruction limit.
  - `core_smoke.c` (`check_buffer`) and `core_regression.py`
    (`core_buffer`): the exports, handles as arguments and results, invalid
    and released handles, environments that are not buffers, results beyond
    `results_cap`, release and collection, the memory limit, a state without
    the library, no charge for `xlua_core_newbuffer`, and
    `XLUA_EXPECT_BUFFER=ON` or `OFF`.
  - `Test/Core/BufferTests.cs`: 8 tests (35 in all): the library opt-in,
    buffers from Lua, the layout through every accessor and `AsSpan` in both
    directions, bounds, handles (disposal, copies, generations, equality,
    `LuaValue`, results spans, another script, script disposal), release of
    buffer results on failed reads (memory checked after 700 failed reads of
    1 MiB buffers), the memory, instruction and time limits, and an allocation
    probe.
  - `Test/Fuzz/fuzz_core.c` opens the library (bit 4 of the first byte leaves
    it out), passes buffer results back and checks their handles; two seeds
    and the function names in `lua.dict`.
  - `presets_check.py`: `XLUA_BUFFER` per preset, refused without the
    sandbox, `OFF` in core.
- **Results** (macOS arm64; the sanitizers in the Linux arm64 container
  `xlua-analysis:26.04`):
  - `native_regression.py` (with `core_buffer`), `core_smoke.c`,
    `check_exports.py` and `run_core.py` (35 tests on Mono 6.14.1 and .NET 10;
    36 with `--bridge`) pass on core 5.5.1 and 5.4.9, on core 5.5.1 with
    `XLUA_BUFFER=OFF` (the 52 cases skipped, the refusals checked), and on
    legacy 5.5.1 with `XLUA_LUASCRIPT=ON`, with and without `XLUA_BUFFER`.
    The allocation probes allocate 0 bytes: 70,000 primitive calls as before,
    and 2,000 rounds of a buffer result read through its handle, a buffer
    argument and a `NewBuffer`.
  - `sanitize.sh` with `VERSIONS="core-5.4.9 core-5.5.1"` and
    `XLUA_WERROR=ON` (ASan, UBSan, the Lua API checks): `check_exports.py
    --preset core`, `native_regression.py` and `core_smoke.c` with every
    export, and the exact check and `core_smoke.c` with the 21 managed
    exports.
  - `check_android_elf.py --preset core` passes on the four Android
    libraries (21 exports); `check_pinvoke_abi.py` (140 imports),
    `presets_check.py`, `Tools/presets.py --check` and the site's link check
    pass.
  - `fuzz_core` with the buffer library ran 180 s on Lua 5.5.1 (283,435 runs,
    peak RSS 1.7 GB) and on 5.4.9 (315,303 runs) without a finding.
- **Sizes**, Android API 23 release build (NDK r30, default flags),
  stripped, Info-ZIP deflate 6, main `65f7ea17` against this branch:

  | Lua | ABI | main stripped | branch | main zip | branch |
  |---|---|---:|---:|---:|---:|
  | 5.5.1 | arm64-v8a | 255,720 | 263,544 (+7,824) | 116,214 | 119,272 (+3,058) |
  | 5.5.1 | armeabi-v7a | 172,980 | 177,884 (+4,904) | 102,466 | 105,248 (+2,782) |
  | 5.4.9 | arm64-v8a | 240,992 | 248,832 (+7,840) | 108,534 | 111,829 (+3,295) |
  | 5.4.9 | armeabi-v7a | 164,948 | 169,900 (+4,952) | 97,224 | 100,008 (+2,784) |

  Within section 6's budget (290 KB and 125 KB on arm64-v8a, Lua 5.5.1), with
  about 26 KB and 5.7 KB left. With `XLUA_BUFFER=OFF` core is 208 bytes over
  main (the export stubs); legacy is unchanged. Section 6 estimated 5 KB for
  the library; the measured 7.6 KB is 4.7 KB of functions plus messages, the
  function table and unwind data ([SIZE.md](../../build/SIZE.md#the-buffer-library)).
- **Not done:** borrowed buffer handles in callbacks (`LuaCall.GetBuffer`,
  milestone 7) and `Retain` (milestone 8); buffers for `LuaEnv` (it ignores
  `LuaLibs.Buffer`); the WebGL plugin (no sandbox there, question 20);
  `readbits`/`writebits`; 64-bit integer accessors.
- **Questions for the owner:** 22 to 27 in section 9.

### 8.10 Milestone 11, first part (2026-10-18)

The parts of milestone 11 that do not wait for milestones 8 and 9, on main
`1ff0609d` (milestones 6, 7 and 10): the size variants and the stripping
test, a core-only player, the luban fixture and the documentation. The fuzz
target exists since milestone 5 (`Test/Fuzz/fuzz_core.c`); CI wiring, iOS and
the release checks are still to do.

- **Files.**
  - `Test/MobileSmoke/size_report.py`: the variants `core-5.5` (and
    `core-5.4`), `core-5.5+nodefine` and `mixed-5.5`, the configurations
    `core` and `core-high` (Android, Minimal and High stripping) and `core`
    for iOS, and `--one-unity` (wait while another editor runs). The variants
    install the UPM package with one library as its plugin: a package cannot
    carry plugin variants (they have no `.meta`), so the core library becomes
    the package's plugin, as a core-only project would install it. The
    defines go into the Player Settings in a launch of their own
    (`SmokeBuild.ApplyDefines`), so the build launch compiles the Editor with
    them and `XLua.Editor`'s build callbacks are not loaded, as in a project
    that has the define already.
  - `Test/MobileSmoke/SizeProbeCore.cs`: the probe of section 6 without
    tables and coroutines (milestones 8 and 9): a `LuaScript` with limits and
    the `buffer` library, one `Register<long, long, long>`, a chunk, two
    `Call<long>`, a `NewBuffer`, `Load<string>`.
  - `Test/MobileSmoke/core_only_player_check.py`: section 6's check on an
    APK or `.app` (`ScriptingAssemblies.json`, `global-metadata.dat`, the
    library's exports, the stripped assemblies with `--project`, the iOS link
    map with `--link-map`). The stripped `libil2cpp.so` has no symbols, so the
    stripped assemblies stand in for section 6's symbol check. `--report`
    prints the findings for players that have the bridge.
  - `Test/MobileSmoke/CoreSmoke.cs` and `prepare.py --core`: the core-only
    player smoke (below).
  - `Test/Core/LubanFixtureTests.cs` and `Test/Core/Fixtures/luban-lua-lua/`
    (section 7.5; `SOURCE.md` there); `run_core.py` expects 52 tests and
    passes the fixture folder in `XLUA_CORE_FIXTURES`; luban's release is in
    `Tools/dependencies.py`.
  - The user guide [LuaScript.md](../../Assets/XLua/Doc/LuaScript.md) (also
    on the site) and the "LuaScript" section of
    [Security.md](../../Assets/XLua/Doc/Security.md#luascript), from section
    5's draft, with the known issues below.
- **The stripping test** (section 6; question 12). Unity 6000.3.21f1, release
  ARM64 IL2CPP, Lua 5.5.1, numbers in
  [SIZE.md](../../build/SIZE.md#luascript-in-players):
  - **Unity's managed stripping drops the unreferenced `XLua` assembly**, at
    Minimal (Unity's default for IL2CPP) as well as at High: with
    `XLUA_LUASCRIPT` instead of `XLUA_CORE_ONLY` the player has the same
    `libil2cpp.so` (within 2 bytes) and `global-metadata.dat`, the stripped
    assemblies are `XLua.Core.dll` alone, and the metadata names no `XLua`
    type. `ScriptingAssemblies.json` still lists `XLua.dll` and
    `globalgamemanagers` lists its scripts: +1,288 bytes of APK. Minimal keeps
    every method of an assembly that is linked, but an assembly nothing
    references is not linked. Unity 2021.3.45f2 at Minimal stripping gives the
    same result (the same IL2CPP code within a byte, the same metadata, +640
    bytes of names).
  - **Sizes**, APK over an empty project, Minimal / High: core-only +223,530 /
    +193,802 (legacy `LuaEnv` +1,447,742 / +1,304,870); `LuaScript` added to a
    `LuaEnv` player +87,580 / +63,348. Section 6's budget of 200 KB is
    exceeded by 23,530 bytes at Minimal stripping (question 34); the native
    library (121,686 in the APK) is within its own.
  - `core_only_player_check.py` passes on the core-only players and reports
    the bridge in the `mixed-5.5` ones.
- **The core-only player smoke.** `prepare.py --package --core` makes a
  project with the package (the core library as its plugin) and
  `CoreSmoke.cs`; `XLUA_CORE_ONLY` comes from `SmokeBuild.ApplyDefines`. The
  player checks `Load` and `Call` (every result type used, 64-bit integers,
  multiple results, a missing function, a syntax error), `Register` (a typed
  shorthand, a `LuaCallback`, a C# exception caught by `pcall`), the sandbox
  (no `io`, `os`, `debug`, `package`, `dofile`, `print`; the locked string
  metatable; binary chunks refused by `Load` and by `load`), the buffer
  library and `LuaBufferRef` both ways, the memory limit (and `pcall`
  catching it), the instruction limit (also through `pcall`), the time limit,
  `Interrupt` from a timer thread, and that no `XLua` assembly is loaded.
  - **Emulator** (`xlua16k`, Android 16, 16 KB pages): Development with
    Minimal stripping, release Minimal and release High all print
    `XLUA_NG_CORE_SMOKE_PASS`; `core_only_player_check.py` passes on all three
    APKs. High stripping needs no `link.xml` for `LuaScript`.
  - **Phone** (moto g play 2024, Android 14, 4 KB pages): release Minimal and
    High pass (2026-10-18; the phone log is not in the repository).
  - The same checks pass on the macOS host (Mono, the core library) when
    `CoreSmoke.cs` is compiled against `Assets/XLua/Core` with a stub of
    `UnityEngine`.
- **The luban fixture** (section 7.5, owner decision 10): luban v5.1.0's
  released CLI (pinned and hash-checked; its sources and build scripts are not
  used) generated `lua-lua` code and `lua` data for a schema and data written
  for xlua-ng: 4 tables, 271 records, 50 KB of Lua. `TestLubanFixture` loads
  them through `require` with `MemoryLimit = 16 MB` and `InstructionLimit =
  50,000,000` and checks record counts, an FNV-1a hash of every table's
  canonical form and 18 values against luban's JSON export of the same data.
  It passes on Mono and .NET, Lua 5.5.1 and 5.4.9: about 7,000 instructions,
  a memory peak of 308 KB (5.4.9) and 338 KB (5.5.1), 1 to 3 ms.
  - **Section 7.5's question:** the generated files use no `setmetatable`,
    no `__gc` and no globals across files; they load in the core sandbox
    unchanged.
  - **A luban bug:** the `lua` data target formats floats with the current
    culture, so under a decimal-comma locale it writes `weight=90,25`, which
    Lua reads as `weight=90` plus a stray list element (question 37).
    `regenerate.py` runs luban with the invariant culture.
- **Finding: `TimeLimit` and `Interrupt` are late while an `InstructionLimit`
  is set.** Host measurements (macOS arm64, Mono, core 5.5.1, `while true do
  end`, 20 runs each):

  | Limits | Stop after |
  | --- | --- |
  | `TimeLimit` 100 ms alone | 106 to 131 ms |
  | `TimeLimit` 100 ms and `InstructionLimit` 3e9 | 100 to 747 ms |
  | `TimeLimit` 100 ms and `InstructionLimit` 1e11 | 118 to 2,092 ms |
  | `Interrupt()` at 50 ms and `InstructionLimit` 3e9 | 50 ms once, otherwise 2,162 to 4,233 ms |
  | `Interrupt()` at 50 ms, no limit | never: `Interrupt` does nothing (below) |

  The emulator's smoke saw 966 ms for a 100 ms limit in one run (release,
  High), the phone 222 to 243 ms. A likely cause, not confirmed with a
  debugger: with an instruction limit the count hook is armed with a slice of
  up to 2^30 instructions, and the running thread decrements `L->hookcount`
  in `luaG_traceexec` on every instruction; the store of `hookcount = 1` by
  `arm_stop_remote` on the watchdog's or `Interrupt`'s thread races with that
  read-modify-write and is usually lost. The stop then waits for the slice's
  end (about 2 s at this machine's speed). The watchdog re-arms every poll
  (so a time limit is late by hundreds of milliseconds), `Interrupt` arms once.
  Item 5 of the security statement is broken by this; Security.md lists it as
  a known issue. Question 35. *Root-caused and fixed: section 8.11.*
- **Finding: `Interrupt()` needs a limit.** `xlua_core_interrupt` acts only
  while `entry_active`, which `xlua_core_begin` sets only for a state with an
  instruction or time limit (`xc->limited`). A `LuaScript` with
  `new LuaOptions()` cannot be interrupted; the documentation of `Interrupt`
  says it stops the running call. Question 36. *Fixed for `LuaScript`:
  section 8.11.*
- **Not done:** the luban data through `LuaEnv` in the managed suite (the
  suite runs every case in every binding mode; left out as not cheap); iOS
  size and smoke players; `core-5.4` and `core-5.5+srcplugin` players;
  `size_breakdown.py` on the core players; the CI wiring of section 7.6 and
  the 2% size check; WebGL (no sandbox in its plugin, question 20).
- **Questions for the owner:** 12 (updated), 34 to 38.

### 8.11 Late stops with an instruction limit, and `Interrupt` without limits (2026-10-18)

The two findings of section 8.10, on main `c74f4bbc`; merged with main
`98b3bab5` (milestone 8). Evidence, tables and sources:
[core-stop-latency.md](../research/core-stop-latency.md).

- **Cause.** While an instruction limit runs, `luaG_traceexec` decrements
  `L->hookcount` before every instruction with a plain load and store
  (`ldebug.c:939` in 5.4.9, `947` in 5.5.1). The `hookcount = 1` that
  `arm_stop_remote` stores from the watchdog's or `Interrupt`'s thread is
  overwritten by the running thread's next store: a diagnostic read it back
  as 1 right after the store and as the old count 1 µs later, in 19 of 20
  armings on 5.5.1 and 20 of 20 on 5.4.9. `basehookcount = 1` survives, so
  the stop came at the end of the slice, up to 2^30 instructions (1 to 5 s).
  Without an instruction limit no hook runs, nothing writes the count, and
  the time limit held. `Interrupt` armed once and waited for the slice; the
  watchdog arms on every poll, so a time limit was late by 0.1 to 2 s.
- **Fix** (`build/xlua_core_limits.c`, no vendored Lua change):
  - `arm_stop_remote` also sets `LUA_MASKLINE`. Only the arming thread
    writes `hookmask`, so the store is not lost, and the running thread
    calls the hook at its next backward jump, new line or Lua call. The hook
    then replaces the remote arming with its own thread's count-1 stop hook.
  - Count slices are at most 2^20 instructions. Any stop that misses the
    thread is late by one slice at most (1 to 5 ms), and with an
    instruction limit the hook reads the native deadline at every slice end.
  - `XLUA_LIMITS_INTERRUPT` (flag 4 of `xlua_core_set_limits`) runs the
    entry protocol for every outermost call, so `Interrupt` finds the running
    thread without a limit. `LuaScript` sets it on every state (question 36);
    `LuaEnv` does not, and its `Interrupt` still needs a limit, as
    documented.
- **Latency** (macOS arm64, `Test/Native/core_stop_latency.c`, a 50 ms
  time limit with a 12.5 ms watchdog period, Interrupt 20 ms into the call,
  six endless chunks; milliseconds after the deadline or the interrupt).
  Before: main, core 5.5.1 and 5.4.9 and LuaEnv's entry on 5.5.1, 5 runs per
  case. After: the merged tree on a quiet machine (load average under 7),
  core and LuaEnv's entry on 5.4.9 and 5.5.1, 20 runs per case:

  | Case | before | after |
  |---|---|---|
  | time limit, no instruction limit | median 3.8 to 11.9, max 18 | median 6.6 to 11.7, max 17 |
  | time limit and an instruction limit | median 10 to 1,571, max 2,160 | median 0.3 to 4.0, max 9.7 |
  | Interrupt and an instruction limit | median 2,148 to 5,323, max 5,381 | median 0.1 to 0.3, max 2.5 |
  | Interrupt, watchdog polling, instruction limit | median 30 to 761, max 2,169 | median 0.0 to 0.2, max 6.3 |
  | Interrupt without any limit (`LuaScript`) | never stops | median 0.1 to 0.2, max 0.3 |

  While other agents loaded the machine (load average 30 to 320), single
  stops came up to 125 ms late with an instruction limit and up to 1 s late
  with a time limit alone (a path this change does not touch): the threads
  were not scheduled. The suites' checks therefore allow 100 ms.
- **Cost:** `flag_bench.c` on the host (legacy 5.5.1, five interleaved
  rounds) shows no change beyond the noise with any limit (geometric means
  -1.1% to +0.6%, single cases ±4% both ways). `XLUA_LIMITS_INTERRUPT` costs
  about 6 ns per `xlua_core_call` (98 to 104 ns for a one-argument call) and
  1 ns per host function call, the entry's lock and stores; a state without
  the flag pays nothing. `benchmark.py` (gencode, `XLUA_BENCH_LIMITS`
  `instructions` and `time`, against main `98b3bab5`): geometric means +0.8%
  and +1.5%, the largest moves in cases the change does not reach.
- **Tests:** `core_stop_latency.c` (all cases, both entries; fails a stop
  later than 50 ms); `core_stop_latency` in `core_regression.py` and
  `core_stop_latency_env` in `native_regression.py`
  (`Test/Native/stop_latency.py`: three runs per case, bound 100 ms);
  `TestTimeLimitIsOnTimeWithAnInstructionLimit` and `TestInterruptIsOnTime`
  in `Test/Core/StopLatencyTests.cs`, and with `--bridge`
  `TestLuaEnvStopsOnTimeWithAnInstructionLimit` (bound 100 ms; `run_core.py`
  expects 68 tests, 70 with `--bridge`). All of them fail on main: stops 0.1
  to 5.4 s late, and Interrupt without limits does nothing. The suites pass
  on the merged tree (macOS arm64; core and legacy, Lua 5.4.9 and 5.5.1;
  `run_core.py` on Mono and .NET, `run_managed.py --gen-code
  --gen-code-minimize` on legacy 5.5.1, `core_watchdog_race` three times per
  version), and so do `sanitize.sh` (ASan, UBSan) for `core-5.4.9` and
  `core-5.5.1` and `webgl_compile_check.py --package`. ThreadSanitizer
  reports only the races on the hook fields that were there before.
- **On target** (rechecked the same day, the core-only player smoke with a
  latency distribution per case,
  [core-stop-latency.md](../research/core-stop-latency.md#on-target-2026-10-18)):
  before the fix the players showed the same lost arming on the moto g play,
  the `xlua16k` AVD and the iOS Simulator (Interrupt with an instruction limit
  2.4 to 12.2 s late, a time limit with one up to 1.2 s, Interrupt without
  limits never). After it the phone stops every Interrupt within 2.9 ms and
  every time limit within 21 ms (12.3 ms with an instruction limit); the
  Simulator is within 92 ms, and the AVD's single stops scatter up to 316 ms
  on a loaded host, in cases the fix does not touch as well (median 0.5 to
  10 ms).
- **Not done:** the ThreadSanitizer races of question 14 remain
  (the arming is still a cross-thread write to the hook fields);
  `coverage.sh` not rerun.
- **Questions for the owner:** 50 to 52; 35 and 36 are answered here.

### 8.12 C loops under `__len`, and handle errors under `xpcall` (2026-10-18)

The two HIGH findings of the pre-public audit
([pre-public-audit.md](../research/pre-public-audit.md), section 6), found
by reading the code on main `6b37d258`, reproduced and fixed the same day,
merged with main `4af16fdd` (milestone 9).

- **C loops whose length a script chooses** (Lua 5.4.9 and 5.5.1). `tinsert`
  and `tremove` take their length from `luaL_len`, which honours `__len`, and
  loop in C without the ticks of section 3.11 (`ltablib.c:82-125` in 5.5.1,
  the same shape in 5.4.9). `core_setmetatable` refuses only `__gc`. Nil
  stores allocate nothing, so `table.remove(setmetatable({}, {__len =
  function() return math.maxinteger - 1 end}), 1)` ran up to 2^63 steps that
  no hook, limit, watchdog or `Interrupt` could stop. Reproduced on core
  5.5.1 and 5.4.9 with an instruction limit of 100,000: the call ran until
  the test killed it (20 to 30 s). Three more of the same kind:
  - `table.sort` with C metamethods (`__index = rawlen`, `__newindex =
    rawequal`) and `__len` = 2^31 - 2: about 2^31 log 2^31 steps without
    memory; it ran until killed;
  - `table.concat` with `__len` and `__index = tostring`: bounded only by
    `MemoryLimit` (the unfixed build stopped with a memory error at the
    sandbox's 16 MB, not at the instruction limit);
  - `string.rep("", math.maxinteger)` on **5.4.9 only**: `str_rep` makes
    n - 1 empty copies (`lstrlib.c:163-177`); 5.5.1 returns `""` at once for
    two empty strings (`lstrlib.c:152`). It ran until killed.
- **Fix** (`ltablib.c` of both versions, `lstrlib.c` of 5.4.9; recorded in
  `build/LUA_PATCHES.md`): a `budget_step` macro charges `BUDGET_TICK` every
  4,096 steps, in `tinsert`'s and `tremove`'s loops, `tconcat`'s loop and
  `partition`'s two loops and outer loop (through a counter that `sort`
  passes down `auxsort`; a charge per interval before the work would leave
  the first partition of 2^31 elements unchecked). 5.4.9's `str_rep` gets
  5.5's early return. About 20 lines per version. `__len` stays allowed
  (question 70). `table.unpack` needs nothing: `lua_checkstack` bounds it.
- **Handle operations under `xpcall`** (`xlua_core_handle`,
  `build/xlua_core_sandbox.c`). The body ran under `luaD_rawrunprotected`,
  which saves only `errorJmp` and `nCcalls`. Inside a host function that a
  script called through `xpcall(host, handler)`, `L->errfunc` is the
  script's handler, so a handle operation that raises (SET with a nil or
  NaN key; NEXT with an invalid key) ran script code from inside C#,
  contrary to section 8.7 ("It runs no Lua code (raw access only)"). A handler
  that raises recurses until Lua's "error in error handling", and the
  longjmp then lands with `L->ci` on a dead handler frame. Reproduced with
  `core_smoke.c` and `Test/Core` on 5.5.1: the handler ran (430 times when it
  raised), the operation returned `LUA_ERRERR` instead of `XLUA_ERRRUN`, and
  the host function's `lua_gettop` changed; in C# the errors never reached
  the host's `catch` and `xpcall` returned "error in error handling". A
  looping handler under an instruction limit stopped the whole call. ASan
  and UBSan with `LUAI_ASSERT` and `LUA_USE_APICHECK` (macOS arm64,
  RelWithDebInfo) reported no memory error: `precallC` restores `L->ci`
  from its local copy when the host function returns, so the stale frame
  lasts until then, and the use-after-free the audit thought plausible was
  not shown [C for the stale frame, U for corruption].
- **Fix:** the body runs under `luaD_pcall(L, handle_body, &a,
  savestack(L, L->top.p), 0)`: no message handler, and on an error `L->ci`,
  `allowhook`, `errfunc` and the top are restored as `lua_pcall` restores
  them. It was the only direct `luaD_rawrunprotected` call in the
  `xlua_core*.c` files (also after milestone 9).
- **Cost:** `luaD_pcall` saves three fields more than
  `luaD_rawrunprotected`. The table loops, best of 15 runs, core 5.5.1 and
  5.4.9 on macOS arm64, with and without an instruction limit: no change
  beyond the run-to-run noise of ±5% (insert and remove at the front of 2,000
  elements, 200 times: 3.7 ms before and after; sort of 200,000 numbers: 51
  to 61 ms either way; concat of 200,000 strings, 5 times: 13 to 15 ms).
  Size, Android arm64 (NDK 30, release, API 23), stripped and zip deflate
  6: core 5.5.1 +368 and +200 bytes, core 5.4.9 +368 and +161, legacy 5.5.1
  +352 and +153 (the patch is in every libxlua, like section 3.11's).
- **Tests:** in `core_sandbox_cases.txt`, `table.move`, `table.remove`,
  `table.insert`, `table.sort` and `table.concat` under the C budget (status
  10 at 100,000 instructions), `string.rep` of empty strings returning at
  once, and the table functions' results with the budget active; in
  `core_smoke.c`, the host function `badset` (a nil key inside a host
  function) under `xpcall` with a handler that counts, one that raises and
  one that loops under a limit, and without `xpcall`; in `Test/Core`,
  `TestHandleErrorInsideXpcall` and the two C loops added to
  `StopLatencyTests`' endless codes (time limit, `Interrupt`, and with
  `--bridge` `LuaEnv`); `fuzz_core.c` gets the host function `hset` with a
  frame oracle and two seeds (`len_loops.lua`, `host_handle_errors.lua`).
  Each new case fails or hangs on main. The official 5.4.9 and 5.5.1 suites
  pass on the patched trees (`lua_tests.py`, basic, complete, internal), and
  `sort.lua`, `nextvar.lua`, `strings.lua` and `pm.lua` pass inside legacy
  libxlua with an instruction limit active, so the ticks run (`sort.lua`
  charged about 12.7 million instructions).
- **Questions for the owner:** 70 to 72.

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
    - **Answer of the test (section 8.10):** it does, at Minimal and High
      stripping on Unity 6000.3.21f1 and at Minimal on 2021.3.45f2: without
      the define the player has the
      same IL2CPP code and metadata as with it, and only 1,288 bytes of names
      more. So the define's managed effect is small: it saves those bytes,
      the compile time of `XLua` and `XLua.Editor`, and their build callbacks
      in the Editor, and it stops code from referencing `XLua` by accident.
      What it decides is the native library: without it the selector ships
      the default (legacy) library, which is 42,007 bytes larger in the APK
      and has no sandbox unless built with `XLUA_LUASCRIPT=ON`.
    - **Recommendation:** keep `XLUA_CORE_ONLY` as the one explicit switch,
      described as "use the core library and leave the bridge out", and do
      not infer it: an Editor scan of references would decide the native
      library from code that can change with any script, and a wrong guess
      is a missing-symbol crash on first `LuaEnv` use. A build check that
      warns when a player uses `LuaScript`, nothing references `XLua`, and
      the define is absent would catch the common mistake cheaply.
13. **Signatures in core:** keep `SignatureLoader` (RSA) opt-in for core as
    presets.md:34 says, with a `Load(code, name, signature)` overload, or wait
    for Ed25519 and ship core without signature checking?
14. **The watchdog's frame walk** (section 8.3, "Watchdog hang"):
    `lua_sethook` from the watchdog thread still reads frames that the
    running thread is writing (a frame being reused, a new frame's
    `previous` link). No run has shown an effect. Removing it needs a
    vendored change to Lua's VM, so that the watchdog stores only the hook
    mask (one more load where the VM reloads its trap flag, to be measured).
    Keep the current code, or patch the VM? *Update (section 8.11):* with
    line events in the remote arming, the mask alone already stops the
    call, so the VM change would now let the watchdog store nothing else.

15. **`XLua.LuaDLL.Bridge` derives from `XLua.LuaDLL.Lua`** (milestone 6,
    section 8.5), so one alias reaches the C API and the bridge imports, and
    the runtime and the generators changed only their alias. Code that calls
    a bridge import through `LuaDLL.Lua` (`xlua_pushinteger`,
    `lua_pushstring`, `xlua_getglobal` ...) must switch to `Bridge`
    (MIGRATING.md), and so must wrappers generated before the change.
    - **Alternative A:** no inheritance; call sites name the class of each
      import (`Lua.lua_gettop`, `Bridge.xlua_pushinteger`): stricter, and a
      larger change to the templates and the source generator.
    - **Alternative B:** keep `XLua.LuaDLL.Lua` as the full class in `XLua`,
      deriving from a Core class with a new name (`LuaDLL.Core`): nothing
      breaks for users, but the Core class is not the one the design names.
    - **Keep the inheritance, or A or B?**
16. **The `XLUA_LUASCRIPT` define.** `LuaScript` compiles only with
    `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY` (section 8.5): at Minimal stripping
    every LuaScript import would reach every player and break the iOS and
    WebGL link of a legacy library without the sandbox, and every player
    would carry about 22.5 KB of LuaScript IL. The alternative is
    `XLUA_LUASCRIPT=ON` by default in the legacy and minimal libraries
    (section 8.4, question 1: about 7 KB native), LuaScript always compiled,
    and no define. Keep the define?
17. **The core library exports 19 functions, not every Core import** (49):
    the Lua C API imports of `LuaDLL.Lua` serve the bridge, and a core-only
    player never calls them. Exporting them cost 5,096 bytes stripped
    (arm64-v8a, 5.5.1). A project that links `libxlua.so` directly through
    IL2CPP's `FORCE_PINVOKE_xlua_INTERNAL` would need them exported. Keep the
    short list?
18. **`LuaEnv`'s memory errors:** still `LuaException("not enough memory")`.
    `LuaMemoryLimitException` is a `LuaException`, so throwing it from `LuaEnv`
    too would keep existing `catch (LuaException)` code working, but changes
    the type a `LuaEnv(LuaOptions)` user sees. Change it?
19. **`XLUA_CORE_ONLY` in the Assets layout** excludes nothing: without
    asmdefs, `Assets/XLua/Src` still compiles into Assembly-CSharp, while the
    plugin selector ships the core library, whose missing bridge symbols break
    an iOS link. Should the build check stop a build that defines
    `XLUA_CORE_ONLY` while the bridge compiles (the asmdef layout or the
    package is then required), or only warn?
20. **LuaScript on WebGL:** `xlua_webgl.cpp` does not include the sandbox.
    Including it always costs every WebGL build about 7 KB (as question 16),
    or a define in the plugin source could gate it. Which?
21. **Binary chunks in LuaScript:** `xlua_core_call` carries the load mode
    (section 8.5), and `xlua_core_run_mode` is gone rather than kept as a test
    entry, so the trusted-bytecode tests drive the same entry as LuaScript. The
    C# `LuaScript` has no binary `Load` overload yet; add one (with
    `XLUA_TRUSTED_BYTECODE` only), or keep binary chunks native-only?
22. **The buffer library in legacy and minimal** (milestone 10, section 8.9):
    CMake `XLUA_BUFFER` is on in core and off in legacy and minimal, which can
    turn it on with `XLUA_LUASCRIPT=ON`. It costs about 7.6 KB stripped and
    3 KB compressed on arm64-v8a. If question 1 of section 8.4 (or 16) puts
    the sandbox into the full plugin by default, should the buffer come with
    it, or stay a separate opt-in?
23. **`readbits` and `writebits`:** Luau has them (bit offsets, 0 to 32 bits);
    section 2.10's list does not, so they are left out. Add them (about 0.5 KB),
    or keep the list?
24. **Integer writes of fractions:** `buffer.writeu8(b, 0, 1.5)` raises "number
    has no integer representation", as `string.char` and every other Lua 5.4
    library function do; Luau truncates to 1 (its bit32 rules). Keep Lua's
    rule, or follow Luau? The names are Luau's by decision, not its
    behaviour, so the proposal is to keep Lua's.
25. **`type(b)` is `"userdata"`:** Lua 5.x cannot add a type name. A script
    can tell buffers apart only with `pcall(buffer.len, b)`. Add a
    `buffer.isbuffer(v)` (not in Luau), or have the sandbox's `type` return
    `"buffer"` (a wrapped base function, which changes `type` for everything)?
    Or leave it?
26. **The C budget rate:** one instruction-equivalent per 32 bytes for
    operations of 1 KiB and more, charged per 128 KiB. A 1 MiB `fill` costs
    32,768, so an `InstructionLimit` of 10,000,000 allows about 300 MB of
    buffer traffic. Is that the rate wanted, or should bulk byte operations be
    cheaper (they are memset and memmove) or free below the time limit?
27. **64-bit integers:** Lua 5.4 and 5.5 have 64-bit integers, Luau's buffer
    has no `readi64`/`writei64`. Add them (lossless, about 0.1 KB), or stay
    with Luau's list?
28. **`LuaCall.Yield()` outside `Start`.** It works in any coroutine that
    can yield, so with Lua's own `coroutine.resume` and `wrap` too
    (section 8.6), not only under milestone 9's `Start` as section 2.8 says.
    Keep, or refuse it unless C# drives the coroutine?
29. **A registration reaching existing environments.** `Register("lib.f")`
    sets the key in every environment's copy of `lib` that still has nil or
    the old function there, and copies a new library into environments
    without one; a script's own value stays (section 8.6). It costs two weak
    tables and about 80 lines of C. The alternative: environments see only
    what was registered before they were made, which leaves `Globals` and
    loaded modules without later registrations. Keep?
30. **What a script sees of a C# exception:** `"TypeName: message"` at the
    caller's position (a `LuaException` without the type name). Scripts from
    users then see C# type names and messages. Alternative: the message
    only, or a fixed "host function failed" (the exception still reaches the
    host as `InnerException`). Which?
31. **The stop check after every host function** takes the core lock while
    a limit is configured, which is the default (`ScriptDefaults` has a 1 s
    `TimeLimit`). On macOS arm64 it is within the noise (0.5 ns on .NET, 3 ns
    on Mono per host function call); not measured on a
    phone. An atomic read of `stop` without the lock would remove most of
    it, but the watchdog and `Interrupt` must then write `stop` atomically
    everywhere (ThreadSanitizer). Change it, or measure on the phone first?
32. **Nested memory errors:** a `LuaMemoryLimitException` that a host
    function lets through becomes Lua's memory error in the calling script,
    also when another `LuaScript` threw it. Keep, or only for the same
    script (the exception would carry its script)?
33. **`Register` over base functions:** a name such as `"tostring"`,
    `"pairs"` or `"string.format"` replaces the function for every
    environment of the script. Allow (as now), or refuse the names of the
    base functions and standard libraries without an explicit override?
34. **The 200 KB budget** (section 6): a core-only player adds 223,530 bytes
    with Minimal stripping (Unity's default for IL2CPP) and 193,802 with High
    (section 8.10), before milestones 8 and 9. The native library is within
    its budget; the IL2CPP code (84,624 compressed, 347,584 uncompressed) is
    above the design's estimate. Raise the budget (for example to 250 KB at
    Minimal), state it at High stripping, or look for C# to trim first (a
    `size_breakdown.py` run over the IL2CPP output would show which methods
    and generic instantiations it is)?
35. **`TimeLimit` and `Interrupt` with an `InstructionLimit`** are late by up
    to the length of one count slice (about 2 s for 2^30 instructions;
    section 8.10). A cheap fix would cap the slice while a time limit is set
    or an `Interrupt` may come, for example at 2^20 instructions (one hook
    call per million instructions, a stop within about 2 ms even when every
    remote arming is lost), and let `Interrupt` set a flag that the watchdog
    polls as it does for a late stop. A complete fix would make the remote
    stop independent of `hookcount`, which needs the vendored VM change of
    question 14. Cap the slice now (milestone 3's code), or wait for the VM
    change? *Done (section 8.11):* root-caused (the lost `hookcount` store),
    fixed with line events in the remote arming and slices capped at 2^20,
    without the VM change.
36. **`Interrupt()` without limits** does nothing, because a call of a state
    without an instruction or time limit skips the entry protocol. Run the
    entry protocol for every outermost `LuaScript` call (a lock and a few
    stores per call), or document that `Interrupt` needs a limit (as
    LuaScript.md now does)? *Done for `LuaScript` (section 8.11):* the entry
    protocol runs for every call (`XLUA_LIMITS_INTERRUPT`, about 6 ns per
    call); see question 50.
37. **luban's culture bug** (section 8.10, `SOURCE.md` of the fixture): its
    `lua` data target writes floats with a decimal comma under such locales,
    and Lua reads them as two values without an error. Report it upstream?
38. **Where security reports go.** Security.md's LuaScript section asks for
    private reports, but the repository names no channel. GitHub's private
    vulnerability reporting (once the repository is public), an e-mail
    address, or something else?
39. **One handle export** (milestone 8, section 8.7): `xlua_core_handle(L, op,
    v[4])` does every table, key, iterator and buffer operation, in place of
    section 3.3's dozen exports and of `xlua_core_newenv`, `_newbuffer` and
    `_tobuffer`. It keeps the core library within section 6's budget (two
    exports fewer than before the milestone) at the cost of an op switch per
    call and a less self-describing ABI. Keep, or split it as section 3.3
    drew it?
40. **Borrowed handles of host function arguments** are registry references
    made on `LuaCall.GetTable` (one P/Invoke) and released after the frame,
    not section 3.2's negative stack indices, which name another value once
    a nested host function runs on the same thread. A host function that
    reads many table arguments pays one P/Invoke each. Keep, or add a
    stack-index fast path valid only while the frame is the innermost one?
41. **No non-raising fast path:** every table operation, `LuaKey` and integer
    reads included, runs under `luaD_rawrunprotected` (one `setjmp`; `luaD_pcall` since section 8.12).
    Measured on macOS arm64 (.NET 10): `GetLong(LuaKey)` 71 ns, `GetLong(string)`
    81 ns, `Set(LuaKey)` 58 ns, `Pairs` 93 ns per pair; most of it is the
    P/Invoke and the checks. A fast path would save a few ns per read for
    more native code. Measure on the phone first, or leave it?
42. **`ErrorValue` holds an owning handle** for `error({code = 5})`: every
    such exception keeps its table until the handle is disposed or the
    script is, and `LiveHandles` counts it. Alternatives: a handle borrowed
    until the next call into the script, or a `ToDictionary` copy (allocates).
    Which?
43. **Any table is an environment:** `Load(code, name, env)`, `Compile` and
    `LuaTableRef.Call` take any table of the script, also a plain one without
    the base functions (the host's choice; nothing reaches the frozen base).
    Keep, or accept only `Globals` and tables of `NewEnvironment`?
44. **Smaller C# surface than section 2.9 and 2.6:** the named getters are
    the ones section 2.9 lists, read through one non-generic path; other
    key and type combinations go through the generic `Get<T>` and `TryGet<T>`;
    `LuaTableRef.Call` has the span forms only. `XLua.Core.dll` grows from
    65,536 to 92,672 bytes of IL and metadata (section 8.7), and question 34
    says the core-only player is already over its budget, mostly in IL2CPP
    code of `XLua.Core`. Cut further (`ToDictionary`, `IPairs`, the tuple
    calls, `LuaPair`'s span accessors behind a define), or keep?
45. **`ToDictionary`** turns functions and other values into their type name
    and number keys into text, so it works on configuration tables with
    methods. Throw instead, as for cycles and table keys?
46. **`TryGet<T>` and the `TryGet...` getters** return false for a missing
    key and also for a value of another type (they throw only for a `T`
    that is not a result type). Keep, or throw `LuaTypeException` for a wrong
    type as the plain getters do?
47. **`Get(LuaValue key)`** returns nil for a missing key, as `rawget` does,
    where `Get<T>` and the named getters throw `KeyNotFoundException`. Keep the
    difference?
48. **`Register(Globals, name, ...)`** puts the function into the `Globals`
    table itself (`rawget(_G, name)` sees it, a script can overwrite it),
    while `Register(name, ...)` puts it into the frozen base. Keep, or make
    the `Globals` overload mean the base?
49. **`LuaValue` is `IDisposable`** now: `Dispose` releases the table,
    function or buffer handle it holds (results are owning handles), and
    does nothing for other values. Keep, or leave disposal to `AsTable()` and
    the other handle types?
50. **`Interrupt()` without limits costs every `LuaScript` call** about
    6 ns (98 to 104 ns for the cheapest `xlua_core_call` on macOS arm64) and
    every host function call about 1 ns, also for scripts that never call
    `Interrupt`. Keep it on for every script (as now), or make it a
    `LuaOptions` switch? And should `LuaEnv(LuaOptions)` without limits get
    it too (the same cost on every `lua_pcall` from C#), or stay as
    documented?
51. **The count slice is 2^20 instructions** (was 2^30; section 8.11). It
    bounds how late any stop can be and makes the hook read the native
    deadline every 1 to 5 ms while an instruction limit runs, at no
    measurable cost. Keep 2^20, or go smaller (2^16, about 0.1 to 0.3 ms)
    for a tighter native time limit?
52. **Line events in the remote stop** rely on the running thread never
    writing `L->hookmask`, which holds for Lua 5.4.9 and 5.5.1 as vendored
    (only `lua_sethook` writes it, and core calls that only under its lock).
    A Lua upgrade must keep that; `core_stop_latency` and the
    `StopLatencyTests` catch a regression. Accept, or prefer the VM change of
    question 14 now?

60. **Coroutines in the ABI and in `LuaType`** (milestone 9, section 8.8):
    start, resume and close are kinds of `xlua_core_call` (no new export, in
    the spirit of question 39), and a coroutine value is a handle of the new
    `XLUA_T_THREAD` / `LuaType.Coroutine` (9) with its status in `len`. Lua
    calls the type "thread"; the C# name follows the design's `LuaCoroutine`.
    Keep both, or name the type `Thread`, or split the exports as section 3.3
    drew them?
61. **`LuaCoroutine.Get<T>(i)`** of section 2.11 is not built: `co[i]` is a
    `LuaValue` with `AsLong`, `AsString`, `AsTable` ..., and a generic reader
    table for `LuaValue` cost about 2.5 KB of IL in `XLua.Core.dll`, which is
    over its budget already (question 34). Add it anyway?
62. **The drivers are compiled by default** (about 5.6 KB of IL with
    `RunAwaitable`, measured against Unity 6000.3.26f1's reference
    assemblies); `XLUA_NO_COROUTINE_DRIVERS` leaves them out. Default on (as
    now, the design's API), or opt-in with a define like `XLUA_LUASCRIPT`?
63. **`Dispose` of a coroutine of `Start` closes it and drops errors** of its
    `__close` handlers (`Close` reports them), so a `using` block cannot hide
    the exception that left it. A script-made coroutine is only released.
    Keep, or let `Dispose` throw as `Close` does?
64. **`LuaReply` does not own handles:** a table a handler creates for the
    reply is the handler's to dispose after the resume, which an `IEnumerator`
    or `async` handler has no natural place for (the test keeps a list). The
    alternative: the driver disposes owning handles in the reply after the
    resume (a handle the host wants to keep it passes as `Retain()`). Which?
65. **At most 64 values** (`LuaCoroutine.MaxValues`) of a yield or return
    reach C#; more are dropped, so a step needs no allocation for its results
    after the first. Enough, or should the count be unbounded (a reused array
    that grows, sized from the previous step)?
66. **`Status` is what C# last saw.** A script that resumes a coroutine C#
    holds (a script-made one, or `coroutine.running()` of a started one)
    changes it behind C#'s back; `Resume` then finds the real state natively
    and throws for a dead or running coroutine. Reading the native status on
    every `Status` costs a P/Invoke per read, which the drivers do per step.
    Keep the cached status, or read it natively?
67. **A lifetime budget per coroutine** (decision 5: "may be added later") is
    not built. If it is wanted, the simplest form is C#-only: a
    `LuaCoroutine.InstructionBudget` that each step's `LastInstructionCount`
    is taken from, with the step's limit set to the rest. Wanted now?
68. **Cost of a step:** about 240 ns on .NET and 415 ns on Mono (macOS
    arm64), against 75 ns for a resume and yield inside Lua; the P/Invoke, the
    protected entry and the value conversion are most of it. Measure on the
    phone before anything is changed?
69. **A failed `Start`** throws and returns nothing: the coroutine is closed
    (its `__close` handlers run) and released first. The alternative is to
    return it `Faulted` and not throw, which would let a host read its error
    and traceback from the coroutine but makes every `Start` need a status
    check. Keep throwing?
70. **`__len` stays allowed** in `LuaScript`'s `setmetatable` (section
    8.12): the C budget now charges the loops it lengthens. The
    alternatives are to refuse a metatable with `__len`, as `__gc` is
    refused (a behaviour change for scripts that emulate arrays), or to cap
    what `__len` may report for the table library. Keep the budget only?
71. **The table ticks are in every libxlua** (section 8.12), as the
    pattern and `table.move` ticks are: in the legacy preset they do
    nothing unless `LuaEnv(LuaOptions)` sets a limit, and then `insert`,
    `remove`, `concat` and `sort` count towards `InstructionLimit` there too
    (one per element step; a Lua comparator's own instructions count as
    before). Cost: +352 bytes stripped, +153 zipped on Android arm64 legacy
    5.5.1. Compiling them only into builds with the sandbox would keep
    legacy smaller but need a second macro next to `luai_budget`. Keep as
    is?
72. **`string.rep` of two empty strings on 5.4.9** (section 8.12) loops
    2^63 times in upstream Lua 5.4.9; Lua 5.5 has the check. The backport
    is in every 5.4.9 build, legacy included (the result is the same `""`).
    Report it to lua-l as a 5.4 bug?

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
| 13. Signatures | The existing RSA `SignatureLoader` stays an opt-in flag. 2026-10-18: no native Ed25519; a project that wants another scheme checks signatures in its own C# loader ([backlog.md](backlog.md)). |
| `buffer` library | Part of the first core release (the owner wants it). It uses Luau's function names because they are well designed, not for compatibility. |
| Size trims in core | Core loads text only, so the bytecode loader and dumper (`lundump.c`, `ldump.c`, `string.dump`) are left out of the core native build, together with the `LUA_COMPAT_*` options. `utf8` and `coroutine` stay: they cost about 4 KB and 2.5 KB, and core's yield model needs coroutines. |
