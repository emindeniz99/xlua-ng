/* How late the time limit and Interrupt stop a running call, with and without
 * an instruction limit (docs/design/core-luascript.md, sections 3.6, 8.11 and
 * 8.14; docs/research/core-stop-latency.md).
 *
 * Every run calls one endless chunk under a time limit or interrupts it from
 * a second thread, and measures how long after the deadline or the interrupt
 * the call returns. A thread polls xlua_core_watch for the time-limit cases as
 * Assets/XLua/Core/LuaWatchdog.cs does (every quarter of the limit, 1 to
 * 20 ms); an Interrupt case calls xlua_core_interrupt, as LuaScript.Interrupt
 * and LuaEnv.Interrupt do, without a watchdog (the C# watchdog polls only
 * states with a time limit). Each case runs with no instruction limit and with
 * limits far above what the call can run; "interrupt-unlimited" interrupts a
 * call without any limit, which both entries allow with XLUA_LIMITS_INTERRUPT
 * (LuaOptions.AllowInterrupt: on by default for LuaScript, off for LuaEnv, so
 * the pcall entry sets the flag for this case only).
 *
 * xlua_core_interrupt returns 0 while no call runs. On a loaded machine the
 * call may not have begun its entry 20 ms after the run started, and an
 * Interrupt then does nothing (section 8.14): the interrupting thread tries
 * again every 0.2 ms until it returns 1, and the latency counts from that
 * call. The "early" column counts the runs where the first try came too
 * soon; --interrupt-once tries once, as the tests did before section 8.14, and
 * reports such a run as LOST.
 *
 * Every late run is printed with the CPU time the running thread got after the
 * deadline or the interrupt: a stop that the running thread did not see takes
 * as much CPU as wall time, a thread that the machine did not run takes
 * almost none (the stop was on time, the scheduler was not).
 *
 * Entries:
 *   --entry core   xlua_core_open + xlua_core_call (LuaScript; a core library or
 *                  a bridge library built with XLUA_LUASCRIPT=ON);
 *   --entry pcall  xlua_core_newstate + luaopen_xlua + xluaL_loadbuffer +
 *                  lua_pcall (LuaEnv.DoString with LuaOptions; a bridge
 *                  library with the Lua API exported).
 *
 * Fails (exit 1) when a stop is later than --slack-ms after the deadline or
 * the interrupt, or a call ends with another status; a call that runs for
 * --cap-ms is reported as never stopped and ends the program (exit 3), since
 * nothing can stop it. The library is loaded with dlopen, so one binary
 * serves every build.
 *
 * With a library built with -DXLUA_LIMITS_TEST_POINTS (xlua_core_limits.c),
 * --point NAME[:N] holds the thread that reaches the point NAME for the N-th
 * time in a run (default 1) for --pause-ms; an Interrupt case then interrupts
 * while the running thread is held there instead of after --interrupt-ms (the
 * remote points are on the interrupting thread, which they hold instead).
 *
 * Build: cc -std=c17 -O2 Test/Native/core_stop_latency.c -o core_stop_latency -lpthread (-ldl on Linux)
 * Run:   ./core_stop_latency /abs/libxlua.dylib --entry core --runs 20
 * Options: --runs N (20), --limit-ms T (50), --interrupt-ms I (20), --slack-ms S (50),
 *          --cap-ms C (10000), --kinds time,interrupt,interrupt-watched,interrupt-unlimited
 *          (default: all), --instructions 0,3e9,1e11 (default), --codes 0,...,9 (default: 0-8),
 *          --interrupt-once 1, --demote main|helpers|all (a lower thread priority),
 *          --point NAME[:N] --pause-ms P (2)
 */
#define _GNU_SOURCE
#define _DARWIN_C_SOURCE
#include <dlfcn.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <sys/resource.h>
#if defined(__APPLE__)
#include <mach/mach.h>
#include <pthread/qos.h>
#else
#include <sys/syscall.h>
#endif

typedef struct lua_State lua_State;
typedef int (*lua_CFunction)(lua_State *L);

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

