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
 *  - host functions (xlua_core_register and a C dispatcher in place of C#'s):
 *    arguments, results, every error kind of the frame, yields, nested calls
 *    on the host function's thread, unregistering, dotted names across
 *    environments, the limits and the overdraft rule around a host function;
 *  - xlua_core_call's mode: binary chunks refused without CMake
 *    XLUA_TRUSTED_BYTECODE, and with it the host's precompiled chunks
 *    (XLUA_EXPECT_TRUSTED_BYTECODE=ON or OFF says which build it is).
 *  - the buffer library (CMake XLUA_BUFFER; XLUA_EXPECT_BUFFER=ON or OFF says
 *    which build it is): the cases with "--- libs: buffer" in a second state,
 *    and buffer handles of xlua_core_handle and in xlua_core_call;
 *  - handles (milestone 8): every operation of xlua_core_handle, tables and
 *    functions as arguments and results, XLUA_CALL_REF and _COMPILE, the
 *    error value, borrowed arguments of host functions, environments as any
 *    table, registration into a table, and the release rules of
 *    xlua_core_unref.
 *  - coroutines (milestone 9): XLUA_CALL_START, _RESUME and _CLOSE, their
 *    statuses and values, errors and tracebacks, __close, the limits per
 *    step, a yield from a host function, coroutines as handles and their
 *    status, the refusals, and a resume nested in a host function.
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
#include <math.h>
#include <time.h>

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
    int32_t error_len, traceback_len, error_ref, mode, flags;
} xlua_core_call_t;

typedef struct xlua_core_frame {
    lua_State *L;
    const xlua_value *args;
    xlua_value *results;
    const char *error;
    int32_t nargs, results_cap, nresults, status, error_len, error_arg, yieldable, id;
} xlua_core_frame;

typedef void (*xlua_core_dispatch)(void *host, int32_t fn_id, xlua_core_frame *frame);
enum { FRAME_RETURN, FRAME_YIELD, FRAME_ERROR, FRAME_ERROR_WHERE, FRAME_ARGERROR, FRAME_TYPEERROR, FRAME_MEMERR };

enum { OK = 0, ERRRUN = 2, ERRSYNTAX = 3, ERRMEM = 4, LIMIT_INSTR = 10, LIMIT_TIME = 11, INTERRUPTED = 12,
       ERRTYPE = 20, ERRSTACK = 21, ERRHANDLE = 22, ERRCONFIG = 23 };
enum { YIELD = 1 };
enum { T_NIL, T_BOOL, T_INT, T_NUM, T_STR, T_TABLE, T_FUNC, T_BUFFER, T_OTHER, T_THREAD };
enum { CO_SUSPENDED = 0, CO_FINISHED = 1, CO_FAILED = 2, CO_RUNNING = 4 };
enum { CALL_GLOBAL = 0, CALL_REF = 1, CALL_CHUNK = 2, CALL_COMPILE = 3, CALL_RESUME = 4, CALL_CLOSE = 5, CALL_START = 8 };
enum { LIBS_COROUTINE = 1, LIBS_STRING = 2, LIBS_TABLE = 4, LIBS_MATH = 8, LIBS_UTF8 = 16, LIBS_BUFFER = 32,
       LIBS_IO = 64, LIBS_PACKAGE = 512 };
#define SMALL_SLICES 2

int xlua_core_version(void);
lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status);
void xlua_core_close(lua_State *L);
int xlua_core_set_memory_limit(lua_State *L, int64_t bytes);
int xlua_core_get_stats(lua_State *L, xlua_core_stats *out);
int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
int xlua_core_handle(lua_State *L, int32_t op, xlua_value *v);
int xlua_core_unref(lua_State *L, int32_t ref);
int xlua_core_call(lua_State *L, xlua_core_call_t *c);
void xlua_core_set_resolver(xlua_core_resolver fn);
int xlua_core_set_require(lua_State *L, int32_t enabled);
void xlua_core_set_dispatcher(xlua_core_dispatch fn);
int xlua_core_register(lua_State *L, int32_t env, const char *name, int32_t name_len, int32_t fn_id, int32_t *old_id);
int xlua_core_interrupt(lua_State *L);
int64_t xlua_core_watch(lua_State *L);
int xlua_gc(lua_State *L, int what, int data);
int lua_checkstack(lua_State *L, int n);
int lua_gettop(lua_State *L);
enum { H_GET, H_SET, H_LEN, H_NEXT, H_INEXT, H_NEWTABLE, H_NEWENV, H_REF, H_KEY, H_EQUAL, H_NEWBUFFER, H_BUFFER };
enum { CALLF_HOST_ERROR = 1, CALLF_ERROR_TABLE = 2, CALLF_ERROR_FUNC = 4, CALLF_DEAD = 8 };

/* What the exports xlua_core_newenv, _newbuffer and _tobuffer did before
 * xlua_core_handle replaced them (milestone 8). */
static int newenv(lua_State *L, int32_t *env) {
    xlua_value v[4];
    int status;
    memset(v, 0, sizeof(v));
    status = xlua_core_handle(L, H_NEWENV, v);
    *env = status == 0 ? (int32_t)v[0].u.i : 0;
    return status;
}

static int newbuffer(lua_State *L, int32_t len, int32_t *ref, void **data) {
    xlua_value v[4];
    int status;
    memset(v, 0, sizeof(v));
    v[1].u.i = len;
    status = xlua_core_handle(L, H_NEWBUFFER, v);
    *ref = status == 0 ? (int32_t)v[0].u.i : 0;
    *data = status == 0 ? (void *)v[2].u.s : NULL;
    return status;
}

static void *tobuffer(lua_State *L, int32_t ref, int32_t *len) {
    xlua_value v[4];
    memset(v, 0, sizeof(v));
    v[0].u.i = ref;
    if (xlua_core_handle(L, H_BUFFER, v) != 0) {
        *len = 0;
        return NULL;
    }
    *len = v[2].len;
    return (void *)v[2].u.s;
}

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
    int buffer;  /* "--- libs: buffer" */
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
            else if (cur != NULL && cur->code == p && starts(v, vlen, "libs: ")) {
                if (vlen != 12 || memcmp(v + 6, "buffer", 6) != 0) {
                    fprintf(stderr, "case file: unknown libs: %.*s\n", (int)vlen, v);
                    exit(2);
                }
                cur->buffer = 1;
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
    char text[512];       /* host functions: a string result */
    xlua_value many[24];  /* host functions: results beyond the inline 8 */
    int32_t co;           /* FN_CORESUME: the coroutine it resumes */
    int co_status;        /* FN_CORESUME: the status of that resume */
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
    static const char *const names[] = {"nil", NULL, NULL, NULL, NULL, "table", "function", "buffer", "userdata", "thread"};
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
        snprintf(buf, cap, "%s", v->type >= 0 && v->type <= T_THREAD && names[v->type] ? names[v->type] : "?");
        break;
    }
    *len = (int32_t)strlen(buf);
    return buf;
}

/* Loads and runs a chunk through xlua_core_call. *out is the first result as
 * text ("nil" without one), or the message and the traceback of an error,
 * valid until the next run; NULL for an error without a message. */
static int run_mode(lua_State *L, int32_t env, const char *code, int32_t len, const char *name, int32_t name_len,
                    int32_t mode, const char **out, int32_t *out_len);

static int run_named(lua_State *L, int32_t env, const char *code, int32_t len, const char *name, int32_t name_len,
                     const char **out, int32_t *out_len) {
    return run_mode(L, env, code, len, name, name_len, 0, out, out_len);
}

