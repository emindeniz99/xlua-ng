// C# `in` parameters (readonly by reference) must look like by-value inputs
// from Lua: no extra return value and nothing written back, in generated,
// emitted and reflection wrappers and in CSharpCallLua bridges.
//
// Mono's mcs compiles the `in` keyword as a plain by-value parameter, so these
// types use the other metadata form xLua treats as `in`: [In] ref without
// IsReadOnlyAttribute. Roslyn's `in` (IsReadOnlyAttribute, modreq on virtual
// methods) is covered by the Unity editor tests with Vector3.Distance.
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

// Generated in the generated-code suites, emitted in the reflection suite.
[LuaCallCSharp]
public class InParamsGen
{
    public int Offset;

    public InParamsGen([In] ref int offset)
    {
        Offset = offset;
    }

    // Only an `in` overload, so Lua must reach it.
    public static int Sum([In] ref InParamPoint p)
    {
        return p.X + p.Y;
    }

    // An `in` parameter between real ref and out parameters.
    public static int Scale([In] ref int factor, ref int value, out int old)
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

    public static int Product([In] ref InParamPoint p)
    {
        return p.X * p.Y;
    }

    public int Add([In] ref InParamPoint p)
    {
        return p.X + p.Y + Offset;
    }
}

// Not in LuaCallCSharp: reflection in the generated-code suites, emitted in the reflection suite.
public class InParamsReflect
{
    public int Offset;

    public InParamsReflect([In] ref int offset)
    {
        Offset = offset;
    }

    public static int Sum([In] ref InParamPoint p)
    {
        return p.X + p.Y;
    }

    public static int Scale([In] ref int factor, ref int value, out int old)
    {
        old = value;
        value = value * factor;
        return factor;
    }

    public static int Product(InParamPoint p)
    {
        return p.X * p.Y;
    }

    public static int Product([In] ref InParamPoint p)
    {
        return p.X * p.Y;
    }

    public int Add([In] ref InParamPoint p)
    {
        return p.X + p.Y + Offset;
    }
}

// Lua implements these; Lua returns the result and the new counter only.
[CSharpCallLua]
public delegate int InParamCallback([In] ref InParamPoint p, ref int counter);

[CSharpCallLua]
public interface IInParamCallback
{
    int Measure([In] ref InParamPoint p, ref int counter);
}

[LuaCallCSharp]
public static class InParamsCaller
{
    // Returns "result,counter,X,Y" so Lua can check that p was left alone.
    public static string CallDelegate(InParamCallback callback)
    {
        var p = new InParamPoint(3, 4);
        int counter = 10;
        int result = callback(ref p, ref counter);
        return result + "," + counter + "," + p.X + "," + p.Y;
    }

    public static string CallInterface(IInParamCallback callback)
    {
        var p = new InParamPoint(3, 4);
        int counter = 10;
        int result = callback.Measure(ref p, ref counter);
        return result + "," + counter + "," + p.X + "," + p.Y;
    }
}
