/* libFuzzer target: adversarial calls into the native bridge (the crash sweep
 * of docs/design/backlog.md, "Tests and CI").
 *
 * A Lua driver (DRIVER below) reads the input as a stream of operations: it
 * builds a pool of values of every kind and calls native functions with
 * arguments drawn from the pool, so that each native sees nil, booleans,
 * huge and boundary integers, NaN and infinities, numeric and odd strings,
 * tables with metatables, light userdata, foreign full userdata, xLua's own
 * struct and C# object userdata, the int64 userdata of LuaJIT, functions,
 * coroutines and the results of earlier calls. It also runs Lua operators
 * on pool values (so the metamethods run), changes metatables, runs the
 * debug library on the natives (getupvalue, setupvalue, setmetatable,
 * sethook ...), collects garbage and replaces globals.
 *
 * The natives (the bridge presets, legacy and minimal):
 *  - xlua.genaccessor, xlua.structclone, xlua.sethook and the accessor
 *    closures genaccessor returns, default and checked (option bit 1);
 *  - every function of the uint64 table, and on LuaJIT the int64
 *    metamethods (registry[8]);
 *  - the helpers C# calls, through the C wrappers of X below, as C# calls
 *    them: tocsobj, touint, tointeger64, number_kind, pushargerror, the
 *    protected table helpers, pnext, plen, running, the userdata cache,
 *    pushcsobj, pushstruct, newstruct, pushcstable, gettypeid, pack and
 *    unpack after gettypeid (CopyByValue), is_eq_str, lua_setfenv,
 *    getloaders, rawgeti/rawseti, pcall_prepare + lua_pcall (a delegate
 *    bridge), xlua_gc, the memory snapshot, the int64 helpers;
 *  - the class and object indexers (gen_obj_indexer ...) with upvalues
 *    from the pool;
 *  - the C# function trampolines (xlua_push_csharp_function and the
 *    GEN_CODE_MINIMIZE wrapper) around C functions that do what C# does:
 *    return values, report an error (xlua_csharp_str_error,
 *    xlua_csharp_error), call back into Lua, read their upvalues, allocate,
 *    build argument errors, call the helpers.
 * The state is a plain one with an instruction limit, or (option bit 0, PUC
 * Lua) a core state with a memory limit and, on 5.4 and 5.5, an instruction
 * limit, as LuaEnv creates with LuaOptions.
 *
 * The core preset (FUZZ_BRIDGE_CORE) has no bridge: the same driver runs in
 * LuaScript's sandbox through xlua_core_call, and the natives are the buffer
 * library, the sandbox's coroutine functions and host functions whose C
 * dispatcher answers with every frame status, invalid result counts and
 * handles, yields, and handle operations on its arguments.
 *
 * Oracles: no crash, sanitizer report, Lua API check failure or hang (a
 * libFuzzer timeout); a helper that C# calls outside a protected call
 * raises no error except a memory error (X.check); the helpers leave the
 * stack as their callers expect; struct headers stay intact.
 *
 * Known crashes of docs/research/crash-inventory.md are left out unless
 * FUZZ_BRIDGE_TRICKS=1 is set in the environment (see "tricks" in the
 * driver): foreign userdata with a struct header or shorter than an int
 * (item 10), LuaJIT's newproxy (item 10), debug.getregistry (registry[8]
 * and xLua's references, item 22), the C function inside a trampoline and
 * replacing it, and a class indexer's fields upvalue that is not a table
 * (item 18), __gc errors on Lua 5.3 (item 8), debug.setmetatable on io
 * files, which LuaJIT's trace recorder takes as immutable (item 23).
 * FUZZ_BRIDGE_TRACE=1 says how the driver ended, 2 also lists every call,
 * 3 also the value readers. */
#define _GNU_SOURCE /* dladdr, for the trace */
#include <dlfcn.h>
#include <math.h>
#include <stdio.h>

#include "fuzz_common.h"
#if !defined(USING_LUAJIT)
#include "lstate.h" /* xlua_core.h's xlua_core_of */
#endif
#if LUA_VERSION_NUM == 501
#define lua_rawlen lua_objlen
#endif

static int tricks; /* FUZZ_BRIDGE_TRICKS=1: also the known crashes */
static int trace;  /* FUZZ_BRIDGE_TRACE: say how the driver ended */

/* ------------------------------------------------------------------------
 * The Lua driver, shared by every preset. Arguments: the operation bytes,
 * the natives (an array), X (harness helpers; in the core preset a table of
 * host functions), tricks, and the Lua version.
 * ------------------------------------------------------------------------ */
