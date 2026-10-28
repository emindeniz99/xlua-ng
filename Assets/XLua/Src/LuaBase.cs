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
        protected RealStatePtr _L { get { return State; } }
        protected ObjectTranslator _translator { get { return luaEnv.translator; } }
#endif

        // The state for every use of this object: calls, reads, writes, pushes to
        // Lua, the bridges (CodeEmit's through _L, generated ones through
        // BindingApi.State). After Dispose it throws ObjectDisposedException: the
        // registry reference was released, and the next luaL_ref may have given
        // it to another function or table, which a use would then call or change
        // (docs/research/crash-inventory.md, item 12; owner decision of 2026-10-28).
        // After LuaEnv.Dispose it throws too (LuaEnv.L). One comparison.
        internal RealStatePtr State
        {
            get
            {
                if (disposed)
                {
                    ThrowDisposed();
                }
                return luaEnv.L;
            }
        }

        // Out of line, so that State stays small enough to inline.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void ThrowDisposed()
        {
            throw new ObjectDisposedException(GetType().Name,
                "this " + GetType().Name + " has been disposed; its Lua reference may belong to another object now");
        }

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
                    var L = State;
                    if (L != rhs.State)
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
            var L = State;
            LuaAPI.lua_getref(L, luaReference);
            var pointer = LuaAPI.lua_topointer(L, -1);
            LuaAPI.lua_pop(L, 1);
            // Not ToInt32(): it throws OverflowException for an address above 2^31 in a 64-bit
            // .NET Framework process. Mono and IL2CPP give the same value for both.
            return pointer.GetHashCode();
        }

        // Pushes the object onto L, a state of its env that the caller already
        // holds (a LuaTable or LuaFunction passed to Lua or stored in a table).
        internal virtual void push(RealStatePtr L)
        {
            if (disposed)
            {
                ThrowDisposed();
            }
            LuaAPI.lua_getref(L, luaReference);
        }
    }
}
