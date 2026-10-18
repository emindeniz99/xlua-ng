/* Drives the core library (XLUA_PRESET=core, or a bridge library built with
 * XLUA_LUASCRIPT=ON) through the xlua_core_* exports only, without Mono or
 * Python: LuaScript's sandbox (docs/design/core-luascript.md, section 4).
 *
 *  - every case of Test/Native/core_sandbox_cases.txt (the sandbox rules),
 *    against a state with the safe libraries and a 16 MB memory limit;
 *  - the edges of the ABI: xlua_core_open's configuration and memory errors,
 *    library subsets, the hash seed, environment handles, require without a
 *    resolver, and the memory overdraft rule around the resolver;
 *  - xlua_core_call: functions by name, typed arguments and results,
 *    multiple results, errors and tracebacks, the anchoring of string
 *    results and the instruction count.
 *
 * Exits 0 and prints XLUA_CORE_SMOKE_PASS when every check passes. Usable
 * where only a cross-compiler is available, like plugin_smoke.c.
 * Build: cc core_smoke.c -L<dir> -lxlua -o core_smoke
 * Run:   ./core_smoke Test/Native/core_sandbox_cases.txt
 */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct lua_State lua_State;

/* Mirrors of build/xlua_core.h. */
typedef struct xlua_core_config {
    int64_t memory_limit;
    uint32_t seed;
    int32_t has_seed;
    int32_t libs;
    int32_t sandbox;
} xlua_core_config;

typedef struct xlua_core_stats {
    int64_t memory_used, memory_peak, memory_limit, refusals, overdrafts, host_oom;
    int64_t instructions, stop_reason, instruction_limit, time_limit_ns;
} xlua_core_stats;

typedef struct xlua_core_source {
    const char *code;
    int32_t code_len;
    const char *chunkname;
    int32_t chunkname_len;
} xlua_core_source;

typedef int32_t (*xlua_core_resolver)(void *host, const char *name, int32_t name_len, xlua_core_source *out);

typedef struct xlua_value {
    int32_t type;
    int32_t len;
    union {
        int64_t i;
        double n;
        const char *s;
    } u;
} xlua_value;

typedef struct xlua_core_call_t {
    int64_t instructions;
    const char *name;
    const char *code;
    const xlua_value *args;
    xlua_value *results;
    const char *error;
    const char *traceback;
    int32_t kind, env, fn, name_len, code_len, nargs, results_cap, nresults;
    int32_t error_len, traceback_len, error_ref, flags;
} xlua_core_call_t;

enum { OK = 0, ERRRUN = 2, ERRSYNTAX = 3, ERRMEM = 4, LIMIT_INSTR = 10, LIMIT_TIME = 11, INTERRUPTED = 12,
       ERRTYPE = 20, ERRSTACK = 21, ERRHANDLE = 22, ERRCONFIG = 23 };
enum { T_NIL, T_BOOL, T_INT, T_NUM, T_STR, T_TABLE, T_FUNC, T_BUFFER, T_OTHER };
enum { CALL_GLOBAL = 0, CALL_REF = 1, CALL_CHUNK = 2, CALL_COMPILE = 3 };
enum { LIBS_COROUTINE = 1, LIBS_STRING = 2, LIBS_TABLE = 4, LIBS_MATH = 8, LIBS_UTF8 = 16, LIBS_BUFFER = 32,
       LIBS_IO = 64, LIBS_PACKAGE = 512 };
#define SMALL_SLICES 2

int xlua_core_version(void);
lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status);
void xlua_core_close(lua_State *L);
int xlua_core_set_memory_limit(lua_State *L, int64_t bytes);
int xlua_core_get_stats(lua_State *L, xlua_core_stats *out);
int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
int xlua_core_newenv(lua_State *L, int32_t *env);
int xlua_core_unref(lua_State *L, int32_t env);
int xlua_core_call(lua_State *L, xlua_core_call_t *c);
void xlua_core_set_resolver(xlua_core_resolver fn);
int xlua_core_set_require(lua_State *L, int32_t enabled);
int xlua_core_interrupt(lua_State *L);
int64_t xlua_core_watch(lua_State *L);
int xlua_gc(lua_State *L, int what, int data);
int lua_checkstack(lua_State *L, int n);
int lua_gettop(lua_State *L);

static int failures = 0;

#define CHECK(cond) do { if (!(cond)) { fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #cond); failures++; } } while (0)

/* The first bytes of a Lua 5.4 binary chunk ("@BINARY@" in the cases). */
static const char binary_chunk[] = "\x1bLua\x54\x00\x19\x93\r\n\x1a\n";

/* ---- the case file ---- */

#define MAX_ITEMS 256

typedef struct item {
    char name[160];
    char env[32];
    char expect[160];
    int status;
    long long instructions;
    int require_on;
    const char *code;
    size_t code_len;
} item;

static item cases[MAX_ITEMS], modules[MAX_ITEMS];
static int ncases = 0, nmodules = 0;

