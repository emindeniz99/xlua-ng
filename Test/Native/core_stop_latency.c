/* How late the time limit and Interrupt stop a running call, with and without
 * an instruction limit (docs/design/core-luascript.md, sections 3.6 and 8.11;
 * docs/research/core-stop-latency.md).
 *
 * Every run calls one endless chunk under a time limit or interrupts it from
 * a second thread, and measures how long after the deadline or the interrupt
 * the call returns. A thread polls xlua_core_watch for the time-limit cases as
 * Assets/XLua/Core/LuaWatchdog.cs does (every quarter of the limit, 1 to
 * 20 ms); an Interrupt case calls xlua_core_interrupt once, as
 * LuaScript.Interrupt and LuaEnv.Interrupt do, without a watchdog (the C#
 * watchdog polls only states with a time limit). Each case runs with no
 * instruction limit and with limits far above what the call can run;
 * "interrupt-unlimited" interrupts a call without any limit, which the core
 * entry allows with XLUA_LIMITS_INTERRUPT (LuaScript sets it on every state).
 *
 * Entries:
 *   --entry core   xlua_core_open + xlua_core_call (LuaScript; a core library or
 *                  a bridge library built with XLUA_LUASCRIPT=ON);
 *   --entry pcall  xlua_core_newstate + luaopen_xlua + lua_pcall (LuaEnv with
 *                  LuaOptions; a bridge library with the Lua API exported).
 *
 * Fails (exit 1) when a stop is later than --slack-ms after the deadline or
 * the interrupt, or a call ends with another status; a call that runs for
 * --cap-ms is reported as never stopped and ends the program (exit 3), since
 * nothing can stop it. The library is loaded with dlopen, so one binary
 * serves every build.
 *
 * Build: cc -std=c17 -O2 Test/Native/core_stop_latency.c -o core_stop_latency -lpthread (-ldl on Linux)
 * Run:   ./core_stop_latency /abs/libxlua.dylib --entry core --runs 20
 * Options: --runs N (20), --limit-ms T (50), --interrupt-ms I (20), --slack-ms S (50),
 *          --cap-ms C (10000), --kinds time,interrupt,interrupt-watched,interrupt-unlimited
 *          (default: all), --instructions 0,3e9,1e11 (default), --codes 0,1,2,3,4,5 (default: all)
 */
#define _POSIX_C_SOURCE 200809L
#include <dlfcn.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

typedef struct lua_State lua_State;

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

enum { LIMIT_INSTR = 10, LIMIT_TIME = 11, INTERRUPTED = 12, CALL_CHUNK = 2, LIMITS_INTERRUPT = 4 };

static lua_State *(*core_open)(const xlua_core_config *, void *, int32_t *);
static lua_State *(*core_newstate)(const xlua_core_config *, int32_t *);
static void (*core_close)(lua_State *);
static int (*core_set_limits)(lua_State *, int64_t, int64_t, int32_t);
static int (*core_interrupt)(lua_State *);
static int64_t (*core_watch)(lua_State *);
static int (*core_get_stats)(lua_State *, xlua_core_stats *);
static int (*core_call)(lua_State *, xlua_core_call_t *);
static void (*l_openlibs)(lua_State *);
static int (*l_openxlua)(lua_State *);
static int (*l_loadstring)(lua_State *, const char *);
static int (*l_pcall)(lua_State *, int, int, int);  /* xlua.c's lua_pcall, LuaEnv's outermost entry */
static void (*l_settop)(lua_State *, int);

/* The endless chunks: a plain loop, a loop of calls, a loop in a coroutine,
 * table and arithmetic work, endless tail calls (no loop at all), and a loop
 * over two lines. */
static const char *const codes[] = {
    "while true do end",
    "local function f() end while true do f() end",
    "coroutine.wrap(function() while true do end end)()",
    "local t, i = {}, 0 while true do i = i + 1 t[i % 64 + 1] = i * 2 end",
    "local function f(n) return f(n + 1) end return f(0)",
    "local n = 0\nwhile true do\n  n = n + 1\nend",
};
#define NCODES ((int)(sizeof(codes) / sizeof(codes[0])))

enum { KIND_TIME, KIND_INTERRUPT, KIND_INTERRUPT_WATCHED, KIND_INTERRUPT_UNLIMITED, NKINDS };
static const char *const kind_names[NKINDS] = {"time", "interrupt", "interrupt-watched", "interrupt-unlimited"};

static int64_t now_ns(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000 + ts.tv_nsec;
}

