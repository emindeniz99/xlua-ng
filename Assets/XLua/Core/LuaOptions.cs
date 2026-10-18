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

    /// <summary>
    /// Options for a new Lua state: <c>new LuaEnv(new LuaOptions { ... })</c>.
    /// The constructor copies them; later changes to this object do not
    /// reach a state that already exists. <c>new LuaOptions()</c> sets no limit,
    /// so such an environment behaves like <c>new LuaEnv()</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Libraries"/> and <see cref="CheckThread"/> apply to
    /// LuaScript only for now (docs/design/core-luascript.md, section 2.14):
    /// <see cref="LuaEnv"/> refuses a library selection and has no thread check.
    /// </remarks>
    public sealed class LuaOptions
    {
        /// <summary>
        /// What <c>new LuaScript()</c> uses: a 64 MiB <see cref="MemoryLimit"/>
        /// and a 1 s <see cref="TimeLimit"/>, the rest as in <c>new LuaOptions()</c>
        /// (the owner's decision 2). Every read returns a new object.
        /// </summary>
        public static LuaOptions ScriptDefaults
        {
            get { return new LuaOptions { MemoryLimit = 64L << 20, TimeLimit = TimeSpan.FromSeconds(1) }; }
        }

        /// <summary>
        /// The standard libraries of a LuaScript; <see cref="LuaLibs.Default"/>
        /// (0) is <see cref="LuaLibs.Safe"/> as far as the native library has
        /// them. The base library is always open. LuaScript refuses
        /// <see cref="LuaLibs.Io"/>, <see cref="LuaLibs.Os"/>, <see cref="LuaLibs.Debug"/>
        /// and <see cref="LuaLibs.Package"/>. LuaEnv accepts only Default.
        /// </summary>
        public LuaLibs Libraries { get; set; }

        bool checkThread = true;

        /// <summary>
        /// Whether a LuaScript checks that it is used from the thread that
        /// created it (default true); its members then throw
        /// <see cref="InvalidOperationException"/> on another thread, except
        /// Interrupt, IsDisposed and the statistics.
        /// </summary>
        public bool CheckThread
        {
            get { return checkThread; }
            set { checkThread = value; }
        }

        long memoryLimit;

        /// <summary>
        /// The most bytes the Lua state may hold; 0 (the default) is unlimited.
        /// A Lua allocation that would go over it fails with Lua's
        /// "not enough memory" error, which a script can catch with
        /// <c>pcall</c>; uncaught, it reaches C# as a <see cref="LuaException"/>.
        /// While a C# function called from Lua runs, its allocations in the
        /// state are never refused; the error is raised when it returns. The
        /// limit applies from the end of <see cref="LuaEnv"/>'s constructor,
        /// so it covers what scripts allocate beyond the environment itself.
        /// Not available on LuaJIT (<see cref="NotSupportedException"/>).
        /// </summary>
        public long MemoryLimit
        {
            get { return memoryLimit; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", value, "MemoryLimit must be 0 (unlimited) or more");
                }
                memoryLimit = value;
            }
        }

        /// <summary>
        /// The seed of Lua's string hash. Null (the default) takes a random
        /// seed for every state, as Lua does. A fixed seed makes the order of
        /// <c>pairs</c> and <c>next</c> over string and number keys the same
        /// in every state and process for the same insertion history; tables,
        /// functions and userdata as keys still hash by address, and
        /// <c>math.random</c> seeds itself. Lua 5.4 and 5.5 only; Lua 5.3 and
        /// LuaJIT throw <see cref="NotSupportedException"/>.
        /// </summary>
        public uint? HashSeed { get; set; }

        long instructionLimit;
        TimeSpan timeLimit;

        /// <summary>
        /// The most Lua VM instructions one call from C# into Lua may run; 0
        /// (the default) is unlimited. A call is every outermost entry: a
        /// <c>DoString</c>, a <c>LuaFunction.Call</c>, a delegate bound to a
        /// Lua function; Lua code that a C# callback calls again runs within
        /// the outer call's budget. The budget counts across coroutines, and
        /// the string pattern functions and <c>table.move</c> charge their
        /// work too. Over the limit the call fails with
        /// <see cref="LuaInstructionLimitException"/>; a script cannot catch the
        /// error and go on. Costs a per-instruction hook while a limited call
        /// runs. Lua 5.4 and 5.5 only.
        /// </summary>
        public long InstructionLimit
        {
            get { return instructionLimit; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", value, "InstructionLimit must be 0 (unlimited) or more");
                }
                instructionLimit = value;
            }
        }

        /// <summary>
        /// The longest wall-clock time one call from C# into Lua may run
        /// (calls as for <see cref="InstructionLimit"/>); <see cref="TimeSpan.Zero"/>
        /// (the default) is unlimited. A watchdog thread stops the call with
        /// <see cref="LuaTimeLimitException"/> a few milliseconds after the
        /// deadline, and the instruction hook and the string and table
        /// functions check the deadline as well. A C# function called from Lua
        /// is not interrupted; the call stops when it returns. On WebGL, which
        /// has no threads, the hook checks the clock every 10,000 instructions.
        /// Lua 5.4 and 5.5 only.
        /// </summary>
        public TimeSpan TimeLimit
        {
            get { return timeLimit; }
            set
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException("value", value, "TimeLimit must be TimeSpan.Zero (unlimited) or more");
                }
                timeLimit = value;
            }
        }

        internal LuaOptions Clone()
        {
            return (LuaOptions)MemberwiseClone();
        }
    }

    /// <summary>The standard libraries of <see cref="LuaOptions.Libraries"/> (core-luascript.md, section 2.14).</summary>
    [Flags]
    public enum LuaLibs
    {
        /// <summary>LuaScript: <see cref="Safe"/>. LuaEnv: every library, as before.</summary>
        Default = 0,
        Coroutine = 1 << 0,
        String = 1 << 1,
        Table = 1 << 2,
        Math = 1 << 3,
        Utf8 = 1 << 4,
        /// <summary>Luau's buffer library: not built yet (milestone 10).</summary>
        Buffer = 1 << 5,
        /// <summary>LuaEnv only.</summary>
        Io = 1 << 6,
        /// <summary>LuaEnv only.</summary>
        Os = 1 << 7,
        /// <summary>LuaEnv only.</summary>
        Debug = 1 << 8,
        /// <summary>LuaEnv only.</summary>
        Package = 1 << 9,
        Safe = Coroutine | String | Table | Math | Utf8,
    }
}
