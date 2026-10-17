// Tests for code the suite did not run before 2026-10-18 (Test/README.md,
// "C# coverage"). Lua cases: StreamingAssets/luaCallCsCoverage.lua.
//
// Types with [LuaCallCSharp] get generated wrappers in the generated-code
// suites and emitted ones in the reflection suite; the others use emitted
// wrappers in the reflection suite and reflection in the generated-code
// suites. The C# side of the API (LuaTable, LuaFunction, LuaEnv, ObjectPool)
// is checked by the static methods of CSharpApi, which throw on a failure.
using System;
using System.Collections.Generic;
using System.Reflection;
using XLua;

namespace Coverage
{
    public class Ref
    {
        public string Name;

        public Ref(string name)
        {
            Name = name;
        }
    }

    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    // Not in CSharpCallLua, and no generated or emitted bridge has their
    // signature: xLua builds them from DelegateBridge's generic Action and
    // Func methods (GenericDelegateBridge.cs), which is what IL2CPP builds do
    // for a delegate that was left out of CSharpCallLua.
    public delegate Ref NoBridgeFunc0();
    public delegate Ref NoBridgeFunc1(Ref a);
    public delegate Ref NoBridgeFunc2(Ref a, Ref b);
    public delegate Ref NoBridgeFunc3(Ref a, Ref b, Ref c);
    public delegate Ref NoBridgeFunc4(Ref a, Ref b, Ref c, Ref d);
    public delegate void NoBridgeAction1(Ref a);
    public delegate void NoBridgeAction2(Ref a, Ref b);
    public delegate void NoBridgeAction3(Ref a, Ref b, Ref c);
    public delegate void NoBridgeAction4(Ref a, Ref b, Ref c, Ref d);
    // A value-type result: the generic methods take it outside Unity only
    // (ObjectTranslator.getCreatorUsingGeneric, !XLUA_GENERAL).
    public delegate int NoBridgeValueResult(Ref a);
    // Neither has a generic method: a value-type parameter, five parameters.
    public delegate Ref NoBridgeValueParam(int a);
    public delegate Ref NoBridgeFive(Ref a, Ref b, Ref c, Ref d, Ref e);

    [LuaCallCSharp]
    public static class NoBridge
    {
        static Ref r(string name)
        {
            return new Ref(name);
        }

        static string name(Ref x)
        {
            return x == null ? "nil" : x.Name;
        }

        public static string Funcs(NoBridgeFunc0 f0, NoBridgeFunc1 f1, NoBridgeFunc2 f2, NoBridgeFunc3 f3, NoBridgeFunc4 f4)
        {
            return name(f0()) + "," + name(f1(r("a"))) + "," + name(f2(r("a"), r("b"))) + ","
                + name(f3(r("a"), r("b"), r("c"))) + "," + name(f4(r("a"), r("b"), r("c"), r("d")));
        }

        public static string Actions(NoBridgeAction1 a1, NoBridgeAction2 a2, NoBridgeAction3 a3, NoBridgeAction4 a4)
        {
            a1(r("a"));
            a2(r("a"), r("b"));
            a3(r("a"), r("b"), r("c"));
            a4(r("a"), r("b"), r("c"), r("d"));
            return "done";
        }

        public static int ValueResult(NoBridgeValueResult f)
        {
            return f(r("abc"));
        }

        public static string ValueParam(NoBridgeValueParam f)
        {
            return name(f(1));
        }

        public static string Five(NoBridgeFive f)
        {
            return name(f(r("a"), r("b"), r("c"), r("d"), r("e")));
        }

        // A Lua error inside the generic bridge reaches C# as a LuaException.
        public static string Throws(NoBridgeFunc1 f, NoBridgeAction1 a)
        {
            string result = "";
            try
            {
                f(r("x"));
            }
            catch (LuaException e)
            {
                result += e.Message.Contains("func failed") ? "func;" : e.Message;
            }
            try
            {
                a(r("x"));
            }
            catch (LuaException e)
            {
                result += e.Message.Contains("action failed") ? "action" : e.Message;
            }
            return result;
        }
    }

