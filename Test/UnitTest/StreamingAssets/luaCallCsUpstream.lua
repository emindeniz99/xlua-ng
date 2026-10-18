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

-- XLUA_RESTRICT_PRIVATE_ACCESS without HOTFIX_ENABLE leaves out
-- xlua.private_accessible (luaBridgeHardening.lua checks that).
local function noPrivateAccess()
    return CS.BridgeHardening.Defines.RestrictPrivateAccess() and not CS.BridgeHardening.Defines.HotfixEnabled()
end

-- Inherited from upstream, not reported there: private_accessible listed each
-- method once per non-public method of its name (Upstream.PrivateOverloads).
function CMyTestCaseLuaCallCSUpstream.CasePrivateOverloadsListedOnce(self)
    if noPrivateAccess() then return end
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Overloads("Pick"), "3 Boolean,Double,PrivateOverloads")
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Overloads("Single"), "1 Int32;Int32|1 String")
end

function CMyTestCaseLuaCallCSUpstream.CasePrivateOverloadsPick(self)
    if noPrivateAccess() then return end
    xlua.private_accessible(CS.Upstream.PrivateOverloads)
    local o = CS.Upstream.PrivateOverloads()
    ASSERT_EQ(o:Pick(1.5), "double")
    ASSERT_EQ(o:Pick(2), "double")
    ASSERT_EQ(o:Pick(true), "bool")
    ASSERT_EQ(o:Pick(o), "object")
end

function CMyTestCaseLuaCallCSUpstream.CasePrivateSingleOverloadUnchecked(self)
    if noPrivateAccess() then return end
    xlua.private_accessible(CS.Upstream.PrivateOverloads)
    local o = CS.Upstream.PrivateOverloads()
    ASSERT_EQ(o:Single("a"), "s:a")
    ASSERT_EQ(o:Single(5), "s:5")
    ASSERT_EQ(CS.Upstream.PrivateOverloads.Single(1, 2), "ij")
end

-- Tencent/xLua#1118: each LuaEnv without generated code emitted a new
-- XLuaCodeEmit dynamic assembly, never unloaded, so with domain reload
-- disabled they added up. One assembly serves every env in the process
-- (none with generated delegate bridges); later envs reuse its wrappers and
-- bridges. Test types: Upstream.EmitCache.
function CMyTestCaseLuaCallCSUpstream.CaseEmitCacheOneAssembly(self)
    ASSERT_EQ(CS.Upstream.EmitCache.Assemblies(), CS.Upstream.EmitCache.Expected())
end

function CMyTestCaseLuaCallCSUpstream.CaseEmitCacheSequentialEnvs(self)
    ASSERT_EQ(CS.Upstream.EmitCache.Sequential(3), "")
end

function CMyTestCaseLuaCallCSUpstream.CaseEmitCacheTwoEnvsAlive(self)
    ASSERT_EQ(CS.Upstream.EmitCache.TwoAlive(), "")
end

function CMyTestCaseLuaCallCSUpstream.CaseEmitCacheEnvsOnThreads(self)
    ASSERT_EQ(CS.Upstream.EmitCache.Threads(4), "")
end

-- Without generated code each LuaEnv scanned every loaded type for
-- [CSharpCallLua], most of its creation time. The scan is kept for the
-- process while the loaded assemblies stay the same, and made again when one
-- is loaded. Test types: Upstream.EnvScanCache.
function CMyTestCaseLuaCallCSUpstream.CaseEnvScanReused(self)
    ASSERT_EQ(CS.Upstream.EnvScanCache.Reused(3), "")
end

function CMyTestCaseLuaCallCSUpstream.CaseEnvScanSeesLaterAssembly(self)
    ASSERT_EQ(CS.Upstream.EnvScanCache.LaterAssembly(), "")
end

