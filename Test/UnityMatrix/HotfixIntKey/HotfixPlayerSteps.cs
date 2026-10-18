using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// hotfix_player.py: builds a macOS Mono player (ARM64 by default) with one scene that
// holds HotfixPlayerProbe. With HOTFIX_ENABLE, xLua's post-build step
// (IPostBuildPlayerScriptDLLs) injects the player's script assemblies.
// Usage: -executeMethod HotfixPlayerSteps.BuildMac -quit -xluaPlayerOut <path.app> [-xluaPlayerArch x64]
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
        // ARM64 by default (the rebuilt plugin is arm64 only); -xluaPlayerArch
        // x64 or x64ARM64 builds an Intel or universal player, for a project
        // with the universal xlua.bundle (hotfix_player.py --bundle).
        // UserBuildSettings.architecture has a different enum type in 2021.3
        // and Unity 6, so it is set by name.
        int a = Array.IndexOf(args, "-xluaPlayerArch");
        string arch = a >= 0 && a + 1 < args.Length ? args[a + 1] : "ARM64";
        var settings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                       ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor");
        var architecture = settings == null ? null : settings.GetProperty("architecture");
        if (architecture != null)
        {
            architecture.SetValue(null, Enum.Parse(architecture.PropertyType, arch));
        }
        Debug.Log("XLUA_HOTFIX_PLAYER_ARCH: " + (architecture == null ? "(not set)" : architecture.GetValue(null).ToString()));
        // The repository's import settings as they are: until 2026-10-18 the
        // dylib had CPU ARM64, which Unity 2021.3 copies to
        // Contents/PlugIns/ARM64, where its Mono player does not look
        // (DllNotFoundException); this project then set AnyCPU itself.
        var plugin = (PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/arm64/libxlua.dylib");
        Debug.Log("XLUA_HOTFIX_PLAYER_PLUGIN_CPU: " + (plugin == null ? "(no plugin)" : plugin.GetPlatformData(BuildTarget.StandaloneOSX, "CPU")));
        var bundle = (PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/xlua.bundle");
        Debug.Log("XLUA_HOTFIX_PLAYER_BUNDLE_CPU: " + (bundle == null ? "(no bundle)" : bundle.GetPlatformData(BuildTarget.StandaloneOSX, "CPU")));
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
