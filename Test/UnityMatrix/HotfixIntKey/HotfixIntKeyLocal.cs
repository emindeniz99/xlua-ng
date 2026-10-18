// In Assembly-CSharp, configured by LocalHotfix.cs: IntKey through Assembly-CSharp's own slots.
public class HotfixIntKeyLocal
{
    public int Price()
    {
        return 2;
    }

    // C# `in` and C# 12 `ref readonly` parameters are inputs only: a patch's
    // extra Lua result reaches the `ref` argument of Counted and never the
    // others (HotfixIntKeyTests). The parameters of the virtual methods carry
    // modreq(InAttribute), which the injector used to miss ("can not find
    // delegate"). `ref readonly` needs C# 12 (hotfix_intkey.py --csharp12,
    // Unity 6000.6's Roslyn).
    public int Counted(ref int x)
    {
        return x;
    }

    public int Scaled(in int x)
    {
        return x;
    }

    public virtual int ScaledVirtual(in int x)
    {
        return x;
    }

#if XLUA_TEST_HOTFIX_CSHARP12
    public int ScaledRefReadonly(ref readonly int x)
    {
        return x;
    }

    public virtual int ScaledRefReadonlyVirtual(ref readonly int x)
    {
        return x;
    }
#endif
}
