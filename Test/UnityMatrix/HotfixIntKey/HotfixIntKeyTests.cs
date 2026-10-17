using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;

// IntKey hotfix with two assemblies: HotfixIntKeyLocal in Assembly-CSharp (ids)
// and Game.Shop in the asmdef Game (field mode). Hotfix Inject In Editor, then
// play mode, which reloads the domain with the injected assemblies.
public sealed class HotfixIntKeyTests
{
    static string MapDir
    {
        get { return CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/"; }
    }

    static int HotfixFields(System.Type type)
    {
        return type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Count(f => f.Name.StartsWith("__Hotfix"));
    }

    [UnityTest]
    public IEnumerator InjectInEditorThenPatchBothAssembliesInPlayMode()
    {
        Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled, "play mode must reload the domain");
        var warnings = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type == LogType.Warning) warnings.Add(message);
        };
        Application.logMessageReceived += capture;
        try
        {
            Hotfix.HotfixInject();
        }
        finally
        {
            Application.logMessageReceived -= capture;
        }
        TestContext.WriteLine("XLUA_INTKEY_WARNINGS:\n" + string.Join("\n", warnings.ToArray()));
        Assert.IsTrue(warnings.Any(w => w.StartsWith("IntKey falls back to field mode in Game.dll for 1 type: Game.Shop")),
            "fallback warning; got:\n" + string.Join("\n", warnings.ToArray()));
        Assert.IsFalse(warnings.Any(w => w.Contains("Assembly-CSharp.dll for")), "no fallback warning for Assembly-CSharp");

        string local = File.ReadAllText(MapDir + "hotfix_id_map_Assembly-CSharp.lua.txt");
        string game = File.ReadAllText(MapDir + "hotfix_id_map_Game.lua.txt");
        string merged = File.ReadAllText(MapDir + "hotfix_id_map.lua.txt");
        TestContext.WriteLine("XLUA_INTKEY_MAP_LOCAL:\n" + local + "\nXLUA_INTKEY_MAP_GAME:\n" + game
                              + "\nXLUA_INTKEY_MAP_MERGED:\n" + merged);
        StringAssert.Contains("[\"HotfixIntKeyLocal\"]", local);
        StringAssert.Contains("[\"Price\"]", local);
        StringAssert.DoesNotContain("Game.Shop", game);
        StringAssert.Contains("HotfixIntKeyLocal", merged);

        yield return new EnterPlayMode();

        // The domain now has the injected assemblies.
        int localFields = HotfixFields(typeof(HotfixIntKeyLocal));
        int gameFields = HotfixFields(typeof(Game.Shop));
        int beforeLocal = new HotfixIntKeyLocal().Price();
        int beforeGame = new Game.Shop().Price();
        var env = new LuaEnv();
        env.DoString(@"
            require('xlua.util').auto_id_map()
            xlua.hotfix(CS.HotfixIntKeyLocal, 'Price', function(self) return 20 end)
            xlua.hotfix(CS.Game.Shop, 'Price', function(self) return 10 end)");
        int patchedLocal = new HotfixIntKeyLocal().Price();
        int patchedGame = new Game.Shop().Price();
        env.DoString(@"
            xlua.hotfix(CS.HotfixIntKeyLocal, 'Price', nil)
            xlua.hotfix(CS.Game.Shop, 'Price', nil)");
        int restoredLocal = new HotfixIntKeyLocal().Price();
        int restoredGame = new Game.Shop().Price();
        env.Dispose();
        TestContext.WriteLine("XLUA_INTKEY_RESULT: fields " + localFields + "," + gameFields + " before " + beforeLocal + ","
                              + beforeGame + " patched " + patchedLocal + "," + patchedGame
                              + " restored " + restoredLocal + "," + restoredGame);

        yield return new ExitPlayMode();

        Assert.AreEqual(0, localFields, "IntKey: no __Hotfix fields in HotfixIntKeyLocal");
        Assert.Greater(gameFields, 0, "field mode: __Hotfix fields in Game.Shop");
        Assert.AreEqual(2, beforeLocal);
        Assert.AreEqual(1, beforeGame);
        Assert.AreEqual(20, patchedLocal, "HotfixIntKeyLocal.Price patched through its IntKey id");
        Assert.AreEqual(10, patchedGame, "Game.Shop.Price patched through its field");
        Assert.AreEqual(2, restoredLocal);
        Assert.AreEqual(1, restoredGame);
    }
}
