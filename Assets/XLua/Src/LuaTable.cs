/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
#endif

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;

namespace XLua
{
    public partial class LuaTable : LuaBase
    {
        public LuaTable(int reference, LuaEnv luaenv) : base(reference, luaenv)
        {
        }

        // What Get and GetInPath need to know of a value type T, found once
        // per type, since a typeof costs a Type lookup under a lock in an
        // IL2CPP player (ObjectTranslator.TypeOps): whether it refuses nil
        // (typeof(T).IsValueType()), and how Get reads it in its native call
        // (ScalarRead; null for most types). One class, so that IL2CPP
        // compiles one type initializer per T.
        static class ValueOf<T>
        {
            internal static readonly bool NilRefused = typeof(T).IsValueType();
            internal static readonly ScalarRead<T> Scalar = (ScalarRead<T>)ScalarReaders.For(typeof(T));
        }

        // Whether TKey is string, tested once per type: a string key goes to
        // the native side with the read or write (xlua_getref_getfield,
        // xlua_getref_setfield) instead of in a push of its own.
        static class StringKey<T>
        {
            internal static readonly bool Value = typeof(T) == typeof(string);
        }

        // How Get reads a value of type T in its native call
        // (xlua_getref_getvalue), without ObjectTranslator.Get<T> and the two
        // P/Invokes of its get function and lua_settop: the number and boolean
        // types whose get functions are the bridge's own and cannot be
        // replaced (RegisterPushAndGetAndUpdate refuses a type that has one).
        // conv says how the native side reads the value (1 xlua_tointeger64,
        // 2 lua_tonumber, 3 lua_toboolean), Read converts it as the get
        // function does, with its exceptions. None for every other type.
        abstract class ScalarRead<T>
        {
            internal readonly int conv;

            protected ScalarRead(int conv)
            {
                this.conv = conv;
            }

            internal abstract T Read(long value, int valid);
        }

        static class ScalarReaders
        {
            internal static object For(Type type)
            {
                if (type == typeof(int)) return new Int32Read();
                if (type == typeof(long)) return new Int64Read();
                if (type == typeof(uint)) return new UInt32Read();
                if (type == typeof(ulong)) return new UInt64Read();
                if (type == typeof(double)) return new DoubleRead();
                if (type == typeof(float)) return new SingleRead();
                if (type == typeof(bool)) return new BooleanRead();
                return null;
            }

            // The get functions of ObjectTranslator.tryGetGetFuncByType:
            // xlua_getint32 and the other integer types by Lua 5.3's integer
            // rule, double and float through lua_tonumber, bool lua_toboolean.
            sealed class Int32Read : ScalarRead<int>
            {
                internal Int32Read() : base(1) { }
                internal override int Read(long value, int valid) { return LuaAPI.int32Value(value, valid); }
            }

            sealed class Int64Read : ScalarRead<long>
            {
                internal Int64Read() : base(1) { }
                internal override long Read(long value, int valid) { return LuaAPI.int64Value(value, valid); }
            }

            sealed class UInt32Read : ScalarRead<uint>
            {
                internal UInt32Read() : base(1) { }
                internal override uint Read(long value, int valid) { return LuaAPI.uint32Value(value, valid); }
            }

            sealed class UInt64Read : ScalarRead<ulong>
            {
                internal UInt64Read() : base(1) { }
                internal override ulong Read(long value, int valid) { return LuaAPI.uint64Value(value, valid); }
            }

            sealed class DoubleRead : ScalarRead<double>
            {
                internal DoubleRead() : base(2) { }
                internal override double Read(long value, int valid) { return BitConverter.Int64BitsToDouble(value); }
            }

            sealed class SingleRead : ScalarRead<float>
            {
                internal SingleRead() : base(2) { }
                internal override float Read(long value, int valid) { return (float)BitConverter.Int64BitsToDouble(value); }
            }

            sealed class BooleanRead : ScalarRead<bool>
            {
                internal BooleanRead() : base(3) { }
                internal override bool Read(long value, int valid) { return value != 0; }
            }
        }

        // Pushes t[key] as lua_gettable gives it (__index included), with
        // oldTop the stack top before. Returns the value's type, or -1 with
        // the error on the top (xlua_getref_get).
        int Fetch<TKey>(RealStatePtr L, ObjectTranslator translator, TKey key, out int oldTop)
        {
#if !NATIVE_LUA_PUSHSTRING
            if (StringKey<TKey>.Value)
            {
                string name = (string)(object)key;
                if (name != null)
                {
                    return LuaAPI.xlua_getref_getfield(L, luaReference, name, out oldTop);
                }
            }
#endif
            translator.PushByType(L, key);
            return LuaAPI.xlua_getref_get(L, luaReference, out oldTop);
        }

