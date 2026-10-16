-- C# `in` parameters (readonly by reference) are plain inputs for Lua: calls
-- return no extra value for them and callbacks get nothing written back.
-- Test types: Test/UnitTest/xLuaTest/LuaCallCS/InParamsForLuaCallCS.cs.
require("luaunit_compat")

CMyTestCaseLuaCallCSInParams = TestCase:new()
function CMyTestCaseLuaCallCSInParams:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestCaseLuaCallCSInParams.SetUpTestCase(self) end
function CMyTestCaseLuaCallCSInParams.TearDownTestCase(self) end
function CMyTestCaseLuaCallCSInParams.SetUp(self) end
function CMyTestCaseLuaCallCSInParams.TearDown(self) end

local function pack(...)
    return { n = select("#", ...), ... }
end

-- InParamsGen goes through generated or emitted wrappers, InParamsReflect
-- through reflection or emitted wrappers; both must behave the same.
for _, name in ipairs({ "InParamsGen", "InParamsReflect" }) do
    local function class() return CS[name] end
    local function point(x, y) return CS.InParamPoint(x, y) end

    CMyTestCaseLuaCallCSInParams["CaseInOnlyOverload" .. name] = function(self)
        local r = pack(class().Sum(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 7)
    end

    CMyTestCaseLuaCallCSInParams["CaseInBetweenRefAndOut" .. name] = function(self)
        -- Scale(in factor, ref value, out old): returns result, value, old.
        local r = pack(class().Scale(3, 5))
        ASSERT_EQ(r.n, 3)
        ASSERT_EQ(r[1], 3)
        ASSERT_EQ(r[2], 15)
        ASSERT_EQ(r[3], 5)
    end

    CMyTestCaseLuaCallCSInParams["CaseByValueAndInPair" .. name] = function(self)
        local r = pack(class().Product(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 12)
    end

    CMyTestCaseLuaCallCSInParams["CaseInConstructorAndMethod" .. name] = function(self)
        local r = pack(class()(5))
        ASSERT_EQ(r.n, 1)
        local obj = r[1]
        ASSERT_EQ(obj.Offset, 5)
        r = pack(obj:Add(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 12)
    end
end

function CMyTestCaseLuaCallCSInParams.CaseDelegateWithInParam(self)
    local argc
    local ret = CS.InParamsCaller.CallDelegate(function(...)
        argc = select("#", ...)
        local p, counter = ...
        -- Result and the new counter; p is an input and gets nothing back.
        return p.X + p.Y, counter + 1
    end)
    ASSERT_EQ(argc, 2)
    ASSERT_EQ(ret, "7,11,3,4")
end

function CMyTestCaseLuaCallCSInParams.CaseInterfaceWithInParam(self)
    local argc
    local ret = CS.InParamsCaller.CallInterface({
        Measure = function(...)
            argc = select("#", ...)
            local _, p, counter = ...
            return p.X * p.Y, counter + 2
        end,
    })
    ASSERT_EQ(argc, 3)
    ASSERT_EQ(ret, "12,12,3,4")
end