static const char DRIVER[] =
    "local input, N, X, tricks, core = ...\n"
    "local sbyte, ssub, srep = string.byte, string.sub, string.rep\n"
    "local pcall, select, type, tostring, tonumber = pcall, select, type, tostring, tonumber\n"
    "local setmetatable, getmetatable, rawset, rawget = setmetatable, getmetatable, rawset, rawget\n"
    "local unpack = table.unpack or unpack\n"
    "local function pack(...) return {n = select('#', ...), ...} end\n"
    "local dbg = (not core) and debug or nil\n"
    "local co = coroutine\n"
    "local G = _G or _ENV\n"
    "local xlua, buffer, collectgarbage = xlua, buffer, collectgarbage or function() end\n"
    "local pairs, ipairs, rawequal, rawlen, error = pairs, ipairs, rawequal, rawlen, error\n"
    "local v53 = math.type ~= nil\n"
    /* a __gc that raises only with tricks on Lua 5.3, where the error leaves the GC step (item 8) */
    "local gc_ok = tricks or _VERSION ~= 'Lua 5.3'\n"
    "local check = X.check or function() end\n"
    "local log = X.log\n"
    "local pos, len = 1, #input\n"
    "local function u8() local b = sbyte(input, pos); pos = pos + 1; return b or 0 end\n"
    "local P, np, wp = {}, 0, 0\n"
    "local KEEP = {}\n"
    "local function put(v) wp = wp % 48 + 1; P[wp] = v; if np < wp then np = wp end end\n"
    "local function get() if np == 0 then return nil end return P[u8() % np + 1] end\n"
    "local TRAMP = setmetatable({}, {__mode = 'k'})\n"
    "local BLOCK = setmetatable({}, {__mode = 'k'})\n"
    "local FOREIGN = setmetatable({}, {__mode = 'k'})\n"
    "local maxi = math.maxinteger or 2^63\n"
    "local mini = math.mininteger or -2^63\n"
    "local INTS = {0, 1, -1, 2, 7, 8, 15, 16, 255, 256, -256, 65535, 2147483647, -2147483648, 2147483648,\n"
    "  4294967295, 4294967296, 9007199254740992, -9007199254740992, maxi, mini, maxi - 1, mini + 1}\n"
    "local FLOATS = {0.5, -0.5, -1.5, 1e308, -1e308, 1/0, -1/0, 0/0, -0.0, 2^63, -2^63, 2^64, 4294967295.5,\n"
    "  1e-310, 3.0, -3.0, 2^53 + 2, 1e15 + 0.5, 2147483648.0, -2147483649.0}\n"
    "local STRS = {'', '0', '1', '-1', '0x10', ' 12 ', '1e2', '1.5', '9223372036854775807', '9223372036854775808',\n"
    "  '-9223372036854775809', '18446744073709551615', '0xffffffffffffffff', '-0x8000000000000000', 'nan', 'inf',\n"
    "  'x', 'a.b', 'a..b', '.', '..', 'a.', '.a', '__index', 'BaseType', '\\0', 'a\\0b', srep('a', 300),\n"
    "  '%s%d%n%%', 'package', 'debug', 'searchers', 'uint64', 'xlua', '_G', 'loaded', '0x', '1e400', '-'}\n"
    "local EVENTS = {'__index', '__newindex', '__len', '__call', '__eq', '__lt', '__le', '__tostring', '__concat',\n"
    "  '__add', '__unm', '__gc', '__mode', '__metatable', '__close', '__name', '__pairs', '__idiv', '__band', '__shl'}\n"
    "local GLOBALS = {'package', 'debug', 'xlua', 'uint64', '_G', 'coroutine', 'string', 'require', 'buffer'}\n"
    "local function gcsafe() end\n"
    "local SNIP = {\n"
    "  function(...) return ... end,\n"
    "  function(a) error(a) end,\n"
    "  function(a, b) return a, b, a end,\n"
    "  function(f, ...) return f(...) end,\n"
    "  function(...) return co.yield(...) end,\n"
    "  function(t, k) return t[k] end,\n"
    "  function(t, k, v) t[k] = v end,\n"
    "  function(a, b) return a == b, a < b end,\n"
    "  function(a) return #a end,\n"
    "  function(a) return tostring(a) end,\n"
    "  function(...) return select('#', ...) end,\n"
    "  function(n) local function r(k) if k > 0 then return (r(k - 1)) end return k end\n"
    "    n = tonumber(n) or 0; if n ~= n or n > 60 then n = 60 end; return r(n) end,\n"
    "  function(f, ...) return pcall(f, ...) end,\n"
    "  function(a, b) return a .. b end,\n"
    "  function(f, ...) local c = co.wrap(f); return c(...) end,\n"
    "}\n"
    "if gc_ok then\n"
    "  SNIP[#SNIP + 1] = function() return setmetatable({}, {__gc = function() error('gc') end}) end\n"
    "end\n"
    "local OPS = {\n"
    "  function(a, b) return a + b end, function(a, b) return a - b end, function(a, b) return a * b end,\n"
    "  function(a, b) return a / b end, function(a, b) return a % b end, function(a, b) return a ^ b end,\n"
    "  function(a) return -a end, function(a, b) return a == b end, function(a, b) return a < b end,\n"
    "  function(a, b) return a <= b end, function(a, b) return a .. b end, function(a) return #a end,\n"
    "  function(a, b) return a[b] end, function(a, b, c) a[b] = c end, function(a, ...) return a(...) end,\n"
    "  function(a) return tostring(a) end, function(a) return tonumber(a) end,\n"
    "  function(a) local r = {} for k, v in pairs(a) do r[#r + 1] = k; if #r > 8 then break end end return r end,\n"
    "  function(a) for i, v in ipairs(a) do if i > 8 then break end end end,\n"
    "  function(a, b) return rawequal(a, b), rawlen(a) end,\n"
    "}\n"
    "local ld = load or loadstring\n"
    "for _, src in ipairs({'return function(a, b) return a // b end', 'return function(a, b) return a & b end',\n"
    "    'return function(a, b) return a | b end', 'return function(a, b) return a ~ b end',\n"
    "    'return function(a, b) return a << b end', 'return function(a, b) return a >> b end',\n"
    "    'return function(a) return ~a end'}) do\n"
    "  local f = v53 and ld(src)\n"
    "  if f then OPS[#OPS + 1] = f() end\n"
    "end\n"
    "local function mkmt()\n"
    "  local t = {}\n"
    "  for _ = 1, u8() % 4 + 1 do\n"
    "    local ev = EVENTS[u8() % #EVENTS + 1]\n"
    "    local v = get()\n"
    "    if ev == '__gc' and not gc_ok then v = gcsafe end\n"
    "    if ev == '__mode' then v = ({'k', 'v', 'kv'})[u8() % 3 + 1] end\n"
    "    t[ev] = v\n"
    "    if log then log(nil, 300, true, 'mt.' .. ev .. ' = ' .. type(v) .. ' ' .. (type(v) == 'number' and tostring(v) or '')) end\n"
    "  end\n"
    "  if u8() % 4 == 0 then t[1] = get() end\n"
    "  return t\n"
    "end\n"
    "local function args(f, ...)\n"
    "  local n = u8() % 6\n"
    "  local a = {}\n"
    "  for i = 1, n do a[i] = get() end\n"
    "  local r = pack(pcall(f, unpack(a, 1, n)))\n"
    "  if log then log(f, n, r[1], r[2], a[1], a[2]) end\n"
    "  return r\n"
    "end\n"
    "local function keep(r)\n"
    "  if r.n >= 2 then put(r[2]) end\n"
    "  if r.n >= 3 then put(r[3]) end\n"
    "  if r.n >= 4 then put(r[4]) end\n"
    "  check(r[1], r[2])\n"
    "end\n"
    /* the BaseType chain of the indexers: tables the driver keeps to itself,
     * at first; they reach the pool too, so the operations can make cycles, which
     * the indexers stop with an error since 2026-10-28 (item 18) */
    "local function typechain()\n"
    "  local depth = u8() % 4\n"
    "  local head, funcs = nil, {}\n"
    "  for i = 1, depth do\n"
    "    local t = {BaseType = head}\n"
    "    if u8() % 2 == 0 then funcs[t] = get() end\n"
    "    head = t\n"
    "  end\n"
    "  if u8() % 4 == 0 and head then put(head); put(funcs) end\n"
    "  return head, funcs\n"
    "end\n"
    /* make one value */
    "local function make()\n"
    "  local k = u8() % 30\n"
    "  if log then log(nil, 100 + k) end\n"
    "  if k == 0 then put(nil) elseif k == 1 then put(false) elseif k == 2 then put(true)\n"
    "  elseif k == 3 then put(INTS[u8() % #INTS + 1])\n"
    "  elseif k == 4 then put(FLOATS[u8() % #FLOATS + 1])\n"
    "  elseif k == 5 then put(STRS[u8() % #STRS + 1])\n"
    "  elseif k == 6 and X.num then local s = ssub(input, pos, pos + 7); pos = pos + 8; put(X.num(s, u8() % 4))\n"
    "  elseif k == 7 then local l = u8() % 24; put(ssub(input, pos, pos + l - 1)); pos = pos + l\n"
    "  elseif k == 8 then put({})\n"
    "  elseif k == 9 then local t = {} for i = 1, u8() % 5 do t[i] = get() end put(t)\n"
    "  elseif k == 10 then put(setmetatable({}, mkmt()))\n"
    "  elseif k == 11 and X.lud then put(X.lud(u8()))\n"
    "  elseif k == 12 and X.ud then put(X.ud(u8(), u8(), u8()))\n"
    "  elseif k == 13 and X.struct then local s = X.struct(u8() % 72); KEEP[#KEEP + 1] = s; put(s)\n"
    "  elseif k == 14 and X.newstruct then put(X.newstruct(get()))\n"
    "  elseif k == 15 and X.csobj then put(X.csobj(get(), u8() % 2))\n"
    "  elseif k == 16 then put(SNIP[u8() % #SNIP + 1])\n"
    "  elseif k == 17 then put(N[u8() % #N + 1])\n"
    "  elseif k == 18 then\n"
    "    local f = get()\n"
    "    local ok, c = pcall(co.create, f)\n"
    "    if ok then\n"
    "      if u8() % 2 == 0 then put(pack(pcall(co.resume, c, get(), get()))[3]) end\n"
    "      put(c)\n"
    "    end\n"
    "  elseif k == 19 and X.csfunc then\n"
    "    local f = X.csfunc(u8(), u8() % 4, get(), get(), get())\n"
    "    TRAMP[f] = true; put(f)\n"
    "  elseif k == 20 and X.cswrapper then local f = X.cswrapper(get()); TRAMP[f] = true; put(f)\n"
    "  elseif k == 21 and X.indexer then\n"
    "    local kind = u8() % 4\n"
    "    local base, funcs = typechain()\n"
    "    local f\n"
    "    if kind == 0 then f = X.indexer(0, get(), get(), get(), base, funcs, get()); BLOCK[f] = {}\n"
    "    elseif kind == 1 then f = X.indexer(1, get(), get(), base, funcs, get()); BLOCK[f] = {}\n"
    /* cls_indexer reads its fields (upvalue 2) with lua_rawget: C# passes the class
     * table; anything else needs debug.setupvalue (item 18) */
    "    elseif kind == 2 then\n"
    "      local fields = get()\n"
    "      if not tricks and type(fields) ~= 'table' then fields = {} end\n"
    "      f = X.indexer(2, get(), fields, base, funcs); BLOCK[f] = {[2] = not tricks and 1 or nil}\n"
    "    else f = X.indexer(3, get(), base, funcs); BLOCK[f] = {} end\n"
    "    put(f)\n"
    "  elseif k == 22 and xlua then\n"
    "    local r = pack(pcall(xlua.genaccessor, get(), get()))\n"
    "    keep(r)\n"
    "  elseif k == 23 and X.cstable then put(X.cstable(get()))\n"
    "  elseif k == 24 and X.foreign then local v = X.foreign(u8()); if v ~= nil then FOREIGN[v] = true end; put(v)\n"
    "  elseif k == 25 and xlua then local r = pack(pcall(xlua.structclone, get())); keep(r)\n"
    "  elseif k == 26 and X.typed then local s = X.typed(u8() % 4); KEEP[#KEEP + 1] = s; put(s)\n"
    "  elseif k == 27 and buffer then local ok, b = pcall(buffer.create, u8() % 40); put(b)\n"
    "  elseif k == 28 then put(INTS[u8() % #INTS + 1] + (u8() % 3 - 1))\n"
    "  else put(STRS[u8() % #STRS + 1]) end\n"
    "end\n"
    /* the debug library on the pool */
    "local function debugop()\n"
    "  local k = u8() % 12\n"
    "  if log then log(nil, 200 + k) end\n"
    "  if k == 0 then put(dbg.getmetatable(get()))\n"
    "  elseif k == 1 then\n"
    "    local v, mt = get(), get()\n"
    "    if type(mt) ~= 'table' then mt = nil end\n"
    /* LuaJIT's trace recorder takes the metatable of an io file as immutable and
     * reads it without a NULL check (lj_record_mm_lookup): a known LuaJIT limit */
    "    if not tricks and FOREIGN[v] then return end\n"
    "    pcall(dbg.setmetatable, v, mt)\n"
    "  elseif k == 2 or k == 3 then\n"
    "    local f, n = get(), u8() % 9 - 1\n"
    "    if type(f) ~= 'function' then return end\n"
    "    if not tricks and (TRAMP[f] and n <= 1 or (BLOCK[f] and BLOCK[f][n])) then return end\n"
    "    if k == 2 then local r = pack(pcall(dbg.getupvalue, f, n)); keep(r)\n"
    "    else\n"
    "      local v = get()\n"
    "      if not tricks and TRAMP[f] and n == 1 then return end\n"
    "      keep(pack(pcall(dbg.setupvalue, f, n, v)))\n"
    "    end\n"
    "  elseif k == 4 then\n"
    "    local f = get()\n"
    "    if type(f) ~= 'function' and f ~= nil then return end\n"
    "    local masks = {'c', 'r', 'cr', 'l', ''}\n"
    "    pcall(dbg.sethook, f, masks[u8() % 5 + 1], u8() % 3)\n"
    "  elseif k == 5 then keep(pack(pcall(dbg.getinfo, get(), 'nSlu')))\n"
    /* any level: luaL_traceback clamps it since item 24 (build/LUA_PATCHES.md) */
    "  elseif k == 6 then keep(pack(pcall(dbg.traceback, get(), get())))\n"
    "  elseif k == 7 and dbg.getuservalue then keep(pack(pcall(dbg.getuservalue, get(), u8() % 3)))\n"
    "  elseif k == 8 and dbg.setuservalue then\n"
    "    local u = get()\n"
    "    if type(u) == 'userdata' then pcall(dbg.setuservalue, u, get(), u8() % 3) end\n"
    "  elseif k == 9 and dbg.upvalueid then\n"
    "    local f = get()\n"
    "    if type(f) == 'function' then keep(pack(pcall(dbg.upvalueid, f, u8() % 9 - 1))) end\n"
    "  elseif k == 10 and tricks then\n"
    "    local r = dbg.getregistry()\n"
    "    local key = get()\n"
    "    if key ~= nil and key == key then\n"
    "      if u8() % 2 == 0 then put(r[key]) else pcall(rawset, r, key, get()) end\n"
    "    else put(r) end\n"
    "  elseif k == 11 and xlua then pcall(xlua.sethook, get())\n"
    "  end\n"
    "end\n"
    "local steps = 0\n"
    "put(0); put(1); put(''); put({}); put(true)\n"
    "if X.struct then local s = X.struct(16); KEEP[#KEEP + 1] = s; put(s) end\n"
    "if X.csobj then put(X.csobj(1, 1)) end\n"
    "while pos <= len and steps < 400 do\n"
    "  steps = steps + 1\n"
    "  local op = u8() % 16\n"
    "  if log then log(nil, op) end\n"
    "  if op <= 2 then make()\n"
    "  elseif op <= 6 then keep(args(N[u8() % #N + 1]))\n"
    "  elseif op == 7 then keep(args(get()))\n"
    "  elseif op == 8 then keep(args(OPS[u8() % #OPS + 1]))\n"
    "  elseif op == 9 then\n"
    "    local t = get()\n"
    "    if type(t) == 'table' then\n"
    "      if u8() % 2 == 0 then pcall(rawset, t, get(), get()) else pcall(setmetatable, t, u8() % 3 == 0 and nil or mkmt()) end\n"
    "    end\n"
    "  elseif op == 10 and dbg then debugop()\n"
    "  elseif op == 11 then\n"
    "    local c = get()\n"
    "    if type(c) == 'thread' then\n"
    "      local s = u8() % 4\n"
    "      if s == 0 or s == 3 then keep(pack(pcall(co.resume, c, get(), get())))\n"
    "      elseif s == 1 then put(co.status(c))\n"
    "      elseif co.close then keep(pack(pcall(co.close, c))) end\n"
    "    end\n"
    "  elseif op == 12 then\n"
    "    local s = u8() % 4\n"
    "    if s == 0 then collectgarbage('collect'); if log then log(nil, 303, true, 'collect') end\n"
    "    elseif s == 1 then local n = u8(); if log then log(nil, 304, true, 'step ' .. n) end; collectgarbage('step', n)\n"
    "    elseif X.gc then\n"
    "      local w, d = u8() % 10, get()\n"
    "      if log then log(nil, 305, true, 'X.gc ' .. w .. ' ' .. type(d) .. ' ' .. (type(d) == 'number' and tostring(d) or '')) end\n"
    "      keep(pack(pcall(X.gc, w, d)))\n"
    "    end\n"
    "  elseif op == 13 then\n"
    "    local name = GLOBALS[u8() % #GLOBALS + 1]\n"
    "    if u8() % 4 == 0 then\n"
    "      local mt = mkmt()\n"
    "      if log then log(nil, 301, true, 'setmetatable(_G, mt)') end\n"
    "      pcall(setmetatable, G, mt)\n"
    "    else\n"
    "      local v = get()\n"
    "      if log then log(nil, 302, true, '_G.' .. name .. ' = ' .. type(v)) end\n"
    "      G[name] = v\n"
    "    end\n"
    "  elseif op == 14 then\n"
    "    local f = N[u8() % #N + 1]\n"
    "    local c = co.create(function(...) return f(...) end)\n"
    "    keep(pack(pcall(co.resume, c, get(), get(), get())))\n"
    "  else\n"
    "    local f = get()\n"
    "    if type(f) == 'function' then keep(pack(pcall(f, get(), get()))) end\n"
    "  end\n"
    "end\n"
    "check(true)\n";

