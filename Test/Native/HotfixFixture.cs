// Fixture for hotfix_check.py. Compiled together with the xLua sources and
// generated wrappers (XLUA_GENERAL, HOTFIX_ENABLE), instrumented by
// XLuaHotfixInject, then run under Mono.
//
//   HotfixFixture.exe baseline          checks the original C# behaviour only
//   HotfixFixture.exe patch <id map>    also patches from Lua and removes the patches
//
// Every run prints "SYMBOLS: present" or "SYMBOLS: missing" (whether the stack
// trace of an exception thrown in an instrumented method has this file's line
// number; run under `mono --debug`) and ends with HOTFIX_FIXTURE_PASS or exits 1.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using XLua;

namespace HotfixFixture
{
    public class MarkedException : Exception
    {
        public readonly int Line;
        public MarkedException(int line) : base("thrown at line " + line) { Line = line; }
        public static int Here([CallerLineNumber] int line = 0) { return line; }
    }

    [Hotfix]
    public class Calc
    {
        public int Built;
        public Calc() { Built = 1; }

        public int Add(int a, int b) { return a - b; }
        public static int Twice(int a) { return a; }

        int value;
        public int Value { get { return value; } set { this.value = value + 1; } }

        public int Bump(ref int counter, out int extra) { counter += 1; extra = 7; return counter; }

        // [In] ref is a normal ref ([In] is only a marshaling hint): like Poke,
        // a patch's extra result is written back. (A real `in` parameter cannot
        // be tested here: mcs compiles the generated `in` bridge as by-value.)
        public int Peek([System.Runtime.InteropServices.In] ref int value) { return value; }
        public int Poke(ref int value) { value += 1; return value; }

        public int Fail() { throw new MarkedException(MarkedException.Here()); }
    }

    [Hotfix]
    public struct Vec
    {
        public int X, Y;
        public Vec(int x, int y) { X = x; Y = y; }
        public int Sum() { return X + Y; }
    }

    [Hotfix]
    public class Box<T>
    {
        T item;
        public Box(T item) { this.item = item; }
        public T Get() { return item; }
        public string Describe() { return "box"; }
    }

    public class Animal
    {
        public virtual string Speak() { return "..."; }
    }

    [Hotfix]
    public class Dog : Animal
    {
        public override string Speak() { return "woof"; }
    }

    // Selected through a configuration list instead of an attribute.
    public class ByConfig
    {
        public int Mul(int a, int b) { return a * b + 1; }
    }

    public static class HotfixCfg
    {
        [Hotfix]
        public static List<Type> types = new List<Type>() { typeof(ByConfig) };
    }

    [Hotfix(HotfixFlag.IntKey)]
    public class Keyed
    {
        public int Neg(int a) { return a; }
    }

