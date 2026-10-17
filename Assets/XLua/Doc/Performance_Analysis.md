# Performance Analysis Tools

xLua includes a function-duration profiler for Lua functions and Lua-to-C# calls. Measured call duration is not necessarily CPU time; for example, time spent after a coroutine yields is included in the call duration. For memory, see [Lua memory](#lua-memory) below.

## Function call duration profiler

The profiler is the Lua module `perf.profiler`:

```lua
local profiler = require 'perf.profiler'
profiler.start()
-- ... the code to measure ...
print(profiler.report('TOTAL'))
profiler.stop()
```

The module is embedded in xLua's assembly, so `require` finds it without a file in `Resources` and with any loader setup ([Loading Lua sources](Lua_Loaders.md)). The `XLUA_NO_PROFILER` scripting define leaves it out of release players; the Editor and Development Builds keep it, because it is compiled whenever `UNITY_EDITOR` or `DEVELOPMENT_BUILD` is defined (Unity defines `DEVELOPMENT_BUILD` when the Development Build option is set). In a release player built with the define, `require 'perf.profiler'` fails with "module not found".

The profiler has three simple APIs: `start`, `stop`, and `report`. `start` begins collecting statistics and takes no arguments. `stop` ends collection and also takes no arguments. You can call `report` multiple times between `start` and `stop`; you may also report without calling `stop`. Each report is returned as a string and covers the time from `start` up to that call to `report`.

`report` has one optional argument that selects the sort order:

- `"TOTAL"` sorts by total time and is the default.
- `"AVERAGE"` sorts by average time per call.
- `"CALLED"` sorts by call count.

A typical report contains these columns:

1. Function name.
2. Source location. For Lua, this includes the file and line. Exported C# code is marked `[C#]`; C functions are marked `[C]`.
3. Total time, average time per call, percentage of total measured time, and call count.

## Lua memory

`collectgarbage('count')` returns the Lua heap size in kilobytes; use it to
see sustained growth. To find what grows and what keeps it alive, take memory
snapshots in a development build.

### Memory snapshots

The Editor and Development Builds have `LuaMemoryDiagnostics`: they compile it
when `UNITY_EDITOR` or `DEVELOPMENT_BUILD` is defined. A release player has it
only with the `XLUA_MEMORY_DIAGNOSTICS` scripting define. A snapshot walks
everything Lua can reach from `_G`, the registry, the type metatables and the
LuaEnv's thread, and records:

- every table, function, userdata and thread, with its entry count and its
  size in bytes (tables and closures on Lua 5.3 to 5.5, userdata everywhere;
  LuaJIT reports no table sizes), the source line of each Lua function, and
  a shortest path from a root such as `_G.ui.panels[3].button`;
- counts and bytes by type, and the distinct reachable strings;
- the C# objects Lua holds: type, `ToString()` or the `UnityEngine.Object`
  name, whether a `UnityEngine.Object` was destroyed while Lua still holds
  it, and the Lua path to the userdata that holds it;
- the registry references C# holds into Lua: `LuaTable`, `LuaFunction` and
  delegate bridges, and xLua's own references.

```csharp
using XLua;

var before = luaEnv.TakeMemorySnapshot();   // runs a full Lua collection first
OpenAndCloseTheShop();
var after = luaEnv.TakeMemorySnapshot();
LuaMemoryDiff diff = LuaMemoryDiagnostics.Diff(before, after);
Debug.Log(diff.Report(20));                 // at most 20 lines per section
foreach (var c in diff.AddedCSharpObjects)
    Debug.Log(c.TypeName + " held by Lua at " + c.Path);
```

The diff lists the objects that appeared and disappeared, the tables that
grew, the C# objects that Lua started or stopped holding, and the new
registry references. An object counts as the same in both snapshots when it
has the same address and type and is still referenced from the same place at
least once; addresses alone are reused after a collection. When an object's
shortest path changes, it shows as removed and added.

`LuaMemorySnapshotOptions` controls a snapshot:

- `FullGc` (default true): run `collectgarbage("collect")` first, so that
  only live objects are listed.
- `CollectCSharpGarbage` (default false): also run the C# collector, wait for
  finalizers and call `LuaEnv.Tick`, so that `LuaTable`, `LuaFunction` and
  delegates that C# no longer uses release their Lua side first.
- `MaxObjects` (default 0, no limit): stop recording objects after this
  many; `Truncated` then tells that objects further from the roots are
  missing.
- `DescribeCSharpObjects` (default true): call `ToString()` on the C#
  objects Lua holds.

A snapshot can be taken from C# code that Lua called, from a finalizer
(`__gc`) and while a coroutine runs. The native walk stops the collector and
removes the debug hook while it runs, reads tables without metamethods, and
reports a Lua out-of-memory error as an exception instead of crashing. Take
snapshots on the thread that runs the LuaEnv.

Paths name the step that reaches each object: `.name` or `["key"]` for a
table value, `[3]` for an integer key, `.(key)` for a table key, and
`.(metatable)`, `.(upvalue name)`, `.(uservalue 1)`, `.(local name @
file.lua:12)` and `.(frame 1 file.lua:12)` for the other references. A path
through `thread` means that only a call frame on the stack holds the object;
it can be a temporary that a later call overwrites.

### The Lua Memory window

**XLua > Lua Memory** in the Editor takes snapshots of the running LuaEnv in
Play Mode. Take snapshot A, do what should not leak (open and close a UI,
load and unload a scene), take snapshot B, and read the diff. The window can
filter the report, copy it and save it to a file, and lists the Unity
objects Lua started holding as object fields that select them.

### From Lua

`xlua.memory` is a thin wrapper of the same API, embedded in the xLua
assembly like `xlua.util`:

```lua
local memory = require 'xlua.memory'
local before = memory.snapshot()      -- memory.snapshot(false) skips the full collection
-- ...
local diff = memory.diff(before, memory.snapshot())
print(memory.report(diff, 20))
```

It exists where `LuaMemoryDiagnostics` does. A release player that sets
`XLUA_MEMORY_DIAGNOSTICS` together with both `XLUA_NO_UTIL` and
`XLUA_NO_PROFILER` has no embedded module searcher, so there only the C# API
works.

### Cost and limits

None of this is compiled into a release player without
`XLUA_MEMORY_DIAGNOSTICS`, so it adds no managed code there.

The native walk is a build option of the plugin, `XLUA_MEMORY_SNAPSHOT`
([build/README.md](../../../build/README.md#memory-snapshot)). It is on for
the macOS, Windows and Linux plugins, which the Editor loads, and for the
static iOS library, whose players drop it when nothing calls it. It is off
for Android, OpenHarmony, UWP and Switch, so their release players do not
carry it (about 7.5 KB of a stripped Android arm64 `libxlua.so`). The WebGL
build and the Android source plugin compile it and strip it in release
players. To take snapshots in a development build on Android, build the
plugin with the option on:

```sh
XLUA_MEMORY_SNAPSHOT=ON build/make_android_lua55.sh
```

With a plugin built without it, `TakeMemorySnapshot` throws a
`NotSupportedException` that says so.

A snapshot holds a copy of the object graph: about 40 bytes per object and
32 per reference in native memory during the walk, and a C# object per Lua
object afterwards. Strings are counted but not listed. LuaJIT FFI cdata is
not followed. Values in tables with weak keys are treated as strong.

`General/LuaMemoryLeakChecker` and the internal-header leak checker it
drives (`xlua_report_table_size` in `build/memory_leak_checker.c`, Lua 5.3
to 5.5 only) remain for command-line use outside Unity.
