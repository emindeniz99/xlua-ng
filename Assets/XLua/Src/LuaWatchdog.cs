/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

namespace XLua
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>
    /// One background thread per process that stops calls past their
    /// <see cref="LuaOptions.TimeLimit"/> (docs/design/core-luascript.md,
    /// section 3.6). The native side keeps each call's deadline; the thread
    /// asks every state with a time limit (xlua_core_watch), which arms the
    /// stop hook under the state's lock when the deadline has passed. It wakes
    /// every quarter of the shortest time limit, between 1 and 20 ms, and
    /// sleeps while no state has a time limit. Removing a state waits for a
    /// running check, so the state can be closed right after.
    /// </summary>
    internal static class LuaWatchdog
    {
        static readonly object gate = new object();
        static readonly Dictionary<IntPtr, TimeSpan> states = new Dictionary<IntPtr, TimeSpan>();
        static Thread thread;

        /// <summary>
        /// Tests: the WebGL fallback on any platform, where the instruction hook
        /// reads the clock every 10,000 instructions instead.
        /// </summary>
        internal static bool ForceClockCheck;

        /// <summary>False where threads are not available (WebGL).</summary>
        internal static bool Available
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return !ForceClockCheck;
#endif
            }
        }

        internal static void Add(IntPtr L, TimeSpan timeLimit)
        {
            lock (gate)
            {
                states[L] = timeLimit;
                if (thread == null)
                {
                    thread = new Thread(Run);
                    thread.IsBackground = true;
                    thread.Name = "xLua watchdog";
                    thread.Start();
                }
                Monitor.PulseAll(gate);
            }
        }

        internal static void Remove(IntPtr L)
        {
            lock (gate)
            {
                states.Remove(L);
            }
        }

        static void Run()
        {
            lock (gate)
            {
                while (true)
                {
                    if (states.Count == 0)
                    {
                        Monitor.Wait(gate);
                        continue;
                    }
                    TimeSpan shortest = TimeSpan.MaxValue;
                    foreach (var pair in states)
                    {
                        LuaAPI.xlua_core_watch(pair.Key);
                        if (pair.Value < shortest)
                        {
                            shortest = pair.Value;
                        }
                    }
                    double wait = Math.Max(1.0, Math.Min(20.0, shortest.TotalMilliseconds / 4));
                    Monitor.Wait(gate, (int)wait);
                }
            }
        }
    }
}