-- Each LuaEnv reflected over System.RuntimeType when it was created, and
-- over every other type it wraps by reflection. What reflection finds is
-- kept for the process; every env binds the same members. Test types:
-- Upstream.EnvReflectionCache.
function CMyTestCaseLuaCallCSUpstream.CaseEnvReflectionReused(self)
    ASSERT_EQ(CS.Upstream.EnvReflectionCache.Reused(3), "")
end

function CMyTestCaseLuaCallCSUpstream.CaseEnvReflectionOnThreads(self)
    ASSERT_EQ(CS.Upstream.EnvReflectionCache.Threads(4), "")
end

-- Tencent/xLua#924: Generate Code failed on ApplySelfTo<T>(this T, Action<T>).
-- Generated code leaves it out; reflection and emitted wrappers keep it.
function CMyTestCaseLuaCallCSUpstream.CaseGenericExtensionOverGenericType(self)
    local o = CS.Upstream.ApplyTarget()
    o.Value = 4
    ASSERT_EQ(o:Twice(), 8)
    local ok, apply = pcall(function() return o.ApplySelfTo end)
    if ok and apply ~= nil then
        ASSERT_EQ(o:ApplySelfTo(function(t) t.Value = t.Value + 1 end).Value, 5)
    end
end

-- Tencent/xLua#1186 and #1141: a [GCOptimize] struct with bool, char, IntPtr
-- and UIntPtr fields. Its values keep their fields on every path, and
-- generated code copies them without allocating (they were boxed).
function CMyTestCaseLuaCallCSUpstream.CaseGCOptimizeBoolCharPointerFields(self)
    local S = CS.StaticPusherStructFlags
    local v = S.Make(true, 65, 1234)
    ASSERT_EQ(v.boolVar, true)
    ASSERT_EQ(v.charVar, 65)
    ASSERT_EQ(S.Ptr(v), 1234 + 1235)
    ASSERT_EQ(S.Describe(v), "True,A,1234,1235")
    ASSERT_EQ(S.Describe(S.Make(false, 66, -5)), "False,B,-5,18446744073709551612")
    ASSERT_EQ(S.Describe({ boolVar = true, charVar = 67 }), "True,C,0,0")
    if CS.XLua.DelegateBridge.Gen_Flag then
        local bytes = S.Allocated(function(n)
            local sum = 0
            for i = 1, n do
                sum = sum + S.Ptr(S.Make(true, 65, i))
            end
            return sum
        end, 2000)
        ASSERT_EQ(bytes < 2000 and "under 2000 bytes" or (tostring(bytes) .. " bytes"), "under 2000 bytes")
    end
end

-- Tencent/xLua#1187: the generated operator wrappers blamed the right-hand
-- operand for every mismatch; Lua calls the metamethod of either operand.
-- (Reflection and emitted wrappers name the operator method instead.)
function CMyTestCaseLuaCallCSUpstream.CaseOperatorErrorNamesTheOperand(self)
    local o = CS.LuaTestObj()
    local ok, err = pcall(function() return true - o end)
    ASSERT_EQ(ok, false)
    local ok2, err2 = pcall(function() return o - true end)
    ASSERT_EQ(ok2, false)
    if CS.XLua.DelegateBridge.Gen_Flag then
        ASSERT_EQ(string.match(tostring(err), "invalid arguments to %a+ hand of %- operator"), "invalid arguments to left hand of - operator")
        ASSERT_EQ(string.match(tostring(err2), "invalid arguments to %a+ hand of %- operator"), "invalid arguments to right hand of - operator")
    end
end

-- Tencent/xLua#1081 and #1179: chunk names, table paths and C# error
-- messages with non-ASCII characters reach Lua as UTF-8 outside Unity too.
function CMyTestCaseLuaCallCSUpstream.CaseUtf8StringParameters(self)
    ASSERT_EQ(CS.Upstream.Utf8Marshal.Check(), "")
    local ok, err = pcall(CS.Upstream.Utf8Marshal.Throw)
    ASSERT_EQ(ok, false)
    ASSERT_EQ(string.find(tostring(err), "\228\184\173\230\150\135\233\148\153\232\175\175", 1, true) ~= nil, true)