static void sleep_ns(int64_t ns) {
    struct timespec ts;
    if (ns <= 0) {
        return;
    }
    ts.tv_sec = (time_t)(ns / 1000000000);
    ts.tv_nsec = (long)(ns % 1000000000);
    nanosleep(&ts, NULL);
}

/* The state of the running call, shared with the helper threads (atomics, so
 * that ThreadSanitizer reports only the library's own races). */
static lua_State *g_L;                  /* set before the threads start */
static _Atomic int64_t g_start;         /* start of the running call, 0: none */
static _Atomic int64_t g_interrupt_at;  /* when to interrupt (absolute), 0: never */
static _Atomic int64_t g_poll_ns;       /* the watchdog's period, 0: no watchdog */
static _Atomic int g_quit;
static _Atomic(const char *) g_case = "";
static _Atomic int g_run;
static int64_t g_cap_ns = 10000LL * 1000000;
static pthread_mutex_t g_mutex = PTHREAD_MUTEX_INITIALIZER;  /* held around the call's begin and end */

static void *watchdog_main(void *arg) {
    (void)arg;
    while (!g_quit) {
        int64_t poll = g_poll_ns;
        pthread_mutex_lock(&g_mutex);
        if (g_start != 0 && poll != 0) {
            core_watch(g_L);
        }
        pthread_mutex_unlock(&g_mutex);
        sleep_ns(poll != 0 ? poll : 1000000);
    }
    return NULL;
}

static void *interrupter_main(void *arg) {
    (void)arg;
    while (!g_quit) {
        int64_t at = g_interrupt_at;
        if (at != 0 && now_ns() >= at) {
            pthread_mutex_lock(&g_mutex);
            if (g_interrupt_at == at && g_start != 0) {
                core_interrupt(g_L);  /* once, as Interrupt() */
                g_interrupt_at = 0;
            }
            pthread_mutex_unlock(&g_mutex);
        }
        sleep_ns(200000);
    }
    return NULL;
}

static void *monitor_main(void *arg) {
    (void)arg;
    while (!g_quit) {
        int64_t start = g_start;
        if (start != 0 && now_ns() - start > g_cap_ns) {
            printf("NEVER_STOPPED case %s run %d: the call ran for %lld ms and nothing stopped it\n",
                   atomic_load(&g_case), atomic_load(&g_run), (long long)(g_cap_ns / 1000000));
            fflush(stdout);
            _exit(3);
        }
        sleep_ns(50000000);
    }
    return NULL;
}

static void *sym(void *lib, const char *name, int required) {
    void *p = dlsym(lib, name);
    if (p == NULL && required) {
        fprintf(stderr, "missing export %s\n", name);
        exit(2);
    }
    return p;
}

static int cmp_i64(const void *a, const void *b) {
    int64_t x = *(const int64_t *)a, y = *(const int64_t *)b;
    return x < y ? -1 : x > y;
}

static int has_item(const char *list, const char *item) {
    size_t n = strlen(item);
    const char *p = list;
    while ((p = strstr(p, item)) != NULL) {
        if ((p == list || p[-1] == ',') && (p[n] == ',' || p[n] == '\0')) {
            return 1;
        }
        p += n;
    }
    return 0;
}

/* One call of code on L; returns its status (a stop reason for a stopped call). */
static int call_once(lua_State *L, int pcall, const char *code) {
    if (pcall) {
        int status;
        xlua_core_stats st;
        if (l_loadstring(L, code) != 0) {
            fprintf(stderr, "load failed: %s\n", code);
            exit(2);
        }
        status = l_pcall(L, 0, 0, 0);
        l_settop(L, 0);
        if (status == 0) {
            return 0;
        }
        memset(&st, 0, sizeof(st));
        core_get_stats(L, &st);
        return st.stop_reason != 0 ? (int)st.stop_reason : status;
    }
    else {
        xlua_core_call_t c;
        xlua_value result;
        memset(&c, 0, sizeof(c));
        c.kind = CALL_CHUNK;
        c.code = code;
        c.code_len = (int32_t)strlen(code);
        c.name = "latency";
        c.name_len = 7;
        c.results = &result;
        c.results_cap = 1;
        return core_call(L, &c);
    }
}

