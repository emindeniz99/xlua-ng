-- Tests for code the suite did not run before 2026-10-18 (Test/README.md,
-- "C# coverage"). Test types: Test/UnitTest/xLuaTest/LuaCallCS/CoverageForLuaCallCS.cs.
require("luaunit_compat")

CMyTestCaseLuaCallCSCoverage = TestCase:new()
function CMyTestCaseLuaCallCSCoverage:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestCaseLuaCallCSCoverage.SetUpTestCase(self) end
function CMyTestCaseLuaCallCSCoverage.TearDownTestCase(self) end
function CMyTestCaseLuaCallCSCoverage.SetUp(self) end
function CMyTestCaseLuaCallCSCoverage.TearDown(self) end

local function expectError(pattern, f, ...)
    local ok, err = pcall(f, ...)
    ASSERT_EQ(ok, false)
    ASSERT_TRUE(string.find(tostring(err), pattern, 1, true) ~= nil)
end

-- Delegates without a bridge, built from DelegateBridge's generic methods.
function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeFuncs(self)
    local Ref = CS.Coverage.Ref
    local s = CS.Coverage.NoBridge.Funcs(
        function() return Ref("f0") end,
        function(a) return Ref("f1" .. a.Name) end,
        function(a, b) return Ref(a.Name .. b.Name) end,
        function(a, b, c) return Ref(a.Name .. b.Name .. c.Name) end,
        function(a, b, c, d) return nil end)
    ASSERT_EQ(s, "f0,f1a,ab,abc,nil")
end

