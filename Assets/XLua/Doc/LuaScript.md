# LuaScript: Lua scripts you do not trust

`LuaScript` runs Lua 5.4 or 5.5 source text in a sandbox, with memory,
instruction and time limits. It is for code that the game does not fully
control: mods, levels, rules or configuration downloaded at runtime, scripts
written by players or by an AI. It has no bridge to C#: a script reaches only
the functions you register. `LuaEnv`, the rest of xLua, is not a sandbox
([Security and sandboxing](Security.md)).

**Status: preview.** What this page describes is implemented and tested
today: loading and calling (milestone 6 of
[core-luascript.md](../../../docs/design/core-luascript.md)), C# functions
called from Lua (milestone 7), table and function handles (`LuaTableRef`,
`LuaFunctionRef`, `Globals`, `NewEnvironment()`, `Compile`; milestone 8),
coroutines driven from C# (`LuaCoroutine` with `IEnumerator`, `Task` and
`Awaitable` drivers; milestone 9) and the `buffer` library (milestone 10).
The API can still change before the first release.

## Setting up

`LuaScript` is in the `XLua.Core` assembly (`Assets/XLua/Core`) and compiles
only when the Player Settings define one of two symbols. It needs a native
library with LuaScript's sandbox (Lua 5.4 or 5.5; LuaJIT and Lua 5.3 have
none).

| Project | Scripting define | Native library |
| --- | --- | --- |
| `LuaScript` only (**core-only**) | `XLUA_CORE_ONLY` | the core library: `XLUA_PRESET=core`, for example `XLUA_VARIANT=core XLUA_PRESET=core build/make_android_lua55.sh` |
| `LuaScript` next to `LuaEnv` | `XLUA_LUASCRIPT` | the bridge library built with `XLUA_LUASCRIPT=ON` (and `XLUA_BUFFER=ON` for the `buffer` library) |

- **Core-only needs the package layout** (xLua as the UPM package of
  `Tools/make_upm_package.py`, or the opt-in asmdefs of `Assets/XLua/Asmdef~`).
  There `XLUA_CORE_ONLY` leaves the `XLua` and `XLua.Editor` assemblies out of
  the build. In the plain Assets layout everything compiles into
  `Assembly-CSharp` and the define leaves nothing out.
- **The core library** can be the package's plugin
  (`make_upm_package.py --plugin Android/libs/arm64-v8a/libxlua.so=PATH`), or
  the plugin variant `Plugins/<platform>/xlua-variants/core/` next to the
  default plugin, which the plugin selector picks when `XLUA_CORE_ONLY` is
  defined ([build/README.md](../../../build/README.md), "Plugin variants").
  The first is what the Android players below were built with.
- **Without either define** `LuaScript` does not exist, and an IL2CPP player
  carries none of its code.

## A first script

```csharp
using System;
using UnityEngine;
using XLua;

public class RewardRules : MonoBehaviour
{
    LuaScript lua;

    void Start()
    {
        lua = new LuaScript(new LuaOptions
        {
            MemoryLimit = 4 << 20,                       // bytes the state may hold
            InstructionLimit = 10000000,                 // VM instructions per call
            TimeLimit = TimeSpan.FromMilliseconds(50),   // wall clock per call
            Libraries = LuaLibs.String | LuaLibs.Table | LuaLibs.Math,
        });
        // A C# function the script may call; nothing else of C# is reachable.
        lua.Register<long, long>("playerLevel", id => 12);
        lua.Load("function reward(id, kind) return playerLevel(id) * (kind == 'gold' and 10 or 1) end", "rules");
        try
        {
            long gold = lua.Call<long>("reward", 7, "gold");   // 120
            Debug.Log("reward " + gold);
        }
        catch (LuaLimitException e)
        {
            Debug.LogWarning("the script hit its " + e.Limit + " limit: " + e.Message);
        }
        catch (LuaException e)
        {
            Debug.LogError(e.Message + "\n" + e.LuaStackTrace);
        }
    }

    void OnDestroy()
    {
        lua?.Dispose();
    }
}
```

`new LuaScript()` without options starts with `LuaOptions.ScriptDefaults`: a
64 MiB memory limit and a 1 s time limit. `new LuaOptions()` sets no limit at
all; set the limits you want explicitly.

## Loading and calling

