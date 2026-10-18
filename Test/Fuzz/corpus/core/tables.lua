@local t = {10, 20, x = {y = 1}, s = 'str'}
function f(a, b, c) a[#a + 1] = b return a, c end
error_kind = 1
return t, function() return t end, setmetatable({}, {__index = t})
