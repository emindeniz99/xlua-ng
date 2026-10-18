using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XLua;

// hotfix_player.py: the scene object of a macOS player built with
// HOTFIX_ENABLE, where the post-build step injected Assembly-CSharp and Game.
// It patches HotfixIntKeyLocal.Price and Game.Shop.Price from Lua, restores
// them, logs one XLUA_HOTFIX_PLAYER_ line and quits.
public class HotfixPlayerProbe : MonoBehaviour
{
    static int HotfixFields(Type type)
    {
        return type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Count(f => f.Name.StartsWith("__Hotfix"));
    }

    static string Run()
    {
        int localFields = HotfixFields(typeof(HotfixIntKeyLocal)), gameFields = HotfixFields(typeof(Game.Shop));
        int beforeLocal = new HotfixIntKeyLocal().Price(), beforeGame = new Game.Shop().Price();
        var env = new LuaEnv();
        // IntKey types need the id map; field mode types do not.
        object idMap = env.DoString("return (pcall(function() require('xlua.util').auto_id_map() end))")[0];
        env.DoString(@"
            xlua.hotfix(CS.HotfixIntKeyLocal, 'Price', function(self) return 20 end)
            xlua.hotfix(CS.Game.Shop, 'Price', function(self) return 10 end)");
        int patchedLocal = new HotfixIntKeyLocal().Price(), patchedGame = new Game.Shop().Price();
        env.DoString(@"
            xlua.hotfix(CS.HotfixIntKeyLocal, 'Price', nil)
            xlua.hotfix(CS.Game.Shop, 'Price', nil)");
        int restoredLocal = new HotfixIntKeyLocal().Price(), restoredGame = new Game.Shop().Price();
        string values = "fields " + localFields + "," + gameFields + " idmap " + idMap + " before " + beforeLocal + "," + beforeGame
                        + " patched " + patchedLocal + "," + patchedGame + " restored " + restoredLocal + "," + restoredGame;
        // With the source generator (XLUA_TEST_HOTFIX_SOURCE_GEN) Game.Shop uses IntKey slots, otherwise field mode.
#if XLUA_TEST_HOTFIX_SOURCE_GEN
        bool gameMode = gameFields == 0 && localFields == 0 && true.Equals(idMap);
#else
        bool gameMode = gameFields > 0;
#endif
        bool ok = gameMode && beforeLocal == 2 && beforeGame == 1 && patchedLocal == 20 && patchedGame == 10
                  && restoredLocal == 2 && restoredGame == 1;
        return (ok ? "XLUA_HOTFIX_PLAYER_PASS: " : "XLUA_HOTFIX_PLAYER_FAIL: ") + values;
    }

    void Start()
    {
        string result;
        try
        {
            result = Run();
        }
        catch (Exception e)
        {
            result = "XLUA_HOTFIX_PLAYER_FAIL: " + e;
        }
        Debug.Log(result);
        Application.Quit(result.StartsWith("XLUA_HOTFIX_PLAYER_PASS") ? 0 : 1);
    }
}
