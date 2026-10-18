// Second assembly for hotfix_check.py (HotfixFixture.Second.dll), built by mcs
// without the xLua source generator. It references HotfixFixture.exe, which
// holds xLua, and is injected with that assembly as the xLua assembly. Its only
// hotfix type is named by a configuration list, which the injector reads but
// the source generator does not, so there are no hotfix bridges for it since
// the legacy generator went (milestone M11): injection must stop and say to
// convert the list. (HotfixFixture.exe's "multi" mode, which patched Remote
// by name with Generate Code's bridges, is not run any more.)
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