/* run_named with xlua_core_call_t.mode (XLUA_CORE_MODE_* bits; 0 is text). */
static int run_mode(lua_State *L, int32_t env, const char *code, int32_t len, const char *name, int32_t name_len,
                    int32_t mode, const char **out, int32_t *out_len) {
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
    c.mode = mode;
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

/* Whether the library has the buffer library (CMake XLUA_BUFFER), checked
 * against XLUA_EXPECT_BUFFER when it is set. */
static int buffer_built(void) {
    int32_t status = -1;
    lua_State *L = open_state(0, LIBS_COROUTINE | LIBS_STRING | LIBS_TABLE | LIBS_MATH | LIBS_UTF8 | LIBS_BUFFER, NULL, &status);
    const char *expect = getenv("XLUA_EXPECT_BUFFER");
    int built = L != NULL;
    if (L != NULL) {
        xlua_core_close(L);
    }
    else {
        CHECK(status == ERRCONFIG);
    }
    if (expect != NULL && (strcmp(expect, "ON") == 0 || strcmp(expect, "on") == 0)) {
        CHECK(built);
    }
    if (expect != NULL && (strcmp(expect, "OFF") == 0 || strcmp(expect, "off") == 0)) {
        CHECK(!built);
    }
    return built;
}

typedef struct case_state {
    lua_State *L;
    int32_t envs[16];
    char env_names[16][32];
    int nenvs;
} case_state;

static void run_cases(void) {
    host h;
    int32_t status = -1;
    case_state states[2];  /* the safe libraries; with the buffer library */
    int i, skipped = 0;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    memset(states, 0, sizeof(states));
    L = open_state(16 << 20, 0, &h, &status);
    CHECK(L != NULL && status == OK);
    if (L == NULL) {
        return;
    }
    states[0].L = L;
    if (buffer_built()) {
        states[1].L = open_state(16 << 20, LIBS_COROUTINE | LIBS_STRING | LIBS_TABLE | LIBS_MATH | LIBS_UTF8 | LIBS_BUFFER, &h, &status);
        CHECK(states[1].L != NULL && status == OK);
    }
    h.L = L;
    xlua_core_set_resolver(resolver);
    for (i = 0; i < ncases; i++) {
        item *c = &cases[i];
        case_state *cs = &states[c->buffer ? 1 : 0];
        int32_t env = 0, fresh = 0;
        const char *out = NULL;
        int32_t out_len = 0;
        char buf[4096];
        int got, ok;
        if (cs->L == NULL) {
            skipped++;
            continue;
        }
        L = cs->L;
        if (c->require_on) {
            CHECK(xlua_core_set_require(L, 1) == OK);
        }
        if (c->env[0] == '\0') {
            CHECK(newenv(L, &env) == OK && env > 0);
            fresh = 1;
        }
        else if (strcmp(c->env, "globals") != 0) {
            int j;
            for (j = 0; j < cs->nenvs && strcmp(cs->env_names[j], c->env) != 0; j++) {
            }
            if (j == cs->nenvs) {
                CHECK(cs->nenvs < 16 && newenv(L, &cs->envs[cs->nenvs]) == OK);
                strcpy(cs->env_names[cs->nenvs++], c->env);
            }
            env = cs->envs[j];
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
    xlua_core_close(states[0].L);
    if (states[1].L != NULL) {
        CHECK(lua_gettop(states[1].L) == 0);
        xlua_core_close(states[1].L);
    }
    if (skipped > 0) {
        printf("core_smoke: %d buffer cases skipped (no buffer library)\n", skipped);
    }
}

/* xlua_core_open's configuration, its memory errors and library subsets. */
static void check_open(void) {
    xlua_core_config cfg;
    int32_t status = -1;
    int64_t limit;
    int saw_mem = 0;
    lua_State *L;
    CHECK(xlua_core_version() == 6);
    memset(&cfg, 0, sizeof(cfg));
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);  /* sandbox = 0 */
    cfg.sandbox = 1;
    cfg.libs = LIBS_IO;
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    cfg.libs = LIBS_PACKAGE | LIBS_STRING;
    CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    if (!buffer_built()) {
        cfg.libs = LIBS_BUFFER;
        CHECK(xlua_core_open(&cfg, NULL, &status) == NULL && status == ERRCONFIG);
    }
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
    CHECK(newenv(L, &env) == OK && newenv(L, &other) == OK && env != other);
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
        CHECK(newenv(L, &env) == ERRMEM);
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
    /* Tables, functions (milestone 8) and coroutines (milestone 9) are new owning handles. */
    CHECK(call(L, CALL_GLOBAL, 0, "kinds", NULL, 0, res, 3, &c) == OK && c.nresults == 3);
    CHECK(res[0].type == T_TABLE && res[1].type == T_FUNC && res[2].type == T_THREAD && res[2].len == CO_SUSPENDED);
    CHECK(res[0].u.i > 3 && res[1].u.i > 3 && res[0].u.i != res[1].u.i && res[2].u.i > 3);
    CHECK(xlua_core_unref(L, (int32_t)res[0].u.i) == 1 && xlua_core_unref(L, (int32_t)res[1].u.i) == 1);
    CHECK(xlua_core_unref(L, (int32_t)res[2].u.i) == 1);
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
    /* The error value itself: an owning handle (LuaRuntimeException.ErrorValue). */
    CHECK(c.flags == CALLF_ERROR_TABLE && c.error_ref > 3 && xlua_core_unref(L, c.error_ref) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "fail", NULL, 0, res, 1, &c) == ERRRUN && c.error_ref == 0 && c.flags == 0);
    /* Arguments that cannot be passed, and other kinds. */
    args[0].type = T_TABLE;
    args[0].u.i = 99999;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == ERRHANDLE && has(c.error, c.error_len, "argument 1 is not a valid handle"));
    args[0].type = T_STR;
    args[0].len = -1;  /* a key handle */
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == ERRHANDLE);
    args[0].type = 99;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 1, res, 1, &c) == ERRTYPE && has(c.error, c.error_len, "cannot be passed"));
    args[0].type = T_STR;
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, -1, res, 1, &c) == ERRSTACK);
    CHECK(call(L, CALL_GLOBAL, 0, "echo", NULL, 1, res, 1, &c) == ERRSTACK);
    CHECK(call(L, CALL_GLOBAL, 0, "echo", args, 2000000, res, 1, &c) == ERRSTACK && has(c.error, c.error_len, "too many"));
    CHECK(call(L, CALL_REF, 0, "echo", NULL, 0, res, 1, &c) == ERRHANDLE && has(c.error, c.error_len, "invalid function handle"));
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
    CHECK(newenv(L, &env) == OK);
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

/* ---- xlua_core_call's mode and CMake XLUA_TRUSTED_BYTECODE ---- */

enum { MODE_TEXT = 1, MODE_BINARY = 2 };

/* The chunks of core_regression.py's TRUSTED, precompiled with Lua 5.4.9 and
 * 5.5.1 (stripped): "main" returns "trusted" when its _ENV is the environment
 * it runs in, "noup" is a function without upvalues that returns 7. */
static const char trusted_54_main[] =
    "\x1b\x4c\x75\x61\x54\x00\x19\x93\x0d\x0a\x1a\x0a\x04\x08\x08\x78\x56\x00\x00\x00\x00\x00\x00\x00"
    "\x00\x00\x00\x00\x28\x77\x40\x01\x80\x80\x80\x00\x01\x02\x91\x51\x00\x00\x00\x0b\x00\x00\x00\x89"
    "\x00\x00\x00\x39\x00\x01\x00\x38\x04\x00\x80\x0b\x00\x00\x01\x3c\x80\x02\x00\xb8\x02\x00\x80\x0b"
    "\x00\x00\x03\x3c\x00\x02\x00\x38\x01\x00\x80\x03\x00\x02\x00\x42\x80\x00\x00\x38\x00\x00\x80\x03"
    "\x80\x02\x00\x46\x00\x02\x01\x46\x00\x01\x01\x86\x04\x83\x5f\x47\x04\x87\x73\x74\x72\x69\x6e\x67"
    "\x00\x04\x83\x69\x6f\x04\x88\x74\x72\x75\x73\x74\x65\x64\x04\x86\x77\x72\x6f\x6e\x67\x81\x01\x00"
    "\x00\x80\x80\x80\x80\x80";
static const char trusted_54_noup[] =
    "\x1b\x4c\x75\x61\x54\x00\x19\x93\x0d\x0a\x1a\x0a\x04\x08\x08\x78\x56\x00\x00\x00\x00\x00\x00\x00"
    "\x00\x00\x00\x00\x28\x77\x40\x00\x80\x81\x81\x00\x00\x02\x83\x01\x00\x03\x80\x48\x00\x02\x00\x47"
    "\x00\x01\x00\x80\x80\x80\x80\x80\x80\x80";
static const char trusted_55_main[] =
    "\x1b\x4c\x75\x61\x55\x00\x19\x93\x0d\x0a\x1a\x0a\x04\x88\xa9\xff\xff\x04\x78\x56\x34\x12\x08\x88"
    "\xa9\xff\xff\xff\xff\xff\xff\x08\x00\x00\x00\x00\x00\x28\x77\xc0\x01\x00\x00\x00\x01\x02\x11\x00"
    "\x53\x00\x00\x00\x0b\x00\x00\x00\x89\x00\x00\x00\x39\x00\x01\x00\x38\x04\x00\x80\x0b\x00\x00\x01"
    "\x3c\x80\x02\x00\xb8\x02\x00\x80\x0b\x00\x00\x03\x3c\x00\x02\x00\x38\x01\x00\x80\x03\x00\x02\x00"
    "\x42\x80\x00\x00\x38\x00\x00\x80\x03\x80\x02\x00\x46\x00\x02\x01\x46\x00\x01\x01\x06\x04\x03\x5f"
    "\x47\x00\x04\x07\x73\x74\x72\x69\x6e\x67\x00\x00\x04\x03\x69\x6f\x00\x04\x08\x74\x72\x75\x73\x74"
    "\x65\x64\x00\x04\x06\x77\x72\x6f\x6e\x67\x00\x01\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00";
static const char trusted_55_noup[] =
    "\x1b\x4c\x75\x61\x55\x00\x19\x93\x0d\x0a\x1a\x0a\x04\x88\xa9\xff\xff\x04\x78\x56\x34\x12\x08\x88"
    "\xa9\xff\xff\xff\xff\xff\xff\x08\x00\x00\x00\x00\x00\x28\x77\xc0\x00\x01\x01\x00\x00\x02\x03\x00"
    "\x01\x00\x03\x80\x48\x00\x02\x00\x47\x00\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00";

/* Runs code (len bytes) with a mode; checks the status and that *out contains expect. */
static void expect_mode(lua_State *L, int32_t env, const char *code, size_t len, int32_t mode, int status,
                        const char *expect, const char *what) {
    const char *out = NULL;
    int32_t out_len = 0;
    char buf[512];
    int got = run_mode(L, env, code, (int32_t)len, "t", 1, mode, &out, &out_len);
    copy_field(buf, sizeof(buf), out ? out : "", out ? (size_t)out_len : 0);
    if (got != status || (expect != NULL && strstr(buf, expect) == NULL)) {
        fprintf(stderr, "FAIL %s: status %d (expected %d), out: %s\n", what, got, status, buf);
        failures++;
    }
}

