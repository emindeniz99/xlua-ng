/* Times one libxlua build: pure Lua workloads, the C API paths the C# bridge uses,
 * compiling and state creation. Test/Native/flag_sweep.py builds it once per ABI and
 * runs it against every compiler and linker variant of libxlua (on an Android device
 * or the host), so the same runner measures each shared library as it would ship.
 *
 * The library is opened with dlopen and only the functions LuaDLL.cs imports are
 * looked up, so a library that exports nothing else (a linker version script) runs too.
 * Lua 5.3 or later.
 *
 * Usage: flag_bench <libxlua.so> [--runs N] [--warmup N] [--scale X] [--filter TEXT]
 *                   [--cpus 4,5,6,7] [--core] [--instruction-limit N] [--time-limit-ns N]
 *                   [--smoke] [--list]
 * Output: one line per case, XLUA_FLAG_BENCH {"name": ..., "median_ns": ...}, then
 * XLUA_FLAG_BENCH_DONE. --smoke runs a short correctness script instead and prints
 * XLUA_FLAG_SMOKE_OK. "check" is the case's result; it must be the same for every
 * variant of one Lua version. On Linux "cpus" lists the CPUs the process was on at the
 * end of each timed run (sched_getcpu), and XLUA_FLAG_AFFINITY the allowed CPUs.
 * --core creates every state with xlua_core_newstate, as LuaEnv(LuaOptions) does;
 * --instruction-limit and --time-limit-ns (which imply --core) also set the limits of
 * LuaOptions.InstructionLimit and TimeLimit with xlua_core_set_limits (Lua 5.4 and 5.5,
 * a library with the core limits). Choose limits that no case reaches.
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <sched.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

typedef struct lua_State lua_State;
typedef double lua_Number;
typedef int (*lua_CFunction)(lua_State *L);

/* LUA_REGISTRYINDEX differs between Lua versions; the library reports its own. */
static int registry_index;

/* Functions resolved from the library; all of them are in LuaDLL.cs. */
static lua_State *(*luaL_newstate)(void);
static void (*luaL_openlibs)(lua_State *L);
static void (*luaopen_xlua)(lua_State *L);
static int (*luaopen_i64lib)(lua_State *L);
static void (*lua_close)(lua_State *L);
static int (*xluaL_loadbuffer)(lua_State *L, const char *buff, int size, const char *name);
static int (*lua_pcall)(lua_State *L, int nargs, int nresults, int errfunc);
static const char *(*lua_tolstring)(lua_State *L, int idx, size_t *len);
static void (*lua_settop)(lua_State *L, int idx);
static int (*lua_gettop)(lua_State *L);
static void (*lua_pushvalue)(lua_State *L, int idx);
static void (*lua_pushnumber)(lua_State *L, lua_Number n);
static lua_Number (*lua_tonumber)(lua_State *L, int idx);
static void (*xlua_pushinteger)(lua_State *L, int n);
static int (*xlua_tointeger)(lua_State *L, int idx);
static void (*lua_pushcclosure)(lua_State *L, lua_CFunction fn, int n);
static int (*xlua_setglobal)(lua_State *L, const char *name);
static void (*xlua_rawgeti)(lua_State *L, int idx, int64_t n);
static void (*lua_pushstring)(lua_State *L, const char *s);
static int (*xlua_pgettable)(lua_State *L, int idx);
static int (*luaL_newmetatable)(lua_State *L, const char *name);
static int (*luaL_ref)(lua_State *L, int t);
static void (*xlua_pushcsobj)(lua_State *L, int key, int meta_ref, int need_cache, int cache_ref);
static int (*xlua_tocsobj_fast)(lua_State *L, int index);
static void (*lua_createtable)(lua_State *L, int narr, int nrec);
static int (*xlua_get_lib_version)(void);
static int (*xlua_get_registry_index)(void);
/* Only with --core: xlua_core.h's config struct and the core exports. */
typedef struct {
    int64_t memory_limit;
    uint32_t seed;
    int32_t has_seed, libs, sandbox;
} core_config;
static lua_State *(*xlua_core_newstate)(const core_config *cfg, int32_t *status);
static void (*xlua_core_close)(lua_State *L);
static int (*xlua_core_set_limits)(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
static int (*xlua_core_get_stats)(lua_State *L, int64_t *out);  /* xlua_core_stats: ten int64_t */
static int core_mode;
static long long instruction_limit, time_limit_ns;

static void *lib;

static void *sym(const char *name) {
    void *p = dlsym(lib, name);
    if (!p) {
        fprintf(stderr, "missing export %s\n", name);
        exit(2);
    }
    return p;
}

static void load(const char *path) {
    lib = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!lib) {
        fprintf(stderr, "dlopen %s: %s\n", path, dlerror());
        exit(2);
    }
#define S(name) *(void **)&name = sym(#name)
    S(luaL_newstate); S(luaL_openlibs); S(luaopen_xlua); S(luaopen_i64lib); S(lua_close);
    S(xluaL_loadbuffer); S(lua_pcall); S(lua_tolstring); S(lua_settop); S(lua_gettop);
    S(lua_pushvalue); S(lua_pushnumber); S(lua_tonumber); S(xlua_pushinteger); S(xlua_tointeger);
    S(lua_pushcclosure); S(xlua_setglobal); S(xlua_rawgeti); S(lua_pushstring); S(xlua_pgettable);
    S(luaL_newmetatable); S(luaL_ref); S(xlua_pushcsobj); S(xlua_tocsobj_fast); S(lua_createtable);
    S(xlua_get_lib_version); S(xlua_get_registry_index);
    if (core_mode) {
        S(xlua_core_newstate); S(xlua_core_close);
        if (instruction_limit || time_limit_ns) { S(xlua_core_set_limits); S(xlua_core_get_stats); }
    }
#undef S
    registry_index = xlua_get_registry_index();
}

