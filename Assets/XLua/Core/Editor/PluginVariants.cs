/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// Native plugin variants: which of several builds of the xlua native library goes
// into a player (docs/design/presets.md, "Mechanism"; build/README.md, "Plugin
// variants").
//
// A variant is the same file as the default plugin, at the same path with
// "xlua-variants/<name>/" inserted, for example
//
//   Assets/Plugins/Android/libs/arm64-v8a/libxlua.so                              default
//   Assets/Plugins/Android/xlua-variants/development/libs/arm64-v8a/libxlua.so    "development"
//   Assets/Plugins/iOS/xlua-variants/nosocket/libxlua.a                            "nosocket"
//
// Plugins that differ only by that part of the path form a group, and exactly one
// plugin of a group goes into a build. The first of these that the group has wins:
//
//   1. the variant a Player Settings scripting define names: XLUA_PLUGIN_<NAME>
//      (XLUA_PLUGIN_NOSOCKET picks "nosocket"; at most one such define);
//   2. "core" when XLUA_CORE_ONLY is defined;
//   3. "development" for a Development Build;
//   4. the preset's variant when a preset define is set: XLUA_PRESET_MINIMAL picks
//      "minimal" (docs/design/presets.md; at most one preset define);
//   5. the default plugin, which is the legacy preset's library.
//
// An iOS build for the Simulator SDK (Player Settings, Target SDK) needs libraries
// built for the simulator, which a device library cannot be combined with (both are
// arm64). Their variants carry the suffix "_simulator": "simulator" is the default
// plugin's, "minimal_simulator" the minimal one's (build/make_ios_lua5*.sh with
// XLUA_IOS_SDK=simulator). A simulator build looks for the simulator form of each
// name above first, then for the names themselves; a device build never takes a
// simulator variant.
//
// Unity ignores Define Constraints on native plugins and fails a build that has
// two libraries of the same name and CPU (docs/research/platform-levels.md), so
// the choice is made with PluginImporter.SetIncludeInBuildDelegate. Variants must
// not be enabled for the Editor, which loads the default plugin; "XLua/Plugin
// Variants/Copy Import Settings" gives every variant the platforms and CPU of its
// default plugin and turns the Editor off for it.

#if !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.Linq;
#if !XLUA_PLUGIN_VARIANTS_TEST
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#endif

namespace XLua
{
    // The selection rules, without UnityEditor (Test/Native/plugin_variants_check.py
    // compiles this part on its own and tests it).
    public static class PluginVariants
    {
        public const string Folder = "xlua-variants";
        public const string DefinePrefix = "XLUA_PLUGIN_";
        public const string Core = "core";
        public const string Development = "development";
        public const string PresetPrefix = "XLUA_PRESET_";
        // The variant of the default plugin built for the iOS Simulator, and the suffix
        // of the other variants' simulator builds ("minimal_simulator").
        public const string Simulator = "simulator";
        public const string SimulatorSuffix = "_simulator";

        // Whether a variant name is a simulator build.
        public static bool IsSimulatorVariant(string name)
        {
            return name != null && (name == Simulator || name.EndsWith(SimulatorSuffix, StringComparison.Ordinal));
        }

        // The simulator build's variant name of a variant (null: the default plugin).
        public static string SimulatorVariant(string name)
        {
            return name == null ? Simulator : name + SimulatorSuffix;
        }

        // The presets that have a plugin variant of their own, by name (Tools/presets.py,
        // VARIANT). XLUA_PRESET_LEGACY is the default plugin.
        public static readonly string[] PresetVariants = { "minimal" };

        // File names of the xlua native library on every platform (xlua.bundle is
        // the macOS bundle folder).
        public static readonly string[] FileNames = { "libxlua.so", "libxlua.a", "xlua.dll", "libxlua.dylib", "xlua.bundle" };

