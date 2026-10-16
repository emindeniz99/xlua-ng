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

-- Tencent/xLua#1114: ref struct parameters, returns and properties are left
-- out; the overloads with ordinary types still work.
function CMyTestCaseLuaCallCSUpstream.CaseSpanOverloadsBcl(self)
    ASSERT_EQ(CS.System.IO.Path.GetExtension("a.txt"), ".txt")
    ASSERT_EQ(CS.System.IO.Path.GetFileName("dir/b.lua"), "b.lua")
    ASSERT_EQ(CS.System.IO.Path.HasExtension("c.txt"), true)
    ASSERT_EQ(CS.System.Int32.Parse("42"), 42)
    -- byte[] arguments and results are Lua strings.
    ASSERT_EQ(CS.System.Convert.ToBase64String("xyz"), "eHl6")
    ASSERT_EQ(CS.System.Convert.FromBase64String("eHl6"), "xyz")
    ASSERT_EQ(CS.System.BitConverter.ToInt32("\1\0\0\0", 0), 1)
end

function CMyTestCaseLuaCallCSUpstream.CaseSpanOverloadsStatic(self)
    local T = CS.Upstream.SpanOverloads
    ASSERT_EQ(T.Len("abcd"), 4)
    ASSERT_EQ(T.AsChars, nil)
    ASSERT_EQ(T.Chars, nil)
end

function CMyTestCaseLuaCallCSUpstream.CaseSpanOverloadsInstance(self)
    local obj = CS.Upstream.SpanOverloads()
    obj.Offset = 10
    ASSERT_EQ(obj:Count("ab"), 12)
    ASSERT_EQ(obj.Bytes, nil)
end

-- Tencent/xLua#1167: a pointer-returning method is left out.
function CMyTestCaseLuaCallCSUpstream.CasePointerReturn(self)
    local T = CS.Upstream.PointerReturns
    if not T then return end -- XLUA_GENERAL only
    ASSERT_EQ(T.Plain(), 1)
    ASSERT_EQ(T.Address, nil)
end
