/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// How soon TimeLimit and Interrupt stop a running call, with and without an
// InstructionLimit (docs/design/core-luascript.md, section 8.11;
// docs/research/core-stop-latency.md). Before the fix of 2026-10-18 an
// instruction limit made both wait for the end of a 2^30-instruction count
// slice: a 50 ms time limit stopped after up to 2 s, Interrupt after 2 to 5 s,
// and Interrupt did nothing on a script without limits. Test/Native/
// core_stop_latency.c measures the same with many more runs.
using System;
using System.Diagnostics;
using System.Threading;
using XLua;

public static partial class CoreTests
{
    // A stop may come this late after its deadline: the watchdog's period (a
    // quarter of the limit, 12.5 ms) and the wake-up of a busy machine (single
    // stops of 40 ms seen while other tests ran). Before the fix the stops came
    // 0.1 to 5 s late; in the run before the fix every case had a stop more
    // than 170 ms late. XLUA_STOP_SLACK_MS raises the bound up to 1000 ms
    // (Test/Native/stop_latency.py reads the same variable): native-build.yml
    // sets 250 for hosted runners, where single stops came 101 to 110 ms late
    // (2026-10-18). A lost stop (2 to 5 s late before the fix) still fails.
    static readonly int StopSlackMs = ReadStopSlackMs();

