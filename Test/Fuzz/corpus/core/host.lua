local a, b = echo(1, 'two', 3.5, nil, true)
local ok, e = pcall(fail)
local t = {host.many()}
local co = coroutine.wrap(function(x) local n = yield(x, 2) return n + int(5) end)
co(1)
local r = co(3)
pcall(int, 'x')
for i = 1, 100 do echo(i) end
function f(...) return echo(...) end
return a, b, #t, r
