using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// hotfix_il2cpp.py: builds an Android ARM64 IL2CPP player with one scene that
// holds HotfixRegionsProbe. With HOTFIX_ENABLE, xLua's post-build step
// (IPostBuildPlayerScriptDLLs) injects Assembly-CSharp before IL2CPP converts
// it. A release player by default: its scripts are compiled with
// optimizations, so a handler at the end of a constructor ends at its `ret`;
// in a Development player Roslyn puts a nop between them.
// -xluaBuildLog <file> also writes every message logged during the build to that file,
// for a launch whose editor log goes to stdout (GameCI's unity-builder in
// unity-matrix.yml); hotfix_il2cpp.py --verify-only checks it.
// Usage: -buildTarget Android -executeMethod HotfixRegionsSteps.BuildAndroid -quit -xluaPlayerOut <path.apk> [-xluaDevelopment] [-xluaBuildLog <file>]
public static class HotfixRegionsSteps
{
    public static void BuildAndroid()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-xluaPlayerOut");
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("-xluaPlayerOut <path.apk> is missing");
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
            Build(args, i);
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

    static void Build(string[] args, int i)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("HotfixRegionsProbe").AddComponent<HotfixRegionsProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/HotfixRegions.unity");
        var named = UnityEditor.Build.NamedBuildTarget.Android;
        PlayerSettings.companyName = "XluaNg";
        PlayerSettings.productName = "XluaHotfixRegions";
        PlayerSettings.SetApplicationIdentifier(named, "org.xluang.hotfixregions");
        PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
#if UNITY_6000_6_OR_NEWER
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
#else
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
#endif
        bool development = Array.IndexOf(args, "-xluaDevelopment") >= 0;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = development;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/HotfixRegions.unity" },
            target = BuildTarget.Android,
            locationPathName = args[i + 1],
            options = development ? BuildOptions.Development : BuildOptions.None,
        });
        Debug.Log("XLUA_HOTFIX_IL2CPP_BUILD: " + report.summary.result + " " + args[i + 1] + (development ? " (Development)" : " (release)"));
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("player build failed: " + report.summary.result);
    }
}