        // A variant name: lower-case letters, digits and underscores, starting with a letter.
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'a' || name[0] > 'z') return false;
            foreach (char c in name)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        // Whether assetPath is an xlua native library, default or variant. variant is
        // null for the default plugin; key is the path without "xlua-variants/<name>/",
        // the same for every plugin of a group. A path with an invalid variant name or
        // with the variant folder twice is not a plugin of a group (problem says why).
        public static bool TryParse(string assetPath, out string variant, out string key, out string problem)
        {
            variant = null;
            key = null;
            problem = null;
            if (string.IsNullOrEmpty(assetPath)) return false;
            string[] parts = assetPath.Replace('\\', '/').Split('/');
            string file = parts[parts.Length - 1];
            if (!FileNames.Any(n => string.Equals(n, file, StringComparison.OrdinalIgnoreCase))) return false;
            var kept = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] != Folder)
                {
                    kept.Add(parts[i]);
                    continue;
                }
                if (variant != null || i + 1 >= parts.Length - 1)
                {
                    problem = assetPath + ": a variant is \"" + Folder + "/<name>/\" once, followed by the default plugin's path";
                    variant = null;
                    return false;
                }
                variant = parts[++i];
                if (!IsValidName(variant))
                {
                    problem = assetPath + ": variant name \"" + variant + "\" must be lower-case letters, digits and underscores";
                    variant = null;
                    return false;
                }
            }
            kept.Add(file);
            key = string.Join("/", kept.ToArray());
            return true;
        }

        // The preset variant the defines select ("minimal" for XLUA_PRESET_MINIMAL), or
        // null for the default plugin. error is set for an unknown preset define or
        // more than one.
        public static string PresetVariant(IEnumerable<string> defines, out string error)
        {
            error = null;
            var presets = new List<string>();
            foreach (string raw in defines ?? Enumerable.Empty<string>())
            {
                string define = raw == null ? "" : raw.Trim();
                if (!define.StartsWith(PresetPrefix, StringComparison.Ordinal)) continue;
                string name = define.Substring(PresetPrefix.Length).ToLowerInvariant();
                if (name != "legacy" && !PresetVariants.Contains(name))
                {
                    error = "scripting define " + define + " is not a preset (XLUA_PRESET_MINIMAL; legacy needs no define)";
                    continue;
                }
                if (!presets.Contains(name)) presets.Add(name);
            }
            if (presets.Count > 1)
            {
                error = "more than one preset define: " + string.Join(", ", presets.Select(n => PresetPrefix + n.ToUpperInvariant()).ToArray());
                return null;
            }
            return presets.Count == 1 && presets[0] != "legacy" ? presets[0] : null;
        }

        // The variants to look for, in order, for a build with these scripting
        // defines; null (the default plugin) is last. error is set when more than one
        // XLUA_PLUGIN_ define is given or one names an invalid variant, and for the
        // preset define errors of PresetVariant; the list then leaves those defines out.
        public static List<string> Preference(IEnumerable<string> defines, bool development, out string error)
        {
            return Preference(defines, development, false, out error);
        }

        // The same for an iOS Simulator SDK build when simulator is set: the simulator
        // form of every name first ("minimal_simulator", ..., "simulator"), then the
        // device order, so a group without simulator builds keeps its device plugin as
        // before. A XLUA_PLUGIN_ define that names a simulator variant is an error: the
        // Target SDK chooses those.
        public static List<string> Preference(IEnumerable<string> defines, bool development, bool simulator, out string error)
        {
            var order = DevicePreference(defines, development, out error);
            if (order.Any(IsSimulatorVariant))
            {
                error = "scripting define " + DefinePrefix + order.First(IsSimulatorVariant).ToUpperInvariant()
                    + " names a simulator variant; the iOS Target SDK (Simulator SDK) selects those";
                order.RemoveAll(IsSimulatorVariant);
            }
            if (!simulator) return order;
            var both = order.Select(SimulatorVariant).ToList();
            both.AddRange(order);
            return both;
        }

        static List<string> DevicePreference(IEnumerable<string> defines, bool development, out string error)
        {
            error = null;
            var named = new List<string>();
            bool coreOnly = false;
            var list = (defines ?? Enumerable.Empty<string>()).ToList();
            string presetError;
            string preset = PresetVariant(list, out presetError);
            foreach (string raw in list)
            {
                string define = raw == null ? "" : raw.Trim();
                if (define == "XLUA_CORE_ONLY") coreOnly = true;
                if (!define.StartsWith(DefinePrefix, StringComparison.Ordinal)) continue;
                string name = define.Substring(DefinePrefix.Length).ToLowerInvariant();
                if (!IsValidName(name))
                {
                    error = "scripting define " + define + " does not name a plugin variant (XLUA_PLUGIN_<NAME>, letters, digits and underscores)";
                    continue;
                }
                if (!named.Contains(name)) named.Add(name);
            }
            if (named.Count > 1)
            {
                error = "more than one plugin variant define: " + string.Join(", ", named.Select(n => DefinePrefix + n.ToUpperInvariant()).ToArray());
            }
            var order = new List<string>();
            if (error == null) order.AddRange(named);
            if (coreOnly && !order.Contains(Core)) order.Add(Core);
            if (development && !order.Contains(Development)) order.Add(Development);
            if (preset != null && !order.Contains(preset)) order.Add(preset);
            if (error == null) error = presetError;
            order.Add(null);
            return order;
        }

        // The plugin of a group (variant name or null, asset path) that goes into the
        // build, or null when the group has none of the preferred variants and no
        // default plugin.
        public static string Choose(IEnumerable<KeyValuePair<string, string>> group, IList<string> preference)
        {
            var members = group.ToList();
            foreach (string wanted in preference)
            {
                foreach (var member in members)
                {
                    if (member.Key == wanted) return member.Value;
                }
            }
            return null;
        }

        // Groups of xlua plugins by key: only groups with more than one plugin need
        // a choice. Paths that are not xlua plugins are skipped; problems collects
        // the ones that look like variants but are not valid.
        public static Dictionary<string, List<KeyValuePair<string, string>>> Groups(IEnumerable<string> assetPaths, List<string> problems)
        {
            var groups = new Dictionary<string, List<KeyValuePair<string, string>>>();
            foreach (string path in assetPaths)
            {
                string variant, key, problem;
                if (!TryParse(path, out variant, out key, out problem))
                {
                    if (problem != null && problems != null) problems.Add(problem);
                    continue;
                }
                List<KeyValuePair<string, string>> members;
                if (!groups.TryGetValue(key, out members))
                {
                    members = new List<KeyValuePair<string, string>>();
                    groups.Add(key, members);
                }
                if (members.Any(m => m.Key == variant))
                {
                    if (problems != null) problems.Add(path + ": a second \"" + (variant ?? "default") + "\" plugin for " + key);
                    continue;
                }
                members.Add(new KeyValuePair<string, string>(variant, path));
            }
            return groups;
        }
    }