static void copy_field(char *dst, size_t cap, const char *src, size_t len) {
    if (len >= cap) {
        len = cap - 1;
    }
    memcpy(dst, src, len);
    dst[len] = '\0';
}

static int starts(const char *line, size_t len, const char *prefix) {
    size_t n = strlen(prefix);
    return len >= n && memcmp(line, prefix, n) == 0;
}

static void finish(item *it, const char *end) {
    if (it != NULL && it->code != NULL) {
        while (end > it->code && (end[-1] == '\n' || end[-1] == '\r')) {
            end--;  /* the blank lines before the next item */
        }
        if (*end == '\n') {
            end++;  /* keep the chunk's last newline */
        }
        it->code_len = (size_t)(end - it->code);
        if (it->code_len == strlen("@BINARY@\n") && memcmp(it->code, "@BINARY@", 8) == 0) {
            it->code = binary_chunk;
            it->code_len = sizeof(binary_chunk) - 1;
        }
    }
}

/* Splits the file into cases and modules; pointers stay into text. */
static void parse(const char *text) {
    const char *p = text;
    item *cur = NULL;
    while (*p) {
        const char *eol = strchr(p, '\n');
        size_t len = eol ? (size_t)(eol - p) : strlen(p);
        const char *next = eol ? eol + 1 : p + len;
        if (starts(p, len, "=== module ")) {
            finish(cur, p);
            cur = &modules[nmodules++];
            memset(cur, 0, sizeof(*cur));
            copy_field(cur->name, sizeof(cur->name), p + 11, len - 11);
            cur->code = next;
        }
        else if (starts(p, len, "--- ")) {
            const char *v = p + 4;
            size_t vlen = len - 4;
            /* A directive follows the header or another directive directly. */
            if (cur != NULL && cur->code == p && starts(v, vlen, "env: ")) {
                copy_field(cur->env, sizeof(cur->env), v + 5, vlen - 5);
            }
            else if (cur != NULL && cur->code == p && starts(v, vlen, "status: ")) {
                cur->status = atoi(v + 8);
            }
            else if (cur != NULL && cur->code == p && starts(v, vlen, "expect: ")) {
                copy_field(cur->expect, sizeof(cur->expect), v + 8, vlen - 8);
            }
            else if (cur != NULL && cur->code == p && starts(v, vlen, "instructions: ")) {
                cur->instructions = atoll(v + 14);
            }
            else if (cur != NULL && cur->code == p && starts(v, vlen, "require: on")) {
                cur->require_on = 1;
            }
            else {
                finish(cur, p);
                cur = &cases[ncases++];
                memset(cur, 0, sizeof(*cur));
                copy_field(cur->name, sizeof(cur->name), v, vlen);
            }
            if (cur != NULL) {
                cur->code = next;  /* the chunk starts after the last directive */
            }
        }
        else if (cur == NULL && !(len > 0 && p[0] == '#') && len > 0) {
            fprintf(stderr, "case file: text before the first case: %.*s\n", (int)len, p);
            exit(2);
        }
        if (ncases >= MAX_ITEMS || nmodules >= MAX_ITEMS) {
            fprintf(stderr, "case file: too many items\n");
            exit(2);
        }
        p = next;
    }
    finish(cur, p);
}

/* ---- the resolver ---- */

typedef struct host {
    lua_State *L;
    int flaky;
    int calls;
    int overdraft;  /* 1: keep the stack, 2: then remove the limit, 3: then raise the limit */
} host;

static int run_named(lua_State *L, int32_t env, const char *code, int32_t len, const char *name, int32_t name_len,
                     const char **out, int32_t *out_len);

