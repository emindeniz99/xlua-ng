-- Runs the xLua unit-test classes with luaunit (luaunit.lua, vendored next to
-- this file) while keeping the test-file API they were written against:
-- TestCase/TestEnvironment classes, SetUpTestCase/SetUp/TearDown/TearDownTestCase
-- hooks, ASSERT_EQ and ASSERT_TRUE.
--
-- Assertion semantics: ASSERT_EQ is luaunit's assertEquals and ASSERT_TRUE is
-- assertEvalToTrue (any value other than false and nil). A failed assertion
-- stops the test. It also stays recorded for the running test, so a failure
-- raised inside a pcall or a C# callback, where the test code may swallow the
-- error, still fails the test.
-- The fields of os as the native library opened them, before the getenv stand-in
-- below (CaseLibraryPartsFollowTheBuild). The minimal preset's os has only time,
-- clock, date and difftime (CMake XLUA_OS_FULL=OFF); luaunit and the cases read
-- their settings with os.getenv, which then comes from C#.
NATIVE_OS_FIELDS = nil
if rawget(_G, "os") then
    NATIVE_OS_FIELDS = {}
    for name in pairs(os) do NATIVE_OS_FIELDS[name] = true end
    if not os.getenv then
        os.getenv = function(name) return CS.System.Environment.GetEnvironmentVariable(name) end
    end
end

local lu = require("luaunit")

local M = {}

TestEnvironment = {}
function TestEnvironment:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function TestEnvironment.SetUp(self)
end

function TestEnvironment.TearDown(self)
end

TestCase = {}
function TestCase:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function TestCase.SetUpTestCase(self)
end

function TestCase.TearDownTestCase(self)
end

function TestCase.SetUp(self)
end

function TestCase.TearDown(self)
end

-- luaunit calls SetUp and TearDown itself; these map the per-class hooks.
function TestCase.setupClass(self)
    self:SetUpTestCase()
end

function TestCase.teardownClass(self)
    self:TearDownTestCase()
end

local currentFailure -- first assertion failure in the running test step

local function check(assertion, ...)
    local ok, err = pcall(assertion, ...)
    if not ok then
        -- Name the line of the ASSERT_* call (level 3: check, ASSERT_*, caller).
        local caller = debug.getinfo(3, "Sl")
        if caller and caller.currentline > 0 then
            err = caller.short_src .. ":" .. caller.currentline .. ": " .. tostring(err)
        end
        currentFailure = currentFailure or err
        error(err, 0)
    end
end

function ASSERT_EQ(v1, v2)
    check(lu.assertEquals, v1, v2)
end

function ASSERT_TRUE(v)
    check(lu.assertEvalToTrue, v)
end

-- How a C# ulong looks in Lua depends on the build. Lua 5.3+ keeps it in a
-- native integer, and LuaJIT by default in the signed int64 userdata of
-- build/i64lib.c (lua_pushuint64 calls lua_pushint64), so tostring shows the
-- signed value with the same bits: UInt64.MaxValue prints "-1". A LuaJIT
-- plugin built with UINT_ESPECIALLY pushes a separate unsigned kind, which
-- the uint64 helpers also return and which prints the unsigned value with a
-- "U" suffix (int64_tostring).
DISTINCT_UINT64 = uint64 ~= nil and tostring(uint64.parse("1")) == "1U"

-- Whether the native library has the uint64 table (CMake XLUA_UINT64, Lua 5.3+;
-- LuaJIT always has it). run_managed.py passes XLUA_EXPECT_UINT64 from the
-- environment; without it (Unity) the default build's table is expected.
function UINT64_BUILT()
    local expect = os.getenv("XLUA_EXPECT_UINT64")
    if expect ~= nil then expect = expect:upper() end
    return not (expect == "OFF" or expect == "0" or expect == "FALSE" or expect == "NO")
end

-- Expected tostring() of a C# ulong: `signed` on the default builds, and
-- `unsigned` (the same digits when omitted) followed by "U" with UINT_ESPECIALLY.
function ULONG_TOSTRING(signed, unsigned)
    if unsigned == nil and signed:sub(1, 1) == "-" then
        error("ULONG_TOSTRING: a negative signed text needs the unsigned text too", 2)
    end
    if DISTINCT_UINT64 then
        return (unsigned or signed) .. "U"
    end
    return signed
end

-- A method is a test case when its name contains `filter` and it is not a hook.
-- Cases run in name order, like they did under ltest.
local NOT_CASES = {
    new = true, SetUpTestCase = true, TearDownTestCase = true, SetUp = true, TearDown = true,
    setupClass = true, teardownClass = true,
}

