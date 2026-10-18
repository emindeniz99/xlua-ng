/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The rule file xLua gives obfuz (https://github.com/focus-creative-games/obfuz,
// 4.0.0), the obfuscator xlua-ng supports with an adapter
// (docs/design/source-generator.md, milestone M8; docs/research/obfuscators.md).
// obfuz renames types and members of the assemblies it is told to obfuscate
// and does not read [Obfuscation]; rule files say what keeps its name. Lua
// finds generated and named types through the name table and wrapped members
// through literals, so the rules cover only what Lua reaches by name:
//
// - the fragments the source generator writes per assembly (ObfuzRules.Xml in
//   the generated namespace): [ReflectionUse] types, [DoNotGen] and fallback
//   members, enums read by name;
// - xLua's own code where it is compiled into an obfuscated assembly (the
//   default layout puts it into Assembly-CSharp): everything in the XLua
//   namespaces, which xLua reaches by name (CodeEmit, CS.XLua.* from Lua, the
//   legacy generated code's bridge scan);
// - with HOTFIX_ENABLE, the fields the injector adds (__Hotfix*, _c__Hotfix*,
//   which field-mode xlua.hotfix finds by name) and its flag type.
//
// This file has no Unity dependency, so the host test
// (Test/Native/obfuscation_check.py) builds the rules the same way;
// ObfuzAdapter.cs is the Editor's side (menu, build check).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace XLua
{
    public static class ObfuzRules
    {
        // Field-mode hotfix looks these fields up by name (LuaEnv.cs, xlua.hotfix).
        public static readonly string[] HotfixFieldPatterns = { "__Hotfix*", "_c__Hotfix*" };

        // The type the injector adds to mark an injected assembly (Hotfix.cs).
        public const string HotfixFlagType = "__XLUA_GEN.__XLUA_GEN_FLAG";

        // The generator's fragments in the given assemblies.
        public static List<string> Fragments(IEnumerable<Assembly> assemblies)
        {
            var fragments = new List<string>();
            foreach (var assembly in assemblies)
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
                    var holder = assembly.GetType(marker.Bindings.Namespace + ".ObfuzRules", false);
                    var field = holder == null ? null : holder.GetField("Xml", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var xml = field == null ? null : field.GetRawConstantValue() as string;
                    if (!string.IsNullOrEmpty(xml)) fragments.Add(xml);
                }
            }
            return fragments;
        }

        // Whether an assembly holds types of the XLua namespaces (xLua itself,
        // or generated code).
        public static bool HasXLuaCode(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            catch (Exception)
            {
                return false;
            }
            foreach (var t in types)
            {
                string ns = t.Namespace;
                if (ns != null && (ns == "XLua" || ns.StartsWith("XLua.", StringComparison.Ordinal))) return true;
            }
            return false;
        }

        // The rule document for the obfuscated assemblies: obfuz stops on a
        // rule for an assembly it does not obfuscate, so only those appear.
        // obfuscated: the names in obfuz's AssemblySettings; xluaAssemblies:
        // those of them that hold xLua code (HasXLuaCode); hotfix: whether
        // the injector runs (HOTFIX_ENABLE).
        public static string Build(IEnumerable<string> fragments, ICollection<string> obfuscated, ICollection<string> xluaAssemblies, bool hotfix)
        {
            var document = new XmlDocument();
            var byAssembly = new SortedDictionary<string, List<XmlElement>>(StringComparer.Ordinal);
            var seen = new HashSet<string>();
            Func<string, List<XmlElement>> rulesOf = name =>
            {
                List<XmlElement> list;
                if (!byAssembly.TryGetValue(name, out list)) byAssembly.Add(name, list = new List<XmlElement>());
                return list;
            };
            Action<string, XmlElement> add = (name, type) =>
            {
                // obfuz takes the last rule that sets obName; these only keep names,
                // so the order does not matter and duplicates can go.
                if (seen.Add(name + "\n" + type.OuterXml)) rulesOf(name).Add(type);
            };

            foreach (var xml in fragments)
            {
                var fragment = new XmlDocument();
                fragment.LoadXml(xml);
                foreach (XmlElement a in fragment.DocumentElement.SelectNodes("assembly"))
                {
                    string name = a.GetAttribute("name");
                    if (!obfuscated.Contains(name)) continue;
                    foreach (XmlElement t in a.SelectNodes("type")) add(name, (XmlElement)document.ImportNode(t, true));
                }
            }
            foreach (var name in obfuscated)
            {
                if (xluaAssemblies.Contains(name))
                {
                    add(name, TypeRule(document, "XLua.*", "0", "*"));
                }
                if (hotfix)
                {
                    var any = TypeRule(document, "*", null, null);
                    foreach (var pattern in HotfixFieldPatterns)
                    {
                        var f = document.CreateElement("field");
                        f.SetAttribute("name", pattern);
                        f.SetAttribute("obName", "0");
                        any.AppendChild(f);
                    }
                    add(name, any);
                    add(name, TypeRule(document, HotfixFlagType, "0", null));
                }
            }

            var root = document.CreateElement("obfuz");
            document.AppendChild(root);
            root.AppendChild(document.CreateComment(" Written by xLua (XLua > Obfuscation > Write obfuz Rule File, and before each build while obfuz lists this file); do not edit. "));
            foreach (var kv in byAssembly)
            {
                var a = document.CreateElement("assembly");
                a.SetAttribute("name", kv.Key);
                foreach (var t in kv.Value) a.AppendChild(t);
                root.AppendChild(a);
            }
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n", OmitXmlDeclaration = false };
            using (var writer = new StringWriter())
            {
                using (var xw = XmlWriter.Create(writer, settings)) document.Save(xw);
                return writer.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"") + "\n";
            }
        }

        static XmlElement TypeRule(XmlDocument document, string name, string obName, string applyToMembers)
        {
            var t = document.CreateElement("type");
            t.SetAttribute("name", name);
            if (obName != null) t.SetAttribute("obName", obName);
            if (applyToMembers != null) t.SetAttribute("applyToMembers", applyToMembers);
            return t;
        }
    }
}