#if !defined(FUZZ_BRIDGE_CORE)
/* ------------------------------------------------------------------------
 * The bridge presets.
 * ------------------------------------------------------------------------ */

/* The exports of build/xlua.c, build/xlua_core.c and build/i64lib.c that C#
 * imports (LuaDLLBridge.cs, Core/LuaDLL.cs), declared as C# calls them. */
LUA_API int xlua_tocsobj_safe(lua_State *L, int idx);
LUA_API int xlua_tocsobj_fast(lua_State *L, int idx);
LUA_API uint32_t xlua_touint(lua_State *L, int idx);
LUA_API int xlua_tointeger(lua_State *L, int idx);
LUA_API int64_t xlua_tointeger64(lua_State *L, int idx, int *valid);
LUA_API int xlua_isinteger_number(lua_State *L, int idx);
LUA_API int xlua_number_kind(lua_State *L, int idx);
LUA_API void xlua_pushargerror(lua_State *L, int arg, const char *extramsg);
LUA_API int xlua_pgettable(lua_State *L, int idx);
LUA_API int xlua_psettable(lua_State *L, int idx);
LUA_API int xlua_pgettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_psettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_getglobal(lua_State *L, const char *name);
LUA_API int xlua_setglobal(lua_State *L, const char *name);
LUA_API int xlua_pnext(lua_State *L, int idx);
LUA_API int xlua_plen(lua_State *L, int idx, uint32_t *len);
LUA_API int xlua_running(lua_State *L);
LUA_API int xlua_tryget_cachedud(lua_State *L, int key, int cache_ref);
LUA_API int xlua_tryget_cachedud_checked(lua_State *L, int key, int cache_ref);
LUA_API void xlua_pushcsobj(lua_State *L, int key, int meta_ref, int need_cache, int cache_ref);
LUA_API int xlua_gettypeid(lua_State *L, int idx);
LUA_API void *xlua_newstruct(lua_State *L, int size, int meta_ref);
LUA_API void xlua_pushcstable(lua_State *L, unsigned int size, int meta_ref);
LUA_API int xlua_is_eq_str(lua_State *L, int idx, const char *str, int str_len);
LUA_API void xlua_getloaders(lua_State *L);
LUA_API void xlua_rawgeti(lua_State *L, int idx, int64_t n);
LUA_API void xlua_rawseti(lua_State *L, int idx, int64_t n);
LUA_API int load_error_func(lua_State *L, int ref);
LUA_API int pcall_prepare(lua_State *L, int error_func_ref, int func_ref);
LUA_API int get_error_func_ref(lua_State *L);
LUA_API int errorfunc(lua_State *L);
LUA_API int xlua_gc(lua_State *L, int what, int data);
LUA_API void xlua_push_csharp_function(lua_State *L, lua_CFunction fn, int n);
LUA_API void xlua_push_csharp_wrapper(lua_State *L, int wrapperid);
LUA_API void xlua_set_csharp_wrapper_caller(int (*caller)(lua_State *L, int wrapperid, int top));
LUALIB_API int xlua_upvalueindex(int n);
LUALIB_API int xlua_csharp_str_error(lua_State *L, const char *msg);
LUALIB_API int xlua_csharp_error(lua_State *L);
LUA_API int gen_obj_indexer(lua_State *L);
LUA_API int gen_obj_newindexer(lua_State *L);
LUA_API int gen_cls_indexer(lua_State *L);
LUA_API int gen_cls_newindexer(lua_State *L);
LUA_API int xlua_check_struct_accessors(lua_State *L);
LUALIB_API void lua_pushint64(lua_State *L, int64_t n);
LUALIB_API void lua_pushuint64(lua_State *L, uint64_t n);
LUALIB_API int lua_isint64(lua_State *L, int pos);
LUALIB_API int lua_isuint64(lua_State *L, int pos);
LUALIB_API int64_t lua_toint64(lua_State *L, int pos);
LUALIB_API uint64_t lua_touint64(lua_State *L, int pos);
#define PACK_DECL(name, type) \
    LUALIB_API int xlua_pack_##name(void *p, int offset, type field); \
    LUALIB_API int xlua_unpack_##name(void *p, int offset, type *field);
PACK_DECL(int8_t, int8_t)
PACK_DECL(int16_t, int16_t)
PACK_DECL(int32_t, int32_t)
PACK_DECL(int64_t, int64_t)
PACK_DECL(float, float)
PACK_DECL(double, double)
LUALIB_API int xlua_pack_float2(void *p, int offset, float f1, float f2);
LUALIB_API int xlua_unpack_float2(void *p, int offset, float *f1, float *f2);
LUALIB_API int xlua_pack_float6(void *p, int offset, float f1, float f2, float f3, float f4, float f5, float f6);
LUALIB_API int xlua_unpack_float6(void *p, int offset, float *f1, float *f2, float *f3, float *f4, float *f5,
                                  float *f6);
LUALIB_API int xlua_pack_decimal(void *p, int offset, const int *decimal);
LUALIB_API int xlua_unpack_decimal(void *p, int offset, uint8_t *scale, uint8_t *sign, int *hi32, uint64_t *lo64);
#if defined(FUZZ_SNAPSHOT)
LUA_API int xlua_memory_snapshot(lua_State *L, int max_objects, void **out, int *out_size);
LUA_API void xlua_memory_snapshot_free(void *buffer);
#endif
#if LUA_VERSION_NUM >= 503
#include "xlua_core.h"
/* xlua.c's lua_pcall, the protected entry C# calls (the header's is a macro of lua_pcallk). */
#undef lua_pcall
LUA_API int lua_pcall(lua_State *L, int nargs, int nresults, int errfunc);
LUA_API int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
#define FUZZ_CORE_STATE 1
#endif

#define STRUCT_TYPEID 5
#define CSOBJ_TYPEID 6

static int struct_meta_ref, csobj_meta_ref, cache_ref, error_func_ref;
static int unprot;         /* helpers C# calls outside a protected call that are running */
static const char *unprot_name;
static struct {
    unsigned char *p;
    unsigned int len;
} headers[64];             /* struct userdata the harness made, kept alive by the driver */
static int nheaders;
static union {
    int32_t i[16];
    unsigned char b[64];
} fake_struct = {{-1, 1 << 20}}; /* a light userdata that looks like a struct with a huge length */
static int other_static;

static void fail(const char *what, const char *detail) {
    fprintf(stderr, "fuzz_bridge: %s%s%s\n", what, detail != NULL ? ": " : "", detail != NULL ? detail : "");
    abort();
}

/* A Lua value as a C# int, saturated, and as an int64. */
static int64_t arg_i64(lua_State *L, int idx) {
#if LUA_VERSION_NUM >= 503
    int isnum;
    lua_Integer v = lua_tointegerx(L, idx, &isnum);
    if (isnum) {
        return v;
    }
#endif
    if (lua_isint64(L, idx) || lua_isuint64(L, idx)) {
        return lua_toint64(L, idx);
    }
    {
        lua_Number d = lua_tonumber(L, idx);
        if (!(d >= -9223372036854775808.0 && d < 9223372036854775808.0)) {
            return d > 0 ? INT64_MAX : (d < 0 ? INT64_MIN : 0);
        }
        return (int64_t)d;
    }
}

static int arg_int(lua_State *L, int idx) {
    int64_t v = arg_i64(L, idx);
    return v > INT32_MAX ? INT32_MAX : (v < INT32_MIN ? INT32_MIN : (int)v);
}

/* The name is the outermost helper's: one that raised leaves the count above
 * 0, and a pcall of the script may catch its error before X.check runs. */
#define ENTER(name) do { if (unprot++ == 0) unprot_name = (name); } while (0)
#define LEAVE() (unprot--)

static void expect_top(lua_State *L, int top, const char *name) {
    if (lua_gettop(L) != top) {
        char buf[96];
        snprintf(buf, sizeof(buf), "%s left %d values, expected %d", name, lua_gettop(L), top);
        fail("stack", buf);
    }
}

/* X.check(ok, err): no helper of the list above raised (a memory error excepted),
 * and every struct header the harness made is intact. */
static int x_check(lua_State *L) {
    int i;
    if (unprot != 0) {
        const char *msg = lua_tostring(L, 2);
        if (lua_toboolean(L, 1) || msg == NULL || strstr(msg, "not enough memory") == NULL) {
            fprintf(stderr, "fuzz_bridge: the error: %s\n", msg != NULL ? msg : luaL_typename(L, 2));
            fail("a helper that C# calls unprotected raised an error", unprot_name);
        }
        unprot = 0;
    }
    for (i = 0; i < nheaders; i++) {
        int32_t id;
        uint32_t len;
        memcpy(&id, headers[i].p, 4);
        memcpy(&len, headers[i].p + 4, 4);
        if (id != -1 || len != headers[i].len) {
            fail("struct header changed", NULL);
        }
    }
    return 0;
}