local function caseNames(instance, filter)
    local names, seen = {}, {}
    for _, t in ipairs({ instance, instance.__index }) do
        for k, v in pairs(t) do
            if type(k) == "string" and k:sub(1, 2) ~= "__" and type(v) == "function"
                    and k:find(filter) and not NOT_CASES[k] and not seen[k] then
                seen[k] = true
                names[#names + 1] = k
            end
        end
    end
    table.sort(names)
    return names
end

-- luaunit output through print(), which xLua routes to Debug.Log in Unity.
-- Flushing keeps each line whole when C# writes to the same stdout (LuaJIT's
-- print does not flush).
local function say(text)
    if io and io.stdout then io.stdout:flush() end
    print(text)
    if io and io.stdout then io.stdout:flush() end
end

local PrintOutput = lu.genericOutput.new()
local PrintOutput_MT = { __index = PrintOutput }

function PrintOutput.new(runner)
    return setmetatable(lu.genericOutput.new(runner, lu.VERBOSITY_DEFAULT), PrintOutput_MT)
end

function PrintOutput:endTest(node)
    if node:isSuccess() then
        say("    " .. node.testName .. " ... Ok")
    else
        say("    " .. node.testName .. " ... " .. node.status)
        say(tostring(node.msg))
        if node.stackTrace then say(node.stackTrace) end
    end
end

function PrintOutput:endSuite()
    local tag = "[luaunit " .. self.runner.mode .. "] "
    for _, nodes in ipairs({ self.result.failedTests, self.result.errorTests }) do
        for _, node in ipairs(nodes) do
            say(tag .. node.status .. " " .. node.testName)
        end
    end
    say(tag .. lu.LuaUnit.statusLine(self.result))
end

-- Fail a test step whose assertion failure was caught before luaunit saw it.
local function protectedCall(runner, instance, method, name)
    currentFailure = nil
    local result = lu.LuaUnit.protectedCall(runner, instance, method, name)
    if currentFailure and result.status == lu.NodeStatus.SUCCESS then
        local msg = lu.adjust_err_msg_with_iter(currentFailure)
        result = { status = lu.NodeStatus.FAIL, trace = "",
                   msg = msg .. "\n(the test caught this assertion failure)" }
    end
    currentFailure = nil
    return result
end

local function writeLines(path, lines)
    if not path then return end
    -- A library built without io (XLUA_LIBS, the minimal preset) writes through C#.
    if not io then
        local text = {}
        for _, line in ipairs(lines) do text[#text + 1] = line .. "\r\n" end
        CS.System.IO.File.WriteAllText(path, table.concat(text))
        return
    end
    local f = io.open(path, "w+b")
    if not f then return end
    for _, line in ipairs(lines) do f:write(line, "\r\n") end
    f:close()
end

-- Runs the cases of each suite with luaunit and raises an error if any fails.
-- params.mode:        name printed in the summary line ("[luaunit <mode>] Ran ...")
-- params.suites:      list of { instance, class name, case-name filter }
-- params.env:         optional TestEnvironment, set up before and torn down after
-- params.list_tests:  optional file that receives the test names
-- params.list_failed: optional file that receives failed tests and messages
function M.run(params)
    local list, names = {}, {}
    for _, suite in ipairs(params.suites) do
        local instance, className, filter = suite[1], suite[2], suite[3]
        for _, method in ipairs(caseNames(instance, filter)) do
            list[#list + 1] = { className .. "." .. method, instance }
            names[#names + 1] = className .. "." .. method
        end
    end
    writeLines(params.list_tests, names)

    local runner = lu.LuaUnit.new()
    runner.mode = params.mode
    runner.testPrefix = "" -- the list is explicit: accept every name in it
    runner.outputType = PrintOutput
    runner.protectedCall = protectedCall

    if params.env then params.env:SetUp() end
    local notSuccess = runner:runSuiteByInstancesNoCmdLineParsing(list)
    if params.env then params.env:TearDown() end

    local failed = {}
    for _, nodes in ipairs({ runner.result.failedTests, runner.result.errorTests }) do
        for _, node in ipairs(nodes) do
            failed[#failed + 1] = node.testName .. "\r\n" .. tostring(node.msg) .. "\r\n"
        end
    end
    writeLines(params.list_failed, failed)

    if notSuccess ~= 0 then
        error(string.format("luaunit %s mode: %d of %d tests did not pass",
            params.mode, notSuccess, #list), 0)
    end
    return runner.result
end

return M