typedef struct xlua_core_frame {
    lua_State *L;
    const xlua_value *args;
    xlua_value *results;
    const char *error;
    int32_t nargs, results_cap, nresults, status, error_len, error_arg, yieldable, id;
} xlua_core_frame;

typedef void (*xlua_core_dispatch)(void *host, int32_t fn_id, xlua_core_frame *frame);

enum { LIMIT_INSTR = 10, LIMIT_TIME = 11, INTERRUPTED = 12, CALL_GLOBAL = 0, CALL_CHUNK = 2, LIMITS_INTERRUPT = 4 };
enum { FRAME_ERROR = 2, FN_HOST = 1, FN_REENTER = 2 };

static lua_State *(*core_open)(const xlua_core_config *, void *, int32_t *);
static lua_State *(*core_newstate)(const xlua_core_config *, int32_t *);
static void (*core_close)(lua_State *);
static int (*core_set_limits)(lua_State *, int64_t, int64_t, int32_t);
static int (*core_interrupt)(lua_State *);
static int64_t (*core_watch)(lua_State *);
static int (*core_get_stats)(lua_State *, xlua_core_stats *);
static int (*core_call)(lua_State *, xlua_core_call_t *);
static void (*core_set_dispatcher)(xlua_core_dispatch);
static int (*core_register)(lua_State *, int32_t, const char *, int32_t, int32_t, int32_t *);
static void (*core_debug_state)(lua_State *, char *, int);  /* test-point builds */
static void (*l_openlibs)(lua_State *);
static int (*l_openxlua)(lua_State *);
static int (*l_loadstring)(lua_State *, const char *);
static int (*x_loadbuffer)(lua_State *, const char *, int, const char *);  /* LuaEnv's load entry */
static int (*l_pcall)(lua_State *, int, int, int);  /* xlua.c's lua_pcall, LuaEnv's outermost entry */
static void (*l_settop)(lua_State *, int);
static void (*l_pushcclosure)(lua_State *, lua_CFunction, int);
static void (*l_setglobal)(lua_State *, const char *);
static int (*l_getglobal)(lua_State *, const char *);
static void (*l_xmove)(lua_State *, lua_State *, int);
static int (*l_error)(lua_State *);

/* The endless chunks: a plain loop, a loop of calls, a loop in a coroutine,
 * table and arithmetic work, endless tail calls (no loop at all), a loop over
 * two lines, a loop of host function calls (a registered host function in
 * LuaScript, a C function in LuaEnv), the same in a coroutine, and a coroutine
 * whose host function calls back into Lua (code 8): LuaEnv's LuaFunction.Call
 * and delegates call on the main thread (luaEnv.L) while the coroutine waits
 * in the host function; LuaScript calls on the coroutine's thread. Code 9 is
 * the callback without the coroutine. Code 10 compiles for about 100 ms before
 * it loops: an Interrupt or a deadline during the compile (LuaEnv.DoString's
 * own entry, xluaL_loadbuffer) must stop the call too. */
static const char *codes[] = {
    "while true do end",
    "local function f() end while true do f() end",
    "coroutine.wrap(function() while true do end end)()",
    "local t, i = {}, 0 while true do i = i + 1 t[i % 64 + 1] = i * 2 end",
    "local function f(n) return f(n + 1) end return f(0)",
    "local n = 0\nwhile true do\n  n = n + 1\nend",
    "while true do host() end",
    "coroutine.wrap(function() while true do host() end end)()",
    "inner = function() while true do end end coroutine.wrap(function() reenter() end)()",
    "inner = function() while true do end end reenter()",
    NULL,  /* code 10: a chunk that takes about 100 ms to compile, then loops (built in main) */
};
#define LONG_CHUNK_CODE 10
#define NCODES ((int)(sizeof(codes) / sizeof(codes[0])))
#define DEFAULT_CODES "0,1,2,3,4,5,6,7,8"

enum { KIND_TIME, KIND_INTERRUPT, KIND_INTERRUPT_WATCHED, KIND_INTERRUPT_UNLIMITED, NKINDS };
static const char *const kind_names[NKINDS] = {"time", "interrupt", "interrupt-watched", "interrupt-unlimited"};