static int32_t resolver(void *ud, const char *name, int32_t len, xlua_core_source *out) {
    host *h = (host *)ud;
    int i;
    h->calls++;
    if (len == 7 && memcmp(name, "missing", 7) == 0) {
        return 0;
    }
    if (len == 6 && memcmp(name, "broken", 6) == 0) {
        out->code = "resolver failed";
        out->code_len = 15;
        return -1;
    }
    if (len == 9 && memcmp(name, "no_reason", 9) == 0) {
        out->code_len = -1;  /* a negative length is empty */
        return -1;
    }
    if (len == 5 && memcmp(name, "flaky", 5) == 0) {
        out->code = h->flaky++ == 0 ? "return = 1" : "return 'fixed'";
        out->code_len = (int32_t)strlen(out->code);
        return 1;
    }
    if (len == 5 && memcmp(name, "empty", 5) == 0) {
        out->code_len = -5;
        return 1;
    }
    if (len == 6 && memcmp(name, "nested", 6) == 0) {
        /* C# calls into the state while require waits for it: nested entries. */
        const char *inner;
        int32_t inner_len;
        CHECK(run_named(h->L, 0, "return 'inner'", 14, "n", 1, &inner, &inner_len) == OK && inner_len == 5);
        CHECK(run_named(h->L, 0, "error('inner error')", 20, "n", 1, &inner, &inner_len) == ERRRUN);
        CHECK(inner != NULL && strstr(inner, "inner error") != NULL);
        out->code = "return 'outer'";
        out->code_len = 14;
        return 1;
    }
    if (len == 9 && memcmp(name, "interrupt", 9) == 0) {
        /* Interrupt from C# while the call runs: the module never starts. */
        xlua_core_stats stats;
        CHECK(xlua_core_interrupt(h->L) == 1 && xlua_core_interrupt(h->L) == 0);
        CHECK(xlua_core_get_stats(h->L, &stats) == 1 && stats.stop_reason == INTERRUPTED);
        out->code = "return 'never'";
        out->code_len = 14;
        return 1;
    }
    if (len == 5 && memcmp(name, "watch", 5) == 0) {
        /* The watchdog's entry: time left, then the stop once the deadline passed. */
        CHECK(xlua_core_watch(h->L) > 0);
        while (xlua_core_watch(h->L) != -1) {
        }
        out->code = "return 'never'";
        out->code_len = 14;
        return 1;
    }
    if (len == 9 && memcmp(name, "overdraft", 9) == 0) {
        xlua_core_stats stats;
        /* C# runs: the stack grows over the limit, which is granted. */
        CHECK(lua_checkstack(h->L, 200000) == 1);
        CHECK(xlua_core_get_stats(h->L, &stats) == 1 && stats.overdrafts > 0);
        if (h->overdraft == 2) {
            xlua_core_set_memory_limit(h->L, 0);
        }
        else if (h->overdraft == 3) {
            xlua_core_set_memory_limit(h->L, stats.memory_used * 4);
        }
        out->code = "return 'loaded'";
        out->code_len = 15;
        return 1;
    }
    for (i = 0; i < nmodules; i++) {
        if (strlen(modules[i].name) == (size_t)len && memcmp(modules[i].name, name, (size_t)len) == 0) {
            out->code = modules[i].code;
            out->code_len = (int32_t)modules[i].code_len;
            if (strcmp(modules[i].name, "named") == 0) {
                out->chunkname = "custom/named.lua";
                out->chunkname_len = 16;
            }
            return 1;
        }
    }
    return 0;
}

/* ---- running ---- */

/* The text of a value, as tostring makes it for the types the cases return. */
static const char *value_text(const xlua_value *v, char *buf, size_t cap, int32_t *len) {
    static const char *const names[] = {"nil", NULL, NULL, NULL, NULL, "table", "function", "buffer", "userdata"};
    switch (v->type) {
    case T_STR:
        *len = v->len;
        return v->u.s;
    case T_BOOL:
        snprintf(buf, cap, "%s", v->u.i ? "true" : "false");
        break;
    case T_INT:
        snprintf(buf, cap, "%lld", (long long)v->u.i);
        break;
    case T_NUM:
        snprintf(buf, cap, "%.14g", v->u.n);
        break;
    default:
        snprintf(buf, cap, "%s", v->type >= 0 && v->type <= T_OTHER && names[v->type] ? names[v->type] : "?");
        break;
    }
    *len = (int32_t)strlen(buf);
    return buf;
}

/* Loads and runs a chunk through xlua_core_call. *out is the first result as
 * text ("nil" without one), or the message and the traceback of an error,
 * valid until the next run; NULL for an error without a message. */
static int run_named(lua_State *L, int32_t env, const char *code, int32_t len, const char *name, int32_t name_len,
                     const char **out, int32_t *out_len) {
    static char text[8192];
    xlua_core_call_t c;
    xlua_value result;
    int status;
    memset(&c, 0, sizeof(c));
    c.kind = CALL_CHUNK;
    c.env = env;
    c.code = code;
    c.code_len = len;
    c.name = name;
    c.name_len = name_len;
    c.results = &result;
    c.results_cap = 1;
    status = xlua_core_call(L, &c);
    if (status != OK) {
        CHECK(c.nresults == 0);
        if (c.error == NULL) {
            CHECK(c.traceback == NULL);
            *out = NULL;
            *out_len = 0;
            return status;
        }
        snprintf(text, sizeof(text), "%.*s%s%.*s", (int)c.error_len, c.error, c.traceback ? "\n" : "",
                 c.traceback ? (int)c.traceback_len : 0, c.traceback ? c.traceback : "");
        *out = text;
        *out_len = (int32_t)strlen(text);
        return status;
    }
    if (c.nresults == 0) {
        result.type = T_NIL;
    }
    *out = value_text(&result, text, sizeof(text), out_len);
    return status;
}

static int run(lua_State *L, int32_t env, const char *code, size_t len, const char **out, int32_t *out_len) {
    return run_named(L, env, code, (int32_t)len, "t", 1, out, out_len);
}

