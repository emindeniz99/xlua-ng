/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript: sandboxed Lua 5.4 and 5.5 without the C# bridge
// (docs/design/core-luascript.md, section 2; milestone 6: lifecycle, limits,
// Load, Call, multiple results, exceptions and the thread check; milestone 8:
// environments and handles in calls, LuaTableRef.cs and LuaFunctionRef.cs;
// milestone 9: coroutines, LuaCoroutine.cs). It needs a
// native library with the sandbox: the core preset's, or a bridge library
// built with XLUA_LUASCRIPT=ON. Compiled with the scripting define
// XLUA_LUASCRIPT or XLUA_CORE_ONLY, so that a project without LuaScript pays
// nothing for it and its player links no sandbox import.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using XLua.LuaDLL;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>
    /// Resolves <c>require(name)</c> for a <see cref="LuaScript"/>: true with
    /// the module's text and an optional chunk name (null or empty: the
    /// module name), false when there is no such module.
    /// </summary>
    public delegate bool LuaModuleResolver(string name, out string code, out string chunkName);

    /// <summary>
    /// A sandboxed Lua state: text chunks only, no access to C# objects, the
    /// limits of <see cref="LuaOptions"/>, values without boxing
    /// (<see cref="LuaValue"/>). Use it from the thread that created it;
    /// <see cref="Interrupt"/> works from any thread.
    /// </summary>
    public sealed unsafe partial class LuaScript : IDisposable  // host functions: LuaCall.cs; handles: LuaTableRef.cs, LuaFunctionRef.cs, LuaBufferRef.cs; coroutines: LuaCoroutine.cs
    {
        const int CORE_VERSION_EXPECT = 6;
        internal const int XLUA_OK = 0, XLUA_YIELD = 1;
        const int XLUA_ERRRUN = 2, XLUA_ERRSYNTAX = 3, XLUA_ERRMEM = 4, XLUA_ERRERR = 5;
        const int XLUA_LIMIT_INSTR = 10, XLUA_LIMIT_TIME = 11, XLUA_INTERRUPTED = 12;
        const int XLUA_ERRTYPE = 20, XLUA_ERRSTACK = 21, XLUA_ERRHANDLE = 22, XLUA_ERRCONFIG = 23;
        internal const int XLUA_CALL_GLOBAL = 0, XLUA_CALL_REF = 1, XLUA_CALL_CHUNK = 2, XLUA_CALL_COMPILE = 3;
        internal const int XLUA_CALL_RESUME = 4, XLUA_CALL_CLOSE = 5, XLUA_CALL_START = 8;  // milestone 9
        const int XLUA_CALLF_ERROR_TABLE = 2;
        const int XLUA_LIMITS_CLOCK_CHECK = 1;
        // Every outermost call tracks its running thread, so that Interrupt
        // stops a call also without a limit (docs/design/core-luascript.md, 8.11);
        // LuaOptions.AllowInterrupt = false leaves it out.
        const int XLUA_LIMITS_INTERRUPT = 4;
        // Up to this many arguments or results live on the stack; more use a reused array.
        const int StackValues = 16;
        const LuaLibs NotForScripts = LuaLibs.Io | LuaLibs.Os | LuaLibs.Debug | LuaLibs.Package | LuaLibs.Bit | LuaLibs.Jit | LuaLibs.Ffi;

        IntPtr L;
        GCHandle self;  // weak: the host pointer of the resolver
        int ownerThread;  // the creating thread, or the last caller of SetOwnerThread
        readonly bool checkThread;
        readonly int interruptFlag;  // XLUA_LIMITS_INTERRUPT, or 0 (LuaOptions.AllowInterrupt = false)
        readonly object interruptLock = new object();
        int depth;  // C# entries now running (a resolver can call back in)
        long instructionLimit;
        TimeSpan timeLimit;
        long lastInstructions;
        long hostOomSeen;
        byte[] scratch = new byte[256];         // UTF-8 of the name, the code and string arguments
        // Arguments or results beyond StackValues, in native memory (an array of the struct
        // would cost an IL2CPP player its generic array code). An outgrown buffer stays allocated
        // until Dispose: a running call (or the caller's results) may still use it. Each buffer
        // is twice the size of the one before, so the retired ones add up to less than the last.
        xlua_value* valueBuffer;
        int valueCapacity;
        IntPtr[] retiredBuffers = new IntPtr[0];
        int retiredCount;
        LuaModuleResolver moduleResolver;
        GCHandle resolved;  // the pinned text of the last module the resolver returned

        /// <summary>A script with <see cref="LuaOptions.ScriptDefaults"/>: 64 MiB of memory and 1 s per call.</summary>
        public LuaScript() : this(LuaOptions.ScriptDefaults)
        {
        }

        /// <summary>
        /// A script with these options, which are copied: later changes to
        /// the object do not apply. Throws <see cref="ArgumentException"/> for
        /// the io, os, debug and package libraries, <see cref="NotSupportedException"/>
        /// when the native library has no sandbox or lacks an option, and
        /// <see cref="LuaMemoryLimitException"/> when the memory limit is too
        /// small for a state.
        /// </summary>
        public LuaScript(LuaOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if ((options.Libraries & NotForScripts) != 0)
            {
                throw new ArgumentException("LuaScript cannot open io, os, debug, package, bit, jit or ffi (LuaOptions.Libraries: " + options.Libraries + ")", "options");
            }
            int version = LuaAPI.xlua_core_version();
            if (version != CORE_VERSION_EXPECT)
            {
                throw new InvalidProgramException("wrong core version expect:" + CORE_VERSION_EXPECT + " but got:" + version);
            }
            ownerThread = Environment.CurrentManagedThreadId;
            checkThread = options.CheckThread;
            interruptFlag = options.AllowInterrupt.GetValueOrDefault(true) ? XLUA_LIMITS_INTERRUPT : 0;
            var config = new xlua_core_config();
            config.memory_limit = options.MemoryLimit;
            config.seed = options.HashSeed.GetValueOrDefault();
            config.has_seed = options.HashSeed.HasValue ? 1 : 0;
            config.libs = (int)options.Libraries;
            config.sandbox = 1;
            self = GCHandle.Alloc(this, GCHandleType.Weak);
            int status;
            try
            {
                L = LuaAPI.xlua_core_open(ref config, GCHandle.ToIntPtr(self), out status);
            }
            catch (EntryPointNotFoundException)
            {
                self.Free();
                throw new NotSupportedException("this xlua native library has no LuaScript sandbox: build it with "
                    + "XLUA_PRESET=core, or with XLUA_LUASCRIPT=ON for the legacy and minimal presets");
            }
            if (L == IntPtr.Zero)
            {
                self.Free();
                if (status == XLUA_ERRMEM)
                {
                    throw new LuaMemoryLimitException("not enough memory for a new Lua state (LuaOptions.MemoryLimit "
                        + options.MemoryLimit + ")", options.MemoryLimit, 0, options.MemoryLimit == 0);
                }
                throw new NotSupportedException("this xlua native library cannot open the state: LuaOptions.HashSeed needs "
                    + "Lua 5.4 with the luaconf.h hook or 5.5, and LuaOptions.Libraries (" + options.Libraries
                    + ") only libraries the library was built with (LuaLibs.Buffer: CMake XLUA_BUFFER, on in the core preset)");
            }
            if (options.InstructionLimit != 0 || options.TimeLimit != TimeSpan.Zero)
            {
                SetLimits(options.InstructionLimit, options.TimeLimit);
            }
            else if (interruptFlag != 0)
            {
                // Interrupt without limits; a build without limits returns 0 and has no Interrupt.
                LuaAPI.xlua_core_set_limits(L, 0, 0, interruptFlag);
            }
        }

        ~LuaScript()
        {
            // An unreachable script: no other thread uses it, and the callback handle is weak.
            Close();
        }

        public bool IsDisposed { get { return L == IntPtr.Zero; } }

        /// <summary>
        /// Closes the state. Throws <see cref="InvalidOperationException"/>
        /// while a call of this script runs (from a callback such as the
        /// module resolver); a second Dispose does nothing.
        /// </summary>
        public void Dispose()
        {
            if (L == IntPtr.Zero)
            {
                return;
            }
            CheckThread();
            if (depth > 0)
            {
                throw new InvalidOperationException("a LuaScript cannot be disposed while one of its calls runs");
            }
            Close();
            GC.SuppressFinalize(this);
        }

        void Close()
        {
            lock (interruptLock)
            {
                if (L != IntPtr.Zero)
                {
                    LuaWatchdog.Remove(L);
                    LuaAPI.xlua_core_close(L);
                    L = IntPtr.Zero;
                }
            }
            if (self.IsAllocated)
            {
                self.Free();
            }
            if (resolved.IsAllocated)
            {
                resolved.Free();
            }
            CloseCallbacks();  // LuaCall.cs
            FreeValues();
            CloseHandles();    // LuaTableRef.cs: every handle of the script is invalid now
        }

        IntPtr State
        {
            get
            {
                if (L == IntPtr.Zero)
                {
                    throw new ObjectDisposedException("LuaScript");
                }
                return L;
            }
        }

        void CheckOpen()
        {
            if (L == IntPtr.Zero)
            {
                throw new ObjectDisposedException("LuaScript");
            }
        }

        /// <summary>
        /// Makes the calling thread the owner of this script, for a script
        /// handed from one thread to another (created by a loader thread, then
        /// used on the main thread): with <see cref="LuaOptions.CheckThread"/>
        /// the members throw <see cref="InvalidOperationException"/> on any
        /// other thread. Call it on the new thread once the old one is done
        /// with the script. <c>LuaEnv.SetOwnerThread</c> does the same for an
        /// environment.
        /// </summary>
        public void SetOwnerThread()
        {
            ownerThread = Environment.CurrentManagedThreadId;
        }

        void CheckThread()
        {
            if (checkThread && Environment.CurrentManagedThreadId != ownerThread)
            {
                throw new InvalidOperationException("a LuaScript is used from thread " + Environment.CurrentManagedThreadId
                    + " but belongs to thread " + ownerThread + " (LuaOptions.CheckThread); call SetOwnerThread() on the"
                    + " thread that takes it over");
            }
        }

        IntPtr Owned
        {
            get
            {
                IntPtr state = State;
                CheckThread();
                return state;
            }
        }

        // ---- limits and statistics ----

        xlua_core_stats Stats()
        {
            xlua_core_stats stats;
            LuaAPI.xlua_core_get_stats(State, out stats);
            return stats;
        }

        /// <summary>
        /// Bytes the state may hold; 0 is unlimited. Lowering it below
        /// <see cref="MemoryUsed"/> makes the next growth fail.
        /// </summary>
        public long MemoryLimit
        {
            get { return Stats().memory_limit; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", value, "MemoryLimit must be 0 (unlimited) or more");
                }
                LuaAPI.xlua_core_set_memory_limit(Owned, value);
            }
        }

        /// <summary>VM instructions per top-level call; 0 is unlimited. Applies from the next call.</summary>
        public long InstructionLimit
        {
            get { CheckOpen(); return instructionLimit; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", value, "InstructionLimit must be 0 (unlimited) or more");
                }
                SetLimits(value, timeLimit);
            }
        }

        /// <summary>Wall-clock time per top-level call; <see cref="TimeSpan.Zero"/> is unlimited. Applies from the next call.</summary>
        public TimeSpan TimeLimit
        {
            get { CheckOpen(); return timeLimit; }
            set
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException("value", value, "TimeLimit must be TimeSpan.Zero (unlimited) or more");
                }
                SetLimits(instructionLimit, value);
            }
        }

        void SetLimits(long instructions, TimeSpan time)
        {
            IntPtr state = Owned;
            long ns = time.Ticks > long.MaxValue / 100 ? long.MaxValue : time.Ticks * 100;
            bool watchdog = time != TimeSpan.Zero && LuaWatchdog.Available;
            int flags = (time != TimeSpan.Zero && !watchdog ? XLUA_LIMITS_CLOCK_CHECK : 0) | interruptFlag;
            if (LuaAPI.xlua_core_set_limits(state, instructions, ns, flags) == 0)
            {
                throw new NotSupportedException("this xlua native library has no instruction and time limits");
            }
            instructionLimit = instructions;
            timeLimit = time;
            if (watchdog)
            {
                LuaWatchdog.Add(state, time);
            }
            else
            {
                LuaWatchdog.Remove(state);
            }
        }

        /// <summary>Bytes the allocator holds for this state; any thread.</summary>
        public long MemoryUsed { get { return Stats().memory_used; } }

        /// <summary>The most bytes the state has held; any thread.</summary>
        public long MemoryPeak { get { return Stats().memory_peak; } }

        /// <summary>
        /// The VM instructions of the last top-level call, counted while an
        /// <see cref="InstructionLimit"/> is set (0 otherwise); the same for
        /// the same code, input and seed.
        /// </summary>
        public long LastInstructionCount { get { CheckOpen(); return lastInstructions; } }

        /// <summary>
        /// Any thread: stops the running call, which then throws
        /// <see cref="LuaInterruptedException"/>, with or without an
        /// instruction or time limit (without one only if
        /// <see cref="LuaOptions.AllowInterrupt"/> was not false). Lua code stops within microseconds;
        /// a registered C# function is not preempted, and the stop hits when
        /// it returns. Does nothing when no call runs or after Dispose.
        /// </summary>
        public void Interrupt()
        {
            lock (interruptLock)
            {
                if (L != IntPtr.Zero)
                {
                    LuaAPI.xlua_core_interrupt(L);
                }
            }
        }

        /// <summary>A full garbage collection.</summary>
        public void CollectGarbage()
        {
            LuaAPI.lua_gc(Owned, LuaGCOptions.LUA_GCCOLLECT, 0);
        }

        // ---- require ----

        static readonly xlua_core_resolver resolverCallback = Resolve;
        static int resolverInstalled;

        /// <summary>
        /// Enables <c>require</c>: the resolver is asked for a module's text,
        /// which loads (text only) into a fresh environment once per script.
        /// Null (the default) removes <c>require</c>. It runs inside the call
        /// that requires the module; an exception it throws becomes the Lua
        /// error "error loading module".
        /// </summary>
        public LuaModuleResolver ModuleResolver
        {
            get { CheckOpen(); return moduleResolver; }
            set
            {
                IntPtr state = Owned;
                if (value != null && Interlocked.Exchange(ref resolverInstalled, 1) == 0)
                {
                    LuaAPI.xlua_core_set_resolver(Marshal.GetFunctionPointerForDelegate(resolverCallback));
                }
                if (LuaAPI.xlua_core_set_require(state, value != null ? 1 : 0) != XLUA_OK)
                {
                    throw new LuaMemoryLimitException("not enough memory", MemoryLimit, MemoryUsed, false);
                }
                moduleResolver = value;
            }
        }

        [MonoPInvokeCallback(typeof(xlua_core_resolver))]
        static unsafe int Resolve(IntPtr host, IntPtr name, int nameLen, xlua_core_source* result)
        {
            LuaScript script = null;
            try
            {
                script = GCHandle.FromIntPtr(host).Target as LuaScript;
                if (script == null || script.moduleResolver == null)
                {
                    return 0;
                }
                string code, chunkName;
                if (!script.moduleResolver(Encoding.UTF8.GetString((byte*)name, nameLen), out code, out chunkName) || code == null)
                {
                    return 0;
                }
                script.Pin(code, chunkName, result);
                return 1;
            }
            catch (Exception e)
            {
                if (script == null)
                {
                    return 0;
                }
                try
                {
                    script.Pin(e.GetType().Name + ": " + e.Message, null, result);
                }
                catch (Exception)
                {
                    result->code_len = 0;
                }
                return -1;
            }
        }

        // The module's text (and chunk name) in a pinned array, until the next module.
        unsafe void Pin(string code, string chunkName, xlua_core_source* result)
        {
            if (resolved.IsAllocated)
            {
                resolved.Free();
            }
            chunkName = chunkName ?? "";
            int codeLen = Encoding.UTF8.GetByteCount(code), nameLen = Encoding.UTF8.GetByteCount(chunkName);
            var bytes = new byte[codeLen + nameLen + 1];
            Encoding.UTF8.GetBytes(code, 0, code.Length, bytes, 0);
            Encoding.UTF8.GetBytes(chunkName, 0, chunkName.Length, bytes, codeLen);
            resolved = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr p = resolved.AddrOfPinnedObject();
            result->code = p;
            result->code_len = codeLen;
            result->chunkname = p + codeLen;
            result->chunkname_len = nameLen;
        }

        // ---- Load ----

        /// <summary>
        /// Loads a text chunk as "@" + chunkName into the default environment
        /// and runs it. A binary chunk or a syntax error throws
        /// <see cref="LuaSyntaxException"/>; the limits apply to the run.
        /// </summary>
        public void Load(string code, string chunkName)
        {
            if (code == null) throw new ArgumentNullException("code");
            Invoke(XLUA_CALL_CHUNK, 0, 0, chunkName, code, default(ReadOnlySpan<byte>), new Args(), default(Span<LuaValue>));
        }

        /// <summary>As <see cref="Load(string, string)"/>, from UTF-8 text.</summary>
        public void Load(ReadOnlySpan<byte> utf8Code, string chunkName)
        {
            Invoke(XLUA_CALL_CHUNK, 0, 0, chunkName, null, utf8Code, new Args(), default(Span<LuaValue>));
        }

        /// <summary>
        /// As <see cref="Load(string, string)"/>, with the environment env as
        /// the chunk's globals: any table of this script (<see cref="NewEnvironment"/>
        /// makes one over the frozen base); default(LuaTableRef) is <see cref="Globals"/>.
        /// </summary>
        public void Load(string code, string chunkName, LuaTableRef env)
        {
            if (code == null) throw new ArgumentNullException("code");
            Invoke(XLUA_CALL_CHUNK, EnvHandle(env), 0, chunkName, code, default(ReadOnlySpan<byte>), new Args(), default(Span<LuaValue>));
        }

        /// <summary>Loads and runs a chunk; its first result as T (see <see cref="Call{T}(string)"/>).</summary>
        public T Load<T>(string code, string chunkName)
        {
            if (code == null) throw new ArgumentNullException("code");
            xlua_value r = One(XLUA_CALL_CHUNK, 0, 0, chunkName, code, new Args());
            return Read<T>(ref r);
        }

        /// <summary>As <see cref="Load{T}(string, string)"/> in the environment env.</summary>
        public T Load<T>(string code, string chunkName, LuaTableRef env)
        {
            if (code == null) throw new ArgumentNullException("code");
            xlua_value r = One(XLUA_CALL_CHUNK, EnvHandle(env), 0, chunkName, code, new Args());
            return Read<T>(ref r);
        }

        /// <summary>Loads and runs a chunk; fills results and returns how many values the chunk returned.</summary>
        public int Load(string code, string chunkName, Span<LuaValue> results)
        {
            if (code == null) throw new ArgumentNullException("code");
            return Invoke(XLUA_CALL_CHUNK, 0, 0, chunkName, code, default(ReadOnlySpan<byte>), new Args(), results);
        }

        /// <summary>As <see cref="Load(string, string, Span{LuaValue})"/> in the environment env.</summary>
        public int Load(string code, string chunkName, LuaTableRef env, Span<LuaValue> results)
        {
            if (code == null) throw new ArgumentNullException("code");
            return Invoke(XLUA_CALL_CHUNK, EnvHandle(env), 0, chunkName, code, default(ReadOnlySpan<byte>), new Args(), results);
        }

        // ---- Call ----

        /// <summary>
        /// Calls the global function <paramref name="function"/> of the default
        /// environment (raw lookups; dotted names such as "quest.onStart" walk
        /// tables). Primitive arguments and results allocate nothing.
        /// </summary>
        public void Call(string function) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(2, a1, a2, default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(3, a1, a2, a3, default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(4, a1, a2, a3, a4), default(Span<LuaValue>)); }
        public void Call(string function, ReadOnlySpan<LuaValue> args) { Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(args), default(Span<LuaValue>)); }

        /// <summary>
        /// Calls a function and reads its first result as T: long, int,
        /// double, float, bool (Lua's truth), string, byte[] or
        /// <see cref="LuaValue"/>. Numbers convert between integer and float
        /// only when exact; int and float check their range
        /// (<see cref="LuaTypeException"/>). Another T throws
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        public T Call<T>(string function) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args()); return Read<T>(ref r); }
        public T Call<T>(string function, LuaValue a1) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue))); return Read<T>(ref r); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args(2, a1, a2, default(LuaValue), default(LuaValue))); return Read<T>(ref r); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2, LuaValue a3) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args(3, a1, a2, a3, default(LuaValue))); return Read<T>(ref r); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args(4, a1, a2, a3, a4)); return Read<T>(ref r); }
        public T Call<T>(string function, ReadOnlySpan<LuaValue> args) { xlua_value r = One(XLUA_CALL_GLOBAL, 0, 0, function, null, new Args(args)); return Read<T>(ref r); }

        /// <summary>
        /// Calls a function and fills <paramref name="results"/>: returns how
        /// many values it returned. Values beyond results.Length are dropped;
        /// missing ones are nil.
        /// </summary>
        public int Call(string function, ReadOnlySpan<LuaValue> args, Span<LuaValue> results)
        {
            return Invoke(XLUA_CALL_GLOBAL, 0, 0, function, null, default(ReadOnlySpan<byte>), new Args(args), results);
        }

