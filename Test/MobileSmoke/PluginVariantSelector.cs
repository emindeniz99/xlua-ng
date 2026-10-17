// size_report.py copies this file to Assets/Editor for the baseline+select variant. It decides at build time
// which of two prebuilt native plugins goes into the player: Plugins/Android/full/libxlua.so when the Player
// Settings scripting defines contain XLUA_FULL_PLUGIN or EditorUserBuildSettings.development is set, and
// Plugins/Android/minimal/libxlua.so otherwise. PluginImporter define constraints do the same for managed
// plugins only; Unity ignores them on native plugins.
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

[InitializeOnLoad]
static class PluginVariantSelector {
 const string Full = "Assets/Plugins/Android/full/libxlua.so";
 const string Minimal = "Assets/Plugins/Android/minimal/libxlua.so";

 static PluginVariantSelector() {
  Select(Full, true);
  Select(Minimal, false);
 }

 static bool WantFull() {
  var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
  return System.Array.IndexOf(defines.Split(';'), "XLUA_FULL_PLUGIN") >= 0 || EditorUserBuildSettings.development;
 }

 static void Select(string path, bool full) {
  var importer = AssetImporter.GetAtPath(path) as PluginImporter;
  if (importer == null) return;
  importer.SetIncludeInBuildDelegate(p => {
   bool include = WantFull() == full;
   Debug.Log("XLUA_PLUGIN_SELECT: " + path + " include=" + include + " development=" + EditorUserBuildSettings.development);
   return include;
  });
 }
}