/* Runs code and checks the status and that *out contains expect (if not NULL). */
static int expect_run(lua_State *L, int32_t env, const char *code, int status, const char *expect, const char *what) {
    const char *out = NULL;
    int32_t len = 0;
    int got = run(L, env, code, strlen(code), &out, &len);
    int ok = got == status;
    char buf[2048];
    copy_field(buf, sizeof(buf), out ? out : "", out ? (size_t)len : 0);
    if (ok && expect != NULL) {
        ok = strstr(buf, expect) != NULL;
    }
    if (!ok) {
        fprintf(stderr, "FAIL %s: status %d (expected %d), out: %s\n", what, got, status, buf);
        failures++;
    }
    return ok;
}

static lua_State *open_state(int64_t limit, int32_t libs, host *h, int32_t *status) {
    xlua_core_config cfg;
    memset(&cfg, 0, sizeof(cfg));
    cfg.memory_limit = limit;
    cfg.libs = libs;
    cfg.sandbox = 1;
    return xlua_core_open(&cfg, h, status);
}

static void run_cases(void) {
    host h;
    int32_t status = -1, envs[16];
    char env_names[16][32];
    int nenvs = 0, i;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    L = open_state(16 << 20, 0, &h, &status);
    CHECK(L != NULL && status == OK);
    if (L == NULL) {
        return;
    }
    h.L = L;
    xlua_core_set_resolver(resolver);
    for (i = 0; i < ncases; i++) {
        item *c = &cases[i];
        int32_t env = 0, fresh = 0;
        const char *out = NULL;
        int32_t out_len = 0;
        char buf[4096];
        int got, ok;
        if (c->require_on) {
            CHECK(xlua_core_set_require(L, 1) == OK);
        }
        if (c->env[0] == '\0') {
            CHECK(xlua_core_newenv(L, &env) == OK && env > 0);
            fresh = 1;
        }
        else if (strcmp(c->env, "globals") != 0) {
            int j;
            for (j = 0; j < nenvs && strcmp(env_names[j], c->env) != 0; j++) {
            }
            if (j == nenvs) {
                CHECK(nenvs < 16 && xlua_core_newenv(L, &envs[nenvs]) == OK);
                strcpy(env_names[nenvs++], c->env);
            }
            env = envs[j];
        }
        if (c->instructions > 0) {
            CHECK(xlua_core_set_limits(L, c->instructions, 0, SMALL_SLICES) == 1);
        }
        got = run(L, env, c->code, c->code_len, &out, &out_len);
        if (c->instructions > 0) {
            CHECK(xlua_core_set_limits(L, 0, 0, 0) == 1);
        }
        copy_field(buf, sizeof(buf), out ? out : "", out ? (size_t)out_len : 0);
        ok = got == c->status && (c->expect[0] == '\0' || strstr(buf, c->expect) != NULL);
        if (!ok) {
            fprintf(stderr, "FAIL case '%s': status %d (expected %d), out: %s\n", c->name, got, c->status, buf);
            failures++;
        }
        if (fresh) {
            CHECK(xlua_core_unref(L, env) == 1);
        }
    }
    /* "flaky" was asked twice, "mod" once although required three times. */
    CHECK(h.flaky == 2);
    xlua_core_close(L);
}

/* xlua_core_open's configuration, its memory errors and library subsets. */
static void check_open(void) {
    xlua_core_config cfg;
    int32_t status = -1;
    int64_t limit;
    int saw_mem = 0;
    lua_State *L;
    CHECK(xlua_core_version() == 3);
    memset(&cfg, 0, sizeof(cfg));
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);  /* sandbox = 0 */
    cfg.sandbox = 1;
    cfg.libs = LIBS_IO;
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    cfg.libs = LIBS_PACKAGE | LIBS_STRING;
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    cfg.libs = LIBS_BUFFER;
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    cfg.libs = 0;
    cfg.memory_limit = 1;  /* not even the core struct */
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRMEM);
    /* Limits below what a sandbox needs: memory errors, first in
     * xlua_core_newstate, then while the libraries open. */
    for (limit = 512; limit < (1 << 20); limit += 512) {
        L = open_state(limit, 0, NULL, &status);
        if (L != NULL) {
            break;
        }
        CHECK(status == ERRMEM);
        saw_mem++;
    }
    CHECK(L != NULL && saw_mem > 4);
    if (L != NULL) {
        xlua_core_stats stats;
        CHECK(xlua_core_get_stats(L, &stats) == 1 && stats.memory_used <= limit);
        xlua_core_close(L);
    }
    /* Subsets. */
    L = open_state(0, LIBS_STRING, NULL, &status);
    CHECK(L != NULL);
    expect_run(L, 0, "assert(coroutine == nil and table == nil and math == nil and utf8 == nil)\n"
                     "return ('x'):rep(3)", OK, "xxx", "only string");
    xlua_core_close(L);
    L = open_state(0, LIBS_COROUTINE | LIBS_MATH, NULL, &status);
    CHECK(L != NULL);
    expect_run(L, 0, "assert(string == nil and coroutine.wrap and math.pi) return getmetatable('')", OK, "nil",
               "no string library: strings have no metatable");
    expect_run(L, 0, "return ('x'):upper()", ERRRUN, "attempt to index a string value", "no string methods");
    xlua_core_close(L);
    L = open_state(0, LIBS_TABLE | LIBS_UTF8, NULL, &status);
    CHECK(L != NULL);
    expect_run(L, 0, "return table.concat({utf8.char(72), 'i'}) .. tostring(coroutine)", OK, "Hinil", "table and utf8");
    xlua_core_close(L);
}

