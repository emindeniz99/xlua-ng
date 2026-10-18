// C# `in` parameters (readonly by reference, IsReadOnlyAttribute) must look
// like by-value inputs from Lua: no extra return value and nothing written
// back. [In] ref without the attribute is a normal `ref` and keeps its
// write-back. Lua cases: StreamingAssets/luaCallCsInParams.lua.
using System.Runtime.InteropServices;
using XLua;

public struct InParamPoint
{
    public int X;
    public int Y;

    public InParamPoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

// [In] ref is still ref: generated in the generated-code suites, emitted in
// the reflection suite.
[LuaCallCSharp]
public class InRefGen
{
    public static int Bump([In] ref InParamPoint p)
    {
        p.X += 1;
        return p.X + p.Y;
    }
}

// The same through reflection in the generated-code suites.
public class InRefReflect
{
    public static int Bump([In] ref InParamPoint p)
    {
        p.X += 1;
        return p.X + p.Y;
    }
}

[CSharpCallLua]
public delegate int InRefCallback([In] ref int counter);

[LuaCallCSharp]
public static class InRefCaller
{
    // "result,counter": Lua's second result is written back into counter.
    public static string Call(InRefCallback callback)
    {
        int counter = 10;
        int result = callback(ref counter);
        return result + "," + counter;
    }
}

// A Lua table as an interface whose indexer has an `in` key: the bridge
// passes the key to get_Item and set_Item and expects no value back for it.
// An `in` parameter of an interface member carries modreq(InAttribute),
// which mcs cannot declare, so the key is by value here (and in Unity); the
// host Roslyn builds compile it with `in` (Test/Native/roslyn_fixtures.py:
// coverage_cs_dotnet.py, run_managed.py --source-gen).
[CSharpCallLua]
public interface IInKeyIndexer
{
    int this[/*in*/ int key] { get; set; }
}

[LuaCallCSharp]
public static class InKeyIndexerCaller
{
    public static string Use(IInKeyIndexer x)
    {
        int key = 4;
        x[key] = 40;
        return x[key] + "," + key;
    }

    // "in" where the key was compiled as `in`, else "value".
    public static string KeyKind()
    {
        var key = typeof(IInKeyIndexer).GetProperty("Item").GetGetMethod().GetParameters()[0];
        return key.ParameterType.IsByRef ? "in" : "value";
    }
}

#if XLUA_GENERAL
// Real `in` parameters. Mono's mcs compiles the `in` keyword as a by-value
// parameter, so these spell out the metadata Roslyn emits for `in` on a
// non-virtual method: [In] ref with IsReadOnlyAttribute (Roslyn rejects the
// attribute in source, hence XLUA_GENERAL only).
// mcs sees these as `ref`, so it cannot compile generated wrappers that pass
// plain arguments to them, nor declare the `in` bridges that a delegate or
// interface would need. They are left out of LuaCallCSharp and use emitted
// wrappers in the reflection suite and reflection in the generated-code
// suites; Unity's editor tests cover Roslyn's `in` in generated code and in
// Lua callbacks (delegates and interfaces, including their modreq).
namespace InParams
{
    using System.Runtime.CompilerServices;

    public class Real
    {
        public int Offset;

        public Real([In, IsReadOnly] ref int offset)
        {
            Offset = offset;
        }

        // Only an `in` overload, so Lua must reach it.
        public static int Sum([In, IsReadOnly] ref InParamPoint p)
        {
            return p.X + p.Y;
        }

        // An `in` parameter between real ref and out parameters.
        public static int Scale([In, IsReadOnly] ref int factor, ref int value, out int old)
        {
            old = value;
            value = value * factor;
            return factor;
        }

        // A by-value and `in` pair, like Unity 6.3's Vector3.Distance.
        public static int Product(InParamPoint p)
        {
            return p.X * p.Y;
        }

        public static int Product([In, IsReadOnly] ref InParamPoint p)
        {
            return p.X * p.Y;
        }

        public int Add([In, IsReadOnly] ref InParamPoint p)
        {
            return p.X + p.Y + Offset;
        }
    }
}

// C# 12 `ref readonly` parameters: Roslyn emits [In] ref with
// RequiresLocationAttribute (embedded in the assembly when the BCL lacks it,
// as Mono 6.14's does), spelled out here for mcs. Lua treats them like `in`.
// Unlike `in`, generated wrappers pass them with `ref`, which mcs compiles,
// so Gen is in LuaCallCSharp; Reflect uses emitted wrappers in the reflection
// suite and reflection in the generated-code suites. A delegate or interface
// would need an `in` bridge, which mcs cannot declare; Unity's editor tests
// (prepare.py --csharp12) cover those.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter)]
    sealed class RequiresLocationAttribute : Attribute
    {
    }
}

namespace RefReadonlyParams
{
    using System.Runtime.CompilerServices;

    [LuaCallCSharp]
    public class Gen
    {
        public int Offset;

        public Gen([In, RequiresLocation] ref int offset)
        {
            Offset = offset;
        }

        public static int Sum([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X + p.Y;
        }

        public static int Scale([In, RequiresLocation] ref int factor, ref int value, out int old)
        {
            old = value;
            value = value * factor;
            return factor;
        }

        public static int Product(InParamPoint p)
        {
            return p.X * p.Y;
        }

        public static int Product([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X * p.Y;
        }

        public int Add([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X + p.Y + Offset;
        }
    }

    public class Reflect
    {
        public int Offset;

        public Reflect([In, RequiresLocation] ref int offset)
        {
            Offset = offset;
        }

        public static int Sum([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X + p.Y;
        }

        public static int Scale([In, RequiresLocation] ref int factor, ref int value, out int old)
        {
            old = value;
            value = value * factor;
            return factor;
        }

        public static int Product(InParamPoint p)
        {
            return p.X * p.Y;
        }

        public static int Product([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X * p.Y;
        }

        public int Add([In, RequiresLocation] ref InParamPoint p)
        {
            return p.X + p.Y + Offset;
        }
    }
}
#endif
