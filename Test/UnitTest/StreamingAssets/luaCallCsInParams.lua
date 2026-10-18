-- C# `in` and C# 12 `ref readonly` parameters (readonly by reference) are
-- plain inputs for Lua: calls return no extra value for them (Lua callbacks
-- with such parameters are covered by Test/UnityMatrix, which compiles them
-- with Roslyn). [In] ref without IsReadOnlyAttribute or
-- RequiresLocationAttribute stays a normal ref.
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

local function point(x, y) return CS.InParamPoint(x, y) end

-- InRefGen goes through generated or emitted wrappers, InRefReflect through
-- reflection or emitted wrappers.
for _, name in ipairs({ "InRefGen", "InRefReflect" }) do
    CMyTestCaseLuaCallCSInParams["CaseInRefIsStillRef" .. name] = function(self)
        local r = pack(CS[name].Bump(point(3, 4)))
        ASSERT_EQ(r.n, 2)
        ASSERT_EQ(r[1], 8)
        ASSERT_EQ(r[2].X, 4)
        ASSERT_EQ(r[2].Y, 4)
    end
end

function CMyTestCaseLuaCallCSInParams.CaseInRefCallbackIsStillRef(self)
    local ret = CS.InRefCaller.Call(function(counter) return counter * 2, counter + 1 end)
    ASSERT_EQ(ret, "20,11")
end

function CMyTestCaseLuaCallCSInParams.CaseInOnlyOverload(self)
    local r = pack(CS.InParams.Real.Sum(point(3, 4)))
    ASSERT_EQ(r.n, 1)
    ASSERT_EQ(r[1], 7)
end

function CMyTestCaseLuaCallCSInParams.CaseInBetweenRefAndOut(self)
    -- Scale(in factor, ref value, out old): returns result, value, old.
    local r = pack(CS.InParams.Real.Scale(3, 5))
    ASSERT_EQ(r.n, 3)
    ASSERT_EQ(r[1], 3)
    ASSERT_EQ(r[2], 15)
    ASSERT_EQ(r[3], 5)
end

function CMyTestCaseLuaCallCSInParams.CaseByValueAndInPair(self)
    local r = pack(CS.InParams.Real.Product(point(3, 4)))
    ASSERT_EQ(r.n, 1)
    ASSERT_EQ(r[1], 12)
end

function CMyTestCaseLuaCallCSInParams.CaseInConstructorAndMethod(self)
    local r = pack(CS.InParams.Real(5))
    ASSERT_EQ(r.n, 1)
    local obj = r[1]
    ASSERT_EQ(obj.Offset, 5)
    r = pack(obj:Add(point(3, 4)))
    ASSERT_EQ(r.n, 1)
    ASSERT_EQ(r[1], 12)
end

-- C# 12 `ref readonly` parameters are inputs like `in`. RefReadonlyParams.Gen
-- goes through generated or emitted wrappers, Reflect through reflection or
-- emitted wrappers.
for _, name in ipairs({ "Gen", "Reflect" }) do
    local function class() return CS.RefReadonlyParams[name] end

    CMyTestCaseLuaCallCSInParams["CaseRefReadonlyOnlyOverload" .. name] = function(self)
        local r = pack(class().Sum(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 7)
    end

    CMyTestCaseLuaCallCSInParams["CaseRefReadonlyBetweenRefAndOut" .. name] = function(self)
        -- Scale(ref readonly factor, ref value, out old): returns result, value, old.
        local r = pack(class().Scale(3, 5))
        ASSERT_EQ(r.n, 3)
        ASSERT_EQ(r[1], 3)
        ASSERT_EQ(r[2], 15)
        ASSERT_EQ(r[3], 5)
    end

    CMyTestCaseLuaCallCSInParams["CaseByValueAndRefReadonlyPair" .. name] = function(self)
        local r = pack(class().Product(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 12)
    end

    CMyTestCaseLuaCallCSInParams["CaseRefReadonlyConstructorAndMethod" .. name] = function(self)
        local r = pack(class()(5))
        ASSERT_EQ(r.n, 1)
        local obj = r[1]
        ASSERT_EQ(obj.Offset, 5)
        r = pack(obj:Add(point(3, 4)))
        ASSERT_EQ(r.n, 1)
        ASSERT_EQ(r[1], 12)
    end
end

-- An interface indexer with an `in` key (IInKeyIndexer): the key is an input
-- of get_Item and set_Item, and the getter's only result is the value. The
-- key is `in` in the Roslyn builds (coverage_cs_dotnet.py, run_managed.py
-- --source-gen) and by value under mcs and in Unity.
function CMyTestCaseLuaCallCSInParams.CaseInKeyInterfaceIndexer(self)
    local store = {}
    local t = {}
    function t:get_Item(k) return store[k] end
    function t:set_Item(k, v) store[k] = v end
    print("IInKeyIndexer key: " .. CS.InKeyIndexerCaller.KeyKind())
    ASSERT_EQ(CS.InKeyIndexerCaller.Use(t), "40,4")
    ASSERT_EQ(store[4], 40)
end