/* X.log(f, nargs, ok, err, a1, a2): FUZZ_BRIDGE_TRACE=2 prints each call the driver makes. */
static int x_log(lua_State *L) {
    lua_CFunction cf = lua_tocfunction(L, 1);
    Dl_info info;
    const char *name = "?";
    lua_getfield(L, LUA_REGISTRYINDEX, "__fuzz_names");
    if (lua_istable(L, -1)) {
        lua_pushvalue(L, 1);
        lua_rawget(L, -2);
    }
    if (lua_type(L, -1) == LUA_TSTRING) {
        name = lua_tostring(L, -1);
    } else if (cf != NULL && dladdr((void *)cf, &info) && info.dli_sname != NULL) {
        name = info.dli_sname;
    } else if (lua_type(L, 1) == LUA_TFUNCTION && cf == NULL) {
        name = "(Lua function)";
    } else if (cf == NULL) {
        name = lua_typename(L, lua_type(L, 1));
    }
    fprintf(stderr, "call %s/%d n=%d ok=%d (%s, %s) %s\n", name,
            lua_getupvalue(L, 1, 1) != NULL && lua_type(L, -1) == LUA_TNUMBER ? (int)lua_tointeger(L, -1) : -1,
            (int)lua_tointeger(L, 2), lua_toboolean(L, 3), luaL_typename(L, 5), luaL_typename(L, 6),
            lua_type(L, 4) == LUA_TSTRING ? lua_tostring(L, 4) : "");
    return 0;
}

/* X.num(bytes, mode): 0 an integer (a double on LuaJIT), 1 a double with these
 * bits, 2 lua_pushint64, 3 lua_pushuint64. */
static int x_num(lua_State *L) {
    size_t n;
    const char *s = lua_tolstring(L, 1, &n);
    uint64_t bits = 0;
    double d;
    if (s != NULL) {
        memcpy(&bits, s, n < 8 ? n : 8);
    }
    switch (lua_tointeger(L, 2)) {
    case 0:
#if LUA_VERSION_NUM >= 503
        lua_pushinteger(L, (lua_Integer)bits);
#else
        lua_pushnumber(L, (lua_Number)(int64_t)bits);
#endif
        break;
    case 1:
        memcpy(&d, &bits, 8);
        lua_pushnumber(L, d);
        break;
    case 2:
        lua_pushint64(L, (int64_t)bits);
        break;
    default:
        lua_pushuint64(L, bits);
        break;
    }
    return 1;
}

/* X.lud(sel): a light userdata. A native that reads through one is the bug. */
static int x_lud(lua_State *L) {
    static void *const values[] = {NULL, (void *)1, (void *)UINTPTR_MAX, NULL, fake_struct.b, &other_static, (void *)16};
    int sel = (int)(lua_tointeger(L, 1) % 7);
    lua_pushlightuserdata(L, sel == 3 ? xlua_tag() : values[sel < 0 ? -sel : sel]);
    return 1;
}

/* X.ud(size, fill, mt): a full userdata of another library. Without tricks it
 * has room for an int and does not start with -1, the struct marker: shorter
 * ones, and those with a struct header, are crash-inventory item 10. */
static int x_ud(lua_State *L) {
    unsigned size = (unsigned)lua_tointeger(L, 1) % 33;
    unsigned fill = (unsigned)lua_tointeger(L, 2) & 0xff;
    int mt = (int)(lua_tointeger(L, 3) % 6);
    unsigned char *p;
    if (!tricks) {
        if (size < 4) {
            size = 4;
        }
        if (fill == 0xff) {
            fill = 0x7f;
        }
    }
    p = (unsigned char *)lua_newuserdata(L, size);
    memset(p, (int)fill, size);
    if (size >= 8 && (fill & 1)) {
        uint32_t big = 1u << 20;
        memcpy(p + 4, &big, 4); /* a length far past the block */
    }
    switch (mt) {
    case 1:
        lua_newtable(L);
        break;
    case 2:
        lua_newtable(L);
        lua_pushinteger(L, STRUCT_TYPEID);
        lua_rawseti(L, -2, 1);
        break;
    case 3:
        lua_newtable(L);
        lua_pushinteger(L, CSOBJ_TYPEID);
        lua_rawseti(L, -2, 1);
        break;
    case 4: /* the bridge tag on another library's userdata: tricks only */
        if (!tricks) {
            return 1;
        }
        lua_rawgeti(L, LUA_REGISTRYINDEX, struct_meta_ref);
        break;
    default:
        return 1;
    }
    lua_setmetatable(L, -2);
    return 1;
}

/* X.foreign(sel): a userdata a standard library makes (io's files), or
 * LuaJIT's newproxy (0 bytes, item 10: tricks only). */
static int x_foreign(lua_State *L) {
    int sel = (int)(lua_tointeger(L, 1) % 2);
    if (sel == 1 && tricks) {
        lua_getglobal(L, "__fuzz_newproxy");
        if (lua_isfunction(L, -1)) {
            lua_pushboolean(L, 1);
            lua_call(L, 1, 1);
            return 1;
        }
        lua_pop(L, 1);
    }
    lua_getfield(L, LUA_REGISTRYINDEX, "__fuzz_stdin");
    return 1;
}

static void add_header(void *p, unsigned int len) {
    if (p != NULL && nheaders < 64) {
        headers[nheaders].p = (unsigned char *)p;
        headers[nheaders].len = len;
        nheaders++;
    }
}

/* X.struct(size): a struct as ObjectTranslator pushes one (xlua_pushstruct). */
static int x_struct(lua_State *L) {
    unsigned int size = (unsigned int)(lua_tointeger(L, 1) & 0xff);
    void *p;
    ENTER("xlua_pushstruct");
    p = xlua_pushstruct(L, size, struct_meta_ref);
    LEAVE();
    memset((char *)p + 8, 0x5a, size);
    add_header(p, size);
    return 1;
}

/* X.typed(kind): a struct of the harness type whose metatable has the
 * generated __index and __newindex (pushed with the same metatable). */
static int x_typed(lua_State *L) {
    void *p;
    ENTER("xlua_pushstruct");
    p = xlua_pushstruct(L, 24, struct_meta_ref);
    LEAVE();
    memset((char *)p + 8, 0, 24);
    add_header(p, 24);
    (void)lua_tointeger(L, 1);
    return 1;
}

/* X.newstruct(size): xlua_newstruct with any int, as generated code passes it. */
static int x_newstruct(lua_State *L) {
    int size = arg_int(L, 1);
    void *data;
    int top;
    if (size > 4096) {
        size = 4096;
    }
    lua_settop(L, 0);
    top = lua_gettop(L);
    ENTER("xlua_newstruct");
    data = xlua_newstruct(L, size, struct_meta_ref);
    LEAVE();
    expect_top(L, top + 1, "xlua_newstruct");
    if ((data == NULL) != (size < 0)) {
        fail("xlua_newstruct", "result does not match the size");
    }
    return 1;
}

/* X.csobj(key, need_cache): a C# object (xlua_pushcsobj). Keys are object pool
 * indices, which are never negative. */
static int x_csobj(lua_State *L) {
    int key = arg_int(L, 1);
    int need = lua_toboolean(L, 2) || lua_tointeger(L, 2) != 0;
    int top;
    if (!tricks) {
        key = key < 0 ? 0 : key % 1000;
    }
    lua_settop(L, 0);
    top = lua_gettop(L);
    ENTER("xlua_pushcsobj");
    xlua_pushcsobj(L, key, csobj_meta_ref, need, cache_ref);
    LEAVE();
    expect_top(L, top + 1, "xlua_pushcsobj");
    return 1;
}

/* X.cstable(size) */
static int x_cstable(lua_State *L) {
    int64_t size = arg_i64(L, 1);
    lua_settop(L, 0);
    ENTER("xlua_pushcstable");
    xlua_pushcstable(L, size < 0 ? 0 : (unsigned int)(size % 4096), csobj_meta_ref);
    LEAVE();
    expect_top(L, 1, "xlua_pushcstable");
    return 1;
}

/* X.cached(key, checked): ObjectTranslator.tryGetCachedUserdata. */
static int x_cached(lua_State *L) {
    int key = arg_int(L, 1), checked = lua_toboolean(L, 2), r;
    lua_settop(L, 0);
    ENTER("xlua_tryget_cachedud");
    r = checked ? xlua_tryget_cachedud_checked(L, key, cache_ref) : xlua_tryget_cachedud(L, key, cache_ref);
    LEAVE();
    if (r != 0 && r != 1) {
        fail("xlua_tryget_cachedud", "result not 0 or 1");
    }
    expect_top(L, r, "xlua_tryget_cachedud");
    if (r == 1 && checked && xlua_tocsobj_safe(L, 1) != key) {
        fail("xlua_tryget_cachedud_checked", "returned another object");
    }
    return r;
}

/* The value readers: one argument, the stack unchanged. */
static int x_read(lua_State *L) {
    int which = (int)lua_tointeger(L, lua_upvalueindex(1));
    int top = lua_gettop(L), valid = 0, r = 0;
    int64_t v = 0;
    if (top == 0) {
        lua_pushnil(L);
        top = 1;
    }
    if (trace > 2) {
        fprintf(stderr, "reader %d on %s, top %d\n", which, luaL_typename(L, 1), top);
    }
    ENTER("a value reader");
    switch (which) {
    case 0: r = xlua_tocsobj_safe(L, 1); break;
    case 1: r = xlua_tocsobj_fast(L, 1); break;
    case 2: v = xlua_touint(L, 1); break;
    case 3: r = xlua_tointeger(L, 1); break;
    case 4: v = xlua_tointeger64(L, 1, &valid); r = valid; break;
    case 5: r = xlua_isinteger_number(L, 1); break;
    case 6: r = xlua_number_kind(L, 1); if (r < 0 || r > 5) fail("xlua_number_kind", "out of range"); break;
    case 7: r = xlua_gettypeid(L, 1); break;
    case 8: v = lua_toint64(L, 1); break;
    case 9: v = (int64_t)lua_touint64(L, 1); break;
    case 10: r = lua_isint64(L, 1); break;
    case 11: r = lua_isuint64(L, 1); break;
    case 12: r = xlua_running(L); break;
    default: {
        size_t n;
        const char *s = lua_tolstring(L, 2, &n);
        r = xlua_is_eq_str(L, 1, s != NULL ? s : "", s != NULL ? (int)n : 0);
        break;
    }
    }
    LEAVE();
    expect_top(L, top, "a value reader");
    lua_pushinteger(L, r);
    lua_pushnumber(L, (lua_Number)v);
    return 2;
}

