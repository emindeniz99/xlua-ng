-- Regression tests for upstream Tencent/xLua issues (UPSTREAM.md at the
-- repository root). Test types: Test/UnitTest/xLuaTest/LuaCallCS/UpstreamForLuaCallCS.cs.
require("luaunit_compat")

CMyTestCaseLuaCallCSUpstream = TestCase:new()
function CMyTestCaseLuaCallCSUpstream:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestCaseLuaCallCSUpstream.SetUpTestCase(self) end
function CMyTestCaseLuaCallCSUpstream.TearDownTestCase(self) end
function CMyTestCaseLuaCallCSUpstream.SetUp(self) end
function CMyTestCaseLuaCallCSUpstream.TearDown(self) end

-- Tencent/xLua#991: optional Nullable<T> parameters.
function CMyTestCaseLuaCallCSUpstream.CaseNullableDefaultTypeLoads(self)
    ASSERT_EQ(CS.Upstream.NullableDefaults.Plain(), 1)
end

function CMyTestCaseLuaCallCSUpstream.CaseNullableDefaultValue(self)
    ASSERT_EQ(CS.Upstream.NullableDefaults.Zero(), "v0")
    ASSERT_EQ(CS.Upstream.NullableDefaults.Zero(3), "v3")
    ASSERT_EQ(CS.Upstream.NullableDefaults.Seven("a"), "av7")
    ASSERT_EQ(CS.Upstream.NullableDefaults.Seven("a", 2), "av2")
end

function CMyTestCaseLuaCallCSUpstream.CaseNullableDefaultNull(self)
    ASSERT_EQ(CS.Upstream.NullableDefaults.Null(), "null")
    ASSERT_EQ(CS.Upstream.NullableDefaults.Null(4), "v4")
end

-- Tencent/xLua#1105: `in` parameter with a default value.
function CMyTestCaseLuaCallCSUpstream.CaseInDefaultTypeLoads(self)
    ASSERT_EQ(CS.Upstream.InDefaults.Plain(), 1)
end

function CMyTestCaseLuaCallCSUpstream.CaseInDefaultValue(self)
    local r = { CS.Upstream.InDefaults.Five() }
    ASSERT_EQ(#r, 1)
    ASSERT_EQ(r[1], 50)
    r = { CS.Upstream.InDefaults.Five(7) }
    ASSERT_EQ(#r, 1)
    ASSERT_EQ(r[1], 70)
end
