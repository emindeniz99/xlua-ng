/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The host tests of LuaScript (docs/design/core-luascript.md, section 7.2;
// milestone 6: lifecycle, LuaValue, Load, Call, multiple results, exceptions,
// limits, the thread check, require, and the allocation probe). Compiled with
// Assets/XLua/Core only (no bridge) by Test/Native/run_core.py, which runs them
// on Mono and .NET against a core library or a bridge library with
// XLUA_LUASCRIPT=ON. Every test is a method named Test*; the run prints
// XLUA_CORE_TESTS_PASS with the count.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using XLua;

public static partial class CoreTests  // host functions: RegisterTests.cs; buffers: BufferTests.cs
{
    static int failures;

    public static int Main(string[] args)
    {
        var tests = new List<MethodInfo>();
        foreach (var m in typeof(CoreTests).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name.StartsWith("Test", StringComparison.Ordinal) && m.GetParameters().Length == 0)
            {
                if (args.Length == 0 || Array.IndexOf(args, m.Name) >= 0)
                {
                    tests.Add(m);
                }
            }
        }
        tests.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        Console.WriteLine("runtime: " + (Type.GetType("Mono.Runtime") != null ? "Mono" : ".NET") + " " + Environment.Version
            + ", " + Probe("return _VERSION"));
        int run = 0;
        foreach (var m in tests)
        {
            int before = failures;
            try
            {
                m.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                failures++;
                Console.WriteLine("FAIL " + m.Name + ": " + e.InnerException);
            }
            run++;
            Console.WriteLine((failures == before ? "ok   " : "FAIL ") + m.Name);
        }
        if (failures > 0)
        {
            Console.WriteLine(failures + " failures in " + run + " tests");
            return 1;
        }
        Console.WriteLine("XLUA_CORE_TESTS_PASS: " + run + " tests");
        return 0;
    }

    static string Probe(string code)
    {
        using (var lua = new LuaScript(new LuaOptions()))
        {
            return lua.Load<string>(code, "probe");
        }
    }

    static void Check(bool condition, string what)
    {
        if (!condition)
        {
            failures++;
            Console.WriteLine("  check failed: " + what);
        }
    }

    static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            failures++;
            Console.WriteLine("  " + what + ": expected <" + expected + ">, got <" + actual + ">");
        }
    }

    static TException Throws<TException>(Action action, string what) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException e)
        {
            return e;
        }
        catch (Exception e)
        {
            failures++;
            Console.WriteLine("  " + what + ": expected " + typeof(TException).Name + ", got " + e.GetType().Name + ": " + e.Message);
            return null;
        }
        failures++;
        Console.WriteLine("  " + what + ": expected " + typeof(TException).Name + ", nothing was thrown");
        return null;
    }

    static void Contains(string text, string part, string what)
    {
        if (text == null || text.IndexOf(part, StringComparison.Ordinal) < 0)
        {
            failures++;
            Console.WriteLine("  " + what + ": <" + text + "> does not contain <" + part + ">");
        }
    }

    static LuaScript Open()
    {
        return new LuaScript(new LuaOptions());
    }

    const string Functions = @"
