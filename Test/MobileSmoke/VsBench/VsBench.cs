// The library-neutral part of the cross-library player bench (vs_bench.py, docs/research/benchmark-vs-puerts-zlua.md):
// a subset of zlua-benchmark's case list, the C# targets those cases call, one Lua module with every case, and the
// timing loop. An adapter per library (VsBenchXLua.cs, VsBenchPuerts.cs, VsBenchZLua.cs) loads the module and
// turns its functions into C# delegates; nothing here depends on a Lua library.
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
// cases. Output lines: XLUA_VSBENCH_META, XLUA_VSBENCH (one per case: ns per operation of each round, the Lua or C#
// loop included), XLUA_VSBENCH_SKIP (a case the library cannot run, with the error), XLUA_VSBENCH_CHECK (values
// the cases left behind, so a mapping that did nothing shows) and XLUA_VSBENCH_DONE; the same lines go to
// vsbench-result.txt in persistentDataPath.
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
        // SampleClass first: static initializers run in textual order, and Instance's
        // Prop_Class reads it. Until 2026-10-29 Instance came first, so Prop_Class was
        // null and lua2cs.instance.prop.get.class pushed nil in every library.
        public static readonly TestClass SampleClass = new TestClass(1, 2, 3);
        public static readonly TestTargets Instance = new TestTargets();

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

        public static string Module(string prelude)
        {
            var sb = new StringBuilder();
            sb.Append("local M = {}\n").Append(prelude).Append('\n');
            sb.Append("M.version = function() return _VERSION end\n");
            sb.Append("M.gc = function() collectgarbage('collect') collectgarbage('collect') end\n");
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
            string check;
            try { check = lua.Get<Str0>("check")(); } catch (Exception e) { check = "error " + e.Message; }
            var o = TestTargets.Instance;
            log("XLUA_VSBENCH_CHECK {\"lua\": " + Json(check) + ", \"csharp\": " + Json("field=" + o.Field_Int + " prop=" + o.Prop_Int
                + " static=" + TestTargets.StaticField_Int + " v3=" + o.Prop_Vector3 + " str=" + o.Prop_String
                + " class=" + (o.Prop_Class == TestTargets.SampleClass)) + ", \"cs2lua\": " + Json(Cs2LuaValues(lua)) + "}");
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
