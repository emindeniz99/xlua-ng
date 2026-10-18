// Second assembly of hotfix_check.py's source generator case
// (HotfixFixture.SecondSG.dll, docs/design/source-generator.md section 6,
// milestone M7). It references HotfixFixture.exe, which holds xLua, and is
// compiled with the xLua source generator, which writes its HotfixBridges:
// the injector then calls the bridges directly and gives IntKey types ids of
// this assembly's own slots. It is configured with [Hotfix] on the types,
// which the injector used to ignore outside the xLua assembly, and with the
// assembly-level form. HotfixFixture.exe loads it in its "multi-sg" mode.
using System;
using XLua;

[assembly: Hotfix(typeof(HotfixFixture.SecondSG.ByAssembly))]

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
}