/* X.argerror(arg, extramsg) */
static int x_argerror(lua_State *L) {
    int arg = arg_int(L, 1), top;
    const char *msg = lua_tostring(L, 2);
    top = lua_gettop(L);
    ENTER("xlua_pushargerror");
    xlua_pushargerror(L, arg, msg != NULL ? msg : "");
    LEAVE();
    expect_top(L, top + 1, "xlua_pushargerror");
    return 1;
}

/* The protected table helpers (LuaTable.Get/Set/GetInPath/SetInPath, LuaEnv.Global). */
static int x_table(lua_State *L) {
    int which = (int)lua_tointeger(L, lua_upvalueindex(1)), status = 0;
    const char *s;
    lua_settop(L, 3);
    ENTER("a protected table helper");
    switch (which) {
    case 0: /* t, k -> v */
        lua_settop(L, 2);
        status = xlua_pgettable(L, 1);
        expect_top(L, 2, "xlua_pgettable");
        break;
    case 1: /* t, k, v */
        status = xlua_psettable(L, 1);
        expect_top(L, status == 0 ? 1 : 2, "xlua_psettable");
        break;
    case 2: /* t, path */
        s = lua_tostring(L, 2);
        lua_settop(L, 2);
        status = xlua_pgettable_bypath(L, 1, s != NULL ? s : "");
        expect_top(L, 3, "xlua_pgettable_bypath");
        break;
    case 3: /* t, path, v */
        s = lua_tostring(L, 2);
        lua_pushvalue(L, 2); /* the path string stays referenced */
        lua_insert(L, 1);
        status = xlua_psettable_bypath(L, 2, s != NULL ? s : "");
        expect_top(L, status == 0 ? 3 : 4, "xlua_psettable_bypath");
        break;
    case 4: /* name -> v */
        s = lua_tostring(L, 1);
        lua_settop(L, 1);
        status = xlua_getglobal(L, s != NULL ? s : "");
        expect_top(L, 2, "xlua_getglobal");
        break;
    default: /* name, v */
        s = lua_tostring(L, 1);
        lua_settop(L, 2);
        status = xlua_setglobal(L, s != NULL ? s : "");
        expect_top(L, status == 0 ? 1 : 2, "xlua_setglobal");
        break;
    }
    LEAVE();
    if (status != 0) {
        return lua_error(L);
    }
    return 1;
}

/* X.pnext(t, k): one step of LuaTable.ForEach (tables only, as C# has). */
static int x_pnext(lua_State *L) {
    int r;
    lua_settop(L, 2);
    if (!lua_istable(L, 1)) {
        return 0;
    }
    ENTER("xlua_pnext");
    r = xlua_pnext(L, 1);
    LEAVE();
    expect_top(L, r == 1 ? 3 : (r == 0 ? 1 : 2), "xlua_pnext");
    if (r == -1) {
        return lua_error(L);
    }
    return r == 1 ? 2 : 0;
}

/* X.plen(v) */
static int x_plen(lua_State *L) {
    uint32_t n = 0;
    int r;
    lua_settop(L, 1);
    ENTER("xlua_plen");
    r = xlua_plen(L, 1, &n);
    LEAVE();
    expect_top(L, r == 0 ? 1 : 2, "xlua_plen");
    if (r != 0) {
        return lua_error(L);
    }
    lua_pushinteger(L, n);
    return 1;
}

/* X.setfenv(f, env): LuaEnv.LoadString with an env (any value as the env). */
static int x_setfenv(lua_State *L) {
    int r;
    lua_settop(L, 2);
    if (!lua_istable(L, 2)) {
        return 0; /* C# passes a LuaTable; LuaJIT's lua_setfenv requires one */
    }
    ENTER("lua_setfenv");
    r = lua_setfenv(L, 1);
    LEAVE();
    expect_top(L, 1, "lua_setfenv");
    lua_pushboolean(L, r);
    return 2;
}

/* X.getloaders(): the first step of LuaEnv.AddSearcher. */
static int x_getloaders(lua_State *L) {
    lua_settop(L, 0);
    ENTER("xlua_getloaders");
    xlua_getloaders(L);
    LEAVE();
    expect_top(L, 1, "xlua_getloaders");
    return 1;
}

/* X.rawgeti(t, n) and X.rawseti(t, n, v): tables only, as C# calls them. */
static int x_rawi(lua_State *L) {
    int64_t n = arg_i64(L, 2);
    int set = (int)lua_tointeger(L, lua_upvalueindex(1));
    lua_settop(L, 3);
    if (!lua_istable(L, 1)) {
        return 0;
    }
    ENTER("xlua_rawgeti/xlua_rawseti");
    if (set) {
        xlua_rawseti(L, 1, n);
        expect_top(L, 2, "xlua_rawseti");
    } else {
        lua_settop(L, 1);
        xlua_rawgeti(L, 1, n);
        expect_top(L, 2, "xlua_rawgeti");
    }
    LEAVE();
    return 1;
}

/* X.pcallref(f, ...): a delegate bridge: pcall_prepare and xlua.c's lua_pcall
 * with LuaEnv's error handler. */
static int x_pcallref(lua_State *L) {
    int n = lua_gettop(L), ref, base, status, i;
    if (n == 0) {
        lua_pushnil(L);
        n = 1;
    }
    lua_pushvalue(L, 1);
    ref = luaL_ref(L, LUA_REGISTRYINDEX);
    luaL_checkstack(L, n + 4, NULL);
    ENTER("pcall_prepare");
    base = pcall_prepare(L, error_func_ref, ref);
    LEAVE();
    for (i = 2; i <= n; i++) {
        lua_pushvalue(L, i);
    }
    status = lua_pcall(L, n - 1, LUA_MULTRET, base);
    luaL_unref(L, LUA_REGISTRYINDEX, ref);
    lua_remove(L, base);
    lua_pushinteger(L, status);
    lua_insert(L, n + 1);
    return lua_gettop(L) - n;
}

/* X.gc(what, data): LuaEnv's GC controls (the operations LuaGCOptions names). */
static int x_gc(lua_State *L) {
    static const int ops[] = {0, 1, 2, 3, 4, 5, 6, 7, 9, 2};
    int what = ops[(unsigned)lua_tointeger(L, 1) % 10], r;
    int data = arg_int(L, 2);
    if (what == 0) {
        what = 1; /* LUA_GCSTOP would let the input grow without bound */
    }
    lua_settop(L, 0);
    ENTER("xlua_gc");
    r = xlua_gc(L, what, data);
    LEAVE();
    expect_top(L, 0, "xlua_gc");
    lua_pushinteger(L, r);
    return 1;
}

/* X.unpack(v, kind, offset) and X.pack(v, kind, offset, x): CopyByValue as
 * generated code calls it: only on a userdata whose type id is the struct's. */
static int x_copy(lua_State *L) {
    int pack = (int)lua_tointeger(L, lua_upvalueindex(1));
    int kind = (int)((unsigned)lua_tointeger(L, 2) % 9), offset = arg_int(L, 3), ok = 0;
    double x = lua_tonumber(L, 4);
    void *p;
    if (!(x > -1e9 && x < 1e9)) {
        x = 0; /* the harness's own conversions stay defined (NaN included) */
    }
    if (lua_type(L, 1) != LUA_TUSERDATA) {
        return 0;
    }
    ENTER("xlua_gettypeid");
    if (xlua_gettypeid(L, 1) != STRUCT_TYPEID) {
        LEAVE();
        return 0;
    }
    LEAVE();
    p = lua_touserdata(L, 1);
    if (pack) {
        int dec[4] = {(int)x, 0, 0, 0};
        switch (kind) {
        case 0: ok = xlua_pack_int8_t(p, offset, (int8_t)(int)fmod(x, 128)); break;
        case 1: ok = xlua_pack_int16_t(p, offset, (int16_t)(int)fmod(x, 32768)); break;
        case 2: ok = xlua_pack_int32_t(p, offset, (int32_t)fmod(x, 2147483648.0)); break;
        case 3: ok = xlua_pack_int64_t(p, offset, (int64_t)fmod(x, 9.2e18)); break;
        case 4: ok = xlua_pack_float(p, offset, (float)fmod(x, 1e30)); break;
        case 5: ok = xlua_pack_double(p, offset, x); break;
        case 6: ok = xlua_pack_float2(p, offset, 1.0f, 2.0f); break;
        case 7: ok = xlua_pack_float6(p, offset, 1, 2, 3, 4, 5, 6); break;
        default: ok = xlua_pack_decimal(p, offset, dec); break;
        }
        lua_pushboolean(L, ok);
        return 1;
    }
    {
        int8_t i8; int16_t i16; int32_t i32; int64_t i64; float f[6]; double d;
        uint8_t scale, sign; int hi; uint64_t lo;
        switch (kind) {
        case 0: ok = xlua_unpack_int8_t(p, offset, &i8); d = i8; break;
        case 1: ok = xlua_unpack_int16_t(p, offset, &i16); d = i16; break;
        case 2: ok = xlua_unpack_int32_t(p, offset, &i32); d = i32; break;
        case 3: ok = xlua_unpack_int64_t(p, offset, &i64); d = (double)i64; break;
        case 4: ok = xlua_unpack_float(p, offset, &f[0]); d = f[0]; break;
        case 5: ok = xlua_unpack_double(p, offset, &d); break;
        case 6: ok = xlua_unpack_float2(p, offset, &f[0], &f[1]); d = f[1]; break;
        case 7: ok = xlua_unpack_float6(p, offset, &f[0], &f[1], &f[2], &f[3], &f[4], &f[5]); d = f[5]; break;
        default: ok = xlua_unpack_decimal(p, offset, &scale, &sign, &hi, &lo); d = (double)lo; break;
        }
        lua_pushboolean(L, ok);
        if (ok) {
            lua_pushnumber(L, d);
            return 2;
        }
        return 1;
    }
}

#if defined(FUZZ_SNAPSHOT)
/* X.snapshot(max) */
static int x_snapshot(lua_State *L) {
    void *buffer = NULL;
    int size = 0, status, max = arg_int(L, 1);
    lua_settop(L, 0);
    ENTER("xlua_memory_snapshot");
    status = xlua_memory_snapshot(L, max < 0 ? 0 : max % 100000, &buffer, &size);
    LEAVE();
    expect_top(L, 0, "xlua_memory_snapshot");
    if (status == 0) {
        xlua_memory_snapshot_free(buffer);
    }
    lua_pushinteger(L, status);
    return 1;
}
#endif

/* X.load(s): LuaEnv.LoadString's loader (xluaL_loadbuffer), text only. */
static int x_load(lua_State *L) {
    size_t n;
    const char *s = luaL_checklstring(L, 1, &n);
    if (n > 0 && s[0] == '\033') {
        return 0; /* binary chunks: crash-inventory item 14 */
    }
    if (xluaL_loadbuffer(L, s, (int)n, "=fuzz") != 0) {
        return lua_error(L);
    }
    return 1;
}