static uint64_t now_ns(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000000000u + (uint64_t)ts.tv_nsec;
}

static lua_State *new_env(void) {
    lua_State *L;
    if (core_mode) {
        core_config cfg = { 0, 0, 0, 0, 0 };
        int32_t status = 0;
        L = xlua_core_newstate(&cfg, &status);
        if (!L) {
            fprintf(stderr, "xlua_core_newstate failed: %d\n", (int)status);
            exit(2);
        }
    } else {
        L = luaL_newstate();
    }
    luaL_openlibs(L);
    luaopen_xlua(L);
    luaopen_i64lib(L);
    if ((instruction_limit || time_limit_ns)
        && !xlua_core_set_limits(L, (int64_t)instruction_limit, (int64_t)time_limit_ns, 0)) {
        fprintf(stderr, "xlua_core_set_limits: this library has no limits\n");
        exit(2);
    }
    return L;
}

static void close_env(lua_State *L) {
    if (core_mode) xlua_core_close(L);
    else lua_close(L);
}

static void fail(lua_State *L, const char *what) {
    const char *msg = lua_tolstring(L, -1, NULL);
    fprintf(stderr, "%s: %s\n", what, msg ? msg : "(no message)");
    exit(1);
}

static void run_chunk(lua_State *L, const char *src, int nresults) {
    if (xluaL_loadbuffer(L, src, (int)strlen(src), "=bench") != 0 || lua_pcall(L, 0, nresults, 0) != 0)
        fail(L, "chunk");
}

/* ---------------------------------------------------------------- cases */

typedef struct Case Case;
struct Case {
    const char *name;
    long iters;
    const char *lua;           /* chunk returning function(n), or NULL for a C case */
    void (*c)(lua_State *L, long n, char *check, size_t cap); /* C case body */
};

/* Lua calls a C function with two numbers, like a static C# method through a wrapper. */
static int c_add(lua_State *L) {
    lua_pushnumber(L, lua_tonumber(L, 1) + lua_tonumber(L, 2));
    return 1;
}

static void case_lua_calls_c(lua_State *L, long n, char *check, size_t cap) {
    lua_pushcclosure(L, c_add, 0);
    xlua_setglobal(L, "c_add");
    run_chunk(L, "local f = c_add return function(n) local s = 0 for i = 1, n do s = f(s, 1) end return s end", 1);
    xlua_pushinteger(L, (int)n);
    if (lua_pcall(L, 1, 1, 0) != 0) fail(L, "lua_calls_c");
    snprintf(check, cap, "%.0f", lua_tonumber(L, -1));
    lua_settop(L, 0);
}

