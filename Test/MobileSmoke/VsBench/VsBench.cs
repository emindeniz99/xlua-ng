// The library-neutral part of the cross-library player bench (vs_bench.py, docs/research/benchmark-vs-puerts-zlua.md):
// a subset of zlua-benchmark's case list, the C# targets those cases call, one Lua module with every case, and the
// timing loop. An adapter per library (VsBenchXLua.cs, VsBenchPuerts.cs, VsBenchZLua.cs, VsBenchMoonSharp.cs) loads
// the module and turns its functions into C# delegates; nothing here depends on a Lua library.
//
// Source of the case list: github.com/focus-creative-games/zlua-benchmark at ef18f1e9 (2026-07-28),
// shared/case_manifest.txt (the `line` column below), shared/Lua/bench_lua2cs.lua (each Lua -> C# case runs its
// operation 10 times per loop iteration, BenchmarkConfig.Lua2CsCallsPerLoop) and shared/Lua/bench_cs2lua.lua.
// The targets copy the members of shared/CSharp/TestTargets.Generated.cs and TestTypes.cs that these cases use,
// with two changes: methods that return a string, object or class return their first argument instead of null,
// and Prop_String and Prop_Class start with a value, so those results are marshalled rather than nil; and Vector3
// is built from float literals (1.0, not 1), which PuerTS's Lua backend needs for float parameters.
//
// Method (as Test/MobileSmoke/PlayerBench.cs): 7 rounds; a round runs every case once with 100,000 operations after
// a full Lua and C# collection; a case's result is the median of its rounds. The coroutine yields a frame between
// cases. Then the pure Lua workloads (Pure below, docs/research/benchmark-vs-moonsharp-ng.md): PureRuns rounds of
// every workload with its full iteration count, ns per iteration; and an allocation pass with both collectors
// stopped. Output lines: XLUA_VSBENCH_META, XLUA_VSBENCH (one per case: ns per operation of each round, the Lua or
// C# loop included; pure.* cases also give their check value), XLUA_VSBENCH_SKIP (a case the library cannot run,
// with the error), XLUA_VSBENCH_ALLOC (managed and Lua bytes per operation), XLUA_VSBENCH_CHECK (values the cases
// left behind, so a mapping that did nothing shows) and XLUA_VSBENCH_DONE; the same lines go to vsbench-result.txt
// in persistentDataPath.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
using Debug = UnityEngine.Debug;

namespace VsBench
{
    public enum TestEnum { Zero = 0, One = 1, Two = 2 }

    [Preserve]
    public sealed class TestClass
    {
        public int A, B, C;
        public TestClass(int a, int b, int c) { A = a; B = b; C = c; }
    }

    [Preserve]
    public struct TestStruct
    {
        public int A, B, C;
        public TestStruct(int a, int b, int c) { A = a; B = b; C = c; }
    }

    [Preserve]
    public sealed class TestTargets
    {
        public static readonly TestTargets Instance = new TestTargets();
        public static readonly TestClass SampleClass = new TestClass(1, 2, 3);

        public static int StaticField_Int;
        public static int StaticProp_Int { get; set; }
        public int Field_Int;
        public int Prop_Int { get; set; }
        public string Prop_String { get; set; } = "bench";
        public TestClass Prop_Class { get; set; } = SampleClass;
        public Vector3 Field_Vector3;
        public Vector3 Prop_Vector3 { get; set; }
        public TestEnum Prop_Enum { get; set; } = TestEnum.Two;

