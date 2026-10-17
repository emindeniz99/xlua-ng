using System;
using System.Collections.Generic;
using XLua;

// The hotfix configuration, under an Editor folder as Hotfix_EN.md asks for
// assemblies other than Assembly-CSharp: the Editor passes it to the injector
// for every injected assembly (hotfix_cfg_in_editor.data).
public static class HotfixIntKeyConfig
{
    [Hotfix(HotfixFlag.IntKey)]
    public static List<Type> Types = new List<Type>
    {
        typeof(HotfixIntKeyLocal),
        typeof(Game.Shop),
    };
}
