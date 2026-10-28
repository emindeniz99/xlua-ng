// Fixtures of CaseNoReflection* (luaCallCs.lua): XLUA_NO_REFLECTION, which the
// minimal preset defines unless XLUA_REFLECTION is defined. The types without
// [LuaCallCSharp] have no generated bindings; with the define Lua reaches none
// of their members, and the Editor (XLUA_GENERAL here) emits no delegate or
// interface bridges.
using System;
using System.Collections.Generic;
using XLua;

namespace NoReflection
{
    // No bindings.
    public class Unbound
    {
        public int Field = 1;
        public static int StaticField = 2;

        public int Method()
        {
            return 3;
        }

        public static int StaticMethod()
        {
            return 4;
        }

        public class Nested
        {
            public int Inner = 5;
        }
    }

    // No bindings either; only CaseNoReflectionPrivateAccessible uses it, since
    // xlua.private_accessible makes it reachable for the rest of the env.
    public class Accessible
    {
        public int Field = 11;
#pragma warning disable 0414
        private int hidden = 12;
#pragma warning restore 0414
    }

    public enum UnboundKind
    {
        A = 1,
        B = 2,
    }

    // No [CSharpCallLua]: no generated bridge.
    public interface IUnbound
    {
        int Get();
    }

    // No [CSharpCallLua], and a signature no configured delegate shares.
    public delegate string UnboundDelegate(string text, double scale, long count);

    [LuaCallCSharp]
    public class BoundBase
    {
        public int BaseValue = 6;

        public int BaseMethod()
        {
            return 7;
        }
    }

    // No bindings of its own: the members of its bound base class stay reachable.
    public class UnboundDerived : BoundBase
    {
        public int Own = 8;
    }

    // Table conversions (CaseNoReflectionTableConversion). Without [GCOptimize]
    // the source generator writes no table converter, so only reflection fills
    // these from a Lua table.
    public class TableClass
    {
        public int A;
        public int B;
    }

    public struct TableStruct
    {
        public int A;
        public int B;
    }

    // [GCOptimize] class: the generator's table reader (ObjectTranslator.RegisterCaster).
    [GCOptimize]
    public class ConvertedClass
    {
        public int A;
        public int B;
    }

    // [GCOptimize] packable struct: the generator's reader, which takes tables
    // too (RegisterPushAndGetAndUpdate). [LuaCallCSharp] binds it, so that the
    // passes without reflection do not warn XLSG014.
    [GCOptimize]
    [LuaCallCSharp]
    public struct ConvertedStruct
    {
        public int A;
        public int B;
    }

    [LuaCallCSharp]
    public static class TableApi
    {
        public static TableClass MakeClass()
        {
            return new TableClass { A = 1, B = 2 };
        }

        public static int SumClass(TableClass c)
        {
            return c.A + c.B;
        }

        public static int SumStruct(TableStruct s)
        {
            return s.A + s.B;
        }

        public static int SumClasses(List<TableClass> list)
        {
            int sum = 0;
            foreach (var c in list) sum += c.A + c.B;
            return sum;
        }

        public static int GetClass(LuaTable t)
        {
            TableClass c = t.Get<TableClass>("v");
            return c.A + c.B;
        }

        public static int CastClass(LuaTable t)
        {
            TableClass c = t.Cast<TableClass>();
            return c.A + c.B;
        }

        // A delegate result (LuaFunction.Func, as the delegate bridges read one).
        public static int ClassResult(LuaFunction f)
        {
            TableClass c = f.Func<int, TableClass>(0);
            return c.A + c.B;
        }

        public static int SumConvertedClass(ConvertedClass c)
        {
            return c.A + c.B;
        }

        public static int SumConvertedStruct(ConvertedStruct s)
        {
            return s.A + s.B;
        }

        // Cast goes through ObjectTranslator.GetObject and the casters, not
        // through the typed readers that the generated wrappers call.
        public static int CastConvertedClass(LuaTable t)
        {
            ConvertedClass c = t.Cast<ConvertedClass>();
            return c.A + c.B;
        }

        public static int CastConvertedStruct(LuaTable t)
        {
            ConvertedStruct s = t.Cast<ConvertedStruct>();
            return s.A + s.B;
        }

        public static int SumConvertedStructs(List<ConvertedStruct> list)
        {
            int sum = 0;
            foreach (var s in list) sum += s.A + s.B;
            return sum;
        }
    }

    [LuaCallCSharp]
    public static class Api
    {
        public static Unbound MakeUnbound()
        {
            return new Unbound();
        }

        public static UnboundDerived MakeDerived()
        {
            return new UnboundDerived();
        }

        public static bool IsUnbound(object o)
        {
            return o is Unbound;
        }

        public static List<int> MakeList()
        {
            return new List<int> { 10, 20, 30 };
        }

        public static Dictionary<string, int> MakeDictionary()
        {
            return new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        }

        public static UnboundKind MakeKind()
        {
            return UnboundKind.B;
        }

        public static int KindValue(UnboundKind kind)
        {
            return (int)kind;
        }

        public static string NameOf(Type type)
        {
            return type.FullName;
        }

        public static int CallInterface(IUnbound target)
        {
            return target.Get();
        }

        public static string CallDelegate(UnboundDelegate d)
        {
            return d("x", 2.0, 3);
        }

        // Whether CodeEmit is compiled (the Editor and XLUA_GENERAL; never with XLUA_NO_REFLECTION).
        public static bool HasCodeEmit()
        {
            return typeof(LuaEnv).Assembly.GetType("XLua.CodeEmit") != null;
        }

        // Whether the reflection wrappers are compiled (ReflectionWrap); XLUA_NO_REFLECTION
        // leaves them out unless xlua.private_accessible exists.
        public static bool HasReflectionWrappers()
        {
            return typeof(Utils).GetMethod("ReflectionWrap") != null;
        }
    }
}