/* The same seed gives the same pairs order in two states. */
static void check_seed(void) {
    static const char order[] =
        "local t = {} for i = 1, 64 do t['key' .. i] = i end\n"
        "local s = {} for k in pairs(t) do s[#s + 1] = k end return table.concat(s, ',')";
    xlua_core_config cfg;
    char first[4096];
    int32_t status, len;
    const char *out;
    lua_State *a, *b;
    memset(&cfg, 0, sizeof(cfg));
    cfg.sandbox = 1;
    cfg.seed = 42;
    cfg.has_seed = 1;
    a = xlua_core_open(&cfg, NULL, &status);
    b = xlua_core_open(&cfg, NULL, &status);
    CHECK(a != NULL && b != NULL);
    if (a == NULL || b == NULL) {
        return;
    }
    CHECK(run(a, 0, order, strlen(order), &out, &len) == OK);
    copy_field(first, sizeof(first), out, (size_t)len);
    CHECK(run(b, 0, order, strlen(order), &out, &len) == OK);
    CHECK(strlen(first) == (size_t)len && memcmp(first, out, (size_t)len) == 0);
    xlua_core_close(a);
    xlua_core_close(b);
}

/* Environment handles. */
static void check_handles(void) {
    int32_t status, env = 0, other = 0, len;
    const char *out;
    lua_State *L = open_state(0, 0, NULL, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    CHECK(xlua_core_unref(L, 0) == 0 && xlua_core_unref(L, -3) == 0);
    CHECK(xlua_core_unref(L, 2) == 0);  /* LUA_RIDX_GLOBALS: the base, not an environment */
    CHECK(xlua_core_unref(L, 1) == 0);  /* LUA_RIDX_MAINTHREAD */
    CHECK(xlua_core_newenv(L, &env) == OK && xlua_core_newenv(L, &other) == OK && env != other);
    expect_run(L, env, "x = 'mine' return x", OK, "mine", "a new environment");
    expect_run(L, other, "return tostring(x)", OK, "nil", "environments are separate");
    CHECK(xlua_core_unref(L, env) == 1 && xlua_core_unref(L, env) == 0);
    CHECK(run(L, env, "return 1", 8, &out, &len) == ERRHANDLE);
    CHECK(out != NULL && strstr(out, "invalid environment handle") != NULL);
    CHECK(run(L, -1, "return 1", 8, &out, &len) == ERRHANDLE);
    CHECK(run(L, 2, "return 1", 8, &out, &len) == ERRHANDLE);
    CHECK(run(L, 99999, "return 1", 8, &out, &len) == ERRHANDLE);
    /* Negative lengths are empty. */
    CHECK(run_named(L, other, "return 1", -1, "neg", -4, &out, &len) == OK && len == 3);  /* "nil" */
    CHECK(xlua_core_unref(L, other) == 1);
    /* The stack is left as it was. */
    CHECK(lua_gettop(L) == 0);
    /* xlua_gc, LuaScript's CollectGarbage: Lua 5.5 maps the managed option numbers. */
    CHECK(xlua_gc(L, 3, 0) > 0);
    {
        static const int whats[] = {2, 4, 5, 6, 7, 0, 9, 1};  /* collect ... stop, isrunning, restart */
        size_t i;
        for (i = 0; i < sizeof(whats) / sizeof(whats[0]); i++) {
            xlua_gc(L, whats[i], whats[i] == 6 || whats[i] == 7 ? 100 : 0);
        }
    }
    CHECK(xlua_gc(L, 9, 0) == 1 && xlua_gc(L, 8, 0) == -1 && xlua_gc(L, -1, 0) == -1);
    xlua_core_close(L);
}

/* Interrupt and the watchdog entry, from inside a call (the resolver). */
static void check_stops(void) {
    host h;
    int32_t status;
    xlua_core_stats stats;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    L = open_state(0, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    xlua_core_set_resolver(resolver);
    /* A limit below the current use: every growth fails, also in the exports' own work. */
    {
        int32_t env = 7;
        CHECK(xlua_core_set_memory_limit(L, 1) == 1);
        expect_run(L, 0, "return {}", ERRMEM, "not enough memory", "a limit below the use");
        CHECK(xlua_core_newenv(L, &env) == ERRMEM);
    }
    CHECK(xlua_core_set_memory_limit(L, -5) == 1 && xlua_core_get_stats(L, &stats) == 1 && stats.memory_limit == 0);
    CHECK(xlua_core_set_require(L, 1) == OK);
    CHECK(xlua_core_interrupt(L) == 0 && xlua_core_watch(L) == -1);  /* nothing runs */
    CHECK(xlua_core_set_limits(L, 1000000000, 0, 0) == 1);
    expect_run(L, 0, "require('interrupt')", INTERRUPTED, "interrupted", "interrupt");
    expect_run(L, 0, "return require('nested')", OK, "outer", "nested entries");
    CHECK(xlua_core_set_limits(L, 0, 1000000, 0) == 1);
    expect_run(L, 0, "require('watch')", LIMIT_TIME, "time limit exceeded", "watch");
    CHECK(xlua_core_set_limits(L, 0, 0, 0) == 1);
    xlua_core_close(L);
}

/* require without a resolver, switched off again, and the overdraft rule
 * around the resolver (core-luascript.md, section 3.4). */
static void check_require(void) {
    host h;
    int32_t status;
    xlua_core_stats stats;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    L = open_state(0, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    CHECK(xlua_core_set_require(L, 1) == OK);
    xlua_core_set_resolver(NULL);
    expect_run(L, 0, "require('x')", ERRRUN, "module 'x' not found: no module resolver", "no resolver");
    xlua_core_set_resolver(resolver);
    expect_run(L, 0, "require('no_reason')", ERRRUN, "error loading module 'no_reason': \n", "no reason");
    expect_run(L, 0, "return require('empty')", OK, "true", "an empty module");
    CHECK(xlua_core_set_require(L, 0) == OK);
    expect_run(L, 0, "return require", OK, "nil", "require switched off");
    CHECK(xlua_core_set_require(L, 1) == OK);
    /* Over the limit while the resolver runs: granted, then Lua's memory error
     * once it has returned; without a limit, or with room after the
     * collection, the module loads. */
    CHECK(xlua_core_get_stats(L, &stats) == 1);
    xlua_core_set_memory_limit(L, stats.memory_used + 64 * 1024);
    h.overdraft = 1;
    expect_run(L, 0, "return require('overdraft')", ERRMEM, "not enough memory", "an overdraft");
    xlua_core_set_memory_limit(L, stats.memory_used + 64 * 1024);
    h.overdraft = 2;
    expect_run(L, 0, "return require('overdraft')", OK, "loaded", "an overdraft, then no limit");
    xlua_core_close(L);
    /* The same, with a fresh cache: raising the limit in the resolver. */
    memset(&h, 0, sizeof(h));
    L = open_state(0, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    CHECK(xlua_core_set_require(L, 1) == OK);
    CHECK(xlua_core_get_stats(L, &stats) == 1);
    xlua_core_set_memory_limit(L, stats.memory_used + 64 * 1024);
    h.overdraft = 3;
    expect_run(L, 0, "return require('overdraft')", OK, "loaded", "an overdraft, then room");
    xlua_core_close(L);
}

/* One xlua_core_call of kind with the given arguments and room for cap results. */
static int call(lua_State *L, int32_t kind, int32_t env, const char *name, const xlua_value *args, int32_t nargs,
                xlua_value *results, int32_t cap, xlua_core_call_t *c) {
    memset(c, 0, sizeof(*c));
    c->kind = kind;
    c->env = env;
    c->name = name;
    c->name_len = name != NULL ? (int32_t)strlen(name) : 0;
    c->args = args;
    c->nargs = nargs;
    c->results = results;
    c->results_cap = cap;
    return xlua_core_call(L, c);
}

static int has(const char *s, int32_t len, const char *part) {
    char buf[4096];
    copy_field(buf, sizeof(buf), s ? s : "", s ? (size_t)len : 0);
    return s != NULL && strstr(buf, part) != NULL;
}

/* xlua_core_call: functions by name, typed arguments and results, multiple
 * results, errors with the traceback apart, the anchors of string results,
 * and the instruction count (core-luascript.md, sections 2.6, 2.7 and 2.12). */
static void check_call(void) {
    static const char setup[] =
        "function add(a, b) return a + b end\n"
        "function echo(...) return ... end\n"
        "function many() return 1, 2, 3, 4, 5 end\n"
        "function none() end\n"
        "function fail() error('boom') end\n"
        "function failt() error({code = 5}) end\n"
        "function big() return ('x'):rep(1 << 20) end\n"
        "function kinds() return {}, none, coroutine.create(none) end\n"
        "function spin(n) local s = 0 for i = 1, n do s = s + i end return s end\n"
        "quest = { onStart = function(x) return x * 2 end }\n"
        "n = 5\n";
    xlua_core_call_t c;
    xlua_value args[6], res[8];
    xlua_core_stats stats;
    int32_t status, env = 0;
    int64_t first, used;
    lua_State *L = open_state(64 << 20, 0, NULL, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    expect_run(L, 0, setup, OK, "nil", "the functions of the call checks");
    /* Arguments and a float result. */
    memset(args, 0, sizeof(args));
    args[0].type = T_INT;
    args[0].u.i = 2;
    args[1].type = T_NUM;
    args[1].u.n = 0.5;
    CHECK(call(L, CALL_GLOBAL, 0, "add", args, 2, res, 1, &c) == OK && c.nresults == 1);
    CHECK(res[0].type == T_NUM && res[0].u.n == 2.5 && c.error == NULL && c.instructions == 0);
    /* Every argument type comes back with its type; 64-bit integers are exact. */
    args[0].type = T_NIL;
    args[1].type = T_BOOL;
    args[1].u.i = 1;
    args[2].type = T_INT;
    args[2].u.i = INT64_MIN;
    args[3].type = T_NUM;
    args[3].u.n = -0.25;
    args[4].type = T_STR;
    args[4].u.s = "a\0b";
    args[4].len = 3;
    args[5].type = T_STR;
    args[5].u.s = NULL;
    args[5].len = 0;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 6, res, 8, &c) == OK && c.nresults == 6);
    CHECK(res[0].type == T_NIL && res[1].type == T_BOOL && res[1].u.i == 1);
    CHECK(res[2].type == T_INT && res[2].u.i == INT64_MIN && res[3].type == T_NUM && res[3].u.n == -0.25);
    CHECK(res[4].type == T_STR && res[4].len == 3 && memcmp(res[4].u.s, "a\0b", 3) == 0);
    CHECK(res[5].type == T_STR && res[5].len == 0);
    /* More results than room: the count is the function's. */
    CHECK(call(L, CALL_GLOBAL, 0, "many", NULL, 0, res, 2, &c) == OK && c.nresults == 5);
    CHECK(res[0].u.i == 1 && res[1].u.i == 2);
    CHECK(call(L, CALL_GLOBAL, 0, "many", NULL, 0, NULL, 4, &c) == OK && c.nresults == 5);
    CHECK(call(L, CALL_GLOBAL, 0, "none", NULL, 0, res, 2, &c) == OK && c.nresults == 0);
    /* Tables, functions and other values: their type only (handles come with milestone 8). */
    CHECK(call(L, CALL_GLOBAL, 0, "kinds", NULL, 0, res, 3, &c) == OK && c.nresults == 3);
    CHECK(res[0].type == T_TABLE && res[1].type == T_FUNC && res[2].type == T_OTHER);
    /* Dotted names, the base, and what is not there. */
    args[0].type = T_INT;
    args[0].u.i = 21;
    CHECK(call(L, CALL_GLOBAL, 0, "quest.onStart", args, 1, res, 1, &c) == OK && res[0].u.i == 42);
    CHECK(call(L, CALL_GLOBAL, 0, "tostring", args, 1, res, 1, &c) == OK && res[0].type == T_STR
          && res[0].len == 2 && memcmp(res[0].u.s, "21", 2) == 0);
    CHECK(call(L, CALL_GLOBAL, 0, "missing", NULL, 0, res, 1, &c) == ERRRUN && c.traceback == NULL);
    CHECK(has(c.error, c.error_len, "attempt to call a nil value (global 'missing')"));
    CHECK(call(L, CALL_GLOBAL, 0, "quest.nothing", NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "attempt to call a nil value (field 'nothing')"));
    CHECK(call(L, CALL_GLOBAL, 0, "n.x", NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "attempt to index a number value (global 'n')"));
    CHECK(call(L, CALL_GLOBAL, 0, "nope.x", NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "attempt to index a nil value (global 'nope')"));
    CHECK(call(L, CALL_GLOBAL, 0, NULL, NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "(global '')"));
    CHECK(call(L, CALL_GLOBAL, 0, "n", NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "attempt to call a number value"));
    /* Errors: the message and the traceback apart. */
    CHECK(call(L, CALL_GLOBAL, 0, "fail", NULL, 0, res, 1, &c) == ERRRUN && c.nresults == 0);
    CHECK(has(c.error, c.error_len, "t:5: boom") && !has(c.error, c.error_len, "stack traceback"));
    CHECK(has(c.traceback, c.traceback_len, "stack traceback:") && has(c.traceback, c.traceback_len, "t:5:"));
    CHECK(call(L, CALL_GLOBAL, 0, "failt", NULL, 0, res, 1, &c) == ERRRUN);
    CHECK(has(c.error, c.error_len, "(error object is a table value)") && c.traceback != NULL);
    /* Arguments that cannot be passed, and other kinds. */
    args[0].type = T_TABLE;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == ERRTYPE && has(c.error, c.error_len, "argument 1"));
    args[0].type = T_STR;
    args[0].len = -1;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == ERRTYPE);
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, -1, res, 1, &c) == ERRSTACK);
    CHECK(call(L, CALL_GLOBAL, 0, "echo", NULL, 1, res, 1, &c) == ERRSTACK);
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 2000000, res, 1, &c) == ERRSTACK && has(c.error, c.error_len, "too many"));
    CHECK(call(L, CALL_REF, 0, "echo", NULL, 0, res, 1, &c) == ERRCONFIG && c.error != NULL);
    CHECK(call(L, CALL_COMPILE, 0, "echo", NULL, 0, res, 1, &c) == ERRCONFIG);
    CHECK(call(L, 99, 0, "echo", NULL, 0, res, 1, &c) == ERRCONFIG);
    CHECK(call(L, CALL_GLOBAL, 12345, "echo", NULL, 0, res, 1, &c) == ERRHANDLE);
    CHECK(has(c.error, c.error_len, "invalid environment handle"));
    /* Syntax errors have no traceback; a chunk gets its environment. */
    memset(&c, 0, sizeof(c));
    c.kind = CALL_CHUNK;
    c.code = "return +";
    c.code_len = 8;
    c.name = "syn";
    c.name_len = 3;
    CHECK(xlua_core_call(L, &c) == ERRSYNTAX && has(c.error, c.error_len, "syn:1:") && c.traceback == NULL);
    CHECK(xlua_core_newenv(L, &env) == OK);
    c.code = "x = 7 return x";
    c.code_len = 14;
    c.env = env;
    c.results = res;
    c.results_cap = 1;
    CHECK(xlua_core_call(L, &c) == OK && c.nresults == 1 && res[0].u.i == 7);
    CHECK(call(L, CALL_GLOBAL, env, "add", NULL, 0, res, 1, &c) == ERRRUN);  /* other environment */
    CHECK(xlua_core_unref(L, env) == 1);
    /* String results are anchored until the next call: a full collection keeps them. */
    xlua_gc(L, 2, 0);
    CHECK(xlua_core_get_stats(L, &stats) == 1);
    used = stats.memory_used;
    CHECK(call(L, CALL_GLOBAL, 0, "big", NULL, 0, res, 1, &c) == OK && res[0].type == T_STR && res[0].len == 1 << 20);
    xlua_gc(L, 2, 0);
    CHECK(res[0].u.s[0] == 'x' && res[0].u.s[(1 << 20) - 1] == 'x');
    CHECK(xlua_core_get_stats(L, &stats) == 1 && stats.memory_used >= used + (1 << 20));
    /* A result passed back as an argument: the anchors go only after the arguments are pushed. */
    args[0] = res[0];
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == OK && res[0].len == 1 << 20);
    CHECK(res[0].u.s[(1 << 20) - 1] == 'x');
    CHECK(call(L, CALL_GLOBAL, 0, "none", NULL, 0, res, 1, &c) == OK);
    xlua_gc(L, 2, 0);
    CHECK(xlua_core_get_stats(L, &stats) == 1 && stats.memory_used < used + (1 << 19));
    /* The instruction count of a call with an instruction limit, the same every time. */
    args[0].type = T_INT;
    args[0].u.i = 1000;
    CHECK(xlua_core_set_limits(L, 1000000000, 0, 0) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "spin", args, 1, res, 1, &c) == OK && res[0].u.i == 500500 && c.instructions > 1000);
    first = c.instructions;
    CHECK(call(L, CALL_GLOBAL, 0, "spin", args, 1, res, 1, &c) == OK && c.instructions == first);
    CHECK(xlua_core_set_limits(L, 2000, 0, 0) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "spin", args, 1, res, 1, &c) == LIMIT_INSTR && c.instructions == 2000);
    CHECK(has(c.error, c.error_len, "instruction limit exceeded") && c.traceback != NULL);
    CHECK(xlua_core_set_limits(L, 0, 0, 0) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "spin", args, 1, res, 1, &c) == OK && c.instructions == 0);
    /* Memory errors have no traceback. */
    CHECK(xlua_core_get_stats(L, &stats) == 1);
    CHECK(xlua_core_set_memory_limit(L, stats.memory_used + 4096) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "big", NULL, 0, res, 1, &c) == ERRMEM && c.traceback == NULL);
    CHECK(has(c.error, c.error_len, "not enough memory"));
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);
}

