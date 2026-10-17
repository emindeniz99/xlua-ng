
require("luaCallCs") 
require("luaCallCsReflect") 
require("luaCallCsInParams")
require("luaCallCsUpstream")
require("csCallLua")
require("genCode")
local luaunit_compat = require("luaunit_compat")
function islua53() return not not math.type end
-- for test case
CMyTestEnv = TestEnvironment:new()
function CMyTestEnv:new(oo)
    local o = oo or {}
    setmetatable(o, self)
    self.__index = self
    return o
end

function CMyTestEnv.SetUp(self)
	print("CMyTestEnv.SetUp")
end

function CMyTestEnv.TearDown(self)
	print("CMyTestEnv.TearDown")
end

-- Runs every enabled suite once; raises an error if a test fails.
local function RunAllSuites(mode, fileSuffix)
	local resultPath = CS.LuaTestCommon.resultPath
	luaunit_compat.run({
		mode = mode,
		env = CMyTestEnv:new(),
		list_tests = resultPath.."luaunit_case_list"..fileSuffix..".txt",
		list_failed = resultPath.."luaunit_case_failed"..fileSuffix..".txt",
		suites = {
			{CMyTestCaseLuaCallCS:new(), "CMyTestCaseLuaCallCS", "Case"},
			{CMyTestCaseLuaCallCSReflect:new(), "CMyTestCaseLuaCallCSReflect", "Case"},
			{CMyTestCaseLuaCallCSInParams:new(), "CMyTestCaseLuaCallCSInParams", "Case"},
			{CMyTestCaseLuaCallCSUpstream:new(), "CMyTestCaseLuaCallCSUpstream", "Case"},
			--{CMyTestCaseGenCode:new(), "CMyTestCaseGenCode", "Case"},
			{CMyTestCaseCSCallLua:new(), "CMyTestCaseCSCallLua", "test"},
		},
	})
end

-- The same cases run twice: first inside a coroutine, then on the main thread.
co = coroutine.create(function()
	RunAllSuites("coroutine", "_co")
	coroutine.yield()
end)

function main()
	local coOk, coErr = coroutine.resume(co)
	print(coOk, coErr)

	RunAllSuites("normal", "")

	print('--------------------------------------------------------')
	if not coOk then error(coErr, 0) end
end

main()

local ret = islua53()
print("islua53")
print(tostring(ret))