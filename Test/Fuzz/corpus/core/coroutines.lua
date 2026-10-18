	local co = coroutine.wrap(function(...) for i = 1, 10 do coroutine.yield(i) end end)
for i = 1, 5 do co() end local c = coroutine.create(function() coroutine.yield() end)
coroutine.resume(c) return coroutine.close(c), collectgarbage('count')
