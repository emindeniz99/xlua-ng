-- Misuse of C# ulong on LuaJIT built with UINT_ESPECIALLY. Each line marked
-- `-- expect: <code>` must get exactly that lua-language-server diagnostic, and
-- no other line any. xLua's argument checks reject the other signed kind
-- (overload resolution fails); where it converts without a check, as for a
-- field, a single overload or an array table, it reinterprets the bits.

local c = CS.LuaCATSSample.Circle()

c.Mask = c.Id -- expect: assign-type-mismatch
local off = c:Offset(c.Mask, {}) -- expect: param-type-mismatch
---@type xlua.Int64
local big = uint64.parse('1') -- expect: assign-type-mismatch
local masks = c:Offset(1, { c.Id }) -- expect: assign-type-mismatch
---@type xlua.UInt64
local neg = -uint64.parse('1') -- expect: assign-type-mismatch
---@type xlua.Int64
local low = uint64.band(c.Mask, 0xFF) -- expect: assign-type-mismatch
---@type xlua.UInt64
local shifted = uint64.lshift(c.Id, c.Mask) -- expect: assign-type-mismatch

-- LuaCATS cannot express these run-time errors and surprises, so they are not
-- reported:
local sum = c.Mask + c.Id -- raises "type not match": Int64 and UInt64 do not mix
local same = c.Mask == c.Id -- raises "type not match" too
local both = uint64.band(c.Mask, c.Id) -- raises "type not match" as well
local huge = c.Mask + 2 ^ 63 -- the number saturates to 2^63 - 1 first
