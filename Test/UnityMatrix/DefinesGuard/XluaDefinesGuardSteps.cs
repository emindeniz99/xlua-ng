// The build step of defines_guard.py: optionally set the Player Settings defines
// of Standalone, then build a macOS Mono player in the same launch.
//
// Usage: -executeMethod XluaDefinesGuardSteps.Build -quit -xluaPlayerOut <path.app>
//        [-xluaSetDefines <a;b>] (set them first; "-" for none) [-xluaExtraDefines <a;b>]
//
// Logs XLUA_DEFINES_GUARD_LINKXML (the preserve="all" types of the generator's
// link.xml as the Editor's assemblies give it) and XLUA_DEFINES_GUARD_BUILD
// (the result). A failed build is not an error here: defines_guard.py decides.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class XluaDefinesGuardSteps
{
    static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = Arg(args, "-xluaPlayerOut");
        if (output == null) throw new ArgumentException("-xluaPlayerOut <path> is missing");
        string linkXml = XLua.SourceGenChecks.MergedLinkXml();
        Debug.Log("XLUA_DEFINES_GUARD_LINKXML: " + Regex.Matches(linkXml, "preserve=\"all\"").Count + " types kept whole, compiled with "
            + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone));

        string set = Arg(args, "-xluaSetDefines");
        if (set != null)
        {
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, set == "-" ? "" : set);
            AssetDatabase.SaveAssets();
            Debug.Log("XLUA_DEFINES_GUARD_SET: " + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone));
        }
        string extra = Arg(args, "-xluaExtraDefines");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/XluaDefinesGuard.unity");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        // ARM64 (the rebuilt plugin is arm64 only); the property's enum differs
        // between 2021.3 and Unity 6, so it is set by name.
        var settings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                       ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor");
        var architecture = settings == null ? null : settings.GetProperty("architecture");
        if (architecture != null) architecture.SetValue(null, Enum.Parse(architecture.PropertyType, "ARM64"));

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/XluaDefinesGuard.unity" },
            target = BuildTarget.StandaloneOSX,
            locationPathName = output,
            options = BuildOptions.None,
            extraScriptingDefines = extra == null ? null : extra.Split(';').Where(d => d.Length > 0).ToArray(),
        });
        Debug.Log("XLUA_DEFINES_GUARD_BUILD: " + report.summary.result);
    }
}
