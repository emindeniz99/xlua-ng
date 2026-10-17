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

-- Tencent/xLua#1153: C# that pushes more than LUA_MINSTACK values while Lua
-- calls it. Each case runs in a new LuaEnv (StackRoom.Fresh), whose stack
-- has its initial size; the ASan build catches a write past it.
function CMyTestCaseLuaCallCSUpstream.CaseStackRoomLuaFunctionCall(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        return CS.Upstream.StackRoom.CallBack(function(...) return select('#', ...) end, 60)
    ]]), 60)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomDelegate(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        return CS.Upstream.StackRoom.CallMany(function(...)
            local sum = 0
            for i = 1, select('#', ...) do sum = sum + select(i, ...) end
            return select('#', ...) * 10000 + sum
        end)
    ]]), 601770)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomInterface(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        return CS.Upstream.StackRoom.CallInterface({ Sum = function(self, ...)
            local sum = 0
            for i = 1, select('#', ...) do sum = sum + select(i, ...) end
            return select('#', ...) * 10000 + sum
        end })
    ]]), 601770)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomManyOuts(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        local r = { CS.Upstream.StackRoom.ManyOuts() }
        return #r * 1000 + r[100]
    ]]), 100099)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomRegisterTypeAfterOuts(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        local r = { CS.Upstream.StackRoom.OutsThenNewType() }
        return #r * 100 + r[15] + r[16].V
    ]]), 1621)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomManyOutsGen(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        local r = { CS.Upstream.StackRoomGen.ManyOuts() }
        return #r * 1000 + r[1] + r[101]
    ]]), 101098)
end

-- 20 out values that are C# objects. The wrappers asked for one slot per
-- value, but pushing an object also needs the userdata cache table, so the
-- last pushes went past the room Lua gave (LUA_USE_APICHECK reports it).
function CMyTestCaseLuaCallCSUpstream.CaseStackRoomObjectOuts(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        local r = { CS.Upstream.StackRoom.ObjectOuts() }
        return #r * 100 + r[1].V + r[20].V
    ]]), 2019)
end

function CMyTestCaseLuaCallCSUpstream.CaseStackRoomObjectOutsGen(self)
    ASSERT_EQ(CS.Upstream.StackRoom.Fresh([[
        local r = { CS.Upstream.StackRoomGen.ObjectOuts() }
        return #r * 100 + r[1] + r[21].V
    ]]), 2139)
end

-- Inherited from upstream, not reported there: private_accessible listed each
-- method once per non-public method of its name (Upstream.PrivateOverloads).
function CMyTestCaseLuaCallCSUpstream.CasePrivateOverloadsListedOnce(self)
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Overloads("Pick"), "3 Boolean,Double,PrivateOverloads")
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Overloads("Single"), "1 Int32;Int32|1 String")
end

function CMyTestCaseLuaCallCSUpstream.CasePrivateOverloadsPick(self)
    xlua.private_accessible(CS.Upstream.PrivateOverloads)
    local o = CS.Upstream.PrivateOverloads()
    ASSERT_EQ(o:Pick(1.5), "double")
    ASSERT_EQ(o:Pick(2), "double")
    ASSERT_EQ(o:Pick(true), "bool")
    ASSERT_EQ(o:Pick(o), "object")
end

function CMyTestCaseLuaCallCSUpstream.CasePrivateSingleOverloadUnchecked(self)
    xlua.private_accessible(CS.Upstream.PrivateOverloads)
    local o = CS.Upstream.PrivateOverloads()
    ASSERT_EQ(o:Single("a"), "s:a")
    ASSERT_EQ(o:Single(5), "s:5")
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Single(1, 2), "ij")
end