- **`Load(code, chunkName)`** compiles and runs a chunk; `Load<T>` returns its
  first result, and `Load(code, name, results)` fills a `Span<LuaValue>`.
  Only text is accepted: a precompiled chunk (`"\x1bLua"`) throws
  `LuaSyntaxException`, and a script's own `load` refuses binary chunks too.
  Error messages and tracebacks name the chunk as `chunkName:line`.
- **`Call(name, ...)`** calls a global function, also a dotted one
  (`"rules.reward"`), with 0 to 4 arguments or a `ReadOnlySpan<LuaValue>`.
  `Call<T>` converts the first result to `long`, `int`, `double`, `float`,
  `bool`, `string`, `byte[]`, `LuaValue`, `LuaTableRef`, `LuaFunctionRef` or
  `LuaBufferRef`; nil reads as null, false or the default handle, and fails
  for the number types.
  `Call(name, args, results)` returns the number of results, and
  `Call<T1, T2>` and `Call<T1, T2, T3>` return tuples.
- **`LuaValue`** carries nil, booleans, 64-bit integers, floats, strings and
  handles to tables, functions and buffers; every C# integer type and
  `float`, `double`, `string` and the handle types convert to it implicitly.
  Integers stay 64-bit both ways.
- **Calls with primitive arguments and results allocate nothing** on the
  managed heap; string results allocate the string.
- **`require`** exists only when you set `ModuleResolver`, a delegate that
  returns a module's source text and chunk name. Each module runs once, in
  an environment of its own, and its result is cached:

  ```csharp
  lua.ModuleResolver = (string name, out string code, out string chunkName) =>
  {
      chunkName = "mods/" + name + ".lua";
      code = LoadModText(name);   // null or false: "module not found"
      return code != null;
  };
  ```

## Tables and functions

Tables and functions reach C# as handles: `LuaTableRef` and `LuaFunctionRef`,
structs that allocate nothing. Table access is raw (no metamethod runs), so it
never runs script code.

```csharp
using (LuaTableRef cfg = lua.Globals.GetTable("config"))   // an owning handle: dispose it
{
    double rate = cfg.GetDouble("dropRate");            // KeyNotFoundException if missing
    long gold = cfg.GetLong("gold", 0);                 // or a fallback
    LuaKey hot = lua.Key("hp");                         // anchored once: the fast path for hot fields
    cfg.Set(hot, cfg.GetLong(hot) - 1);
    foreach (LuaPair p in cfg.Pairs())                  // allocates nothing
    {
        if (p.KeyEquals("items")) Debug.Log(p.Value.AsTable().Length);  // a table borrowed until the next step
    }
}
using (LuaFunctionRef f = lua.Compile("return ... * 2", "double"))  // loaded, not run
{
    long four = f.Call<long>(2);
}
```

- **Handles you get** from `GetTable`, `GetFunction`, `NewTable`,
  `NewEnvironment`, `Compile` or a call's result are owning: dispose them. A
  forgotten one keeps its table until the script is disposed, which
  invalidates every handle; `LiveHandles` counts the open ones. A copy of a
  disposed handle throws `ObjectDisposedException`.
- **Borrowed handles** end by themselves: a table argument of a C# function
  (`call.GetTable(i)`, valid until it returns) and a table value during
  `Pairs` (until the next step). `Retain()` makes an owning copy.
- **Environments:** `Globals` is the default one. `NewEnvironment()` makes
  another over the same frozen base; `Load(code, name, env)`,
  `Compile(code, name, env)`, `env.Call("f")` and `Register(env, name, f)`
  use it, and scripts in one environment do not see the globals of another.
- **`ToDictionary()`** copies a table into `Dictionary<string, object>` for
  debugging; it allocates.

## C# functions called from Lua

`Register(name, callback)` makes a C# function visible to every environment of
the script, as a global (`"name"`) or in a library table (`"game.log"`).
Registering a name again replaces the function; `Unregister(name)` removes it,
and a script that kept the old function gets an error when it calls it.

- **Typed shorthands:** `Register<A1, ..., R>(name, Func<...>)` and
  `Register(name, Action<...>)` with up to four arguments of the types of
  `Call<T>` (not `LuaBufferRef` arguments). They do not box.
