local t = {a = {b = {c = 1}}, n = 5, s = "str"}
setpath(t, "a.b.d", 2)
assert(getpath(t, "a.b.d") == 2 and getpath(t, "a.n.x") == nil)
pset(t, "k", true)
assert(pget(t, "k") == true)
setglobal("g", t)
assert(getglobal("g") == t)
local s = newstruct(8)
local values = {nil, true, 0, -1.5, 2^40, 0/0, "12", "x", t, s, pcall, coroutine.create(pcall)}
for i = 1, 13 do
  local v = values[i]
  pcall(getpath, v, "a.b")
  pcall(setpath, v, "a.b", 1)
  pcall(pget, v, v)
  pcall(pset, v, 1, v)
  typeid(v)
  csobj(v)
  tostruct(v, 1)
  touint(v)
  eqstr(v, "12")
end
return touint(-1.5), touint(1e300), typeid(s), tostruct(s, 1)
