using XLua;

// C# 12 `ref readonly` parameters. prepare.py --csharp12 copies this file
// into the runtime assembly and compiles the project with -langversion:12
// (Unity 6000.6 and later ship a Roslyn that accepts it). Lua treats them
// like `in` parameters: plain inputs with no extra result, and Lua
// callbacks return only the result and the ref parameters.
namespace XluaEditorTest
{
    [CSharpCallLua]
    public delegate float MeasureRefReadonly(ref readonly Pair p, ref int calls);

    [CSharpCallLua]
    public interface IRefReadonlyMeasure
    {
        float Measure(ref readonly Pair p, ref int calls);
    }

    [LuaCallCSharp]
    public class RefReadonlyTarget
    {
        public int Offset;

        public RefReadonlyTarget(ref readonly int offset)
        {
            Offset = offset;
        }

        // Only a ref readonly overload, so Lua must reach it.
        public static float Length(ref readonly Pair p)
        {
            return p.A + p.B;
        }

        // ref readonly between real ref and out parameters.
        public static int Scale(ref readonly int factor, ref int value, out int old)
        {
            old = value;
            value = value * factor;
            return factor;
        }

        // A by-value and ref readonly pair: one entry for Lua.
        public static float Product(Pair p)
        {
            return p.A * p.B;
        }

        public static float Product(ref readonly Pair p)
        {
            return p.A * p.B;
        }

        public float Add(ref readonly Pair p)
        {
            return p.A + p.B + Offset;
        }

        // "result,calls,A,B": p must come back unchanged.
        public static string CallMeasure(MeasureRefReadonly f)
        {
            var p = new Pair(2, 0.5f);
            int calls = 1;
            float r = f(in p, ref calls);
            return r + "," + calls + "," + p.A + "," + p.B;
        }

        public static string CallMeasurer(IRefReadonlyMeasure m)
        {
            var p = new Pair(2, 0.5f);
            int calls = 1;
            float r = m.Measure(in p, ref calls);
            return r + "," + calls + "," + p.A + "," + p.B;
        }
    }

    // Indexers with a ref readonly key; generated code passes it with `ref`.
    [LuaCallCSharp]
    public class RefReadonlyIndexed
    {
        public float Scale = 1;

        public float this[ref readonly Pair p]
        {
            get { return (p.A + p.B) * Scale; }
            set { Scale = value - p.A; }
        }
    }

    // Virtual, so the accessors carry modreq(InAttribute).
    [LuaCallCSharp]
    public class RefReadonlyIndexedVirtual
    {
        public int Last;

        public virtual int this[ref readonly int x]
        {
            get { return x * 10 + Last; }
            set { Last = value + x; }
        }
    }

    // Reached through reflection; its extension method goes through the
    // generated register's __GEN_DELEGATE.
    public class RefReadonlyPlain
    {
    }

    [LuaCallCSharp]
    public static class RefReadonlyExtensions
    {
        public static int Plus(this RefReadonlyPlain self, ref readonly int x)
        {
            return x + 1;
        }
    }
}
