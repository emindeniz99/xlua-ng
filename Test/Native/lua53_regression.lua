-- Exercise the loader GC fixes with long constants and nested prototypes.
assert(_VERSION == "Lua 5.3")
local text = string.rep("long-constant-", 100)
local chunk = assert(load("local value = " .. string.format("%q", text) .. "; return function() return value end", "@" .. string.rep("source-name-", 60)))
for i=1,100 do
 local dumped = string.dump(chunk)
 local pos = 1
 local restored = assert(load(function()
  -- The reader can run GC while the loader is constructing its objects.
  collectgarbage("collect")
  if pos > #dumped then return nil end
  local piece = dumped:sub(pos, pos + 6)
  pos = pos + 7
  return piece
 end))
 assert(restored()() == text)
end
assert(os.execute == nil)
assert(math.abs(math.log(8, 2) - 3) < 1e-12)
local ok, err = pcall(io.popen, "", "invalid")
assert(not ok and err:find("invalid mode", 1, true))
print("LUA53_REGRESSION_PASS", _VERSION)