- **`LuaCallback`** (`delegate void LuaCallback(ref LuaCall call)`) reads the
  arguments by 0-based index (`ArgCount`, `ArgType(i)`, `GetLong(i)`,
  `GetDouble(i)`, `GetBool(i)`, `GetString(i)`, `GetUtf8(i)`,
  `GetBufferSpan(i)`, `ArgEquals(i, text)`, `Get(i)` and the `Opt...` getters)
  and returns any number of values with `Return(...)` or `ReturnNil()`. A
  wrong argument type gives Lua's own message, such as `bad argument #1 to
  'reward' (number expected, got string)`. `GetUtf8` and `ArgEquals` allocate
  nothing.
- **Exceptions** become Lua errors that a script can catch with `pcall`;
  uncaught, the call throws `LuaRuntimeException` with the host exception as
  `InnerException`.
- **A C# function is never interrupted** by a limit; the call stops right
  after it returns. What it does with its arguments, how long it runs and how
  much it allocates is your code's responsibility.
- **`call.Yield()`** suspends the coroutine that called the function: one
  that `Start` runs, or Lua's own `coroutine.resume` and `coroutine.wrap`
  (`CanYield` tells whether that is possible). The function's results are the
  yielded values, and the values of the next resume become its results in Lua
  ([Coroutines](#coroutines)).
- A C# function may call back into the same script (`Call`, `Load`); the
  nested call runs within the outer call's limits.

## Coroutines

A script yields a request, and C# serves it and resumes the script:

```lua
function quest(level)
  local ok = coroutine.yield({op = "wait", seconds = 1.5})
  local data = coroutine.yield({op = "fetch", key = "rewards"})
  return data.gold * level
end
```

```csharp
using (LuaCoroutine co = lua.Start("quest", 3))   // runs to the first yield
{
    while (co.IsYielded)
    {
        using (LuaTableRef reward = lua.NewTable())
        {
            string op = co[0].AsTable().GetString("op");   // the yielded values: co[0], co[1] ...
            reward.Set("gold", 100);
            if (op == "wait") co.Resume(true); else co.Resume(reward);  // the results of coroutine.yield
        }
    }
    long gold = co[0].AsLong();                    // Finished: co[i] are the return values
}
```

- **`Start(name, args)`** (or `Start(functionRef, args)`) runs a function as
  a new coroutine to its first yield or its end. **`Resume(args)`** runs it on
  to the next one. `Status` is `Yielded`, `Finished`, `Faulted`, `Closed` or
  `Running` (while it runs, for a C# function it called); `ValueCount` and
  `co[i]` are the values of the last yield or return (at most
  `LuaCoroutine.MaxValues`, 64). Resuming one that is not `Yielded` throws
  `InvalidOperationException`.
- **Each step is a call of its own:** `Start` and every `Resume` get the full
  `InstructionLimit` and `TimeLimit`, and `LastInstructionCount` counts one
  step. `MemoryLimit` covers the whole script. Coroutines a step runs inside
  Lua share its budget.
- **Errors and limits** throw from the step, as from `Call`, with the
  coroutine's own traceback in `LuaStackTrace`, and leave it `Faulted`. An
  error in `Start` throws and leaves no coroutine.
- **Handles:** a `LuaCoroutine` is a handle like `LuaTableRef`: dispose it
  (`LiveHandles` counts it; disposing the script invalidates it). Tables,
  functions and buffers among `co[i]` are borrowed until the next step;
  `Retain()` keeps one. Primitive values in and out of a step allocate
  nothing.
- **`Close()`** runs the coroutine's pending to-be-closed variables
  (`local x <close>`) and makes it `Closed`, as `coroutine.close` does.
  `Dispose()` closes a coroutine of `Start` and releases it.
- **Coroutines a script made** (`coroutine.create`) reach C# as
  `LuaCoroutine` too: `Call<LuaCoroutine>("f")`, `table.Get<LuaCoroutine>(key)`,
  `co[i].AsCoroutine()`, or `call.GetCoroutine(i)` in a C# function (borrowed:
  `Retain()` keeps it). C# can resume them, and pass them back
  (`LuaValue` converts from `LuaCoroutine`). Disposing one only releases the
  handle: the script may still use it.

**Drivers** loop for you: the handler serves one request (`co[0]` ...) and
fills the `LuaReply`, whose values the driver passes to the next `Resume`.

```csharp
// Unity coroutine: the handler's enumerator runs inside it, so it may wait.
StartCoroutine(co.Run((c, reply) => Serve(c, reply)));