/* C calls a Lua function, like a C# delegate bound to Lua (delegate_int). */
static void case_c_calls_lua(lua_State *L, long n, char *check, size_t cap) {
    run_chunk(L, "return function(a, b) return a + b end", 1);
    long sum = 0;
    for (long i = 0; i < n; i++) {
        lua_pushvalue(L, 1);
        xlua_pushinteger(L, (int)(i & 0xffff));
        xlua_pushinteger(L, 1);
        if (lua_pcall(L, 2, 1, 0) != 0) fail(L, "c_calls_lua");
        sum += xlua_tointeger(L, -1);
        lua_settop(L, 1);
    }
    snprintf(check, cap, "%ld", sum);
    lua_settop(L, 0);
}

/* LuaTable.Get<int, int> from C#: xlua_rawgeti on an array table. */
static void case_table_rawgeti(lua_State *L, long n, char *check, size_t cap) {
    run_chunk(L, "local t = {} for i = 1, 64 do t[i] = i end return t", 1);
    long sum = 0;
    for (long i = 0; i < n; i++) {
        xlua_rawgeti(L, 1, (i & 63) + 1);
        sum += xlua_tointeger(L, -1);
        lua_settop(L, 1);
    }
    snprintf(check, cap, "%ld", sum);
    lua_settop(L, 0);
}

/* LuaTable.Get<int>("value") from C#: xlua_pgettable, a protected lua_gettable. */
static void case_table_pgettable(lua_State *L, long n, char *check, size_t cap) {
    run_chunk(L, "return { value = 7 }", 1);
    long sum = 0;
    for (long i = 0; i < n; i++) {
        lua_pushstring(L, "value");
        if (xlua_pgettable(L, 1) != 0) fail(L, "pgettable");
        sum += xlua_tointeger(L, -1);
        lua_settop(L, 1);
    }
    snprintf(check, cap, "%ld", sum);
    lua_settop(L, 0);
}

/* ObjectTranslator.Push of a C# object: a new int userdata with a metatable. */
static void case_push_csobj(lua_State *L, long n, char *check, size_t cap) {
    luaL_newmetatable(L, "flag_bench_meta");
    int meta_ref = luaL_ref(L, registry_index);
    long sum = 0;
    for (long i = 0; i < n; i++) {
        xlua_pushcsobj(L, (int)(i & 1023), meta_ref, 0, 0);
        sum += xlua_tocsobj_fast(L, -1);
        lua_settop(L, 0);
    }
    snprintf(check, cap, "%ld", sum);
}

/* Compiling Lua source: lexer, parser and code generator. */
static char *compile_src;
static int compile_len;

static void make_compile_src(void) {
    size_t cap = 1 << 16, len = 0;
    compile_src = malloc(cap);
    len += (size_t)snprintf(compile_src, cap, "local M = {}\n");
    for (int i = 0; len + 512 < cap; i++)
        len += (size_t)snprintf(compile_src + len, cap - len,
            "function M.f%d(a, b, ...)\n"
            "  local t = { x = a, y = b, %d, 'str%d', n = select('#', ...) }\n"
            "  for i = 1, #t do if t[i] ~= nil and i %% 2 == 0 then t.x = t.x + i * %d.5 end end\n"
            "  while a < b do a = a + 1 if a > %d then break end end\n"
            "  return t.x .. ':' .. tostring(t.y), function(z) return z + a end\n"
            "end\n", i, i, i, i, i * 7);
    len += (size_t)snprintf(compile_src + len, cap - len, "return M\n");
    compile_len = (int)len;
}

static void case_compile(lua_State *L, long n, char *check, size_t cap) {
    int top = 0;
    for (long i = 0; i < n; i++) {
        if (xluaL_loadbuffer(L, compile_src, compile_len, "=compile") != 0) fail(L, "compile");
        top += lua_gettop(L);
        lua_settop(L, 0);
    }
    snprintf(check, cap, "%d:%d", compile_len, top);
}

/* new LuaEnv() without the managed half: state, standard libraries, xlua and uint64. */
static void case_env_create(lua_State *L, long n, char *check, size_t cap) {
    (void)L;
    for (long i = 0; i < n; i++) close_env(new_env());
    snprintf(check, cap, "%ld", n);
}

