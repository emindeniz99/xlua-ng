-- Opt-in bridge hardening (docs/research/bridge-audit.md, docs/MIGRATING.md
-- "Opt-in defines"). Each case checks the behaviour of the define the suite
-- was built with (CS.BridgeHardening.Defines), so it passes with and without
-- it; run_managed.py --define NAME runs the suite with one. Test types:
-- Test/UnitTest/xLuaTest/LuaCallCS/BridgeHardeningForLuaCallCS.cs.
require("luaunit_compat")

CMyTestCaseBridgeHardening = TestCase:new()
function CMyTestCaseBridgeHardening:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestCaseBridgeHardening.SetUpTestCase(self) end
function CMyTestCaseBridgeHardening.TearDownTestCase(self) end
function CMyTestCaseBridgeHardening.SetUp(self) end
function CMyTestCaseBridgeHardening.TearDown(self) end

local Defines = CS.BridgeHardening.Defines

-- Item 2, XLUA_CHECK_OBJECTS: the bridge reads `self` (and every other C#
-- object it takes with FastGetCSObj) as the first int of any userdata. A
-- foreign userdata whose first int is a valid pool index then reaches that
-- C# object. With the define only a userdata with the bridge tag counts.
function CMyTestCaseBridgeHardening.CaseCheckObjectsForeignSelf(self)
    local checked = Defines.CheckObjects()
    local P = CS.BridgeHardening.Probe
    local o = P(5)
    local fake = CS.BridgeHardening.Natives.Forger()(o)
    ASSERT_EQ(type(fake), "userdata")
    ASSERT_EQ(debug.getmetatable(fake), nil)
    local mt = debug.getmetatable(o)
    -- __tostring (StaticLuaCallbacks.ToString) is the same function in every binding mode.
    local text = mt.__tostring(fake)
    if checked then
        ASSERT_EQ(text, "<invalid c# object>")
        -- Method wrappers and field getters see no object (a C# exception), in every mode.
        ASSERT_EQ(pcall(o.GetValue, fake), false)
        ASSERT_EQ(pcall(mt.__index, fake, "Value"), false)
    else
        -- Upstream: the forged block names o.
        ASSERT_EQ(text, tostring(o))
    end
    -- Real objects are unaffected.
    ASSERT_EQ(o:GetValue(), 5)
    ASSERT_EQ(o.Value, 5)
    ASSERT_EQ(P.Make(6):GetValue(), 6)
    ASSERT_TRUE(string.find(tostring(o), "Probe5", 1, true) == 1)
    -- Structs and delegates are read the same way.
    ASSERT_EQ(string.sub(tostring(CS.System.Convert.ToDecimal(2)), 1, 1), "2")
end

-- Item 5, XLUA_CHECK_STRUCT_ACCESSORS: the xlua.genaccessor accessors used to
-- trust any full userdata whose header says "struct" (fake_id -1 and a length);
-- with the define they also require the bridge tag, as xlua.structclone does.
function CMyTestCaseBridgeHardening.CaseCheckStructAccessorsForeign(self)
    local checked = Defines.CheckStructAccessors()
    local fake = CS.BridgeHardening.Natives.FakeStruct()(8)
    ASSERT_EQ(type(fake), "userdata")
    local get, set = xlua.genaccessor(0, 4)
    if checked then
        ASSERT_EQ(pcall(get, fake), false)
        ASSERT_EQ(pcall(set, fake, 3), false)
        ASSERT_EQ(pcall(xlua.structclone, fake), false)
    else
        ASSERT_EQ(get(fake), 0)
        set(fake, 3)
        ASSERT_EQ(get(fake), 3)
        -- structclone already checked the tag.
        ASSERT_EQ(pcall(xlua.structclone, fake), false)
    end
    -- A decimal is a struct userdata with the tag in every binding mode.
    local d = CS.System.Convert.ToDecimal(1)
    set(d, 7)
    ASSERT_EQ(get(d), 7)
    local getd = xlua.genaccessor(8, 9)
    ASSERT_EQ(type(getd(d)), "number")
end

-- Item 6, XLUA_RESTRICT_GETMETATABLE: xlua.getmetatable returned the raw type
-- metatable, so a script could call its __gc on a live object. With the define
-- it returns a copy without __gc and the bridge tag; xlua.setmetatable copies
-- both back, so get, edit and set still works.
function CMyTestCaseBridgeHardening.CaseRestrictGetMetatable(self)
    local restricted = Defines.RestrictGetMetatable()
    local T = CS.BridgeHardening.Probe
    local o = T(1)
    local raw = debug.getmetatable(o)
    local mt = xlua.getmetatable(T)
    ASSERT_EQ(type(mt), "table")
    ASSERT_TRUE(rawget(mt, "__index") == rawget(raw, "__index"))
    ASSERT_TRUE(rawget(mt, "__tostring") == rawget(raw, "__tostring"))
    if restricted then
        ASSERT_TRUE(not rawequal(mt, raw))
        ASSERT_EQ(rawget(mt, "__gc"), nil)
        for k in pairs(mt) do
            ASSERT_TRUE(type(k) ~= "userdata") -- the tag is a light userdata key
        end
        -- The copy is not the live metatable: editing it changes nothing.
        mt.__tostring = function() return "edited" end
        ASSERT_TRUE(tostring(o) ~= "edited")
        mt.__tostring = rawget(raw, "__tostring")
        -- Round trip: set the copy; the bridge puts the tag and __gc back.
        xlua.setmetatable(T, mt)
        local o2 = T(2)
        ASSERT_TRUE(rawequal(debug.getmetatable(o2), mt))
        ASSERT_TRUE(rawget(mt, "__gc") ~= nil)
        ASSERT_EQ(o2:GetValue(), 2)
        xlua.setmetatable(T, raw)
        ASSERT_TRUE(rawequal(debug.getmetatable(T(3)), raw))
    else
        ASSERT_TRUE(rawequal(mt, raw))
        ASSERT_TRUE(rawget(mt, "__gc") ~= nil)
    end
    ASSERT_EQ(o:GetValue(), 1)
end

-- Item 7, XLUA_CHECK_OBJECT_CACHE: the userdata cache is a registry table; an
-- entry a script stores there was returned for every later push of that C#
-- object. With the define only a C# object userdata holding the same pool
-- index counts; anything else is a miss and gets a fresh userdata.
function CMyTestCaseBridgeHardening.CaseCheckObjectCache(self)
    local checked = Defines.CheckObjectCache()
    local P = CS.BridgeHardening.Probe
    local o, other = P(3), P(4)
    local idx = CS.BridgeHardening.Natives.Indexer()(o)
    ASSERT_TRUE(idx >= 0)
    local cache
    for _, v in pairs(debug.getregistry()) do
        if type(v) == "table" and rawequal(rawget(v, idx), o) then
            cache = v
            break
        end
    end
    ASSERT_TRUE(cache ~= nil)
    ASSERT_TRUE(rawequal(o:Self(), o))
    local index = CS.BridgeHardening.Natives.Indexer()
    local cur, used = o, {}
    for _, planted in ipairs({other, "planted", {}}) do
        local i = index(cur)
        used[#used + 1] = i
        rawset(cache, i, planted)
        local back = o:Self()
        if checked then
            -- A miss: a new userdata for o, cached under its new index.
            ASSERT_EQ(type(back), "userdata")
            ASSERT_TRUE(not rawequal(back, other))
            ASSERT_EQ(back:GetValue(), 3)
            ASSERT_TRUE(rawequal(rawget(cache, index(back)), back))
            cur = back
        else
            ASSERT_TRUE(rawequal(back, planted))
        end
    end
    -- Leave no planted value behind: a reused pool index would find it.
    for _, i in ipairs(used) do rawset(cache, i, nil) end
    rawset(cache, idx, o)
    if not checked then
        ASSERT_TRUE(rawequal(o:Self(), o))
    end
    ASSERT_EQ(o:Self():GetValue(), 3)
    ASSERT_EQ(other:GetValue(), 4)
end

-- Item 9, XLUA_LOCK_XLUA_TABLE: xlua is a plain table, so any script could
-- replace xlua.cast or xlua.hotfix for every other script, and xLua's own
-- helpers (xlua.hotfix, xlua.getmetatable, the CS lookup) looked up
-- xlua.access, rawset and so on when they ran. With the define xlua is a
-- read-only proxy and the helpers use functions captured at start-up.
function CMyTestCaseBridgeHardening.CaseLockXLuaTable(self)
    local locked = Defines.LockXLuaTable()
    local cast, metatable_operation = xlua.cast, xlua.metatable_operation
    ASSERT_EQ(type(cast), "function")
    local T = CS.BridgeHardening.Probe
    local replaced = function() error("replaced") end
    if locked then
        ASSERT_EQ(pcall(function() xlua.cast = print end), false)
        ASSERT_EQ(pcall(function() xlua.bridge_hardening_probe = 1 end), false)
        ASSERT_TRUE(rawequal(xlua.cast, cast))
        ASSERT_EQ(getmetatable(xlua), "locked")
        ASSERT_TRUE(rawequal(package.loaded.xlua, xlua) or package.loaded.xlua == nil)
        if not jit then -- LuaJIT's pairs has no __pairs
            local seen = false
            for k in pairs(xlua) do
                if k == "cast" then seen = true end
            end
            ASSERT_TRUE(seen)
        end
        -- rawset still shadows a field (documented limit), but the helpers keep
        -- the function they captured.
        rawset(xlua, "metatable_operation", replaced)
        local ok, mt = pcall(xlua.getmetatable, T)
        rawset(xlua, "metatable_operation", nil)
        ASSERT_EQ(ok, true)
        ASSERT_EQ(type(mt), "table")
        ASSERT_TRUE(rawequal(xlua.metatable_operation, metatable_operation))
    else
        xlua.bridge_hardening_probe = 1
        ASSERT_EQ(rawget(xlua, "bridge_hardening_probe"), 1)
        xlua.bridge_hardening_probe = nil
        ASSERT_EQ(debug.getmetatable(xlua), nil)
        -- Upstream: the helper looks the field up when it runs.
        xlua.metatable_operation = replaced
        local ok = pcall(xlua.getmetatable, T)
        xlua.metatable_operation = metatable_operation
        ASSERT_EQ(ok, false)
    end
    ASSERT_EQ(type(xlua.getmetatable(T)), "table")
    -- The CS lookup still caches namespaces and raises for missing types.
    ASSERT_EQ(pcall(function() CS.BridgeHardening.NoSuchType.X = 1 end), false)
    ASSERT_TRUE(CS.BridgeHardening.Probe ~= nil)
end

-- Items 10 and 11, XLUA_RESTRICT_PRIVATE_ACCESS: without HOTFIX_ENABLE there is
-- no xlua.access, xlua.private_accessible or xlua.hotfix; with it xlua.access
-- reaches only the injected __Hotfix fields (Test/Native/hotfix_check.py).
-- xlua.tofunction and xlua.get_generic_method take public methods only.
function CMyTestCaseBridgeHardening.CaseRestrictPrivateAccess(self)
    local restricted = Defines.RestrictPrivateAccess()
    local P = CS.BridgeHardening.Probe
    if restricted and not Defines.HotfixEnabled() then
        ASSERT_EQ(xlua.access, nil)
        ASSERT_EQ(xlua.private_accessible, nil)
        ASSERT_EQ(xlua.hotfix, nil)
    else
        ASSERT_EQ(type(xlua.access), "function")
        ASSERT_EQ(type(xlua.private_accessible), "function")
        ASSERT_EQ(type(xlua.hotfix), "function")
    end
    local make = xlua.tofunction(P.PublicMethod())
    ASSERT_EQ(make(5):GetValue(), 5)
    local ok, secret = pcall(xlua.tofunction, P.SecretMethod())
    ASSERT_EQ(ok, not restricted)
    if not restricted then
        ASSERT_EQ(secret(), 42)
    end
    ASSERT_EQ(xlua.get_generic_method(P, "Echo")(CS.System.Int32)(7), 7)
    ASSERT_EQ(xlua.get_generic_method(P, "Hidden") == nil, restricted)
    -- Limit (bridge-audit.md item 12): reflection still reaches private members
    -- in both states, through typeof or any object's GetType, until a type
    -- allowlist closes System.Type's members.
    local NonPublicStatic = 40 -- BindingFlags.NonPublic | BindingFlags.Static
    ASSERT_EQ(typeof(P):GetMethod("Secret", NonPublicStatic):Invoke(nil, nil), 42)
    ASSERT_EQ(P(1):GetType():GetMethod("Secret", NonPublicStatic):Invoke(nil, nil), 42)
end

-- Item 12 (part), XLUA_NO_LOAD_ASSEMBLY: no xlua.load_assembly.
function CMyTestCaseBridgeHardening.CaseNoLoadAssembly(self)
    if Defines.NoLoadAssembly() then
        ASSERT_EQ(xlua.load_assembly, nil)
    else
        ASSERT_EQ(type(xlua.load_assembly), "function")
    end
    -- Types of loaded assemblies still resolve.
    ASSERT_TRUE(CS.System.Text.StringBuilder ~= nil)
end
