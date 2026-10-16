-- Strict CS mode (luacats_strict): configured types and their namespaces
-- resolve; luacats_check.py expects no diagnostics.

local Circle = CS.LuaCATSSample.Circle
local c = Circle(2.5, CS.LuaCATSSample.Color.Green)
local style = CS.LuaCATSSample.Circle.Style()
style.Width = c.Radius
local p = CS.LuaCATSSample.Point(3, 4)
local text = CS.System.Object.ReferenceEquals(c, p)
local obj = CS.LuaTestObj()
local e = CS.LuaTestObj.TestEnumFunc(CS.LuaTestType.DEF)
