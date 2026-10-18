// The game assembly of Test/Native/obfuscation_check.py
// (docs/design/source-generator.md, milestone M8). It is compiled with the
// xLua source generator against XLua.Mini.dll (xLua in an assembly of its own,
// as with the XLua asmdef), then renamed by ObfuscationRenamer.cs, and run:
//
//     ObfuscationFixture.exe <plain|rules|attributes> [check that must fail...]
//
// Each check below runs from Lua with the original names. After renaming,
// the checks that rely on the name table (CS.Game.Shop, the CS path of nested
// and generic types, metatable keys, xlua.typename), on literals in wrappers
// (members, events, enums of 20 values or fewer) and on registrations keyed by
// Type (delegates, interfaces, GCOptimize structs) must still pass. The checks
// that Lua does by name through reflection pass only when the rules keep
// those names: [ReflectionUse] members, a [DoNotGen] member, a member left to
// reflection, and the value names of an enum with more than 20 values. A type
// without configuration (Unbound) is not in the name table, so renaming it
// always breaks CS.Game.Unbound. The script passes the checks expected to fail
// in each mode; any other result fails the run.
using System;
using System.Collections.Generic;
using XLua;

[assembly: LuaCallCSharp(typeof(Game.Box<int>))]
[assembly: DoNotGen(typeof(Game.Slow), "Secret")]
[assembly: ReflectionUse(typeof(Game.Reflected))]

namespace System.Runtime.CompilerServices
{
    // Not in the .NET Framework profile the fixture compiles against.
    internal static class IsExternalInit
    {
    }
}

namespace Game
{
    [LuaCallCSharp]
    public class Shop
    {
        public int Gold = 10;
        public static string Title = "shop";
        public int Price { get; set; } = 3;
        public Color Paint = Color.Green;
        public event Action Sold;

        public int Buy(int n)
        {
            Gold -= n * Price;
            return Gold;
        }

        public static Shop Create(int gold)
        {
            return new Shop { Gold = gold };
        }

        public void Sell()
        {
            if (Sold != null) Sold();
        }

        public Point Move(Point p)
        {
            return new Point { X = p.X + 1, Y = p.Y + 1 };
        }

        public int Call(Twice f)
        {
            return f(21);
        }

        public int Score(IScorer s)
        {
            return s.Score(2);
        }

        [LuaCallCSharp]
        public class Item
        {
            public string Name = "item";
        }
    }

    // Secret is [DoNotGen]: Lua calls it through reflection, by name, so the
    // rules keep its name and the type's (for link.xml); Fast is wrapped.
    [LuaCallCSharp]
    public class Slow
    {
        public int Secret()
        {
            return 99;
        }

        public int Fast()
        {
            return 1;
        }
    }

    [LuaCallCSharp]
    public enum Color { Red, Green, Blue }

    // More than 20 values: the wrapper reads the names by reflection.
    [LuaCallCSharp]
    public enum Big { V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20, V21, V22, V23, V24 }

    public class Box<T>
    {
        public T Value;

        public Box(T value)
        {
            Value = value;
        }

        public T Get()
        {
            return Value;
        }
    }

    [GCOptimize]
    [LuaCallCSharp]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [CSharpCallLua]
    public delegate int Twice(int x);

    [CSharpCallLua]
    public interface IScorer
    {
        int Score(int x);
    }

    // [ReflectionUse] at assembly level: no wrapper, Lua reaches it by reflection.
    public class Reflected
    {
        public int Value = 7;

        public int Get()
        {
            return Value * 2;
        }
    }

    // The init-only setter is left to reflection (section 5.5).
    [LuaCallCSharp]
    public class Locked
    {
        public int X { get; init; }
    }

    // No configuration.
    public class Unbound
    {
        public static int Answer = 42;
    }
}

