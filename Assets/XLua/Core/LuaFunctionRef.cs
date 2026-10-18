/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's function handles (docs/design/core-luascript.md, sections 2.5,
// 2.6 and 2.9; milestone 8): LuaFunctionRef, a handle to a Lua function with
// the Call overloads of LuaScript.Call, and LuaScript.Compile, which loads a
// chunk without running it. Compiled with the scripting define XLUA_LUASCRIPT
// or XLUA_CORE_ONLY, like LuaScript.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// A handle to a Lua function of a <see cref="LuaScript"/>, with the
    /// <c>Call</c> overloads of <see cref="LuaScript.Call(string)"/>: one
    /// P/Invoke per call, the script's limits, and no allocation for primitive
    /// arguments and results. A struct with the handle rules of
    /// <see cref="LuaTableRef"/>: dispose an owning handle, a copy of a disposed
    /// one is invalid, and a borrowed one ends with its frame or step.
    /// </summary>
    public readonly struct LuaFunctionRef : IDisposable, IEquatable<LuaFunctionRef>
    {
        readonly LuaScript script;
        readonly int slot;
        readonly int generation;

        internal LuaFunctionRef(LuaScript script, int slot, int generation)
        {
            this.script = script;
            this.slot = slot;
            this.generation = generation;
        }

        internal int Slot { get { return slot; } }
        internal int Generation { get { return generation; } }

        /// <summary>The script the function belongs to; null for default(LuaFunctionRef).</summary>
        public LuaScript Script { get { return script; } }

        /// <summary>False after Dispose, for a copy of a disposed handle, after a borrowed handle's frame or step, after the script is disposed, and for default(LuaFunctionRef).</summary>
        public bool IsValid { get { return script != null && script.HandleValid(slot, generation, LuaType.Function); } }

        int Native
        {
            get
            {
                if (script == null)
                {
                    throw new ObjectDisposedException("LuaFunctionRef", "default(LuaFunctionRef) refers to no function");
                }
                return script.NativeHandle(slot, generation, LuaType.Function);
            }
        }

        /// <summary>Calls the function; errors and limits as for <see cref="LuaScript.Call(string)"/>.</summary>
        public void Call() { script.CallRef(Native, new LuaScript.Args(), default(Span<LuaValue>)); }
        public void Call(LuaValue a1) { script.CallRef(Native, new LuaScript.Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(LuaValue a1, LuaValue a2) { script.CallRef(Native, new LuaScript.Args(2, a1, a2, default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(LuaValue a1, LuaValue a2, LuaValue a3) { script.CallRef(Native, new LuaScript.Args(3, a1, a2, a3, default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { script.CallRef(Native, new LuaScript.Args(4, a1, a2, a3, a4), default(Span<LuaValue>)); }
        public void Call(ReadOnlySpan<LuaValue> args) { script.CallRef(Native, new LuaScript.Args(args), default(Span<LuaValue>)); }

        /// <summary>Calls the function and reads its first result as T (as for <see cref="LuaScript.Call{T}(string)"/>).</summary>
        public T Call<T>() { return script.CallRefRead<T>(Native, new LuaScript.Args()); }
        public T Call<T>(LuaValue a1) { return script.CallRefRead<T>(Native, new LuaScript.Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue))); }
        public T Call<T>(LuaValue a1, LuaValue a2) { return script.CallRefRead<T>(Native, new LuaScript.Args(2, a1, a2, default(LuaValue), default(LuaValue))); }
        public T Call<T>(LuaValue a1, LuaValue a2, LuaValue a3) { return script.CallRefRead<T>(Native, new LuaScript.Args(3, a1, a2, a3, default(LuaValue))); }
        public T Call<T>(LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { return script.CallRefRead<T>(Native, new LuaScript.Args(4, a1, a2, a3, a4)); }
        public T Call<T>(ReadOnlySpan<LuaValue> args) { return script.CallRefRead<T>(Native, new LuaScript.Args(args)); }

        /// <summary>Calls the function and fills results; returns how many values it returned.</summary>
        public int Call(ReadOnlySpan<LuaValue> args, Span<LuaValue> results) { return script.CallRef(Native, new LuaScript.Args(args), results); }

        /// <summary>The first two results; missing ones are nil.</summary>
        public ValueTuple<T1, T2> Call<T1, T2>(ReadOnlySpan<LuaValue> args) { return script.CallRead2<T1, T2>(LuaScript.XLUA_CALL_REF, 0, Native, null, new LuaScript.Args(args)); }

        /// <summary>The first three results; missing ones are nil.</summary>
        public ValueTuple<T1, T2, T3> Call<T1, T2, T3>(ReadOnlySpan<LuaValue> args) { return script.CallRead3<T1, T2, T3>(LuaScript.XLUA_CALL_REF, 0, Native, null, new LuaScript.Args(args)); }

        /// <summary>A new owning handle to the same function (also from a borrowed one); dispose it separately.</summary>
        public LuaFunctionRef Retain()
        {
            return script.RetainFunction(Native);
        }

        /// <summary>Releases an owning handle; a borrowed one, a second Dispose or one after the script's do nothing.</summary>
        public void Dispose()
        {
            if (script != null)
            {
                script.ReleaseHandle(slot, generation, LuaType.Function);
            }
        }

        /// <summary>The same function (two handles may refer to one function).</summary>
        public bool Equals(LuaFunctionRef other)
        {
            return LuaScript.SameValue(script, slot, generation, other.script, other.slot, other.generation, LuaType.Function);
        }

        public override bool Equals(object obj)
        {
            return obj is LuaFunctionRef && Equals((LuaFunctionRef)obj);
        }

        public override int GetHashCode()
        {
            return script == null ? 0 : RuntimeHelpers.GetHashCode(script);
        }

        public static bool operator ==(LuaFunctionRef a, LuaFunctionRef b) { return a.Equals(b); }
        public static bool operator !=(LuaFunctionRef a, LuaFunctionRef b) { return !a.Equals(b); }

        public override string ToString()
        {
            return IsValid ? "function" : "function (invalid handle)";
        }
    }

    public sealed partial class LuaScript
    {
        /// <summary>
        /// Loads a text chunk as "@" + chunkName with the environment env
        /// (default: <see cref="Globals"/>) without running it, and returns it
        /// as an owning function handle: call it any number of times. A syntax
        /// error or a binary chunk throws <see cref="LuaSyntaxException"/>;
        /// compiling counts against <see cref="MemoryLimit"/> only.
        /// </summary>
        public LuaFunctionRef Compile(string code, string chunkName, LuaTableRef env = default(LuaTableRef))
        {
            if (code == null) throw new ArgumentNullException("code");
            return InvokeRead<LuaFunctionRef>(XLUA_CALL_COMPILE, EnvHandle(env), 0, chunkName, code, new Args());
        }

        internal int CallRef(int fn, Args args, Span<LuaValue> results)
        {
            return Invoke(XLUA_CALL_REF, 0, fn, null, null, default(ReadOnlySpan<byte>), args, results);
        }

        internal T CallRefRead<T>(int fn, Args args)
        {
            return InvokeRead<T>(XLUA_CALL_REF, 0, fn, null, null, args);
        }

        internal int CallIn(int env, string function, Args args, Span<LuaValue> results)
        {
            return Invoke(XLUA_CALL_GLOBAL, env, 0, function, null, default(ReadOnlySpan<byte>), args, results);
        }

        internal T CallInRead<T>(int env, string function, Args args)
        {
            return InvokeRead<T>(XLUA_CALL_GLOBAL, env, 0, function, null, args);
        }

        // The native handle of an environment argument: default(LuaTableRef) is Globals.
        int EnvHandle(LuaTableRef env)
        {
            if (env.Script == null)
            {
                return 0;
            }
            if (env.Script != this)
            {
                throw new ArgumentException("the environment belongs to another LuaScript", "env");
            }
            return NativeHandle(env.Slot, env.Generation, LuaType.Table);
        }
    }
}
#endif