/* xlua_core_limits.c's test points (XLUA_LIMITS_TEST_POINTS) */
static const char *const point_names[] = {"", "begin", "end", "hook", "hook-raise", "switch", "check-stop",
                                          "remote-mask", "remote-traps"};
#define NPOINTS ((int)(sizeof(point_names) / sizeof(point_names[0])))
#define FIRST_REMOTE_POINT 7

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

/* CPU time of the thread that runs the calls, readable from any thread. */
#if defined(__APPLE__)
static mach_port_t g_main_port;
static int64_t main_cpu_ns(void) {
    thread_basic_info_data_t info;
    mach_msg_type_number_t n = THREAD_BASIC_INFO_COUNT;
    if (thread_info(g_main_port, THREAD_BASIC_INFO, (thread_info_t)&info, &n) != KERN_SUCCESS) {
        return 0;
    }
    return ((int64_t)info.user_time.seconds + info.system_time.seconds) * 1000000000
           + ((int64_t)info.user_time.microseconds + info.system_time.microseconds) * 1000;
}
#else
static clockid_t g_main_clock;
static int64_t main_cpu_ns(void) {
    struct timespec ts;
    if (clock_gettime(g_main_clock, &ts) != 0) {
        return 0;
    }
    return (int64_t)ts.tv_sec * 1000000000 + ts.tv_nsec;
}
#endif

/* --demote: the utility QoS class on macOS (lower priority, mostly the
 * efficiency cores; the background class starves a thread on a loaded
 * machine), nice 10 elsewhere. */
static void demote_self(void) {
#if defined(__APPLE__)
    pthread_set_qos_class_self_np(QOS_CLASS_UTILITY, 0);
#else
    setpriority(PRIO_PROCESS, (id_t)syscall(SYS_gettid), 10);
#endif
}

/* The state of the running call, shared with the helper threads (atomics, so
 * that ThreadSanitizer reports only the library's own races). */
static lua_State *g_L;                  /* set before the threads start */
static _Atomic int64_t g_start;         /* start of the running call, 0: none */
static _Atomic int64_t g_interrupt_at;  /* when to interrupt (absolute), 0: never */
static _Atomic int64_t g_deadline;      /* the time limit's deadline, to sample the CPU time, 0: none */
static _Atomic int64_t g_poll_ns;       /* the watchdog's period, 0: no watchdog */
static _Atomic int64_t g_mark_ns;       /* when the interrupt was accepted or the deadline passed */
static _Atomic int64_t g_mark_cpu;      /* the running thread's CPU time then */
static _Atomic int g_early;             /* tries before the call ran */
static _Atomic int g_tries_held;        /* tries while the running thread was held at the test point */
static _Atomic int g_once;              /* --interrupt-once */
static _Atomic int g_quit;
static _Atomic(const char *) g_case = "";
static _Atomic int g_run;
static int64_t g_cap_ns = 10000LL * 1000000;
static pthread_mutex_t g_mutex = PTHREAD_MUTEX_INITIALIZER;  /* held around the call's begin and end */
static pthread_t g_main;
static int g_demote_helpers;

/* --point: the point, the hit that pauses, the hits so far in this run, and
 * the pause; g_held is set while a thread is held at the point. */
static int g_point, g_point_n = 1;
static _Atomic int g_point_hits;
static _Atomic int g_held;
static int64_t g_pause_ns = 2000000;

static void test_point(int point) {
    int remote = point >= FIRST_REMOTE_POINT;
    if (point != g_point || g_start == 0 || remote != !pthread_equal(pthread_self(), g_main)) {
        return;
    }
    if (atomic_fetch_add(&g_point_hits, 1) + 1 != g_point_n) {
        return;
    }
    g_held = 1;
    sleep_ns(g_pause_ns);
    g_held = 0;
}