IEnumerator Serve(LuaCoroutine co, LuaReply reply)
{
    yield return new WaitForSeconds((float)co[0].AsTable().GetDouble("seconds"));
    reply.Set(true);
}

// async: continues on the caller's SynchronizationContext (Unity's main thread)
await co.RunAsync(async (c, reply, ct) => { reply.Set(await LoadRewardAsync(ct)); }, ct);

#if UNITY_2023_1_OR_NEWER
await co.RunAwaitable(async (c, reply, ct) => { await Awaitable.WaitForSecondsAsync(1.5f, ct); reply.Set(true); }, ct);
#endif
```

- **`RunAsync`** resumes on the `SynchronizationContext` it was called on,
  also when the handler uses `ConfigureAwait(false)`. Without one (a plain
  .NET thread) it resumes on whatever thread finishes the handler, which
  needs `LuaOptions.CheckThread = false`.
- **`RunAwaitable`** (Unity 2023.1 and later) returns to the main thread if a
  handler left it.
- **Cancelling `ct`** closes the coroutine (its `__close` handlers run) and
  ends the task with `OperationCanceledException`. A script error faults it.
- The drivers allocate their state once per run (an `IEnumerator` run of ten
  yields about 300 bytes, `RunAsync` with handlers that finish at once about
  400; an awaiting handler adds what its own `async` code costs). The
  scripting define `XLUA_NO_COROUTINE_DRIVERS` leaves them out: about 24 KB
  of an Android APK in an otherwise empty project, less in a game that uses
  coroutines and `async` code of its own (the TPL and `IEnumerator` code is
  then there anyway).

## Smaller players

A core-only Android player (ARM64, Unity 6000.3, Lua 5.5.1) adds about
268 KB to its APK with Unity's default Minimal managed stripping and about
188 KB with High stripping; the native library is 123 KB of either. Two
scripting defines leave out parts that few scripts need:

| Define | Leaves out | APK bytes (Minimal) |
| --- | --- | ---: |
| `XLUA_NO_COROUTINE_DRIVERS` | `LuaCoroutine.Run`, `RunAsync`, `RunAwaitable`, `LuaReply` | about 24,000 |
| `XLUA_NO_LUASCRIPT_EXTRAS` | `LuaTableRef.ToDictionary`, `IPairs`, the tuple calls `Call<T1, T2>` and `Call<T1, T2, T3>`, `Register` with three or four arguments | about 14,000 |

With High stripping only what the project calls is compiled, so the defines
matter little there. Measurements: [SIZE.md](../../../build/SIZE.md#trimming-the-core-only-player).

## Limits

Each limit applies to one call from C# (`Load`, `Call`, a coroutine's `Start`
or `Resume`) with everything it runs, nested calls and coroutines included. They can be changed between
calls through the properties of the same names.

| Limit | Over the limit | Can the script catch it? |
| --- | --- | --- |
| `MemoryLimit` (bytes) | Lua's "not enough memory" error; uncaught, `LuaMemoryLimitException` with `MemoryUsed` and `MemoryLimit` | yes, with `pcall` |
| `InstructionLimit` (VM instructions; string patterns, `table.move` and large `buffer` operations are charged too) | `LuaInstructionLimitException` | no: the call ends |
| `TimeLimit` (wall clock) | `LuaTimeLimitException`, a few milliseconds after the deadline (a watchdog thread; on WebGL the clock is read every 10,000 instructions) | no |
| `Interrupt()` from any thread | `LuaInterruptedException` | no |

- **`Interrupt()` stops the running call** with or without an instruction
  or time limit, from any thread. Lua code stops within a fraction of a
  millisecond (measured on a macOS arm64 host); a C# function is not
  interrupted, and the call stops when it returns. Without a limit this
  costs about 6 ns per call from C# and 1 ns per C# function called from
  Lua; `LuaOptions.AllowInterrupt = false` saves that, and `Interrupt()`
  then stops only calls with a limit.
- **The time limit** stops a call a few milliseconds after its deadline:
  within the watchdog's period, a quarter of the limit (1 to 20 ms). With an
  `InstructionLimit` as well, the instruction hook also reads the clock about
  every million instructions.
- `MemoryUsed`, `MemoryPeak` and `LastInstructionCount` (counted while an
  instruction limit is set) report what a call used; `CollectGarbage()` runs
  a full collection.
- A runaway loop without any limit runs forever; there is no termination
  guarantee without `InstructionLimit` or `TimeLimit`.

## The sandbox

A script sees the base functions (`assert`, `error`, `ipairs`, `pairs`,
`pcall`, `select`, `tonumber`, `tostring`, `type`, `xpcall` and the `raw`
functions), and the libraries `LuaOptions.Libraries` names: `coroutine`,
`string`, `table`, `math`, `utf8` (`LuaLibs.Safe`, the default) and the
opt-in `buffer` (`LuaLibs.Buffer`).

- **Absent:** `io`, `os`, `debug`, `package`, `dofile`, `loadfile`,
  `string.dump`, `print` and `warn`. Register your own `print` if scripts
  should log.
- **Wrapped:** `load` (text only; the caller's environment by default),
  `setmetatable` (refuses a metatable with `__gc`) and `collectgarbage`
  (`"count"` only).
- **Each environment** has its own copies of the library tables, so a script
  that writes `string.format = nil` changes only its own. The string
  metatable is locked (`getmetatable("")` is `false`).
- Asking for `io`, `os`, `debug`, `package` or LuaJIT's libraries in
  `Libraries` throws `ArgumentException`.

## Buffers

With `LuaLibs.Buffer`, scripts get Luau's `buffer` library: fixed-size byte
arrays with `create`, `fromstring`, `tostring`, `len`, the little-endian
`read`/`write` functions for 8-, 16- and 32-bit integers and 32- and 64-bit
floats, `readstring`, `writestring`, `copy` and `fill`, plus two that Luau
does not have: `readi64`/`writei64` (a whole 64-bit Lua integer) and
`isbuffer(v)`. Every access is bounds-checked, and buffers count against
`MemoryLimit`.

C# shares them without copying: `lua.NewBuffer(length)` or a buffer result
(`Call<LuaBufferRef>`) is a `LuaBufferRef` with `AsSpan()`, `CopyFrom`,
`CopyTo` and `ReadInt32`-style accessors that cost no native call. Dispose a
handle when you are done; disposing the script releases all of them
(`LiveHandles` counts the open ones). Differences from Luau: integer writes
refuse fractions as Lua does (`writei64` too), `type(b)` is `"userdata"`
(`buffer.isbuffer(b)` tells buffers apart), and `readbits` and `writebits`
are not there.

## Errors

| Exception | When |
| --- | --- |
| `LuaSyntaxException` (`ChunkName`) | the chunk does not compile, or is binary |
| `LuaRuntimeException` (`LuaStackTrace`, `InnerException` for a C# exception, `ErrorValue` for `error({code = 5})`) | an error the script did not catch |
| `LuaTypeException` (`Expected`, `Actual`) | `Call<T>` got a result it cannot convert |
| `LuaMemoryLimitException`, `LuaInstructionLimitException`, `LuaTimeLimitException`, `LuaInterruptedException` | the limits above (all `LuaLimitException`, with `Limit`) |
| `NotSupportedException` | the native library has no sandbox, or no `buffer` library for `LuaLibs.Buffer` |

All of them derive from `LuaException`.

## Threads

A `LuaScript` belongs to the thread that created it: with
`LuaOptions.CheckThread` (the default) every other member throws
`InvalidOperationException` on another thread, except `Interrupt()`,
`IsDisposed` and the statistics. Two scripts on two threads run in parallel.
`Dispose` inside a call of the same script throws; the finalizer closes a
script that was not disposed.

## Size

Measured on 2026-10-18 with Unity 6000.3.21f1, release ARM64 IL2CPP APKs of a
small probe (a script with limits and the buffer library, one registered C#
function, a chunk, two calls and a buffer), Lua 5.5.1, against an empty
project ([build/SIZE.md](../../../build/SIZE.md#luascript-in-players)):

| | Minimal stripping | High stripping |
| --- | ---: | ---: |
| `LuaScript` alone (core-only) | +223,530 bytes | +193,802 bytes |
| `LuaEnv` alone (legacy, for comparison) | +1,447,742 bytes | +1,304,870 bytes |
| `LuaScript` added to a `LuaEnv` player | +87,580 bytes | +63,348 bytes |

The core library is 121,686 of the core-only figure, IL2CPP code and metadata
most of the rest.

## Security

What counts as a security bug in `LuaScript`, and what is out of scope, is in
[Security and sandboxing](Security.md#luascript).
