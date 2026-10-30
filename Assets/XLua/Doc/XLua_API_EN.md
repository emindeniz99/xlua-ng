## C# APIs

### LuaEnv type

#### LuaEnv(), LuaEnv(LuaOptions options)

Description:

`new LuaEnv()` creates an environment as upstream xLua does. `new LuaEnv(options)` copies the options and applies them (`null` is the same as no options):

- `MemoryLimit` (bytes, 0 = unlimited): a Lua allocation that would go over it fails with Lua's `not enough memory` error. A script can catch it with `pcall`; uncaught it becomes a `LuaException`. Allocations made while a C# function called from Lua runs are never refused, because a Lua error must not unwind through C# frames; when the function returns, the state is collected and, if it is still over the limit, the call fails with the same error. The limit starts once the constructor has finished, so it does not count against the environment's own setup. Not available on LuaJIT (`NotSupportedException`).
- `HashSeed` (`uint?`, null = random): the seed of Lua's string hash. A fixed seed gives the same `pairs` order over string and number keys in every state and process for the same insertion history. Lua 5.4 and 5.5 only; Lua 5.3 and LuaJIT throw `NotSupportedException`.
- `InstructionLimit` (0 = unlimited): the most Lua VM instructions one call from C# may run. A call is every outermost entry into Lua: `DoString`, `LuaFunction.Call`, a delegate bound to a Lua function, a `LuaTable` access that runs a metamethod. Lua code that a C# callback calls again runs within the outer call's budget, and coroutines share it. The string pattern functions and `table.move` charge their work as well. Over the limit the call fails with `LuaInstructionLimitException`. A script cannot catch the stop: `pcall`, `xpcall` handlers and `__close` handlers fail again until the error reaches C#. `LastInstructionCount` gives the count of the last call; it is the same for the same code and input. While a limited call runs, Lua takes its per-instruction hook path, which costs speed; see the measurements in `docs/design/core-luascript.md`, section 8.3.
- `TimeLimit` (`TimeSpan.Zero` = unlimited): the longest wall-clock time of one call. A watchdog thread stops it with `LuaTimeLimitException` a few milliseconds after the deadline, and the instruction hook and the string and table functions check the deadline too. A C# function called from Lua is not interrupted; the call stops when it returns. On WebGL, which has no threads, the hook checks the clock every 10,000 instructions.
- `AllowInterrupt` (`bool?`, null = the API's default: off for `LuaEnv`, on for `LuaScript`): whether `Interrupt()` can stop a call that runs without an instruction or time limit. On, every outermost call records its running thread (about 6 ns per call from C#, 1 ns per C# function called from Lua). `true` on Lua 5.3 and LuaJIT throws `NotSupportedException`.
- `Libraries` (`LuaLibs` flags, `LuaLibs.Default` = every library the native library contains): the standard libraries scripts get, from `Coroutine`, `String`, `Table`, `Math`, `Utf8`, `Io`, `Os`, `Debug`, `Package` and LuaJIT's `Bit`, `Jit` and `Ffi` (`LuaLibs.Safe` is the first five, `LuaLibs.All` all of them). The base library is always open. A library the native build leaves out (CMake `XLUA_LIBS`) stays absent even when asked for. A removed library is gone from the globals and from `package.loaded`; without `String`, strings also lose their metatable. Two libraries are only reduced, because the bridge needs them: without `Debug`, `debug` has only `traceback` (the error handler uses it) and `getinfo`; without `Package`, there is no `package` table, but `require`, the native library's `package.preload` entries (LuaSocket) and the loaders (`AddLoader`, the built-in ones) keep working. With the minimal preset (`XLUA_REDUCED_DEBUG`), `LuaLibs.Default` in a release player means everything but `Debug`; the Editor and Development Builds keep the whole library, and `LuaLibs.All` asks for it explicitly. `xlua.util`'s `state` and `print_func_ref_by_csharp` need the whole `debug` library, and `perf.profiler` needs `os.clock`.

The limits start once the constructor has finished, so they do not count against the environment's own setup. Instruction and time limits are Lua 5.4 and 5.5 only (`NotSupportedException` on Lua 5.3 and LuaJIT). An environment created with options also has `MemoryLimit` (settable; lowering it below the use makes the next growth fail), `MemoryUsed`, `MemoryPeak`, `InstructionLimit` and `TimeLimit` (settable, from the next call), `LastInstructionCount`, and `Interrupt()`, which any thread may call to stop the running call with `LuaInterruptedException` while an instruction or time limit is set (without one it does nothing unless the options set `AllowInterrupt = true`, which costs about 6 ns per call from C#; `LuaScript.Interrupt()` works without limits unless `AllowInterrupt = false`). The three exceptions derive from `LuaLimitException`, a `LuaException`. On a `new LuaEnv()` environment the limits read 0, setting one throws `InvalidOperationException`, and so do the statistics.

```csharp
var env = new LuaEnv(new LuaOptions {
    MemoryLimit = 16 << 20, HashSeed = 42,
    InstructionLimit = 10_000_000, TimeLimit = TimeSpan.FromMilliseconds(50),
});
```

In a `LuaEnv` these limits guard against runaway scripts; they are not a sandbox. Lua code that calls `debug.sethook` replaces the hook of its thread and can escape the instruction limit (the watchdog still stops the call), and every `CS.*` call runs C# code without a limit.

#### object[] DoString(string chunk, string chunkName = "chunk", LuaTable env = null)

Description:

Executes a code block.

Parameter:

- `chunk`: Lua code string.
- `chunkName`: This is used in the debug message when an error occurs, indicating a certain line in a certain code block has an error.
- `env`: This is the environment variable of this code block.

Returned value:

The returned value of the return statement in the code block.

For example: `return 1, "hello"`. `DoString` returns an array that will contain two objects. One is `1` of the double type, and the other is the string "hello".

For example:

```csharp
LuaEnv luaenv = new LuaEnv();
object[] ret = luaenv.DoString("print('hello')\r\nreturn 1");
UnityEngine.Debug.Log("ret="+ret[0]);
luaenv.Dispose();
```

#### `T LoadString<T>(string chunk, string chunkName = "chunk", LuaTable env = null)`

Description:

This loads a code block, but does not execute it. It returns the compiled chunk as a delegate or LuaFunction, according to T.

Parameter:

- `chunk`: Lua code string.
- `chunkName`: This is used in the debug message when an error occurs, indicating a certain line in a certain code block has an error.
- `env`: This is the environment variable of this code block.

Returned Value:

This is the delegate or LuaFunction type that represents the code block.

#### LuaTable Global;

Description:

This is the LuaTable representing the Lua global environment.

### void Tick()

Description:

This clears Lua's LuaBase objects that have not been manually released (for example LuaTable, LuaFunction), and other things.

This needs to be called periodically, for example in the Update of MonoBehaviour.

### void AddLoader(CustomLoader loader)

Description:

Adds a custom loader

Parameter:

- `loader`: A delegate that includes the loaded function. The type is `delegate byte[] CustomLoader(ref string filepath)`. When a file is required, the loader will be called back. Its parameters are the parameters used to call require. If the loader finds the file, it reads it into memory and returns a byte array. If debug support is required, the filepath should be set to one the IDE can find (relative or absolute).

Custom loaders run after xLua's built-in libraries and before Resources. See Loading Lua sources (Lua_Loaders.md) for loaders for each storage location.

`StreamingAssetsLoader.Load` is such a loader for `Application.streamingAssetsPath`: `luaenv.AddLoader(StreamingAssetsLoader.Load)` makes `require 'a.b'` read `StreamingAssets/a/b.lua`, as the legacy preset's StreamingAssets searcher does, also in the minimal preset, which has no searcher.

#### StreamingAssetsLoader (static class)

- `byte[] Load(ref string filepath)`: a `CustomLoader`. Reads the module `a.b` from `StreamingAssets/a/b.lua` and sets `filepath` to `a/b.lua`; returns `null`, with `filepath` unchanged, when there is no such file.
- `byte[] ReadAllBytes(string path)`: the bytes of the file at `path` below StreamingAssets (`/` between folders), or `null` when there is none.

Both read synchronously and work on any thread: with `File` in the Editor, on desktop and iOS, through Android's `AssetManager` on Android, and with `UnityEngine.WWW` from the OBB of an Android APK built with "Split Application Binary". On WebGL they throw `NotSupportedException`, because a synchronous call cannot wait for a download. See [Loading Lua sources](Lua_Loaders.md#streamingassets).

#### void Dispose()

Description:

This disposes the LuaEnv.

> LuaEnv usage suggestion: use only one instance globally. Call Tick periodically from Update, and call Dispose when it is not required.

#### void SetOwnerThread()

Description:

Makes the calling thread the owner of the LuaEnv. A LuaEnv is not thread-safe and belongs to the first thread that uses it after the constructor, not to the thread that created it: an env created in a field initializer or static constructor that Unity runs on a loading thread, then used on the main thread, needs no hand-off. With the [bridge checks](#xlua_bridge_checks) (the Editor, Development Builds, `XLUA_GENERAL`), and without `THREAD_SAFE` or `HOTFIX_ENABLE`, which lock every call, a call from another thread throws `InvalidOperationException`; this includes a C# finalizer that calls Lua. To hand an env that one thread has used to another thread, call `SetOwnerThread()` on that thread once the previous one is done with it. `Interrupt()` and the statistics work from any thread, and `LuaOptions.CheckThread = false` turns the check off for one env. `LuaScript.SetOwnerThread()` does the same for a script.

### LuaTable type

#### `T Get<T>(string key)`

Description:

Gets the value at key and converts it to T. A missing value (nil) becomes null for a reference type. For a value type it throws `InvalidCastException: can not assign nil to T` before any conversion: `int`, `float`, `bool`, an enum, a struct and `Nullable<T>` alike, in every binding mode, as upstream xLua does (kept by owner decision Q55 of 2026-10-28). A value of another kind also throws for a struct, such as a number where a `Vector3` is expected (`InvalidCastException: UnityEngine.Vector3 expected, got number`); a table converts.

Where a key may be missing, test it first with `ContainsKey`, or read it as `object` (or another class type), which gives null for nil:

```csharp
int hp = table.ContainsKey("hp") ? table.Get<int>("hp") : 100;
object raw = table.Get<object>("hp");   // null when missing
```

`LuaTable` has no `TryGet`; the `TryGet` methods of `LuaTableRef` and `LuaValue` belong to `LuaScript`, below.

#### `T GetInPath<T>(string path)`

Description:

The difference from Get is that this function will identify the "." in the path. For example, `var i = tbl.GetInPath<int>("a.b.c")` is equivalent to executing `i = tbl.a.b.c` in Lua. This avoids calling Get multiple times and obtaining intermediate variables. It has higher execution efficiency. A missing value throws for a value type as `Get<T>` does; `GetInPath<object>` gives null for it.

#### `void SetInPath<T>(string path, T val)`

Description:

Setter corresponding to `GetInPath<T>`.

#### `void Get<TKey, TValue>(TKey key, out TValue value)`

Description:

The key of the APIs described above can only be a string, but this API has no such restriction. A missing value throws for a value type, as for `Get<T>`.

#### `bool ContainsKey<TKey>(TKey key)`

Description:

Whether the value at key is not nil, without converting it. Use it before `Get` where a key may be missing.

#### `void Set<TKey, TValue>(TKey key, TValue value)`

Description:

This is the setter corresponding to `Get<TKey, TValue>`.

#### `T Cast<T>()`

Description:

Converts the table to a type specified by T. It can be an interface with a CSharpCallLua declaration, a type or struct with a default constructor, a Dictionary, a List, and so on.

#### void SetMetaTable(LuaTable metaTable)

Description:

Sets metaTable as this table's metatable.

### LuaFunction type

> Note: Accessing Lua functions with this type will result in overhead from boxing and unboxing. For the sake of performance, do not use this type if frequent calls are required. It is recommended that you use `table.Get<ABCDelegate>` to get a delegate and then call it (assuming ABCDelegate is a delegate of C#). Before using `table.Get<ABCDelegate>`, configure ABCDelegate as `CSharpCallLua`.

#### object[] Call(params object[] args)

Description:

This calls the Lua function with the variable parameters and returns the returned value of the call.

#### object[] Call(object[] args, Type[] returnTypes)

Description:

This calls the Lua function and specifies the type of the returned parameter. The system will automatically convert the specified type.

#### void SetEnv(LuaTable env)

Description:

Equivalent to Lua's setfenv function: env becomes the global table of this function only.

On Lua 5.3 and later this sets the function's `_ENV` upvalue; other functions that shared it,
such as the rest of the chunk that defined a closure, keep their globals. C functions and
functions that use no globals are left unchanged.

### LuaDLL.Bridge type (debugging aid)

#### static string StackToString(IntPtr L), StackToString(IntPtr L, int maxStringLength)

Description:

Describes the Lua stack of L as text, one line per slot from 1 to the top. Use it while
writing raw lua_CSFunction callbacks ([MonoPInvokeCallback], LuaEnv.AddBuildin) or while
debugging the bridge, for example `Debug.Log(XLua.LuaDLL.Bridge.StackToString(L))`
(`XLua.LuaDLL.Lua.StackToString` before 2026-10-18; `LuaDLL.Lua` now holds only the Lua C API).

Each line shows the absolute and negative index, the Lua type and a short value:
integer or float on Lua 5.3+, strings quoted with control characters escaped and cut
after maxStringLength characters (default 64), the address of tables, functions and threads, the
type and ToString() of xLua C# objects, the type of copied C# structs, and the value
of int64 userdata on LuaJIT.

It leaves the stack as it was, runs no metamethod (no `__tostring` or `__index`), does not
allocate on the Lua side and never throws: a slot it cannot describe reads
`<error: ...>`. It uses only native functions xLua already exports.
The C# ToString() of a pushed object does run, so keep it out of hot paths.

Example output:

```text
Lua stack (top 5):
  [1 | -5] number(integer) 42
  [2 | -4] number(float) 1.5
  [3 | -3] string(8) "hi\nthere"
  [4 | -2] table: 0x5581c2d0a3f0
  [5 | -1] userdata(C#) System.Text.StringBuilder: hey
```

### LuaScript type (preview)

Sandboxed Lua 5.4 or 5.5 without the C# bridge: scripts reach no C# object,
load text only and run under limits ([core-luascript.md](../../../docs/design/core-luascript.md)).
It compiles with the scripting define `XLUA_LUASCRIPT` (next to `LuaEnv`) or
`XLUA_CORE_ONLY`, and needs a native library with the sandbox (the core
preset's, or one built with `XLUA_LUASCRIPT=ON`). Table handles and C#-driven
coroutines are not there yet.

```csharp
using var lua = new LuaScript(new LuaOptions {
    MemoryLimit = 4 << 20, InstructionLimit = 10_000_000, TimeLimit = TimeSpan.FromMilliseconds(50),
    Libraries = LuaLibs.String | LuaLibs.Table | LuaLibs.Math,
});
lua.Register("log", (ref LuaCall c) => Debug.Log(c.GetString(0)));
lua.Register<long, long, long>("bonus", (level, streak) => level * 2 + streak);
lua.Load(remoteCode, "remote");                  // chunk name "@remote": errors read "remote:3: ..."
long reward = lua.Call<long>("calcReward", level, "gold");
var results = new LuaValue[2];
int n = lua.Call("quest.onStart", new LuaValue[] { 1, true }, results);
```

- **Construction:** `new LuaScript()` uses `LuaOptions.ScriptDefaults` (64 MiB,
  1 s per call); `new LuaScript(options)` copies the options. `Libraries`
  may not name `Io`, `Os`, `Debug` or `Package` (`ArgumentException`). The
  base library has no `print`, `dofile`, `loadfile`, `warn`, `string.dump` or
  `collectgarbage` other than `"count"`, and `setmetatable` refuses `__gc`.
- **`Load(code, chunkName)`**, `Load<T>`, `Load(code, chunkName, results)` and
  `Load(ReadOnlySpan<byte> utf8, chunkName)` run text chunks; a binary chunk
  or a syntax error throws `LuaSyntaxException`.
- **`Call(function, ...)`** and **`Call<T>(function, ...)`** call a global
  function by name (dotted names walk tables; lookups are raw) with up to four
  `LuaValue` arguments or a span; `T` is `long`, `int`, `double`, `float`,
  `bool`, `string`, `byte[]` or `LuaValue`. `Call(function, args, results)`
  returns how many values the function returned; `Call<T1, T2>` and
  `Call<T1, T2, T3>` return tuples. Numbers, booleans and strings as arguments
  allocate nothing on the managed heap; a string result allocates the string.
- **`LuaValue`:** nil, a boolean, a 64-bit integer, a float or a string, with
  implicit conversions from the C# types; `AsLong`, `AsDouble`, `AsString`,
  `AsBytes`, `AsBool` (Lua's truth) and `TryGet...`. Tables and functions a
  call returns carry only their type for now.
- **Limits and statistics:** `MemoryLimit`, `InstructionLimit` and `TimeLimit`
  (per top-level call) can change between calls; `MemoryUsed`, `MemoryPeak`,
  `LastInstructionCount`; `Interrupt()` from any thread; `CollectGarbage()`.
- **Errors:** `LuaRuntimeException` (message, and the traceback in
  `LuaStackTrace`), `LuaSyntaxException`, `LuaTypeException` (a result of
  another type), `LuaMemoryLimitException`, `LuaInstructionLimitException`,
  `LuaTimeLimitException`, `LuaInterruptedException`.
- **`require`:** set `ModuleResolver`, a delegate that returns a module's text;
  each module loads once per script, into an environment of its own.
- **Host functions:** `Register(name, LuaCallback)` makes a C# function
  callable from every environment of the script; `"library.name"` puts it
  into a library table that each environment copies, like `string`.
  `LuaCallback` is `void (ref LuaCall call)`: `ArgCount`, `ArgType(i)`,
  `GetBool`, `GetLong`, `GetDouble`, `GetString`, `GetUtf8` (the bytes,
  without a copy), `ArgEquals(i, "text")`, `Get(i)`, `OptLong`, `OptDouble`,
  `OptString` read the arguments (0-based) without a P/Invoke, and
  `GetBufferSpan` gives a buffer argument's bytes in place; `Return(...)`
  (bool, long, double, string, bytes, `LuaValue`, `LuaBufferRef`) and
  `ReturnNil()` add results. Primitive arguments and results allocate nothing. A wrong
  argument type gives Lua's `bad argument #1 to 'f' (number expected, got
  string)`; any other exception becomes a Lua error, and if the script does
  not catch it the call throws `LuaRuntimeException` with the exception as
  `InnerException`. The typed shorthands `Register<A1, R>(name, Func<A1, R>)`
  and `Register<A1>(name, Action<A1>)` (0 to 4 arguments; long, int, double,
  float, bool, string, byte[], LuaValue) do the same. `Unregister(name)`
  removes a function; a script that kept it gets an error when it calls it.
  `LuaCall.Yield()` suspends the coroutine that called the function (Lua's
  `coroutine.resume` or `wrap`); its results are the yielded values. A host
  function may call back into the script (`Call`, `Load`) under the same
  limits; it is never preempted, but a limit that ran out while it ran stops
  the call as soon as it returns.
- **Threads:** use a script from one thread, the first that uses it after
  the constructor (`LuaOptions.CheckThread`, default on), or from the last
  thread that called `SetOwnerThread()`; `Interrupt`, `IsDisposed` and the statistics work from
  any thread. `Dispose()` closes the state.

## Lua API

### CS objects

#### CS.namespace.class(...)

Description:

This calls a C# type constructor and returns a type instance.

For Example:

```lua
local v1=CS.UnityEngine.Vector3(1,1,1)
```

#### CS.namespace.class.field

Description:

This accesses a C# static member.

For Example:

```lua
print(CS.UnityEngine.Vector3.one)
```

#### CS.namespace.enum.field

Description:

This accesses an enumerated value.

#### Typeof Functions

Description:

This is similar to the typeof keyword in C#: a Type object is returned. For example, in GameObject.AddComponent, one overload requires a Type parameter.

For Example:

```lua
newGameObj:AddComponent(typeof(CS.UnityEngine.ParticleSystem))
```

#### Unsigned 64-bit is supported

##### uint64.tostring

Description:

Unsigned number to string.

##### uint64.divide

Description:

Unsigned number division.

##### uint64.compare

Description:

Unsigned comparison: 0 is returned for equal, positive for greater than, and negative for less than.

##### uint64.remainder

Description:

Unsigned modulus.

##### uint64.parse

Description:
String to unsigned number.

##### LuaJIT only: uint64.band, bor, bxor, bnot, lshift, rshift, arshift, idiv

Description:

Lua 5.3 and later have 64-bit integer operators; LuaJIT builds of xLua add these
helpers for the int64 userdata instead, and Lua 5.3+ builds do not register them.

- `uint64.band(a, b)`, `uint64.bor(a, b)`, `uint64.bxor(a, b)`: `a & b`, `a | b`, `a ~ b`.
- `uint64.bnot(a)`: `~a`.
- `uint64.lshift(x, n)`: `x << n`.
- `uint64.rshift(x, n)`: `x >> n` (logical: fills with zeros).
- `uint64.arshift(x, n)`: arithmetic right shift (copies bit 63, as C# `>>` does for long).
- `uint64.idiv(a, b)`: `a // b` (floor division).

Arguments are int64 values or Lua numbers; a number is truncated toward zero, a NaN
becomes 0, and a number beyond the int64 range saturates to it, as for the arithmetic
operators. Any other argument raises "Integer64 expected". Every result is an int64
userdata, also for two number arguments.

Shifts follow Lua 5.3: a negative n shifts the other way, and shifting by 64 bits or
more gives 0. arshift to the right by 64 or more gives 0 for a non-negative x and -1
for a negative one.

idiv rounds toward minus infinity like Lua 5.3's `//`: `uint64.idiv(-7, 2)` is -4, while
the `/` operator truncates (`-7 / 2` is -3). `uint64.idiv(a, 0)` raises
"attempt to perform 'n//0'", and `-2^63 // -1` wraps to `-2^63`. The `%` operator still
truncates, so `idiv(a, b) * b + a % b` differs from a when a and b have opposite signs
and b does not divide a.

With UINT_ESPECIALLY (ulong as a separate unsigned kind), band, bor, bxor and idiv
follow the arithmetic operators: an unsigned operand makes the result unsigned
(idiv then divides unsigned), a number takes the kind of the other operand, and an
int64 with a uint64 raises "type not match". bnot and the shifts return the kind of
their first argument; the shift count can be of either kind.

Example:

```lua
local id = CS.Game.Player.Id          -- a C# long
local shard = uint64.band(id, 0xFF)
local high = uint64.rshift(id, 32)
print("shard " .. shard .. ", high " .. high)
```

#### xlua.structclone

Description:

This clones a c# structure.

#### xlua.private_accessible(class)

Description:

This makes the private fields, properties, methods of a type available.

Example:

```lua
xlua.private_accessible(CS.UnityEngine.GameObject)
```

#### xlua.get_generic_method

Gets a generic method definition. Call the returned function with C# type arguments to construct the method. Pass the instance explicitly as the first argument when invoking an instance method.

```lua
local foo_generic = xlua.get_generic_method(CS.GetGenericMethodTest, 'Foo')
local bar_generic = xlua.get_generic_method(CS.GetGenericMethodTest, 'Bar')

local foo = foo_generic(CS.System.Int32, CS.System.Double)
local bar = bar_generic(CS.System.Double, CS.UnityEngine.GameObject)

-- call instance method
local o = CS.GetGenericMethodTest()
local ret = foo(o, 1, 2)
print(ret)

-- call static method
bar(2, nil)
```

Generic methods must be available in the target runtime; on AOT platforms, ensure the required instantiations are compiled. See the `09_GenericMethod` example.

When the type has no generic method of that name, `xlua.get_generic_method` returns `nil` in the legacy preset, as upstream did. With `XLUA_RESTRICT_PRIVATE_ACCESS` (part of the minimal preset) it raises instead:

```text
xlua.get_generic_method: UnityEngine.GameObject has no public generic method 'GetComponent'.
```

In an IL2CPP player the message goes on: stripping may have removed the method; add `[ReflectionUse]` to the type (or `[assembly: XLua.ReflectionUse(typeof(...))]`), or keep it in a link.xml. The minimal preset's generated link.xml does not keep a public generic method that only this function reaches.

#### Cast Function

Description:

This indicates that the object is accessed with a specific interface, which is useful when the implementation type is inaccessible (such as internal modification). Use it in the following way (assuming that the following calc object implements C#'s PerformentTest.ICalc interface):

For Example:

```lua
cast(calc, typeof(CS.PerformentTest.ICalc))
```

Then, no other APIs are available.

The first argument must be a C# object that is an instance of the type: its own type, a base class or an interface it implements. Any other value (a table, a number, a struct, a LuaJIT int64, or an object of an unrelated type) raises an error. Before xlua-ng, `cast` set the metatable on any value; on a number or boolean that changed the metatable of every number or boolean.
Accessing a csharp object is like accessing a table. Calling a function is like calling the Lua function. Operators can also be used to access the C# operator. Here is an example:

```lua
local v1=CS.UnityEngine.Vector3(1,1,1)
local v2=CS.UnityEngine.Vector3(1,1,1)
v1.x = 100
v2.y = 100
print(v1, v2)
local v3 = v1 + v2
print(v1.x, v2.x)
print(CS.UnityEngine.Vector3.one)
print(CS.UnityEngine.Vector3.Distance(v1, v2))
```

## Type mapping

### Basic data type

|C# type|Lua type|
|-|-|
|sbyte, byte, short, ushort, int ,uint ,double ,char ,float|number|
|decimal|userdata|
|long ,ulong|userdata (LuaJIT) / lua_Integer (Lua 5.3 and later)|
|byte[]|string|
|bool|boolean|
|string|string|

### Complex data type

|C# type|Lua type|
|-|-|
|LuaTable|table|
|LuaFunction|function|
|class or struct instance|userdata, table|
|method, delegate|function|

#### LuaTable:

If C# specifies inputting the LuaTable type (including the input parameters of the C# method or the returned value of the Lua method) from Lua, then it must be a table in Lua. Or, if C# does not specify the type, the table in Lua should be converted to LuaTable .

#### LuaFunction:

If C# specifies inputting the LuaFunction type (including the input parameters of the C# method or the returned value of the Lua method) from Lua, then it must be a function in Lua. Or, if C# does not specify the type, the function on Lua should be converted to LuaFunction.

#### LuaUserData:

This is Lua userdata corresponding to the object that is not a managed C# object.

#### Class or struct instance:

This transfers a class or struct instance from C#, maps it to Lua userdata, and accesses the member of the userdata via __index.
If C# specifies inputting objects of the specified type from Lua, then the userdata of the type instance is used directly in Lua. If the specified type has a default constructor, the table in Lua is automatically converted. The conversion rule is: call the constructor to construct an instance, and convert the field corresponding to table to each setter member in C#.

A table never converts to `UnityEngine.Object` or a type derived from it (`GameObject`, `Component`, `MonoBehaviour`, `ScriptableObject`...): passing one raises `bad argument #1 to 'f' (UnityEngine.GameObject expected, got table)` for a parameter, field or property, and `InvalidCastException: UnityEngine.GameObject expected, got table` for a value C# reads from Lua, such as a delegate's result. Upstream made a new object from the table, which for a `GameObject` added an empty object to the scene ([Tencent/xLua#918](https://github.com/Tencent/xLua/issues/918)). A struct takes the fields the table sets and keeps the default value of the others; a table that sets none of them, such as `{}` or `{X = 1}` for a `Vector3`, raises `InvalidCastException: table sets none of the fields of UnityEngine.Vector3 (x, y, z)`, where upstream gave the zero value ([Tencent/xLua#751](https://github.com/Tencent/xLua/issues/751)). Pass `Vector3.zero` for a zero vector.

#### Method and delegate:

Both member methods and delegates correspond to the Lua functions.
The ordinary parameters and reference parameters on C# correspond to the Lua function parameters. The returned value on C# corresponds to the first returned value on Lua. The reference parameters and out parameters correspond to the 2nd through Nth return values from Lua in sequence.

## Macros

#### HOTFIX_ENABLE

This enables the hotfix function.

#### NOT_GEN_WARNING

This prints warning when there is reflection.

#### GEN_CODE_MINIMIZE

Generates code in a way that minimizes the code segments. The source generator follows it: each wrapped member becomes a plain C# method that one native trampoline calls, instead of a native callback of its own. With many wrapped types the IL2CPP player is noticeably smaller, and each call into C# costs a few nanoseconds more. Set it in the Player Settings scripting defines, so that xLua and the assemblies with bindings compile with it.

#### XLUA_NO_STREAMING_ASSETS_LOADER

Leaves out the StreamingAssets searcher, so `require` no longer looks in `Application.streamingAssetsPath`. The minimal preset defines it. `luaenv.AddLoader(StreamingAssetsLoader.Load)` gives the same lookup back. Until 2026-10-29 the searcher read Android's StreamingAssets with `UnityEngine.WWW`, and the define dropped the UnityWebRequest engine modules with it; since then it reads them through Android's `AssetManager`, and the define saves only the searcher's own code. See [Loading Lua sources](Lua_Loaders.md#xlua_no_streaming_assets_loader).

#### XLUA_LOCK_METATABLES

Locks the metatables that xLua creates. Each one gets a `__metatable` field, so Lua's `getmetatable` returns the string `"locked"` and `setmetatable` raises an error. This covers:

- the metatables of C# objects, structs, enums, arrays, delegates and C# functions;
- class tables (`CS.Some.Type`), the `CS` namespace tables and `base(obj)` proxies;
- the int64/uint64 userdata on LuaJIT;
- xLua's registry tables;
- any table passed to `xlua.setmetatable`.

Without this define, a script can call `getmetatable(obj).__gc(obj)`. That releases the C# handle of an object Lua still uses. It also lets a script read or replace the `__index`, `__newindex` and `__tostring` functions that the bridge relies on.

Code that reads these metatables must use `debug.getmetatable` and `debug.setmetatable`, which ignore the field. `xlua.util` and the generated code already do. `xlua.getmetatable` and `xlua.setmetatable` keep working. The lock does not affect the debug library or `xlua.metatable_operation`, so it is not a sandbox on its own; see [Security](Security.md). [XLUA_RESTRICT_GETMETATABLE](#xlua_restrict_getmetatable) keeps `__gc` out of what `xlua.getmetatable` returns.

The define is off by default in the legacy preset.

#### XLUA_BRIDGE_CHECKS

The bridge checks: before the bridge uses a userdata as a C# object or struct, it checks that the userdata's metatable carries xLua's tag.

- Every C# object the bridge reads from Lua (the `self` of a method, field and property access, `__tostring`, delegate calls, enum operators) must be such a userdata. Without the checks, the bridge reads the first four bytes of any full userdata as an object handle, so a foreign userdata (an `io` file, a socket) passed as `self`, for example `obj.Method(other)` where `obj:Method(other)` was meant, can name another C# object.
- The getters and setters returned by `xlua.genaccessor` also require the tag, as `xlua.structclone` already does. Without the checks they accept any full userdata whose first eight bytes look like a struct header.
- When C# pushes an object Lua already holds, the bridge takes the userdata from its cache only if the cached value is a C# object userdata for that object. A script with `debug.getregistry` can otherwise store any value in the cache and have it returned for that object everywhere.
- The registry entries xLua reads, which a script with `debug.getregistry` can replace, are used only when they are tables: the metatable of each C# type (by reference and by name), the userdata cache, the enum caches of generated code and, on LuaJIT, the int64 metatable `registry[8]`. A replaced metatable leaves the pushed object, struct or int64 without one, so the script gets a Lua error when it uses the value; a replaced cache is not used; int64 arithmetic raises an error. Without the checks such a script crashes the process.
- Every call from C# into a `LuaEnv` (`DoString`, `LuaTable`, `LuaFunction`, delegate and interface bridges) must come from the env's owner thread ([SetOwnerThread](#void-setownerthread)), unless `THREAD_SAFE` or `HOTFIX_ENABLE` is defined. Two threads in one env, or a finalizer that calls Lua, corrupt the Lua heap without it.

Correct scripts behave the same with and without the checks; a foreign value gets the error a wrong argument gets. They follow the build type in every preset: on in the Editor (`UNITY_EDITOR`), in Development Builds (`DEVELOPMENT_BUILD`) and outside Unity (`XLUA_GENERAL`), off in release players, which save about 10 to 13 ns per instance call, field or property access and 14 ns per push of an object Lua already holds. Define `XLUA_BRIDGE_CHECKS` as a scripting define to keep them in release players. A light userdata (such as an `IntPtr`) is never read as an object, with or without the checks. Code of your own that tests `XLUA_BRIDGE_CHECKS` sees it defined in debug builds only if it copies the `<xlua-preset>` block that `Tools/presets.py` writes at the top of xLua's files.

#### XLUA_NO_BRIDGE_CHECKS

Leaves the [bridge checks](#xlua_bridge_checks) out of the Editor, Development Builds and `XLUA_GENERAL` builds as well, so they behave as release players do. Use it to profile the call costs of a Development player, or in a program that ships on `XLUA_GENERAL`, where xLua has no release build type. `XLUA_BRIDGE_CHECKS` wins when both are defined.

#### Bridge hardening defines

Each of these defines closes one way a script can make the bridge act on state it does not own, and changes what a correct legacy script may see. All are off by default in the legacy preset and on in the minimal preset, in every build type ([presets.md](../../../docs/design/presets.md)). None of them, alone or together, makes a `LuaEnv` a sandbox: while scripts can reach `CS.System.Type` and reflection, they reach every member of every type; see [Security](Security.md). The reasons and measurements are in [bridge-audit.md](../../../docs/research/bridge-audit.md).

##### XLUA_RESTRICT_GETMETATABLE

`xlua.getmetatable(cls)` returns a copy of the type's metatable without `__gc` and the tag, so a script cannot call the finalizer on a live object. Changes to the copy do not affect the type until it is passed to `xlua.setmetatable`, which puts `__gc` and the tag back. Code that edits the returned table in place must call `xlua.setmetatable` with it.

##### XLUA_LOCK_XLUA_TABLE

The global `xlua` becomes a read-only proxy: `xlua.cast = f` raises, `getmetatable(xlua)` returns `"locked"`, and `pairs(xlua)` lists the fields on Lua 5.3 and later (not on LuaJIT). `xlua.hotfix`, `xlua.getmetatable`, `xlua.setmetatable`, `xlua.setclass` and the `CS` lookup use the functions they had when the `LuaEnv` started, so replacing `xlua.access` or `rawset` no longer changes them. Limits: `rawset(xlua, k, v)` still adds a field that hides the real one (no Lua table can refuse `rawset`), and the global `xlua` itself can still be replaced. Code that adds functions to `xlua` must use `rawset`.

##### XLUA_RESTRICT_PRIVATE_ACCESS

Without `HOTFIX_ENABLE`, `xlua.access`, `xlua.private_accessible` and `xlua.hotfix` are not defined. With `HOTFIX_ENABLE` they are, but `xlua.access` only reaches the fields the hotfix injector adds (`__Hotfix...`). In both cases `xlua.tofunction` and `xlua.get_generic_method` only take public methods, and `xlua.get_generic_method` raises `xlua.get_generic_method: T has no public generic method 'M'.` when the type has none of that name (in an IL2CPP player with a hint about stripping) instead of returning `nil`.

##### XLUA_NO_LOAD_ASSEMBLY

`xlua.load_assembly` is not defined, so scripts cannot load assemblies by name or path.

#### XLUA_NO_SIGNATURE_LOADER

Leaves `SignatureLoader` out of the build. Without it the RSA and SHA-256 code from `System.Security.Cryptography` is not linked: on Android with Minimal managed stripping that is 239,384 bytes of APK; Medium stripping already removes it when nothing calls it. Projects that sign their Lua sources need `SignatureLoader`; see [Source Code Signatures](signature.md).

#### XLUA_NO_TEMPLATE

Players do not open the `template` Lua library (`template.compile`, `template.execute`) and do not compile `XLua.TemplateEngine`. The Editor (`UNITY_EDITOR`) keeps both, so Editor scripts can still fill templates ([Custom_Generate_EN.md](Custom_Generate_EN.md#template-syntax)); the command-line tools (`XLUA_GENERAL`) keep the engine. Lua code in a player can then no longer call `template.compile`.

#### XLUA_NO_UTIL

Leaves out `xlua.util`, which xLua otherwise embeds in its assembly and finds after your loaders and `Resources` ([Loading Lua sources](Lua_Loaders.md)). `require 'xlua.util'` then fails with "module not found" unless a loader or a `Resources` file provides it.

#### XLUA_NO_PROFILER

Leaves out the embedded `perf.profiler` ([Performance Analysis Tools](Performance_Analysis.md)) from release players. The Editor and Development Builds keep it: the profiler is compiled when `UNITY_EDITOR` or `DEVELOPMENT_BUILD` is defined, which Unity sets for scripts built with the Development Build option.

