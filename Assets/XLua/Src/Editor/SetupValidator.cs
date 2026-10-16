/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#if !XLUA_GENERAL
using UnityEngine;
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using XLua;

namespace CSObjectWrapEditor
{
    // "XLua/Validate Setup": reports configuration problems and changes
    // nothing. Generator's static constructor creates a LuaEnv, which throws
    // when the native plugin is missing or does not match, so the plugin is
    // checked first and Generator is only used once it passed.
    public static class SetupValidator
    {
        // Checks the current setup; returns one message per problem.
        public static List<string> Validate()
        {
#if !XLUA_GENERAL && HOTFIX_ENABLE
            const bool hotfix = true;
#else
            const bool hotfix = false;
#endif
            return Validate(XLua.Utils.GetAllTypes(), hotfix, "./Tools/");
        }

        // types: where the configuration is read from. hotfixEnabled: whether
        // HOTFIX_ENABLE is defined, so toolsPath must hold the hotfix injector.
        public static List<string> Validate(IEnumerable<Type> types, bool hotfixEnabled, string toolsPath)
        {
            var problems = new List<string>();
            bool pluginOk = checkPlugin(problems);
            checkGeneratedCode(problems);
            checkLuaCATS(problems, pluginOk);
            if (hotfixEnabled) checkHotfixTools(problems, toolsPath);
            if (pluginOk) checkConfig(problems, types.ToList());
            return problems;
        }

#if !XLUA_GENERAL
        [MenuItem("XLua/Validate Setup", false, 5)]
        public static void ValidateFromMenu()
        {
            var problems = Validate();
            foreach (var problem in problems)
            {
                Debug.LogWarning("xLua setup: " + problem);
            }
            Debug.Log(problems.Count == 0 ? "xLua setup: no problems found." : "xLua setup: " + problems.Count + " problem(s) found.");
        }
#endif

        static bool checkPlugin(List<string> problems)
        {
            var field = typeof(LuaEnv).GetField("LIB_VERSION_EXPECT", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || !field.IsLiteral || field.FieldType != typeof(int))
            {
                problems.Add("Native plugin: cannot read the bridge version the C# code expects (LuaEnv.LIB_VERSION_EXPECT); the configuration was not checked.");
                return false;
            }
            int expected = (int)field.GetRawConstantValue();
            int actual;
            try
            {
                actual = XLua.LuaDLL.Lua.xlua_get_lib_version();
            }
            catch (Exception e)
            {
                // DllNotFoundException, EntryPointNotFoundException, BadImageFormatException.
                problems.Add("Native plugin: cannot call xlua_get_lib_version (" + e.GetType().Name + ": " + e.Message
                    + "). Build the plugin from build/ for this platform; the configuration was not checked.");
                return false;
            }
            if (actual != expected)
            {
                problems.Add("Native plugin: bridge version " + actual + ", but the C# code expects " + expected
                    + ". Rebuild the plugin from the build/ folder of this xLua version; the configuration was not checked.");
                return false;
            }
            return true;
        }

        static void checkGeneratedCode(List<string> problems)
        {
            string path = GeneratorConfig.common_path;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                problems.Add("Generated code: " + fullPath(path) + " does not exist. Run XLua > Generate Code before building a player; until then xLua uses reflection.");
            }
        }

        static void checkLuaCATS(List<string> problems, bool pluginOk)
        {
            string suffixProblem = Generator.LuaCATSFiles.SuffixProblem(GeneratorConfig.luacats_file_suffix);
            if (suffixProblem != null) problems.Add(suffixProblem);

            string path = GeneratorConfig.luacats_path;
            string writable = writableProblem(path);
            if (writable != null) problems.Add("LuaCATS: " + writable);

            if (string.IsNullOrEmpty(path)) return;
            string luarc = Generator.LuaCATSFiles.LuarcPath(path);
            if (luarc == null || !pluginOk) return;
            bool mayWrite;
            string luarcProblem = Generator.LuaCATSFiles.LuarcProblem(luarc, Generator.LuaCATSRuntime(),
                Generator.LuaCATSFiles.LuarcLibrary(luarc, path), out mayWrite);
            if (luarcProblem != null) problems.Add(luarcProblem);
        }

