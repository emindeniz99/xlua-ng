/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The configuration migration tool (docs/design/source-generator.md, section 4,
// "Migration tool", milestone M9). It evaluates the static configuration lists
// that only the legacy generator reads ([LuaCallCSharp] static List<Type> ...,
// computed ones included) the way "Generate Code" does (GetGenConfig), and
// writes the same configuration as assembly-level attributes, which the source
// generator reads:
//
//     [assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Transform))]
//
// one type per attribute, or with MigrationOptions.Group several types of a
// namespace in one attribute, one file per assembly that receives
// configuration, plus a report of what was
// converted, what was left out (and why the legacy generator left it out too)
// and what cannot be expressed. [BlackList] Func<MemberInfo, bool> filters are
// evaluated against the members of every wrapped type and written as one
// BlackList entry per member they match. Attributes on types and members are
// read by the source generator as they are and are not repeated.
//
// The core below runs in the Editor (XLua > Migrate Configuration to
// Attributes) and in XLuaGenerate --migrate (XLUA_GENERAL); only the Editor
// part at the end knows about Unity's assemblies and folders.

#if !XLUA_GENERAL
using UnityEngine;
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using XLua;

namespace CSObjectWrapEditor
{
    // Where the migration tool may write and what it knows about the project.
    public sealed class MigrationOptions
    {
        // The assembly that receives the configuration of types from assemblies
        // the tool cannot write into (the engine, .NET, packages, precompiled
        // DLLs): XLuaBindings, an asmdef the Editor creates (decision 4 of the
        // design), or Assembly-CSharp when xLua itself compiles into it.
        public string BindingsHost = "XLuaBindings";

        // The assembly for such types that exist only in the Editor (UnityEditor),
        // or null: they are reported, not written.
        public string EditorHost = null;

        // Whether the tool may write configuration into this assembly. A
        // project's own types are configured in the assembly that defines them.
        public Func<Assembly, bool> CanWrite = assembly => false;

        // Whether the source generator runs when this assembly compiles (it
        // references xLua and is compiled from source). Attributes on the types
        // of other assemblies, such as precompiled DLLs, are written into the
        // bindings host, since no generator sees them where they are. Null: the
        // assemblies CanWrite accepts.
        public Func<Assembly, bool> GeneratorRunsIn = null;

        // Whether this assembly exists only in the Editor.
        public Func<Assembly, bool> IsEditorOnly = assembly => assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal);

        // The file that already holds the converted form of a list (by its name,
        // such as "ExampleGenConfig.LuaCallCSharp"), or null. Such lists are left
        // out, so that running the tool twice, or in a project that contains
        // xLua's converted examples, gives no duplicate entries.
        public Func<string, string> ConvertedElsewhere = list => null;

        // Whether to convert the lists declared in this assembly; null: all. The
        // others still count for what Generate Code would wrap (filters, the
        // types a BlackList entry names), but get no attributes.
        public Func<Assembly, bool> ConvertListsIn = null;

        // The source file of a configuration class, or null when unknown; used to
        // point at #if branches, whose entries depend on the defines at tool time.
        public Func<Type, string> SourceFile = type => null;

        // The scripting defines of the project, for the report.
        public IEnumerable<string> Defines = new string[0];

        // How the file header names the tool.
        public string Tool = "XLua > Migrate Configuration to Attributes";

        // Write the types of one list that share an attribute, its flags and a
        // namespace into one attribute, one typeof per line:
        //
        //     [assembly: XLua.LuaCallCSharp(
        //         typeof(UnityEngine.GameObject),
        //         typeof(UnityEngine.Transform))]
        //
        // instead of one attribute per type (the default, whose diffs show one
        // line per configured type). LuaCallCSharp, CSharpCallLua, GCOptimize,
        // ReflectionUse and Hotfix only; BlackList, DoNotGen and
        // AdditionalProperties name members and stay one per entry.
        public bool Group = false;
    }

    public sealed class MigrationFile
    {
        public string Assembly;
        public string Text;
        // Configured types and members; Attributes is the number of attributes
        // written for them (fewer with MigrationOptions.Group).
        public int Entries;
        public int Attributes;
        // The configuration lists this file converts ("Class.member").
        public List<string> Lists = new List<string>();
    }

    public sealed class MigrationResult
    {
        // One file per assembly, sorted by assembly name.
        public List<MigrationFile> Files = new List<MigrationFile>();
        // The assemblies whose types the bindings host configures, for its
        // asmdef references.
        public List<string> HostReferences = new List<string>();
        // Lists that could not be evaluated; the files then miss their entries.
        public List<string> Errors = new List<string>();
        // Markdown.
        public string Report;
        // One line for the console.
        public string Summary;
    }

    public static partial class Generator
    {
        // The line that records, in a written file, which list it converts.
        public const string MigratedListMarker = "// xlua-migrated-list: ";

        enum CfgKind { LuaCallCSharp, CSharpCallLua, GCOptimize, ReflectionUse, Hotfix, BlackList, DoNotGen, AdditionalProperties }

        static readonly Type[] cfgAttributes =
        {
            typeof(LuaCallCSharpAttribute), typeof(CSharpCallLuaAttribute), typeof(GCOptimizeAttribute), typeof(ReflectionUseAttribute),
            typeof(HotfixAttribute), typeof(BlackListAttribute), typeof(DoNotGenAttribute), typeof(AdditionalPropertiesAttribute),
        };

        // A configuration list: a static field or property with a configuration attribute.
        sealed class CfgList
        {
            public string Name;          // Class.member
            public CfgKind Kind;
            public MemberInfo Member;
            public bool Computed;        // a property: its getter computes the list at tool time
            public int Flag;             // GCOptimize and Hotfix flags
            public List<Type> Types = new List<Type>();
            public List<List<string>> Entries;
            public Func<MemberInfo, bool> Filter;
            public Dictionary<Type, List<string>> Members;
            public string Error;
            public string ConvertedIn;
            public int Written;
        }

        sealed class CfgLine
        {
            public string Target;
            public string Source;   // the list or filter it comes from
            public string Text;     // the attribute without "[assembly: " and "]"
            // For MigrationOptions.Group: the attribute (LuaCallCSharp...), the
            // typeof spelling, the flags and the type's namespace; Kind is null
            // for entries that name members.
            public string Kind;
            public string TypeCs;
            public string Flag;
            public string Namespace;
        }

        sealed class MigrationReport
        {
            public readonly List<string> Converted = new List<string>();
            public readonly List<string> LeftOut = new List<string>();
            public readonly List<string> NotConverted = new List<string>();
            public readonly List<string> Check = new List<string>();
            public readonly List<string> ByName = new List<string>();
        }

        // The configured type: a single Type or an IEnumerable<Type> (Generator.cs AddToList).
        static List<Type> typesOf(object value)
        {
            if (value is Type) return new List<Type> { (Type)value };
            var many = value as IEnumerable<Type>;
            return many == null ? null : many.Where(t => t != null).ToList();
        }

        static int flagOf(MemberInfo member, Type attribute)
        {
            object ca = GetCustomAttribute(member, attribute);
            if (ca == null) return 0;
            var flag = ca.GetType().GetProperty("Flag");
            try
            {
                return flag == null ? 0 : Convert.ToInt32(flag.GetValue(ca, null));
            }
            catch
            {
                return 0;
            }
        }

        static string listName(MemberInfo member)
        {
            return member.DeclaringType.FullName.Replace('+', '.') + "." + member.Name;
        }

        // The lists, in the order GetGenConfig reads them: types in the given
        // order, fields before properties, and for each the attributes in the
        // order MergeCfg tests them.
        static List<CfgList> scanLists(List<Type> types, MigrationOptions options, MigrationReport report)
        {
            var lists = new List<CfgList>();
            foreach (var t in types)
            {
                if (!t.IsAbstract || !t.IsSealed) continue;
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var members = t.GetFields(flags).Cast<MemberInfo>().Concat(t.GetProperties(flags).Cast<MemberInfo>());
                foreach (var member in members)
                {
                    var kinds = Enumerable.Range(0, cfgAttributes.Length).Where(i => isDefined(member, cfgAttributes[i])).Select(i => (CfgKind)i).ToList();
                    if (kinds.Count == 0) continue;
                    Type memberType = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;
                    string name = listName(member);
                    if (options.ConvertListsIn != null && !options.ConvertListsIn(t.Assembly))
                    {
                        report.LeftOut.Add("`" + name + "`: declared in assembly " + assemblyName(t) + ", whose lists this run does not convert.");
                        continue;
                    }
                    if (t.FullName == "XLua.SysGenConfig")
                    {
                        report.LeftOut.Add("`" + name + "`: xLua's own defaults, which xLua's assembly also states as `[assembly: GCOptimize]` and `[assembly: AdditionalProperties]` (GenAttributes.cs).");
                        continue;
                    }
                    object value = null;
                    string error = null;
                    try
                    {
                        value = member is FieldInfo ? ((FieldInfo)member).GetValue(null) : ((PropertyInfo)member).GetValue(null, null);
                    }
                    catch (Exception e)
                    {
                        var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                        error = inner.GetType().Name + ": " + inner.Message;
                    }
                    foreach (var kind in kinds)
                    {
                        var list = new CfgList { Name = name, Kind = kind, Member = member, Computed = member is PropertyInfo, Error = error };
                        if (error == null && !readList(list, memberType, value, report)) continue;
                        if (list.Error == null) list.ConvertedIn = options.ConvertedElsewhere(name);
                        lists.Add(list);
#if !XLUA_GENERAL
#pragma warning disable 618
                        // [LuaCallCSharp(GenFlag.GCOptimize)] also lists the types under GCOptimize (MergeCfg).
                        if (kind == CfgKind.LuaCallCSharp && list.Error == null && (flagOf(member, typeof(LuaCallCSharpAttribute)) & (int)GenFlag.GCOptimize) != 0)
#pragma warning restore 618
                        {
                            lists.Add(new CfgList { Name = name, Kind = CfgKind.GCOptimize, Member = member, Computed = list.Computed, Types = list.Types, ConvertedIn = list.ConvertedIn });
                        }
#endif
                    }
                }
            }
            return lists;
        }

        // Fills the list from its value as MergeCfg (and, for Hotfix, HotfixConfig.mergeConfig)
        // reads it; false for a member that neither reads.
        static bool readList(CfgList list, Type memberType, object value, MigrationReport report)
        {
            switch (list.Kind)
            {
                case CfgKind.LuaCallCSharp:
                case CfgKind.CSharpCallLua:
                case CfgKind.GCOptimize:
                case CfgKind.ReflectionUse:
                    list.Types = typesOf(value);
                    if (list.Types == null)
                    {
                        list.Error = "not a Type or an IEnumerable<Type> (Generate Code fails on it too)";
                        list.Types = new List<Type>();
                    }
                    list.Flag = list.Kind == CfgKind.GCOptimize ? flagOf(list.Member, typeof(GCOptimizeAttribute)) : 0;
                    return true;
                case CfgKind.Hotfix:
                    if (!typeof(IEnumerable<Type>).IsAssignableFrom(memberType)) break;
                    list.Types = typesOf(value) ?? new List<Type>();
                    list.Flag = flagOf(list.Member, typeof(HotfixAttribute));
                    return true;
                case CfgKind.BlackList:
                    if (typeof(List<List<string>>).IsAssignableFrom(memberType))
                    {
                        list.Entries = value as List<List<string>> ?? new List<List<string>>();
                        return true;
                    }
                    if (typeof(Func<MemberInfo, bool>).IsAssignableFrom(memberType))
                    {
                        list.Filter = value as Func<MemberInfo, bool>;
                        return true;
                    }
                    break;
                case CfgKind.DoNotGen:
                case CfgKind.AdditionalProperties:
                    if (typeof(Dictionary<Type, List<string>>).IsAssignableFrom(memberType))
                    {
                        list.Members = value as Dictionary<Type, List<string>> ?? new Dictionary<Type, List<string>>();
                        return true;
                    }
                    break;
            }
            report.LeftOut.Add("`" + list.Name + "`: [" + list.Kind + "] on a " + memberType.Name + ", which no generator reads.");
            return false;
        }

        // ---- C# for typeof(...) ----

        static readonly Dictionary<Type, string> csKeywords = new Dictionary<Type, string>
        {
            { typeof(object), "object" }, { typeof(string), "string" }, { typeof(bool), "bool" }, { typeof(char), "char" },
            { typeof(byte), "byte" }, { typeof(sbyte), "sbyte" }, { typeof(short), "short" }, { typeof(ushort), "ushort" },
            { typeof(int), "int" }, { typeof(uint), "uint" }, { typeof(long), "long" }, { typeof(ulong), "ulong" },
            { typeof(float), "float" }, { typeof(double), "double" }, { typeof(decimal), "decimal" },
        };

        // Names of types in the global namespace: a qualified name that starts with
        // one of them needs global:: to mean the namespace.
        static HashSet<string> globalTypeNames = new HashSet<string>();

        static string assemblyName(Type type)
        {
            return type.Assembly.GetName().Name;
        }

        static bool accessibleFrom(Type type, string target)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                bool same = assemblyName(t) == target;
                if (t.IsNested)
                {
                    if (!(t.IsNestedPublic || (same && (t.IsNestedAssembly || t.IsNestedFamORAssem)))) return false;
                }
                else if (!(t.IsPublic || (same && t.IsNotPublic)))
                {
                    return false;
                }
            }
            return true;
        }

        static bool isIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_')) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        static string stripArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        // The C# spelling of a type for typeof(...) in assembly target, or null
        // with the reason when there is none (by-ref, pointer, generic parameter,
        // a type that code in target cannot see, a partly open type).
        static string csType(Type type, string target, out string problem)
        {
            problem = null;
            if (type.IsByRef || type.IsPointer)
            {
                problem = "a by-ref or pointer type";
                return null;
            }
            if (type.IsGenericParameter)
            {
                problem = "a generic parameter";
                return null;
            }
            if (type.IsArray)
            {
                string element = csType(type.GetElementType(), target, out problem);
                return element == null ? null : element + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            }
            string keyword;
            if (csKeywords.TryGetValue(type, out keyword)) return keyword;
            bool open = type.IsGenericTypeDefinition;
            if (type.ContainsGenericParameters && !open)
            {
                problem = "a partly open generic type";
                return null;
            }
            var chain = new List<Type>();
            for (var t = type; t != null; t = t.DeclaringType) chain.Insert(0, t);
            if (chain.Any(t => !isIdentifier(stripArity(t.Name))))
            {
                problem = "a compiler-generated name";
                return null;
            }
            if (!accessibleFrom(type, target))
            {
                problem = "not accessible from assembly " + target;
                return null;
            }
            Type[] args = type.IsGenericType ? type.GetGenericArguments() : new Type[0];
            var sb = new StringBuilder();
            string ns = chain[0].Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                if (globalTypeNames.Contains(ns.Split('.')[0])) sb.Append("global::");
                sb.Append(ns).Append('.');
            }
            int used = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0) sb.Append('.');
                var level = chain[i];
                int total = level.IsGenericType ? level.GetGenericArguments().Length : 0;
                int own = total - used;
                sb.Append(stripArity(level.Name));
                if (own > 0)
                {
                    sb.Append('<');
                    for (int k = 0; k < own; k++)
                    {
                        if (k > 0) sb.Append(open ? "," : ", ");
                        if (!open)
                        {
                            string arg = csType(args[used + k], target, out problem);
                            if (arg == null) return null;
                            sb.Append(arg);
                        }
                    }
                    sb.Append('>');
                }
                used = Math.Max(used, total);
            }
            return sb.ToString();
        }

        static string csString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string csFlags(Type enumType, int value)
        {
            if (value == 0) return null;
            var parts = new List<string>();
            int rest = value;
            foreach (FieldInfo f in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                int bit = Convert.ToInt32(f.GetValue(null));
                if (bit == 0 || (rest & bit) != bit) continue;
                if (f.IsDefined(typeof(ObsoleteAttribute), false)) continue;   // Stateful is an error to name
                parts.Add("XLua." + enumType.Name + "." + f.Name);
                rest &= ~bit;
            }
            if (rest != 0) parts.Add("(XLua." + enumType.Name + ")" + rest);
            return string.Join(" | ", parts.ToArray());
        }

        // ---- which assembly a type's configuration goes to ----

        // The types a type is made of: itself or its definition, its type
        // arguments and array or by-ref element types, recursively.
        static IEnumerable<Type> components(Type type)
        {
            if (type.HasElementType)
            {
                foreach (var c in components(type.GetElementType())) yield return c;
                yield break;
            }
            if (type.IsGenericParameter) yield break;
            yield return type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                foreach (var arg in type.GetGenericArguments())
                    foreach (var c in components(arg)) yield return c;
            }
        }

        static bool references(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetReferencedAssemblies().Any(r => r.Name == name);
            }
            catch
            {
                return false;
            }
        }

        // The assembly that receives a type's configuration: the project assembly
        // that defines it (or the one that defines a type argument), otherwise the
        // bindings host, or the Editor host for an Editor-only type. Null when no
        // assembly can (an Editor-only type without an Editor host).
        static string homeOf(Type type, MigrationOptions options)
        {
            var writable = components(type).Select(c => c.Assembly).Distinct().Where(a => options.CanWrite(a)).ToList();
            if (writable.Count > 0)
            {
                // The one that references the others, when there are several.
                var top = writable.FirstOrDefault(a => writable.All(o => o == a || references(a, o.GetName().Name)));
                return (top ?? writable[0]).GetName().Name;
            }
            if (components(type).Any(c => options.IsEditorOnly(c.Assembly))) return options.EditorHost;
            return options.BindingsHost;
        }

        // Whether code in assembly target can name the type: every project
        // assembly the type is made of is target or referenced by it.
        static bool canHold(string target, Type type, MigrationOptions options, Dictionary<string, Assembly> loaded)
        {
            if (target == null) return false;
            Assembly targetAssembly;
            loaded.TryGetValue(target, out targetAssembly);
            foreach (var a in components(type).Select(c => c.Assembly).Distinct())
            {
                if (!options.CanWrite(a)) continue;
                string name = a.GetName().Name;
                if (name != target && (targetAssembly == null || !references(targetAssembly, name))) return false;
            }
            return target != options.BindingsHost || !components(type).Any(c => options.IsEditorOnly(c.Assembly));
        }

        const string NotOwnType = "not a type of the project: its members differ between versions and build targets";

        // ---- the members a [BlackList] entry or filter matches ----

        const BindingFlags declaredAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags declaredPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static IEnumerable<MemberInfo> namedMembers(Type type, string name)
        {
            if (name == ".ctor") return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Cast<MemberInfo>();
            return type.GetMember(name, declaredAll);
        }

        // As the source generator's XLSG003 check (Config.cs MatchesBlackList):
        // fields, properties and events by name, methods by exact parameter types.
        static bool blackListMatches(Type type, List<string> entry)
        {
            foreach (var m in namedMembers(type, entry[1]))
            {
                if (m is MethodBase)
                {
                    if (BlackListRules.MatchesMethod(entry, (MethodBase)m)) return true;
                }
                else if (m is FieldInfo || m is PropertyInfo || m is EventInfo)
                {
                    return true;
                }
            }
            return false;
        }

        // Members that the legacy generator leaves out whatever the filters say.
        static bool excludedWithoutFilters(MemberInfo m, List<List<string>> entries)
        {
            if (isDefined(m, typeof(BlackListAttribute)) || isObsolete(m)) return true;
            if (m is FieldInfo && isUnsupportedType(((FieldInfo)m).FieldType)) return true;
            if (m is PropertyInfo && isUnsupportedType(((PropertyInfo)m).PropertyType)) return true;
            var method = m as MethodBase;
            if (method != null)
            {
                if (method.GetParameters().Any(p => isUnsupportedType(p.ParameterType))) return true;
                if (method is MethodInfo && isUnsupportedType(((MethodInfo)method).ReturnType)) return true;
                return entries.Any(e => BlackListRules.MatchesMethod(e, method));
            }
            return entries.Any(e => BlackListRules.MatchesMember(e, m));
        }

        // The members of a wrapped type that the legacy generator passes to the
        // filters (getClassInfo, GenEnumWrap and the extension method map).
        static IEnumerable<MemberInfo> filteredMembers(Type type)
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Cast<MemberInfo>()
                .Concat(type.GetMethods(declaredPublic).Cast<MemberInfo>())
                .Concat(type.GetProperties(declaredPublic).Cast<MemberInfo>())
                .Concat(type.GetFields(declaredPublic).Cast<MemberInfo>())
                .Concat(type.GetEvents(declaredPublic).Cast<MemberInfo>());
        }

        // The BlackList attribute for one member: by typeof when the type and every
        // parameter type have a C# spelling in target, by name otherwise or when
        // byName is already true (a type the project does not own).
        static string blackListFor(Type type, MemberInfo member, string target, ref bool byName)
        {
            string problem;
            string typeName = byName ? null : csType(type, target, out problem);
            string name = member is ConstructorInfo ? ".ctor" : member.Name;
            var method = member as MethodBase;
            var parameters = method == null ? new ParameterInfo[0] : method.GetParameters();
            var typed = new List<string>();
            foreach (var p in parameters)
            {
                string pt = typeName == null ? null : csType(p.ParameterType, target, out problem);
                if (pt == null) break;
                typed.Add("typeof(" + pt + ")");
            }
            byName = byName || typeName == null || typed.Count != parameters.Length;
            if (!byName)
            {
                return "XLua.BlackList(typeof(" + typeName + "), " + csString(name) + string.Concat(typed.Select(t => ", " + t).ToArray()) + ")";
            }
            return "XLua.BlackList(" + csString(type.ToString()) + ", " + csString(name)
                + string.Concat(parameters.Select(p => ", " + csString(p.ParameterType.ToString())).ToArray()) + ")";
        }

        static string leftOutReason(Type type, CfgKind kind)
        {
            if (type.IsGenericTypeDefinition) return "an open generic type";
            if (kind != CfgKind.ReflectionUse && !IsPublic(type)) return "not public";
            if (isObsolete(type)) return "[Obsolete]";
            if (kind == CfgKind.LuaCallCSharp && typeof(Delegate).IsAssignableFrom(type))
                return "a delegate (Lua calls delegates through reflection; list it under CSharpCallLua if C# calls Lua through it)";
            if (kind != CfgKind.ReflectionUse && IsUnnameable(type)) return "a type that generated code cannot name (compiler-generated, or a private or protected nested type, also as a generic argument)";
            if (kind == CfgKind.LuaCallCSharp && type.IsArray) return "an array type (every array uses the common array metatable)";
            if (kind == CfgKind.CSharpCallLua && (type == typeof(Delegate) || type == typeof(MulticastDelegate))) return "Delegate itself";
            return null;
        }

        // The C# files that declare each class or struct name, for
        // MigrationOptions.SourceFile: the first file in the given order wins.
        public static Dictionary<string, string> IndexClassSources(IEnumerable<string> files)
        {
            var index = new Dictionary<string, string>();
            var declaration = new Regex(@"\b(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)");
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }
                foreach (Match m in declaration.Matches(text))
                {
                    if (!index.ContainsKey(m.Groups[1].Value)) index.Add(m.Groups[1].Value, file);
                }
            }
            return index;
        }

        // Runs the conversion over the types the legacy generator would read
        // (Generate Code reads XLua.Utils.GetAllTypes()).
        public static MigrationResult MigrateConfiguration(IEnumerable<Type> types, MigrationOptions options)
        {
            var result = new MigrationResult();
            var report = new MigrationReport();
            var all = types.Where(t => t != null).Distinct().ToList();
            globalTypeNames = new HashSet<string>(all.Where(t => string.IsNullOrEmpty(t.Namespace) && !t.IsNested).Select(t => stripArity(t.Name)));
            var loaded = new Dictionary<string, Assembly>();
            foreach (var a in all.Select(t => t.Assembly).Distinct())
            {
                if (!loaded.ContainsKey(a.GetName().Name)) loaded.Add(a.GetName().Name, a);
            }
            string attributePrefix = globalTypeNames.Contains("XLua") ? "global::" : "";

            // The configuration as Generate Code sees it, with its filters (GetGenConfig).
            try
            {
                type_has_extension_methods = null;
                GetGenConfig(all);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                result.Errors.Add("Generate Code cannot evaluate the configuration either: " + inner.GetType().Name + ": " + inner.Message);
                result.Report = "# xLua configuration migration\n\nNothing was written. " + result.Errors[0] + "\n";
                result.Summary = "xLua configuration migration failed: " + result.Errors[0];
                return result;
            }
            Func<Assembly, bool> generatorRuns = options.GeneratorRunsIn ?? options.CanWrite;
            var lists = scanLists(all, options, report);
            // Attributes on types of assemblies that the source generator does not
            // compile (precompiled DLLs) act like lists: no generator sees them there.
            foreach (var kind in new[] { CfgKind.LuaCallCSharp, CfgKind.CSharpCallLua, CfgKind.GCOptimize, CfgKind.ReflectionUse })
            {
                var effective = kind == CfgKind.LuaCallCSharp ? LuaCallCSharp : kind == CfgKind.CSharpCallLua ? CSharpCallLua : kind == CfgKind.GCOptimize ? GCOptimizeList : ReflectionUse;
                foreach (var group in effective.Where(t => isDefined(t, cfgAttributes[(int)kind]) && !generatorRuns(t.Assembly)).GroupBy(t => assemblyName(t)))
                {
                    string name = "[" + kind + "] on the types of " + group.Key;
                    lists.Add(new CfgList { Name = name, Kind = kind, Types = group.ToList(), ConvertedIn = options.ConvertedElsewhere(name) });
                }
            }
            foreach (var list in lists.Where(l => l.Error != null))
            {
                result.Errors.Add("`" + list.Name + "` could not be evaluated: " + list.Error);
            }
            var live = lists.Where(l => l.Error == null && l.ConvertedIn == null).ToList();
            foreach (var list in lists.Where(l => l.Error == null && l.ConvertedIn != null).GroupBy(l => l.Name).Select(g => g.First()))
            {
                report.LeftOut.Add("`" + list.Name + "`: already converted in `" + list.ConvertedIn + "`.");
            }

            var lines = new List<CfgLine>();
            // Types configured in the bindings host besides the wrapped ones, for its references.
            var hostTypes = new List<Type>();
            Action<string, string, string> add = (target, source, text) =>
            {
                if (lines.Any(l => l.Target == target && l.Text == text)) return;
                lines.Add(new CfgLine { Target = target, Source = source, Text = text });
            };
            // XLua.<kind>(typeof(cs)[, flag]), which MigrationOptions.Group may merge.
            Action<string, string, CfgKind, Type, string, string> addTyped = (target, source, kind, type, cs, flag) =>
            {
                string text = "XLua." + kind + "(typeof(" + cs + ")" + (flag == null ? "" : ", " + flag) + ")";
                if (lines.Any(l => l.Target == target && l.Text == text)) return;
                lines.Add(new CfgLine { Target = target, Source = source, Text = text, Kind = kind.ToString(), TypeCs = cs, Flag = flag, Namespace = type.Namespace ?? "" });
            };
            Func<Type, CfgKind, bool> typeLevel = (type, kind) => isDefined(type, cfgAttributes[(int)kind]) && generatorRuns(type.Assembly);

            // [LuaCallCSharp]: where each wrapped type is wrapped. Types configured
            // on the type itself are wrapped in their own assembly.
            var wrapped = new HashSet<Type>(LuaCallCSharp);
            var wrapTarget = new Dictionary<Type, string>();
            var wrapSource = new Dictionary<Type, string>();
            foreach (var t in LuaCallCSharp.Where(t => typeLevel(t, CfgKind.LuaCallCSharp))) wrapTarget[t] = assemblyName(t);
            foreach (var list in live.Where(l => l.Kind == CfgKind.LuaCallCSharp))
            {
                foreach (var t in list.Types)
                {
                    if (wrapTarget.ContainsKey(t) || wrapSource.ContainsKey(t)) continue;
                    if (!wrapped.Contains(t))
                    {
                        report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: " + (leftOutReason(t, CfgKind.LuaCallCSharp) ?? "left out by the legacy generator") + "; the legacy generator does not wrap it either.");
                        wrapSource[t] = null;
                        continue;
                    }
                    string home = homeOf(t, options);
                    if (home == null)
                    {
                        report.NotConverted.Add("`" + t + "` in `" + list.Name + "`: an Editor-only type, and no Editor assembly receives configuration; add `[assembly: XLua.LuaCallCSharp(typeof(" + t + "))]` to an Editor assembly that references XLua.");
                        wrapSource[t] = null;
                        continue;
                    }
                    wrapTarget[t] = home;
                    wrapSource[t] = list.Name;
                }
            }
            // Extension methods are merged into the wrapper of the type they extend
            // only when both are wrapped in the same assembly: the source generator
            // sees one compilation. Move a list-configured type to its extension class.
            type_has_extension_methods = null;
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                foreach (var t in LuaCallCSharp.ToList())
                {
                    string tTarget;
                    if (!wrapTarget.TryGetValue(t, out tTarget)) continue;
                    foreach (var ext in GetExtensionMethods(t).Select(m => m.DeclaringType).Distinct())
                    {
                        string eTarget;
                        if (!wrapTarget.TryGetValue(ext, out eTarget) || eTarget == tTarget) continue;
                        if (wrapSource.ContainsKey(t) && wrapSource[t] != null && canHold(eTarget, t, options, loaded))
                        {
                            wrapTarget[t] = tTarget = eTarget;
                            moved = true;
                        }
                        else if (wrapSource.ContainsKey(ext) && wrapSource[ext] != null && canHold(tTarget, ext, options, loaded))
                        {
                            wrapTarget[ext] = tTarget;
                            moved = true;
                        }
                    }
                }
                if (!moved) break;
            }
            foreach (var t in LuaCallCSharp.Where(t => wrapTarget.ContainsKey(t)))
            {
                foreach (var ext in GetExtensionMethods(t).Select(m => m.DeclaringType).Distinct())
                {
                    string eTarget;
                    if (wrapTarget.TryGetValue(ext, out eTarget) && eTarget != wrapTarget[t])
                    {
                        report.Check.Add("The extension methods of `" + ext + "` (wrapped in " + eTarget + ") for `" + t + "` (wrapped in " + wrapTarget[t]
                            + ") are not in `" + t + "`'s wrapper: the source generator merges extension methods only within one assembly. Configure both in one assembly.");
                    }
                }
            }
            foreach (var list in live.Where(l => l.Kind == CfgKind.LuaCallCSharp))
            {
                foreach (var t in list.Types.Where(t => wrapSource.ContainsKey(t) && wrapSource[t] == list.Name))
                {
                    string problem;
                    string cs = csType(t, wrapTarget[t], out problem);
                    if (cs == null)
                    {
                        report.NotConverted.Add("`" + t + "` in `" + list.Name + "`: no typeof spelling (" + problem + ").");
                        continue;
                    }
                    addTyped(wrapTarget[t], list.Name, CfgKind.LuaCallCSharp, t, cs, null);
                }
            }
            foreach (var t in LuaCallCSharp.Where(t => typeLevel(t, CfgKind.LuaCallCSharp)))
            {
                var first = live.FirstOrDefault(l => l.Kind == CfgKind.LuaCallCSharp && l.Types.Contains(t));
                if (first != null)
                    report.LeftOut.Add("`" + t + "` in `" + first.Name + "`: also `[LuaCallCSharp]` on the type, which the source generator reads.");
            }

            // [CSharpCallLua], [GCOptimize] and [ReflectionUse] lists.
            var gcTarget = new Dictionary<Type, string>();
            foreach (var t in GCOptimizeList.Where(t => typeLevel(t, CfgKind.GCOptimize))) gcTarget[t] = assemblyName(t);
            foreach (var kind in new[] { CfgKind.CSharpCallLua, CfgKind.GCOptimize, CfgKind.ReflectionUse })
            {
                var effective = new HashSet<Type>(kind == CfgKind.CSharpCallLua ? CSharpCallLua : kind == CfgKind.GCOptimize ? GCOptimizeList : ReflectionUse);
                var done = new HashSet<Type>();
                foreach (var list in live.Where(l => l.Kind == kind))
                {
                    foreach (var t in list.Types)
                    {
                        if (!done.Add(t)) continue;
                        if (typeLevel(t, kind))
                        {
                            report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: also `[" + kind + "]` on the type, which the source generator reads.");
                            continue;
                        }
                        // CSharpCallLua: the legacy generator also drops delegates and
                        // interfaces with ref readonly parameters that its compiler cannot
                        // call; the source generator decides that from its own compiler.
                        bool kept = effective.Contains(t) || (kind == CfgKind.CSharpCallLua && IsPublic(t) && !isObsolete(t) && !t.IsGenericTypeDefinition
                            && t != typeof(Delegate) && t != typeof(MulticastDelegate) && isRefReadOnlyUnusable(t));
                        if (!kept)
                        {
                            report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: " + (leftOutReason(t, kind) ?? "left out by the legacy generator") + "; the legacy generator does not use it either.");
                            continue;
                        }
                        if (kind == CfgKind.CSharpCallLua && !typeof(Delegate).IsAssignableFrom(t) && !t.IsInterface)
                        {
                            report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: neither a delegate nor an interface; the legacy generator makes no bridge for it either.");
                            continue;
                        }
                        string home = homeOf(t, options);
                        string problem = null;
                        string cs = home == null ? null : csType(t, home, out problem);
                        if (cs == null)
                        {
                            report.NotConverted.Add("`" + t + "` in `" + list.Name + "`: " + (home == null ? "an Editor-only type, and no Editor assembly receives configuration" : "no typeof spelling (" + problem + ")") + ".");
                            continue;
                        }
                        string flag = null;
                        if (kind == CfgKind.GCOptimize)
                        {
                            OptimizeFlag f;
                            flag = csFlags(typeof(OptimizeFlag), OptimizeCfg.TryGetValue(t, out f) ? (int)f : list.Flag);
                            gcTarget[t] = home;
                        }
                        if (home == options.BindingsHost) hostTypes.Add(t);
                        addTyped(home, list.Name, kind, t, cs, flag);
                    }
                }
            }

            // [BlackList] List<List<string>>: by typeof for a member of a wrapped type,
            // by name for everything else (members missing on this platform, types
            // that are not wrapped: they change nothing until a type is wrapped).
            var typesByName = new Dictionary<string, Type>();
            var ambiguous = new HashSet<string>();
            foreach (var t in all.Concat(LuaCallCSharp))
            {
                Type existing;
                string n = t.ToString();
                if (typesByName.TryGetValue(n, out existing)) { if (existing != t) ambiguous.Add(n); }
                else typesByName.Add(n, t);
            }
            // Names of types outside the given ones (parameter types such as
            // System.String) come from the assemblies loaded now.
            Func<string, Type> resolve = n =>
            {
                Type found;
                if (typesByName.TryGetValue(n, out found)) return ambiguous.Contains(n) ? null : found;
                found = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t;
                    try
                    {
                        t = a.GetType(n, false);
                    }
                    catch
                    {
                        continue;
                    }
                    if (t == null || t == found) continue;
                    if (found != null)
                    {
                        ambiguous.Add(n);
                        break;
                    }
                    found = t;
                }
                typesByName[n] = found;
                return ambiguous.Contains(n) ? null : found;
            };
            foreach (var list in live.Where(l => l.Kind == CfgKind.BlackList && l.Entries != null))
            {
                foreach (var entry in list.Entries)
                {
                    if (entry == null || entry.Count < 2)
                    {
                        report.LeftOut.Add("An entry of `" + list.Name + "` with fewer than two strings, which matches nothing.");
                        continue;
                    }
                    Type type = resolve(entry[0]);
                    string target = type != null && wrapTarget.ContainsKey(type) ? wrapTarget[type] : (type != null ? homeOf(type, options) : options.BindingsHost) ?? options.BindingsHost;
                    string text = null;
                    // Only the project's own types get the typeof form: the members
                    // of engine, .NET, package and precompiled types differ between
                    // versions and build targets, and a typeof entry whose member
                    // is missing is warning XLSG003 (the by-name form gives an info).
                    bool own = type != null && options.CanWrite(type.Assembly);
                    if (type != null && own && wrapped.Contains(type) && blackListMatches(type, entry))
                    {
                        string problem;
                        string cs = csType(type, target, out problem);
                        var ps = entry.Skip(2).Select(n =>
                        {
                            Type pt = resolve(n);
                            return pt == null ? null : csType(pt, target, out problem);
                        }).ToList();
                        if (cs != null && ps.All(p => p != null))
                        {
                            text = "XLua.BlackList(typeof(" + cs + "), " + csString(entry[1]) + string.Concat(ps.Select(p => ", typeof(" + p + ")").ToArray()) + ")";
                        }
                    }
                    if (text == null)
                    {
                        text = "XLua.BlackList(" + string.Join(", ", entry.Select(csString).ToArray()) + ")";
                        report.ByName.Add("`" + string.Join(", ", entry.ToArray()) + "` (" + list.Name + "): "
                            + (type == null ? "no loaded type has that name" : !own ? NotOwnType : !wrapped.Contains(type) ? "the type is not wrapped" : !blackListMatches(type, entry) ? "no member matches" : "a parameter type has no typeof spelling"));
                    }
                    add(target, list.Name, text);
                }
            }

            // [BlackList] Func<MemberInfo, bool>: evaluated against the members of the
            // wrapped types, as the legacy generator applies them.
            var liveEntries = live.Where(l => l.Kind == CfgKind.BlackList && l.Entries != null).SelectMany(l => l.Entries)
                .Where(e => e != null && e.Count >= 2).ToList();
            foreach (var list in live.Where(l => l.Kind == CfgKind.BlackList && l.Entries == null))
            {
                if (list.Filter == null)
                {
                    report.LeftOut.Add("`" + list.Name + "`: a null filter.");
                    continue;
                }
                int matched = 0, redundant = 0;
                string failure = null;
                foreach (var t in LuaCallCSharp.Where(t => wrapTarget.ContainsKey(t)))
                {
                    foreach (var m in filteredMembers(t))
                    {
                        bool hit;
                        try
                        {
                            hit = list.Filter(m);
                        }
                        catch (Exception e)
                        {
                            if (failure == null) failure = t + "." + m.Name + ": " + e.GetType().Name + ": " + e.Message;
                            continue;
                        }
                        if (!hit) continue;
                        if (excludedWithoutFilters(m, liveEntries))
                        {
                            redundant++;
                            continue;
                        }
                        bool notOwn = !options.CanWrite(t.Assembly);
                        bool byName = notOwn;
                        add(wrapTarget[t], list.Name + " (filter, evaluated)", blackListFor(t, m, wrapTarget[t], ref byName));
                        if (byName) report.ByName.Add("`" + t + "." + m.Name + "` (" + list.Name + "): " + (notOwn ? NotOwnType : "a type in its signature has no typeof spelling"));
                        matched++;
                    }
                }
                list.Written = matched;
                report.Check.Add("`" + list.Name + "` is a filter: it matched " + matched + (matched == 1 ? " member" : " members") + " of the wrapped types, written as BlackList entries"
                    + (redundant > 0 ? " (" + redundant + " more that are left out anyway: [Obsolete], [BlackList], a list entry, or a pointer or ref struct in the signature)" : "")
                    + ". The entries are a snapshot: run the tool again after changing the filter, the wrapped types or the Unity version.");
                if (failure != null)
                    report.Check.Add("`" + list.Name + "` threw for " + failure + "; such members were taken as not matching (Generate Code fails on them).");
            }

            // [DoNotGen] and [AdditionalProperties] dictionaries.
            var foreignMembers = new List<string>();
            foreach (var kind in new[] { CfgKind.DoNotGen, CfgKind.AdditionalProperties })
            {
                var order = new List<Type>();
                var sources = new Dictionary<Type, string>();
                foreach (var list in live.Where(l => l.Kind == kind))
                {
                    foreach (var kv in list.Members)
                    {
                        if (kv.Key == null || sources.ContainsKey(kv.Key)) continue;
                        order.Add(kv.Key);
                        sources.Add(kv.Key, list.Name);
                    }
                }
                foreach (var t in order)
                {
                    // DoNotGen merges the lists (a set per type), AdditionalProperties
                    // keeps the first list's names, as GetGenConfig does.
                    IEnumerable<string> names = kind == CfgKind.DoNotGen
                        ? live.Where(l => l.Kind == kind).SelectMany(l => l.Members.Where(kv => kv.Key == t).SelectMany(kv => kv.Value ?? new List<string>()))
                        : (AdditionalProperties.ContainsKey(t) ? AdditionalProperties[t] : new List<string>());
                    names = names.Where(n => n != null).Distinct().ToList();
                    var extensionNames = wrapTarget.ContainsKey(t) ? new HashSet<string>(GetExtensionMethods(t).Where(m => wrapTarget.ContainsKey(m.DeclaringType) && wrapTarget[m.DeclaringType] == wrapTarget[t]).Select(m => m.Name)) : new HashSet<string>();
                    var missing = names.Where(n => kind == CfgKind.DoNotGen
                        ? !namedMembers(t, n).Any() && !extensionNames.Contains(n)
                        : !namedMembers(t, n).Any(m => m is PropertyInfo || m is FieldInfo)).ToList();
                    if (missing.Count > 0)
                        report.LeftOut.Add("`" + string.Join("`, `", missing.ToArray()) + "` of `" + t + "` in `" + sources[t] + "`: no such " + (kind == CfgKind.DoNotGen ? "member" : "property or field") + "; it changes nothing in the legacy generator either.");
                    var kept = names.Where(n => !missing.Contains(n)).ToList();
                    if (kept.Count == 0) continue;
                    string target = kind == CfgKind.DoNotGen
                        ? (wrapTarget.ContainsKey(t) ? wrapTarget[t] : homeOf(t, options))
                        : (gcTarget.ContainsKey(t) ? gcTarget[t] : homeOf(t, options));
                    string problem = null;
                    string cs = target == null ? null : csType(t, target, out problem);
                    if (cs == null || t.IsGenericTypeDefinition)
                    {
                        report.NotConverted.Add("`" + t + "` in `" + sources[t] + "`: " + (target == null ? "an Editor-only type, and no Editor assembly receives configuration" : t.IsGenericTypeDefinition ? "an open generic type" : "no typeof spelling (" + problem + ")") + ".");
                        continue;
                    }
                    if (target == options.BindingsHost) hostTypes.Add(t);
                    if (!options.CanWrite(t.Assembly)) foreignMembers.Add("`" + t + "` ([" + kind + "])");
                    add(target, sources[t], "XLua." + kind + "(typeof(" + cs + ")" + string.Concat(kept.Select(n => ", " + csString(n)).ToArray()) + ")");
                }
            }

            if (foreignMembers.Count > 0)
            {
                report.Check.Add("DoNotGen and AdditionalProperties entries name members of types the project does not own ("
                    + string.Join(", ", foreignMembers.ToArray()) + "). They have no by-name form, so a member that another Unity version or build target"
                    + " does not have gives warning XLSG003 there; remove it from the entry, or put the entry under `#if`.");
            }

            // [Hotfix] lists, filtered as HotfixConfig.mergeConfig does, in the assembly
            // that defines the type: the hotfix bridges are generated next to it.
            var hotfixDone = new HashSet<Type>();
            foreach (var list in live.Where(l => l.Kind == CfgKind.Hotfix))
            {
                foreach (var t in list.Types)
                {
                    if (!hotfixDone.Add(t)) continue;
                    if (t.IsDefined(typeof(ObsoleteAttribute), false) || t.IsEnum || typeof(Delegate).IsAssignableFrom(t)
                        || (t.IsGenericType && !t.IsGenericTypeDefinition)
                        || (t.Namespace != null && (t.Namespace == "XLua" || t.Namespace.StartsWith("XLua."))))
                    {
                        report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: the hotfix injector skips it (obsolete, an enum, a delegate, a constructed generic type or an xLua type).");
                        continue;
                    }
                    if (typeLevel(t, CfgKind.Hotfix))
                    {
                        report.LeftOut.Add("`" + t + "` in `" + list.Name + "`: also `[Hotfix]` on the type.");
                        continue;
                    }
                    string target = options.CanWrite(t.Assembly) ? assemblyName(t) : null;
                    string problem = null;
                    string cs = target == null ? null : csType(t, target, out problem);
                    if (cs == null)
                    {
                        report.NotConverted.Add("`" + t + "` in `" + list.Name + "` (Hotfix): " + (target == null ? "its assembly " + assemblyName(t) + " is not one the tool writes into, and the assembly form must be in the assembly that defines the type" : "no typeof spelling (" + problem + ")") + ".");
                        continue;
                    }
                    string flag = csFlags(typeof(HotfixFlag), list.Flag);
                    addTyped(target, list.Name, CfgKind.Hotfix, t, cs, flag);
                }
            }

            // ---- the files ----
            foreach (var group in lines.GroupBy(l => l.Target).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var file = new MigrationFile { Assembly = group.Key };
                var sb = new StringBuilder();
                sb.Append("// xLua configuration of assembly ").Append(group.Key).Append(", in the attribute form\n");
                sb.Append("// that the xLua source generator reads. Generate Code reads the configuration\n");
                sb.Append("// lists named below, not this file. Written by the migration tool:\n");
                sb.Append("//\n");
                sb.Append("//     ").Append(options.Tool).Append('\n');
                sb.Append("//\n");
                sb.Append("// Run it again after changing the lists; it rewrites this file. See\n");
                sb.Append("// docs/MIGRATING.md, \"Converting configuration lists\".\n");
                sb.Append("//\n");
                foreach (var source in group.Select(l => l.Source.Replace(" (filter, evaluated)", "")).Distinct())
                {
                    sb.Append(MigratedListMarker).Append(source).Append('\n');
                    file.Lists.Add(source);
                }
                foreach (var bySource in group.GroupBy(l => l.Source))
                {
                    sb.Append('\n').Append("// ").Append(bySource.Key).Append('\n');
                    // Each attribute in the place of its first entry.
                    var attributes = new List<List<CfgLine>>();
                    var byKey = new Dictionary<string, List<CfgLine>>();
                    foreach (var line in bySource)
                    {
                        List<CfgLine> same;
                        string key = line.Kind + "|" + line.Flag + "|" + line.Namespace;
                        if (!options.Group || line.Kind == null || !byKey.TryGetValue(key, out same))
                        {
                            same = new List<CfgLine>();
                            attributes.Add(same);
                            if (options.Group && line.Kind != null) byKey.Add(key, same);
                        }
                        same.Add(line);
                    }
                    foreach (var same in attributes)
                    {
                        if (same.Count == 1)
                        {
                            sb.Append("[assembly: ").Append(attributePrefix).Append(same[0].Text).Append("]\n");
                        }
                        else
                        {
                            // The constructor with the flags first: XLua.Hotfix(XLua.HotfixFlag.IntKey, typeof(A), typeof(B)).
                            sb.Append("[assembly: ").Append(attributePrefix).Append("XLua.").Append(same[0].Kind).Append('(')
                                .Append(same[0].Flag == null ? "" : same[0].Flag + ",").Append('\n');
                            for (int i = 0; i < same.Count; i++)
                            {
                                sb.Append("    typeof(").Append(same[i].TypeCs).Append(')').Append(i + 1 < same.Count ? ",\n" : ")]\n");
                            }
                        }
                        file.Entries += same.Count;
                        file.Attributes++;
                    }
                }
                file.Text = sb.ToString();
                result.Files.Add(file);
            }
            foreach (var t in wrapTarget.Where(kv => kv.Value == options.BindingsHost).Select(kv => kv.Key)) hostTypes.Add(t);
            result.HostReferences = hostTypes.SelectMany(components).Select(c => assemblyName(c)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

            foreach (var list in live.GroupBy(l => l.Name))
            {
                int count = lines.Count(l => l.Source == list.Key || l.Source == list.Key + " (filter, evaluated)");
                var targets = lines.Where(l => l.Source == list.Key || l.Source == list.Key + " (filter, evaluated)").Select(l => l.Target).Distinct().ToArray();
                report.Converted.Add("`" + list.Key + "` ([" + string.Join("], [", list.Select(l => l.Kind.ToString()).Distinct().ToArray()) + "]"
                    + (list.First().Computed ? ", computed by a property getter" : "") + "): " + count + (count == 1 ? " entry" : " entries")
                    + (targets.Length > 0 ? " in " + string.Join(", ", targets) : "") + ".");
                if (list.First().Computed && list.Any(l => l.Kind != CfgKind.BlackList))
                {
                    report.Check.Add("`" + list.Key + "` is computed when it is read: the attributes are the list as it was when the tool ran ("
                        + list.Where(l => l.Types != null).SelectMany(l => l.Types).Distinct().Count() + " types). Run the tool again after adding types, assemblies or packages, or upgrading Unity.");
                }
            }
            checks(all, lists, options, report);

            result.Report = reportText(result, report, options);
            result.Summary = "xLua configuration migration: " + result.Files.Sum(f => f.Attributes) + " attributes"
                + (options.Group ? " (" + result.Files.Sum(f => f.Entries) + " entries)" : "") + " in " + result.Files.Count + " file(s) from "
                + live.Select(l => l.Name).Distinct().Count() + " list(s); " + report.NotConverted.Count + " not converted, " + result.Errors.Count + " error(s)";
            type_has_extension_methods = null;
            return result;
        }

        static void checks(List<Type> all, List<CfgList> lists, MigrationOptions options, MigrationReport report)
        {
            // Configuration that has no attribute form.
            foreach (var t in all)
            {
                try
                {
                    checkGenAttributes(t, report);
                }
                catch (Exception)
                {
                    // A type whose members do not load; Generate Code does not read it either.
                }
            }
            checkDefines(lists, options, report);
        }

        static void checkGenAttributes(Type t, MigrationReport report)
        {
            {
                foreach (var m in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MemberInfo>()
                    .Concat(t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MemberInfo>()))
                {
                    if (isDefined(m, typeof(GenPathAttribute)))
                        report.NotConverted.Add("`[GenPath]` on `" + listName(m) + "`: the output folder of Generate Code. The source generator writes no files.");
                }
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (isDefined(m, typeof(GenCodeMenuAttribute)))
                        report.NotConverted.Add("`[GenCodeMenu]` on `" + listName(m) + "`: a custom generator run by Generate Code (custom templates, `Generator.CustomGen`). The source generator has no replacement (decision 10 of the design); keep running it through Generate Code, or rewrite it.");
                }
            }
        }

        static void checkDefines(List<CfgList> lists, MigrationOptions options, MigrationReport report)
        {
            var defines = new HashSet<string>(options.Defines ?? new string[0]);
            if (defines.Contains("GEN_CODE_MINIMIZE"))
                report.Check.Add("`GEN_CODE_MINIMIZE`: the source generator writes minimized wrappers with the define, as Generate Code does. Keep it in the Player Settings scripting defines, so that xLua and every assembly with bindings compile with it (warning XLSG013 otherwise).");
            if (defines.Contains("GENERIC_SHARING"))
                report.NotConverted.Add("`GENERIC_SHARING`: dropped (decision 2 of the design); the source generator wraps closed generic types one by one.");
            if (lists.Any(l => l.Kind == CfgKind.Hotfix))
                report.Check.Add("[Hotfix] lists: the hotfix injector reads the lists and `[Hotfix]` on types, not the assembly form, which is for the source generator's hotfix bridges (milestone M7). Keep the lists while you inject.");
            report.Check.Add("Keep the lists while the project uses Generate Code, which reads only them. The reflection wrappers in the Editor also read the [BlackList] lists and filters (BlackListRules). Remove the lists when you no longer use Generate Code.");

            // #if branches in the configuration classes.
            foreach (var t in lists.Where(l => l.Member != null).Select(l => l.Member.DeclaringType).Distinct())
            {
                string file = options.SourceFile(t);
                if (file == null) continue;
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }
                var conditions = Regex.Matches(text, @"^[ \t]*#[ \t]*(?:if|elif)\b[ \t]*([^\r\n]*?)[ \t]*$", RegexOptions.Multiline).Cast<Match>()
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                if (conditions.Count > 0)
                {
                    report.Check.Add("`" + file + "` (" + t.FullName + ") has `#if` branches (" + string.Join(", ", conditions.Take(6).Select(c => "`" + c + "`").ToArray())
                        + (conditions.Count > 6 ? "..." : "") + "): the tool evaluated them with the defines it ran with, so the attributes hold the entries of "
                        + (defines.Count > 0 ? "those defines" : "this build") + " only. Put entries that depend on the platform under the same `#if` in the written file by hand,"
                        + " or rely on the by-name BlackList form, which only gives an info where the member is missing.");
                }
            }
        }

        static string reportText(MigrationResult result, MigrationReport report, MigrationOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("# xLua configuration migration\n\n");
            sb.Append("Written by ").Append(options.Tool).Append(". Configuration lists, which only Generate Code reads, ")
              .Append("were converted to assembly-level attributes, which the source generator reads. ")
              .Append("Attributes on types and members are read by both and were not repeated.\n\n");
            sb.Append("| Assembly | Attributes | Lists |\n|---|---|---|\n");
            foreach (var f in result.Files)
            {
                sb.Append("| ").Append(f.Assembly).Append(" | ").Append(f.Entries).Append(" | ").Append(string.Join(", ", f.Lists.ToArray())).Append(" |\n");
            }
            if (result.Files.Count == 0) sb.Append("| (none) | 0 | |\n");
            Action<string, IEnumerable<string>> section = (title, items) =>
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                sb.Append("\n## ").Append(title).Append("\n\n");
                foreach (var item in list) sb.Append("- ").Append(item).Append('\n');
            };
            section("Errors", result.Errors);
            section("Converted", report.Converted);
            section("Not converted", report.NotConverted);
            section("Check by hand", report.Check);
            if (report.ByName.Count > 0)
            {
                section("BlackList entries kept by name", new[] { report.ByName.Count + (report.ByName.Count == 1 ? " entry names its" : " entries name their") + " type and member as strings, as the list did; the source generator matches them by name and reports info XLSG003 when nothing matches (for example a member that exists only on some platforms)." }
                    .Concat(report.ByName));
            }
            section("Left out, as the legacy generator leaves them out", report.LeftOut);
            return sb.ToString();
        }

        // The lists a written file converts, from its marker lines.
        public static IEnumerable<string> MigratedLists(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith(MigratedListMarker, StringComparison.Ordinal)) yield return line.Substring(MigratedListMarker.Length).Trim();
            }
        }
    }

