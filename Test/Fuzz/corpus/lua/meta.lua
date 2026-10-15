local mt = {__index = function(t, k) return k end, __gc = function() end}
local t = setmetatable({}, mt)
local co = coroutine.wrap(function(...) coroutine.yield(t.x) return select("#", ...) end)
co(1, 2) co()
collectgarbage()
return string.format("%d %s %q", 1, "a", "b"), table.concat({1, 2}, ",")
