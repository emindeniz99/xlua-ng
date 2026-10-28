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

using System.Collections.Generic;
using System;

namespace XLua
{
    public class ObjectTranslatorPool
    {
#if !SINGLE_ENV
        // The translators by main state, and the last one found. Find runs on every
        // Lua -> C# call, and under THREAD_SAFE and HOTFIX_ENABLE envs on several
        // threads call it at once; it reads without a lock. Add and Remove (under the
        // lock, at LuaEnv creation and Dispose) never change a published dictionary or
        // cache entry: they publish new ones. Until 2026-10-28 every Find took the
        // lock, 40 to 80 ns per Lua -> C# call on Mono (docs/research/hotfix.md).
        Dictionary<RealStatePtr, WeakReference> translators = new Dictionary<RealStatePtr, WeakReference>();

        sealed class Found
        {
            internal readonly RealStatePtr Ptr;
            internal readonly ObjectTranslator Translator;

            internal Found(RealStatePtr ptr, ObjectTranslator translator)
            {
                Ptr = ptr;
                Translator = translator;
            }
        }

        Found last;
#else
        ObjectTranslator lastTranslator = default(ObjectTranslator);
#endif

        public static ObjectTranslatorPool Instance
        {
            get
            {
                return InternalGlobals.objectTranslatorPool;
            }
        }

#if UNITY_EDITOR || XLUA_GENERAL
        public static ObjectTranslator FindTranslator(RealStatePtr L)
        {
            return InternalGlobals.objectTranslatorPool.Find(L);
        }
#endif

        public ObjectTranslatorPool ()
        {
        }

        public void Add (RealStatePtr L, ObjectTranslator translator)
        {
            lock (this)
            {
#if SINGLE_ENV
                lastTranslator = translator;
#else
                var ptr = LuaAPI.xlua_gl(L);
                var copy = new Dictionary<RealStatePtr, WeakReference>(translators);
                copy.Add(ptr, new WeakReference(translator));
                System.Threading.Interlocked.Exchange(ref translators, copy);
                System.Threading.Interlocked.Exchange(ref last, new Found(ptr, translator));
#endif
            }
        }

        public ObjectTranslator Find (RealStatePtr L)
        {
#if SINGLE_ENV
            return lastTranslator;
#else
            var ptr = LuaAPI.xlua_gl(L);
            var found = last;
            if (found != null && found.Ptr == ptr) return found.Translator;
            WeakReference weak;
            if (translators.TryGetValue(ptr, out weak))
            {
                var translator = weak.Target as ObjectTranslator;
                // Only callbacks of this env look it up, and they run in its Lua calls, which
                // Dispose (Remove) does not overlap: this cannot bring back a removed entry.
                System.Threading.Interlocked.Exchange(ref last, new Found(ptr, translator));
                return translator;
            }
            return null;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_MEMORY_DIAGNOSTICS
        // The live translators, for LuaMemoryDiagnostics.LiveEnvironments.
        internal List<ObjectTranslator> Translators()
        {
            var result = new List<ObjectTranslator>();
#if SINGLE_ENV
            var single = lastTranslator;
            if (single != null) result.Add(single);
#else
            foreach (var weak in translators.Values)
            {
                var translator = weak.Target as ObjectTranslator;
                if (translator != null) result.Add(translator);
            }
#endif
            return result;
        }

#endif
        public void Remove (RealStatePtr L)
        {
            lock (this)
            {
#if SINGLE_ENV
                lastTranslator = default(ObjectTranslator);
#else
                var ptr = LuaAPI.xlua_gl(L);
                if (!translators.ContainsKey (ptr))
                    return;

                var found = last;
                if (found != null && found.Ptr == ptr)
                {
                    System.Threading.Interlocked.Exchange(ref last, null);
                }

                var copy = new Dictionary<RealStatePtr, WeakReference>(translators);
                copy.Remove(ptr);
                System.Threading.Interlocked.Exchange(ref translators, copy);
#endif
            }
        }
    }
}
