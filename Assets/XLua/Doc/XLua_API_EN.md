## C# APIs

### LuaEnv type

#### object[] DoString(string chunk, string chunkName = "chunk", LuaTable env = null)

Description:

```text
Executes a code block.
```

Parameter:

```text
chunk: Lua code string;
chunkName: This is used in the debug message when an error occurs, indicating a certain line in a certain code block has an error.
env: This is the environment variable of this code block.
```

Returned value:

```text
The returned value of the return statement in the code block.
For example: return 1, "hello". DoString returns an array that will contain two objects. One is 1 of the double type, and the other is the string "hello".
```

For example:

```csharp
LuaEnv luaenv = new LuaEnv();
object[] ret = luaenv.DoString("print('hello')\r\nreturn 1");
UnityEngine.Debug.Log("ret="+ret[0]);
luaenv.Dispose();
```

#### T LoadString<T>(string chunk, string chunkName = "chunk", LuaTable env = null)

Description:

```text
This loads a code block, but does not execute it. It returns the compiled chunk as a delegate or LuaFunction, according to T.
```

Parameter:

```text
chunk: Lua code string;
chunkName: This is used in the debug message when an error occurs, indicating a certain line in a certain code block has an error.
env: This is the environment variable of this code block.
```

Returned Value:

```text
This is the delegate or LuaFunction type that represents the code block.
```

#### LuaTable Global;

Description:

```text
This is the LuaTable representing the Lua global environment.
```

### void Tick()

Description:

```text
This clears Lua's LuaBase objects that have not been manually released (for example LuaTable, LuaFunction), and other things.
This needs to be called periodically, for example in the Update of MonoBehaviour.
```

### void AddLoader(CustomLoader loader)

Description:

```text
Adds a custom loader
```

Parameter:

```text
loader: A delegate that includes the loaded function. The type is delegate byte[] CustomLoader(ref string filepath). When a file is required, the loader will be called back. Its parameters are the parameters used to call require. If the loader finds the file, it reads it into memory and returns a byte array. If debug support is required, the filepath should be set to one the IDE can find (relative or absolute).
Custom loaders run after xLua's built-in libraries and before Resources. See Loading Lua sources (Lua_Loaders.md) for loaders for each storage location.
```

#### void Dispose()

Description:

```text
This disposes the LuaEnv.
```

> LuaEnv usage suggestion: use only one instance globally. Call Tick periodically from Update, and call Dispose when it is not required.

### LuaTable type

#### T Get<T>(string key)

Description:

```text
Gets the value at key and converts it to T. A missing value can become null for reference types; conversion to an incompatible type, including nil to a non-nullable value type, can throw an exception.
```

#### T GetInPath<T>(string path)

Description:

```text
The difference from Get is that this function will identify the "." in the path. For example, var i = tbl.GetInPath<int>("a.b.c") is equivalent to executing i = tbl.a.b.c in Lua. This avoids calling Get multiple times and obtaining intermediate variables. It has higher execution efficiency.
```

#### void SetInPath<T>(string path, T val)

Description:

```text
Setter corresponding to GetInPath<T>.
```

#### void Get<TKey, TValue>(TKey key, out TValue value)

Description:

```text
The key of the APIs described above can only be a string, but this API has no such restriction.
```

#### void Set<TKey, TValue>(TKey key, TValue value)

Description:

```text
This is the setter corresponding to Get<TKey, TValue>.
```

#### T Cast<T>()

Description:

```text
Converts the table to a type specified by T. It can be an interface with a CSharpCallLua declaration, a type or struct with a default constructor, a Dictionary, a List, and so on.
```

#### void SetMetaTable(LuaTable metaTable)

Description:

```text
Sets metaTable as this table's metatable.
```

### LuaFunction type

