using XLua;

// The types of Game/GameShapes.cs (asmdef and package layouts), patched from Lua
// and restored: [Hotfix] on a type, an Inline type and a generic method, all in
// Game.dll, outside xLua's assembly. HotfixPlayerProbe (hotfix_player.py) and
// HotfixIntKeyTests (hotfix_intkey.py) call Run after injection; without asmdefs
// (XLUA_TEST_HOTFIX_GAME_INLINE) there are no such types and Run returns null.
public static class HotfixShapes
{
    public const string Expected = "before 3,4,a patched 30,40,echo a restored 3,4,a";

    public static string Run(LuaEnv env)
    {
#if XLUA_TEST_HOTFIX_GAME_INLINE
        return null;
#else
        string before = Values();
        env.DoString(@"
            xlua.hotfix(CS.Game.TaggedShop, 'Price', function(self) return 30 end)
            xlua.hotfix(CS.Game.InlineShop, 'Price', function(self) return 40 end)
            xlua.hotfix(CS.Game.GenericShop, 'Echo', function(self, v) return 'echo ' .. v end)");
        string patched = Values();
        env.DoString(@"
            xlua.hotfix(CS.Game.TaggedShop, 'Price', nil)
            xlua.hotfix(CS.Game.InlineShop, 'Price', nil)
            xlua.hotfix(CS.Game.GenericShop, 'Echo', nil)");
        return "before " + before + " patched " + patched + " restored " + Values();
#endif
    }

#if !XLUA_TEST_HOTFIX_GAME_INLINE
    static string Values()
    {
        return new Game.TaggedShop().Price() + "," + new Game.InlineShop().Price() + "," + new Game.GenericShop().Echo("a");
    }
#endif
}
