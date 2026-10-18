// Second assembly of hotfix_check.py's source generator case
// (HotfixFixture.SecondSG.dll, docs/design/source-generator.md section 6,
// milestone M7). It references HotfixFixture.exe, which holds xLua, and is
// compiled with the xLua source generator, which writes its HotfixBridges:
// the injector then calls the bridges directly and gives IntKey types ids of
// this assembly's own slots. It is configured with [Hotfix] on the types,
// which the injector used to ignore outside the xLua assembly, and with the
// assembly-level forms: one type per attribute, and several types in one
// attribute, also with flags. HotfixFixture.exe loads it in its "multi-sg" mode.
using System;
using XLua;

[assembly: Hotfix(typeof(HotfixFixture.SecondSG.ByAssembly))]
[assembly: Hotfix(typeof(HotfixFixture.SecondSG.ByList), typeof(HotfixFixture.SecondSG.ByListToo))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(HotfixFixture.SecondSG.ByListKeyed), typeof(HotfixFixture.SecondSG.ByListKeyedToo))]

namespace HotfixFixture.SecondSG
{
    [Hotfix(HotfixFlag.IntKey)]
    public class RemoteKeyed
    {
        public int Neg(int a) { return a; }
        public string Name(string s) { return s; }
    }

    // Inline: patched through DelegateBridge's generic calls, which the
    // injector used to skip outside the xLua assembly.
    [Hotfix(HotfixFlag.Inline)]
    public class RemoteInline
    {
        public int Twice(int a) { return a; }
    }

    [Hotfix]
    public class RemoteGeneric
    {
        // A generic method takes the same generic path.
        public T Echo<T>(T value) { return value; }
        public int Plain(int a, int b) { return a - b; }
    }

    [Hotfix]
    public struct RemoteVec
    {
        public int X;
        public int Get() { return X; }
    }

    public class ByAssembly
    {
        public int Mul(int a, int b) { return a * b + 1; }
    }

    public class ByList
    {
        public int Add(int a, int b) { return a + b; }
    }

    public class ByListToo
    {
        public int Sub(int a, int b) { return a - b; }
    }

    public class ByListKeyed
    {
        public int Neg(int a) { return a; }
    }

    public class ByListKeyedToo
    {
        public string Name(string s) { return s; }
    }

    // C# `in` and C# 12 `ref readonly` parameters are inputs only: a patch's
    // extra Lua result is never written back into them, as it is into `ref`.
    // Only this assembly can declare them, because only Roslyn compiles it
    // (hotfix_check.py builds it at C# 12); mcs, which builds the main fixture
    // in most steps, compiles `in` as by value and has no `ref readonly`. The
    // parameters of the virtual methods carry modreq(InAttribute).
    [Hotfix]
    public class RemoteReadonlyArgs
    {
        public int In(in int x) { return x; }
        public int RefReadonly(ref readonly int x) { return x; }
        public int Ref(ref int x) { return x; }
        public virtual int InVirtual(in int x) { return x; }
        public virtual int RefReadonlyVirtual(ref readonly int x) { return x; }
    }

    [Hotfix(HotfixFlag.IntKey)]
    public class RemoteReadonlyArgsKeyed
    {
        public int In(in int x) { return x; }
        public int RefReadonly(ref readonly int x) { return x; }
        public int Ref(ref int x) { return x; }
        public virtual int InVirtual(in int x) { return x; }
        public virtual int RefReadonlyVirtual(ref readonly int x) { return x; }
    }

    [Hotfix(HotfixFlag.Inline)]
    public class RemoteReadonlyArgsInline
    {
        public int In(in int x) { return x; }
        public int RefReadonly(ref readonly int x) { return x; }
        public int Ref(ref int x) { return x; }
        public virtual int InVirtual(in int x) { return x; }
        public virtual int RefReadonlyVirtual(ref readonly int x) { return x; }
    }
}
