Hfunction f(co, t, w) local ok, v = coroutine.resume(co, 5) return co, t, coroutine.running() end
local co = coroutine.create(function(a) for i = 1, 3 do a = coroutine.yield(i, {i}) end return 'end' end)
return co, {co}, coroutine.wrap(function() end), coroutine.create(function() error({}) end)
