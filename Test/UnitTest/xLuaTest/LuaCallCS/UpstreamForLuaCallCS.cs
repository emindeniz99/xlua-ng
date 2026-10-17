// Regression tests for upstream Tencent/xLua issues (UPSTREAM.md).
// Lua cases: StreamingAssets/luaCallCsUpstream.lua. The types here are not in
// LuaCallCSharp: they use emitted wrappers in the reflection suite and
// reflection in the generated-code suites.
using System;
using System.Runtime.InteropServices;
using XLua;

namespace Upstream
{
    // Tencent/xLua#991: optional Nullable<T> parameters. Building the
    // reflection wrapper threw InvalidCastException (Convert.ChangeType to
    // int?), which broke every member of the type.
    public class NullableDefaults
    {
        public static int Plain()
        {
            return 1;
        }

        public static string Zero(int? x = 0)
        {
            return x.HasValue ? "v" + x.Value : "null";
        }

        public static string Null(int? y = null)
        {
            return y.HasValue ? "v" + y.Value : "null";
        }

        public static string Seven(string prefix, long? z = 7)
        {
            return prefix + (z.HasValue ? "v" + z.Value : "null");
        }
    }

    // Tencent/xLua#1105: `in int x = 5`. mcs compiles the `in` keyword as
    // by-value, so XLUA_GENERAL builds spell out what Roslyn emits: [In] ref
    // with IsReadOnlyAttribute and a default constant (see
    // InParamsForLuaCallCS.cs).
    public class InDefaults
    {
        public static int Plain()
        {
            return 1;
        }

#if XLUA_GENERAL
        public static int Five([In, System.Runtime.CompilerServices.IsReadOnly, Optional, DefaultParameterValue(5)] ref int x)
#else
        public static int Five(in int x = 5)
#endif
        {
            return x * 10;
        }
    }

    // Tencent/xLua#1114: ref struct (Span<T>, ReadOnlySpan<T>) parameters,
    // returns and properties cannot be boxed, so wrappers must leave those
    // members out and keep the overloads that take ordinary types.
    public class SpanOverloads
    {
        public int Offset = 0;

        public static int Len(string s)
        {
            return s.Length;
        }

        public static int Len(ReadOnlySpan<char> s)
        {
            return s.Length + 1000;
        }

        public static ReadOnlySpan<char> AsChars(string s)
        {
            return s.AsSpan();
        }

        public static ReadOnlySpan<char> Chars
        {
            get { return "abc".AsSpan(); }
        }

        public int Count(byte[] bytes)
        {
            return bytes.Length + Offset;
        }

        public int Count(Span<byte> bytes)
        {
            return bytes.Length + 1000;
        }

        public Span<byte> Bytes
        {
            get { return new Span<byte>(new byte[2]); }
        }
    }

#if XLUA_GENERAL
    // Tencent/xLua#1167: a method that returns a pointer next to one that
    // does not (Unity projects need "Allow unsafe code" for this).
    public unsafe class PointerReturns
    {
        static int value = 3;

        public static int Plain()
        {
            return 1;
        }

        public static int* Address()
        {
            fixed (int* p = &value)
            {
                return p;
            }
        }
    }
#endif

    // Tencent/xLua#1153: Lua guarantees only LUA_MINSTACK (20) free slots
    // when it calls a C function, so C# that pushes more must ask for room.
    // Fresh runs a chunk in a new LuaEnv, whose Lua stack has its initial
    // size: a push past the room Lua gave leaves the stack allocation, which
    // the ASan build (Test/Native/sanitize.sh) reports.
    [CSharpCallLua]
    public delegate int ManyArgs(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17, int a18, int a19, int a20, int a21, int a22, int a23, int a24, int a25, int a26, int a27, int a28, int a29, int a30, int a31, int a32, int a33, int a34, int a35, int a36, int a37, int a38, int a39, int a40, int a41, int a42, int a43, int a44, int a45, int a46, int a47, int a48, int a49, int a50, int a51, int a52, int a53, int a54, int a55, int a56, int a57, int a58, int a59);

    [CSharpCallLua]
    public interface IManyArgs
    {
        int Sum(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17, int a18, int a19, int a20, int a21, int a22, int a23, int a24, int a25, int a26, int a27, int a28, int a29, int a30, int a31, int a32, int a33, int a34, int a35, int a36, int a37, int a38, int a39, int a40, int a41, int a42, int a43, int a44, int a45, int a46, int a47, int a48, int a49, int a50, int a51, int a52, int a53, int a54, int a55, int a56, int a57, int a58, int a59);
    }

    public class OutBox
    {
        public int V;
    }

