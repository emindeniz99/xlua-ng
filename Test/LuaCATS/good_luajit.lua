-- Correct use of C# long and ulong on LuaJIT, where xLua passes them as the
-- int64 userdata xlua.Int64; luacats_check.py --luajit expects no diagnostics.

local Circle = CS.LuaCATSSample.Circle
local c = Circle()

-- Reads are xlua.Int64; arithmetic takes numbers on either side.
---@type xlua.Int64
local id = c.Id
---@type xlua.Int64
local sum = (id + 1) * 2 - c.Id
---@type xlua.Int64
local mixed = 2 * id / 3 % 5 ^ 2
local neg = -id
local text = tostring(id) .. uint64.tostring(c.Mask)

-- Parameters, fields and properties take numbers or xlua.Int64.
---@type xlua.Int64
local off = c:Offset(5, { 1, 2 })
off = c:Offset(id, { c.Mask, 3 })
c.Mask = 7
c.Mask = c.Mask + id
c.Ticks = nil
c.Ticks = 10
local ticks = c.Ticks or 0

-- Values Lua supplies take numbers: indexer keys, operator operands and what a
-- Lua function returns for a delegate. The function's parameters are userdata.
local ledger = CS.LuaCATSSample.Ledger()
ledger[5] = 'five'
ledger[id] = ledger[5]
local scaled = ledger * 2 * id
ledger.Tick = function(now, mask)
    ---@type xlua.Int64
    local start = now
    return 0, mask
end
CS.LuaCATSSample.Ledger().Tick = function(now, mask) return now + 1, 7 end

-- uint64 helpers take numbers or xlua.Int64; parse, divide and remainder
-- return xlua.Int64.
---@type xlua.Int64
local big = uint64.parse('18446744073709551615')
---@type xlua.Int64
local half = uint64.divide(big, 2) + uint64.remainder(big, id)
local order = uint64.compare(big, 1) < 0
