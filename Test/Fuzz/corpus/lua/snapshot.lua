-- The memory snapshot walk from a coroutine, pcall and a finalizer, over cycles, weak tables and structs.
local t = {self = nil, s = newstruct(8), list = {}}
t.self = t
for i = 1, 20 do t.list[i] = {i, tostring(i)} end
local weak = setmetatable({{}, t}, {__mode = "kv"})
local co = coroutine.create(function(a, ...) local inner = {a, ...}; coroutine.yield(snapshot()); return inner end)
assert(coroutine.resume(co, t, 1, 2))
assert(pcall(function() return snapshot(5) end))
setmetatable({}, {__gc = function(o) snapshot() end})
collectgarbage()
return snapshot(), weak
