-- Misuse of the Unity and XluaEditorTest definitions. Each line marked
-- `-- expect: <code>` must get exactly that lua-language-server diagnostic,
-- and no other line any.

local V = CS.UnityEngine.Vector3
local T, Mood = CS.XluaEditorTest.Target, CS.XluaEditorTest.Mood
local v = V(1, 2, 3)
local t = T(5)

local w = v.w -- expect: undefined-field
local c = V.Cross(v) -- expect: missing-parameter
local d = V.Dot(v, 'up') -- expect: param-type-mismatch
local go = CS.UnityEngine.GameObject(42) -- expect: param-type-mismatch
local n = go.transform.childCount .. go.transform.Parent -- expect: undefined-field
t:Describe(true) -- expect: param-type-mismatch
local q = T.Div(17) -- expect: missing-parameter
t:Next(2) -- expect: param-type-mismatch
local sad = Mood.Sad -- expect: undefined-field
t.Callback = function(x, s, extra) return x end -- expect: redundant-parameter