    public static class StackRoom
    {
        public static object Fresh(string chunk)
        {
            var env = new LuaEnv();
            try
            {
                return env.DoString(chunk)[0];
            }
            finally
            {
                // Release the delegates the chunk made so Dispose accepts the env.
                env.FullGc();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                env.Tick();
                env.Dispose();
            }
        }

        // C# called from Lua calls a Lua function with n arguments.
        public static object CallBack(LuaFunction f, int n)
        {
            var args = new object[n];
            for (int i = 0; i < n; i++)
            {
                args[i] = i;
            }
            return f.Call(args, null)[0];
        }

        // C# called from Lua calls a Lua function through a delegate with 60 parameters.
        public static int CallMany(ManyArgs d)
        {
            return d(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59);
        }

        // The same through an interface implemented by a Lua table.
        public static int CallInterface(IManyArgs t)
        {
            return t.Sum(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59);
        }

        // 100 out values.
        public static void ManyOuts(out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, out int o8, out int o9, out int o10, out int o11, out int o12, out int o13, out int o14, out int o15, out int o16, out int o17, out int o18, out int o19, out int o20, out int o21, out int o22, out int o23, out int o24, out int o25, out int o26, out int o27, out int o28, out int o29, out int o30, out int o31, out int o32, out int o33, out int o34, out int o35, out int o36, out int o37, out int o38, out int o39, out int o40, out int o41, out int o42, out int o43, out int o44, out int o45, out int o46, out int o47, out int o48, out int o49, out int o50, out int o51, out int o52, out int o53, out int o54, out int o55, out int o56, out int o57, out int o58, out int o59, out int o60, out int o61, out int o62, out int o63, out int o64, out int o65, out int o66, out int o67, out int o68, out int o69, out int o70, out int o71, out int o72, out int o73, out int o74, out int o75, out int o76, out int o77, out int o78, out int o79, out int o80, out int o81, out int o82, out int o83, out int o84, out int o85, out int o86, out int o87, out int o88, out int o89, out int o90, out int o91, out int o92, out int o93, out int o94, out int o95, out int o96, out int o97, out int o98, out int o99)
        {
            o0 = 0;
            o1 = 1;
            o2 = 2;
            o3 = 3;
            o4 = 4;
            o5 = 5;
            o6 = 6;
            o7 = 7;
            o8 = 8;
            o9 = 9;
            o10 = 10;
            o11 = 11;
            o12 = 12;
            o13 = 13;
            o14 = 14;
            o15 = 15;
            o16 = 16;
            o17 = 17;
            o18 = 18;
            o19 = 19;
            o20 = 20;
            o21 = 21;
            o22 = 22;
            o23 = 23;
            o24 = 24;
            o25 = 25;
            o26 = 26;
            o27 = 27;
            o28 = 28;
            o29 = 29;
            o30 = 30;
            o31 = 31;
            o32 = 32;
            o33 = 33;
            o34 = 34;
            o35 = 35;
            o36 = 36;
            o37 = 37;
            o38 = 38;
            o39 = 39;
            o40 = 40;
            o41 = 41;
            o42 = 42;
            o43 = 43;
            o44 = 44;
            o45 = 45;
            o46 = 46;
            o47 = 47;
            o48 = 48;
            o49 = 49;
            o50 = 50;
            o51 = 51;
            o52 = 52;
            o53 = 53;
            o54 = 54;
            o55 = 55;
            o56 = 56;
            o57 = 57;
            o58 = 58;
            o59 = 59;
            o60 = 60;
            o61 = 61;
            o62 = 62;
            o63 = 63;
            o64 = 64;
            o65 = 65;
            o66 = 66;
            o67 = 67;
            o68 = 68;
            o69 = 69;
            o70 = 70;
            o71 = 71;
            o72 = 72;
            o73 = 73;
            o74 = 74;
            o75 = 75;
            o76 = 76;
            o77 = 77;
            o78 = 78;
            o79 = 79;
            o80 = 80;
            o81 = 81;
            o82 = 82;
            o83 = 83;
            o84 = 84;
            o85 = 85;
            o86 = 86;
            o87 = 87;
            o88 = 88;
            o89 = 89;
            o90 = 90;
            o91 = 91;
            o92 = 92;
            o93 = 93;
            o94 = 94;
            o95 = 95;
            o96 = 96;
            o97 = 97;
            o98 = 98;
            o99 = 99;
        }

        // 15 out values, then an object whose type is registered while they
        // are on the stack.
        public static void OutsThenNewType(out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, out int o8, out int o9, out int o10, out int o11, out int o12, out int o13, out int o14, out OutBox box)
        {
            o0 = 0;
            o1 = 1;
            o2 = 2;
            o3 = 3;
            o4 = 4;
            o5 = 5;
            o6 = 6;
            o7 = 7;
            o8 = 8;
            o9 = 9;
            o10 = 10;
            o11 = 11;
            o12 = 12;
            o13 = 13;
            o14 = 14;
            box = new OutBox { V = 7 };
        }

