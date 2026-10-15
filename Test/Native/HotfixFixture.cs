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
            Check(phase + " Vec.Sum", new Vec(3, 4).Sum(), 7);
            Check(phase + " Box<int>.Get", new Box<int>(9).Get(), 9);
            Check(phase + " Box<int>.Describe", new Box<int>(9).Describe(), "box");
            Check(phase + " Box<string>.Describe", new Box<string>("s").Describe(), "box");
            Check(phase + " Dog.Speak", new Dog().Speak(), "woof");
            Check(phase + " ByConfig.Mul", new ByConfig().Mul(2, 3), 7);
            Check(phase + " Keyed.Neg", new Keyed().Neg(5), 5);
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
            Check("patched Vec.Sum", new Vec(3, 4).Sum(), 12);
            Check("patched Box<int>.Get", new Box<int>(9).Get(), 9);
            Check("patched Box<int>.Describe", new Box<int>(9).Describe(), "int box 9");
            Check("unpatched Box<string>.Describe", new Box<string>("s").Describe(), "box");
            Check("patched Dog.Speak", new Dog().Speak(), "base said ...");
            Check("patched ByConfig.Mul", new ByConfig().Mul(2, 3), 6);
            Check("patched Keyed.Neg", new Keyed().Neg(5), -5);

            env.DoString(@"
                local F = CS.HotfixFixture
                for _, name in ipairs({'.ctor', 'Add', 'Twice', 'get_Value', 'set_Value', 'Bump'}) do
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
