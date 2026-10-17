// Second assembly for hotfix_check.py (HotfixFixture.Second.dll). It references
// HotfixFixture.exe, which holds xLua, and is injected with that assembly as
// the xLua assembly, like an asmdef or a UPM package next to Assembly-CSharp.
// IntKey works only in the xLua assembly, so the injector falls back to field
// mode here. HotfixFixture.exe loads this assembly in its
// "multi" mode and patches Remote by name after xlua.util.auto_id_map().
using System;
using System.Collections.Generic;
using XLua;

namespace HotfixFixture.Second
{
    public class Remote
    {
        public int Neg(int a) { return a; }
    }

    // A [Hotfix] attribute on a type of another assembly is not seen by the
    // injector, which compares it with the xLua assembly's own attribute type;
    // a configuration list is.
    public static class SecondHotfixCfg
    {
        [Hotfix(HotfixFlag.IntKey)]
        public static List<Type> types = new List<Type>() { typeof(Remote) };
    }
}
