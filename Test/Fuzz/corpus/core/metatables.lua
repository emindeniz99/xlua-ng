local t = setmetatable({}, {__index = function(_, k) return k end, __close = function() end})
pcall(setmetatable, {}, {__gc = print}) do local c <close> = t end return t.x
