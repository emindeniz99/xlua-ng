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
 // size_report.py sets these for release size builds; unset, the smoke builds are unchanged.
 static string Env(string name) { var v=Environment.GetEnvironmentVariable(name); return string.IsNullOrEmpty(v)?null:v; }
 static T EnvEnum<T>(string name,T fallback) { var v=Env(name); return v==null?fallback:(T)Enum.Parse(typeof(T),v); }
 public static void CheckEditor() { Debug.Log(XluaSmoke.Run()); }
 // Puts XLUA_SMOKE_DEFINES into the Player Settings of the active build target in a launch of its own,
 // so that the next launch compiles the Editor and the scripts with them, as in a project that has them
 // already: with XLUA_CORE_ONLY, XLua.Editor and its build callbacks are then not loaded during the build.
 public static void ApplyDefines() {
  var target=EditorUserBuildSettings.activeBuildTarget;
  var named=UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
  PlayerSettings.SetScriptingDefineSymbols(named,Env("XLUA_SMOKE_DEFINES")??"");
  AssetDatabase.SaveAssets();
  Debug.Log("XLUA_NG_DEFINES: "+target+" "+PlayerSettings.GetScriptingDefineSymbols(named));
 }
 public static void BuildIos() { Build(BuildTarget.iOS,Output("unity-ios-smoke")); }
 public static void BuildAndroid() {
  EditorUserBuildSettings.buildAppBundle=Env("XLUA_SMOKE_AAB")=="1";
  Build(BuildTarget.Android,Output(EditorUserBuildSettings.buildAppBundle?"unity-android-smoke.aab":"unity-android-smoke.apk"));
 }
 // An ARM64 macOS Mono player (the editors here have no macOS IL2CPP module), for the plugin
 // selector with the macOS plugin variants. UserBuildSettings.architecture has a different enum
 // type in 2021.3 and Unity 6, so it is set by name.
 public static void BuildMac() {
  var settings=Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")??Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor");
  var architecture=settings==null?null:settings.GetProperty("architecture");
  if(architecture!=null) architecture.SetValue(null,Enum.Parse(architecture.PropertyType,"ARM64"));
  Build(BuildTarget.StandaloneOSX,Output("unity-macos-smoke.app"),ScriptingImplementation.Mono2x);
 }
 public static void BuildWebGL() {
  // No compression by default, so a plain static file server can host the player without Content-Encoding headers.
  PlayerSettings.WebGL.compressionFormat=EnvEnum("XLUA_SMOKE_WEBGL_COMPRESSION",WebGLCompressionFormat.Disabled);
#if UNITY_2022_1_OR_NEWER
  // Above 4096 MB, Unity 6.6 and later build WebAssembly64 (Memory64) instead of wasm32.
  if(Env("XLUA_SMOKE_WEBGL_MAX_MEMORY")!=null) PlayerSettings.WebGL.maximumMemorySize=int.Parse(Env("XLUA_SMOKE_WEBGL_MAX_MEMORY"));
#endif
  Build(BuildTarget.WebGL,Output("unity-webgl-smoke"));
 }
 // prepare.py and size_report.py copy plugin variants (Plugins/.../xlua-variants/<name>/) without
 // a .meta; this runs XLua/Plugin Variants/Copy Import Settings, as a project does once after
 // adding one. The selector may be in Assembly-CSharp-Editor or XLua.Editor, or absent.
 static void CopyVariantImportSettings() {
  if(Directory.GetDirectories(Application.dataPath,"xlua-variants",SearchOption.AllDirectories).Length==0) return;
  foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
   var selector=assembly.GetType("XLua.PluginVariantSelector");
   if(selector==null) continue;
   selector.GetMethod("CopyImportSettings").Invoke(null,null);
   return;
  }
  throw new Exception("Plugin variants without XLua.PluginVariantSelector (Assets/XLua/Src/Editor/PluginVariants.cs)");
 }
 static void Build(BuildTarget target,string path,ScriptingImplementation backend=ScriptingImplementation.IL2CPP) {
  var named=UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
  PlayerSettings.companyName="XluaNg";PlayerSettings.productName="XluaNgSmoke";
  PlayerSettings.SetApplicationIdentifier(named,"org.xluang.smoke");
  PlayerSettings.allowUnsafeCode=true;
  PlayerSettings.SetScriptingBackend(named,backend);
  PlayerSettings.iOS.targetOSVersionString="15.0";
  PlayerSettings.Android.targetArchitectures=EnvEnum("XLUA_SMOKE_ANDROID_ARCHS",AndroidArchitecture.ARM64);
#if UNITY_6000_6_OR_NEWER
  // Unity 6.6 builds Android players for API 26 and up only; lower values are obsolete there.
  PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
#else
  PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel25;
#endif
  if(Env("XLUA_SMOKE_STRIPPING")!=null) PlayerSettings.SetManagedStrippingLevel(named,EnvEnum("XLUA_SMOKE_STRIPPING",ManagedStrippingLevel.Minimal));
#if UNITY_2022_1_OR_NEWER
  if(Env("XLUA_SMOKE_IL2CPP_CODEGEN")!=null) PlayerSettings.SetIl2CppCodeGeneration(named,EnvEnum("XLUA_SMOKE_IL2CPP_CODEGEN",UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed));
  var codeGen=PlayerSettings.GetIl2CppCodeGeneration(named);
#else
  if(Env("XLUA_SMOKE_IL2CPP_CODEGEN")!=null) EditorUserBuildSettings.il2CppCodeGeneration=EnvEnum("XLUA_SMOKE_IL2CPP_CODEGEN",UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
  var codeGen=EditorUserBuildSettings.il2CppCodeGeneration;
#endif
  if(Env("XLUA_SMOKE_IL2CPP_CONFIG")!=null) PlayerSettings.SetIl2CppCompilerConfiguration(named,EnvEnum("XLUA_SMOKE_IL2CPP_CONFIG",Il2CppCompilerConfiguration.Release));
  if(Env("XLUA_SMOKE_IL2CPP_ARGS")!=null) PlayerSettings.SetAdditionalIl2CppArgs(Env("XLUA_SMOKE_IL2CPP_ARGS"));
  if(Env("XLUA_SMOKE_DEFINES")!=null) PlayerSettings.SetScriptingDefineSymbols(named,Env("XLUA_SMOKE_DEFINES"));
  if(Env("XLUA_SMOKE_IOS_SDK")!=null) PlayerSettings.iOS.sdkVersion=EnvEnum("XLUA_SMOKE_IOS_SDK",iOSSdkVersion.DeviceSDK);
#if UNITY_2022_1_OR_NEWER
  // Simulator builds default to x86_64, which an Apple silicon Mac's simulator does not run.
  if(PlayerSettings.iOS.sdkVersion==iOSSdkVersion.SimulatorSDK) PlayerSettings.iOS.simulatorSdkArchitecture=AppleMobileArchitectureSimulator.ARM64;
#endif
  var release=Env("XLUA_SMOKE_RELEASE")=="1";
#if UNITY_2022_1_OR_NEWER
  var webglMemory=" webglMaxMemory="+PlayerSettings.WebGL.maximumMemorySize;
#else
  var webglMemory="";
#endif
  // Build-time editor code (PluginImporter.SetIncludeInBuildDelegate) reads the development flag from here.
  EditorUserBuildSettings.development=!release;
  CopyVariantImportSettings();
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  new GameObject("XluaSmoke").AddComponent<XluaSmoke>();
  EditorSceneManager.SaveScene(scene,"Assets/Smoke.unity");
  Debug.Log("XLUA_NG_BUILD_SETTINGS: "+target+" unity="+Application.unityVersion+" release="+release+
   " stripping="+PlayerSettings.GetManagedStrippingLevel(named)+" stripEngineCode="+PlayerSettings.stripEngineCode+
   " android="+PlayerSettings.Android.targetArchitectures+" aab="+EditorUserBuildSettings.buildAppBundle+
   " webgl="+PlayerSettings.WebGL.compressionFormat+webglMemory+" il2cppCodeGen="+codeGen+
   " il2cppConfig="+PlayerSettings.GetIl2CppCompilerConfiguration(named)+" il2cppArgs="+PlayerSettings.GetAdditionalIl2CppArgs()+
   " defines="+PlayerSettings.GetScriptingDefineSymbols(named)+" iosSdk="+PlayerSettings.iOS.sdkVersion);
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Smoke.unity"},target=target,locationPathName=path,options=release?BuildOptions.None:BuildOptions.Development });
  if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Smoke build failed: "+report.summary.result);
  Debug.Log("XLUA_NG_BUILD_PASS: "+target);
 }
}