function add(a, b) return a + b end
function echo(...) return ... end
function count(...) return select('#', ...) end
function none() end
function many() return 1, 2, 3, 4, 5 end
function fail() error('boom') end
function failt() error({code = 5}) end
function spin(n) local s = 0 for i = 1, n do s = s + i end return s end
function forever() while true do end end
function types(...) local t = {} for i = 1, select('#', ...) do t[i] = math.type(select(i, ...)) or type(select(i, ...)) end return table.concat(t, ',') end
function kinds() return {}, none, coroutine.create(none) end
quest = { onStart = function(x) return x * 2 end, deep = { f = function() return 'deep' end } }
n = 5
";

    static LuaScript WithFunctions(LuaOptions options = null)
    {
        var lua = new LuaScript(options ?? new LuaOptions());
        lua.Load(Functions, "functions");
        return lua;
    }

    // ---- lifecycle ----

    public static void TestCreateAndDispose()
    {
        var lua = Open();
        Check(!lua.IsDisposed, "a new script is open");
        lua.Dispose();
        Check(lua.IsDisposed, "disposed");
        lua.Dispose();  // a second Dispose does nothing
        Throws<ObjectDisposedException>(() => lua.Call("x"), "Call after Dispose");
        Throws<ObjectDisposedException>(() => lua.Load("return 1", "x"), "Load after Dispose");
        Throws<ObjectDisposedException>(() => { long x = lua.MemoryUsed; }, "MemoryUsed after Dispose");
        Throws<ObjectDisposedException>(() => { long x = lua.InstructionLimit; }, "InstructionLimit after Dispose");
        Throws<ObjectDisposedException>(() => lua.CollectGarbage(), "CollectGarbage after Dispose");
        lua.Interrupt();  // does nothing
    }

    public static void TestScriptDefaults()
    {
        using (var lua = new LuaScript())
        {
            Equal(64L << 20, lua.MemoryLimit, "default MemoryLimit");
            Equal(TimeSpan.FromSeconds(1), lua.TimeLimit, "default TimeLimit");
            Equal(0L, lua.InstructionLimit, "default InstructionLimit");
        }
        var o = new LuaOptions();
        Equal(0L, o.MemoryLimit, "new LuaOptions() is unlimited");
        Check(o.CheckThread && o.Libraries == LuaLibs.Default, "new LuaOptions() defaults");
        Check(!ReferenceEquals(LuaOptions.ScriptDefaults, LuaOptions.ScriptDefaults), "ScriptDefaults is a new object each time");
    }

    public static void TestOptionsAreCopied()
    {
        var options = new LuaOptions { MemoryLimit = 8 << 20 };
        using (var lua = new LuaScript(options))
        {
            options.MemoryLimit = 1;
            Equal(8L << 20, lua.MemoryLimit, "later edits do not apply");
            lua.MemoryLimit = 16 << 20;
            Equal(16L << 20, lua.MemoryLimit, "MemoryLimit can change");
            Throws<ArgumentOutOfRangeException>(() => lua.MemoryLimit = -1, "a negative MemoryLimit");
            Throws<ArgumentOutOfRangeException>(() => lua.InstructionLimit = -1, "a negative InstructionLimit");
            Throws<ArgumentOutOfRangeException>(() => lua.TimeLimit = TimeSpan.FromTicks(-1), "a negative TimeLimit");
        }
        Throws<ArgumentNullException>(() => new LuaScript(null), "null options");
    }

    public static void TestLibraries()
    {
        foreach (var lib in new[] { LuaLibs.Io, LuaLibs.Os, LuaLibs.Debug, LuaLibs.Package, LuaLibs.Bit, LuaLibs.Jit, LuaLibs.Ffi, LuaLibs.Safe | LuaLibs.Io })
        {
            Throws<ArgumentException>(() => new LuaScript(new LuaOptions { Libraries = lib }), "Libraries = " + lib);
        }
        using (var lua = new LuaScript(new LuaOptions { Libraries = LuaLibs.String | LuaLibs.Math }))
        {
            Equal(true, lua.Load<bool>("return table == nil and coroutine == nil and utf8 == nil and string.rep and math.pi and true", "libs"),
                "only the libraries asked for");
        }
        using (var lua = Open())
        {
            Equal(true, lua.Load<bool>("return io == nil and os == nil and debug == nil and package == nil and print == nil and dofile == nil", "sandbox"),
                "the sandbox has no io, os, debug, package, print or dofile");
            Equal(true, lua.Load<bool>("return string.dump == nil and getmetatable('') == false and _G == _ENV", "sandbox"),
                "no string.dump, a locked string metatable, _G is the environment");
        }
    }

    public static void TestMemoryLimitTooSmallForAState()
    {
        var e = Throws<LuaMemoryLimitException>(() => new LuaScript(new LuaOptions { MemoryLimit = 1024 }), "a 1 KB state");
        Check(e != null && e.Limit == LuaLimit.Memory && e.MemoryLimit == 1024, "the exception names the limit");
    }

    static WeakReference Leak()
    {
        var lua = Open();
        lua.Load("x = ('x'):rep(1000)", "leak");
        return new WeakReference(lua);
    }

    public static void TestFinalizerClosesALeakedScript()
    {
        WeakReference weak = Leak();
        for (int i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(!weak.IsAlive, "a script without references is collected (its finalizer closed the state)");
    }

    // ---- LuaValue ----

    public static void TestLuaValueConversions()
    {
        LuaValue v = 5;
        Equal(LuaType.Integer, v.Type, "int widens to an integer");
        Equal(5L, v.AsLong(), "AsLong");
        Equal(5.0, v.AsDouble(), "an integer AsDouble");
        v = 2.5f;
        Equal(LuaType.Number, v.Type, "float widens to a number");
        Equal(2.5, v.AsDouble(), "AsDouble");
        Throws<LuaTypeException>(() => v.AsLong(), "2.5 has no integer value");
        LuaValue whole = 3.0;
        Equal(3L, whole.AsLong(), "an integral float AsLong");
        LuaValue big = 1e19;
        long l;
        Check(!big.TryGetLong(out l), "1e19 is out of the long range");
        LuaValue u = ulong.MaxValue;
        Equal(-1L, u.AsLong(), "ulong is its two's-complement long");
        LuaValue s = "héllo";
        Equal(LuaType.String, s.Type, "a string");
        Equal("héllo", s.AsString(), "AsString");
        Equal(6, s.AsBytes().Length, "AsBytes is UTF-8");
        Throws<LuaTypeException>(() => s.AsLong(), "a string is not converted to a number");
        LuaValue nullString = (string)null;
        Check(nullString.IsNil && LuaValue.Nil.IsNil && default(LuaValue).IsNil, "null and default are nil");
        Equal(null, nullString.AsString(), "nil AsString is null");
        Check(!LuaValue.Nil.AsBool() && !((LuaValue)false).AsBool() && ((LuaValue)0).AsBool() && ((LuaValue)"").AsBool(),
            "Lua's truth: only nil and false are false");
        var e = Throws<LuaTypeException>(() => ((LuaValue)true).AsString(), "a boolean is no string");
        Check(e != null && e.Expected == LuaType.String && e.Actual == LuaType.Boolean, "LuaTypeException names the types");
        LuaValue bytes = LuaValue.FromBytes(new byte[] { 0xff, 0x00, 0x41 });
        Equal(3, bytes.AsBytes().Length, "FromBytes keeps any bytes");
        Equal("�\0A", bytes.AsString(), "invalid UTF-8 decodes to U+FFFD");
        Check(LuaValue.FromBytes(null).IsNil, "FromBytes(null) is nil");
    }

    public static void TestLuaValueEquality()
    {
        Check((LuaValue)1 == (LuaValue)1.0, "1 == 1.0, as in Lua");
        Check(((LuaValue)1).GetHashCode() == ((LuaValue)1.0).GetHashCode(), "equal numbers hash alike");
        Check((LuaValue)1 != (LuaValue)1.5 && (LuaValue)1 != (LuaValue)"1", "different values");
        Check((LuaValue)"abc" == LuaValue.FromBytes(Encoding.UTF8.GetBytes("abc")), "a string equals its UTF-8 bytes");
        Check(((LuaValue)"abc").GetHashCode() == LuaValue.FromBytes(Encoding.UTF8.GetBytes("abc")).GetHashCode(), "and hashes alike");
        Check(LuaValue.Nil == default(LuaValue) && (LuaValue)true == (LuaValue)true && (LuaValue)true != (LuaValue)false, "nil and booleans");
        Check(((LuaValue)double.NaN) != ((LuaValue)double.NaN), "NaN is not equal to itself");
        Check(((LuaValue)long.MaxValue) != ((LuaValue)9223372036854775807.0), "2^63 as a float is not long.MaxValue");
        Equal("nil", LuaValue.Nil.ToString(), "ToString nil");
        Equal("1.5", ((LuaValue)1.5).ToString(), "ToString 1.5");
        Equal("-7", ((LuaValue)(-7)).ToString(), "ToString -7");
        Check(((LuaValue)1).Equals((object)(LuaValue)1) && !((LuaValue)1).Equals((object)1), "Equals(object)");
    }

    // ---- Load ----

    public static void TestLoad()
    {
        using (var lua = Open())
        {
            lua.Load("x = 41", "a");
            Equal(42L, lua.Load<long>("return x + 1", "b"), "Load<T> returns the first result");
            var results = new LuaValue[3];
            Equal(2, lua.Load("return 'a', 2", "c", results), "Load with results returns the count");
            Check(results[0] == "a" && results[1] == 2 && results[2].IsNil, "Load's results; missing ones are nil");
            lua.Load(Encoding.UTF8.GetBytes("y = 'from bytes'"), "d");
            Equal("from bytes", lua.Load<string>("return y", "e"), "Load from UTF-8 bytes");
            Equal("ünï", lua.Load<string>("return 'ünï'", "f"), "non-ASCII text");
            Throws<ArgumentNullException>(() => lua.Load((string)null, "x"), "null code");
            Throws<ArgumentNullException>(() => lua.Load("return 1", null), "null chunk name");
        }
    }

    public static void TestLoadSyntaxErrors()
    {
        using (var lua = Open())
        {
            var e = Throws<LuaSyntaxException>(() => lua.Load("return +", "remote"), "a syntax error");
            Check(e != null && e.ChunkName == "remote" && e.LuaStackTrace == null, "the chunk name, no traceback");
            if (e != null) Contains(e.Message, "remote:1:", "the chunk name is '@remote'");
            e = Throws<LuaSyntaxException>(() => lua.Load("\u001bLuaT\u0000", "bin"), "a binary chunk");
            if (e != null) Contains(e.Message, "binary", "binary chunks are refused");
            e = Throws<LuaSyntaxException>(() => lua.Load(new byte[] { 0x1b, 0x4c, 0x75, 0x61, 0x54, 0x00 }, "bin"), "a binary chunk as bytes");
            Contains(lua.Load<string>("local f, err = load('return 1', 'x', 'b') return err", "mode"), "text chunks only",
                "load with mode 'b' fails");
        }
    }

    public static void TestChunkNamesInErrors()
    {
        using (var lua = Open())
        {
            var e = Throws<LuaRuntimeException>(() => lua.Load("local x = 1\nerror('boom')", "quest/main.lua"), "a runtime error");
            if (e != null)
            {
                Equal("quest/main.lua:2: boom", e.Message, "the message is name:line: text");
                Contains(e.LuaStackTrace, "stack traceback:", "the traceback is apart");
                Contains(e.ToString(), "stack traceback:", "ToString shows the traceback");
            }
        }
    }

    // ---- Call ----

    public static void TestCallArgumentShapes()
    {
        using (var lua = WithFunctions())
        {
            Equal(0L, lua.Call<long>("count"), "no argument");
            Equal(1L, lua.Call<long>("count", 1), "one");
            Equal(2L, lua.Call<long>("count", 1, 2), "two");
            Equal(3L, lua.Call<long>("count", 1, 2, 3), "three");
            Equal(4L, lua.Call<long>("count", 1, 2, 3, 4), "four");
            Equal(5L, lua.Call<long>("count", new LuaValue[] { 1, 2, 3, 4, 5 }), "a span");
            Equal(3L, lua.Call<long>("count", 1, LuaValue.Nil, LuaValue.Nil), "trailing nils count");
            var many = new LuaValue[40];
            for (int i = 0; i < many.Length; i++) many[i] = "s" + i;
            Equal(40L, lua.Call<long>("count", many), "40 arguments (more than the stack buffer)");
            var results = new LuaValue[40];
            Equal(40, lua.Call("echo", many, results), "40 results");
            Check(results[39] == "s39" && results[0] == "s0", "40 results in order");
            Equal("nil,boolean,integer,float,string,string", lua.Call<string>("types",
                new LuaValue[] { LuaValue.Nil, true, 7, 7.0, "x", LuaValue.FromBytes(new byte[] { 1 }) }), "argument types");
            lua.Call("none");
            lua.Call("none", 1);
            lua.Call("none", 1, 2);
            lua.Call("none", 1, 2, 3);
            lua.Call("none", 1, 2, 3, 4);
            lua.Call("none", new LuaValue[0]);
            Throws<ArgumentNullException>(() => lua.Call(null), "a null function name");
        }
    }

    public static void TestCallResultTypes()
    {
        using (var lua = WithFunctions())
        {
            Equal(3L, lua.Call<long>("add", 1, 2), "long");
            Equal(3, lua.Call<int>("add", 1, 2), "int");
            Equal(3.5, lua.Call<double>("add", 1, 2.5), "double");
            Equal(3.5f, lua.Call<float>("add", 1, 2.5), "float");
            Equal(3.0, lua.Call<double>("add", 1, 2), "an integer as double");
            Equal(4L, lua.Call<long>("add", 1.5, 2.5), "an integral float as long");
            Throws<LuaTypeException>(() => lua.Call<long>("add", 1, 0.5), "1.5 as long");
            Throws<LuaTypeException>(() => lua.Call<int>("echo", 1L << 40), "2^40 as int");
            Throws<LuaTypeException>(() => lua.Call<float>("echo", 1e300), "1e300 as float");
            Equal(float.PositiveInfinity, lua.Call<float>("echo", double.PositiveInfinity), "infinity as float");
            Throws<LuaTypeException>(() => lua.Call<long>("echo", "5"), "no string-to-number coercion");
            Throws<LuaTypeException>(() => lua.Call<long>("none"), "nil as long");
            Throws<LuaTypeException>(() => lua.Call<double>("echo", true), "a boolean as double");
            Throws<LuaTypeException>(() => lua.Call<string>("echo", 5), "no number-to-string conversion");
            Equal(null, lua.Call<string>("none"), "nil as string is null");
            Equal(null, lua.Call<byte[]>("none"), "nil as byte[] is null");
            Equal(true, lua.Call<bool>("echo", 0), "0 is true in Lua");
            Equal(false, lua.Call<bool>("none"), "nil is false");
            Equal(long.MinValue, lua.Call<long>("echo", long.MinValue), "64-bit integers are exact");
            Equal(long.MaxValue, lua.Call<long>("echo", long.MaxValue), "long.MaxValue");
            Equal("a\0b", lua.Call<string>("echo", "a\0b"), "embedded NULs");
            Equal("日本語", lua.Call<string>("echo", "日本語"), "UTF-8 round trip");
            byte[] raw = { 0xff, 0xfe, 0x00, 0x80 };
            byte[] back = lua.Call<byte[]>("echo", LuaValue.FromBytes(raw));
            Check(back != null && back.Length == 4 && back[0] == 0xff && back[3] == 0x80, "bytes round trip");
            Equal("��\0�", lua.Call<string>("echo", LuaValue.FromBytes(raw)), "invalid UTF-8 as string");
            LuaValue v = lua.Call<LuaValue>("echo", 2.25);
            Check(v.Type == LuaType.Number && v.AsDouble() == 2.25, "LuaValue");
            Throws<NotSupportedException>(() => lua.Call<decimal>("echo", 1), "an unsupported T");
            Throws<NotSupportedException>(() => lua.Call<object>("echo", 1), "object is not a result type");
            var kinds = new LuaValue[3];
            Equal(3, lua.Call("kinds", ReadOnlySpan<LuaValue>.Empty, kinds), "tables and functions");
            Check(kinds[0].Type == LuaType.Table && kinds[1].Type == LuaType.Function && kinds[2].Type == LuaType.Coroutine,
                "their types");
            Equal(3, lua.LiveHandles, "the table, the function and the coroutine are owning handles (milestones 8 and 9)");
            Check(lua.Call<LuaValue>("echo", kinds[0]).Equals(kinds[0]), "a table goes back as itself");
            using (LuaCoroutine echoed = lua.Call<LuaCoroutine>("echo", kinds[2]))
            {
                Check(echoed != null && echoed != kinds[2].AsCoroutine() && (LuaValue)echoed == kinds[2],
                    "a coroutine goes back as itself (a handle of its own)");
            }
            Throws<ArgumentException>(() => lua.Call("echo", LuaValue.OfType(LuaType.Table)), "a table without a handle cannot go back");
            foreach (LuaValue k in kinds) k.Dispose();
            Equal(1, lua.LiveHandles, "the echoed table is a handle of its own");
        }
    }

    public static void TestCallLookup()
    {
        using (var lua = WithFunctions())
        {
            Equal(42L, lua.Call<long>("quest.onStart", 21), "a dotted name");
            Equal("deep", lua.Call<string>("quest.deep.f"), "two dots");
            Equal("5", lua.Call<string>("tostring", 5), "a base function");
            var e = Throws<LuaRuntimeException>(() => lua.Call("missing"), "a missing function");
            if (e != null) Equal("attempt to call a nil value (global 'missing')", e.Message, "the message");
            e = Throws<LuaRuntimeException>(() => lua.Call("quest.nothing"), "a missing field");
            if (e != null) Equal("attempt to call a nil value (field 'nothing')", e.Message, "the message");
            e = Throws<LuaRuntimeException>(() => lua.Call("n.x"), "a number in the path");
            if (e != null) Equal("attempt to index a number value (global 'n')", e.Message, "the message");
            Throws<LuaRuntimeException>(() => lua.Call("n"), "a number is not callable");
            lua.Load("setmetatable(quest, {__index = function() return function() return 'meta' end end})", "meta");
            Throws<LuaRuntimeException>(() => lua.Call("quest.viaMeta"), "lookups are raw: no __index");
            lua.Load("callable = setmetatable({}, {__call = function(self, x) return x + 1 end})", "call");
            Equal(8L, lua.Call<long>("callable", 7), "__call works for the function itself");
        }
    }

    public static void TestMultipleResults()
    {
        using (var lua = WithFunctions())
        {
            var two = new LuaValue[2];
            Equal(5, lua.Call("many", ReadOnlySpan<LuaValue>.Empty, two), "more results than room: the count is all");
            Check(two[0] == 1 && two[1] == 2, "the first two");
            var seven = new LuaValue[7];
            for (int i = 0; i < seven.Length; i++) seven[i] = "stale";
            Equal(5, lua.Call("many", ReadOnlySpan<LuaValue>.Empty, seven), "fewer results than room");
            Check(seven[4] == 5 && seven[5].IsNil && seven[6].IsNil, "missing results are nil");
            Equal(0, lua.Call("none", ReadOnlySpan<LuaValue>.Empty, seven), "no results");
            Check(seven[0].IsNil, "all nil");
            Equal(5, lua.Call("many", ReadOnlySpan<LuaValue>.Empty, Span<LuaValue>.Empty), "no room");
            var t2 = lua.Call<long, string>("echo", new LuaValue[] { 7, "x" });
            Check(t2.Item1 == 7 && t2.Item2 == "x", "a pair");
            var t3 = lua.Call<long, double, bool>("echo", new LuaValue[] { 1, 2.5, false });
            Check(t3.Item1 == 1 && t3.Item2 == 2.5 && !t3.Item3, "a triple");
            var missing = lua.Call<string, string>("echo", new LuaValue[] { "only" });
            Check(missing.Item1 == "only" && missing.Item2 == null, "a missing second result is nil");
        }
    }

    // ---- errors ----

    public static void TestRuntimeErrors()
    {
        using (var lua = WithFunctions())
        {
            var e = Throws<LuaRuntimeException>(() => lua.Call("fail"), "error()");
            if (e != null)
            {
                Equal("functions:7: boom", e.Message, "the message");
                Contains(e.LuaStackTrace, "stack traceback:", "a traceback");
                Contains(e.LuaStackTrace, "functions:7:", "the traceback names the line");
            }
            e = Throws<LuaRuntimeException>(() => lua.Call("failt"), "error({code = 5})");
            if (e != null) Equal("(error object is a table value)", e.Message, "a table error is described");
            e = Throws<LuaRuntimeException>(() => lua.Load("error(setmetatable({}, {__tostring = function() return 'custom' end}))", "ts"), "__tostring");
            if (e != null) Equal("custom", e.Message, "__tostring of the error");
            Equal(8L, lua.Call<long>("add", 3, 5), "the script stays usable after errors");
        }
    }

    public static void TestInstructionLimit()
    {
        using (var lua = WithFunctions(new LuaOptions { InstructionLimit = 100000 }))
        {
            var e = Throws<LuaInstructionLimitException>(() => lua.Call("forever"), "an endless loop");
            if (e != null)
            {
                Check(e.Limit == LuaLimit.Instructions && e.InstructionLimit == 100000, "the limit");
                Contains(e.Message, "instruction limit exceeded", "the message");
                Check(e.LuaStackTrace != null, "a traceback");
            }
            Equal(100000L, lua.LastInstructionCount, "the count of a stopped call is the limit");
            Throws<LuaInstructionLimitException>(() => lua.Load("while true do pcall(function() while true do end end) end", "pcall"),
                "pcall cannot catch the stop");
            Equal(500500L, lua.Call<long>("spin", 1000), "a call within the limit");
            long first = lua.LastInstructionCount;
            Check(first > 1000 && first < 100000, "the count of a call");
            lua.Call<long>("spin", 1000);
            Equal(first, lua.LastInstructionCount, "the same count for the same call");
            lua.InstructionLimit = 0;
            Equal(0L, lua.InstructionLimit, "no instruction limit");
            lua.Call<long>("spin", 1000);
            Equal(0L, lua.LastInstructionCount, "no count without a limit");
            lua.InstructionLimit = 3L << 31;  // above 2^31: slices
            Equal(500500L, lua.Call<long>("spin", 1000), "a budget above 2^31");
        }
    }

    public static void TestTimeLimit()
    {
        using (var lua = WithFunctions(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(50) }))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var e = Throws<LuaTimeLimitException>(() => lua.Call("forever"), "an endless loop");
            Check(watch.ElapsedMilliseconds < 5000, "the watchdog stopped it in time (" + watch.ElapsedMilliseconds + " ms)");
            if (e != null) Check(e.Limit == LuaLimit.Time && e.TimeLimit == TimeSpan.FromMilliseconds(50), "the limit");
            Equal(3L, lua.Call<long>("add", 1, 2), "usable afterwards");
            lua.TimeLimit = TimeSpan.Zero;
            Equal(TimeSpan.Zero, lua.TimeLimit, "no time limit");
            for (int i = 0; i < 200; i++)
            {
                lua.TimeLimit = TimeSpan.FromMilliseconds(2);
                Throws<LuaTimeLimitException>(() => lua.Call("forever"), "a short limit, " + i);
            }
        }
    }

    public static void TestInterruptFromAnotherThread()
    {
        using (var lua = WithFunctions(new LuaOptions { InstructionLimit = long.MaxValue / 2 }))
        {
            var stopper = WithStarted(lua);  // the Interrupt comes once the call runs (StopLatencyTests.cs)
            lua.Load("function waiting() started() while true do end end", "w");
            long used = 0;
            stopper.Begin(() =>
            {
                lua.Interrupt();
                used = lua.MemoryUsed;  // statistics from any thread
            });
            var e = Throws<LuaInterruptedException>(() => lua.Call("waiting"), "Interrupt");
            stopper.End();
            Check(used > 0, "statistics from another thread");
            if (e != null) Check(e.Limit == LuaLimit.Interrupt && e.Message.Contains("interrupted"), "the limit");
            lua.Interrupt();  // nothing runs: a no-op
            Equal(3L, lua.Call<long>("add", 1, 2), "the next call runs");
        }
    }

    public static void TestMemoryLimit()
    {
        using (var lua = WithFunctions(new LuaOptions { MemoryLimit = 4 << 20 }))
        {
            var e = Throws<LuaMemoryLimitException>(() => lua.Load("local t = {} for i = 1, 1e8 do t[i] = ('x'):rep(100) .. i end", "grow"),
                "a table growth loop");
            if (e != null)
            {
                Check(e.Limit == LuaLimit.Memory && !e.HostOutOfMemory, "the memory limit, not the host");
                Check(e.MemoryLimit == 4 << 20 && e.MemoryUsed <= (4 << 20) + 16 * 1024, "used within the limit and the reserve: " + e.MemoryUsed);
                Contains(e.Message, "not enough memory", "Lua's message");
            }
            Throws<LuaMemoryLimitException>(() => lua.Load("return ('x'):rep(1 << 30)", "rep"), "a 1 GB string");
            Equal("caught", lua.Load<string>("local ok, err = pcall(string.rep, 'x', 1 << 30) return ok and 'no' or 'caught'", "pcall"),
                "a script can catch the memory error");
            Check(lua.MemoryPeak <= (4 << 20) + 16 * 1024, "the peak stays within the limit: " + lua.MemoryPeak);
            lua.CollectGarbage();
            long before = lua.MemoryUsed;
            lua.Load("junk = {} for i = 1, 1000 do junk[i] = {i} end junk = nil", "junk");
            lua.CollectGarbage();
            Check(lua.MemoryUsed < before + 64 * 1024, "CollectGarbage frees garbage");
            lua.MemoryLimit = lua.MemoryUsed;
            Throws<LuaMemoryLimitException>(() => lua.Load("x = {} for i = 1, 1000 do x[i] = i end", "low"), "a limit lowered below the use");
        }
    }

    // The first limit of a script without AllowInterrupt creates the stop messages
    // (xlua_core_set_limits). With the memory limit used up the allocation is refused,
    // which until 2026-10-28 raised outside a protected call: Lua panicked and the
    // process aborted.
    public static void TestFirstLimitWithoutMemory()
    {
        using (var lua = new LuaScript(new LuaOptions { MemoryLimit = 4 << 20, AllowInterrupt = false }))
        {
            lua.MemoryLimit = 1;
            Throws<LuaMemoryLimitException>(() => lua.InstructionLimit = 1000, "the first limit with no memory left");
            Equal(0L, lua.InstructionLimit, "the limit stays as it was");
            lua.MemoryLimit = 4 << 20;
            lua.InstructionLimit = 1000;
            Equal(1000L, lua.InstructionLimit, "a later call with room sets it");
            lua.MemoryLimit = 1;
            lua.InstructionLimit = 2000;  // the messages exist: nothing to allocate
            Equal(2000L, lua.InstructionLimit, "later limits need no memory");
            lua.MemoryLimit = 4 << 20;
            Throws<LuaInstructionLimitException>(() => lua.Load("while true do end", "loop"), "the limit applies");
        }
    }

    // ---- threads ----

    public static void TestThreadCheck()
    {
        using (var lua = WithFunctions())
        {
            Exception error = null;
            long used = 0;
            var t = new Thread(() =>
            {
                try { lua.Call("none"); }
                catch (Exception e) { error = e; }
                used = lua.MemoryUsed;
                lua.Interrupt();
            });
            t.Start();
            t.Join();
            Check(error is InvalidOperationException, "a call from another thread throws: " + error);
            Check(used > 0, "statistics from another thread");
        }
        using (var lua = WithFunctions(new LuaOptions { CheckThread = false }))
        {
            long result = 0;
            var t = new Thread(() => result = lua.Call<long>("add", 2, 3));
            t.Start();
            t.Join();
            Equal(5L, result, "CheckThread = false: another thread, one at a time");
        }
        using (var lua = WithFunctions())
        {
            // SetOwnerThread hands the script to another thread, and back.
            long result = 0;
            Exception error = null;
            var t = new Thread(() =>
            {
                try { lua.SetOwnerThread(); result = lua.Call<long>("add", 2, 3); }
                catch (Exception e) { error = e; }
            });
            t.Start();
            t.Join();
            Check(error == null, "SetOwnerThread on another thread: " + error);
            Equal(5L, result, "SetOwnerThread: the new owner calls");
            Throws<InvalidOperationException>(() => lua.Call("none"), "the creator after the hand-off");
            lua.SetOwnerThread();
            Equal(7L, lua.Call<long>("add", 3, 4), "SetOwnerThread: back on the creator");
        }
    }

    // Runs body on a thread of its own and returns what it threw.
    static Exception OnThread(Action body)
    {
        Exception error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { error = e; }
        });
        t.Start();
        t.Join();
        return error;
    }

    // A script belongs to the first thread that uses it after the constructor,
    // as a LuaEnv does (owner decision of 2026-10-28): a loader thread may create
    // it for the main thread.
    public static void TestThreadFirstUse()
    {
        LuaScript lua = null;
        Exception error = OnThread(() => lua = new LuaScript(new LuaOptions()));
        Check(error == null, "created on another thread: " + error);
        try
        {
            lua.Load(Functions, "functions");
            Equal(5L, lua.Call<long>("add", 2, 3), "created on another thread, used only here");
            error = OnThread(() => lua.Call("none"));
            Check(error is InvalidOperationException, "after the first use, another thread throws: " + error);
            lua.SetOwnerThread();
            Equal(7L, lua.Call<long>("add", 3, 4), "SetOwnerThread by the owner");
        }
        finally
        {
            lua.Dispose();
        }

        // The first use on a third thread: neither the creator nor this thread owns it.
        error = OnThread(() => lua = new LuaScript(new LuaOptions()));
        Check(error == null, "created on another thread: " + error);
        long result = 0;
        error = OnThread(() => { lua.Load(Functions, "functions"); result = lua.Call<long>("add", 1, 1); });
        Check(error == null && result == 2, "the first use on a third thread: " + error);
        Throws<InvalidOperationException>(() => lua.Call("none"), "this thread after a worker's first use");
        Throws<InvalidOperationException>(() => lua.Dispose(), "Dispose on a thread that does not own the script");
        Check(!lua.IsDisposed, "a refused Dispose leaves the script open");
        lua.SetOwnerThread();
        lua.Dispose();
        Check(lua.IsDisposed, "Dispose after SetOwnerThread");

        // Dispose as the first use.
        error = OnThread(() => lua = new LuaScript(new LuaOptions()));
        Check(error == null, "created on another thread: " + error);
        lua.Dispose();
        Check(lua.IsDisposed, "Dispose as the first use");

        // SetOwnerThread before any use hands the script over; the first user does not take it.
        lua = new LuaScript(new LuaOptions());
        error = OnThread(() => lua.SetOwnerThread());
        Check(error == null, "SetOwnerThread on another thread: " + error);
        Throws<InvalidOperationException>(() => lua.Load(Functions, "functions"), "after SetOwnerThread on another thread");
        lua.SetOwnerThread();
        lua.Dispose();

        // Two threads race for the first use: exactly one wins.
        for (int round = 0; round < 10; round++)
        {
            using (var raced = new LuaScript(new LuaOptions()))
            {
                // Created here, so the race is between the two threads alone.
                int ran = 0, refused = 0;
                var start = new ManualResetEvent(false);
                var racers = new Thread[2];
                for (int i = 0; i < racers.Length; i++)
                {
                    racers[i] = new Thread(() =>
                    {
                        start.WaitOne();
                        try { raced.Load("x = 1", "x"); Interlocked.Increment(ref ran); }
                        catch (InvalidOperationException) { Interlocked.Increment(ref refused); }
                    });
                    racers[i].Start();
                }
                start.Set();
                foreach (var t in racers) t.Join();
                Check(ran == 1 && refused == 1, "two threads racing for the first use: " + ran + " ran, " + refused + " refused");
                raced.SetOwnerThread();
            }
        }
    }

    public static void TestScriptsOnTwoThreads()
    {
        long a = 0, b = 0;
        Exception error = null;
        ThreadStart run = () =>
        {
            try
            {
                using (var lua = WithFunctions())
                {
                    long sum = 0;
                    for (int i = 0; i < 2000; i++) sum += lua.Call<long>("spin", 100);
                    if (Thread.CurrentThread.Name == "a") a = sum; else b = sum;
                }
            }
            catch (Exception e) { error = e; }
        };
        var ta = new Thread(run) { Name = "a" };
        var tb = new Thread(run) { Name = "b" };
        ta.Start();
        tb.Start();
        ta.Join();
        tb.Join();
        Check(error == null, "no error: " + error);
        Equal(2000L * 5050, a, "thread a");
        Equal(2000L * 5050, b, "thread b");
    }

    // ---- seed and require ----

    public static void TestHashSeed()
    {
        const string order = "local t = {} for i = 1, 64 do t['key' .. i] = i end local s = {} for k in pairs(t) do s[#s + 1] = k end return table.concat(s, ',')";
        string x, y;
        using (var lua = new LuaScript(new LuaOptions { HashSeed = 42 })) x = lua.Load<string>(order, "o");
        using (var lua = new LuaScript(new LuaOptions { HashSeed = 42 })) y = lua.Load<string>(order, "o");
        Equal(x, y, "the same seed, the same order");
        bool differs = false;
        for (uint seed = 1; seed < 8 && !differs; seed++)
        {
            using (var lua = new LuaScript(new LuaOptions { HashSeed = seed })) differs = lua.Load<string>(order, "o") != x;
        }
        Check(differs, "other seeds give another order");
    }

    public static void TestRequire()
    {
        using (var lua = Open())
        {
            Equal(true, lua.Load<bool>("return require == nil", "r"), "no require without a resolver");
            int asked = 0;
            lua.ModuleResolver = (string name, out string code, out string chunkName) =>
            {
                asked++;
                chunkName = null;
                code = null;
                if (name == "mod") { code = "return { v = 7, name = ... }"; return true; }
                if (name == "named") { code = "error('in named')"; chunkName = "lib/named.lua"; return true; }
                if (name == "throws") throw new InvalidOperationException("resolver broke");
                return false;
            };
            Equal(7L, lua.Load<long>("return require('mod').v", "r"), "require");
            Equal("mod", lua.Load<string>("return require('mod').name", "r"), "the module gets its name");
            Equal(1, asked, "modules are cached");
            var e = Throws<LuaRuntimeException>(() => lua.Load("require('missing')", "r"), "a missing module");
            if (e != null) Contains(e.Message, "module 'missing' not found", "the message");
            e = Throws<LuaRuntimeException>(() => lua.Load("require('named')", "r"), "an error in a module");
            if (e != null) Contains(e.Message, "lib/named.lua:1: in named", "the module's chunk name");
            e = Throws<LuaRuntimeException>(() => lua.Load("require('throws')", "r"), "a resolver that throws");
            if (e != null) Contains(e.Message, "error loading module 'throws': InvalidOperationException: resolver broke", "the message");
            Check(lua.ModuleResolver != null, "the resolver is kept");
            lua.ModuleResolver = null;
            Equal(true, lua.Load<bool>("return require == nil", "r"), "a null resolver removes require");
        }
    }

    public static void TestDisposeInsideACall()
    {
        using (var lua = Open())
        {
            bool refused = false;
            lua.ModuleResolver = (string name, out string code, out string chunkName) =>
            {
                try { lua.Dispose(); }
                catch (InvalidOperationException) { refused = true; }
                // A nested call from inside the call runs too.
                code = "return " + lua.Load<long>("return 40 + 2", "nested");
                chunkName = null;
                return true;
            };
            Equal(42L, lua.Load<long>("return require('m')", "outer"), "a nested call inside a call");
            Check(refused && !lua.IsDisposed, "Dispose inside a call throws and the script stays open");
        }
    }

    // ---- allocations ----

    static long Allocated()
    {
        return GC.GetAllocatedBytesForCurrentThread();
    }

    public static void TestPrimitiveCallsAllocateNothing()
    {
        // The runtime must count: an array of 1,000 bytes shows up.
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            Console.WriteLine("  GC.GetAllocatedBytesForCurrentThread does not count on this runtime; skipped");
            return;
        }
        using (var lua = WithFunctions(new LuaOptions { MemoryLimit = 16 << 20, InstructionLimit = 1000000 }))
        {
            lua.Load("function prim(a, b, c) if c then return a + b end return 0 end function str(s) return #s end", "alloc");
            var args = new LuaValue[] { 1, 2.5, true, "text" };
            var results = new LuaValue[4];
            for (int round = 0; round < 2; round++)  // the first round warms up buffers and JIT
            {
                long before = Allocated();
                double sum = 0;
                for (int i = 0; i < 10000; i++)
                {
                    sum += lua.Call<double>("prim", i, 2.5, true);
                    sum += lua.Call<long>("count", 1L, 2.5, false, i);
                    sum += lua.Call<long>("str", "a string argument");
                    sum += lua.Call<int>("count", args);
                    sum += lua.Call("echo", args.AsSpan(0, 3), results);
                    lua.Call("none");
                    sum += lua.Load<long>("return 1", "chunk");
                }
                long allocated = Allocated() - before;
                if (round == 1)
                {
                    Console.WriteLine("  allocation probe: " + allocated + " bytes for 70,000 calls");
                    Equal(0L, allocated, "bytes allocated by 70,000 calls with primitive arguments and results");
                }
                Check(sum > 0, "the calls ran");
            }
        }
        GC.KeepAlive(keep);
    }

    public static void TestStringResultsAllocateOnlyTheString()
    {
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            return;
        }
        using (var lua = WithFunctions())
        {
            for (int round = 0; round < 2; round++)
            {
                long before = Allocated();
                int length = 0;
                for (int i = 0; i < 1000; i++)
                {
                    length += lua.Call<string>("echo", "abcdefgh").Length;
                }
                long allocated = Allocated() - before;
                if (round == 1)
                {
                    // 1,000 strings of 8 chars: about 40 bytes each on 64-bit runtimes.
                    Check(allocated > 0 && allocated <= 1000 * 64, "only the result strings are allocated: " + allocated);
                }
                Check(length == 8000, "the strings");
            }
        }
        GC.KeepAlive(keep);
    }