#if !XLUA_GENERAL
    // XLua > Migrate Configuration to Attributes. Writes, for each assembly that
    // receives configuration, XLuaConfig.cs into a folder of that assembly:
    //
    //     Assembly-CSharp                   Assets/XLuaConfig/XLuaConfig.cs
    //     Assembly-CSharp-Editor            Assets/XLuaConfig/Editor/XLuaConfig.cs
    //     Assembly-CSharp-firstpass         Assets/Plugins/XLuaConfig/XLuaConfig.cs
    //     an asmdef under Assets            next to its .asmdef
    //     XLuaBindings (asmdef layout)      Assets/XLuaConfig/XLuaBindings/, with XLuaBindings.asmdef
    //
    // and the report to Assets/XLuaConfig/XLuaMigrationReport.md. When xLua
    // compiles into Assembly-CSharp (the default layout) everything goes into
    // Assembly-CSharp, since no asmdef could reference xLua.
    public static class ConfigMigrationMenu
    {
        public const string Folder = "Assets/XLuaConfig";
        const string BindingsHost = "XLuaBindings";

        [MenuItem("XLua/Migrate Configuration to Attributes", false, 6)]
        public static void MigrateMenu()
        {
            Migrate(false);
        }

        // The same, with the types of a list that share an attribute, flags and
        // namespace in one attribute (MigrationOptions.Group). Also for
        // -executeMethod.
        [MenuItem("XLua/Migrate Configuration to Attributes (Grouped)", false, 7)]
        public static void MigrateGrouped()
        {
            Migrate(false, true);
        }

        // For -executeMethod: everything into Assembly-CSharp (and its Editor
        // assembly), without an XLuaBindings asmdef.
        public static void MigrateIntoAssemblyCSharp()
        {
            Migrate(true);
        }

        public static void MigrateWithBindingsAsmdef()
        {
            Migrate(false);
        }

        static string FileFor(string assembly)
        {
            switch (assembly)
            {
                case "Assembly-CSharp": return Folder + "/XLuaConfig.cs";
                case "Assembly-CSharp-Editor": return Folder + "/Editor/XLuaConfig.cs";
                case "Assembly-CSharp-firstpass": return "Assets/Plugins/XLuaConfig/XLuaConfig.cs";
                case "Assembly-CSharp-Editor-firstpass": return "Assets/Plugins/Editor/XLuaConfig/XLuaConfig.cs";
                case BindingsHost: return Folder + "/" + BindingsHost + "/XLuaConfig.cs";
            }
            string asmdef = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly);
            return string.IsNullOrEmpty(asmdef) ? null : Path.GetDirectoryName(asmdef).Replace('\\', '/') + "/XLuaConfig.cs";
        }

        public static MigrationResult Migrate(bool oneAssembly)
        {
            return Migrate(oneAssembly, false);
        }

        public static MigrationResult Migrate(bool oneAssembly, bool group)
        {
            var xlua = typeof(LuaEnv).Assembly;
            string xluaName = xlua.GetName().Name;
            bool defaultLayout = xluaName == "Assembly-CSharp";
            var scripts = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor)
                .GroupBy(a => a.name).ToDictionary(g => g.Key, g => g.First());
            Func<System.Reflection.Assembly, bool> canWrite = assembly =>
            {
                string name = assembly.GetName().Name;
                UnityEditor.Compilation.Assembly script;
                if (name == xluaName || name == "XLua.Editor" || !scripts.TryGetValue(name, out script)) return false;
                if (!script.sourceFiles.Any(f => f.StartsWith("Assets/", StringComparison.Ordinal))) return false;
                if (FileFor(name) == null) return false;
                return assembly.GetReferencedAssemblies().Any(r => r.Name == xluaName) || name.StartsWith("Assembly-CSharp", StringComparison.Ordinal);
            };
            string host = defaultLayout || oneAssembly ? "Assembly-CSharp" : BindingsHost;
            var named = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

            // Files the tool wrote before, and the lists they convert.
            var convertedIn = new Dictionary<string, string>();
            var existing = Directory.GetFiles("Assets", "XLuaConfig.cs", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).ToList();
            foreach (var path in existing)
            {
                foreach (var list in Generator.MigratedLists(File.ReadAllText(path)))
                {
                    if (!convertedIn.ContainsKey(list)) convertedIn.Add(list, path);
                }
            }
            var allTypes = XLua.Utils.GetAllTypes().ToList();
            var targetFiles = new HashSet<string>(allTypes.Select(t => t.Assembly.GetName().Name).Distinct().Concat(new[] { host, "Assembly-CSharp-Editor" })
                .Select(FileFor).Where(f => f != null));
            Dictionary<string, string> sources = null;
            var options = new MigrationOptions
            {
                BindingsHost = host,
                EditorHost = "Assembly-CSharp-Editor",
                CanWrite = canWrite,
                IsEditorOnly = assembly =>
                {
                    UnityEditor.Compilation.Assembly script;
                    string name = assembly.GetName().Name;
                    return name.StartsWith("UnityEditor", StringComparison.Ordinal)
                        || (scripts.TryGetValue(name, out script) && (script.flags & UnityEditor.Compilation.AssemblyFlags.EditorAssembly) != 0);
                },
                // A list converted in a file this run does not rewrite (xLua's
                // converted examples, a package) stays there.
                ConvertedElsewhere = list =>
                {
                    string path;
                    return convertedIn.TryGetValue(list, out path) && !targetFiles.Contains(path) ? path : null;
                },
                SourceFile = type =>
                {
                    if (sources == null)
                    {
                        sources = Generator.IndexClassSources(Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
                            .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal));
                    }
                    string found;
                    return sources.TryGetValue(type.Name, out found) ? found : null;
                },
                Defines = PlayerSettings.GetScriptingDefineSymbols(named).Split(';').Where(d => d.Length > 0).ToList(),
                Group = group,
            };
            if (group) options.Tool += " (Grouped)";
            var result = Generator.MigrateConfiguration(allTypes, options);
            if (result.Errors.Count > 0 && result.Files.Count == 0)
            {
                Debug.LogError(result.Summary);
                return result;
            }
            var written = new List<string>();
            foreach (var file in result.Files)
            {
                string path = FileFor(file.Assembly);
                if (path == null)
                {
                    Debug.LogError("xLua: no folder for assembly " + file.Assembly + "; its configuration is in the report");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path) || File.ReadAllText(path) != file.Text) File.WriteAllText(path, file.Text, new UTF8Encoding(false));
                written.Add(path);
                string owner = UnityEditor.Compilation.CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                if (owner != null && owner.EndsWith(".dll", StringComparison.Ordinal)) owner = owner.Substring(0, owner.Length - 4);
                if (owner != null && owner != file.Assembly && file.Assembly != BindingsHost)
                {
                    Debug.LogError("xLua: " + path + " compiles into " + owner + ", not " + file.Assembly + "; move it into a folder of " + file.Assembly);
                }
            }
            // Earlier files that now receive nothing keep their header only.
            foreach (var path in existing.Where(p => targetFiles.Contains(p) && !written.Contains(p)))
            {
                File.WriteAllText(path, "// xLua configuration: the last run of " + options.Tool + " wrote nothing for this assembly.\n", new UTF8Encoding(false));
            }
            if (result.Files.Any(f => f.Assembly == BindingsHost)) WriteBindingsAsmdef(result.HostReferences, scripts, xluaName);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/XLuaMigrationReport.md", result.Report, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log(result.Summary + ". Report: " + Folder + "/XLuaMigrationReport.md");
            foreach (var error in result.Errors) Debug.LogError("xLua migration: " + error);
            return result;
        }

        // XLuaBindings.asmdef: references XLua and the script assemblies of the
        // types it configures; auto-referenced, so that Assembly-CSharp sees its
        // GCOptimize configuration. References already in the file are kept.
        static void WriteBindingsAsmdef(List<string> references, Dictionary<string, UnityEditor.Compilation.Assembly> scripts, string xluaName)
        {
            string path = Folder + "/" + BindingsHost + "/" + BindingsHost + ".asmdef";
            var names = new List<string> { xluaName };
            // The generated bindings also name the Lua C API types of XLua.Core
            // (its own assembly in the asmdef and package layouts), and Unity does
            // not pass XLua's references on (XLSG012).
            string core = typeof(XLua.LuaDLL.Lua).Assembly.GetName().Name;
            if (core != xluaName) names.Add(core);
            names.AddRange(references.Where(r => scripts.ContainsKey(r) && r != xluaName && !r.StartsWith("Assembly-CSharp", StringComparison.Ordinal)));
            if (File.Exists(path))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(path), "\"references\"\\s*:\\s*\\[([^\\]]*)\\]"))
                {
                    foreach (Match r in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"")) names.Add(r.Groups[1].Value);
                }
            }
            names = names.Distinct().ToList();
            string text = "{\n    \"name\": \"" + BindingsHost + "\",\n    \"references\": [\n"
                + string.Join(",\n", names.Select(n => "        \"" + n + "\"").ToArray())
                + "\n    ],\n    \"includePlatforms\": [],\n    \"excludePlatforms\": [],\n    \"allowUnsafeCode\": false,\n"
                + "    \"overrideReferences\": false,\n    \"precompiledReferences\": [],\n    \"autoReferenced\": true,\n"
                + "    \"defineConstraints\": [],\n    \"versionDefines\": [],\n    \"noEngineReferences\": false\n}\n";
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
#endif
}
