// Method shapes for hotfix injection around exception handling regions and
// long branches (Tencent/xLua#1064). Compiled into the hotfix_check.py fixture
// (with HotfixFixture.cs, which runs RegionShapes under Mono) and copied into
// the IL2CPP player of Test/UnityMatrix/hotfix_il2cpp.py (HotfixRegionsProbe.cs).
//
// A constructor hotfix is injected before every `ret`. When a try block ends
// the constructor, its handler ends at that `ret`, and the hotfix code placed
// before it used to become the end of the handler: the `leave` out of the try
// block then jumped into the handler. Mono runs that; IL2CPP (Unity 2021 and
// later) fails to compile the generated C++ ("use of undeclared label").
//
// Each shape returns an int; RegionShapes lists the values before patching,
// after the Lua patch below and after it is removed.
using System;
using System.Collections.Generic;
using XLua;

namespace HotfixFixture
{
    public static class RegionHits
    {
        public static int Count;

        // IL2CPP leaves out the divide-by-zero check by default (Il2CppSetOption
        // DivideByZeroChecks), and ARM64 does not trap: 100 / 0 gives 0 there.
        public static int Divide(int a, int b)
        {
            if (b == 0) throw new DivideByZeroException();
            return a / b;
        }
    }

    // Tencent/xLua#1064 as reported: a field initialiser before the base
    // constructor call, then an `if` whose body is a foreach over a List<T>
    // (a try/finally that ends at the constructor's `ret`).
    [Hotfix]
    public class TailForeach
    {
        public int Sum;
        readonly List<int> seen = new List<int>();
        public TailForeach(List<int> items)
        {
            seen.Add(-1);
            if (items != null)
            {
                foreach (var x in items)
                {
                    if (x > 2) Sum += x;
                }
            }
        }
    }

    // A catch handler that ends at the `ret`.
    [Hotfix]
    public class TailCatch
    {
        public int Result;
        public TailCatch(int n)
        {
            try
            {
                Result = RegionHits.Divide(100, n);
            }
            catch (DivideByZeroException)
            {
                Result = -1;
            }
        }
    }

    // A try/finally inside a try/catch with a filter, both ending at the `ret`.
    [Hotfix]
    public class TailNested
    {
        public int Result;
        public TailNested(int n)
        {
            try
            {
                try
                {
                    Result = RegionHits.Divide(100, n);
                }
                finally
                {
                    Result += 1;
                }
            }
            catch (DivideByZeroException) when (n == 0)
            {
                Result -= 10;
            }
        }
    }

    // A struct constructor: a switch, then a try/finally that ends at the `ret`.
    // The hotfix gets a boxed copy of the struct, so the patch counts calls.
    [Hotfix]
    public struct TailStruct
    {
        public int Value;
        public TailStruct(int k)
        {
            switch (k)
            {
                case 0: Value = 10; break;
                case 1: Value = 11; break;
                case 2: Value = 12; break;
                case 3: Value = 13; break;
                default: Value = -1; break;
            }
            try
            {
                Value *= 2;
            }
            finally
            {
                Value += 1;
            }
        }
    }