        // 20 out values that are C# objects: each push needs a few slots of its
        // own (the userdata and the cache table) besides the slot it fills.
        public static void ObjectOuts(out OutBox b0, out OutBox b1, out OutBox b2, out OutBox b3, out OutBox b4, out OutBox b5, out OutBox b6, out OutBox b7, out OutBox b8, out OutBox b9, out OutBox b10, out OutBox b11, out OutBox b12, out OutBox b13, out OutBox b14, out OutBox b15, out OutBox b16, out OutBox b17, out OutBox b18, out OutBox b19)
        {
            b0 = new OutBox { V = 0 };
            b1 = new OutBox { V = 1 };
            b2 = new OutBox { V = 2 };
            b3 = new OutBox { V = 3 };
            b4 = new OutBox { V = 4 };
            b5 = new OutBox { V = 5 };
            b6 = new OutBox { V = 6 };
            b7 = new OutBox { V = 7 };
            b8 = new OutBox { V = 8 };
            b9 = new OutBox { V = 9 };
            b10 = new OutBox { V = 10 };
            b11 = new OutBox { V = 11 };
            b12 = new OutBox { V = 12 };
            b13 = new OutBox { V = 13 };
            b14 = new OutBox { V = 14 };
            b15 = new OutBox { V = 15 };
            b16 = new OutBox { V = 16 };
            b17 = new OutBox { V = 17 };
            b18 = new OutBox { V = 18 };
            b19 = new OutBox { V = 19 };
        }
    }

    // ManyOuts through generated wrappers in the generated-code suites.
    [LuaCallCSharp]
    public static class StackRoomGen
    {
        // 20 out values that are C# objects: each push needs a few slots of its
        // own (the userdata and the cache table) besides the slot it fills.
        public static int ObjectOuts(out OutBox b0, out OutBox b1, out OutBox b2, out OutBox b3, out OutBox b4, out OutBox b5, out OutBox b6, out OutBox b7, out OutBox b8, out OutBox b9, out OutBox b10, out OutBox b11, out OutBox b12, out OutBox b13, out OutBox b14, out OutBox b15, out OutBox b16, out OutBox b17, out OutBox b18, out OutBox b19)
        {
            b0 = new OutBox { V = 0 };
            b1 = new OutBox { V = 1 };
            b2 = new OutBox { V = 2 };
            b3 = new OutBox { V = 3 };
            b4 = new OutBox { V = 4 };
            b5 = new OutBox { V = 5 };
            b6 = new OutBox { V = 6 };
            b7 = new OutBox { V = 7 };
            b8 = new OutBox { V = 8 };
            b9 = new OutBox { V = 9 };
            b10 = new OutBox { V = 10 };
            b11 = new OutBox { V = 11 };
            b12 = new OutBox { V = 12 };
            b13 = new OutBox { V = 13 };
            b14 = new OutBox { V = 14 };
            b15 = new OutBox { V = 15 };
            b16 = new OutBox { V = 16 };
            b17 = new OutBox { V = 17 };
            b18 = new OutBox { V = 18 };
            b19 = new OutBox { V = 19 };
            return 20;
        }

