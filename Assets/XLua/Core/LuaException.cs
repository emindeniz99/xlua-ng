/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using System;

namespace XLua
{
    [Serializable]
    public class LuaException : Exception
    {
        public LuaException(string message) : base(message)
        {}

        /// <summary>A Lua error with Lua's traceback kept apart from the message.</summary>
        public LuaException(string message, string luaStackTrace) : base(message)
        {
            LuaStackTrace = luaStackTrace;
        }

        public LuaException(string message, string luaStackTrace, Exception innerException) : base(message, innerException)
        {
            LuaStackTrace = luaStackTrace;
        }

        /// <summary>
        /// Lua's traceback of the error ("stack traceback: ..."), or null when
        /// none was taken. <see cref="LuaScript"/> sets it for runtime and limit
        /// errors; <see cref="LuaEnv"/> keeps its traceback in the message.
        /// </summary>
        public string LuaStackTrace { get; protected set; }

        public override string ToString()
        {
            return LuaStackTrace == null ? base.ToString() : base.ToString() + Environment.NewLine + LuaStackTrace;
        }
    }

    /// <summary>The limit that stopped a call (<see cref="LuaLimitException"/>).</summary>
    public enum LuaLimit
    {
        Instructions,
        Time,
        Interrupt,
        /// <summary>The memory limit refused an allocation (<see cref="LuaOptions.MemoryLimit"/>).</summary>
        Memory,
    }

    /// <summary>
    /// A call that an instruction or time limit or <see cref="LuaEnv.Interrupt"/>
    /// stopped (<see cref="LuaOptions"/>), or, from <see cref="LuaScript"/>,
    /// that ran out of memory. The message is Lua's; once stopped by an
    /// instruction, time or interrupt limit, the call cannot catch the error
    /// and continue.
    /// </summary>
    [Serializable]
    public abstract class LuaLimitException : LuaException
    {
        protected LuaLimitException(string message, LuaLimit limit) : base(message)
        {
            Limit = limit;
        }

        protected LuaLimitException(string message, string luaStackTrace, LuaLimit limit) : base(message, luaStackTrace)
        {
            Limit = limit;
        }

        public LuaLimit Limit { get; private set; }
    }

    [Serializable]
    public sealed class LuaInstructionLimitException : LuaLimitException
    {
        public LuaInstructionLimitException(string message, long instructionLimit)
            : base(message, LuaLimit.Instructions)
        {
            InstructionLimit = instructionLimit;
        }

        public LuaInstructionLimitException(string message, string luaStackTrace, long instructionLimit)
            : base(message, luaStackTrace, LuaLimit.Instructions)
        {
            InstructionLimit = instructionLimit;
        }

        public long InstructionLimit { get; private set; }
    }

    [Serializable]
    public sealed class LuaTimeLimitException : LuaLimitException
    {
        public LuaTimeLimitException(string message, TimeSpan timeLimit) : base(message, LuaLimit.Time)
        {
            TimeLimit = timeLimit;
        }

        public LuaTimeLimitException(string message, string luaStackTrace, TimeSpan timeLimit)
            : base(message, luaStackTrace, LuaLimit.Time)
        {
            TimeLimit = timeLimit;
        }

        public TimeSpan TimeLimit { get; private set; }
    }

    [Serializable]
    public sealed class LuaInterruptedException : LuaLimitException
    {
        public LuaInterruptedException(string message) : base(message, LuaLimit.Interrupt)
        {
        }

        public LuaInterruptedException(string message, string luaStackTrace) : base(message, luaStackTrace, LuaLimit.Interrupt)
        {
        }
    }

#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
    /// <summary>
    /// A runtime error of a <see cref="LuaScript"/> call: <c>error()</c>, a
    /// failed operation, or a function that is not there. The message is the
    /// error's (non-string errors are described), <see cref="LuaException.LuaStackTrace"/>
    /// the traceback.
    /// </summary>
    [Serializable]
    public class LuaRuntimeException : LuaException
    {
        public LuaRuntimeException(string message, string luaStackTrace) : base(message, luaStackTrace)
        {
        }

        public LuaRuntimeException(string message, string luaStackTrace, Exception innerException)
            : base(message, luaStackTrace, innerException)
        {
        }
    }

    /// <summary>A chunk that does not compile, or a binary chunk (<see cref="LuaScript"/> loads text only).</summary>
    [Serializable]
    public sealed class LuaSyntaxException : LuaException
    {
        public LuaSyntaxException(string message, string chunkName) : base(message)
        {
            ChunkName = chunkName;
        }

        /// <summary>The chunk name given to Load, without the '@' Lua sees.</summary>
        public string ChunkName { get; private set; }
    }

    /// <summary>A Lua value that cannot be read as the C# type asked for (<see cref="LuaValue"/>, <c>Call&lt;T&gt;</c>).</summary>
    [Serializable]
    public sealed class LuaTypeException : LuaException
    {
        public LuaTypeException(string message, LuaType expected, LuaType actual) : base(message)
        {
            Expected = expected;
            Actual = actual;
        }

        public LuaType Expected { get; private set; }
        public LuaType Actual { get; private set; }

        // An argument of a host function (LuaCall): the frame it belongs to, the argument
        // (1-based), and what the trampoline raises: luaL_typeerror with the expected type
        // name, or luaL_argerror with the message.
        internal int ArgFrame;
        internal int Arg;
        internal bool IsTypeError;
        internal string LuaMessage;
    }

    /// <summary>
    /// A <see cref="LuaScript"/> call that ran out of memory: the memory limit
    /// refused an allocation (Lua's "not enough memory", which a script can
    /// catch with <c>pcall</c>, reached C#), or the C library had no memory
    /// left (<see cref="HostOutOfMemory"/>).
    /// </summary>
    [Serializable]
    public sealed class LuaMemoryLimitException : LuaLimitException
    {
        public LuaMemoryLimitException(string message, long memoryLimit, long memoryUsed, bool hostOutOfMemory)
            : base(message, LuaLimit.Memory)
        {
            MemoryLimit = memoryLimit;
            MemoryUsed = memoryUsed;
            HostOutOfMemory = hostOutOfMemory;
        }

        public long MemoryLimit { get; private set; }
        public long MemoryUsed { get; private set; }
        public bool HostOutOfMemory { get; private set; }
    }
#endif
}