#define K 1000L
static const Case cases[] = {
    /* The pure Lua cases of Test/Native/Benchmark.cs, same sources and counts. */
    { "empty_loop", 4000 * K, "return function(n) for i = 1, n do end end", NULL },
    { "function_call", 1000 * K, "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", NULL },
    { "method_call", 1000 * K, "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\n"
      "local o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", NULL },
    { "table_array_insert", 1000 * K, "return function(n) local t = {} for i = 1, n do t[i] = i end return #t end", NULL },
    { "table_array_read", 1000 * K, "local t = {} for i = 1, 1024 do t[i] = i end\n"
      "return function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", NULL },
    { "table_hash_set_get", 1000 * K, "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\n"
      "return function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", NULL },
    { "string_concat", 500 * K, "return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = 'item' .. i j = j + 1 if j > 64 then j = 1 end end return buf[64] end", NULL },
    { "string_format", 500 * K, "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", NULL },
    { "closure_create_call", 1000 * K, "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", NULL },
    { "int_math", 2000 * K, "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", NULL },
    { "int64_math", 500 * K, "local seed, m, c = 0x0123456789ABCDEF, 6364136223846793005, 1442695040888963407\n"
      "return function(n) local x = seed for i = 1, n do x = x * m + c end return x end", NULL },
    /* Wider coverage: recursion, floats, the string and table libraries, coroutines, GC. */
    { "fib_recursive", 20 * K, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\n"
      "return function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", NULL },
    { "float_math", 2000 * K, "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", NULL },
    { "string_pattern", 100 * K, "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\n"
      "return function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end", NULL },
    { "table_sort", 2 * K, "local src, x = {}, 12345 for i = 1, 256 do x = (x * 1103515245 + 12345) % 2147483648 src[i] = x end\n"
      "return function(n) local s = 0 for i = 1, n do local t = table.move(src, 1, 256, 1, {}) table.sort(t) s = s + t[1] % 1000 end return s end", NULL },
    { "table_concat", 50 * K, "local parts = {} for i = 1, 32 do parts[i] = tostring(i) end\n"
      "return function(n) local s = 0 for i = 1, n do s = s + #table.concat(parts, ',') end return s end", NULL },
    { "coroutine_switch", 500 * K, "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\n"
      "local s = 0 for i = 1, n do s = s + co() end return s end", NULL },
    { "gc_small_tables", 500 * K, "return function(n) local keep, s = {}, 0 for i = 1, n do local t = { i, i + 1, x = i } keep[i % 128 + 1] = t s = s + t[2] end return s end", NULL },
    { "oop_inherit", 1000 * K, "local Base = {} Base.__index = Base function Base:get() return self.v end\n"
      "local Derived = setmetatable({}, Base) Derived.__index = Derived\n"
      "local o = setmetatable({ v = 3 }, Derived)\nreturn function(n) local s = 0 for i = 1, n do s = s + o:get() end return s end", NULL },
    /* The C API paths of the C# bridge. */
    { "c_lua_calls_c", 1000 * K, NULL, case_lua_calls_c },
    { "c_c_calls_lua", 1000 * K, NULL, case_c_calls_lua },
    { "c_table_rawgeti", 2000 * K, NULL, case_table_rawgeti },
    { "c_table_pgettable", 1000 * K, NULL, case_table_pgettable },
    { "c_push_csobj", 1000 * K, NULL, case_push_csobj },
    { "c_compile", 20, NULL, case_compile },
    { "c_env_create", 200, NULL, case_env_create },
};
#define NCASES (sizeof(cases) / sizeof(cases[0]))

/* One timed run of a case in a fresh state; returns ns for n operations. */
/* CPUs seen at the end of the timed runs of the current case, as a bit mask. */
static unsigned long long cpus_seen;

static void note_cpu(void) {
#ifdef __linux__
    int cpu = sched_getcpu();
    if (cpu >= 0 && cpu < 64) cpus_seen |= 1ull << cpu;
#endif
}

static void cpu_list(unsigned long long mask, char *buf, size_t cap) {
    size_t len = 0;
    buf[0] = 0;
    for (int i = 0; i < 64 && len + 4 < cap; i++)
        if (mask & (1ull << i)) len += (size_t)snprintf(buf + len, cap - len, "%s%d", len ? "," : "", i);
}

static uint64_t run_case(const Case *c, long n, char *check, size_t cap) {
    lua_State *L = new_env();
    uint64_t t0, t1;
    if (c->lua) {
        run_chunk(L, c->lua, 1);
        lua_pushvalue(L, 1);
        xlua_pushinteger(L, (int)n);
        t0 = now_ns();
        if (lua_pcall(L, 1, 1, 0) != 0) fail(L, c->name);
        t1 = now_ns();
        size_t len = 0;
        const char *s = lua_tolstring(L, -1, &len);
        snprintf(check, cap, "%s", s ? s : "nil");
    } else {
        t0 = now_ns();
        c->c(L, n, check, cap);
        t1 = now_ns();
    }
    note_cpu();
    close_env(L);
    return t1 - t0;
}

