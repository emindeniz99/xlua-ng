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
        readonly LuaValueList values = new LuaValueList();

        /// <summary>How many values the reply holds.</summary>
        public int Count { get { return values.Count; } }

        /// <summary>No values: the yield returns nothing.</summary>
        public void Clear()
        {
            values.Clear();
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
            values.Add(v);
        }

        internal void ResumeWith(LuaCoroutine co)
        {
            co.ResumeWith(values);
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

        Task RunTask(LuaAsyncRequestHandler handler, CancellationToken ct)
        {
            var run = new TaskDriver(this, handler, ct);
            run.Next(false);
            return run.Task;
        }

        // RunAsync's loop as continuations of the handler's ValueTask instead of an async
        // method: an async method costs an IL2CPP player the method builder's generic code over
        // its state machine (docs/design/core-luascript.md, section 8.15). The same semantics:
        // the loop continues on the context the awaiter captures (as await does), a synchronous
        // handler continues at once, an OperationCanceledException cancels the task (and with ct
        // cancelled closes the coroutine first), any other exception faults it.
        sealed class TaskDriver
        {
            readonly LuaCoroutine co;
            readonly LuaAsyncRequestHandler handler;
            readonly CancellationToken ct;
            readonly LuaReply reply = new LuaReply();
            readonly TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            System.Runtime.CompilerServices.ValueTaskAwaiter awaiter;
            Action resume;

            internal TaskDriver(LuaCoroutine co, LuaAsyncRequestHandler handler, CancellationToken ct)
            {
                this.co = co;
                this.handler = handler;
                this.ct = ct;
            }

            internal Task Task { get { return source.Task; } }

            internal void Next(bool awaited)
            {
                try
                {
                    if (awaited)
                    {
                        Served();
                    }
                    while (co.IsYielded)
                    {
                        ct.ThrowIfCancellationRequested();
                        reply.Clear();
                        awaiter = handler(co, reply, ct).GetAwaiter();
                        if (!awaiter.IsCompleted)
                        {
                            if (resume == null)
                            {
                                resume = Resume;
                            }
                            awaiter.OnCompleted(resume);
                            return;
                        }
                        Served();
                    }
                    source.TrySetResult(null);
                }
                catch (OperationCanceledException e)
                {
                    if (ct.IsCancellationRequested)
                    {
                        try
                        {
                            co.Cancelled();
                        }
                        catch (Exception closing)
                        {
                            source.TrySetException(closing);
                            return;
                        }
                    }
                    source.TrySetCanceled(e.CancellationToken);
                }
                catch (Exception e)
                {
                    source.TrySetException(e);
                }
            }

            // The handler finished: its exception, if any, then the resume with its reply.
            void Served()
            {
                System.Runtime.CompilerServices.ValueTaskAwaiter done = awaiter;
                awaiter = default(System.Runtime.CompilerServices.ValueTaskAwaiter);
                done.GetResult();
                ct.ThrowIfCancellationRequested();
                reply.ResumeWith(co);
            }

            void Resume()
            {
                Next(true);
            }
        }

        // Cancellation: the coroutine is closed unless it ran to its end.
        internal void Cancelled()
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

        UnityEngine.Awaitable RunAwaitableSteps(LuaAwaitableRequestHandler handler, CancellationToken ct, int thread)
        {
            var run = new AwaitableDriver(this, handler, ct, thread);
            run.Next(false);
            return run.Awaitable;
        }

        // RunAwaitable's loop as continuations, as TaskDriver for RunAsync, completing an
        // AwaitableCompletionSource; an exception (OperationCanceledException too) completes it
        // with that exception, as an async Awaitable method does.
        sealed class AwaitableDriver
        {
            readonly LuaCoroutine co;
            readonly LuaAwaitableRequestHandler handler;
            readonly CancellationToken ct;
            readonly int thread;
            readonly LuaReply reply = new LuaReply();
            readonly UnityEngine.AwaitableCompletionSource source = new UnityEngine.AwaitableCompletionSource();
            UnityEngine.Awaitable pending;  // the handler's, until its continuation has read the result
            Action resume, resumeOnMain;

            internal AwaitableDriver(LuaCoroutine co, LuaAwaitableRequestHandler handler, CancellationToken ct, int thread)
            {
                this.co = co;
                this.handler = handler;
                this.ct = ct;
                this.thread = thread;
            }

            internal UnityEngine.Awaitable Awaitable { get { return source.Awaitable; } }

            internal void Next(bool awaited)
            {
                try
                {
                    if (awaited && !Served())
                    {
                        return;
                    }
                    while (co.IsYielded)
                    {
                        ct.ThrowIfCancellationRequested();
                        reply.Clear();
                        pending = handler(co, reply, ct);
                        var waiting = pending.GetAwaiter();
                        if (!waiting.IsCompleted)
                        {
                            if (resume == null)
                            {
                                resume = Resume;
                            }
                            waiting.OnCompleted(resume);
                            return;
                        }
                        if (!Served())
                        {
                            return;
                        }
                    }
                    source.TrySetResult();
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }

            // The handler finished: its exception, if any; then back to the main thread (false:
            // the step goes on there) and the resume with the reply.
            bool Served()
            {
                UnityEngine.Awaitable done = pending;
                pending = null;
                done.GetAwaiter().GetResult();
                if (Environment.CurrentManagedThreadId != thread)
                {
                    var main = UnityEngine.Awaitable.MainThreadAsync().GetAwaiter();
                    if (!main.IsCompleted)
                    {
                        if (resumeOnMain == null)
                        {
                            resumeOnMain = ResumeOnMain;
                        }
                        main.OnCompleted(resumeOnMain);
                        return false;
                    }
                }
                Resumed();
                return true;
            }

            void Resumed()
            {
                ct.ThrowIfCancellationRequested();
                reply.ResumeWith(co);
            }

            void Fail(Exception e)
            {
                if (e is OperationCanceledException && ct.IsCancellationRequested)
                {
                    try
                    {
                        co.Cancelled();
                    }
                    catch (Exception closing)
                    {
                        e = closing;
                    }
                }
                source.TrySetException(e);
            }

            void Resume()
            {
                Next(true);
            }

            void ResumeOnMain()
            {
                try
                {
                    Resumed();
                }
                catch (Exception e)
                {
                    Fail(e);
                    return;
                }
                Next(false);
            }
        }
#endif
    }
}
#endif
