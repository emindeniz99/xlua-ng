/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's coroutines (docs/design/core-luascript.md, section 2.11;
// milestone 9): LuaCoroutine, a handle to a Lua thread that C# starts
// (LuaScript.Start), resumes and closes, each step one entry with the
// script's limits; a coroutine a script made (coroutine.create) reaches C# as
// the same handle. The drivers are in LuaCoroutineDrivers.cs. Compiled with
// the scripting define XLUA_LUASCRIPT or XLUA_CORE_ONLY, like LuaScript.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using XLua.LuaDLL;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>Where a <see cref="LuaCoroutine"/> stands; the numbers are the native ones (build/xlua_core.h, XLUA_CO_*).</summary>
    public enum LuaCoroutineStatus
    {
        /// <summary>Suspended: it yielded, or a script made it and nothing started it yet. <see cref="LuaCoroutine.Resume()"/> runs it on.</summary>
        Yielded = 0,
        /// <summary>Its function returned; <see cref="LuaCoroutine.ValueCount"/> values are its results.</summary>
        Finished = 1,
        /// <summary>An error (or a limit) stopped it; the exception was thrown by the step that ran it.</summary>
        Faulted = 2,
        /// <summary>Closed by <see cref="LuaCoroutine.Close"/> or <see cref="LuaCoroutine.Dispose"/>, or its script is disposed.</summary>
        Closed = 3,
        /// <summary>It runs now (a host function it called asks), or it resumed another coroutine; it cannot be resumed or closed.</summary>
        Running = 4,
    }

    /// <summary>
    /// A Lua coroutine of a <see cref="LuaScript"/>: <see cref="LuaScript.Start(string)"/>
    /// runs a function to its first yield, <see cref="Resume()"/> to the next
    /// one or to its end. Each step is a call of its own: the full
    /// <see cref="LuaScript.InstructionLimit"/> and <see cref="LuaScript.TimeLimit"/>
    /// apply to it (<see cref="LuaScript.MemoryLimit"/> covers the whole
    /// state), and an error or a limit throws from the step and leaves the
    /// coroutine <see cref="LuaCoroutineStatus.Faulted"/>. The values it
    /// yielded or returned are <see cref="this[int]"/>; tables, functions,
    /// buffers and coroutines among them are borrowed until the next step.
    /// Use it from the script's thread. Dispose it: like the other handles it
    /// has no finalizer, and <see cref="LuaScript.LiveHandles"/> counts it
    /// until then.
    /// </summary>
    public sealed partial class LuaCoroutine : IDisposable  // the drivers: LuaCoroutineDrivers.cs
    {
        /// <summary>The most values a yield or a return passes to C#; more are dropped.</summary>
        public const int MaxValues = 64;

        readonly LuaScript script;
        readonly int slot;
        readonly int generation;
        readonly bool started;  // made by LuaScript.Start: Dispose closes it
        LuaCoroutineStatus status;
        readonly LuaValueList values = new LuaValueList();
        int[] held = NoSlots;  // the borrowed slots of the values
        int heldCount;
        static readonly int[] NoSlots = new int[0];

        internal LuaCoroutine(LuaScript script, int slot, int generation, LuaCoroutineStatus status, bool started)
        {
            this.script = script;
            this.slot = slot;
            this.generation = generation;
            this.status = status;
            this.started = started;
        }

        internal int Slot { get { return slot; } }
        internal int Generation { get { return generation; } }

        /// <summary>The script the coroutine belongs to.</summary>
        public LuaScript Script { get { return script; } }

        /// <summary>False after Dispose, after a borrowed handle's frame or step ends, and after the script is disposed.</summary>
        public bool IsValid { get { return script.HandleValid(slot, generation, LuaType.Coroutine); } }

        /// <summary>
        /// The status after the last step C# saw (<see cref="LuaCoroutineStatus.Closed"/>
        /// once the handle is invalid). A script may resume a coroutine it
        /// reaches too; a <see cref="Resume()"/> then finds the real state.
        /// </summary>
        public LuaCoroutineStatus Status { get { return IsValid ? status : LuaCoroutineStatus.Closed; } }

        /// <summary>Suspended: <see cref="Resume()"/> runs it on.</summary>
        public bool IsYielded { get { return Status == LuaCoroutineStatus.Yielded; } }

        /// <summary>Finished, faulted or closed.</summary>
        public bool IsDone { get { LuaCoroutineStatus s = Status; return s != LuaCoroutineStatus.Yielded && s != LuaCoroutineStatus.Running; } }

        /// <summary>How many values the last yield or return passed (at most <see cref="MaxValues"/>).</summary>
        public int ValueCount { get { return IsValid ? values.Count : 0; } }

        /// <summary>
        /// Value i of the last yield or return; nil beyond <see cref="ValueCount"/>.
        /// A table, function, buffer or coroutine is borrowed until the next
        /// step (Retain keeps it).
        /// </summary>
        public LuaValue this[int i]
        {
            get
            {
                if (i < 0)
                {
                    throw new ArgumentOutOfRangeException("i", i, "value indices start at 0");
                }
                Check();
                return values[i];
            }
        }

        void Check()
        {
            if (!IsValid)
            {
                throw new ObjectDisposedException("LuaCoroutine", script.IsDisposed ? "the LuaScript is disposed" : "the coroutine handle is disposed");
            }
        }

        // The native handle, on the owner thread.
        internal int Native { get { return script.NativeHandle(slot, generation, LuaType.Coroutine); } }

        /// <summary>
        /// Runs the coroutine to its next yield or its end; the arguments
        /// become the results of the <c>coroutine.yield</c> (or of the host
        /// function that yielded, <see cref="LuaCall.Yield"/>), or the
        /// function's arguments when a script made it and it has not started.
        /// Throws <see cref="InvalidOperationException"/> unless
        /// <see cref="IsYielded"/>, and the exceptions of
        /// <see cref="LuaScript.Call(string)"/> for errors and limits, which
        /// leave it <see cref="LuaCoroutineStatus.Faulted"/>. Primitive
        /// values in and out allocate nothing.
        /// </summary>
        public void Resume() { Step(new LuaScript.Args()); }
        public void Resume(LuaValue a1) { Step(new LuaScript.Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue))); }
        public void Resume(LuaValue a1, LuaValue a2) { Step(new LuaScript.Args(2, a1, a2, default(LuaValue), default(LuaValue))); }
        public void Resume(LuaValue a1, LuaValue a2, LuaValue a3) { Step(new LuaScript.Args(3, a1, a2, a3, default(LuaValue))); }
        public void Resume(LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { Step(new LuaScript.Args(4, a1, a2, a3, a4)); }
        public void Resume(ReadOnlySpan<LuaValue> args) { Step(new LuaScript.Args(args)); }
        internal void ResumeWith(LuaValueList args) { Step(new LuaScript.Args(args)); }

        void Step(LuaScript.Args args)
        {
            int native = Native;
            if (status != LuaCoroutineStatus.Yielded)
            {
                throw new InvalidOperationException(status == LuaCoroutineStatus.Running
                    ? "the coroutine is running: resume it after it yields"
                    : "cannot resume a " + status.ToString().ToLowerInvariant() + " coroutine");
            }
            script.RunCoroutine(this, LuaScript.XLUA_CALL_RESUME, native, null, args);
        }

        /// <summary>
        /// Closes the coroutine: runs its pending to-be-closed variables
        /// (<c>local x &lt;close&gt;</c>) as one call with the limits, as
        /// <c>coroutine.close</c> does, and makes it <see cref="LuaCoroutineStatus.Closed"/>.
        /// An error of a __close handler throws (for a faulted coroutine only
        /// a limit does: its own error was thrown already). Nothing for a
        /// finished or closed one, or after the script is disposed.
        /// </summary>
        public void Close()
        {
            if (!IsValid)
            {
                return;
            }
            int native = Native;
            if (status == LuaCoroutineStatus.Running)
            {
                throw new InvalidOperationException("a running coroutine cannot be closed");
            }
            if (status == LuaCoroutineStatus.Yielded || status == LuaCoroutineStatus.Faulted)
            {
                bool faulted = status == LuaCoroutineStatus.Faulted;
                try
                {
                    script.RunCoroutine(this, LuaScript.XLUA_CALL_CLOSE, native, null, new LuaScript.Args());
                }
                catch (LuaRuntimeException) when (faulted)
                {
                }
            }
            ReleaseValues();
            values.Clear();
            status = LuaCoroutineStatus.Closed;
        }

        /// <summary>
        /// Releases the handle; a coroutine of <see cref="LuaScript.Start(string)"/>
        /// is closed first (errors of its __close handlers are not reported:
        /// call <see cref="Close"/> to see them). One a script made is only
        /// released: the script may still use it. Nothing for a borrowed
        /// handle, a second time, or after the script is disposed.
        /// </summary>
        public void Dispose()
        {
            if (!IsValid)
            {
                return;
            }
            if (started && status == LuaCoroutineStatus.Running)
            {
                throw new InvalidOperationException("a running coroutine of Start cannot be disposed");
            }
            try
            {
                if (started)
                {
                    try
                    {
                        Close();
                    }
                    catch (LuaException)
                    {
                    }
                }
            }
            finally
            {
                script.ReleaseHandle(slot, generation, LuaType.Coroutine);
            }
        }

        /// <summary>A new owning handle to the same coroutine (also from a borrowed one); dispose it separately.</summary>
        public LuaCoroutine Retain()
        {
            return script.RetainCoroutine(Native, status);
        }

        public override string ToString()
        {
            return "coroutine (" + Status.ToString().ToLowerInvariant() + ")";
        }

        // ---- the values of a step ----

        // After a step: its values (borrowed handles for tables and the like), and its status.
        internal unsafe void Take(xlua_value* r, int n, LuaCoroutineStatus after)
        {
            ReleaseValues();
            values.Clear();  // drop strings of the last step
            int keep = n < MaxValues ? n : MaxValues;
            for (int i = 0; i < keep; i++)
            {
                values.Add(script.HeldValue(ref r[i], this));
            }
            status = after;
        }

        // A step that failed: the status it left; the values of the last step end
        // once the native call has run (they may have been its arguments).
        internal void Fail(LuaCoroutineStatus after, bool ran)
        {
            if (ran)
            {
                ReleaseValues();
                values.Clear();
            }
            status = after;
        }

        internal void SetStatus(LuaCoroutineStatus s)
        {
            status = s;
        }

        internal LuaCoroutineStatus RawStatus { get { return status; } }

        internal void Hold(int heldSlot)
        {
            if (heldCount == held.Length)
            {
                Array.Resize(ref held, Math.Max(4, heldCount * 2));
            }
            held[heldCount++] = heldSlot;
        }

        // The borrowed handles of the last step end.
        internal void ReleaseValues()
        {
            while (heldCount > 0)
            {
                script.ReleaseBorrowed(held[--heldCount]);
            }
        }
    }

    public sealed unsafe partial class LuaScript
    {
        const int XLUA_CALLF_DEAD = 8;

        /// <summary>
        /// Runs the global function <paramref name="function"/> (raw lookups,
        /// dotted names as for <see cref="Call(string)"/>) as a new coroutine,
        /// to its first yield or its end, as one call with the limits. An error
        /// throws, as for Call, and leaves no coroutine. Dispose the result.
        /// </summary>
        public LuaCoroutine Start(string function) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args()); }
        public LuaCoroutine Start(string function, LuaValue a1) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args(1, a1, default(LuaValue), default(LuaValue), default(LuaValue))); }
        public LuaCoroutine Start(string function, LuaValue a1, LuaValue a2) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args(2, a1, a2, default(LuaValue), default(LuaValue))); }
        public LuaCoroutine Start(string function, LuaValue a1, LuaValue a2, LuaValue a3) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args(3, a1, a2, a3, default(LuaValue))); }
        public LuaCoroutine Start(string function, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args(4, a1, a2, a3, a4)); }
        public LuaCoroutine Start(string function, ReadOnlySpan<LuaValue> args) { return StartRaw(XLUA_CALL_START | XLUA_CALL_GLOBAL, 0, function, new Args(args)); }

        /// <summary>As <see cref="Start(string)"/>, for a function handle of this script.</summary>
        public LuaCoroutine Start(LuaFunctionRef function, ReadOnlySpan<LuaValue> args)
        {
            if (function.Script != this)
            {
                throw new ArgumentException(function.Script == null ? "default(LuaFunctionRef) refers to no function" : "the function belongs to another LuaScript", "function");
            }
            return StartRaw(XLUA_CALL_START | XLUA_CALL_REF, NativeHandle(function.Slot, function.Generation, LuaType.Function), null, new Args(args));
        }

        LuaCoroutine failedStart;  // the coroutine of a start that failed, for StartRaw to close

        LuaCoroutine StartRaw(int kind, int fn, string name, Args args)
        {
            try
            {
                return RunCoroutine(null, kind, fn, name, args);
            }
            catch
            {
                // A failed start leaves no coroutine: its pending __close handlers run (their errors are dropped).
                LuaCoroutine failed = failedStart;
                failedStart = null;
                if (failed != null)
                {
                    failed.Dispose();
                }
                throw;
            }
        }

        // One step of a coroutine (co null: a start, which makes it) as one
        // xlua_core_call; its values land in the coroutine.
        internal LuaCoroutine RunCoroutine(LuaCoroutine co, int kind, int fn, string name, Args args)
        {
            xlua_value* r = Values(LuaCoroutine.MaxValues);
            LuaCoroutineStatus before = co != null ? co.RawStatus : LuaCoroutineStatus.Yielded;
            if (co != null)
            {
                co.SetStatus(LuaCoroutineStatus.Running);
            }
            int n = 0;
            bool ok = false;
            {
                try
                {
                    n = Raw(kind, 0, fn, name, null, default(ReadOnlySpan<byte>), args, r, LuaCoroutine.MaxValues);
                    ok = true;
                }
                finally
                {
                    if (co == null && rawFn != 0)
                    {
                        int slot = AddHandle(LuaType.Coroutine, rawFn, SlotOwned, 0);
                        co = new LuaCoroutine(this, slot, slots[slot].generation, LuaCoroutineStatus.Yielded, true);
                        slots[slot].owner = co;
                        if (!ok)
                        {
                            failedStart = co;
                        }
                    }
                    if (co != null && co.IsValid)
                    {
                        if (ok)
                        {
                            co.Take(r, n, kind == XLUA_CALL_CLOSE ? LuaCoroutineStatus.Closed
                                : rawStatus == XLUA_YIELD ? LuaCoroutineStatus.Yielded : LuaCoroutineStatus.Finished);
                        }
                        else
                        {
                            bool dead = (rawFlags & XLUA_CALLF_DEAD) != 0;
                            co.Fail(kind == XLUA_CALL_CLOSE ? LuaCoroutineStatus.Closed
                                : dead ? (rawStatus == XLUA_ERRHANDLE ? LuaCoroutineStatus.Finished : LuaCoroutineStatus.Faulted) : before,
                                rawStatus >= 0);
                        }
                    }
                }
            }
            return co;
        }

        // A value of a step as the coroutine keeps it: a handle type becomes a slot borrowed from co.
        internal LuaValue HeldValue(ref xlua_value v, LuaCoroutine co)
        {
            if (!IsHandle(v.type))
            {
                return ToValue(ref v);
            }
            LuaType kind = (LuaType)v.type;
            int slot = Adopt(ref v, SlotBorrowed);
            co.Hold(slot);
            return LuaValue.Handle(kind, this, slot, slots[slot].generation);
        }

        // Ends a borrowed slot of a coroutine's values. Inside a call the native
        // reference goes later (ReleaseUnrefs, LuaCall.cs): a host function may have
        // returned the value, which the trampoline pushes after it returns.
        internal void ReleaseBorrowed(int slot)
        {
            if (L == IntPtr.Zero || slots[slot].state != SlotBorrowed)
            {
                return;
            }
            int native = slots[slot].nativeRef;
            FreeSlot(slot);
            if (depth > 0)
            {
                if (unrefCount == unrefs.Length)
                {
                    Array.Resize(ref unrefs, Math.Max(8, unrefCount * 2));
                }
                unrefs[unrefCount++] = native;
            }
            else
            {
                LuaAPI.xlua_core_unref(L, native);
            }
        }

        internal LuaCoroutine RetainCoroutine(int native, LuaCoroutineStatus status)
        {
            int slot = RetainNative(native, LuaType.Coroutine);
            slots[slot].length = (int)status;
            return CoroutineOf(slot, slots[slot].generation);
        }
    }
}
#endif