/* XLUA_EXPECT_TRUSTED_BYTECODE (ON or OFF) says which build it is; unset, the library decides. */
static void check_trusted_bytecode(void) {
    static const char off[] = "binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)";
    const char *expect_env = getenv("XLUA_EXPECT_TRUSTED_BYTECODE");
    const char *out = NULL, *main_chunk, *noup;
    size_t main_len, noup_len;
    int32_t status = -1, out_len = 0;
    int built;
    lua_State *L = open_state(0, 0, NULL, &status);
    CHECK(L != NULL && status == OK);
    if (L == NULL) {
        return;
    }
    expect_mode(L, 0, "return 'text'", 13, MODE_TEXT, OK, "text", "mode text");
    expect_mode(L, 0, "return 1", 8, 0, OK, "1", "mode 0 is text");
    expect_mode(L, 0, "return 1", 8, 4, ERRCONFIG, "invalid mode", "mode 4");
    built = run_mode(L, 0, "return 1", 8, "t", 1, MODE_TEXT | MODE_BINARY, &out, &out_len) == OK;
    if (expect_env != NULL && strcmp(expect_env, "ON") == 0) {
        CHECK(built);
    }
    if (expect_env != NULL && strcmp(expect_env, "OFF") == 0) {
        CHECK(!built);
    }
    CHECK(run(L, 0, "return _VERSION", 15, &out, &out_len) == OK && out_len == 7);
    if (out != NULL && strncmp(out, "Lua 5.4", 7) == 0) {
        main_chunk = trusted_54_main, main_len = sizeof(trusted_54_main) - 1;
        noup = trusted_54_noup, noup_len = sizeof(trusted_54_noup) - 1;
    } else {
        main_chunk = trusted_55_main, main_len = sizeof(trusted_55_main) - 1;
        noup = trusted_55_noup, noup_len = sizeof(trusted_55_noup) - 1;
    }
    if (!built) {
        expect_mode(L, 0, main_chunk, main_len, MODE_BINARY, ERRCONFIG, off, "binary refused");
        expect_mode(L, 0, "return 1", 8, MODE_TEXT | MODE_BINARY, ERRCONFIG, off, "text and binary refused");
    } else {
        int32_t env = 0;
        CHECK(newenv(L, &env) == OK);
        expect_mode(L, 0, main_chunk, main_len, MODE_BINARY, OK, "trusted", "binary chunk");
        expect_mode(L, env, main_chunk, main_len, MODE_TEXT | MODE_BINARY, OK, "trusted", "binary chunk, new environment");
        expect_mode(L, 0, noup, noup_len, MODE_BINARY, OK, "7", "binary function without upvalues");
        expect_mode(L, 0, main_chunk, main_len, MODE_TEXT, ERRSYNTAX, "attempt to load a binary chunk (mode is 't')", "binary in text mode");
        expect_mode(L, 0, "return 1", 8, MODE_BINARY, ERRSYNTAX, "attempt to load a text chunk (mode is 'b')", "text in binary mode");
        expect_mode(L, 0, main_chunk, main_len - 8, MODE_BINARY, ERRSYNTAX, "truncated", "truncated chunk");
        CHECK(xlua_core_unref(L, env) == 1);
    }
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);
}

/* ---- host functions (xlua_core_register, xlua_core_set_dispatcher; milestone 7) ---- */

enum { FN_ECHO = 1, FN_TYPES, FN_ERROR, FN_WHERE, FN_ARGERR, FN_TYPEERR, FN_MEMERR, FN_YIELD, FN_MANY, FN_NESTED,
       FN_OVERDRAFT, FN_INTERRUPT, FN_SLEEP, FN_BADSTATUS, FN_BADCOUNT, FN_BADTYPE, FN_REGISTER, FN_ID,
       FN_BUFLEN, FN_BUFRES, FN_BORROW, FN_CORESUME, FN_LAST };

static host *dispatched_host = NULL;  /* the host of the last call, to check the host pointer */
static int32_t borrowed_ref = 0;      /* FN_BORROW's handle of its argument */

static int hop(lua_State *L, int op, xlua_value *v) {
    return xlua_core_handle(L, op, v);
}

static void set_str(xlua_value *v, const char *s) {
    v->type = T_STR;
    v->len = (int32_t)strlen(s);
    v->u.s = s;
}

static void set_int(xlua_value *v, int64_t i) {
    v->type = T_INT;
    v->len = 0;
    v->u.i = i;
}

static void set_ref(xlua_value *v, int type, int64_t ref) {
    v->type = type;
    v->len = 0;
    v->u.i = ref;
}


/* Keeps the CPU busy for about ms milliseconds: clock() is portable C, and
 * the processor time it counts passes while the loop runs. */
static void busy_ms(int64_t ms) {
    clock_t start = clock();
    while ((double)(clock() - start) * 1000.0 / CLOCKS_PER_SEC < (double)ms) {
    }
}

/* The C side of a C# dispatcher: one behaviour per function id. */
static void host_dispatch(void *ud, int32_t id, xlua_core_frame *f) {
    host *h = (host *)ud;
    int i;
    dispatched_host = h;
    h->calls++;
    switch (id) {
    case FN_ECHO:  /* primitive arguments back as results; strings point into the Lua strings */
        for (i = 0; i < f->nargs && i < f->results_cap; i++) {
            f->results[i] = f->args[i];
        }
        f->nresults = i;
        break;
    case FN_TYPES: {  /* "type:payload" of every argument, as one string */
        size_t n = 0;
        h->text[0] = '\0';
        for (i = 0; i < f->nargs; i++) {
            const xlua_value *v = &f->args[i];
            n += (size_t)snprintf(h->text + n, sizeof(h->text) - n, "%s%d:%lld", i ? "," : "", (int)v->type,
                                  v->type == T_NUM ? (long long)(v->u.n * 10) : v->type == T_STR ? (long long)v->len : (long long)v->u.i);
        }
        f->results[0].type = T_STR;
        f->results[0].u.s = h->text;
        f->results[0].len = (int32_t)n;
        f->nresults = 1;
        break;
    }
    case FN_ERROR:
        f->status = FRAME_ERROR;
        f->error = "raw\0message";
        f->error_len = 11;
        break;
    case FN_WHERE:
        f->status = FRAME_ERROR_WHERE;
        f->error = "positioned";
        f->error_len = 10;
        break;
    case FN_ARGERR:
        f->status = FRAME_ARGERROR;
        f->error_arg = 1;
        f->error = "custom problem";
        break;
    case FN_TYPEERR:
        f->status = FRAME_TYPEERROR;
        f->error_arg = 2;
        f->error = "number";
        break;
    case FN_MEMERR:
        f->status = FRAME_MEMERR;
        break;
    case FN_YIELD:  /* yields 7 (also where the caller cannot yield: Lua's error) */
        f->results[0].type = T_INT;
        f->results[0].u.i = 7 + f->yieldable;
        f->nresults = 1;
        f->status = FRAME_YIELD;
        break;
    case FN_MANY: {  /* n results, from the host's own memory beyond the 8 inline ones */
        int64_t n = f->nargs > 0 && f->args[0].type == T_INT ? f->args[0].u.i : 0;
        if (n > 24) {
            n = 24;
        }
        for (i = 0; i < n; i++) {
            h->many[i].type = T_INT;
            h->many[i].u.i = i + 1;
        }
        f->results = h->many;
        f->results_cap = 24;
        f->nresults = (int32_t)n;
        break;
    }
    case FN_NESTED: {  /* C# calls into the script: on the host function's thread */
        xlua_core_call_t c;
        xlua_value r;
        memset(&c, 0, sizeof(c));
        c.kind = CALL_GLOBAL;
        c.name = "where";
        c.name_len = 5;
        c.results = &r;
        c.results_cap = 1;
        CHECK(xlua_core_call(h->L, &c) == OK && c.nresults == 1 && r.type == T_STR);
        f->results[0] = r;  /* anchored until the next call: the trampoline pushes it first */
        f->nresults = 1;
        break;
    }
    case FN_OVERDRAFT: {  /* the stack grows over the limit while C# runs: granted */
        xlua_core_stats stats;
        CHECK(lua_checkstack(f->L, 200000) == 1);
        CHECK(xlua_core_get_stats(h->L, &stats) == 1 && stats.overdrafts > 0);
        break;
    }
    case FN_INTERRUPT:
        CHECK(xlua_core_interrupt(h->L) == 1);
        f->results[0].type = T_INT;
        f->results[0].u.i = 1;
        f->nresults = 1;
        break;
    case FN_SLEEP:
        busy_ms(f->nargs > 0 ? f->args[0].u.i : 0);
        break;
    case FN_BADSTATUS:
        f->status = 99;
        break;
    case FN_BADCOUNT:
        f->nresults = f->results_cap + 1;
        break;
    case FN_BADTYPE:  /* a table result whose handle is none (0 would be the default environment) */
        f->results[0].type = T_TABLE;
        f->results[0].u.i = 99999;
        f->nresults = 1;
        break;
    case FN_REGISTER: {  /* registering from inside a host function */
        int32_t old = -1;
        CHECK(xlua_core_register(h->L, 0, "late", 4, FN_ECHO, &old) == OK && old == 0);
        break;
    }
    case FN_ID:  /* the frame serial grows */
        f->results[0].type = T_INT;
        f->results[0].u.i = f->id;
        f->results[1].type = T_BOOL;
        f->results[1].u.i = f->L != NULL;
        f->nresults = 2;
        break;
    case FN_BUFLEN:  /* a buffer argument: its length and first byte, which it then sets to 99 */
        CHECK(f->nargs == 1 && f->args[0].type == T_BUFFER);
        if (f->nargs == 1 && f->args[0].type == T_BUFFER && f->args[0].len > 0) {
            f->results[0].type = T_INT;
            f->results[0].u.i = f->args[0].len * 1000 + (unsigned char)f->args[0].u.s[0];
            f->nresults = 1;
            ((char *)f->args[0].u.s)[0] = 99;
        }
        break;
    case FN_BUFRES:  /* the buffer of the owning handle given as an integer */
        f->results[0].type = T_BUFFER;
        f->results[0].u.i = f->nargs > 0 ? f->args[0].u.i : 0;
        f->nresults = 1;
        break;
    case FN_CORESUME: {  /* resumes coroutine h->co (milestone 9): a step nested in a host function */
        xlua_core_call_t c;
        memset(&c, 0, sizeof(c));
        c.kind = CALL_RESUME;
        c.fn = h->co;
        c.results = f->results;
        c.results_cap = 1;
        h->co_status = xlua_core_call(h->L, &c);
        f->nresults = h->co_status == YIELD || h->co_status == OK ? (c.nresults > 0 ? 1 : 0) : 0;
        break;
    }
    case FN_BORROW: {  /* argument 1 (a table) as a handle: its field v, and the table itself */
        xlua_value w[4];
        memset(w, 0, sizeof(w));
        w[0].len = 1;  /* its stack index */
        CHECK(f->nargs >= 1 && f->args[0].type == T_TABLE && f->args[0].u.i == 1);
        CHECK(xlua_core_handle(h->L, H_REF, w) == 0 && w[2].type == T_TABLE);
        borrowed_ref = (int32_t)w[2].u.i;
        memset(w, 0, sizeof(w));
        set_ref(&w[0], T_TABLE, borrowed_ref);
        set_str(&w[1], "v");
        CHECK(xlua_core_handle(h->L, H_GET, w) == 0);
        f->results[0] = w[2];
        set_ref(&f->results[1], T_TABLE, borrowed_ref);
        f->nresults = 2;
        w[0].len = 9;  /* beyond its arguments */
        CHECK(xlua_core_handle(h->L, H_REF, w) == ERRHANDLE);
        break;
    }
    default:
        CHECK(!"unknown host function id");
        break;
    }
}

