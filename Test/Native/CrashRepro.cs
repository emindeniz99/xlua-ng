// Crash inventory reproductions (docs/research/crash-inventory.md). Each case
// runs in its own process: Test/Native/crash_repro.py starts
// `mono CrashRepro.exe <case>` with a timeout and records the exit status. A
// case prints REPRO_OK when the operation ended in a managed exception or a
// normal return; a crash prints nothing after the case's own output.
using System;
using System.Collections.Generic;
using System.Threading;
using XLua;

public static class ReproTarget
{
    public static LuaEnv Env;
    public static void DisposeEnv() { Env.Dispose(); }
    public static int MaxDepth;
    public static int Depth(LuaFunction f, int n) { if (n > MaxDepth) MaxDepth = n; if (n % 1000 == 0) { Console.WriteLine("depth " + n); Console.Out.Flush(); } return Convert.ToInt32(f.Call(n)[0]); }
    public static int Sum(int[] a) { int s = 0; foreach (var x in a) s += x; return s; }
    public static int Count(List<int> a) { return a.Count; }
    public static void Recurse() { Recurse(); }
}

public static class CrashRepro
{
    static void Run(string name)
    {
        var env = new LuaEnv();
        ReproTarget.Env = env;
        switch (name)
        {
            case "next_invalid_key":
            {
                // ForEach: the action clears the current key and grows the table, so the
                // next lua_next sees a key the rehash dropped: "invalid key to 'next'".
                env.DoString("t = {} for i = 1, 4 do t['k' .. i] = i end");
                var t = env.Global.Get<LuaTable>("t");
                int n = 0;
                t.ForEach<string, int>((k, v) =>
                {
                    t.Set<string, object>(k, null);
                    for (int i = 0; i < 64; i++) t.Set("n" + (n++), i);
                });
                break;
            }
            case "len_metamethod_error_length":
                env.DoString("t = setmetatable({}, { __len = function() error('len failed') end })");
                Console.WriteLine(env.Global.Get<LuaTable>("t").Length);
                break;
            case "len_metamethod_error_array_arg":
                // Inside a C# callback: the error longjmps across the caster's managed frames.
                env.DoString("t = setmetatable({1, 2}, { __len = function() error('len failed') end })");
                Console.WriteLine(env.DoString("local ok, e = pcall(CS.ReproTarget.Sum, t) return tostring(ok) .. ' ' .. tostring(e)")[0]);
                env.DoString("print('after', CS.ReproTarget.Sum({1, 2, 3}))");
                break;
            case "len_metamethod_error_list_arg":
                env.DoString("t = setmetatable({1, 2}, { __len = function() error('len failed') end })");
                Console.WriteLine(env.DoString("local ok, e = pcall(CS.ReproTarget.Count, t) return tostring(ok) .. ' ' .. tostring(e)")[0]);
                break;
            case "gc_error_fullgc":
                // Lua 5.3 and LuaJIT propagate errors of __gc; FullGc calls lua_gc unprotected.
                env.DoString(@"
                    local mt = { __gc = function() error('gc failed') end }
                    if newproxy then local p = newproxy(true) getmetatable(p).__gc = mt.__gc p = nil
                    else setmetatable({}, mt) end");
                env.FullGc();
                break;
            case "gc_error_alloc":
                // The same error from a GC step inside an unprotected C# API call that allocates.
                env.DoString(@"
                    local f = function() error('gc failed') end
                    for i = 1, 2000 do
                        if newproxy then local p = newproxy(true) getmetatable(p).__gc = f
                        else setmetatable({}, { __gc = f }) end
                    end");
                var g = env.Global;
                for (int i = 0; i < 200000; i++) g.Set("k" + (i % 1000), "value" + i);
                break;
            case "dispose_in_callback":
                env.DoString("CS.ReproTarget.DisposeEnv() print('back in Lua')");
                break;
            case "dispose_in_coroutine":
                env.DoString("local co = coroutine.create(function() CS.ReproTarget.DisposeEnv() print('back in Lua') end) print(coroutine.resume(co))");
                break;
            case "deep_recursion_cs_lua":
                env.DoString("function rec(n) return CS.ReproTarget.Depth(rec, n + 1) end");
                try { env.DoString("return rec(0)"); }
                catch (LuaException e) { Console.WriteLine("LuaException: " + e.Message.Split('\n')[0]); }
                Console.WriteLine("deepest C# call: " + ReproTarget.MaxDepth);
                break;
            case "lua_stack_overflow":
                try { env.DoString("local function f() return 1 + f() end return f()"); }
                catch (LuaException e) { Console.WriteLine("LuaException: " + e.Message.Split('\n')[0]); }
                break;
            case "foreign_self_file":
                Console.WriteLine(env.DoString("local sb = CS.System.Text.StringBuilder() local ok, e = pcall(sb.Append, io.stdout, 'x') return tostring(ok) .. ' ' .. tostring(e)")[0]);
                break;
            case "foreign_self_tag":
                Console.WriteLine(env.DoString("local sb = CS.System.Text.StringBuilder() local ok, e = pcall(sb.ToString, coroutine.create(print)) return tostring(ok) .. ' ' .. tostring(e)")[0]);
                break;
            case "two_threads":
            {
                // Two threads in one LuaEnv without THREAD_SAFE.
                env.DoString("function work(i) local t = {} for j = 1, 200 do t[j] = tostring(i + j) end return #t end");
                var work = env.Global.Get<LuaFunction>("work");
                var stop = DateTime.UtcNow.AddSeconds(5);
                Exception seen = null;
                ThreadStart body = () =>
                {
                    try { for (int i = 0; DateTime.UtcNow < stop; i++) work.Call(i); }
                    catch (Exception e) { seen = e; }
                };
                var a = new Thread(body); var b = new Thread(body);
                a.Start(); b.Start(); a.Join(); b.Join();
                Console.WriteLine("threads ended: " + (seen == null ? "no exception" : seen.GetType().Name + ": " + seen.Message.Split('\n')[0]));
                break;
            }
            case "finalizer_thread_call":
            {
                // A C# finalizer calls a Lua delegate on the finalizer thread while the main thread runs Lua.
                env.DoString("function work(i) local t = {} for j = 1, 200 do t[j] = tostring(i + j) end return #t end");
                var work = env.Global.Get<LuaFunction>("work");
                var stop = DateTime.UtcNow.AddSeconds(5);
                var spawner = new Thread(() => { while (DateTime.UtcNow < stop) { FinalizerCaller.Make(work); GC.Collect(); } });
                spawner.Start();
                for (int i = 0; DateTime.UtcNow < stop; i++) work.Call(i);
                spawner.Join();
                Console.WriteLine("calls: " + FinalizerCaller.Calls);
                break;
            }
            case "csharp_stack_overflow":
                env.DoString("CS.ReproTarget.Recurse()");
                break;
            default:
                Console.WriteLine("unknown case " + name);
                Environment.Exit(2);
                break;
        }
        Console.WriteLine("REPRO_OK " + name);
    }

    public static int Main(string[] args)
    {
        try { Run(args[0]); }
        catch (Exception e)
        {
            Console.WriteLine("REPRO_OK " + args[0] + " managed exception " + e.GetType().Name + ": " + e.Message.Split('\n')[0]);
        }
        return 0;
    }
}

class FinalizerCaller
{
    public static int Calls;
    LuaFunction f;
    public static void Make(LuaFunction f) { new FinalizerCaller { f = f }; }
    ~FinalizerCaller() { try { f.Call(1); Interlocked.Increment(ref Calls); } catch { } }
}
