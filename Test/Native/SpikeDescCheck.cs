// SPIKE (spike/descriptor-wrappers): the Lua-visible behaviour of descriptor
// wrappers against the plain and GCM wrappers of the same member subset.
// spike_desc_check.py compiles this with XLUA_SPIKE_SUBSET (plain),
// GEN_CODE_MINIMIZE + XLUA_SPIKE_SUBSET (GCM) and GEN_CODE_MINIMIZE +
// XLUA_SPIKE_DESC (descriptors) and diffs the output.
using System;
using XLua;

namespace SpikeCheck
{
    public enum Color { Red, Green, Blue }

    [LuaCallCSharp]
    public interface IFace { int V(); }

    public class FaceImpl : IFace { public int V() { return 42; } }

    [LuaCallCSharp]
    public struct Bar
    {
        public int X;
        public float Y;
        public Bar(int x, float y) { X = x; Y = y; }
        public int Sum() { return X + (int)Y; }
        public void Inc() { X++; }
        public int Prop { get { return X; } set { X = value; } }
        public static Bar Zero { get { return default(Bar); } }
        public override string ToString() { return "Bar(" + X + "," + Y + ")"; }
    }

    [LuaCallCSharp]
    public static class Util
    {
        public static int Twice(int x) { return 2 * x; }
        public static Color Next(Color c) { return (Color)(((int)c + 1) % 3); }
        public static int ColorValue(Color c) { return (int)c; }
    }

    [LuaCallCSharp]
    public class Foo
    {
        public int I;
        public string S;
        public double D;
        public long L;
        public ulong UL;
        public bool B;
        public float F;
        public byte U8;
        public sbyte I8;
        public short I16;
        public ushort U16;
        public uint U32;
        public char C;
        public Foo Other;
        public Bar BarField;
        public static int SI = 3;
        public static string SS = "s";
        public const int K = 7;
        public static readonly string RO = "ro";
        public int P { get; set; }
        public static string SP { get; set; }
        public int ReadOnlyP { get { return I + 1; } }

        public Foo() { }
        public Foo(int i) { I = i; }
        public Foo(string s) { S = s; }
        public Foo(int i, string s, double d = 2.5) { I = i; S = s; D = d; }

        public int Add(int a, int b) { return a + b; }
        public double Add(double a, double b) { return a + b + 0.5; }
        public string Add(string a, string b) { return a + b; }
        public int Over(int a) { return 1; }
        public int Over(int a, int b) { return 2; }
        public int Over(Foo f) { return 3; }
        public int Over(string s) { return 4; }
        public int Over(object o) { return 5; }
        public static long Big(long x) { return x * 2; }
        public static ulong UBig(ulong x) { return x + 1; }
        public static Foo Make(int i) { return new Foo(i); }
        public Bar GetBar() { return new Bar(I, 0.5f); }
        public static Bar Scale(Bar b, int k) { return new Bar(b.X * k, b.Y * k); }
        public static IFace Face() { return new FaceImpl(); }
        public static int TakeFace(IFace f) { return f == null ? -1 : f.V(); }
        public static object Any(object o) { return o; }
        public static string TypeOf(object o) { return o == null ? "null" : o.GetType().FullName; }
        public static int Opt(int a, int b = 10, int c = 20) { return a + b + c; }
        public void Throw() { throw new InvalidOperationException("boom"); }
        public static int TakeInt(int x) { return x; }
        public static byte TakeByte(byte x) { return x; }
        public static uint TakeUInt(uint x) { return x; }
        public static int SumArr(int[] a) { int s = 0; foreach (var v in a) s += v; return s; }
        public int[] Arr() { return new[] { 1, 2, 3 }; }
        public static bool Not(bool b) { return !b; }
        public static float Half(float f) { return f / 2; }
        public static char NextChar(char c) { return (char)(c + 1); }
        public static string Nul(string s) { return s == null ? "null" : s; }
        public static short Neg16(short s) { return (short)-s; }
        public static sbyte Neg8(sbyte s) { return (sbyte)-s; }
        public static ushort U16Id(ushort s) { return s; }
        public static void Nothing() { }
        public Foo Self() { return this; }
        public static Color Pick(Color c) { return c; }
        // Re-entry: the member calls Lua, which calls it again (the pooled argument buffer).
        public static string Nest(string s, int n)
        {
            if (n <= 0) return s;
            object inner = Program.Env.DoString("return CS.SpikeCheck.Foo.Nest('" + s + n + "', " + (n - 1) + ")")[0];
            return s + "<" + inner + ">" + n;
        }
        public Bar NestBar(int n) { if (n > 0) Program.Env.DoString("return CS.SpikeCheck.Foo():NestBar(" + (n - 1) + ")"); return new Bar(n, n); }
    }

    public static class Program
    {
        public static LuaEnv Env;

        public static int Main(string[] args)
        {
            var env = Env = new LuaEnv();
            try
            {
                env.DoString(System.IO.File.ReadAllText(args[0]));
            }
            catch (Exception e)
            {
                Console.WriteLine("SCRIPT ERROR " + e.Message);
                return 1;
            }
            env.Dispose();
            Console.WriteLine("SPIKE_CHECK_DONE");
            return 0;
        }
    }
}