#if !XLUA_NO_LUASCRIPT_EXTRAS  // ToDictionary, IPairs, the tuple calls, Register with 3 or 4 arguments
        /// <summary>The first two results, read as for <see cref="Call{T}(string)"/>; missing ones are nil.</summary>
        public ValueTuple<T1, T2> Call<T1, T2>(string function, ReadOnlySpan<LuaValue> args)
        {
            return CallRead2<T1, T2>(XLUA_CALL_GLOBAL, 0, 0, function, new Args(args));
        }

        /// <summary>The first three results, read as for <see cref="Call{T}(string)"/>; missing ones are nil.</summary>
        public ValueTuple<T1, T2, T3> Call<T1, T2, T3>(string function, ReadOnlySpan<LuaValue> args)
        {
            return CallRead3<T1, T2, T3>(XLUA_CALL_GLOBAL, 0, 0, function, new Args(args));
        }

        internal unsafe ValueTuple<T1, T2> CallRead2<T1, T2>(int kind, int env, int fn, string name, Args args)
        {
            xlua_value* r = stackalloc xlua_value[2];
            int n = Raw(kind, env, fn, name, null, default(ReadOnlySpan<byte>), args, r, 2);
            for (int i = n; i < 2; i++)
            {
                r[i] = default(xlua_value);
            }
            T1 v1;
            try
            {
                v1 = Read<T1>(ref r[0]);
            }
            catch
            {
                ReleaseResults(r, n, 1, 2);
                throw;
            }
            try
            {
                return new ValueTuple<T1, T2>(v1, Read<T2>(ref r[1]));
            }
            catch
            {
                DisposeResult(__makeref(v1));
                throw;
            }
        }

        internal unsafe ValueTuple<T1, T2, T3> CallRead3<T1, T2, T3>(int kind, int env, int fn, string name, Args args)
        {
            xlua_value* r = stackalloc xlua_value[3];
            int n = Raw(kind, env, fn, name, null, default(ReadOnlySpan<byte>), args, r, 3);
            for (int i = n; i < 3; i++)
            {
                r[i] = default(xlua_value);
            }
            T1 v1 = default(T1);
            T2 v2 = default(T2);
            int read = 0;
            try
            {
                v1 = Read<T1>(ref r[0]);
                read = 1;
                v2 = Read<T2>(ref r[1]);
                read = 2;
                return new ValueTuple<T1, T2, T3>(v1, v2, Read<T3>(ref r[2]));
            }
            catch
            {
                ReleaseResults(r, n, read + 1, 3);
                if (read > 0) DisposeResult(__makeref(v1));
                if (read > 1) DisposeResult(__makeref(v2));
                throw;
            }
        }

        unsafe void ReleaseResults(xlua_value* results, int n, int from, int cap)
        {
            for (int i = from; i < n && i < cap; i++)
            {
                ReleaseResult(ref results[i]);
            }
        }

