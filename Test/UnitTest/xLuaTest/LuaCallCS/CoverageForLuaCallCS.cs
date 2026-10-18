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

// Configuration only in the assembly-level forms, with no list that says the
// same (CaseAssemblyLevelConfig): what a project has once the migration tool
// has converted its lists and they are deleted. The emitted bridges and the
// reflection wrappers read these forms too.
[assembly: CSharpCallLua(typeof(Coverage.IAssemblyConfigured))]
[assembly: LuaCallCSharp(typeof(Coverage.AssemblyConfiguredExtensions))]
[assembly: BlackList(typeof(Coverage.AssemblyConfiguredTarget), "Hidden")]

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

    // An array type in a [LuaCallCSharp] list. Generate Code wrote a wrapper
    // for it that did not compile ("new int[](_)" in __CreateInstance); it
    // now leaves array types out with a warning, as the source generator does
    // (XLSG004). Lua reaches every array through the common array metatable,
    // so nothing changes at run time (CaseListedArrayType).
    public static class ArrayTypeConfig
    {
        [LuaCallCSharp]
        public static List<Type> LuaCallCSharp = new List<Type>()
        {
            typeof(int[]),
        };
    }

    [LuaCallCSharp]
    public static class ListedArrays
    {
        public static int[] Make(int n)
        {
            return new int[n];
        }

        public static int Sum(int[] values)
        {
            int sum = 0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum;
        }
    }

    // Members left out by a [BlackList] list: the generator and the emitted
    // wrappers must agree.
    [LuaCallCSharp]
    public class BlackListed
    {
        public int Visible = 1;
        public int Hidden = 2;
        public int HiddenMethod() { return 3; }
        public string Overload(int x) { return "int"; }
        public string Overload(string x) { return "string"; }
    }

    public static class BlackListConfig
    {
        [BlackList]
        public static List<List<string>> BlackList = new List<List<string>>()
        {
            new List<string>() { "Coverage.BlackListed", "Hidden" },
            new List<string>() { "Coverage.BlackListed", "HiddenMethod" },
            new List<string>() { "Coverage.BlackListed", "Overload", "System.String" },
        };

        // Members of Filtered and FilteredReflected hidden by a filter: the
        // generator, the emitted wrappers and the reflection wrappers must
        // all leave them out.
        [BlackList]
        public static Func<MemberInfo, bool> Filter = member =>
        {
            if (member.DeclaringType != typeof(Filtered) && member.DeclaringType != typeof(FilteredReflected)
                && member.DeclaringType != typeof(MemberExtensions))
            {
                return false;
            }
            if (member.Name.StartsWith("Hidden")) return true;
            var method = member as MethodBase;
            if (method == null || method.GetParameters().Length != 1) return false;
            var parameter = method.GetParameters()[0].ParameterType;
            return (method.Name == "Pick" && parameter == typeof(string))
                || (method.IsConstructor && parameter == typeof(int));
        };
    }

    // The same members twice: Filtered gets generated wrappers in the
    // generated-code suites, FilteredReflected (no [LuaCallCSharp]) uses
    // reflection there; both use emitted wrappers in the reflection suite.
    [LuaCallCSharp]
    public class Filtered
    {
        public string Made = "default";
        public Filtered() { }
        public Filtered(string s) { Made = "string"; }
        public Filtered(int x) { Made = "int"; }
        public int Visible = 1;
        public int HiddenField = 2;
        public int HiddenProperty { get { return 3; } set { } }
        public int HiddenMethod() { return 4; }
        public static int HiddenStatic() { return 5; }
        public static int HiddenStaticField = 6;
        public event Action HiddenEvent { add { } remove { } }
        public string Pick(int x) { return "int"; }
        public string Pick(string s) { return "string"; }
    }

    public class FilteredReflected
    {
        public string Made = "default";
        public FilteredReflected() { }
        public FilteredReflected(string s) { Made = "string"; }
        public FilteredReflected(int x) { Made = "int"; }
        public int Visible = 1;
        public int HiddenField = 2;
        public int HiddenProperty { get { return 3; } set { } }
        public int HiddenMethod() { return 4; }
        public static int HiddenStatic() { return 5; }
        public static int HiddenStaticField = 6;
        public event Action HiddenEvent { add { } remove { } }
        public string Pick(int x) { return "int"; }
        public string Pick(string s) { return "string"; }
    }

    // [DoNotGen] methods that share a name with an extension method: the
    // lazy member of generated code must also see the extension method, as
    // the emitted and reflection wrappers do.
    [LuaCallCSharp]
    public class LazyExtended
    {
        public string Method(int x) { return "own:" + x; }
        public static string StaticName(int x) { return "static:" + x; }
        public string Generated(int x) { return "own:" + x; }
    }

    public static class LazyExtendedConfig
    {
        [DoNotGen]
        public static Dictionary<Type, List<string>> DoNotGen = new Dictionary<Type, List<string>>()
        {
            { typeof(LazyExtended), new List<string>() { "Method", "StaticName", "ExtensionOnly" } },
        };
    }

    // A public method and an extension method of the same name, on a type
    // that xlua.private_accessible opens up.
    public class PrivateExtended
    {
        int secret = 7;
        public string Method(int x) { return "own:" + x; }
        int Secret() { return secret; }
    }

    [LuaCallCSharp]
    public static class MemberExtensions
    {
        public static string Method(this LazyExtended o, string s) { return "ext:" + s; }
        public static string StaticName(this LazyExtended o, string s) { return "ext:" + s; }
        public static string Generated(this LazyExtended o, string s) { return "ext:" + s; }
        public static string ExtensionOnly(this LazyExtended o) { return "ext"; }
        public static int HiddenExtension(this Filtered o) { return 7; }
        public static int HiddenExtension(this FilteredReflected o) { return 7; }
        public static string Method(this PrivateExtended o, string s) { return "ext:" + s; }
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

        // Two generic methods with one name, for the index of xlua.get_generic_method.
        public static string Twin<T>(T x)
        {
            return "one";
        }

        public static string Twin<T>(T x, T y)
        {
            return "two";
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

    [Flags]
    public enum BigFlags : ulong
    {
        Low = 1,
        High = 0x8000000000000000,
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

        // An int: a long result is an int64 userdata on LuaJIT.
        public static int PointerValue(IntPtr p)
        {
            return p.ToInt32();
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
        public static PackedPoint[] PackedPoints() { return new PackedPoint[] { new PackedPoint(1, 2), new PackedPoint(3, 4) }; }

        // A parameter of type DelegateBridge takes the bridge of a Lua function itself.
        public static bool IsBridge(DelegateBridge bridge)
        {
            return bridge != null;
        }

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

    // Arrays of a GCOptimize struct go through the generated array helpers
    // (InternalGlobals.genTryArrayGetPtr) in the generated-code suites.
    [GCOptimize]
    [LuaCallCSharp]
    public struct PackedPoint
    {
        public int X;
        public int Y;

        public PackedPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    // Private members for xlua.access.
    public class Access
    {
        private int secret = 1;
        private static string staticSecret = "s";
        private int Prop { get; set; }
        private static int StaticProp { get; set; }
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
            return XLua.LuaDLL.Bridge.lua_gettop((IntPtr)property.GetValue(env, null));
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

                // Errors raised by metamethods reach C# as exceptions and leave the stack as it was.
                env.DoString("bad = setmetatable({}, { __index = function() error('no read') end, __newindex = function() error('no write') end })");
                LuaTable bad = env.Global.Get<LuaTable>("bad");
                expect(throws(() => bad.Get<string, int>("x")).Contains("no read"), "Get with a failing __index");
                expect(throws(() => bad.ContainsKey("x")).Contains("no read"), "ContainsKey with a failing __index");
                expect(throws(() => bad.Set("x", 1)).Contains("no write"), "Set with a failing __newindex");
                expect(throws(() => bad.GetInPath<int>("x")).Contains("no read"), "GetInPath with a failing __index");
                expect(throws(() => bad.SetInPath("x", 1)).Contains("no write"), "SetInPath with a failing __newindex");
                expectEqual(0, top(env), "stack after the metamethod errors");
            });
        }

        // A nil read into a value type throws InvalidCastException and must
        // leave the Lua stack as it was.
        public static string NilToValueTypeKeepsStack()
        {
            string messages = "";
            fresh(env =>
            {
                env.DoString("t = { inner = {} }");
                LuaTable t = env.Global.Get<LuaTable>("t");
                int before = top(env);
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        t.Get<string, int>("missing");
                        throw new Exception("Get<string, int> of nil must throw");
                    }
                    catch (InvalidCastException e)
                    {
                        if (i == 0) messages += e.Message + ";";
                    }
                    try
                    {
                        t.GetInPath<double>("inner.missing");
                        throw new Exception("GetInPath<double> of nil must throw");
                    }
                    catch (InvalidCastException e)
                    {
                        if (i == 0) messages += e.Message;
                    }
                }
                expectEqual(before, top(env), "Lua stack top after the exceptions");
            });
            return messages;
        }

        // A Cast that throws must leave the Lua stack as it was.
        public static void FailedCastKeepsStack()
        {
            fresh(env =>
            {
                env.DoString("t = {} function f() end");
                LuaTable t = env.Global.Get<LuaTable>("t");
                LuaFunction f = env.Global.Get<LuaFunction>("f");
                int before = top(env);
                for (int i = 0; i < 3; i++)
                {
                    expect(throws(() => f.Cast<NoBridgeValueParam>()).StartsWith("InvalidCastException"), "LuaFunction.Cast to a delegate without a bridge");
                    expect(throws(() => t.Cast<IDisposable>()).StartsWith("InvalidCastException"), "LuaTable.Cast to an interface not in CSharpCallLua");
                }
                expectEqual(before, top(env), "Lua stack top after the exceptions");
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

        // LuaFunction.Call with result types, and Lua tables read as
        // collections of the narrow types (the lenient casters).
        public static void NarrowConversions()
        {
            fresh(env =>
            {
                env.DoString("function many() return 65, -5, -300, 65000, 4000000000, 7 end function none() end");
                LuaFunction many = env.Global.Get<LuaFunction>("many");
                object[] r = many.Call(null, new Type[] { typeof(char), typeof(sbyte), typeof(short), typeof(ushort), typeof(uint), typeof(ulong) });
                expectEqual('A', r[0], "char result");
                expectEqual((sbyte)-5, r[1], "sbyte result");
                expectEqual((short)-300, r[2], "short result");
                expectEqual((ushort)65000, r[3], "ushort result");
                expectEqual(4000000000u, r[4], "uint result");
                expectEqual(7ul, r[5], "ulong result");
                // typeof(void) first: the types of the results start at the second entry.
                r = many.Call(new object[0], new Type[] { typeof(void), typeof(char), typeof(sbyte), typeof(short), typeof(ushort), typeof(uint), typeof(ulong) });
                expectEqual('A', r[0], "char result after void");
                expectEqual(null, env.Global.Get<LuaFunction>("none").Call(null, new Type[] { typeof(int) }), "no results");

                env.DoString("list = { 65, 66 } dict = { a = -5, b = 7 }");
                LuaTable list = env.Global.Get<LuaTable>("list");
                LuaTable dict = env.Global.Get<LuaTable>("dict");
                expectEqual("AB", new string(list.Cast<List<char>>().ToArray()), "List<char>");
                expectEqual((ushort)66, list.Cast<List<ushort>>()[1], "List<ushort>");
                expectEqual(65u, list.Cast<List<uint>>()[0], "List<uint>");
                expectEqual(66ul, list.Cast<List<ulong>>()[1], "List<ulong>");
                expectEqual((sbyte)-5, dict.Cast<Dictionary<string, sbyte>>()["a"], "Dictionary<string, sbyte>");
                expectEqual((short)7, dict.Cast<Dictionary<string, short>>()["b"], "Dictionary<string, short>");
                expectEqual(0, top(env), "stack after the conversions");
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

    public enum Allocation
    {
        None = 0,
        Temp = 2,
        Persistent = 4,
    }

    // Unity 6000.6 added PhysicsScene2D.GetRayIntersection(Ray, float,
    // int = -5, Allocator = ...) next to (Ray, float, int = -5). A wrapper
    // call that leaves the optional parameter out was ambiguous (CS0121) and
    // the generated code did not compile; the generators now write the
    // defaults out for such calls. Which of two such overloads a Lua call
    // with fewer arguments reaches depends on the overload order, so the
    // results show the defaults each overload received.
    [LuaCallCSharp]
    public class AmbiguousDefaults
    {
        public string Kind;

        public AmbiguousDefaults(string name, int size = 1)
        {
            Kind = "ctor:" + name + ":" + size;
        }

        public AmbiguousDefaults(string name, int size = 1, Allocation allocator = Allocation.Temp)
        {
            Kind = "ctor:" + name + ":" + size + ":" + allocator;
        }

        public string Ray(Point origin, float distance, int layerMask = -5)
        {
            return "hit:" + layerMask;
        }

        public string Ray(Point origin, float distance, int layerMask = -5, Allocation allocator = Allocation.Temp)
        {
            return "hit:" + layerMask + ":" + allocator;
        }

        // Every kind of constant the generators write out.
        public static string Mixed(int a, byte b = 3, sbyte sb = -2, short s = -300, ushort us = 65000, uint u = 4000000000,
            long l = long.MinValue, ulong ul = ulong.MaxValue, float f = 1.5f, double d = double.PositiveInfinity,
            decimal m = 2.5m, char c = 'x', bool flag = true, string text = "q\"\\\né", string none = null,
            Ref r = null, Point pt = default(Point), int? n = 7, Allocation e = Allocation.Persistent,
            Signed sg = Signed.Minus)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join("|", new string[] {
                a.ToString(inv), b.ToString(inv), sb.ToString(inv), s.ToString(inv), us.ToString(inv), u.ToString(inv),
                l.ToString(inv), ul.ToString(inv), f.ToString("R", inv), double.IsPositiveInfinity(d) ? "inf" : d.ToString("R", inv), m.ToString(inv),
                ((int)c).ToString(inv), flag ? "true" : "false", text, none ?? "null", r == null ? "null" : r.Name,
                pt.X + "," + pt.Y, n.HasValue ? n.Value.ToString(inv) : "null", e.ToString(), sg.ToString() });
        }

        public static string Mixed(int a, byte b = 3, sbyte sb = -2, short s = -300, ushort us = 65000, uint u = 4000000000,
            long l = long.MinValue, ulong ul = ulong.MaxValue, float f = 1.5f, double d = double.PositiveInfinity,
            decimal m = 2.5m, char c = 'x', bool flag = true, string text = "q\"\\\né", string none = null,
            Ref r = null, Point pt = default(Point), int? n = 7, Allocation e = Allocation.Persistent,
            Signed sg = Signed.Minus, bool extra = false)
        {
            return Mixed(a, b, sb, s, us, u, l, ul, f, d, m, c, flag, text, none, r, pt, n, e, sg) + "|extra=" + (extra ? "true" : "false");
        }
    }

    // Interface bridges: a Lua table used as a C# interface. The reflection
    // suite emits the bridge (CodeEmit.EmitInterfaceImpl), the generated-code
    // suites generate it (LuaInterfaceBridge.tpl.txt), the source generator
    // suite has its own. Only IBridgeShape is in CSharpCallLua; the members
    // of the interfaces it inherits are part of its bridge.
    public delegate void BridgeNotify(string what, int value);

    public interface IBridgeBase
    {
        int Id { get; }
        string Describe(string prefix);
    }

    public interface IBridgeMiddle : IBridgeBase
    {
        long Scale(long x);
    }

    [CSharpCallLua]
    public interface IBridgeShape : IBridgeMiddle
    {
        int Area();
        void Reset();
        string Join(string separator, params string[] parts);
        Ref Wrap(Ref r);
        Point Move(Point p, int dx);
        void Fail();

        string Name { get; set; }
        int ReadOnly { get; }
        int WriteOnly { set; }
        IBridgeShape Next { get; }

        int this[int i] { get; set; }
        string this[string key] { get; }

        event BridgeNotify Changed;
        event Action Ticked;

        void Split(int v, out int half, ref int acc);
        bool TryGet(string key, out string value);
        int Swap(ref string a, ref string b);
        void Fill(out Point p);
    }

    // Member names the emitted bridge got wrong before 2026-10-18: ordinary
    // methods named like accessors were left unimplemented and an indexer
    // renamed with [IndexerName] was taken for a property (the type failed
    // to load), and a property called Item was taken for an indexer. The
    // legacy generator left out methods called add_X or remove_X and failed
    // on both properties.
    [CSharpCallLua]
    public interface IBridgeNames
    {
        int get_Raw();
        void set_Raw(int v);
        int add_One(int v);
        int remove_One(int v);
        [System.Runtime.CompilerServices.IndexerName("At")]
        string this[int i] { get; }
        int Item { get; set; }
    }

    public static class Bridges
    {
        public static string Names(IBridgeNames s)
        {
            s.set_Raw(4);
            s.Item = 6;
            return s.get_Raw() + "," + s.add_One(1) + "," + s.remove_One(1) + "," + s[3] + "," + s.Item;
        }

        static int top(IBridgeShape s)
        {
            var env = (LuaEnv)typeof(LuaBase).GetField("luaEnv", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
            var property = typeof(LuaEnv).GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic);
            return XLua.LuaDLL.Lua.lua_gettop((IntPtr)property.GetValue(env, null));
        }

        // Methods, properties and indexers, from this interface and the ones
        // it inherits; also through a base interface reference.
        public static string Members(IBridgeShape s)
        {
            int before = top(s);
            IBridgeBase b = s;
            IBridgeMiddle m = s;
            var parts = new List<string>();
            parts.Add(s.Area().ToString());
            parts.Add(b.Describe("d:"));
            parts.Add(s.Describe("s:"));
            parts.Add(b.Id.ToString());
            parts.Add(m.Scale(1L << 40 | 3).ToString());
            parts.Add(s.Join("-", "a", "b", "c"));
            parts.Add(s.Join("-"));
            parts.Add(s.Wrap(new Ref("r")).Name);
            Point p = s.Move(new Point { X = 1, Y = 2 }, 3);
            parts.Add(p.X + ":" + p.Y);
            s.Name = "renamed";
            parts.Add(s.Name);
            parts.Add(s.ReadOnly.ToString());
            s.WriteOnly = 7;
            parts.Add(s.Next.Name + ":" + s.Next.Area());
            s[1] = 10;
            s[2] = 20;
            parts.Add((s[1] + s[2] + s[3]).ToString());
            parts.Add(s["k"]);
            try
            {
                s.Fail();
                parts.Add("no error");
            }
            catch (LuaException e)
            {
                parts.Add(e.Message.Contains("shape failed") ? "error" : "wrong error: " + e.Message);
            }
            if (top(s) != before) parts.Add("stack " + before + " -> " + top(s));
            return string.Join(",", parts.ToArray());
        }

        // out and ref parameters: Lua returns the result first, then each
        // out and ref value in order.
        public static string OutRef(IBridgeShape s)
        {
            int before = top(s);
            var parts = new List<string>();
            int half;
            int acc = 5;
            s.Split(9, out half, ref acc);
            parts.Add(half + ":" + acc);
            string value;
            parts.Add(s.TryGet("a", out value) + ":" + value);
            parts.Add(s.TryGet("zz", out value) + ":" + (value ?? "null"));
            string x = "x", y = "y";
            int n = s.Swap(ref x, ref y);
            parts.Add(n + ":" + x + y);
            Point p;
            s.Fill(out p);
            parts.Add(p.X + ":" + p.Y);
            if (top(s) != before) parts.Add("stack " + before + " -> " + top(s));
            return string.Join(",", parts.ToArray());
        }

        // Events: add_<name> and remove_<name> in Lua get the C# handler;
        // Reset in Lua raises them.
        public static string Events(IBridgeShape s)
        {
            var log = new List<string>();
            BridgeNotify changed = (what, value) => log.Add(what + "=" + value);
            Action ticked = () => log.Add("tick");
            s.Changed += changed;
            s.Ticked += ticked;
            s.Reset();
            s.Changed -= changed;
            s.Reset();
            s.Ticked -= ticked;
            s.Reset();
            return string.Join(",", log.ToArray());
        }

        // Several threads call Lua through one env's delegate bridge (IntFunc)
        // and interface bridge (IBridgeShape: a method, property and indexer
        // accessors, event add and remove) at the same time. The bridges lock
        // the env under THREAD_SAFE or HOTFIX_ENABLE; the emitted ones
        // (CodeEmit, the reflection suite) took no lock before 2026-10-19, and
        // the threads then shared one Lua stack: wrong results, Lua errors or
        // a crash. Without the defines the threads run one after another. The
        // env is a new one: the suite's env is locked by the Lua call that
        // runs this. Returns "<delegate kind>,<interface kind>;" (emitted or
        // generated) and the errors.
        const string ThreadsChunk = @"
            function bridge_threads_twice(x) return x * 2 end
            bridge_threads_shape = { Id = 0, Name = 'n', ReadOnly = 7, total = 0, store = {}, handlers = 0 }
            local shape = bridge_threads_shape
            function shape:Scale(x)
                local t = self.total
                self.total = t + x
                return x + 1
            end
            function shape:get_Item(k) return self.store[k] or 0 end
            function shape:set_Item(k, v) self.store[k] = v end
            function shape:add_Ticked(d) self.handlers = self.handlers + 1 end
            function shape:remove_Ticked(d) self.handlers = self.handlers - 1 end
        ";

        public static string Threads(int threads, int calls)
        {
            var env = new LuaEnv();
            try
            {
                env.DoString(ThreadsChunk);
                string r = threadsUse(env, threads, calls);
                if (r.Contains("did not finish"))
                {
                    // A thread still holds the env (a deadlock): Dispose would wait too.
                    env = null;
                    return r;
                }
                long want = 0;
                for (long v = 0; v < (long)threads * calls; v++) want += v;
                object[] after = env.DoString("return bridge_threads_shape.total, bridge_threads_shape.handlers");
                r += EnvCheck("Scale total", Convert.ToInt64(after[0]), want)
                    + EnvCheck("event handlers left", Convert.ToInt64(after[1]), 0);
                var property = typeof(LuaEnv).GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic);
                r += EnvCheck("stack after the threads", XLua.LuaDLL.Lua.lua_gettop((IntPtr)property.GetValue(env, null)), 0);
                return r;
            }
            finally
            {
                if (env != null) env.Dispose();
            }
        }

        static string EnvCheck(string what, long got, long want)
        {
            return got == want ? "" : what + ": " + got + " != " + want + "; ";
        }

        static string kind(object bridge)
        {
            return bridge.GetType().Assembly.IsDynamic ? "emitted" : "generated";
        }

        // The bridges live only in this frame and in shared, which is cleared
        // before it returns, so that LuaEnv.Dispose finds no C# callback left.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string threadsUse(LuaEnv env, int threads, int calls)
        {
            var shared = new object[] { env.Global.Get<IntFunc>("bridge_threads_twice"), env.Global.Get<IBridgeShape>("bridge_threads_shape") };
            string head = kind(((Delegate)shared[0]).Target) + "," + kind(shared[1]) + ";";
            var results = new string[threads];
            var workers = new System.Threading.Thread[threads];
            var go = new System.Threading.ManualResetEvent(false);
            for (int i = 0; i < threads; i++)
            {
                int k = i;
                workers[i] = new System.Threading.Thread(() =>
                {
                    var errors = new System.Text.StringBuilder();
                    try
                    {
                        var f = (IntFunc)shared[0];
                        var s = (IBridgeShape)shared[1];
                        Action handler = () => { };
                        go.WaitOne();
                        for (int j = 0; j < calls && errors.Length < 400; j++)
                        {
                            int v = k * calls + j;
                            int twice = f(v);
                            if (twice != 2 * v) errors.Append("f(" + v + ") = " + twice + "; ");
                            long scaled = s.Scale(v);
                            if (scaled != v + 1) errors.Append("Scale(" + v + ") = " + scaled + "; ");
                            s[k] = j;
                            int back = s[k];
                            if (back != j) errors.Append("s[" + k + "] = " + back + " != " + j + "; ");
                            int ro = s.ReadOnly;
                            if (ro != 7) errors.Append("ReadOnly = " + ro + "; ");
                            s.WriteOnly = v;
                            s.Ticked += handler;
                            s.Ticked -= handler;
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Append(e.GetType().Name + ": " + e.Message + "; ");
                    }
                    results[k] = errors.Length == 0 ? "" : "thread " + k + ": " + errors;
                });
                workers[i].IsBackground = true;
            }
            string hung = "";
#if THREAD_SAFE || HOTFIX_ENABLE
            for (int i = 0; i < threads; i++) workers[i].Start();
            go.Set();
            for (int i = 0; i < threads; i++)
            {
                if (!workers[i].Join(60000)) hung += "thread " + i + " did not finish in 60 s; ";
            }
#else
            go.Set();
            for (int i = 0; i < threads; i++)
            {
                workers[i].Start();
                if (!workers[i].Join(60000)) hung += "thread " + i + " did not finish in 60 s; ";
            }
#endif
            if (hung == "") ((IDisposable)shared[1]).Dispose();
            shared[0] = shared[1] = null;
            return head + string.Join("", results) + hung;
        }
    }

    // Types configured only by lists of several types. run_managed.py
    // --source-gen converts the lists with XLuaGenerate --migrate --group, so
    // the source generator build reads them as [assembly: X(typeof(A),
    // typeof(B))] (Test/Native/XLuaUnitTest.XLuaConfig.cs), and the binding
    // surfaces must still equal Generate Code's (CaseMultiTypeLists).
    public static class MultiTypeConfig
    {
        [LuaCallCSharp]
        public static List<Type> LuaCallCSharp = new List<Type>() { typeof(MultiListedA), typeof(MultiListedB) };

        [CSharpCallLua]
        public static List<Type> CSharpCallLua = new List<Type>() { typeof(MultiListedCallback), typeof(IMultiListed) };

        [GCOptimize]
        public static List<Type> GCOptimize = new List<Type>() { typeof(MultiListedVec), typeof(MultiListedPair) };

        [ReflectionUse]
        public static List<Type> ReflectionUse = new List<Type>() { typeof(MultiReflected), typeof(MultiReflectedToo) };
    }

    public class MultiListedA
    {
        public int Value;

        public MultiListedA(int value)
        {
            Value = value;
        }

        public int Twice()
        {
            return Value * 2;
        }
    }

    public delegate int MultiListedCallback(int n);

    public interface IMultiListed
    {
        int Answer(int n);
    }

    public struct MultiListedVec
    {
        public float x;
        public float y;
    }

    public struct MultiListedPair
    {
        public int a;
        public int b;
    }

    public static class MultiListedB
    {
        public static string Join(string s, int n)
        {
            return s + n;
        }

        public static int Call(MultiListedCallback f, int n)
        {
            return f(n);
        }

        public static int Ask(IMultiListed o, int n)
        {
            return o.Answer(n);
        }

        public static MultiListedVec MakeVec(float x, float y)
        {
            return new MultiListedVec { x = x, y = y };
        }

        public static float SumVec(MultiListedVec v)
        {
            return v.x + v.y;
        }

        public static MultiListedPair Swap(MultiListedPair p)
        {
            return new MultiListedPair { a = p.b, b = p.a };
        }
    }

    public class MultiReflected
    {
        public static string Name()
        {
            return "reflected";
        }
    }

    public class MultiReflectedToo
    {
        public int Field = 3;
    }

    // Configured only by the [assembly: ...] attributes at the top of this file.
    public interface IAssemblyConfigured
    {
        int Answer(int n);
    }

    public class AssemblyConfiguredTarget
    {
        public int Value = 1;

        public string Hidden()
        {
            return "hidden";
        }
    }

    public static class AssemblyConfiguredExtensions
    {
        public static int Plus(this AssemblyConfiguredTarget target, int n)
        {
            return target.Value + n;
        }

        public static int Ask(IAssemblyConfigured answer, int n)
        {
            return answer.Answer(n);
        }
    }
}
