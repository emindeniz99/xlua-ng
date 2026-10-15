-- Correct use of the generated definitions; luacats_check.py expects no
-- lua-language-server diagnostics at Warning level or above.

local Circle = CS.LuaCATSSample.Circle
local Color = CS.LuaCATSSample.Color

-- Constructors, static members, enums.
local c = Circle(2.5, Color.Green)
local plain = Circle()
local unit = Circle.Unit()
Circle.Count = Circle.Count + 1
local tint = Color.__CastFrom('Blue')
---@type CS.LuaCATSSample.Color
local green = c.Tint

-- Fields, properties, overloads and inherited members.
local area = c:Area() + unit.Radius * Circle.Pi
c:Scale(2)
c:Scale(0.5)
c.Name = 'disc'
local text = c:ToString() .. c.Name
local same = c:Equals(plain)
local t = c:GetType()

-- out and ref parameters come back as extra results.
local ok, radius, steps = c:TryGrow(1.5, 3)
---@type boolean, number, integer
local ok2, radius2, steps2 = ok, radius, steps

-- params arrays, operators, indexers, nullable, arrays, strings for byte[].
local total = c:Sum('values', 1, 2, 3) + c:Sum('none')
local bigger = c + unit
local flipped = -c
local first = c[0]
local layer = c.Layer or 0
local samples = c.Samples
local n = samples.Length + samples[0]
c.Samples = { 1, 2, 3 }
local bytes = c.Payload:upper()

-- Events, delegates and Lua functions.
c:Changed('+', function(sender) print(sender.Radius) end)
Circle.Reset('-', function() end)
c.Callback = function(value) print(value + 1) end
c.Score = function(name) return #name, true end
local scored, found = c.Score('x')

-- Extension methods, constrained generic methods, nested types, structs.
local perimeter = c:Perimeter()
local shape = c:Find(c)
local style = CS.LuaCATSSample.Circle.Style()
style.Width = 1.5
local p = CS.LuaCATSSample.Point(3, 4)
local origin = CS.LuaCATSSample.Point()
local len = p:Length() + origin.X

-- Constructed generic types and table conversion.
c.Values:Add(4)
c.Values:AddRange({ 5, 6 })
local count = c.Values.Count + c.Values[0]
c.Values:CopyTo({ 0 })
c.Values:CopyTo({ 0 }, 1)
c.Values:CopyTo(0, { 0 }, 1, 1)

-- xlua helpers and globals.
xlua.private_accessible(Circle)
local typ = typeof(Circle)
xlua.hotfix(Circle, 'Area', function(self) return 0 end)
xlua.hotfix(Circle, { Area = false })
local util = require 'xlua.util'
local bound = util.bind(function(obj) return obj end, c)
local hex = uint64.tostring(-1)

-- Unit-test assembly: global namespace types.
local LuaTestObj = CS.LuaTestObj
local obj = LuaTestObj()
obj.testVar = obj:Sum(1, 2, 3) + LuaTestObj.Sum(1, 2)
local y, z = LuaTestObj.OutRefFunc1(1, 2)
local o1 = LuaTestObj.OverLoad1(1) + LuaTestObj.OverLoad1(1, 2) + LuaTestObj.OverLoad1(1, 2, 3)
local o2 = LuaTestObj.OverLoad2(1, 2.5) + LuaTestObj.OverLoad2('a', 'b')
local v = LuaTestObj.VariableParamFunc(1, 'a', 'b') + LuaTestObj.VariableParamFunc(1, 2, 3)
local e = LuaTestObj.TestEnumFunc(CS.LuaTestType.DEF)
local sum = (obj + obj + 1 - obj) * 2
obj:TestEvent1('+', function(value) return 1 end)

-- Types outside the configuration are still reachable through reflection.
CS.System.Console.WriteLine('typed any')