static void reg(lua_State *L, const char *name, int32_t id, int32_t expect_old) {
    int32_t old = -1;
    CHECK(xlua_core_register(L, 0, name, (int32_t)strlen(name), id, &old) == OK);
    if (old != expect_old) {
        fprintf(stderr, "register %s: old id %d, expected %d\n", name, (int)old, (int)expect_old);
        failures++;
    }
}

/* A chunk's call: status and flags. */
static int run_flags(lua_State *L, const char *code, int32_t *flags) {
    xlua_core_call_t c;
    memset(&c, 0, sizeof(c));
    c.kind = CALL_CHUNK;
    c.code = code;
    c.code_len = (int32_t)strlen(code);
    c.name = "h";
    c.name_len = 1;
    c.flags = -1;
    int status = xlua_core_call(L, &c);
    *flags = c.flags;
    return status;
}

static void check_host(void) {
    host h;
    int32_t status = -1, old = -1, env = 0, flags = -1, limit_status;
    xlua_core_call_t c;
    xlua_value args[2], r[4];
    lua_State *L;
    static const char *const names[] = {NULL, "echo", "types", "fail", "where", "argerr", "typeerr", "memerr",
                                        "yield", "many", "nested", "overdraft", "interrupt", "sleep", "badstatus",
                                        "badcount", "badtype", "register", "id", "buflen", "bufres", "borrow", "coresume"};
    int i;
    memset(&h, 0, sizeof(h));
    L = open_state(16 << 20, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    /* Without a dispatcher every host function fails, also a registered one. */
    xlua_core_set_dispatcher(NULL);
    reg(L, "echo", FN_ECHO, 0);
    expect_run(L, 0, "return echo(1)", ERRRUN, "unregistered", "no dispatcher");
    xlua_core_set_dispatcher(host_dispatch);
    for (i = 2; i < FN_LAST; i++) {
        reg(L, names[i], i, 0);
    }
    /* Names: the base or one library; anything else is refused. */
    CHECK(xlua_core_register(L, 0, "", 0, FN_ECHO, &old) == ERRCONFIG && old == 0);
    CHECK(xlua_core_register(L, 0, NULL, 4, FN_ECHO, &old) == ERRCONFIG);
    CHECK(xlua_core_register(L, 0, ".a", 2, FN_ECHO, &old) == ERRCONFIG);
    CHECK(xlua_core_register(L, 0, "a.", 2, FN_ECHO, &old) == ERRCONFIG);
    CHECK(xlua_core_register(L, 0, "a.b.c", 5, FN_ECHO, &old) == ERRCONFIG);
    CHECK(xlua_core_register(L, 1, "a", 1, FN_ECHO, &old) == ERRHANDLE);  /* not a table handle */
    CHECK(xlua_core_register(L, 0, "a", -1, FN_ECHO, &old) == ERRCONFIG);

    /* Arguments and results. */
    expect_run(L, 0, "local a, b, c, d, e = echo(nil, true, -3, 2.5, 'x\\0y') "
                     "return tostring(a) .. tostring(b) .. c .. math.type(c) .. d .. #e", OK, "niltrue-3integer2.53", "echo");
    expect_run(L, 0, "return types(nil, false, 5, 0.5, 'abc', {}, print or type, coroutine.create(type))", OK,
               "0:0,1:0,2:5,3:5,4:3,5:6,6:7,9:8", "the argument types and stack indices");
    expect_run(L, 0, "return types(1,2,3,4,5,6,7,8,9,10)", OK, "2:1,2:2,2:3,2:4,2:5,2:6,2:7,2:8,2:9,2:10", "10 arguments");
    expect_run(L, 0, "return select('#', echo(1,2,3,4,5,6,7,8,9))", OK, "8", "echo returns what fits inline");
    expect_run(L, 0, "return select('#', many(24)) .. ',' .. select(24, many(24))", OK, "24,24", "results from the host's memory");
    expect_run(L, 0, "return select('#', many(0))", OK, "0", "no results");
    /* Errors, raised once C# has returned. */
    expect_run(L, 0, "fail()", ERRRUN, "raw", "a raw error");
    expect_run(L, 0, "local ok, e = pcall(fail) return #e", OK, "11", "the message keeps its NUL");
    expect_run(L, 0, "\nwhere()", ERRRUN, "t:2: positioned", "an error at the caller's position");
    expect_run(L, 0, "argerr(1)", ERRRUN, "bad argument #1 to 'argerr' (custom problem)", "luaL_argerror");
    expect_run(L, 0, "typeerr(1, 'x')", ERRRUN, "bad argument #2 to 'typeerr' (number expected, got string)", "luaL_typeerror");
    expect_run(L, 0, "typeerr(1)", ERRRUN, "(number expected, got no value)", "no value");
    expect_run(L, 0, "typeerr(1, 2, 3, 4, 5, 6, 7, 8, 9)", ERRRUN, "(number expected, got number)", "nine arguments");
    expect_run(L, 0, "memerr()", ERRMEM, "not enough memory", "Lua's memory error");
    expect_run(L, 0, "return select(2, pcall(memerr))", OK, "not enough memory", "pcall catches the memory error");
    expect_run(L, 0, "badstatus()", ERRRUN, "invalid frame status 99", "an invalid status");
    expect_run(L, 0, "badcount()", ERRRUN, "invalid result count 9", "an invalid result count");
    expect_run(L, 0, "badtype()", ERRRUN, "result 1 is not a valid handle (type 5)", "a table result without a handle");
    /* XLUA_CALLF_HOST_ERROR: the host function's error reached the host unchanged. */
    CHECK(run_flags(L, "fail()", &flags) == ERRRUN && flags == 1);
    CHECK(run_flags(L, "where()", &flags) == ERRRUN && flags == 1);
    CHECK(run_flags(L, "local ok, e = pcall(fail) error(e, 0)", &flags) == ERRRUN && flags == 1);
    CHECK(run_flags(L, "local ok, e = pcall(fail) error(e .. '!', 0)", &flags) == ERRRUN && flags == 0);
    CHECK(run_flags(L, "error('raw\\0message', 0)", &flags) == ERRRUN && flags == 0);  /* the next call cleared it */
    CHECK(run_flags(L, "argerr(1)", &flags) == ERRRUN && flags == 0);
    CHECK(run_flags(L, "return 1", &flags) == OK && flags == 0);
    /* Yield: in a coroutine, and Lua's error elsewhere. */
    expect_run(L, 0, "local co = coroutine.create(function() return yield() + 1 end) "
                     "local ok, v = coroutine.resume(co) local ok2, w = coroutine.resume(co, 41) "
                     "return v .. ',' .. w .. ',' .. coroutine.status(co)", OK, "8,42,dead", "a yield from the host");
    expect_run(L, 0, "yield()", ERRRUN, "attempt to yield from outside a coroutine", "not yieldable");
    /* Nested calls run on the host function's thread. */
    expect_run(L, 0, "function where() return coroutine.isyieldable() and 'coroutine' or 'main' end "
                     "return nested() .. ',' .. coroutine.wrap(function() return nested() end)()", OK, "main,main",
               "a nested call cannot yield on either thread");
    expect_run(L, 0, "function where() local co, main = coroutine.running() return main and 'main thread' or 'coroutine thread' end "
                     "return nested() .. ',' .. coroutine.wrap(function() return nested() end)()", OK,
               "main thread,coroutine thread", "the nested call runs on the coroutine");
    /* The frame. */
    expect_run(L, 0, "local a, la = id() local b = id() return (b - a) .. tostring(la)", OK, "1true", "the frame serial and thread");
    CHECK(dispatched_host == &h);
    /* Registration from inside a host function. */
    expect_run(L, 0, "register() return late(5)", OK, "5", "a registration inside a host function");

    /* Unregistering and replacing: the old closure fails wherever it is kept. */
    expect_run(L, 0, "keep = echo return keep(3)", OK, "3", "kept");
    reg(L, "echo", 0, FN_ECHO);
    reg(L, "echo", 0, 0);  /* again: nothing to remove */
    expect_run(L, 0, "return keep(3)", ERRRUN, "unregistered", "a kept closure after unregistering");
    expect_run(L, 0, "return echo == nil", OK, "true", "the name is gone");
    reg(L, "echo", FN_ECHO, 0);
    expect_run(L, 0, "keep = echo", OK, NULL, "keep the new one");
    reg(L, "echo", FN_TYPES, FN_ECHO);  /* replaced */
    expect_run(L, 0, "return keep(1)", ERRRUN, "unregistered", "the replaced closure");
    expect_run(L, 0, "return echo(1)", OK, "2:1", "the new function");
    reg(L, "echo", FN_ECHO, FN_TYPES);

    /* Dotted names: a library that every environment copies. */
    CHECK(newenv(L, &env) == OK);
    expect_run(L, env, "game = 5", OK, NULL, "an environment with its own 'game'");
    reg(L, "game.log", FN_ECHO, 0);
    expect_run(L, 0, "return game.log('a')", OK, "a", "a new library in the default environment");
    expect_run(L, env, "return game", OK, "5", "an environment's own value is kept");
    reg(L, "nolib.f", 0, 0);  /* removing from a library that does not exist */
    expect_run(L, 0, "mine = {} game2 = game game.own = 1 game.gone = nil", OK, NULL, "the default environment's copy");
    reg(L, "game.own", FN_ECHO, 0);
    expect_run(L, 0, "return game.own", OK, "1", "a script's own value in its copy is kept");
    reg(L, "game.extra", FN_ECHO, 0);
    expect_run(L, 0, "return game.extra('e') .. tostring(rawequal(game, game2))", OK, "etrue", "the copy gets a later function");
    expect_run(L, 0, "game = mine", OK, NULL, "a script's own library table");
    reg(L, "game.third", FN_ECHO, 0);
    expect_run(L, 0, "return game.third == nil and game2.third == nil", OK, "true", "only the copy an environment holds is updated");
    expect_run(L, 0, "game = nil", OK, NULL, "the library removed");
    reg(L, "game.fourth", FN_ECHO, 0);
    expect_run(L, 0, "return game == nil", OK, "true", "an existing library is not copied again");
    reg(L, "game.extra", 0, FN_ECHO);
    expect_run(L, 0, "return select(2, pcall(game2.extra))", OK, "unregistered", "an unregistered function fails in any copy");
    {
        int32_t env2 = 0;
        CHECK(newenv(L, &env2) == OK);
        expect_run(L, env2, "return game.log('n') .. tostring(game.extra) .. game.fourth('4')", OK, "nnil4", "a new environment's copy");
        CHECK(xlua_core_unref(L, env2) == 1);
    }
    reg(L, "string.shout", FN_ECHO, 0);
    expect_run(L, 0, "return string.shout('s') .. ('x'):shout()", OK, "sx", "a standard library and its string methods");
    CHECK(xlua_core_unref(L, env) == 1);

    /* Limits around host functions. */
    xlua_core_set_limits(L, (int64_t)1 << 40, 0, 0);
    expect_run(L, 0, "interrupt() return 2", INTERRUPTED, "interrupted", "Interrupt inside a host function");
    memset(&c, 0, sizeof(c));
    c.kind = CALL_GLOBAL;
    c.name = "interrupt";
    c.name_len = 9;
    c.results = r;
    c.results_cap = 4;
    CHECK(xlua_core_call(L, &c) == INTERRUPTED && c.nresults == 0);  /* no Lua instruction after it */
    xlua_core_set_limits(L, 0, 20 * 1000000LL, 1);  /* the clock check (no watchdog) */
    memset(&c, 0, sizeof(c));
    c.kind = CALL_GLOBAL;
    c.name = "sleep";
    c.name_len = 5;
    args[0].type = T_INT;
    args[0].u.i = 60;
    c.args = args;
    c.nargs = 1;
    limit_status = xlua_core_call(L, &c);
    CHECK(limit_status == LIMIT_TIME);
    args[0].u.i = 0;
    CHECK(xlua_core_call(L, &c) == OK);  /* within the deadline */
    xlua_core_set_limits(L, 0, 0, 0);
    expect_run(L, 0, "return echo(1)", OK, "1", "without limits");
    reg(L, "tostring", FN_ECHO, 0);  /* a base function that is not a host function: replaced, old id 0 */
    expect_run(L, 0, "return tostring('s')", OK, "s", "a base function replaced");
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);

    /* The overdraft rule: an allocation under C# is granted and raised after it. */
    memset(&h, 0, sizeof(h));
    L = open_state(0, 0, &h, &status);
    CHECK(L != NULL);
    if (L != NULL) {
        xlua_core_stats stats;
        h.L = L;
        reg(L, "overdraft", FN_OVERDRAFT, 0);
        CHECK(xlua_core_get_stats(L, &stats) == 1);
        xlua_core_set_memory_limit(L, stats.memory_used + 4096);
        expect_run(L, 0, "overdraft()", ERRMEM, "not enough memory", "the memory error after the host function");
        xlua_core_set_memory_limit(L, 0);
        expect_run(L, 0, "overdraft() return 'ok'", OK, "ok", "no limit: no error");
        xlua_core_close(L);
    }
    /* Memory errors of the registration itself. */
    {
        int64_t limit;
        int saw = 0, done = 0;
        for (limit = 0; limit < 64 * 1024 && !done; limit += 64) {
            xlua_core_stats stats;
            memset(&h, 0, sizeof(h));
            L = open_state(0, 0, &h, &status);
            CHECK(L != NULL);
            if (L == NULL) {
                break;
            }
            xlua_gc(L, 2, 0);  /* LUA_GCCOLLECT: no garbage for the emergency collection to free */
            CHECK(xlua_core_get_stats(L, &stats) == 1);
            xlua_core_set_memory_limit(L, stats.memory_used + limit);
            status = xlua_core_register(L, 0, "lib.function_with_a_long_name", 29, FN_ECHO, &old);
            if (status == ERRMEM) {
                saw++;
            }
            else {
                CHECK(status == OK && old == 0);
                done = 1;
            }
            xlua_core_close(L);
        }
        CHECK(saw > 0 && done);
    }
}

