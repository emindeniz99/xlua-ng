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
        xlua_value[] overflow = new xlua_value[0];  // results beyond the trampoline's inline 8, by frame
        int overflowTop;
        Exception hostException;  // the exception of the last host function that raised one

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
        public void Register<A1, A2, A3>(string name, Action<A1, A2, A3> fn) { Register(name, new ActionThunk<A1, A2, A3>(fn).Invoke); }
        public void Register<A1, A2, A3, A4>(string name, Action<A1, A2, A3, A4> fn) { Register(name, new ActionThunk<A1, A2, A3, A4>(fn).Invoke); }
        public void Register<R>(string name, Func<R> fn) { Register(name, new FuncThunk<R>(fn).Invoke); }
        public void Register<A1, R>(string name, Func<A1, R> fn) { Register(name, new FuncThunk<A1, R>(fn).Invoke); }
        public void Register<A1, A2, R>(string name, Func<A1, A2, R> fn) { Register(name, new FuncThunk<A1, A2, R>(fn).Invoke); }
        public void Register<A1, A2, A3, R>(string name, Func<A1, A2, A3, R> fn) { Register(name, new FuncThunk<A1, A2, A3, R>(fn).Invoke); }
        public void Register<A1, A2, A3, A4, R>(string name, Func<A1, A2, A3, A4, R> fn) { Register(name, new FuncThunk<A1, A2, A3, A4, R>(fn).Invoke); }

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

        static int SetFunction(IntPtr state, string name, int id, out int old)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            fixed (byte* p = utf8)
            {
                return LuaAPI.xlua_core_register(state, 0, (IntPtr)p, utf8.Length, id, out old);
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
            callbacks = new LuaCallback[0];
            callbackCount = freeCount = 0;
            hostException = null;
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
            int arenaMark = 0, overflowMark = 0;
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

        // A buffer result: the native handle of a LuaBufferRef of this script, which keeps owning it.
        internal int BufferResult(LuaValue v, int index)
        {
            return BufferArgument(v, index);  // LuaBufferRef.cs
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
            if (index >= overflow.Length)
            {
                Array.Resize(ref overflow, Math.Max(16, Math.Max(index + 1, overflow.Length * 2)));
            }
            if (index >= overflowTop)
            {
                overflowTop = index + 1;
            }
            return ref overflow[index];
        }

        // ---- typed shorthands ----

        internal delegate T ArgReader<T>(ref LuaCall call, int i);
        internal delegate void ResultWriter<T>(ref LuaCall call, T value);

        // One reader and one writer per type, chosen once per T, as LuaRead<T> for Call<T>.
        internal static class ArgOf<T>
        {
            internal static readonly ArgReader<T> Get = (ArgReader<T>)HostTypes.Reader(typeof(T));
        }

        internal static class ResultOf<T>
        {
            internal static readonly ResultWriter<T> Put = (ResultWriter<T>)HostTypes.Writer(typeof(T));
        }

        internal static class HostTypes
        {
            const string Supported = "long, int, double, float, bool, string, byte[] or LuaValue; LuaBufferRef as a result";

            internal static void Arg<T>()
            {
                if (ArgOf<T>.Get == null)
                {
                    throw new NotSupportedException(typeof(T) + " is not a host function argument type (" + Supported + ")");
                }
            }

            internal static void Result<T>()
            {
                if (ResultOf<T>.Put == null)
                {
                    throw new NotSupportedException(typeof(T) + " is not a host function result type (" + Supported + ")");
                }
            }

            internal static Delegate Reader(Type t)
            {
                if (t == typeof(long)) return (ArgReader<long>)GetLong;
                if (t == typeof(int)) return (ArgReader<int>)GetInt;
                if (t == typeof(double)) return (ArgReader<double>)GetDouble;
                if (t == typeof(float)) return (ArgReader<float>)GetFloat;
                if (t == typeof(bool)) return (ArgReader<bool>)GetBool;
                if (t == typeof(string)) return (ArgReader<string>)GetString;
                if (t == typeof(byte[])) return (ArgReader<byte[]>)GetBytes;
                if (t == typeof(LuaValue)) return (ArgReader<LuaValue>)GetValue;
                return null;
            }

            internal static Delegate Writer(Type t)
            {
                if (t == typeof(long)) return (ResultWriter<long>)PutLong;
                if (t == typeof(int)) return (ResultWriter<int>)PutInt;
                if (t == typeof(double)) return (ResultWriter<double>)PutDouble;
                if (t == typeof(float)) return (ResultWriter<float>)PutFloat;
                if (t == typeof(bool)) return (ResultWriter<bool>)PutBool;
                if (t == typeof(string)) return (ResultWriter<string>)PutString;
                if (t == typeof(byte[])) return (ResultWriter<byte[]>)PutBytes;
                if (t == typeof(LuaValue)) return (ResultWriter<LuaValue>)PutValue;
                if (t == typeof(LuaBufferRef)) return (ResultWriter<LuaBufferRef>)PutBuffer;
                return null;
            }

            static long GetLong(ref LuaCall c, int i) { return c.GetLong(i); }
            static int GetInt(ref LuaCall c, int i) { return c.GetInt(i); }
            static double GetDouble(ref LuaCall c, int i) { return c.GetDouble(i); }
            static float GetFloat(ref LuaCall c, int i) { return c.GetFloat(i); }
            static bool GetBool(ref LuaCall c, int i) { return c.GetBool(i); }
            static string GetString(ref LuaCall c, int i) { return c.GetString(i); }
            static byte[] GetBytes(ref LuaCall c, int i) { return c.GetBytes(i); }
            static LuaValue GetValue(ref LuaCall c, int i) { return c.Get(i); }
            static void PutLong(ref LuaCall c, long v) { c.Return(v); }
            static void PutInt(ref LuaCall c, int v) { c.Return((long)v); }
            static void PutDouble(ref LuaCall c, double v) { c.Return(v); }
            static void PutFloat(ref LuaCall c, float v) { c.Return((double)v); }
            static void PutBool(ref LuaCall c, bool v) { c.Return(v); }
            static void PutString(ref LuaCall c, string v) { c.Return(v); }
            static void PutBytes(ref LuaCall c, byte[] v) { if (v == null) c.ReturnNil(); else c.Return(new ReadOnlySpan<byte>(v)); }
            static void PutValue(ref LuaCall c, LuaValue v) { c.Return(v); }
            static void PutBuffer(ref LuaCall c, LuaBufferRef v) { c.Return(v); }
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
            internal ActionThunk(Action<A1> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(ArgOf<A1>.Get(ref c, 0)); }
        }

        sealed class ActionThunk<A1, A2>
        {
            readonly Action<A1, A2> fn;
            internal ActionThunk(Action<A1, A2> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1)); }
        }

        sealed class ActionThunk<A1, A2, A3>
        {
            readonly Action<A1, A2, A3> fn;
            internal ActionThunk(Action<A1, A2, A3> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); HostTypes.Arg<A3>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1), ArgOf<A3>.Get(ref c, 2)); }
        }

        sealed class ActionThunk<A1, A2, A3, A4>
        {
            readonly Action<A1, A2, A3, A4> fn;
            internal ActionThunk(Action<A1, A2, A3, A4> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); HostTypes.Arg<A3>(); HostTypes.Arg<A4>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1), ArgOf<A3>.Get(ref c, 2), ArgOf<A4>.Get(ref c, 3)); }
        }

        sealed class FuncThunk<R>
        {
            readonly Func<R> fn;
            internal FuncThunk(Func<R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Result<R>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { ResultOf<R>.Put(ref c, fn()); }
        }

        sealed class FuncThunk<A1, R>
        {
            readonly Func<A1, R> fn;
            internal FuncThunk(Func<A1, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Result<R>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { ResultOf<R>.Put(ref c, fn(ArgOf<A1>.Get(ref c, 0))); }
        }

        sealed class FuncThunk<A1, A2, R>
        {
            readonly Func<A1, A2, R> fn;
            internal FuncThunk(Func<A1, A2, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); HostTypes.Result<R>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { ResultOf<R>.Put(ref c, fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1))); }
        }

        sealed class FuncThunk<A1, A2, A3, R>
        {
            readonly Func<A1, A2, A3, R> fn;
            internal FuncThunk(Func<A1, A2, A3, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); HostTypes.Arg<A3>(); HostTypes.Result<R>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { ResultOf<R>.Put(ref c, fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1), ArgOf<A3>.Get(ref c, 2))); }
        }

        sealed class FuncThunk<A1, A2, A3, A4, R>
        {
            readonly Func<A1, A2, A3, A4, R> fn;
            internal FuncThunk(Func<A1, A2, A3, A4, R> fn) { if (fn == null) throw new ArgumentNullException("fn"); HostTypes.Arg<A1>(); HostTypes.Arg<A2>(); HostTypes.Arg<A3>(); HostTypes.Arg<A4>(); HostTypes.Result<R>(); this.fn = fn; }
            internal void Invoke(ref LuaCall c) { ResultOf<R>.Put(ref c, fn(ArgOf<A1>.Get(ref c, 0), ArgOf<A2>.Get(ref c, 1), ArgOf<A3>.Get(ref c, 2), ArgOf<A4>.Get(ref c, 3))); }
        }
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
            return v->type >= 0 && v->type <= (int)LuaType.Other ? (LuaType)v->type : LuaType.Other;
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
            if (v == null || v->type != (int)LuaType.String)
            {
                return false;
            }
            byte* s = (byte*)v->s;
            int n = v->len;
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

        /// <summary>Argument i as a <see cref="LuaValue"/> (a string is decoded; tables and functions carry their type only until milestone 8).</summary>
        public LuaValue Get(int i)
        {
            xlua_value* v = Arg(i);
            return v == null ? LuaValue.Nil : LuaScript.ToValue(ref *v);
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

        /// <summary>Adds a result of any type a <see cref="LuaValue"/> holds by value; tables and functions need handles (milestone 8).</summary>
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
                    {
                        // The buffer of a LuaBufferRef of this script, which keeps its handle.
                        int handle = script.BufferResult(v, F->nresults);
                        ref xlua_value b = ref Next();
                        b.type = (int)LuaType.Buffer;
                        b.len = 0;
                        b.i = handle;
                    }
                    return;
                default:
                    throw new NotSupportedException("a " + LuaValue.Name(v.Type) + " without a handle cannot be returned yet");
            }
        }

        /// <summary>Adds a buffer result: the buffer of the handle, which stays the caller's to dispose.</summary>
        public void Return(LuaBufferRef v)
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