    // The generic path of the injector (injectGenericMethod): a foreach over an
    // IEnumerable<T> (try/finally with a null check) at the end.
    [Hotfix]
    public class GenericTail<T>
    {
        public int Count;
        public GenericTail(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                Count++;
            }
        }
    }

    // A switch with an early `return` (two `ret`s), a body of more than 127
    // bytes of IL and a try/finally at the end.
    [Hotfix]
    public class LongSwitch
    {
        public int Result;
        public LongSwitch(int k)
        {
            switch (k)
            {
                case 0: Result = 1; break;
                case 1: Result = 2; break;
                case 2: Result = 3; break;
                case 3: Result = 4; break;
                case 4: Result = 5; return;
                case 5: Result = 6; break;
                default: Result = 7; break;
            }
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            Result += 1000;
            try
            {
                Result += 100000;
            }
            finally
            {
                Result += 1000000;
            }
        }
    }

    // Ordinary methods: the hotfix goes in front of the first instruction.
    [Hotfix]
    public class RegionMethods
    {
        // The body starts with a try block (instruction 0 is its start).
        public static int TryFirst(int n)
        {
            try
            {
                if (n < 0) throw new ArgumentException("negative");
                return n * 2;
            }
            catch (ArgumentException)
            {
                return -1;
            }
            finally
            {
                RegionHits.Count += 1;
            }
        }

        // Instruction 0 is the target of the loop's backward branch.
        public int LoopFirst(int n)
        {
            do
            {
                n -= 3;
            }
            while (n > 0);
            return n;
        }

        // A switch table whose cases are more than 127 bytes away, inside a try block.
        public static int Switch(int k)
        {
            int r = 0;
            try
            {
                switch (k)
                {
                    case 0: r = 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 1: r = 2; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 2: r = 3; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 3: r = 4; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 4: r = 5; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 5: r = 6; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    case 6: r = 7; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; r = r * 3 + 1; break;
                    default: r = -1; break;
                }
            }
            finally
            {
                r += 1000;
            }
            return r;
        }
    }

    public static class RegionShapes
    {
        // check(name, make, expected): make throws or returns the wrong value
        // when the injected IL is wrong.
        public static void Check(string phase, Action<string, Func<int>, int> check, bool patched)
        {
            int p = patched ? 1 : 0;
            check(phase + " TailForeach", () => new TailForeach(new List<int> { 1, 3, 5 }).Sum, 8 + 1000 * p);
            check(phase + " TailForeach null", () => new TailForeach(null).Sum, 1000 * p);
            check(phase + " TailCatch", () => new TailCatch(5).Result, 20 + 1000 * p);
            check(phase + " TailCatch caught", () => new TailCatch(0).Result, -1 + 1000 * p);
            check(phase + " TailNested", () => new TailNested(5).Result, 21 + 1000 * p);
            check(phase + " TailNested caught", () => new TailNested(0).Result, -9 + 1000 * p);
            check(phase + " TailStruct", () =>
            {
                int before = RegionHits.Count;
                return new TailStruct(2).Value * 100 + RegionHits.Count - before;
            }, 2500 + 2 * p);
            check(phase + " TailStruct default", () => new TailStruct(9).Value, -1);
            check(phase + " GenericTail<int>", () => new GenericTail<int>(new[] { 4, 5, 6 }).Count, 3 + 1000 * p);
            check(phase + " LongSwitch", () => new LongSwitch(2).Result, patched ? -1110003 : 1110003);
            check(phase + " LongSwitch return", () => new LongSwitch(4).Result, patched ? -5 : 5);
            check(phase + " LongSwitch default", () => new LongSwitch(9).Result, patched ? -1110007 : 1110007);
            check(phase + " RegionMethods.TryFirst", () =>
            {
                int before = RegionHits.Count;
                return RegionMethods.TryFirst(3) * 10 + RegionHits.Count - before;
            }, patched ? 30000 : 61);
            check(phase + " RegionMethods.TryFirst caught", () => RegionMethods.TryFirst(-1), patched ? -1000 : -1);
            check(phase + " RegionMethods.LoopFirst", () => new RegionMethods().LoopFirst(10), patched ? 20 : -2);
            check(phase + " RegionMethods.Switch", () => RegionMethods.Switch(2), patched ? 2 : 1283);
            check(phase + " RegionMethods.Switch default", () => RegionMethods.Switch(9), patched ? 9 : 999);
        }

        public const string Patch = @"
            local F = CS.HotfixFixture
            xlua.hotfix(F.TailForeach, '.ctor', function(self) self.Sum = self.Sum + 1000 end)
            xlua.hotfix(F.TailCatch, '.ctor', function(self) self.Result = self.Result + 1000 end)
            xlua.hotfix(F.TailNested, '.ctor', function(self) self.Result = self.Result + 1000 end)
            xlua.hotfix(F.TailStruct, '.ctor', function(self, k) F.RegionHits.Count = F.RegionHits.Count + k end)
            xlua.hotfix(F.GenericTail(CS.System.Int32), '.ctor', function(self) self.Count = self.Count + 1000 end)
            xlua.hotfix(F.LongSwitch, '.ctor', function(self) self.Result = -self.Result end)
            xlua.hotfix(F.RegionMethods, {
                TryFirst = function(n) return n * 1000 end;
                LoopFirst = function(self, n) return n * 2 end;
                Switch = function(k) return k end;
            })
        ";

        public const string Unpatch = @"
            local F = CS.HotfixFixture
            for _, t in ipairs({F.TailForeach, F.TailCatch, F.TailNested, F.TailStruct, F.GenericTail(CS.System.Int32), F.LongSwitch}) do
                xlua.hotfix(t, '.ctor', nil)
            end
            xlua.hotfix(F.RegionMethods, {TryFirst = false, LoopFirst = false, Switch = false})
        ";
    }
}
