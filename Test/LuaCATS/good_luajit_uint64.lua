-- Correct use of C# ulong on LuaJIT built with UINT_ESPECIALLY, where xLua
-- passes ulong as the unsigned userdata xlua.UInt64, separate from xlua.Int64;
-- luacats_check.py --uint-especially expects no diagnostics.

local c = CS.LuaCATSSample.Circle()
---@type xlua.Int64
local id = c.Id

-- Values are xlua.UInt64; arithmetic takes numbers on either side.
---@type xlua.UInt64
local mask = uint64.parse('7')
---@type xlua.UInt64
local grown = (mask + 1) * 2 - c.Mask
---@type xlua.UInt64
local mixed = 2 * mask / 3 % 5 ^ 2
-- Unary minus returns the signed kind.
---@type xlua.Int64
local neg = -mask
local text = tostring(mask) .. uint64.tostring(mask)

-- ulong parameters, fields and array elements take numbers or xlua.UInt64.
c.Mask = 7
c.Mask = mask + 1
local off = c:Offset(id, { mask, 3 })

-- uint64 helpers take a number or either userdata and read its bits; parse,
-- divide and remainder return xlua.UInt64.
---@type xlua.UInt64
local big = uint64.parse('18446744073709551615')
---@type xlua.UInt64
local half = uint64.divide(big, 2) + uint64.remainder(id, 3)
local order = uint64.compare(big, id) < 0
local bits = uint64.tostring(id)
c.Mask = uint64.divide(id, 1)

-- A Lua function for a delegate gets xlua.UInt64 and may return a number.
local ledger = CS.LuaCATSSample.Ledger()
ledger.Tick = function(now, m)
    ---@type xlua.UInt64
    local before = m
    return now, before + 1
end
CS.LuaCATSSample.Ledger().Tick = function(now, m) return 1, 2 end