/* ---- C# functions behind the trampolines ---- */

/* What a C# function does; upvalues as xlua_push_csharp_function gives them. */
static int fake_body(lua_State *L, int kind) {
    int n = lua_gettop(L), i;
    switch (kind) {
    case 0: /* return the arguments */
        return n;
    case 1:
        return xlua_csharp_str_error(L, "fake c# error");
    case 2: /* an error object from the arguments (any value), as a caught LuaException */
        if (n == 0) {
            lua_pushnil(L);
        } else {
            lua_pushvalue(L, 1);
        }
        return xlua_csharp_error(L);
    case 3: { /* call back into Lua (a delegate or LuaFunction.Call from C#) */
        int status;
        if (n == 0) {
            return 0;
        }
        /* nested without a limit here: the trampolines stop it at 199 levels (item 7) */
        status = lua_pcall(L, n - 1, LUA_MULTRET, 0);
        if (status != 0) {
            return xlua_csharp_error(L);
        }
        return lua_gettop(L);
    }
    case 4: /* read its own upvalues */
        for (i = 1; i <= 4; i++) {
            lua_pushvalue(L, xlua_upvalueindex(i));
        }
        return 4;
    case 5: { /* allocate (a large result: the overdraft rule of a core state) */
        int k = (int)(arg_i64(L, 1) & 15);
        luaL_checkstack(L, k + 1, NULL);
        for (i = 0; i <= k; i++) {
            char buf[256];
            memset(buf, 'a' + i, sizeof(buf));
            lua_pushlstring(L, buf, sizeof(buf));
            lua_pushvalue(L, -1);
            lua_concat(L, 2);
        }
        return 1;
    }
    case 6: /* a bad argument, as generated wrappers report it */
        ENTER("xlua_pushargerror");
        xlua_pushargerror(L, arg_int(L, 1), "fake type");
        LEAVE();
        return xlua_csharp_error(L);
    case 7: { /* LuaTable.Get on the first argument, as C# does in a callback */
        int status;
        if (n < 2) {
            return 0;
        }
        lua_settop(L, 2);
        ENTER("xlua_pgettable");
        status = xlua_pgettable(L, 1);
        LEAVE();
        if (status != 0) {
            return xlua_csharp_error(L);
        }
        return 1;
    }
    case 8: { /* read every argument as C# overload resolution does */
        int valid;
        ENTER("argument readers");
        for (i = 1; i <= n; i++) {
            (void)xlua_number_kind(L, i);
            (void)xlua_tointeger64(L, i, &valid);
            (void)xlua_tocsobj_fast(L, i);
            (void)xlua_gettypeid(L, i);
            (void)lua_toint64(L, i);
        }
        LEAVE();
        lua_pushinteger(L, n);
        return 1;
    }
    default: { /* push a struct and a C# object as results */
        void *p;
        ENTER("result pushers");
        p = xlua_pushstruct(L, 8, struct_meta_ref);
        xlua_pushcsobj(L, 3, csobj_meta_ref, 1, cache_ref);
        LEAVE();
        memset((char *)p + 8, 0, 8);
        return 2;
    }
    }
}

#define FAKE(k) static int fake_##k(lua_State *L) { return fake_body(L, k); }
FAKE(0) FAKE(1) FAKE(2) FAKE(3) FAKE(4) FAKE(5) FAKE(6) FAKE(7) FAKE(8) FAKE(9)
static const lua_CFunction fakes[10] = {fake_0, fake_1, fake_2, fake_3, fake_4, fake_5, fake_6, fake_7, fake_8, fake_9};

/* GEN_CODE_MINIMIZE: the process-wide wrapper caller. */
static int wrapper_caller(lua_State *L, int wrapperid, int top) {
    (void)top;
    if (wrapperid < 0) {
        return xlua_csharp_str_error(L, "no such wrapper");
    }
    return fake_body(L, wrapperid % 10);
}

/* X.csfunc(kind, nup, up1, up2, up3) */
static int x_csfunc(lua_State *L) {
    int kind = (int)((unsigned)lua_tointeger(L, 1) % 10), nup = (int)(lua_tointeger(L, 2) & 3), i;
    lua_settop(L, 5);
    for (i = 0; i < nup; i++) {
        lua_pushvalue(L, 3 + i);
    }
    xlua_push_csharp_function(L, fakes[kind], nup);
    return 1;
}

/* X.cswrapper(id) */
static int x_cswrapper(lua_State *L) {
    xlua_push_csharp_wrapper(L, arg_int(L, 1));
    return 1;
}

/* X.indexer(kind, upvalues...): the closures of gen_obj_indexer,
 * gen_obj_newindexer, gen_cls_indexer and gen_cls_newindexer, which C# calls
 * with the upvalues on the stack. */
static int x_indexer(lua_State *L) {
    static const int counts[] = {6, 5, 4, 3};
    int kind = (int)(lua_tointeger(L, 1) & 3);
    lua_settop(L, counts[kind] + 1);
    lua_remove(L, 1);
    switch (kind) {
    case 0: gen_obj_indexer(L); break;
    case 1: gen_obj_newindexer(L); break;
    case 2: gen_cls_indexer(L); break;
    default: gen_cls_newindexer(L); break;
    }
    expect_top(L, 1, "gen_*_indexer");
    return 1;
}

/* For the trace: registry.__fuzz_names[f] = name of the function on top. */
static void name_top(lua_State *L, const char *name) {
    if (trace > 1) {
        lua_getfield(L, LUA_REGISTRYINDEX, "__fuzz_names");
        if (!lua_istable(L, -1)) {
            lua_pop(L, 1);
            lua_newtable(L);
            lua_pushvalue(L, -1);
            lua_setfield(L, LUA_REGISTRYINDEX, "__fuzz_names");
        }
        lua_pushvalue(L, -2);
        lua_pushstring(L, name);
        lua_rawset(L, -3);
        lua_pop(L, 1);
    }
}

static void reg(lua_State *L, const char *name, lua_CFunction f, int up) {
    if (up >= 0) {
        lua_pushinteger(L, up);
        lua_pushcclosure(L, f, 1);
    } else {
        lua_pushcfunction(L, f);
    }
    name_top(L, name);
    lua_setfield(L, -2, name);
}

/* What the harness removes from the state before the driver runs: what
 * could reach the file system, the network, the process or stdin, and
 * binary chunks (crash-inventory item 14) and FFI (item 19). */
static const char SETUP[] =
    "local tricks = ...\n"
    "if io then debug.getregistry().__fuzz_stdin = io.stdin end\n"
    "if newproxy then __fuzz_newproxy = newproxy; newproxy = nil end\n"
    "local function strip(t, names) if type(t) == 'table' then for _, k in ipairs(names) do t[k] = nil end end end\n"
    "strip(os, {'exit', 'execute', 'remove', 'rename', 'tmpname', 'getenv', 'setlocale'})\n"
    "if io then\n"
    "  local mt = debug.getmetatable(io.stdin)\n"
    "  if type(mt) == 'table' and type(mt.__index) == 'table' then\n"
    "    strip(mt.__index, {'close', 'write', 'read', 'lines', 'seek', 'setvbuf', 'flush'})\n"
    "  end\n"
    "  strip(mt, {'__gc', '__close'})\n"
    "  io = nil; package.loaded.io = nil\n"
    "end\n"
    "strip(debug, {'debug'})\n"
    "strip(string, {'dump'})\n"
    "strip(_G, {'dofile', 'loadfile', 'print'})\n"
    "if package then\n"
    "  strip(package, {'loadlib'}); package.cpath = ''\n"
    "  strip(package.preload, {'socket', 'socket.core', 'mime', 'mime.core', 'ffi'})\n"
    "  if package.searchers then package.searchers[3] = nil; package.searchers[4] = nil end\n"
    "  if package.loaders then package.loaders[3] = nil; package.loaders[4] = nil end\n"
    "  strip(package.loaded, {'ffi', 'socket', 'mime', 'os'})\n"
    "end\n"
    "if jit then strip(jit, {'attach'}) end\n"
    "local ld = load\n"
    "load = function(s, name, mode, env) if type(s) ~= 'string' then return nil end return ld(s, name, 't', env) end\n"
    "loadstring = nil\n";

/* The natives the driver calls, as an array. */
static void push_natives(lua_State *L) {
    static const char *const names[] = {"genaccessor", "structclone", "sethook"};
    int n = 0, i, natives, t;
    lua_newtable(L);
    natives = lua_gettop(L);
    lua_getglobal(L, "xlua");
    t = lua_gettop(L);
    if (lua_istable(L, t)) {
        for (i = 0; i < 3; i++) {
            lua_getfield(L, t, names[i]);
            name_top(L, names[i]);
            lua_rawseti(L, natives, ++n);
        }
        /* accessors of every field type at a few offsets: getter and setter */
        for (i = 0; i < 10; i++) {
            lua_getfield(L, t, "genaccessor");
            lua_pushinteger(L, (i * 3) % 17);
            lua_pushinteger(L, i);
            lua_call(L, 2, 3);
            lua_pop(L, 1);
            name_top(L, "accessor setter");
            lua_rawseti(L, natives, ++n);
            name_top(L, "accessor getter");
            lua_rawseti(L, natives, ++n);
        }
    }
    lua_settop(L, natives);
    lua_getglobal(L, "uint64");
    t = lua_gettop(L);
    if (lua_istable(L, t)) {
        lua_pushnil(L);
        while (lua_next(L, t)) {
            if (lua_type(L, -2) == LUA_TSTRING) {
                name_top(L, lua_tostring(L, -2));
            }
            lua_rawseti(L, natives, ++n);
        }
    }
    lua_settop(L, natives);
#if LUA_VERSION_NUM == 501
    lua_rawgeti(L, LUA_REGISTRYINDEX, 8); /* the int64 metatable of i64lib.c */
    t = lua_gettop(L);
    if (lua_istable(L, t)) {
        lua_pushnil(L);
        while (lua_next(L, t)) {
            if (lua_iscfunction(L, -1)) {
                name_top(L, lua_tostring(L, -2));
                lua_rawseti(L, natives, ++n);
            } else {
                lua_pop(L, 1);
            }
        }
    }
    lua_settop(L, natives);
#endif
    lua_pushcfunction(L, errorfunc);
    name_top(L, "errorfunc");
    lua_rawseti(L, natives, ++n);
}

/* The harness's types: a struct metatable with the bridge tag, type id
 * STRUCT_TYPEID and generated __index/__newindex over accessors at offsets 0,
 * 4 and 8; a C# object metatable; a plain metatable; the userdata cache. */
