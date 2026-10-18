/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The Editor side of Test/UnityMatrix/core_only_check.py: in a project that
// installs the xLua package with XLUA_CORE_ONLY in Player Settings, checks
// what the editor compiled and loaded, runs a LuaScript on the core library,
// asks the plugin variant selector for its choice, then drives coroutines
// (milestone 9) from EditorApplication.update: the IEnumerator driver stepped
// once per update, as StartCoroutine steps it; the Task driver on Unity's
// SynchronizationContext; and on Unity 2023.1 and later the Awaitable driver
// (completion sources that later updates complete, a cancellation, a script
// error, and Awaitable.NextFrameAsync in a handler, reported only). Exits with
// 0 or 1.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using XLua;

public static class XluaCoreOnlyProbe
{
    static readonly List<string> problems = new List<string>();
    static readonly List<string> checks = new List<string>();
    static string summary;

    public static void Run()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
        foreach (var name in new[] { "XLua", "XLua.Editor" })
        {
            if (loaded.Contains(name)) problems.Add(name + " is loaded");
        }
        foreach (var name in new[] { "XLua.Core", "XLua.Core.Editor" })
        {
            if (!loaded.Contains(name)) problems.Add(name + " is not loaded");
        }
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (!defines.Split(';').Contains("XLUA_CORE_ONLY")) problems.Add("XLUA_CORE_ONLY is not in Player Settings: " + defines);
        string error;
        var preference = PluginVariantSelector.CurrentPreference(out error);
        if (error != null || preference.Count == 0 || preference[0] != PluginVariants.Core)
        {
            problems.Add("the plugin selector prefers " + string.Join(",", preference.Select(p => p ?? "default").ToArray()) + " " + error);
        }
        string result = null;
        try
        {
            using (var lua = new LuaScript(new LuaOptions { MemoryLimit = 4 << 20, InstructionLimit = 1000000 }))
            {
                lua.Load("function calcReward(level, kind) return level * (kind == 'gold' and 10 or 1) end", "probe");
                result = lua.Call<long>("calcReward", 3, "gold") + " " + lua.Load<string>("return _VERSION", "version");
                if (result != "30 Lua 5.5" && result != "30 Lua 5.4") problems.Add("LuaScript returned " + result);
                // Host functions (milestone 7): the dispatcher in the editor's Mono, and an exception back as a Lua error.
                lua.Register("host.bonus", (ref LuaCall c) => c.Return(c.GetLong(0) * 2 + (c.ArgEquals(1, "gold") ? 1 : 0)));
                lua.Register<string, string>("upper", s => s.ToUpperInvariant());
                lua.Register("fail", (ref LuaCall c) => { throw new InvalidOperationException("boom"); });
                string host = lua.Load<string>("return host.bonus(20, 'gold') .. upper('x') .. select(2, pcall(function() fail() end))", "host");
                if (host != "41Xhost:1: InvalidOperationException: boom") problems.Add("host functions returned " + host);
            }
        }
        catch (Exception e)
        {
            problems.Add("LuaScript failed: " + e);
        }
        if (problems.Count > 0)
        {
            Debug.LogError("XLUA_CORE_ONLY_PROBE_FAIL: " + string.Join("; ", problems.ToArray()));
            EditorApplication.Exit(1);
            return;
        }
        summary = result + "; assemblies " + string.Join(",", loaded.Where(n => n.StartsWith("XLua")).ToArray())
            + "; plugin preference " + string.Join(",", preference.Select(p => p ?? "default").ToArray());
        try
        {
            StartCoroutines();
        }
        catch (Exception e)
        {
            Debug.LogError("XLUA_CORE_ONLY_PROBE_FAIL: starting the coroutines failed: " + e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.update += Tick;  // the editor keeps running (no -quit) until Tick exits
    }

    // ---- coroutines (milestone 9), driven by the editor loop ----

    const string Scripts = @"
function quest(level)
  local ok = coroutine.yield({op = 'wait', frames = 3})
  local data = coroutine.yield({op = 'fetch', key = 'rewards'})
  return data.gold * level
end
function sum(n) local s = 0 for i = 1, n do s = s + coroutine.yield(i) end return s end
function closer() local x <close> = setmetatable({}, {__close = function() closed = (closed or 0) + 1 end}) coroutine.yield('open') end
function failafter() coroutine.yield(1) error('boom') end";

    static LuaScript lua;
    static readonly List<LuaTableRef> made = new List<LuaTableRef>();
    static int ticks;
    static double started;
    static LuaCoroutine enumCo, taskCo;
    static IEnumerator enumRun;
    static Task task;

    static void StartCoroutines()
    {
        lua = new LuaScript(new LuaOptions { MemoryLimit = 8 << 20, InstructionLimit = 1000000, TimeLimit = TimeSpan.FromSeconds(1) });
        lua.Load(Scripts, "drivers");
        started = EditorApplication.timeSinceStartup;
        enumCo = lua.Start("quest", 3);
        enumRun = enumCo.Run(Serve);
        taskCo = lua.Start("sum", 4);
        if (SynchronizationContext.Current == null) problems.Add("no SynchronizationContext on the main thread");
        task = taskCo.RunAsync(async (co, reply, ct) =>
        {
            long request = co[0].AsLong();
            await Task.Delay(5).ConfigureAwait(false);  // off the main thread: the driver resumes on Unity's context
            reply.Set(request * 10);
        });
#if UNITY_2023_1_OR_NEWER
        StartAwaitables();
#else
        checks.Add("no Awaitable before Unity 2023.1");
#endif
    }

    // The IEnumerator driver's handler: waits a few updates, as yield return null does under StartCoroutine.
    static IEnumerator Serve(LuaCoroutine co, LuaReply reply)
    {
        LuaTableRef request = co[0].AsTable();
        if (request.GetString("op") == "wait")
        {
            long frames = request.GetLong("frames");
            for (long i = 0; i < frames; i++) yield return null;
            reply.Set(true);
        }
        else
        {
            LuaTableRef data = lua.NewTable();
            data.Set("gold", 100);
            made.Add(data);
            reply.Set(data);
        }
    }

    static void Tick()
    {
        ticks++;
        try
        {
            if (enumRun != null && !enumRun.MoveNext())
            {
                enumRun = null;
                if (enumCo.Status != LuaCoroutineStatus.Finished || enumCo[0].AsLong() != 300) problems.Add("IEnumerator driver: " + enumCo.Status + " " + enumCo[0]);
                else checks.Add("IEnumerator driver (" + ticks + " updates)");
            }
#if UNITY_2023_1_OR_NEWER
            TickAwaitables();
#endif
        }
        catch (Exception e)
        {
            problems.Add("a driver failed: " + e);
            enumRun = null;
        }
        bool done = enumRun == null && task.IsCompleted
#if UNITY_2023_1_OR_NEWER
            && AwaitablesDone()
#endif
            ;
        bool timedOut = EditorApplication.timeSinceStartup - started > 120;
        if (!done && !timedOut)
        {
            return;
        }
        EditorApplication.update -= Tick;
        try
        {
            Finish(timedOut);
        }
        catch (Exception e)
        {
            problems.Add("the checks failed: " + e);
        }
        if (problems.Count > 0)
        {
            Debug.LogError("XLUA_CORE_ONLY_PROBE_FAIL: " + string.Join("; ", problems.ToArray()));
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("XLUA_CORE_ONLY_PROBE_PASS: " + summary + "; coroutines: " + string.Join(", ", checks.ToArray()));
        EditorApplication.Exit(0);
    }

    static void Finish(bool timedOut)
    {
        if (timedOut) problems.Add("the coroutine drivers did not finish in 120 s (" + ticks + " updates)");
        if (task.IsCompleted)
        {
            if (task.Status != TaskStatus.RanToCompletion || taskCo[0].AsLong() != 100) problems.Add("Task driver: " + task.Status + " " + task.Exception);
            else checks.Add("Task driver on the main thread");
        }
        foreach (LuaTableRef t in made) t.Dispose();
        enumCo.Dispose();
        taskCo.Dispose();
#if UNITY_2023_1_OR_NEWER
        FinishAwaitables();
#endif
        if (lua.LiveHandles != 0) problems.Add(lua.LiveHandles + " handles left");
        lua.Dispose();
    }

#if UNITY_2023_1_OR_NEWER
    // A request of the Awaitable driver that a later update completes.
    struct Pending
    {
        public AwaitableCompletionSource Source;
        public LuaReply Reply;
        public long Value;
        public int Due;
    }

    static readonly List<Pending> pending = new List<Pending>();
    static LuaCoroutine awaitCo, cancelCo, failCo, frameCo;
    static string awaitOutcome, cancelOutcome, failOutcome, frameOutcome;
    static CancellationTokenSource cts;
    static int frameStart;

    static void StartAwaitables()
    {
        awaitCo = lua.Start("sum", 5);
        _ = Observe(awaitCo.RunAwaitable((co, reply, ct) =>
        {
            var source = new AwaitableCompletionSource();
            pending.Add(new Pending { Source = source, Reply = reply, Value = co[0].AsLong() * 2, Due = ticks + 2 });
            return source.Awaitable;
        }), o => awaitOutcome = o);
        lua.Load("closed = 0", "reset");
        cancelCo = lua.Start("closer");
        cts = new CancellationTokenSource();
        _ = Observe(cancelCo.RunAwaitable((co, reply, ct) =>
        {
            var source = new AwaitableCompletionSource();
            ct.Register(() => source.TrySetCanceled());  // the handler observes the token
            return source.Awaitable;
        }, cts.Token), o => cancelOutcome = o);
        failCo = lua.Start("failafter");
        _ = Observe(failCo.RunAwaitable((co, reply, ct) =>
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }), o => failOutcome = o);
        // Awaitable.NextFrameAsync in a handler: whether edit mode in batch mode advances it.
        frameCo = lua.Start("sum", 2);
        frameStart = ticks;
        _ = Observe(frameCo.RunAwaitable(async (co, reply, ct) =>
        {
            long v = co[0].AsLong();
            await Awaitable.NextFrameAsync(ct);
            reply.Set(v);
        }), o => frameOutcome = o);
    }

    static async Awaitable Observe(Awaitable run, Action<string> outcome)
    {
        try
        {
            await run;
            outcome("ok");
        }
        catch (OperationCanceledException)
        {
            outcome("cancelled");
        }
        catch (Exception e)
        {
            outcome("error " + e.GetType().Name + ": " + e.Message);
        }
    }

    static void TickAwaitables()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Pending p = pending[i];
            if (ticks >= p.Due)
            {
                pending.RemoveAt(i);
                p.Reply.Set(p.Value);
                p.Source.SetResult();  // the driver resumes the coroutine right here, on the main thread
            }
        }
        if (ticks == 5)
        {
            cts.Cancel();
        }
        if (frameOutcome == null && ticks - frameStart > 600)
        {
            frameOutcome = "did not complete in 600 updates";
        }
    }

    static bool AwaitablesDone()
    {
        return awaitOutcome != null && cancelOutcome != null && failOutcome != null && frameOutcome != null;
    }

    static void FinishAwaitables()
    {
        if (awaitOutcome != "ok" || awaitCo.Status != LuaCoroutineStatus.Finished || awaitCo[0].AsLong() != 30)
            problems.Add("Awaitable driver: " + awaitOutcome + " " + awaitCo.Status + " " + awaitCo[0]);
        else checks.Add("Awaitable driver");
        if (cancelOutcome != "cancelled" || cancelCo.Status != LuaCoroutineStatus.Closed || lua.Globals.GetLong("closed") != 1)
            problems.Add("Awaitable cancellation: " + cancelOutcome + " " + cancelCo.Status + " closed=" + lua.Globals.GetLong("closed"));
        else checks.Add("Awaitable cancellation closes the coroutine");
        if (failOutcome == null || !failOutcome.StartsWith("error LuaRuntimeException") || failCo.Status != LuaCoroutineStatus.Faulted)
            problems.Add("Awaitable error: " + failOutcome + " " + failCo.Status);
        else checks.Add("Awaitable error faults it");
        if (frameOutcome == "ok" && frameCo.Status == LuaCoroutineStatus.Finished && frameCo[0].AsLong() == 3) checks.Add("Awaitable.NextFrameAsync in a handler");
        else checks.Add("Awaitable.NextFrameAsync in a handler: " + frameOutcome);  // reported, not required (edit mode, batch mode)
        awaitCo.Dispose();
        cancelCo.Dispose();
        failCo.Dispose();
        frameCo.Dispose();
        cts.Dispose();
    }
#endif
}
