using System;
using UnityEngine;
using XLua;

// hotfix_il2cpp.py: the scene object of an IL2CPP player built with
// HOTFIX_ENABLE, where xLua's post-build step injected Assembly-CSharp with
// the shapes of Test/Native/HotfixRegions.cs (Tencent/xLua#1064). It runs
// them before patching, patched from Lua and restored, logs one
// XLUA_HOTFIX_REGIONS_ line, writes it to xlua-hotfix-regions.txt under
// Application.persistentDataPath and quits.
public class HotfixRegionsProbe : MonoBehaviour
{
    static int checks, failures;

    static void Check(string name, Func<int> make, int expected)
    {
        checks++;
        string actual;
        try
        {
            int value = make();
            actual = value.ToString();
            if (value == expected) return;
        }
        catch (Exception e)
        {
            actual = e.GetType().Name + ": " + e.Message;
        }
        failures++;
        Debug.Log("XLUA_HOTFIX_REGIONS_CHECK_FAIL " + name + " = " + actual + ", expected " + expected);
    }

    static string Run()
    {
        HotfixFixture.RegionShapes.Check("original", Check, false);
        var env = new LuaEnv();
        env.DoString(HotfixFixture.RegionShapes.Patch);
        HotfixFixture.RegionShapes.Check("patched", Check, true);
        env.DoString(HotfixFixture.RegionShapes.Unpatch);
        HotfixFixture.RegionShapes.Check("restored", Check, false);
        env.Dispose();
        int fields = typeof(HotfixFixture.TailForeach).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Length;
        return (failures == 0 && fields > 0 ? "XLUA_HOTFIX_REGIONS_PASS: " : "XLUA_HOTFIX_REGIONS_FAIL: ")
               + (checks - failures) + " of " + checks + " checks, TailForeach hotfix fields " + fields
               + ", " + Application.unityVersion + " " + Application.platform;
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
            result = "XLUA_HOTFIX_REGIONS_FAIL: " + e;
        }
        Debug.Log(result);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "xlua-hotfix-regions.txt"), result + "\n");
        }
        catch (Exception e)
        {
            Debug.Log("XLUA_HOTFIX_REGIONS_RESULT_FILE: " + e.Message);
        }
        Application.Quit(result.StartsWith("XLUA_HOTFIX_REGIONS_PASS") ? 0 : 1);
    }
}