end

-- Tencent/xLua#1189: with debug.sethook(h, "cr"), calling a function twice
-- on one line (`_ = f() and f().x`) must report as many returns as calls,
-- for a Lua function and for a C# method.
function CMyTestCaseLuaCallCSUpstream.CaseReturnHookOnRepeatedCalls(self)
    if not debug.sethook then return end
    local t = { x = 1 }
    local function f() return t end
    local abs = CS.System.Math.Abs
    local counts = {}
    local function hook(event)
        local info = debug.getinfo(2, "f")
        local name = info and (info.func == f and "f" or info.func == abs and "abs")
        if name then
            local key = name .. " " .. event
            counts[key] = (counts[key] or 0) + 1
        end
    end
    -- LuaJIT calls no hooks in compiled code, and this test may run inside a
    -- trace of the test runner's loop.
    if jit and jit.off then jit.flush() jit.off() end
    debug.sethook(hook, "cr")
    local _ = f() and f().x
    local _ = abs(-1) and abs(-2) + 1
    debug.sethook()
    if jit and jit.on then jit.on() end
    ASSERT_EQ(counts["f call"], 2)
    ASSERT_EQ(counts["f return"], 2)
    ASSERT_EQ(counts["abs call"], 2)
    -- LuaJIT reports no returns from C functions at all (os.time, for one).
    if not jit then ASSERT_EQ(counts["abs return"], 2) end
end

-- Tencent/xLua#741: references queued by LuaBase finalizers are released at
-- the next Tick (the queue is now a lock-free list).
function CMyTestCaseLuaCallCSUpstream.CaseFinalizerQueueReleasesAll(self)
    ASSERT_EQ(CS.Upstream.GCQueue.Check(3000), "")
end

-- Tencent/xLua#1082: an error inside an xlua.util.cs_generator coroutine
-- kept only its message; the coroutine's traceback now follows it.
function CMyTestCaseLuaCallCSUpstream.CaseGeneratorErrorKeepsTraceback(self)
    local ok, util = pcall(require, 'xlua.util')
    if not ok then return end -- XLUA_NO_UTIL
    local function inner_generator_failure() error("boom") end
    local g = util.cs_generator(function()
        coroutine.yield(1)
        inner_generator_failure()
    end)
    ASSERT_EQ(g:MoveNext(), true)
    ASSERT_EQ(g.Current, 1)
    local ok2, err = pcall(g.MoveNext, g)
    ASSERT_EQ(ok2, false)
    ASSERT_EQ(string.find(err, "boom", 1, true) ~= nil, true)
    ASSERT_EQ(string.find(err, "inner_generator_failure", 1, true) ~= nil, true)
    -- A non-string error value is raised unchanged.
    local value = {}
    local g2 = util.cs_generator(function() error(value) end)
    local ok3, err3 = pcall(g2.MoveNext, g2)
    ASSERT_EQ(ok3, false)
    ASSERT_EQ(err3 == value, true)
end

-- Collects mismatches so that one case reports all of them.
local function upstreamFailures()
    local failures = {}
    local function check(label, got, expected)
        if got ~= expected then
            table.insert(failures, label .. ": got " .. tostring(got) .. ", expected " .. tostring(expected))
        end
    end
    -- "" when f(...) raises an error containing each of the texts.
    local function raises(label, f, texts, ...)
        local ok, err = pcall(f, ...)
        if ok then
            table.insert(failures, label .. ": no error")
            return
        end
        for _, text in ipairs(texts) do
            if not string.find(tostring(err), text, 1, true) then
                table.insert(failures, label .. ": error without '" .. text .. "': " .. tostring(err))
            end
        end
    end
    return failures, check, raises
end