        // Fetch with the value read as conv says and popped (ScalarRead,
        // xlua_getref_getvalue): the stack is at oldTop again, but for an
        // error (-1), which is on the top.
        int FetchValue<TKey>(RealStatePtr L, ObjectTranslator translator, TKey key, int conv, out int oldTop,
            out long value, out int valid)
        {
#if !NATIVE_LUA_PUSHSTRING
            if (StringKey<TKey>.Value)
            {
                string name = (string)(object)key;
                if (name != null)
                {
                    return LuaAPI.xlua_getref_getfieldvalue(L, luaReference, name, conv, out oldTop, out value, out valid);
                }
            }
#endif
            translator.PushByType(L, key);
            return LuaAPI.xlua_getref_getvalue(L, luaReference, conv, out oldTop, out value, out valid);
        }

        // no boxing version get
        public void Get<TKey, TValue>(TKey key, out TValue value)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                var translator = luaEnv.translator;
                int oldTop;
                int type;

                ScalarRead<TValue> scalar = ValueOf<TValue>.Scalar;
                if (scalar != null)
                {
                    // A number or a boolean in one native call, popped there.
                    long raw;
                    int valid;
                    type = FetchValue(L, translator, key, scalar.conv, out oldTop, out raw, out valid);
                    if (type < 0)
                    {
                        string err = LuaAPI.lua_tostring(L, -1);
                        LuaAPI.lua_settop(L, oldTop);
                        throw new Exception("get field [" + key + "] error:" + err);
                    }
                    if ((LuaTypes)type == LuaTypes.LUA_TNIL && ValueOf<TValue>.NilRefused)
                    {
                        throw new InvalidCastException("can not assign nil to " + typeof(TValue).GetFriendlyName());
                    }
                    value = scalar.Read(raw, valid);
                    return;
                }

                type = Fetch(L, translator, key, out oldTop);

                if (type < 0)
                {
                    string err = LuaAPI.lua_tostring(L, -1);
                    LuaAPI.lua_settop(L, oldTop);
                    throw new Exception("get field [" + key + "] error:" + err);
                }

                LuaTypes lua_type = (LuaTypes)type;
                if (lua_type == LuaTypes.LUA_TNIL && ValueOf<TValue>.NilRefused)
                {
                    LuaAPI.lua_settop(L, oldTop);
                    throw new InvalidCastException("can not assign nil to " + typeof(TValue).GetFriendlyName());
                }

                try
                {
                    translator.Get(L, -1, out value);
                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    LuaAPI.lua_settop(L, oldTop);
                }
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        // no boxing version get
        public bool ContainsKey<TKey>(TKey key)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                var translator = luaEnv.translator;
                int oldTop;
                // Fetch and lua_settop: xlua_getref_getvalue with only the type
                // (conv 0) measured 2 to 3 ns slower on the xlua16k emulator
                // (2026-10-31), its out values and call costing more than the
                // second P/Invoke.
                int type = Fetch(L, translator, key, out oldTop);

                if (type < 0)
                {
                    string err = LuaAPI.lua_tostring(L, -1);
                    LuaAPI.lua_settop(L, oldTop);
                    throw new Exception("get field [" + key + "] error:" + err);
                }

                bool ret = (LuaTypes)type != LuaTypes.LUA_TNIL;

                LuaAPI.lua_settop(L, oldTop);

                return ret;

#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        //no boxing version set
        public void Set<TKey, TValue>(TKey key, TValue value)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                int oldTop;
                int status;
                var translator = luaEnv.translator;

#if !NATIVE_LUA_PUSHSTRING
                string name = StringKey<TKey>.Value ? (string)(object)key : null;
                if (name != null)
                {
                    translator.PushByType(L, value);
                    status = LuaAPI.xlua_getref_setfield(L, luaReference, name, out oldTop);
                }
                else
#endif
                {
                    translator.PushByType(L, key);
                    translator.PushByType(L, value);
                    status = LuaAPI.xlua_getref_set(L, luaReference, out oldTop);
                }

                // Done: the stack is at oldTop again.
                if (0 != status)
                {
                    luaEnv.ThrowExceptionFromError(oldTop);
                }
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }


        public T GetInPath<T>(string path)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                var translator = luaEnv.translator;
                int oldTop = LuaAPI.lua_gettop(L);
                LuaAPI.lua_getref(L, luaReference);
                if (0 != LuaAPI.xlua_pgettable_bypath(L, -1, path))
                {
                    luaEnv.ThrowExceptionFromError(oldTop);
                }
                LuaTypes lua_type = LuaAPI.lua_type(L, -1);
                if (lua_type == LuaTypes.LUA_TNIL && ValueOf<T>.NilRefused)
                {
                    LuaAPI.lua_settop(L, oldTop);
                    throw new InvalidCastException("can not assign nil to " + typeof(T).GetFriendlyName());
                }

                T value;
                try
                {
                    translator.Get(L, -1, out value);
                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    LuaAPI.lua_settop(L, oldTop);
                }
                return value;
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        public void SetInPath<T>(string path, T val)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                int oldTop = LuaAPI.lua_gettop(L);
                LuaAPI.lua_getref(L, luaReference);
                luaEnv.translator.PushByType(L, val);
                if (0 != LuaAPI.xlua_psettable_bypath(L, -2, path))
                {
                    luaEnv.ThrowExceptionFromError(oldTop);
                }