    static int ReadStopSlackMs()
    {
        string text = (Environment.GetEnvironmentVariable("XLUA_STOP_SLACK_MS") ?? "").Trim();
        if (text.Length == 0)
        {
            return 100;
        }
        int ms;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ms)
            || ms < 100 || ms > 1000)
        {
            Console.WriteLine("XLUA_STOP_SLACK_MS must be an integer from 100 to 1000, not '" + text + "'");
            Environment.Exit(2);
        }
        return ms;
    }

    // Interrupts from another thread 20 ms after the running call reached the host
    // function started(). Interrupt does nothing before the call has begun
    // (core-luascript.md, section 8.14), and on a loaded machine the call may begin
    // more than 20 ms after the thread started: one run of TestInterruptIsOnTime
    // lost its Interrupt that way, and the 30 s time limit ended the call.
    sealed class Interrupter
    {
        readonly ManualResetEventSlim started = new ManualResetEventSlim(false);
        Stopwatch watch;
        Thread thread;
        long interruptedAt;

        // The body of the host function started().
        public void Started() { started.Set(); }

        public void Begin(Action interrupt)
        {
            started.Reset();
            interruptedAt = 0;
            watch = Stopwatch.StartNew();
            thread = new Thread(() =>
            {
                if (!started.Wait(60000))
                {
                    return;
                }
                Thread.Sleep(20);
                interrupt();
                Interlocked.Exchange(ref interruptedAt, watch.ElapsedMilliseconds);
            });
            thread.Start();
        }

        // After the call: how long after the Interrupt it ended.
        public long End()
        {
            long ended = watch.ElapsedMilliseconds;
            started.Set();  // a call that never reached started(): the thread interrupts nothing
            thread.Join();
            return ended - Interlocked.Read(ref interruptedAt);
        }
    }

    static Interrupter WithStarted(LuaScript lua)
    {
        var stopper = new Interrupter();
        lua.Register("started", (ref LuaCall c) => stopper.Started());
        return stopper;
    }

    static readonly string[] EndlessCodes =
    {
        "while true do end",
        "local function f() end while true do f() end",
        "coroutine.wrap(function() while true do end end)()",
        "local function f(n) return f(n + 1) end return f(0)",
        // Loops in C whose length __len chooses: only the C budget stops them (section 8.12).
        "table.remove(setmetatable({}, {__len = function() return math.maxinteger - 1 end}), 1)",
        "table.sort(setmetatable({}, {__len = function() return 2^31 - 2 end, __index = rawlen, __newindex = rawequal}))",
    };

    public static void TestTimeLimitIsOnTimeWithAnInstructionLimit()
    {
        foreach (long instructions in new[] { 0L, 3000000000L, 100000000000L })
        {
            using (var lua = new LuaScript(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(50), InstructionLimit = instructions }))
            {
                for (int c = 0; c < EndlessCodes.Length; c++)
                {
                    long worst = 0;
                    for (int run = 0; run < 6; run++)
                    {
                        var watch = Stopwatch.StartNew();
                        Throws<LuaTimeLimitException>(() => lua.Load(EndlessCodes[c], "endless"), "the time limit, code " + c);
                        worst = Math.Max(worst, watch.ElapsedMilliseconds - 50);
                    }
                    Check(worst <= StopSlackMs, "InstructionLimit " + instructions + ", code " + c
                        + ": a 50 ms time limit stopped the call " + worst + " ms late");
                }
            }
        }
    }

    public static void TestInterruptIsOnTime()
    {
        var limits = new[]
        {
            new LuaOptions(),  // no limit: Interrupt needs XLUA_LIMITS_INTERRUPT
            new LuaOptions { InstructionLimit = 3000000000L },
            new LuaOptions { InstructionLimit = 100000000000L },
            new LuaOptions { TimeLimit = TimeSpan.FromSeconds(30) },  // the watchdog polls
            new LuaOptions { TimeLimit = TimeSpan.FromSeconds(30), InstructionLimit = 100000000000L },
        };
        foreach (var options in limits)
        {
            string what = "InstructionLimit " + options.InstructionLimit + ", TimeLimit " + options.TimeLimit;
            // Without a limit nothing else stops the call: a loop that ends on its
            // own after seconds, so that a lost Interrupt fails the check instead
            // of hanging the run.
            bool unlimited = options.InstructionLimit == 0 && options.TimeLimit == TimeSpan.Zero;
            var codes = unlimited ? new[] { "for i = 1, 3e9 do end" } : EndlessCodes;
            using (var lua = new LuaScript(options))
            {
                var stopper = WithStarted(lua);
                for (int c = 0; c < codes.Length; c++)
                {
                    string code = "started() " + codes[c];
                    long worst = 0;
                    for (int run = 0; run < 6; run++)
                    {
                        stopper.Begin(lua.Interrupt);
                        Throws<LuaInterruptedException>(() => lua.Load(code, "endless"), what + ", code " + c);
                        worst = Math.Max(worst, stopper.End());
                    }
                    Check(worst <= StopSlackMs, what + ", code " + c + ": Interrupt stopped the call " + worst + " ms late");
                }
            }
        }
    }

    // Interrupt on a call that the switch leaves alone: a loop that ends by itself
    // (0.1 to 1 s) runs to its end although Interrupt comes after 20 ms.
    static void RunsToItsEnd(Func<string, long> run, Action interrupt, string what)
    {
        var t = new Thread(() =>
        {
            Thread.Sleep(20);
            interrupt();
        });
        t.Start();
        long n = run("local n = 0 for i = 1, 3e7 do n = n + 1 end return n");
        t.Join();
        Check(n == 30000000, what + ": the call ran to its end");
    }

    // run calls started() first (Interrupter).
    static void InterruptedAfter20Ms(Interrupter stopper, Action run, Action interrupt, string what)
    {
        stopper.Begin(interrupt);
        Throws<LuaInterruptedException>(run, what);
        stopper.End();
    }

    // LuaOptions.AllowInterrupt (owner, 2026-10-18): null and true keep LuaScript's
    // Interrupt without limits; false leaves the entry protocol out of calls without
    // limits (no 6 ns per call), and Interrupt then reaches only calls with a limit.
    public static void TestAllowInterruptSwitch()
    {
        Check(new LuaOptions().AllowInterrupt == null && LuaOptions.ScriptDefaults.AllowInterrupt == null, "the default is null");
        using (var lua = new LuaScript(new LuaOptions { AllowInterrupt = true }))
        {
            InterruptedAfter20Ms(WithStarted(lua), () => lua.Load("started() while true do end", "endless"), lua.Interrupt,
                "AllowInterrupt true, no limits");
        }
        using (var lua = new LuaScript(new LuaOptions { AllowInterrupt = false }))
        {
            var stopper = WithStarted(lua);
            RunsToItsEnd(code => lua.Load<long>(code, "loop"), lua.Interrupt, "AllowInterrupt false, no limits");
            lua.InstructionLimit = 100000000000L;  // a limit: Interrupt works again
            InterruptedAfter20Ms(stopper, () => lua.Load("started() while true do end", "endless"), lua.Interrupt,
                "AllowInterrupt false with a limit");
            lua.InstructionLimit = 0;
            RunsToItsEnd(code => lua.Load<long>(code, "loop"), lua.Interrupt, "AllowInterrupt false, limit removed");
        }
        using (var lua = new LuaScript(new LuaOptions { AllowInterrupt = false, TimeLimit = TimeSpan.FromSeconds(30) }))
        {
            InterruptedAfter20Ms(WithStarted(lua), () => lua.Load("started() while true do end", "endless"), lua.Interrupt,
                "AllowInterrupt false with a time limit");
        }
    }

