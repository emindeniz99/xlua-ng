-- Misuse of the generated definitions. Each line marked `-- expect: <code>`
-- must get exactly that lua-language-server diagnostic, and no other line any.

local Circle = CS.LuaCATSSample.Circle
local Color = CS.LuaCATSSample.Color
local c = Circle(1)

c:Scale('twice') -- expect: param-type-mismatch
local r = c.Radiuss -- expect: undefined-field
c:TryGrow() -- expect: missing-parameter
local u = Circle.Unit(1) -- expect: redundant-parameter
c:Changed('*', function() end) -- expect: param-type-mismatch
local purple = Color.Purple -- expect: undefined-field
local s = c:Sum('values', 'one') -- expect: param-type-mismatch
local size = c.Samples.Count -- expect: undefined-field
c.Tint = 2 -- expect: assign-type-mismatch
local p = CS.LuaCATSSample.Point('a', 'b') -- expect: param-type-mismatch
local big = uint64.parse(42) -- expect: param-type-mismatch
c.Callback = function(a, b, c) end -- expect: redundant-parameter
local e = CS.LuaTestObj.TestEnumFunc(1) -- expect: param-type-mismatch

-- LuaLS only checks argument types against an overload it can pick by
-- argument count, so these mistakes are not reported:
c.Values:CopyTo({ 0 }, 1, 2) -- no overload takes three arguments
local oops = CS.LuaTestObj.OverLoad2(1, 'b') -- two overloads take two arguments
