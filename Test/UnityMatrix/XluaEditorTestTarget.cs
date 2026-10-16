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

    // Strings for XluaStringTests: Lua strings are bytes, read as UTF-8.
    public static class Text
    {
        public static string Echo(string s)
        {
            return s;
        }

        // The UTF-16 code units in hex, such as "0061 FFFD".
        public static string CodeUnits(string s)
        {
            return string.Join(" ", s.Select(c => ((int)c).ToString("X4")).ToArray());
        }
    }

    // Members with Turkish and Chinese names, spelled with \u escapes: Sehir,
    // Agirlik and Carp with Turkish letters, "shuliang" and "mingzi" in Chinese,
    // and names of 120 CJK characters (360 bytes, more than the 256-byte buffer
    // xlua_pushasciistring reuses). Lua reaches them by their UTF-8 bytes.
    public class Names
    {
        public int \u015Eehir = 1;
        public int \u6570\u91CF = 2;
        public int \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3;
        public string A\u011F\u0131rl\u0131k { get; set; }
        public string \u540D\u5B57 { get; set; }

        public int \u00C7arp(int x)
        {
            return x * 3;
        }

        public int \u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD()
        {
            return 4;
        }

        // The same members on a non-public type, which xLua wraps by reflection.
        public static object MakeReflect()
        {
            return new NamesReflect();
        }

        public static int Value(NameEnum e)
        {
            return (int)e;
        }
    }

    class NamesReflect
    {
        public int \u015Eehir = 1;
        public int \u6570\u91CF = 2;
        public int \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3;
        public string A\u011F\u0131rl\u0131k { get; set; }
        public string \u540D\u5B57 { get; set; }

        public int \u00C7arp(int x)
        {
            return x * 3;
        }

        public int \u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD()
        {
            return 4;
        }
    }

    // Generated enum wrappers compare names with LuaAPI.xlua_is_eq_str.
    public enum NameEnum
    {
        \u015Eimdi = 1,
        \u4E2D\u6587 = 2,
        \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3,
    }

    public static class Config
    {
        [LuaCallCSharp]
        public static List<Type> LuaCallCSharp = new List<Type>()
        {
            typeof(Vector3),
            typeof(GameObject),
            typeof(Transform),
            typeof(Camera),
            typeof(Target),
            typeof(Pair),
            typeof(Mood),
            typeof(Text),
            typeof(Names),
            typeof(NameEnum),
        };

        // Camera's nested types, the way a config that lists a type with its
        // nested types gets them. Unity 2022.3 marks RenderRequest,
        // RenderRequestMode and RenderRequestOutputSpace [Obsolete(error: true)],
        // so C# cannot name them and generated code must leave them out
        // (https://github.com/Tencent/xLua/issues/1216).
        [LuaCallCSharp]
        public static IEnumerable<Type> CameraNestedTypes = typeof(Camera).GetNestedTypes();

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