        [MethodImpl(MethodImplOptions.NoInlining)] public static void StaticNoRet() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public static int StaticRet() { return 0; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void MethodVoidIntArity0() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public int MethodRetIntArity3(int p0, int p1, int p2) { return 0; }
        [MethodImpl(MethodImplOptions.NoInlining)] public int MethodRetIntArity6(int p0, int p1, int p2, int p3, int p4, int p5) { return 0; }
        [MethodImpl(MethodImplOptions.NoInlining)] public long MethodRet_Long3(long a, long b, long c) { return 0L; }
        [MethodImpl(MethodImplOptions.NoInlining)] public double MethodRet_Double3(double a, double b, double c) { return 0.0; }
        [MethodImpl(MethodImplOptions.NoInlining)] public string MethodRet_String3(string a, string b, string c) { return a; }
        [MethodImpl(MethodImplOptions.NoInlining)] public object MethodRet_Object3(object a, object b, object c) { return a; }
        [MethodImpl(MethodImplOptions.NoInlining)] public TestClass MethodRet_Class3(TestClass a, TestClass b, TestClass c) { return a; }
        [MethodImpl(MethodImplOptions.NoInlining)] public Vector3 MethodRet_Vector33(Vector3 a, Vector3 b, Vector3 c) { return default(Vector3); }
        [MethodImpl(MethodImplOptions.NoInlining)] public TestStruct MethodRet_Struct3(TestStruct a, TestStruct b, TestStruct c) { return default(TestStruct); }
    }

    // C# -> Lua signatures as named delegates: every library binds a named delegate type the same way, while
    // generic Func instances differ between them.
    public delegate void Step(int n);
    public delegate void Void0();
    public delegate string Str0();
    public delegate void VoidInt3(int a, int b, int c);
    public delegate int RetInt3(int a, int b, int c);
    public delegate long RetLong3(long a, long b, long c);
    public delegate double RetDouble3(double a, double b, double c);
    public delegate string RetString3(string a, string b, string c);
    public delegate object RetObject3(object a, object b, object c);
    public delegate TestClass RetClass3(TestClass a, TestClass b, TestClass c);
    public delegate Vector3 RetVector33(Vector3 a, Vector3 b, Vector3 c);
    public delegate TestStruct RetStruct3(TestStruct a, TestStruct b, TestStruct c);
    // The pure Lua workloads: n iterations, the result as a string (the check value).
    public delegate string Work(int n);
    // collectgarbage('count') for the allocation pass.
    public delegate double Num0();

    public interface ILuaAdapter
    {
        // The Lua code that defines the locals TT (TestTargets), TC (TestClass), TS (TestStruct), TE (TestEnum)
        // and V3 (UnityEngine.Vector3) in this library's syntax.
        string Prelude { get; }
        // Runs the module (its text returns the table M) and keeps M.
        void Load(string module);
        // M[name] as a C# delegate of type T.
        T Get<T>(string name) where T : MulticastDelegate;
        // A loop of n reads of M.tbl.x from C#, or null when the library has no table API in C#.
        Step TableRead();
        void Dispose();
    }

    public static class Runner
    {
        const int Rounds = 7;
        const int N = 100000;
        const int PerLoop = 10;  // BenchmarkConfig.Lua2CsCallsPerLoop
        const int AllocOps = 10000;  // operations per bridge case in the allocation pass

