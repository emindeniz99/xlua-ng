Yfunction f(...) local x <close> = setmetatable({}, {__close = function() end}) coroutine.yield(...) error('after') end
return coroutine.create(function() local t <close> = setmetatable({}, {__close = function() error('c') end}) coroutine.yield(1) while true do end end), 2
