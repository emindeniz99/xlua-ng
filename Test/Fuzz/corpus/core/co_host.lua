(function f(...) local a = yield(1) return echo(a, ...), coroutine.isyieldable() end
return coroutine.create(function(x) local y = yield(x) return int(y) end), coroutine.create(echo)