        // name, case_manifest.txt line (0: not in zlua's list), setup (Lua), operation (Lua, run PerLoop times).
        static readonly string[,] Lua2Cs = {
            {"lua2cs.static.method.0arg.void", "57", "", "TT.StaticNoRet()"},
            {"lua2cs.static.method.0arg.ret", "58", "", "local _ = TT.StaticRet()"},
            {"lua2cs.static.prop.get.int", "83", "", "local _ = TT.StaticProp_Int"},
            {"lua2cs.static.field.set.int", "86", "local v = 42", "TT.StaticField_Int = v"},
            {"lua2cs.ctor.class.3int", "143", "", "TC(1, 2, 3)"},
            {"lua2cs.ctor.Vector3.full", "146", "", "V3(1.0, 2.0, 3.0)"},
            {"lua2cs.instance.method.0arg.void", "148", "local o = TT.Instance", "o:MethodVoidIntArity0()"},
            {"lua2cs.instance.method.3arg.ret", "155", "local o = TT.Instance", "local _ = o:MethodRetIntArity3(1, 2, 3)"},
            {"lua2cs.instance.method.6arg.ret", "161", "local o = TT.Instance", "local _ = o:MethodRetIntArity6(1, 2, 3, 4, 5, 6)"},
            {"lua2cs.instance.prop.get.int", "200", "local o = TT.Instance", "local _ = o.Prop_Int"},
            {"lua2cs.instance.prop.set.int", "201", "local o, v = TT.Instance, 42", "o.Prop_Int = v"},
            {"lua2cs.instance.field.get.int", "202", "local o = TT.Instance", "local _ = o.Field_Int"},
            {"lua2cs.instance.field.set.int", "203", "local o, v = TT.Instance, 42", "o.Field_Int = v"},
            {"lua2cs.instance.method.long.3arg.ret", "211", "local o, v = TT.Instance, 999999", "local _ = o:MethodRet_Long3(v, v, v)"},
            {"lua2cs.instance.method.double.3arg.ret", "229", "local o, v = TT.Instance, 2.718", "local _ = o:MethodRet_Double3(v, v, v)"},
            {"lua2cs.instance.method.object.3arg.ret", "235", "local o, v = TT.Instance, 42", "local _ = o:MethodRet_Object3(v, v, v)"},
            {"lua2cs.instance.method.string.3arg.ret", "241", "local o, v = TT.Instance, 'bench'", "local _ = o:MethodRet_String3(v, v, v)"},
            {"lua2cs.instance.prop.get.string", "242", "local o = TT.Instance", "local _ = o.Prop_String"},
            {"lua2cs.instance.prop.set.string", "243", "local o, v = TT.Instance, 'bench'", "o.Prop_String = v"},
            {"lua2cs.instance.method.Vector3.3arg.ret", "259", "local o, v = TT.Instance, V3(1.0, 2.0, 3.0)", "local _ = o:MethodRet_Vector33(v, v, v)"},
            {"lua2cs.instance.prop.get.Vector3", "260", "local o = TT.Instance", "local _ = o.Prop_Vector3"},
            {"lua2cs.instance.prop.set.Vector3", "261", "local o, v = TT.Instance, V3(1.0, 2.0, 3.0)", "o.Prop_Vector3 = v"},
            {"lua2cs.instance.field.get.Vector3", "262", "local o = TT.Instance", "local _ = o.Field_Vector3"},
            {"lua2cs.instance.prop.get.enum", "272", "local o = TT.Instance", "local _ = o.Prop_Enum"},
            {"lua2cs.instance.method.class.3arg.ret", "277", "local o, v = TT.Instance, TC(1, 2, 3)", "local _ = o:MethodRet_Class3(v, v, v)"},
            {"lua2cs.instance.prop.get.class", "278", "local o = TT.Instance", "local _ = o.Prop_Class"},
            {"lua2cs.instance.method.struct.3arg.ret", "283", "local o, v = TT.Instance, TS(1, 2, 3)", "local _ = o:MethodRet_Struct3(v, v, v)"},
            // Not in zlua's list: the Lua loop alone, and table access inside Lua (the VM, no bridge).
            {"lua.loop", "0", "local s = 0", "s = s + 1"},
            {"lua.table.rw", "0", "local t = {x = 1}", "t.x = t.x + 1"},
        };

        // name, line, Lua function (bench_cs2lua.lua: the function takes its arguments and returns the first)
        static readonly string[,] Cs2Lua = {
            {"cs2lua.int.arity0.void", "1", "function() end"},
            {"cs2lua.int.3arg.void", "27", "function(a, b, c) end"},
            {"cs2lua.int.3arg.ret", "28", "function(a, b, c) return a end"},
            {"cs2lua.long.3arg.ret", "32", "function(a, b, c) return a end"},
            {"cs2lua.double.3arg.ret", "38", "function(a, b, c) return a end"},
            {"cs2lua.object.3arg.ret", "40", "function(a, b, c) return a end"},
            {"cs2lua.string.3arg.ret", "42", "function(a, b, c) return a end"},
            {"cs2lua.Vector3.3arg.ret", "48", "function(a, b, c) return a end"},
            {"cs2lua.class.3arg.ret", "54", "function(a, b, c) return a end"},
            {"cs2lua.struct.3arg.ret", "56", "function(a, b, c) return a end"},
        };