    // Tencent/xLua#1003: a constructor hotfix is injected before every `ret`,
    // so branches across a `ret` in the middle of a constructor grow. LoopN has
    // a backward branch and FwdN a forward one across such a `ret`; N is the
    // padding in bytes. Once hotfix_check.py has shortened the branches as
    // Roslyn does (ShortBranches.cs), injection moves them across the short
    // branch limits (-128 and +127 bytes): Loop72-76 with the long forms the
    // injector inserts, Loop82-86 and Fwd101-106 with Cecil's short forms.
    [Hotfix]
    public class Loop72
    {
        public int Result;
        public Loop72(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
            }
        }
    }

    [Hotfix]
    public class Loop73
    {
        public int Result;
        public Loop73(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Loop74
    {
        public int Result;
        public Loop74(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
            }
        }
    }

    [Hotfix]
    public class Loop75
    {
        public int Result;
        public Loop75(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                pad += 100;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Loop76
    {
        public int Result;
        public Loop76(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Loop82
    {
        public int Result;
        public Loop82(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
            }
        }
    }

    [Hotfix]
    public class Loop83
    {
        public int Result;
        public Loop83(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                pad += 100;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Loop84
    {
        public int Result;
        public Loop84(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Loop85
    {
        public int Result;
        public Loop85(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
            }
        }
    }

    [Hotfix]
    public class Loop86
    {
        public int Result;
        public Loop86(int n)
        {
            int pad = 0;
            int i = 0;
            while (true)
            {
                i++;
                if (i >= n)
                {
                    Result = i * 1000 + pad;
                    return;
                }
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                pad += 1;
            }
        }
    }

    [Hotfix]
    public class Fwd101
    {
        public int Result;
        public Fwd101(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    [Hotfix]
    public class Fwd102
    {
        public int Result;
        public Fwd102(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                pad += 1;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    [Hotfix]
    public class Fwd103
    {
        public int Result;
        public Fwd103(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                pad += 100;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    [Hotfix]
    public class Fwd104
    {
        public int Result;
        public Fwd104(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    [Hotfix]
    public class Fwd105
    {
        public int Result;
        public Fwd105(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 1;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    [Hotfix]
    public class Fwd106
    {
        public int Result;
        public Fwd106(int n)
        {
            int pad = 0;
            if (n > 0)
            {
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100000;
                pad += 100;
                pad += 100;
                Result = pad;
                return;
            }
            Result = -1;
        }
    }

    public static class Program
    {
        static int failures;
        static int checks;

        static void Check<T>(string name, T actual, T expected)
        {
            checks++;
            if (EqualityComparer<T>.Default.Equals(actual, expected))
            {
                Console.WriteLine("ok   " + name + " = " + actual);
            }
            else
            {
                failures++;
                Console.WriteLine("FAIL " + name + " = " + actual + ", expected " + expected);
            }
        }

        static void CheckOriginal(string phase)
        {
            var calc = new Calc();
            Check(phase + " Calc..ctor", calc.Built, 1);
            Check(phase + " Calc.Add", calc.Add(2, 4), -2);
            Check(phase + " Calc.Twice", Calc.Twice(5), 5);
            calc.Value = 3;
            Check(phase + " Calc.Value", calc.Value, 4);
            int counter = 1, extra;
            Check(phase + " Calc.Bump", calc.Bump(ref counter, out extra), 2);
            Check(phase + " Calc.Bump ref", counter, 2);
            Check(phase + " Calc.Bump out", extra, 7);
            int peeked = 4, poked = 4;
            Check(phase + " Calc.Peek", calc.Peek(ref peeked), 4);
            Check(phase + " Calc.Peek ref", peeked, 4);
            Check(phase + " Calc.Poke", calc.Poke(ref poked), 5);
            Check(phase + " Calc.Poke ref", poked, 5);
            Check(phase + " Vec.Sum", new Vec(3, 4).Sum(), 7);
            Check(phase + " Box<int>.Get", new Box<int>(9).Get(), 9);
            Check(phase + " Box<int>.Describe", new Box<int>(9).Describe(), "box");
            Check(phase + " Box<string>.Describe", new Box<string>("s").Describe(), "box");
            Check(phase + " Dog.Speak", new Dog().Speak(), "woof");
            Check(phase + " ByConfig.Mul", new ByConfig().Mul(2, 3), 7);
            Check(phase + " Keyed.Neg", new Keyed().Neg(5), 5);
            CheckBranches(phase);
        }

        // Invalid IL after injection shows as InvalidProgramException or a wrong result.
        static void CheckBranch(string phase, string name, Func<int> make, int expected)
        {
            int actual;
            try
            {
                actual = make();
            }
            catch (Exception e)
            {
                checks++;
                failures++;
                Console.WriteLine("FAIL " + phase + " " + name + ": " + e.GetType().Name + ": " + e.Message);
                return;
            }
            Check(phase + " " + name, actual, expected);
        }

        static void CheckBranches(string phase)
        {
            CheckBranch(phase, "Loop72", () => new Loop72(3).Result, 1803000);
            CheckBranch(phase, "Loop73", () => new Loop73(3).Result, 1603202);
            CheckBranch(phase, "Loop74", () => new Loop74(3).Result, 1603400);
            CheckBranch(phase, "Loop75", () => new Loop75(3).Result, 1403602);
            CheckBranch(phase, "Loop76", () => new Loop76(3).Result, 1803002);
            CheckBranch(phase, "Loop82", () => new Loop82(3).Result, 1803400);
            CheckBranch(phase, "Loop83", () => new Loop83(3).Result, 1603602);
            CheckBranch(phase, "Loop84", () => new Loop84(3).Result, 2003002);
            CheckBranch(phase, "Loop85", () => new Loop85(3).Result, 2003200);
            CheckBranch(phase, "Loop86", () => new Loop86(3).Result, 1803402);
            CheckBranch(phase, "Fwd101", () => new Fwd101(1).Result, 1200100);
            CheckBranch(phase, "Fwd101 skip", () => new Fwd101(0).Result, -1);
            CheckBranch(phase, "Fwd102", () => new Fwd102(1).Result, 1100201);
            CheckBranch(phase, "Fwd102 skip", () => new Fwd102(0).Result, -1);
            CheckBranch(phase, "Fwd103", () => new Fwd103(1).Result, 1100300);
            CheckBranch(phase, "Fwd103 skip", () => new Fwd103(0).Result, -1);
            CheckBranch(phase, "Fwd104", () => new Fwd104(1).Result, 1300000);
            CheckBranch(phase, "Fwd104 skip", () => new Fwd104(0).Result, -1);
            CheckBranch(phase, "Fwd105", () => new Fwd105(1).Result, 1200101);
            CheckBranch(phase, "Fwd105 skip", () => new Fwd105(0).Result, -1);
            CheckBranch(phase, "Fwd106", () => new Fwd106(1).Result, 1200200);
            CheckBranch(phase, "Fwd106 skip", () => new Fwd106(0).Result, -1);
        }

        static void CheckSymbols()
        {
            try
            {
                new Calc().Fail();
            }
            catch (MarkedException e)
            {
                string marker = "HotfixFixture.cs:" + e.Line;
                Console.WriteLine("SYMBOLS: " + (e.StackTrace.Contains(marker) ? "present" : "missing"));
                Console.WriteLine(e.StackTrace);
            }
        }

        static void Patch(LuaEnv env, string idMapPath)
        {
            env.Global.Set("id_map_path", idMapPath);
            env.DoString(@"
                local F = CS.HotfixFixture
                xlua.hotfix(F.Calc, '.ctor', function(self) self.Built = self.Built + 10 end)
                xlua.hotfix(F.Calc, 'Add', function(self, a, b) return a + b end)
                xlua.hotfix(F.Calc, 'Twice', function(a) return a * 2 end)
                xlua.hotfix(F.Calc, {
                    get_Value = function(self) return 100 end;
                    set_Value = function(self, v) end;
                })
                xlua.hotfix(F.Calc, 'Bump', function(self, counter) return counter * 10, counter + 5, 8 end)
                xlua.hotfix(F.Calc, 'Peek', function(self, value) return value * 3, 99 end)
                xlua.hotfix(F.Calc, 'Poke', function(self, value) return value * 2, value + 100 end)
                xlua.hotfix(F.Vec, 'Sum', function(self) return self.X * self.Y end)
                xlua.hotfix(F.Box(CS.System.Int32), 'Describe', function(self) return 'int box ' .. self:Get() end)
                xlua.hotfix(F.Dog, 'Speak', function(self) return 'base said ' .. base(self):Speak() end)
                xlua.hotfix(F.ByConfig, 'Mul', function(self, a, b) return a * b end)
                local id = dofile(id_map_path)['HotfixFixture.Keyed']['Neg'][1]
                CS.XLua.HotfixDelegateBridge.Set(id, function(self, a) return -a end)
            ");
            var calc = new Calc();
            Check("patched Calc..ctor", calc.Built, 11);
            Check("patched Calc.Add", calc.Add(2, 4), 6);
            Check("patched Calc.Twice", Calc.Twice(5), 10);
            calc.Value = 3;
            Check("patched Calc.Value", calc.Value, 100);
            int counter = 1, extra;
            Check("patched Calc.Bump", calc.Bump(ref counter, out extra), 10);
            Check("patched Calc.Bump ref", counter, 6);
            Check("patched Calc.Bump out", extra, 8);
            int peeked = 4, poked = 4;
            Check("patched Calc.Peek", calc.Peek(ref peeked), 12);
            Check("patched Calc.Peek ref", peeked, 99);
            Check("patched Calc.Poke", calc.Poke(ref poked), 8);
            Check("patched Calc.Poke ref", poked, 104);
            Check("patched Vec.Sum", new Vec(3, 4).Sum(), 12);
            Check("patched Box<int>.Get", new Box<int>(9).Get(), 9);
            Check("patched Box<int>.Describe", new Box<int>(9).Describe(), "int box 9");
            Check("unpatched Box<string>.Describe", new Box<string>("s").Describe(), "box");
            Check("patched Dog.Speak", new Dog().Speak(), "base said ...");
            Check("patched ByConfig.Mul", new ByConfig().Mul(2, 3), 6);
            Check("patched Keyed.Neg", new Keyed().Neg(5), -5);

            env.DoString(@"
                local F = CS.HotfixFixture
                for _, name in ipairs({'.ctor', 'Add', 'Twice', 'get_Value', 'set_Value', 'Bump', 'Peek', 'Poke'}) do
                    xlua.hotfix(F.Calc, name, nil)
                end
                xlua.hotfix(F.Vec, 'Sum', nil)
                xlua.hotfix(F.Box(CS.System.Int32), 'Describe', nil)
                xlua.hotfix(F.Dog, 'Speak', nil)
                xlua.hotfix(F.ByConfig, 'Mul', nil)
                local id = dofile(id_map_path)['HotfixFixture.Keyed']['Neg'][1]
                CS.XLua.HotfixDelegateBridge.Set(id, nil)
            ");
        }

        public static int Main(string[] args)
        {
            CheckOriginal("original");
            CheckSymbols();
            if (args.Length > 0 && args[0] == "patch")
            {
                var env = new LuaEnv();
                Patch(env, args[1]);
                CheckOriginal("restored");
                env.Dispose();
            }
            if (failures > 0)
            {
                Console.WriteLine("HOTFIX_FIXTURE_FAIL: " + failures + " of " + checks + " checks failed");
                return 1;
            }
            Console.WriteLine("HOTFIX_FIXTURE_PASS: " + checks + " checks");
            return 0;
        }
    }
}
