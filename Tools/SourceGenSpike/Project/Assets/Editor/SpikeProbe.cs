using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

// Batch-mode entry points for the source generator spike. Every result line starts
// with XLUA_SG_ so the runner can grep the editor log.
[InitializeOnLoad]
public static class SpikeProbe
{
    const string StateFile = "sgspike-state.txt";
    const string GameFile = "Assets/Game/GameExports.cs";
    const string GameBackup = "sgspike-GameExports.cs.orig";

    static SpikeProbe()
    {
        if (File.Exists(StateFile) && File.ReadAllText(StateFile).Trim() == "phase2")
        {
            File.Delete(StateFile);
            // Directly, not via delayCall: in 6000.6 batch mode the reload happens inside
            // AssetDatabase.Refresh and neither delayCall nor update fire afterwards.
            IncrementalPhase2();
        }
    }

    // ---- compile check ----

    public static void Run()
    {
        bool pass = Probe(false);
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static Assembly Loaded(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == name)
                return a;
        return null;
    }

    static bool Probe(bool afterEdit)
    {
        Debug.Log("XLUA_SG_UNITY " + Application.unityVersion);
        var markers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var gens = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (UnityEditor.Compilation.Assembly ca in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
        {
            Assembly asm = Loaded(ca.name);
            string state = "not-loaded";
            if (asm != null)
            {
                state = "no-generator";
                foreach (Type t in asm.GetTypes())
                {
                    if (t.Namespace != "XLuaTest.Generated")
                        continue;
                    if (t.Name.StartsWith("XLuaSGMarker_", StringComparison.Ordinal))
                    {
                        string exports = (string)t.GetField("Exports").GetRawConstantValue();
                        markers[ca.name] = exports;
                        state = "generator-ran exports=[" + exports + "]";
                    }
                    else if (t.Name.StartsWith("XLuaGen_", StringComparison.Ordinal))
                        gens[ca.name] = t;
                }
            }
            Debug.Log("XLUA_SG_SCOPE " + ca.name + ": " + state + (gens.ContainsKey(ca.name) ? " wrapper=" + gens[ca.name].Name : ""));
        }

        bool pass = true;
        void Fail(string why) { pass = false; sb.Append(why).Append("; "); }

        string gameExpect = afterEdit ? "Game.UserThing;Game.UserThing2;UnityEngine.Vector3" : "Game.UserThing;UnityEngine.Vector3";
        string gameAnswer = afterEdit ? "Game.UserThing.Answer()=43" : "Game.UserThing.Answer()=42";
        CheckGen(gens, "Assembly-CSharp", "XLuaGen_Assembly_CSharp", gameExpect, gameAnswer, Fail);
        CheckGen(gens, "UserGame", "XLuaGen_UserGame", "UnityEngine.Time;UserGame.UserGameThing", "UserGame.UserGameThing.Hello=hello", Fail);
        if (markers.ContainsKey("Unrelated"))
            Fail("generator ran in Unrelated (no reference to XLuaTest.Runtime)");
        if (!markers.ContainsKey("Assembly-CSharp-Editor"))
            Debug.Log("XLUA_SG_NOTE generator did not run in Assembly-CSharp-Editor");

        string tag = afterEdit ? "XLUA_SG_INCREMENTAL_" : "XLUA_SG_";
        Debug.Log(pass ? tag + "PASS" : tag + "FAIL " + sb);
        return pass;
    }

    static void CheckGen(SortedDictionary<string, Type> gens, string asm, string cls, string exports, string invokeContains, Action<string> fail)
    {
        if (!gens.TryGetValue(asm, out Type t))
        {
            fail("no wrapper class in " + asm);
            return;
        }
        if (t.Name != cls)
            fail(asm + ": wrapper named " + t.Name + ", expected " + cls);
        if (!t.IsAbstract || !t.IsSealed || t.IsPublic)
            fail(asm + ": wrapper is not an internal static class");
        string got = (string)t.GetField("Exports").GetRawConstantValue();
        if (got != exports)
            fail(asm + ": exports [" + got + "], expected [" + exports + "]");
        MethodInfo reg = t.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static);
        if (reg == null || reg.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() == null)
            fail(asm + ": Register lacks [RuntimeInitializeOnLoadMethod]");
        string r = (string)t.GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Debug.Log("XLUA_SG_INVOKE " + asm + ": " + r);
        if (!r.Contains(invokeContains))
            fail(asm + ": Invoke() missing " + invokeContains);
    }

    // ---- incrementality inside one editor session ----

    public static void RunIncremental()
    {
        if (!Probe(false))
        {
            EditorApplication.Exit(1);
            return;
        }
        string src = File.ReadAllText(GameFile);
        File.WriteAllText(GameBackup, src);
        string edited = src.Replace("public const int Version = 0;", "public const int Version = 1;")
            + "\nnamespace Game\n{\n    [XLuaTest.Export]\n    public class UserThing2\n    {\n        public static string Name => \"two\";\n    }\n}\n";
        File.WriteAllText(GameFile, edited);
        File.WriteAllText(StateFile, "phase2");
        Debug.Log("XLUA_SG_EDITED " + GameFile + " at " + DateTime.Now.ToString("HH:mm:ss.fff"));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        double start = EditorApplication.timeSinceStartup;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup - start > 240)
            {
                Debug.Log("XLUA_SG_INCREMENTAL_FAIL no domain reload within 240 s");
                Restore();
                EditorApplication.Exit(3);
            }
        };
    }

    static void IncrementalPhase2()
    {
        Debug.Log("XLUA_SG_RELOADED at " + DateTime.Now.ToString("HH:mm:ss.fff"));
        bool pass = Probe(true);
        Restore();
        EditorApplication.Exit(pass ? 0 : 2);
    }

    static void Restore()
    {
        if (File.Exists(GameBackup))
        {
            File.Copy(GameBackup, GameFile, true);
            File.Delete(GameBackup);
        }
        if (File.Exists(StateFile))
            File.Delete(StateFile);
    }

    // ---- players ----

    static string Arg(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return fallback;
    }

    static string[] Scene()
    {
        const string path = "Assets/Scenes/Main.unity";
        if (!File.Exists(path))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
        }
        return new[] { path };
    }

    static void Build(BuildTarget target, NamedBuildTarget named, ScriptingImplementation backend, string output)
    {
        PlayerSettings.SetScriptingBackend(named, backend);
        ManagedStrippingLevel level = (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), Arg("-sgStripping", "High"));
        PlayerSettings.SetManagedStrippingLevel(named, level);
        PlayerSettings.productName = "SGSpike";
        Debug.Log("XLUA_SG_BUILD target=" + target + " backend=" + backend + " stripping=" + level + " out=" + output);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scene(),
            locationPathName = output,
            target = target,
            options = BuildOptions.None,
        });
        Debug.Log("XLUA_SG_BUILD_RESULT " + report.summary.result + " errors=" + report.summary.totalErrors
            + " size=" + report.summary.totalSize + " secs=" + (int)report.summary.totalTime.TotalSeconds);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public static void BuildMacMono()
    {
        EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
        Build(BuildTarget.StandaloneOSX, NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x, Arg("-sgOut", "Builds/mac/SGSpike.app"));
    }

    public static void BuildIosSimulator()
    {
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.xluang.sgspike");
        PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        Build(BuildTarget.iOS, NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP, Arg("-sgOut", "Builds/ios"));
    }

    public static void BuildAndroidExport()
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.xluang.sgspike");
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
        Build(BuildTarget.Android, NamedBuildTarget.Android, ScriptingImplementation.IL2CPP, Arg("-sgOut", "Builds/android"));
    }
}
