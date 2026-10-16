using System;
using System.IO;
using CSObjectWrapEditor;
using UnityEngine;

// Batch-mode entry points for editor_tests.py (-executeMethod ... -quit).
// Generated code only takes effect after Unity compiles it, so code
// generation and the tests that check it run in separate editor launches.
// Each step records the expected state in XluaEditorTestMode.txt for the
// next test run; without that file the tests expect plain reflection.
public static class XluaEditorTestSteps
{
    public const string Generated = "generated";
    public const string Cleared = "cleared";
    public const string Reflection = "reflection";

    public static string ModeFile
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "XluaEditorTestMode.txt")); }
    }

    public static string ReadMode()
    {
        return File.Exists(ModeFile) ? File.ReadAllText(ModeFile).Trim() : Reflection;
    }

    // The C# files in the generated code folder; none after ClearCode.
    public static string[] GeneratedFiles()
    {
        string dir = GeneratorConfig.common_path;
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories) : new string[0];
    }

    // Same entry as the "XLua/Generate Code" menu item.
    public static void GenerateCode()
    {
        Generator.GenAll();
        string register = Path.Combine(GeneratorConfig.common_path, "XLuaGenAutoRegister.cs");
        if (!File.Exists(register))
        {
            throw new InvalidOperationException("XLUA_EDITOR_STEP_FAIL: Generate Code did not write " + register);
        }
        File.WriteAllText(ModeFile, Generated + "\n");
        Debug.Log("XLUA_EDITOR_STEP_PASS: generated " + GeneratedFiles().Length
                  + " files in " + GeneratorConfig.common_path);
    }

    // Same entry as the "XLua/Clear Generated Code" menu item.
    public static void ClearCode()
    {
        Generator.ClearAll();
        if (GeneratedFiles().Length != 0)
        {
            throw new InvalidOperationException("XLUA_EDITOR_STEP_FAIL: Clear Generated Code left " + GeneratedFiles()[0]);
        }
        if (Directory.Exists(GeneratorConfig.common_path))
        {
            // Generator.clear passes "/XLua/Gen/" instead of "Assets/XLua/Gen" to
            // AssetDatabase.DeleteAsset, so Gen.meta stays and the refresh
            // recreates the folder empty.
            Debug.LogWarning("XLUA_EDITOR_STEP_NOTE: Clear Generated Code left an empty " + GeneratorConfig.common_path);
        }
        File.WriteAllText(ModeFile, Cleared + "\n");
        Debug.Log("XLUA_EDITOR_STEP_PASS: cleared " + GeneratorConfig.common_path);
    }
}