        // Generation creates the folder, so the nearest existing ancestor must
        // accept a new file. The probe file is deleted when it is closed.
        static string writableProblem(string path)
        {
            if (string.IsNullOrEmpty(path)) return "the output folder (luacats_path) is not set.";
            string full = fullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(full)) return full + " is a file, not a folder.";
            string dir = full;
            while (!Directory.Exists(dir))
            {
                if (File.Exists(dir)) return "cannot create " + full + ": " + dir + " is a file.";
                dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(dir)) return "cannot create " + full + ".";
            }
            try
            {
                string probe = Path.Combine(dir, ".xlua-write-check-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }
            }
            catch (Exception e)
            {
                return dir + " is not writable (" + e.Message + ").";
            }
            return null;
        }

        static void checkHotfixTools(List<string> problems, string toolsPath)
        {
            // build_tools.py copies these next to the injector; a Tools folder built
            // before the injector used Mono.Cecil.Rocks lacks that one.
            foreach (var name in new[] { "XLuaHotfixInject.exe", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll" })
            {
                string file = Path.Combine(toolsPath, name);
                if (!File.Exists(file))
                {
                    problems.Add("Hotfix: HOTFIX_ENABLE is defined, but " + fullPath(file)
                        + " is missing. Run python3 Tools/build_tools.py from the project root.");
                }
            }
        }

        const BindingFlags anyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // Configuration that names types or members by string, or members of
        // a type by name, silently does nothing once they are renamed.
        static void checkConfig(List<string> problems, List<Type> types)
        {
            Generator.GetGenConfig(types.Where(t => !t.IsGenericTypeDefinition));
            var byName = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in types.Concat(XLua.Utils.GetAllTypes()).Concat(Generator.LuaCallCSharp).Concat(Generator.ReflectionUse))
            {
                string name = t.ToString();
                if (!byName.ContainsKey(name)) byName.Add(name, t);
            }

            foreach (var entry in Generator.BlackList)
            {
                string text = entry == null ? "null" : "{" + string.Join(", ", entry.Select(e => "\"" + e + "\"").ToArray()) + "}";
                if (entry == null || entry.Count < 2)
                {
                    problems.Add("BlackList: entry " + text + " needs a type name and a member name.");
                    continue;
                }
                Type type;
                if (!byName.TryGetValue(entry[0], out type))
                {
                    problems.Add("BlackList: entry " + text + " names type " + entry[0] + ", which does not exist.");
                    continue;
                }
                // xLua compares the declaring type, so inherited members do not match.
                var members = type.GetMember(entry[1], anyMember | BindingFlags.DeclaredOnly);
                if (members.Length == 0)
                {
                    problems.Add("BlackList: entry " + text + ": " + type + " declares no member " + entry[1] + ".");
                    continue;
                }
                if (entry.Count > 2)
                {
                    var parameterTypes = entry.Skip(2).ToList();
                    bool found = members.OfType<MethodBase>().Any(m => m.GetParameters().Select(p => p.ParameterType.ToString()).SequenceEqual(parameterTypes));
                    if (!found)
                    {
                        problems.Add("BlackList: entry " + text + ": no " + type + "." + entry[1] + " overload takes ("
                            + string.Join(", ", parameterTypes.ToArray()) + ").");
                    }
                }
            }

            checkMemberNames(problems, "DoNotGen", Generator.DoNotGen.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value));
            checkMemberNames(problems, "AdditionalProperties", Generator.AdditionalProperties.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value));
        }

        static void checkMemberNames(List<string> problems, string what, Dictionary<Type, IEnumerable<string>> config)
        {
            foreach (var kv in config.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
            {
                foreach (var name in kv.Value.OrderBy(n => n, StringComparer.Ordinal))
                {
                    if (kv.Key.GetMember(name, anyMember).Length == 0)
                    {
                        problems.Add(what + ": " + kv.Key + " has no member " + name + ".");
                    }
                }
            }
        }

        static string fullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(not set)";
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
