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

Upstream xLua shipped a `perf.memory` module whose `total` and `snapshot` functions only raised "use memory leak checker instead!". xlua-ng removed it. `collectgarbage('count')` returns the Lua heap size in kilobytes; use it to detect sustained growth. To find the tables that grow, `General/LuaMemoryLeakChecker` drives the native leak checker (`build/memory_leak_checker.c`) outside Unity and reports the tables that grew between two snapshots. A memory snapshot tool for development builds is planned; see the [backlog](../../../docs/design/backlog.md).
