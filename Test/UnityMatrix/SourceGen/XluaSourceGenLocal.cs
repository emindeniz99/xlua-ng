using System.Runtime.CompilerServices;
using UnityEngine;
using XLua;

// Assembly-CSharp's part of the source generator fixture (prepare.py
// --source-gen copies it to Assets/XluaSourceGenLocal.cs; editor_tests.py
// requires XluaSourceGenTests when it is there). The configuration uses only
// the forms the source generator reads: attributes on types and assembly
// attributes, no configuration lists.

// A UnityEngine type, wrapped in Assembly-CSharp.
[assembly: LuaCallCSharp(typeof(UnityEngine.Rect))]
// SgGame wraps Shop already, so Assembly-CSharp does not wrap it again (info
// XLSG008). Without asmdefs Shop is in Assembly-CSharp, and this names it twice.
[assembly: LuaCallCSharp(typeof(SgGame.Shop))]
// Through the generated wrapper Lua does not see Hidden; through reflection it does.
[assembly: BlackList(typeof(SgLocal), "Hidden")]
// A member that does not exist: warning XLSG003 with this file and line,
// which editor_tests.py looks for in the compile log.
[assembly: BlackList(typeof(SgLocal), "NoSuchMember")]

// An interface bridge generated in Assembly-CSharp (milestone M3).
[CSharpCallLua]
public interface ISgScore
{
    int Score(int x);
}

[LuaCallCSharp]
public class SgLocal
{
    public string Name;
    public int Hidden = 7;

    public SgLocal()
    {
        Name = "local";
    }

    public SgLocal(string name)
    {
        Name = name;
    }

    // A parameter type from the game assembly.
    public int Total(SgGame.Shop shop, int count)
    {
        return shop.Price(count) + 1;
    }

    public float Length(in Vector3 v)
    {
        return v.magnitude;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Caller()
    {
        return SgGame.Calls.Of(typeof(SgLocal));
    }
}
