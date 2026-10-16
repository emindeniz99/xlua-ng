using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XLua;

// Types that the editor tests expose to Lua. prepare.py --editor-tools copies
// this file into the runtime assembly, where generated wrappers can see it.
namespace XluaEditorTest
{
    public enum Mood { Calm = 1, Busy = 2, Lost = 4 }

    public struct Pair
    {
        public int A;
        public float B;

        public Pair(int a, float b)
        {
            A = a;
            B = b;
        }

        public float Sum()
        {
            return A + B;
        }
    }

    [CSharpCallLua]
    public delegate int Combine(int a, string b);

    // C# `in` parameters, compiled by Unity's Roslyn: Lua passes them like
    // values and gets no extra result for them; Lua callbacks return only
    // the result and the ref parameter.
    [CSharpCallLua]
    public delegate float MeasurePair(in Pair p, ref int calls);

    [CSharpCallLua]
    public interface IPairMeasure
    {
        float Measure(in Pair p, ref int calls);
    }

    public class Target
    {
        // Every instance method call from Lua records the first stack frame
        // outside this class: a generated wrapper, an emitted one or reflection.
        public static int Calls;
        public static string LastCaller;

        public int Value;
        public Combine Callback;

        public Target()
        {
        }

        public Target(int value)
        {
            Value = value;
        }

        public string Describe(int x)
        {
            Note();
            return "int:" + x;
        }

        public string Describe(string s)
        {
            Note();
            return "string:" + s;
        }

        public string Describe(int x, int y)
        {
            Note();
            return "sum:" + (x + y);
        }

        public static float Length(in Pair p)
        {
            return p.A + p.B;
        }

        public float Scaled(in Pair p, ref int factor)
        {
            factor += 1;
            return (p.A + p.B) * Value;
        }

        // "result,calls,A,B": p must come back unchanged.
        public static string CallMeasure(MeasurePair f)
        {
            var p = new Pair(2, 0.5f);
            int calls = 1;
            float r = f(p, ref calls);
            return r + "," + calls + "," + p.A + "," + p.B;
        }

        public static string CallMeasurer(IPairMeasure m)
        {
            var p = new Pair(2, 0.5f);
            int calls = 1;
            float r = m.Measure(p, ref calls);
            return r + "," + calls + "," + p.A + "," + p.B;
        }

        public static int Div(int a, int b, out int rem)
        {
            rem = a % b;
            return a / b;
        }

        public void Twice(ref int v)
        {
            Note();
            v *= 2;
        }

        public Pair MakePair(int a, float b)
        {
            Note();
            return new Pair(a, b);
        }

        public Mood Next(Mood m)
        {
            Note();
            return m == Mood.Lost ? Mood.Calm : (Mood)((int)m * 2);
        }

        public int Apply(Combine f, int a, string b)
        {
            Note();
            return f(a, b);
        }

        public static void Reset()
        {
            Calls = 0;
            LastCaller = null;
        }

        static void Note()
        {
            Calls++;
            var frames = new StackTrace(false).GetFrames();
            LastCaller = null;
            for (int i = 0; frames != null && i < frames.Length; i++)
            {
                var method = frames[i].GetMethod();
                var type = method == null ? null : method.DeclaringType;
                if (type == typeof(Target)) continue;
                LastCaller = type == null ? "(none)" : type.FullName + (type.Assembly.IsDynamic ? " [dynamic]" : "");
                break;
            }
        }
    }

    public static class Config
    {
        [LuaCallCSharp]
        public static List<Type> LuaCallCSharp = new List<Type>()
        {
            typeof(Vector3),
            typeof(GameObject),
            typeof(Transform),
            typeof(Target),
            typeof(Pair),
            typeof(Mood),
        };

        [CSharpCallLua]
        public static List<Type> CSharpCallLua = new List<Type>()
        {
            typeof(Combine),
            typeof(MeasurePair),
            typeof(IPairMeasure),
        };

        // Unity 6 adds Span<Vector3> and ReadOnlySpan<EntityId> overloads to
        // Transform and GameObject; Generate Code must skip them by itself.
        // Vector3 is also listed in XLua.SysGenConfig, and listing it again
        // must not break Generate Code.
        [GCOptimize]
        public static List<Type> GCOptimize = new List<Type>()
        {
            typeof(Pair),
            typeof(Vector3),
        };
    }
}