static void check_host_buffer(void);

/* The buffer library's C exports and buffer handles in xlua_core_call
 * (core-luascript.md, section 2.10). */
static void check_buffer(void) {
    int32_t status = -1, ref = 5, len = 7, made, env = 0;
    void *data = (void *)&len;
    xlua_value arg, results[2];
    xlua_core_call_t c;
    xlua_core_stats stats;
    lua_State *L;
    if (!buffer_built()) {
        L = open_state(0, 0, NULL, &status);
        CHECK(L != NULL);
        CHECK(newbuffer(L, 4, &ref, &data) == ERRCONFIG && ref == 0 && data == NULL);
        CHECK(tobuffer(L, 1, &len) == NULL && len == 0);
        xlua_core_close(L);
        printf("core_smoke: no buffer library (XLUA_BUFFER=OFF)\n");
        return;
    }
    L = open_state(0, 0, NULL, &status);  /* without LuaLibs.Buffer */
    CHECK(newbuffer(L, 4, &ref, &data) == ERRCONFIG && ref == 0);
    xlua_core_close(L);
    L = open_state(8 << 20, LIBS_COROUTINE | LIBS_STRING | LIBS_TABLE | LIBS_MATH | LIBS_UTF8 | LIBS_BUFFER, NULL, &status);
    CHECK(L != NULL && status == OK);
    if (L == NULL) {
        return;
    }
    expect_run(L, 0, "function len(b) return buffer.len(b) end\n"
                     "function make(n) local b = buffer.create(n) buffer.writeu32(b, 0, 0xA1B2C3D4) return b end\n"
                     "function keep(b) kept = b end", OK, NULL, "buffer functions");
    CHECK(newbuffer(L, -1, &ref, &data) == ERRCONFIG && ref == 0);
    CHECK(newbuffer(L, (1 << 30) + 1, &ref, &data) == ERRCONFIG);
    CHECK(newbuffer(L, 16, &ref, &data) == OK && ref > 0 && data != NULL);
    if (data != NULL) {
        static const char zeros[16];
        CHECK(memcmp(data, zeros, 16) == 0);
        memcpy(data, "\x04\x03\x02\x01", 4);
    }
    CHECK(tobuffer(L, ref, &len) == data && len == 16);
    memset(&arg, 0, sizeof(arg));
    arg.type = T_BUFFER;
    arg.u.i = ref;
    CHECK(call(L, CALL_GLOBAL, 0, "len", &arg, 1, results, 2, &c) == OK && c.nresults == 1 && results[0].type == T_INT
          && results[0].u.i == 16);
    CHECK(call(L, CALL_GLOBAL, 0, "keep", &arg, 1, results, 2, &c) == OK);
    expect_run(L, 0, "return buffer.readu32(kept, 0)", OK, "16909060", "a buffer from C seen by Lua");
    arg.type = T_INT;
    arg.u.i = 8;
    CHECK(call(L, CALL_GLOBAL, 0, "make", &arg, 1, results, 2, &c) == OK && c.nresults == 1);
    CHECK(results[0].type == T_BUFFER && results[0].len == 8 && results[0].u.i > 0);
    made = (int32_t)results[0].u.i;
    data = tobuffer(L, made, &len);
    CHECK(data != NULL && len == 8 && memcmp(data, "\xd4\xc3\xb2\xa1", 4) == 0);
    CHECK(tobuffer(L, 0, &len) == NULL && len == 0);
    CHECK(tobuffer(L, 2, &len) == NULL);  /* the base table */
    CHECK(newenv(L, &env) == OK && tobuffer(L, env, &len) == NULL);
    arg.type = T_BUFFER;
    arg.u.i = env;
    CHECK(call(L, CALL_GLOBAL, 0, "len", &arg, 1, results, 2, &c) == ERRHANDLE
          && has(c.error, c.error_len, "argument 1 is not a valid handle"));
    CHECK(xlua_core_unref(L, env) == 1);
    CHECK(xlua_core_unref(L, made) == 1 && xlua_core_unref(L, made) == 0);
    CHECK(tobuffer(L, made, &len) == NULL);
    arg.u.i = made;
    CHECK(call(L, CALL_GLOBAL, 0, "len", &arg, 1, results, 2, &c) == ERRHANDLE);
    CHECK(xlua_core_unref(L, ref) == 1);
    /* The memory limit applies to buffers from C. */
    CHECK(newbuffer(L, 16 << 20, &ref, &data) == ERRMEM && ref == 0 && data == NULL);
    CHECK(xlua_core_get_stats(L, &stats) == 1 && stats.memory_used <= (8 << 20) + 16384);
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);
}