int main(int argc, char **argv) {
    const char *libpath, *entry = "core", *kinds = "time,interrupt,interrupt-watched,interrupt-unlimited";
    const char *instr_list = "0,3e9,1e11", *code_list = NULL;
    int runs = 20, pcall, i, failures = 0, worst_case_ms = 0;
    int64_t limit_ms = 50, interrupt_ms = 20, slack_ms = 50, instrs[8];
    int ninstrs = 0;
    void *lib;
    lua_State *L;
    pthread_t wd, intr, mon;
    xlua_core_config cfg;
    int32_t status;
    int64_t *lat;
    char *copy, *tok;

    if (argc < 2) {
        fprintf(stderr, "usage: %s /abs/libxlua [--entry core|pcall] [--runs N] ...\n", argv[0]);
        return 2;
    }
    libpath = argv[1];
    for (i = 2; i + 1 < argc; i += 2) {
        if (strcmp(argv[i], "--entry") == 0) entry = argv[i + 1];
        else if (strcmp(argv[i], "--runs") == 0) runs = atoi(argv[i + 1]);
        else if (strcmp(argv[i], "--limit-ms") == 0) limit_ms = atoll(argv[i + 1]);
        else if (strcmp(argv[i], "--interrupt-ms") == 0) interrupt_ms = atoll(argv[i + 1]);
        else if (strcmp(argv[i], "--slack-ms") == 0) slack_ms = atoll(argv[i + 1]);
        else if (strcmp(argv[i], "--cap-ms") == 0) g_cap_ns = atoll(argv[i + 1]) * 1000000;
        else if (strcmp(argv[i], "--kinds") == 0) kinds = argv[i + 1];
        else if (strcmp(argv[i], "--instructions") == 0) instr_list = argv[i + 1];
        else if (strcmp(argv[i], "--codes") == 0) code_list = argv[i + 1];
        else {
            fprintf(stderr, "unknown option %s\n", argv[i]);
            return 2;
        }
    }
    if (i != argc) {
        fprintf(stderr, "option %s needs a value\n", argv[i]);
        return 2;
    }
    pcall = strcmp(entry, "pcall") == 0;
    copy = strdup(instr_list);
    for (tok = strtok(copy, ","); tok != NULL && ninstrs < 8; tok = strtok(NULL, ",")) {
        instrs[ninstrs++] = (int64_t)strtod(tok, NULL);
    }
    free(copy);

    lib = dlopen(libpath, RTLD_NOW | RTLD_LOCAL);
    if (lib == NULL) {
        fprintf(stderr, "dlopen: %s\n", dlerror());
        return 2;
    }
    core_close = (void (*)(lua_State *))sym(lib, "xlua_core_close", 1);
    core_set_limits = (int (*)(lua_State *, int64_t, int64_t, int32_t))sym(lib, "xlua_core_set_limits", 1);
    core_interrupt = (int (*)(lua_State *))sym(lib, "xlua_core_interrupt", 1);
    core_watch = (int64_t (*)(lua_State *))sym(lib, "xlua_core_watch", 1);
    core_get_stats = (int (*)(lua_State *, xlua_core_stats *))sym(lib, "xlua_core_get_stats", 1);
    memset(&cfg, 0, sizeof(cfg));
    if (pcall) {
        core_newstate = (lua_State * (*)(const xlua_core_config *, int32_t *)) sym(lib, "xlua_core_newstate", 1);
        l_openlibs = (void (*)(lua_State *))sym(lib, "luaL_openlibs", 1);
        l_openxlua = (int (*)(lua_State *))sym(lib, "luaopen_xlua", 1);
        l_loadstring = (int (*)(lua_State *, const char *))sym(lib, "luaL_loadstring", 1);
        l_pcall = (int (*)(lua_State *, int, int, int))sym(lib, "lua_pcall", 1);
        l_settop = (void (*)(lua_State *, int))sym(lib, "lua_settop", 1);
        L = core_newstate(&cfg, &status);
        if (L != NULL) {
            l_openlibs(L);
            l_openxlua(L);
            l_settop(L, 0);
        }
    }
    else {
        core_open = (lua_State * (*)(const xlua_core_config *, void *, int32_t *)) sym(lib, "xlua_core_open", 1);
        core_call = (int (*)(lua_State *, xlua_core_call_t *))sym(lib, "xlua_core_call", 1);
        cfg.sandbox = 1;
        L = core_open(&cfg, NULL, &status);
    }
    if (L == NULL) {
        fprintf(stderr, "no state (status %d)\n", (int)status);
        return 2;
    }
    if (core_set_limits(L, 0, 0, 0) == 0) {
        printf("core_stop_latency: no limits in this build\n");
        return 0;
    }
    g_L = L;
    pthread_create(&wd, NULL, watchdog_main, NULL);
    pthread_create(&intr, NULL, interrupter_main, NULL);
    pthread_create(&mon, NULL, monitor_main, NULL);
    lat = (int64_t *)calloc((size_t)runs, sizeof(int64_t));
    printf("entry %s, %d runs per case, time limit %lld ms, interrupt at %lld ms, bound +%lld ms\n", entry, runs,
           (long long)limit_ms, (long long)interrupt_ms, (long long)slack_ms);
    printf("%-20s %-12s %-5s %8s %8s %8s %8s %6s\n", "kind", "instr.limit", "code", "min", "p50", "p90", "max", "late");
    fflush(stdout);
    for (int k = 0; k < NKINDS; k++) {
        if (!has_item(kinds, kind_names[k])) {
            continue;
        }
        for (int ii = 0; ii < (k == KIND_INTERRUPT_UNLIMITED ? 1 : ninstrs); ii++) {
            int64_t instr = k == KIND_INTERRUPT_UNLIMITED ? 0 : instrs[ii];
            int64_t time_ms, target_ms;
            if (k == KIND_INTERRUPT && instr == 0) {
                continue;  /* interrupt-unlimited */
            }
            if (k == KIND_INTERRUPT_UNLIMITED && pcall) {
                printf("%-20s LuaEnv: Interrupt needs a limit (documented), skipped\n", kind_names[k]);
                continue;
            }
            time_ms = k == KIND_TIME ? limit_ms : k == KIND_INTERRUPT_WATCHED ? 60000 : 0;
            target_ms = k == KIND_TIME ? limit_ms : interrupt_ms;
            for (int ci = 0; ci < NCODES; ci++) {
                char name[64], idx[8];
                int late = 0, bad = 0;
                snprintf(idx, sizeof(idx), "%d", ci);
                if (code_list != NULL && !has_item(code_list, idx)) {
                    continue;
                }
                snprintf(name, sizeof(name), "%s/%g/%d", kind_names[k], (double)instr, ci);
                g_case = name;
                /* LuaScript passes XLUA_LIMITS_INTERRUPT on every state; LuaEnv does not */
                core_set_limits(L, instr, time_ms * 1000000, pcall ? 0 : LIMITS_INTERRUPT);
                /* LuaWatchdog: a quarter of the shortest limit, 1 to 20 ms; only states with a time limit */
                g_poll_ns = time_ms == 0 ? 0 : (time_ms / 4 < 1 ? 1 : time_ms / 4 > 20 ? 20 : time_ms / 4) * 1000000;
                for (int r = 0; r < runs; r++) {
                    int64_t t0, t1;
                    int st, expect = k == KIND_TIME ? LIMIT_TIME : INTERRUPTED;
                    g_run = r;
                    pthread_mutex_lock(&g_mutex);
                    t0 = now_ns();
                    g_start = t0;
                    g_interrupt_at = k == KIND_TIME ? 0 : t0 + interrupt_ms * 1000000;
                    pthread_mutex_unlock(&g_mutex);
                    st = call_once(L, pcall, codes[ci]);
                    t1 = now_ns();
                    pthread_mutex_lock(&g_mutex);
                    g_start = 0;
                    g_interrupt_at = 0;
                    pthread_mutex_unlock(&g_mutex);
                    lat[r] = (t1 - t0) - target_ms * 1000000;
                    if (st != expect) {
                        bad++;
                        printf("  %s run %d: status %d, expected %d (after %.1f ms)\n", name, r, st, expect,
                               (double)(t1 - t0) / 1e6);
                    }
                    if (lat[r] > slack_ms * 1000000) {
                        late++;
                    }
                }
                qsort(lat, (size_t)runs, sizeof(int64_t), cmp_i64);
                printf("%-20s %-12g %-5d %8.1f %8.1f %8.1f %8.1f %3d/%-3d%s\n", kind_names[k], (double)instr, ci,
                       (double)lat[0] / 1e6, (double)lat[runs / 2] / 1e6, (double)lat[(runs * 9) / 10 < runs ? (runs * 9) / 10 : runs - 1] / 1e6,
                       (double)lat[runs - 1] / 1e6, late, runs, late || bad ? "  FAIL" : "");
                fflush(stdout);
                if ((int)(lat[runs - 1] / 1000000) > worst_case_ms) {
                    worst_case_ms = (int)(lat[runs - 1] / 1000000);
                }
                failures += late + bad;
            }
        }
    }
    g_quit = 1;
    pthread_join(wd, NULL);
    pthread_join(intr, NULL);
    pthread_join(mon, NULL);
    core_set_limits(L, 0, 0, 0);
    core_close(L);
    free(lat);
    if (failures) {
        printf("XLUA_CORE_STOP_LATENCY_FAIL: %d late or wrong stops, the latest %d ms after its deadline\n", failures,
               worst_case_ms);
        return 1;
    }
    printf("XLUA_CORE_STOP_LATENCY_PASS (latest stop %d ms after its deadline)\n", worst_case_ms);
    return 0;
}
