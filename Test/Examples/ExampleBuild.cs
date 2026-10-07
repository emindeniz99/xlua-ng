using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
public static class ExampleBuild {
 public static void Generate() { CSObjectWrapEditor.Generator.GenAll(); }
 public static void Android() {
  PlayerSettings.companyName="XluaNg"; PlayerSettings.productName="XluaNg Examples";
  PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android,"org.xluang.examples");
  PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
  PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
  PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel25;
  PlayerSettings.allowUnsafeCode=true;
  var s=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  new GameObject("ExampleRunner").AddComponent<ExampleRunner>();
  EditorSceneManager.SaveScene(s,"Assets/ExampleRunner.unity");
  var scenes=new[]{"Assets/ExampleRunner.unity","Assets/XLua/Tutorial/CSharpCallLua/CSCallLua.unity","Assets/XLua/Tutorial/LuaCallCSharp/LuaCallCs.unity","Assets/XLua/Examples/02_U3DScripting/U3DScripting.unity","Assets/XLua/Examples/03_UIEvent/UI.unity"};
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,target=BuildTarget.Android,locationPathName=Environment.GetEnvironmentVariable("XLUA_EXAMPLE_APK"),options=BuildOptions.Development});
  if(report.summary.result!=BuildResult.Succeeded) throw new Exception(report.summary.result.ToString());
  Debug.Log("XLUA_EXAMPLES_BUILD_PASS");
 }
}
