// Configuration lists with the cases the migration tool has to spell or report
// (Test/Native/migration_check.py, configuration "edge"). The surface diff
// checks that the attributes it writes give the source generator what the
// legacy generator makes from these lists; the expected report lines are
// checked by the script.
using System;
using System.Collections.Generic;
using System.Reflection;
using XLua;

namespace MigrationEdge
{
    public class Outer<T>
    {
        public class Inner<U>
        {
            public T First;
            public U Second;
            public string Both() { return First + "/" + Second; }
        }
    }

    public class Plain
    {
        public int Value;
        public int Twice(ref int x) { x *= 2; return x; }
        public int Twice(int x) { return x * 2; }
        public int Hidden() { return 0; }
        public int Thrower() { return 1; }
        public int Kept() { return 2; }
    }

    // On the type and in a list: the tool leaves the list entry out.
    [LuaCallCSharp]
    public class Both
    {
        public int X;
    }

    public static class PlainExtensions
    {
        public static int Doubled(this Plain p) { return p.Value * 2; }
        // An extension method for a type of another assembly: the extended
        // type must be wrapped in this assembly for the source generator to
        // merge it.
        public static string Shout(this global::UnityEngine.Vector2 v) { return v.ToString().ToUpper(); }
    }

    public struct Packed
    {
        public int A;
        public float B;
    }

    public delegate int Callback(Plain p, ref int x);

    public interface IBridge
    {
        int Get(int x);
    }

    class Hidden
    {
        public class Nested { }
        private class Private { }
    }

    public static class Lists
    {
        [LuaCallCSharp]
        public static List<Type> Wrapped = new List<Type>
        {
            typeof(Outer<int>.Inner<string>),
            typeof(Plain),
            typeof(Both),
            typeof(PlainExtensions),
            typeof(global::UnityEngine.Vector2),
            typeof(Outer<>),                 // open: left out
            typeof(Callback),                // a delegate: left out
            typeof(int[]),                   // an array type
            typeof(Packed),
        };

        [CSharpCallLua]
        static IEnumerable<Type> Bridged
        {
            get
            {
                yield return typeof(Callback);
                yield return typeof(IBridge);
                yield return typeof(Plain);          // neither a delegate nor an interface: left out
                yield return typeof(Func<Outer<int>.Inner<string>, int>);
            }
        }

        [GCOptimize]
        static List<Type> Structs = new List<Type> { typeof(Packed) };

        [ReflectionUse]
        static List<Type> Reflected = new List<Type> { typeof(Hidden), typeof(Hidden.Nested), typeof(Hidden).GetNestedType("Private", BindingFlags.NonPublic) };

        [BlackList]
        static List<List<string>> Black = new List<List<string>>
        {
            new List<string> { "MigrationEdge.Plain", "Hidden" },
            new List<string> { "MigrationEdge.Plain", "Twice", "System.Int32&" },
            new List<string> { "MigrationEdge.Plain", "NoSuchMember" },
            new List<string> { "MigrationEdge.NoSuchType", "Member" },
            new List<string> { "MigrationEdge.Outer`1+Inner`1[System.Int32,System.String]", "Both" },
            // An engine member that Unity 2021.3 has and 6000.6 removed: the
            // by-name form, which gives info, not warning, XLSG003 where it is
            // missing.
            new List<string> { "UnityEngine.GameObject", "networkView" },
        };

        // Throws for one member when XLUA_MIGRATION_EDGE_THROW is 1, as
        // migration_check.py sets it for the tool's run: Generate Code would
        // fail; the tool reports it and treats the member as not matching. The
        // legacy run, without the variable, sees the filter return false there.
        [BlackList]
        static Func<MemberInfo, bool> Throwing = member =>
        {
            if (member.DeclaringType == typeof(Plain) && member.Name == "Thrower" && Environment.GetEnvironmentVariable("XLUA_MIGRATION_EDGE_THROW") == "1")
                throw new InvalidOperationException("filter failure");
            return member.DeclaringType == typeof(Plain) && member.Name == "Value";
        };

        [DoNotGen]
        static Dictionary<Type, List<string>> Lazy = new Dictionary<Type, List<string>>
        {
            { typeof(Plain), new List<string> { "Kept", "Missing", "Doubled" } },
        };

        [Hotfix(HotfixFlag.IntKey | HotfixFlag.IgnoreProperty)]
        static List<Type> Patched = new List<Type> { typeof(Plain), typeof(Outer<>), typeof(Outer<int>) };
    }
}