-- Tencent/xLua#918: a Lua table where a UnityEngine.Object type is expected
-- raises instead of making a new object (outside Unity, stand-in types:
-- UpstreamForLuaCallCS.cs). Parameters, fields and properties that Lua sets
-- name the argument, as luaL_argerror does.
local unityObjectSlotClasses = {{"UnityObjectSlots", CS.Upstream.UnityObjectSlots},
                                {"UnityObjectSlotsReflect", CS.Upstream.UnityObjectSlotsReflect}}

function CMyTestCaseLuaCallCSUpstream.CaseUnityObjectParameterRefusesTable(self)
    local failures, check, raises = upstreamFailures()
    local go = CS.Upstream.UnityObjectSlots.Make("a")
    for _, class in ipairs(unityObjectSlotClasses) do
        local cname, C = class[1], class[2]
        check(cname .. ".Name(go)", C.Name(go), "go:a")
        check(cname .. ".Name(nil)", C.Name(nil), "null")
        raises(cname .. ".Name({})", C.Name, {"bad argument #1", "UnityEngine.GameObject expected, got table"}, {})
        raises(cname .. ".Name({name = 'b'})", C.Name, {"UnityEngine.GameObject expected, got table"}, {name = "b"})
        -- No public default constructor: a table passed null before.
        raises(cname .. ".Part({})", C.Part, {"bad argument #1", "UnityEngine.Component expected, got table"}, {})
        check(cname .. ".Part(nil)", C.Part(nil), "null")
        -- The overload check does not take the table for the GameObject.
        check(cname .. ".Pick({})", C.Pick({}), "LuaTable")
        check(cname .. ".Pick(go)", C.Pick(go), "GameObject")
    end
    local C = CS.Upstream.UnityObjectSlots
    raises("Asset({})", C.Asset, {"bad argument #1", "UnityEngine.ScriptableObject expected, got table"}, {})
    check("Asset(nil)", C.Asset(nil), "null")
    local o = C()
    check("o:Self(go)", o:Self(go), "go:a")
    -- Not a tail call, and only "bad argument #": the number luaL_argerror gives
    -- depends on whether Lua knows the call was a method call (LuaJIT may not).
    raises("o:Self({})", function() local r = o:Self({}) return r end, {"bad argument #", "UnityEngine.GameObject expected, got table"})
    ASSERT_EQ(table.concat(failures, "\n"), "")
end

function CMyTestCaseLuaCallCSUpstream.CaseUnityObjectFieldRefusesTable(self)
    local failures, check, raises = upstreamFailures()
    local go = CS.Upstream.UnityObjectSlots.Make("f")
    for _, class in ipairs(unityObjectSlotClasses) do
        local cname, C = class[1], class[2]
        for _, member in ipairs({"Field", "Property"}) do
            local label = cname .. "." .. member
            C[member] = go
            check(label .. " = go", C.Name(C[member]), "go:f")
            raises(label .. " = {}", function() C[member] = {} end, {"UnityEngine.GameObject expected, got table"})
            check(label .. " unchanged", C.Name(C[member]), "go:f")
            C[member] = nil
            check(label .. " = nil", C.Name(C[member]), "null")
        end
    end
    -- Other classes still convert from a table, field by field; a GameObject
    -- field in it refuses a table.
    local C = CS.Upstream.UnityObjectSlots
    check("Plain{Count = 2}", C.Plain({Count = 2}), "2:null")
    check("Plain{Count = 1, Go = go}", C.Plain({Count = 1, Go = go}), "1:go:f")
    raises("Plain{Go = {}}", C.Plain, {"UnityEngine.GameObject expected, got table"}, {Go = {}})
    ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Values C# reads from Lua raise an InvalidCastException without the argument.