#if XLUA_CORE_TEST_BRIDGE
    // LuaEnv(LuaOptions): AllowInterrupt is off by default there and true turns it on.
    public static void TestLuaEnvAllowInterrupt()
    {
        using (var env = new LuaEnv(new LuaOptions { AllowInterrupt = true }))
        {
            var stopper = WithStarted(env);
            for (int c = 0; c < EndlessCodes.Length; c++)
            {
                string code = "started() " + EndlessCodes[c];
                InterruptedAfter20Ms(stopper, () => env.DoString(code), env.Interrupt, "LuaEnv, AllowInterrupt true, code " + c);
            }
        }
        using (var env = new LuaEnv(new LuaOptions()))
        {
            RunsToItsEnd(code => Convert.ToInt64(env.DoString(code)[0]), env.Interrupt, "LuaEnv, default options");
        }
    }

    // LuaEnv(LuaOptions) uses the same native limits through lua_pcall.
    public static void TestLuaEnvStopsOnTimeWithAnInstructionLimit()
    {
        using (var env = new LuaEnv(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(50), InstructionLimit = 100000000000L }))
        {
            for (int c = 0; c < EndlessCodes.Length; c++)
            {
                long worst = 0;
                for (int run = 0; run < 6; run++)
                {
                    var watch = Stopwatch.StartNew();
                    Throws<LuaTimeLimitException>(() => env.DoString(EndlessCodes[c]), "LuaEnv, the time limit, code " + c);
                    worst = Math.Max(worst, watch.ElapsedMilliseconds - 50);
                }
                Check(worst <= StopSlackMs, "LuaEnv, code " + c + ": a 50 ms time limit stopped the call " + worst + " ms late");
            }
            env.TimeLimit = TimeSpan.Zero;  // LuaEnv's Interrupt needs a limit: the instruction limit stays
            var stopper = WithStarted(env);
            for (int c = 0; c < EndlessCodes.Length; c++)
            {
                stopper.Begin(env.Interrupt);
                Throws<LuaInterruptedException>(() => env.DoString("started() " + EndlessCodes[c]), "LuaEnv, Interrupt, code " + c);
                long late = stopper.End();
                Check(late <= StopSlackMs, "LuaEnv, code " + c + ": Interrupt stopped the call " + late + " ms late");
            }
        }
    }

    static Interrupter WithStarted(LuaEnv env)
    {
        var stopper = new Interrupter();
        env.Global.Set("started", (Action)stopper.Started);
        return stopper;
    }

    // A coroutine's C# function that calls back into Lua: LuaFunction.Call runs on the
    // main thread (luaEnv.L) while the coroutine waits in the C# function. Before
    // the fix of core-luascript.md, section 8.14, the stop, the remote arming and the
    // instruction count went to the waiting coroutine, and neither Interrupt, the
    // time limit nor the instruction limit stopped that call. The loop is bounded,
    // so that a lost stop fails the test instead of hanging it.
    public static void TestLuaEnvStopsACallbackInACoroutine()
    {
        const string Code = "inner = function() for i = 1, 3e9 do end end coroutine.wrap(function() started() reenter() end)()";
        using (var env = new LuaEnv(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(50) }))
        {
            var stopper = WithStarted(env);
            env.Global.Set("reenter", (Action)(() => env.Global.Get<LuaFunction>("inner").Call()));
            var watch = Stopwatch.StartNew();
            Throws<LuaTimeLimitException>(() => env.DoString(Code), "LuaEnv, the time limit in a callback from a coroutine");
            Check(watch.ElapsedMilliseconds - 50 <= StopSlackMs, "the time limit stopped the callback "
                + (watch.ElapsedMilliseconds - 50) + " ms late");
            env.TimeLimit = TimeSpan.Zero;
            env.InstructionLimit = 100000000000L;
            stopper.Begin(env.Interrupt);
            Throws<LuaInterruptedException>(() => env.DoString(Code), "LuaEnv, Interrupt in a callback from a coroutine");
            long late = stopper.End();
            Check(late <= StopSlackMs, "Interrupt stopped the callback " + late + " ms late");
            env.InstructionLimit = 1000000;
            Throws<LuaInstructionLimitException>(() => env.DoString(Code), "LuaEnv, the instruction limit counts the callback");
        }
    }
#endif
}
