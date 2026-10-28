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

-- int[] in a [LuaCallCSharp] list (Coverage.ArrayTypeConfig): Generate Code
-- leaves array types out, and arrays keep the common array metatable, with
-- System.Array's members, in every mode.
function CMyTestCaseLuaCallCSCoverage.CaseListedArrayType(self)
    local Arrays = CS.Coverage.ListedArrays
    local a = Arrays.Make(3)
    ASSERT_EQ(a.Length, 3)
    a[0] = 4
    a[2] = 5
    ASSERT_EQ(a[0], 4)
    ASSERT_EQ(a:GetValue(2), 5)
    a[1] = 6
    ASSERT_EQ(Arrays.Sum(a), 15)
    ASSERT_EQ(a:GetLength(0), 3)
    ASSERT_EQ(a.Rank, 1)
    local b = a:Clone()
    b[0] = 0
    ASSERT_EQ(Arrays.Sum(b), 11)
    ASSERT_EQ(Arrays.Sum(a), 15)
    ASSERT_EQ(a:GetType():ToString(), "System.Int32[]")
end

-- Players read only the attribute forms of [BlackList]; the lists and filters of
-- static classes reach generated code only (XLUA_TEST_PLAYER_BLACKLIST runs the
-- players' rules outside Unity).
local function listsRead()
    return not CS.LuaTestCommon.PlayerBlackList()
end

-- [BlackList] on members and [assembly: BlackList] hide members of a type without
-- generated code in every build, players included (their reflection wrappers
-- applied no [BlackList] before).
function CMyTestCaseLuaCallCSCoverage.CaseBlackListAttributes(self)
    local T = CS.Coverage.AttributeListed
    local o = T()
    ASSERT_EQ(o.Visible, 1)
    ASSERT_EQ(o.AttributeHidden, nil)
    ASSERT_EQ(o.AttributeHiddenProperty, nil)
    ASSERT_EQ(o.AttributeHiddenMethod, nil)
    ASSERT_EQ(o.AssemblyHidden, nil)
    ASSERT_EQ(o:AssemblyHiddenMethod("s"), "string")
    local ok, r = pcall(o.AssemblyHiddenMethod, o, 1)
    ASSERT_TRUE(not ok or r == "string")
    local made, obj = pcall(T, 1)
    ASSERT_TRUE(not made or obj.Made ~= "int")
end

-- [BlackList] entries hide the same members in generated and emitted wrappers.
function CMyTestCaseLuaCallCSCoverage.CaseBlackList(self)
    if not listsRead() then return end
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
    if not listsRead() then return end
    checkFiltered(CS.Coverage.Filtered)
end

function CMyTestCaseLuaCallCSCoverage.CaseBlackListFilterReflected(self)
    if not listsRead() then return end
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

-- xlua.typename: the name a type has in Lua, the name table entry of
-- generated bindings (the original name, also after an obfuscator renamed the
-- type) or Type.ToString(); the IntKey hotfix map is keyed by it.
function CMyTestCaseLuaCallCSCoverage.CaseTypeName(self)
    ASSERT_EQ(xlua.typename(CS.Coverage.Ref), "Coverage.Ref")
    ASSERT_EQ(xlua.typename(typeof(CS.Coverage.Ref)), "Coverage.Ref")
    ASSERT_EQ(xlua.typename("Coverage.Ref"), "Coverage.Ref")
    ASSERT_EQ(xlua.typename(CS.Coverage.Lazy), "Coverage.Lazy")
    local List = CS.System.Collections.Generic.List(CS.System.Int32)
    ASSERT_EQ(xlua.typename(List), "System.Collections.Generic.List`1[System.Int32]")
    ASSERT_EQ(xlua.typename(List), typeof(List):ToString())
    expectError("xlua.typename: #1 argument must be a C# type", xlua.typename, {})
    expectError("xlua.typename: #1 argument must be a C# type", xlua.typename, "Coverage.NoSuchType")
    expectError("xlua.typename: #1 argument must be a C# type", xlua.typename, 1)
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
    ASSERT_EQ(tostring(u[0]), ULONG_TOSTRING("1"))
    u[0] = 6
    ASSERT_EQ(tostring(u[0]), ULONG_TOSTRING("6"))
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

-- A handler added to a struct's instance event from Lua stays in the Lua value and
-- runs when the event is raised through it, in every binding mode. The emitted and
-- generated wrappers added it to a copy, so it never ran; reflection kept it.
function CMyTestCaseLuaCallCSCoverage.CaseStructEvent(self)
    for _, T in ipairs({CS.Coverage.EventStruct, CS.Coverage.GenEventStruct}) do
        local s = T()
        local ran = 0
        local function handler() ran = ran + 1 end
        s:Changed("+", handler)
        ASSERT_EQ(s:Fire(), 1)
        ASSERT_EQ(ran, 1)
        s:Changed("+", handler)
        ASSERT_EQ(s:Fire(), 1)
        ASSERT_EQ(ran, 3)
        s:Changed("-", handler)
        s:Changed("-", handler)
        ASSERT_EQ(s:Fire(), 0)
        ASSERT_EQ(ran, 3)
    end
    -- holder.Inner is a copy, as in C#: the handler stays in Lua's copy until it is
    -- assigned back.
    local holder = CS.Coverage.EventStructHolder()
    local inner = holder.Inner
    local ran = 0
    inner:Changed("+", function() ran = ran + 1 end)
    ASSERT_EQ(holder:FireInner(), 0)
    ASSERT_EQ(inner:Fire(), 1)
    holder.Inner = inner
    ASSERT_EQ(holder:FireInner(), 1)
    ASSERT_EQ(ran, 2)
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

-- LuaOptions.Libraries: the libraries a LuaEnv opens, the reduced debug and package (LuaOptionsForLuaCallCS.cs).
function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsLibraries(self)
    CS.Coverage.LuaOptionsApi.Libraries()
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

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsInterruptWithoutLimits(self)
    CS.Coverage.LuaOptionsApi.InterruptWithoutLimits()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsCBudget(self)
    CS.Coverage.LuaOptionsApi.CBudget()
end

function CMyTestCaseLuaCallCSCoverage.CaseLuaOptionsStopAfterLastInstruction(self)
    CS.Coverage.LuaOptionsApi.StopAfterLastInstruction()
end

-- Overloads whose defaulted calls are ambiguous in C# (Unity 6000.6's
-- PhysicsScene2D.GetRayIntersection); generated wrappers write the defaults out.
function CMyTestCaseLuaCallCSCoverage.CaseAmbiguousDefaultsMethod(self)
    local o = CS.Coverage.AmbiguousDefaults("n")
    local p = CS.Coverage.Point(1, 2)
    local r = o:Ray(p, 2.5)
    ASSERT_TRUE(r == "hit:-5" or r == "hit:-5:Temp")
    r = o:Ray(p, 2.5, 9)
    ASSERT_TRUE(r == "hit:9" or r == "hit:9:Temp")
    ASSERT_EQ(o:Ray(p, 2.5, 9, CS.Coverage.Allocation.Persistent), "hit:9:Persistent")