static int cmp_u64(const void *a, const void *b) {
    uint64_t x = *(const uint64_t *)a, y = *(const uint64_t *)b;
    return x < y ? -1 : x > y;
}

static const char smoke[] =
    "assert(_VERSION:match('^Lua 5%.[345]$'), _VERSION)\n"
    "assert(os.execute == nil)\n"
    "assert(string.format('%5.2f', math.pi) == ' 3.14')\n"
    "assert(uint64.tostring(uint64.parse('18446744073709551615')) == '18446744073709551615')\n"
    "assert(math.type(1) == 'integer' and 7 // 2 == 3 and 7 % -3 == -2 and 2^53 == 9007199254740992.0)\n"
    "assert(0x7fffffffffffffff + 1 == math.mininteger)\n"
    "assert(('%d %s %q'):format(42, 'x', 'a\\nb') == '42 x \"a\\\\\\nb\"')\n"
    "assert(('hello world'):gsub('o', '0') == 'hell0 w0rld')\n"
    "assert(select('#', string.byte('abc', 1, -1)) == 3)\n"
    "local t = {} for i = 1, 1000 do t[i] = (i * 7919) % 1000 end table.sort(t) for i = 2, 1000 do assert(t[i - 1] <= t[i]) end\n"
    "assert(tostring(1e15) == '1e+15' and tostring(0.1) == '0.1' and tonumber('0x10') == 16 and tonumber('  12  ') == 12)\n"
    "local co = coroutine.wrap(function(a) local b = coroutine.yield(a + 1) return b * 2 end)\n"
    "assert(co(1) == 2 and co(5) == 10)\n"
    "local ok, err = pcall(error, { code = 1 }) assert(not ok and err.code == 1)\n"
    "local ok2, err2 = pcall(function() local x = nil; return x.y end) assert(not ok2 and err2:find('nil'))\n"
    "local mt = { __index = function(_, k) return k .. '!' end, __add = function(a, b) return 42 end }\n"
    "local o = setmetatable({}, mt) assert(o.hi == 'hi!' and o + 1 == 42)\n"
    "local f = load(string.dump(function(x) return x * 3 end)) assert(f(5) == 15)\n"
    "assert(utf8.len('\\u{48}\\u{20AC}') == 2 and utf8.char(0x20AC) == '\\u{20AC}')\n"
    /* os.tmpname needs /tmp, which Android does not have; write next to the runner. */
    "local name = 'flag_bench_smoke.tmp'\n"
    "local fh = assert(io.open(name, 'w+b'))\n"
    "assert(fh:write(string.rep('x', 1000)))\n"
    "assert(fh:seek('set', 10) == 10 and fh:read(3) == 'xxx')\n"
    "fh:close() assert(os.remove(name))\n"
    "local p = io.popen('echo xlua') assert(p:read('l') == 'xlua') p:close()\n"
    "local w = setmetatable({}, { __mode = 'v' }) w[1] = {} collectgarbage() assert(w[1] == nil)\n"
    "local big = {} for i = 1, 100000 do big[i] = tostring(i) end collectgarbage()\n"
    "assert(#big == 100000 and big[100000] == '100000')\n"
    "local s = 0 for k, v in pairs({ a = 1, b = 2, c = 3 }) do s = s + v end assert(s == 6)\n"
    "assert(string.pack('<i4', 1) == '\\1\\0\\0\\0' and string.unpack('<i4', '\\2\\0\\0\\0') == 2)\n"
    "return 'ok'\n";