static void make_types(lua_State *L) {
    int i;
    static const struct {
        const char *name;
        int offset, type;
    } fields[] = {{"x", 0, 8}, {"y", 4, 4}, {"z", 8, 9}, {"w", 16, 7}};
    lua_newtable(L); /* the struct metatable */
    lua_pushlightuserdata(L, xlua_tag());
    lua_pushboolean(L, 1);
    lua_rawset(L, -3);
    lua_pushinteger(L, STRUCT_TYPEID);
    lua_rawseti(L, -2, 1);
    lua_newtable(L); /* getters */
    lua_newtable(L); /* setters */
    for (i = 0; i < 4; i++) {
        lua_getglobal(L, "xlua");
        lua_getfield(L, -1, "genaccessor");
        lua_remove(L, -2);
        lua_pushinteger(L, fields[i].offset);
        lua_pushinteger(L, fields[i].type);
        lua_call(L, 2, 3);
        lua_pop(L, 1);
        lua_setfield(L, -3, fields[i].name); /* setter */
        lua_setfield(L, -3, fields[i].name); /* getter */
    }
    /* __index: obj_indexer(methods nil, getters, csindexer nil, base nil, indexfuncs {}, arrayindexer nil) */
    lua_pushnil(L);
    lua_pushvalue(L, -3);
    lua_pushnil(L);
    lua_pushnil(L);
    lua_newtable(L);
    lua_pushnil(L);
    gen_obj_indexer(L);
    lua_setfield(L, -4, "__index");
    /* __newindex: obj_newindexer(setters, csnewindexer nil, base nil, newindexfuncs {}, arrayindexer nil) */
    lua_pushvalue(L, -1);
    lua_pushnil(L);
    lua_pushnil(L);
    lua_newtable(L);
    lua_pushnil(L);
    gen_obj_newindexer(L);
    lua_setfield(L, -4, "__newindex");
    lua_pop(L, 2);
    struct_meta_ref = luaL_ref(L, LUA_REGISTRYINDEX);

    lua_newtable(L); /* the C# object metatable */
    lua_pushlightuserdata(L, xlua_tag());
    lua_pushboolean(L, 1);
    lua_rawset(L, -3);
    lua_pushinteger(L, CSOBJ_TYPEID);
    lua_rawseti(L, -2, 1);
    lua_newtable(L); /* methods: C# functions */
    for (i = 0; i < 10; i++) {
        xlua_push_csharp_function(L, fakes[i], 0);
        lua_rawseti(L, -2, i + 1);
    }
    xlua_push_csharp_function(L, fakes[0], 0);
    lua_setfield(L, -2, "Echo");
    xlua_push_csharp_function(L, fakes[3], 0);
    lua_setfield(L, -2, "Call");
    lua_pushnil(L);
    lua_pushnil(L);
    lua_pushnil(L);
    lua_newtable(L);
    lua_pushnil(L);
    gen_obj_indexer(L);
    lua_setfield(L, -2, "__index");
    xlua_push_csharp_function(L, fakes[0], 0);
    lua_setfield(L, -2, "__gc");
    xlua_push_csharp_function(L, fakes[0], 0);
    lua_setfield(L, -2, "__tostring");
    csobj_meta_ref = luaL_ref(L, LUA_REGISTRYINDEX);

    lua_newtable(L); /* the userdata cache, weak values as in ObjectTranslator */
    lua_newtable(L);
    lua_pushstring(L, "v");
    lua_setfield(L, -2, "__mode");
    lua_setmetatable(L, -2);
    cache_ref = luaL_ref(L, LUA_REGISTRYINDEX);

    error_func_ref = get_error_func_ref(L);
}

static void push_x(lua_State *L) {
    static const char *const readers[] = {"tocsobj_safe", "tocsobj_fast", "touint", "tointeger", "tointeger64",
                                          "isinteger_number", "number_kind", "gettypeid", "toint64", "touint64",
                                          "isint64", "isuint64", "running", "eqstr"};
    static const char *const tables[] = {"pget", "pset", "getpath", "setpath", "getglobal", "setglobal"};
    int i;
    lua_newtable(L);
    reg(L, "check", x_check, -1);
    if (trace > 1) {
        reg(L, "log", x_log, -1);
    }
    reg(L, "num", x_num, -1);
    reg(L, "lud", x_lud, -1);
    reg(L, "ud", x_ud, -1);
    reg(L, "foreign", x_foreign, -1);
    reg(L, "struct", x_struct, -1);
    reg(L, "typed", x_typed, -1);
    reg(L, "newstruct", x_newstruct, -1);
    reg(L, "csobj", x_csobj, -1);
    reg(L, "cstable", x_cstable, -1);
    reg(L, "csfunc", x_csfunc, -1);
    reg(L, "cswrapper", x_cswrapper, -1);
    reg(L, "indexer", x_indexer, -1);
    reg(L, "gc", x_gc, -1);
    /* the natives of X also go into the array of natives (below) */
    lua_newtable(L);
    {
        int n = 0;
#define NATIVE(name, f, up) do { reg(L, name, f, up); lua_getfield(L, -1, name); lua_rawseti(L, -3, ++n); } while (0)
        lua_insert(L, -2); /* list, X */
        for (i = 0; i < 14; i++) {
            NATIVE(readers[i], x_read, i);
        }
        for (i = 0; i < 6; i++) {
            NATIVE(tables[i], x_table, i);
        }
        NATIVE("cached", x_cached, -1);
        NATIVE("argerror", x_argerror, -1);
        NATIVE("pnext", x_pnext, -1);
        NATIVE("plen", x_plen, -1);
        NATIVE("setfenv", x_setfenv, -1);
        NATIVE("getloaders", x_getloaders, -1);
        NATIVE("rawgeti", x_rawi, 0);
        NATIVE("rawseti", x_rawi, 1);
        NATIVE("pcallref", x_pcallref, -1);
        NATIVE("unpack", x_copy, 0);
        NATIVE("pack", x_copy, 1);
        NATIVE("load", x_load, -1);
        NATIVE("newstruct2", x_newstruct, -1);
        NATIVE("csobj2", x_csobj, -1);
        NATIVE("cstable2", x_cstable, -1);
        NATIVE("gc2", x_gc, -1);
#if defined(FUZZ_SNAPSHOT)
        NATIVE("snapshot", x_snapshot, -1);
#endif
#undef NATIVE
        lua_insert(L, -2); /* X, list */
    }
}

static lua_State *new_bridge_state(unsigned flags) {
    lua_State *L = NULL;
#if defined(FUZZ_CORE_STATE)
    if (flags & 1) {
        xlua_core_config cfg;
        int32_t status;
        memset(&cfg, 0, sizeof(cfg));
        cfg.memory_limit = (int64_t)16 << 20;
#if LUA_VERSION_NUM >= 504
        cfg.has_seed = 1; /* LuaOptions.HashSeed: the same table order every run */
        cfg.seed = 20261028u;
#endif
        L = xlua_core_newstate(&cfg, &status);
        if (L == NULL) {
            abort();
        }
        luaopen_xlua(L);
        luaopen_i64lib(L);
        /* LuaOptions.MaxInstructions; Lua 5.3 has no limits and returns 0 */
        if (xlua_core_set_limits(L, 3000000, 0, XLUA_LIMITS_SMALL_SLICES) == 0) {
            lua_sethook(L, fuzz_count_hook, LUA_MASKCOUNT, FUZZ_HOOK_PERIOD);
        }
        fuzz_hook_calls = 0;
        return L;
    }
#endif
    (void)flags;
    L = fuzz_new_state();
    return L;
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    lua_State *L;
    unsigned flags;
    static int init;
    if (!init) {
        const char *t = getenv("FUZZ_BRIDGE_TRICKS");
        tricks = t != NULL && t[0] == '1';
        trace = getenv("FUZZ_BRIDGE_TRACE") != NULL ? atoi(getenv("FUZZ_BRIDGE_TRACE")) : 0;
        xlua_set_csharp_wrapper_caller(wrapper_caller);
        init = 1;
    }
    if (size == 0 || size > 8192) {
        return -1;
    }
    flags = data[0];
    unprot = 0;
    nheaders = 0;
    L = new_bridge_state(flags);
    if (flags & 2) {
        xlua_check_struct_accessors(L); /* the bridge checks of the Editor and Development builds */
    }
    if (luaL_loadstring(L, SETUP) != 0) {
        fail("harness", lua_tostring(L, -1));
    }
    lua_pushboolean(L, tricks);
    if (lua_pcall(L, 1, 0, 0) != 0) {
        fail("harness setup", lua_tostring(L, -1));
    }
    make_types(L);
    if (xluaL_loadbuffer(L, DRIVER, (int)sizeof(DRIVER) - 1, "=driver") != 0) {
        fail("driver", lua_tostring(L, -1));
    }
    lua_pushlstring(L, (const char *)data + 1, size - 1);
    push_natives(L);   /* driver, input, natives */
    push_x(L);         /* driver, input, natives, X, list */
    {
        /* append the list of X natives to the natives */
        int n = (int)lua_rawlen(L, -3), i, m = (int)lua_rawlen(L, -1);
        for (i = 1; i <= m; i++) {
            lua_rawgeti(L, -1, i);
            lua_rawseti(L, -4, n + i);
        }
        lua_pop(L, 1);
    }
    lua_pushboolean(L, tricks);
    lua_pushboolean(L, 0);
    /* the driver runs as LuaEnv.DoString runs a chunk: xlua.c's lua_pcall */
    if (lua_pcall(L, 5, 0, 0) != 0) {
        const char *msg = lua_tostring(L, -1);
        /* an error of the driver itself (a memory error or the instruction limit
         * can stop it anywhere) */
        if (unprot != 0 && (msg == NULL || strstr(msg, "not enough memory") == NULL)) {
            fail("a helper that C# calls unprotected raised an error", unprot_name);
        }
        if (trace) {
            fprintf(stderr, "fuzz_bridge: the driver stopped: %s\n", msg != NULL ? msg : "(not a string)");
        }
    } else if (trace) {
        fprintf(stderr, "fuzz_bridge: the driver finished\n");
    }
    lua_settop(L, 0);
#if defined(FUZZ_CORE_STATE)
    if (flags & 1) {
        xlua_core_close(L);
        return 0;
    }
#endif
    lua_close(L);
    return 0;
}

#else /* FUZZ_BRIDGE_CORE */
/* ------------------------------------------------------------------------
 * The core preset: LuaScript's sandbox, no bridge.
 * ------------------------------------------------------------------------ */
#include "xlua_core.h"

LUA_API lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status);
LUA_API int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
LUA_API int xlua_core_handle(lua_State *L, int32_t op, xlua_value *v);
LUA_API int xlua_core_call(lua_State *L, xlua_core_call_t *c);
LUA_API void xlua_core_set_dispatcher(xlua_core_dispatch fn);
LUA_API int xlua_core_register(lua_State *L, int32_t env, const char *name, int32_t name_len, int32_t fn_id, int32_t *old_id);
LUA_API int xlua_core_unref(lua_State *L, int32_t env);