static void *watchdog_main(void *arg) {
    (void)arg;
    if (g_demote_helpers) {
        demote_self();
    }
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

/* Interrupts at g_interrupt_at (or while the running thread is held at a
 * running-side test point), and samples the running thread's CPU time when
 * the interrupt is accepted or the deadline passes. */
static void *interrupter_main(void *arg) {
    (void)arg;
    if (g_demote_helpers) {
        demote_self();
    }
    while (!g_quit) {
        int64_t at = g_interrupt_at, deadline = g_deadline, t = now_ns();
        int at_point = g_point != 0 && g_point < FIRST_REMOTE_POINT;
        /* at a running-side test point: while the running thread is held there,
         * or at the usual time when it has not got there by then */
        if (at != 0 && (at_point ? g_held != 0 || t >= at : t >= at)) {
            pthread_mutex_lock(&g_mutex);
            if (g_interrupt_at == at && g_start != 0) {
                g_tries_held += g_held != 0;
                if (core_interrupt(g_L)) {
                    g_mark_ns = now_ns();
                    g_mark_cpu = main_cpu_ns();
                    g_interrupt_at = 0;
                }
                else {
                    g_early++;  /* the call has not begun its entry */
                    if (g_once) {
                        g_interrupt_at = 0;
                    }
                }
            }
            pthread_mutex_unlock(&g_mutex);
        }
        if (deadline != 0 && t >= deadline && g_mark_ns == 0) {
            g_mark_cpu = main_cpu_ns();
            g_mark_ns = deadline;
        }
        sleep_ns(at_point && at != 0 ? 20000 : 200000);
    }
    return NULL;
}

static void *monitor_main(void *arg) {
    (void)arg;
    while (!g_quit) {
        int64_t start = g_start;
        if (start != 0 && now_ns() - start > g_cap_ns) {
            int64_t mark = g_mark_ns, cpu = main_cpu_ns() - g_mark_cpu;
            printf("NEVER_STOPPED case %s run %d: the call ran for %lld ms and nothing stopped it (%s; the running"
                   " thread got %.1f ms of CPU since then)\n", atomic_load(&g_case), atomic_load(&g_run),
                   (long long)(g_cap_ns / 1000000), mark != 0 ? "the interrupt or deadline came" : "no interrupt was accepted",
                   mark != 0 ? (double)cpu / 1e6 : -1.0);
            /* a test-point build tells the native state; CSL_SAMPLE=file samples the stacks (macOS) */
            for (int k = 0; k < 3; k++) {
                if (core_debug_state != NULL) {
                    char state[768];
                    core_debug_state(g_L, state, (int)sizeof(state));
                    printf("  state: %s\n", state);
                }
                printf("  the running thread's CPU time: %.1f ms\n", (double)main_cpu_ns() / 1e6);
                sleep_ns(200000000);
            }
            if (getenv("CSL_SAMPLE") != NULL) {
                char cmd[512];
                snprintf(cmd, sizeof(cmd), "sample %d 1 -mayDie > '%s' 2>&1", (int)getpid(), getenv("CSL_SAMPLE"));
                fflush(stdout);
                if (system(cmd) != 0) {
                    printf("  sample failed\n");
                }
            }
            fflush(stdout);
            _exit(3);
        }
        sleep_ns(50000000);
    }
    return NULL;
}

/* Host functions: host() returns nothing; reenter() calls the global
 * function "inner" from inside the host function, as a C# callback does. */
static void dispatch(void *ud, int32_t id, xlua_core_frame *f) {
    (void)ud;
    f->nresults = 0;
    if (id == FN_REENTER) {
        xlua_core_call_t c;
        memset(&c, 0, sizeof(c));
        c.kind = CALL_GLOBAL;
        c.name = "inner";
        c.name_len = 5;
        if (core_call(g_L, &c) != 0) {  /* LuaScript calls on the host function's thread */
            f->status = FRAME_ERROR;
            f->error = c.error != NULL ? c.error : "inner failed";
            f->error_len = (int32_t)strlen(f->error);
        }
    }
}

static int host_c(lua_State *L) {
    (void)L;
    return 0;
}

static int reenter_c(lua_State *co) {
    /* LuaEnv's LuaFunction.Call and DelegateBridge use luaEnv.L, the main
     * thread, also while a coroutine runs the C# function. */
    l_getglobal(g_L, "inner");
    if (l_pcall(g_L, 0, 0, 0) != 0) {
        if (co != g_L) {
            l_xmove(g_L, co, 1);
        }
        return l_error(co);
    }
    return 0;
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
static int run_call(lua_State *L, int pcall, const char *code) {
    if (pcall) {
        int status;
        xlua_core_stats st;
        status = x_loadbuffer != NULL ? x_loadbuffer(L, code, (int)strlen(code), "latency") : l_loadstring(L, code);
        if (status == 0) {
            status = l_pcall(L, 0, 0, 0);
        }
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
    const char *instr_list = "0,3e9,1e11", *code_list = DEFAULT_CODES, *demote = "none", *point = NULL;
    int runs = 20, pcall, i, failures = 0, worst_case_ms = 0, lost_total = 0, early_total = 0;
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
    setvbuf(stdout, NULL, _IOLBF, 0);
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
        else if (strcmp(argv[i], "--interrupt-once") == 0) g_once = atoi(argv[i + 1]) != 0;
        else if (strcmp(argv[i], "--demote") == 0) demote = argv[i + 1];
        else if (strcmp(argv[i], "--point") == 0) point = argv[i + 1];
        else if (strcmp(argv[i], "--pause-ms") == 0) g_pause_ns = (int64_t)(atof(argv[i + 1]) * 1e6);
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
    {
        /* code 10: "do end" compiles to nothing, so the chunk costs only parse time */
        static const char unit[] = "do end ", tail[] = "while true do end";
        size_t n = 2000000, u = sizeof(unit) - 1;
        char *text = (char *)malloc(n * u + sizeof(tail)), *p = text;
        for (size_t j = 0; j < n; j++, p += u) {
            memcpy(p, unit, u);
        }
        memcpy(p, tail, sizeof(tail));
        codes[LONG_CHUNK_CODE] = text;
    }
    g_main = pthread_self();
#if defined(__APPLE__)
    g_main_port = pthread_mach_thread_np(g_main);
#else
    pthread_getcpuclockid(g_main, &g_main_clock);
#endif
    if (strcmp(demote, "main") == 0 || strcmp(demote, "all") == 0) {
        demote_self();
    }
    g_demote_helpers = strcmp(demote, "helpers") == 0 || strcmp(demote, "all") == 0;

    lib = dlopen(libpath, RTLD_NOW | RTLD_LOCAL);
    if (lib == NULL) {
        fprintf(stderr, "dlopen: %s\n", dlerror());
        return 2;
    }
    if (point != NULL) {
        void (*set_point)(void (*)(int)) = (void (*)(void (*)(int)))sym(lib, "xlua_core_set_test_point", 0);
        const char *colon = strchr(point, ':');
        size_t len = colon != NULL ? (size_t)(colon - point) : strlen(point);
        for (i = 1; i < NPOINTS; i++) {
            if (strlen(point_names[i]) == len && strncmp(point_names[i], point, len) == 0) {
                g_point = i;
            }
        }
        g_point_n = colon != NULL ? atoi(colon + 1) : 1;
        if (set_point == NULL || g_point == 0) {
            fprintf(stderr, set_point == NULL ? "--point needs a library built with -DXLUA_LIMITS_TEST_POINTS\n"
                                              : "unknown test point %s\n", point);
            return 2;
        }
        set_point(test_point);
    }
    core_close = (void (*)(lua_State *))sym(lib, "xlua_core_close", 1);
    core_set_limits = (int (*)(lua_State *, int64_t, int64_t, int32_t))sym(lib, "xlua_core_set_limits", 1);
    core_interrupt = (int (*)(lua_State *))sym(lib, "xlua_core_interrupt", 1);
    core_watch = (int64_t (*)(lua_State *))sym(lib, "xlua_core_watch", 1);
    core_get_stats = (int (*)(lua_State *, xlua_core_stats *))sym(lib, "xlua_core_get_stats", 1);
    core_debug_state = (void (*)(lua_State *, char *, int))sym(lib, "xlua_core_debug_state", 0);
    memset(&cfg, 0, sizeof(cfg));
    if (pcall) {
        core_newstate = (lua_State * (*)(const xlua_core_config *, int32_t *)) sym(lib, "xlua_core_newstate", 1);
        l_openlibs = (void (*)(lua_State *))sym(lib, "luaL_openlibs", 1);
        l_openxlua = (int (*)(lua_State *))sym(lib, "luaopen_xlua", 1);
        l_loadstring = (int (*)(lua_State *, const char *))sym(lib, "luaL_loadstring", 1);
        x_loadbuffer = (int (*)(lua_State *, const char *, int, const char *))sym(lib, "xluaL_loadbuffer", 0);
        l_pcall = (int (*)(lua_State *, int, int, int))sym(lib, "lua_pcall", 1);
        l_settop = (void (*)(lua_State *, int))sym(lib, "lua_settop", 1);
        l_pushcclosure = (void (*)(lua_State *, lua_CFunction, int))sym(lib, "lua_pushcclosure", 1);
        l_setglobal = (void (*)(lua_State *, const char *))sym(lib, "lua_setglobal", 1);
        l_getglobal = (int (*)(lua_State *, const char *))sym(lib, "lua_getglobal", 1);
        l_xmove = (void (*)(lua_State *, lua_State *, int))sym(lib, "lua_xmove", 1);
        l_error = (int (*)(lua_State *))sym(lib, "lua_error", 1);
        L = core_newstate(&cfg, &status);
        if (L != NULL) {
            l_openlibs(L);
            l_openxlua(L);
            l_settop(L, 0);
            l_pushcclosure(L, host_c, 0);
            l_setglobal(L, "host");
            l_pushcclosure(L, reenter_c, 0);
            l_setglobal(L, "reenter");
        }
    }
    else {
        int32_t old;
        core_open = (lua_State * (*)(const xlua_core_config *, void *, int32_t *)) sym(lib, "xlua_core_open", 1);
        core_call = (int (*)(lua_State *, xlua_core_call_t *))sym(lib, "xlua_core_call", 1);
        core_set_dispatcher = (void (*)(xlua_core_dispatch))sym(lib, "xlua_core_set_dispatcher", 1);
        core_register = (int (*)(lua_State *, int32_t, const char *, int32_t, int32_t, int32_t *))sym(lib, "xlua_core_register", 1);
        cfg.sandbox = 1;
        L = core_open(&cfg, NULL, &status);
        if (L != NULL) {
            core_set_dispatcher(dispatch);
            core_register(L, 0, "host", 4, FN_HOST, &old);
            core_register(L, 0, "reenter", 7, FN_REENTER, &old);
        }
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
    printf("entry %s, %d runs per case, time limit %lld ms, interrupt at %lld ms, bound +%lld ms%s%s%s\n", entry, runs,
           (long long)limit_ms, (long long)interrupt_ms, (long long)slack_ms, g_once ? ", one interrupt" : "",
           point != NULL ? ", test point " : "", point != NULL ? point : "");
    printf("%-20s %-12s %-5s %8s %8s %8s %8s %6s %5s\n", "kind", "instr.limit", "code", "min", "p50", "p90", "max", "late",
           "early");
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
            time_ms = k == KIND_TIME ? limit_ms : k == KIND_INTERRUPT_WATCHED ? 60000 : 0;
            target_ms = k == KIND_TIME ? limit_ms : interrupt_ms;
            for (int ci = 0; ci < NCODES; ci++) {
                char name[64], idx[8];
                int late = 0, bad = 0, early = 0, held = 0;
                snprintf(idx, sizeof(idx), "%d", ci);
                if (!has_item(code_list, idx)) {
                    continue;
                }
                snprintf(name, sizeof(name), "%s/%g/%d", kind_names[k], (double)instr, ci);
                g_case = name;
                /* LuaScript passes XLUA_LIMITS_INTERRUPT by default; LuaEnv only with
                 * LuaOptions.AllowInterrupt = true, which a call without limits needs */
                core_set_limits(L, instr, time_ms * 1000000,
                                !pcall || k == KIND_INTERRUPT_UNLIMITED ? LIMITS_INTERRUPT : 0);
                /* LuaWatchdog: a quarter of the shortest limit, 1 to 20 ms; only states with a time limit */
                g_poll_ns = time_ms == 0 ? 0 : (time_ms / 4 < 1 ? 1 : time_ms / 4 > 20 ? 20 : time_ms / 4) * 1000000;
                for (int r = 0; r < runs; r++) {
                    int64_t t0, t1, mark, cpu;
                    int st, run_early, expect = k == KIND_TIME ? LIMIT_TIME : INTERRUPTED;
                    g_run = r;
                    pthread_mutex_lock(&g_mutex);
                    g_mark_ns = 0;
                    g_mark_cpu = 0;
                    g_early = 0;
                    g_tries_held = 0;
                    g_point_hits = 0;
                    g_held = 0;
                    t0 = now_ns();
                    g_start = t0;
                    g_deadline = k == KIND_TIME ? t0 + limit_ms * 1000000 : 0;
                    g_interrupt_at = k == KIND_TIME ? 0 : t0 + interrupt_ms * 1000000;
                    pthread_mutex_unlock(&g_mutex);
                    st = run_call(L, pcall, codes[ci]);
                    t1 = now_ns();
                    cpu = main_cpu_ns();
                    pthread_mutex_lock(&g_mutex);
                    g_start = 0;
                    g_interrupt_at = 0;
                    g_deadline = 0;
                    mark = g_mark_ns;
                    cpu -= g_mark_cpu;
                    run_early = g_early;
                    held += g_tries_held != 0;
                    pthread_mutex_unlock(&g_mutex);
                    /* from the accepted interrupt or the deadline; without one, from the planned moment */
                    lat[r] = mark != 0 ? t1 - mark : (t1 - t0) - target_ms * 1000000;
                    early += run_early != 0;
                    if (st != expect) {
                        bad++;
                        if (k != KIND_TIME && mark == 0) {
                            lost_total++;
                            printf("  LOST %s run %d: status %d after %.1f ms; the interrupt came before the call began"
                                   " (xlua_core_interrupt returned 0)\n", name, r, st, (double)(t1 - t0) / 1e6);
                        }
                        else {
                            printf("  %s run %d: status %d, expected %d (after %.1f ms)\n", name, r, st, expect,
                                   (double)(t1 - t0) / 1e6);
                        }
                    }
                    else if (lat[r] > slack_ms * 1000000) {
                        late++;
                        printf("  LATE %s run %d: %.1f ms after the %s; the running thread got %.1f ms of CPU in that time\n",
                               name, r, (double)lat[r] / 1e6, k == KIND_TIME ? "deadline" : "interrupt",
                               mark != 0 ? (double)cpu / 1e6 : -1.0);
                    }
                }
                early_total += early;
                qsort(lat, (size_t)runs, sizeof(int64_t), cmp_i64);
                printf("%-20s %-12g %-5d %8.1f %8.1f %8.1f %8.1f %3d/%-3d %5d%s\n", kind_names[k], (double)instr, ci,
                       (double)lat[0] / 1e6, (double)lat[runs / 2] / 1e6, (double)lat[(runs * 9) / 10 < runs ? (runs * 9) / 10 : runs - 1] / 1e6,
                       (double)lat[runs - 1] / 1e6, late, runs, early, late || bad ? "  FAIL" : "");
                if (g_point != 0 && g_point < FIRST_REMOTE_POINT && k != KIND_TIME) {
                    printf("  (%d of %d interrupts while the running thread was held at the test point)\n", held, runs);
                }
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
    printf("runs where the first interrupt came before the call began: %d (lost: %d)\n", early_total, lost_total);
    if (failures) {
        printf("XLUA_CORE_STOP_LATENCY_FAIL: %d late or wrong stops, the latest %d ms after its deadline\n", failures,
               worst_case_ms);
        return 1;
    }
    printf("XLUA_CORE_STOP_LATENCY_PASS (latest stop %d ms after its deadline)\n", worst_case_ms);
    return 0;
}