    // In the generated-code suites, the members listed in LazyConfig are
    // registered as lazy members (Utils.RegisterLazyFunc) and wrapped by
    // reflection on first use (Utils.LazyReflectionCall).
    [LuaCallCSharp]
    public class Lazy
    {
        public int Field = 1;
        public static int StaticField = 2;
        int prop = 3;
        static int staticProp = 4;
        public int Property { get { return prop; } set { prop = value; } }
        public static int StaticProperty { get { return staticProp; } set { staticProp = value; } }
        public int Method(int x) { return x + Field; }
        public static int StaticMethod(int x) { return x * 2; }
        public event Action Changed;
        public static event Action StaticChanged;
        public int Generated() { return 42; }

        public int Fire()
        {
            int n = 0;
            if (Changed != null) { Changed(); n++; }
            if (StaticChanged != null) { StaticChanged(); n++; }
            return n;
        }
    }

    public static class LazyConfig
    {
        [DoNotGen]
        public static Dictionary<Type, List<string>> DoNotGen = new Dictionary<Type, List<string>>()
        {
            { typeof(Lazy), new List<string>() { "Field", "StaticField", "Property", "StaticProperty",
                "Method", "StaticMethod", "Changed", "StaticChanged" } },
        };
    }

    // xlua.get_generic_method, and generic methods whose type parameters
    // are all constrained (Utils.MakeGenericMethodWithConstraints).
    public class Generics
    {
        public string Tag = "g";

        public static string Echo<T>(T x)
        {
            return typeof(T).Name + ":" + x;
        }

        public string Wrap<T>(T x)
        {
            return Tag + "<" + typeof(T).Name + ">" + x;
        }

        public static string Pair<T1, T2>(T1 a, T2 b)
        {
            return typeof(T1).Name + "," + typeof(T2).Name;
        }

        public static string Constrained<T>(T x) where T : Ref
        {
            return typeof(T).Name + ":" + x.Name;
        }

        public static int NotGeneric()
        {
            return 0;
        }
    }

    [LuaCallCSharp]
    public class GenGenerics
    {
        public static string Constrained<T>(T x) where T : Ref
        {
            return typeof(T).Name + ":" + x.Name;
        }
    }

    [Flags]
    public enum Flags
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    public enum Other
    {
        X = 1,
    }

    public enum Signed : long
    {
        Minus = -2,
        One = 1,
    }

    [LuaCallCSharp]
    public static class Probe
    {
        public static int Twice(int x)
        {
            return x * 2;
        }

        // The type of what Lua passed, after XLua.Cast.* unwrapping.
        public static string TypeName(object o)
        {
            return o == null ? "null" : o.GetType().Name + ":" + o;
        }

        public static string EnumValue(object o)
        {
            return o.GetType().Name + ":" + Convert.ToString(o);
        }

        public static IntPtr Pointer(int value)
        {
            return new IntPtr(value);
        }

        public static long PointerValue(IntPtr p)
        {
            return p.ToInt64();
        }

        public static string AssemblyName()
        {
            return typeof(Probe).Assembly.FullName;
        }

        public static object ReleaseTarget()
        {
            return new Ref("released");
        }

        public static float[] Floats() { return new float[] { 1.5f, 2.5f }; }
        public static double[] Doubles() { return new double[] { 0.25, 0.5 }; }
        public static bool[] Bools() { return new bool[] { true, false }; }
        public static long[] Longs() { return new long[] { long.MaxValue, -1 }; }
        public static ulong[] ULongs() { return new ulong[] { 1, ulong.MaxValue }; }
        public static sbyte[] SBytes() { return new sbyte[] { -128, 127 }; }
        public static short[] Shorts() { return new short[] { -32768, 32767 }; }
        public static ushort[] UShorts() { return new ushort[] { 0, 65535 }; }
        public static char[] Chars() { return new char[] { 'a', 'z' }; }
        public static uint[] UInts() { return new uint[] { 0, uint.MaxValue }; }
        public static IntPtr[] Pointers() { return new IntPtr[] { new IntPtr(1), new IntPtr(2) }; }
        public static decimal[] Decimals() { return new decimal[] { 1.25m, 2.5m }; }
        public static string[] Strings() { return new string[] { "x", "y" }; }