function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeActions(self)
    local seen = {}
    local s = CS.Coverage.NoBridge.Actions(
        function(a) seen[#seen + 1] = a.Name end,
        function(a, b) seen[#seen + 1] = a.Name .. b.Name end,
        function(a, b, c) seen[#seen + 1] = a.Name .. b.Name .. c.Name end,
        function(a, b, c, d) seen[#seen + 1] = a.Name .. b.Name .. c.Name .. d.Name end)
    ASSERT_EQ(s, "done")
    ASSERT_EQ(table.concat(seen, ","), "a,ab,abc,abcd")
end

function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeLuaError(self)
    local s = CS.Coverage.NoBridge.Throws(
        function(a) error("func failed") end,
        function(a) error("action failed") end)
    ASSERT_EQ(s, "func;action")
end

-- A value-type result works outside Unity only; Unity builds need the
-- delegate in CSharpCallLua (getCreatorUsingGeneric).
function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeValueResult(self)
    local f = function(a) return #a.Name end
    if CS.LuaTestCommon.IsXLuaGeneral() then
        ASSERT_EQ(CS.Coverage.NoBridge.ValueResult(f), 3)
    else
        expectError("CSharpCallLua", CS.Coverage.NoBridge.ValueResult, f)
    end
end

function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeValueParam(self)
    expectError("This type must add to CSharpCallLua: Coverage.NoBridgeValueParam",
        CS.Coverage.NoBridge.ValueParam, function(a) return nil end)
end

-- More than four parameters has no generic method either.
function CMyTestCaseLuaCallCSCoverage.CaseNoBridgeFiveParams(self)
    expectError("This type must add to CSharpCallLua: Coverage.NoBridgeFive",
        CS.Coverage.NoBridge.Five, function(a, b, c, d, e) return a end)
end

-- Members in a [DoNotGen] list: lazy reflection members in generated code.
function CMyTestCaseLuaCallCSCoverage.CaseLazyMembers(self)
    local Lazy = CS.Coverage.Lazy
    local o = Lazy()
    for _ = 1, 2 do
        ASSERT_EQ(o.Field, 1)
        ASSERT_EQ(o.Property, 3)
        ASSERT_EQ(Lazy.StaticField, 2)
        ASSERT_EQ(Lazy.StaticProperty, 4)
        ASSERT_EQ(o:Method(1), 2)
        ASSERT_EQ(Lazy.StaticMethod(3), 6)
    end
    o.Field = 5
    o.Property = 6
    Lazy.StaticField = 7
    Lazy.StaticProperty = 8
    ASSERT_EQ(o.Field, 5)
    ASSERT_EQ(o.Property, 6)
    ASSERT_EQ(Lazy.StaticField, 7)
    ASSERT_EQ(Lazy.StaticProperty, 8)
    Lazy.StaticField = 2
    Lazy.StaticProperty = 4
    local n = 0
    local f = function() n = n + 1 end
    o:Changed("+", f)
    Lazy.StaticChanged("+", f)
    ASSERT_EQ(o:Fire(), 2)
    ASSERT_EQ(n, 2)
    o:Changed("-", f)
    Lazy.StaticChanged("-", f)
    ASSERT_EQ(o:Fire(), 0)
    ASSERT_EQ(o:Generated(), 42)
    expectError("invalid", o.Changed, o, "*", f)
end

-- [BlackList] entries hide the same members in generated and emitted wrappers.
function CMyTestCaseLuaCallCSCoverage.CaseBlackList(self)
    local o = CS.Coverage.BlackListed()
    ASSERT_EQ(o.Visible, 1)
    ASSERT_EQ(o.Hidden, nil)
    ASSERT_EQ(o.HiddenMethod, nil)
    ASSERT_EQ(o:Overload(1), "int")
    -- Only Overload(int) is left, and a string that is not a number reads as 0.
    ASSERT_EQ(o:Overload("s"), "int")
end

-- A [BlackList] Func<MemberInfo, bool> filter hides the same members in
-- generated, emitted and reflection wrappers.
local function checkFiltered(T)
    local o = T()
    ASSERT_EQ(o.Visible, 1)
    ASSERT_EQ(o.HiddenField, nil)
    ASSERT_EQ(o.HiddenProperty, nil)
    ASSERT_EQ(o.HiddenMethod, nil)
    ASSERT_EQ(o.HiddenEvent, nil)
    ASSERT_EQ(o.HiddenExtension, nil)
    ASSERT_EQ(T.HiddenStatic, nil)
    ASSERT_EQ(T.HiddenStaticField, nil)
    ASSERT_EQ(o:Pick(1), "int")
    -- Pick(string) is gone; what Pick(int) makes of a string differs by mode.
    local ok, r = pcall(o.Pick, o, "s")
    ASSERT_TRUE(not ok or r ~= "string")
    ASSERT_EQ(T("s").Made, "string")
    -- So is the constructor that takes an int.
    local made, obj = pcall(T, 5)
    ASSERT_TRUE(not made or obj.Made ~= "int")
end

function CMyTestCaseLuaCallCSCoverage.CaseBlackListFilterGenerated(self)
    checkFiltered(CS.Coverage.Filtered)
end

function CMyTestCaseLuaCallCSCoverage.CaseBlackListFilterReflected(self)
    checkFiltered(CS.Coverage.FilteredReflected)
end

-- A [DoNotGen] method keeps the extension methods of its name.
function CMyTestCaseLuaCallCSCoverage.CaseDoNotGenExtension(self)
    local T = CS.Coverage.LazyExtended
    local o = T()
    for _ = 1, 2 do
        ASSERT_EQ(o:Method(1), "own:1")
        ASSERT_EQ(o:Method("x"), "ext:x")
        ASSERT_EQ(T.StaticName(2), "static:2")
        ASSERT_EQ(o:StaticName("y"), "ext:y")
        ASSERT_EQ(o:ExtensionOnly(), "ext")
        ASSERT_EQ(o:Generated(3), "own:3")
        ASSERT_EQ(o:Generated("z"), "ext:z")
    end
end

-- xlua.private_accessible keeps the overloads an extension method shares
-- with a public method.
-- XLUA_RESTRICT_PRIVATE_ACCESS without HOTFIX_ENABLE leaves out xlua.access and
-- xlua.private_accessible, XLUA_NO_LOAD_ASSEMBLY xlua.load_assembly
-- (luaBridgeHardening.lua checks both states).
local function noPrivateAccess()
    return CS.BridgeHardening.Defines.RestrictPrivateAccess() and not CS.BridgeHardening.Defines.HotfixEnabled()
end

function CMyTestCaseLuaCallCSCoverage.CasePrivateAccessibleExtension(self)
    if noPrivateAccess() then return end
    local T = CS.Coverage.PrivateExtended
    local o = T()
    ASSERT_EQ(o:Method(1), "own:1")
    ASSERT_EQ(o:Method("x"), "ext:x")
    xlua.private_accessible(T)
    ASSERT_EQ(o:Secret(), 7)
    ASSERT_EQ(o:Method(1), "own:1")
    ASSERT_EQ(o:Method("x"), "ext:x")
end

function CMyTestCaseLuaCallCSCoverage.CaseGetGenericMethod(self)
    local G = CS.Coverage.Generics
    local echo = xlua.get_generic_method(G, "Echo")
    ASSERT_EQ(echo(CS.System.Int32)(5), "Int32:5")
    ASSERT_EQ(echo(CS.System.String)("s"), "String:s")
    ASSERT_EQ(xlua.get_generic_method(G, "Echo", 0)(CS.System.Int64)(7), "Int64:7")
    ASSERT_EQ(xlua.get_generic_method(typeof(G), "Wrap")(CS.System.String)(G(), "x"), "g<String>x")
    ASSERT_EQ(xlua.get_generic_method(G, "Pair")(CS.System.Int32, CS.System.String)(1, "x"), "Int32,String")
    ASSERT_EQ(xlua.get_generic_method(G, "NotGeneric"), nil)
    ASSERT_EQ(xlua.get_generic_method(G, "Missing"), nil)
    expectError("xlua.get_generic_method, can not find c# type", xlua.get_generic_method, nil, "Echo")
    expectError("xlua.get_generic_method, #2 param need a string", xlua.get_generic_method, G, "")
    expectError("param #1 is not a type", echo, 1)
    expectError("xlua.get_generic_method", xlua.get_generic_method, G, "Echo", 5)
end

-- The third argument picks one of several generic methods with the name; the
-- order is reflection's, so the case only checks that 0 and 1 differ.
function CMyTestCaseLuaCallCSCoverage.CaseGetGenericMethodIndex(self)
    local G = CS.Coverage.Generics
    local results = {}
    for i = 0, 1 do
        results[#results + 1] = xlua.get_generic_method(G, "Twin", i)(CS.System.Int32)(1, 2)
    end
    table.sort(results)
    ASSERT_EQ(table.concat(results, ","), "one,two")
    ASSERT_EQ(xlua.get_generic_method(G, "Twin", 1.0) ~= nil, true)
end

-- Generic methods whose type parameters all have a class constraint.
function CMyTestCaseLuaCallCSCoverage.CaseConstrainedGenericMethod(self)
    local r = CS.Coverage.Ref("r")
    ASSERT_EQ(CS.Coverage.Generics.Constrained(r), "Ref:r")
    ASSERT_EQ(CS.Coverage.GenGenerics.Constrained(r), "Ref:r")
end

function CMyTestCaseLuaCallCSCoverage.CaseToFunction(self)
    local f = xlua.tofunction(typeof(CS.Coverage.Probe):GetMethod("Twice"))
    ASSERT_EQ(f(21), 42)
    expectError("ToFunction: #1 argument must be a MethodBase", xlua.tofunction, 1)
end

-- Calling a delegate type builds a delegate from a Lua function.
function CMyTestCaseLuaCallCSCoverage.CaseDelegateConstructor(self)
    local d = CS.Coverage.IntFunc(function(x) return x + 1 end)
    ASSERT_EQ(d(1), 2)
    ASSERT_EQ(CS.Coverage.Probe.TypeName(d) ~= "null", true)
end

function CMyTestCaseLuaCallCSCoverage.CaseImportGenericType(self)
    local List = CS.System.Collections.Generic.List(CS.System.Int32)
    local l = List()
    l:Add(3)
    ASSERT_EQ(l.Count, 1)
    ASSERT_EQ(l[0], 3)
    local Dict = CS.System.Collections.Generic.Dictionary(CS.System.String, CS.Coverage.Ref)
    local d = Dict()
    d:Add("k", CS.Coverage.Ref("v"))
    local found, v = d:TryGetValue("k")
    ASSERT_EQ(found, true)
    ASSERT_EQ(v.Name, "v")
    ASSERT_EQ(xlua.import_generic_type("System.Collections.Generic.List<>", CS.System.Int32), typeof(List))
    ASSERT_EQ(xlua.import_generic_type("Coverage.NoSuchType", CS.System.Int32), nil)
    ASSERT_EQ(xlua.import_generic_type("Coverage.Ref", CS.System.Int32), nil)
    expectError("import generic type need at lease 2 arguments", xlua.import_generic_type, "System.Collections.Generic.List")
    expectError("param need a type", xlua.import_generic_type, "System.Collections.Generic.List", 1)
    expectError("No such type: Coverage.NoSuchGeneric", function() return CS.Coverage.NoSuchGeneric(CS.System.Int32) end)
end

function CMyTestCaseLuaCallCSCoverage.CaseLoadAssembly(self)
    if CS.BridgeHardening.Defines.NoLoadAssembly() then return end
    xlua.load_assembly(CS.Coverage.Probe.AssemblyName())
    ASSERT_EQ(CS.Coverage.Probe.Twice(2), 4)
    expectError("c# exception in xlua.load_assembly", xlua.load_assembly, "Coverage.NoSuchAssembly")
end

-- xlua.release drops the C# object behind a userdata.
function CMyTestCaseLuaCallCSCoverage.CaseRelease(self)
    local o = CS.Coverage.Probe.ReleaseTarget()
    ASSERT_EQ(o.Name, "released")
    xlua.release(o)
    ASSERT_EQ(pcall(function() return o.Name end), false)
    xlua.release(1)
    xlua.release({})
end

-- Enum & and | (Lua 5.3 and later; LuaJIT has no bitwise metamethods).
function CMyTestCaseLuaCallCSCoverage.CaseEnumBitwise(self)
    if not math.type then return end
    local band = load("local a, b = ... return a & b")
    local bor = load("local a, b = ... return a | b")
    local value = CS.Coverage.Probe.EnumValue
    local F = CS.Coverage.Flags
    local ab = bor(F.A, F.B)
    ASSERT_EQ(value(ab), "Flags:A, B")
    ASSERT_EQ(value(band(ab, F.B)), "Flags:B")
    ASSERT_EQ(value(band(ab, F.C)), "Flags:None")
    expectError("invalid argument for Enum BitwiseAnd", band, F.A, CS.Coverage.Other.X)
    expectError("invalid argument for Enum BitwiseOr", bor, F.A, CS.Coverage.Other.X)
    expectError("c# exception in Enum BitwiseAnd", band, F.A, 1)
    local S = CS.Coverage.Signed
    ASSERT_EQ(value(bor(S.Minus, S.One)), "Signed:-1")
    ASSERT_EQ(value(band(S.Minus, S.One)), "Signed:0")
end

-- An enum whose underlying type is ulong, with the top bit set.
function CMyTestCaseLuaCallCSCoverage.CaseEnumBitwiseULong(self)
    if not math.type then return end
    local band = load("local a, b = ... return a & b")
    local bor = load("local a, b = ... return a | b")
    local value = CS.Coverage.Probe.EnumValue
    local B = CS.Coverage.BigFlags
    local both = bor(B.Low, B.High)
    ASSERT_EQ(value(both), "BigFlags:Low, High")
    ASSERT_EQ(value(band(both, B.High)), "BigFlags:High")
end

-- The primitive array fast paths of ArrayIndexer and ArrayNewIndexer.
function CMyTestCaseLuaCallCSCoverage.CasePrimitiveArrays(self)
    local P = CS.Coverage.Probe
    local f = P.Floats()
    ASSERT_EQ(f[0], 1.5)
    f[1] = 4.25
    ASSERT_EQ(f[1], 4.25)
    ASSERT_EQ(f.Length, 2)
    local d = P.Doubles()
    ASSERT_EQ(d[0], 0.25)
    d[1] = 0.75
    ASSERT_EQ(d[1], 0.75)
    local b = P.Bools()
    ASSERT_EQ(b[0], true)
    b[1] = true
    ASSERT_EQ(b[1], true)
    local l = P.Longs()
    ASSERT_EQ(tostring(l[0]), "9223372036854775807")
    l[1] = 5
    ASSERT_EQ(tostring(l[1]), "5")
    local u = P.ULongs()
    ASSERT_EQ(tostring(u[0]), ULONG_TOSTRING("1", "1U"))
    u[0] = 6
    ASSERT_EQ(tostring(u[0]), ULONG_TOSTRING("6", "6U"))
    local sb = P.SBytes()
    ASSERT_EQ(sb[0], -128)
    sb[1] = -1
    ASSERT_EQ(sb[1], -1)
    local s = P.Shorts()
    ASSERT_EQ(s[0], -32768)
    s[1] = 12
    ASSERT_EQ(s[1], 12)
    local us = P.UShorts()
    ASSERT_EQ(us[1], 65535)
    us[0] = 7
    ASSERT_EQ(us[0], 7)
    local c = P.Chars()
    ASSERT_EQ(c[0], 97)
    c[1] = 98
    ASSERT_EQ(c[1], 98)
    local ui = P.UInts()
    ASSERT_EQ(ui[1], 4294967295)
    ui[0] = 3000000000
    ASSERT_EQ(ui[0], 3000000000)
    local p = P.Pointers()
    ASSERT_EQ(P.PointerValue(p[0]), 1)
    p[1] = P.Pointer(9)
    ASSERT_EQ(P.PointerValue(p[1]), 9)
    local dec = P.Decimals()
    ASSERT_EQ(P.TypeName(dec[0]), "Decimal:1.25")
    dec[1] = 3.5
    ASSERT_EQ(P.TypeName(dec[1]), "Decimal:3.5")
    dec[1] = dec[0]
    ASSERT_EQ(P.TypeName(dec[1]), "Decimal:1.25")
    local str = P.Strings()
    ASSERT_EQ(str[0], "x")
    str[1] = "w"
    ASSERT_EQ(str[1], "w")
    expectError("index out of range", function() return f[2] end)
    expectError("index out of range", function() f[2] = 1 end)
end

-- Arrays of a GCOptimize struct.
function CMyTestCaseLuaCallCSCoverage.CaseStructArrays(self)
    local a = CS.Coverage.Probe.PackedPoints()
    ASSERT_EQ(a[0].X, 1)
    ASSERT_EQ(a[1].Y, 4)
    a[1] = a[0]
    ASSERT_EQ(a[1].X, 1)
    a[0] = {X = 7, Y = 8}
    ASSERT_EQ(a[0].X, 7)
    ASSERT_EQ(a[0].Y, 8)
end

-- Lua tables passed for primitive array parameters.
function CMyTestCaseLuaCallCSCoverage.CaseTablesToPrimitiveArrays(self)
    ASSERT_EQ(CS.Coverage.Probe.Join({1.5}, {0.25}, {true}, {7}, {8}, {-1}, {-2}, {3}, {65}, {4}, {"s"}),
        "1.5|0.25|True|7|8|-1|-2|3|A|4|s")
end

-- xlua.access reads and writes private fields and properties.
function CMyTestCaseLuaCallCSCoverage.CaseXLuaAccess(self)
    if noPrivateAccess() then return end
    local A = CS.Coverage.Access
    local o = A()
    ASSERT_EQ(xlua.access(o, "secret"), 1)
    xlua.access(o, "secret", 2)
    ASSERT_EQ(xlua.access(o, "secret"), 2)
    ASSERT_EQ(xlua.access(A, "staticSecret"), "s")
    ASSERT_EQ(xlua.access("Coverage.Access", "staticSecret"), "s")
    xlua.access(o, "Prop", 5)
    ASSERT_EQ(xlua.access(o, "Prop"), 5)
    xlua.access(A, "StaticProp", 6)
    ASSERT_EQ(xlua.access(typeof(A), "StaticProp"), 6)
    xlua.access(A, "StaticProp", 0)
    expectError("xlua.access, no field missing", xlua.access, o, "missing")
    expectError("xlua.access, no field missing", xlua.access, o, "missing", 1)
    expectError("xlua.access, can not find c# type", xlua.access, 1, "secret")
    expectError("xlua.access, can not find c# type", xlua.access, {}, "secret")
    local released = A()
    xlua.release(released)
    expectError("xlua.access, #1 parameter must a type/c# object/string", xlua.access, released, "secret")
end

-- Generic type names in the reflection form (ObjectTranslator.FindType).
function CMyTestCaseLuaCallCSCoverage.CaseImportTypeGenericName(self)
    ASSERT_EQ(xlua.import_type("System.Collections.Generic.Dictionary`2[System.String,Coverage.Ref]"), true)
    ASSERT_EQ(xlua.import_type("System.Collections.Generic.List`1[Coverage.NoSuchType]"), nil)
end

-- A DelegateBridge parameter: the bridge of the Lua function, made once.
function CMyTestCaseLuaCallCSCoverage.CaseDelegateBridgeParameter(self)
    local f = function() end
    ASSERT_EQ(CS.Coverage.Probe.IsBridge(f), true)
    ASSERT_EQ(CS.Coverage.Probe.IsBridge(f), true)
end

-- XLua.Cast.* wrap a number so that C# sees the named type.
function CMyTestCaseLuaCallCSCoverage.CaseXLuaCast(self)
    local X = CS.XLua.Cast
    local name = CS.Coverage.Probe.TypeName
    ASSERT_EQ(name(X.Byte(1)), "Byte:1")
    ASSERT_EQ(name(X.SByte(-1)), "SByte:-1")
    ASSERT_EQ(name(X.Char(65)), "Char:A")
    ASSERT_EQ(name(X.Int16(-2)), "Int16:-2")
    ASSERT_EQ(name(X.UInt16(3)), "UInt16:3")
    ASSERT_EQ(name(X.Int32(4)), "Int32:4")
    ASSERT_EQ(name(X.UInt32(5)), "UInt32:5")
    ASSERT_EQ(name(X.Int64(6)), "Int64:6")
    ASSERT_EQ(name(X.UInt64(7)), "UInt64:7")
    ASSERT_EQ(name(X.Float(1.5)), "Single:1.5")
end

-- Overloads that only the checker of the parameter type tells apart.
function CMyTestCaseLuaCallCSCoverage.CaseCheckers(self)
    local K = CS.Coverage.Checks
    ASSERT_EQ(K.Table({}), "table")
    ASSERT_EQ(K.Table("s"), "string")
    ASSERT_EQ(K.Function(function() end), "function")
    ASSERT_EQ(K.Function("s"), "string")
    ASSERT_EQ(K.Decimal(1.5), "decimal:1.5")
    ASSERT_EQ(K.Decimal(CS.Coverage.Probe.Decimals()[0]), "decimal:1.25")
    ASSERT_EQ(K.Decimal("s"), "string")
    ASSERT_EQ(K.Pointer(CS.Coverage.Probe.Pointer(3)), "pointer:3")
    ASSERT_EQ(K.Pointer("s"), "string")
    ASSERT_EQ(K.ULong(5), "ulong:5")
    ASSERT_EQ(K.ULong("s"), "string")
end

-- Default parameter values of several types.
function CMyTestCaseLuaCallCSCoverage.CaseDefaultValues(self)
    local D = CS.Coverage.Defaults
    ASSERT_EQ(D.All(1), "1|7|1.5|2.5|0|null|x|9|0.25|B")
    ASSERT_EQ(D.All(1, 8), "1|8|1.5|2.5|0|null|x|9|0.25|B")
    ASSERT_EQ(D.All(1, 8, 2.5, 3, CS.Coverage.Point(4, 5), "s"), "1|8|2.5|3|4|s|x|9|0.25|B")
end

-- A Lua table as an interface with an indexer, an event and a params method.
function CMyTestCaseLuaCallCSCoverage.CaseInterfaceIndexerEventParams(self)
    local store = {}
    local events = 0
    local t = {
        get_Item = function(self, i) return store[i] or 0 end,
        set_Item = function(self, i, v) store[i] = v end,
        add_Changed = function(self, d) events = events + 1 end,
        remove_Changed = function(self, d) events = events + 10 end,
        Count = 3,
        Sum = function(self, ...)
            local s = 0
            for _, v in ipairs({...}) do s = s + v end
            return s
        end,
    }
    ASSERT_EQ(CS.Coverage.Probe.Pick(t), "20,3,6")
    ASSERT_EQ(events, 11)
end

-- Lua results converted to narrow integer types; ref decimal, string and struct.
function CMyTestCaseLuaCallCSCoverage.CaseNarrowResultsAndRefs(self)
    local r = CS.Coverage.Probe.Narrow(
        function(d, s, p)
            p.X = 5
            return -7, d, s .. "!", p
        end,
        function() return 255 end,
        function() return 66 end,
        function() return -3 end,
        function() return 65535 end)
    ASSERT_EQ(r, "-7|1.5|in!|5,2|255|B|-3|65535")
end

function CMyTestCaseLuaCallCSCoverage.CaseParamsDelegate(self)
    ASSERT_EQ(CS.Coverage.Probe.Variadic(function(head, ...)
        return select("#", ...) * 10 + #head
    end), 34)
end

-- An instance event of a struct (a question for the owner in
-- docs/design/backlog.md): emitted and generated wrappers add the handler to
-- a copy, so the struct in Lua never fires it; the reflection wrapper of the
-- generated-code suites adds it to the boxed struct.
function CMyTestCaseLuaCallCSCoverage.CaseStructEvent(self)
    local s = CS.Coverage.EventStruct()
    s:Changed("+", function() end)
    local fired = s:Fire()
    ASSERT_TRUE(fired == 0 or fired == 1)
    s:Changed("-", function() end)
end

-- The C# side of the API; each check throws on a failure.
function CMyTestCaseLuaCallCSCoverage.CaseLuaTableApi(self)
    CS.Coverage.CSharpApi.LuaTableApi()
end

function CMyTestCaseLuaCallCSCoverage.CaseNilToValueTypeKeepsStack(self)
    ASSERT_EQ(CS.Coverage.CSharpApi.NilToValueTypeKeepsStack(),
        "can not assign nil to int;can not assign nil to double")
end

function CMyTestCaseLuaCallCSCoverage.CaseFailedCastKeepsStack(self)
    CS.Coverage.CSharpApi.FailedCastKeepsStack()
end

function CMyTestCaseLuaCallCSCoverage.CaseFriendlyTypeNames(self)
    ASSERT_EQ(CS.Coverage.CSharpApi.FriendlyNames(),
        "int;short;byte;bool;long;float;double;decimal;System.Collections.Generic.KeyValuePair<int, string>;Coverage.Point")
end

function CMyTestCaseLuaCallCSCoverage.CaseNarrowValuesFromLua(self)
    CS.Coverage.CSharpApi.NarrowValues()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaFunctionApi(self)
    CS.Coverage.CSharpApi.LuaFunctionApi()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaBaseEquality(self)
    CS.Coverage.CSharpApi.LuaBaseEquality()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaEnvGcApi(self)
    CS.Coverage.CSharpApi.LuaEnvGcApi()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaEnvAlias(self)
    CS.Coverage.CSharpApi.Alias()
end

function CMyTestCaseLuaCallCSCoverage.CaseNarrowConversions(self)
    CS.Coverage.CSharpApi.NarrowConversions()
end

function CMyTestCaseLuaCallCSCoverage.CaseObjectPool(self)
    CS.Coverage.CSharpApi.ObjectPoolApi()
end

-- LuaEnv(LuaOptions): memory limit, overdraft, error reserve, hash seed (LuaOptionsForLuaCallCS.cs).
function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsBasics(self)
    CS.Coverage.LuaOptionsApi.Basics()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsMemoryLimit(self)
    CS.Coverage.LuaOptionsApi.MemoryLimit()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsOverdraft(self)
    CS.Coverage.LuaOptionsApi.Overdraft()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsErrorReserve(self)
    CS.Coverage.LuaOptionsApi.ErrorReserve()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsHashSeed(self)
    CS.Coverage.LuaOptionsApi.HashSeed()
end

-- LuaEnv(LuaOptions): instruction and time limits, Interrupt, the C budget (LuaOptionsForLuaCallCS.cs).
function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsLimitsBasics(self)
    CS.Coverage.LuaOptionsApi.LimitsBasics()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsInstructionLimit(self)
    CS.Coverage.LuaOptionsApi.InstructionLimit()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsTimeLimit(self)
    CS.Coverage.LuaOptionsApi.TimeLimit()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsInterrupt(self)
    CS.Coverage.LuaOptionsApi.Interrupt()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsCBudget(self)
    CS.Coverage.LuaOptionsApi.CBudget()
end
