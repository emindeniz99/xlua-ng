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
    }

    /// <summary>The limit that stopped a call (<see cref="LuaLimitException"/>).</summary>
    public enum LuaLimit
    {
        Instructions,
        Time,
        Interrupt,
    }

    /// <summary>
    /// A call that an instruction or time limit or <see cref="LuaEnv.Interrupt"/>
    /// stopped (<see cref="LuaOptions"/>). The message is Lua's, with its
    /// traceback; once stopped, the call cannot catch the error and continue.
    /// </summary>
    [Serializable]
    public abstract class LuaLimitException : LuaException
    {
        protected LuaLimitException(string message, LuaLimit limit) : base(message)
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

        public long InstructionLimit { get; private set; }
    }

    [Serializable]
    public sealed class LuaTimeLimitException : LuaLimitException
    {
        public LuaTimeLimitException(string message, TimeSpan timeLimit) : base(message, LuaLimit.Time)
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
    }
}
