// Source of XluaRefReadonlyLib.dll, a precompiled library with C# 12
// `ref readonly` parameters. prepare.py --roslyn-sdk compiles it with a
// Roslyn that knows C# 12 (Unity 6000.6's .NET SDK) against .NET Standard
// 2.1, so editors whose own Roslyn cannot declare `ref readonly` (2021.3 to
// 6000.3 ship Roslyn 4.3) can still reference it. It must not reference xLua:
// XluaRefReadonlyLibConfig.cs configures the types for Lua.
//
// Roslyn 4.3 reports CS0570 for every use of a virtual, abstract, interface
// or delegate member with a `ref readonly` parameter (Roslyn marks those
// with modreq(InAttribute)); non-virtual ones look like plain `ref`.
namespace XluaRefReadonlyLib
{
    public delegate float Measure(ref readonly int x);

    public interface IGauge
    {
        float Read(ref readonly int x);
    }

    public class Gauge
    {
        public int Offset;
        public Measure OnMeasure;

        public event Measure Measured;

        public Gauge(ref readonly int offset)
        {
            Offset = offset;
        }

        // Non-virtual and static: usable by every compiler (passed with `ref`).
        public float Plain(ref readonly int x)
        {
            return x + Offset;
        }

        public static float Twice(ref readonly int x)
        {
            return x * 2;
        }

        // Calls a Lua function through a delegate whose Invoke has a
        // ref readonly parameter; the delegate type only appears as an argument.
        public static float Call(Measure m, int x)
        {
            return m(in x);
        }

        public float Raise(int x)
        {
            return Measured == null ? -1 : Measured(in x);
        }

        // Virtual: unusable for Roslyn 4.3.
        public virtual float Scaled(ref readonly int x)
        {
            return x * 10 + Offset;
        }
    }

    // An indexer with a ref readonly key. Roslyn 4.3 reports it as not
    // supported (CS1545) and only calls its accessors, get_Item(ref x);
    // Roslyn 4.10 needs dial[ref x] and rejects accessor calls (CS0571).
    public class Dial
    {
        public int Offset;

        public float this[ref readonly int x]
        {
            get { return x + Offset; }
            set { Offset = (int)value - x; }
        }
    }

    // Virtual: its accessors are unusable for Roslyn 4.3 (CS0570).
    public class DialVirtual
    {
        public int Last;

        public virtual int this[ref readonly int x]
        {
            get { return x * 10 + Last; }
            set { Last = value + x; }
        }
    }

    // Roslyn gives the public implicit implementation no modreq (a private
    // stub with the modreq implements IGauge.Read), so Read stays usable.
    public class GaugeImpl : IGauge
    {
        public float Read(ref readonly int x)
        {
            return x + 0.5f;
        }
    }
}
