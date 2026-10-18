/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The Editor's side of the xLua source generator (docs/design/source-generator.md,
// sections 5.3 and 5.7, milestone M5):
//
// - after each domain reload, an error for every assembly that has xLua
//   configuration but no generated bindings, which is what a generator that did
//   not run leaves (most often warning CS9057: the compiler is older than the
//   one the generator was built against);
// - before a build the same check, which fails an IL2CPP build when such an
//   assembly has [CSharpCallLua] types (they would throw InvalidCastException
//   in the player) and warns otherwise;
// - for the managed code stripper, one link.xml made of the fragments the
//   generator writes per assembly (LinkXml.Xml): the members Lua reaches by
//   reflection.
//
// All three do nothing unless the generator is installed (its DLL with the
// RoslynAnalyzer label), so projects on "Generate Code" see no change. They
// read the assemblies loaded in the Editor; a player build compiles the same
// configuration, except for code under player-only #if branches.

#if !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace XLua
{
    public static class SourceGenChecks
    {
        // The generator is installed: XLua.SourceGenerator.dll with the label
        // Unity runs analyzers by (Tools/build_source_generator.py --install).
        public static bool GeneratorInstalled()
        {
            return AssetDatabase.FindAssets("XLua.SourceGenerator l:RoslynAnalyzer").Length > 0;
        }

        static readonly Type[] configurationAttributes =
        {
            typeof(LuaCallCSharpAttribute), typeof(CSharpCallLuaAttribute), typeof(GCOptimizeAttribute), typeof(ReflectionUseAttribute),
        };

        // Assemblies with xLua configuration ([LuaCallCSharp], [CSharpCallLua],
        // [GCOptimize] or [ReflectionUse] on a type or at assembly level) and
        // without [assembly: XLua.Binding.GeneratedBindings]; the value says
        // whether the assembly has [CSharpCallLua] configuration.
        public static Dictionary<Assembly, bool> AssembliesWithoutBindings()
        {
            var configured = new Dictionary<Assembly, bool>();
            Action<Assembly, bool> mark = (assembly, callLua) =>
            {
                bool before;
                configured[assembly] = (configured.TryGetValue(assembly, out before) && before) || callLua;
            };
            foreach (var attribute in configurationAttributes)
            {
                foreach (var type in TypeCache.GetTypesWithAttribute(attribute))
                {
                    mark(type.Assembly, attribute == typeof(CSharpCallLuaAttribute));
                }
            }
            var xlua = typeof(LuaEnv).Assembly;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (assembly != xlua && !assembly.GetReferencedAssemblies().Any(r => r.Name == xlua.GetName().Name)) continue;
                object[] attributes;
                try
                {
                    attributes = assembly.GetCustomAttributes(false);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var a in attributes)
                {
                    if (configurationAttributes.Contains(a.GetType())) mark(assembly, a is CSharpCallLuaAttribute);
                }
            }
            var missing = new Dictionary<Assembly, bool>();
            foreach (var kv in configured)
            {
                bool hasBindings;
                try
                {
                    hasBindings = kv.Key.GetCustomAttributes(typeof(XLua.Binding.GeneratedBindingsAttribute), false).Length > 0;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!hasBindings) missing.Add(kv.Key, kv.Value);
            }
            return missing;
        }

        internal const string LikelyCauses = "the xLua source generator did not run for it. Likely causes: the compiler is older than the one the "
            + "generator needs (warning CS9057 in the Console); XLua.SourceGenerator.dll lost its RoslynAnalyzer label; the assembly does not "
            + "reference the XLua assembly next to which the generator is installed; Unity is older than 2021.3";

        // The Editor layer: after each domain reload.
        [InitializeOnLoadMethod]
        static void CheckAfterReload()
        {
            EditorApplication.delayCall += () =>
            {
                if (!GeneratorInstalled()) return;
                foreach (var kv in AssembliesWithoutBindings().OrderBy(kv => kv.Key.GetName().Name, StringComparer.Ordinal))
                {
                    Debug.LogError("xLua: assembly " + kv.Key.GetName().Name + " has xLua configuration but no generated bindings, so its types are bound "
                        + "through reflection; " + LikelyCauses);
                }
            };
        }

        // The fragments the generator wrote (LinkXml.Xml in each assembly's
        // generated namespace), merged by assembly.
        public static string MergedLinkXml()
        {
            var merged = new SortedDictionary<string, SortedDictionary<string, XmlElement>>(StringComparer.Ordinal);
            var document = new XmlDocument();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                object[] markers;
                try
                {
                    markers = assembly.GetCustomAttributes(typeof(XLua.Binding.GeneratedBindingsAttribute), false);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (XLua.Binding.GeneratedBindingsAttribute marker in markers)
                {
                    if (marker.Bindings == null) continue;
                    var holder = assembly.GetType(marker.Bindings.Namespace + ".LinkXml", false);
                    var field = holder == null ? null : holder.GetField("Xml", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var xml = field == null ? null : field.GetRawConstantValue() as string;
                    if (string.IsNullOrEmpty(xml)) continue;
                    var fragment = new XmlDocument();
                    fragment.LoadXml(xml);
                    foreach (XmlElement a in fragment.DocumentElement.SelectNodes("assembly"))
                    {
                        string name = a.GetAttribute("fullname");
                        SortedDictionary<string, XmlElement> types;
                        if (!merged.TryGetValue(name, out types)) merged.Add(name, types = new SortedDictionary<string, XmlElement>(StringComparer.Ordinal));
                        foreach (XmlElement t in a.SelectNodes("type"))
                        {
                            string typeName = t.GetAttribute("fullname");
                            XmlElement existing;
                            if (!types.TryGetValue(typeName, out existing))
                            {
                                types.Add(typeName, (XmlElement)document.ImportNode(t, true));
                            }
                            else if (existing.GetAttribute("preserve") != "all")
                            {
                                if (t.GetAttribute("preserve") == "all")
                                {
                                    types[typeName] = (XmlElement)document.ImportNode(t, true);
                                }
                                else
                                {
                                    foreach (XmlElement m in t.ChildNodes.OfType<XmlElement>()) existing.AppendChild(document.ImportNode(m, true));
                                }
                            }
                        }
                    }
                }
            }
            var linker = document.CreateElement("linker");
            document.AppendChild(linker);
            foreach (var a in merged)
            {
                var assembly = document.CreateElement("assembly");
                assembly.SetAttribute("fullname", a.Key);
                foreach (var t in a.Value.Values) assembly.AppendChild(t);
                linker.AppendChild(assembly);
            }
            using (var writer = new StringWriter())
            {
                document.Save(writer);
                return writer.ToString();
            }
        }
    }

    // The build layer (section 5.3, owner decision 9).
    class SourceGenBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!SourceGenChecks.GeneratorInstalled()) return;
            var missing = SourceGenChecks.AssembliesWithoutBindings();
            if (missing.Count == 0) return;
            var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            bool il2cpp = PlayerSettings.GetScriptingBackend(target) == ScriptingImplementation.IL2CPP;
            var names = string.Join(", ", missing.Keys.Select(a => a.GetName().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var callLua = missing.Where(kv => kv.Value).Select(kv => kv.Key.GetName().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (il2cpp && callLua.Length > 0)
            {
                throw new BuildFailedException("xLua: " + string.Join(", ", callLua) + " ha" + (callLua.Length == 1 ? "s" : "ve")
                    + " [CSharpCallLua] configuration but no generated bindings, so its delegates and interfaces would fail in this IL2CPP player; "
                    + SourceGenChecks.LikelyCauses);
            }
            Debug.LogWarning("xLua: " + names + " ha" + (missing.Count == 1 ? "s" : "ve") + " xLua configuration but no generated bindings; "
                + SourceGenChecks.LikelyCauses);
        }
    }

    // One link.xml for UnityLinker with the members Lua reaches by reflection
    // (section 5.7).
    class SourceGenLinkXml : IUnityLinkerProcessor
    {
        public int callbackOrder { get { return 0; } }

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            if (!SourceGenChecks.GeneratorInstalled()) return null;
            string path = Path.GetFullPath(Path.Combine("Temp", "XLuaSourceGen", "link.xml"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, SourceGenChecks.MergedLinkXml());
            return path;
        }
    }
}
#endif