int main(int argc, char **argv) {
    if (argc < 2) {
        fprintf(stderr, "usage: %s <libxlua> [--runs N] [--warmup N] [--scale X] [--filter TEXT] [--cpus LIST] [--core] [--instruction-limit N] [--time-limit-ns N] [--smoke] [--list]\n", argv[0]);
        return 2;
    }
    int runs = 5, warmup = 1, do_smoke = 0, list = 0;
    const char *cpus = NULL;
    double scale = 1.0;
    const char *filter = NULL;
    for (int i = 2; i < argc; i++) {
        if (!strcmp(argv[i], "--runs") && i + 1 < argc) runs = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--warmup") && i + 1 < argc) warmup = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--scale") && i + 1 < argc) scale = atof(argv[++i]);
        else if (!strcmp(argv[i], "--filter") && i + 1 < argc) filter = argv[++i];
        else if (!strcmp(argv[i], "--cpus") && i + 1 < argc) cpus = argv[++i];
        else if (!strcmp(argv[i], "--core")) core_mode = 1;
        else if (!strcmp(argv[i], "--instruction-limit") && i + 1 < argc) instruction_limit = atoll(argv[++i]), core_mode = 1;
        else if (!strcmp(argv[i], "--time-limit-ns") && i + 1 < argc) time_limit_ns = atoll(argv[++i]), core_mode = 1;
        else if (!strcmp(argv[i], "--smoke")) do_smoke = 1;
        else if (!strcmp(argv[i], "--list")) list = 1;
        else { fprintf(stderr, "unknown argument %s\n", argv[i]); return 2; }
    }
    if (list) {
        for (size_t i = 0; i < NCASES; i++) printf("%s\n", cases[i].name);
        return 0;
    }
#ifdef __linux__
    /* Pin to a set of cores (one cluster): a single core can be paused by the kernel. */
    if (cpus) {
        cpu_set_t set;
        CPU_ZERO(&set);
        for (const char *p = cpus; *p; p++) {
            if (*p < '0' || *p > '9') continue;
            CPU_SET(atoi(p), &set);
            p += strspn(p, "0123456789") - 1;
        }
        if (sched_setaffinity(0, sizeof(set), &set) != 0) perror("sched_setaffinity");
    }
    {
        cpu_set_t now;
        unsigned long long mask = 0;
        char buf[256];
        if (sched_getaffinity(0, sizeof(now), &now) == 0)
            for (int i = 0; i < 64; i++)
                if (CPU_ISSET(i, &now)) mask |= 1ull << i;
        cpu_list(mask, buf, sizeof(buf));
        printf("XLUA_FLAG_AFFINITY %s\n", buf);
    }
#else
    (void)cpus;
#endif
    load(argv[1]);
    if (do_smoke) {
        lua_State *L = new_env();
        run_chunk(L, smoke, 1);
        char limits[128] = "";
        if (instruction_limit || time_limit_ns) {
            /* The smoke chunk ran as one limited call: its instruction count shows the limit was armed. */
            int64_t st[10] = { 0 };
            xlua_core_get_stats(L, st);
            snprintf(limits, sizeof(limits), " instruction_limit=%lld time_limit_ns=%lld instructions=%lld",
                     (long long)st[8], (long long)st[9], (long long)st[6]);
        }
        printf("XLUA_FLAG_SMOKE_OK %s lib_version=%d%s%s\n", lua_tolstring(L, -1, NULL), xlua_get_lib_version(),
               core_mode ? " core" : "", limits);
        close_env(L);
        return 0;
    }
    make_compile_src();
    if (runs < 1) runs = 1;
    uint64_t *times = calloc((size_t)runs, sizeof(uint64_t));
    for (size_t i = 0; i < NCASES; i++) {
        const Case *c = &cases[i];
        if (filter && !strstr(c->name, filter)) continue;
        long n = (long)(c->iters * scale);
        if (n < 1) n = 1;
        char check[128] = "";
        for (int w = 0; w < warmup; w++) run_case(c, n, check, sizeof(check));
        cpus_seen = 0;
        for (int r = 0; r < runs; r++) times[r] = run_case(c, n, check, sizeof(check));
        char seen[256];
        cpu_list(cpus_seen, seen, sizeof(seen));
        qsort(times, (size_t)runs, sizeof(uint64_t), cmp_u64);
        double med = runs % 2 ? (double)times[runs / 2] : ((double)times[runs / 2 - 1] + (double)times[runs / 2]) / 2;
        printf("XLUA_FLAG_BENCH {\"name\": \"%s\", \"iters\": %ld, \"median_ns\": %.3f, \"min_ns\": %.3f, \"max_ns\": %.3f, \"check\": \"%s\", \"cpus\": \"%s\"}\n",
               c->name, n, med / n, (double)times[0] / n, (double)times[runs - 1] / n, check, seen);
        fflush(stdout);
    }
    printf("XLUA_FLAG_BENCH_DONE\n");
    free(times);
    return 0;
}
