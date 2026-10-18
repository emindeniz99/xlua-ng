/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's host functions (docs/design/core-luascript.md, sections 2.8 and
// 3.7; milestone 7): Register, Unregister and the typed shorthands, the
// process-wide dispatcher that the native trampoline calls, and LuaCall, a
// host function's view of its arguments and results. The trampoline copies the
// arguments into xlua_value records before C# runs and pushes the results or
// raises the error after C# has returned, so no Lua error crosses a managed
// frame and no C# exception crosses a Lua frame. Compiled with the scripting
// define XLUA_LUASCRIPT or XLUA_CORE_ONLY, like LuaScript.cs.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using XLua.LuaDLL;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>
    /// A host function registered with <see cref="LuaScript.Register(string, LuaCallback)"/>.
    /// It reads its arguments and writes its results through
    /// <paramref name="call"/>, which is valid only while it runs. An
    /// exception it throws becomes a Lua error.
    /// </summary>
    public delegate void LuaCallback(ref LuaCall call);

    public sealed unsafe partial class LuaScript
    {
        // xlua_core_frame.status and xlua_core_call_t.flags (build/xlua_core.h).
        const int XLUA_FRAME_YIELD = 1, XLUA_FRAME_ERROR = 2, XLUA_FRAME_ERROR_WHERE = 3;
        const int XLUA_FRAME_ARGERROR = 4, XLUA_FRAME_TYPEERROR = 5, XLUA_FRAME_MEMERR = 6;
        const int XLUA_CALLF_HOST_ERROR = 1;
        // An arena bigger than this is released at the start of the next outermost host function.
        const int ArenaKeep = 64 << 10;

        LuaCallback[] callbacks = new LuaCallback[0];  // by id - 1; id 0 is never used
        int callbackCount;                              // ids handed out so far
        int[] freeIds = new int[0];
        int freeCount;
        // Native memory for the strings, error messages and long result lists of
        // host functions, until the trampoline has pushed them. A stack: each
        // host function starts at the current top and gives its part back when
        // it returns, which is safe because no C# runs before the push.
        IntPtr arena;
        int arenaSize, arenaTop;
        // Results beyond the trampoline's inline 8, by frame, in native memory (no array of the
        // struct: see valueBuffer). Only read and written through OverflowSlot, never kept.
        xlua_value* overflow;
        int overflowCapacity;
        int overflowTop;
        Exception hostException;  // the exception of the last host function that raised one
        // Borrowed handles of the running host functions' arguments (LuaCall.GetTable ...), by
        // frame: their slots end with the frame; their native references are released at the
        // next host function or after the outermost call, since a result may still need them.
        int[] borrowed = new int[0];
        int borrowedTop;
        int[] unrefs = new int[0];
        int unrefCount;

        static readonly xlua_core_dispatch dispatchCallback = Dispatch;
        static readonly object dispatchLock = new object();
        static bool dispatchInstalled;
        static IntPtr fallbackMessage;  // when even the error of a host function cannot be reported
        static int fallbackLength;

        // ---- Register ----

        /// <summary>
        /// Registers a host function: <paramref name="name"/> in the base that
        /// every environment of this script sees, or for "library.name" in a
        /// library table that every environment gets a copy of, like the
        /// standard libraries (an existing environment gets the new function
        /// too). A name registered again replaces its function; a script that
        /// kept the old one gets an error when it calls it.
        /// </summary>
        public void Register(string name, LuaCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }
            CheckName(name);
            IntPtr state = Owned;
            InstallDispatcher();
            int id = AllocId(callback);
            int old;
            int status = SetFunction(state, name, id, out old);
            FreeId(old);
            if (status != XLUA_OK)
            {
                // The id stays taken: the native side may hold it already.
                throw RegisterError(status, name);
            }
        }

        /// <summary>
        /// Removes a host function registered under <paramref name="name"/>; a
        /// script that kept it gets an error when it calls it. Nothing happens
        /// for a name without a host function.
        /// </summary>
        public void Unregister(string name)
        {
            CheckName(name);
            int old;
            int status = SetFunction(Owned, name, 0, out old);
            FreeId(old);
            if (status != XLUA_OK)
            {
                throw RegisterError(status, name);
            }
        }

        // Typed shorthands: the arguments and results are long, int, double,
        // float, bool, string, byte[] or LuaValue, read and written as LuaCall
        // does, without boxing. Another type throws NotSupportedException here.
        public void Register(string name, Action fn) { Register(name, new ActionThunk(fn).Invoke); }
        public void Register<A1>(string name, Action<A1> fn) { Register(name, new ActionThunk<A1>(fn).Invoke); }
        public void Register<A1, A2>(string name, Action<A1, A2> fn) { Register(name, new ActionThunk<A1, A2>(fn).Invoke); }
#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        public void Register<A1, A2, A3>(string name, Action<A1, A2, A3> fn) { Register(name, new ActionThunk<A1, A2, A3>(fn).Invoke); }
#endif
#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        public void Register<A1, A2, A3, A4>(string name, Action<A1, A2, A3, A4> fn) { Register(name, new ActionThunk<A1, A2, A3, A4>(fn).Invoke); }
#endif
        public void Register<R>(string name, Func<R> fn) { Register(name, new FuncThunk<R>(fn).Invoke); }
        public void Register<A1, R>(string name, Func<A1, R> fn) { Register(name, new FuncThunk<A1, R>(fn).Invoke); }
        public void Register<A1, A2, R>(string name, Func<A1, A2, R> fn) { Register(name, new FuncThunk<A1, A2, R>(fn).Invoke); }
