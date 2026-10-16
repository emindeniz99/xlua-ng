-- Correct use of C# long and ulong on LuaJIT, where xLua passes them as the
-- int64 userdata xlua.Int64; luacats_check.py --luajit expects no diagnostics.
-- Everything here also holds with UINT_ESPECIALLY; good_luajit_signed_ulong.lua
-- has what holds only without it.

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
c.Ticks = nil
c.Ticks = 10
local ticks = c.Ticks or 0
local order = uint64.compare(id, 1) < 0

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

-- `..` uses the tostring text; uint64 adds bitwise, shift and floor division
-- helpers that take numbers or xlua.Int64 and return xlua.Int64.
---@type string
local label = 'id ' .. id .. '!'
---@type xlua.Int64
local masked = uint64.band(id, 0xFF)
---@type xlua.Int64
local bits = uint64.bor(uint64.lshift(1, 40), uint64.rshift(id, 3))
---@type xlua.Int64
local flipped = uint64.bxor(uint64.bnot(id), uint64.arshift(id, 63))
---@type xlua.Int64
local q = uint64.idiv(id, -7)
local back = c:Offset(uint64.band(id, 1), {})