static xlua_value own[16];
static char text[64] = "host text\0with a zero";
static unsigned dispatch_count;

static void expect_status(int st) {
    switch (st) {
    case XLUA_OK: case XLUA_ERRRUN: case XLUA_ERRMEM: case XLUA_ERRHANDLE: case XLUA_ERRTYPE: case XLUA_ERRCONFIG:
        break;
    default:
        fprintf(stderr, "fuzz_bridge: xlua_core_handle returned %d\n", st);
        abort();
    }
}

/* Handle operations on argument a of the running host function. */
static void handle_ops(xlua_core_frame *f, int a, unsigned salt) {
    xlua_value v[4];
    int32_t h;
    int st, k;
    memset(v, 0, sizeof(v));
    v[0].len = a; /* REF of the argument at that stack index (0, negative and too large included) */
    st = xlua_core_handle(f->L, XLUA_HANDLE_REF, v);
    expect_status(st);
    if (st != XLUA_OK) {
        return;
    }
    h = (int32_t)v[2].u.i;
    if (v[2].type != XLUA_T_TABLE && v[2].type != XLUA_T_FUNC && v[2].type != XLUA_T_BUFFER
        && v[2].type != XLUA_T_THREAD) {
        return;
    }
    for (k = 0; k < 4; k++) {
        int op = (int)((salt >> (k * 3)) % 12);
        memset(v, 0, sizeof(v));
        v[0].type = v[2].type;
        v[0].u.i = h;
        v[1] = f->nargs > 0 ? f->args[(salt + (unsigned)k) % (unsigned)f->nargs] : v[1];
        if (v[1].type > XLUA_T_STR) {
            v[1].type = XLUA_T_INT; /* arguments carry stack indices, not handles */
            v[1].u.i = (int64_t)salt - 7;
        }
        v[2].type = XLUA_T_INT;
        v[2].u.i = k;
        if (op == XLUA_HANDLE_NEWTABLE) {
            v[1].type = XLUA_T_INT;
            v[1].u.i = (int64_t)(salt % 50);
            v[1].len = (int32_t)(salt % 7);
        }
        if (op == XLUA_HANDLE_NEWBUFFER) {
            v[1].type = XLUA_T_INT;
            v[1].u.i = (salt & 1) ? -1 : (int64_t)(salt % 100);
        }
        if (op == XLUA_HANDLE_REF) {
            v[0].len = 0;
        }
        st = xlua_core_handle(f->L, op, v);
        expect_status(st);
        if (st == XLUA_OK && op == XLUA_HANDLE_BUFFER && v[2].u.s != NULL && v[2].len > 0) {
            ((char *)v[2].u.s)[v[2].len - 1] ^= 1; /* the host may write its bytes */
        }
        if (st == XLUA_OK && (op == XLUA_HANDLE_GET || op == XLUA_HANDLE_REF)
            && (v[2].type == XLUA_T_TABLE || v[2].type == XLUA_T_FUNC || v[2].type == XLUA_T_BUFFER
                || v[2].type == XLUA_T_THREAD) && v[2].u.i > 0) {
            if (xlua_core_unref(f->L, (int32_t)v[2].u.i) != 1) {
                abort();
            }
        }
        if (st == XLUA_OK && (op == XLUA_HANDLE_NEWTABLE || op == XLUA_HANDLE_NEWENV || op == XLUA_HANDLE_NEWBUFFER)
            && v[0].u.i > 0) {
            if (xlua_core_unref(f->L, (int32_t)v[0].u.i) != 1) {
                abort();
            }
        }
    }
    if (xlua_core_unref(f->L, h) != 1) {
        abort();
    }
}

/* The host functions: every frame status and result shape C# can produce. */
static void dispatch(void *host, int32_t id, xlua_core_frame *f) {
    unsigned salt = ++dispatch_count * 2654435761u;
    int i;
    (void)host;
    if (f->nargs > 0 && f->args[0].type == XLUA_T_INT) {
        salt ^= (unsigned)f->args[0].u.i;
    }
    switch (id) {
    case 1: /* echo primitive arguments */
        for (i = 0; i < f->nargs && i < f->results_cap; i++) {
            f->results[i] = f->args[i];
            if (f->results[i].type > XLUA_T_STR) {
                f->results[i].type = XLUA_T_NIL;
            }
        }
        f->nresults = i;
        break;
    case 2: f->status = XLUA_FRAME_ERROR_WHERE; f->error = "host error"; f->error_len = 10; break;
    case 3: f->status = XLUA_FRAME_ERROR; f->error = (salt & 1) ? NULL : text; f->error_len = (int32_t)(salt % 40); break;
    /* LuaCall.Report passes an argument number of 1 or more (LuaTypeException.Arg > 0),
     * also above nargs: LuaCall.GetLong(30) on a call with two arguments ("got no value") */
    case 4: f->status = XLUA_FRAME_ARGERROR; f->error_arg = 1 + (int32_t)(salt % 64); f->error = "bad"; break;
    case 5: f->status = XLUA_FRAME_TYPEERROR; f->error_arg = 1 + (int32_t)(salt % 64); f->error = (salt & 2) ? NULL : "number"; break;
    case 6: f->status = XLUA_FRAME_MEMERR; break;
    case 7: f->status = (int32_t)(salt % 20) - 5; break; /* undocumented statuses */
    case 8: f->nresults = (int32_t)(salt % 20) - 5; break;  /* result counts outside 0..cap */
    case 9: { /* results that are not valid handles */
        static const int32_t types[] = {XLUA_T_TABLE, XLUA_T_FUNC, XLUA_T_BUFFER, XLUA_T_THREAD, XLUA_T_STR, XLUA_T_OTHER, 99, -1};
        for (i = 0; i < 4; i++) {
            own[i].type = types[(salt >> (i * 3)) % 8];
            own[i].u.i = (int64_t)(salt % 9) - 3 + i * 1000;
            own[i].len = (int32_t)(salt % 5) - 1;
            if (own[i].type == XLUA_T_STR && own[i].len >= 0) {
                own[i].u.s = text;
            }
        }
        f->results = own;
        f->results_cap = 16;
        f->nresults = 4;
        break;
    }
    case 10: /* yield, also where it cannot */
        f->results[0].type = XLUA_T_INT;
        f->results[0].u.i = f->nargs;
        f->nresults = 1;
        f->status = (salt & 1) ? XLUA_FRAME_YIELD : (f->yieldable ? XLUA_FRAME_YIELD : XLUA_FRAME_RETURN);
        break;
    case 11: /* handle operations on the arguments */
        for (i = 0; i < 3; i++) {
            handle_ops(f, (int)(salt % 7) - 2 + i, salt + (unsigned)i);
        }
        break;
    default: /* strings with zeros and lengths from the host's memory */
        for (i = 0; i < 16; i++) {
            own[i].type = XLUA_T_STR;
            own[i].u.s = text;
            own[i].len = (int32_t)((salt + (unsigned)i) % 60);
        }
        f->results = own;
        f->results_cap = 16;
        f->nresults = 16;
        break;
    }
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    static const char *const names[] = {"h.echo", "h.where", "h.error", "h.argerror", "h.typeerror", "h.memerr",
                                        "h.status", "h.count", "h.handles", "h.yield", "h.ops", "h.strings"};
    static const char natives[] =
        "N = {}\n"
        "for _, lib in ipairs({h, buffer, coroutine}) do\n"
        "  if type(lib) == 'table' then for _, f in pairs(lib) do N[#N + 1] = f end end\n"
        "end\n"
        "table.sort(N, function(a, b) return tostring(a) < tostring(b) end)\n";
    xlua_core_config cfg;
    xlua_core_call_t c;
    xlua_value args[5], results[4];
    int32_t status, old;
    lua_State *L;
    int i;
    if (size == 0 || size > 8192) {
        return -1;
    }
    memset(&cfg, 0, sizeof(cfg));
    cfg.memory_limit = (int64_t)4 << 20;
    cfg.sandbox = 1;
    cfg.libs = XLUA_LUALIBS_SAFE | XLUA_LUALIBS_BUFFER;
    L = xlua_core_open(&cfg, NULL, &status);
    if (L == NULL) {
        abort();
    }
    xlua_core_set_dispatcher(dispatch);
    for (i = 0; i < 12; i++) {
        if (xlua_core_register(L, 0, names[i], (int32_t)strlen(names[i]), i + 1, &old) != XLUA_OK) {
            abort();
        }
    }
    xlua_core_set_limits(L, 3000000, 0, (data[0] & 1) ? XLUA_LIMITS_SMALL_SLICES : 0);
    memset(&c, 0, sizeof(c));
    c.kind = XLUA_CALL_CHUNK;
    c.code = natives;
    c.code_len = (int32_t)sizeof(natives) - 1;
    c.name = "natives";
    c.name_len = 7;
    if (xlua_core_call(L, &c) != XLUA_OK) {
        abort();
    }
    memset(&c, 0, sizeof(c));
    memset(args, 0, sizeof(args));
    args[0].type = XLUA_T_STR;
    args[0].u.s = (const char *)data + 1;
    args[0].len = (int32_t)(size - 1);
    c.kind = XLUA_CALL_CHUNK;
    c.code = "local input = ...\nlocal X = {}\nreturn (load(DRIVER_SRC, '=driver'))(input, N, X, false, true)\n";
    c.code_len = (int32_t)strlen(c.code);
    c.name = "fuzz";
    c.name_len = 4;
    {
        /* the driver's text as a global (an argument string would be anchored the same way) */
        xlua_value v[4];
        memset(v, 0, sizeof(v));
        v[1].type = XLUA_T_STR;
        v[1].u.s = "DRIVER_SRC";
        v[1].len = 10;
        v[2].type = XLUA_T_STR;
        v[2].u.s = DRIVER;
        v[2].len = (int32_t)sizeof(DRIVER) - 1;
        v[0].type = XLUA_T_TABLE;
        v[0].u.i = 0; /* the default environment */
        if (xlua_core_handle(L, XLUA_HANDLE_SET, v) != XLUA_OK) {
            abort();
        }
    }
    c.args = args;
    c.nargs = 1;
    c.results = results;
    c.results_cap = 4;
    status = xlua_core_call(L, &c);
    switch (status) {
    case XLUA_OK: case XLUA_ERRRUN: case XLUA_ERRMEM: case XLUA_ERRERR: case XLUA_LIMIT_INSTR:
        break;
    default:
        fprintf(stderr, "fuzz_bridge: xlua_core_call returned %d: %.*s\n", status, c.error_len, c.error ? c.error : "");
        abort();
    }
    xlua_core_close(L);
    return 0;
}
#endif