        // Tables passed for array parameters (ObjectCasters' array caster).
        public static string Join(float[] f, double[] d, bool[] b, long[] l, ulong[] u, sbyte[] sb, short[] s,
            ushort[] us, char[] c, uint[] ui, string[] str)
        {
            return f[0] + "|" + d[0] + "|" + b[0] + "|" + l[0] + "|" + u[0] + "|" + sb[0] + "|" + s[0] + "|"
                + us[0] + "|" + c[0] + "|" + ui[0] + "|" + str[0];
        }

        public static string Pick(IIndexed x)
        {
            x[2] = 20;
            int sum = x.Sum(1, 2, 3);
            int calls = 0;
            Action handler = () => calls++;
            x.Changed += handler;
            x.Changed -= handler;
            return x[2] + "," + x.Count + "," + sum;
        }

        public static string Narrow(NarrowResult f, ByteResult b, CharResult c, SByteResult sb, UShortResult us)
        {
            decimal d = 1.5m;
            string s = "in";
            Point p = new Point(1, 2);
            short r = f(ref d, ref s, ref p);
            return r + "|" + d + "|" + s + "|" + p.X + "," + p.Y + "|" + b() + "|" + c() + "|" + sb() + "|" + us();
        }

        public static int Variadic(ParamsCallback f)
        {
            return f("head", 1, 2, 3);
        }
    }

    // A Lua table as a C# interface with an indexer, an event and a params method.
    [CSharpCallLua]
    public interface IIndexed
    {
        int this[int i] { get; set; }
        event Action Changed;
        int Count { get; }
        int Sum(params int[] values);
    }

    // Lua results converted to the narrow integer types, and ref parameters
    // of types other than the primitive ones.
    [CSharpCallLua]
    public delegate short NarrowResult(ref decimal d, ref string s, ref Point p);
    [CSharpCallLua]
    public delegate byte ByteResult();
    [CSharpCallLua]
    public delegate char CharResult();
    [CSharpCallLua]
    public delegate sbyte SByteResult();
    [CSharpCallLua]
    public delegate ushort UShortResult();
    [CSharpCallLua]
    public delegate int IntFunc(int x);
    [CSharpCallLua]
    public delegate int ParamsCallback(string head, params object[] rest);

    // Overloads that tell the checkers apart (ObjectCheckers): each method
    // has a string overload, so a wrong check picks the other one.
    public static class Checks
    {
        public static string Table(LuaTable t) { return "table"; }
        public static string Table(string s) { return "string"; }
        public static string Function(LuaFunction f) { return "function"; }
        public static string Function(string s) { return "string"; }
        public static string Decimal(decimal d) { return "decimal:" + d; }
        public static string Decimal(string s) { return "string"; }
        public static string Pointer(IntPtr p) { return "pointer:" + p.ToInt64(); }
        public static string Pointer(string s) { return "string"; }
        public static string ULong(ulong u) { return "ulong:" + u; }
        public static string ULong(string s) { return "string"; }
    }

    // Default parameter values that the emitted wrappers load as literals.
    public static class Defaults
    {
        public static string All(int first, uint a = 7, float b = 1.5f, decimal c = 2.5m, Point p = default(Point),
            string s = null, char ch = 'x', ulong u = 9, double d = 0.25, Flags f = Flags.B)
        {
            return first + "|" + a + "|" + b + "|" + c + "|" + p.X + "|" + (s == null ? "null" : s) + "|" + ch
                + "|" + u + "|" + d + "|" + f;
        }
    }