#endif
        // ---- the entry ----

        // The arguments of one call: up to four values, a span, or a list (a driver's reply).
        internal ref struct Args
        {
            readonly int count;
            readonly LuaValue a1, a2, a3, a4;
            readonly ReadOnlySpan<LuaValue> span;
            readonly bool isSpan;
            readonly LuaValueList list;

            public Args(int count, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4)
            {
                this.count = count;
                this.a1 = a1;
                this.a2 = a2;
                this.a3 = a3;
                this.a4 = a4;
                span = default(ReadOnlySpan<LuaValue>);
                isSpan = false;
                list = null;
            }

            public Args(ReadOnlySpan<LuaValue> span)
            {
                count = span.Length;
                a1 = a2 = a3 = a4 = default(LuaValue);
                this.span = span;
                isSpan = true;
                list = null;
            }

            public Args(LuaValueList list)
            {
                count = list.Count;
                a1 = a2 = a3 = a4 = default(LuaValue);
                span = default(ReadOnlySpan<LuaValue>);
                isSpan = false;
                this.list = list;
            }

            public int Count { get { return count; } }

            public LuaValue this[int i]
            {
                get
                {
                    if (isSpan) return span[i];
                    if (list != null) return list[i];
                    switch (i)
                    {
                        case 0: return a1;
                        case 1: return a2;
                        case 2: return a3;
                        default: return a4;
                    }
                }
            }
        }

        // A call with one result (nil when the function returned none), for Call<T> and the like.
        internal unsafe xlua_value One(int kind, int env, int fn, string name, string code, Args args)
        {
            xlua_value r;
            int n = Raw(kind, env, fn, name, code, default(ReadOnlySpan<byte>), args, &r, 1);
            if (n < 1)
            {
                r = default(xlua_value);
            }
            return r;
        }

        // Reads a result as T (core-luascript.md, section 2.6). A table, function, buffer or
        // coroutine result is an owning handle (LuaTableRef.cs): adopted for LuaTableRef,
        // LuaFunctionRef, LuaBufferRef, LuaCoroutine and LuaValue, released for every other T.
        // The generic part is only this: ReadInto converts through a TypedReference, so IL2CPP
        // makes no reader delegate or reader class per T (section 8.15).
        internal T Read<T>(ref xlua_value v)
        {
            T value = default(T);
            ReadInto(LuaKind<T>.Code, ref v, __makeref(value));
            return value;
        }

        internal void ReadInto(int kind, ref xlua_value v, TypedReference value)
        {
            switch (kind)
            {
                case LuaKinds.Value: __refvalue(value, LuaValue) = ResultValue(ref v); return;
                case LuaKinds.Table: __refvalue(value, LuaTableRef) = ReadTableResult(ref v); return;
                case LuaKinds.Function: __refvalue(value, LuaFunctionRef) = ReadFunctionResult(ref v); return;
                case LuaKinds.Buffer: __refvalue(value, LuaBufferRef) = ReadBufferResult(ref v); return;
                case LuaKinds.Coroutine: __refvalue(value, LuaCoroutine) = ReadCoroutineResult(ref v); return;
            }
            ReleaseResult(ref v);
            switch (kind)
            {
                case LuaKinds.Long: __refvalue(value, long) = ReadLong(ref v); return;
                case LuaKinds.Int: __refvalue(value, int) = ReadInt(ref v); return;
                case LuaKinds.Double: __refvalue(value, double) = ReadDouble(ref v); return;
                case LuaKinds.Float: __refvalue(value, float) = ReadFloat(ref v); return;
                case LuaKinds.Bool: __refvalue(value, bool) = ReadBool(ref v); return;
                case LuaKinds.String: __refvalue(value, string) = ReadString(ref v); return;
                case LuaKinds.Bytes: __refvalue(value, byte[]) = ReadBytes(ref v); return;
                default:
                    throw new NotSupportedException(__reftype(value) + " is not a LuaScript result type (long, int, double, float, bool, string, byte[], LuaValue, LuaTableRef, LuaFunctionRef, LuaBufferRef, LuaCoroutine)");
            }
        }

        unsafe int Invoke(int kind, int env, int fn, string name, string code, ReadOnlySpan<byte> utf8Code, Args args, Span<LuaValue> results)
        {
            int cap = results.Length;
            xlua_value* r;
            int n;
            if (cap <= StackValues)
            {
                xlua_value* stack = stackalloc xlua_value[StackValues];
                r = stack;
                n = Raw(kind, env, fn, name, code, utf8Code, args, r, cap);
                Fill(results, r, n);
            }
            else
            {
                r = Values(cap);
                n = Raw(kind, env, fn, name, code, utf8Code, args, r, cap);
                Fill(results, r, n);
            }
            return n;
        }

        // Tables, functions and buffers become owning handles (ResultValue, LuaTableRef.cs) that the caller disposes.
        unsafe void Fill(Span<LuaValue> results, xlua_value* r, int n)
        {
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = i < n ? ResultValue(ref r[i]) : LuaValue.Nil;
            }
        }

        internal xlua_value* Values(int count)
        {
            if (valueCapacity < count)
            {
                int n = Math.Max(count, valueCapacity * 2);
                if (n > int.MaxValue / sizeof(xlua_value))
                {
                    throw new OutOfMemoryException("too many values");
                }
                if (valueBuffer != null)
                {
                    if (retiredCount == retiredBuffers.Length)
                    {
                        Array.Resize(ref retiredBuffers, Math.Max(4, retiredCount * 2));
                    }
                    retiredBuffers[retiredCount++] = (IntPtr)valueBuffer;
                }
                valueBuffer = (xlua_value*)Marshal.AllocHGlobal(n * sizeof(xlua_value));
                valueCapacity = n;
            }
            return valueBuffer;
        }

        void FreeRetired()
        {
            while (retiredCount > 0)
            {
                Marshal.FreeHGlobal(retiredBuffers[--retiredCount]);
                retiredBuffers[retiredCount] = IntPtr.Zero;
            }
        }

        void FreeValues()
        {
            FreeRetired();
            if (valueBuffer != null)
            {
                Marshal.FreeHGlobal((IntPtr)valueBuffer);
                valueBuffer = null;
                valueCapacity = 0;
            }
        }

        static int Utf8Length(LuaValue v)
        {
            if (v.Type != LuaType.String)
            {
                return 0;
            }
            var bytes = v.Object as byte[];
            return bytes != null ? bytes.Length : Encoding.UTF8.GetByteCount((string)v.Object);
        }

        // The status, the coroutine handle (c.fn) and the flags of the last
        // xlua_core_call, for the coroutines (LuaCoroutine.cs): -1 until the
        // native call has run. Set after the call returned, so nested calls
        // made meanwhile do not matter.
        internal int rawStatus, rawFn, rawFlags;

        // One xlua_core_call: the name, the code and the string arguments go
        // into the reused scratch array as UTF-8, which is pinned for the call.
        // XLUA_YIELD (a coroutine yielded) is a success.
        unsafe int Raw(int kind, int env, int fn, string name, string code, ReadOnlySpan<byte> utf8Code, Args args, xlua_value* results, int cap)
        {
            rawStatus = -1;
            rawFn = 0;
            rawFlags = 0;
            IntPtr state = Owned;
            if (name == null)
            {
                if (kind == XLUA_CALL_GLOBAL || kind == (XLUA_CALL_START | XLUA_CALL_GLOBAL) || kind == XLUA_CALL_CHUNK || kind == XLUA_CALL_COMPILE)
                {
                    throw new ArgumentNullException(kind == XLUA_CALL_CHUNK || kind == XLUA_CALL_COMPILE ? "chunkName" : "function");
                }
                name = "";
            }
            int nargs = args.Count;
            int nameLen = Encoding.UTF8.GetByteCount(name);
            int codeLen = code != null ? Encoding.UTF8.GetByteCount(code) : 0;
            int total = nameLen + codeLen;
            for (int i = 0; i < nargs; i++)
            {
                total += Utf8Length(args[i]);
            }
            if (scratch.Length < total)
            {
                scratch = new byte[Math.Max(total, scratch.Length * 2)];
            }
            int offset = Encoding.UTF8.GetBytes(name, 0, name.Length, scratch, 0);
            if (code != null)
            {
                offset += Encoding.UTF8.GetBytes(code, 0, code.Length, scratch, offset);
            }
            xlua_value* stackArgs = stackalloc xlua_value[StackValues];
            xlua_value* argv = nargs > StackValues ? Values(nargs) : stackArgs;
            xlua_core_call c = default(xlua_core_call);
            int status;
            fixed (byte* s = scratch)
            fixed (byte* u = utf8Code)
            {
                for (int i = 0; i < nargs; i++)
                {
                    // Strings into the scratch array; tables, functions and buffers as native handles (LuaTableRef.cs).
                    PutValue(args[i], argv + i, s, ref offset, i);
                }
                c.kind = kind;
                c.env = env;
                c.fn = fn;
                c.name = (IntPtr)s;
                c.name_len = nameLen;
                if (kind == XLUA_CALL_CHUNK || kind == XLUA_CALL_COMPILE)
                {
                    c.code = code != null ? (IntPtr)(s + nameLen) : (IntPtr)u;
                    c.code_len = code != null ? codeLen : utf8Code.Length;
                }
                c.args = (IntPtr)argv;
                c.nargs = nargs;
                c.results = (IntPtr)results;
                c.results_cap = cap;
                depth++;
                LuaScript outer = active;  // LuaCall.cs: the script whose host functions this thread runs
                active = this;
                try
                {
                    status = LuaAPI.xlua_core_call(state, &c);
                }
                finally
                {
                    depth--;
                    active = outer;
                }
                rawStatus = status;
                rawFn = c.fn;
                rawFlags = c.flags;
            }
            if (depth == 0 && unrefCount > 0)
            {
                ReleaseUnrefs();  // LuaCall.cs: what the host functions of this call borrowed
            }
            if (status != XLUA_OK && status != XLUA_YIELD)
            {
                Exception error = Error(status, ref c, name);
                if (depth == 0)
                {
                    lastInstructions = c.instructions;
                    hostException = null;  // LuaCall.cs: an exception a script caught is not kept
                }
                throw error;
            }
            if (depth == 0)
            {
                lastInstructions = c.instructions;
                hostException = null;
            }
            return c.nresults;
        }

        static unsafe string Text(IntPtr p, int len)
        {
            return p == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)p, len);
        }

        Exception Error(int status, ref xlua_core_call c, string name)
        {
            if (c.error_ref != 0 && status != XLUA_ERRRUN && status != XLUA_ERRERR)
            {
                LuaAPI.xlua_core_unref(L, c.error_ref);  // a handle no exception keeps
                c.error_ref = 0;
            }
            string message = Text(c.error, c.error_len) ?? "unknown error (status " + status + ")";
            string traceback = Text(c.traceback, c.traceback_len);
            switch (status)
            {
                case XLUA_ERRSYNTAX:
                    return new LuaSyntaxException(message, name);
                case XLUA_ERRRUN:
                case XLUA_ERRERR:
                    {
                        // A host function's exception that the script did not catch is the inner exception (LuaCall.cs).
                        var e = new LuaRuntimeException(message, traceback, TakeHostException(c.flags));
                        e.ErrorValue = message;
                        if (c.error_ref != 0)
                        {
                            // error({code = 5}): the table (or function) itself, an owning handle the exception holds.
                            LuaType kind = (c.flags & XLUA_CALLF_ERROR_TABLE) != 0 ? LuaType.Table : LuaType.Function;
                            int slot = AddHandle(kind, c.error_ref, SlotOwned);
                            c.error_ref = 0;
                            e.ErrorValue = LuaValue.Handle(kind, this, slot, slots[slot].generation);
                        }
                        return e;
                    }
                case XLUA_ERRMEM:
                    {
                        xlua_core_stats stats = Stats();
                        bool host = stats.host_oom > hostOomSeen;
                        hostOomSeen = stats.host_oom;
                        return new LuaMemoryLimitException(message, stats.memory_limit, stats.memory_used, host);
                    }
                case XLUA_LIMIT_INSTR:
                    return new LuaInstructionLimitException(message, traceback, instructionLimit);
                case XLUA_LIMIT_TIME:
                    return new LuaTimeLimitException(message, traceback, timeLimit);
                case XLUA_INTERRUPTED:
                    return new LuaInterruptedException(message, traceback);
                case XLUA_ERRTYPE:
                case XLUA_ERRSTACK:
                    return new ArgumentException(message);
                case XLUA_ERRHANDLE:
                    return new InvalidOperationException(message);
                case XLUA_ERRCONFIG:
                    return new NotSupportedException(message);
                default:
                    return new LuaException(message);
            }
        }

        // ---- values ----

        internal static unsafe LuaValue ToValue(ref xlua_value v)
        {
            switch (v.type)
            {
                case 0: return LuaValue.Nil;
                case 1: return v.i != 0;
                case 2: return v.i;
                case 3: return v.n;
                case 4: return Encoding.UTF8.GetString((byte*)v.s, v.len);
                // Tables, functions and buffers are handles: ResultValue (LuaTableRef.cs) adopts them.
                default: return LuaValue.OfType(TypeOf(ref v));
            }
        }

        static LuaType TypeOf(ref xlua_value v)
        {
            return v.type >= 0 && v.type <= (int)LuaType.Coroutine ? (LuaType)v.type : LuaType.Other;
        }

        // Lua's float to integer conversion for exact values (lua_numbertointeger).
        internal static bool FloatToLong(double d, out long v)
        {
            // -2^63 is exact as a double; 2^63 is the first value out of range.
            if (d >= -9223372036854775808.0 && d < 9223372036854775808.0 && Math.Floor(d) == d)
            {
                v = (long)d;
                return true;
            }
            v = 0;
            return false;
        }

        internal static long ReadLong(ref xlua_value v)
        {
            if (v.type == 2)
            {
                return v.i;
            }
            long l;
            if (v.type == 3 && FloatToLong(v.n, out l))
            {
                return l;
            }
            throw LuaValue.TypeError(LuaType.Integer, TypeOf(ref v), v.type == 3 ? v.n : 0);
        }

        internal static int ReadInt(ref xlua_value v)
        {
            long l = ReadLong(ref v);
            if (l < int.MinValue || l > int.MaxValue)
            {
                throw new LuaTypeException("integer " + l + " is out of the range of int", LuaType.Integer, LuaType.Integer);
            }
            return (int)l;
        }

        internal static double ReadDouble(ref xlua_value v)
        {
            if (v.type == 3)
            {
                return v.n;
            }
            if (v.type == 2)
            {
                return v.i;
            }
            throw LuaValue.TypeError(LuaType.Number, TypeOf(ref v), 0);
        }

        internal static float ReadFloat(ref xlua_value v)
        {
            double d = ReadDouble(ref v);
            if ((d > float.MaxValue || d < -float.MaxValue) && !double.IsInfinity(d))
            {
                throw new LuaTypeException("number " + d + " is out of the range of float", LuaType.Number, TypeOf(ref v));
            }
            return (float)d;
        }

        internal static bool ReadBool(ref xlua_value v)
        {
            return !(v.type == 0 || (v.type == 1 && v.i == 0));
        }

        internal static unsafe string ReadString(ref xlua_value v)
        {
            if (v.type == 4)
            {
                return Encoding.UTF8.GetString((byte*)v.s, v.len);
            }
            if (v.type == 0)
            {
                return null;
            }
            throw LuaValue.TypeError(LuaType.String, TypeOf(ref v), 0);
        }

        internal static byte[] ReadBytes(ref xlua_value v)
        {
            if (v.type == 4)
            {
                var bytes = new byte[v.len];
                if (v.len > 0)
                {
                    Marshal.Copy(v.s, bytes, 0, v.len);
                }
                return bytes;
            }
            if (v.type == 0)
            {
                return null;
            }
            throw LuaValue.TypeError(LuaType.String, TypeOf(ref v), 0);
        }
    }

    // The type of T for the conversions of LuaScript.Read and the host functions'
    // arguments and results, found once per T. Its only generic code is this class.
    internal static class LuaKind<T>
    {
        internal static readonly int Code = LuaKinds.Of(typeof(T));
    }

    internal static class LuaKinds
    {
        internal const int None = 0, Long = 1, Int = 2, Double = 3, Float = 4, Bool = 5, String = 6, Bytes = 7;
        // Readers that adopt a handle result.
        internal const int Value = 8, Table = 9, Function = 10, Buffer = 11, Coroutine = 12;

        internal static int Of(Type t)
        {
            if (t == typeof(long)) return Long;
            if (t == typeof(int)) return Int;
            if (t == typeof(double)) return Double;
            if (t == typeof(float)) return Float;
            if (t == typeof(bool)) return Bool;
            if (t == typeof(string)) return String;
            if (t == typeof(byte[])) return Bytes;
            if (t == typeof(LuaValue)) return Value;
            if (t == typeof(LuaTableRef)) return Table;
            if (t == typeof(LuaFunctionRef)) return Function;
            if (t == typeof(LuaBufferRef)) return Buffer;
            if (t == typeof(LuaCoroutine)) return Coroutine;
            return None;
        }

        // Whether a table read of this type keeps a table, function, buffer or coroutine value as a handle.
        internal static bool KeepsHandles(int kind)
        {
            return kind >= Value;
        }
    }
}
#endif
