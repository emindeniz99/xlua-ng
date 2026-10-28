require("luaunit_compat")

gTestNumber = 1

function islua53() return not not math.type end
	
-- for test case
CMyTestCaseLuaCallCS = TestCase:new()
function CMyTestCaseLuaCallCS:new(oo)
    local o = oo or {}
    o.count = 1
    
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestCaseLuaCallCS.SetUpTestCase(self)
    self.count = 1 + self.count
	print("CMyTestCaseLuaCallCS.SetUpTestCase")
end

function CMyTestCaseLuaCallCS.TearDownTestCase(self)
    self.count = 1 + self.count
	print("CMyTestCaseLuaCallCS.TearDownTestCase")
end


function CMyTestCaseLuaCallCS.SetUp(self)
    self.count = 1 + self.count
	print("CMyTestCaseLuaCallCS.SetUp")
end

function CMyTestCaseLuaCallCS.TearDown(self)
    self.count = 1 + self.count

	print("CMyTestCaseLuaCallCS.TearDown")
end


function CMyTestCaseLuaCallCS.CaseDefaultParamFunc1(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.DefaultParaFuncSingle(100, "abc")
	ASSERT_EQ(ret, 100)
end

function CMyTestCaseLuaCallCS.CaseDefaultParamFunc2(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.DefaultParaFuncSingle(100)
	ASSERT_EQ(ret, 100)
end

function CMyTestCaseLuaCallCS.CaseDefaultParamFunc3(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.DefaultParaFuncMulti(100, "")
	ASSERT_EQ(ret, 101)
end

function CMyTestCaseLuaCallCS.CaseDefaultParamFunc4(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.DefaultParaFuncMulti(100, "efg", 1)
	ASSERT_EQ(ret, 101)
end

function CMyTestCaseLuaCallCS.CaseDefaultParamFunc5(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.DefaultParaFuncMulti(100, "efg", 1, 98)
	ASSERT_EQ(ret, 101)
end

function CMyTestCaseLuaCallCS.CaseDefaultParamFunc6(self)
    self.count = 1 + self.count
	--if (CS.LuaTestCommon.IsMacPlatform() == false) then
    if (true) then
		local ret, error = pcall(function() CS.LuaTestObj.DefaultParaFuncMulti(100, "efg", 1, 98, 0) end)
		ASSERT_EQ(ret, false)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseVariableParamFunc1(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.VariableParamFunc(0)
	ASSERT_EQ(ret, 0)
end

function CMyTestCaseLuaCallCS.CaseVariableParamFunc2(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.VariableParamFunc(0, "a")
	ASSERT_EQ(ret, 0)
end

function CMyTestCaseLuaCallCS.CaseVariableParamFunc3(self)
    self.count = 1 + self.count
	--if (CS.LuaTestCommon.IsMacPlatform() == false) then
    if (true) then
		local ret, error = pcall(function() CS.LuaTestObj.VariableParamFunc(0, "a", 1, "b") end)
		ASSERT_EQ(ret, true)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseLuaAccessEnum1(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestEnumFunc(CS.LuaTestType.DEF)
	ASSERT_EQ(ret, 1)
end

--[[ 2016.11.25 Integers can no longer be used directly as enum values.
function CMyTestCaseLuaCallCS.CaseLuaAccessEnum2(self)
    self.count = 1 + self.count
	local enumValue = -1
	local ret = CS.LuaTestObj.TestEnumFunc(enumValue)
	ASSERT_EQ(ret, -1)
end
]]

function CMyTestCaseLuaCallCS.CaseLuaAccessEnum3(self)
    self.count = 1 + self.count
	local enumValue = CS.LuaTestType.__CastFrom(2)
	local ret = CS.LuaTestObj.TestEnumFunc(enumValue)
	ASSERT_EQ(ret, 2)
end

function CMyTestCaseLuaCallCS.CaseLuaAccessEnum4(self)
    self.count = 1 + self.count
	local enumValue = CS.LuaTestType.__CastFrom(4)
	local ret = CS.LuaTestObj.TestEnumFunc(enumValue)
	ASSERT_EQ(ret, 4)
end

function CMyTestCaseLuaCallCS.CaseLuaAccessEnum5(self)
    self.count = 1 + self.count
	local enumValue = CS.LuaTestType.__CastFrom("GHI")
	local ret = CS.LuaTestObj.TestEnumFunc(enumValue)
	ASSERT_EQ(ret, 2)
end

function CMyTestCaseLuaCallCS.CaseLuaAccessEnum6(self)
    self.count = 1 + self.count
	--if (CS.LuaTestCommon.IsMacPlatform() == false and CS.LuaTestCommon.IsIOSPlatform() == false) then
    if (true) then
		local ret, error = pcall(function() CS.LuaTestObj.TestEnumFunc(CS.LuaTestType.__CastFrom("BCD")) end)
		ASSERT_EQ(ret, false)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseGetType1(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.LuaTestObj))
	ASSERT_EQ(ret, "LuaTestObj")
end

function CMyTestCaseLuaCallCS.CaseGetType2(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.System.String))
	ASSERT_EQ(ret, "System.String")
end

function CMyTestCaseLuaCallCS.CaseGetType3(self)
    self.count = 1 + self.count
    if CS.LuaTestCommon.IsXLuaGeneral() then return end
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.UnityEngine.Vector3))
	ASSERT_EQ(ret, "UnityEngine.Vector3")
end

function CMyTestCaseLuaCallCS.CaseGetType4(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.System.Int16))
	ASSERT_EQ(ret, "System.Int16")
end

function CMyTestCaseLuaCallCS.CaseGetType5(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.XLua.LuaTable))
	ASSERT_EQ(ret, "XLua.LuaTable")
end

function CMyTestCaseLuaCallCS.CaseGetType6(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.XLua.LuaFunction))
	ASSERT_EQ(ret, "XLua.LuaFunction")
end

--[[ The LuaUserData class was removed in v2.1.0.
function CMyTestCaseLuaCallCS.CaseGetType7(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.TestGetType(typeof(CS.XLua.LuaUserData))
	ASSERT_EQ(ret, "XLua.LuaUserData")
end
]]

function CMyTestCaseLuaCallCS.Case64BitInt1(self)
    self.count = 1 + self.count
	CS.LuaTestObj.Gen64BitInt()
	local x1 = CS.LuaTestObj.ulX1
	local x2 = CS.LuaTestObj.ulX2
	local ret = x1 + x2
	ASSERT_EQ(ULONG_TOSTRING("6917529027641081856"), tostring(ret))
	ret  = x2 - x1
	ASSERT_EQ(ULONG_TOSTRING("-2305843009213693952", "16140901064495857664"), tostring(ret))
	x1 = CS.LuaTestObj.lY1
	x2 = CS.LuaTestObj.lY2
	ret = x1 + x2
	ASSERT_EQ("6917529027641081856", tostring(ret))
	ret = x2 - x1
	ASSERT_EQ("-2305843009213693952", tostring(ret))
	x1 = CS.LuaTestObj.i64Z1
	x2 = CS.LuaTestObj.i64Z2
	ret = x1 + x2
	ASSERT_EQ("6917529027641081856", tostring(ret))
	ret = x2 - x1
	ASSERT_EQ("-2305843009213693952", tostring(ret))
end

function CMyTestCaseLuaCallCS.Case64BitInt2(self)
    self.count = 1 + self.count
	CS.LuaTestObj.Gen64BitInt()
	local x1 = CS.LuaTestObj.ulX1
	local x2 = CS.LuaTestObj.ulX2
	ASSERT_EQ(true, x2 < x1)
	ASSERT_EQ(true, x2 <= x1)
	ASSERT_EQ(false, x1 <= x2)
	ASSERT_EQ(false, x1 < x2)
end

function CMyTestCaseLuaCallCS.Case64BitInt3(self)
    self.count = 1 + self.count
	CS.LuaTestObj.Gen64BitInt()
	local y = CS.LuaTestObj.lY3
	y = y * 100
	ASSERT_EQ(tostring(y), "112589990684262400")
	y = y / 10000
	if islua53() then
	    ASSERT_EQ(true, math.abs(y - 11258999068426.24) < 0.01)
	else
		ASSERT_EQ(tostring(y), "11258999068426")
	end
	y = y % 1000
	if islua53() then
	    ASSERT_EQ(tostring(y), "426.240234375")
	else
		ASSERT_EQ(tostring(y), "426")
	end
end

function CMyTestCaseLuaCallCS.Case64BitInt4(self)
    self.count = 1 + self.count
	CS.LuaTestObj.Gen64BitInt()
	local ret = CS.LuaTestObj.lY3 * CS.LuaTestObj.lY4
	ASSERT_EQ(tostring(ret), "138485688541642752")
	ret = CS.LuaTestObj.lY3 / CS.LuaTestObj.lY5
	if islua53() then
		ASSERT_EQ(true, math.abs(ret - 91202908614.226) < 0.001)
	else
	    ASSERT_EQ(tostring(ret), "91202908614")
	end
	ret = CS.LuaTestObj.lY3 % CS.LuaTestObj.lY6
	ASSERT_EQ(tostring(ret), "52636")
end

function CMyTestCaseLuaCallCS.CaseCast1(self)
    self.count = 1 + self.count
	local castObj = CS.TestCastClass()
	cast(castObj, typeof(CS.TestCastClass))
	ASSERT_EQ(true, castObj:TestFunc1())
end

function CMyTestCaseLuaCallCS.CaseCast2(self)
	self.count = 1 + self.count
	local castObj = CS.LuaTestObj.CreateTestLuaObj()
	cast(castObj, typeof(CS.TestLuaClass))
	ASSERT_EQ(true, castObj:TestFunc1())
end

function LuaFunc1(x)
	return x + 1
end

function LuaFunc2(x)
	return x * 2
end	

function CMyTestCaseLuaCallCS.CaseDelgate1(self)
	self.count = 1 + self.count
	
	CS.LuaTestObj.initNumber = 5
	CS.LuaTestObj.GenDelegate()
	local luaDelgateLink = CS.LuaTestObj.csDelegate
	luaDelgateLink = CS.LuaTestObj.csDelegate1
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate2
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate3
	local ret = luaDelgateLink(1)
	ASSERT_EQ(5, ret)
	luaDelgateLink = luaDelgateLink + LuaFunc1
	ret = luaDelgateLink(1)
	ASSERT_EQ(2, ret)
	CS.LuaTestObj.csDelegate4 = LuaFunc2
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate4
	ret = luaDelgateLink(2)
	ASSERT_EQ(4, ret)
	luaDelgateLink = luaDelgateLink - LuaFunc1
	ret = luaDelgateLink(10)
	ASSERT_EQ(20 ,ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate4
	ret = luaDelgateLink(19)
	ASSERT_EQ(1900, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate3
	ret = luaDelgateLink(2)
	ASSERT_EQ(1900, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate2
	ret = luaDelgateLink(3)
	ASSERT_EQ(1903, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate2
	ret = luaDelgateLink(4)
	ASSERT_EQ(1907, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
    local ret, error = pcall(function() luaDelgateLink(5) end)
    ASSERT_EQ(false, ret)

	CS.LuaTestObj.initNumber = 5
	luaDelgateLink = CS.LuaTestObj.csDelegate3
	ret = luaDelgateLink(1)
	ASSERT_EQ(5, ret)
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(1)
	ASSERT_EQ(6, ret)
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(1)
	ASSERT_EQ(8, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(1)
	ASSERT_EQ(9, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(1)
	ASSERT_EQ(9, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate3
	CS.LuaTestObj.initNumber = 1
	local function LuaFunc4(x)
		CS.LuaTestObj.initNumber = CS.LuaTestObj.initNumber * 2
		return CS.LuaTestObj.initNumber
	end
	luaDelgateLink = CS.LuaTestObj.csDelegate1
	luaDelgateLink = luaDelgateLink + LuaFunc4
	ret = luaDelgateLink(1)
	ASSERT_EQ(ret, 4)
	luaDelgateLink = luaDelgateLink + LuaFunc4
	ret = luaDelgateLink(1)
	ASSERT_EQ(ret, 20)
	luaDelgateLink = luaDelgateLink - LuaFunc4
	ret = luaDelgateLink(1)
	ASSERT_EQ(ret, 42)
	luaDelgateLink = luaDelgateLink - LuaFunc4
	ret = luaDelgateLink(1)
	ASSERT_EQ(ret, 43)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
	
	CS.LuaTestObj.initNumber = 1907
	luaDelgateLink = LuaFunc1
	ret = luaDelgateLink(1)
	ASSERT_EQ(2, ret)	
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(2)
	ASSERT_EQ(1909, ret)
	luaDelgateLink = luaDelgateLink + CS.LuaTestObj.csDelegate4
	ret = luaDelgateLink(3)
	ASSERT_EQ(6, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(4)
	ASSERT_EQ(8, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate1
	ret = luaDelgateLink(5)
	ASSERT_EQ(10, ret)
	luaDelgateLink = luaDelgateLink - LuaFunc1
	ret = luaDelgateLink(6)
	ASSERT_EQ(12, ret)
	luaDelgateLink = luaDelgateLink - LuaFunc1
	ret = luaDelgateLink(7)
	ASSERT_EQ(14, ret)
	luaDelgateLink = luaDelgateLink - CS.LuaTestObj.csDelegate4
    local ret, error = pcall(function() luaDelgateLink(8) end)
    ASSERT_EQ(false, ret)
end

function EvtFunc11(y)
	gTestNumber = y + gTestNumber
	return gTestNumber
end

function EvtFunc12(y)
	gTestNumber = y + 1 + gTestNumber
	return gTestNumber
end

function CMyTestCaseLuaCallCS.CaseEvent1(self)
	self.count = 1 + self.count

	gTestNumber = 1
	local testObj = CS.LuaTestObj()
	testObj:TestEvent1('+', EvtFunc11)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(2, ret)
	testObj:TestEvent1('+', EvtFunc12)
	local ret = testObj:CallEvent(2)
	ASSERT_EQ(7, ret)
	testObj:TestEvent1('+', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(12, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(15, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(16, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(17, ret)
	testObj:TestEvent1('-', EvtFunc11)
end

function CMyTestCaseLuaCallCS.CaseCalc1(self)
	self.count = 1 + self.count
	local a = CS.LuaTestObj()
	a.testVar = 100
	local b = CS.LuaTestObj()
	b.testVar = 200
	local ret = a + b
	ASSERT_EQ(ret.testVar, 300)
	ret = a - b
	ASSERT_EQ(ret.testVar, -100)
	ret = a * b
	ASSERT_EQ(ret.testVar, 20000)
	ret = b / a
	ASSERT_EQ(ret.testVar, 2)
	ret = b % a
	ASSERT_EQ(ret.testVar, 0)
	ret = a < b
	ASSERT_EQ(true, ret)
	ret = a <= b
	ASSERT_EQ(true, ret)
	ret = -a
	ASSERT_EQ(ret.testVar, -100)
	local c = CS.LuaTestObj()
	c.testArr[0] = 1000
	ret = c[0]
	ASSERT_EQ(1000, ret)
	c[1] = 10000
	ret = c.testArr[1]
	ASSERT_EQ(10000, ret)
	
	a.testVar = 100
	b.testVar = 200
	c.testVar = 300
	local d = CS.LuaTestObj()
	d.testVar = 20
	local e = CS.LuaTestObj()
	e.testVar = 7
	ret = (a + b) * c / d % e
	ASSERT_EQ(ret.testVar, 6)
end

function CMyTestCaseLuaCallCS.CaseCalc2(self)
	self.count = 1 + self.count
	local a = CS.LuaTestObj()
	a.testVar = 100
	local ret = 300 + a + 200
	ASSERT_EQ(ret.testVar, 600)
	ret = 100 - a - 400
	ASSERT_EQ(ret.testVar, -400)
	ret = 2 * a * 3
	ASSERT_EQ(600, ret.testVar)
	ret = 20000 / a / 2
	ASSERT_EQ(100, ret.testVar)

	
end

function CMyTestCaseLuaCallCS.CaseOverLoad1(self)
	self.count = 1 + self.count
	local ret = CS.LuaTestObj.OverLoad1(1, 2)
	ASSERT_EQ(ret, 1)
	ret = CS.LuaTestObj.OverLoad2("1", "2")
	ASSERT_EQ(ret, 4);
	ret = CS.LuaTestObj.OverLoad3(1)
	ASSERT_EQ(ret, 5);
end

function CMyTestCaseLuaCallCS.CaseOutRef1(self)
	self.count = 1 + self.count
	local a = 2
	local b
	a, b = CS.LuaTestObj.OutRefFunc1(1, a, b)
	ASSERT_EQ(a, 100)
	b, a = CS.LuaTestObj.OutRefFunc2(b, 1, a)
	ASSERT_EQ(a, 200)
	a, b = CS.LuaTestObj.OutRefFunc3(1, a, b)
	ASSERT_EQ(a, 300)
	ret, a, b = CS.LuaTestObj.OutRefFunc4(1, a, b)
	ASSERT_EQ(a, 400)
	ASSERT_EQ(ret, 400)
	ret, b, a = CS.LuaTestObj.OutRefFunc5(b, 1, a)
	ASSERT_EQ(a, 500)
	ASSERT_EQ(ret, 500)
	ret = CS.LuaTestObj.OutRefFunc6(1, 2)
	ASSERT_EQ(ret, 600)
end

function CMyTestCaseLuaCallCS.CaseOutRef2(self)
	self.count = 1 + self.count
	local a = CS.LuaTestObj.CreateTestLuaObj()
	local b = CS.LuaTestObj.CreateTestLuaObj()
	local c = CS.LuaTestObj.CreateTestLuaObj()
	a, b = CS.LuaTestObj.OutRefFunc11(c, a, b)
	ASSERT_EQ(a.cmpTarget, 100)
	b, a = CS.LuaTestObj.OutRefFunc12(b, c, a)
	ASSERT_EQ(a.cmpTarget, 200)
	a, b = CS.LuaTestObj.OutRefFunc13(c, a, b)
	ASSERT_EQ(a.cmpTarget, 300)
	ret, a, b = CS.LuaTestObj.OutRefFunc14(c, a, b)
	ASSERT_EQ(a.cmpTarget, 400)
	ASSERT_EQ(ret, 400)
	ret, b, a = CS.LuaTestObj.OutRefFunc15(b, c, a)
	ASSERT_EQ(a.cmpTarget, 500)
	ASSERT_EQ(ret, 500)
	ret = CS.LuaTestObj.OutRefFunc16(a, b)
	ASSERT_EQ(ret, 600)
end

function CMyTestCaseLuaCallCS.CaseOutRef3(self)
	self.count = 1 + self.count
	local a = CS.LuaTestObj.csDelegate11
	local b = CS.LuaTestObj.csDelegate12
	local c = CS.LuaTestObj.csDelegate13
	CS.LuaTestObj.initNumber = 1
	
	a, b = CS.LuaTestObj.OutRefFunc21(c, a, b)
	ASSERT_EQ(a(1), 2)
	b, a = CS.LuaTestObj.OutRefFunc22(b, c, a)
	ASSERT_EQ(a(1), 3)
	a, b = CS.LuaTestObj.OutRefFunc23(c, a, b)
	ASSERT_EQ(a(1), 4)
	ret, a, b = CS.LuaTestObj.OutRefFunc24(c, a, b)
	ASSERT_EQ(a(1), 6)
	ASSERT_EQ(ret, 5)
	ret, b, a = CS.LuaTestObj.OutRefFunc25(b, c, a)
	ASSERT_EQ(a(1), 8)
	ASSERT_EQ(ret, 7)
	ret = CS.LuaTestObj.OutRefFunc26(a, b)
	ASSERT_EQ(ret, 600)
end

function CMyTestCaseLuaCallCS.CaseNewClass1And5(self)
	self.count = 1 + self.count
	local class = CS.NoContClass()
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)	
end

function CMyTestCaseLuaCallCS.CaseNewClass2(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OneParamContClass(2)
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)	
	ASSERT_EQ(class.key1, 2)
	ASSERT_EQ(class.key2, 1)
end

function CMyTestCaseLuaCallCS.CaseNewClass3(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.TwoParamsContClass(2, 3)
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)	
	ASSERT_EQ(class.key1, 2)
	ASSERT_EQ(class.key2, 3)
end

function CMyTestCaseLuaCallCS.CaseNewClass4(self)
	self.count = 1 + self.count
	local class1 = CS.testLuaCallCS.MultiContClass(2)
	local class2 = CS.testLuaCallCS.MultiContClass()
	local ret = class1:add(1,2)
	ASSERT_EQ(ret, 3)	
	ASSERT_EQ(class1.key1, 2)
	ASSERT_EQ(class1.key2, 1)
	ret = class2:add(2,2)
	ASSERT_EQ(ret, 4)	
	ASSERT_EQ(class2.key1, 1)
	ASSERT_EQ(class2.key2, 1)
end

function CMyTestCaseLuaCallCS.CaseNewClass6(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OverClassA()
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)
	ret = class:sub(9, 1)
	ASSERT_EQ(ret, 8)
	ASSERT_EQ(class.key1, 1)
	ASSERT_EQ(class.key2, 1)
	ASSERT_EQ(class.key3, 0)
end

function CMyTestCaseLuaCallCS.CaseNewClass7(self)
	self.count = 1 + self.count
	CS.testLuaCallCS.StaticTestClass.n = 0
	CS.testLuaCallCS.StaticTestClass:Add()
	ASSERT_EQ(CS.testLuaCallCS.StaticTestClass.n, 1)
end

function CMyTestCaseLuaCallCS.CaseVisitStaticMemFunc_1(self)
	self.count = 1 + self.count
	CS.testLuaCallCS.MultiContClass.d = 3
	local ret = CS.testLuaCallCS.MultiContClass.d
	ASSERT_EQ(ret, 3)
	ret = CS.testLuaCallCS.MultiContClass.dec(10, 1)
	ASSERT_EQ(ret, 9)
end

function CMyTestCaseLuaCallCS.CaseVisitStaticMemFunc_2(self)
	self.count = 1 + self.count
	CS.NoContClass.d = 4
	local ret = CS.NoContClass.d
	ASSERT_EQ(ret, 4)
	ret = CS.NoContClass.dec(10, 1)
	ASSERT_EQ(ret, 9)
end

function CMyTestCaseLuaCallCS.CaseVisitStaticMemFunc_3(self)
	self.count = 1 + self.count
	CS.testLuaCallCS.StaticTestClass.n = 2
	CS.testLuaCallCS.StaticTestClass:Add()
	ASSERT_EQ(CS.testLuaCallCS.StaticTestClass.n, 3)
end

function CMyTestCaseLuaCallCS.CaseVisitClassMemFunc_1(self)
	self.count = 1 + self.count
	local class = CS.NoContClass()
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)
	class.key3 = false;
	ASSERT_EQ(class.key3, false)
end

function CMyTestCaseLuaCallCS.CaseVisitClassMemFunc_2(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OneParamContClass(2)
	local ret = class:add(1,2)
	ASSERT_EQ(ret, 3)	
	class.key1 = 3
	class.key2 = 4
	ASSERT_EQ(class.key1, 3)
	ASSERT_EQ(class.key2, 4)
end

function CMyTestCaseLuaCallCS.CaseVisitFatherClassMemFunc_1_1(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OverClassC()
	CS.testLuaCallCS.OverClassC.bValue = true
	ASSERT_EQ(CS.testLuaCallCS.OverClassC.bValue, true)	
	CS.testLuaCallCS.OverClassC.Set(false)
	ASSERT_EQ(CS.testLuaCallCS.OverClassC.bValue, false)
	
	CS.testLuaCallCS.OverClassC.d = 3
	ASSERT_EQ(CS.testLuaCallCS.OverClassC.d, 3)	
	local ret = CS.testLuaCallCS.OverClassC.dec(10, 1)
	ASSERT_EQ(ret, 9)
	class.key4 = 5
	class.key1 = 1
	class.key3 = false
	ret = class:sub(10, 1)
	ASSERT_EQ(ret, 10)
	ASSERT_EQ(class.key4 , 5)
	ASSERT_EQ(class.key1 , 1)
	ASSERT_EQ(class.key3 , false)
	ASSERT_EQ(class:add(1,2), 3)
	--ASSERT_EQ(class:sum(1, 2, 3), 6)
end

function CMyTestCaseLuaCallCS.CaseVisitFatherClassMemFunc_1_2(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OverClassB()
	
	CS.testLuaCallCS.OverClassB.d = 3
	ASSERT_EQ(CS.testLuaCallCS.OverClassB.d, 3)	
	local ret = CS.testLuaCallCS.OverClassB.dec(10, 1)
	ASSERT_EQ(ret, 9)
	class.key1 = 2
	class.key2 = 3
	ret = class:add(10, 1)
	ASSERT_EQ(ret, 11)
	ASSERT_EQ(class.key1, 2)
	ASSERT_EQ(class.key2, 3)
	ret = class:sub(10, 1)
	ASSERT_EQ(ret, 9)
end

function CMyTestCaseLuaCallCS.CaseVisitFatherClassMemFunc_2(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.OverClassCDeriveNGA()
	--[[CS.testLuaCallCS.OverClassCDeriveNGA.bValue = true
	ASSERT_EQ(CS.testLuaCallCS.OverClassCDeriveNGA.bValue, true)	
	CS.testLuaCallCS.OverClassCDeriveNGA.Set(false)
	ASSERT_EQ(CS.testLuaCallCS.OverClassCDeriveNGA.bValue, false)]]
	
	CS.testLuaCallCS.OverClassCDeriveNGA.d = 3
	ASSERT_EQ(CS.testLuaCallCS.OverClassCDeriveNGA.d, 3)	
	local ret = CS.testLuaCallCS.OverClassCDeriveNGA.dec(10, 1)
	ASSERT_EQ(ret, 9)
	--class.key4 = 5
	class.key1 = 1
	class.key3 = false
	--ret = class:sub(10, 1)
	--ASSERT_EQ(ret, 9)
	--ASSERT_EQ(class.key4 , 5)
	ASSERT_EQ(class.key1 , 1)
	ASSERT_EQ(class.key3 , false)
	ASSERT_EQ(class:add(1,2), 3)
end

function CMyTestCaseLuaCallCS.CaseVisitFatherClassMemFunc_3(self)
	self.count = 1 + self.count
	local class = CS.testLuaCallCS.ChildCalss()

	local ret = class:add(10, 1)
	ASSERT_EQ(ret, 11)
	
	class.a = 3
	ASSERT_EQ(class:getA() , 3)
	class:setA(4)
	ASSERT_EQ(class:getA() , 4)
	
end

function CMyTestCaseLuaCallCS.CaseGetChineseString(self)
	self.count = 1 + self.count
	local class = CS.TestChineseString()

	local ret = class:GetShortChinString()
	ASSERT_EQ(ret, "中文字符串")
	
	ASSERT_EQ(class:GetLongChineString() , "为Unity3D增加Lua脚本编程的能力，进而提供代码逻辑增量更新的可能，支持lua的所有基本类型，哈哈哈哈")
	ASSERT_EQ(class:GetCombineString() , "中文字符串.* ? [ ] ^ $~`!@#$%^&()_-+=[];',““〈〉〖【℃ ＄ ¤№ ☆ ★■ⅷ②㈣12345abc")
	ASSERT_EQ(class:GetComplexString() , "繁體國陸")
	ASSERT_EQ(class:GetHuoxingString() , "吙煋呅僦媞這樣孒")
	
end

function CMyTestCaseLuaCallCS.CaseVisitUlong(self)
	self.count = 1 + self.count
	local class = CS.TestUlongAndLongType()

	local ulong_max = class.UlongMax
	ASSERT_EQ(tostring(ulong_max), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(tostring(class:UlongAdd()), ULONG_TOSTRING("-1000", "18446744073709550616"))
	local ulong_min = class.UlongMin
	ASSERT_EQ(tostring(ulong_min), ULONG_TOSTRING("0"))
	
	local ulong_mid = class.UlongMid
	ASSERT_EQ(tostring(ulong_mid), ULONG_TOSTRING("-9223372036854775808", "9223372036854775808"))
	class.UlongMax = 1844674407370955
	ulong_max = class.UlongMax
	ASSERT_EQ(tostring(ulong_max), ULONG_TOSTRING("1844674407370955"))
	
	class.UlongMin = 100
	ulong_min = class.UlongMin
	ASSERT_EQ(tostring(ulong_min), ULONG_TOSTRING("100"))
	
	local ulong_add = ulong_min + ulong_mid
	ASSERT_EQ(tostring(ulong_add), ULONG_TOSTRING("-9223372036854775708", "9223372036854775908"))
end

function CMyTestCaseLuaCallCS.CaseVisitlong(self)
	self.count = 1 + self.count
	local class = CS.TestUlongAndLongType()

	local long_max = class.LongMax
	ASSERT_EQ(tostring(long_max), "9223372036854775807")
	
	ASSERT_EQ(tostring(class:LongAdd()), "9223372036854774808")
	
	local long_min = class.LongMin
	ASSERT_EQ(tostring(long_min), "-9223372036854775808")
	
	local long_mid = class.LongMid
	ASSERT_EQ(tostring(long_mid), "4611686018427387904")
	
	class.LongMax = 461168601842738
	long_max = class.LongMax
	ASSERT_EQ(tostring(long_max), "461168601842738")
	
	class.LongMin = 100
	long_min = class.LongMin
	ASSERT_EQ(tostring(long_min), "100")
	
	local long_add = long_min + long_mid
	ASSERT_EQ(tostring(long_add), "4611686018427388004")
	
	class.UlongMin = 100
	ulong_min = class.UlongMin
	ASSERT_EQ(tostring(ulong_min), ULONG_TOSTRING("100"))
	
	--local ret,errMessage = pcall(ulong_min + long_min)
	--ASSERT_EQ(errMessage, "type not match, lhs is UInt64, rhs is Int64")
end

-- `..` on C# long and ulong values: native integers on Lua 5.3+, the int64
-- userdata's __concat on LuaJIT; both give the tostring text.
function CMyTestCaseLuaCallCS.CaseInt64Concat(self)
	self.count = 1 + self.count
	local class = CS.TestUlongAndLongType()
	ASSERT_EQ("max " .. class.LongMax, "max 9223372036854775807")
	ASSERT_EQ(class.LongMin .. "!", "-9223372036854775808!")
	ASSERT_EQ("[" .. class.LongMid .. "]" .. 1, "[4611686018427387904]1")
	ASSERT_EQ("u=" .. class.UlongMax, "u=" .. ULONG_TOSTRING("-1", "18446744073709551615"))
end

-- The uint64 bitwise, shift and floor division helpers exist on LuaJIT only;
-- Lua 5.3+ has the operators.
function CMyTestCaseLuaCallCS.CaseInt64HelpersLuaJIT(self)
	self.count = 1 + self.count
	if jit == nil then
		if not UINT64_BUILT() then return end
		ASSERT_EQ(uint64.band, nil)
		ASSERT_EQ(uint64.idiv, nil)
		return
	end
	local class = CS.TestUlongAndLongType()
	local max, min = class.LongMax, class.LongMin
	ASSERT_EQ(tostring(uint64.band(max, 0xFF)), "255")
	ASSERT_EQ(tostring(uint64.bor(min, 1)), "-9223372036854775807")
	ASSERT_EQ(tostring(uint64.bxor(max, min)), "-1")
	ASSERT_EQ(tostring(uint64.bnot(max)), "-9223372036854775808")
	ASSERT_EQ(tostring(uint64.lshift(1, 63)), "-9223372036854775808")
	ASSERT_EQ(tostring(uint64.lshift(1, 64)), "0")
	ASSERT_EQ(tostring(uint64.rshift(min, 63)), "1")
	ASSERT_EQ(tostring(uint64.rshift(min, -1)), "0")
	ASSERT_EQ(tostring(uint64.arshift(min, 62)), "-2")
	ASSERT_EQ(tostring(uint64.idiv(min, -1)), "-9223372036854775808")
	ASSERT_EQ(tostring(uint64.idiv(-7, 2)), "-4")
	local ok, err = pcall(uint64.idiv, max, 0)
	ASSERT_EQ(ok, false)
	ASSERT_TRUE(string.find(err, "attempt to perform 'n//0'", 1, true))
	-- Results are Int64 values that C# long members take.
	class.LongMin = uint64.lshift(1, 40)
	ASSERT_EQ(tostring(class.LongMin), "1099511627776")
	-- ulong: the unsigned kind with UINT_ESPECIALLY, else the same signed bits.
	local umax = class.UlongMax
	ASSERT_EQ(tostring(uint64.rshift(umax, 60)), ULONG_TOSTRING("15"))
	ASSERT_EQ(tostring(uint64.idiv(umax, 2)), ULONG_TOSTRING("-1", "9223372036854775807"))
	class.UlongMin = uint64.band(umax, 0xFFFF)
	ASSERT_EQ(tostring(class.UlongMin), ULONG_TOSTRING("65535"))
	if DISTINCT_UINT64 then
		local mixed, mixErr = pcall(uint64.band, umax, max)
		ASSERT_EQ(mixed, false)
		ASSERT_TRUE(string.find(mixErr, "type not match", 1, true))
	end
end

function CMyTestCaseLuaCallCS.CaseVisitExtensionMethodForClass(self)
	self.count = 1 + self.count
    local class = CS.TestChineseString()
    class:PrintAllString()
	
	local length = class:GetLongStringLength()
	ASSERT_EQ(54, length)
	
	length = class:Add(class)
	ASSERT_EQ(108, length)
	local class_a = CS.TestChineseString()
	class_a.short_simple_string = "啊"
	ASSERT_EQ(class_a:GetShortChinString() , "啊")
	
	local class_d = CS.TestChineseString()
	class_d.short_simple_string = "ha"
	local ret1
	local ret2
	ret1, ret2 = class:Replace(class_d, class_a)
	ASSERT_EQ(ret1:GetShortChinString(), "中文字符串")
	ASSERT_EQ(ret2:GetCombineString(), "中文字符串")
	ASSERT_EQ(ret2:GetShortChinString(), "中文字符串ha")
end

function CMyTestCaseLuaCallCS.CaseVisitExtensionMethodForStruct(self)
	self.count = 1 + self.count
    local employ_1 = CS.Employeestruct()
	employ_1.Name = "HONGFANG"
	employ_1.Age = 30
	employ_1.Salary = 12000
	employ_1.AnnualBonus = 30000
    employ_1:PrintSalary()
	
	local income = employ_1:GetIncomeForOneYear()
	ASSERT_EQ(income, 174000)
	
	local employ_2 = CS.Employeestruct()
	employ_2.Name = "xiaojun"
	employ_2.Age =25
	employ_2.Salary = 5000
	employ_2.AnnualBonus = 10000
    employ_2:PrintSalary()
	
	local cost = employ_1:Add(employ_2)
	ASSERT_EQ(cost, 244000)	
	
	local employ_3 = CS.Employeestruct()
	employ_3.Name = "xiaojuan"
	employ_3.Age =25
	employ_3.Salary = 5000
	employ_3.AnnualBonus = 10000
    employ_3:PrintSalary()
	
	local ret1
	local ret2
	ret1, ret2 = employ_1:Sub(employ_2, employ_3)
	ASSERT_EQ(ret1.Name, "xiaojun")
	ASSERT_EQ(ret1.Salary, 10000)
	ASSERT_EQ(ret2.Name, "xiaojuan")
	ASSERT_EQ(ret2.Salary, 7000)
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_1(self)
	self.count = 1 + self.count
    local struct_1 = CS.NoGenCodeStruct()
	
	local ret = struct_1:Add(2, 3)
	struct_1.Byte = 10;
	struct_1.Char = 97;
	struct_1.Decimal = 12.33333333;
	struct_1.Double = 10.111111111111;
	struct_1.Float = 11.120;
	struct_1.IntVar1 = 12345678;
	struct_1.IntVar2 = -12345678;
	struct_1.Long = CS.LongStatic.LONG_MAX;
	struct_1.ULong = CS.LongStatic.ULONG_MAX;
	struct_1.Short = 123;
	struct_1.String = "just for test";
	struct_1.UInt = 12345;
	struct_1.UShort = 255;
	struct_1.IncludeStruct = CS.ConStruct (1, 2, "haha");
	ASSERT_EQ(ret, 5)
	ASSERT_EQ(struct_1.Byte, 10)
	ASSERT_EQ(tostring(struct_1.Char), "97")
	ASSERT_EQ(string.sub(tostring(struct_1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(struct_1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(struct_1.Float), 1, 6), "11.119") --Precision loss
	ASSERT_EQ(struct_1.IntVar1, 12345678)
	ASSERT_EQ(struct_1.IntVar2, -12345678)
	ASSERT_EQ(tostring(struct_1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(struct_1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(struct_1.Short, 123)
	ASSERT_EQ(struct_1.String, "just for test")
	ASSERT_EQ(struct_1.UInt, 12345)
	ASSERT_EQ(struct_1.UShort, 255)	
	ASSERT_EQ(struct_1.IncludeStruct.x, 1)
	ASSERT_EQ(struct_1.IncludeStruct.y, 2)
	ASSERT_EQ(struct_1.IncludeStruct.z, "haha")
	
	
	local struct_2 = CS.ConStruct (2, 2, "haha");
	ASSERT_EQ(struct_2.x, 2)
	ASSERT_EQ(struct_2.y, 2)
	ASSERT_EQ(struct_2.z, "haha")
	
	struct_1.String = nil;
	ASSERT_EQ(struct_1.String, nil)
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_2(self)
    --2.Structs as static and instance properties
	self.count = 1 + self.count
    CS.NoGenCodeBaseClass.struct_var2 = CS.ConStruct(33333333, 44444444, "test") --Assigning individual fields through the property would fail.
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var2.x, 33333333)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var2.y, 44444444)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var2.z, "test")
	
	
	--[[Passed by value: modifying the copy does not change the original.
	local ret = CS.NoGenCodeBaseClass.struct_var1:Add(2, 3)
	CS.NoGenCodeBaseClass.struct_var1.Byte = 10;
	CS.NoGenCodeBaseClass.struct_var1.Char = 97;
	CS.NoGenCodeBaseClass.struct_var1.Decimal = 12.33333333;
	CS.NoGenCodeBaseClass.struct_var1.Double = 10.111111111111;
	CS.NoGenCodeBaseClass.struct_var1.Float = 11.1234;
	CS.NoGenCodeBaseClass.struct_var1.IntVar1 = 12345678;
	CS.NoGenCodeBaseClass.struct_var1.IntVar2 = -12345678;
	CS.NoGenCodeBaseClass.struct_var1.Long = CS.LongStatic.LONG_MAX;
	CS.NoGenCodeBaseClass.struct_var1.ULong = CS.LongStatic.ULONG_MAX;
	CS.NoGenCodeBaseClass.struct_var1.Short = 123;
	CS.NoGenCodeBaseClass.struct_var1.String = "just for test";
	CS.NoGenCodeBaseClass.struct_var1.UInt = 12345;
	CS.NoGenCodeBaseClass.struct_var1.UShort = 255;
	CS.NoGenCodeBaseClass.struct_var1.IncludeStruct = CS.ConStruct (1, 2, "haha");
	ASSERT_EQ(ret, 5)
	
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.Byte, 10)
	ASSERT_EQ(tostring(CS.NoGenCodeBaseClass.struct_var1.Char), "97")
	ASSERT_EQ(string.sub(tostring(CS.NoGenCodeBaseClass.struct_var1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(CS.NoGenCodeBaseClass.struct_var1.Float), 1, 7), "11.1234")
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.IntVar1, 12345678)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.IntVar2, -12345678)
	ASSERT_EQ(tostring(CS.NoGenCodeBaseClass.struct_var1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(CS.NoGenCodeBaseClass.struct_var1.ULong), "18446744073709551615")
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.Short, 123)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.String, "just for test")
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.UInt, 12345)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.UShort, 255)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.IncludeStruct.x, 1)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.IncludeStruct.y, 2)
	ASSERT_EQ(CS.NoGenCodeBaseClass.struct_var1.IncludeStruct.z, "haha")
	
	local struct_2 = CS.NoGenCodeBaseClass.GetStaticVar();
	ASSERT_EQ(struct_2.Byte, 10)
	ASSERT_EQ(tostring(struct_2.Char), "97")
	ASSERT_EQ(string.sub(tostring(struct_2.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(struct_2.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(struct_2.Float), 1, 7), "11.1234")
	ASSERT_EQ(struct_2.IntVar1, 12345678)
	ASSERT_EQ(struct_2.IntVar2, -12345678)
	ASSERT_EQ(tostring(struct_2.Long), "9223372036854775807")
	ASSERT_EQ(tostring(struct_2.ULong), "18446744073709551615")
	ASSERT_EQ(struct_2.Short, 123)
	ASSERT_EQ(struct_2.String, "just for test")
	ASSERT_EQ(struct_2.UInt, 12345)
	ASSERT_EQ(struct_2.UShort, 255)
	ASSERT_EQ(struct_2.IncludeStruct.x, 1)
	ASSERT_EQ(struct_2.IncludeStruct.y, 2)
	ASSERT_EQ(struct_2.IncludeStruct.z, "haha")
	]]
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_3_5_9(self)
    --3.Structs as input arguments to static and instance methods
	--5.Structs as ref parameters to static and instance methods
	--9.Structs as input, out, and ref parameters to overloaded methods
	self.count = 1 + self.count
    	
	local struct_1 = CS.NoGenCodeStruct()
	
	struct_1.Byte = 10;
	struct_1.Char = 97;
	struct_1.Decimal = 12.33333333;
	struct_1.Double = 10.111111111111;
	struct_1.Float = 11.1234;
	struct_1.IntVar1 = 12345678;
	struct_1.IntVar2 = -12345678;
	struct_1.Long = CS.LongStatic.LONG_MAX;
	struct_1.ULong = CS.LongStatic.ULONG_MAX;
	struct_1.Short = 123;
	struct_1.String = "just for test";
	struct_1.UInt = 12345;
	struct_1.UShort = 256;
	struct_1.IncludeStruct = CS.ConStruct (2, 2, "haha");
	
	local class_1 = CS.NoGenCodeBaseClass();
	local ret1, ret2 = class_1:SetStruct(struct_1)
	ASSERT_EQ(ret1.Byte, 10)
	ASSERT_EQ(tostring(ret1.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret1.IntVar1, 12345678)
	ASSERT_EQ(ret1.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret1.Short, 123)
	ASSERT_EQ(ret1.String, "just for test")
	ASSERT_EQ(ret1.UInt, 12345)
	ASSERT_EQ(ret1.UShort, 256)	
	ASSERT_EQ(ret1.IncludeStruct.x, 2)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "haha")
	
	ASSERT_EQ(ret2.Byte, 10)
	ASSERT_EQ(tostring(ret2.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret2.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret2.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret2.Float), 1, 7), "11.1233")  --Precision loss
	ASSERT_EQ(ret2.IntVar1, 12345678)
	ASSERT_EQ(ret2.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret2.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret2.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret2.Short, 123)
	ASSERT_EQ(ret2.String, "just for test")
	ASSERT_EQ(ret2.UInt, 12345)
	ASSERT_EQ(ret2.UShort, 256)	
	ASSERT_EQ(ret2.IncludeStruct.x, 2)
	ASSERT_EQ(ret2.IncludeStruct.y, 2)
	ASSERT_EQ(ret2.IncludeStruct.z, "haha")
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_4(self)
    --4.Structs as outputs of static and instance methods
	self.count = 1 + self.count
	
	local class_1 = CS.NoGenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ASSERT_EQ(ret1.Byte, 10)
	ASSERT_EQ(tostring(ret1.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret1.IntVar1, 12345678)
	ASSERT_EQ(ret1.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret1.Short, 123)
	ASSERT_EQ(ret1.String, "just for test")
	ASSERT_EQ(ret1.UInt, 12345)
	ASSERT_EQ(ret1.UShort, 255)
	ASSERT_EQ(ret1.IncludeStruct.x, 1)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "haha")
	ASSERT_EQ(ret2, 0)
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_6(self)
    --6.Return a struct from a static or instance method, modify its fields, then pass it to another method.
	self.count = 1 + self.count
	
	local class_1 = CS.NoGenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ret1.Byte = 100
	ret1.Char = 98
	ret1.Decimal = 22.33333333
	ret1.Double = 20.111111111111
	ret1.Float = 21.1234
	ret1.IntVar1 = 22345678
	ret1.IntVar2 = -22345678
	ret1.Long = 9223372036854
	ret1.ULong = 18446744073709
	ret1.Short = 223
	ret1.String = "lua call c#"
	ret1.UInt = 22345
	ret1.UShort = 155
	ret1.IncludeStruct = CS.ConStruct (2, 2, "hahaTEST");
	
	local ret3, ret4 = class_1:SetStruct(ret1)
	ASSERT_EQ(ret3.Byte, 100)
	ASSERT_EQ(tostring(ret3.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret3.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret3.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret3.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret3.IntVar1, 22345678)
	ASSERT_EQ(ret3.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret3.Long), "9223372036854")
	ASSERT_EQ(tostring(ret3.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret3.Short, 223)
	ASSERT_EQ(ret3.String, "lua call c#")
	ASSERT_EQ(ret3.UInt, 22345)
	ASSERT_EQ(ret3.UShort, 155)	
	ASSERT_EQ(ret3.IncludeStruct.x, 2)
	ASSERT_EQ(ret3.IncludeStruct.y, 2)
	ASSERT_EQ(ret3.IncludeStruct.z, "hahaTEST")
	
	ASSERT_EQ(ret4.Byte, 100)
	ASSERT_EQ(tostring(ret4.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret4.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret4.IntVar1, 22345678)
	ASSERT_EQ(ret4.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret4.Long), "9223372036854")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret4.Short, 223)
	ASSERT_EQ(ret4.String, "lua call c#")
	ASSERT_EQ(ret4.UInt, 22345)
	ASSERT_EQ(ret4.UShort, 155)	
	ASSERT_EQ(ret4.IncludeStruct.x, 2)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "hahaTEST")
end

function CMyTestCaseLuaCallCS.CaseVisitNoGenCodeStruct_7(self)
    --7.Read and write base class static and instance struct properties through a derived instance in Lua.
	self.count = 1 + self.count
	
	local class_1 = CS.NoGenCodeDrivedClass();
	local ret1, ret2 = class_1:InitStruct()
	ret1.Byte = 100
	ret1.Char = 98
	ret1.Decimal = 22.33333333
	ret1.Double = 20.111111111111
	ret1.Float = 21.1234
	ret1.IntVar1 = 22345678
	ret1.IntVar2 = -22345678
	ret1.Long = 9223372036854
	ret1.ULong = 18446744073709
	ret1.Short = 223
	ret1.String = "lua call c#"
	ret1.UInt = 22345
	ret1.UShort = 155
	ret1.IncludeStruct = CS.ConStruct (2, 2, "hahaTEST");
	
	local ret3, ret4 = class_1:SetStruct(ret1)
	ASSERT_EQ(ret3.Byte, 100)
	ASSERT_EQ(tostring(ret3.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret3.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret3.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret3.Float), 1, 7), "21.1233")  --Precision loss
	ASSERT_EQ(ret3.IntVar1, 22345678)
	ASSERT_EQ(ret3.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret3.Long), "9223372036854")
	ASSERT_EQ(tostring(ret3.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret3.Short, 223)
	ASSERT_EQ(ret3.String, "lua call c#")
	ASSERT_EQ(ret3.UInt, 22345)
	ASSERT_EQ(ret3.UShort, 155)	
	ASSERT_EQ(ret3.IncludeStruct.x, 2)
	ASSERT_EQ(ret3.IncludeStruct.y, 2)
	ASSERT_EQ(ret3.IncludeStruct.z, "hahaTEST")
	
	ASSERT_EQ(ret4.Byte, 100)
	ASSERT_EQ(tostring(ret4.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret4.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret4.IntVar1, 22345678)
	ASSERT_EQ(ret4.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret4.Long), "9223372036854")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret4.Short, 223)
	ASSERT_EQ(ret4.String, "lua call c#")
	ASSERT_EQ(ret4.UInt, 22345)
	ASSERT_EQ(ret4.UShort, 155)	
	ASSERT_EQ(ret4.IncludeStruct.x, 2)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "hahaTEST")
end

function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_10(self)
	self.count = 1 + self.count
    local struct_1 = CS.GenCodeStruct()
	
	local ret = struct_1:Add(2, 3)
	struct_1.Byte = 10;
	struct_1.Char = 97;
	struct_1.Decimal = 12.33333333;
	struct_1.Double = 10.111111111111;
	struct_1.Float = 11.1234;
	struct_1.IntVar1 = 12345678;
	struct_1.IntVar2 = -12345678;
	struct_1.Long = CS.LongStatic.LONG_MAX;
	struct_1.ULong = CS.LongStatic.ULONG_MAX;
	struct_1.Short = 123;
	struct_1.String = "just for test";
	struct_1.UInt = 12345;
	struct_1.UShort = 255;
	struct_1.IncludeStruct = CS.HasConstructStruct (1, 2, "haha");
	ASSERT_EQ(ret, 5)
	ASSERT_EQ(struct_1.Byte, 10)
	ASSERT_EQ(tostring(struct_1.Char), "97")
	ASSERT_EQ(string.sub(tostring(struct_1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(struct_1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(struct_1.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(struct_1.IntVar1, 12345678)
	ASSERT_EQ(struct_1.IntVar2, -12345678)
	ASSERT_EQ(tostring(struct_1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(struct_1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(struct_1.Short, 123)
	ASSERT_EQ(struct_1.String, "just for test")
	ASSERT_EQ(struct_1.UInt, 12345)
	ASSERT_EQ(struct_1.UShort, 255)	
	ASSERT_EQ(struct_1.IncludeStruct.x, 1)
	ASSERT_EQ(struct_1.IncludeStruct.y, 2)
	ASSERT_EQ(struct_1.IncludeStruct.z, "haha")
end

function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_11(self)
	self.count = 1 + self.count
    	
	local ret = CS.GenCodeBaseClass.struct_var1:Add(2, 3)
	ASSERT_EQ(CS.GenCodeBaseClass.GBS, 1)
	CS.GenCodeBaseClass.GBS = 2;
	ASSERT_EQ(CS.GenCodeBaseClass.GBS, 2)
	CS.GenCodeBaseClass.GBS = 1;
	CS.GenCodeBaseClass.struct_var2 = CS.HasConstructStruct(33333333, 44444444, "test") --Assigning individual fields through the property would fail.
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var2.x, 33333333)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var2.y, 44444444)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var2.z, "test")
	
	--[[Passed by value: modifying the copy does not change the original.
	CS.GenCodeBaseClass.struct_var1.Byte = 10;
	CS.GenCodeBaseClass.struct_var1.Char = 97;
	CS.GenCodeBaseClass.struct_var1.Decimal = 12.33333333;
	CS.GenCodeBaseClass.struct_var1.Double = 10.111111111111;
	CS.GenCodeBaseClass.struct_var1.Float = 11.1234;
	CS.GenCodeBaseClass.struct_var1.IntVar1 = 12345678;
	CS.GenCodeBaseClass.struct_var1.IntVar2 = -12345678;
	CS.GenCodeBaseClass.struct_var1.Long = CS.LongStatic.LONG_MAX;
	CS.GenCodeBaseClass.struct_var1.ULong = CS.LongStatic.ULONG_MAX;
	CS.GenCodeBaseClass.struct_var1.Short = 123;
	CS.GenCodeBaseClass.struct_var1.String = "just for test";
	CS.GenCodeBaseClass.struct_var1.UInt = 12345;
	CS.GenCodeBaseClass.struct_var1.UShort = 255;
	CS.GenCodeBaseClass.struct_var1.IncludeStruct = CS.HasConstructStruct (1, 2, "haha");
	ASSERT_EQ(ret, 5)
	
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.Byte, 10)
	ASSERT_EQ(tostring(CS.GenCodeBaseClass.struct_var1.Char), "97")
	ASSERT_EQ(string.sub(tostring(CS.GenCodeBaseClass.struct_var1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(CS.GenCodeBaseClass.struct_var1.Float), 1, 7), "11.1234")
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.IntVar1, 12345678)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.IntVar2, -12345678)
	ASSERT_EQ(tostring(CS.GenCodeBaseClass.struct_var1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(CS.GenCodeBaseClass.struct_var1.ULong), "18446744073709551615")
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.Short, 123)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.String, "just for test")
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.UInt, 12345)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.UShort, 255)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.IncludeStruct.x, 1)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.IncludeStruct.y, 2)
	ASSERT_EQ(CS.GenCodeBaseClass.struct_var1.IncludeStruct.z, "haha")
	
	local struct_2 = CS.GenCodeBaseClass.GetStaticVar();
	ASSERT_EQ(struct_2.Byte, 10)
	ASSERT_EQ(tostring(struct_2.Char), "97")
	ASSERT_EQ(string.sub(tostring(struct_2.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(struct_2.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(struct_2.Float), 1, 7), "11.1234")
	ASSERT_EQ(struct_2.IntVar1, 12345678)
	ASSERT_EQ(struct_2.IntVar2, -12345678)
	ASSERT_EQ(tostring(struct_2.Long), "9223372036854775807")
	ASSERT_EQ(tostring(struct_2.ULong), "18446744073709551615")
	ASSERT_EQ(struct_2.Short, 123)
	ASSERT_EQ(struct_2.String, "just for test")
	ASSERT_EQ(struct_2.UInt, 12345)
	ASSERT_EQ(struct_2.UShort, 255)
	ASSERT_EQ(struct_2.IncludeStruct.x, 1)
	ASSERT_EQ(struct_2.IncludeStruct.y, 2)
	ASSERT_EQ(struct_2.IncludeStruct.z, "haha")]]
end


function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_12_14_18(self)
	self.count = 1 + self.count
    	
	local struct_1 = CS.GenCodeStruct()
	
	struct_1.Byte = 10;
	struct_1.Char = 97;
	struct_1.Decimal = 12.33333333;
	struct_1.Double = 10.111111111111;
	struct_1.Float = 11.1234;
	struct_1.IntVar1 = 12345678;
	struct_1.IntVar2 = -12345678;
	struct_1.Long = CS.LongStatic.LONG_MAX;
	struct_1.ULong = CS.LongStatic.ULONG_MAX;
	struct_1.Short = 123;
	struct_1.String = "just for test";
	struct_1.UInt = 12345;
	struct_1.UShort = 256;
	struct_1.IncludeStruct = CS.HasConstructStruct (2, 2, "haha");
	
	local class_1 = CS.GenCodeBaseClass();
	local ret1, ret2 = class_1:SetStruct(struct_1)
	ASSERT_EQ(ret1.Byte, 10)
	ASSERT_EQ(tostring(ret1.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret1.IntVar1, 12345678)
	ASSERT_EQ(ret1.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret1.Short, 123)
	ASSERT_EQ(ret1.String, "just for test")
	ASSERT_EQ(ret1.UInt, 12345)
	ASSERT_EQ(ret1.UShort, 256)
	ASSERT_EQ(ret1.IncludeStruct.x, 2)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "haha")
	
	ASSERT_EQ(ret2.Byte, 10)
	ASSERT_EQ(tostring(ret2.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret2.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret2.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret2.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret2.IntVar1, 12345678) 
	ASSERT_EQ(ret2.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret2.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret2.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret2.Short, 123)
	ASSERT_EQ(ret2.String, "just for test")
	ASSERT_EQ(ret2.UInt, 12345)
	ASSERT_EQ(ret2.UShort, 256)	
	ASSERT_EQ(ret2.IncludeStruct.x, 2)
	ASSERT_EQ(ret2.IncludeStruct.y, 2)
	ASSERT_EQ(ret2.IncludeStruct.z, "haha")
end

function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_13(self)
	self.count = 1 + self.count
	
	local class_1 = CS.GenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ASSERT_EQ(ret1.Byte, 100)
	ASSERT_EQ(tostring(ret1.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret1.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret1.IntVar1, 22345678)
	ASSERT_EQ(ret1.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret1.Long), "-9223372036854775808")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("0"))
	ASSERT_EQ(ret1.Short, 223)
	ASSERT_EQ(ret1.String, "2just for test")
	ASSERT_EQ(ret1.UInt, 22345)
	ASSERT_EQ(ret1.UShort, 128)
	ASSERT_EQ(ret1.IncludeStruct.x, 2)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "2haha")
	ASSERT_EQ(ret2, 0)
end

function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_15(self)
	self.count = 1 + self.count
	
	local class_1 = CS.GenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ret1.Byte = 100
	ret1.Char = 98
	ret1.Decimal = 22.33333333
	ret1.Double = 20.111111111111
	ret1.Float = 21.1234
	ret1.IntVar1 = 22345678
	ret1.IntVar2 = -22345678
	ret1.Long = 9223372036854
	ret1.ULong = 18446744073709
	ret1.Short = 223
	ret1.String = "lua call c#"
	ret1.UInt = 22345
	ret1.UShort = 155
	ret1.IncludeStruct = CS.HasConstructStruct (2, 2, "hahaTEST");
	
	local ret3, ret4 = class_1:SetStruct(ret1)
	ASSERT_EQ(ret3.Byte, 100)
	ASSERT_EQ(tostring(ret3.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret3.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret3.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret3.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret3.IntVar1, 22345678)
	ASSERT_EQ(ret3.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret3.Long), "9223372036854")
	ASSERT_EQ(tostring(ret3.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret3.Short, 223)
	ASSERT_EQ(ret3.String, "lua call c#")
	ASSERT_EQ(ret3.UInt, 22345)
	ASSERT_EQ(ret3.UShort, 155)	
	ASSERT_EQ(ret3.IncludeStruct.x, 2)
	ASSERT_EQ(ret3.IncludeStruct.y, 2)
	ASSERT_EQ(ret3.IncludeStruct.z, "hahaTEST")
	
	ASSERT_EQ(ret4.Byte, 100)
	ASSERT_EQ(tostring(ret4.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret4.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret4.IntVar1, 22345678)
	ASSERT_EQ(ret4.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret4.Long), "9223372036854")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret4.Short, 223)
	ASSERT_EQ(ret4.String, "lua call c#")
	ASSERT_EQ(ret4.UInt, 22345)
	ASSERT_EQ(ret4.UShort, 155)	
	ASSERT_EQ(ret4.IncludeStruct.x, 2)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "hahaTEST")
end


function CMyTestCaseLuaCallCS.CaseVisitGenCodeStruct_17(self)
	self.count = 1 + self.count
	
	local class_1 = CS.GenCodeDrivedClass();
	local ret1, ret2 = class_1:InitStruct()
	ret1.Byte = 100
	ret1.Char = 98
	ret1.Decimal = 22.33333333
	ret1.Double = 20.111111111111
	ret1.Float = 11.1234
	ret1.IntVar1 = 22345678
	ret1.IntVar2 = -22345678
	ret1.Long = 9223372036854
	ret1.ULong = 18446744073709
	ret1.Short = 223
	ret1.String = "lua call c#"
	ret1.UInt = 22345
	ret1.UShort = 155
	ret1.IncludeStruct = CS.HasConstructStruct (2, 2, "hahaTEST");
	
	local ret3, ret4 = class_1:SetStruct(ret1)
	ASSERT_EQ(ret3.Byte, 100)
	ASSERT_EQ(tostring(ret3.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret3.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret3.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret3.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret3.IntVar1, 22345678)
	ASSERT_EQ(ret3.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret3.Long), "9223372036854")
	ASSERT_EQ(tostring(ret3.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret3.Short, 223)
	ASSERT_EQ(ret3.String, "lua call c#")
	ASSERT_EQ(ret3.UInt, 22345)
	ASSERT_EQ(ret3.UShort, 155)	
	ASSERT_EQ(ret3.IncludeStruct.x, 2)
	ASSERT_EQ(ret3.IncludeStruct.y, 2)
	ASSERT_EQ(ret3.IncludeStruct.z, "hahaTEST")
	
	ASSERT_EQ(ret4.Byte, 100)
	ASSERT_EQ(tostring(ret4.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret4.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "11.1233") --Precision loss
	ASSERT_EQ(ret4.IntVar1, 22345678)
	ASSERT_EQ(ret4.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret4.Long), "9223372036854")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("18446744073709"))
	ASSERT_EQ(ret4.Short, 223)
	ASSERT_EQ(ret4.String, "lua call c#")
	ASSERT_EQ(ret4.UInt, 22345)
	ASSERT_EQ(ret4.UShort, 155)	
	ASSERT_EQ(ret4.IncludeStruct.x, 2)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "hahaTEST")
end

function CMyTestCaseLuaCallCS.CaseVisitStruct_19(self)
    --19.Return a struct through reflection, then pass it as an input or ref argument to a generated wrapper.
	self.count = 1 + self.count
	
	local class_1 = CS.NoGenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ASSERT_EQ(ret1.Byte, 10)
	ASSERT_EQ(tostring(ret1.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret1.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "11.1233")  --Precision loss
	ASSERT_EQ(ret1.IntVar1, 12345678)
	ASSERT_EQ(ret1.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret1.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret1.Short, 123)
	ASSERT_EQ(ret1.String, "just for test")
	ASSERT_EQ(ret1.UInt, 12345)
	ASSERT_EQ(ret1.UShort, 255)
	ASSERT_EQ(ret1.IncludeStruct.x, 1)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "haha")
	ASSERT_EQ(ret2, 0)
	
	local class_2 = CS.GenCodeBaseClass();
	local ret3, ret4 = class_2:SetStruct(ret1)
	ASSERT_EQ(ret3, 0)
	ASSERT_EQ(ret4.Byte, 10)
	ASSERT_EQ(tostring(ret4.Char), "97")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "12.33333333")
	ASSERT_EQ(ret4.Double, 10.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "11.1233")  --Precision loss
	ASSERT_EQ(ret4.IntVar1, 12345678)
	ASSERT_EQ(ret4.IntVar2, -12345678)
	ASSERT_EQ(tostring(ret4.Long), "9223372036854775807")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("-1", "18446744073709551615"))
	ASSERT_EQ(ret4.Short, 123)
	ASSERT_EQ(ret4.String, "just for test")
	ASSERT_EQ(ret4.UInt, 12345)
	ASSERT_EQ(ret4.UShort, 255)
	ASSERT_EQ(ret4.IncludeStruct.x, 1)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "haha")
end

function CMyTestCaseLuaCallCS.CaseVisitStruct_20(self)
    --20.Return a struct through a generated wrapper, then pass it as an input or ref argument through reflection.
	self.count = 1 + self.count
	
	local class_1 = CS.GenCodeBaseClass();
	local ret1, ret2 = class_1:InitStruct()
	ASSERT_EQ(ret1.Byte, 100)
	ASSERT_EQ(tostring(ret1.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret1.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret1.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret1.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret1.IntVar1, 22345678)
	ASSERT_EQ(ret1.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret1.Long), "-9223372036854775808")
	ASSERT_EQ(tostring(ret1.ULong), ULONG_TOSTRING("0"))
	ASSERT_EQ(ret1.Short, 223)
	ASSERT_EQ(ret1.String, "2just for test")
	ASSERT_EQ(ret1.UInt, 22345)
	ASSERT_EQ(ret1.UShort, 128)
	ASSERT_EQ(ret1.IncludeStruct.x, 2)
	ASSERT_EQ(ret1.IncludeStruct.y, 2)
	ASSERT_EQ(ret1.IncludeStruct.z, "2haha")
	ASSERT_EQ(ret2, 0)
	
	local class_2 = CS.NoGenCodeBaseClass();
	local ret3, ret4 = class_2:SetStruct(ret1)
	ASSERT_EQ(ret3, 0)
	ASSERT_EQ(tostring(ret4.Char), "98")
	ASSERT_EQ(string.sub(tostring(ret4.Decimal), 1, 11), "22.33333333")
	ASSERT_EQ(ret4.Double, 20.111111111111)
	ASSERT_EQ(string.sub(tostring(ret4.Float), 1, 7), "21.1233") --Precision loss
	ASSERT_EQ(ret4.IntVar1, 22345678)
	ASSERT_EQ(ret4.IntVar2, -22345678)
	ASSERT_EQ(tostring(ret4.Long), "-9223372036854775808")
	ASSERT_EQ(tostring(ret4.ULong), ULONG_TOSTRING("0"))
	ASSERT_EQ(ret4.Short, 223)
	ASSERT_EQ(ret4.String, "2just for test")
	ASSERT_EQ(ret4.UInt, 22345)
	ASSERT_EQ(ret4.UShort, 128)
	ASSERT_EQ(ret4.IncludeStruct.x, 2)
	ASSERT_EQ(ret4.IncludeStruct.y, 2)
	ASSERT_EQ(ret4.IncludeStruct.z, "2haha")
	ASSERT_EQ(ret2, 0)
end

function CMyTestCaseLuaCallCS.CaseVisitStruct_21(self)
    --21.A inherits B, which inherits C. If A and C have generated wrappers, B must also have one; alternatively, use reflection for all three.
	self.count = 1 + self.count
	
	--22.Access struct properties declared by B and C through an instance of A.
	local class_a = CS.AClass(1, 2, "haha")
	ASSERT_EQ(class_a.BConStruct.x, 1)
	ASSERT_EQ(class_a.BConStruct.y, 2)
	ASSERT_EQ(class_a.BConStruct.z, "haha")
	ASSERT_EQ(class_a.CConStruct.x, 1)
	ASSERT_EQ(class_a.CConStruct.y, 2)
	ASSERT_EQ(class_a.CConStruct.z, "haha")
	
	--23.Call methods declared by B through an instance of A, using struct input, out, and ref parameters.
	local struct_1 = CS.HasConstructStruct(2, 3, "TEST")
	ASSERT_EQ(struct_1.x, 2)
	ASSERT_EQ(struct_1.y, 3)
	ASSERT_EQ(struct_1.z, "TEST")
	local struct_2 = CS.HasConstructStruct(10, 1, "heihei")
	local ret1, ret2 = class_a:Sub(struct_1, struct_2)
	ASSERT_EQ(struct_2.x, 2)
	ASSERT_EQ(ret1.x, 2)
	ASSERT_EQ(ret1.y, 1)
	ASSERT_EQ(ret1.z, "heihei")
	ASSERT_EQ(ret2.x, -8)
	ASSERT_EQ(ret2.y, 3)
	ASSERT_EQ(ret2.z, "TEST")
	
	--24.Call methods declared by C through an instance of A, using struct input, out, and ref parameters.
	local ret3, ret4 = class_a:Add(struct_1, struct_2)
	ASSERT_EQ(struct_2.x, 2)
	ASSERT_EQ(ret3.x, 2)
	ASSERT_EQ(ret3.y, 1)
	ASSERT_EQ(ret3.z, "heihei")
    ASSERT_EQ(ret4.x, 4)
	ASSERT_EQ(ret4.y, 3)
	ASSERT_EQ(ret4.z, "TEST")
end

function CMyTestCaseLuaCallCS.CaseVisitStructVaribleParam(self)
    --Structs in a params argument list
	self.count = 1 + self.count
	
	local struct_1 = CS.HasConstructStruct(2, 3, "TEST")
	local struct_2 = CS.HasConstructStruct(10, 1, "heihei")
	local class_c = CS.CClass(1, 2, "haha")
	local ret = class_c:VariableParamFunc(struct_1, struct_2)
	ASSERT_EQ(ret, 12)
end

function CMyTestCaseLuaCallCS.CaseVisitFloat1(self)

    self.count = 1 + self.count
	
	local struct_2 = CS.NoGen2FloatStruct(1.17549435e-38, 2.125)
	ASSERT_EQ(string.sub(tostring(struct_2.a), 1, 10) , "1.17549435")
	ASSERT_EQ(string.sub(tostring(struct_2.a), -4, -1) , "e-38")
	ASSERT_EQ(string.sub(tostring(struct_2.b), 1, 5), "2.125")
end

function CMyTestCaseLuaCallCS.CaseVisitStructFloat1(self)
    --A struct with two float fields, accessed through reflection; expect precision loss.
	self.count = 1 + self.count
	
	local struct_2 = CS.NoGen2FloatStruct(1.234, 2.125)
	ASSERT_EQ(string.sub(tostring(struct_2.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_2.b), 1, 5), "2.125")
	
	local struct_3 = CS.NoGen3FloatStruct(struct_2.a, struct_2.b, struct_2.a + struct_2.b)
	
	ASSERT_EQ(string.sub(tostring(struct_3.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_3.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_3.c), 1, 5), "3.358")
	
	local struct_4 = CS.NoGen4FloatStruct(struct_3.a, struct_3.b, struct_3.a + struct_3.b, struct_3.c)
	
	ASSERT_EQ(string.sub(tostring(struct_4.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_4.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_4.c), 1, 5), "3.358")
	ASSERT_EQ(string.sub(tostring(struct_4.d), 1, 5), "3.358")
	
	local struct_5 = CS.NoGen5FloatStruct(struct_2.a - struct_2.b, 2.125, 3.123, 4.500, 5.333)
	ASSERT_EQ(string.sub(tostring(struct_5.a), 1, 6), "-0.891")
	ASSERT_EQ(string.sub(tostring(struct_5.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_5.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(struct_5.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(struct_5.e), 1, 5), "5.333")
	
	local struct_6 = CS.NoGen6FloatStruct(struct_2.a * struct_2.b, struct_2.a/2, 3.123, 4.500, 5.333, 2.1)
	ASSERT_EQ(string.sub(tostring(struct_6.a), 1, 5), "2.622")
	ASSERT_EQ(string.sub(tostring(struct_6.b), 1, 6), "0.6169")
	ASSERT_EQ(string.sub(tostring(struct_6.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(struct_6.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(struct_6.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(struct_6.f), 1, 5), "2.099")
end

function CMyTestCaseLuaCallCS.CaseVisitStructFloatInClass(self)
	--A class with multiple struct properties containing float fields. Exercise argument passing through generated wrappers and reflection; expect precision loss.
	self.count = 1 + self.count
	local class_1 = CS.TestNoGenFloatStructClass()
	local class_2 = CS.TestGenFloatStructClass()
	
	local no_gen_struct_2 = CS.NoGen2FloatStruct(1.234, 2.125)
	no_gen_struct_2.a = 1.234
	no_gen_struct_2.b = 2.125
	
	local no_gen_struct_3 = CS.NoGen3FloatStruct(970.12, 1012.34, 1111.45)
	no_gen_struct_3.a = 970.12
	no_gen_struct_3.b = 1012.34
	no_gen_struct_3.c = 1111.45
	
	local no_gen_struct_4 = CS.NoGen4FloatStruct(1.0, 1.0, 1.0, 2.0)
	no_gen_struct_4.a = 1018.2
	no_gen_struct_4.b = -972.1
	no_gen_struct_4.c = -100.2
	no_gen_struct_4.d = 999.2
	
	local no_gen_struct_5 = CS.NoGen5FloatStruct(1.2, 1.2, 1.2, 1.3, 1.3)
	no_gen_struct_5.a = 2.120
	no_gen_struct_5.b = 2.125
	no_gen_struct_5.c = 3.123
	no_gen_struct_5.d = 4.500
	no_gen_struct_5.e = 5.333
	
	local no_gen_struct_6 = CS.NoGen6FloatStruct(1.1, 1.1, 1.1, 1.1, 1.1, 1.2)
	no_gen_struct_6.a = 10.11
	no_gen_struct_6.b = 120.22
	no_gen_struct_6.c = 3.123
	no_gen_struct_6.d = 4.500
	no_gen_struct_6.e = 5.333
	no_gen_struct_6.f = 2.1
	
	local gen_struct_2 = CS.Gen2FloatStruct(1.0, 1.2)
	gen_struct_2.a = 1.234
	gen_struct_2.b = 2.125
	
	local gen_struct_3 = CS.Gen3FloatStruct(1.3, 1.3, 1.3)
	gen_struct_3.a = 970.12
	gen_struct_3.b = 1012.34
	gen_struct_3.c = 1111.45
	
	local gen_struct_4 = CS.Gen4FloatStruct(1.0, 1.0, 1.0, 2.0)
	gen_struct_4.a = 1018.2
	gen_struct_4.b = -972.1
	gen_struct_4.c = -100.2
	gen_struct_4.d = 999.2
	
	local gen_struct_5 = CS.Gen5FloatStruct(1.2, 1.2, 1.2, 1.3, 1.3)
	gen_struct_5.a = 2.120
	gen_struct_5.b = 2.125
	gen_struct_5.c = 3.123
	gen_struct_5.d = 4.500
	gen_struct_5.e = 5.333
	
	local gen_struct_6 = CS.Gen6FloatStruct(1.1, 1.1, 1.1, 1.1, 1.1, 1.2)
	gen_struct_6.a = 10.11
	gen_struct_6.b = 120.22
	gen_struct_6.c = 3.123
	gen_struct_6.d = 4.500
	gen_struct_6.e = 5.333
	gen_struct_6.f = 2.1
	
	class_1.Struct2 = no_gen_struct_2;
	class_1.Struct3 = no_gen_struct_3;
	class_1.Struct4 = no_gen_struct_4;
	class_1.Struct5 = no_gen_struct_5;
	class_1.Struct6 = no_gen_struct_6;
	ASSERT_EQ(string.sub(tostring(class_1.Struct2.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(class_1.Struct2.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(class_1.Struct3.a), 1, 6), "970.11")
	ASSERT_EQ(string.sub(tostring(class_1.Struct3.b), 1, 7), "1012.34")
	ASSERT_EQ(string.sub(tostring(class_1.Struct3.c), 1, 7), "1111.44")
	ASSERT_EQ(string.sub(tostring(class_1.Struct4.a), 1, 7), "1018.20")
	ASSERT_EQ(string.sub(tostring(class_1.Struct4.b), 1, 7), "-972.09")
	ASSERT_EQ(string.sub(tostring(class_1.Struct4.c), 1, 7), "-100.19")
	ASSERT_EQ(string.sub(tostring(class_1.Struct4.d), 1, 6), "999.20")
	ASSERT_EQ(string.sub(tostring(class_1.Struct5.a), 1, 5), "2.119")
	ASSERT_EQ(string.sub(tostring(class_1.Struct5.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(class_1.Struct5.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(class_1.Struct5.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(class_1.Struct5.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.a), 1, 6), "10.109")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.b), 1, 7), "120.220")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(class_1.Struct6.f), 1, 5), "2.099")
	
	class_2.Struct2 = gen_struct_2;
	class_2.Struct3 = gen_struct_3;
	class_2.Struct4 = gen_struct_4;
	class_2.Struct5 = gen_struct_5;
	class_2.Struct6 = gen_struct_6;
	ASSERT_EQ(string.sub(tostring(class_2.Struct2.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(class_2.Struct2.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(class_2.Struct3.a), 1, 6), "970.11")
	ASSERT_EQ(string.sub(tostring(class_2.Struct3.b), 1, 7), "1012.34")
	ASSERT_EQ(string.sub(tostring(class_2.Struct3.c), 1, 7), "1111.44")
	ASSERT_EQ(string.sub(tostring(class_2.Struct4.a), 1, 7), "1018.20")
	ASSERT_EQ(string.sub(tostring(class_2.Struct4.b), 1, 7), "-972.09")
	ASSERT_EQ(string.sub(tostring(class_2.Struct4.c), 1, 7), "-100.19")
	ASSERT_EQ(string.sub(tostring(class_2.Struct4.d), 1, 6), "999.20")
	ASSERT_EQ(string.sub(tostring(class_2.Struct5.a), 1, 5), "2.119")
	ASSERT_EQ(string.sub(tostring(class_2.Struct5.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(class_2.Struct5.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(class_2.Struct5.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(class_2.Struct5.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.a), 1, 6), "10.109")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.b), 1, 7), "120.220")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(class_2.Struct6.f), 1, 5), "2.099")
	
	local ret1, ret2 = class_1:Add2(no_gen_struct_2, gen_struct_2)
	ASSERT_EQ(string.sub(tostring(ret1.a), 1, 5), "2.467")
	ASSERT_EQ(string.sub(tostring(ret1.b), 1, 5), "4.25")
	ASSERT_EQ(string.sub(tostring(ret2.a), 1, 5), "2.467")
	ASSERT_EQ(string.sub(tostring(ret2.b), 1, 5), "4.25")
	
end

function CMyTestCaseLuaCallCS.CaseVisitGenStructFloat1(self)
    --A struct with two float fields, accessed through generated wrappers; expect precision loss.
	self.count = 1 + self.count
	
	local struct_2 = CS.Gen2FloatStruct(1.234, 2.125)
	ASSERT_EQ(string.sub(tostring(struct_2.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_2.b), 1, 5), "2.125")
	
	local struct_3 = CS.Gen3FloatStruct(struct_2.a, struct_2.b, struct_2.a + struct_2.b)
	
	ASSERT_EQ(string.sub(tostring(struct_3.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_3.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_3.c), 1, 5), "3.358")
	
	local struct_4 = CS.Gen4FloatStruct(struct_3.a, struct_3.b, struct_3.a + struct_3.b, struct_3.c)
	
	ASSERT_EQ(string.sub(tostring(struct_4.a), 1, 5), "1.233")
	ASSERT_EQ(string.sub(tostring(struct_4.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_4.c), 1, 5), "3.358")
	ASSERT_EQ(string.sub(tostring(struct_4.d), 1, 5), "3.358")
	
	local struct_5 = CS.Gen5FloatStruct(struct_2.a - struct_2.b, 2.125, 3.123, 4.500, 5.333)
	ASSERT_EQ(string.sub(tostring(struct_5.a), 1, 6), "-0.891")
	ASSERT_EQ(string.sub(tostring(struct_5.b), 1, 5), "2.125")
	ASSERT_EQ(string.sub(tostring(struct_5.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(struct_5.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(struct_5.e), 1, 5), "5.333")
	
	local struct_6 = CS.Gen6FloatStruct(struct_2.a * struct_2.b, struct_2.a/2, 3.123, 4.500, 5.333, 2.1)
	ASSERT_EQ(string.sub(tostring(struct_6.a), 1, 5), "2.622")
	ASSERT_EQ(string.sub(tostring(struct_6.b), 1, 6), "0.6169")
	ASSERT_EQ(string.sub(tostring(struct_6.c), 1, 5), "3.122")
	ASSERT_EQ(string.sub(tostring(struct_6.d), 1, 5), "4.5")
	ASSERT_EQ(string.sub(tostring(struct_6.e), 1, 5), "5.333")
	ASSERT_EQ(string.sub(tostring(struct_6.f), 1, 5), "2.099")
end

function CMyTestCaseLuaCallCS.CaseVisitStructInt1(self)
    --A struct with two int fields, accessed through reflection.
	self.count = 1 + self.count
	
	local struct_2 = CS.NoGen2IntStruct(2, 3)
	ASSERT_EQ(struct_2.a, 2)
	ASSERT_EQ(struct_2.b, 3)
end

function CMyTestCaseLuaCallCS.CaseVisitStructIntString1(self)
    --A struct with two int fields and one string field, accessed through reflection.
	self.count = 1 + self.count
	
	local struct_2 = CS.ConStruct(2, 3, "haha")
	ASSERT_EQ(struct_2.x, 2)
	ASSERT_EQ(struct_2.y, 3)
	ASSERT_EQ(struct_2.z, "haha")
end

function CMyTestCaseLuaCallCS.CaseReflectEvent1(self)
	self.count = 1 + self.count

	gTestNumber = 1
	local testObj = CS.TestReflectEventClass()
	testObj:TestEvent1('+', EvtFunc11)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(2, ret)
	testObj:TestEvent1('+', EvtFunc12)
	local ret = testObj:CallEvent(2)
	ASSERT_EQ(7, ret)
	testObj:TestEvent1('+', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(12, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(15, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(16, ret)
	testObj:TestEvent1('-', EvtFunc12)
	local ret = testObj:CallEvent(1)
	ASSERT_EQ(17, ret)
	testObj:TestEvent1('-', EvtFunc11)
end

function CMyTestCaseLuaCallCS.CaseEventStatic(self)
	self.count = 1 + self.count

	gTestNumber = 1
	CS.LuaTestObj.TestStaticEvent1('+', EvtFunc11)
	local ret = CS.LuaTestObj.CallStaticEvent(1)
	ASSERT_EQ(2, ret)
	CS.LuaTestObj.TestStaticEvent1('+', EvtFunc12)
	local ret = CS.LuaTestObj.CallStaticEvent(2)
	ASSERT_EQ(7, ret)
	CS.LuaTestObj.TestStaticEvent1('+', EvtFunc12)
	local ret = CS.LuaTestObj.CallStaticEvent(1)
	ASSERT_EQ(12, ret)
	CS.LuaTestObj.TestStaticEvent1('-', EvtFunc12)
	local ret = CS.LuaTestObj.CallStaticEvent(1)
	ASSERT_EQ(15, ret)
	CS.LuaTestObj.TestStaticEvent1('-', EvtFunc12)
	local ret = CS.LuaTestObj.CallStaticEvent(1)
	ASSERT_EQ(16, ret)
	CS.LuaTestObj.TestStaticEvent1('-', EvtFunc12)
	local ret = CS.LuaTestObj.CallStaticEvent(1)
	ASSERT_EQ(17, ret)
	CS.LuaTestObj.TestStaticEvent1('-', EvtFunc11)
end

function CMyTestCaseLuaCallCS.CaseUpLowerMethod(self)
	self.count = 1 + self.count
	CS.LuaTestObj.initNumber = 5
	local ret = CS.LuaTestObj.CalcAdd(1)
	ASSERT_EQ(6, ret)
	
	local ret = CS.LuaTestObj.calcadd(2)
	ASSERT_EQ(8, ret)
end

function CMyTestCaseLuaCallCS.CaseUpLowerMethod(self)
	self.count = 1 + self.count

	local ret = CS.LuaTestObj.OverLoad1(1, 2, 3, 5)
	ASSERT_EQ(11, ret)
end

function CMyTestCaseLuaCallCS.CaseVisitStaticPusherStruct_1(self)
	self.count = 1 + self.count
    local struct_1 = CS.StaticPusherStructA(97, -100)
	local struct_2 = CS.StaticPusherStructB(-32525, 65535, -2147483647, 4294967295)
	ASSERT_EQ(struct_1.byteVar, 97)
	ASSERT_EQ(struct_1.sbyteVar, -100)
	ASSERT_EQ(struct_2.shortVar, -32525)
	ASSERT_EQ(struct_2.ushortVar, 65535)
	ASSERT_EQ(struct_2.intVar, -2147483647)
	ASSERT_EQ(struct_2.uintVar, 4294967295)
	local struct_all = CS.StaticPusherStructAll()
	struct_all.structA = struct_1
	struct_all.structB = struct_2
	struct_all.longVar = -4294967297
	struct_all.ulongVar = 4294967297
	struct_all.floatVar = 1.123456
	struct_all.doubleVar = 1.7976931348623157
	ASSERT_EQ(struct_all.structA.byteVar, 97)
	ASSERT_EQ(struct_all.structA.sbyteVar, -100)
	ASSERT_EQ(struct_all.structB.shortVar, -32525)
	ASSERT_EQ(struct_all.structB.ushortVar, 65535)
	ASSERT_EQ(struct_all.structB.intVar, -2147483647)
	ASSERT_EQ(struct_all.structB.uintVar, 4294967295)
	ASSERT_EQ(tostring(struct_all.longVar + 4294967297), "0")
	ASSERT_EQ(tostring(struct_all.ulongVar), ULONG_TOSTRING("4294967297"))
	ASSERT_EQ(string.sub(tostring(struct_all.floatVar), 1, 8), "1.123456")
	ASSERT_EQ(string.sub(tostring(struct_all.doubleVar), 1, 14), "1.797693134862")
	
	local struct_all_2 = CS.StaticPusherStructAll()
	struct_all_2.structA =  CS.StaticPusherStructA(0, 100)
	struct_all_2.structB = CS.StaticPusherStructB(-1, 2, 214748364, 429496729)
	struct_all_2.longVar = 4294967298
	struct_all_2.ulongVar = 4294967297
	struct_all_2.floatVar = 2.12345
	struct_all_2.doubleVar = 2.7976931348
	local class_1 = CS.NoGenCodeBaseClass()
	local class_2 = CS.GenCodeBaseClass()
	
	local ret1, ret2 = class_1:SetStaticPusherStruct(struct_all_2, struct_all)
	print("test ok")
	ASSERT_EQ(struct_all.structA.byteVar, 98)
	ASSERT_EQ(struct_all.structA.sbyteVar, 101)
	ASSERT_EQ(struct_all.structB.shortVar, -1)
	ASSERT_EQ(struct_all.structB.ushortVar, 2)
	ASSERT_EQ(struct_all.structB.intVar, -2147483646)
	ASSERT_EQ(struct_all.structB.uintVar, 0)
	ASSERT_EQ(tostring(struct_all.longVar + 4294967298), "0")
	ASSERT_EQ(tostring(struct_all.ulongVar), ULONG_TOSTRING("4294967296"))
	ASSERT_EQ(string.sub(tostring(struct_all.floatVar), 1, 8), "0.123456")
	ASSERT_EQ(string.sub(tostring(struct_all.doubleVar), 1, 14), "0.797693134862")
	ASSERT_EQ(ret1.structA.byteVar, 98)
	ASSERT_EQ(ret1.structA.sbyteVar, 101)
	ASSERT_EQ(ret1.structB.shortVar, -1)
	ASSERT_EQ(ret1.structB.ushortVar, 2)
	ASSERT_EQ(ret1.structB.intVar, -2147483646)
	ASSERT_EQ(ret1.structB.uintVar, 0)
	ASSERT_EQ(tostring(ret1.longVar + 4294967298), "0")
	ASSERT_EQ(tostring(ret1.ulongVar), ULONG_TOSTRING("4294967296"))
	ASSERT_EQ(string.sub(tostring(ret1.floatVar), 1, 8), "0.123456")
	ASSERT_EQ(string.sub(tostring(ret1.doubleVar), 1, 14), "0.797693134862")
	local ret3 = class_1.static_pushstruct_var;
	ASSERT_EQ(ret3.structA.byteVar, 10)
	ASSERT_EQ(ret3.structA.sbyteVar, 100)
	ASSERT_EQ(ret3.structB.shortVar, -32525)
	ASSERT_EQ(ret3.structB.ushortVar, 2)
	ASSERT_EQ(ret3.structB.intVar, -1932735283)
	ASSERT_EQ(ret3.structB.uintVar, 429496728)
	ASSERT_EQ(tostring(ret3.longVar), "1")
	ASSERT_EQ(tostring(ret3.ulongVar), ULONG_TOSTRING("8589934594"))
	ASSERT_EQ(string.sub(tostring(ret3.floatVar), 1, 8), "3.246906")
	ASSERT_EQ(string.sub(tostring(ret3.doubleVar), 1, 14), "4.595386269662")
	ASSERT_EQ(ret2.structA.byteVar, 10)
	ASSERT_EQ(ret2.structA.sbyteVar, 100)
	ASSERT_EQ(ret2.structB.shortVar, -32525)
	ASSERT_EQ(ret2.structB.ushortVar, 2)
	ASSERT_EQ(ret2.structB.intVar, -1932735283)
	ASSERT_EQ(ret2.structB.uintVar, 429496728)
	ASSERT_EQ(tostring(ret2.longVar), "1")
	ASSERT_EQ(tostring(ret2.ulongVar), ULONG_TOSTRING("8589934594"))
	ASSERT_EQ(string.sub(tostring(ret2.floatVar), 1, 8), "3.246906")
	ASSERT_EQ(string.sub(tostring(ret2.doubleVar), 1, 14), "4.595386269662")
	
	
	local ret1, ret2 = class_2:SetStaticPusherStruct(struct_all_2, struct_all)
	print("test ok")
	ASSERT_EQ(struct_all.structA.byteVar, 98)
	ASSERT_EQ(struct_all.structA.sbyteVar, 101)
	ASSERT_EQ(struct_all.structB.shortVar, -1)
	ASSERT_EQ(struct_all.structB.ushortVar, 2)
	ASSERT_EQ(struct_all.structB.intVar, -2147483645)
	ASSERT_EQ(struct_all.structB.uintVar, 1)
	ASSERT_EQ(tostring(struct_all.longVar + 4294967299), "0")
	ASSERT_EQ(tostring(struct_all.ulongVar), ULONG_TOSTRING("4294967295"))
	ASSERT_EQ(string.sub(tostring(struct_all.floatVar), 1, 8), "-0.87654")
	ASSERT_EQ(string.sub(tostring(struct_all.doubleVar), 1, 14), "-0.20230686513")
	ASSERT_EQ(ret1.structA.byteVar, 98)
	ASSERT_EQ(ret1.structA.sbyteVar, 101)
	ASSERT_EQ(ret1.structB.shortVar, -1)
	ASSERT_EQ(ret1.structB.ushortVar, 2)
	ASSERT_EQ(ret1.structB.intVar, -2147483645)
	ASSERT_EQ(ret1.structB.uintVar, 1)
	ASSERT_EQ(tostring(ret1.longVar + 4294967299), "0")
	ASSERT_EQ(tostring(ret1.ulongVar), ULONG_TOSTRING("4294967295"))
	ASSERT_EQ(string.sub(tostring(ret1.floatVar), 1, 8), "-0.87654")
	ASSERT_EQ(string.sub(tostring(ret1.doubleVar), 1, 14), "-0.20230686513")
	local ret3 = class_2.static_pushstruct_var;
	ASSERT_EQ(ret3.structA.byteVar, 97)
	ASSERT_EQ(ret3.structA.sbyteVar, 100)
	ASSERT_EQ(ret3.structB.shortVar, -1)
	ASSERT_EQ(ret3.structB.ushortVar, 2)
	ASSERT_EQ(ret3.structB.intVar, -1932735282)
	ASSERT_EQ(ret3.structB.uintVar, 429496729)
	ASSERT_EQ(tostring(ret3.longVar), "0")
	ASSERT_EQ(tostring(ret3.ulongVar), ULONG_TOSTRING("8589934593"))
	ASSERT_EQ(string.sub(tostring(ret3.floatVar), 1, 8), "2.246906")
	ASSERT_EQ(string.sub(tostring(ret3.doubleVar), 1, 14), "3.595386269662")
	ASSERT_EQ(ret2.structA.byteVar, 97)
	ASSERT_EQ(ret2.structA.sbyteVar, 100)
	ASSERT_EQ(ret2.structB.shortVar, -1)
	ASSERT_EQ(ret2.structB.ushortVar, 2)
	ASSERT_EQ(ret2.structB.intVar, -1932735282)
	ASSERT_EQ(ret2.structB.uintVar, 429496729)
	ASSERT_EQ(tostring(ret2.longVar), "0")
	ASSERT_EQ(tostring(ret2.ulongVar), ULONG_TOSTRING("8589934593"))
	ASSERT_EQ(string.sub(tostring(ret2.floatVar), 1, 8), "2.246906")
	ASSERT_EQ(string.sub(tostring(ret2.doubleVar), 1, 14), "3.595386269662")
end

function CMyTestCaseLuaCallCS.CaseGetStaticSum(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.Sum(1, 2)
	ASSERT_EQ(ret, 3)
end

function CMyTestCaseLuaCallCS.CaseGetSum(self)
    self.count = 1 + self.count
	local class = CS.LuaTestObj()
	local ret = class:Sum(1, 2, 6)
	ASSERT_EQ(ret, 9)
end

function CMyTestCaseLuaCallCS.CaseVisitTemplateMethod(self)
    self.count = 1 + self.count
	ASSERT_EQ(CS.EmployeeTemplate.GetBasicSalary, nil)
	ASSERT_EQ(CS.EmployeeTemplate.AddBonus, nil)
	local class = CS.Manager()
	ASSERT_EQ(1, class:GetBasicSalary())   
end

function CMyTestCaseLuaCallCS.CaseVisitGenericMethod(self)
	self.count = 1 + self.count
	local class = CS.LuaTestObj()
	local a = "abc"
	if (true) then
		local ret, error = pcall(function() class:GenericMethod(a) end)
		ASSERT_EQ(ret, false)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseVisitIntPtr(self)
    self.count = 1 + self.count
	local class = CS.LuaTestObj()
	local ptr = class.ptr
	print(ptr)
	local ptr1 = class:GetPtr()
    print(ptr1)
    local bytevar = class:PrintPtr(ptr1)
	ASSERT_EQ(bytevar, 97)
end

function CMyTestCaseLuaCallCS.CaseVisitVarAndDefaultFunc1(self)
    self.count = 1 + self.count
	local class = CS.LuaTestObj()

	local ret = class:VariableParamFuncDefault(1)
	ASSERT_EQ(ret, 2)
	
	local ret = CS.LuaTestObj.StaticVariableParamFuncDefault(1.0)
	ASSERT_EQ(ret, 2.0)
end

function CMyTestCaseLuaCallCS.CaseVisitVarAndDefaultFunc2(self)
    self.count = 1 + self.count
	local class = CS.LuaTestObj()

	local ret = class:VariableParamFuncDefault(1, 2, "john", "che")
	ASSERT_EQ(ret, 3)
	
	local ret = CS.LuaTestObj.StaticVariableParamFuncDefault(1.0, 2.0, "john", "che")
	ASSERT_EQ(ret, 3.0)
end

function CMyTestCaseLuaCallCS.CaseFuncReturnByteArray(self)
    self.count = 1 + self.count

	local ret = CS.LuaTestObj.FuncReturnByteArray()
	ASSERT_EQ(ret, "abc")
end

function CMyTestCaseLuaCallCS.CaseFuncReturnByte(self)
    self.count = 1 + self.count

	local ret = CS.LuaTestObj.FuncReturnByte()
	ASSERT_EQ(ret, 97)
end

function CMyTestCaseLuaCallCS.CaseFuncReturnIntArray(self)
    self.count = 1 + self.count

	local ret = CS.LuaTestObj.FuncReturnIntArray()
	ASSERT_EQ(type(ret), "userdata")
end

function CMyTestCaseLuaCallCS.CaseFuncReturnInt(self)
    self.count = 1 + self.count
	
	local ret = CS.LuaTestObj.FuncReturnInt()
	ASSERT_EQ(ret, 97)
end

function CMyTestCaseLuaCallCS.CaseFuncUint64Tostring(self)
    self.count = 1 + self.count
	if not UINT64_BUILT() then return end
	local uint64Var = 1234567890
	local ret = uint64.tostring(uint64Var)
	ASSERT_EQ(ret, "1234567890")
	local ret = uint64.tostring(CS.LongStatic.ULONG_MAX)
	ASSERT_EQ(ret, "18446744073709551615")
	local ret = uint64.tostring(0)
	ASSERT_EQ(ret, "0")
	local ret = uint64.tostring(CS.LongStatic.LONG_MAX)
	ASSERT_EQ(ret, "9223372036854775807")
	local ret = uint64.tostring(-1)
	ASSERT_EQ(ret, uint64.tostring(CS.LongStatic.ULONG_MAX))
end

function CMyTestCaseLuaCallCS.CaseFuncUint64divide(self)
    self.count = 1 + self.count
	if not UINT64_BUILT() then return end
	local uint64Var = 1234567890
	local ret = uint64.divide(uint64Var, 10)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("123456789"))
	local ret = uint64.divide(CS.LongStatic.ULONG_MAX, 10)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("1844674407370955161"))
	local ret = uint64.divide(CS.LongStatic.LONG_MAX, 100)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("92233720368547758"))
	local ret = uint64.divide(CS.LongStatic.ULONG_MAX, -1)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("1"))
	local ret = uint64.divide(0, CS.LongStatic.ULONG_MAX)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("0"))
	if (true) then
		local ret, error = pcall(function() uint64.divide(CS.LongStatic.ULONG_MAX, 0) end)
		ASSERT_EQ(ret, false)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseFuncUint64compare(self)
    self.count = 1 + self.count
	if not UINT64_BUILT() then return end
	local uint64Var = 1234567890
	local ret = uint64.compare(uint64Var, uint64Var)
	ASSERT_EQ(ret, 0)
	local ret = uint64.compare(CS.LongStatic.ULONG_MAX, uint64Var)
	ASSERT_EQ(ret, 1)
	local ret = uint64.compare(CS.LongStatic.LONG_MAX, CS.LongStatic.LONG_MAX)
	ASSERT_EQ(ret, 0)
	local ret = uint64.compare(CS.LongStatic.ULONG_MAX, -1)
	ASSERT_EQ(ret, 0)
	local ret = uint64.compare(0, CS.LongStatic.ULONG_MAX)
	ASSERT_EQ(ret, -1)
end

function CMyTestCaseLuaCallCS.CaseFuncUint64remainder(self)
    self.count = 1 + self.count
	if not UINT64_BUILT() then return end
	local uint64Var = 1234567890
	local ret = uint64.remainder(uint64Var, uint64Var)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("0"))
	local ret = uint64.remainder(CS.LongStatic.ULONG_MAX, uint64Var)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("834183465"))
	local ret = uint64.remainder(CS.LongStatic.LONG_MAX, CS.LongStatic.LONG_MAX)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("0"))
	local ret = uint64.remainder(CS.LongStatic.ULONG_MAX, -1)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("0"))
	local ret = uint64.remainder(0, CS.LongStatic.ULONG_MAX)
	ASSERT_EQ(tostring(ret), ULONG_TOSTRING("0"))
	if (true) then
		local ret, error = pcall(function() uint64.remainder(CS.LongStatic.ULONG_MAX, 0) end)
		ASSERT_EQ(ret, false)
    else
        ASSERT_EQ(true, false)
	end
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransSimpleClassMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:SimpleClassMethod({x=97, y="123", z=100000000000})
	ASSERT_EQ(ret.x, 97)
	ASSERT_EQ(ret.y, "123")
	ASSERT_EQ(tostring(ret.z), "100000000000")
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransComplexClassMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:ComplexClassMethod({A=97, B={x=97, y="123", z=100000000000}})
	ASSERT_EQ(ret.IntVar, 97)
	ASSERT_EQ(ret.ClassVar.IntVar, 97)
	ASSERT_EQ(ret.ClassVar.StringVar, "123")
	ASSERT_EQ(tostring(ret.ClassVar.LongVar), "100000000000")
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransSimpleStructMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:SimpleStructMethod({a=97})
	ASSERT_EQ(ret.a, 97)
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransComplexStructMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:ComplexStructMethod({a=-101, b=1000, c=1.000000132, d={a=97}})
	ASSERT_EQ(ret.a, -101)
	ASSERT_EQ(ret.b, 1000)
	ASSERT_EQ(string.sub(tostring(ret.c), 1, 11), "1.000000132")
	ASSERT_EQ(ret.d.a, 97)
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransOneListMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:OneListMethod({1, 2, 3, 4, 5})
	ASSERT_EQ(ret, 15)
end

function CMyTestCaseLuaCallCS.CaseTableAutoTransTwoDimensionListMethod(self)
    self.count = 1 + self.count
	local class = CS.TestTableAutoTransClass()
	local ret = class:TwoDimensionListMethod({{1, 2, 3},{4, 5, 6}})
	ASSERT_EQ(ret, 21)
end

function CMyTestCaseLuaCallCS.CaseTestImplicit(self)
	self.count = 1 + self.count
    if CS.LuaTestCommon.IsXLuaGeneral() then return end
	local ret = CS.LuaTestObj.TestImplicit():GetType()
	ASSERT_EQ(ret, typeof(CS.UnityEngine.LayerMask))
end

function CMyTestCaseLuaCallCS.CaseVariableParamFunc2_1_4(self)
    self.count = 1 + self.count
	local ret, err = pcall(function() CS.LuaTestObj.VariableParamFunc(0, CS.LuaTestObj()) end)
	ASSERT_EQ(ret, false)
	ASSERT_TRUE(err:find("invalid arguments"))
end

function CMyTestCaseLuaCallCS.CaseFirstPushEnum(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.FirstPushEnumFunc(1)
	ASSERT_EQ(ret, "1")
	local ret = CS.LuaTestObj.FirstPushEnumFunc(2)
	ASSERT_EQ(ret, "4")
end

function CMyTestCaseLuaCallCS.CaseReferTestClass(self)
	self.count = 1 + self.count
	local int_x = 10
	local int_y = 12
	local str_z = "abc"
	local class1, ret_y, ret_z = CS.ReferTestClass(int_x, int_y)
	local ret = class1:Get_X_Y_ADD()
	ASSERT_EQ(ret, 22)
	ASSERT_EQ(ret_y, 11)
	ASSERT_EQ(ret_z, "test1")

	local class3, ret_z = CS.ReferTestClass(int_x)
	local ret = class3:Get_X_Y_ADD()
	ASSERT_EQ(ret, 20)
	ASSERT_EQ(ret_z, "test3")
end

function CMyTestCaseLuaCallCS.CaseVariableParamFuncNoParam(self)
    self.count = 1 + self.count
	local ret = CS.LuaTestObj.VariableParamFunc2()
	ASSERT_EQ(ret, 0)
	local ret = CS.LuaTestObj.VariableParamFunc2("abc", "haha")
	ASSERT_EQ(ret, 2)
end
function CMyTestCaseLuaCallCS.CaseGenAccessorRejectsNonStruct(self)
    self.count = 1 + self.count
	local obj = CS.System.Object()
	local d = CS.System.Convert.ToDecimal(1)
	local get0, set0 = xlua.genaccessor(0, 4)
	ASSERT_EQ(type(get0(d)), "number")
	ASSERT_EQ(pcall(xlua.genaccessor(16, 4), d), false)
	ASSERT_EQ(pcall(get0, obj), false)
	ASSERT_EQ(pcall(set0, 42, 1), false)
	-- The profiler hook is stored under a light userdata key whose first int
	-- is -1, the CSharpStruct marker; accessors must still reject it.
	local hook = function() end
	xlua.sethook(hook)
	local lud
	for k, v in pairs(debug.getregistry()) do
		if v == hook then lud = k end
	end
	xlua.sethook(nil)
	ASSERT_EQ(type(lud), "userdata")
	ASSERT_EQ(pcall(get0, lud), false)
	ASSERT_EQ(pcall(set0, lud, 1), false)
end

-- Struct field setters take numbers and numeric strings; any other value
-- raises and leaves the field unchanged (upstream stored 0). A decimal is a
-- 16-byte struct userdata in every binding mode.
function CMyTestCaseLuaCallCS.CaseStructSetterRejectsNonNumber(self)
    self.count = 1 + self.count
	local d = CS.System.Convert.ToDecimal(1)
	local get32, set32 = xlua.genaccessor(0, 4)
	local get8, set8 = xlua.genaccessor(4, 1)
	local get64, set64 = xlua.genaccessor(8, 6)
	local getd, setd = xlua.genaccessor(8, 9)
	set32(d, 7)
	ASSERT_EQ(get32(d), 7)
	set32(d, "12")
	ASSERT_EQ(get32(d), 12)
	set32(d, 3.0)
	ASSERT_EQ(get32(d), 3)
	set8(d, "255")
	ASSERT_EQ(get8(d), 255)
	setd(d, "0.25")
	ASSERT_EQ(getd(d), 0.25)
	setd(d, 1.5)
	ASSERT_EQ(getd(d), 1.5)
	-- On LuaJIT a C# long is an int64 userdata; on Lua 5.3+ an integer.
	set64(d, CS.LongStatic.LONG_MAX)
	ASSERT_EQ(tostring(get64(d)), tostring(CS.LongStatic.LONG_MAX))
	set64(d, "-5")
	ASSERT_EQ(tostring(get64(d)), "-5")
	for _, set in ipairs({set32, set8, set64, setd}) do
		for _, bad in ipairs({false, true, {}, "x", "12abc", print, CS.System.Object()}) do
			local ok, err = pcall(set, d, bad)
			ASSERT_EQ(ok, false)
			ASSERT_EQ(string.find(tostring(err), "number expected", 1, true) ~= nil, true)
		end
		local ok, err = pcall(set, d, nil)
		ASSERT_EQ(ok, false)
		ASSERT_EQ(string.find(tostring(err), "number expected, got nil", 1, true) ~= nil, true)
	end
	ASSERT_EQ(get32(d), 3)
	ASSERT_EQ(get8(d), 255)
	ASSERT_EQ(tostring(get64(d)), "-5")
end

-- Integer struct fields follow the rule of CaseIntegerArgumentRule: a value
-- without an integer representation raises and leaves the field unchanged.
function CMyTestCaseLuaCallCS.CaseStructSetterRejectsFraction(self)
    self.count = 1 + self.count
	local d = CS.System.Convert.ToDecimal(1)
	local get32, set32 = xlua.genaccessor(0, 4)
	local getu, setu = xlua.genaccessor(4, 5)
	local get64, set64 = xlua.genaccessor(8, 6)
	local getd, setd = xlua.genaccessor(8, 9)
	set32(d, 7.0)
	setu(d, "4294967295")
	ASSERT_EQ(get32(d), 7)
	ASSERT_EQ(getu(d), 4294967295)
	for _, bad in ipairs({2.75, -0.5, "2.75", 0/0, 1/0, 2^63}) do
		for _, set in ipairs({set32, setu, set64}) do
			local ok, err = pcall(set, d, bad)
			ASSERT_EQ(ok, false)
			ASSERT_EQ(string.find(tostring(err), "bad argument #2", 1, true) ~= nil, true)
			ASSERT_EQ(string.find(tostring(err), "number has no integer representation", 1, true) ~= nil, true)
		end
	end
	ASSERT_EQ(get32(d), 7)
	ASSERT_EQ(getu(d), 4294967295)
	setd(d, 2.75)
	ASSERT_EQ(getd(d), 2.75)
end

-- On LuaJIT an int64 is a userdata whose first int is -1, the CSharpStruct
-- marker. The next four bytes, where a struct keeps its length, were the type
-- byte and uninitialised padding, so struct accessors could treat an int64 as
-- a struct of any length and read or write past its 16 bytes.
function CMyTestCaseLuaCallCS.CaseStructAccessorRejectsInt64(self)
    self.count = 1 + self.count
	if type(CS.LongStatic.LONG_MAX) ~= "userdata" then return end -- Lua 5.3+: plain integers
	-- Leave freed blocks of the int64 userdata size full of 0xFF bytes, so
	-- uninitialised padding in the int64 values below is not zero by chance.
	local junk = {}
	for i = 1, 4000 do
		junk[i] = string.char(1 + i % 255, 1 + math.floor(i / 255) % 255) .. string.rep("\255", 24 + i % 32)
	end
	junk = nil
	collectgarbage("collect")
	local values = {}
	for i = 1, 200 do
		values[#values + 1] = CS.LongStatic.LONG_MAX
		values[#values + 1] = CS.LongStatic.ULONG_MAX
	end
	local get, set = xlua.genaccessor(0, 4)  -- int32 at offset 0
	local getd = xlua.genaccessor(0, 9)      -- double at offset 0
	for _, v in ipairs(values) do
		ASSERT_EQ(pcall(get, v), false)
		ASSERT_EQ(pcall(set, v, 1), false)
		ASSERT_EQ(pcall(getd, v), false)
	end
	ASSERT_EQ(tostring(values[1]), tostring(CS.LongStatic.LONG_MAX))
	-- A GCOptimize struct field getter on an int64 given the struct type's
	-- metatable (xlua.cast no longer accepts an int64, so debug.setmetatable).
	-- Generated code packs the struct into a CSharpStruct userdata; with
	-- reflection it is a boxed object, and the int64 is not a struct.
	local s4 = CS.Gen4FloatStruct(1, 2, 3, 4)
	local packed = pcall(getd, s4)
	if packed then
		for i = 1, 20 do
			local v = CS.LongStatic.LONG_MAX
			debug.setmetatable(v, debug.getmetatable(s4))
			ASSERT_EQ(pcall(function() return v.a end), false)
			ASSERT_EQ(pcall(function() v.d = 1 end), false)
		end
	end
end

-- C# callbacks are C closures whose first upvalue is the C function to call.
-- debug.setupvalue can replace it; the wrapper then called a NULL pointer.
function CMyTestCaseLuaCallCS.CaseCSharpFunctionUpvalueReplaced(self)
    self.count = 1 + self.count
	if not debug.setupvalue then return end -- release players reduce debug (XLUA_REDUCED_DEBUG)
	local f = CS.StackDumpHelpers.GetDump()
	ASSERT_EQ(type(f), "function")
	ASSERT_EQ(select(2, f(1)), true)
	ASSERT_EQ(debug.setupvalue(f, 1, "not a function"), "")
	local ok, err = pcall(f, 1)
	ASSERT_EQ(ok, false)
	ASSERT_TRUE(string.find(tostring(err), "not a C function", 1, true) ~= nil)
	ASSERT_EQ(debug.setupvalue(f, 1, {}), "")
	ASSERT_EQ(pcall(f, 1), false)
	-- Other closures of the same C# function are separate and still work.
	ASSERT_EQ(select(2, CS.StackDumpHelpers.GetDump()(1)), true)
end

-- xlua.cast gives a C# object the metatable of a type it is an instance of
-- (its own type, a base class or an interface), to use that type's wrappers.
-- It used to set the metatable on any value: a table, a struct, an int64, or
-- every number or boolean at once.
function CMyTestCaseLuaCallCS.CaseCastRejectsInvalid(self)
    self.count = 1 + self.count
	if not debug.getmetatable then return end -- release players reduce debug (XLUA_REDUCED_DEBUG)
	-- A number or boolean metatable is shared by every value of that type
	-- (on Lua 5.3+ an int64 is a number); restored below if a cast set it.
	local number_mt, boolean_mt = debug.getmetatable(1), debug.getmetatable(true)
	local sb = CS.System.Text.StringBuilder("ab")
	cast(sb, typeof(CS.System.Object))
	ASSERT_EQ(sb:ToString(), "ab")
	local impl = CS.LuaTestObj.CreateTestLuaObj()
	xlua.getmetatable(CS.ITestLuaClass) -- loads the interface's wrapper with reflection
	cast(impl, typeof(CS.ITestLuaClass))
	ASSERT_EQ(impl:TestFunc1(), true)
	local function rejected(v, t)
		local ok, err = pcall(cast, v, t)
		ASSERT_EQ(ok, false)
		ASSERT_TRUE(string.find(tostring(err), "xlua.cast", 1, true) ~= nil)
	end
	local tbl = {}
	rejected(tbl, typeof(CS.TestCastClass))
	ASSERT_EQ(getmetatable(tbl), nil)
	rejected(CS.TestCastClass, typeof(CS.TestCastClass))
	rejected(CS.System.Text.StringBuilder("x"), typeof(CS.TestCastClass))
	rejected(CS.LongStatic.LONG_MAX, typeof(CS.TestCastClass))
	rejected(CS.Gen4FloatStruct(1, 2, 3, 4), typeof(CS.TestCastClass))
	rejected(nil, typeof(CS.TestCastClass))
	rejected(1, typeof(CS.TestCastClass))
	rejected(true, typeof(CS.TestCastClass))
	local number_ok, boolean_ok = debug.getmetatable(1) == number_mt, debug.getmetatable(true) == boolean_mt
	debug.setmetatable(1, number_mt)
	debug.setmetatable(true, boolean_mt)
	ASSERT_EQ(number_ok, true)
	ASSERT_EQ(boolean_ok, true)
	-- Valid casts keep working.
	local castObj = CS.TestCastClass()
	cast(castObj, typeof(CS.TestCastClass))
	ASSERT_EQ(castObj:TestFunc1(), true)
end

-- xlua.setmetatable (xlua.metatable_operation) replaces a type's metatable. The
-- new table used to lack the bridge tag and __gc, so later objects of the
-- type were not recognised as C# objects and their pool slots never freed.
function CMyTestCaseLuaCallCS.CaseMetatableOperationSetKeepsBridge(self)
    self.count = 1 + self.count
	if not debug.getmetatable then return end -- release players reduce debug (XLUA_REDUCED_DEBUG)
	local T = CS.MetatableOperationProbe
	T.Reset()
	local old = xlua.getmetatable(T)
	local mt = {}
	for k, v in pairs(old) do
		-- Keep the user-facing fields; leave out the tag (a light userdata key),
		-- the type id at [1] and __gc.
		if type(k) == "string" and k ~= "__gc" then mt[k] = v end
	end
	xlua.setmetatable(T, mt)
	local function use(i)
		local o = T(i)
		ASSERT_TRUE(rawequal(debug.getmetatable(o), mt))
		ASSERT_EQ(o:Twice(), 2 * i)
		ASSERT_EQ(o.Value, i)
		ASSERT_EQ(T.IsProbe(o), true)
	end
	for i = 1, 10 do use(i) end
	collectgarbage("collect")
	collectgarbage("collect")
	local alive = T.AliveCount()
	xlua.setmetatable(T, old)
	-- Without the fix all 10 stay alive. Mono scans native stacks conservatively,
	-- so one object can stay reachable from a stale stack slot (seen on Linux CI).
	ASSERT_TRUE(alive <= 1)
	-- The bridge copied its finalizer into the table the script passed.
	ASSERT_EQ(rawget(mt, "__gc") ~= nil, true)
end

-- XLUA_LOCK_METATABLES: bridge metatables carry __metatable, so getmetatable
-- returns a placeholder and the finalizer cannot be reached from a script.
-- Without the define nothing changes. The raw path (debug.getmetatable) works
-- in both builds, so xLua's own Lua code keeps working.
local function BridgeUserdata()
	CS.LuaTestObj.GenDelegate()
	local values = {
		generated = CS.LuaTestObj(),
		reflected = CS.System.Text.StringBuilder("ab"),
		array = CS.System.Array.CreateInstance(typeof(CS.System.Int32), 2),
		delegate = CS.LuaTestObj.csDelegate1,
		enum = CS.LuaTestType.DEF,
		type = typeof(CS.System.String),
		decimal = CS.System.Convert.ToDecimal(1),
		struct = CS.NativeCoverageHelpers.MakeStructA(7, -3),
	}
	-- LuaJIT keeps 64-bit integers in userdata with their own metatable.
	if type(CS.LongStatic.LONG_MAX) == "userdata" then
		values.int64 = CS.LongStatic.LONG_MAX
		values.uint64 = CS.LongStatic.ULONG_MAX
	end
	return values
end

function CMyTestCaseLuaCallCS.CaseMetatableLockUserdata(self)
    self.count = 1 + self.count
	local locked = CS.LuaTestCommon.LockMetatables()
	for name, v in pairs(BridgeUserdata()) do
		ASSERT_EQ(type(v), "userdata")
		local raw = debug.getmetatable(v)
		ASSERT_EQ(type(raw), "table")
		if locked then
			ASSERT_EQ(getmetatable(v), "locked")
			ASSERT_EQ(rawget(raw, "__metatable"), "locked")
			-- The placeholder is a string: it has no __gc to call.
			ASSERT_EQ(pcall(function() return getmetatable(v).__gc(v) end), false)
		else
			ASSERT_TRUE(rawequal(getmetatable(v), raw))
			ASSERT_EQ(rawget(raw, "__metatable"), nil)
		end
	end
	-- The objects still work after the checks above.
	local values = BridgeUserdata()
	ASSERT_EQ(values.reflected:ToString(), "ab")
	ASSERT_EQ(values.array.Length, 2)
	ASSERT_TRUE(string.find(tostring(values.delegate), "TestDelegate", 1, true) ~= nil)
	ASSERT_EQ(values.enum, CS.LuaTestType.DEF)
	ASSERT_EQ(string.sub(tostring(values.decimal), 1, 1), "1")
end

function CMyTestCaseLuaCallCS.CaseMetatableLockTables(self)
    self.count = 1 + self.count
	if not debug.getmetatable then return end -- release players reduce debug (XLUA_REDUCED_DEBUG)
	local locked = CS.LuaTestCommon.LockMetatables()
	local tables = {
		cs = CS,
		namespace = CS.System.Text,
		generated_class = CS.LuaTestObj,
		reflected_class = CS.System.Text.StringBuilder,
		enum_class = CS.LuaTestType,
		base_proxy = base(CS.LuaTestObj()),
	}
	-- Generated code packs this struct into a Lua table; reflection boxes it.
	local packed = CS.NativeCoverageHelpers.MakePackAsTable(3, 0.5)
	if type(packed) == "table" then tables.packed_struct = packed end
	for name, t in pairs(tables) do
		ASSERT_EQ(type(t), "table")
		ASSERT_EQ(type(debug.getmetatable(t)), "table")
		if locked then
			ASSERT_EQ(getmetatable(t), "locked")
			ASSERT_EQ(pcall(setmetatable, t, {}), false)
			ASSERT_EQ(pcall(setmetatable, t, nil), false)
		else
			ASSERT_TRUE(rawequal(getmetatable(t), debug.getmetatable(t)))
		end
	end
	-- Lookups through the locked metatables still work.
	ASSERT_EQ(CS.NativeCoverageHelpers.Twice(2), 4)
	ASSERT_EQ(CS.System.Text.StringBuilder("x"):ToString(), "x")
	ASSERT_TRUE(CS.LuaTestType.GHI ~= CS.LuaTestType.DEF)
end

function CMyTestCaseLuaCallCS.CaseMetatableLockKeepsUtilState(self)
    self.count = 1 + self.count
	-- xlua.util is embedded in xLua's C# (EmbeddedModules.cs); XLUA_NO_UTIL leaves it out.
	if CS.LuaTestCommon.NoUtil() then return end
	local util = require("xlua.util")
	-- state() reads the object's metatable with debug.getmetatable and replaces it
	-- with debug.setmetatable, so it works whether or not metatables are locked.
	local obj = CS.System.Text.StringBuilder("abc")
	util.state(obj, {extra = 1})
	ASSERT_EQ(obj.extra, 1)
	ASSERT_EQ(obj.Length, 3)
	obj.extra = 2
	ASSERT_EQ(obj.extra, 2)
	ASSERT_EQ(obj:ToString(), "abc")
	if CS.LuaTestCommon.LockMetatables() then
		ASSERT_EQ(getmetatable(obj), "locked")
	end
	-- createdelegate uses the enum | helper, which util reads with debug.getmetatable.
	local d = util.createdelegate(CS.TestDelegate, nil, CS.NativeCoverageHelpers, "Twice")
	ASSERT_EQ(d(21), 42)
end

-- C# feature defines (docs/MIGRATING.md, "Opt-in defines"). Each case checks the
-- behaviour of the define the suite was built with, so it passes with and without it.

-- XLUA_PRESET_MINIMAL implies the C# defines of the minimal column of
-- docs/design/presets.md (Tools/presets.py writes the expansion into each file
-- that tests one). The cases of each define then check its behaviour. Without
-- the preset define nothing is implied, and the case has nothing to check.
function CMyTestCaseLuaCallCS.CasePresetImpliesItsDefines(self)
    self.count = 1 + self.count
	local common, hardening = CS.LuaTestCommon, CS.BridgeHardening.Defines
	-- xLua's wrapper mode is the suite's: CSharpWrapper exists only with GEN_CODE_MINIMIZE,
	-- which the minimal preset implies unless XLUA_NO_GCM is defined.
	ASSERT_EQ(common.XLuaHasType("XLua.CSharpWrapper"), common.Gcm())
	if not common.PresetMinimal() then return end
	local implied = {
		StreamingAssetsLoader = common.NoStreamingAssetsLoader(),
		SignatureLoader = common.NoSignatureLoader(),
		-- The Editor keeps the template library, Development Builds the profiler.
		Template = common.NoTemplate() or common.InEditor(),
		Util = common.NoUtil() ~= common.UtilOptIn(),
		Profiler = common.NoProfiler() or common.InEditor() or common.DevelopmentBuild(),
		LoadAssembly = hardening.NoLoadAssembly(),
		LockMetatables = common.LockMetatables(),
		RestrictGetMetatable = hardening.RestrictGetMetatable(),
		LockXLuaTable = hardening.LockXLuaTable(),
		RestrictPrivateAccess = hardening.RestrictPrivateAccess(),
		ReducedDebug = common.ReducedDebug(),
		Gcm = common.Gcm() or common.NoGcm(),
		Reflection = common.NoReflection() ~= common.ReflectionOptIn(),
	}
	for name, on in pairs(implied) do
		ASSERT_EQ(name .. ":" .. tostring(on), name .. ":true")
	end
	-- The xLua assembly got the same expansion: it has no SignatureLoader, and
	-- without XLUA_REFLECTION no CodeEmit, which XLUA_GENERAL builds have otherwise.
	ASSERT_EQ(common.XLuaHasType("XLua.SignatureLoader"), false)
	if common.NoReflection() then
		ASSERT_EQ(common.XLuaHasType("XLua.CodeEmit"), false)
	end
end

-- XLUA_NO_REFLECTION (the minimal preset without XLUA_REFLECTION; fixtures in
-- NoReflectionForLuaCallCS.cs): a type without generated bindings keeps its class
-- table, typeof and its objects, but a member it declares raises an error that
-- names the type and the fix; the Editor (XLUA_GENERAL here) emits no bridges.
-- With reflection the same members work. Both states are checked.
local function noBindingsError(f, ...)
	local ok, err = pcall(f, ...)
	ASSERT_EQ(ok, false)
	return tostring(err)
end

local function has(text, part)
	ASSERT_EQ(text:find(part, 1, true) ~= nil and part or text, part)
end

function CMyTestCaseLuaCallCS.CaseNoReflectionUnboundType(self)
    self.count = 1 + self.count
	local api, common = CS.NoReflection.Api, CS.LuaTestCommon
	local Unbound = CS.NoReflection.Unbound
	-- The class table, typeof, nested types and objects exist either way.
	ASSERT_EQ(api.NameOf(typeof(Unbound)), "NoReflection.Unbound")
	ASSERT_EQ(api.NameOf(typeof(Unbound.Nested)), "NoReflection.Unbound+Nested")
	local u = api.MakeUnbound()
	ASSERT_EQ(api.IsUnbound(u), true)
	has(tostring(u), "NoReflection.Unbound")
	if not common.NoReflection() then
		ASSERT_EQ(u.Field, 1)
		ASSERT_EQ(u:Method(), 3)
		ASSERT_EQ(Unbound.StaticField, 2)
		ASSERT_EQ(Unbound.StaticMethod(), 4)
		ASSERT_EQ(Unbound().Field, 1)
		ASSERT_EQ(api.HasReflectionWrappers(), true)
		return
	end
	-- The reflection wrappers are compiled only for xlua.private_accessible.
	ASSERT_EQ(api.HasReflectionWrappers(), xlua.private_accessible ~= nil)
	local err = noBindingsError(function() return u.Field end)
	has(err, "cannot use NoReflection.Unbound.Field: NoReflection.Unbound has no generated bindings")
	has(err, "Mark it [LuaCallCSharp], or add [assembly: XLua.LuaCallCSharp(typeof(NoReflection.Unbound))]")
	has(err, "define XLUA_REFLECTION")
	has(noBindingsError(function() return u:Method() end), "cannot use NoReflection.Unbound.Method:")
	has(noBindingsError(function() u.Field = 2 end), "cannot set NoReflection.Unbound.Field:")
	has(noBindingsError(function() return Unbound.StaticField end), "cannot use NoReflection.Unbound.StaticField:")
	has(noBindingsError(function() return Unbound.StaticMethod() end), "cannot use NoReflection.Unbound.StaticMethod:")
	has(noBindingsError(function() Unbound.StaticField = 3 end), "cannot set NoReflection.Unbound.StaticField:")
	has(noBindingsError(function() return Unbound() end), "cannot construct NoReflection.Unbound:")
	has(noBindingsError(function() return Unbound.Nested().Inner end), "cannot construct NoReflection.Unbound+Nested:")
	-- A name the type does not declare finds nothing, as on a bound type.
	ASSERT_EQ(u.NoSuchMember, nil)
	ASSERT_EQ(Unbound.NoSuchMember, nil)
end

-- A type without bindings whose base class has them: the base class's members
-- work, the type's own do not.
function CMyTestCaseLuaCallCS.CaseNoReflectionBoundBase(self)
    self.count = 1 + self.count
	local d = CS.NoReflection.Api.MakeDerived()
	ASSERT_EQ(d.BaseValue, 6)
	ASSERT_EQ(d:BaseMethod(), 7)
	d.BaseValue = 9
	ASSERT_EQ(d.BaseValue, 9)
	if CS.LuaTestCommon.NoReflection() then
		has(noBindingsError(function() return d.Own end), "cannot use NoReflection.UnboundDerived.Own: NoReflection.UnboundDerived has no generated bindings")
	else
		ASSERT_EQ(d.Own, 8)
	end
end

-- An enum without bindings: its values pass through Lua and numbers convert to
-- it, but CS.E.Name and __CastFrom need bindings.
function CMyTestCaseLuaCallCS.CaseNoReflectionEnum(self)
    self.count = 1 + self.count
	local api = CS.NoReflection.Api
	local Kind = CS.NoReflection.UnboundKind
	ASSERT_EQ(api.KindValue(api.MakeKind()), 2)
	ASSERT_EQ(api.KindValue(1), 1)
	if CS.LuaTestCommon.NoReflection() then
		has(noBindingsError(function() return Kind.A end), "cannot use NoReflection.UnboundKind.A: NoReflection.UnboundKind has no generated bindings")
		has(noBindingsError(function() return Kind.__CastFrom(2) end), "cannot use NoReflection.UnboundKind.__CastFrom:")
	else
		ASSERT_EQ(api.KindValue(Kind.A), 1)
		ASSERT_EQ(Kind.__CastFrom(2), api.MakeKind())
	end
end

-- pairs over C# collections whose types and enumerators have no bindings: the
-- C# __pairs of XLUA_NO_REFLECTION gives what the Lua version gives (index from
-- 0 and element, or key and value). Called through the metatable, since
-- LuaJIT's pairs ignores __pairs.
function CMyTestCaseLuaCallCS.CaseNoReflectionPairs(self)
    self.count = 1 + self.count
	if not debug.getmetatable then return end -- release players reduce debug (XLUA_REDUCED_DEBUG)
	local api = CS.NoReflection.Api
	local function each(o)
		local found = {}
		for k, v in debug.getmetatable(o).__pairs(o) do found[#found + 1] = tostring(k) .. "=" .. tostring(v) end
		return found
	end
	local list = api.MakeList()
	ASSERT_EQ(table.concat(each(list), ","), "0=10,1=20,2=30")
	local pairsOfDictionary = each(api.MakeDictionary())
	table.sort(pairsOfDictionary)
	ASSERT_EQ(table.concat(pairsOfDictionary, ","), "a=1,b=2")
	if CS.LuaTestCommon.NoReflection() then
		has(noBindingsError(function() return list.Count end), "cannot use System.Collections.Generic.List`1[System.Int32].Count:")
	else
		ASSERT_EQ(list.Count, 3)
	end
end

-- A delegate or an interface without [CSharpCallLua] has no bridge: players
-- never emit one, and the Editor's CodeEmit (XLUA_GENERAL here) emits only for
-- [CSharpCallLua] types. XLUA_NO_REFLECTION leaves CodeEmit out, so the Editor
-- also needs generated bridges for those, and the error says how to get one.
function CMyTestCaseLuaCallCS.CaseNoReflectionBridges(self)
    self.count = 1 + self.count
	local api, strict = CS.NoReflection.Api, CS.LuaTestCommon.NoReflection()
	local interfaceError = noBindingsError(api.CallInterface, {Get = function() return 42 end})
	local delegateError = noBindingsError(api.CallDelegate, function(text) return text .. "!" end)
	has(interfaceError, "This type must add to CSharpCallLua: NoReflection.IUnbound")
	has(delegateError, "This type must add to CSharpCallLua: NoReflection.UnboundDelegate")
	ASSERT_EQ(interfaceError:find("Mark it [CSharpCallLua]", 1, true) ~= nil, strict)
	ASSERT_EQ(delegateError:find("Mark it [CSharpCallLua]", 1, true) ~= nil, strict)
	if strict then
		ASSERT_EQ(api.HasCodeEmit(), false)
	end
end

-- System.Object and System.Type have no bindings in the suite, as in a minimal
-- project that does not add them (owner decision 2026-10-28): tostring() works
-- through __tostring for objects and types, and without reflection the error for
-- their members says so.
function CMyTestCaseLuaCallCS.CaseNoReflectionObjectAndType(self)
    self.count = 1 + self.count
	local api = CS.NoReflection.Api
	local Unbound = CS.NoReflection.Unbound
	local u = api.MakeUnbound()
	ASSERT_EQ(tostring(u):match("^NoReflection%.Unbound: %-?%d+$") ~= nil, true)
	ASSERT_EQ(tostring(typeof(Unbound)):match("^NoReflection%.Unbound: %-?%d+$") ~= nil, true)
	if not CS.LuaTestCommon.NoReflection() then
		ASSERT_EQ(u:ToString(), "NoReflection.Unbound")
		ASSERT_EQ(typeof(Unbound).Name, "Unbound")
		return
	end
	local err = noBindingsError(function() return u:ToString() end)
	has(err, "cannot use System.Object.ToString: System.Object has no generated bindings")
	has(err, "tostring(obj) gives obj:ToString()")
	-- typeof gives a System.RuntimeType, which is not public; its Name or the
	-- one of a public base class.
	err = noBindingsError(function() return typeof(Unbound).Name end)
	has(err, ".Name: ")
	has(err, "tostring(typeof(CS.T)) gives the type's name")
	has(err, "[assembly: XLua.LuaCallCSharp(typeof(System.Type))]")
end

-- XLUA_NO_REFLECTION: a Lua table converts to a class or struct only through a
-- generated table converter (owner decision 2026-10-28, question 4 of
-- presets.md; fixtures TableApi and the *Class/*Struct types). A [GCOptimize]
-- type has one in every build; without one, a parameter, LuaTable.Get, Cast, a
-- delegate result and the elements of a list raise an error that names the type
-- and the fix. With reflection they fill the fields as before.
function CMyTestCaseLuaCallCS.CaseNoReflectionTableConversion(self)
    self.count = 1 + self.count
	local api = CS.NoReflection.TableApi
	local t = {A = 1, B = 2}
	ASSERT_EQ(api.SumConvertedClass(t), 3)
	ASSERT_EQ(api.SumConvertedStruct(t), 3)
	ASSERT_EQ(api.CastConvertedClass(t), 3)
	ASSERT_EQ(api.CastConvertedStruct(t), 3)
	ASSERT_EQ(api.SumConvertedStructs({t, {A = 3, B = 4}}), 10)
	-- A C# object of a type without a converter passes either way.
	ASSERT_EQ(api.SumClass(api.MakeClass()), 3)
	if not CS.LuaTestCommon.NoReflection() then
		ASSERT_EQ(api.SumClass(t), 3)
		ASSERT_EQ(api.SumStruct(t), 3)
		ASSERT_EQ(api.SumClasses({t, {A = 3, B = 4}}), 10)
		ASSERT_EQ(api.GetClass({v = t}), 3)
		ASSERT_EQ(api.CastClass(t), 3)
		ASSERT_EQ(api.ClassResult(function() return t end), 3)
		return
	end
	local err = noBindingsError(api.SumClass, t)
	has(err, "cannot convert a table to NoReflection.TableClass: NoReflection.TableClass has no generated table converter")
	has(err, "XLUA_NO_REFLECTION, part of XLUA_PRESET_MINIMAL")
	has(err, "Mark it [GCOptimize], or add [assembly: XLua.GCOptimize(typeof(NoReflection.TableClass))]")
	has(err, "define XLUA_REFLECTION")
	has(noBindingsError(api.SumStruct, t), "cannot convert a table to NoReflection.TableStruct: NoReflection.TableStruct has no generated table converter")
	local classError = "cannot convert a table to NoReflection.TableClass:"
	has(noBindingsError(api.SumClasses, {t}), classError)
	has(noBindingsError(api.GetClass, {v = t}), classError)
	has(noBindingsError(api.CastClass, t), classError)
	has(noBindingsError(api.ClassResult, function() return t end), classError)
end

-- xlua.private_accessible (without XLUA_RESTRICT_PRIVATE_ACCESS, or with
-- HOTFIX_ENABLE) asks for reflection on one type: under XLUA_NO_REFLECTION a
-- type without bindings then gets its public and private members and its
-- constructors by reflection.
local accessibleChecked = false
function CMyTestCaseLuaCallCS.CaseNoReflectionPrivateAccessible(self)
    self.count = 1 + self.count
	if not CS.LuaTestCommon.NoReflection() or xlua.private_accessible == nil then return end
	local Accessible = CS.NoReflection.Accessible
	if not accessibleChecked then
		has(noBindingsError(function() return Accessible() end), "cannot construct NoReflection.Accessible:")
		accessibleChecked = true
	end
	xlua.private_accessible(Accessible)
	local a = Accessible()
	ASSERT_EQ(a.Field, 11)
	ASSERT_EQ(a.hidden, 12)
end

-- XLUA_NO_SIGNATURE_LOADER: SignatureLoader is not compiled.
function CMyTestCaseLuaCallCS.CaseSignatureLoaderFollowsTheDefine(self)
    self.count = 1 + self.count
	ASSERT_EQ(CS.LuaTestCommon.XLuaHasType("XLua.SignatureLoader"), not CS.LuaTestCommon.NoSignatureLoader())
end

-- XLUA_NO_TEMPLATE: LuaEnv does not open the template library outside the Editor, and
-- players do not compile the engine. XLUA_GENERAL builds keep the engine for XLuaGenerate.
function CMyTestCaseLuaCallCS.CaseTemplateLibraryFollowsTheDefine(self)
    self.count = 1 + self.count
	local common = CS.LuaTestCommon
	if common.NoTemplate() then
		ASSERT_EQ(rawget(_G, "template"), nil)
	else
		local compiled = template.compile("<%for i = 1, n do%><%=i%>,<%end%>")
		ASSERT_EQ(template.execute(compiled, setmetatable({n = 3}, {__index = _G})), "1,2,3,")
	end
	ASSERT_EQ(common.XLuaHasType("XLua.TemplateEngine.LuaTemplate"), common.IsXLuaGeneral() or not common.NoTemplate())
end

-- xlua.util and perf.profiler are embedded in C#, so require finds them with no file on
-- disk (run_managed.py copies no Resources). XLUA_NO_UTIL and XLUA_NO_PROFILER leave them out.
local function require_fresh(name)
	local saved = package.loaded[name]
	package.loaded[name] = nil
	local ok, module = pcall(require, name)
	package.loaded[name] = saved
	return ok, module
end

function CMyTestCaseLuaCallCS.CaseEmbeddedUtilFollowsTheDefine(self)
    self.count = 1 + self.count
	local ok, util = require_fresh("xlua.util")
	if CS.LuaTestCommon.NoUtil() then
		ASSERT_EQ(ok, false)
		ASSERT_TRUE(string.find(util, "module 'xlua.util' not found", 1, true) ~= nil)
		return
	end
	ASSERT_EQ(ok, true)
	-- The chunk name is the one the Resources file had.
	ASSERT_EQ(debug.getinfo(util.bind, "S").source, "@xlua/util.lua")
	ASSERT_EQ(util.bind(function(a, b) return a + b end, 1)(2), 3)
	-- A searcher ahead of the embedded one, where LuaEnv.AddLoader's loaders and the
	-- Resources searcher sit, still provides its own xlua.util.
	local searchers = package.searchers or package.loaders
	table.insert(searchers, 3, function(name)
		if name == "xlua.util" then return function() return "override" end end
		return "\n\tno override for '" .. name .. "'"
	end)
	local ok2, override = require_fresh("xlua.util")
	table.remove(searchers, 3)
	ASSERT_EQ(ok2, true)
	ASSERT_EQ(override, "override")
end

-- XLUA_NO_PROFILER: perf.profiler is left out, except in the Editor and Development Builds.
function CMyTestCaseLuaCallCS.CaseEmbeddedProfilerFollowsTheDefine(self)
    self.count = 1 + self.count
	local ok, profiler = require_fresh("perf.profiler")
	if CS.LuaTestCommon.NoProfiler() then
		ASSERT_EQ(ok, false)
		ASSERT_TRUE(string.find(profiler, "module 'perf.profiler' not found", 1, true) ~= nil)
		return
	end
	ASSERT_EQ(ok, true)
	local function profiled_function(n)
		local s = 0
		for i = 1, n do s = s + i end
		return s
	end
	local cs_twice = CS.NativeCoverageHelpers.Twice
	profiler.start()
	for _ = 1, 3 do profiled_function(10) end
	for _ = 1, 4 do cs_twice(1) end
	local report = profiler.report("CALLED")
	profiler.stop()
	ASSERT_TRUE(string.find(report, "FUNCTION", 1, true) ~= nil)
	-- The CALLED column, the last one: each call counts once (PUC Lua counted C# calls twice).
	local function called(name)
		for line in string.gmatch(report, "[^\n]+") do
			if string.sub(line, 1, #name + 2) == "|" .. name .. " " then
				return tonumber(string.match(line, "(%d+)%s*|$")), line
			end
		end
	end
	ASSERT_EQ(called("profiled_function"), 3)
	local count, line = called("cs_twice")
	ASSERT_EQ(count, 4)
	ASSERT_TRUE(string.find(line, "[C#]", 1, true) ~= nil)
end

-- perf.profiler counts a function reached through `return f()` once, as on LuaJIT. On
-- Lua 5.2 and later the hook's event 4 is a tail call, which xlua.sethook named
-- "tail return": 5 direct and 2 tail calls reported 9 on PUC Lua and 7 on LuaJIT.
function CMyTestCaseLuaCallCS.CaseProfilerCountsTailCalls(self)
    self.count = 1 + self.count
	local ok, profiler = require_fresh("perf.profiler")
	if not ok then return end -- XLUA_NO_PROFILER
	local function tail_target(n) return n + 1 end
	local function tail_caller(n) return tail_target(n) end
	local cs_twice = CS.NativeCoverageHelpers.Twice
	local function cs_tail_caller(n) return cs_twice(n) end
	-- LuaJIT calls no hooks in compiled code; this may run inside a trace of the runner.
	if jit and jit.off then jit.flush() jit.off() end
	profiler.start()
	tail_target(1) tail_target(2) tail_target(3) tail_target(4) tail_target(5)
	tail_caller(1) tail_caller(2)
	cs_twice(1) cs_tail_caller(2) cs_tail_caller(3)
	local report = profiler.report("CALLED")
	profiler.stop()
	if jit and jit.on then jit.on() end
	local function called(name)
		for line in string.gmatch(report, "[^\n]+") do
			if string.sub(line, 1, #name + 2) == "|" .. name .. " " then
				return tonumber(string.match(line, "(%d+)%s*|$"))
			end
		end
	end
	ASSERT_EQ(called("tail_target"), 7)
	ASSERT_EQ(called("tail_caller"), 2)
	-- On LuaJIT perf.profiler does not count a C# function called in a tail call
	-- (`return cs_twice(n)`), before this change as well: only the direct call counts.
	ASSERT_EQ(called("cs_twice"), jit and 1 or 3)
	ASSERT_EQ(called("cs_tail_caller"), 2)
end

-- Memory diagnostics (LuaMemoryDiagnostics, Assets/XLua/Src/LuaMemorySnapshot.cs): compiled in the
-- Editor, Development Builds and with XLUA_MEMORY_DIAGNOSTICS; run_managed.py --define
-- XLUA_MEMORY_DIAGNOSTICS runs the cases below, a build without it checks that nothing is compiled in.
-- Each snapshot is taken from C# code that Lua called, the reentrant case of
-- https://github.com/Tencent/xLua/issues/1149.
function CMyTestCaseLuaCallCS.CaseMemoryDiagnosticsFollowsTheDefine(self)
    self.count = 1 + self.count
	local common, memory = CS.LuaTestCommon, CS.MemoryDiagnosticsHelpers
	local enabled = memory.Enabled()
	ASSERT_EQ(common.XLuaHasType("XLua.LuaMemorySnapshot"), enabled)
	ASSERT_EQ(common.XLuaHasType("XLua.LuaMemoryDiagnostics"), enabled)
	ASSERT_EQ(memory.HasSnapshotImport(), enabled)
	local ok, module = require_fresh("xlua.memory")
	ASSERT_EQ(ok, enabled)
	if not enabled then
		ASSERT_TRUE(string.find(module, "module 'xlua.memory' not found", 1, true) ~= nil)
		return
	end
	-- A plugin built with XLUA_MEMORY_SNAPSHOT=OFF (the default for Android) has no walk: the
	-- snapshot says how to get it, and the cases below do nothing.
	local status = memory.NativeSnapshotStatus()
	if status ~= "" then
		ASSERT_TRUE(string.find(status, "XLUA_MEMORY_SNAPSHOT=ON", 1, true) ~= nil)
	end
end

-- A C# object held by Lua is reported with the Lua path that reaches it
-- (https://github.com/Tencent/xLua/issues/1020: the walk follows userdata).
function CMyTestCaseLuaCallCS.CaseMemorySnapshotPathOfHeldObject(self)
    self.count = 1 + self.count
	local memory = CS.MemoryDiagnosticsHelpers
	if not memory.Enabled() or not memory.NativeSnapshot() then return end
	-- Built in a function of its own, so that no stale temporary in this frame refers to it.
	local function hold() memtest_holder = {inner = {sb = CS.System.Text.StringBuilder("memtest held")}} end
	hold()
	ASSERT_EQ(memory.PathOfHeldStringBuilder("memtest held"), "_G.memtest_holder.inner.sb")
	memtest_holder = nil
	ASSERT_EQ(memory.PathOfHeldStringBuilder("memtest held"), "not held")
end

function CMyTestCaseLuaCallCS.CaseMemorySnapshotDiff(self)
    self.count = 1 + self.count
	local memory = CS.MemoryDiagnosticsHelpers
	if not memory.Enabled() or not memory.NativeSnapshot() then return end
	memtest_grow = {}
	local before = memory.Take()
	for i = 1, 10 do memtest_grow[i] = i end
	memtest_leak = {}
	for i = 1, 50 do memtest_leak[i] = {} end
	memtest_leak.objects = {CS.System.Text.StringBuilder("memtest 1"), CS.System.Text.StringBuilder("memtest 2"),
		CS.System.Text.StringBuilder("memtest 3")}
	local after = memory.Take()
	ASSERT_EQ(memory.CheckDiff(before, after), "")
	memtest_leak, memtest_grow = nil, nil
	ASSERT_EQ(memory.CheckReleased(after), "")
end

-- Snapshots from __gc finalizers while the collector runs (Lua 5.1 and LuaJIT run __gc for
-- userdata only, so newproxy makes one there).
function CMyTestCaseLuaCallCS.CaseMemorySnapshotInFinalizer(self)
    self.count = 1 + self.count
	local memory = CS.MemoryDiagnosticsHelpers
	if not memory.Enabled() or not memory.NativeSnapshot() then return end
	local counts = {}
	local function finalized(fn)
		if newproxy then
			local p = newproxy(true)
			getmetatable(p).__gc = fn
			return p
		end
		return setmetatable({}, {__gc = fn})
	end
	for _ = 1, 5 do
		finalized(function() counts[#counts + 1] = memory.CountObjectsWithoutGc() end)
	end
	collectgarbage()
	collectgarbage()
	ASSERT_EQ(#counts, 5)
	for _, n in ipairs(counts) do ASSERT_TRUE(n > 100) end
	ASSERT_TRUE(collectgarbage("isrunning") ~= false)
end

-- Lua objects that C# holds: a LuaFunction is a registry reference until it is disposed.
function CMyTestCaseLuaCallCS.CaseMemorySnapshotRegistryReference(self)
    self.count = 1 + self.count
	local memory = CS.MemoryDiagnosticsHelpers
	if not memory.Enabled() or not memory.NativeSnapshot() then return end
	ASSERT_EQ(memory.CheckRegistryReference(), "")
end

-- The xlua.memory module: snapshot, diff and report from Lua.
function CMyTestCaseLuaCallCS.CaseMemoryLuaModule(self)
    self.count = 1 + self.count
	local helpers = CS.MemoryDiagnosticsHelpers
	if not helpers.Enabled() or not helpers.NativeSnapshot() then return end
	local memory = require("xlua.memory")
	local a = memory.snapshot()
	memtest_module = {{}, {}}
	local b = memory.snapshot()
	local d = memory.diff(a, b)
	ASSERT_TRUE(d.Added.Count >= 3)
	ASSERT_TRUE(string.find(memory.report(d), "_G.memtest_module", 1, true) ~= nil)
	ASSERT_TRUE(string.find(memory.report(b, 5), "Largest tables", 1, true) ~= nil)
	memtest_module = nil
end

-- The string.pack example that replaces the removed tdr module (docs/ECOSYSTEM.md,
-- "string.pack and string.unpack"). LuaJIT has no string.pack.
function CMyTestCaseLuaCallCS.CaseStringPackVersionedMessage(self)
    self.count = 1 + self.count
	if not string.pack then return end

	-- Example start
	local Bag = {VERSION = 2}

	-- A message is its version and a length-prefixed body. Each version only appends
	-- fields to the body, so a reader skips the fields of newer versions it does not know.
	function Bag.pack(bag, version)
		version = version or Bag.VERSION
		local body = {string.pack(">s1 I2", bag.name, #bag.items)}
		for _, item in ipairs(bag.items) do
			body[#body + 1] = string.pack(">I4 I2", item.id, item.qty)
		end
		if version >= 2 then
			body[#body + 1] = string.pack(">i4", bag.extra) -- added in version 2
		end
		return string.pack(">I2 s4", version, table.concat(body))
	end

	function Bag.unpack(data)
		local version, body = string.unpack(">I2 s4", data)
		local bag = {items = {}, extra = 0} -- defaults for fields an older writer did not send
		local count, pos
		bag.name, count, pos = string.unpack(">s1 I2", body)
		for i = 1, count do
			local id, qty
			id, qty, pos = string.unpack(">I4 I2", body, pos)
			bag.items[i] = {id = id, qty = qty}
		end
		if version >= 2 then
			bag.extra, pos = string.unpack(">i4", body, pos)
		end
		return bag, version
	end
	-- Example end

	local bag = {name = "hi", items = {{id = 7, qty = 3}, {id = 8, qty = 1}}, extra = -9}
	local data = Bag.pack(bag)
	ASSERT_EQ(#data, 2 + 4 + (1 + 2) + 2 + 2 * 6 + 4)
	local copy, version = Bag.unpack(data)
	ASSERT_EQ(version, 2)
	ASSERT_EQ(copy.name, "hi")
	ASSERT_EQ(#copy.items, 2)
	ASSERT_EQ(copy.items[2].id, 8)
	ASSERT_EQ(copy.items[2].qty, 1)
	ASSERT_EQ(copy.extra, -9)
	-- A version 1 message has no extra field; the reader uses the default.
	local old = Bag.unpack(Bag.pack(bag, 1))
	ASSERT_EQ(old.extra, 0)
	ASSERT_EQ(old.items[1].id, 7)
	-- A version 3 writer appended a field; this reader ignores it.
	local _, body = string.unpack(">I2 s4", data)
	local newer = Bag.unpack(string.pack(">I2 s4", 3, body .. string.pack(">d", 1.5)))
	ASSERT_EQ(newer.extra, -9)
	ASSERT_EQ(newer.name, "hi")
end

-- The C modules the native library was built with (XLUA_LUASOCKET, XLUA_MIME
-- in build/CMakeLists.txt). run_managed.py sets XLUA_EXPECT_NATIVE_MODULES;
-- without it (Unity) the default build's "socket,mime" is expected.
local function expectedNativeModules()
	local list = os.getenv("XLUA_EXPECT_NATIVE_MODULES") or "socket,mime"
	local have = {}
	for name in string.gmatch(list, "[^,%s]+") do have[name] = true end
	return have
end

function CMyTestCaseLuaCallCS.CaseNativeModulesFollowTheBuild(self)
    self.count = 1 + self.count
	-- The native library puts the loaders into package.preload itself; C#
	-- registers nothing for them. Without the module, require fails cleanly.
	local have = expectedNativeModules()
	local path, cpath = package.path, package.cpath
	package.path, package.cpath = "", "" -- no module files from the host
	local ok, err = pcall(function()
		for _, base in ipairs({"socket", "mime"}) do
			for _, name in ipairs({base .. ".core", base}) do
				if have[base] then
					ASSERT_EQ(type(package.preload[name]), "function")
					ASSERT_EQ(type(require(name)._VERSION), "string")
				else
					ASSERT_EQ(package.preload[name], nil)
					local found, message = pcall(require, name)
					ASSERT_EQ(found, false)
					ASSERT_TRUE(string.find(tostring(message), "module '" .. name .. "' not found", 1, true) ~= nil)
				end
			end
		end
		if have.mime then
			ASSERT_EQ((require("mime").b64("xlua")), "eGx1YQ==")
		end
		-- LuaDLL.Lua.luaopen_socket_core, the former import, now runs the preload entry.
		local legacy = CS.LuaTestCommon.LegacySocketImport()
		if have.socket then
			ASSERT_EQ(legacy, "table")
		else
			ASSERT_TRUE(string.find(legacy, "LuaSocket is not compiled", 1, true) ~= nil)
		end
	end)
	package.path, package.cpath = path, cpath
	if not ok then error(err, 0) end
end

-- The uint64 table follows the build (XLUA_UINT64): without it C# long and
-- ulong values still keep all 64 bits, a ulong of 2^63 or more just reads as a
-- negative integer on Lua 5.3+.
function CMyTestCaseLuaCallCS.CaseUint64TableFollowsTheBuild(self)
    self.count = 1 + self.count
	if UINT64_BUILT() or jit then
		ASSERT_EQ(type(uint64), "table")
		ASSERT_EQ(type(uint64.tostring), "function")
		ASSERT_EQ(uint64.tostring(CS.LongStatic.ULONG_MAX), "18446744073709551615")
		return
	end
	ASSERT_EQ(uint64, nil)
	local class = CS.TestUlongAndLongType()
	ASSERT_EQ(class.UlongMax, -1)
	ASSERT_EQ(math.ult(1, class.UlongMax), true)
	class.UlongMin = class.UlongMax
	ASSERT_EQ(class.UlongMin, -1)
	ASSERT_EQ(CS.LongStatic.ULONG_MAX, -1)
	ASSERT_EQ(tostring(class.LongMin), "-9223372036854775808")
end

-- The Lua standard libraries follow the build (XLUA_LIBS). run_managed.py
-- passes XLUA_EXPECT_LIBS ("all" or a list) from the environment; without it
-- every library is expected. The suite's own harness needs io, os, string,
-- table and math, so only the others can be missing here.
function CMyTestCaseLuaCallCS.CaseStdLibrariesFollowTheBuild(self)
    self.count = 1 + self.count
	local names = jit and {"package", "table", "io", "os", "string", "math", "debug", "bit", "jit"}
		or {"package", "coroutine", "table", "io", "os", "string", "math", "utf8", "debug"}
	local list = os.getenv("XLUA_EXPECT_LIBS") or "all"
	local want = {}
	for _, name in ipairs(names) do want[name] = list:lower() == "all" end
	if list:lower() ~= "all" then
		for name in string.gmatch(list:lower(), "[^,;%s]+") do
			if want[name] ~= nil then want[name] = true end
		end
	end
	for _, name in ipairs(names) do
		ASSERT_EQ(name .. ":" .. type(rawget(_G, name)), name .. ":" .. (want[name] and "table" or "nil"))
	end
end

-- The parts of os, package and the base library that the minimal preset leaves
-- out (CMake XLUA_OS_FULL, XLUA_LOADLIB, XLUA_DOFILE). run_managed.py passes
-- XLUA_EXPECT_OS_FULL, XLUA_EXPECT_LOADLIB and XLUA_EXPECT_DOFILE from the
-- preset; unset (Unity), the default build's ON is expected. os is read from
-- NATIVE_OS_FIELDS, its fields before luaunit_compat's getenv stand-in.
local function expect_part(name)
	local value = os.getenv("XLUA_EXPECT_" .. name)
	value = value and value:upper()
	return not (value == "OFF" or value == "0" or value == "FALSE" or value == "NO")
end

function CMyTestCaseLuaCallCS.CaseLibraryPartsFollowTheBuild(self)
    self.count = 1 + self.count
	if NATIVE_OS_FIELDS then
		local full = expect_part("OS_FULL")
		for _, name in ipairs({"clock", "date", "difftime", "time"}) do
			ASSERT_EQ("os." .. name .. ":" .. tostring(NATIVE_OS_FIELDS[name]), "os." .. name .. ":true")
		end
		for _, name in ipairs({"exit", "getenv", "remove", "rename", "setlocale", "tmpname"}) do
			ASSERT_EQ("os." .. name .. ":" .. tostring(NATIVE_OS_FIELDS[name] == true), "os." .. name .. ":" .. tostring(full))
		end
		ASSERT_EQ(os.difftime(5, 3), 2)
		ASSERT_EQ(os.date("!%Y-%m-%d", 86400), "1970-01-02")
		ASSERT_EQ(type(os.time()), "number")
		ASSERT_EQ(type(os.clock()), "number")
	end
	local dofile_on = expect_part("DOFILE")
	ASSERT_EQ("dofile:" .. type(rawget(_G, "dofile")), "dofile:" .. (dofile_on and "function" or "nil"))
	ASSERT_EQ("loadfile:" .. type(rawget(_G, "loadfile")), "loadfile:" .. (dofile_on and "function" or "nil"))
	local loadlib_on = expect_part("LOADLIB")
	ASSERT_EQ("loadlib:" .. type(package.loadlib), "loadlib:" .. (loadlib_on and "function" or "nil"))
	-- The C searchers look on package.cpath; without them the message names no C file.
	local path, cpath = package.path, package.cpath
	package.path, package.cpath = "./?.xlua_lua", "./?.xlua_c"
	local ok, message = pcall(require, "xlua_no_such_module")
	package.path, package.cpath = path, cpath
	ASSERT_EQ(ok, false)
	ASSERT_EQ(string.find(message, "xlua_no_such_module.xlua_c", 1, true) ~= nil, loadlib_on)
	-- require keeps the preload searcher.
	package.preload.xlua_parts_probe = function() return 42 end
	ASSERT_EQ(require("xlua_parts_probe"), 42)
	package.preload.xlua_parts_probe, package.loaded.xlua_parts_probe = nil, nil
end

-- XLUA_REDUCED_DEBUG (the minimal preset): LuaLibs.Default reduces debug to
-- traceback and getinfo in players only. The Editor, Development Builds and the
-- XLUA_GENERAL tools, which run this suite, keep the whole library; the reduced
-- table itself is checked by CaseLuaOptionsLibraries.
function CMyTestCaseLuaCallCS.CaseDebugLibraryFollowsTheDefine(self)
    self.count = 1 + self.count
	local common = CS.LuaTestCommon
	local player = not (common.InEditor() or common.DevelopmentBuild() or common.IsXLuaGeneral())
	ASSERT_EQ(common.DefaultHasFullDebug(), not (common.ReducedDebug() and player))
	ASSERT_EQ(type(debug.sethook) == "function", common.DefaultHasFullDebug())
	ASSERT_EQ(type(debug.traceback), "function")
	ASSERT_EQ(type(debug.getinfo), "function")
end

function CMyTestCaseLuaCallCS.CaseLuaSocketLoopback(self)
    self.count = 1 + self.count
	if not expectedNativeModules().socket then return end
	-- xLua maps require 'socket' to the C core (socket.core), so only core
	-- functions are available: tcp(), udp(), connect().
	local socket = require("socket")
	local server = assert(socket.tcp())
	assert(server:bind("127.0.0.1", 0))
	assert(server:listen(1))
	local _, port = server:getsockname()
	local client = assert(socket.connect("127.0.0.1", port))
	server:settimeout(2)
	local conn = assert(server:accept())
	conn:settimeout(2)
	assert(client:send("ping\n"))
	ASSERT_EQ(conn:receive("*l"), "ping")
	-- Unknown option names longer than the old 45-byte message buffer must
	-- raise a Lua error rather than overflow the stack.
	local ok, err = pcall(client.setoption, client, string.rep("x", 60), true)
	ASSERT_EQ(ok, false)
	ASSERT_EQ(string.find(tostring(err), "unsupported option", 1, true) ~= nil, true)
	conn:close()
	client:close()
	server:close()

	local receiver = assert(socket.udp())
	assert(receiver:setsockname("127.0.0.1", 0))
	local _, udp_port = receiver:getsockname()
	receiver:settimeout(2)
	local sender = assert(socket.udp())
	assert(sender:sendto("hi", "127.0.0.1", udp_port))
	ASSERT_EQ(receiver:receive(), "hi")
	sender:close()
	receiver:close()
end

function CMyTestCaseLuaCallCS.CasePackAsTableStruct(self)
    self.count = 1 + self.count
	-- Generated code packs it into a table, reflection passes a boxed value;
	-- both read the same fields.
	local v = CS.NativeCoverageHelpers.MakePackAsTable(3, 0.5)
	ASSERT_EQ(v.count, 3)
	ASSERT_EQ(v.ratio, 0.5)
end

function CMyTestCaseLuaCallCS.CaseStructCloneCopiesStruct(self)
    self.count = 1 + self.count
	local a = CS.NativeCoverageHelpers.MakeStructA(7, -3)
	local ok, b = pcall(xlua.structclone, a)
	if ok then
		-- A GCOptimize struct in generated code is a copied C struct in Lua.
		ASSERT_TRUE(rawequal(a, b) == false)
		ASSERT_EQ(b.byteVar, 7)
		ASSERT_EQ(b.sbyteVar, -3)
		b.byteVar = 9
		ASSERT_EQ(a.byteVar, 7)
	else
		-- Without generated code the struct is a boxed object.
		ASSERT_TRUE(tostring(b):find("invalid c# struct", 1, true))
	end
	ASSERT_EQ(pcall(xlua.structclone, {}), false)
end

-- Setting a member a type does not have walks the BaseType chain up to
-- System.Object and then raises; it must not create the member.
function CMyTestCaseLuaCallCS.CaseSetUnknownMemberRaises(self)
    self.count = 1 + self.count
	local function raises(text, f)
		local ok, err = pcall(f)
		ASSERT_EQ(ok, false)
		ASSERT_TRUE(string.find(tostring(err), text, 1, true) ~= nil)
	end
	local obj = CS.LuaTestObj()
	raises("cannot set NoSuchField, no such field", function() obj.NoSuchField = 1 end)
	ASSERT_EQ(obj.NoSuchField, nil)
	local sb = CS.System.Text.StringBuilder("ab")
	raises("cannot set NoSuchField, no such field", function() sb.NoSuchField = 1 end)
	-- An array takes number keys; another key goes up to System.Array and System.Object.
	local arr = CS.System.Array.CreateInstance(typeof(CS.System.Int32), 2)
	arr[0] = 5
	ASSERT_EQ(arr[0], 5)
	raises("cannot set foo, no such field", function() arr.foo = 1 end)
	raises("no static field NoSuchStatic", function() CS.LuaTestObj.NoSuchStatic = 1 end)
	raises("no static field NoSuchStatic", function() CS.System.Text.StringBuilder.NoSuchStatic = 1 end)
	ASSERT_EQ(CS.LuaTestObj.NoSuchStatic, nil)
	-- The objects still work.
	ASSERT_EQ(sb:ToString(), "ab")
	ASSERT_EQ(arr.Length, 2)
end

-- LuaTable.GetInPath/SetInPath with dotted paths.
function CMyTestCaseLuaCallCS.CaseLuaTableInPath(self)
    self.count = 1 + self.count
	local H = CS.NativeCoverageHelpers
	local t = {a = {b = {c = 1}, n = 5}, top = "top"}
	ASSERT_EQ(H.GetInPath(t, "a.b.c"), 1)
	ASSERT_EQ(H.GetInPath(t, "top"), "top")
	ASSERT_EQ(H.GetInPath(t, "a.missing"), nil)
	ASSERT_EQ(H.GetInPath(t, "a.missing.c"), nil)
	ASSERT_EQ(H.GetInPath(t, "a.n.c"), nil)
	ASSERT_EQ(H.GetInPath(t, "nothing.at.all"), nil)
	ASSERT_TRUE(rawequal(H.GetInPath(t, "a.b"), t.a.b))
	H.SetInPath(t, "a.b.d", "set")
	ASSERT_EQ(t.a.b.d, "set")
	H.SetInPath(t, "a.b.c", 2)
	ASSERT_EQ(t.a.b.c, 2)
	H.SetInPath(t, "new", 3)
	ASSERT_EQ(t.new, 3)
	for _, path in ipairs({"a.missing.d", "a.n.d", "nothing.at"}) do
		local ok, err = pcall(H.SetInPath, t, path, 1)
		ASSERT_EQ(ok, false)
		ASSERT_TRUE(string.find(tostring(err), "can not set value to " .. path, 1, true) ~= nil)
	end
	ASSERT_EQ(t.a.missing, nil)
	ASSERT_EQ(t.nothing, nil)
end

-- LuaFunction.SetEnv sets the function's _ENV. On Lua 5.3+ it used to set
-- upvalue 1 in place: in a closure that can be another local, and an _ENV
-- shared with this file's chunk, so every function here lost its globals.
function CMyTestCaseLuaCallCS.CaseLuaFunctionSetEnv(self)
    self.count = 1 + self.count
	local H = CS.NativeCoverageHelpers
	local chunk = load("return y")
	H.SetEnv(chunk, {y = 1})
	ASSERT_EQ(chunk(), 1)
	local x = "up"
	local get = function() return x, y end
	H.SetEnv(get, {y = 2})
	local a, b = get()
	ASSERT_EQ(a, "up")
	ASSERT_EQ(b, 2)
	ASSERT_EQ(x, "up")
	local only_env = function() return y end
	H.SetEnv(only_env, {y = 3})
	ASSERT_EQ(only_env(), 3)
	-- This function shares only_env's original _ENV; its globals are unchanged.
	ASSERT_EQ(y, nil)
	ASSERT_EQ(type(ASSERT_EQ), "function")
	-- A function without _ENV and a C function are left as they are.
	local no_env = function() return x end
	H.SetEnv(no_env, {x = "env"})
	ASSERT_EQ(no_env(), "up")
	local wrapped = coroutine.wrap(function() return "wrapped" end)  -- a C closure
	H.SetEnv(wrapped, {})
	ASSERT_EQ(wrapped(), "wrapped")
end

-- A Lua float passed as a C# uint converts like the integer it truncates
-- to. A plain C cast was undefined for negative and too large values, and
-- -1.5 gave 0 on ARM64 but 4294967295 on x86-64.
function CMyTestCaseLuaCallCS.CaseUintArgumentFromFloat(self)
    self.count = 1 + self.count
	local U = CS.NativeCoverageHelpers.UintArg
	ASSERT_EQ(U(7), 7)
	ASSERT_EQ(U(4294967295), 4294967295)
	if CS.BridgeHardening.Defines.StrictIntRange() then
		-- Outside the uint range (CaseIntegerRangeFollowsTheDefine).
		for _, big in ipairs({-1, -2.0, 1e10}) do
			local ok, err = pcall(U, big)
			ASSERT_EQ(ok, false)
			ASSERT_EQ(string.find(tostring(err), "value out of range", 1, true) ~= nil, true)
		end
	else
		ASSERT_EQ(U(-1), 4294967295)
		ASSERT_EQ(U(-2.0), 4294967294)
		ASSERT_EQ(U(1e10), 1410065408)
	end
	-- A float without an integer value raises (CaseIntegerArgumentRule).
	for _, bad in ipairs({2.75, -1.5, 4294967296.5, 1e300, 0/0}) do
		local ok, err = pcall(U, bad)
		ASSERT_EQ(ok, false)
		ASSERT_EQ(string.find(tostring(err), "number has no integer representation", 1, true) ~= nil, true)
	end
end

-- Lua 5.3's rule for a number that goes into a C# integer parameter or field
-- (luaL_checkinteger), on every runtime: an integer, a float with an integer
-- value or a numeric string that converts to one is accepted; a fraction, NaN,
-- an infinity or a value outside the 64-bit range raises "number has no
-- integer representation". Integral values outside the C# type's range keep
-- wrapping like a C# unchecked cast. IntegerSlots is emitted or generated,
-- IntegerSlotsReflect goes through reflection.
local integerSlotClasses = {{"IntegerSlots", CS.IntegerSlots}, {"IntegerSlotsReflect", CS.IntegerSlotsReflect}}
-- Read on first use: System.Int64 and System.UInt64 have no bindings in the suite,
-- so under XLUA_NO_REFLECTION the cases that read them raise (needs_reflection.lua).
local integerSlotTypeList
local function integerSlotTypes()
	integerSlotTypeList = integerSlotTypeList or {
		-- name, smallest, largest
		{"SByte", -128, 127}, {"Byte", 0, 255}, {"Short", -32768, 32767}, {"UShort", 0, 65535},
		{"Int", -2147483648, 2147483647}, {"UInt", 0, 4294967295}, {"Char", 0, 65535},
		{"Long", CS.System.Int64.MinValue, CS.System.Int64.MaxValue},
		{"ULong", 0, CS.System.UInt64.MaxValue},
	}
	return integerSlotTypeList
end
-- Values without an integer representation, for every integer type.
local noIntegerValues = {2.75, -2.75, 0.5, -0.5, "2.75", "-2.75", 0/0, 1/0, -1/0, 2^63, -2^64, 1e300, "1e300"}

-- What C# returned, as text: LuaJIT returns a long or ulong as an int64
-- userdata, which prints with a "U" suffix on UINT_ESPECIALLY builds.
local function slotText(v)
	return (tostring(v):gsub("U$", ""))
end

-- Calls f(...) and returns "" when it raised an error containing text, or
-- what happened instead.
local function raisesText(text, f, ...)
	local ok, err = pcall(f, ...)
	if ok then return "returned " .. slotText(err) end
	if string.find(tostring(err), text, 1, true) == nil then return "raised " .. tostring(err) end
	return ""
end

function CMyTestCaseLuaCallCS.CaseIntegerArgumentRule(self)
	self.count = 1 + self.count
	local failures = {}
	local function check(label, got, expected)
		if got ~= expected then
			table.insert(failures, label .. ": got " .. tostring(got) .. ", expected " .. tostring(expected))
		end
	end
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		for _, t in ipairs(integerSlotTypes()) do
			local name, lo, hi = t[1], t[2], t[3]
			local f = C[name]
			local accepted = {{lo, slotText(lo)}, {hi, slotText(hi)}, {0, "0"}, {3.0, "3"}, {"12", "12"},
			                  {" 7 ", "7"}, {"3.0", "3"}, {"0x10", "16"}}
			if name ~= "Long" and name ~= "ULong" then
				-- The boundaries as floats (the 64-bit ones are below).
				table.insert(accepted, {lo + 0.0, slotText(lo)})
				table.insert(accepted, {hi + 0.0, slotText(hi)})
			end
			for _, case in ipairs(accepted) do
				local ok, got = pcall(f, case[1])
				check(cname .. "." .. name .. "(" .. tostring(case[1]) .. ")", ok and slotText(got) or tostring(got), case[2])
			end
			for _, v in ipairs(noIntegerValues) do
				check(cname .. "." .. name .. "(" .. tostring(v) .. ")", raisesText("number has no integer representation", f, v), "")
			end
		end
		-- -2^63 is the smallest long, also as a float on every runtime.
		check(cname .. ".Long(-2^63)", slotText(C.Long(-2^63)), slotText(CS.System.Int64.MinValue))
		check(cname .. ".Long(2^53)", slotText(C.Long(2^53)), "9007199254740992")
		-- Integral values outside the type's range wrap, as before, unless
		-- XLUA_STRICT_INT_RANGE is set (CaseIntegerRangeFollowsTheDefine).
		if not CS.BridgeHardening.Defines.StrictIntRange() then
		for _, case in ipairs({{"Byte", 300, "44"}, {"SByte", 200, "-56"}, {"Short", 32768, "-32768"},
		                       {"UShort", -1, "65535"}, {"UInt", -1, "4294967295"}, {"Int", 4294967301, "5"},
		                       {"Char", 65537, "1"}}) do
			local ok, got = pcall(C[case[1]], case[2])
			check(cname .. "." .. case[1] .. "(" .. tostring(case[2]) .. ")", ok and slotText(got) or tostring(got), case[3])
		end
		end
		check(cname .. ".ULong(-1)", slotText(C.ULong(-1)), slotText(ULONG_TOSTRING("-1", "18446744073709551615")))
		-- The error names the argument as Lua counts it, and the function when
		-- Lua code calls it. pcall(C.Int, 2.75) names it '?', as luaL_argerror
		-- does for a function called from C, and so does a tail call on LuaJIT,
		-- hence the locals.
		check(cname .. ".Int(2.75) message", raisesText("bad argument #1 to 'Int' (number has no integer representation)", function() local r = C.Int(2.75); return r end), "")
		check(cname .. ".Add(1, 2.5) message", raisesText("bad argument #2 to 'Add' (number has no integer representation)", function() local r = C.Add(1, 2.5); return r end), "")
		check(cname .. " pcall(Int, 2.75) message", raisesText("bad argument #1 to '?' (number has no integer representation)", C.Int, 2.75), "")
		local obj = C()
		check(cname .. ":Echo(1.5) message", raisesText("bad argument #1 to 'Echo' (number has no integer representation)", function() local r = obj:Echo(1.5); return r end), "")
		check(cname .. ":Echo(6.0)", obj:Echo(6.0), 6)
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

function CMyTestCaseLuaCallCS.CaseIntegerFieldRule(self)
	self.count = 1 + self.count
	local failures = {}
	local function check(label, got, expected)
		if got ~= expected then
			table.insert(failures, label .. ": got " .. tostring(got) .. ", expected " .. tostring(expected))
		end
	end
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		for _, t in ipairs(integerSlotTypes()) do
			local field = t[1] .. "Field"
			local label = cname .. "." .. field
			C[field] = 3.0
			check(label .. " = 3.0", slotText(C[field]), "3")
			C[field] = "12"
			check(label .. ' = "12"', slotText(C[field]), "12")
			C[field] = t[3]
			check(label .. " = largest", slotText(C[field]), slotText(t[3]))
			for _, v in ipairs(noIntegerValues) do
				check(label .. " = " .. tostring(v), raisesText("number has no integer representation", function() C[field] = v end), "")
				check(label .. " unchanged after " .. tostring(v), slotText(C[field]), slotText(t[3]))
			end
			C[field] = 0
		end
		C.IntProperty = 4.0
		check(cname .. ".IntProperty = 4.0", C.IntProperty, 4)
		check(cname .. ".IntProperty = 2.5", raisesText("number has no integer representation", function() C.IntProperty = 2.5 end), "")
		check(cname .. ".IntProperty unchanged", C.IntProperty, 4)
		local obj = C()
		obj.Value = 6.0
		check(cname .. ".Value = 6.0", obj.Value, 6)
		check(cname .. ".Value = 1.5", raisesText("number has no integer representation", function() obj.Value = 1.5 end), "")
		check(cname .. ".Value unchanged", obj.Value, 6)
		-- xlua.access sets a field through reflection on every path
		-- (XLUA_RESTRICT_PRIVATE_ACCESS leaves it out or limits it to hotfix fields).
		if not CS.BridgeHardening.Defines.RestrictPrivateAccess() then
		xlua.access(obj, "Value", "8")
		check(cname .. " xlua.access Value", obj.Value, 8)
		check(cname .. " xlua.access Value 0.5", raisesText("bad argument #3 to 'access' (number has no integer representation)", function() xlua.access(obj, "Value", 0.5) end), "")
		check(cname .. ".Value unchanged by xlua.access", obj.Value, 8)
		end
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Lua 5.3+ has an integer subtype: 3 is an integer, 3.0 a float. LuaJIT
-- numbers are doubles, and an integral value counts as an integer there.
local overloadHasIntegers = math.type ~= nil
local function overloadFloatInt(withIntegers, luajit)
	if overloadHasIntegers then return withIntegers end
	return luajit
end

-- Overload checks use the same rule: a float with a fraction does not match
-- an integer parameter, so Pick(int)/Pick(double) picks double for 2.75 and
-- int for 3. When both match, the best match wins (CaseOverload* below): an
-- integer prefers the integer overload in either declaration order
-- (PickDoubleFirst(3) calls the int one), and a float with an integer value
-- (3.0) is a float on Lua 5.3+ and prefers double; LuaJIT has no integer
-- subtype, so 3.0 is the integer 3 there.
function CMyTestCaseLuaCallCS.CaseIntegerOverloadRule(self)
	self.count = 1 + self.count
	local failures = {}
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		for _, case in ipairs({
			{"Pick", 2.75, "double"}, {"Pick", -0.5, "double"}, {"Pick", 0/0, "double"}, {"Pick", 1/0, "double"},
			{"Pick", 3, "int"}, {"Pick", 3.0, overloadFloatInt("double", "int")}, {"Pick", -7, "int"},
			{"PickDoubleFirst", 2.75, "double"}, {"PickDoubleFirst", 3, "int"},
			{"PickLong", 2.75, "double"}, {"PickLong", 3, "long"}, {"PickLong", 2^63, "double"},
			{"PickLong", CS.System.Int64.MaxValue, "long"},
			{"PickByte", 2.5, "float"}, {"PickByte", 7, "byte"}, {"PickByte", 7.0, overloadFloatInt("float", "byte")},
		}) do
			local ok, got = pcall(C[case[1]], case[2])
			if not ok or got ~= case[3] then
				table.insert(failures, cname .. "." .. case[1] .. "(" .. tostring(case[2]) .. "): got " .. tostring(got) .. ", expected " .. case[3])
			end
		end
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Overload selection: when several overloads match the arguments, the best
-- match wins in every binding path, as C# overload resolution would choose
-- for the Lua value's natural C# type, in either declaration order. A Lua
-- integer is an int (a long outside the int range): int, then long, then the
-- other integer types, then double, float and decimal; a float prefers
-- double, then float, then decimal, then an integer type; a string prefers
-- string over byte[]; a C# object its own class over a base class, an
-- interface and object; object is the last choice for any value. nil and
-- tables keep the first matching overload. A params array or a default
-- value is used only when nothing else matches as well.
local overloadClasses = {{"OverloadPick", CS.OverloadPick}, {"OverloadPickReflect", CS.OverloadPickReflect}}
local overloadUnpack = table.unpack or unpack
-- Stands for a nil argument in the case tables below.
local NIL = {}

-- Each case is {method, arguments..., expected result}.
local function overloadRun(cases)
	local failures = {}
	for _, class in ipairs(overloadClasses) do
		local cname, C = class[1], class[2]
		for _, case in ipairs(cases) do
			local name, expected, n = case[1], case[#case], #case - 2
			local args, texts = {}, {}
			for i = 1, n do
				local v = case[i + 1]
				if v == NIL then v = nil end
				args[i] = v
				texts[i] = tostring(v)
			end
			local ok, got = pcall(C[name], overloadUnpack(args, 1, n))
			if not ok or got ~= expected then
				table.insert(failures, cname .. "." .. name .. "(" .. table.concat(texts, ", ") .. "): got " .. tostring(got) .. ", expected " .. expected)
			end
		end
	end
	return table.concat(failures, "\n")
end

function CMyTestCaseLuaCallCS.CaseOverloadNumbers(self)
	self.count = 1 + self.count
	local big = 2^40
	if overloadHasIntegers then big = math.tointeger(big) end
	ASSERT_EQ(overloadRun({
		{"DoubleInt", 3, "int"}, {"IntDouble", 3, "int"}, {"DoubleInt", -7, "int"},
		{"DoubleInt", 2.5, "double"}, {"IntDouble", 2.5, "double"},
		{"DoubleInt", 3.0, overloadFloatInt("double", "int")}, {"IntDouble", 3.0, overloadFloatInt("double", "int")},
		{"LongInt", 3, "int"}, {"IntLong", 3, "int"},
		{"LongInt", big, "long"}, {"IntLong", big, "long"},
		{"LongInt", -big, "long"}, {"IntLong", 2147483647, "int"}, {"IntLong", 2147483648, "long"},
		{"FloatDouble", 2.5, "double"}, {"DoubleFloat", 2.5, "double"},
		{"FloatDouble", 3, "double"}, {"DoubleFloat", 3, "double"},
		{"ByteInt", 7, "int"}, {"UIntLong", 7, "long"},
		{"DecimalDouble", 2.5, "double"}, {"DecimalDouble", 3, "double"},
		{"ObjectInt", 3, "int"}, {"ObjectInt", 2.5, "object"}, {"ObjectInt", "x", "object"},
		{"ObjectDouble", 2.5, "double"}, {"ObjectDouble", 3, "double"},
		{"NullableInt", 3, "int"}, {"NullableInt", NIL, "int?"},
		{"Two", 1, 2, "int,int"}, {"Two", 1, 2.5, "double,double"}, {"Two", 1.5, 2, "double,double"},
		-- C# finds Mixed(1, "x") ambiguous; the lower total cost wins here.
		{"Mixed", 1, "x", "double,string"}, {"Mixed", 1.5, "x", "double,string"}, {"Mixed", 1, 2, "int,object"},
	}), "")
end

function CMyTestCaseLuaCallCS.CaseOverloadReferenceTypes(self)
	self.count = 1 + self.count
	local base, derived = CS.OverloadBase(), CS.OverloadDerived()
	ASSERT_EQ(overloadRun({
		{"ObjectString", "x", "string"}, {"StringObject", "x", "string"},
		{"ObjectString", 3, "object"}, {"BytesString", "x", "string"},
		{"ObjectBool", true, "bool"}, {"ObjectBool", "x", "object"},
		{"BaseDerived", derived, "derived"}, {"DerivedBase", derived, "derived"},
		{"BaseDerived", base, "base"}, {"DerivedBase", base, "base"},
		{"ObjectBase", derived, "base"}, {"ObjectBase", base, "base"}, {"ObjectBase", 3, "object"},
		{"InterfaceBase", derived, "base"}, {"InterfaceBase", base, "base"},
		-- nil and tables: the first matching overload, as before.
		{"ObjectString", NIL, "object"}, {"StringObject", NIL, "string"},
		{"BaseDerived", NIL, "base"}, {"DerivedBase", NIL, "derived"},
		{"ObjectTable", {}, "object"},
	}), "")
end

-- The cost of a C# object is cached per pair of object and parameter type:
-- the same call site sees a derived class, its base class and an unrelated
-- class in turn, in both orders, three times over.
function CMyTestCaseLuaCallCS.CaseOverloadTypeCache(self)
	self.count = 1 + self.count
	local base, derived, other = CS.OverloadBase(), CS.OverloadDerived(), CS.OverloadOther()
	local cases = {}
	for _ = 1, 3 do
		for _, case in ipairs({
			{"ObjectBase", derived, "base"}, {"ObjectBase", base, "base"}, {"ObjectBase", other, "object"},
			{"ObjectBase", base, "base"}, {"ObjectBase", derived, "base"},
			{"InterfaceBase", derived, "base"}, {"InterfaceBase", base, "base"}, {"InterfaceBase", other, "interface"},
			{"InterfaceBase", derived, "base"},
			{"BaseDerived", derived, "derived"}, {"BaseDerived", base, "base"}, {"BaseDerived", derived, "derived"},
			{"DerivedBase", base, "base"}, {"DerivedBase", derived, "derived"}, {"DerivedBase", base, "base"},
		}) do
			table.insert(cases, case)
		end
	end
	local failures = {overloadRun(cases)}
	-- An unrelated class matches neither overload, also after the others.
	for _, class in ipairs(overloadClasses) do
		for _, name in ipairs({"BaseDerived", "DerivedBase"}) do
			if pcall(class[2][name], other) then
				table.insert(failures, class[1] .. "." .. name .. "(OverloadOther) did not fail")
			end
		end
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

function CMyTestCaseLuaCallCS.CaseOverloadParamsAndDefaults(self)
	self.count = 1 + self.count
	ASSERT_EQ(overloadRun({
		{"ParamsObjects", 3, "int"}, {"ParamsObjects", "x", "params object"}, {"ParamsObjects", 3, 4, "params object"},
		{"ParamsInts", 1, 2, "int,int"}, {"ParamsInts", 1, "params int"}, {"ParamsInts", 1, 2, 3, "params int"},
		{"Optional", 1, "int"}, {"Optional", 1, 2, "int,int=0"},
		{"OptionalDouble", 1, "int,int=0"}, {"OptionalDouble", 1.5, "double"},
		-- Generated wrappers expand the defaults into overloads of their own; the
		-- one that drops the params array must not win for more arguments.
		{"DefaultsAndParams", 1, 2, "a", "b", "1,2,2"}, {"DefaultsAndParams", 1, "1,1,0"}, {"DefaultsAndParams", 1, 2, "1,2,0"},
	}), "")
end

-- Constructors, instance methods, indexers and operators choose the same way.
function CMyTestCaseLuaCallCS.CaseOverloadMembers(self)
	self.count = 1 + self.count
	local failures = {}
	local function check(what, got, expected)
		if got ~= expected then table.insert(failures, what .. ": got " .. tostring(got) .. ", expected " .. tostring(expected)) end
	end
	for _, class in ipairs(overloadClasses) do
		local cname, C = class[1], class[2]
		check(cname .. "(3)", C(3).Kind, "int")
		check(cname .. "(2.5)", C(2.5).Kind, "double")
		check(cname .. "(3.0)", C(3.0).Kind, overloadFloatInt("double", "int"))
		local o = C()
		check(cname .. ":Instance(3)", o:Instance(3), "int")
		check(cname .. ":Instance(2.5)", o:Instance(2.5), "double")
		check(cname .. "[3]", o[3], "int")
		check(cname .. "[2.5]", o[2.5], "double")
		o[3] = "v"
		check(cname .. "[3] = v", o.Kind, "set int")
		o[2.5] = "v"
		check(cname .. "[2.5] = v", o.Kind, "set double")
		check(cname .. " + 3", o + 3, "int")
		check(cname .. " + 2.5", o + 2.5, "double")
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- The integer rule for the slots besides parameters and fields (C4's
-- follow-up): array elements and indices, tables converted to arrays,
-- collections and structs, params arrays, nullable integers and enums. A float
-- without an integer value raises "number has no integer representation"
-- instead of passing 0 (Lua 5.3+) or the truncated value (LuaJIT). The values
-- C# reads from Lua are in CaseIntegerResultRule.
local function newFailures()
	local failures = {}
	return failures, function(label, got, expected)
		if got ~= expected then
			table.insert(failures, label .. ": got " .. tostring(got) .. ", expected " .. tostring(expected))
		end
	end
end

local integerArrayFields = {"Ints", "UInts", "Longs", "ULongs", "Shorts", "UShorts", "SBytes", "Chars"}

function CMyTestCaseLuaCallCS.CaseIntegerArrayElementRule(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		for _, field in ipairs(integerArrayFields) do
			local a = C[field]
			local label = cname .. "." .. field
			a[0] = 3.0
			check(label .. "[0] = 3.0", slotText(a[0]), "3")
			a[1] = "12"
			check(label .. '[1] = "12"', slotText(a[1]), "12")
			for _, v in ipairs(noIntegerValues) do
				check(label .. "[0] = " .. tostring(v), raisesText("number has no integer representation", function() a[0] = v end), "")
				check(label .. "[0] unchanged after " .. tostring(v), slotText(a[0]), "3")
			end
			-- The index is an int by the same rule.
			check(label .. "[1.0]", slotText(a[1.0]), "12")
			a[2.0] = 5
			check(label .. "[2.0] = 5", slotText(a[2]), "5")
			check(label .. "[0.5]", raisesText("number has no integer representation", function() return a[0.5] end), "")
			check(label .. "[0.5] = 1", raisesText("number has no integer representation", function() a[0.5] = 1 end), "")
			a[0], a[1], a[2] = 0, 0, 0
		end
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

function CMyTestCaseLuaCallCS.CaseIntegerTableContentsRule(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	local NoInt = "number has no integer representation"
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		local function ok(label, f, arg, expected)
			local fine, got = pcall(f, arg)
			check(cname .. "." .. label, fine and slotText(got) or tostring(got), expected)
		end
		local function raises(label, f, arg)
			check(cname .. "." .. label, raisesText(NoInt, f, arg), "")
		end
		ok("JoinInts{1, 2.0, '3'}", C.JoinInts, {1, 2.0, "3"}, "1,2,3")
		raises("JoinInts{1, 2.5}", C.JoinInts, {1, 2.5})
		raises("JoinInts{'0.5'}", C.JoinInts, {"0.5"})
		ok("JoinUInts{4294967295, 0}", C.JoinUInts, {4294967295, 0}, "4294967295,0")
		raises("JoinUInts{0.5}", C.JoinUInts, {0.5})
		ok("JoinLongs{2^53, -1}", C.JoinLongs, {2^53, -1}, "9007199254740992,-1")
		raises("JoinLongs{2^63}", C.JoinLongs, {2^63})
		ok("JoinULongs{0, 3.0}", C.JoinULongs, {0, 3.0}, "0,3")
		raises("JoinULongs{1.5}", C.JoinULongs, {1.5})
		-- A List or Dictionary value: a fraction raises instead of being left
		-- out; values of other types are still left out.
		ok("JoinIntList{1, 2.0}", C.JoinIntList, {1, 2.0}, "1,2")
		ok("JoinIntList{1, 'x', 3}", C.JoinIntList, {1, "x", 3}, "1,3")
		raises("JoinIntList{1, 2.5}", C.JoinIntList, {1, 2.5})
		raises("JoinByteList{0.5}", C.JoinByteList, {0.5})
		ok("JoinDict{a = 1, b = 2.0}", C.JoinDict, {a = 1, b = 2.0}, "a=1,b=2")
		raises("JoinDict{a = 2.5}", C.JoinDict, {a = 2.5})
		-- Keys only select the entries: 1.5 is not an int key.
		ok("CountIntKeys{[1] = 'a', [1.5] = 'b', x = 'c'}", C.CountIntKeys, {[1] = "a", [1.5] = "b", x = "c"}, "1")
		-- Structs: through reflection (IntegerSlotStruct) and the generated
		-- unpacking of a [GCOptimize] struct (StaticPusherStructB).
		ok("StructArg{I = 3.0, B = 7, L = 2^40}", C.StructArg, {I = 3.0, B = 7, L = 2^40}, "3,7,1099511627776")
		raises("StructArg{I = 2.5}", C.StructArg, {I = 2.5})
		raises("StructArg{B = 0.5}", C.StructArg, {B = 0.5})
		raises("StructArg{L = 2^63}", C.StructArg, {L = 2^63})
		ok("PackedArg{intVar = 3.0, shortVar = 2}", C.PackedArg, {intVar = 3.0, shortVar = 2}, "2,0,3,0")
		raises("PackedArg{intVar = 2.5}", C.PackedArg, {intVar = 2.5})
		raises("PackedArg{uintVar = 0.5}", C.PackedArg, {uintVar = 0.5})
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- The elements of a params array are arguments: the error names the argument.
function CMyTestCaseLuaCallCS.CaseIntegerParamsRule(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		check(cname .. ".Params(1, 2.0, '3')", C.Params(1, 2.0, "3"), "1,2,3")
		check(cname .. ".Params()", C.Params(), "")
		check(cname .. ".Params(1, 2.5)", raisesText("bad argument #2 to 'Params' (number has no integer representation)", function() local r = C.Params(1, 2.5); return r end), "")
		check(cname .. ".Params(1, 2, 0/0)", raisesText("bad argument #3 to 'Params' (number has no integer representation)", function() local r = C.Params(1, 2, 0/0); return r end), "")
		check(cname .. ".ParamsLong(2^53, 1.0)", C.ParamsLong(2^53, 1.0), "9007199254740992,1")
		check(cname .. ".ParamsLong(1, 2^63)", raisesText("number has no integer representation", C.ParamsLong, 1, 2^63), "")
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

function CMyTestCaseLuaCallCS.CaseIntegerNullableRule(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		check(cname .. ".Nullable(nil)", C.Nullable(nil), "null")
		check(cname .. ".Nullable(3.0)", C.Nullable(3.0), "3")
		check(cname .. ".Nullable('12')", C.Nullable("12"), "12")
		check(cname .. ".Nullable(2.5)", raisesText("number has no integer representation", C.Nullable, 2.5), "")
		C.NullableField = 4.0
		check(cname .. ".NullableField = 4.0", C.NullableField, 4)
		check(cname .. ".NullableField = 0.5", raisesText("number has no integer representation", function() C.NullableField = 0.5 end), "")
		check(cname .. ".NullableField unchanged", C.NullableField, 4)
		C.NullableField = nil
		check(cname .. ".NullableField = nil", C.NullableField, nil)
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Enums take a number through their underlying type: the integer rule, and
-- all 64 bits of a long enum (Enum.ToObject used to get a 32-bit int).
function CMyTestCaseLuaCallCS.CaseIntegerEnumRule(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		check(cname .. ".EnumArg(2.0)", C.EnumArg(2.0), "B")
		check(cname .. ".EnumArg(2.5)", raisesText("number has no integer representation", C.EnumArg, 2.5), "")
		check(cname .. ".EnumArg(0/0)", raisesText("number has no integer representation", C.EnumArg, 0/0), "")
		check(cname .. ".LongEnumValue(2^40)", slotText(C.LongEnumValue(2^40)), "1099511627776")
		check(cname .. ".LongEnumValue(0.5)", raisesText("number has no integer representation", C.LongEnumValue, 0.5), "")
		-- __CastFrom: generated for IntegerSlotEnum in the --gen-code runs,
		-- reflection for IntegerSlotLongEnum.
		check(cname .. ".EnumArg(__CastFrom(1.0))", C.EnumArg(CS.IntegerSlotEnum.__CastFrom(1.0)), "A")
		check(cname .. ".LongEnumValue(__CastFrom(2^40))", slotText(C.LongEnumValue(CS.IntegerSlotLongEnum.__CastFrom(2^40))), "1099511627776")
	end
	check("IntegerSlotEnum.__CastFrom(2.5)", raisesText("number has no integer representation", CS.IntegerSlotEnum.__CastFrom, 2.5), "")
	check("IntegerSlotLongEnum.__CastFrom(0.5)", raisesText("number has no integer representation", CS.IntegerSlotLongEnum.__CastFrom, 0.5), "")
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Values C# reads from Lua (IntegerSlotResults in LuaTestObj.cs): delegate and
-- interface results and out values, LuaFunction results and LuaTable reads,
-- each with a check that a failed conversion leaves the Lua stack top as it was.
function CMyTestCaseLuaCallCS.CaseIntegerResultRule(self)
	self.count = 1 + self.count
	ASSERT_EQ(CS.IntegerSlotResults.Run(), "")
end

-- XLUA_STRICT_INT_RANGE: integral values outside an 8- to 32-bit type's range
-- raise "value out of range" in every slot instead of wrapping; long and
-- ulong keep their bit patterns either way. Without the define they wrap
-- (also CaseIntegerArgumentRule).
function CMyTestCaseLuaCallCS.CaseIntegerRangeFollowsTheDefine(self)
	self.count = 1 + self.count
	local failures, check = newFailures()
	local strict = CS.BridgeHardening.Defines.StrictIntRange()
	local Range = "value out of range"
	for _, class in ipairs(integerSlotClasses) do
		local cname, C = class[1], class[2]
		-- name, value, wrapped result
		for _, case in ipairs({{"Byte", 300, "44"}, {"Byte", -1, "255"}, {"SByte", 128, "-128"}, {"SByte", -129, "127"},
		                       {"Short", 32768, "-32768"}, {"UShort", 65536, "0"}, {"UShort", -1, "65535"},
		                       {"Int", 2147483648, "-2147483648"}, {"Int", -2147483649, "2147483647"},
		                       {"UInt", -1, "4294967295"}, {"UInt", 4294967296, "0"}, {"Char", 65536, "0"}, {"Char", -1, "65535"}}) do
			local label = cname .. "." .. case[1] .. "(" .. tostring(case[2]) .. ")"
			if strict then
				check(label, raisesText(Range, C[case[1]], case[2]), "")
			else
				check(label, slotText(C[case[1]](case[2])), case[3])
			end
		end
		-- The boundaries are accepted, and long and ulong never raise.
		check(cname .. ".Byte(255)", C.Byte(255), 255)
		check(cname .. ".SByte(-128)", C.SByte(-128), -128)
		check(cname .. ".UInt(4294967295)", C.UInt(4294967295), 4294967295)
		check(cname .. ".Int(-2147483648)", C.Int(-2147483648), -2147483648)
		check(cname .. ".ULong(-1)", slotText(C.ULong(-1)), slotText(ULONG_TOSTRING("-1", "18446744073709551615")))
		check(cname .. ".Long(-1)", slotText(C.Long(-1)), "-1")
		if strict then
			check(cname .. ".Byte(300) message", raisesText("bad argument #1 to 'Byte' (value out of range)", function() local r = C.Byte(300); return r end), "")
			check(cname .. ".ByteField = 256", raisesText(Range, function() C.ByteField = 256 end), "")
			check(cname .. ".SBytes[0] = 128", raisesText(Range, function() C.SBytes[0] = 128 end), "")
			check(cname .. ".Ints[2^32]", raisesText(Range, function() return C.Ints[2^32] end), "")
			check(cname .. ".JoinUInts{-1}", raisesText(Range, C.JoinUInts, {-1}), "")
			check(cname .. ".Params(1, 2^32 + 5)", raisesText("bad argument #2 to 'Params' (value out of range)", function() local r = C.Params(1, 2^32 + 5); return r end), "")
			check(cname .. ".Nullable(2^31)", raisesText(Range, C.Nullable, 2^31), "")
			check(cname .. ".JoinByteList{256}", raisesText(Range, C.JoinByteList, {256}), "")
		else
			C.ByteField = 256
			check(cname .. ".ByteField = 256", C.ByteField, 0)
			C.SBytes[0] = 128
			check(cname .. ".SBytes[0] = 128", C.SBytes[0], -128)
			C.SBytes[0] = 0
			check(cname .. ".JoinUInts{-1}", C.JoinUInts({-1}), "4294967295")
			check(cname .. ".Params(1, 2^32 + 5)", C.Params(1, 2^32 + 5), "1,5")
			check(cname .. ".Nullable(2^31)", C.Nullable(2^31), "-2147483648")
			check(cname .. ".JoinByteList{256}", C.JoinByteList({256}), "0")
		end
		C.ByteField = 0
	end
	ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- One call and one return event for a C# call, the return marked "[C#]", in every
-- binding mode and on every runtime. PUC Lua reported the return twice (its own
-- return hook and the trampoline's report for LuaJIT), and perf.profiler counted
-- every C# call twice.
function CMyTestCaseLuaCallCS.CaseHookSeesCSharpReturn(self)
    self.count = 1 + self.count
	local cs_twice = CS.NativeCoverageHelpers.Twice
	local calls, returns, unmarked = 0, 0, 0
	xlua.sethook(function(event, name, where)
		if name ~= "[?cs_twice]" then return end
		if event == "call" then calls = calls + 1
		elseif event == "return" and where == "[C#]" then returns = returns + 1
		else unmarked = unmarked + 1 end
	end)
	local r = cs_twice(21)
	xlua.sethook(nil)
	ASSERT_EQ(r, 42)
	ASSERT_EQ(calls, 1)
	ASSERT_EQ(returns, 1)
	ASSERT_EQ(unmarked, 0)
end

local function StackDumpLines(text)
	local lines = {}
	for line in string.gmatch(text, "[^\n]+") do lines[#lines + 1] = line end
	return lines
end

function CMyTestCaseLuaCallCS.CaseStackToStringPlainValues(self)
    self.count = 1 + self.count
	local dump = CS.StackDumpHelpers.GetDump()
	local text, unchanged = dump(nil, true, false, 42, 1.5, 2.0, "a\n\"b\"\\\1", {}, print, coroutine.create(function() end))
	ASSERT_EQ(unchanged, true)
	local lines = StackDumpLines(text)
	ASSERT_EQ(#lines, 11)
	ASSERT_EQ(lines[1], "Lua stack (top 10):")
	ASSERT_EQ(lines[2], "  [1 | -10] nil")
	ASSERT_EQ(lines[3], "  [2 | -9] boolean true")
	ASSERT_EQ(lines[4], "  [3 | -8] boolean false")
	if islua53() then
		ASSERT_EQ(lines[5], "  [4 | -7] number(integer) 42")
		ASSERT_EQ(lines[6], "  [5 | -6] number(float) 1.5")
		ASSERT_EQ(lines[7], "  [6 | -5] number(float) 2.0")
	else
		ASSERT_EQ(lines[5], "  [4 | -7] number 42")
		ASSERT_EQ(lines[6], "  [5 | -6] number 1.5")
		ASSERT_EQ(lines[7], "  [6 | -5] number 2")
	end
	ASSERT_EQ(lines[8], '  [7 | -4] string(7) "a\\n\\"b\\"\\\\\\001"')
	ASSERT_TRUE(string.find(lines[9], "^  %[8 | %-3%] table: 0x%x+$") ~= nil)
	ASSERT_TRUE(string.find(lines[10], "^  %[9 | %-2%] function: 0x%x+$") ~= nil)
	ASSERT_TRUE(string.find(lines[11], "^  %[10 | %-1%] thread: 0x%x+$") ~= nil)
	ASSERT_EQ(dump(), "Lua stack (top 0): empty")
end

function CMyTestCaseLuaCallCS.CaseStackToStringCSharpValues(self)
    self.count = 1 + self.count
	local dump = CS.StackDumpHelpers.GetDump()
	local ran = false
	local guarded = setmetatable({}, {
		__tostring = function() ran = true return "x" end,
		__index = function() ran = true end,
	})
	local text, unchanged = dump(CS.System.Text.StringBuilder("hi\nthere"), CS.StackDumpThrowingToString(),
		typeof(CS.System.String), CS.LongStatic.LONG_MAX, CS.LongStatic.ULONG_MAX,
		CS.NativeCoverageHelpers.MakeStructA(7, -3), guarded)
	ASSERT_EQ(unchanged, true)
	ASSERT_EQ(ran, false)
	local lines = StackDumpLines(text)
	ASSERT_EQ(#lines, 8)
	ASSERT_EQ(lines[2], "  [1 | -7] userdata(C#) System.Text.StringBuilder: hi\\nthere")
	ASSERT_EQ(lines[3], "  [2 | -6] userdata(C#) StackDumpThrowingToString: <error: InvalidOperationException: boom>")
	ASSERT_TRUE(string.find(lines[4], "^  %[3 | %-5%] userdata%(C#%) System%.%w*Type: System%.String$") ~= nil)
	if islua53() then
		ASSERT_EQ(lines[5], "  [4 | -4] number(integer) 9223372036854775807")
		ASSERT_EQ(lines[6], "  [5 | -3] number(integer) -1")
	elseif DISTINCT_UINT64 then
		-- UINT_ESPECIALLY gives ulong its own unsigned userdata kind.
		ASSERT_EQ(lines[5], "  [4 | -4] userdata(int64) 9223372036854775807")
		ASSERT_EQ(lines[6], "  [5 | -3] userdata(uint64) 18446744073709551615")
	else
		-- LuaJIT keeps 64-bit integers in userdata; ulong shares the int64 type by default.
		ASSERT_EQ(lines[5], "  [4 | -4] userdata(int64) 9223372036854775807")
		ASSERT_EQ(lines[6], "  [5 | -3] userdata(int64) -1")
	end
	-- Generated code copies a GCOptimize struct into Lua; reflection boxes it.
	ASSERT_TRUE(string.find(lines[7], "^  %[6 | %-2%] userdata%(C#[ a-z]*%) StaticPusherStructA") ~= nil)
	ASSERT_TRUE(string.find(lines[8], "^  %[7 | %-1%] table: 0x%x+$") ~= nil)
end

function CMyTestCaseLuaCallCS.CaseStackToStringCutsLongValues(self)
    self.count = 1 + self.count
	local short = CS.StackDumpHelpers.GetDumpShort()
	-- Five CJK characters (15 bytes) and "abc" followed by a character outside the BMP.
	local text, unchanged = short(string.rep("x", 100), "\228\184\173\230\150\135\229\173\151\231\172\166\229\143\183",
		"abc\240\159\152\128", "abcd", CS.System.Text.StringBuilder("12345"))
	ASSERT_EQ(unchanged, true)
	local lines = StackDumpLines(text)
	ASSERT_EQ(lines[2], '  [1 | -5] string(100) "xxxx"...')
	ASSERT_EQ(lines[3], '  [2 | -4] string(15) "\228\184\173\230\150\135\229\173\151\231\172\166"...')
	ASSERT_EQ(lines[4], '  [3 | -3] string(7) "abc"...')
	ASSERT_EQ(lines[5], '  [4 | -2] string(4) "abcd"')
	ASSERT_EQ(lines[6], "  [5 | -1] userdata(C#) System.Text.StringBuilder: 1234...")
	-- A deep stack keeps its absolute and negative indexes.
	local values = {}
	for i = 1, 200 do values[i] = i end
	local unpack = table.unpack or unpack
	text, unchanged = CS.StackDumpHelpers.GetDump()(unpack(values))
	ASSERT_EQ(unchanged, true)
	lines = StackDumpLines(text)
	ASSERT_EQ(#lines, 201)
	ASSERT_EQ(string.find(lines[201], "  [200 | -1] number", 1, true), 1)
	ASSERT_EQ(string.find(lines[2], "  [1 | -200] number", 1, true), 1)
end

-- UTF-8 text crosses the bridge in both directions. Utf8StringHelpers spells
-- the same strings with C# escapes.
local Utf8Turkish = "\197\159e\196\159i\196\177\196\176\195\167\195\182\195\188"
local Utf8Chinese = "\228\184\173\230\150\135"
local Utf8Emoji = "\240\159\152\128"

function CMyTestCaseLuaCallCS.CaseUtf8TextRoundTrip(self)
    self.count = 1 + self.count
	local U = CS.Utf8StringHelpers
	ASSERT_EQ(U.Turkish, Utf8Turkish)
	ASSERT_EQ(U.Chinese, Utf8Chinese)
	ASSERT_EQ(U.Emoji, Utf8Emoji)
	ASSERT_EQ(U.CodeUnits(Utf8Turkish), "015F 0065 011F 0069 0131 0130 00E7 00F6 00FC")
	ASSERT_EQ(U.CodeUnits(Utf8Chinese), "4E2D 6587")
	ASSERT_EQ(U.CodeUnits(Utf8Emoji), "D83D DE00")
	ASSERT_EQ(U.Length(Utf8Emoji), 2)
	local all = Utf8Turkish .. " " .. Utf8Chinese .. " " .. Utf8Emoji
	ASSERT_EQ(U.Echo(all), all)
	ASSERT_EQ(U.Echo(""), "")
	ASSERT_EQ(U.ThroughLua(function(s)
		ASSERT_EQ(s, Utf8Turkish)
		return s .. Utf8Emoji
	end, U.Turkish), Utf8Turkish .. Utf8Emoji)
end

-- Longer than the 256-byte buffer that short strings reuse.
function CMyTestCaseLuaCallCS.CaseUtf8LongText(self)
    self.count = 1 + self.count
	local U = CS.Utf8StringHelpers
	local long = string.rep(Utf8Turkish .. Utf8Chinese .. Utf8Emoji, 40)
	ASSERT_EQ(#long, 40 * 26)
	ASSERT_EQ(U.Length(long), 40 * 13)
	ASSERT_EQ(U.Echo(long), long)
	local mixed = string.rep("a", 300) .. "\255"
	ASSERT_EQ(U.Length(mixed), 301)
	ASSERT_EQ(U.Echo(mixed), string.rep("a", 300) .. "\239\191\189")
	-- A short string read after a long one does not see stale bytes.
	ASSERT_EQ(U.Echo("ok"), "ok")
end

-- Invalid UTF-8 (GBK text, binary data) reads as U+FFFD and never raises.
function CMyTestCaseLuaCallCS.CaseUtf8InvalidBytesBecomeReplacementChars(self)
    self.count = 1 + self.count
	local U = CS.Utf8StringHelpers
	ASSERT_EQ(U.CodeUnits("\255"), "FFFD")
	ASSERT_EQ(U.CodeUnits("a\255b"), "0061 FFFD 0062")
	ASSERT_EQ(U.Echo("a\255b"), "a\239\191\189b")
	-- "ni hao" in GBK: decoders may differ in how many U+FFFD they write.
	local gbk = U.CodeUnits("\196\227\186\195")
	ASSERT_TRUE(gbk ~= "")
	ASSERT_EQ((string.gsub(gbk, "FFFD ?", "")), "")
	local binary = string.char(0, 1, 128, 255, 254, 192, 128, 65)
	local units = U.CodeUnits(binary)
	ASSERT_EQ(string.sub(units, 1, 14), "0000 0001 FFFD")
	ASSERT_EQ(string.sub(units, -4), "0041")
	-- byte[] parameters still get the raw bytes.
	ASSERT_EQ(U.BytesHex(binary), "00 01 80 FF FE C0 80 41")
	-- The message is followed by a stack traceback.
	local message = U.ErrorMessage(function() error("bad \255 byte", 0) end)
	ASSERT_EQ(string.sub(message, 1, 13), "bad \239\191\189 byte\n")
	ASSERT_EQ(U.GetJoin()("a", "\255", 1), "0061 FFFD 0031")
	print("print of invalid UTF-8: \255\254")
end

-- Strings carry their length, so an embedded NUL does not end them.
function CMyTestCaseLuaCallCS.CaseUtf8EmbeddedNul(self)
    self.count = 1 + self.count
	local U = CS.Utf8StringHelpers
	ASSERT_EQ(U.Length("a\0b"), 3)
	ASSERT_EQ(U.CodeUnits("a\0b"), "0061 0000 0062")
	if CS.BridgeHardening.Defines.NativeLuaPushString() then
		-- NATIVE_LUA_PUSHSTRING pushes C# strings with lua_pushstring, which
		-- ends them at the first NUL; Lua to C# keeps the length.
		ASSERT_EQ(U.WithNul, "x")
		ASSERT_EQ(U.Echo("a\0" .. Utf8Turkish), "a")
	else
		ASSERT_EQ(U.WithNul, "x\0y")
		ASSERT_EQ(#U.WithNul, 3)
		ASSERT_EQ(U.Echo("a\0" .. Utf8Turkish), "a\0" .. Utf8Turkish)
	end
	ASSERT_EQ(U.BytesHex("a\0b"), "61 00 62")
	-- C# writes U+FFFD for a lone surrogate.
	ASSERT_EQ(U.LoneSurrogate, "a\239\191\189b")
end

-- Member names with Turkish and Chinese letters reach Lua as UTF-8, through
-- generated, emitted and reflection wrappers (Utf8Names, Utf8Enum).
local Utf8NameSehir = "\197\158ehir"
local Utf8NameShuliang = "\230\149\176\233\135\143"
local Utf8NameAgirlik = "A\196\159\196\177rl\196\177k"
local Utf8NameMingzi = "\229\144\141\229\173\151"
local Utf8NameCarp = "\195\135arp"
local Utf8NameJiabei = "\229\138\160\229\128\141"
local Utf8NameSimdi = "\197\158imdi"
local Utf8NameZhongwen = "\228\184\173\230\150\135"
-- 120 CJK characters, longer than the 256-byte name buffer.
local Utf8NameLongField = string.rep("\233\149\191", 120)
local Utf8NameLongMethod = string.rep("\229\174\189", 120)

local function CheckUtf8Names(o)
	ASSERT_EQ(o[Utf8NameSehir], 1)
	ASSERT_EQ(o[Utf8NameShuliang], 2)
	ASSERT_EQ(o[Utf8NameLongField], 3)
	o[Utf8NameSehir] = 10
	ASSERT_EQ(o[Utf8NameSehir], 10)
	o[Utf8NameLongField] = 30
	ASSERT_EQ(o[Utf8NameLongField], 30)
	o[Utf8NameAgirlik] = "kg"
	ASSERT_EQ(o[Utf8NameAgirlik], "kg")
	o[Utf8NameMingzi] = Utf8NameZhongwen
	ASSERT_EQ(o[Utf8NameMingzi], Utf8NameZhongwen)
	ASSERT_EQ(o[Utf8NameCarp](o, 5), 15)
	ASSERT_EQ(o[Utf8NameJiabei](o, 5), 10)
	ASSERT_EQ(o[Utf8NameLongMethod](o), 4)
end

function CMyTestCaseLuaCallCS.CaseUtf8MemberNames(self)
    self.count = 1 + self.count
	local N = CS.Utf8Names
	CheckUtf8Names(N())
	local static = Utf8NameLongField .. "Static"
	ASSERT_EQ(N[static], 5)
	N[static] = 50
	ASSERT_EQ(N[static], 50)
	N[static] = 5
end

function CMyTestCaseLuaCallCS.CaseUtf8MemberNamesByReflection(self)
    self.count = 1 + self.count
	CheckUtf8Names(CS.Utf8Names.MakeReflect())
end

function CMyTestCaseLuaCallCS.CaseUtf8EnumNames(self)
    self.count = 1 + self.count
	local E, value = CS.Utf8Enum, CS.Utf8Names.EnumValue
	ASSERT_EQ(value(E[Utf8NameSimdi]), 1)
	ASSERT_EQ(value(E[Utf8NameZhongwen]), 2)
	ASSERT_EQ(value(E[Utf8NameLongField]), 3)
	ASSERT_EQ(value(E.__CastFrom(Utf8NameSimdi)), 1)
	ASSERT_EQ(value(E.__CastFrom(Utf8NameZhongwen)), 2)
	ASSERT_EQ(value(E.__CastFrom(Utf8NameLongField)), 3)
	ASSERT_EQ(value(E.__CastFrom(3)), 3)
end