        // The pure Lua workloads (docs/research/benchmark-vs-moonsharp-ng.md): the Lua sources and iteration counts
        // of the pure-Lua cases of Test/Native/flag_bench.c, as MoonSharp-ng's src/DevTools/Benchmarks/LuaWorkloads.cs
        // copies them (the 18 rows of its docs/comparison-xlua-ng.md). table_sort is LuaWorkloads.cs's
        // table_sort_52, which copies with a loop because Lua 5.2 has no table.move; every library runs that one.
        // name, iterations, source (a chunk that returns function(n)), check value (tostring of the result; empty:
        // not checked: method_call depends on earlier runs, and table_sort's seed arithmetic differs between
        // Lua 5.2's doubles and 5.3+'s integers).
        static readonly string[,] Pure = {
            {"pure.empty_loop", "4000000", "return function(n) for i = 1, n do end end", "nil"},
            {"pure.function_call", "1000000", "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", "1000000"},
            {"pure.method_call", "1000000", "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\nlocal o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", ""},
            {"pure.table_array_insert", "1000000", "return function(n) local t = {} for i = 1, n do t[i] = i end return #t end", "1000000"},
            {"pure.table_array_read", "1000000", "local t = {} for i = 1, 1024 do t[i] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", "512370976"},
            {"pure.table_hash_set_get", "1000000", "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", "500000500000"},
            {"pure.string_concat", "500000", "return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = 'item' .. i j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "item499968"},
            {"pure.string_format", "500000", "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "499968:x"},
            {"pure.closure_create_call", "1000000", "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", "500000500000"},
            {"pure.int_math", "2000000", "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", "967568"},
            {"pure.fib_recursive", "20000", "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", "2880000"},
            {"pure.float_math", "2000000", "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", "0.004500"},
            {"pure.string_pattern", "100000", "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\nreturn function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end", "2500000"},
            {"pure.table_sort", "2000", "local src, x = {}, 12345 for i = 1, 256 do x = (x * 1103515245 + 12345) % 2147483648 src[i] = x end\nreturn function(n) local s = 0 for i = 1, n do local t = {} for k = 1, 256 do t[k] = src[k] end table.sort(t) s = s + t[1] % 1000 end return s end", ""},
            {"pure.table_concat", "50000", "local parts = {} for i = 1, 32 do parts[i] = tostring(i) end\nreturn function(n) local s = 0 for i = 1, n do s = s + #table.concat(parts, ',') end return s end", "4300000"},
            {"pure.coroutine_switch", "500000", "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\nlocal s = 0 for i = 1, n do s = s + co() end return s end", "125000250000"},
            {"pure.gc_small_tables", "500000", "return function(n) local keep, s = {}, 0 for i = 1, n do local t = { i, i + 1, x = i } keep[i % 128 + 1] = t s = s + t[2] end return s end", "125000750000"},
            {"pure.oop_inherit", "1000000", "local Base = {} Base.__index = Base function Base:get() return self.v end\nlocal Derived = setmetatable({}, Base) Derived.__index = Derived\nlocal o = setmetatable({ v = 3 }, Derived)\nreturn function(n) local s = 0 for i = 1, n do s = s + o:get() end return s end", "3000000"},
        };

        const int PureRuns = 5;  // timed runs per launch of each pure Lua workload, after a warm-up of a tenth

        public static string Module(string prelude)
        {
            var sb = new StringBuilder();
            sb.Append("local M = {}\n").Append(prelude).Append('\n');
            sb.Append("M.version = function() return _VERSION end\n");
            sb.Append("M.gc = function() collectgarbage('collect') collectgarbage('collect') end\n");
            // The allocation pass: the Lua heap in KB (a native Lua's own heap; MoonSharp reports the managed heap),
            // and the Lua collector stopped and restarted around a measured step.
            sb.Append("M.heapkb = function() return collectgarbage('count') end\n");
            sb.Append("M.gcstop = function() collectgarbage('stop') end\n");
            sb.Append("M.gcrestart = function() collectgarbage('restart') end\n");
            // Integer results print without '.0' on every Lua version (tostring of a 5.3+ float would add it).
            sb.Append("local function show(v) if math.type and math.type(v) == 'float' and v == math.floor(v) and v < 2^53 then return string.format('%d', v) end return tostring(v) end\n");
            for (int c = 0; c < Pure.GetLength(0); c++)
                sb.Append("do local f = (function()\n").Append(Pure[c, 2]).Append("\nend)()\nM['").Append(Pure[c, 0])
                    .Append("'] = function(n) return show((f(n))) end end\n");
            sb.Append("M.tbl = {x = 42}\n");
            sb.Append("M.check = function() local o = TT.Instance return string.format('field=%s prop=%s static=%s v3=%s,%s,%s', "
                + "tostring(o.Field_Int), tostring(o.Prop_Int), tostring(TT.StaticField_Int), tostring(o.Prop_Vector3.x), "
                + "tostring(o.Prop_Vector3.y), tostring(o.Prop_Vector3.z)) end\n");
            for (int c = 0; c < Lua2Cs.GetLength(0); c++)
            {
                sb.Append("M['").Append(Lua2Cs[c, 0]).Append("'] = function(n)\n  ").Append(Lua2Cs[c, 2]).Append("\n  for _ = 1, n do\n");
                for (int k = 0; k < PerLoop; k++) sb.Append("    ").Append(Lua2Cs[c, 3]).Append('\n');
                sb.Append("  end\nend\n");
            }
            for (int c = 0; c < Cs2Lua.GetLength(0); c++)
                sb.Append("M['").Append(Cs2Lua[c, 0]).Append("'] = ").Append(Cs2Lua[c, 2]).Append('\n');
            sb.Append("return M\n");
            return sb.ToString();
        }

        static string Num(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        static string Json(string s) { return s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\""; }

        // The C# loops of the C# -> Lua cases; each needs its own delegate type.
        static Step Cs2LuaLoop(ILuaAdapter lua, string name)
        {
            var v3 = new Vector3(1, 2, 3);
            var cls = new TestClass(1, 2, 3);
            var st = new TestStruct(1, 2, 3);
            object boxed = 42;
            switch (name)
            {
                case "cs2lua.int.arity0.void": { var f = lua.Get<Void0>(name); return n => { for (int i = 0; i < n; i++) f(); }; }
                case "cs2lua.int.3arg.void": { var f = lua.Get<VoidInt3>(name); return n => { for (int i = 0; i < n; i++) f(1, 2, 3); }; }
                case "cs2lua.int.3arg.ret": { var f = lua.Get<RetInt3>(name); return n => { int s = 0; for (int i = 0; i < n; i++) s += f(i, 2, 3); Sink(s); }; }
                case "cs2lua.long.3arg.ret": { var f = lua.Get<RetLong3>(name); return n => { long s = 0; for (int i = 0; i < n; i++) s += f(999999L, 2, 3); Sink(s); }; }
                case "cs2lua.double.3arg.ret": { var f = lua.Get<RetDouble3>(name); return n => { double s = 0; for (int i = 0; i < n; i++) s += f(2.718, 2, 3); Sink(s); }; }
                case "cs2lua.object.3arg.ret": { var f = lua.Get<RetObject3>(name); return n => { object s = null; for (int i = 0; i < n; i++) s = f(boxed, boxed, boxed); Sink(s); }; }
                case "cs2lua.string.3arg.ret": { var f = lua.Get<RetString3>(name); return n => { string s = null; for (int i = 0; i < n; i++) s = f("bench", "bench", "bench"); Sink(s); }; }
                case "cs2lua.Vector3.3arg.ret": { var f = lua.Get<RetVector33>(name); return n => { var s = default(Vector3); for (int i = 0; i < n; i++) s = f(v3, v3, v3); Sink(s); }; }
                case "cs2lua.class.3arg.ret": { var f = lua.Get<RetClass3>(name); return n => { TestClass s = null; for (int i = 0; i < n; i++) s = f(cls, cls, cls); Sink(s); }; }
                case "cs2lua.struct.3arg.ret": { var f = lua.Get<RetStruct3>(name); return n => { var s = default(TestStruct); for (int i = 0; i < n; i++) s = f(st, st, st); Sink(s); }; }
            }
            throw new ArgumentException(name);
        }

        static object sink;
        [MethodImpl(MethodImplOptions.NoInlining)] static void Sink<T>(T v) { sink = v; }

        // What one call of each C# -> Lua delegate returns, for the check line.
        static string Cs2LuaValues(ILuaAdapter lua)
        {
            try
            {
                return "int=" + lua.Get<RetInt3>("cs2lua.int.3arg.ret")(7, 2, 3)
                    + " long=" + lua.Get<RetLong3>("cs2lua.long.3arg.ret")(1L << 40, 2, 3)
                    + " str=" + lua.Get<RetString3>("cs2lua.string.3arg.ret")("bench", "b", "c")
                    + " v3=" + lua.Get<RetVector33>("cs2lua.Vector3.3arg.ret")(new Vector3(4, 5, 6), default(Vector3), default(Vector3)).ToString()
                    + " class=" + (lua.Get<RetClass3>("cs2lua.class.3arg.ret")(TestTargets.SampleClass, null, null) == TestTargets.SampleClass)
                    + " struct=" + lua.Get<RetStruct3>("cs2lua.struct.3arg.ret")(new TestStruct(7, 8, 9), default(TestStruct), default(TestStruct)).B;
            }
            catch (Exception e)
            {
                return "error " + e.GetType().Name + ": " + e.Message;
            }
        }

        public static IEnumerator Run(Func<ILuaAdapter> create, string library, string mode)
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;  // keeps the screen on while this app is in front
            var lines = new List<string>();
            Action<string> log = line => { Debug.Log(line); lines.Add(line); };
            yield return null;
            yield return new WaitForSeconds(2f);
            ILuaAdapter lua = null;
            var names = new List<string>();
            var runs = new List<Step>();
            var perCall = new List<int>();
            string version = "?";
            Action gc = null;
            try
            {
                lua = create();
                lua.Load(Module(lua.Prelude));
                version = lua.Get<Str0>("version")();
                var luaGc = lua.Get<Void0>("gc");
                gc = () => luaGc();
            }
            catch (Exception e)
            {
                log("XLUA_VSBENCH_SKIP {\"name\": \"setup\", \"error\": " + Json(e.ToString()) + "}");
                log("XLUA_VSBENCH_DONE " + library + "/" + mode);
                yield break;
            }
            for (int c = 0; c < Lua2Cs.GetLength(0); c++)
            {
                try
                {
                    var f = lua.Get<Step>(Lua2Cs[c, 0]);
                    f(N / PerLoop / 100);  // warm-up: lazy registration of the bindings, Lua's caches
                    names.Add(Lua2Cs[c, 0]); runs.Add(f); perCall.Add(PerLoop);
                }
                catch (Exception e)
                {
                    log("XLUA_VSBENCH_SKIP {\"name\": " + Json(Lua2Cs[c, 0]) + ", \"error\": " + Json(e.GetType().Name + ": " + e.Message) + "}");
                }
            }
            for (int c = 0; c < Cs2Lua.GetLength(0); c++)
            {
                try
                {
                    var f = Cs2LuaLoop(lua, Cs2Lua[c, 0]);
                    f(N / 100);
                    names.Add(Cs2Lua[c, 0]); runs.Add(f); perCall.Add(1);
                }
                catch (Exception e)
                {
                    log("XLUA_VSBENCH_SKIP {\"name\": " + Json(Cs2Lua[c, 0]) + ", \"error\": " + Json(e.GetType().Name + ": " + e.Message) + "}");
                }
            }
            try
            {
                var t = lua.TableRead();
                if (t == null) log("XLUA_VSBENCH_SKIP {\"name\": \"cs.table.get\", \"error\": \"no table API in C#\"}");
                else { t(N / 100); names.Add("cs.table.get"); runs.Add(t); perCall.Add(1); }
            }
            catch (Exception e)
            {
                log("XLUA_VSBENCH_SKIP {\"name\": \"cs.table.get\", \"error\": " + Json(e.GetType().Name + ": " + e.Message) + "}");
            }
            log("XLUA_VSBENCH_META {\"library\": " + Json(library) + ", \"mode\": " + Json(mode) + ", \"lua\": " + Json(version)
                + ", \"device\": " + Json(SystemInfo.deviceModel) + ", \"cpu\": " + Json(SystemInfo.processorType)
                + ", \"cores\": " + SystemInfo.processorCount + ", \"rounds\": " + Rounds + ", \"operations\": " + N
                + ", \"cases\": " + names.Count + ", \"unity\": " + Json(Application.unityVersion)
                + ", \"stopwatch_hz\": " + Stopwatch.Frequency + "}");
            var ns = new double[names.Count, Rounds];
            for (int r = 0; r < Rounds; r++)
            {
                for (int c = 0; c < names.Count; c++)
                {
                    gc();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    int count = N / perCall[c];
                    long start = Stopwatch.GetTimestamp();
                    runs[c](count);
                    long ticks = Stopwatch.GetTimestamp() - start;
                    ns[c, r] = ticks * (1e9 / Stopwatch.Frequency) / N;
                    yield return null;
                }
            }
            for (int c = 0; c < names.Count; c++)
            {
                var row = new double[Rounds];
                var parts = new string[Rounds];
                for (int r = 0; r < Rounds; r++) { row[r] = ns[c, r]; parts[r] = Num(Math.Round(ns[c, r], 2)); }
                Array.Sort(row);
                log("XLUA_VSBENCH {\"name\": " + Json(names[c]) + ", \"library\": " + Json(library) + ", \"mode\": " + Json(mode)
                    + ", \"min\": " + Num(Math.Round(row[0], 2)) + ", \"median\": " + Num(Math.Round(row[Rounds / 2], 2))
                    + ", \"runs\": [" + string.Join(", ", parts) + "]}");
            }

            // The pure Lua workloads: PureRuns rounds, each running every workload once with its full iteration
            // count after both collections; ns per iteration, the median of the rounds.
            var pureNames = new List<string>();
            var pureRuns = new List<Work>();
            var pureIters = new List<int>();
            for (int c = 0; c < Pure.GetLength(0); c++)
            {
                try
                {
                    var f = lua.Get<Work>(Pure[c, 0]);
                    int iters = int.Parse(Pure[c, 1], CultureInfo.InvariantCulture);
                    f(Math.Max(1, iters / 10));  // warm-up
                    pureNames.Add(Pure[c, 0]); pureRuns.Add(f); pureIters.Add(iters);
                }
                catch (Exception e)
                {
                    log("XLUA_VSBENCH_SKIP {\"name\": " + Json(Pure[c, 0]) + ", \"error\": " + Json(e.GetType().Name + ": " + e.Message) + "}");
                }
                yield return null;
            }
            var pureNs = new double[pureNames.Count, PureRuns];
            var pureCheck = new string[pureNames.Count];
            for (int r = 0; r < PureRuns; r++)
            {
                for (int c = 0; c < pureNames.Count; c++)
                {
                    gc();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    long start = Stopwatch.GetTimestamp();
                    string result = pureRuns[c](pureIters[c]);
                    long ticks = Stopwatch.GetTimestamp() - start;
                    pureNs[c, r] = ticks * (1e9 / Stopwatch.Frequency) / pureIters[c];
                    pureCheck[c] = result;
                    yield return null;
                }
            }
            for (int c = 0; c < pureNames.Count; c++)
            {
                var row = new double[PureRuns];
                var parts = new string[PureRuns];
                for (int r = 0; r < PureRuns; r++) { row[r] = pureNs[c, r]; parts[r] = Num(Math.Round(pureNs[c, r], 2)); }
                Array.Sort(row);
                string expected = null;
                for (int k = 0; k < Pure.GetLength(0); k++) if (Pure[k, 0] == pureNames[c]) expected = Pure[k, 3];
                log("XLUA_VSBENCH {\"name\": " + Json(pureNames[c]) + ", \"library\": " + Json(library) + ", \"mode\": " + Json(mode)
                    + ", \"min\": " + Num(Math.Round(row[0], 2)) + ", \"median\": " + Num(Math.Round(row[PureRuns / 2], 2))
                    + ", \"runs\": [" + string.Join(", ", parts) + "], \"iterations\": " + pureIters[c]
                    + ", \"check\": " + Json(pureCheck[c]) + ", \"check_ok\": " + (string.IsNullOrEmpty(expected) ? "null" : expected == pureCheck[c] ? "true" : "false") + "}");
            }

            // The allocation pass, after the timing: each case once more with fewer operations and both collectors
            // stopped. Managed bytes: GC.GetTotalMemory(false) before and after (IL2CPP has no per-thread counter;
            // the managed heap only grows while its collector is off, rounded up to Boehm's size classes). Lua
            // bytes: collectgarbage('count') before and after with Lua's collector stopped (a native Lua's own heap;
            // MoonSharp's 'count' is the managed heap, so its column repeats the managed one).
            Num0 heapKb = null;
            Void0 luaStop = null, luaRestart = null;
            try { heapKb = lua.Get<Num0>("heapkb"); luaStop = lua.Get<Void0>("gcstop"); luaRestart = lua.Get<Void0>("gcrestart"); }
            catch (Exception e) { log("XLUA_VSBENCH_SKIP {\"name\": \"alloc\", \"error\": " + Json(e.GetType().Name + ": " + e.Message) + "}"); }
            if (heapKb != null)
            {
                for (int c = 0; c < names.Count + pureNames.Count; c++)
                {
                    bool pure = c >= names.Count;
                    string name = pure ? pureNames[c - names.Count] : names[c];
                    int ops = pure ? Math.Max(10, pureIters[c - names.Count] / 100) : AllocOps;
                    gc();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    string error = null;
                    long managed = 0;
                    double luaKb = 0;
                    int gcs = 0;
#if ENABLE_IL2CPP && !UNITY_EDITOR
                    UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
#endif
                    try
                    {
                        luaStop();
                        gcs = GC.CollectionCount(0);
                        double kb0 = heapKb();
                        long m0 = GC.GetTotalMemory(false);
                        if (pure) pureRuns[c - names.Count](ops);
                        else runs[c](ops / perCall[c]);
                        long m1 = GC.GetTotalMemory(false);
                        double kb1 = heapKb();
                        gcs = GC.CollectionCount(0) - gcs;
                        managed = m1 - m0;
                        luaKb = kb1 - kb0;
                    }
                    catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
#if ENABLE_IL2CPP && !UNITY_EDITOR
                    UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
#endif
                    try { luaRestart(); } catch (Exception) { }
                    log("XLUA_VSBENCH_ALLOC {\"name\": " + Json(name) + ", \"library\": " + Json(library) + ", \"mode\": " + Json(mode)
                        + ", \"ops\": " + ops + ", \"managed_bytes_per_op\": " + Num(Math.Round((double)managed / ops, 1))
                        + ", \"lua_bytes_per_op\": " + Num(Math.Round(luaKb * 1024 / ops, 1)) + ", \"gcs\": " + gcs
                        + ", \"error\": " + Json(error) + "}");
                    yield return null;
                }
            }
            string check;
            try { check = lua.Get<Str0>("check")(); } catch (Exception e) { check = "error " + e.Message; }
            var o = TestTargets.Instance;
            log("XLUA_VSBENCH_CHECK {\"lua\": " + Json(check) + ", \"csharp\": " + Json("field=" + o.Field_Int + " prop=" + o.Prop_Int
                + " static=" + TestTargets.StaticField_Int + " v3=" + o.Prop_Vector3 + " str=" + o.Prop_String) + ", \"cs2lua\": " + Json(Cs2LuaValues(lua)) + "}");
            log("XLUA_VSBENCH_DONE " + library + "/" + mode);
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "vsbench-result.txt"), string.Join("\n", lines.ToArray()) + "\n", Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.Log("XLUA_VSBENCH_FILE_ERROR " + e.Message);
            }
            runs.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { lua.Dispose(); } catch (Exception e) { Debug.Log("XLUA_VSBENCH_DISPOSE " + e.Message); }
        }
    }
}