/* Buffers as host function arguments (bytes in place) and results (owning handles). */
static void check_host_buffer(void) {
    host h;
    int32_t status = -1, ref = 0, old = -1;
    void *data = NULL;
    char code[128];
    lua_State *L;
    if (!buffer_built()) {
        return;
    }
    memset(&h, 0, sizeof(h));
    L = open_state(0, LIBS_COROUTINE | LIBS_STRING | LIBS_TABLE | LIBS_MATH | LIBS_UTF8 | LIBS_BUFFER, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    xlua_core_set_dispatcher(host_dispatch);
    CHECK(xlua_core_register(L, 0, "buflen", 6, FN_BUFLEN, &old) == OK);
    CHECK(xlua_core_register(L, 0, "bufres", 6, FN_BUFRES, &old) == OK);
    expect_run(L, 0, "local b = buffer.fromstring('\\7xyz') local n = buflen(b) return n .. ',' .. buffer.readu8(b, 0)", OK,
               "4007,99", "a buffer argument in place");
    CHECK(newbuffer(L, 5, &ref, &data) == OK && ref > 0);
    snprintf(code, sizeof(code), "return buffer.len(bufres(%d))", (int)ref);
    expect_run(L, 0, code, OK, "5", "a buffer result from its handle");
    expect_run(L, 0, "return bufres(0)", ERRRUN, "result 1 is not a valid handle (type 7)", "an invalid buffer handle");
    CHECK(xlua_core_unref(L, ref) == 1);
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


/* ---- handles (xlua_core_handle, milestone 8) ---- */

/* t[k] for a string key: the value in out (handles: new owning ones). */
static int get_field(lua_State *L, int32_t t, const char *k, xlua_value *out) {
    xlua_value v[4];
    int status;
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, t);
    set_str(&v[1], k);
    status = hop(L, H_GET, v);
    *out = v[2];
    return status;
}

static int32_t new_table(lua_State *L) {
    xlua_value v[4];
    memset(v, 0, sizeof(v));
    v[1].u.i = 4;
    v[1].len = 4;
    CHECK(hop(L, H_NEWTABLE, v) == OK && v[0].type == T_TABLE && v[0].u.i > 3);
    return (int32_t)v[0].u.i;
}

static void check_tables(void) {
    host h;
    int32_t status, t, t2, key, env, fn, it = 0, steps, nested, len;
    xlua_value v[4], r[4], args[2];
    xlua_core_call_t c;
    xlua_core_stats stats;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    L = open_state(16 << 20, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    xlua_core_set_dispatcher(host_dispatch);
    reg(L, "borrow", FN_BORROW, 0);
    expect_run(L, 0, "function get(t, k) return t[k] end\n"
                     "function count(t) local n = 0 for _ in pairs(t) do n = n + 1 end return n end\n"
                     "cfg = { a = 1, list = { 10, 20, 30, nil, 50 }, nested = { deep = 42 }, s = 'text', f = function(x) return x * 2 end }\n"
                     "return 1", OK, NULL, "the table functions");

    /* NEWTABLE, SET and GET with every key form; LEN. */
    t = new_table(L);
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, t);
    set_str(&v[1], "a");
    set_int(&v[2], 1);
    CHECK(hop(L, H_SET, v) == OK);
    set_int(&v[1], 1);
    set_str(&v[2], "one");
    CHECK(hop(L, H_SET, v) == OK);
    v[1].type = T_BOOL;
    v[1].u.i = 1;
    v[2].type = T_NUM;
    v[2].u.n = 2.5;
    CHECK(hop(L, H_SET, v) == OK);
    CHECK(get_field(L, t, "a", r) == OK && r[0].type == T_INT && r[0].u.i == 1);
    CHECK(get_field(L, t, "missing", r) == OK && r[0].type == T_NIL);
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, t);
    set_int(&v[1], 1);
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_STR && v[2].len == 3 && memcmp(v[2].u.s, "one", 3) == 0);
    v[1].type = T_NUM;
    v[1].u.n = 1.0;  /* a float key finds the integer key */
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_STR);
    v[1].type = T_BOOL;
    v[1].u.i = 1;
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_NUM && v[2].u.n == 2.5);
    CHECK(hop(L, H_LEN, v) == OK && v[2].type == T_INT && v[2].u.i == 1);
    /* Raw: no metamethods, and nil and NaN keys are Lua's errors with the message in v[2]. */
    v[1].type = T_NIL;
    v[2].type = T_INT;
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_NIL);
    v[2].type = T_INT;
    CHECK(hop(L, H_SET, v) == ERRRUN && v[2].type == T_STR && has(v[2].u.s, v[2].len, "index is nil"));
    v[1].type = T_NUM;
    v[1].u.n = (double)NAN;
    v[2].type = T_INT;
    CHECK(hop(L, H_SET, v) == ERRRUN && has(v[2].u.s, v[2].len, "index is NaN"));
    /* Keys and values that cannot be: an unknown type, a handle that is none or of another type. */
    v[1].type = 99;
    CHECK(hop(L, H_GET, v) == ERRTYPE);
    set_str(&v[1], "x");
    v[2].type = T_TABLE;
    v[2].u.i = 99999;
    CHECK(hop(L, H_SET, v) == ERRHANDLE);
    set_ref(&v[0], T_TABLE, 99999);
    CHECK(hop(L, H_GET, v) == ERRHANDLE && hop(L, H_LEN, v) == ERRHANDLE);
    set_ref(&v[0], T_TABLE, 2);  /* Lua's registry slots are no handles */
    CHECK(hop(L, H_GET, v) == ERRHANDLE);
    set_ref(&v[0], T_TABLE, -1);
    CHECK(hop(L, H_GET, v) == ERRHANDLE);
    CHECK(hop(L, 99, v) == ERRCONFIG);

    /* Handle 0 is the default environment; values as handles; type only with a borrow handle. */
    CHECK(get_field(L, 0, "cfg", r) == OK && r[0].type == T_TABLE && r[0].u.i > 3);
    t2 = (int32_t)r[0].u.i;
    CHECK(get_field(L, t2, "nested", r) == OK && r[0].type == T_TABLE);
    nested = (int32_t)r[0].u.i;
    CHECK(get_field(L, nested, "deep", r) == OK && r[0].u.i == 42);
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, t2);
    set_str(&v[1], "f");
    v[3].u.i = INT32_MIN;  /* type only */
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_FUNC && v[2].u.i == INT32_MIN);
    v[3].u.i = 0;
    CHECK(hop(L, H_GET, v) == OK && v[2].type == T_FUNC && v[2].u.i > 3);
    fn = (int32_t)v[2].u.i;
    /* EQUAL and REF. */
    memset(v, 0, sizeof(v));
    v[0].u.i = nested;
    CHECK(hop(L, H_REF, v) == OK && v[2].type == T_TABLE && v[2].u.i != nested);
    v[1].u.i = v[2].u.i;
    CHECK(hop(L, H_EQUAL, v) == OK && v[2].type == T_BOOL && v[2].u.i == 1);
    CHECK(xlua_core_unref(L, (int32_t)v[1].u.i) == 1);
    v[1].u.i = t2;
    CHECK(hop(L, H_EQUAL, v) == OK && v[2].u.i == 0);
    v[1].u.i = 99999;
    CHECK(hop(L, H_EQUAL, v) == OK && v[2].u.i == 0);
    v[0].u.i = 99999;
    CHECK(hop(L, H_REF, v) == ERRHANDLE);
    v[0].len = 1;  /* a stack index, outside any host function */
    v[0].u.i = 0;
    CHECK(hop(L, H_REF, v) == ERRHANDLE);
    /* KEY: an anchored string, never released. */
    memset(v, 0, sizeof(v));
    set_str(&v[1], "deep");
    CHECK(hop(L, H_KEY, v) == OK && v[2].type == T_STR && v[2].len == -1 && v[2].u.i > 3);
    key = (int32_t)v[2].u.i;
    CHECK(xlua_core_unref(L, key) == 0);
    v[1].type = T_INT;
    CHECK(hop(L, H_KEY, v) == ERRTYPE);
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, nested);
    v[1].type = T_STR;
    v[1].len = -1;
    v[1].u.i = key;
    CHECK(hop(L, H_GET, v) == OK && v[2].u.i == 42);
    set_int(&v[2], 43);
    CHECK(hop(L, H_SET, v) == OK && get_field(L, nested, "deep", r) == OK && r[0].u.i == 43);
    v[1].u.i = t;  /* a table handle where a key handle must be a string */
    CHECK(hop(L, H_GET, v) == ERRHANDLE);

    /* Tables and functions as arguments; CALL_REF; COMPILE. */
    set_ref(&args[0], T_TABLE, t);
    set_str(&args[1], "a");
    CHECK(call(L, CALL_GLOBAL, 0, "get", args, 2, r, 1, &c) == OK && r[0].u.i == 1);
    set_int(&args[0], 21);
    memset(&c, 0, sizeof(c));
    c.kind = CALL_REF;
    c.fn = fn;
    c.args = args;
    c.nargs = 1;
    c.results = r;
    c.results_cap = 1;
    CHECK(xlua_core_call(L, &c) == OK && r[0].u.i == 42);
    c.fn = t;  /* a table is no function */
    CHECK(xlua_core_call(L, &c) == ERRHANDLE && has(c.error, c.error_len, "invalid function handle"));
    memset(&c, 0, sizeof(c));
    c.kind = CALL_COMPILE;
    c.code = "n = (n or 0) + 1 return n + ...";
    c.code_len = (int32_t)strlen(c.code);
    c.name = "compiled";
    c.name_len = 8;
    c.results = r;
    c.results_cap = 1;
    CHECK(newenv(L, &env) == OK);
    c.env = env;
    CHECK(xlua_core_call(L, &c) == OK && c.nresults == 1 && r[0].type == T_FUNC);
    CHECK(get_field(L, env, "n", &v[3]) == OK && v[3].type == T_NIL);  /* not run */
    memset(&c, 0, sizeof(c));
    c.kind = CALL_REF;
    c.fn = (int32_t)r[0].u.i;
    set_int(&args[0], 10);
    c.args = args;
    c.nargs = 1;
    c.results = r + 1;
    c.results_cap = 1;
    CHECK(xlua_core_call(L, &c) == OK && r[1].u.i == 11 && xlua_core_call(L, &c) == OK && r[1].u.i == 12);
    CHECK(get_field(L, env, "n", &v[3]) == OK && v[3].u.i == 2 && get_field(L, 0, "n", &v[3]) == OK && v[3].type == T_NIL);
    CHECK(xlua_core_unref(L, c.fn) == 1);
    memset(&c, 0, sizeof(c));
    c.kind = CALL_COMPILE;
    c.code = "return +";
    c.code_len = 8;
    c.name = "bad";
    c.name_len = 3;
    CHECK(xlua_core_call(L, &c) == ERRSYNTAX && c.nresults == 0);
    /* Any table is an environment: a plain one sees only its own keys. */
    expect_run(L, t, "return a", OK, "1", "a plain table as the environment");
    expect_run(L, t, "return tostring(a)", ERRRUN, "global 'tostring'", "no base there");
    /* Register into a table: a key of that table, dots included. */
    CHECK(xlua_core_register(L, t, "my.echo", 7, FN_ECHO, &status) == OK && status == 0);
    expect_run(L, t, "return _ENV['my.echo'](5)", OK, "5", "a host function in a table");
    expect_run(L, 0, "return my", OK, "nil", "not in the base");
    CHECK(xlua_core_register(L, t, "my.echo", 7, 0, &status) == OK && status == FN_ECHO);
    CHECK(xlua_core_register(L, 99999, "x", 1, FN_ECHO, &status) == ERRHANDLE);

    /* NEXT: every pair once, in next's order; table values as borrowed iterator handles. */
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, t2);
    v[3].len = 1;  /* start a new iterator */
    steps = 0;
    while (hop(L, H_NEXT, v) == OK && v[1].type != T_NIL) {
        it = (int32_t)v[3].u.i;
        v[3].len = 0;
        steps++;
        CHECK(v[1].type == T_STR);
        if (v[2].type == T_TABLE) {
            CHECK(v[2].u.i == -2 * it - 1);  /* the iterator's value */
            if (v[1].len == 4 && memcmp(v[1].u.s, "list", 4) == 0) {
                xlua_value w[4];
                memset(w, 0, sizeof(w));
                set_ref(&w[0], T_TABLE, v[2].u.i);
                CHECK(hop(L, H_LEN, w) == OK && (w[2].u.i == 5 || w[2].u.i == 3));
                w[0].u.i = v[2].u.i;
                CHECK(hop(L, H_REF, w) == OK && w[2].type == T_TABLE && w[2].u.i > 3);  /* Retain */
                CHECK(xlua_core_unref(L, (int32_t)w[2].u.i) == 1);
            }
        }
        if (steps > 10) {
            break;
        }
    }
    CHECK(steps == 5 && it > 3);
    CHECK(get_field(L, t2, "list", r) == OK);
    len = (int32_t)r[0].u.i;
    /* The same iterator again, over 1, 2, ... (INEXT). */
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, len);
    v[3].u.i = it;
    v[3].len = 1;
    steps = 0;
    while (hop(L, H_INEXT, v) == OK && v[1].type != T_NIL && steps < 10) {
        v[3].len = 0;
        steps++;
        CHECK(v[1].type == T_INT && v[1].u.i == steps && v[2].u.i == steps * 10);
    }
    CHECK(steps == 3 && v[3].u.i == it);
    /* Not started and not an iterator. */
    memset(v, 0, sizeof(v));
    set_ref(&v[0], T_TABLE, len);
    v[3].u.i = 99999;
    CHECK(hop(L, H_NEXT, v) == ERRHANDLE);
    /* Removing the current key and growing the table: "invalid key to 'next'". */
    {
        int32_t grow = new_table(L);
        char name[32];
        int i, err = 0;
        for (i = 0; i < 8; i++) {
            snprintf(name, sizeof(name), "k%d", i);
            memset(r, 0, sizeof(r));
            set_ref(&r[0], T_TABLE, grow);
            set_str(&r[1], name);
            set_int(&r[2], i);
            CHECK(hop(L, H_SET, r) == OK);
        }
        memset(v, 0, sizeof(v));
        set_ref(&v[0], T_TABLE, grow);
        v[3].u.i = it;
        v[3].len = 1;
        for (i = 0; i < 20 && !err; i++) {
            status = hop(L, H_NEXT, v);
            v[3].len = 0;
            if (status != OK) {
                err = status == ERRRUN && has(v[2].u.s, v[2].len, "invalid key to 'next'");
                break;
            }
            if (v[1].type == T_NIL) {
                break;
            }
            memset(r, 0, sizeof(r));
            set_ref(&r[0], T_TABLE, grow);
            /* The current key through the iterator's borrowed handle; a string handle has len -1. */
            set_ref(&r[1], T_STR, -2 * it);
            r[1].len = -1;
            CHECK(hop(L, H_SET, r) == OK);  /* r[2] nil: removes it */
            {
                int j;
                for (j = 0; j < 64; j++) {
                    snprintf(name, sizeof(name), "new%d_%d", i, j);
                    set_str(&r[1], name);
                    set_int(&r[2], j);
                    CHECK(hop(L, H_SET, r) == OK);
                }
            }
        }
        CHECK(err);
        CHECK(xlua_core_unref(L, grow) == 1);
    }
    CHECK(xlua_core_unref(L, it) == 1);  /* an iterator is a table handle */

    /* Borrowed arguments of a host function (REF from its stack). */
    expect_run(L, 0, "local t = { v = 7 } local v, same = borrow(t) return v * 10 + (same == t and 1 or 0)", OK, "71",
               "a host function's argument as a handle");
    CHECK(xlua_core_unref(L, borrowed_ref) == 1);

    /* Release: tables, functions and environments once; keys and Lua's slots never. */
    CHECK(xlua_core_unref(L, t) == 1 && xlua_core_unref(L, t) == 0);
    CHECK(get_field(L, t, "a", r) == ERRHANDLE);
    CHECK(xlua_core_unref(L, fn) == 1 && xlua_core_unref(L, nested) == 1 && xlua_core_unref(L, len) == 1);
    CHECK(xlua_core_unref(L, t2) == 1 && xlua_core_unref(L, env) == 1);
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);

    /* Memory: SET past the limit is Lua's memory error, never more than the limit plus the reserve. */
    L = open_state(1 << 20, 0, NULL, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    t = new_table(L);
    {
        int64_t i;
        for (i = 1, status = OK; i < 10000000 && status == OK; i++) {
            memset(v, 0, sizeof(v));
            set_ref(&v[0], T_TABLE, t);
            set_int(&v[1], i);
            set_int(&v[2], i);
            status = hop(L, H_SET, v);
        }
    }
    CHECK(status == ERRMEM);
    CHECK(xlua_core_get_stats(L, &stats) == 1 && stats.memory_used <= stats.memory_limit + 16 * 1024 && stats.refusals > 0);
    for (status = 0; status < 100; status++) {
        memset(v, 0, sizeof(v));
        CHECK(hop(L, H_NEWTABLE, v) == ERRMEM || xlua_core_unref(L, (int32_t)v[0].u.i) == 1);
    }
    CHECK(xlua_core_unref(L, t) == 1);
    xlua_gc(L, 2, 0);
    CHECK(new_table(L) > 3);
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);
}