function CMyTestCaseLuaCallCSUpstream.CaseUnityObjectResultRefusesTable(self)
    local failures, check = upstreamFailures()
    local C = CS.Upstream.UnityObjectSlots
    local go = C.Make("r")
    local refused = "UnityEngine.GameObject expected, got table"
    check("delegate result go", C.Result(function() return go end), "go:r")
    check("delegate result {}", C.Result(function() return {} end), refused)
    check("interface result go", C.InterfaceResult({Make = function(self) return go end}), "go:r")
    check("interface result {}", C.InterfaceResult({Make = function(self) return {} end}), refused)
    check("LuaTable.Get go", C.TableGet({go = go}), "go:r")
    check("LuaTable.Get {}", C.TableGet({go = {}}), refused)
    check("LuaTable.Cast", C.TableCast({}), refused)
    ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Tencent/xLua#751: a table that sets none of a struct's fields raises in every
-- preset (the opt-in XLUA_STRICT_STRUCT_TABLES until 2026-10-19; upstream gave the
-- default value). Partial tables convert, and classes are not checked.
function CMyTestCaseLuaCallCSUpstream.CaseStrictStructTables(self)
    local failures, check, raises = upstreamFailures()
    local function none(label, f, arg, typeText)
        raises(label, f, {"table sets none of the fields of " .. typeText}, arg)
    end
    for _, class in ipairs({{"StrictStructs", CS.Upstream.StrictStructs}, {"StrictStructsReflect", CS.Upstream.StrictStructsReflect}}) do
        local cname, P = class[1], class[2]
        check(cname .. ".Point{x = 1}", P.Point({x = 1}), "1,0")
        check(cname .. ".Point{y = 2, z = 3}", P.Point({y = 2, z = 3}), "0,2")
        none(cname .. ".Point{}", P.Point, {}, "Upstream.StrictPoint (x, y)")
        none(cname .. ".Point{X = 1}", P.Point, {X = 1}, "Upstream.StrictPoint (x, y)")
        check(cname .. ".Packed{x = 4}", P.Packed({x = 4}), "4,0")
        none(cname .. ".Packed{}", P.Packed, {}, "Upstream.StrictPacked (x, y)")
        none(cname .. ".Packed{X = 4}", P.Packed, {X = 4}, "Upstream.StrictPacked (x, y)")
    end
    local S = CS.Upstream.StrictStructs
    check("Table{count = 3}", S.Table({count = 3}), "3,0")
    none("Table{}", S.Table, {}, "PackAsTableStruct (count, ratio)")
    check("Holder{}", S.Holder({}), "0")
    ASSERT_EQ(table.concat(failures, "\n"), "")
end

-- Tencent/xLua#1182: xLua's searchers (custom loaders, builtin modules, the
-- embedded modules) give require the loader data. Lua 5.4 and 5.5 pass it to
-- the module and return it as require's second value; Lua 5.3 passes it but
-- returns only the module; LuaJIT passes and returns only the name.
function CMyTestCaseLuaCallCSUpstream.CaseRequireLoaderData(self)
    local got = CS.Upstream.RequireData.Run([[
        local m, data = require 'vmod'
        local b, bdata = require 'vbuiltin'
        local ok, util, udata = pcall(require, 'xlua.util')
        return table.concat({m.n, tostring(m.name), tostring(m.data), tostring(data),
            string.format('%d', b), tostring(bdata), ok and tostring(udata) or 'none'}, '|')
    ]])
    local utilData = "xlua/util.lua"
    local expected
    if jit then
        expected = "1|vmod|nil|nil|1|nil|"
        utilData = "nil"
    elseif _VERSION == "Lua 5.3" then
        expected = "2|vmod|virtual/vmod.lua|nil|2|nil|"
        utilData = "nil"
    else
        expected = "2|vmod|virtual/vmod.lua|virtual/vmod.lua|2|:preload:|"
    end
    -- XLUA_NO_UTIL (minimal) leaves xlua.util out.
    if string.sub(got, -5) == "|none" then utilData = "none" end
    ASSERT_EQ(got, expected .. utilData)
end