end

function CMyTestCaseLuaCallCSCoverage.CaseAmbiguousDefaultsConstructor(self)
    local k = CS.Coverage.AmbiguousDefaults("n").Kind
    ASSERT_TRUE(k == "ctor:n:1" or k == "ctor:n:1:Temp")
    k = CS.Coverage.AmbiguousDefaults("n", 3).Kind
    ASSERT_TRUE(k == "ctor:n:3" or k == "ctor:n:3:Temp")
    ASSERT_EQ(CS.Coverage.AmbiguousDefaults("n", 3, CS.Coverage.Allocation.None).Kind, "ctor:n:3:None")
end

function CMyTestCaseLuaCallCSCoverage.CaseAmbiguousDefaultsConstants(self)
    local expected = "1|3|-2|-300|65000|4000000000|-9223372036854775808|18446744073709551615|1.5|inf|2.5|120|true|q\"\\\n\u{e9}|null|null|0,0|7|Persistent|Minus"
    local r = CS.Coverage.AmbiguousDefaults.Mixed(1)
    ASSERT_TRUE(r == expected or r == expected .. "|extra=false")
    r = CS.Coverage.AmbiguousDefaults.Mixed(1, 4)
    local expected4 = expected:gsub("^1|3|", "1|4|")
    ASSERT_TRUE(r == expected4 or r == expected4 .. "|extra=false")
end

