/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The drivers of LuaCoroutine (docs/design/core-luascript.md, section 2.11;
// milestone 9): a script yields a request, a C# handler serves it and fills a
// LuaReply, and the driver resumes the coroutine with the reply, until it is
// done. Run is an IEnumerator for Unity's StartCoroutine (or any loop that
// steps it), RunAsync a Task, and RunAwaitable a Unity Awaitable, compiled only
// where Unity has Awaitable (2023.1 and later). All of them resume on the
// script's thread. The scripting define XLUA_NO_COROUTINE_DRIVERS leaves them
// out (about 5.6 KB of IL); Resume and IsYielded are all a loop of one's own needs.
#if (XLUA_LUASCRIPT || XLUA_CORE_ONLY) && !XLUA_NO_COROUTINE_DRIVERS
namespace XLua
{
    using System;
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The values a request handler passes to the next resume, the results of
    /// the script's <c>coroutine.yield</c>. The driver clears it before each
    /// request and reuses it.
    /// </summary>
    public sealed class LuaReply
    {
        LuaValue[] values = new LuaValue[2];
        int count;

        /// <summary>How many values the reply holds.</summary>
        public int Count { get { return count; } }

        /// <summary>No values: the yield returns nothing.</summary>
        public void Clear()
        {
            Array.Clear(values, 0, count);
            count = 0;
        }

        /// <summary>One value.</summary>
        public void Set(LuaValue v)
        {
            Clear();
            Add(v);
        }

        /// <summary>Two values.</summary>
        public void Set(LuaValue a, LuaValue b)
        {
            Clear();
            Add(a);
            Add(b);
        }

        /// <summary>One more value.</summary>
        public void Add(LuaValue v)
        {
            if (count == values.Length)
            {
                Array.Resize(ref values, count * 2);
            }
            values[count++] = v;
        }

        internal void ResumeWith(LuaCoroutine co)
        {
            co.Resume(new ReadOnlySpan<LuaValue>(values, 0, count));
        }
    }

    /// <summary>
    /// Serves one request of a coroutine for <see cref="LuaCoroutine.Run"/>:
    /// the request is <c>co[0]</c> ... (what the script yielded); the handler
    /// may wait (its enumerator is stepped by the same loop, so in Unity it
    /// may yield <c>WaitForSeconds</c> and the like) and fills reply.
    /// </summary>
    public delegate IEnumerator LuaRequestHandler(LuaCoroutine co, LuaReply reply);

    /// <summary>Serves one request of a coroutine for <see cref="LuaCoroutine.RunAsync"/>.</summary>
    public delegate ValueTask LuaAsyncRequestHandler(LuaCoroutine co, LuaReply reply, CancellationToken ct);

#if UNITY_2023_1_OR_NEWER
    /// <summary>Serves one request of a coroutine for <see cref="LuaCoroutine.RunAwaitable"/>.</summary>
    public delegate UnityEngine.Awaitable LuaAwaitableRequestHandler(LuaCoroutine co, LuaReply reply, CancellationToken ct);
#endif

    public sealed partial class LuaCoroutine
    {
        /// <summary>
        /// Serves the coroutine's requests until it is done: for each yield,
        /// the handler's enumerator is stepped to its end (its values are
        /// yielded on), then the coroutine resumes with the reply. For Unity:
        /// <c>StartCoroutine(co.Run(Handle))</c>. An error of the script
        /// throws from MoveNext and ends the enumeration. The values of the
        /// end stay readable through <see cref="this[int]"/>.
        /// </summary>
        public IEnumerator Run(LuaRequestHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            Check();
            return RunSteps(handler);
        }

        IEnumerator RunSteps(LuaRequestHandler handler)
        {
            var reply = new LuaReply();
            while (IsYielded)
            {
                reply.Clear();
                IEnumerator wait = handler(this, reply);
                if (wait != null)
                {
                    while (wait.MoveNext())
                    {
                        yield return wait.Current;
                    }
                }
                reply.ResumeWith(this);
            }
        }

        /// <summary>
        /// Serves the coroutine's requests until it is done, awaiting the
        /// handler for each. Continues on the SynchronizationContext of the
        /// caller (Unity's main thread), also when the handler uses
        /// <c>ConfigureAwait(false)</c>; without one (plain .NET) it resumes on
        /// whatever thread completes the handler, which needs
        /// <see cref="LuaOptions.CheckThread"/> false. Cancelling ct closes the
        /// coroutine (its __close handlers run) and cancels the task. An error
        /// of the script faults the task.
        /// </summary>
        public Task RunAsync(LuaAsyncRequestHandler handler, CancellationToken ct = default(CancellationToken))
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            Check();
            return RunTask(handler, ct);
        }

        async Task RunTask(LuaAsyncRequestHandler handler, CancellationToken ct)
        {
            var reply = new LuaReply();
            try
            {
                while (IsYielded)
                {
                    ct.ThrowIfCancellationRequested();
                    reply.Clear();
                    await handler(this, reply, ct);
                    ct.ThrowIfCancellationRequested();
                    reply.ResumeWith(this);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Cancelled();
                throw;
            }
        }

        // Cancellation: the coroutine is closed unless it ran to its end.
        void Cancelled()
        {
            if (IsValid && !IsDone && Status != LuaCoroutineStatus.Running)
            {
                Close();
            }
        }

#if UNITY_2023_1_OR_NEWER
        /// <summary>
        /// As <see cref="RunAsync"/>, as a Unity Awaitable (Unity 2023.1 and
        /// later): after each handler the driver returns to the main thread
        /// (Awaitable.MainThreadAsync) if the handler left it, so the script
        /// must belong to the main thread then.
        /// </summary>
        public UnityEngine.Awaitable RunAwaitable(LuaAwaitableRequestHandler handler, CancellationToken ct = default(CancellationToken))
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            Check();
            return RunAwaitableSteps(handler, ct, Environment.CurrentManagedThreadId);
        }

        async UnityEngine.Awaitable RunAwaitableSteps(LuaAwaitableRequestHandler handler, CancellationToken ct, int thread)
        {
            var reply = new LuaReply();
            try
            {
                while (IsYielded)
                {
                    ct.ThrowIfCancellationRequested();
                    reply.Clear();
                    await handler(this, reply, ct);
                    if (Environment.CurrentManagedThreadId != thread)
                    {
                        await UnityEngine.Awaitable.MainThreadAsync();
                    }
                    ct.ThrowIfCancellationRequested();
                    reply.ResumeWith(this);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Cancelled();
                throw;
            }
        }
#endif
    }
}
#endif
