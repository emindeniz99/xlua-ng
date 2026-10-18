/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript: sandboxed Lua 5.4 and 5.5 without the C# bridge
// (docs/design/core-luascript.md, section 2; milestone 6: lifecycle, limits,
// Load, Call, multiple results, exceptions and the thread check). It needs a
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
    public sealed unsafe class LuaScript : IDisposable
    {
        const int CORE_VERSION_EXPECT = 3;
        const int XLUA_OK = 0, XLUA_ERRRUN = 2, XLUA_ERRSYNTAX = 3, XLUA_ERRMEM = 4, XLUA_ERRERR = 5;
        const int XLUA_LIMIT_INSTR = 10, XLUA_LIMIT_TIME = 11, XLUA_INTERRUPTED = 12;
        const int XLUA_ERRTYPE = 20, XLUA_ERRSTACK = 21, XLUA_ERRHANDLE = 22, XLUA_ERRCONFIG = 23;
        const int XLUA_CALL_GLOBAL = 0, XLUA_CALL_CHUNK = 2;
        const int XLUA_LIMITS_CLOCK_CHECK = 1;
        // Up to this many arguments or results live on the stack; more use a reused array.
        const int StackValues = 16;
        const LuaLibs NotForScripts = LuaLibs.Io | LuaLibs.Os | LuaLibs.Debug | LuaLibs.Package | LuaLibs.Bit | LuaLibs.Jit | LuaLibs.Ffi;

        IntPtr L;
        GCHandle self;  // weak: the host pointer of the resolver
        readonly int ownerThread;
        readonly bool checkThread;
        readonly object interruptLock = new object();
        int depth;  // C# entries now running (a resolver can call back in)
        long instructionLimit;
        TimeSpan timeLimit;
        long lastInstructions;
        long hostOomSeen;
        byte[] scratch = new byte[256];         // UTF-8 of the name, the code and string arguments
        xlua_value[] valueArray = new xlua_value[0];  // arguments or results beyond StackValues
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
            if ((options.Libraries & LuaLibs.Buffer) != 0)
            {
                throw new NotSupportedException("LuaLibs.Buffer is not available yet");
            }
            int version = LuaAPI.xlua_core_version();
            if (version != CORE_VERSION_EXPECT)
            {
                throw new InvalidProgramException("wrong core version expect:" + CORE_VERSION_EXPECT + " but got:" + version);
            }
            ownerThread = Environment.CurrentManagedThreadId;
            checkThread = options.CheckThread;
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
                    + ") only libraries the library was built with");
            }
            if (options.InstructionLimit != 0 || options.TimeLimit != TimeSpan.Zero)
            {
                SetLimits(options.InstructionLimit, options.TimeLimit);
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

        void CheckThread()
        {
            if (checkThread && Environment.CurrentManagedThreadId != ownerThread)
            {
                throw new InvalidOperationException("a LuaScript is used from thread " + Environment.CurrentManagedThreadId
                    + " but belongs to thread " + ownerThread + " (LuaOptions.CheckThread)");
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
            int flags = time != TimeSpan.Zero && !watchdog ? XLUA_LIMITS_CLOCK_CHECK : 0;
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
        /// <see cref="LuaInterruptedException"/>. Does nothing when no call
        /// runs or after Dispose.
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
            Invoke(XLUA_CALL_CHUNK, chunkName, code, default(ReadOnlySpan<byte>), new Args(), default(Span<LuaValue>));
        }

        /// <summary>As <see cref="Load(string, string)"/>, from UTF-8 text.</summary>
        public void Load(ReadOnlySpan<byte> utf8Code, string chunkName)
        {
            Invoke(XLUA_CALL_CHUNK, chunkName, null, utf8Code, new Args(), default(Span<LuaValue>));
        }

        /// <summary>Loads and runs a chunk; its first result as T (see <see cref="Call{T}(string)"/>).</summary>
        public T Load<T>(string code, string chunkName)
        {
            if (code == null) throw new ArgumentNullException("code");
            return InvokeRead<T>(XLUA_CALL_CHUNK, chunkName, code, new Args());
        }

        /// <summary>Loads and runs a chunk; fills results and returns how many values the chunk returned.</summary>
        public int Load(string code, string chunkName, Span<LuaValue> results)
        {
            if (code == null) throw new ArgumentNullException("code");
            return Invoke(XLUA_CALL_CHUNK, chunkName, code, default(ReadOnlySpan<byte>), new Args(), results);
        }

        // ---- Call ----

        /// <summary>
        /// Calls the global function <paramref name="function"/> of the default
        /// environment (raw lookups; dotted names such as "quest.onStart" walk
        /// tables). Primitive arguments and results allocate nothing.
        /// </summary>
        public void Call(string function) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(2, a1, a2, default(LuaValue), default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(3, a1, a2, a3, default(LuaValue)), default(Span<LuaValue>)); }
        public void Call(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(4, a1, a2, a3, a4), default(Span<LuaValue>)); }
        public void Call(string function, ReadOnlySpan<LuaValue> args) { Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(args), default(Span<LuaValue>)); }

        /// <summary>
        /// Calls a function and reads its first result as T: long, int,
        /// double, float, bool (Lua's truth), string, byte[] or
        /// <see cref="LuaValue"/>. Numbers convert between integer and float
        /// only when exact; int and float check their range
        /// (<see cref="LuaTypeException"/>). Another T throws
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        public T Call<T>(string function) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args()); }
        public T Call<T>(string function, LuaValue a1) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue))); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args(2, a1, a2, default(LuaValue), default(LuaValue))); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2, LuaValue a3) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args(3, a1, a2, a3, default(LuaValue))); }
        public T Call<T>(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args(4, a1, a2, a3, a4)); }
        public T Call<T>(string function, ReadOnlySpan<LuaValue> args) { return InvokeRead<T>(XLUA_CALL_GLOBAL, function, null, new Args(args)); }

        /// <summary>
        /// Calls a function and fills <paramref name="results"/>: returns how
        /// many values it returned. Values beyond results.Length are dropped;
        /// missing ones are nil.
        /// </summary>
        public int Call(string function, ReadOnlySpan<LuaValue> args, Span<LuaValue> results)
        {
            return Invoke(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(args), results);
        }

        /// <summary>The first two results, read as for <see cref="Call{T}(string)"/>; missing ones are nil.</summary>
        public unsafe ValueTuple<T1, T2> Call<T1, T2>(string function, ReadOnlySpan<LuaValue> args)
        {
            xlua_value* r = stackalloc xlua_value[2];
            int n = Raw(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(args), r, 2);
            return new ValueTuple<T1, T2>(Read<T1>(r, n, 0), Read<T2>(r, n, 1));
        }

        /// <summary>The first three results, read as for <see cref="Call{T}(string)"/>; missing ones are nil.</summary>
        public unsafe ValueTuple<T1, T2, T3> Call<T1, T2, T3>(string function, ReadOnlySpan<LuaValue> args)
        {
            xlua_value* r = stackalloc xlua_value[3];
            int n = Raw(XLUA_CALL_GLOBAL, function, null, default(ReadOnlySpan<byte>), new Args(args), r, 3);
            return new ValueTuple<T1, T2, T3>(Read<T1>(r, n, 0), Read<T2>(r, n, 1), Read<T3>(r, n, 2));
        }

        // ---- the entry ----

        // The arguments of one call: up to four values or a span.
        ref struct Args
        {
            readonly int count;
            readonly LuaValue a1, a2, a3, a4;
            readonly ReadOnlySpan<LuaValue> span;
            readonly bool isSpan;

            public Args(int count, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4)
            {
                this.count = count;
                this.a1 = a1;
                this.a2 = a2;
                this.a3 = a3;
                this.a4 = a4;
                span = default(ReadOnlySpan<LuaValue>);
                isSpan = false;
            }

            public Args(ReadOnlySpan<LuaValue> span)
            {
                count = span.Length;
                a1 = a2 = a3 = a4 = default(LuaValue);
                this.span = span;
                isSpan = true;
            }

            public int Count { get { return count; } }

            public LuaValue this[int i]
            {
                get
                {
                    if (isSpan) return span[i];
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

        unsafe T InvokeRead<T>(int kind, string name, string code, Args args)
        {
            xlua_value r;
            int n = Raw(kind, name, code, default(ReadOnlySpan<byte>), args, &r, 1);
            return Read<T>(&r, n, 0);
        }

        static unsafe T Read<T>(xlua_value* results, int n, int i)
        {
            LuaReader<T> read = LuaRead<T>.Read;
            if (read == null)
            {
                throw new NotSupportedException(typeof(T) + " is not a LuaScript result type (long, int, double, float, bool, string, byte[], LuaValue)");
            }
            if (i >= n)
            {
                xlua_value nil = default(xlua_value);
                return read(ref nil);
            }
            return read(ref results[i]);
        }

        unsafe int Invoke(int kind, string name, string code, ReadOnlySpan<byte> utf8Code, Args args, Span<LuaValue> results)
        {
            int cap = results.Length;
            xlua_value* r;
            int n;
            if (cap <= StackValues)
            {
                xlua_value* stack = stackalloc xlua_value[StackValues];
                r = stack;
                n = Raw(kind, name, code, utf8Code, args, r, cap);
                Fill(results, r, n);
            }
            else
            {
                xlua_value[] array = Values(cap);
                fixed (xlua_value* p = array)
                {
                    n = Raw(kind, name, code, utf8Code, args, p, cap);
                    Fill(results, p, n);
                }
            }
            return n;
        }

        static unsafe void Fill(Span<LuaValue> results, xlua_value* r, int n)
        {
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = i < n ? ToValue(ref r[i]) : LuaValue.Nil;
            }
        }

        xlua_value[] Values(int count)
        {
            if (valueArray.Length < count)
            {
                valueArray = new xlua_value[Math.Max(count, valueArray.Length * 2)];
            }
            return valueArray;
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

        // One xlua_core_call: the name, the code and the string arguments go
        // into the reused scratch array as UTF-8, which is pinned for the call.
        unsafe int Raw(int kind, string name, string code, ReadOnlySpan<byte> utf8Code, Args args, xlua_value* results, int cap)
        {
            IntPtr state = Owned;
            if (name == null)
            {
                throw new ArgumentNullException(kind == XLUA_CALL_CHUNK ? "chunkName" : "function");
            }
            int nargs = args.Count;
            int nameLen = Encoding.UTF8.GetByteCount(name);
            int codeLen = code != null ? Encoding.UTF8.GetByteCount(code) : 0;
            int total = nameLen + codeLen;
            for (int i = 0; i < nargs; i++)
            {
                LuaValue v = args[i];
                if (v.Type > LuaType.String)
                {
                    throw new ArgumentException("argument " + (i + 1) + " is a " + LuaValue.Name(v.Type)
                        + " without a handle; tables and functions cannot be passed yet", "args");
                }
                total += Utf8Length(v);
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
            xlua_value[] heapArgs = nargs > StackValues ? Values(nargs) : null;
            xlua_value* stackArgs = stackalloc xlua_value[StackValues];
            xlua_core_call c = default(xlua_core_call);
            int status;
            fixed (byte* s = scratch)
            fixed (byte* u = utf8Code)
            fixed (xlua_value* h = heapArgs)
            {
                xlua_value* argv = heapArgs != null ? h : stackArgs;
                for (int i = 0; i < nargs; i++)
                {
                    LuaValue v = args[i];
                    xlua_value* a = argv + i;
                    a->type = (int)v.Type;
                    a->len = 0;
                    a->i = v.Bits;
                    if (v.Type == LuaType.String)
                    {
                        var bytes = v.Object as byte[];
                        int len = bytes != null ? bytes.Length : Encoding.UTF8.GetBytes((string)v.Object, 0, ((string)v.Object).Length, scratch, offset);
                        if (bytes != null && len > 0)
                        {
                            Buffer.BlockCopy(bytes, 0, scratch, offset, len);
                        }
                        a->s = (IntPtr)(s + offset);
                        a->len = len;
                        offset += len;
                    }
                }
                c.kind = kind;
                c.name = (IntPtr)s;
                c.name_len = nameLen;
                if (kind == XLUA_CALL_CHUNK)
                {
                    c.code = code != null ? (IntPtr)(s + nameLen) : (IntPtr)u;
                    c.code_len = code != null ? codeLen : utf8Code.Length;
                }
                c.args = (IntPtr)argv;
                c.nargs = nargs;
                c.results = (IntPtr)results;
                c.results_cap = cap;
                depth++;
                try
                {
                    status = LuaAPI.xlua_core_call(state, &c);
                }
                finally
                {
                    depth--;
                }
            }
            if (depth == 0)
            {
                lastInstructions = c.instructions;
            }
            if (status != XLUA_OK)
            {
                throw Error(status, ref c, name);
            }
            return c.nresults;
        }

        static unsafe string Text(IntPtr p, int len)
        {
            return p == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)p, len);
        }

        Exception Error(int status, ref xlua_core_call c, string name)
        {
            string message = Text(c.error, c.error_len) ?? "unknown error (status " + status + ")";
            string traceback = Text(c.traceback, c.traceback_len);
            switch (status)
            {
                case XLUA_ERRSYNTAX:
                    return new LuaSyntaxException(message, name);
                case XLUA_ERRRUN:
                case XLUA_ERRERR:
                    return new LuaRuntimeException(message, traceback);
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
                default: return LuaValue.OfType(v.type <= (int)LuaType.Other ? (LuaType)v.type : LuaType.Other);
            }
        }

        static LuaType TypeOf(ref xlua_value v)
        {
            return v.type >= 0 && v.type <= (int)LuaType.Other ? (LuaType)v.type : LuaType.Other;
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

    internal delegate T LuaReader<T>(ref xlua_value v);

    // One reader per result type, chosen once per T (core-luascript.md, section
    // 2.6): no boxing, and IL2CPP sees every instantiation at its call site.
    internal static class LuaRead<T>
    {
        internal static readonly LuaReader<T> Read = (LuaReader<T>)LuaReaders.For(typeof(T));
    }

    internal static class LuaReaders
    {
        internal static Delegate For(Type t)
        {
            if (t == typeof(long)) return (LuaReader<long>)LuaScript.ReadLong;
            if (t == typeof(int)) return (LuaReader<int>)LuaScript.ReadInt;
            if (t == typeof(double)) return (LuaReader<double>)LuaScript.ReadDouble;
            if (t == typeof(float)) return (LuaReader<float>)LuaScript.ReadFloat;
            if (t == typeof(bool)) return (LuaReader<bool>)LuaScript.ReadBool;
            if (t == typeof(string)) return (LuaReader<string>)LuaScript.ReadString;
            if (t == typeof(byte[])) return (LuaReader<byte[]>)LuaScript.ReadBytes;
            if (t == typeof(LuaValue)) return (LuaReader<LuaValue>)LuaScript.ToValue;
            return null;
        }
    }
}
#endif