                LuaAPI.lua_settop(L, oldTop);
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        [Obsolete("use no boxing version: GetInPath/SetInPath Get/Set instead!")]
        public object this[string field]
        {
            get
            {
                return GetInPath<object>(field);
            }
            set
            {
                SetInPath(field, value);
            }
        }

        [Obsolete("use no boxing version: GetInPath/SetInPath Get/Set instead!")]
        public object this[object field]
        {
            get
            {
                return Get<object>(field);
            }
            set
            {
                Set(field, value);
            }
        }

        public void ForEach<TKey, TValue>(Action<TKey, TValue> action)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                var translator = luaEnv.translator;
                int oldTop = LuaAPI.lua_gettop(L);
                try
                {
                    LuaAPI.lua_getref(L, luaReference);
                    LuaAPI.lua_pushnil(L);
                    int more;
                    // Protected: the action may change the table so that the key
                    // is gone ("invalid key to 'next'", raised with no handler).
                    while ((more = LuaAPI.xlua_pnext(L, -2)) > 0)
                    {
                        if (translator.Assignable<TKey>(L, -2))
                        {
                            TKey key;
                            TValue val;
                            translator.Get(L, -2, out key);
                            translator.Get(L, -1, out val);
                            action(key, val);
                        }
                        LuaAPI.lua_pop(L, 1);
                    }
                    if (more < 0)
                    {
                        luaEnv.ThrowExceptionFromError(oldTop);
                    }
                }
                finally
                {
                    LuaAPI.lua_settop(L, oldTop);
                }
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        public int Length
        {
            get
            {
#if THREAD_SAFE || HOTFIX_ENABLE
                lock (luaEnv.luaEnvLock)
                {
#endif
                    var L = State;
                    int oldTop = LuaAPI.lua_gettop(L);
                    LuaAPI.lua_getref(L, luaReference);
                    uint len;
                    // A __len metamethod runs protected; its error is thrown here.
                    if (LuaAPI.xlua_plen(L, -1, out len) != 0)
                    {
                        luaEnv.ThrowExceptionFromError(oldTop);
                    }
                    LuaAPI.lua_settop(L, oldTop);
                    return (int)len;
#if THREAD_SAFE || HOTFIX_ENABLE
                }
#endif
            }
        }

#if THREAD_SAFE || HOTFIX_ENABLE
        [Obsolete("not thread safe!", true)]
#endif
        public IEnumerable GetKeys()
        {
            var L = State;
            var translator = luaEnv.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            try
            {
                LuaAPI.lua_getref(L, luaReference);
                LuaAPI.lua_pushnil(L);
                int more;
                while ((more = LuaAPI.xlua_pnext(L, -2)) > 0)
                {
                    yield return translator.GetObject(L, -2);
                    LuaAPI.lua_pop(L, 1);
                }
                if (more < 0)
                {
                    luaEnv.ThrowExceptionFromError(oldTop);
                }
            }
            finally
            {
                LuaAPI.lua_settop(L, oldTop);
            }
        }

#if THREAD_SAFE || HOTFIX_ENABLE
        [Obsolete("not thread safe!", true)]
#endif
        public IEnumerable<T> GetKeys<T>()
        {
            var L = State;
            var translator = luaEnv.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            try
            {
                LuaAPI.lua_getref(L, luaReference);
                LuaAPI.lua_pushnil(L);
                int more;
                while ((more = LuaAPI.xlua_pnext(L, -2)) > 0)
                {
                    if (translator.Assignable<T>(L, -2))
                    {
                        T v;
                        translator.Get(L, -2, out v);
                        yield return v;
                    }
                    LuaAPI.lua_pop(L, 1);
                }
                if (more < 0)
                {
                    luaEnv.ThrowExceptionFromError(oldTop);
                }
            }
            finally
            {
                LuaAPI.lua_settop(L, oldTop);
            }
        }

        [Obsolete("use no boxing version: Get<TKey, TValue> !")]
        public T Get<T>(object key)
        {
            T ret;
            Get(key, out ret);
            return ret;
        }

        public TValue Get<TKey, TValue>(TKey key)
        {
            TValue ret;
            Get(key, out ret);
            return ret;
        }

        public TValue Get<TValue>(string key)
        {
            TValue ret;
            Get(key, out ret);
            return ret;
        }

        public void SetMetaTable(LuaTable metaTable)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                var L = State;
                push(L);
                metaTable.push(L);
                LuaAPI.lua_setmetatable(L, -2);
                LuaAPI.lua_pop(L, 1);
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        public T Cast<T>()
        {
            var L = State;
            var translator = luaEnv.translator;
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (luaEnv.luaEnvLock)
            {
#endif
                int oldTop = LuaAPI.lua_gettop(L);
                push(L);
                try
                {
                    return (T)translator.GetObject(L, -1, typeof(T));
                }
                finally
                {
                    // Also when the conversion throws, e.g. for a type not in CSharpCallLua.
                    LuaAPI.lua_settop(L, oldTop);
                }
#if THREAD_SAFE || HOTFIX_ENABLE
            }
#endif
        }

        public override string ToString()
        {
            return "table :" + luaReference;
        }
    }
}
