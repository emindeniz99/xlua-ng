-- Misuse of C# long and ulong on LuaJIT. Each line marked `-- expect: <code>`
-- must get exactly that lua-language-server diagnostic, and no other line any.

local Circle = CS.LuaCATSSample.Circle
local c = Circle()

local floor = math.floor(c.Id) -- expect: param-type-mismatch
---@type number
local n = c.Id -- expect: assign-type-mismatch
local off = c:Offset('1', {}) -- expect: param-type-mismatch
c.Mask = '7' -- expect: assign-type-mismatch
local masks = c:Offset(1, { 'a' }) -- expect: assign-type-mismatch
CS.LuaCATSSample.Ledger().Tick = function(now, mask) return 'late', mask end -- expect: return-type-mismatch
---@type integer
local text = 'id ' .. c.Id -- expect: assign-type-mismatch
---@type number
local q = uint64.idiv(c.Id, 2) -- expect: assign-type-mismatch

-- LuaCATS has no comparison operators, so these run-time errors and surprises
-- are not reported:
local same = c.Id == 0 -- always false: a userdata never equals a number
local less = c.Id < 1 -- raises: Lua 5.1 cannot compare a userdata with a number
