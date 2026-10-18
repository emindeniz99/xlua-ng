// Fixture for Test/Native/luacats_check.py: one small API per case the LuaCATS
// generator has to map. Compiled with the xLua source generator against
// XLua.Mini.dll, with its configuration in SampleAttributes.cs; never run.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XLua;

namespace LuaCATSSample
{
    public enum Color
    {
        Red = 1,
        Green = 2,
        Blue = 4,
        // Not a Lua identifier: written as ['Şeftali'], used as Color['Şeftali'].
        Şeftali = 8,
    }

    public delegate int Scorer(string name, out bool found);

    // `in` parameters are plain arguments and add no return value. mcs
    // compiles the `in` keyword as by-value, so the fixture spells out what
    // Roslyn emits: [In] ref with IsReadOnlyAttribute.
    public delegate double Measure([In, IsReadOnly] ref Point p, ref int calls);

    // C# 12 `ref readonly` parameters are treated like `in`. Roslyn emits
    // [In] ref with RequiresLocationAttribute (defined at the end of this file
    // because Mono's BCL lacks it; Roslyn embeds such a copy itself).
    public delegate double Gauge([In, RequiresLocation] ref Point p, ref int calls);

    // On LuaJIT, long and ulong values that Lua supplies take numbers too:
    // what a Lua function returns for a delegate, indexer keys and operator
    // operands. The parameters a Lua function receives are the userdata.
    public delegate long Ticker(long now, ref ulong mask);

    public class Ledger
    {
        public Ticker Tick;
        public string this[long key] { get { return null; } set { } }
        public static Ledger operator *(Ledger a, long factor) { return a; }
    }

    public interface IShape
    {
        double Area();
    }

    public class Shape : IShape
    {
        public string Name;
        // Not Lua identifiers: written as ['Şehir'] and ['end'], the form Lua
        // code uses (shape['Şehir']).
        public string Şehir;
        public bool @end;
        public virtual double Area() { return 0; }
        public override string ToString() { return Name; }
    }

    public class Circle : Shape
    {
        public const double Pi = 3.14159;
        public static int Count;
        public readonly long Id;
        public ulong Mask;
        public long? Ticks { get; set; }
        public double Radius { get; set; }
        public Color Tint { get; private set; }
        public int? Layer;
        public byte[] Payload;
        public decimal Price;
        public int[] Samples;
        public List<int> Values = new List<int>();
        public Dictionary<string, int> Index = new Dictionary<string, int>();
        public LuaTable Extra;
        public LuaFunction OnDone;
        public Action<int> Callback;
        public Scorer Score;
        public Measure Measurer;
        public Gauge Gauger;

        public event Action<Circle> Changed;
        public static event Action Reset;

        public Circle() { }
        public Circle(double radius, Color tint = Color.Red) { Radius = radius; Tint = tint; }

        public override double Area() { return Pi * Radius * Radius; }

        public void Scale(double factor) { Radius *= factor; }
        public void Scale(int percent) { Radius = Radius * percent / 100; }

        public bool TryGrow(double by, out double radius, ref int steps)
        {
            radius = Radius + by;
            steps++;
            return true;
        }

        public double Reach([In, IsReadOnly] ref double by, ref int steps) { steps++; return Radius + by; }

        public double Stretch([In, RequiresLocation] ref double by, ref int steps) { steps++; return Radius * by; }

        // [In] ref without IsReadOnlyAttribute is a normal ref.
        public void Nudge([In] ref int steps) { steps++; }

        public int Sum(string label, params int[] values) { return values.Length; }

        public long Offset(long by, ulong[] masks) { return Id + by; }

        public static Circle Unit() { return new Circle(1); }

        public static Circle operator +(Circle a, Circle b) { return new Circle(a.Radius + b.Radius); }
        public static Circle operator -(Circle a) { return new Circle(-a.Radius); }

        public int this[int i] { get { return i; } }

        public T Find<T>(T fallback) where T : Shape { return fallback; }
        // xLua calls generic methods with each type argument set to its first
        // constraint, so Clone runs as Clone<Shape> and returns a Shape even for
        // a Circle: the result is declared as the constraint type.
        public T Clone<T>(T source) where T : Shape, new() { return new T(); }
        // Not callable from Lua, so not declared: a type parameter is not
        // constrained to a class, or no parameter has it as its type.
        public void Log<T>(T value) { }
        public T Create<T>() where T : Shape, new() { return new T(); }
        public U Convert<T, U>(T source) where T : Shape where U : Shape { return null; }

        [Obsolete("use Scale", true)]
        public void Grow() { }

        [BlackList]
        public void Hidden() { }

        public void Raise()
        {
            if (Changed != null) Changed(this);
            if (Reset != null) Reset();
        }

        public class Style
        {
            public float Width;
        }
    }

    public struct Point
    {
        public float X;
        public float Y;
        public Point(float x, float y) { X = x; Y = y; }
        public float Length() { return (float)Math.Sqrt(X * X + Y * Y); }

        // A by-value and `in` pair, like Unity 6.3's Vector3.Distance: one entry.
        public static float Distance(Point a, Point b) { return 0; }
        public static float Distance([In, IsReadOnly] ref Point a, [In, IsReadOnly] ref Point b) { return 0; }

        // The same for a by-value and `ref readonly` pair.
        public static float Angle(Point a, Point b) { return 0; }
        public static float Angle([In, RequiresLocation] ref Point a, [In, RequiresLocation] ref Point b) { return 0; }
    }

    public static class ShapeExtensions
    {
        public static double Perimeter(this Circle c) { return 2 * Circle.Pi * c.Radius; }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter)]
    sealed class RequiresLocationAttribute : Attribute
    {
    }
}