    // An instance event of a struct, through the emitted event wrapper.
    public struct EventStruct
    {
        public int Value;
        public event Action Changed;

        public int Fire()
        {
            if (Changed != null)
            {
                Changed();
                return 1;
            }
            return 0;
        }
    }

    public class AliasBase
    {
        public int Base = 1;

        public static AliasBase MakeDerived()
        {
            return new AliasDerived();
        }
    }

    public class AliasDerived : AliasBase
    {
        public int Extra = 2;
    }

    public static class CSharpApi
    {
        // Runs check with a new LuaEnv and disposes it, as Upstream.StackRoom.Fresh does.
        static void fresh(Action<LuaEnv> check)
        {
            var env = new LuaEnv();
            try
            {
                check(env);
            }
            finally
            {
                env.FullGc();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                env.Tick();
                env.Dispose();
            }
        }

        static void expect(bool ok, string what)
        {
            if (!ok)
            {
                throw new Exception("check failed: " + what);
            }
        }

        static void expectEqual(object expected, object actual, string what)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(what + ": expected " + expected + " (" + (expected == null ? "null" : expected.GetType().Name)
                    + "), got " + actual + " (" + (actual == null ? "null" : actual.GetType().Name) + ")");
            }
        }

        // The Lua stack top of the env's main thread.
        static int top(LuaEnv env)
        {
            var property = typeof(LuaEnv).GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic);
            return XLua.LuaDLL.Lua.lua_gettop((IntPtr)property.GetValue(env, null));
        }

        static string throws(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
            throw new Exception("expected an exception");
        }

        public static void LuaTableApi()
        {
            fresh(env =>
            {
                env.DoString("t = { 10, 20, 30, a = 'x', b = 'y', [true] = 1 }");
                LuaTable t = env.Global.Get<LuaTable>("t");
                expectEqual(3, t.Length, "Length");
                expect(t.ContainsKey("a"), "ContainsKey a");
                expect(t.ContainsKey(2), "ContainsKey 2");
                expect(!t.ContainsKey("missing"), "ContainsKey missing");
                expectEqual("x", t.Get<string, string>("a"), "Get<TKey, TValue>");
                expectEqual("y", t.Get<string>("b"), "Get<TValue>(string)");
                expectEqual(20, t.Get<int, int>(2), "Get by int key");
#pragma warning disable 618
                expectEqual("x", t.Get<string>((object)"a"), "obsolete Get<T>(object)");
                expectEqual("x", t["a"], "obsolete this[string]");
                t["c"] = "z";
                expectEqual("z", t.Get<string, string>("c"), "obsolete this[string] setter");
                t[(object)4] = 40L;
                expectEqual(40L, Convert.ToInt64(t[(object)4]), "obsolete this[object]");
#pragma warning restore 618

                var strings = new List<string>();
                t.ForEach<string, string>((k, v) => strings.Add(k + "=" + v));
                strings.Sort(StringComparer.Ordinal);
                expectEqual("a=x,b=y,c=z", string.Join(",", strings.ToArray()), "ForEach<string, string>");
                long sum = 0;
                t.ForEach<long, long>((k, v) => sum += k * v);
                expectEqual(1 * 10 + 2 * 20 + 3 * 30 + 4 * 40L, sum, "ForEach<long, long>");

#if !THREAD_SAFE && !HOTFIX_ENABLE
                int keys = 0;
                foreach (var key in t.GetKeys())
                {
                    keys++;
                }
                expectEqual(8, keys, "GetKeys");
                var numbers = new List<long>();
                foreach (var key in t.GetKeys<long>())
                {
                    numbers.Add(key);
                }
                numbers.Sort();
                expectEqual("1,2,3,4", string.Join(",", numbers.ConvertAll(n => n.ToString()).ToArray()), "GetKeys<long>");
                // Leaving the loop early restores the stack.
                int before = top(env);
                foreach (var key in t.GetKeys())
                {
                    break;
                }
                expectEqual(before, top(env), "GetKeys break");
#endif

                expect(t.ToString().StartsWith("table :"), "ToString");
                env.DoString("meta = { __index = function(_, k) return 'meta:' .. k end }");
                t.SetMetaTable(env.Global.Get<LuaTable>("meta"));
                expectEqual("meta:q", t.Get<string, string>("q"), "SetMetaTable");
                expect(t.ContainsKey("q"), "ContainsKey goes through __index");

                env.DoString("point = { X = 3, Y = 4 } list = { 5, 6 } dict = { k = 7 }");
                Point p = env.Global.Get<LuaTable>("point").Cast<Point>();
                expectEqual(7, p.X + p.Y, "Cast<Point>");
                List<int> list = env.Global.Get<LuaTable>("list").Cast<List<int>>();
                expectEqual(11, list[0] + list[1], "Cast<List<int>>");
                Dictionary<string, int> dict = env.Global.Get<LuaTable>("dict").Cast<Dictionary<string, int>>();
                expectEqual(7, dict["k"], "Cast<Dictionary>");
                expectEqual(0, top(env), "stack after LuaTable calls");
            });
        }

        // The messages name the type with TypeExtensions.GetFriendlyName.
        public static string FriendlyNames()
        {
            var names = new List<string>();
            fresh(env =>
            {
                LuaTable t = env.NewTable();
                Action<Action> add = a =>
                {
                    try
                    {
                        a();
                    }
                    catch (InvalidCastException e)
                    {
                        names.Add(e.Message.Replace("can not assign nil to ", ""));
                    }
                };
                add(() => t.GetInPath<int>("x"));
                add(() => t.GetInPath<short>("x"));
                add(() => t.GetInPath<byte>("x"));
                add(() => t.GetInPath<bool>("x"));
                add(() => t.GetInPath<long>("x"));
                add(() => t.GetInPath<float>("x"));
                add(() => t.GetInPath<double>("x"));
                add(() => t.GetInPath<decimal>("x"));
                add(() => t.GetInPath<KeyValuePair<int, string>>("x"));
                add(() => t.GetInPath<Point>("x"));
            });
            return string.Join(";", names.ToArray());
        }

        // The lenient casters and the checkers of the less common types,
        // through LuaTable reads (ObjectCasters, ObjectCheckers).
        public static void NarrowValues()
        {
            fresh(env =>
            {
                env.DoString("t = { c = 65, sb = -5, s = -300, us = 65000, ui = 4000000000, ul = 7, d = 1.5, big = 2^53, f = function() end }");
                LuaTable t = env.Global.Get<LuaTable>("t");
                expectEqual('A', t.Get<string, char>("c"), "char");
                expectEqual((sbyte)-5, t.Get<string, sbyte>("sb"), "sbyte");
                expectEqual((short)-300, t.Get<string, short>("s"), "short");
                expectEqual((ushort)65000, t.Get<string, ushort>("us"), "ushort");
                expectEqual(4000000000u, t.Get<string, uint>("ui"), "uint");
                expectEqual(7ul, t.Get<string, ulong>("ul"), "ulong");
                expectEqual(1.5m, t.Get<string, decimal>("d"), "decimal");
                t.Set("p", new IntPtr(1234));
                expectEqual(new IntPtr(1234), t.Get<string, IntPtr>("p"), "IntPtr");
                expect(t.Get<string, LuaFunction>("f") != null, "LuaFunction");
                expect(t.Get<string, LuaFunction>("c") == null, "LuaFunction of a number");

                env.DoString("keys = { [1] = 'a', [2] = 'b', x = 'c' }");
                LuaTable keys = env.Global.Get<LuaTable>("keys");
                int n = 0;
                keys.ForEach<ulong, string>((k, v) => n++);
                expectEqual(2, n, "ForEach<ulong, string>");
                n = 0;
                keys.ForEach<decimal, string>((k, v) => n++);
                expectEqual(2, n, "ForEach<decimal, string>");
                n = 0;
                keys.ForEach<IntPtr, string>((k, v) => n++);
                expectEqual(0, n, "ForEach<IntPtr, string>");
                n = 0;
                keys.ForEach<LuaTable, string>((k, v) => n++);
                expectEqual(0, n, "ForEach<LuaTable, string>");
                n = 0;
                keys.ForEach<LuaFunction, string>((k, v) => n++);
                expectEqual(0, n, "ForEach<LuaFunction, string>");
            });
        }

        public static void LuaFunctionApi()
        {
            fresh(env =>
            {
                env.DoString(@"
                    last = nil
                    function one(a) last = a end
                    function two(a, b) last = a .. b end
                    function inc(a) return a + 1 end
                    function add(a, b) return a + b end
                    function fail() error('boom') end");
                LuaTable g = env.Global;
                g.Get<LuaFunction>("one").Action("x");
                expectEqual("x", g.Get<string>("last"), "Action<T>");
                g.Get<LuaFunction>("two").Action("x", "y");
                expectEqual("xy", g.Get<string>("last"), "Action<T1, T2>");
                expectEqual(2, g.Get<LuaFunction>("inc").Func<int, int>(1), "Func<T, TResult>");
                expectEqual(5L, g.Get<LuaFunction>("add").Func<long, long, long>(2, 3), "Func<T1, T2, TResult>");
                LuaFunction fail = g.Get<LuaFunction>("fail");
                expect(throws(() => fail.Action(1)).Contains("boom"), "Action<T> error");
                expect(throws(() => fail.Action(1, 2)).Contains("boom"), "Action<T1, T2> error");
                expect(throws(() => fail.Func<int, int>(1)).Contains("boom"), "Func<T, TResult> error");
                expect(throws(() => fail.Func<int, int, int>(1, 2)).Contains("boom"), "Func<T1, T2, TResult> error");
                IntFunc cast = g.Get<LuaFunction>("inc").Cast<IntFunc>();
                expectEqual(8, cast(7), "Cast<IntFunc>");
                expect(throws(() => g.Get<LuaFunction>("inc").Cast<string>()).StartsWith("InvalidOperationException"), "Cast<not a delegate>");
                expect(fail.ToString().StartsWith("function :"), "ToString");
                cast = null;
                expectEqual(0, top(env), "stack after LuaFunction calls");
            });
        }

        public static void LuaBaseEquality()
        {
            fresh(env =>
            {
                env.DoString("t = {} u = {} function f() end");
                LuaTable t1 = env.Global.Get<LuaTable>("t");
                LuaTable t2 = env.Global.Get<LuaTable>("t");
                LuaTable u = env.Global.Get<LuaTable>("u");
                LuaFunction f = env.Global.Get<LuaFunction>("f");
                expect(t1.Equals(t2), "same table");
                expect(!t1.Equals(u), "other table");
                expect(!t1.Equals(f), "table and function");
                expect(!t1.Equals(null), "null");
                expect(t1.Equals(t1), "itself");
                expectEqual(t1.GetHashCode(), t2.GetHashCode(), "GetHashCode");
                expect(f.Equals(env.Global.Get<LuaFunction>("f")), "same function");
                var other = new LuaEnv();
                try
                {
                    other.DoString("t = {}");
                    expect(!t1.Equals(other.Global.Get<LuaTable>("t")), "table of another env");
                }
                finally
                {
                    other.Dispose();
                }
            });
        }

        public static void LuaEnvGcApi()
        {
            fresh(env =>
            {
                env.StopGc();
                object running = env.DoString("local ok, r = pcall(collectgarbage, 'isrunning') return ok and r")[0];
                expect(running == null || running.Equals(false), "StopGc stops the collector");
                int before = env.Memroy;
                env.DoString("garbage = {} for i = 1, 2000 do garbage[i] = { i } end garbage = nil");
                expect(env.Memroy > before, "Memroy grows with the collector stopped");
                env.RestartGc();
                running = env.DoString("local ok, r = pcall(collectgarbage, 'isrunning') return ok and r")[0];
                expect(running == null || running.Equals(true), "RestartGc restarts it");
                bool finished = false;
                for (int i = 0; i < 10000 && !finished; i++)
                {
                    finished = env.GcStep(100);
                }
                expect(finished, "GcStep finishes a cycle");
                env.DoString("function f() end");
                LuaFunction f = env.Global.Get<LuaFunction>("f");
                f.Dispose();
                // GC releases what Dispose queued; the env stays usable.
                env.GC();
                expectEqual(3L, Convert.ToInt64(env.DoString("return 1 + 2")[0]), "after GC");
            });
        }

        // LuaEnv.Alias: objects of AliasDerived use the metatable of AliasBase.
        public static void Alias()
        {
            fresh(env =>
            {
                env.Alias(typeof(AliasBase), "Coverage.AliasDerived");
                object[] r = env.DoString("local d = CS.Coverage.AliasBase.MakeDerived() return d.Base, d.Extra");
                expectEqual(1L, Convert.ToInt64(r[0]), "base member");
                expectEqual(null, r[1], "derived member hidden");
                expect(throws(() => env.Alias(typeof(AliasBase), "Coverage.NoSuchType")).StartsWith("ArgumentException"), "unknown alias");
            });
            fresh(env =>
            {
                object[] r = env.DoString("local d = CS.Coverage.AliasBase.MakeDerived() return d.Base, d.Extra");
                expectEqual(2L, Convert.ToInt64(r[1]), "without an alias");
            });
        }

        public static void ObjectPoolApi()
        {
            var pool = new ObjectPool();
            expectEqual(0, pool.Check(0, 10, x => true, new Dictionary<object, int>()), "Check of an empty pool");
            var objects = new List<object>();
            for (int i = 0; i < 600; i++)
            {
                objects.Add(new object());
                expectEqual(i, pool.Add(objects[i]), "Add past the first 512 slots");
            }
            expect(ReferenceEquals(objects[599], pool[599]), "indexer");
            expectEqual(null, pool[600], "indexer past the end");
            expectEqual(null, pool[-1], "indexer below 0");
            expectEqual(null, pool.Get(1000), "Get past the end");
            object slot;
            expect(!pool.TryGetValue(-1, out slot), "TryGetValue below 0");
            expect(ReferenceEquals(objects[3], pool.Remove(3)), "Remove");
            expectEqual(null, pool.Remove(3), "Remove a free slot");
            expectEqual(null, pool.Remove(700), "Remove past the end");
            expect(!pool.TryGetValue(3, out slot), "TryGetValue of a free slot");
            expectEqual(3, pool.Add("reused"), "Add reuses a free slot");
            expectEqual(null, pool.Replace(-1, "x"), "Replace below 0");
            expect(ReferenceEquals(objects[4], pool.Replace(4, "four")), "Replace");

            // Check drops the objects the checker rejects, and their reverse map entries.
            var reverse = new Dictionary<object, int>();
            reverse[objects[5]] = 5;
            reverse[objects[6]] = 99;
            int next = pool.Check(5, 3, x => x != objects[5] && x != objects[6], reverse);
            expectEqual(8, next, "Check returns the next position");
            expectEqual(null, pool[5], "Check cleared a rejected object");
            expect(!reverse.ContainsKey(objects[5]), "Check removed its reverse entry");
            expect(reverse.ContainsKey(objects[6]), "Check keeps an entry for another index");
            expectEqual(null, pool[6], "Check cleared the second rejected object");
            expectEqual(2, pool.Check(599, 3, x => true, reverse), "Check wraps around");

            pool.Clear();
            expectEqual(null, pool[0], "Clear");
            expectEqual(0, pool.Add("first"), "Add after Clear");
        }
    }
}
