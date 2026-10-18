using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// hotfix_player.py: builds a macOS ARM64 Mono player with one scene that
// holds HotfixPlayerProbe. With HOTFIX_ENABLE, xLua's post-build step
// (IPostBuildPlayerScriptDLLs) injects the player's script assemblies.
// Usage: -executeMethod HotfixPlayerSteps.BuildMac -quit -xluaPlayerOut <path.app>
public static class HotfixPlayerSteps
{
    public static void BuildMac()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-xluaPlayerOut");
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("-xluaPlayerOut <path.app> is missing");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("HotfixPlayerProbe").AddComponent<HotfixPlayerProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/HotfixPlayer.unity");
        PlayerSettings.companyName = "XluaNg";
        PlayerSettings.productName = "XluaHotfixPlayer";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        // The rebuilt plugin is arm64 only. UserBuildSettings.architecture has
        // a different enum type in 2021.3 and Unity 6, so it is set by name.
        var settings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                       ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor");
        var architecture = settings == null ? null : settings.GetProperty("architecture");
        if (architecture != null)
        {
            architecture.SetValue(null, Enum.Parse(architecture.PropertyType, "ARM64"));
        }
        Debug.Log("XLUA_HOTFIX_PLAYER_ARCH: " + (architecture == null ? "(not set)" : architecture.GetValue(null).ToString()));
        // With the repository's import settings (OSXUniversal, CPU ARM64) Unity
        // 2021.3 copies the plugin to Contents/PlugIns/ARM64, where the Mono
        // player does not look (DllNotFoundException, 2026-10-18). AnyCPU puts it
        // in Contents/PlugIns. Only this test project is changed;
        // XLUA_HOTFIX_PLAYER_KEEP_CPU=1 keeps the repository's setting.
        var plugin = (PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/arm64/libxlua.dylib");
        if (plugin != null && Environment.GetEnvironmentVariable("XLUA_HOTFIX_PLAYER_KEEP_CPU") != "1")
        {
            plugin.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
            plugin.SaveAndReimport();
        }
        Debug.Log("XLUA_HOTFIX_PLAYER_PLUGIN_CPU: " + (plugin == null ? "(no plugin)" : plugin.GetPlatformData(BuildTarget.StandaloneOSX, "CPU")));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/HotfixPlayer.unity" },
            target = BuildTarget.StandaloneOSX,
            locationPathName = args[i + 1],
            options = BuildOptions.None,
        });
        Debug.Log("XLUA_HOTFIX_PLAYER_BUILD: " + report.summary.result + " " + args[i + 1]);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("player build failed: " + report.summary.result);
    }
}
