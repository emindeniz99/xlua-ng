/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The host tests of LuaScript's coroutines (docs/design/core-luascript.md,
// section 7.4; milestone 9): LuaScript.Start, LuaCoroutine's steps, values,
// errors, Close and handles, the limits per step, the thread check, the
// IEnumerator and Task drivers (the Awaitable one runs in the Unity editor,
// Test/UnityMatrix/CoreOnly/XluaCoreOnlyProbe.cs), the allocation probe and
// the cost. Part of CoreTests (Test/Native/run_core.py).
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XLua;

public static partial class CoreTests
{
    const string CoroutineFunctions = @"
function none() end
function gen(n) for i = 1, n do coroutine.yield(i, i * 2) end return 'done', n end
function sum(n) local s = 0 for i = 1, n do s = s + coroutine.yield(i) end return s end
function args(...) local n = select('#', ...) local back = {coroutine.yield(n, ...)} return #back, table.unpack(back) end
function deep()
  local function inner(x) return coroutine.yield(x) end
  local ok, v = pcall(inner, 'in pcall')
  local t = setmetatable({}, {__index = function(_, k) return coroutine.yield('index ' .. k) end})
  return inner('nested') .. v .. t.key
end
function failafter(n) for i = 1, n do coroutine.yield(i) end error('boom') end
function failtable() coroutine.yield(1) error({code = 5}) end
function closer()
  local x <close> = setmetatable({}, {__close = function(_, e) closed = (closed or 0) + 1 closedwith = e end})
  coroutine.yield('open')
  return 'end'
end
function closefails()
  local x <close> = setmetatable({}, {__close = function() error('close failed') end})
  coroutine.yield('open')
end
function closeloops()
  local x <close> = setmetatable({}, {__close = function() while true do end end})
  coroutine.yield('open')
end
function startfails() local x <close> = setmetatable({}, {__close = function(_, e) closed = (closed or 0) + 1 closedwith = e end}) error('at start') end
function burn(n, rounds) for r = 1, rounds do local s = 0 for i = 1, n do s = s + i end coroutine.yield(r) end return 'burnt' end
function spinafter() coroutine.yield(1) while true do end end
function spread() while true do coroutine.wrap(function() for i = 1, 100 do end end)() end end
function longtail(n) coroutine.yield(1) local s = 0 for i = 1, n do s = s + i end return s end
function handles() coroutine.yield({x = 1}, function() return 7 end, buffer and buffer.create(4)) return {y = 2} end
function make() return coroutine.create(function(a) local b = coroutine.yield(a + 1) return b * 2 end) end
function shared2() shared = coroutine.create(function() coroutine.yield(1) coroutine.yield(2) end) return shared end
function finishshared() while coroutine.status(shared) ~= 'dead' do coroutine.resume(shared) end end
function costatus(co) return coroutine.status(co) end
function many() local t = {} for i = 1, 100 do t[i] = i end coroutine.yield(table.unpack(t)) end
function eat(mb) local t = {} for i = 1, mb do t[i] = string.rep('x', 1 << 20) .. i end coroutine.yield(#t) return #t end
function catcheat() local ok, e = pcall(function() local t = {} for i = 1, 64 do t[i] = string.rep('x', 1 << 20) .. i end end) coroutine.yield(ok, e) return 'alive' end
function quest(level)
  local ok = coroutine.yield({op = 'wait', seconds = 1.5})
  local data = coroutine.yield({op = 'fetch', key = 'rewards'})
  return data.gold * level
end
";

    static LuaScript OpenCoroutines(LuaOptions options)
    {
        var lua = new LuaScript(options);
        lua.Load(CoroutineFunctions, "co");
        return lua;
    }

    // ---- steps and values ----

    public static void TestCoroutineStartAndResume()
    {
        using (var lua = OpenCoroutines(new LuaOptions { Libraries = LuaLibs.Safe }))
        {
            using (LuaCoroutine co = lua.Start("gen", 3))
            {
                Equal(LuaCoroutineStatus.Yielded, co.Status, "after Start: the first yield");
                Check(co.IsYielded && !co.IsDone && co.IsValid, "IsYielded, IsDone, IsValid");
                Equal(2, co.ValueCount, "two values per yield");
                Check(co[0] == 1 && co[1] == 2 && co[2].IsNil, "the yielded values, nil beyond");
                Equal(2L, co[1].AsLong(), "AsLong");
                Equal(2.0, co[1].AsDouble(), "AsDouble");
                Throws<LuaTypeException>(() => co[0].AsString(), "AsString of a number");
                Throws<ArgumentOutOfRangeException>(() => { LuaValue v = co[-1]; }, "a negative index");
                co.Resume();
                co.Resume();
                Check(co[0] == 3 && co[1] == 6, "the third yield");
                co.Resume();
                Equal(LuaCoroutineStatus.Finished, co.Status, "finished");
                Check(co.IsDone && !co.IsYielded, "IsDone");
                Check(co.ValueCount == 2 && co[0].AsString() == "done" && co[1] == 3, "the return values stay readable");
                var e = Throws<InvalidOperationException>(() => co.Resume(), "Resume after the end");
                if (e != null) Contains(e.Message, "finished", "the message");
                Equal(1, lua.LiveHandles, "a coroutine is a handle");
            }
            Equal(0, lua.LiveHandles, "Dispose released it");

            // Resume values become the results of coroutine.yield.
            using (LuaCoroutine co = lua.Start("sum", 4))
            {
                long expected = 0;
                for (int i = 1; co.IsYielded; i++)
                {
                    Equal((long)i, co[0].AsLong(), "yield " + i);
                    co.Resume(i * 10);
                    expected += i * 10;
                }
                Equal(expected, co[0].AsLong(), "the sum of the resume values");
            }
            using (LuaCoroutine co = lua.Start("args", 1, "two", true, 4.5))
            {
                Check(co[0] == 4 && co[1] == 1 && co[2] == "two" && co[3] == true && co[4] == 4.5, "four start arguments");
                co.Resume(new LuaValue[] { "a", "b", 3, false, 5 });
                Check(co[0] == 5 && co[1] == "a" && co[3] == 3 && co[4] == false && co[5] == 5, "a span of five resume values: " + co[0]);
            }
            using (LuaCoroutine co = lua.Start("args", new LuaValue[] { 1, 2, 3, 4, 5, 6 }))
            {
                Equal(7, co.ValueCount, "a span of start arguments");
            }
            using (LuaFunctionRef f = lua.Globals.GetFunction("gen"))
            using (LuaCoroutine co = lua.Start(f, new LuaValue[] { 1 }))
            {
                Check(co.IsYielded && co[0] == 1, "Start of a function handle");
                co.Resume();
                Equal("done", co[0].AsString(), "and its end");
            }
            using (LuaCoroutine co = lua.Start("none"))
            {
                Equal(LuaCoroutineStatus.Finished, co.Status, "a function that never yields finishes in Start");
                Equal(0, co.ValueCount, "no values");
            }
            using (LuaCoroutine co = lua.Start("many"))
            {
                Equal(LuaCoroutine.MaxValues, co.ValueCount, "at most MaxValues values");
                Equal(64L, co[63].AsLong(), "the last one kept");
            }
            Throws<ArgumentNullException>(() => lua.Start((string)null), "a null name");
            Throws<ArgumentException>(() => lua.Start(default(LuaFunctionRef), ReadOnlySpan<LuaValue>.Empty), "default(LuaFunctionRef)");
            Equal(0, lua.LiveHandles, "no handle left");
        }
    }

    public static void TestCoroutineNestedYields()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        using (LuaCoroutine co = lua.Start("deep"))
        {
            Equal("in pcall", co[0].AsString(), "a yield across pcall");
            co.Resume("P");
            Equal("nested", co[0].AsString(), "a yield from a nested function");
            co.Resume("N");
            Equal("index key", co[0].AsString(), "a yield from __index (a metamethod)");
            co.Resume("K");
            Equal("NPK", co[0].AsString(), "the resume values arrived");
        }
    }

