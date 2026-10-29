// vs_bench.py copies this into the zlua project's Assets/Editor. zlua 1.1.0 needs two steps before the build, each
// in a Unity launch of its own:
// - Install: zlua's own BatchCommands.Install (a copy of the editor's il2cpp tree in Library/ZLua with libil2cpp
//   and Lua 5.5.0 patched; the Lua tarball is already in its cache, put there and hash-checked by vs_bench.py);
// - Generate: the player settings of SmokeBuild.cs and the bench scene in the build settings, then zlua's
//   PrebuildCommand.GenerateAll, which builds a throwaway player for the stripped assemblies and writes the C++
//   call stubs and delegate thunks from them.
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VsBenchZLuaEditor
{
    public static void Install() { ZLua.BatchCommands.Install(); }

    public static void Generate()
    {
        var named = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(named, Environment.GetEnvironmentVariable("XLUA_SMOKE_APP_ID") ?? "org.xluang.vs.zlua");
        PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
        PlayerSettings.SetManagedStrippingLevel(named, ManagedStrippingLevel.Minimal);
        PlayerSettings.allowUnsafeCode = true;
        EditorUserBuildSettings.development = false;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("XluaSmoke").AddComponent<XluaSmoke>();
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Smoke.unity", true) };
        AssetDatabase.SaveAssets();
        ZLua.Commands.PrebuildCommand.GenerateAll();
        Debug.Log("XLUA_VSBENCH_GENERATED: zlua");
    }
}