static char *read_file(const char *path) {
    FILE *f = fopen(path, "rb");
    char *text;
    long size;
    if (f == NULL) {
        fprintf(stderr, "cannot open %s\n", path);
        exit(2);
    }
    fseek(f, 0, SEEK_END);
    size = ftell(f);
    fseek(f, 0, SEEK_SET);
    text = (char *)malloc((size_t)size + 1);
    if (text == NULL || fread(text, 1, (size_t)size, f) != (size_t)size) {
        fprintf(stderr, "cannot read %s\n", path);
        exit(2);
    }
    text[size] = '\0';
    fclose(f);
    return text;
}

int main(int argc, char **argv) {
    char *text = read_file(argc > 1 ? argv[1] : "Test/Native/core_sandbox_cases.txt");
    parse(text);
    if (ncases < 50 || nmodules < 5) {
        fprintf(stderr, "case file: only %d cases and %d modules\n", ncases, nmodules);
        return 2;
    }
    check_open();
    check_seed();
    check_handles();
    check_stops();
    check_require();
    check_call();
    run_cases();
    free(text);
    if (failures) {
        fprintf(stderr, "%d failures\n", failures);
        return 1;
    }
    printf("XLUA_CORE_SMOKE_PASS: %d cases, %d modules\n", ncases, nmodules);
    return 0;
}
