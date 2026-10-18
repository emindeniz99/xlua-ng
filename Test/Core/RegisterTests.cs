/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The host function tests of LuaScript (docs/design/core-luascript.md,
// section 7.2 "Register"; milestone 7): LuaCall's getters and results, errors
// and exceptions, nested calls, Unregister, dotted names, the typed
// shorthands, Yield from C#, the limits and the memory rule around host
// functions, the thread check and the allocation probe. Part of CoreTests
// (Test/Native/run_core.py).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using XLua;

public static partial class CoreTests
{
    // ---- LuaCall: arguments ----

    public static void TestRegisterArguments()
    {
        using (var lua = Open())
        {
            var seen = new List<string>();
            lua.Register("probe", (ref LuaCall c) =>
            {
                var parts = new StringBuilder();
                parts.Append(c.ArgCount);
                for (int i = 0; i <= c.ArgCount; i++)  // one past the end: Nil
                {
                    parts.Append(' ').Append(c.ArgType(i));
                }
                seen.Add(parts.ToString());
                Check(c.Script == lua, "LuaCall.Script");
                Check(!c.CanYield, "not in a coroutine");
            });
            lua.Load("probe(nil, true, 3, 2.5, 'x', {}, probe, coroutine.create(print or tostring))", "args");
            Equal("8 Nil Boolean Integer Number String Table Function Other Nil", seen.Count > 0 ? seen[0] : null, "the types");
            lua.Load("probe()", "none");
            Equal("0 Nil", seen.Count > 1 ? seen[1] : null, "no arguments");

            lua.Register("values", (ref LuaCall c) =>
            {
                Check(!c.GetBool(0) && c.GetBool(1) && !c.GetBool(2) && c.GetBool(3) && !c.GetBool(99), "GetBool is Lua's truth");
                Equal(-5L, c.GetLong(3), "GetLong of an integer");
                Equal(4L, c.GetLong(4), "GetLong of an integral float");
                Equal(long.MinValue, c.GetLong(5), "64-bit integers stay exact");
                Equal(-5.0, c.GetDouble(3), "GetDouble of an integer");
                Equal(4.0, c.GetDouble(4), "GetDouble of a float");
                Equal("héllo\0!", c.GetString(6), "GetString keeps UTF-8 and NULs");
                Equal(8, c.GetUtf8(6).Length, "GetUtf8 is the bytes");
                Check(c.ArgEquals(6, "héllo\0!") && !c.ArgEquals(6, "héllo") && !c.ArgEquals(3, "-5"), "ArgEquals, also not ASCII");
                Check(c.ArgEquals(7, "gold") && !c.ArgEquals(7, "golD") && !c.ArgEquals(7, "gol") && !c.ArgEquals(7, "golds"), "ArgEquals in ASCII");
                Check(c.Get(3) == -5 && c.Get(6) == "héllo\0!" && c.Get(99).IsNil && c.Get(0).IsNil, "Get");
                Equal(7L, c.OptLong(0, 7), "OptLong of nil");
                Equal(7L, c.OptLong(99, 7), "OptLong of none");
                Equal(-5L, c.OptLong(3, 7), "OptLong of a value");
                Equal(1.5, c.OptDouble(0, 1.5), "OptDouble");
                Equal("fallback", c.OptString(99, "fallback"), "OptString");
                Equal("gold", c.OptString(7, "fallback"), "OptString of a value");
                try { c.GetLong(-1); Check(false, "a negative index throws"); }
                catch (ArgumentOutOfRangeException) { }
                c.Return(true);
            });
            Equal(true, lua.Load<bool>("return values(nil, true, false, -5, 4.0, math.mininteger, 'h\\u{e9}llo\\0!', 'gold')", "values"),
                "the getters ran");
        }
    }

    public static void TestRegisterArgumentErrors()
    {
        using (var lua = Open())
        {
            lua.Register("need", (ref LuaCall c) => { c.Return(c.GetLong(0)); });
            lua.Register("needs", (ref LuaCall c) => { c.Return(c.GetString(1)); });
            lua.Register("needd", (ref LuaCall c) => { c.Return(c.GetDouble(0)); });
            lua.Register("needu", (ref LuaCall c) => { c.Return(c.GetUtf8(0).Length); });
            var e = Throws<LuaRuntimeException>(() => lua.Load("need('x')", "check"), "a string for GetLong");
            if (e != null)
            {
                Equal("check:1: bad argument #1 to 'need' (number expected, got string)", e.Message, "Lua's message");
                Check(e.InnerException == null, "an argument error has no inner exception");
                Check(e.LuaStackTrace != null, "a traceback");
            }
            e = Throws<LuaRuntimeException>(() => lua.Load("need(1.5)", "check"), "a fraction");
            if (e != null) Equal("check:1: bad argument #1 to 'need' (number has no integer representation)", e.Message, "the integer message");
            e = Throws<LuaRuntimeException>(() => lua.Load("need()", "check"), "no argument");
            if (e != null) Equal("check:1: bad argument #1 to 'need' (number expected, got no value)", e.Message, "no value");
            e = Throws<LuaRuntimeException>(() => lua.Load("needs('a', 5)", "check"), "a number for GetString");
            if (e != null) Equal("check:1: bad argument #2 to 'needs' (string expected, got number)", e.Message, "numbers are not strings");
            e = Throws<LuaRuntimeException>(() => lua.Load("needd({})", "check"), "a table for GetDouble");
            if (e != null) Equal("check:1: bad argument #1 to 'needd' (number expected, got table)", e.Message, "a table");
            e = Throws<LuaRuntimeException>(() => lua.Load("needu(true)", "check"), "a boolean for GetUtf8");
            if (e != null) Equal("check:1: bad argument #1 to 'needu' (string expected, got boolean)", e.Message, "a boolean");
            // 9 arguments: the trampoline's argument array lives in a userdata; the missing one is still "no value".
            lua.Register("tenth", (ref LuaCall c) => { c.Return(c.GetLong(9)); });
            e = Throws<LuaRuntimeException>(() => lua.Load("tenth(1, 2, 3, 4, 5, 6, 7, 8, 9)", "check"), "argument 10 of 9");
            if (e != null) Equal("check:1: bad argument #10 to 'tenth' (number expected, got no value)", e.Message, "no value after 9 arguments");
            Equal(10L, lua.Load<long>("return tenth(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)", "ten"), "ten arguments");
            Equal("bad argument #1 to 'need' (number expected, got string)",
                lua.Load<string>("local ok, e = pcall(need, 'x') return e", "pcall"), "pcall catches it");
            Equal("t:1: bad argument #1 to 'get' (number expected, got string)",
                lua.Load<string>("local t = {get = need} local ok, e = pcall(function() return t.get('x') end) return e", "t"), "a field name");
        }
    }

    // ---- LuaCall: results ----

    public static void TestRegisterReturns()
    {
        using (var lua = Open())
        {
            lua.Register("each", (ref LuaCall c) =>
            {
                c.Return(true);
                c.Return(42L);
                c.Return(2.5);
                c.Return("héllo");
                c.Return(new byte[] { 0, 255, 65 });
                c.Return((LuaValue)7);
                c.Return(LuaValue.FromBytes(new byte[] { 66, 0 }));
                c.Return((string)null);
                c.ReturnNil();
                c.Return((LuaValue)"v");
                c.Return(LuaValue.Nil);
                c.Return((LuaValue)0.5);
                c.Return((LuaValue)false);
            });
            Equal("13|true|42|integer|2.5|float|héllo|0,255,65|7|integer|66,0|nil|nil|v|nil|0.5|false",
                lua.Load<string>(@"
local r = table.pack(each())
local function bytes(s) local t = {} for i = 1, #s do t[i] = s:byte(i) end return table.concat(t, ',') end
return table.concat({r.n, tostring(r[1]), r[2], math.type(r[2]), r[3], math.type(r[3]), r[4], bytes(r[5]), r[6], math.type(r[6]),
  bytes(r[7]), tostring(r[8]), tostring(r[9]), r[10], tostring(r[11]), r[12], tostring(r[13])}, '|')", "each"), "every result type");
            lua.Register("many", (ref LuaCall c) =>
            {
                long n = c.GetLong(0);
                for (long i = 1; i <= n; i++)
                {
                    if (i % 2 == 0) c.Return("s" + i); else c.Return(i);
                }
            });
            Equal(20L, lua.Load<long>("return select('#', many(20))", "twenty"), "20 results (stack growth after the return)");
            Equal("s20", lua.Load<string>("return (select(20, many(20)))", "twenty"), "the 20th result");
            Equal(3000L, lua.Load<long>("return select('#', many(3000))", "3000"), "3,000 results");
            Equal(0L, lua.Load<long>("return select('#', many(0))", "zero"), "no results");
            lua.Register("tbl", (ref LuaCall c) => { c.Return(LuaValue.OfType(LuaType.Table)); });
            var e = Throws<LuaRuntimeException>(() => lua.Load("tbl()", "tbl"), "a table without a handle");
            if (e != null) Check(e.InnerException is NotSupportedException, "NotSupportedException inside");
        }
    }

    // Results of nested host functions: the arena and the overflow stack are per frame.
    public static void TestRegisterNestedResults()
    {
        using (var lua = Open())
        {
            string big = new string('b', 100000);
            lua.Register("inner", (ref LuaCall c) =>
            {
                for (int i = 0; i < 12; i++) c.Return("inner" + i);
                c.Return(big);  // grows the arena while the outer frame has results in it
            });
            lua.Register("outer", (ref LuaCall c) =>
            {
                for (int i = 0; i < 5; i++) c.Return("outer" + i);
                long n = c.Script.Load<long>("return select('#', inner())", "nested");
                c.Return(n);
                for (int i = 5; i < 10; i++) c.Return("outer" + i);
                c.Return(c.Script.Load<string>("return select(13, inner())", "nested2").Length);
            });
            Equal("outer0,outer1,outer2,outer3,outer4,13,outer5,outer6,outer7,outer8,outer9,100000",
                lua.Load<string>("return table.concat({outer()}, ',')", "outer"), "outer results survive the nested frames");
        }
    }

    // ---- errors and exceptions ----

    public static void TestRegisterExceptions()
    {
        using (var lua = Open())
        {
            var boom = new InvalidOperationException("boom");
            lua.Register("fail", (ref LuaCall c) => { throw boom; });
            lua.Register("luafail", (ref LuaCall c) => { throw new LuaException("quota exceeded"); });
            var e = Throws<LuaRuntimeException>(() => lua.Load("\nfail()", "exc"), "an exception reaches C#");
            if (e != null)
            {
                Equal("exc:2: InvalidOperationException: boom", e.Message, "the position and the exception");
                Check(ReferenceEquals(e.InnerException, boom), "the original exception is the inner exception");
                Contains(e.LuaStackTrace, "exc:2", "the traceback");
            }
            e = Throws<LuaRuntimeException>(() => lua.Call("fail"), "a host function called directly");
            if (e != null) Check(ReferenceEquals(e.InnerException, boom), "inner exception of a direct call");
            // As luaL_error: the position is the caller's, and pcall (a C function) has none.
            Equal("InvalidOperationException: boom", lua.Load<string>("local ok, e = pcall(fail) return e", "exc"),
                "pcall catches it as a Lua error");
            Equal("exc:1: InvalidOperationException: boom", lua.Load<string>("local ok, e = pcall(function() fail() end) return e", "exc"),
                "the position of a Lua caller");
            e = Throws<LuaRuntimeException>(() => lua.Load("luafail()", "exc"), "a LuaException");
            if (e != null) Equal("exc:1: quota exceeded", e.Message, "a LuaException's message is the error");
            e = Throws<LuaRuntimeException>(() => lua.Load("local ok, e = pcall(fail) error('other: ' .. e, 0)", "exc"), "another error");
            if (e != null)
            {
                Equal("other: InvalidOperationException: boom", e.Message, "the script's own error");
                Check(e.InnerException == null, "no inner exception for an error the script raised itself");
            }
            e = Throws<LuaRuntimeException>(() => lua.Load("local ok, e = pcall(fail) error(e, 0)", "exc"), "rethrown as it is");
            if (e != null) Check(ReferenceEquals(e.InnerException, boom), "an error rethrown unchanged is still the host function's");
            Equal(3L, lua.Load<long>("return 1 + 2", "after"), "usable afterwards");

            // A nested call's error leaves the host function unchanged: no second position.
            lua.Load("function bad() error('deep') end", "deep");
            lua.Register("nest", (ref LuaCall c) => { c.Script.Call("bad"); });
            e = Throws<LuaRuntimeException>(() => lua.Load("nest()", "outer"), "a nested error");
            if (e != null)
            {
                Equal("deep:1: deep", e.Message, "the nested error's message");
                Check(e.InnerException is LuaRuntimeException && e.InnerException.Message == "deep:1: deep", "the nested exception inside");
            }
            lua.Register("nestfail", (ref LuaCall c) => { c.Script.Load("fail()", "inner"); });
            e = Throws<LuaRuntimeException>(() => lua.Load("nestfail()", "outer"), "two levels");
            if (e != null)
            {
                Equal("inner:1: InvalidOperationException: boom", e.Message, "the innermost message");
                Check(e.InnerException != null && ReferenceEquals(e.InnerException.InnerException, boom), "the chain of inner exceptions");
            }
        }
    }

    // ---- nested calls ----

    public static void TestRegisterNestedCalls()
    {
        using (var lua = WithFunctions(new LuaOptions { InstructionLimit = 1000000 }))
        {
            lua.Register("twice", (ref LuaCall c) => { c.Return(c.Script.Call<long>("add", c.GetLong(0), c.GetLong(0))); });
            Equal(42L, lua.Load<long>("return twice(21)", "nested"), "a nested Call");
            // In a coroutine: the nested call runs on the coroutine's thread, under the same limit.
            Equal(42L, lua.Load<long>("return coroutine.wrap(function() return twice(21) end)()", "co"), "a nested call in a coroutine");
            lua.Register("loop", (ref LuaCall c) => { c.Script.Call("forever"); });
            Throws<LuaInstructionLimitException>(() => lua.Load("coroutine.wrap(function() loop() end)()", "co"),
                "the limit stops a nested call made from a coroutine");
            Throws<LuaInstructionLimitException>(() => lua.Load("local co = coroutine.create(function() loop() end) coroutine.resume(co) while true do end", "co"),
                "the stop stays armed after the coroutine");
            Equal(3L, lua.Call<long>("add", 1, 2), "usable afterwards");
            // A nested call that yields: Lua's error, not a crash.
            lua.Load("function yields() coroutine.yield(1) end", "y");
            lua.Register("callyield", (ref LuaCall c) => { c.Script.Call("yields"); });
            var e = Throws<LuaRuntimeException>(() => lua.Load("coroutine.wrap(function() callyield() end)()", "co"), "a yield across C#");
            if (e != null) Contains(e.Message, "attempt to yield", "Lua's message");
        }
    }

    // ---- Unregister, replacing, ids ----

    public static void TestRegisterUnregister()
    {
        using (var lua = Open())
        {
            lua.Register("a", (ref LuaCall c) => c.Return("a"));
            lua.Load("keep = a", "keep");
            Equal("a", lua.Load<string>("return a()", "a"), "registered");
            lua.Unregister("a");
            Equal(true, lua.Load<bool>("return a == nil", "gone"), "unregistered");
            lua.Register("b", (ref LuaCall c) => c.Return("b"));  // may reuse a's id
            var e = Throws<LuaRuntimeException>(() => lua.Load("keep()", "kept"), "a kept function after Unregister");
            if (e != null) Contains(e.Message, "unregistered", "the message");
            Equal("b", lua.Load<string>("return b()", "b"), "b is b");
            lua.Register("b", (ref LuaCall c) => c.Return("b2"));
            Equal("b2", lua.Load<string>("return b()", "b2"), "registered again: replaced");
            lua.Unregister("never");  // nothing happens
            lua.Unregister("lib.never");
            Throws<ArgumentException>(() => lua.Register("", (ref LuaCall c) => { }), "an empty name");
            Throws<ArgumentException>(() => lua.Register("a.b.c", (ref LuaCall c) => { }), "two dots");
            Throws<ArgumentException>(() => lua.Register(".a", (ref LuaCall c) => { }), "a leading dot");
            Throws<ArgumentException>(() => lua.Register("a.", (ref LuaCall c) => { }), "a trailing dot");
            Throws<ArgumentNullException>(() => lua.Register(null, (ref LuaCall c) => { }), "null");
            Throws<ArgumentNullException>(() => lua.Register("x", (LuaCallback)null), "a null callback");
            for (int i = 0; i < 1000; i++)  // ids are reused
            {
                lua.Register("tmp", (ref LuaCall c) => c.Return(1L));
            }
            Equal(1L, lua.Load<long>("return tmp()", "tmp"), "the last of 1,000 registrations");
            // A script's own value under the name is replaced by Register.
            lua.Register("tostring", (ref LuaCall c) => c.Return("mine"));
            Equal("mine", lua.Load<string>("return tostring(1)", "base"), "a base function replaced");
        }
    }

    // ---- dotted names ----

    public static void TestRegisterDottedNames()
    {
        using (var lua = Open())
        {
            var modules = new Dictionary<string, string>
            {
                // A module's environment exists before game.later is registered.
                { "m", "local M = {} function M.call() return game.later() end function M.log(x) return game.log(x) end return M" },
            };
            lua.ModuleResolver = (string name, out string code, out string chunkName) =>
            {
                chunkName = null;
                return modules.TryGetValue(name, out code);
            };
            lua.Register("game.log", (ref LuaCall c) => c.Return("log:" + c.GetString(0)));
            Equal("log:x", lua.Load<string>("return game.log('x')", "g"), "a library function");
            lua.Load("m = require('m')", "req");
            lua.Register("game.later", (ref LuaCall c) => c.Return("later"));
            Equal("later", lua.Load<string>("return m.call()", "later"), "an existing environment gets a later registration");
            Equal("later", lua.Load<string>("return game.later()", "later2"), "and the default environment");
            lua.Load("game.log = nil", "own");
            Equal("log:y", lua.Load<string>("return m.log('y')", "copy"), "each environment has its own copy");
            lua.Register("game.log", (ref LuaCall c) => c.Return("log2:" + c.GetString(0)));
            Equal("log2:w", lua.Load<string>("return game.log('w')", "own"), "a registration fills a key the script removed");
            Equal("log2:z", lua.Load<string>("return m.log('z')", "copy2"), "and replaces the old function in the other copy");
            lua.Load("game.later = function() return 'script' end", "own2");
            lua.Register("game.later", (ref LuaCall c) => c.Return("later2"));
            Equal("script", lua.Load<string>("return game.later()", "own2"), "a script's own value is kept");
            Equal("later2", lua.Load<string>("return m.call()", "later3"), "the copy that still had the host function is updated");
            lua.Unregister("game.later");
            Equal(true, lua.Load<bool>("return m.call == nil or not pcall(m.call)", "un"), "unregistered from the copies");
            Equal("script", lua.Load<string>("return game.later()", "own3"), "the script's own value stays");
            // Into a standard library: the copies and the string methods.
            lua.Register("string.shout", (ref LuaCall c) => c.Return(c.GetString(0).ToUpperInvariant() + "!"));
            Equal("HI!|HO!", lua.Load<string>("return string.shout('hi') .. '|' .. ('ho'):shout()", "str"), "a string method");
            var e = Throws<LuaRuntimeException>(() => lua.Load("game.log(1)", "name"), "an argument error in a library function");
            if (e != null) Contains(e.Message, "bad argument #1 to 'log'", "the function's name");
        }
    }

    // ---- typed shorthands ----

    public static void TestRegisterTypedShorthands()
    {
        using (var lua = Open())
        {
            string said = null;
            lua.Register<long, long, long>("add2", (a, b) => a + b);
            lua.Register<int, int>("inc", x => x + 1);
            lua.Register<double, bool, double>("scale", (x, neg) => neg ? -x : x * 2);
            lua.Register<float, float>("halff", x => x / 2);
            lua.Register<string, string>("upper", s => s.ToUpperInvariant());
            lua.Register<byte[], byte[]>("rev", b => { if (b == null) return null; Array.Reverse(b); return b; });
            lua.Register<LuaValue, LuaValue>("same", v => v);
            lua.Register<string>("say", s => said = s);
            lua.Register("noop", () => { said = "noop"; });
            lua.Register<long>("seven", () => 7);
            lua.Register<long, long, long, long, long>("sum4", (a, b, c, d) => a + b + c + d);
            lua.Register<long, long, long>("sum3", (a, b, c) => { said = (a + b + c).ToString(); });
            Equal(5L, lua.Load<long>("return add2(2, 3)", "t"), "Func<long, long, long>");
            Equal(42L, lua.Load<long>("return inc(41)", "t"), "Func<int, int>");
            Equal(-1.5, lua.Load<double>("return scale(1.5, true)", "t"), "Func<double, bool, double>");
            Equal(3.0, lua.Load<double>("return scale(1.5)", "t"), "a missing bool is false");
            Equal(0.25, lua.Load<double>("return halff(0.5)", "t"), "Func<float, float>");
            Equal("ABC", lua.Load<string>("return upper('abc')", "t"), "Func<string, string>");
            Equal("cba", lua.Load<string>("return rev('abc')", "t"), "Func<byte[], byte[]>");
            Equal(true, lua.Load<bool>("return rev(nil) == nil", "t"), "a nil byte[]");
            Equal("x", lua.Load<string>("return same('x')", "t"), "Func<LuaValue, LuaValue>");
            lua.Load("say('hi')", "t");
            Equal("hi", said, "Action<string>");
            lua.Load("noop()", "t");
            Equal("noop", said, "Action");
            Equal(7L, lua.Load<long>("return seven()", "t"), "Func<long>");
            Equal(10L, lua.Load<long>("return sum4(1, 2, 3, 4)", "t"), "four arguments");
            lua.Load("sum3(1, 2, 3)", "t");
            Equal("6", said, "Action with three arguments");
            var e = Throws<LuaRuntimeException>(() => lua.Load("add2(1, 'x')", "t"), "a wrong type");
            if (e != null) Equal("t:1: bad argument #2 to 'add2' (number expected, got string)", e.Message, "the argument error");
            e = Throws<LuaRuntimeException>(() => lua.Load("inc(1 << 40)", "t"), "out of the range of int");
            if (e != null) Equal("t:1: bad argument #1 to 'inc' (integer out of the range of int)", e.Message, "the range error");
            Throws<NotSupportedException>(() => lua.Register<DateTime, long>("bad", d => 0), "an unsupported argument type");
            Throws<NotSupportedException>(() => lua.Register<long, DateTime>("bad", d => DateTime.Now), "an unsupported result type");
            Throws<ArgumentNullException>(() => lua.Register<long, long>("bad", null), "a null function");
        }
    }

    // ---- Yield from C# ----

    public static void TestRegisterYield()
    {
        using (var lua = Open())
        {
            lua.Register("fetch", (ref LuaCall c) =>
            {
                c.Return("request:" + c.GetString(0));
                c.Yield();
            });
            lua.Register("canyield", (ref LuaCall c) => c.Return(c.CanYield));
            Equal("false,true", lua.Load<string>("return tostring(canyield()) .. ',' .. tostring(coroutine.wrap(canyield)())", "can"),
                "CanYield in a coroutine only");
            lua.Load(@"
co = coroutine.create(function(base) local reply = fetch('rewards') return reply * base end)
function step(...) return coroutine.resume(co, ...) end", "co");
            var r = new LuaValue[2];
            Equal(2, lua.Call("step", new LuaValue[] { 10 }, r), "the yield");
            Check(r[0] == true && r[1] == "request:rewards", "the yielded request: " + r[1]);
            Equal(2, lua.Call("step", new LuaValue[] { 4 }, r), "the resume");
            Check(r[0] == true && r[1] == 40, "the reply became fetch's result: " + r[1]);
            Equal("dead", lua.Load<string>("return coroutine.status(co)", "st"), "finished");
            Equal(6L, lua.Load<long>("local f = coroutine.wrap(function() return fetch('a') end) local req = f() return req == 'request:a' and f(6)", "wrap"),
                "coroutine.wrap");
            var e = Throws<LuaRuntimeException>(() => lua.Load("fetch('x')", "main"), "Yield outside a coroutine");
            if (e != null)
            {
                Check(e.InnerException is InvalidOperationException, "InvalidOperationException inside: " + e.InnerException);
                Contains(e.Message, "LuaCall.Yield", "the message");
            }
            // Under an instruction limit, the yields cross the budget accounting.
            lua.InstructionLimit = 100000;
            lua.Register("tick", (ref LuaCall c) => { c.Yield(); });
            Throws<LuaInstructionLimitException>(() => lua.Load("local f = coroutine.wrap(function() while true do tick() end end) while true do f() end", "limit"),
                "a loop of yields from C# hits the global budget");
            Equal(3L, lua.Load<long>("local f = coroutine.wrap(function() tick() return 3 end) f() return f()", "after"), "usable afterwards");
        }
    }

    // ---- limits around host functions ----

    public static void TestRegisterLimits()
    {
        using (var lua = WithFunctions(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(30) }))
        {
            lua.Register("sleep", (ref LuaCall c) => { Thread.Sleep((int)c.GetLong(0)); c.Return(1L); });
            var watch = Stopwatch.StartNew();
            Throws<LuaTimeLimitException>(() => lua.Call("sleep", 300), "a host function called directly past the time limit");
            Check(watch.ElapsedMilliseconds >= 290, "the host function was not preempted");
            Throws<LuaTimeLimitException>(() => lua.Load("sleep(300) return 1", "after"), "the script stops right after the host function");
            Throws<LuaTimeLimitException>(() => lua.Load("local ok = pcall(sleep, 300) return 1", "pcall"), "pcall does not catch it");
            Equal(1L, lua.Load<long>("return sleep(0)", "quick"), "a quick host function");
            // The nested call's limit error, caught by the host function: the call still stops.
            lua.TimeLimit = TimeSpan.Zero;
            lua.InstructionLimit = 100000;
            bool caught = false;
            lua.Register("swallow", (ref LuaCall c) =>
            {
                try { c.Script.Call("forever"); }
                catch (LuaInstructionLimitException) { caught = true; }
                c.Return(5L);
            });
            Throws<LuaInstructionLimitException>(() => lua.Load("return swallow()", "swallow"), "a limit is terminal for the call");
            Check(caught, "the host function saw the limit");
            Throws<LuaInstructionLimitException>(() => lua.Call("swallow"), "also for a host function called directly");
            // Interrupt from inside a host function.
            lua.InstructionLimit = long.MaxValue / 4;  // Interrupt stops calls with a limit (milestone 3)
            lua.Register("stopme", (ref LuaCall c) => { c.Script.Interrupt(); });
            Throws<LuaInterruptedException>(() => lua.Load("stopme() return 1", "int"), "Interrupt inside a host function");
            // Without a watchdog (WebGL's clock check, forced here) the trampoline reads the deadline.
            using (var clock = WithClockCheck())
            {
                clock.Register("sleep", (ref LuaCall c) => { Thread.Sleep((int)c.GetLong(0)); });
                Throws<LuaTimeLimitException>(() => clock.Call("sleep", 100), "the clock check after a host function");
            }
            // The instruction count of a script with host functions is exact.
            lua.InstructionLimit = 1000000;
            lua.Register("one", (ref LuaCall c) => c.Return(1L));
            lua.Load("function ones(n) local s = 0 for i = 1, n do s = s + one() end return s end", "ones");
            Equal(1000L, lua.Call<long>("ones", 1000), "the calls");
            long count = lua.LastInstructionCount;
            lua.Call<long>("ones", 1000);
            Equal(count, lua.LastInstructionCount, "the same count");
        }
    }

    static LuaScript WithClockCheck()
    {
        LuaWatchdog.ForceClockCheck = true;
        try
        {
            return new LuaScript(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(30) });
        }
        finally
        {
            LuaWatchdog.ForceClockCheck = false;
        }
    }

    // ---- memory around host functions ----

    public static void TestRegisterMemory()
    {
        using (var lua = Open())
        {
            lua.MemoryLimit = lua.MemoryUsed + (2 << 20);
            string big = new string('m', 4 << 20);
            lua.Register("big", (ref LuaCall c) => c.Return(big));
            var e = Throws<LuaMemoryLimitException>(() => lua.Load("big()", "big"), "a result over the limit");
            if (e != null) Check(!e.HostOutOfMemory && e.MemoryUsed <= e.MemoryLimit + 16 * 1024, "within the limit: " + e.MemoryUsed);
            Equal(true, lua.Load<bool>("local ok, e = pcall(big) return not ok and e == 'not enough memory'", "pcall"), "pcall catches it");
            // A nested call that runs out of memory: the host function lets it through as Lua's memory error.
            lua.Register("grow", (ref LuaCall c) => { c.Script.Load("local t = {} for i = 1, 1e8 do t[i] = i end", "grow"); });
            Throws<LuaMemoryLimitException>(() => lua.Load("grow()", "outer"), "a nested memory error");
            Equal(true, lua.Load<bool>("local ok, e = pcall(grow) return not ok and e == 'not enough memory'", "pcall2"), "caught in the script");
            Equal(3L, lua.Load<long>("return 1 + 2", "after"), "usable afterwards");
        }
    }

    // ---- threads and lifecycle ----

    public static void TestRegisterThreadAndDispose()
    {
        using (var lua = Open())
        {
            bool refused = false;
            lua.Register("dispose", (ref LuaCall c) =>
            {
                try { c.Script.Dispose(); }
                catch (InvalidOperationException) { refused = true; }
            });
            lua.Load("dispose()", "d");
            Check(refused && !lua.IsDisposed, "Dispose inside a host function throws");
            Exception other = null;
            var t = new Thread(() =>
            {
                try { lua.Register("x", (ref LuaCall c) => { }); }
                catch (Exception e) { other = e; }
            });
            t.Start();
            t.Join();
            Check(other is InvalidOperationException, "Register from another thread: " + other);
            lua.Register("late", (ref LuaCall c) => c.Return(1L));
            lua.Dispose();
            Throws<ObjectDisposedException>(() => lua.Register("x", (ref LuaCall c) => { }), "Register after Dispose");
            Throws<ObjectDisposedException>(() => lua.Unregister("late"), "Unregister after Dispose");
        }
        // Many scripts, each with host functions: the dispatcher is process-wide, the ids per script.
        var scripts = new LuaScript[4];
        for (int i = 0; i < scripts.Length; i++)
        {
            long n = i;
            scripts[i] = Open();
            scripts[i].Register("which", (ref LuaCall c) => c.Return(n));
        }
        for (int i = 0; i < scripts.Length; i++)
        {
            Equal((long)i, scripts[i].Load<long>("return which()", "which"), "script " + i + " calls its own function");
            scripts[i].Dispose();
        }
    }

    // ---- buffers (milestone 10) ----

    public static void TestRegisterBuffers()
    {
        using (var lua = OpenBuffer())
        {
            if (lua == null)
            {
                return;  // XLUA_BUFFER=OFF: OpenBuffer checked the refusal
            }
            lua.Register("sum", (ref LuaCall c) =>
            {
                Span<byte> bytes = c.GetBufferSpan(0);
                long sum = 0;
                foreach (byte x in bytes) sum += x;
                bytes[0] = 99;  // seen by the script
                Check(c.ArgType(0) == LuaType.Buffer && c.Get(0).Type == LuaType.Buffer, "the argument's type");
                c.Return(sum);
            });
            using (LuaBufferRef kept = lua.NewBuffer(new byte[] { 1, 2, 3 }))
            {
                lua.Register("give", (ref LuaCall c) => c.Return(kept));
                lua.Register<LuaBufferRef>("giveTyped", () => kept);
                Equal("12,99", lua.Load<string>("local b = buffer.fromstring('\\5\\7') return sum(b) .. ',' .. buffer.readu8(b, 0)", "sum"),
                    "a buffer argument, read and written without a copy");
                Equal("3,1,true", lua.Load<string>("local b = give() return buffer.len(b) .. ',' .. buffer.readu8(b, 0) .. ',' .. tostring(rawequal(b, giveTyped()))", "give"),
                    "a buffer result is the buffer itself");
                Check(kept.IsValid, "the host's handle stays valid");
            }
            var e = Throws<LuaRuntimeException>(() => lua.Load("sum('x')", "sum"), "a string for GetBufferSpan");
            if (e != null) Equal("sum:1: bad argument #1 to 'sum' (buffer expected, got string)", e.Message, "the type error");
            using (var other = OpenBuffer())
            using (LuaBufferRef foreign = other.NewBuffer(2))
            {
                lua.Register("foreign", (ref LuaCall c) => c.Return(foreign));
                e = Throws<LuaRuntimeException>(() => lua.Load("foreign()", "f"), "a buffer of another script");
                if (e != null) Check(e.InnerException is ArgumentException, "ArgumentException inside: " + e.InnerException);
            }
        }
    }

    // ---- allocations and cost ----

    public static void TestCallbacksAllocateNothing()
    {
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            Console.WriteLine("  GC.GetAllocatedBytesForCurrentThread does not count on this runtime; skipped");
            return;
        }
        using (var lua = new LuaScript(new LuaOptions { MemoryLimit = 16 << 20, InstructionLimit = 100000000, TimeLimit = TimeSpan.FromSeconds(30) }))
        {
            long total = 0;
            lua.Register("prim", (ref LuaCall c) =>
            {
                if (c.GetBool(2)) c.Return(c.GetLong(0) + (long)c.GetDouble(1)); else c.ReturnNil();
            });
            lua.Register("text", (ref LuaCall c) =>
            {
                total += c.GetUtf8(0).Length + (c.ArgEquals(0, "gold") ? 1 : 0) + c.ArgCount;
                c.Return("a constant result string");  // encoded into native memory: no managed allocation
                c.Return(c.GetUtf8(0));
            });
            lua.Register<long, long, long>("typed", (a, b) => a * b);
            lua.Register<double, bool, double>("typed2", (x, f) => f ? x : -x);
            lua.Register("many", (ref LuaCall c) => { for (int i = 0; i < 12; i++) c.Return(i); });
            lua.Register("fail", (ref LuaCall c) => { c.Return(c.OptLong(0, 1)); });
            lua.Load(@"function loop(n)
  local s = 0
  for i = 1, n do
    s = s + prim(i, 2.5, true) + #text('gold') + typed(i, 2) + typed2(2.0, true) + select('#', many())
    if prim(i, 1, false) == nil then s = s + 1 end
    s = s + fail() + fail(2)
  end
  return s
end", "alloc");
            for (int round = 0; round < 2; round++)
            {
                long before = Allocated();
                long sum = lua.Call<long>("loop", 10000);
                long allocated = Allocated() - before;
                Check(sum > 0, "the calls ran");
                if (round == 1)
                {
                    Console.WriteLine("  allocation probe: " + allocated + " bytes for 80,000 host function calls");
                    Equal(0L, allocated, "bytes allocated by 80,000 host function calls with primitive arguments and results");
                }
            }
            Check(total > 0, "the text function ran");
        }
        GC.KeepAlive(keep);
    }

    // Prints the cost of a call from Lua to a host function (no assertion: the numbers go into the design notes).
    public static void TestCallbackCost()
    {
        using (var lua = new LuaScript(new LuaOptions()))
        {
            lua.Register("raw", (ref LuaCall c) => c.Return(c.GetLong(0)));
            lua.Register<long, long>("typed", x => x);
            lua.Load(@"
local function id(x) return x end
function base(n) local s = 0 for i = 1, n do s = s + id(i) end return s end
function rawloop(n) local s = 0 for i = 1, n do s = s + raw(i) end return s end
function typedloop(n) local s = 0 for i = 1, n do s = s + typed(i) end return s end", "cost");
            const int N = 1000000;
            double Best(string f)
            {
                double best = double.MaxValue;
                for (int r = 0; r < 7; r++)
                {
                    var w = Stopwatch.StartNew();
                    lua.Call<long>(f, N);
                    best = Math.Min(best, w.Elapsed.TotalMilliseconds * 1e6 / N);
                }
                return best;
            }
            double baseNs = Best("base"), rawNs = Best("rawloop"), typedNs = Best("typedloop");
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  callback cost (no limits, min of 7 x {0:N0}): Lua function {1:F1} ns, LuaCallback {2:F1} ns, Register<long, long> {3:F1} ns per call",
                N, baseNs, rawNs, typedNs));
            // A time limit alone: Lua code runs without a hook, and each host function call takes the core lock.
            lua.TimeLimit = TimeSpan.FromSeconds(60);
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  callback cost (time limit): Lua function {0:F1} ns, LuaCallback {1:F1} ns, Register<long, long> {2:F1} ns per call",
                Best("base"), Best("rawloop"), Best("typedloop")));
            lua.InstructionLimit = long.MaxValue / 4;
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  callback cost (instruction and time limits): Lua function {0:F1} ns, LuaCallback {1:F1} ns, Register<long, long> {2:F1} ns per call",
                Best("base"), Best("rawloop"), Best("typedloop")));
        }
    }
}