#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        public void Register<A1, A2, A3, R>(string name, Func<A1, A2, A3, R> fn) { Register(name, new FuncThunk<A1, A2, A3, R>(fn).Invoke); }
#endif
#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        public void Register<A1, A2, A3, A4, R>(string name, Func<A1, A2, A3, A4, R> fn) { Register(name, new FuncThunk<A1, A2, A3, A4, R>(fn).Invoke); }
#endif

        static void CheckName(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            int dot = name.IndexOf('.');
            if (name.Length == 0 || dot == 0 || dot == name.Length - 1 || (dot > 0 && name.IndexOf('.', dot + 1) >= 0))
            {
                throw new ArgumentException("a host function's name is \"name\" or \"library.name\": \"" + name + "\"", "name");
            }
        }

        /// <summary>
        /// Registers a host function as the key <paramref name="name"/> of the
        /// table env (any table of this script, such as one from
        /// <see cref="NewEnvironment"/>; dots are part of the key): visible only
        /// where that table is. A name registered again replaces its function.
        /// default(LuaTableRef) is <see cref="Globals"/>.
        /// </summary>
        public void Register(LuaTableRef env, string name, LuaCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0)
            {
                throw new ArgumentException("a host function's name is not empty", "name");
            }
            int table = RegisterTable(env);
            InstallDispatcher();
            int id = AllocId(callback);
            int old;
            int status = SetFunction(Owned, table, name, id, out old);
            FreeId(old);
            if (status != XLUA_OK)
            {
                throw RegisterError(status, name);
            }
        }

        /// <summary>Removes a host function that <see cref="Register(LuaTableRef, string, LuaCallback)"/> put into env.</summary>
        public void Unregister(LuaTableRef env, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            int table = RegisterTable(env);
            int old;
            int status = SetFunction(Owned, table, name, 0, out old);
            FreeId(old);
            if (status != XLUA_OK)
            {
                throw RegisterError(status, name);
            }
        }

        // The native handle of a Register table: Globals (native 0 means the base there) gets a reference of its own.
        int RegisterTable(LuaTableRef env)
        {
            int table = EnvHandle(env);  // LuaFunctionRef.cs
            if (table == 0)
            {
                if (globalsRef == 0)
                {
                    globalsRef = RetainNative(0, LuaType.Table);  // a slot of its own, never released
                    slots[globalsRef].state = SlotPermanent;
                    liveHandles--;
                }
                table = slots[globalsRef].nativeRef;
            }
            return table;
        }

        static int SetFunction(IntPtr state, string name, int id, out int old)
        {
            return SetFunction(state, 0, name, id, out old);
        }

        static int SetFunction(IntPtr state, int env, string name, int id, out int old)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            fixed (byte* p = utf8)
            {
                return LuaAPI.xlua_core_register(state, env, (IntPtr)p, utf8.Length, id, out old);
            }
        }

        Exception RegisterError(int status, string name)
        {
            if (status == XLUA_ERRMEM)
            {
                xlua_core_stats stats = Stats();
                return new LuaMemoryLimitException("not enough memory to register \"" + name + "\"", stats.memory_limit, stats.memory_used, false);
            }
            return new NotSupportedException("this xlua native library cannot register \"" + name + "\" (status " + status + ")");
        }

        int AllocId(LuaCallback callback)
        {
            int id;
            if (freeCount > 0)
            {
                id = freeIds[--freeCount];
            }
            else
            {
                if (callbackCount == int.MaxValue)
                {
                    throw new InvalidOperationException("too many host functions");
                }
                id = ++callbackCount;
                if (id > callbacks.Length)
                {
                    Array.Resize(ref callbacks, Math.Max(8, callbacks.Length * 2));
                }
            }
            callbacks[id - 1] = callback;
            return id;
        }

        void FreeId(int id)
        {
            if (id <= 0 || id > callbackCount || callbacks[id - 1] == null)
            {
                return;
            }
            callbacks[id - 1] = null;
            if (freeCount == freeIds.Length)
            {
                Array.Resize(ref freeIds, Math.Max(8, freeIds.Length * 2));
            }
            freeIds[freeCount++] = id;
        }

        static void InstallDispatcher()
        {
            lock (dispatchLock)
            {
                if (!dispatchInstalled)
                {
                    const string text = "a host function failed and its error could not be reported";
                    fallbackMessage = Marshal.StringToHGlobalAnsi(text);
                    fallbackLength = text.Length;
                    LuaAPI.xlua_core_set_dispatcher(Marshal.GetFunctionPointerForDelegate(dispatchCallback));
                    dispatchInstalled = true;
                }
            }
        }

        void CloseCallbacks()
        {
            if (arena != IntPtr.Zero)
            {
                ReleaseArena();
            }
            arenaTop = 0;
            if (overflow != null)
            {
                Marshal.FreeHGlobal((IntPtr)overflow);
                overflow = null;
                overflowCapacity = 0;
            }
            overflowTop = 0;
            callbacks = new LuaCallback[0];
            callbackCount = freeCount = 0;
            hostException = null;
            borrowedTop = 0;
            unrefCount = 0;  // the state is closed: nothing left to release
        }

        // A borrowed handle of argument i of the running host function.
        internal int BorrowArg(int i, LuaType kind)
        {
            xlua_value* v = stackalloc xlua_value[4];
            v[0].len = i + 1;  // its stack index
            v[0].i = 0;
            Op(XLUA_HANDLE_REF, v);
            int slot = AddHandle(kind, (int)v[2].i, SlotBorrowed, kind == LuaType.Coroutine ? v[2].len : -1);
            if (borrowedTop == borrowed.Length)
            {
                Array.Resize(ref borrowed, Math.Max(8, borrowedTop * 2));
            }
            borrowed[borrowedTop++] = slot;
            return slot;
        }

        internal int SlotGeneration(int slot)
        {
            return slots[slot].generation;
        }

        // The end of a host function's frame: its borrowed handles become invalid now, and their
        // native references are released later (ReleaseUnrefs), once the trampoline has pushed the results.
        void EndBorrowed(int mark)
        {
            while (borrowedTop > mark)
            {
                int slot = borrowed[--borrowedTop];
                if (unrefCount == unrefs.Length)
                {
                    Array.Resize(ref unrefs, Math.Max(8, unrefCount * 2));
                }
                unrefs[unrefCount++] = slots[slot].nativeRef;
                FreeSlot(slot);
            }
        }

        void ReleaseUnrefs()
        {
            while (unrefCount > 0)
            {
                LuaAPI.xlua_core_unref(L, unrefs[--unrefCount]);
            }
        }

        // The exception of a host function, when the error that reached C# is
        // the one it raised (XLUA_CALLF_HOST_ERROR).
        Exception TakeHostException(int flags)
        {
            if ((flags & XLUA_CALLF_HOST_ERROR) == 0)
            {
                return null;
            }
            Exception e = hostException;
            hostException = null;
            return e;
        }

        // ---- the dispatcher ----

        // The script whose call runs on this thread, set by every entry
        // (LuaScript.Raw): the dispatcher's fast way from the host pointer to the
        // script. The weak GCHandle's Target, the fallback, cost about 28 ns per
        // call on Mono (macOS arm64). Entries nest in the order they run, so the
        // innermost one is the script whose Lua code calls; the host pointer is
        // checked anyway.
        [ThreadStatic] static LuaScript active;

        // The trampoline's only way into C# (core-luascript.md, section 3.7): a
        // static method, as IL2CPP requires for native callbacks, that never
        // lets an exception out. One try block and no finally: both cost time
        // on every call under Mono.
        [MonoPInvokeCallback(typeof(xlua_core_dispatch))]
        static void Dispatch(IntPtr host, int id, xlua_core_frame* frame)
        {
            LuaScript script = active;
            int arenaMark = 0, overflowMark = 0, borrowedMark = 0;
            try
            {
                if (script == null || GCHandle.ToIntPtr(script.self) != host)
                {
                    script = GCHandle.FromIntPtr(host).Target as LuaScript;
                    if (script == null)
                    {
                        Fallback(frame);
                        return;
                    }
                }
                arenaMark = script.arenaTop;
                overflowMark = script.overflowTop;
                borrowedMark = script.borrowedTop;
                if (script.unrefCount > 0)
                {
                    script.ReleaseUnrefs();  // what earlier frames borrowed; their results were pushed long ago
                }
                if (arenaMark == 0 && script.arenaSize > ArenaKeep)
                {
                    script.ReleaseArena();  // the outermost host function: what a large one left was pushed long ago
                }
                LuaCallback callback = id > 0 && id <= script.callbackCount ? script.callbacks[id - 1] : null;
                if (callback == null)
                {
                    script.Fail(frame, XLUA_FRAME_ERROR_WHERE, "attempt to call a host function that was unregistered", 0);
                }
                else
                {
                    var call = new LuaCall(script, frame, overflowMark);
                    callback(ref call);
                    call.Finish();
                }
            }
            catch (Exception e)
            {
                try
                {
                    if (script != null)
                    {
                        script.Report(frame, e);
                    }
                    else
                    {
                        Fallback(frame);
                    }
                }
                catch (Exception)
                {
                    Fallback(frame);  // even the error could not be reported
                }
            }
            if (script != null)
            {
                // What this frame wrote stays in place until the trampoline has pushed
                // it: no C# runs before that.
                script.arenaTop = arenaMark;
                script.overflowTop = overflowMark;
                if (script.borrowedTop > borrowedMark)
                {
                    script.EndBorrowed(borrowedMark);
                }
            }
        }

        static void Fallback(xlua_core_frame* frame)
        {
            frame->nresults = 0;
            frame->status = XLUA_FRAME_ERROR_WHERE;
            frame->error = fallbackMessage;
            frame->error_len = fallbackLength;
        }

        void ReleaseArena()
        {
            Marshal.FreeHGlobal(arena);
            arena = IntPtr.Zero;
            arenaSize = 0;
        }

        // An exception of a host function as the error the trampoline raises.
        void Report(xlua_core_frame* frame, Exception e)
        {
            var type = e as LuaTypeException;
            if (type != null && type.Arg > 0 && type.ArgFrame == frame->id)
            {
                // "bad argument #1 to 'f' (number expected, got string)"
                Fail(frame, type.IsTypeError ? XLUA_FRAME_TYPEERROR : XLUA_FRAME_ARGERROR, type.LuaMessage, type.Arg);
                return;
            }
            if (e is LuaMemoryLimitException)
            {
                frame->nresults = 0;
                frame->status = XLUA_FRAME_MEMERR;  // a nested call ran out of memory: Lua's memory error
                return;
            }
            hostException = e;
            if (e is LuaRuntimeException || e is LuaSyntaxException || e is LuaLimitException)
            {
                Fail(frame, XLUA_FRAME_ERROR, e.Message, 0);  // a nested call's error: it has its position
            }
            else
            {
                Fail(frame, XLUA_FRAME_ERROR_WHERE, e is LuaException ? e.Message : e.GetType().Name + ": " + e.Message, 0);
            }
        }

        void Fail(xlua_core_frame* frame, int status, string message, int arg)
        {
            message = message ?? "";
            int n = Encoding.UTF8.GetByteCount(message);
            int offset = ArenaAlloc(n + 1, 1);
            byte* p = (byte*)arena + offset;
            fixed (char* c = message)
            {
                Encoding.UTF8.GetBytes(c, message.Length, p, n);
            }
            p[n] = 0;  // luaL_argerror and luaL_typeerror take C strings
            frame->nresults = 0;
            frame->error = (IntPtr)p;
            frame->error_len = n;
            frame->error_arg = arg;
            frame->status = status;
        }

        // ---- the arena ----

        internal byte* ArenaBase { get { return (byte*)arena; } }

        // A table, function or buffer result: the native handle of a handle of this script, which keeps owning it.
        internal int HandleResult(LuaValue v)
        {
            return ValueHandle(v, WhatResult);  // LuaTableRef.cs
        }

        // Room for size bytes at the given alignment (a power of two); returns
        // the offset, which stays valid when the arena grows.
        internal int ArenaAlloc(int size, int align)
        {
            int start = (arenaTop + align - 1) & ~(align - 1);
            if (size < 0 || size > int.MaxValue - start)
            {
                throw new OutOfMemoryException("the results of a host function are larger than 2 GB");
            }
            int end = start + size;
            if (end > arenaSize)
            {
                int grown = arenaSize > int.MaxValue / 2 ? int.MaxValue : Math.Max(256, arenaSize * 2);
                int newSize = Math.Max(end, grown);
                arena = arena == IntPtr.Zero ? Marshal.AllocHGlobal(newSize) : Marshal.ReAllocHGlobal(arena, (IntPtr)newSize);
                arenaSize = newSize;
            }
            arenaTop = end;
            return start;
        }

        internal ref xlua_value OverflowSlot(int index)
        {
            if (index >= overflowCapacity)
            {
                if (index >= int.MaxValue / 2 / sizeof(xlua_value))
                {
                    throw new OutOfMemoryException("too many results");
                }
                int n = Math.Max(16, Math.Max(index + 1, overflowCapacity * 2));
                overflow = (xlua_value*)(overflow == null ? Marshal.AllocHGlobal(n * sizeof(xlua_value))
                    : Marshal.ReAllocHGlobal((IntPtr)overflow, (IntPtr)(n * sizeof(xlua_value))));
                overflowCapacity = n;
            }
            if (index >= overflowTop)
            {
                overflowTop = index + 1;
            }
            return ref overflow[index];
        }

        // ---- typed shorthands ----

        // The arguments and results of the typed shorthands, converted through a
        // TypedReference by the kind of LuaKinds (LuaScript.cs): the generic code per type is
        // only Arg<T> and Put<T>, no reader or writer delegate per type (section 8.12).
        internal static class HostTypes
        {
            const string Supported = "long, int, double, float, bool, string, byte[], LuaValue, LuaTableRef, LuaFunctionRef or LuaBufferRef";

            static bool Supports(int kind)
            {
                return kind != LuaKinds.None && kind != LuaKinds.Coroutine;
            }

            internal static void CheckArg(int kind, Type t)
            {
                if (!Supports(kind))
                {
                    throw new NotSupportedException(t + " is not a host function argument type (" + Supported + ")");
                }
            }

            internal static void CheckResult(int kind, Type t)
            {
                if (!Supports(kind))
                {
                    throw new NotSupportedException(t + " is not a host function result type (" + Supported + ")");
                }
            }

            internal static T Arg<T>(ref LuaCall c, int i)
            {
                T value = default(T);
                ArgInto(ref c, i, LuaKind<T>.Code, __makeref(value));
                return value;
            }

            internal static void Put<T>(ref LuaCall c, T value)
            {
                PutFrom(ref c, LuaKind<T>.Code, __makeref(value));
            }

            static void ArgInto(ref LuaCall c, int i, int kind, TypedReference value)
            {
                switch (kind)
                {
                    case LuaKinds.Long: __refvalue(value, long) = c.GetLong(i); return;
                    case LuaKinds.Int: __refvalue(value, int) = c.GetInt(i); return;
                    case LuaKinds.Double: __refvalue(value, double) = c.GetDouble(i); return;
                    case LuaKinds.Float: __refvalue(value, float) = c.GetFloat(i); return;
                    case LuaKinds.Bool: __refvalue(value, bool) = c.GetBool(i); return;
                    case LuaKinds.String: __refvalue(value, string) = c.GetString(i); return;
                    case LuaKinds.Bytes: __refvalue(value, byte[]) = c.GetBytes(i); return;
                    case LuaKinds.Value: __refvalue(value, LuaValue) = c.Get(i); return;
                    case LuaKinds.Table: __refvalue(value, LuaTableRef) = c.GetTable(i); return;
                    case LuaKinds.Function: __refvalue(value, LuaFunctionRef) = c.GetFunction(i); return;
                    case LuaKinds.Buffer: __refvalue(value, LuaBufferRef) = c.GetBuffer(i); return;
                    default: CheckArg(kind, __reftype(value)); return;
                }
            }

            static void PutFrom(ref LuaCall c, int kind, TypedReference value)
            {
                switch (kind)
                {
                    case LuaKinds.Long: c.Return(__refvalue(value, long)); return;
                    case LuaKinds.Int: c.Return((long)__refvalue(value, int)); return;
                    case LuaKinds.Double: c.Return(__refvalue(value, double)); return;
                    case LuaKinds.Float: c.Return((double)__refvalue(value, float)); return;
                    case LuaKinds.Bool: c.Return(__refvalue(value, bool)); return;
                    case LuaKinds.String: c.Return(__refvalue(value, string)); return;
                    case LuaKinds.Bytes:
                        {
                            byte[] b = __refvalue(value, byte[]);
                            if (b == null) c.ReturnNil(); else c.Return(new ReadOnlySpan<byte>(b));
                            return;
                        }
                    case LuaKinds.Value: c.Return(__refvalue(value, LuaValue)); return;
                    case LuaKinds.Table: c.Return(__refvalue(value, LuaTableRef)); return;
                    case LuaKinds.Function: c.Return(__refvalue(value, LuaFunctionRef)); return;
                    case LuaKinds.Buffer: c.Return(__refvalue(value, LuaBufferRef)); return;
                    default: CheckResult(kind, __reftype(value)); return;
                }
            }
        }

        sealed class ActionThunk
        {
            readonly Action fn;
            internal ActionThunk(Action fn) { if (fn == null) throw new ArgumentNullException("fn"); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(); }
        }

        sealed class ActionThunk<A1>
        {
            readonly Action<A1> fn;
            internal ActionThunk(Action<A1> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(HostTypes.Arg<A1>(ref c, 0)); }
        }

        sealed class ActionThunk<A1, A2>
        {
            readonly Action<A1, A2> fn;
            internal ActionThunk(Action<A1, A2> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1)); }
        }