/* ---- coroutines (XLUA_CALL_START, _RESUME, _CLOSE; milestone 9) ---- */

/* One step of coroutine co: kind CALL_RESUME or CALL_CLOSE. */
static int co_step(lua_State *L, int32_t kind, int32_t co, const xlua_value *args, int32_t nargs, xlua_value *r,
                   int32_t cap, xlua_core_call_t *c) {
    memset(c, 0, sizeof(*c));
    c->kind = kind;
    c->fn = co;
    c->args = args;
    c->nargs = nargs;
    c->results = r;
    c->results_cap = cap;
    return xlua_core_call(L, c);
}

static void check_coroutines(void) {
    static const char setup[] =
        "function gen(n) for i = 1, n do coroutine.yield(i, 'v' .. i) end return 'done' end\n"
        "function fail() coroutine.yield(1) error('boom') end\n"
        "function failt() error({code = 7}) end\n"
        "function closer() local x <close> = setmetatable({}, {__close = function(_, e) closed = e or true end}) coroutine.yield(1) end\n"
        "function closefail() local x <close> = setmetatable({}, {__close = function() error('in close') end}) coroutine.yield(1) end\n"
        "function faultclose() local x <close> = setmetatable({}, {__close = function(_, e) closed = e end}) coroutine.yield(1) error('late') end\n"
        "function spin() coroutine.yield(1) while true do end end\n"
        "function steps(k, n) for r = 1, k do for i = 1, n do end coroutine.yield(r) end return 'steps' end\n"
        "function hy() return yield() + 1 end\n"
        "function tab() coroutine.yield({x = 1}, coroutine.create(gen)) end\n"
        "function running() return coroutine.running() end\n"
        "function dead() local co = coroutine.create(function() end) coroutine.resume(co) return co end\n"
        "function failed() local co = coroutine.create(function() error('x') end) coroutine.resume(co) return co end\n"
        "function drive() return coresume() end\n"
        "function drivemany() while true do coresume() end end\n"
        "function selfdrive() coroutine.yield(1) return coresume() end\n"
        "function eat() coroutine.yield(1) local t = {} for i = 1, 64 do t[i] = string.rep('x', 1 << 20) .. i end end\n";
    host h;
    int32_t status = -1, co, fn, other;
    xlua_core_call_t c;
    xlua_value args[2], r[4], v;
    xlua_core_stats st;
    lua_State *L;
    memset(&h, 0, sizeof(h));
    L = open_state(32 << 20, 0, &h, &status);
    CHECK(L != NULL);
    if (L == NULL) {
        return;
    }
    h.L = L;
    xlua_core_set_dispatcher(host_dispatch);
    reg(L, "yield", FN_YIELD, 0);
    reg(L, "coresume", FN_CORESUME, 0);
    expect_run(L, 0, setup, OK, "nil", "the coroutine functions");

    /* Start, resume to the end, and a resume of a dead coroutine. */
    set_int(&args[0], 2);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "gen", args, 1, r, 4, &c) == YIELD && c.fn > 3 && !(c.flags & CALLF_DEAD));
    co = c.fn;
    CHECK(c.nresults == 2 && r[0].type == T_INT && r[0].u.i == 1 && r[1].type == T_STR && has(r[1].u.s, r[1].len, "v1"));
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == YIELD && r[0].u.i == 2 && c.fn == co);
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == OK && (c.flags & CALLF_DEAD) && c.nresults == 1
          && r[0].type == T_STR && has(r[0].u.s, r[0].len, "done"));
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRHANDLE && (c.flags & CALLF_DEAD)
          && has(c.error, c.error_len, "cannot resume dead coroutine"));
    CHECK(xlua_core_unref(L, co) == 1 && xlua_core_unref(L, co) == 0);
    /* A function handle; the resume values are yield's results. */
    CHECK(get_field(L, 0, "gen", &v) == OK && v.type == T_FUNC);
    fn = (int32_t)v.u.i;
    set_int(&args[0], 1);
    CHECK(call(L, CALL_START | CALL_REF, 0, NULL, args, 1, r, 4, &c) == ERRHANDLE && c.fn == 0);  /* c.fn 0 is no function */
    memset(&c, 0, sizeof(c));
    c.kind = CALL_START | CALL_REF;
    c.fn = fn;
    c.args = args;
    c.nargs = 1;
    c.results = r;
    c.results_cap = 4;
    CHECK(xlua_core_call(L, &c) == YIELD && c.fn > 3 && c.fn != fn && r[0].u.i == 1);
    co = c.fn;
    CHECK(xlua_core_unref(L, fn) == 1);
    CHECK(xlua_core_unref(L, co) == 1);  /* released while suspended: collected */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "nope", NULL, 0, r, 4, &c) == ERRRUN && c.fn == 0
          && has(c.error, c.error_len, "attempt to call a nil value (global 'nope')"));
    CHECK(call(L, CALL_START | CALL_CHUNK, 0, "x", NULL, 0, r, 4, &c) == ERRCONFIG && c.fn == 0);
    CHECK(call(L, 6, 0, "x", NULL, 0, r, 4, &c) == ERRCONFIG);

    /* Errors: the coroutine's traceback, the error value; a failed start still made a coroutine. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "fail", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRRUN && (c.flags & CALLF_DEAD)
          && has(c.error, c.error_len, ":2: boom") && has(c.traceback, c.traceback_len, "in function <")
          && has(c.traceback, c.traceback_len, "'error'"));
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRHANDLE && has(c.error, c.error_len, "dead"));
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == ERRRUN && has(c.error, c.error_len, "boom"));  /* its own error again */
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "failt", NULL, 0, r, 4, &c) == ERRRUN && c.fn > 3
          && (c.flags & CALLF_ERROR_TABLE) && (c.flags & CALLF_DEAD) && c.error_ref > 3);
    CHECK(xlua_core_unref(L, c.fn) == 1 && xlua_core_unref(L, c.error_ref) == 1);

    /* Close: pending __close runs, with the coroutine's error for a failed one. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "closer", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == OK && (c.flags & CALLF_DEAD) && c.nresults == 0);
    expect_run(L, 0, "return closed", OK, "true", "__close ran");
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == OK);  /* closed: nothing to do */
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRHANDLE);
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "closefail", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == ERRRUN && has(c.error, c.error_len, "in close"));
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "faultclose", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRRUN);
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == ERRRUN);
    expect_run(L, 0, "return closed", OK, ":6: late", "a failed coroutine's __close gets its error");
    CHECK(xlua_core_unref(L, co) == 1);

    /* A yield from a host function: its results are yielded, the resume values its results. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "hy", NULL, 0, r, 4, &c) == YIELD && c.nresults == 1 && r[0].u.i == 8);
    co = c.fn;
    set_int(&args[0], 41);
    CHECK(co_step(L, CALL_RESUME, co, args, 1, r, 4, &c) == OK && r[0].u.i == 42);
    CHECK(xlua_core_unref(L, co) == 1);

    /* Coroutines as values: handles with their status. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "tab", NULL, 0, r, 4, &c) == YIELD && c.nresults == 2);
    co = c.fn;
    CHECK(r[0].type == T_TABLE && r[1].type == T_THREAD && r[1].len == CO_SUSPENDED && r[1].u.i > 3);
    other = (int32_t)r[1].u.i;
    CHECK(xlua_core_unref(L, (int32_t)r[0].u.i) == 1);
    set_int(&args[0], 1);
    CHECK(co_step(L, CALL_RESUME, other, args, 1, r, 4, &c) == YIELD && r[0].u.i == 1);  /* a script's coroutine, started */
    CHECK(co_step(L, CALL_RESUME, other, NULL, 0, r, 4, &c) == OK && (c.flags & CALLF_DEAD));
    args[0].type = T_THREAD;
    args[0].len = 0;
    args[0].u.i = other;
    CHECK(call(L, CALL_GLOBAL, 0, "coroutine.status", args, 1, r, 1, &c) == OK && has(r[0].u.s, r[0].len, "dead"));
    CHECK(xlua_core_unref(L, other) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "coroutine.status", args, 1, r, 1, &c) == ERRHANDLE);  /* released */
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "running", NULL, 0, r, 1, &c) == OK && r[0].type == T_THREAD && r[0].len == CO_RUNNING);
    co = (int32_t)r[0].u.i;
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRHANDLE && !(c.flags & CALLF_DEAD)
          && has(c.error, c.error_len, "non-suspended"));
    CHECK(co_step(L, CALL_CLOSE, co, NULL, 0, r, 4, &c) == ERRHANDLE && has(c.error, c.error_len, "running"));
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "dead", NULL, 0, r, 1, &c) == OK && r[0].type == T_THREAD && r[0].len == CO_FINISHED);
    CHECK(xlua_core_unref(L, (int32_t)r[0].u.i) == 1);
    CHECK(call(L, CALL_GLOBAL, 0, "failed", NULL, 0, r, 1, &c) == OK && r[0].type == T_THREAD && r[0].len == CO_FAILED);
    CHECK(xlua_core_unref(L, (int32_t)r[0].u.i) == 1);
    CHECK(get_field(L, 0, "gen", &v) == OK);
    CHECK(co_step(L, CALL_RESUME, (int32_t)v.u.i, NULL, 0, r, 4, &c) == ERRHANDLE && has(c.error, c.error_len, "invalid coroutine handle"));
    CHECK(co_step(L, CALL_RESUME, 0, NULL, 0, r, 4, &c) == ERRHANDLE);
    CHECK(xlua_core_unref(L, (int32_t)v.u.i) == 1);

    /* The limits per step: the full budget each, the stop kept inside the step. */
    CHECK(xlua_core_set_limits(L, 100000, 0, SMALL_SLICES) == 1);
    set_int(&args[0], 10);
    set_int(&args[1], 20000);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "steps", args, 2, r, 4, &c) == YIELD && c.instructions > 20000
          && c.instructions < 100000);
    co = c.fn;
    {
        int steps = 1, s;
        int64_t total = c.instructions;
        while ((s = co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c)) == YIELD) {
            steps++;
            total += c.instructions;
        }
        CHECK(s == OK && steps == 10 && total > 200000 && has(r[0].u.s, r[0].len, "steps"));
    }
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "spin", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == LIMIT_INSTR && (c.flags & CALLF_DEAD)
          && has(c.traceback, c.traceback_len, "in function <"));
    CHECK(xlua_core_get_stats(L, &st) == 1 && st.stop_reason == LIMIT_INSTR);
    CHECK(xlua_core_unref(L, co) == 1);
    set_int(&args[0], 1);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "gen", args, 1, r, 4, &c) == YIELD);  /* the stop ended with its step */
    CHECK(xlua_core_unref(L, c.fn) == 1);
    /* A resume nested in a host function shares the outer call's budget. */
    set_int(&args[0], 1000000000);
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "gen", args, 1, r, 4, &c) == YIELD);
    h.co = c.fn;
    CHECK(call(L, CALL_GLOBAL, 0, "drive", NULL, 0, r, 1, &c) == OK && h.co_status == YIELD && r[0].u.i == 2);
    CHECK(call(L, CALL_GLOBAL, 0, "drivemany", NULL, 0, r, 1, &c) == LIMIT_INSTR);
    CHECK(xlua_core_unref(L, h.co) == 1);
    /* ... and cannot resume the coroutine that runs it. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "selfdrive", NULL, 0, r, 4, &c) == YIELD);
    h.co = c.fn;
    CHECK(co_step(L, CALL_RESUME, h.co, NULL, 0, r, 4, &c) == OK && h.co_status == ERRHANDLE);
    CHECK(xlua_core_unref(L, h.co) == 1);
    CHECK(xlua_core_set_limits(L, 0, 0, 0) == 1);
    /* The memory limit: a refusal in a step is the step's memory error. */
    CHECK(call(L, CALL_START | CALL_GLOBAL, 0, "eat", NULL, 0, r, 4, &c) == YIELD);
    co = c.fn;
    CHECK(co_step(L, CALL_RESUME, co, NULL, 0, r, 4, &c) == ERRMEM && (c.flags & CALLF_DEAD) && c.traceback == NULL);
    CHECK(xlua_core_unref(L, co) == 1);
    CHECK(lua_gettop(L) == 0);
    xlua_core_close(L);
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
    check_trusted_bytecode();
    check_host();
    check_buffer();
    check_host_buffer();
    check_tables();
    check_coroutines();
    run_cases();
    free(text);
    if (failures) {
        fprintf(stderr, "%d failures\n", failures);
        return 1;
    }
    printf("XLUA_CORE_SMOKE_PASS: %d cases, %d modules\n", ncases, nmodules);
    return 0;
}