> Note: Accessing Lua functions with this type will result in overhead from boxing and unboxing. For the sake of performance, do not use this type if frequent calls are required. It is recommended that you use table.Get<ABCDelegate> to get a delegate and then call it (assuming ABCDelegate is a delegate of C#). Before using table.Get<ABCDelegate>, add ABCDelegate to the list of generated code.

#### object[] Call(params object[] args)

Description:

```text
This calls the Lua function with the variable parameters and returns the returned value of the call.
```

#### object[] Call(object[] args, Type[] returnTypes)

Description:

```text
This calls the Lua function and specifies the type of the returned parameter. The system will automatically convert the specified type.
```

#### void SetEnv(LuaTable env)

Description:

```text
Equivalent to Lua's setfenv function: env becomes the global table of this function only.
On Lua 5.3 and later this sets the function's _ENV upvalue; other functions that shared it,
such as the rest of the chunk that defined a closure, keep their globals. C functions and
functions that use no globals are left unchanged.
```

### LuaDLL.Lua type (debugging aid)

#### static string StackToString(IntPtr L), StackToString(IntPtr L, int maxStringLength)

Description:

```text
Describes the Lua stack of L as text, one line per slot from 1 to the top. Use it while
writing raw lua_CSFunction callbacks ([MonoPInvokeCallback], LuaEnv.AddBuildin) or while
debugging the bridge, for example Debug.Log(XLua.LuaDLL.Lua.StackToString(L)).
Each line shows the absolute and negative index, the Lua type and a short value:
integer or float on Lua 5.3+, strings quoted with control characters escaped and cut
after maxStringLength characters (default 64), the address of tables, functions and threads, the
type and ToString() of xLua C# objects, the type of copied C# structs, and the value
of int64 userdata on LuaJIT.
It leaves the stack as it was, runs no metamethod (no __tostring or __index), does not
allocate on the Lua side and never throws: a slot it cannot describe reads
"<error: ...>". It uses only native functions xLua already exports.
The C# ToString() of a pushed object does run, so keep it out of hot paths.
```

Example output:

```text
Lua stack (top 5):
  [1 | -5] number(integer) 42
  [2 | -4] number(float) 1.5
  [3 | -3] string(8) "hi\nthere"
  [4 | -2] table: 0x5581c2d0a3f0
  [5 | -1] userdata(C#) System.Text.StringBuilder: hey
```

## Lua API

### CS objects

#### CS.namespace.class(...)

Description:

```text
This calls a C# type constructor and returns a type instance.
```

For Example:

```lua
local v1=CS.UnityEngine.Vector3(1,1,1)
```

#### CS.namespace.class.field

Description:

```text
This accesses a C# static member.
```

For Example:

```lua
print(CS.UnityEngine.Vector3.one)
```

#### CS.namespace.enum.field

Description:

```text
This accesses an enumerated value.
```

#### Typeof Functions

Description:

```text
This is similar to the typeof keyword in C#: a Type object is returned. For example, in GameObject.AddComponent, one overload requires a Type parameter.
```

For Example:

```lua
newGameObj:AddComponent(typeof(CS.UnityEngine.ParticleSystem))
```

#### Unsigned 64-bit is supported

##### uint64.tostring

Description:

```text
Unsigned number to string.
```

##### uint64.divide

Description:

```text
Unsigned number division.
```

##### uint64.compare

Description:

```text
Unsigned comparison: 0 is returned for equal, positive for greater than, and negative for less than.
```

##### uint64.remainder

Description:

```text
Unsigned modulus.
```

##### uint64.parse

Description:
String to unsigned number.

##### LuaJIT only: uint64.band, bor, bxor, bnot, lshift, rshift, arshift, idiv

Description:

```text
Lua 5.3 and later have 64-bit integer operators; LuaJIT builds of xLua add these
helpers for the int64 userdata instead, and Lua 5.3+ builds do not register them.

uint64.band(a, b), uint64.bor(a, b), uint64.bxor(a, b)   a & b, a | b, a ~ b
uint64.bnot(a)                                           ~a
uint64.lshift(x, n)                                      x << n
uint64.rshift(x, n)                                      x >> n (logical: fills with zeros)
uint64.arshift(x, n)                                     arithmetic right shift (copies bit 63, as C# >> does for long)
uint64.idiv(a, b)                                        a // b (floor division)

Arguments are int64 values or Lua numbers; a number is truncated toward zero, a NaN
becomes 0, and a number beyond the int64 range saturates to it, as for the arithmetic
operators. Any other argument raises "Integer64 expected". Every result is an int64
userdata, also for two number arguments.

Shifts follow Lua 5.3: a negative n shifts the other way, and shifting by 64 bits or
more gives 0. arshift to the right by 64 or more gives 0 for a non-negative x and -1
for a negative one.

idiv rounds toward minus infinity like Lua 5.3's //: uint64.idiv(-7, 2) is -4, while
the / operator truncates (-7 / 2 is -3). uint64.idiv(a, 0) raises
"attempt to perform 'n//0'", and -2^63 // -1 wraps to -2^63. The % operator still
truncates, so idiv(a, b) * b + a % b differs from a when a and b have opposite signs
and b does not divide a.

With UINT_ESPECIALLY (ulong as a separate unsigned kind), band, bor, bxor and idiv
follow the arithmetic operators: an unsigned operand makes the result unsigned
(idiv then divides unsigned), a number takes the kind of the other operand, and an
int64 with a uint64 raises "type not match". bnot and the shifts return the kind of
their first argument; the shift count can be of either kind.
```

Example:

```lua
local id = CS.Game.Player.Id          -- a C# long
local shard = uint64.band(id, 0xFF)
local high = uint64.rshift(id, 32)
print("shard " .. shard .. ", high " .. high)
```

#### xlua.structclone

Description:

```text
This clones a c# structure.
```

#### xlua.private_accessible(class)

Description:

```text
This makes the private fields, properties, methods of a type available.
```

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

#### Cast Function

Description:

```text
This indicates that the object is accessed with a specific interface, which is useful when the implementation type is inaccessible (such as internal modification). Use it in the following way (assuming that the following calc object implements C#'s PerformentTest.ICalc interface):
```

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

#### Method and delegate:

Both member methods and delegates correspond to the Lua functions.
The ordinary parameters and reference parameters on C# correspond to the Lua function parameters. The returned value on C# corresponds to the first returned value on Lua. The reference parameters and out parameters correspond to the 2nd through Nth return values from Lua in sequence.

## Macros

#### HOTFIX_ENABLE

This enables the hotfix function.

#### NOT_GEN_WARNING

This prints warning when there is reflection.

#### GEN_CODE_MINIMIZE

Generates code in a way that minimizes the code segments.

#### XLUA_NO_STREAMING_ASSETS_LOADER

Leaves out the StreamingAssets searcher, so `require` no longer looks in `Application.streamingAssetsPath`. On Android this drops xLua's only `UnityEngine.WWW` reference and with it the UnityWebRequest engine modules, unless the project uses them itself. See [Loading Lua sources](Lua_Loaders.md#xlua_no_streaming_assets_loader).

#### XLUA_LOCK_METATABLES

Locks the metatables that xLua creates. Each one gets a `__metatable` field, so Lua's `getmetatable` returns the string `"locked"` and `setmetatable` raises an error. This covers:

- the metatables of C# objects, structs, enums, arrays, delegates and C# functions;
- class tables (`CS.Some.Type`), the `CS` namespace tables and `base(obj)` proxies;
- the int64/uint64 userdata on LuaJIT;
- xLua's registry tables;
- any table passed to `xlua.setmetatable`.

Without this define, a script can call `getmetatable(obj).__gc(obj)`. That releases the C# handle of an object Lua still uses. It also lets a script read or replace the `__index`, `__newindex` and `__tostring` functions that the bridge relies on.

Code that reads these metatables must use `debug.getmetatable` and `debug.setmetatable`, which ignore the field. `xlua.util` and the generated code already do. `xlua.getmetatable` and `xlua.setmetatable` keep working. The lock does not affect the debug library or `xlua.metatable_operation`, so it is not a sandbox on its own; see [Security](Security.md).

The define is off by default in the legacy preset.

#### XLUA_NO_SIGNATURE_LOADER

Leaves `SignatureLoader` out of the build. Without it the RSA and SHA-256 code from `System.Security.Cryptography` is not linked: on Android with Minimal managed stripping that is 239,384 bytes of APK; Medium stripping already removes it when nothing calls it. Projects that sign their Lua sources need `SignatureLoader`; see [Source Code Signatures](signature.md).

#### XLUA_NO_TEMPLATE

Players do not open the `template` Lua library (`template.compile`, `template.execute`) and do not compile `XLua.TemplateEngine`. The Editor (`UNITY_EDITOR`) keeps both, so XLua/Generate Code and custom templates ([Custom_Generate_EN.md](Custom_Generate_EN.md)) work as before; the command-line tools (`XLUA_GENERAL`) keep the engine. Lua code in a player can then no longer call `template.compile`.

#### XLUA_NO_UTIL

Leaves out `xlua.util`, which xLua otherwise embeds in its assembly and finds after your loaders and `Resources` ([Loading Lua sources](Lua_Loaders.md)). `require 'xlua.util'` then fails with "module not found" unless a loader or a `Resources` file provides it.

#### XLUA_NO_PROFILER

Leaves out the embedded `perf.profiler` ([Performance Analysis Tools](Performance_Analysis.md)) from release players. The Editor and Development Builds keep it: the profiler is compiled when `UNITY_EDITOR` or `DEVELOPMENT_BUILD` is defined, which Unity sets for scripts built with the Development Build option.

