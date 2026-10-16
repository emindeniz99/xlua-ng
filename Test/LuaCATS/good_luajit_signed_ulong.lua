-- Correct use of C# ulong on LuaJIT without UINT_ESPECIALLY, where ulong is the
-- same xlua.Int64 userdata as long; luacats_check.py --luajit expects no
-- diagnostics.

local c = CS.LuaCATSSample.Circle()
---@type xlua.Int64
local id = c.Id

c.Mask = c.Mask + id
c.Mask = c.Id

-- uint64 helpers take numbers or xlua.Int64; parse, divide and remainder
-- return xlua.Int64.
---@type xlua.Int64
local big = uint64.parse('18446744073709551615')
---@type xlua.Int64
local half = uint64.divide(big, 2) + uint64.remainder(big, id)
local off = c:Offset(big, { big })
---@type xlua.Int64
local low = uint64.band(c.Mask, 0xFF)
