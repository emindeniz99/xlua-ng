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
    // than 170 ms late.
    const int StopSlackMs = 100;

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
                for (int c = 0; c < codes.Length; c++)
                {
                    string code = codes[c];
                    long worst = 0;
                    for (int run = 0; run < 6; run++)
                    {
                        long interruptedAt = 0;
                        var watch = Stopwatch.StartNew();
                        var t = new Thread(() =>
                        {
                            Thread.Sleep(20);
                            lua.Interrupt();
                            Interlocked.Exchange(ref interruptedAt, watch.ElapsedMilliseconds);
                        });
                        t.Start();
                        Throws<LuaInterruptedException>(() => lua.Load(code, "endless"), what + ", code " + c);
                        long ended = watch.ElapsedMilliseconds;
                        t.Join();
                        worst = Math.Max(worst, ended - Interlocked.Read(ref interruptedAt));
                    }
                    Check(worst <= StopSlackMs, what + ", code " + c + ": Interrupt stopped the call " + worst + " ms late");
                }
            }
        }
    }

#if XLUA_CORE_TEST_BRIDGE
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
            for (int c = 0; c < EndlessCodes.Length; c++)
            {
                long interruptedAt = 0;
                var watch = Stopwatch.StartNew();
                var t = new Thread(() =>
                {
                    Thread.Sleep(20);
                    env.Interrupt();
                    Interlocked.Exchange(ref interruptedAt, watch.ElapsedMilliseconds);
                });
                t.Start();
                Throws<LuaInterruptedException>(() => env.DoString(EndlessCodes[c]), "LuaEnv, Interrupt, code " + c);
                long ended = watch.ElapsedMilliseconds;
                t.Join();
                long late = ended - Interlocked.Read(ref interruptedAt);
                Check(late <= StopSlackMs, "LuaEnv, code " + c + ": Interrupt stopped the call " + late + " ms late");
            }
        }
    }
#endif
}
