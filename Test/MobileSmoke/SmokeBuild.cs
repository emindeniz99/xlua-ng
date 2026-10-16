using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
public static class SmokeBuild {
 static string Output(string name) {
  var root=Environment.GetEnvironmentVariable("XLUA_SMOKE_OUTPUT_ROOT");
  if(string.IsNullOrEmpty(root)) root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../builds"));
  Directory.CreateDirectory(root);
  return Path.Combine(root,name);
 }
 public static void CheckEditor() { Debug.Log(XluaSmoke.Run()); }
 public static void BuildIos() { Build(BuildTarget.iOS,Output("unity-ios-smoke")); }
 public static void BuildAndroid() { Build(BuildTarget.Android,Output("unity-android-smoke.apk")); }
 public static void BuildWebGL() {
  // No compression, so a plain static file server can host the player without Content-Encoding headers.
  PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Disabled;
  Build(BuildTarget.WebGL,Output("unity-webgl-smoke"));
 }
 static void Build(BuildTarget target,string path) {
  PlayerSettings.companyName="XluaNg";PlayerSettings.productName="XluaNgSmoke";
  PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target)),"org.xluang.smoke");
  PlayerSettings.allowUnsafeCode=true;
  PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target)),ScriptingImplementation.IL2CPP);
  PlayerSettings.iOS.targetOSVersionString="15.0";
  PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
  PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel25;
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  new GameObject("XluaSmoke").AddComponent<XluaSmoke>();
  EditorSceneManager.SaveScene(scene,"Assets/Smoke.unity");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Smoke.unity"},target=target,locationPathName=path,options=BuildOptions.Development });
  if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Smoke build failed: "+report.summary.result);
  Debug.Log("XLUA_NG_BUILD_PASS: "+target);
 }
}
