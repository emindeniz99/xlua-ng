// The source generator in a player (prepare.py --source-gen): Assembly-CSharp's
// configuration, read by XLua.SourceGenerator.dll at compile time, and the
// checks XluaSmoke.Checks runs when XLUA_SMOKE_SOURCE_GEN is defined
// (docs/design/source-generator.md, M1 step 5, M2 item 2, M3 item 1 and M5's
// IL2CPP High-stripping check). Without the generated code, or with members
// stripped that the generated link.xml should keep, a check fails.
#if XLUA_SMOKE_SOURCE_GEN
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using XLua;

// Through the generated wrapper Lua does not see Hidden; through reflection it does.
[assembly: BlackList(typeof(SgApi), "Hidden")]
// Not wrapped: Lua calls SgApi.Reflected through reflection, so only the
// generated link.xml fragment keeps it under High stripping.
[assembly: DoNotGen(typeof(SgApi), "Reflected")]

namespace System.Runtime.CompilerServices
{
    // init accessors need this type; .NET Standard 2.1 does not have it.
    internal static class IsExternalInit { }
}

// A delegate bridge generated into this assembly (a closed static delegate over DelegateBridge).
[CSharpCallLua]
public delegate int SgTwice(int x);

// Pushed and read without boxing by the generated pusher.
[GCOptimize]
public struct SgPoint
{
    public int X;
    public int Y;
}

[LuaCallCSharp]
public class SgApi
{
    public static int Hidden = 7;

    public static int Twice(int x)
    {
        return x * 2;
    }

    public static SgPoint Swap(SgPoint p)
    {
        return new SgPoint { X = p.Y, Y = p.X };
    }

    public static int Apply(SgTwice f, int x)
    {
        return f(x);
    }

    // [assembly: DoNotGen] above.
    public static string Reflected(string s)
    {
        return "reflected:" + s;
    }
}

// An init-only setter: the wrapper falls back to reflection for it (the
// fallback warning in development players), and link.xml keeps the setter.
[LuaCallCSharp]
public class SgInit
{
    public int Locked { get; init; }
}

// Reached only through reflection; [ReflectionUse] keeps all of it.
[ReflectionUse]
public class SgReflected
{
    public int Value = 5;

    public int Plus(int x)
    {
        return Value + x;
    }
}

// Nothing configures or calls this: High stripping removes it (a static check
// of the stripped Assembly-CSharp.dll shows it).
public class SgUnused
{
    public static int Unrelated()
    {
        return 1;
    }
}

public static class SgSmoke
{
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("XLUA_NG_SMOKE_FAIL: source generator: " + name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Run(LuaEnv env)
    {
        var assembly = typeof(SgApi).Assembly;
        var generated = assembly.GetTypes().Where(t => t.Namespace != null && t.Namespace.StartsWith("XLua.Generated.")).ToArray();
        Check(generated.Any(t => t.Name == "Bindings"), "XLua.Generated.*.Bindings in " + assembly.GetName().Name);
        Check(generated.Any(t => t.Name.StartsWith("W_") && t.Name.Contains("SgApi")), "the SgApi wrapper: " + string.Join(",", generated.Select(t => t.Name).ToArray()));

        // The wrapper: the blacklisted field is not there, the methods are.
        Check(Convert.ToInt64(env.DoString("return CS.SgApi.Twice(21)")[0]) == 42, "wrapped method");
        Check((bool)env.DoString("return CS.SgApi.Hidden == nil")[0], "the wrapper leaves out the blacklisted Hidden");

        // DoNotGen: reflection, kept by link.xml.
        Check((string)env.DoString("return CS.SgApi.Reflected('x')")[0] == "reflected:x", "DoNotGen method through reflection");

        // [GCOptimize] struct in and out.
        Check((bool)env.DoString("local p = CS.SgApi.Swap({X = 1, Y = 2}); return p.X == 2 and p.Y == 1")[0], "GCOptimize struct from a table and back");

        // The init-only setter falls back to reflection.
        Check(Convert.ToInt64(env.DoString("local o = CS.SgInit(); o.Locked = 3; return o.Locked")[0]) == 3, "init-only setter through reflection");

        // [ReflectionUse]: constructor, field and method through reflection.
        Check(Convert.ToInt64(env.DoString("local r = CS.SgReflected(); return r:Plus(r.Value)")[0]) == 10, "[ReflectionUse] type through reflection");

        // The generated delegate bridge in this assembly, closed over the DelegateBridge.
        string bridge = Bridge(env);
        Check(Convert.ToInt64(env.DoString("return CS.SgApi.Apply(function(x) return x + 1 end, 41)")[0]) == 42, "Lua function as an SgTwice argument");
        return "source generator (" + generated.Length + " generated types, bridge " + bridge + ")";
    }

    // In its own frame, so that the delegate is gone before LuaEnv.Dispose.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Bridge(LuaEnv env)
    {
        var f = env.DoString("return function(x) return x * 2 end")[0] as LuaFunction;
        var twice = f.Cast<SgTwice>();
        f.Dispose();
        Check(twice(21) == 42, "SgTwice from Lua");
        Check(twice.Method.IsStatic, "bridge is static: " + twice.Method);
        Check(twice.Method.Name.StartsWith("__Gen_Delegate_Imp"), "bridge method: " + twice.Method.Name);
        Check(twice.Method.DeclaringType.Assembly == typeof(SgApi).Assembly, "bridge in Assembly-CSharp: " + twice.Method.DeclaringType.AssemblyQualifiedName);
        Check(twice.Target is DelegateBridge, "bridge closed over its DelegateBridge: " + twice.Target);
        return twice.Method.DeclaringType.FullName + "." + twice.Method.Name;
    }
}
#endif
