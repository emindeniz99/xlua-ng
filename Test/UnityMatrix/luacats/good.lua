-- Correct use of the LuaCATS definitions that XluaEditorToolsTests writes for
-- XluaEditorTest.Config; editor_tests.py expects no lua-language-server
-- diagnostics at Warning level or above. The code mirrors XluaGeneratedCodeTests.

local V = CS.UnityEngine.Vector3
local a, b = V(1, 2, 3), V(4, 5, 6)
local sum, cross, scaled = a + b, V.Cross(a, b), a * 2
local dot = V.Dot(a, b) + V.Distance(a, b) + a.magnitude + V.up.y
a.x = 10
---@type CS.UnityEngine.Vector3
local flat = -sum + cross - scaled / 2

local GameObject = CS.UnityEngine.GameObject
local root = GameObject('root')
local child = GameObject('child')
child.transform:SetParent(root.transform, false)
root.transform.position = V(1, 2, 3)
local found = root.transform:Find('child')
local count = root.transform.childCount + #root.name
CS.UnityEngine.Object.DestroyImmediate(root)

local T, Mood = CS.XluaEditorTest.Target, CS.XluaEditorTest.Mood
local t = T(5)
local text = t:Describe(7) .. t:Describe('x') .. t:Describe(1, 2)
local q, rem = T.Div(17, 5)
---@type integer, integer
local q2, rem2 = q, rem
local doubled = t:Twice(21) + t.Value + T.Calls
local pair = t:MakePair(2, 0.5)
pair.A = pair.A + 1
local total = pair:Sum() + pair.B
---@type CS.XluaEditorTest.Mood
local mood = t:Next(Mood.Busy)
local lost = Mood.__CastFrom('Lost')
local applied = t:Apply(function(x, s) return x + #s end, 3, 'abcd')
t.Callback = function(x, s) return x * 10 + #s end
