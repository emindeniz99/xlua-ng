using System;
using System.Collections.Generic;
using XLua;

// --no-asmdef: xLua is in Assembly-CSharp, which no asmdef can reference, so
// the asmdef Game (NoAsmdef/Game.asmdef, without references) cannot carry
// [Hotfix] or get the source generator's HotfixBridges. A [Hotfix] list under
// an Editor folder still selects Game.Shop for injection (the Editor passes it
// to the injector, hotfix_cfg_in_editor.data), and HotfixFlag.Inline injects
// it without bridges: field mode, patched by name. Without Inline the injector
// refuses it ("can not find delegate ...: the type is configured only by a
// [Hotfix] configuration list"; hotfix_check.py covers that on the host).
public static class HotfixIntKeyInline
{
    [Hotfix(HotfixFlag.Inline)]
    public static List<Type> Types = new List<Type>
    {
        typeof(Game.Shop),
    };
}
