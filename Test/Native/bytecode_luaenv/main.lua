-- LuaEnv on a bytecode-only library (Test/Native/BytecodeOnlyLuaEnv.cs). Precompiled
-- by Test/Native/bytecode_luaenv.py; TEST holds what the C# side was compiled with.

-- The CS.* bridge: init_xlua's namespace tables, a type, its members and constructor.
assert(CS.System.Math.Max(3, 4) == 4)
assert(type(CS.System) == 'table' and rawget(CS, 'System') == CS.System, 'namespace tables are cached')
local Target = CS.BytecodeOnlyTarget
assert(Target.Twice(21) == 42)
local target = Target()
assert(target.Value == 7 and target:Add(1, 2) == 10)
assert(TEST.target:Add(0, 0) == 7)
assert(typeof(Target) == typeof(CS.BytecodeOnlyTarget) and typeof(Target).Name == 'BytecodeOnlyTarget')
local ok, err = pcall(function() return CS.System.NoSuchTypeHere.Foo end)
assert(ok, tostring(err))
ok, err = pcall(function() CS.System.NoSuchTypeHere.Bar = 1 end) -- Foo is cached now
assert(not ok and err:find('No such type: System.NoSuchTypeHere', 1, true), tostring(err))

-- Generic types through the namespace's __call, and pairs over an IEnumerable (enumerable_pairs).
local List = CS.System.Collections.Generic.List(CS.System.Int32)
local list = List()
list:Add(1)
list:Add(2)
local sum = 0
for i, v in pairs(list) do sum = sum + v + i end
assert(sum == 1 + 2 + 0 + 1, sum)
sum = 0
for _, v in pairs(target:Numbers()) do sum = sum + v end
assert(sum == 11, sum)
local named = {}
for k, v in pairs(target:Named()) do named[k] = v end
assert(named.a == 1 and named.b == 2)

-- init_xlua's arguments: LOCKED, LOCK_XLUA and HOTFIX follow the C# defines.
local mt = getmetatable(CS)
if TEST.locked then
    assert(mt == 'locked', tostring(mt))
else
    assert(type(mt) == 'table', tostring(mt))
end
if TEST.lock_xlua then
    ok, err = pcall(function() xlua.test_field = 1 end)
    assert(not ok and err:find('xlua is read-only', 1, true), tostring(err))
else
    xlua.test_field = 1
    xlua.test_field = nil
end
assert((xlua.hotfix ~= nil) == TEST.hotfix, 'xlua.hotfix: ' .. tostring(xlua.hotfix))
assert(type(xlua.setclass) == 'function' and type(base) == 'function' and cast == xlua.cast)

-- setfenv and getfenv, which init_xlua defines on Lua 5.2 and later.
local function f() return y end
setfenv(f, {y = 'env'})
assert(f() == 'env' and getfenv(f).y == 'env')

-- require: a precompiled module from a custom loader, and the embedded modules.
assert(require('helper').value == 'helper')
-- A module the library was built without (CMake XLUA_BYTECODE_UTIL ...) fails with a message.
local function absent(name)
    local ok, err = pcall(require, name)
    assert(not ok and err:find("has no precompiled chunk '" .. name .. "'", 1, true), tostring(err))
end
if TEST.util then
    local util = require('xlua.util')
    local sync = util.async_to_sync(function(a, cb) cb(a + 1) end)
    assert(coroutine.wrap(function() return sync(41) end)() == 42)
    assert(debug.getinfo(util.async_to_sync, 'S').source == '@xlua/util.lua')
else
    absent('xlua.util')
end
if TEST.profiler then
    local profiler = require('perf.profiler')
    profiler.start()
    for i = 1, 100 do target:Add(i, i) end
    local report = profiler.report()
    profiler.stop()
    assert(type(report) == 'string' and #report > 0, tostring(report))
else
    absent('perf.profiler')
end
if TEST.memory then
    local memory = require('xlua.memory')
    local snapshot = memory.snapshot()
    assert(snapshot ~= nil and type(memory.report(snapshot, 3)) == 'string')
else
    absent('xlua.memory')
end

-- For the C# side: a Lua function as a delegate.
function lua_twice(x) return x * 2 end
return 'ok'
