// LuaEnv(LuaOptions): the memory limit, the overdraft rule for C# callbacks,
// the error reserve and the hash seed (docs/design/core-luascript.md, sections
// 3.4 and 3.10, milestone 2). Lua cases: StreamingAssets/luaCallCsCoverage.lua
// (CaseLuaOptions*). Each method builds its own environments and throws on a
// failure. The native side of the same rules is check_core_state in
// Test/Native/native_regression.py.
using System;
using XLua;

namespace Coverage
{
    // [LuaCallCSharp], so the generated-code suites call BigString through a
    // generated wrapper (GEN_CODE_MINIMIZE: the native wrapper trampoline).
    [LuaCallCSharp]
    public static class LuaOptionsHelper
    {
        // A string Lua receives from C#: pushing it allocates in the state
        // while the C# function runs.
        public static string BigString(int length)
        {
            return new string('b', length);
        }
    }

    public static class LuaOptionsApi
    {
        const int Reserve = 16 * 1024;

        static void expect(bool ok, string what)
        {
            if (!ok)
            {
                throw new Exception("check failed: " + what);
            }
        }

        static T throws<T>(Action action, string what) where T : Exception
        {
            try
            {
                action();
            }
            catch (T e)
            {
                return e;
            }
            throw new Exception("expected " + typeof(T).Name + ": " + what);
        }

        // "LuaJIT", "Lua 5.3", "Lua 5.4" or "Lua 5.5", from a plain environment.
        static string Runtime()
        {
            using (var env = new LuaEnv())
            {
                return (string)env.DoString("return jit and 'LuaJIT' or _VERSION")[0];
            }
        }

        static void Dispose(LuaEnv env)
        {
            env.FullGc();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            env.Tick();
            env.Dispose();
        }

        public static void Basics()
        {
            throws<ArgumentOutOfRangeException>(() => new LuaOptions().MemoryLimit = -1, "negative MemoryLimit");
            using (var legacy = new LuaEnv())
            {
                expect(legacy.MemoryLimit == 0, "a plain LuaEnv reports no limit");
                throws<InvalidOperationException>(() => legacy.MemoryLimit = 1 << 20, "limit on a plain LuaEnv");
                throws<InvalidOperationException>(() => { var x = legacy.MemoryUsed; }, "MemoryUsed of a plain LuaEnv");
                throws<InvalidOperationException>(() => { var x = legacy.MemoryPeak; }, "MemoryPeak of a plain LuaEnv");
            }
            using (var none = new LuaEnv(null))
            {
                expect(Convert.ToInt64(none.DoString("return 1 + 1")[0]) == 2, "LuaEnv(null)");
            }

            var options = new LuaOptions();
            var env = new LuaEnv(options);
            try
            {
                options.MemoryLimit = 1;  // copied at construction: no effect
                expect(env.MemoryLimit == 0, "options are copied");
                expect(Convert.ToDouble(env.DoString("return CS.System.Math.Abs(-3.5)")[0]) == 3.5, "the bridge works");
                var e = throws<LuaException>(() => env.DoString("error('plain error')"), "a runtime error");
                expect(e.Message.Contains("plain error") && e.Message.Contains("stack traceback"), e.Message);
            }
            finally
            {
                if (Runtime() == "LuaJIT")
                {
                    expect(env.MemoryUsed == 0 && env.MemoryPeak == 0, "LuaJIT has no statistics");
                    throws<NotSupportedException>(() => env.MemoryLimit = 1 << 20, "MemoryLimit on LuaJIT");
                }
                else
                {
                    expect(env.MemoryUsed > 0 && env.MemoryPeak >= env.MemoryUsed, "statistics");
                }
                Dispose(env);
            }
        }

        // A LuaEnv whose limit leaves 'room' bytes above what it holds now. The
        // helper type is imported first: importing it builds its wrappers in
        // the state, which is more than some of the tests leave room for.
        static LuaEnv Limited(long room)
        {
            var env = new LuaEnv(new LuaOptions());
            env.DoString("local _ = CS.Coverage.LuaOptionsHelper.BigString(1)");
            env.FullGc();
            env.MemoryLimit = env.MemoryUsed + room;
            return env;
        }

        // The chunk fails with Lua's memory error. With checkPeak, the state
        // never held more than the limit plus the error reserve.
        static void ExpectNoMemory(LuaEnv env, string chunk, string what, bool checkPeak = true)
        {
            var e = throws<LuaException>(() => env.DoString(chunk), what);
            expect(e.Message.Contains("not enough memory"), what + ": " + e.Message);
            expect(!checkPeak || env.MemoryPeak <= env.MemoryLimit + Reserve,
                what + ": peak " + env.MemoryPeak + " over " + env.MemoryLimit);
        }