#if !XLUA_PLUGIN_VARIANTS_TEST
    // Registers the include-in-build delegates on load, after imports and for builds.
    [InitializeOnLoad]
    public static class PluginVariantSelector
    {
        // A Development Build as the build reports it (BuildOptions.Development), set
        // by the preprocess callback; EditorUserBuildSettings.development otherwise.
        static bool? buildDevelopment;
        static Dictionary<string, List<KeyValuePair<string, string>>> groups =
            new Dictionary<string, List<KeyValuePair<string, string>>>();
        // Asset path -> group key, for the delegates.
        static Dictionary<string, string> keyOfPath = new Dictionary<string, string>();

        static PluginVariantSelector()
        {
            Register();
        }

        static IEnumerable<PluginImporter> NativeImporters()
        {
            return PluginImporter.GetAllImporters().Where(i => i != null && i.isNativePlugin);
        }

        // Finds the groups and sets a delegate on every plugin of a group with
        // variants; plugins without variants are left as they are.
        public static List<string> Register()
        {
            var problems = new List<string>();
            var importers = NativeImporters().ToList();
            groups = PluginVariants.Groups(importers.Select(i => i.assetPath), problems);
            keyOfPath = new Dictionary<string, string>();
            foreach (var group in groups)
            {
                if (group.Value.Count < 2) continue;
                foreach (var member in group.Value)
                {
                    keyOfPath[member.Value] = group.Key;
                }
            }
            foreach (var importer in importers)
            {
                if (!keyOfPath.ContainsKey(importer.assetPath)) continue;
                string path = importer.assetPath;
                importer.SetIncludeInBuildDelegate(p => Include(path));
            }
            return problems;
        }

        internal static void SetBuildDevelopment(bool? development)
        {
            buildDevelopment = development;
        }

        // The preference for the active build target's Player Settings defines.
        public static List<string> CurrentPreference(out string error)
        {
            return CurrentPreference(IsSimulatorBuild(), out error);
        }

        // The same for a device build (simulator false) or an iOS Simulator SDK build.
        public static List<string> CurrentPreference(bool simulator, out string error)
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            var named = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            string defines = PlayerSettings.GetScriptingDefineSymbols(named);
            bool development = buildDevelopment ?? EditorUserBuildSettings.development;
            return PluginVariants.Preference(defines.Split(';'), development, simulator, out error);
        }

        // Whether the active target builds for the iOS Simulator (Player Settings >
        // Target SDK = Simulator SDK).
        public static bool IsSimulatorBuild()
        {
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS
                && PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK;
        }

        // The plugin of each group with variants that the next build of the active
        // target includes (null when none).
        public static Dictionary<string, string> CurrentSelection(out string error)
        {
            var preference = CurrentPreference(out error);
            var selection = new Dictionary<string, string>();
            foreach (var group in groups)
            {
                if (group.Value.Count < 2) continue;
                selection[group.Key] = PluginVariants.Choose(group.Value, preference);
            }
            return selection;
        }

        // The preset variant the active target's Player Settings defines select.
        public static string CurrentPresetVariant()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            var named = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            string error;
            return PluginVariants.PresetVariant(PlayerSettings.GetScriptingDefineSymbols(named).Split(';'), out error);
        }

        // Whether any xlua plugin has this variant.
        public static bool HasVariant(string variant)
        {
            return groups.Values.Any(members => members.Any(m => m.Key == variant));
        }

        static bool Include(string path)
        {
            string key;
            List<KeyValuePair<string, string>> members;
            if (!keyOfPath.TryGetValue(path, out key) || !groups.TryGetValue(key, out members)) return true;
            string error;
            var preference = CurrentPreference(out error);
            return PluginVariants.Choose(members, preference) == path;
        }

        [MenuItem("XLua/Plugin Variants/Show Selection", false, 200)]
        public static void ShowSelection()
        {
            foreach (string problem in Register()) Debug.LogWarning("XLua plugin variants: " + problem);
            string error;
            var selection = CurrentSelection(out error);
            if (error != null) Debug.LogError("XLua plugin variants: " + error);
            if (selection.Count == 0)
            {
                Debug.Log("XLua plugin variants: no plugin has variants (" + PluginVariants.Folder + "/<name>/ folders); the default plugins are used.");
                return;
            }
            foreach (var entry in selection)
            {
                Debug.Log("XLua plugin variants: " + entry.Key + " -> " + (entry.Value ?? "none (no default plugin and no matching variant)")
                    + " for " + EditorUserBuildSettings.activeBuildTarget + (EditorUserBuildSettings.development ? ", Development Build" : ""));
            }
        }

        // Platforms whose compatibility and CPU setting a variant copies from its
        // default plugin.
        static readonly BuildTarget[] CopiedTargets = {
            BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneLinux64, BuildTarget.iOS, BuildTarget.tvOS, BuildTarget.Android,
            BuildTarget.WSAPlayer, BuildTarget.Switch, BuildTarget.WebGL,
        };

        [MenuItem("XLua/Plugin Variants/Copy Import Settings", false, 201)]
        public static void CopyImportSettings()
        {
            Register();
            int copied = 0;
            foreach (var group in groups)
            {
                var defaultMember = group.Value.FirstOrDefault(m => m.Key == null);
                if (defaultMember.Value == null || group.Value.Count < 2) continue;
                var source = AssetImporter.GetAtPath(defaultMember.Value) as PluginImporter;
                if (source == null) continue;
                foreach (var member in group.Value)
                {
                    if (member.Key == null) continue;
                    var target = AssetImporter.GetAtPath(member.Value) as PluginImporter;
                    if (target == null) continue;
                    target.SetCompatibleWithAnyPlatform(false);
                    target.SetCompatibleWithEditor(false);
                    foreach (BuildTarget platform in CopiedTargets)
                    {
                        bool compatible = source.GetCompatibleWithPlatform(platform);
                        target.SetCompatibleWithPlatform(platform, compatible);
                        if (compatible)
                        {
                            string cpu = source.GetPlatformData(platform, "CPU");
                            if (!string.IsNullOrEmpty(cpu)) target.SetPlatformData(platform, "CPU", cpu);
                        }
                    }
                    target.SaveAndReimport();
                    copied++;
                }
            }
            Debug.Log("XLua plugin variants: copied the import settings of the default plugin to " + copied + " variant(s)");
        }
    }

    // Re-registers when plugins are imported, moved or deleted.
    class PluginVariantPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            string variant, key, problem;
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => PluginVariants.TryParse(p, out variant, out key, out problem) || problem != null))
            {
                foreach (string message in PluginVariantSelector.Register()) Debug.LogWarning("XLua plugin variants: " + message);
            }
        }
    }

    // Checks the choice before a build and logs it: a group without a plugin for
    // the build, two variant defines, or a variant define that no group has stop
    // the build or warn.
    class PluginVariantBuildCheck : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            PluginVariantSelector.SetBuildDevelopment((report.summary.options & BuildOptions.Development) != 0);
            foreach (string problem in PluginVariantSelector.Register()) Debug.LogWarning("XLua plugin variants: " + problem);
            string error;
            var preference = PluginVariantSelector.CurrentPreference(out error);
            if (error != null)
            {
                PluginVariantSelector.SetBuildDevelopment(null);
                throw new BuildFailedException("XLua plugin variants: " + error);
            }
            var selection = PluginVariantSelector.CurrentSelection(out error);
            foreach (var entry in selection)
            {
                Debug.Log("XLua plugin variants: " + entry.Key + " -> " + (entry.Value ?? "none"));
            }
            if (PluginVariantSelector.IsSimulatorBuild())
            {
                var missing = selection.Where(e => e.Key.Contains("/iOS/") && (e.Value == null
                    || !e.Value.Split('/').Any(PluginVariants.IsSimulatorVariant))).Select(e => e.Key).ToList();
                if (missing.Count > 0)
                {
                    Debug.LogWarning("XLua plugin variants: an iOS Simulator SDK build, but " + string.Join(", ", missing.ToArray())
                        + " has no simulator variant (" + PluginVariants.Folder + "/" + PluginVariants.Simulator
                        + "/); the device library does not link for the simulator. Build it with XLUA_IOS_SDK=simulator build/make_ios_<lua>.sh");
                }
            }
            // The device order: its first entry is the variant a XLUA_PLUGIN_ define names, if any.
            var device = PluginVariantSelector.CurrentPreference(false, out error);
            string named = device.Count > 1 && device[0] != PluginVariants.Core && device[0] != PluginVariants.Development
                ? device[0] : null;
            if (named != null && !selection.Values.Any(p => p != null && (p.Contains("/" + PluginVariants.Folder + "/" + named + "/")
                || p.Contains("/" + PluginVariants.Folder + "/" + PluginVariants.SimulatorVariant(named) + "/"))))
            {
                Debug.LogWarning("XLua plugin variants: " + PluginVariants.DefinePrefix + named.ToUpperInvariant()
                    + " is defined, but no plugin has the variant \"" + named + "\"; the default plugins are used");
            }
            string preset = PluginVariantSelector.CurrentPresetVariant();
            if (preset != null && !PluginVariantSelector.HasVariant(preset))
            {
                // The preset's C# runs on the default (legacy) library, which has every
                // function it imports, but the player then carries the legacy native size.
                Debug.LogWarning("XLua plugin variants: " + PluginVariants.PresetPrefix + preset.ToUpperInvariant()
                    + " is defined, but no plugin has the variant \"" + preset + "\"; the default (legacy) plugins are used."
                    + " Build it with XLUA_VARIANT=" + preset + " XLUA_PRESET=" + preset + " build/make_<platform>_<lua>.sh");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            PluginVariantSelector.SetBuildDevelopment(null);
        }
    }
#endif
}
#endif
