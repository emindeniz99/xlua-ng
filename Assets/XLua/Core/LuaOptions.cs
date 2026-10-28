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
    /// Shared by <see cref="LuaEnv"/> and LuaScript (docs/design/core-luascript.md,
    /// section 2.14).
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

        bool checkThread = true;

        /// <summary>
        /// Whether a LuaScript checks that it is used from one thread only, the
        /// first that uses it after the constructor (default true); its members then throw
        /// <see cref="InvalidOperationException"/> on another thread, except
        /// Interrupt, IsDisposed and the statistics. <see cref="LuaEnv"/>
        /// checks the same in the Editor, Development builds and XLUA_GENERAL
        /// only, and not with THREAD_SAFE or HOTFIX_ENABLE; false turns its
        /// check off too. SetOwnerThread hands either to another thread.
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

        /// <summary>
        /// Whether <c>Interrupt()</c> can stop a call that runs without an
        /// instruction or time limit. Null (the default) takes the default of
        /// the API: on for a LuaScript, off for a <see cref="LuaEnv"/>. On,
        /// every outermost call records its running thread, about 6 ns per
        /// call from C# and 1 ns per C# function called from Lua (macOS
        /// arm64); off, such a call pays nothing and <c>Interrupt()</c> does
        /// not reach it. While a limit is set, <c>Interrupt()</c> works either
        /// way. True on Lua 5.3 and LuaJIT throws <see cref="NotSupportedException"/>.
        /// </summary>
        public bool? AllowInterrupt { get; set; }

        /// <summary>
        /// The standard libraries the environment opens, within what the native
        /// library contains (CMake <c>XLUA_LIBS</c>): a library the build does
        /// not contain is not opened, whether it is asked for or not.
        /// <see cref="LuaLibs.Default"/> (0) opens every library the build
        /// contains, as <c>new LuaEnv()</c> does; under <c>XLUA_REDUCED_DEBUG</c>
        /// (the minimal preset) a player's default leaves out
        /// <see cref="LuaLibs.Debug"/>. The base library is always open. The
        /// bridge needs two libraries, so a <see cref="LuaEnv"/> keeps a part of
        /// each: without <see cref="LuaLibs.Debug"/>, <c>debug</c> has only
        /// <c>traceback</c> (the error handler's) and <c>getinfo</c>; without
        /// <see cref="LuaLibs.Package"/>, <c>require</c> and xLua's loaders keep
        /// working, but scripts have no <c>package</c> table. Without
        /// <see cref="LuaLibs.String"/> strings also lose their metatable, so
        /// <c>("x"):upper()</c> fails. A removed library is also gone from
        /// <c>package.loaded</c>, so <c>require</c> does not find it.
        /// For a LuaScript, <see cref="LuaLibs.Default"/> is <see cref="LuaLibs.Safe"/>
        /// as far as the native library has it, and only the safe libraries may
        /// be named: io, os, debug, package, bit, jit and ffi are refused.
        /// </summary>
        public LuaLibs Libraries { get; set; }

        internal LuaOptions Clone()
        {
            return (LuaOptions)MemberwiseClone();
        }
    }

    /// <summary>
    /// The standard libraries of <see cref="LuaOptions.Libraries"/>. The bit
    /// values are those of LuaScript's native sandbox (<c>XLUA_LUALIBS_*</c>
    /// in build/xlua_core.h).
    /// </summary>
    [Flags]
    public enum LuaLibs
    {
        /// <summary>LuaEnv: every library the native library contains. LuaScript: <see cref="Safe"/>.</summary>
        Default = 0,
        Coroutine = 1 << 0,
        String = 1 << 1,
        Table = 1 << 2,
        Math = 1 << 3,
        /// <summary>Lua 5.3 and later; LuaJIT has no <c>utf8</c>.</summary>
        Utf8 = 1 << 4,
        /// <summary>LuaScript only (core-luascript.md, milestone 10); a <see cref="LuaEnv"/> ignores it.</summary>
        Buffer = 1 << 5,
        Io = 1 << 6,
        Os = 1 << 7,
        Debug = 1 << 8,
        Package = 1 << 9,
        /// <summary>LuaJIT's <c>bit</c>.</summary>
        Bit = 1 << 10,
        /// <summary>LuaJIT's <c>jit</c>.</summary>
        Jit = 1 << 11,
        /// <summary>LuaJIT's <c>ffi</c>, which <c>require "ffi"</c> loads.</summary>
        Ffi = 1 << 12,
        /// <summary>The libraries that cannot reach the file system, the process or other scripts.</summary>
        Safe = Coroutine | String | Table | Math | Utf8,
        /// <summary>Every library a <see cref="LuaEnv"/> can open.</summary>
        All = Safe | Io | Os | Debug | Package | Bit | Jit | Ffi,
    }
}
