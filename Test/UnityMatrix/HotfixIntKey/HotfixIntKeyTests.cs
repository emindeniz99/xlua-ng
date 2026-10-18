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
// hotfix_intkey.py --asmdef and --package define XLUA_TEST_HOTFIX_ASMDEF: xLua
// is then in XLua.dll, so without the source generator IntKey falls back to
// field mode in both assemblies (docs/MIGRATING.md, "xLua in its own
// assemblies"). --source-gen defines XLUA_TEST_HOTFIX_SOURCE_GEN: each
// assembly has the generator's HotfixBridges with IntKey slots of its own
// (milestone M7), so neither falls back, and the maps name their assembly.
public sealed class HotfixIntKeyTests
{
#if XLUA_TEST_HOTFIX_SOURCE_GEN
    const bool LocalIntKey = true;
    const bool GameIntKey = true;
#elif XLUA_TEST_HOTFIX_ASMDEF
    const bool LocalIntKey = false;
    const bool GameIntKey = false;
#else
    const bool LocalIntKey = true;
    const bool GameIntKey = false;
#endif

    static string MapDir
    {
        get { return CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/"; }
    }

    static int HotfixFields(System.Type type)
    {
        return type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Count(f => f.Name.StartsWith("__Hotfix"));
    }

    // A map file, or null when the injector wrote none.
    static string ReadMap(string name)
    {
        return File.Exists(MapDir + name) ? File.ReadAllText(MapDir + name) : null;
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
        Assert.AreEqual(!GameIntKey, warnings.Any(w => w.StartsWith("IntKey falls back to field mode in Game.dll for 1 type: Game.Shop")),
            "fallback warning for Game; got:\n" + string.Join("\n", warnings.ToArray()));
        Assert.AreEqual(!LocalIntKey, warnings.Any(w => w.StartsWith("IntKey falls back to field mode in Assembly-CSharp.dll for 1 type: HotfixIntKeyLocal")),
            "fallback warning for Assembly-CSharp; got:\n" + string.Join("\n", warnings.ToArray()));

        string local = ReadMap("hotfix_id_map_Assembly-CSharp.lua.txt");
        string game = ReadMap("hotfix_id_map_Game.lua.txt");
        string merged = ReadMap("hotfix_id_map.lua.txt");
        TestContext.WriteLine("XLUA_INTKEY_MAP_LOCAL:\n" + local + "\nXLUA_INTKEY_MAP_GAME:\n" + game
                              + "\nXLUA_INTKEY_MAP_MERGED:\n" + merged);
        if (GameIntKey)
        {
            StringAssert.Contains("[\"Game.Shop\"]", game);
            StringAssert.Contains("[\"<assembly>\"] = \"Game\"", game, "Game's own slots");
            StringAssert.Contains("Game.Shop", merged);
        }
        else
        {
            StringAssert.DoesNotContain("Game.Shop", game ?? "");
        }
        if (GameIntKey && LocalIntKey)
        {
            StringAssert.Contains("[\"<assembly>\"] = \"Assembly-CSharp\"", local, "Assembly-CSharp's own slots");
        }
        if (LocalIntKey)
        {
            StringAssert.Contains("[\"HotfixIntKeyLocal\"]", local);
            StringAssert.Contains("[\"Price\"]", local);
            StringAssert.Contains("HotfixIntKeyLocal", merged);
        }
        else
        {
            StringAssert.DoesNotContain("HotfixIntKeyLocal", local ?? "");
            StringAssert.DoesNotContain("HotfixIntKeyLocal", merged ?? "");
        }

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

        if (LocalIntKey)
        {
            Assert.AreEqual(0, localFields, "IntKey: no __Hotfix fields in HotfixIntKeyLocal");
        }
        else
        {
            Assert.Greater(localFields, 0, "field mode: __Hotfix fields in HotfixIntKeyLocal");
        }
        if (GameIntKey)
        {
            Assert.AreEqual(0, gameFields, "IntKey: no __Hotfix fields in Game.Shop");
        }
        else
        {
            Assert.Greater(gameFields, 0, "field mode: __Hotfix fields in Game.Shop");
        }
        Assert.AreEqual(2, beforeLocal);
        Assert.AreEqual(1, beforeGame);
        Assert.AreEqual(20, patchedLocal, "HotfixIntKeyLocal.Price patched");
        Assert.AreEqual(10, patchedGame, "Game.Shop.Price patched");
        Assert.AreEqual(2, restoredLocal);
        Assert.AreEqual(1, restoredGame);
    }
}