static class ObfuscationFixture
{
    const string Checks = @"
        local results = {}
        local function check(name, f)
            local ok, err = pcall(f)
            results[name] = ok and '' or tostring(err)
        end
        check('type', function() assert(CS.Game.Shop ~= nil) end)
        check('members', function()
            local s = CS.Game.Shop()
            assert(s.Gold == 10)
            assert(s:Buy(2) == 4)
            s.Price = 1
            assert(s.Price == 1)
        end)
        check('static', function()
            assert(CS.Game.Shop.Title == 'shop')
            assert(CS.Game.Shop.Create(5).Gold == 5)
        end)
        check('event', function()
            local s, n = CS.Game.Shop(), 0
            s:Sold('+', function() n = n + 1 end)
            s:Sell()
            assert(n == 1)
        end)
        check('nested', function() assert(CS.Game.Shop.Item().Name == 'item') end)
        check('enum', function()
            assert(CS.Game.Shop().Paint == CS.Game.Color.Green)
            assert(CS.Game.Color.__CastFrom('Blue') == CS.Game.Color.Blue)
            assert(CS.Game.Color.__CastFrom(0) == CS.Game.Color.Red)
        end)
        check('enum_by_name', function()
            assert(CS.Game.Big.V21 ~= nil)
            assert(CS.Game.Big.__CastFrom('V22') == CS.Game.Big.V22)
        end)
        check('generic', function()
            local Box = CS.Game.Box(CS.System.Int32)
            assert(Box(5):Get() == 5)
        end)
        check('struct', function()
            local p = CS.Game.Shop():Move({X = 1, Y = 2})
            assert(p.X == 2 and p.Y == 3)
        end)
        check('delegate', function() assert(CS.Game.Shop():Call(function(x) return x * 2 end) == 42) end)
        check('interface', function()
            assert(CS.Game.Shop():Score({Score = function(self, x) return x + 1 end}) == 3)
        end)
        check('typename', function()
            assert(xlua.typename(CS.Game.Shop) == 'Game.Shop')
            assert(xlua.typename(CS.Game.Shop.Item) == 'Game.Shop+Item')
            assert(xlua.typename(CS.Game.Box(CS.System.Int32)) == 'Game.Box`1[System.Int32]')
            assert(xlua.typename('Game.Shop') == 'Game.Shop')
        end)
        check('metatable_key', function()
            assert(debug.getregistry()['Game.Shop'] == debug.getmetatable(CS.Game.Shop()))
        end)
        check('reflection_use', function()
            local r = CS.Game.Reflected()
            assert(r.Value == 7)
            assert(r:Get() == 14)
        end)
        check('donotgen', function()
            local s = CS.Game.Slow()
            assert(s:Fast() == 1)
            assert(s:Secret() == 99)
        end)
        check('fallback', function()
            local l = CS.Game.Locked()
            l.X = 3
            assert(l.X == 3)
        end)
        check('unbound', function() assert(CS.Game.Unbound.Answer == 42) end)
        return results
    ";

    static int Main(string[] args)
    {
        string mode = args[0];
        var expectedFailures = new HashSet<string>();
        for (int i = 1; i < args.Length; i++) expectedFailures.Add(args[i]);
        int failures = 0;

        // The assembly was renamed, or not.
        string shop = typeof(Game.Shop).FullName, reflected = typeof(Game.Reflected).FullName;
        Console.WriteLine("Game.Shop is " + shop + ", Game.Reflected is " + reflected);
        bool renamed = shop != "Game.Shop";
        if (renamed != (mode != "plain"))
        {
            Console.WriteLine("FAIL Game.Shop " + (renamed ? "was" : "was not") + " renamed in mode " + mode);
            failures++;
        }
        if (mode == "rules" && reflected != "Game.Reflected")
        {
            Console.WriteLine("FAIL the rules did not keep the name of the [ReflectionUse] type");
            failures++;
        }

        var env = new LuaEnv();
        var results = (LuaTable)env.DoString(Checks)[0];
        var names = new List<string>();
        results.ForEach<string, string>((name, error) => names.Add(name));
        names.Sort(StringComparer.Ordinal);
        foreach (var name in names)
        {
            string error = results.Get<string>(name);
            bool failed = error != "";
            bool expected = expectedFailures.Contains(name);
            Console.WriteLine((failed == expected ? "ok   " : "FAIL ") + name + (failed ? " failed" + (expected ? " as expected" : "") + ": " + error
                : expected ? " passed, but was expected to fail" : ""));
            if (failed != expected) failures++;
        }
        foreach (var name in expectedFailures)
        {
            if (!names.Contains(name))
            {
                Console.WriteLine("FAIL no check named " + name);
                failures++;
            }
        }
        env.Dispose();
        if (failures > 0)
        {
            Console.WriteLine("XLUA_OBFUSCATION_FIXTURE_FAIL: " + mode + ", " + failures + " problems");
            return 1;
        }
        Console.WriteLine("XLUA_OBFUSCATION_FIXTURE_PASS: " + mode + ", " + names.Count + " checks, " + expectedFailures.Count + " failing as expected");
        return 0;
    }
}