        public static int ManyOuts(out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, out int o8, out int o9, out int o10, out int o11, out int o12, out int o13, out int o14, out int o15, out int o16, out int o17, out int o18, out int o19, out int o20, out int o21, out int o22, out int o23, out int o24, out int o25, out int o26, out int o27, out int o28, out int o29, out int o30, out int o31, out int o32, out int o33, out int o34, out int o35, out int o36, out int o37, out int o38, out int o39, out int o40, out int o41, out int o42, out int o43, out int o44, out int o45, out int o46, out int o47, out int o48, out int o49, out int o50, out int o51, out int o52, out int o53, out int o54, out int o55, out int o56, out int o57, out int o58, out int o59, out int o60, out int o61, out int o62, out int o63, out int o64, out int o65, out int o66, out int o67, out int o68, out int o69, out int o70, out int o71, out int o72, out int o73, out int o74, out int o75, out int o76, out int o77, out int o78, out int o79, out int o80, out int o81, out int o82, out int o83, out int o84, out int o85, out int o86, out int o87, out int o88, out int o89, out int o90, out int o91, out int o92, out int o93, out int o94, out int o95, out int o96, out int o97, out int o98, out int o99)
        {
            o0 = 0;
            o1 = 1;
            o2 = 2;
            o3 = 3;
            o4 = 4;
            o5 = 5;
            o6 = 6;
            o7 = 7;
            o8 = 8;
            o9 = 9;
            o10 = 10;
            o11 = 11;
            o12 = 12;
            o13 = 13;
            o14 = 14;
            o15 = 15;
            o16 = 16;
            o17 = 17;
            o18 = 18;
            o19 = 19;
            o20 = 20;
            o21 = 21;
            o22 = 22;
            o23 = 23;
            o24 = 24;
            o25 = 25;
            o26 = 26;
            o27 = 27;
            o28 = 28;
            o29 = 29;
            o30 = 30;
            o31 = 31;
            o32 = 32;
            o33 = 33;
            o34 = 34;
            o35 = 35;
            o36 = 36;
            o37 = 37;
            o38 = 38;
            o39 = 39;
            o40 = 40;
            o41 = 41;
            o42 = 42;
            o43 = 43;
            o44 = 44;
            o45 = 45;
            o46 = 46;
            o47 = 47;
            o48 = 48;
            o49 = 49;
            o50 = 50;
            o51 = 51;
            o52 = 52;
            o53 = 53;
            o54 = 54;
            o55 = 55;
            o56 = 56;
            o57 = 57;
            o58 = 58;
            o59 = 59;
            o60 = 60;
            o61 = 61;
            o62 = 62;
            o63 = 63;
            o64 = 64;
            o65 = 65;
            o66 = 66;
            o67 = 67;
            o68 = 68;
            o69 = 69;
            o70 = 70;
            o71 = 71;
            o72 = 72;
            o73 = 73;
            o74 = 74;
            o75 = 75;
            o76 = 76;
            o77 = 77;
            o78 = 78;
            o79 = 79;
            o80 = 80;
            o81 = 81;
            o82 = 82;
            o83 = 83;
            o84 = 84;
            o85 = 85;
            o86 = 86;
            o87 = 87;
            o88 = 88;
            o89 = 89;
            o90 = 90;
            o91 = 91;
            o92 = 92;
            o93 = 93;
            o94 = 94;
            o95 = 95;
            o96 = 96;
            o97 = 97;
            o98 = 98;
            o99 = 99;
            return -1;
        }
    }

    // Inherited from upstream, not reported there: xlua.private_accessible
    // wrapped each method once per non-public method of the same name (an
    // Enumerable.Join on the name), so Pick below held every overload twice,
    // and Single, whose static and instance forms share a name, lost the
    // unchecked call that a method with one overload gets.
    public class PrivateOverloads
    {
        public string Pick(double x)
        {
            return "double";
        }

        private string Pick(bool b)
        {
            return "bool";
        }

        private string Pick(PrivateOverloads other)
        {
            return "object";
        }

        // One instance and one static overload: each is called without an
        // argument check, so a number converts to the string, as it does for
        // a public method with one overload.
        private string Single(string s)
        {
            return "s:" + s;
        }

        private static string Single(int i, int j)
        {
            return "ij";
        }

        static object Field(object o, string name)
        {
            return o.GetType().GetField(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).GetValue(o);
        }

        // The overloads of each wrapper named `name` that private_accessible
        // builds in a fresh LuaEnv: "<count> <parameter types>,..." per
        // wrapper, the wrappers sorted and joined with "|".
        public static string Overloads(string name)
        {
            var env = new LuaEnv();
            try
            {
                // Load the type first: without emitted or generated wrappers its public
                // members get reflection wrappers too, and those are not counted here.
                env.DoString("local t = CS.Upstream.PrivateOverloads");
                var functions = (System.Collections.IList)Field(Field(env, "translator"), "fix_cs_functions");
                int before = functions.Count;
                env.DoString("xlua.private_accessible(CS.Upstream.PrivateOverloads)");
                var wraps = new System.Collections.Generic.List<string>();
                for (int i = before; i < functions.Count; i++)
                {
                    var wrap = ((Delegate)functions[i]).Target as MethodWrap;
                    if (wrap == null || (string)Field(wrap, "methodName") != name)
                    {
                        continue;
                    }
                    var overloads = (System.Collections.IList)Field(wrap, "overloads");
                    var signatures = new System.Collections.Generic.SortedSet<string>();
                    foreach (var overload in overloads)
                    {
                        var parameters = ((System.Reflection.MethodBase)Field(overload, "method")).GetParameters();
                        var types = new string[parameters.Length];
                        for (int j = 0; j < parameters.Length; j++)
                        {
                            types[j] = parameters[j].ParameterType.Name;
                        }
                        signatures.Add(string.Join(";", types));
                    }
                    wraps.Add(overloads.Count + " " + string.Join(",", new System.Collections.Generic.List<string>(signatures).ToArray()));
                }
                wraps.Sort(string.CompareOrdinal);
                return string.Join("|", wraps.ToArray());
            }
            finally
            {
                env.Dispose();
            }
        }
    }
}