    public static void TestCoroutineHostYield()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        {
            bool canYield = false;
            lua.Register("fetch", (ref LuaCall c) =>
            {
                canYield = c.CanYield;
                c.Return("request:" + c.GetString(0));
                c.Yield();
            });
            lua.Load("function job(base) local reply = fetch('rewards') return reply * base end", "job");
            using (LuaCoroutine co = lua.Start("job", 10))
            {
                Check(canYield, "CanYield in a coroutine of Start");
                Check(co.IsYielded && co[0].AsString() == "request:rewards", "the host function's results are the yielded values");
                co.Resume(4);
                Check(co.Status == LuaCoroutineStatus.Finished && co[0] == 40, "the resume values are fetch's results: " + co[0]);
            }
            var e = Throws<LuaRuntimeException>(() => lua.Load("fetch('x')", "main"), "Yield outside a coroutine");
            if (e != null) Check(e.InnerException is InvalidOperationException, "InvalidOperationException inside");
            // The coroutine itself, from inside: running, so it cannot be resumed or closed.
            LuaCoroutine self = null;
            lua.Register("selfstep", (ref LuaCall c) =>
            {
                Equal(LuaCoroutineStatus.Running, self.Status, "Running while it runs");
                Throws<InvalidOperationException>(() => self.Resume(), "Resume of the running coroutine");
                Throws<InvalidOperationException>(() => self.Close(), "Close of the running coroutine");
                Throws<InvalidOperationException>(() => self.Dispose(), "Dispose of the running coroutine");
                using (LuaCoroutine running = c.GetCoroutine(0).Retain())
                {
                    Equal(LuaCoroutineStatus.Running, running.Status, "coroutine.running() is running");
                    Throws<InvalidOperationException>(() => running.Resume(), "Resume through another handle");
                }
            });
            lua.Load("function selfjob() coroutine.yield(1) selfstep(coroutine.running()) return 2 end", "self");
            using (self = lua.Start("selfjob"))
            {
                self.Resume();
                Check(self.Status == LuaCoroutineStatus.Finished && self[0] == 2, "it ran on");
            }
        }
    }

    // ---- errors ----

    public static void TestCoroutineErrors()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        {
            using (LuaCoroutine co = lua.Start("failafter", 1))
            {
                var e = Throws<LuaRuntimeException>(() => co.Resume(), "an error throws from Resume");
                if (e != null)
                {
                    Contains(e.Message, "co:12: boom", "the message with its position");
                    // The coroutine's own frames: its body has no caller to name it, as in Lua.
                    Contains(e.LuaStackTrace, "'error'\n\tco:12: in function <co:12>", "the coroutine's traceback");
                }
                Equal(LuaCoroutineStatus.Faulted, co.Status, "Faulted");
                Check(co.IsDone && co.ValueCount == 0, "done, no values");
                Throws<InvalidOperationException>(() => co.Resume(), "a faulted coroutine cannot be resumed");
            }
            using (LuaCoroutine co = lua.Start("failtable"))
            {
                var e = Throws<LuaRuntimeException>(() => co.Resume(), "error({code = 5})");
                if (e != null)
                {
                    using (LuaTableRef t = e.ErrorValue.AsTable())
                    {
                        Equal(5L, t.GetLong("code"), "ErrorValue keeps the table");
                    }
                }
            }
            lua.Register("hostfail", (ref LuaCall c) => { throw new FormatException("bad host"); });
            lua.Load("function hostjob() coroutine.yield(1) hostfail() end", "h");
            using (LuaCoroutine co = lua.Start("hostjob"))
            {
                var e = Throws<LuaRuntimeException>(() => co.Resume(), "a host exception");
                if (e != null) Check(e.InnerException is FormatException, "InnerException: " + e.InnerException);
            }
            int before = lua.LiveHandles;
            var se = Throws<LuaRuntimeException>(() => lua.Start("failafter", 0), "an error before the first yield throws from Start");
            if (se != null) Contains(se.LuaStackTrace, "co:12: in function <co:12>", "a traceback for Start");
            Throws<LuaRuntimeException>(() => lua.Start("missing"), "a missing function");
            Throws<LuaRuntimeException>(() => lua.Start("string.nothing"), "a missing dotted function");
            Equal(before, lua.LiveHandles, "a failed Start leaves no handle");
            lua.Load("closed = 0", "reset");
            Throws<LuaRuntimeException>(() => lua.Start("startfails"), "an error with a pending __close");
            Equal(1L, lua.Globals.GetLong("closed"), "a failed Start closes the coroutine: __close ran");
            Contains(lua.Globals.GetString("closedwith"), "at start", "and got the error");
            using (LuaCoroutine co = lua.Start("gen", 2))
            {
                Throws<ArgumentException>(() => co.Resume(LuaValue.OfType(LuaType.Table)), "an argument without a handle");
                Check(co.IsYielded && co[0] == 1, "a refused argument leaves the coroutine and its values");
                co.Resume();
                Equal(2L, co[0].AsLong(), "and it resumes");
            }
        }
    }

    // ---- Close and Dispose ----

    public static void TestCoroutineClose()
    {
        using (var lua = OpenCoroutines(new LuaOptions { InstructionLimit = 1000000 }))
        {
            lua.Load("closed = 0", "reset");
            using (LuaCoroutine co = lua.Start("closer"))
            {
                Equal("open", co[0].AsString(), "suspended with a pending __close");
                co.Close();
                Equal(1L, lua.Globals.GetLong("closed"), "Close runs __close");
                Equal(LuaCoroutineStatus.Closed, co.Status, "Closed");
                Equal(0, co.ValueCount, "no values");
                Throws<InvalidOperationException>(() => co.Resume(), "Resume after Close");
                co.Close();
                Equal(1L, lua.Globals.GetLong("closed"), "a second Close does nothing");
            }
            LuaCoroutine disposed = lua.Start("closer");
            disposed.Dispose();
            Equal(2L, lua.Globals.GetLong("closed"), "Dispose closes a coroutine of Start");
            Check(!disposed.IsValid && disposed.Status == LuaCoroutineStatus.Closed, "and releases it");
            disposed.Dispose();
            using (LuaCoroutine co = lua.Start("closer"))
            {
                co.Resume();
                Equal(3L, lua.Globals.GetLong("closed"), "a return closes it");
                co.Close();
                Equal(LuaCoroutineStatus.Closed, co.Status, "Close of a finished coroutine");
                Equal(3L, lua.Globals.GetLong("closed"), "runs nothing");
            }
            using (LuaCoroutine co = lua.Start("closefails"))
            {
                var e = Throws<LuaRuntimeException>(() => co.Close(), "an error in __close throws from Close");
                if (e != null) Contains(e.Message, "close failed", "its message");
                Equal(LuaCoroutineStatus.Closed, co.Status, "closed anyway");
            }
            LuaCoroutine quiet = lua.Start("closefails");
            quiet.Dispose();
            Check(!quiet.IsValid, "Dispose does not report __close errors");
            using (LuaCoroutine co = lua.Start("closeloops"))
            {
                Throws<LuaInstructionLimitException>(() => co.Close(), "a looping __close stops at the limit");
            }
            // A faulted coroutine: its own error was thrown; Close runs the handlers quietly.
            lua.Load("function faultclose() local x <close> = setmetatable({}, {__close = function(_, e) closed = closed + 1 closedwith = e end}) coroutine.yield(1) error('late') end", "fc");
            using (LuaCoroutine co = lua.Start("faultclose"))
            {
                Throws<LuaRuntimeException>(() => co.Resume(), "the error");
                Equal(LuaCoroutineStatus.Faulted, co.Status, "faulted, __close pending");
                long n = lua.Globals.GetLong("closed");
                co.Close();
                Equal(n + 1, lua.Globals.GetLong("closed"), "Close of a faulted coroutine runs __close");
                Contains(lua.Globals.GetString("closedwith"), "late", "with the coroutine's error");
            }
            // A coroutine of a script is only released: the script may use it.
            lua.Load("keep = coroutine.create(function() coroutine.yield(1) return 'still' end)", "keep");
            using (LuaCoroutine kept = lua.Globals.Get<LuaCoroutine>("keep"))
            {
                kept.Resume();
            }
            Equal("still", lua.Load<string>("return select(2, coroutine.resume(keep))", "after"), "Dispose did not close the script's coroutine");
        }
    }

    // ---- handles ----

    public static void TestCoroutineHandles()
    {
        using (var lua = OpenCoroutines(new LuaOptions { Libraries = LuaLibs.Safe | (BufferLibraryBuilt() ? LuaLibs.Buffer : 0) }))
        {
            LuaTableRef kept;
            using (LuaCoroutine co = lua.Start("handles"))
            {
                LuaTableRef t = co[0].AsTable();
                Equal(1L, t.GetLong("x"), "a yielded table");
                Equal(7L, co[1].AsFunction().Call<long>(), "a yielded function");
                if (BufferLibraryBuilt())
                {
                    Equal(4, co[2].AsBuffer().Length, "a yielded buffer");
                }
                Equal(1, lua.LiveHandles, "yielded handles are borrowed: only the coroutine counts");
                kept = t.Retain();
                Equal(2, lua.LiveHandles, "Retain makes an owning one");
                co.Resume();
                Check(!t.IsValid, "a borrowed table ends with the next step");
                Equal(1L, kept.GetLong("x"), "the retained one stays");
                Equal(2L, co[0].AsTable().GetLong("y"), "a returned table");
            }
            Equal(1, lua.LiveHandles, "the retained table");
            kept.Dispose();
            Equal(0, lua.LiveHandles, "all released");

            // A coroutine a script made is a LuaCoroutine too.
            using (LuaCoroutine sc = lua.Call<LuaCoroutine>("make"))
            {
                Equal(LuaCoroutineStatus.Yielded, sc.Status, "not started: suspended");
                sc.Resume(10);
                Equal(11L, sc[0].AsLong(), "started with its arguments");
                Equal("suspended", lua.Call<string>("costatus", sc), "passed back to the script");
                sc.Resume(5);
                Check(sc.Status == LuaCoroutineStatus.Finished && sc[0] == 10, "finished");
                Equal("dead", lua.Call<string>("costatus", sc), "the script sees it dead");
            }
            // The script resumes it behind C#'s back: Resume finds the real state.
            using (LuaCoroutine sc = lua.Call<LuaCoroutine>("shared2"))
            {
                lua.Call("finishshared");
                Equal(LuaCoroutineStatus.Yielded, sc.Status, "C# saw it suspended");
                var e = Throws<InvalidOperationException>(() => sc.Resume(), "Resume of a coroutine the script finished");
                if (e != null) Contains(e.Message, "dead", "Lua's message");
                Equal(LuaCoroutineStatus.Finished, sc.Status, "now finished");
            }
            using (LuaValueHolder v = new LuaValueHolder(lua.Load<LuaValue>("return coroutine.create(function() end), coroutine.running()", "values")))
            {
                Equal(LuaType.Coroutine, v.Value.Type, "a LuaValue of a coroutine");
                Check(v.Value.AsCoroutine() == v.Value.AsCoroutine(), "one LuaCoroutine per handle");
                Equal(LuaCoroutineStatus.Yielded, v.Value.AsCoroutine().Status, "its status");
            }
            using (LuaCoroutine main = lua.Load<LuaCoroutine>("return coroutine.running()", "main"))
            {
                Equal(LuaCoroutineStatus.Running, main.Status, "the main thread is always running");
                Throws<InvalidOperationException>(() => main.Resume(), "and cannot be resumed");
            }
            Equal(0, lua.LiveHandles, "LuaValue.Dispose released the coroutine");
            // In tables and iterations.
            lua.Load("tab = {co = coroutine.create(function() coroutine.yield('from pairs') end)}", "tab");
            using (LuaTableRef tab = lua.Globals.GetTable("tab"))
            {
                Equal(LuaType.Coroutine, tab.TypeOf("co"), "TypeOf");
                foreach (LuaPair p in tab.Pairs())
                {
                    Equal(LuaType.Coroutine, p.ValueType, "a pair's value type");
                    LuaCoroutine borrowed = p.Value.AsCoroutine();
                    borrowed.Resume();
                    Equal("from pairs", borrowed[0].AsString(), "a borrowed coroutine of a pair resumes");
                }
                using (LuaCoroutine co = tab.Get<LuaCoroutine>("co"))
                {
                    Equal(LuaCoroutineStatus.Yielded, co.Status, "Get<LuaCoroutine> of a table");
                    co.Resume();
                    Equal(LuaCoroutineStatus.Finished, co.Status, "finished");
                }
                Check(tab.Get<LuaCoroutine>("missing", null) == null, "a fallback");
            }
            // As host function arguments: borrowed, Retain keeps them.
            LuaCoroutine scheduled = null;
            lua.Register("schedule", (ref LuaCall c) =>
            {
                Equal(LuaType.Coroutine, c.ArgType(0), "ArgType");
                LuaCoroutine arg = c.GetCoroutine(0);
                Check(arg.IsYielded, "a new coroutine argument");
                scheduled = arg.Retain();
                bool refused = false;
                try { c.GetCoroutine(1); } catch (LuaTypeException) { refused = true; }
                Check(refused, "GetCoroutine of a number throws");
                c.Return(c.Get(0));
            });
            Equal(true, lua.Load<bool>("local co = coroutine.create(function(x) coroutine.yield(x) return x * 3 end) return schedule(co, 1) == co", "sched"),
                "a coroutine argument returned as the same value");
            using (scheduled)
            {
                scheduled.Resume(5);
                Equal(5L, scheduled[0].AsLong(), "a coroutine from a host function, resumed from C#");
                scheduled.Resume();
                Equal(15L, scheduled[0].AsLong(), "to its end");
            }
            Equal(0, lua.LiveHandles, "nothing left");

            // Script disposal releases every coroutine.
            var other = OpenCoroutines(new LuaOptions());
            LuaCoroutine left = other.Start("gen", 5);
            Equal(1, other.LiveHandles, "one coroutine");
            other.Dispose();
            Check(!left.IsValid && left.Status == LuaCoroutineStatus.Closed, "invalid after the script's Dispose");
            Throws<ObjectDisposedException>(() => left.Resume(), "Resume");
            Throws<ObjectDisposedException>(() => { LuaValue v = left[0]; }, "a value");
            left.Close();
            left.Dispose();
            using (var third = OpenCoroutines(new LuaOptions()))
            using (LuaCoroutine foreign = third.Start("gen", 1))
            {
                Throws<ArgumentException>(() => lua.Call("costatus", foreign), "a coroutine of another script");
            }
            Equal(0, lua.LiveHandles, "nothing left");
        }
    }

    static bool BufferLibraryBuilt()
    {
        try
        {
            using (new LuaScript(new LuaOptions { Libraries = LuaLibs.Safe | LuaLibs.Buffer }))
            {
                return true;
            }
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    // ---- limits ----

    public static void TestCoroutineLimits()
    {
        // Each step is a call of its own: the full budget per Resume.
        using (var lua = OpenCoroutines(new LuaOptions { InstructionLimit = 200000 }))
        {
            using (LuaCoroutine co = lua.Start("burn", 10000, 20))
            {
                long total = lua.LastInstructionCount;
                Check(total > 10000 && total < 200000, "Start counts its own instructions: " + total);
                while (co.IsYielded)
                {
                    co.Resume();
                    total += lua.LastInstructionCount;
                }
                Equal("burnt", co[0].AsString(), "twenty steps of about 20,000 instructions under a limit of 200,000");
                Check(total > 200000, "the total is above the limit: " + total);
            }
            using (LuaCoroutine co = lua.Start("spinafter"))
            {
                Throws<LuaInstructionLimitException>(() => co.Resume(), "a step past the limit");
                Equal(LuaCoroutineStatus.Faulted, co.Status, "a limit leaves it faulted");
            }
            Throws<LuaInstructionLimitException>(() => lua.Start("spread"), "coroutines inside a step share its budget (switch accounting)");
            Throws<LuaInstructionLimitException>(() => lua.Start("burn", 10000000, 1), "Start past the limit");
            // The same count for the same step.
            long first = -1;
            for (int i = 0; i < 20; i++)
            {
                using (LuaCoroutine co = lua.Start("burn", 100, 3))
                {
                    co.Resume();
                    if (first < 0) first = lua.LastInstructionCount;
                    else Equal(first, lua.LastInstructionCount, "an exact count per step");
                }
            }
            // A coroutine left suspended with a budget hook, resumed without a limit, runs to its end.
            using (LuaCoroutine co = lua.Start("longtail", 3000000))
            {
                lua.InstructionLimit = 0;
                co.Resume();
                Equal(4500001500000L, co[0].AsLong(), "no stale budget");
                lua.InstructionLimit = 200000;
            }
        }
        using (var lua = OpenCoroutines(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(200) }))
        {
            lua.Register("wait", (ref LuaCall c) => Thread.Sleep((int)c.GetLong(0)));
            lua.Load("function slow(rounds) for r = 1, rounds do wait(60) coroutine.yield(r) end return 'slow' end", "slow");
            var watch = Stopwatch.StartNew();
            using (LuaCoroutine co = lua.Start("slow", 5))
            {
                while (co.IsYielded) co.Resume();
                Equal("slow", co[0].AsString(), "five steps of 60 ms under a time limit of 200 ms");
            }
            Check(watch.ElapsedMilliseconds >= 290, "the total is above the limit: " + watch.ElapsedMilliseconds + " ms");
            using (LuaCoroutine co = lua.Start("spinafter"))
            {
                watch.Restart();
                Throws<LuaTimeLimitException>(() => co.Resume(), "a step past the time limit (the watchdog)");
                Check(watch.ElapsedMilliseconds < 5000, "stopped in " + watch.ElapsedMilliseconds + " ms");
                Equal(LuaCoroutineStatus.Faulted, co.Status, "faulted");
            }
            // Interrupt from another thread stops the running step.
            lua.TimeLimit = TimeSpan.FromSeconds(30);
            using (LuaCoroutine co = lua.Start("spinafter"))
            {
                var timer = new Thread(() => { Thread.Sleep(50); lua.Interrupt(); });
                watch.Restart();
                timer.Start();
                Throws<LuaInterruptedException>(() => co.Resume(), "Interrupt during a step");
                timer.Join();
                Check(watch.ElapsedMilliseconds < 10000, "interrupted in " + watch.ElapsedMilliseconds + " ms");
            }
        }
        using (var lua = OpenCoroutines(new LuaOptions { MemoryLimit = 16 << 20 }))
        {
            using (LuaCoroutine co = lua.Start("eat", 4))
            {
                Equal(4L, co[0].AsLong(), "4 MB in a step under 16 MB");
                co.Resume();
            }
            Throws<LuaMemoryLimitException>(() => lua.Start("eat", 64), "64 MB in a step under 16 MB");
            using (LuaCoroutine co = lua.Start("catcheat"))
            {
                Check(co[0] == false && co[1] == "not enough memory", "pcall inside the coroutine catches the memory error: " + co[1]);
                co.Resume();
                Equal("alive", co[0].AsString(), "and it runs on");
            }
        }
    }

    // A host function that resumes coroutines: nested steps share the outer call's budget.
    public static void TestCoroutineNestedSteps()
    {
        using (var lua = OpenCoroutines(new LuaOptions { InstructionLimit = 100000 }))
        {
            LuaCoroutine inner = lua.Start("gen", 1000000000);
            lua.Register("step", (ref LuaCall c) =>
            {
                inner.Resume();
                c.Return(inner[0]);
            });
            lua.Register("startin", (ref LuaCall c) =>
            {
                using (LuaCoroutine co = c.Script.Start("gen", 2))
                {
                    co.Resume();
                    c.Return(co[1]);
                }
            });
            Equal(4L, lua.Load<long>("return startin()", "startin"), "Start inside a host function");
            Equal(3L, lua.Load<long>("step() return step()", "steps"), "nested steps");
            Throws<LuaInstructionLimitException>(() => lua.Load("while true do step() end", "loop"), "the outer call's budget covers the nested steps");
            Check(inner.Status == LuaCoroutineStatus.Faulted || inner.Status == LuaCoroutineStatus.Yielded,
                "the stop hit the nested step (faulted) or the loop (still suspended): " + inner.Status);
            inner.Dispose();
            Equal(0, lua.LiveHandles, "released");
        }
    }

    // ---- threads ----

    public static void TestCoroutineThreadCheck()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        using (LuaCoroutine co = lua.Start("gen", 3))
        {
            Exception error = null;
            var t = new Thread(() => { try { co.Resume(); } catch (Exception e) { error = e; } });
            t.Start();
            t.Join();
            Check(error is InvalidOperationException, "Resume from another thread throws: " + error);
            Check(co.IsYielded && co[0] == 1, "and leaves the coroutine as it was");
            co.Resume();
            Equal(2L, co[0].AsLong(), "the owner thread resumes it");
        }
        using (var lua = OpenCoroutines(new LuaOptions { CheckThread = false }))
        using (LuaCoroutine co = lua.Start("gen", 3))
        {
            var t = new Thread(() => co.Resume());
            t.Start();
            t.Join();
            Equal(2L, co[0].AsLong(), "CheckThread = false: another thread, one at a time");
        }
    }

    // ---- drivers ----

    public static void TestCoroutineEnumeratorDriver()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        {
            var made = new List<LuaTableRef>();
            LuaRequestHandler handle = (co, reply) => Serve(lua, co, reply, made);
            using (LuaCoroutine co = lua.Start("quest", 3))
            {
                IEnumerator run = co.Run(handle);
                var seen = new List<object>();
                while (run.MoveNext())  // a test loop instead of Unity's StartCoroutine
                {
                    seen.Add(run.Current);
                }
                Equal("wait 1.5", seen.Count == 1 ? seen[0] as string : string.Join(",", seen), "the handler's waits are yielded on");
                Equal(LuaCoroutineStatus.Finished, co.Status, "the driver ran it to the end");
                Equal(300L, co[0].AsLong(), "the result");
            }
            foreach (LuaTableRef t in made) t.Dispose();
            Equal(0, lua.LiveHandles, "nothing left");
            using (LuaCoroutine co = lua.Start("failafter", 2))
            {
                IEnumerator run = co.Run((c, reply) => null);
                Throws<LuaRuntimeException>(() => { while (run.MoveNext()) { } }, "an error of the script throws from MoveNext");
                Equal(LuaCoroutineStatus.Faulted, co.Status, "faulted");
            }
            using (LuaCoroutine co = lua.Start("gen", 1))
            {
                Throws<ArgumentNullException>(() => co.Run(null), "a null handler");
                co.Resume();
                IEnumerator run = co.Run((c, reply) => null);
                Check(!run.MoveNext(), "a finished coroutine: nothing to do");
            }
        }
    }

    static IEnumerator Serve(LuaScript lua, LuaCoroutine co, LuaReply reply, List<LuaTableRef> made)
    {
        LuaTableRef request = co[0].AsTable();
        string op = request.GetString("op");
        if (op == "wait")
        {
            yield return "wait " + request.GetDouble("seconds").ToString(System.Globalization.CultureInfo.InvariantCulture);
            reply.Set(true);
        }
        else if (op == "fetch")
        {
            LuaTableRef data = lua.NewTable();
            data.Set("gold", 100);
            made.Add(data);
            reply.Set(data);
        }
    }

    // A single-thread SynchronizationContext, as Unity's main thread has one.
    sealed class TestContext : SynchronizationContext
    {
        readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> queue = new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();

        public override void Post(SendOrPostCallback d, object state)
        {
            queue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
        }

        // Runs the posted work on this thread until the task is done, at most ten seconds.
        public bool RunUntil(Task task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 10000)
            {
                KeyValuePair<SendOrPostCallback, object> work;
                if (queue.TryTake(out work, 10))
                {
                    work.Key(work.Value);
                }
            }
            return task.IsCompleted;
        }
    }

    static Task RunOnContext(Func<Task> start, out bool completed)
    {
        SynchronizationContext previous = SynchronizationContext.Current;
        var context = new TestContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            Task task = start();
            completed = context.RunUntil(task);
            return task;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public static void TestCoroutineTaskDriver()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        {
            int owner = Environment.CurrentManagedThreadId;
            var handlerThreads = new HashSet<int>();
            LuaAsyncRequestHandler doubler = async (co, reply, ct) =>
            {
                long request = co[0].AsLong();
                await Task.Delay(2, ct).ConfigureAwait(false);  // off the context: the driver still resumes on it
                lock (handlerThreads) handlerThreads.Add(Environment.CurrentManagedThreadId);
                reply.Set(request * 2);
            };
            LuaCoroutine sumCo = lua.Start("sum", 5);
            bool done;
            Task task = RunOnContext(() => sumCo.RunAsync(doubler), out done);
            Check(done, "the task finished");
            Equal(TaskStatus.RanToCompletion, task.Status, "ran to completion: " + task.Exception);
            Equal(30L, sumCo[0].AsLong(), "every resume ran on the owner thread with the reply (CheckThread is on)");
            Check(handlerThreads.Count > 0 && !handlerThreads.Contains(owner), "the handler continued on other threads (ConfigureAwait(false))");
            sumCo.Dispose();

            // A handler that completes synchronously.
            using (LuaCoroutine co = lua.Start("sum", 100))
            {
                Task t = co.RunAsync((c, reply, ct) => { reply.Set(1); return default(ValueTask); });
                Check(t.IsCompleted && t.Status == TaskStatus.RanToCompletion, "synchronous handlers: the task is complete when RunAsync returns");
                Equal(100L, co[0].AsLong(), "the result");
            }

            // Cancellation closes the coroutine and cancels the task.
            lua.Load("closed = 0", "reset");
            using (var cts = new CancellationTokenSource())
            using (LuaCoroutine co = lua.Start("closer"))
            {
                LuaAsyncRequestHandler forever = async (c, reply, ct) => { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); };
                cts.CancelAfter(30);
                Task t = RunOnContext(() => co.RunAsync(forever, cts.Token), out done);
                Check(done, "cancelled in time");
                Equal(TaskStatus.Canceled, t.Status, "the task is cancelled");
                Equal(LuaCoroutineStatus.Closed, co.Status, "the coroutine is closed");
                Equal(1L, lua.Globals.GetLong("closed"), "its __close ran");
            }

            // An error of the script faults the task.
            using (LuaCoroutine co = lua.Start("failafter", 2))
            {
                Task t = RunOnContext(() => co.RunAsync(async (c, reply, ct) => { await Task.Yield(); }), out done);
                Check(done && t.IsFaulted && t.Exception.InnerException is LuaRuntimeException, "faulted with the Lua error: " + t.Status);
                Equal(LuaCoroutineStatus.Faulted, co.Status, "the coroutine faulted");
            }
            using (LuaCoroutine co = lua.Start("gen", 1))
            {
                Throws<ArgumentNullException>(() => co.RunAsync(null), "a null handler");
            }
        }
        // Without a context (plain .NET): resumes on the thread that completes the handler.
        using (var lua = OpenCoroutines(new LuaOptions { CheckThread = false }))
        using (LuaCoroutine co = lua.Start("sum", 3))
        {
            Task t = co.RunAsync(async (c, reply, ct) => { long v = c[0].AsLong(); await Task.Delay(1).ConfigureAwait(false); reply.Set(v); });
            Check(t.Wait(10000), "done without a context");
            Equal(6L, co[0].AsLong(), "the sum");
        }
    }

    // ---- allocations and cost ----

    public static void TestCoroutineResumesAllocateNothing()
    {
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            Console.WriteLine("  GC.GetAllocatedBytesForCurrentThread does not count on this runtime; skipped");
            return;
        }
        using (var lua = OpenCoroutines(new LuaOptions { MemoryLimit = 16 << 20, InstructionLimit = 1000000, TimeLimit = TimeSpan.FromSeconds(5) }))
        {
            lua.Load("function pump() local n, x = 0, 0 while true do local a, b, c = coroutine.yield(n, x, n % 2 == 0) n = n + 1 x = (a or 0) + (b or 0) end end", "pump");
            using (LuaCoroutine co = lua.Start("pump"))
            {
                var args = new LuaValue[] { 1, 2.5, true };
                for (int round = 0; round < 2; round++)
                {
                    long before = Allocated();
                    double sum = 0;
                    for (int i = 0; i < 10000; i++)
                    {
                        co.Resume(i, 2.5);
                        sum += co[0].AsLong() + co[1].AsDouble() + (co[2].AsBool() ? 1 : 0);
                        co.Resume(args);
                        co.Resume();
                    }
                    long allocated = Allocated() - before;
                    if (round == 1)
                    {
                        Console.WriteLine("  allocation probe: " + allocated + " bytes for 30,000 resumes");
                        Equal(0L, allocated, "bytes allocated by 30,000 resumes with primitive values");
                    }
                    Check(sum > 0, "the resumes ran");
                }
            }
            // What a Start and the drivers allocate (reported, not required to be 0).
            long b0 = Allocated();
            for (int i = 0; i < 100; i++)
            {
                using (LuaCoroutine co = lua.Start("gen", 2))
                {
                }
            }
            long start = (Allocated() - b0) / 100;
            LuaRequestHandler none = (c, reply) => { reply.Set(1); return null; };
            b0 = Allocated();
            for (int i = 0; i < 100; i++)
            {
                using (LuaCoroutine co = lua.Start("sum", 10))
                {
                    IEnumerator run = co.Run(none);
                    while (run.MoveNext()) { }
                }
            }
            long enumerator = (Allocated() - b0) / 100 - start;
            LuaAsyncRequestHandler sync = (c, reply, ct) => { reply.Set(1); return default(ValueTask); };
            b0 = Allocated();
            for (int i = 0; i < 100; i++)
            {
                using (LuaCoroutine co = lua.Start("sum", 10))
                {
                    co.RunAsync(sync).Wait();
                }
            }
            long task = (Allocated() - b0) / 100 - start;
            Console.WriteLine("  bytes allocated: Start and Dispose " + start + "; per run of 10 yields: Run " + enumerator
                + ", RunAsync with synchronous handlers " + task);
            Check(start < 1024 && enumerator < 1024 && task < 2048, "small fixed allocations");
        }
        GC.KeepAlive(keep);
    }

    public static void TestCoroutineCost()
    {
        using (var lua = OpenCoroutines(new LuaOptions()))
        {
            lua.Load("function pump() local n = 0 while true do n = n + (coroutine.yield(n) or 0) end end"
                + " function luaside(k) local co = coroutine.wrap(pump) co() for i = 1, k do co(1) end end", "cost");
            const int N = 200000;
            double best = double.MaxValue, bestLua = double.MaxValue;
            using (LuaCoroutine co = lua.Start("pump"))
            {
                for (int run = 0; run < 7; run++)
                {
                    var w = Stopwatch.StartNew();
                    for (int i = 0; i < N; i++)
                    {
                        co.Resume(1);
                    }
                    best = Math.Min(best, w.Elapsed.TotalMilliseconds * 1e6 / N);
                    w.Restart();
                    lua.Call("luaside", N);
                    bestLua = Math.Min(bestLua, w.Elapsed.TotalMilliseconds * 1e6 / N);
                }
            }
            Console.WriteLine("  cost per resume and yield: C# Resume " + best.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ns, Lua coroutine.wrap "
                + bestLua.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ns");
            Check(best > 0, "measured");
        }
    }
}