-- Interface bridges (Coverage.IBridgeShape): emitted in the reflection suite
-- (CodeEmit.EmitInterfaceImpl), generated in the generated-code suites.
-- Members of inherited interfaces, params, indexers (get_Item and set_Item),
-- events (add_<name> and remove_<name>), out and ref values.
local function bridgeShape(name, w, h)
    local store, changed, ticked = {}, {}, {}
    local shape = { Id = 42, Name = name, ReadOnly = 5, w = w, h = h }
    function shape:Area() return self.w * self.h end
    function shape:Describe(prefix) return prefix .. self.Name end
    function shape:Scale(x) return x * 2 end
    function shape:Join(separator, ...) return "[" .. table.concat({...}, separator) .. "]" end
    function shape:Wrap(r) return CS.Coverage.Ref(r.Name .. "!") end
    function shape:Move(p, dx)
        p.X = p.X + dx
        return p
    end
    function shape:Fail() error("shape failed") end
    function shape:get_Item(k)
        if type(k) == "number" then return store[k] or 0 end
        return "key:" .. k
    end
    function shape:set_Item(i, v) store[i] = v end
    local function remove(list, d)
        for i = #list, 1, -1 do
            if list[i] == d then table.remove(list, i) end
        end
    end
    function shape:add_Changed(d) changed[#changed + 1] = d end
    function shape:remove_Changed(d) remove(changed, d) end
    function shape:add_Ticked(d) ticked[#ticked + 1] = d end
    function shape:remove_Ticked(d) remove(ticked, d) end
    function shape:Reset()
        for _, d in ipairs(changed) do d("reset", #changed + #ticked) end
        for _, d in ipairs(ticked) do d() end
    end
    function shape:Split(v, acc) return math.floor(v / 2), acc + v end
    function shape:TryGet(key)
        if key == "a" then return true, "A" end
        return false, nil
    end
    function shape:Swap(a, b) return 2, b, a end
    function shape:Fill() return { X = 3, Y = 4 } end
    return shape
end

function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeMembers(self)
    local s = bridgeShape("sq", 3, 4)
    s.Next = bridgeShape("next", 2, 5)
    ASSERT_EQ(CS.Coverage.Bridges.Members(s),
        "12,d:sq,s:sq,42,2199023255558,[a-b-c],[],r!,4:2,renamed,5,next:10,30,key:k,error")
    ASSERT_EQ(s.Name, "renamed")
    ASSERT_EQ(s.WriteOnly, 7)
end

function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeOutRef(self)
    ASSERT_EQ(CS.Coverage.Bridges.OutRef(bridgeShape("o", 1, 1)), "4:14,True:A,False:null,2:yx,3:4")
end

function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeEvents(self)
    ASSERT_EQ(CS.Coverage.Bridges.Events(bridgeShape("e", 1, 1)), "reset=2,tick,tick")
end

-- A table without the function an interface member calls (Area nil, Reset a
-- number, get_Item and add_Ticked nil): "no such function <name>" from every
-- bridge; emitted ones raised "attempt to call a nil value" before 2026-10-19.
function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeMissingFunction(self)
    local s = bridgeShape("m", 1, 1)
    s.Area = nil
    s.Reset = 5
    s.get_Item = nil
    s.add_Ticked = nil
    ASSERT_EQ(CS.Coverage.Bridges.Missing(s),
        "no such function Area,no such function Reset,no such function get_Item,no such function add_Ticked")
end

-- An interface with a generic method cannot be bridged (Coverage.IBridgeGeneric):
-- the emitted bridge names the method instead of failing to load. "" when
-- the build emits no bridges.
function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeGenericMethod(self)
    local r = CS.Coverage.Bridges.Generic({ Plain = function() return 1 end })
    if r ~= "" then
        ASSERT_EQ(r, "InvalidCastException: Coverage.IBridgeGeneric cannot be bridged to Lua: "
            .. "'Echo' is a generic method, which an interface bridge cannot implement")
    end
end

-- One env's delegate and interface bridges called from four threads at once
-- (THREAD_SAFE or HOTFIX_ENABLE; one after another without them): the
-- bridges lock the env. The emitted ones (reflection suite) took no lock
-- before 2026-10-19. Coverage.Bridges.Threads.
function CMyTestCaseLuaCallCSCoverage.CaseBridgesOnThreads(self)
    local kinds = CS.Upstream.EmitCache.Expected() == 1 and "emitted,emitted;" or "generated,generated;"
    ASSERT_EQ(CS.Coverage.Bridges.Threads(4, 3000), kinds)
end

-- Ordinary methods named like accessors, an indexer renamed with
-- [IndexerName] (Lua gets its accessor's name) and a plain property called
-- Item (Coverage.IBridgeNames).
function CMyTestCaseLuaCallCSCoverage.CaseInterfaceBridgeNames(self)
    local t = { raw = 0, Item = 1 }
    function t:get_Raw() return self.raw end
    function t:set_Raw(v) self.raw = v end
    function t:add_One(v) return v + 1 end
    function t:remove_One(v) return v - 1 end
    function t:get_At(i) return "at" .. i end
    function t:get_Item() error("get_Item called for the property Item") end
    ASSERT_EQ(CS.Coverage.Bridges.Names(t), "4,2,0,at3,6")
    ASSERT_EQ(t.Item, 6)
end

-- Types configured only by lists of several types (Coverage.MultiTypeConfig),
-- which the source generator build reads as [assembly: X(typeof(A), typeof(B))].
function CMyTestCaseLuaCallCSCoverage.CaseMultiTypeLists(self)
    local B = CS.Coverage.MultiListedB
    local a = CS.Coverage.MultiListedA(21)
    ASSERT_EQ(a:Twice(), 42)
    ASSERT_EQ(B.Join("x", 3), "x3")
    ASSERT_EQ(B.Call(function(n) return n + 1 end, 4), 5)
    ASSERT_EQ(B.Ask({ Answer = function(self, n) return n * 3 end }, 5), 15)
    local v = B.MakeVec(1.5, 2.5)
    ASSERT_EQ(v.x, 1.5)
    ASSERT_EQ(v.y, 2.5)
    ASSERT_EQ(B.SumVec(v), 4)
    ASSERT_EQ(B.SumVec({ x = 1, y = 2 }), 3)
    local p = B.Swap({ a = 3, b = 4 })
    ASSERT_EQ(p.a, 4)
    ASSERT_EQ(p.b, 3)
    ASSERT_EQ(CS.Coverage.MultiReflected.Name(), "reflected")
    ASSERT_EQ(CS.Coverage.MultiReflectedToo().Field, 3)
end

-- Configuration only in [assembly: CSharpCallLua], [assembly: LuaCallCSharp]
-- and [assembly: BlackList] (Coverage.IAssemblyConfigured and the types after
-- it), with no list: the emitted interface bridge, the extension method and
-- the black-listed member of a reflected type follow those forms in every
-- binding mode.
function CMyTestCaseLuaCallCSCoverage.CaseAssemblyLevelConfig(self)
    local E = CS.Coverage.AssemblyConfiguredExtensions
    ASSERT_EQ(E.Ask({ Answer = function(self, n) return n * 4 end }, 5), 20)
    local t = CS.Coverage.AssemblyConfiguredTarget()
    ASSERT_EQ(t:Plus(2), 3)
    ASSERT_EQ(t.Hidden, nil)
end

-- Binding-mode parity (2026-10-28). Coverage.Parity has [LuaCallCSharp]
-- (generated wrappers in the source generator's passes, emitted ones in the
-- reflection pass); Coverage.ParityReflected is the same class without
-- configuration (reflection wrappers in the source generator's passes,
-- emitted ones in the reflection pass). Each case runs the same calls on
-- both, the configured one first, and checks the results and error texts,
-- which are the same in every binding mode. The ParityReflected half needs
-- reflection (needs_reflection.lua).
local parityTypes = {"Parity", "ParityReflected"}

local function errorOf(f, ...)
    local ok, err = pcall(f, ...)
    ASSERT_EQ(ok, false)
    return tostring(err)
end

local function expectErrorText(text, f, ...)
    local err = errorOf(f, ...)
    ASSERT_TRUE(string.find(err, text, 1, true) ~= nil)
end

-- [BlackList] on an operator (and an entry with its parameter types) leaves it
-- out, also in generated code; a binary operator checks its operands in every
-- mode, so a number is not passed as null.
function CMyTestCaseLuaCallCSCoverage.CaseParityOperators(self)
    for _, name in ipairs(parityTypes) do
        local T = CS.Coverage[name]
        local a, b = T(6), T(3)
        ASSERT_EQ((a + b).Value, 9)
        ASSERT_EQ((-a).Value, -6)
        ASSERT_EQ((a * b).Value, 18)
        expectErrorText("attempt to perform arithmetic", function() return a - b end)
        expectErrorText("invalid arguments to right hand of * operator, need Coverage." .. name .. "!", function() return a * 2 end)
        expectErrorText("invalid arguments to right hand of + operator, need Coverage." .. name .. "!", function() return a + 1 end)
        expectErrorText("invalid arguments to left hand of + operator, need Coverage." .. name .. "!", function() return 1 + a end)
        ASSERT_EQ(a < b, false)
        ASSERT_EQ(a > b, true)
    end
end

-- [BlackList] and [Obsolete] on an indexer or one of its accessors, an
-- indexer with a private setter, a string-keyed indexer (get_Item and
-- set_Item) and an indexer with two keys, which Lua's t[k] cannot call.
function CMyTestCaseLuaCallCSCoverage.CaseParityIndexers(self)
    for _, name in ipairs(parityTypes) do
        local listed = CS.Coverage[name .. "ListedIndexer"]()
        ASSERT_EQ(listed[1], nil)
        expectErrorText("cannot set 1, no such field", function() listed[1] = 2 end)
        local getter = CS.Coverage[name .. "ListedGetter"]()
        ASSERT_EQ(getter[1], nil)
        getter[1] = 5
        ASSERT_EQ(getter.Last, 5)
        local private = CS.Coverage[name .. "PrivateSetIndexer"]()
        ASSERT_EQ(private[1], 2)
        expectErrorText("cannot set 1, no such field", function() private[1] = 5 end)
        ASSERT_EQ(private.Last, 0)
        local strings = CS.Coverage[name .. "ListedStringIndexer"]()
        ASSERT_EQ(strings.get_Item, nil)
        ASSERT_EQ(strings.set_Item, nil)
        ASSERT_EQ(strings.Other, 1)
        -- Parity has this[int, int] and this[double]: o[1] reaches the second only.
        local o = CS.Coverage[name](4)
        expectErrorText("c# exception:System.InvalidOperationException: boom 1", function() return o[1] end)
        expectErrorText("c# exception:System.InvalidOperationException: boom 1.5", function() return o[1.5] end)
    end
end

-- A readonly field has a getter only, in every mode.
function CMyTestCaseLuaCallCSCoverage.CaseParityReadonlyField(self)
    for _, name in ipairs(parityTypes) do
        local o = CS.Coverage[name](4)
        ASSERT_EQ(o.ReadOnlyField, 7)
        expectErrorText("cannot set ReadOnlyField, no such field", function() o.ReadOnlyField = 1 end)
        ASSERT_EQ(o.ReadOnlyField, 7)
        ASSERT_EQ(CS.Coverage[name .. "Struct"](3).R, 6)
        expectErrorText("cannot set R, no such field", function() local s = CS.Coverage[name .. "Struct"](3) s.R = 1 end)
    end
end

-- The error texts of the reflection wrappers are those of generated code.
function CMyTestCaseLuaCallCSCoverage.CaseParityErrorTexts(self)
    for _, name in ipairs(parityTypes) do
        local T = CS.Coverage[name]
        local o = T(4)
        ASSERT_EQ(errorOf(T, "x"), "invalid arguments to Coverage." .. name .. " constructor!")
        ASSERT_EQ(errorOf(o.Over, o), "invalid arguments to Coverage." .. name .. ".Over!")
        ASSERT_EQ(errorOf(T.StaticOverload, {}), "invalid arguments to Coverage." .. name .. ".StaticOverload!")
        -- No object as self: a Lua table is not made into a new object (the
        -- emitted wrappers did), and reflection raises what generated code raises.
        expectErrorText("c# exception:System.NullReferenceException", o.Add, {}, 1, 2)
        expectErrorText("c# exception:System.NullReferenceException", o.Add, nil, 1, 2)
        expectErrorText("c# exception:System.InvalidCastException", o.Add, CS.Coverage.Ref("r"), 1, 2)
        -- A C# exception in a field setter or a method.
        expectErrorText("c# exception:XLua.LuaException:", function() o.Value = 1.5 end)
        expectErrorText("c# exception:XLua.LuaException:", o.Add, o, 1.5, 2)
    end
end

-- An event's handler is checked before anything else: nil or a number raised
-- a Lua error through the reflection wrapper's C# frames, or named the bridge
-- of another function.
function CMyTestCaseLuaCallCSCoverage.CaseParityEvents(self)
    for _, name in ipairs(parityTypes) do
        local T = CS.Coverage[name]
        local o = T(4)
        local n = 0
        local f = function() n = n + 1 end
        o:Changed("+", f)
        T.StaticChanged("+", f)
        ASSERT_EQ(o:Fire(), 2)
        o:Changed("-", f)
        T.StaticChanged("-", f)
        ASSERT_EQ(o:Fire(), 0)
        ASSERT_EQ(n, 2)
        ASSERT_EQ(errorOf(o.Changed, o, "+"), "#3 need System.Action!")
        ASSERT_EQ(errorOf(o.Changed, o, "+", nil), "#3 need System.Action!")
        ASSERT_EQ(errorOf(o.Changed, o, "+", 5), "#3 need System.Action!")
        ASSERT_EQ(errorOf(o.Changed, o, "*", f), "invalid arguments to Coverage." .. name .. ".Changed!")
        ASSERT_EQ(errorOf(T.StaticChanged, "+", {}), "#2 need System.Action!")
        ASSERT_EQ(errorOf(T.StaticChanged, "x", f), "invalid arguments to Coverage." .. name .. ".StaticChanged!")
        ASSERT_EQ(o:Fire(), 0)
        -- A C# exception after those errors is caught as usual.
        expectErrorText("boom 1.5", function() return o[1.5] end)
    end
end

-- The same calls on both types give the same results and errors (with the
-- type name and addresses taken out), in every binding mode.
function CMyTestCaseLuaCallCSCoverage.CaseParitySameResults(self)
    local function norm(v)
        local s = tostring(v)
        s = s:gsub("ParityReflected", "Parity"):gsub("\n.*", ""):gsub("0x%x+", "ADDR"):gsub(": %-?%d+$", ": HASH")
        return s
    end
    local function pack(...)
        return {n = select("#", ...), ...}
    end
    local function run(name)
        local T = CS.Coverage[name]
        local o = T(4)
        local results = {}
        local function rec(name, f)
            local r = pack(pcall(f))
            local parts = {}
            for j = 1, r.n do parts[#parts + 1] = norm(r[j]) end
            results[#results + 1] = name .. " " .. table.concat(parts, " ")
        end
        rec("ctor", function() return T().Value, T(1, 2) end)
        rec("Value", function() o.Value = "s" return o.Value end)
        rec("StaticField", function() T.StaticField = 3 local v = T.StaticField T.StaticField = 10 return v end)
        rec("PrivateSetProp", function() return o.PrivateSetProp end)
        rec("PrivateSetProp=", function() o.PrivateSetProp = 1 end)
        rec("PrivateGetProp", function() return o.PrivateGetProp end)
        rec("WriteOnlyProp", function() o.WriteOnlyProp = 6 return o.WriteOnlyProp, o.Value end)
        rec("ReadOnlyProp=", function() o.ReadOnlyProp = 1 end)
        rec("Add", function() return o:Add(1, 2), o:Add("a", 2), o:Add(1), o:Add(1, 2, 3) end)
        rec("Statics", function() return T.StaticAdd(1, 2), o.StaticAdd, T.Add end)
        rec("Missing", function() return o.Missing, T.Missing end)
        rec("Missing=", function() o.Missing = 1 end)
        rec("T.Missing=", function() T.Missing = 1 end)
        rec("Sum", function() return o:Sum(), o:Sum(1, 2, 3) end)
        rec("Def", function() return o:Def(1), o:Def(1, 2) end)
        rec("Def3", function() return o:Def(1, 2, 3) end)
        rec("OutRef", function() return o:OutRef(3, 4) end)
        rec("Over", function() return o:Over(1), o:Over(1.5), o:Over("s"), o:Over(nil), o:Over({}), o:Over(o) end)
        rec("Nul", function() return o:Nul(1), o:Nul(nil), o:Nul(), o:Nul("x") end)
        rec("Day", function() return o:Day(CS.System.DayOfWeek.Friday), o:Day(1) end)
        rec("Arr", function() local a = o:Arr() return a.Length, a[0], a[2] end)
        rec("Self", function() return o:Self() == o, o:Nothing() end)
        rec("Callback", function() local n = 0 o.Callback = function() n = n + 1 end o.Callback() o.Callback = nil return n end)
        rec("Transform", function() o.Transform = function(x) return x * 3 end return o:RunTransform(2) end)
        rec("StaticPrivateSet=", function() T.StaticPrivateSet = 3 end)
        rec("Any", function() o.Any = 3 return o.Any, o:Describe(o.Any), o:Describe(1.5), o:Describe("s"), o:Describe(true) end)
        rec("Money", function() return T.Money, T.BigConst end)
        rec("Overload2", function() return o:Overload2(1), o:Overload2(1, 2), o:Overload2("s") end)
        rec("Object", function() return o:ToString(), o:GetType():ToString(), o:Equals(o), o == o end)
        local S = CS.Coverage[name .. "Struct"]
        rec("Struct", function() local s = S(3) s.X = 5 s:Bump() s.Prop = s.Prop + 1 return s.X, S().X, S.Make(4).X, S.Read(S(6)), S.Read({X = 7}) end)
        -- A struct parameter given nil, a number or nothing raises in every mode
        -- (2026-10-28; generated code threw NullReferenceException, the others
        -- passed the zero value).
        rec("StructNil", function() return S.Read(nil) end)
        rec("StructNumber", function() return S.Read(5) end)
        rec("StructNone", function() return S.Read() end)
        rec("StructOverloads", function() return o:Pick(nil), o:Pick(S(1)), o:Opt(1), o:NulStruct(nil), o:NulStruct() end)
        rec("StructOptNil", function() return o:Opt(1, nil) end)
        rec("StructField", function() o.Inner = nil end)
        -- [Obsolete] members are left out in every mode (2026-10-28).
        rec("Obsolete", function() return o.OldAdd, T.OldStatic, o:Over(true), o.OldField, T.OldConst, o.OldProp, o.OldEvent end)
        rec("ObsoleteCtor", function() return T("x") end)
        local D = CS.Coverage[name .. "Derived"]
        rec("Derived", function() local d = D() return d:Virt(), d:Hidden(), d.BaseField, d.DerivedField, d:BaseOnly() end)
        return results
    end
    -- A missing argument for a Nullable<T> parameter is null, as nil is (it read as 0 in
    -- generated and emitted wrappers).
    ASSERT_EQ(CS.Coverage.Parity(1):Nul(), "null")
    local configured = run("Parity")
    ASSERT_EQ(CS.Coverage.ParityReflected(1):Nul(), "null")
    local reflected = run("ParityReflected")
    ASSERT_EQ(#configured, #reflected)
    for i = 1, #configured do
        ASSERT_EQ(reflected[i], configured[i])
    end
end

-- Members in both [DoNotGen] and [BlackList] are left out, as in the emitted
-- wrappers; a lazy method keeps only the overloads [BlackList] leaves in.
function CMyTestCaseLuaCallCSCoverage.CaseLazyBlackListed(self)
    local T = CS.Coverage.LazyListed
    local o = T()
    for _ = 1, 2 do
        ASSERT_EQ(o.Both, nil)
        ASSERT_EQ(o:Mixed(1), "int")
        ASSERT_EQ(o:Mixed("s"), "int")
        ASSERT_EQ(o.BothField, nil)
        expectErrorText("cannot set BothField, no such field", function() o.BothField = 1 end)
        ASSERT_EQ(o.BothProperty, nil)
        expectErrorText("cannot set BothProperty, no such field", function() o.BothProperty = 1 end)
        ASSERT_EQ(o.GetterListed, nil)
        o.GetterListed = 9
        ASSERT_EQ(o:Last(), 9)
        ASSERT_EQ(o.BothEvent, nil)
        ASSERT_EQ(T.BothStatic, nil)
        ASSERT_EQ(o:Kept(), 6)
    end
end

-- [Obsolete] operators and indexers of a configured type are left out of
-- generated code as of emitted wrappers, and since 2026-10-28 (owner decision)
-- of reflection wrappers too (the minimal preset's first pass, FORCE_REFLECTION),
-- which bound them before.
function CMyTestCaseLuaCallCSCoverage.CaseObsoleteOperator(self)
    local T = CS.Coverage.ObsoleteOperator
    ASSERT_EQ((T(6) + T(3)).Value, 9)
    expectErrorText("attempt to perform arithmetic", function() return T(6) / T(3) end)
    ASSERT_EQ(T(6)[1], nil)
end

-- [Obsolete] members are left out in every binding mode (owner decision of
-- 2026-10-28; reflection wrappers bound them before): methods, an overload next
-- to kept ones, fields, a constant, a property, an event, a constructor, an
-- operator and an indexer.
function CMyTestCaseLuaCallCSCoverage.CaseParityObsolete(self)
    for _, name in ipairs(parityTypes) do
        local T = CS.Coverage[name]
        local o = T(4)
        ASSERT_EQ(o.OldAdd, nil)
        ASSERT_EQ(T.OldStatic, nil)
        ASSERT_EQ(o:Over(true), "object")
        ASSERT_EQ(o.OldField, nil)
        expectErrorText("cannot set OldField, no such field", function() o.OldField = 1 end)
        ASSERT_EQ(T.OldStaticField, nil)
        ASSERT_EQ(T.OldConst, nil)
        ASSERT_EQ(o.OldProp, nil)
        expectErrorText("cannot set OldProp, no such field", function() o.OldProp = 1 end)
        ASSERT_EQ(o.Value, 4)
        ASSERT_EQ(o.OldEvent, nil)
        ASSERT_EQ(errorOf(T, "x"), "invalid arguments to Coverage." .. name .. " constructor!")
        expectErrorText("attempt to perform arithmetic", function() return T(6) / T(3) end)
    end
    local R = CS.Coverage.ObsoleteOperatorReflected
    ASSERT_EQ((R(6) + R(3)).Value, 9)
    expectErrorText("attempt to perform arithmetic", function() return R(6) / R(3) end)
    ASSERT_EQ(R(6)[1], nil)
end

local function firstLine(s)
    return (tostring(s):match("^[^\r\n]*"))
end

-- A struct parameter, field or property given nil, a number, a string, a
-- boolean or nothing raises "c# exception:System.InvalidCastException: T
-- expected, got nil" in every binding mode (owner decision of 2026-10-28):
-- generated code threw NullReferenceException, emitted and reflection wrappers
-- passed the struct's zero value. A Nullable<T> parameter still takes nil, an
-- omitted optional parameter its default, and nil picks a class overload over
-- a struct one.
function CMyTestCaseLuaCallCSCoverage.CaseParityStructArgs(self)
    for _, name in ipairs(parityTypes) do
        local T = CS.Coverage[name]
        local S = CS.Coverage[name .. "Struct"]
        local expected = "c# exception:System.InvalidCastException: Coverage." .. name .. "Struct expected, got "
        local o = T(4)
        ASSERT_EQ(firstLine(errorOf(S.Read, nil)), expected .. "nil")
        ASSERT_EQ(firstLine(errorOf(S.Read, 5)), expected .. "number")
        ASSERT_EQ(firstLine(errorOf(S.Read, "s")), expected .. "string")
        ASSERT_EQ(firstLine(errorOf(S.Read, true)), expected .. "boolean")
        ASSERT_EQ(firstLine(errorOf(S.Read)), expected .. "no value")
        ASSERT_EQ(S.Read(S(6)), 6)
        ASSERT_EQ(S.Read({X = 7}), 7)
        -- Overloads: nil is no struct, so the class overload takes it.
        ASSERT_EQ(o:Pick(nil), "class")
        ASSERT_EQ(o:Pick(o), "class")
        ASSERT_EQ(o:Pick(S(1)), "struct")
        ASSERT_EQ(errorOf(o.Pick, o, 5), "invalid arguments to Coverage." .. name .. ".Pick!")
        -- An optional struct parameter: omitted, its default; nil is no struct.
        ASSERT_EQ(o:Opt(1), "1:0")
        ASSERT_EQ(o:Opt(1, S(3)), "1:3")
        ASSERT_EQ(errorOf(o.Opt, o, 1, nil), "invalid arguments to Coverage." .. name .. ".Opt!")
        -- Nullable<T> takes nil and nothing.
        ASSERT_EQ(o:NulStruct(nil), "null")
        ASSERT_EQ(o:NulStruct(), "null")
        ASSERT_EQ(o:NulStruct(S(2)), "has 2")
        -- A field of a struct type.
        o.Inner = S(3)
        ASSERT_EQ(o.Inner.X, 3)
        expectErrorText("System.InvalidCastException: Coverage." .. name .. "Struct expected, got nil", function() o.Inner = nil end)
        expectErrorText("System.InvalidCastException: Coverage." .. name .. "Struct expected, got number", function() o.Inner = 5 end)
        ASSERT_EQ(o.Inner.X, 3)
    end
    -- GCOptimize structs: a packed one and one read field by field.
    local B = CS.Coverage.MultiListedB
    ASSERT_EQ(firstLine(errorOf(B.SumVec, nil)), "c# exception:System.InvalidCastException: Coverage.MultiListedVec expected, got nil")
    ASSERT_EQ(firstLine(errorOf(B.SumVec, 1)), "c# exception:System.InvalidCastException: Coverage.MultiListedVec expected, got number")
    ASSERT_EQ(firstLine(errorOf(B.Swap, nil)), "c# exception:System.InvalidCastException: Coverage.MultiListedPair expected, got nil")
    ASSERT_EQ(B.SumVec({ x = 1, y = 2 }), 3)
end
