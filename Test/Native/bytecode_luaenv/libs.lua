-- LuaOptions.Libraries without io and os, on a bytecode-only library: LuaEnv
-- removes them with its precompiled remove_libraries chunk.
assert(io == nil and os == nil and package.loaded.io == nil and package.loaded.os == nil)
assert(string.format('%d', 3) == '3' and math.max(1, 2) == 2 and coroutine and table)
assert(CS.System.Math.Abs(-2) == 2)
return 'ok'
