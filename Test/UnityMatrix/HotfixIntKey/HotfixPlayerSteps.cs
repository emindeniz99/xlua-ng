using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// hotfix_player.py: builds a Mono player with one scene that holds
// HotfixPlayerProbe: macOS (ARM64 by default) or, for unity-matrix.yml's Linux
// editors, Linux x86_64. With HOTFIX_ENABLE, xLua's post-build step
// (IPostBuildPlayerScriptDLLs) injects the player's script assemblies.
// -xluaBuildLog <file> also writes every message logged during the build to that file,
// for a launch whose editor log goes to stdout (GameCI's unity-builder in
// unity-matrix.yml); hotfix_player.py --verify-only checks it.
// Usage: -executeMethod HotfixPlayerSteps.BuildMac -quit -xluaPlayerOut <path.app> [-xluaPlayerArch x64] [-xluaBuildLog <file>]
//        -buildTarget Linux64 -executeMethod HotfixPlayerSteps.BuildLinux -quit -xluaPlayerOut <dir/XluaHotfixPlayer.x86_64> [-xluaBuildLog <file>]
public static class HotfixPlayerSteps
{
    public static void BuildMac()
    {
        WithBuildLog(args => Build(args, BuildTarget.StandaloneOSX));
    }

    public static void BuildLinux()
    {
        WithBuildLog(args => Build(args, BuildTarget.StandaloneLinux64));
    }

    static void WithBuildLog(Action<string[]> build)
    {
        string[] args = Environment.GetCommandLineArgs();
        int l = Array.IndexOf(args, "-xluaBuildLog");
        if (l >= 0 && l + 1 >= args.Length) throw new ArgumentException("-xluaBuildLog <file> is missing its file");
        StreamWriter log = l >= 0 ? new StreamWriter(args[l + 1], false) { AutoFlush = true } : null;
        Application.LogCallback write = (message, stack, type) =>
        {
            lock (log)
            {
                log.WriteLine(message);
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) log.WriteLine(stack);
            }
        };
        if (log != null) Application.logMessageReceivedThreaded += write;
        try
        {
            build(args);
        }
        finally
        {
            if (log != null)
            {
                Application.logMessageReceivedThreaded -= write;
                log.Dispose();
            }
        }
    }

    static void Build(string[] args, BuildTarget target)
    {
        int i = Array.IndexOf(args, "-xluaPlayerOut");
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("-xluaPlayerOut <path> is missing");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("HotfixPlayerProbe").AddComponent<HotfixPlayerProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/HotfixPlayer.unity");
        PlayerSettings.companyName = "XluaNg";
        PlayerSettings.productName = "XluaHotfixPlayer";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        if (target == BuildTarget.StandaloneOSX)
        {
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
        }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/HotfixPlayer.unity" },
            target = target,
            locationPathName = args[i + 1],
            options = BuildOptions.None,
        });
        Debug.Log("XLUA_HOTFIX_PLAYER_BUILD: " + report.summary.result + " " + args[i + 1]);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("player build failed: " + report.summary.result);
    }
}
