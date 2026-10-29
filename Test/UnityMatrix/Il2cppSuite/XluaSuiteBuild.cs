// Builds the player of Test/UnityMatrix/il2cpp_suite.py: one scene with
// XluaSuiteRunner, Android IL2CPP, release (no Development flag). The settings
// follow Test/MobileSmoke/SmokeBuild.cs.
//
// il2cpp_suite.py configure writes XluaSuiteConfig.txt next to Assets, one
// key=value per line: defines (the Player Settings scripting defines: the
// preset define, GEN_CODE_MINIMIZE), arch (ARM64 or X86_64) and stripping
// (Minimal unless it says otherwise). Run ApplyDefines in a launch of its own
// first, so that the build launch compiles the scripts and the Editor code
// (the plugin selector reads the defines) with them. BuildAndroid writes the
// APK to the path after -xluaSuiteApk (or in XLUA_SUITE_APK).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class XluaSuiteBuild
{
    static Dictionary<string, string> Config()
    {
        var config = new Dictionary<string, string>();
        string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "XluaSuiteConfig.txt");
        if (!File.Exists(file)) throw new Exception("no " + file + "; run il2cpp_suite.py configure");
        foreach (var line in File.ReadAllLines(file))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) config[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return config;
    }

    static string Get(Dictionary<string, string> config, string key, string fallback)
    {
        string value;
        return config.TryGetValue(key, out value) && value != "" ? value : fallback;
    }

    static string ApkPath()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-xluaSuiteApk") return args[i + 1];
        var v = Environment.GetEnvironmentVariable("XLUA_SUITE_APK");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    static NamedBuildTarget Android
    {
        get { return NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Android); }
    }

    public static void ApplyDefines()
    {
        PlayerSettings.SetScriptingDefineSymbols(Android, Get(Config(), "defines", ""));
        AssetDatabase.SaveAssets();
        Debug.Log("XLUA_SUITE_DEFINES: " + PlayerSettings.GetScriptingDefineSymbols(Android));
    }

    // Plugin variants come without a .meta (Test/MobileSmoke/prepare.py); this is
    // XLua > Plugin Variants > Copy Import Settings, as in SmokeBuild.cs.
    static void CopyVariantImportSettings()
    {
        if (Directory.GetDirectories(Application.dataPath, "xlua-variants", SearchOption.AllDirectories).Length == 0) return;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var selector = assembly.GetType("XLua.PluginVariantSelector");
            if (selector == null) continue;
            selector.GetMethod("CopyImportSettings").Invoke(null, null);
            return;
        }
        throw new Exception("Plugin variants without XLua.PluginVariantSelector");
    }

    public static void BuildAndroid()
    {
        var config = Config();
        string path = ApkPath();
        if (path == null) throw new Exception("pass -xluaSuiteApk PATH (or set XLUA_SUITE_APK)");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        PlayerSettings.companyName = "XluaNg";
        PlayerSettings.productName = "XluaNgSuite";
        PlayerSettings.SetApplicationIdentifier(Android, "org.xluang.suite");
        PlayerSettings.allowUnsafeCode = true;
        PlayerSettings.SetScriptingBackend(Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = (AndroidArchitecture)Enum.Parse(typeof(AndroidArchitecture),
            Get(config, "arch", "ARM64"));
#if UNITY_6000_6_OR_NEWER
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
#else
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
#endif
        PlayerSettings.SetManagedStrippingLevel(Android, (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel),
            Get(config, "stripping", "Minimal")));
        PlayerSettings.SetScriptingDefineSymbols(Android, Get(config, "defines", ""));
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        CopyVariantImportSettings();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("XluaSuiteRunner").AddComponent<XluaSuiteRunner>();
        EditorSceneManager.SaveScene(scene, "Assets/XluaSuite.unity");
#if UNITY_2022_1_OR_NEWER
        var codeGen = PlayerSettings.GetIl2CppCodeGeneration(Android);
#else
        // 2021.3 keeps the setting in the build settings, as SmokeBuild.cs reads it.
        var codeGen = EditorUserBuildSettings.il2CppCodeGeneration;
#endif
        Debug.Log("XLUA_SUITE_BUILD_SETTINGS: unity=" + Application.unityVersion
            + " stripping=" + PlayerSettings.GetManagedStrippingLevel(Android)
            + " android=" + PlayerSettings.Android.targetArchitectures
            + " il2cppCodeGen=" + codeGen
            + " il2cppConfig=" + PlayerSettings.GetIl2CppCompilerConfiguration(Android)
            + " defines=" + PlayerSettings.GetScriptingDefineSymbols(Android));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/XluaSuite.unity" },
            target = BuildTarget.Android,
            locationPathName = path,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Suite build failed: " + report.summary.result);
        Debug.Log("XLUA_SUITE_BUILD_PASS: " + path + " " + report.summary.totalSize);
    }
}
