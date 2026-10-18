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

namespace XLua
{
    public abstract class LuaBase : IDisposable
    {
        protected bool disposed;
        protected readonly int luaReference;
        protected readonly LuaEnv luaEnv;

        // For XLua.Binding.BindingApi, which generated bridges in other assemblies call.
        internal LuaEnv Env { get { return luaEnv; } }
        internal int LuaReference { get { return luaReference; } }

#if UNITY_EDITOR || XLUA_GENERAL
        protected int _errorFuncRef { get { return luaEnv.errorFuncRef; } }
        protected RealStatePtr _L { get { return luaEnv.L; } }
        protected ObjectTranslator _translator { get { return luaEnv.translator; } }
#endif

        public LuaBase(int reference, LuaEnv luaenv)
        {
            luaReference = reference;
            luaEnv = luaenv;
        }

        ~LuaBase()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposeManagedResources)
        {
            if (!disposed)
            {
                if (luaReference != 0)
                {
                    bool is_delegate = this is DelegateBridgeBase;
                    if (disposeManagedResources)
                    {
#if THREAD_SAFE || HOTFIX_ENABLE
                        lock (luaEnv.luaEnvLock)
                        {
#endif
                            // After LuaEnv.Dispose (another OnDestroy, often when play mode
                            // exits) lua_close has already released the reference.
                            if (luaEnv.translator != null)
                            {
                                luaEnv.translator.ReleaseLuaBase(luaEnv.L, luaReference, is_delegate);
                            }
#if THREAD_SAFE || HOTFIX_ENABLE
                        }
#endif
                    }
                    else //will dispse by LuaEnv.GC
                    {
                        // The finalizer thread only queues the reference; the queue has
                        // its own lock. Taking luaEnvLock here deadlocked when the thread
                        // running Lua waited for finalizers (Tencent/xLua#1127).
                        luaEnv.equeueGCAction(new LuaEnv.GCAction { Reference = luaReference, IsDelegate = is_delegate });
                    }
                }
                disposed = true;
            }
        }

        public override bool Equals(object o)
        {
            if (o != null && this.GetType() == o.GetType())
            {
#if THREAD_SAFE || HOTFIX_ENABLE
                lock (luaEnv.luaEnvLock)
                {
#endif
                    LuaBase rhs = (LuaBase)o;
                    var L = luaEnv.L;
                    if (L != rhs.luaEnv.L)
                        return false;
                    int top = LuaAPI.lua_gettop(L);
                    LuaAPI.lua_getref(L, rhs.luaReference);
                    LuaAPI.lua_getref(L, luaReference);
                    int equal = LuaAPI.lua_rawequal(L, -1, -2);
                    LuaAPI.lua_settop(L, top);
                    return (equal != 0);
#if THREAD_SAFE || HOTFIX_ENABLE
                }
#endif
            }
            else return false;
        }

        public override int GetHashCode()
        {
            LuaAPI.lua_getref(luaEnv.L, luaReference);
            var pointer = LuaAPI.lua_topointer(luaEnv.L, -1);
            LuaAPI.lua_pop(luaEnv.L, 1);
            // Not ToInt32(): it throws OverflowException for an address above 2^31 in a 64-bit
            // .NET Framework process. Mono and IL2CPP give the same value for both.
            return pointer.GetHashCode();
        }

        internal virtual void push(RealStatePtr L)
        {
            LuaAPI.lua_getref(L, luaReference);
        }
    }
}