#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        sealed class ActionThunk<A1, A2, A3>
        {
            readonly Action<A1, A2, A3> fn;
            internal ActionThunk(Action<A1, A2, A3> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); HostTypes.CheckArg(LuaKind<A3>.Code, typeof(A3)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1), HostTypes.Arg<A3>(ref c, 2)); }
        }

        sealed class ActionThunk<A1, A2, A3, A4>
        {
            readonly Action<A1, A2, A3, A4> fn;
            internal ActionThunk(Action<A1, A2, A3, A4> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); HostTypes.CheckArg(LuaKind<A3>.Code, typeof(A3)); HostTypes.CheckArg(LuaKind<A4>.Code, typeof(A4)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1), HostTypes.Arg<A3>(ref c, 2), HostTypes.Arg<A4>(ref c, 3)); }
        }

#endif
        sealed class FuncThunk<R>
        {
            readonly Func<R> fn;
            internal FuncThunk(Func<R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckResult(LuaKind<R>.Code, typeof(R)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { HostTypes.Put(ref c, fn()); }
        }

        sealed class FuncThunk<A1, R>
        {
            readonly Func<A1, R> fn;
            internal FuncThunk(Func<A1, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckResult(LuaKind<R>.Code, typeof(R)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { HostTypes.Put(ref c, fn(HostTypes.Arg<A1>(ref c, 0))); }
        }

        sealed class FuncThunk<A1, A2, R>
        {
            readonly Func<A1, A2, R> fn;
            internal FuncThunk(Func<A1, A2, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); HostTypes.CheckResult(LuaKind<R>.Code, typeof(R)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { HostTypes.Put(ref c, fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1))); }
        }

#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        sealed class FuncThunk<A1, A2, A3, R>
        {
            readonly Func<A1, A2, A3, R> fn;
            internal FuncThunk(Func<A1, A2, A3, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); HostTypes.CheckArg(LuaKind<A3>.Code, typeof(A3)); HostTypes.CheckResult(LuaKind<R>.Code, typeof(R)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { HostTypes.Put(ref c, fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1), HostTypes.Arg<A3>(ref c, 2))); }
        }

        sealed class FuncThunk<A1, A2, A3, A4, R>
        {
            readonly Func<A1, A2, A3, A4, R> fn;
            internal FuncThunk(Func<A1, A2, A3, A4, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.CheckArg(LuaKind<A1>.Code, typeof(A1)); HostTypes.CheckArg(LuaKind<A2>.Code, typeof(A2)); HostTypes.CheckArg(LuaKind<A3>.Code, typeof(A3)); HostTypes.CheckArg(LuaKind<A4>.Code, typeof(A4)); HostTypes.CheckResult(LuaKind<R>.Code, typeof(R)); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { HostTypes.Put(ref c, fn(HostTypes.Arg<A1>(ref c, 0), HostTypes.Arg<A2>(ref c, 1), HostTypes.Arg<A3>(ref c, 2), HostTypes.Arg<A4>(ref c, 3))); }
        }
#endif
    }

    /// <summary>
    /// One call of a host function (<see cref="LuaCallback"/>): its arguments,
    /// read without a P/Invoke and, except for <see cref="GetString"/> and
    /// <see cref="Get"/> of a string, without allocating, and its results.
    /// Valid only while the host function runs. Argument indices are 0-based;
    /// a wrong type throws <see cref="LuaTypeException"/>, which becomes Lua's
    /// "bad argument #1 to 'f' (number expected, got string)".
    /// </summary>
    public unsafe ref struct LuaCall
    {
        readonly LuaScript script;
        readonly xlua_core_frame* frame;
        readonly int overflowBase;

        internal LuaCall(LuaScript script, xlua_core_frame* frame, int overflowBase)
        {
            this.script = script;
            this.frame = frame;
            this.overflowBase = overflowBase;
        }

        xlua_core_frame* F
        {
            get
            {
                if (frame == null)
                {
                    throw new InvalidOperationException("this LuaCall does not belong to a running host function");
                }
                return frame;
            }
        }

        /// <summary>The script that called the host function.</summary>
        public LuaScript Script { get { xlua_core_frame* f = F; return script; } }

        /// <summary>How many arguments the script passed.</summary>
        public int ArgCount { get { return F->nargs; } }

        /// <summary>Whether <see cref="Yield"/> is allowed: the script called the host function from a coroutine that can yield.</summary>
        public bool CanYield { get { return F->yieldable != 0; } }

        xlua_value* Arg(int i)
        {
            xlua_core_frame* f = F;
            if (i < 0)
            {
                throw new ArgumentOutOfRangeException("i", i, "argument indices start at 0");
            }
            return i < f->nargs ? f->args + i : null;
        }

        /// <summary>The type of argument i; <see cref="LuaType.Nil"/> for nil and for an argument not passed.</summary>
        public LuaType ArgType(int i)
        {
            xlua_value* v = Arg(i);
            if (v == null)
            {
                return LuaType.Nil;
            }
            return v->type >= 0 && v->type <= (int)LuaType.Coroutine ? (LuaType)v->type : LuaType.Other;
        }

        /// <summary>Lua's truth of argument i: false for nil, false and an argument not passed.</summary>
        public bool GetBool(int i)
        {
            xlua_value* v = Arg(i);
            return v != null && LuaScript.ReadBool(ref *v);
        }

        /// <summary>An integer, or a float with an exact integer value.</summary>
        public long GetLong(int i)
        {
            xlua_value* v = Arg(i);
            if (v != null)
            {
                if (v->type == (int)LuaType.Integer)
                {
                    return v->i;
                }
                if (v->type == (int)LuaType.Number)
                {
                    long l;
                    if (LuaScript.FloatToLong(v->n, out l))
                    {
                        return l;
                    }
                    throw ArgError(i, "number has no integer representation", LuaType.Integer);
                }
            }
            throw TypeError(i, LuaType.Integer);
        }

        internal int GetInt(int i)
        {
            long l = GetLong(i);
            if (l < int.MinValue || l > int.MaxValue)
            {
                throw ArgError(i, "integer out of the range of int", LuaType.Integer);
            }
            return (int)l;
        }

        /// <summary>A number: an integer converts to double.</summary>
        public double GetDouble(int i)
        {
            xlua_value* v = Arg(i);
            if (v != null)
            {
                if (v->type == (int)LuaType.Number)
                {
                    return v->n;
                }
                if (v->type == (int)LuaType.Integer)
                {
                    return v->i;
                }
            }
            throw TypeError(i, LuaType.Number);
        }

        internal float GetFloat(int i)
        {
            double d = GetDouble(i);
            if ((d > float.MaxValue || d < -float.MaxValue) && !double.IsInfinity(d))
            {
                throw ArgError(i, "number out of the range of float", LuaType.Number);
            }
            return (float)d;
        }

        /// <summary>A string argument, decoded as UTF-8 (this allocates the string). Numbers are not converted.</summary>
        public string GetString(int i)
        {
            xlua_value* v = Arg(i);
            if (v == null || v->type != (int)LuaType.String)
            {
                throw TypeError(i, LuaType.String);
            }
            return LuaScript.ReadString(ref *v);
        }

        /// <summary>The bytes of a string argument, without a copy; valid until the host function returns.</summary>
        public ReadOnlySpan<byte> GetUtf8(int i)
        {
            xlua_value* v = Arg(i);
            if (v == null || v->type != (int)LuaType.String)
            {
                throw TypeError(i, LuaType.String);
            }
            return new ReadOnlySpan<byte>((void*)v->s, v->len);
        }

        /// <summary>
        /// The bytes of a buffer argument (<see cref="LuaLibs.Buffer"/>), without a
        /// copy: writes are seen by the script. Valid until the host function
        /// returns; keep a buffer longer through a <see cref="LuaBufferRef"/>.
        /// </summary>
        public Span<byte> GetBufferSpan(int i)
        {
            xlua_value* v = Arg(i);
            if (v == null || v->type != (int)LuaType.Buffer)
            {
                throw TypeError(i, LuaType.Buffer);
            }
            return new Span<byte>((void*)v->s, v->len);
        }

        internal byte[] GetBytes(int i)
        {
            xlua_value* v = Arg(i);
            if (v != null && v->type == (int)LuaType.Nil)
            {
                return null;
            }
            if (v == null || v->type != (int)LuaType.String)
            {
                throw TypeError(i, LuaType.String);
            }
            return LuaScript.ReadBytes(ref *v);
        }

        /// <summary>Whether argument i is a string equal to <paramref name="text"/>'s UTF-8, compared without decoding.</summary>
        public bool ArgEquals(int i, string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }
            xlua_value* v = Arg(i);
            return v != null && v->type == (int)LuaType.String && TextEquals((byte*)v->s, v->len, text);
        }

        // Whether n bytes at s are the UTF-8 of text: ASCII compared as it is, other text encoded.
        internal static bool TextEquals(byte* s, int n, string text)
        {
            int k = 0;
            for (int j = 0; j < text.Length; j++)
            {
                char c = text[j];
                if (c >= 0x80)
                {
                    return Utf8Equals(s, n, text);  // not ASCII: compare the encoding
                }
                if (k >= n || s[k] != c)
                {
                    return false;
                }
                k++;
            }
            return k == n;
        }

        static bool Utf8Equals(byte* s, int n, string text)
        {
            int count = Encoding.UTF8.GetByteCount(text);
            if (count != n)
            {
                return false;
            }
            byte[] heap = count > 256 ? new byte[count] : null;
            byte* stack = stackalloc byte[heap == null ? count : 1];
            fixed (char* c = text)
            fixed (byte* h = heap)
            {
                byte* b = heap == null ? stack : h;
                Encoding.UTF8.GetBytes(c, text.Length, b, count);
                for (int j = 0; j < count; j++)
                {
                    if (b[j] != s[j])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Argument i as a <see cref="LuaValue"/>: a string is decoded; a table, function, buffer or coroutine is a borrowed handle.</summary>
        public LuaValue Get(int i)
        {
            xlua_value* v = Arg(i);
            if (v == null)
            {
                return LuaValue.Nil;
            }
            if (v->type == (int)LuaType.Table || v->type == (int)LuaType.Function || v->type == (int)LuaType.Buffer
                || v->type == (int)LuaType.Coroutine)
            {
                LuaType kind = (LuaType)v->type;
                int slot = script.BorrowArg(i, kind);
                return LuaValue.Handle(kind, script, slot, script.SlotGeneration(slot));
            }
            return LuaScript.ToValue(ref *v);
        }

        /// <summary>
        /// A table argument as a borrowed handle: valid until the host function
        /// returns (then it throws <see cref="ObjectDisposedException"/>);
        /// <see cref="LuaTableRef.Retain"/> keeps it longer. Raw access, no
        /// metamethods. Costs one P/Invoke.
        /// </summary>
        public LuaTableRef GetTable(int i)
        {
            Check(i, LuaType.Table);
            int slot = script.BorrowArg(i, LuaType.Table);
            return new LuaTableRef(script, slot, script.SlotGeneration(slot));
        }

        /// <summary>A function argument as a borrowed handle (as <see cref="GetTable"/>); call it with its own Call methods.</summary>
        public LuaFunctionRef GetFunction(int i)
        {
            Check(i, LuaType.Function);
            int slot = script.BorrowArg(i, LuaType.Function);
            return new LuaFunctionRef(script, slot, script.SlotGeneration(slot));
        }

        /// <summary>A buffer argument as a borrowed handle (as <see cref="GetTable"/>); <see cref="GetBufferSpan"/> needs no handle.</summary>
        public LuaBufferRef GetBuffer(int i)
        {
            Check(i, LuaType.Buffer);
            int slot = script.BorrowArg(i, LuaType.Buffer);
            return new LuaBufferRef(script, slot, script.SlotGeneration(slot));
        }

        /// <summary>
        /// A coroutine argument (<c>coroutine.create(f)</c>) as a borrowed
        /// handle (as <see cref="GetTable"/>): resume it from C#, or keep it
        /// with <see cref="LuaCoroutine.Retain"/>.
        /// </summary>
        public LuaCoroutine GetCoroutine(int i)
        {
            Check(i, LuaType.Coroutine);
            int slot = script.BorrowArg(i, LuaType.Coroutine);
            return script.CoroutineOf(slot, script.SlotGeneration(slot));
        }

        void Check(int i, LuaType expected)
        {
            xlua_value* v = Arg(i);
            if (v == null || v->type != (int)expected)
            {
                throw TypeError(i, expected);
            }
        }

        /// <summary><see cref="GetLong"/>, or <paramref name="fallback"/> for nil or an argument not passed.</summary>
        public long OptLong(int i, long fallback)
        {
            return ArgType(i) == LuaType.Nil ? fallback : GetLong(i);
        }

        /// <summary><see cref="GetDouble"/>, or <paramref name="fallback"/> for nil or an argument not passed.</summary>
        public double OptDouble(int i, double fallback)
        {
            return ArgType(i) == LuaType.Nil ? fallback : GetDouble(i);
        }

        /// <summary><see cref="GetString"/>, or <paramref name="fallback"/> for nil or an argument not passed.</summary>
        public string OptString(int i, string fallback)
        {
            return ArgType(i) == LuaType.Nil ? fallback : GetString(i);
        }

        LuaTypeException TypeError(int i, LuaType expected)
        {
            xlua_core_frame* f = F;
            string name = expected == LuaType.Integer ? "number" : LuaValue.Name(expected);
            string got = i < f->nargs ? LuaValue.Name(ArgType(i)) : "no value";
            var e = new LuaTypeException("bad argument #" + (i + 1) + " (" + name + " expected, got " + got + ")", expected, ArgType(i));
            e.ArgFrame = f->id;
            e.Arg = i + 1;
            e.IsTypeError = true;
            e.LuaMessage = name;
            return e;
        }

        LuaTypeException ArgError(int i, string message, LuaType expected)
        {
            xlua_core_frame* f = F;
            var e = new LuaTypeException("bad argument #" + (i + 1) + " (" + message + ")", expected, ArgType(i));
            e.ArgFrame = f->id;
            e.Arg = i + 1;
            e.LuaMessage = message;
            return e;
        }

        // ---- results ----

        ref xlua_value Next()
        {
            xlua_core_frame* f = F;
            int n = f->nresults;
            if (n == int.MaxValue)
            {
                throw new InvalidOperationException("too many results");
            }
            f->nresults = n + 1;
            if (n < f->results_cap)
            {
                return ref f->results[n];
            }
            return ref script.OverflowSlot(overflowBase + n - f->results_cap);
        }

        /// <summary>Adds a boolean result.</summary>
        public void Return(bool v)
        {
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.Boolean;
            r.len = 0;
            r.i = v ? 1 : 0;
        }

        /// <summary>Adds an integer result.</summary>
        public void Return(long v)
        {
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.Integer;
            r.len = 0;
            r.i = v;
        }

        /// <summary>Adds a float result.</summary>
        public void Return(double v)
        {
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.Number;
            r.len = 0;
            r.n = v;
        }

        /// <summary>Adds a string result (null is nil), encoded as UTF-8 into reused native memory.</summary>
        public void Return(string v)
        {
            if (v == null)
            {
                ReturnNil();
                return;
            }
            int n = Encoding.UTF8.GetByteCount(v);
            int offset = script.ArenaAlloc(n, 1);
            fixed (char* c = v)
            {
                Encoding.UTF8.GetBytes(c, v.Length, script.ArenaBase + offset, n);
            }
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.String;
            r.len = n;
            r.i = offset;  // Finish turns the offset into a pointer
        }

        /// <summary>Adds a string result with these bytes (any bytes: a Lua string need not be UTF-8).</summary>
        public void Return(ReadOnlySpan<byte> utf8)
        {
            int offset = script.ArenaAlloc(utf8.Length, 1);
            utf8.CopyTo(new Span<byte>(script.ArenaBase + offset, utf8.Length));
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.String;
            r.len = utf8.Length;
            r.i = offset;
        }

        /// <summary>Adds a result of any type a <see cref="LuaValue"/> holds; a table, function, buffer or coroutine handle stays the caller's.</summary>
        public void Return(LuaValue v)
        {
            switch (v.Type)
            {
                case LuaType.Nil:
                    ReturnNil();
                    return;
                case LuaType.Boolean:
                case LuaType.Integer:
                case LuaType.Number:
                    ref xlua_value r = ref Next();
                    r.type = (int)v.Type;
                    r.len = 0;
                    r.i = v.Bits;
                    return;
                case LuaType.String:
                    var bytes = v.Object as byte[];
                    if (bytes != null)
                    {
                        Return(new ReadOnlySpan<byte>(bytes));
                    }
                    else
                    {
                        Return((string)v.Object);
                    }
                    return;
                case LuaType.Buffer:
                case LuaType.Table:
                case LuaType.Function:
                case LuaType.Coroutine:
                    {
                        if (!(v.Object is LuaScript))
                        {
                            throw new NotSupportedException("a " + LuaValue.Name(v.Type) + " without a handle cannot be returned");
                        }
                        // The value of a handle of this script, which keeps its handle.
                        int handle = script.HandleResult(v);
                        ref xlua_value b = ref Next();
                        b.type = (int)v.Type;
                        b.len = 0;
                        b.i = handle;
                    }
                    return;
                default:
                    throw new NotSupportedException("a " + LuaValue.Name(v.Type) + " without a handle cannot be returned");
            }
        }

        /// <summary>Adds a buffer result: the buffer of the handle, which stays the caller's to dispose.</summary>
        public void Return(LuaBufferRef v)
        {
            Return((LuaValue)v);
        }

        /// <summary>Adds a table result: the table of the handle (owning or borrowed), which stays the caller's to dispose.</summary>
        public void Return(LuaTableRef v)
        {
            Return((LuaValue)v);
        }

        /// <summary>Adds a function result: the function of the handle, which stays the caller's to dispose.</summary>
        public void Return(LuaFunctionRef v)
        {
            Return((LuaValue)v);
        }

        /// <summary>Adds a nil result.</summary>
        public void ReturnNil()
        {
            ref xlua_value r = ref Next();
            r.type = (int)LuaType.Nil;
            r.len = 0;
            r.i = 0;
        }

        /// <summary>
        /// Suspends the coroutine that called the host function once it
        /// returns: its results are the yielded values, and the values of the
        /// next resume become the results of the call in Lua. Throws
        /// <see cref="InvalidOperationException"/> when the caller cannot
        /// yield (<see cref="CanYield"/>: not in a coroutine, or across a
        /// C# call into the script).
        /// </summary>
        public void Yield()
        {
            xlua_core_frame* f = F;
            if (f->yieldable == 0)
            {
                throw new InvalidOperationException("LuaCall.Yield: the host function was not called from a coroutine that can yield");
            }
            f->status = 1;  // XLUA_FRAME_YIELD
        }

        // After the host function returned: results beyond the inline room
        // move into the arena, and string offsets become pointers.
        internal void Finish()
        {
            xlua_core_frame* f = frame;
            int n = f->nresults;
            if (n > f->results_cap)
            {
                int cap = f->results_cap;
                int offset = script.ArenaAlloc(n * sizeof(xlua_value), 8);
                xlua_value* all = (xlua_value*)(script.ArenaBase + offset);
                for (int i = 0; i < cap; i++)
                {
                    all[i] = f->results[i];
                }
                for (int i = cap; i < n; i++)
                {
                    all[i] = script.OverflowSlot(overflowBase + i - cap);
                }
                f->results = all;
                f->results_cap = n;
            }
            byte* b = script.ArenaBase;
            for (int i = 0; i < n; i++)
            {
                if (f->results[i].type == (int)LuaType.String)
                {
                    f->results[i].s = (IntPtr)(b + f->results[i].i);
                }
            }
        }
    }
}
#endif