        public static void MemoryLimit()
        {
            string runtime = Runtime();
            if (runtime == "LuaJIT")
            {
                throws<NotSupportedException>(() => new LuaEnv(new LuaOptions { MemoryLimit = 1 << 20 }), "LuaJIT");
                return;
            }
            var env = new LuaEnv(new LuaOptions { MemoryLimit = 64L << 20 });
            try
            {
                expect(env.MemoryLimit == 64L << 20, "MemoryLimit from the options");
                expect(Convert.ToInt64(env.DoString("return #('x'):rep(1000)")[0]) == 1000, "work under the limit");
            }
            finally
            {
                Dispose(env);
            }

            env = Limited(1 << 20);
            try
            {
                ExpectNoMemory(env, "local s = '' while true do s = s .. ('x'):rep(1000) end", "string growth");
                env.FullGc();
                ExpectNoMemory(env, "local t = {} for i = 1, 1e8 do t[i] = i end", "table growth");
                env.FullGc();
                ExpectNoMemory(env, "local s = ('x'):rep(1 << 30)", "one large string");
                env.FullGc();
                // pcall catches the error; after freeing, the script finishes normally.
                object[] r = env.DoString(@"
                    local t = {}
                    local ok, err = pcall(function() for i = 1, 1e8 do t[i] = {} end end)
                    t = nil
                    collectgarbage()
                    return ok, err, #('y'):rep(1000)");
                expect(!(bool)r[0] && (string)r[1] == "not enough memory" && Convert.ToInt64(r[2]) == 1000, "pcall catches it");

                // The emergency collection: garbage near the limit is collected
                // when an allocation is refused, and the retry succeeds. The big
                // allocation is a table's array, which grows through Lua's
                // allocation path; string.rep's buffer calls the allocator
                // directly and gets no emergency collection.
                env.FullGc();
                r = env.DoString(@"
                    collectgarbage('stop')
                    local limit = " + env.MemoryLimit + @"
                    local function garbage() local s = ('g'):rep(1000) .. collectgarbage('count') end
                    while collectgarbage('count') * 1024 < limit - 96 * 1024 do garbage() end
                    local before = collectgarbage('count')
                    local big = {}
                    for i = 1, 16384 do big[i] = i end
                    local after = collectgarbage('count')
                    collectgarbage('restart')
                    return #big, before, after");
                expect(Convert.ToInt64(r[0]) == 16384, "the retry after the emergency collection succeeded");
                expect(Convert.ToDouble(r[2]) < Convert.ToDouble(r[1]), "the emergency collection freed the garbage");

                // A limit below the current use: the next growth fails, raising it again works.
                long used = env.MemoryUsed;
                env.MemoryLimit = 1;
                expect(env.MemoryLimit == 1, "lowered limit");
                ExpectNoMemory(env, "return {}", "below the use", false);
                env.MemoryLimit = used + (1 << 20);
                expect(Convert.ToInt64(env.DoString("return #{1, 2, 3}")[0]) == 3, "raised again");
                expect(env.MemoryPeak >= env.MemoryUsed, "MemoryPeak");
            }
            finally
            {
                Dispose(env);
            }
        }

        public static void Overdraft()
        {
            if (Runtime() == "LuaJIT")
            {
                return;
            }
            var env = Limited(256 * 1024);
            try
            {
                // BigString's 1 MB result is pushed while C# runs: granted over the
                // limit, then raised as Lua's memory error once BigString returned.
                object[] r = env.DoString(@"
                    local ok, err = pcall(CS.Coverage.LuaOptionsHelper.BigString, 1 << 20)
                    local small = CS.Coverage.LuaOptionsHelper.BigString(10)
                    return ok, err, small");
                expect(!(bool)r[0] && (string)r[1] == "not enough memory", "overdraft raised: " + r[1]);
                expect((string)r[2] == "bbbbbbbbbb", "the next call works");
                ExpectNoMemory(env, "local s = CS.Coverage.LuaOptionsHelper.BigString(1 << 20)", "uncaught overdraft", false);
                env.FullGc();
                expect(Convert.ToInt64(env.DoString("return #CS.Coverage.LuaOptionsHelper.BigString(100)")[0]) == 100, "usable afterwards");
            }
            finally
            {
                Dispose(env);
            }
        }

        public static void ErrorReserve()
        {
            if (Runtime() == "LuaJIT")
            {
                return;
            }
            var env = Limited(512 * 1024);
            try
            {
                // Fill the state up to the limit in small, reachable steps; the
                // traceback of the error then needs the reserve.
                var e = throws<LuaException>(() => env.DoString(@"
                    local msg, head = 'boom at the limit', nil
                    pcall(function() while true do head = {head} end end)
                    error(msg, 0)"), "error at the limit");
                expect(e.Message.Contains("boom at the limit") && e.Message.Contains("stack traceback"), e.Message);
            }
            finally
            {
                Dispose(env);
            }
        }

        static string PairsOrder(uint? seed)
        {
            var env = new LuaEnv(new LuaOptions { HashSeed = seed });
            try
            {
                return (string)env.DoString(@"
                    local t = {}
                    for i = 1, 64 do t['key' .. i] = i end
                    local keys = {}
                    for k in pairs(t) do keys[#keys + 1] = k end
                    return table.concat(keys, ',')")[0];
            }
            finally
            {
                Dispose(env);
            }
        }

        public static void HashSeed()
        {
            string runtime = Runtime();
            if (runtime == "LuaJIT" || runtime == "Lua 5.3")
            {
                throws<NotSupportedException>(() => new LuaEnv(new LuaOptions { HashSeed = 42 }), runtime);
                return;
            }
            string first = PairsOrder(42);
            expect(first == PairsOrder(42), "the same seed gives the same order");
            expect(first != PairsOrder(43), "another seed gives another order");
            PairsOrder(null);
        }
    }
}
