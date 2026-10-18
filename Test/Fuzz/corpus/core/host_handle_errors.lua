$local seen = 0
local function h(m) seen = seen + 1 error(m) end
local ok, s = xpcall(hset, h, nil)
local ok2, s2 = xpcall(hset, function() while true do end end, 0/0)
local ok3, s3 = xpcall(hset, h, "k")
return seen, s, s2, s3
