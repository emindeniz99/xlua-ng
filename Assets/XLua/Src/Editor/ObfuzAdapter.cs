/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The Editor's side of obfuz support (docs/design/source-generator.md,
// milestone M8; ObfuzRules.cs has the rules themselves):
//
// - XLua > Obfuscation > Write obfuz Rule File writes RuleFile for the
//   assemblies obfuz obfuscates; add the file to obfuz's settings (Symbol
//   Obfuscation > Rule Files) once;
// - before each build with obfuz enabled, the file is written again when obfuz
//   lists it, so it follows the configuration, and a warning says when obfuz
//   does not list it or when, with HOTFIX_ENABLE, obfuz would run before the
//   hotfix injector (which must see original names).
//
// xLua does not reference obfuz: its settings (ProjectSettings/Obfuz.asset,
// Obfuz.Settings.ObfuzSettings in 4.0.0) are read through reflection, and
// without obfuz nothing here does anything.

#if !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace XLua
{
    public static class ObfuzAdapter
    {
        // Where the menu writes the rules, next to obfuz's own files.
        public const string RuleFile = "Assets/Obfuz/xlua-ng-rules.xml";

        // The callback order of xLua's hotfix injection (Hotfix.cs).
        const int HotfixInjectOrder = 0;

        // What xLua needs of obfuz's settings.
        public sealed class Settings
        {
            public bool Enabled;
            public List<string> Obfuscated;
            public string[] RuleFiles;
            public int ObfuscationOrder;

            public bool ListsRuleFile
            {
                get
                {
                    string ours = Path.GetFullPath(RuleFile);
                    return RuleFiles != null && RuleFiles.Any(f => !string.IsNullOrEmpty(f) && Path.GetFullPath(f) == ours);
                }
            }
        }

        static object field(object owner, string name)
        {
            var f = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return f.GetValue(owner);
        }

        // obfuz's settings, or null when obfuz is not installed or its settings
        // have a layout this adapter does not know (then with a warning).
        public static Settings ReadSettings()
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                type = assembly.GetType("Obfuz.Settings.ObfuzSettings", false);
                if (type != null) break;
            }
            if (type == null) return null;
            try
            {
                var instance = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var assemblySettings = field(instance, "assemblySettings");
                var obfuscated = (IEnumerable<string>)assemblySettings.GetType().GetMethod("GetAssembliesToObfuscate").Invoke(assemblySettings, null);
                var pipeline = field(instance, "buildPipelineSettings");
                var symbol = field(instance, "symbolObfusSettings");
                return new Settings
                {
                    Enabled = (bool)field(pipeline, "enable"),
                    ObfuscationOrder = (int)field(pipeline, "obfuscationProcessCallbackOrder"),
                    Obfuscated = obfuscated.ToList(),
                    RuleFiles = (string[])field(symbol, "ruleFiles"),
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("xLua: cannot read obfuz's settings (" + e.GetType().Name + ": " + e.Message
                    + "); the adapter was written for obfuz 4.0.0. Write the rule file with XLua > Obfuscation > Write obfuz Rule File after checking it");
                return null;
            }
        }

        // The rules for the assemblies obfuz obfuscates.
        public static string BuildRules(ICollection<string> obfuscated)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToArray();
            var names = new HashSet<string>(obfuscated, StringComparer.Ordinal);
            var xlua = new HashSet<string>(assemblies.Where(a => names.Contains(a.GetName().Name) && ObfuzRules.HasXLuaCode(a))
                .Select(a => a.GetName().Name), StringComparer.Ordinal);
#if HOTFIX_ENABLE
            bool hotfix = true;
#else
            bool hotfix = false;
#endif
            return ObfuzRules.Build(ObfuzRules.Fragments(assemblies), names, xlua, hotfix);
        }

        public static void Write(ICollection<string> obfuscated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuleFile));
            string rules = BuildRules(obfuscated);
            if (File.Exists(RuleFile) && File.ReadAllText(RuleFile) == rules) return;
            File.WriteAllText(RuleFile, rules);
            AssetDatabase.ImportAsset(RuleFile);
        }

        [MenuItem("XLua/Obfuscation/Write obfuz Rule File", false, 210)]
        public static void WriteRuleFile()
        {
            var settings = ReadSettings();
            if (settings == null)
            {
                Debug.LogWarning("xLua: obfuz is not installed (no Obfuz.Settings.ObfuzSettings); nothing written");
                return;
            }
            Write(settings.Obfuscated);
            Debug.Log("xLua: wrote " + RuleFile + " for " + string.Join(", ", settings.Obfuscated.ToArray()));
            if (!settings.ListsRuleFile)
            {
                Debug.LogWarning("xLua: add " + RuleFile + " to obfuz's settings (Symbol Obfuscation > Rule Files); obfuz does not read it yet");
            }
        }

        // Called before a build (ObfuzBuildCheck).
        internal static void BeforeBuild()
        {
            var settings = ReadSettings();
            if (settings == null || !settings.Enabled) return;
            if (settings.ListsRuleFile)
            {
                Write(settings.Obfuscated);
            }
            else
            {
                Debug.LogWarning("xLua: obfuz is enabled, but its rule files do not include " + RuleFile
                    + ", so it may rename what Lua reaches by name ([ReflectionUse] types, [DoNotGen] members, xLua's own code, hotfix fields). "
                    + "Write the file with XLua > Obfuscation > Write obfuz Rule File and add it to Symbol Obfuscation > Rule Files");
            }
#if HOTFIX_ENABLE
            if (settings.ObfuscationOrder <= HotfixInjectOrder)
            {
                Debug.LogWarning("xLua: obfuz runs at callback order " + settings.ObfuscationOrder + ", not after xLua's hotfix injection ("
                    + HotfixInjectOrder + "); the injector must see the original names. Keep obfuz's default order (10000)");
            }
#endif
        }
    }

    class ObfuzBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            ObfuzAdapter.BeforeBuild();
        }
    }
}
#endif