#if XLUA_CORE_TEST_BRIDGE
    // ---- both APIs in one process (run_core.py --bridge: the full runtime and a bridge library) ----

    public static void TestLuaEnvAndLuaScriptSideBySide()
    {
        using (var env = new LuaEnv())
        using (var a = Open())
        using (var b = new LuaScript(new LuaOptions { InstructionLimit = 10000 }))
        {
            env.DoString("x = 'env'");
            a.Load("x = 'a'", "a");
            Equal("env", (string)env.DoString("return x")[0], "LuaEnv keeps its globals");
            Equal("a", a.Load<string>("return x", "a"), "script a has its own");
            Equal(true, b.Load<bool>("return x == nil", "b"), "script b sees neither");
            Equal(true, a.Load<bool>("return CS == nil and xlua == nil and io == nil and os == nil and debug == nil and package == nil", "a"),
                "a script sees no CS, xlua, io, os, debug or package on the full library");
            Throws<LuaSyntaxException>(() => a.Load("\u001bLuaT\u0000", "bin"), "bytecode is refused on the full library too");
            Throws<LuaInstructionLimitException>(() => b.Load("while true do end", "spin"), "a limit on one script");
            env.DoString("local s = 0 for i = 1, 100000 do s = s + i end");  // no limit on the LuaEnv
            Equal(100000L, a.Load<long>("local s = 0 for i = 1, 100000 do s = s + 1 end return s", "a"), "nor on the other script");
            env.Global.Set("callScript", (Func<long, long>)(v => a.Load<long>("return " + v + " * 2", "fromenv")));
            Equal(42L, Convert.ToInt64(env.DoString("return callScript(21)")[0]), "a LuaEnv callback calls into a LuaScript");
        }
    }
#endif
}
