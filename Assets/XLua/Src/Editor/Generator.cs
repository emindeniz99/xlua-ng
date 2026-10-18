/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The reader of xLua's configuration lists (static [LuaCallCSharp],
// [CSharpCallLua], [GCOptimize], [ReflectionUse], [BlackList], [DoNotGen] and
// [AdditionalProperties] members): GetGenConfig evaluates them as the removed
// "Generate Code" menu did, with its filters. Nothing generates code from them
// any more (source generator milestone M11, docs/design/source-generator.md):
// the migration tool (ConfigMigration.cs) converts them into the attributes the
// source generator reads, Validate Setup reports them, and LuaCATS shares the
// member filters below. The partial class also holds those tools.

#if !XLUA_GENERAL
using UnityEngine;
using UnityEditor;
#endif
using System.Collections.Generic;
using System.IO;
using XLua;
using System;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CSObjectWrapEditor
{
    public static class GeneratorConfig
    {
        // common_path ([GenPath]) is where the hotfix injector writes its id maps
        // (Resources/hotfix_id_map*.lua.txt) and its Editor configuration; it was
        // also the output folder of Generate Code.
#if XLUA_GENERAL
        public static string common_path = "./Gen/";
        public static string luacats_path = "./LuaTypes/";
#else
        public static string common_path = Application.dataPath + "/XLua/Gen/";
        // Outside Assets, so Unity never imports the definition files as scripts.
        public static string luacats_path = Path.GetFullPath(Application.dataPath + "/../LuaTypes/");
#endif
        // Ending of the LuaCATS file names, for example ".d.lua"; it must end in ".lua".
        public static string luacats_file_suffix = ".lua";
        // true: CS and the namespace tables only declare the configured types,
        // so LuaLS reports other CS names instead of typing them `any`.
        public static bool luacats_strict = false;
        // The LuaLS configuration file the LuaCATS generator writes. null: .luarc.json
        // in the folder that contains luacats_path; empty: do not write one.
        public static string luacats_luarc_path = null;

        static GeneratorConfig()
        {
            foreach(var type in (from type in XLua.Utils.GetAllTypes()
            where type.IsAbstract && type.IsSealed
            select type))
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    apply(field, field.FieldType, () => field.GetValue(null));
                }

                foreach (var prop in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    apply(prop, prop.PropertyType, () => prop.GetValue(null, null));
                }
            }
        }

        static void apply(MemberInfo member, Type valueType, Func<object> get)
        {
            if (valueType == typeof(string) && member.IsDefined(typeof(GenPathAttribute), false))
            {
                common_path = get() as string;
                if (!common_path.EndsWith("/"))
                {
                    common_path = common_path + "/";
                }
            }
            if (valueType == typeof(string) && member.IsDefined(typeof(LuaCATSPathAttribute), false))
            {
                luacats_path = get() as string;
            }
            if (valueType == typeof(string) && member.IsDefined(typeof(LuaCATSFileSuffixAttribute), false))
            {
                luacats_file_suffix = get() as string;
            }
            if (valueType == typeof(bool) && member.IsDefined(typeof(LuaCATSStrictAttribute), false))
            {
                luacats_strict = (bool)get();
            }
            if (valueType == typeof(string) && member.IsDefined(typeof(LuaCATSLuarcPathAttribute), false))
            {
                luacats_luarc_path = get() as string;
            }
        }
    }

    // Marks a static string field or property with the folder of the hotfix id
    // maps (default: Assets/XLua/Gen/; GeneratorConfig.common_path).
    public class GenPathAttribute : Attribute
    {

    }

    // Marks a static string field or property with the output folder of
    // "XLua/Generate LuaCATS Definitions" (default: <project>/LuaTypes/).
    public class LuaCATSPathAttribute : Attribute
    {

    }

    // Marks a static string field or property with the ending of the LuaCATS
    // file names (default ".lua"), for example ".d.lua".
    public class LuaCATSFileSuffixAttribute : Attribute
    {

    }

    // Marks a static bool field or property; true omits the `[string] any`
    // fallback on CS and the namespace tables (default false).
    public class LuaCATSStrictAttribute : Attribute
    {

    }

    // Marks a static string field or property with the path of the .luarc.json
    // the LuaCATS generator writes; an empty string turns it off.
    public class LuaCATSLuarcPathAttribute : Attribute
    {

    }

    public static partial class Generator
    {
        // LuaCATS asks it which Lua runtime the plugin has (LuaCATSRuntime).
        static LuaEnv luaenv = new LuaEnv();

        static int OverloadCosting(MethodBase mi)
        {
            int costing = 0;

            if (!mi.IsStatic)
            {
                costing++;
            }

            foreach (var paraminfo in mi.GetParameters())
            {
                if ((!paraminfo.ParameterType.IsPrimitive ) && (paraminfo.IsIn || !paraminfo.IsOut))
                {
                    costing++;
                }
            }
            costing = costing * 10000 + (mi.GetParameters().Length + (mi.IsStatic ? 0 : 1));
            return costing;
        }

        static IEnumerable<Type> type_has_extension_methods = null;

        static IEnumerable<MethodInfo> GetExtensionMethods(Type extendedType)
        {
            if (type_has_extension_methods == null)
            {
                var gen_types = LuaCallCSharp;

                type_has_extension_methods = from type in gen_types
                                             where type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                                                    .Any(method => isDefined(method, typeof(ExtensionAttribute)))
                                             select type;
            }
            return from type in type_has_extension_methods
                   where type.IsSealed && !type.IsGenericType && !type.IsNested
                        from method in type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                        where isSupportedExtensionMethod(method, extendedType)
                        select method;
        }

        static bool isSupportedExtensionMethod(MethodBase method, Type extendedType)
        {
            if (!isDefined(method, typeof(ExtensionAttribute)))
                return false;
            var methodParameters = method.GetParameters();
            if (methodParameters.Length < 1)
                return false;

            var hasValidGenericParameter = false;
            for (var i = 0; i < methodParameters.Length; i++)
            {
                var parameterType = methodParameters[i].ParameterType;
                if (i == 0)
                {
                    if (parameterType.IsGenericParameter)
                    {
                        var parameterConstraints = parameterType.GetGenericParameterConstraints();
                        if (parameterConstraints.Length == 0) return false;
                        bool firstParamMatch = false;
                        foreach (var parameterConstraint in parameterConstraints)
                        {
                            if (parameterConstraint != typeof(ValueType) && parameterConstraint.IsAssignableFrom(extendedType))
                            {
                                firstParamMatch = true;
                            }
                        }
                        if (!firstParamMatch) return false;

                        hasValidGenericParameter = true;
                    }
                    else if (parameterType != extendedType)
                        return false;
                }
                // The other parameters as isSupportedGenericMethod takes them: a
                // generic parameter with a class constraint, but no generic type
                // over one (Action<T>), whose name generated code cannot write
                // (TemplateCommon indexed its nil FullName, Tencent/xLua#924).
                else if (!isSupportGenericParameter(parameterType, true, ref hasValidGenericParameter))
                {
                    return false;
                }
            }
            return hasValidGenericParameter || !method.ContainsGenericParameters;
        }

        static bool isObsolete(MemberInfo mb)
        {
            if (mb == null) return false;
            ObsoleteAttribute oa = GetCustomAttribute(mb, typeof(ObsoleteAttribute)) as ObsoleteAttribute;
#if XLUA_GENERAL && !XLUA_ALL_OBSOLETE || XLUA_JUST_EXCLUDE_ERROR
            return oa != null && oa.IsError;
#else
            return oa != null;
#endif
        }

        static bool isObsolete(Type type)
        {
            if (type == null) return false;
            if (isObsolete(type as MemberInfo))
            {
                return true;
            }
            return (type.DeclaringType != null) ? isObsolete(type.DeclaringType) : false;
        }

        // Pointers and ref structs (Span<T>, ReadOnlySpan<T>) cannot cross the
        // bridge: they cannot be boxed, stored, or used as generic arguments.
        // Type.IsByRefLike is missing from Unity's .NET Framework profile, so
        // the attribute is checked by name.
        static bool isUnsupportedType(Type type)
        {
            if (type.IsByRef) type = type.GetElementType();
            return type.IsPointer || type.GetCustomAttributes(false).Any(
                ca => ca.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");
        }

        // Roslyn before 4.10 reports CS0570 ("not supported by the language")
        // for any use of a member whose C# 12 `ref readonly` parameter carries
        // modreq(InAttribute), which Roslyn emits on virtual, abstract,
        // interface and delegate members (delegate Invoke included).
        // Non-virtual ones have no modreq and older compilers see a plain
        // `ref`. Unity 2021.3 to 6000.3 compile scripts with Roslyn 4.3 and
        // 6000.6 with 4.10; 6000.4 and 6000.5 were not checked, so they are
        // treated as old. XLUA_GENERAL keeps these members: mcs ignores the
        // modreq and calls them as `ref`.
#if UNITY_EDITOR && !UNITY_6000_6_OR_NEWER
        static readonly bool compilerRejectsRefReadOnlyModreq = true;
#else
        static readonly bool compilerRejectsRefReadOnlyModreq = false;
#endif

        // An indexer whose key is a C# 12 `ref readonly` parameter has no
        // spelling that every compiler accepts. Roslyn 4.3 reports the indexer
        // as not supported (CS1545) and only lets code call its accessors,
        // `get_Item(ref key)`; Roslyn 4.10 rejects accessor calls (CS0571) and
        // takes `this[ref key]` at every language version (a plain or `in`
        // argument is CS8773 below C# 12). Generated code uses the accessors
        // through 6000.3 and the indexer from 6000.6; 6000.4 and 6000.5 skip
        // these indexers, as their Roslyn was not checked. XLUA_GENERAL uses
        // the indexer (Mono's mcs can call no indexer with a by-ref key).
        // Indexers with `in` keys take a plain argument everywhere. Since the
        // wrapper templates went (M11) only the skipped case matters here:
        // the migration tool and LuaCATS leave those members out.
#if UNITY_EDITOR && !UNITY_6000_4_OR_NEWER
        static readonly bool refReadOnlyIndexerUnknown = false;
#elif UNITY_EDITOR && !UNITY_6000_6_OR_NEWER
        static readonly bool refReadOnlyIndexerUnknown = true;
#else
        static readonly bool refReadOnlyIndexerUnknown = false;
#endif

        static bool isRefReadOnlyIndexerAccessor(MethodBase method)
        {
            if (!method.IsSpecialName || method.GetParameters().Length == 0 || !XLua.Utils.IsRefReadOnlyParameter(method.GetParameters()[0])) return false;
            return method.DeclaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(p => p.GetIndexParameters().Length > 0 && (p.GetGetMethod(true) == method || p.GetSetMethod(true) == method));
        }

        // Members already reported by isRefReadOnlyUnusable. GenLuaCATS
        // clears it, so each run warns once per member.
        static HashSet<MemberInfo> refReadOnlyReported = new HashSet<MemberInfo>();

        static bool hasRefReadOnlyModreq(MethodBase method)
        {
            return method.GetParameters().Any(p => XLua.Utils.IsRefReadOnlyParameter(p)
                && p.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.InteropServices.InAttribute"));
        }

        // True, with a warning the first time, when generated code could not
        // use `method` (see compilerRejectsRefReadOnlyModreq). `owner` is the
        // type whose generated code needs it: the method's own type, or the
        // [CSharpCallLua] delegate or interface that is skipped as a whole.
        static bool isRefReadOnlyUnusable(MethodBase method, Type owner = null)
        {
            if (refReadOnlyIndexerUnknown && owner == null && method != null && isRefReadOnlyIndexerAccessor(method))
            {
#if UNITY_EDITOR
                if (refReadOnlyReported.Add(method))
                {
                    Debug.LogWarning("xLua skipped " + method.DeclaringType.FullName + "." + method.Name
                        + ": generated calls to indexers with a ref readonly key were not checked with the C# compiler in this Unity version");
                }
#endif
                return true;
            }
            if (!compilerRejectsRefReadOnlyModreq || method == null || !hasRefReadOnlyModreq(method)) return false;
            MemberInfo reported = owner ?? (MemberInfo)method;
            if (refReadOnlyReported.Add(reported))
            {
#if UNITY_EDITOR
                string what = owner == null
                    ? method.DeclaringType.FullName + "." + method.Name
                    : "[CSharpCallLua] " + owner.FullName + " (its " + method.DeclaringType.FullName + "." + method.Name + ")";
                Debug.LogWarning("xLua skipped " + what + ": the C# compiler in this Unity version cannot call members with ref readonly parameters (CS0570)");
#endif
            }
            return true;
        }

        static bool isRefReadOnlyUnusable(PropertyInfo property)
        {
            return isRefReadOnlyUnusable(property.GetGetMethod()) | isRefReadOnlyUnusable(property.GetSetMethod());
        }

        // A [CSharpCallLua] delegate (its Invoke) or interface (every method it
        // has to implement) that generated bridges cannot use.
        static bool isRefReadOnlyUnusable(Type type)
        {
            if (!compilerRejectsRefReadOnlyModreq) return false;
            IEnumerable<MethodInfo> methods = typeof(Delegate).IsAssignableFrom(type)
                ? new[] { type.GetMethod("Invoke") }
                : type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()).SelectMany(i => i.GetMethods()) : new MethodInfo[0];
            return methods.Where(m => m != null).FirstOrDefault(m => isRefReadOnlyUnusable(m, type)) != null;
        }

        static bool isMemberInBlackList(MemberInfo mb)
        {
            if (isDefined(mb, typeof(BlackListAttribute))) return true;
            if (mb is FieldInfo && isUnsupportedType((mb as FieldInfo).FieldType)) return true;
            if (mb is PropertyInfo && isUnsupportedType((mb as PropertyInfo).PropertyType)) return true;
            if (mb is PropertyInfo && isRefReadOnlyUnusable(mb as PropertyInfo)) return true;
            if (mb is MethodBase && isRefReadOnlyUnusable(mb as MethodBase)) return true;

            foreach(var filter in memberFilters)
            {
                if (filter(mb))
                {
                    return true;
                }
            }

            // The emitted and reflection wrappers match entries with the same
            // rules (BlackListRules).
            foreach (var exclude in BlackList)
            {
                if (BlackListRules.MatchesMember(exclude, mb))
                {
                    return true;
                }
            }

            return false;
        }

        static bool isMethodInBlackList(MethodBase mb)
        {
            if (isDefined(mb, typeof(BlackListAttribute))) return true;

            if (mb.GetParameters().Any(pInfo => isUnsupportedType(pInfo.ParameterType))) return true;
            if (mb is MethodInfo && isUnsupportedType((mb as MethodInfo).ReturnType)) return true;
            if (isRefReadOnlyUnusable(mb)) return true;

            foreach (var filter in memberFilters)
            {
                if (filter(mb))
                {
                    return true;
                }
            }

            foreach (var exclude in BlackList)
            {
                if (BlackListRules.MatchesMethod(exclude, mb)) return true;
            }
            return false;
        }

        static bool hasGenericParameter(Type type)
        {
            if (type.IsByRef || type.IsArray)
            {
                return hasGenericParameter(type.GetElementType());
            }
            if (type.IsGenericType)
            {
                foreach (var typeArg in type.GetGenericArguments())
                {
                    if (hasGenericParameter(typeArg))
                    {
                        return true;
                    }
                }
                return false;
            }
            return type.IsGenericParameter;
        }

        //Configure C# types exposed to Lua, such as standard library, Unity API, or third-party types.
        public static List<Type> LuaCallCSharp = null;

        //Configure generated adapters for C# calls into Lua (including event signatures). Only delegates and interfaces are supported.
        public static List<Type> CSharpCallLua = null;

        //Members excluded from code generation
        public static List<List<string>> BlackList = null;

        public static List<Type> GCOptimizeList = null;

        public static Dictionary<Type, List<string>> AdditionalProperties = null;

        public static List<Type> ReflectionUse = null;

        public static Dictionary<Type, OptimizeFlag> OptimizeCfg = null;

        public static Dictionary<Type, HashSet<string>> DoNotGen = null;

        public static List<Func<MemberInfo, bool>> memberFilters = null;

        static void AddToList(List<Type> list, Func<object> get, object attr)
        {
            object obj = get();
            if (obj is Type)
            {
                list.Add(obj as Type);
            }
            else if (obj is IEnumerable<Type>)
            {
                list.AddRange(obj as IEnumerable<Type>);
            }
            else
            {
                throw new InvalidOperationException("Only field/property with the type IEnumerable<Type> can be marked " + attr.GetType().Name);
            }
#if XLUA_GENERAL
            if (attr != null && attr.GetType().ToString() == typeof(GCOptimizeAttribute).ToString())
            {
                var flag = (OptimizeFlag)Convert.ToInt32(attr.GetType().GetProperty("Flag").GetValue(attr, null));
#else
            if (attr is GCOptimizeAttribute)
            {
                var flag = (attr as GCOptimizeAttribute).Flag;
#endif
                if (obj is Type)
                {
                    addOptimizeCfg(obj as Type, flag);
                }
                else if (obj is IEnumerable<Type>)
                {
                    foreach(var type in (obj as IEnumerable<Type>))
                    {
                        addOptimizeCfg(type, flag);
                    }
                }
            }
        }

        // A type may be listed under GCOptimize more than once (for example in
        // a project list and in XLua.SysGenConfig); only conflicting flags fail.
        static void addOptimizeCfg(Type type, OptimizeFlag flag)
        {
            OptimizeFlag existing;
            if (!OptimizeCfg.TryGetValue(type, out existing))
            {
                OptimizeCfg.Add(type, flag);
            }
            else if (existing != flag)
            {
                throw new InvalidOperationException("GCOptimize lists " + type + " with both " + existing + " and " + flag);
            }
        }

        static bool isDefined(MemberInfo test, Type type)
        {
#if XLUA_GENERAL
            return test.GetCustomAttributes(false).Any(ca => ca.GetType().ToString() == type.ToString());
#else
            return test.IsDefined(type, false);
#endif
        }

        static object GetCustomAttribute(MemberInfo test, Type type)
        {
#if XLUA_GENERAL
            return test.GetCustomAttributes(false).FirstOrDefault(ca => ca.GetType().ToString() == type.ToString());
#else
            return test.GetCustomAttributes(type, false).FirstOrDefault();
#endif
        }

        static void MergeCfg(MemberInfo test, Type cfg_type, Func<object> get_cfg)
        {
            if (isDefined(test, typeof(LuaCallCSharpAttribute)))
            {
                object ccla = GetCustomAttribute(test, typeof(LuaCallCSharpAttribute));
                AddToList(LuaCallCSharp, get_cfg, ccla);
#if !XLUA_GENERAL
#pragma warning disable 618
                if (ccla != null && (((ccla as LuaCallCSharpAttribute).Flag & GenFlag.GCOptimize) != 0))
#pragma warning restore 618
                {
                    AddToList(GCOptimizeList, get_cfg, ccla);
                }
#endif
            }
            if (isDefined(test, typeof(CSharpCallLuaAttribute)))
            {
                AddToList(CSharpCallLua, get_cfg, GetCustomAttribute(test, typeof(CSharpCallLuaAttribute)));
            }
            if (isDefined(test, typeof(GCOptimizeAttribute)))
            {
                AddToList(GCOptimizeList, get_cfg, GetCustomAttribute(test, typeof(GCOptimizeAttribute)));
            }
            if (isDefined(test, typeof(ReflectionUseAttribute)))
            {
                AddToList(ReflectionUse, get_cfg, GetCustomAttribute(test, typeof(ReflectionUseAttribute)));
            }
            if (isDefined(test, typeof(BlackListAttribute))
                        && (typeof(List<List<string>>)).IsAssignableFrom(cfg_type))
            {
                BlackList.AddRange(get_cfg() as List<List<string>>);
            }
            if (isDefined(test, typeof(BlackListAttribute)) && typeof(Func<MemberInfo, bool>).IsAssignableFrom(cfg_type))
            {
                memberFilters.Add(get_cfg() as Func<MemberInfo, bool>);
            }

            if (isDefined(test, typeof(AdditionalPropertiesAttribute))
                        && (typeof(Dictionary<Type, List<string>>)).IsAssignableFrom(cfg_type))
            {
                var cfg = get_cfg() as Dictionary<Type, List<string>>;
                foreach (var kv in cfg)
                {
                    if (!AdditionalProperties.ContainsKey(kv.Key))
                    {
                        AdditionalProperties.Add(kv.Key, kv.Value);
                    }
                }
            }

            if (isDefined(test, typeof(DoNotGenAttribute))
                        && (typeof(Dictionary<Type, List<string>>)).IsAssignableFrom(cfg_type))
            {
                var cfg = get_cfg() as Dictionary<Type, List<string>>;
                foreach (var kv in cfg)
                {
                    HashSet<string> set;
                    if (!DoNotGen.TryGetValue(kv.Key, out set))
                    {
                        set = new HashSet<string>();
                        DoNotGen.Add(kv.Key, set);
                    }
                    set.UnionWith(kv.Value);
                }
            }
        }

        static bool IsPublic(Type type)
        {
            if (type.IsPublic || type.IsNestedPublic)
            {
                if (type.DeclaringType != null)
                {
                    return IsPublic(type.DeclaringType);
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        // A type that generated code cannot name: a compiler-generated type
        // (an anonymous type or a closure class; its name has '<') or a
        // private or protected nested type, also as a generic argument, array
        // element or declaring type. Computed lists such as ExampleConfig's
        // AllDelegate pick them up, e.g. Func<Type, MethodInfo,
        // <>f__AnonymousType1<Type, MethodInfo>> from a LINQ query, and
        // DelegatesGensBridge did not compile (Tencent/xLua#731).
        internal static bool IsUnnameable(Type type)
        {
            if (type.HasElementType) return IsUnnameable(type.GetElementType());
            if (type.IsGenericParameter) return false;
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (t.Name.Contains("<")) return true;
                if (t.IsNested && (t.IsNestedPrivate || t.IsNestedFamily || t.IsNestedFamANDAssem)) return true;
            }
            return type.IsGenericType && type.GetGenericArguments().Any(IsUnnameable);
        }

        public static void GetGenConfig(IEnumerable<Type> check_types)
        {
            LuaCallCSharp = new List<Type>();

            CSharpCallLua = new List<Type>();

            GCOptimizeList = new List<Type>();

            AdditionalProperties = new Dictionary<Type, List<string>>();

            ReflectionUse = new List<Type>();

            BlackList = new List<List<string>>()
            {
            };

            OptimizeCfg = new Dictionary<Type, OptimizeFlag>();

            DoNotGen = new Dictionary<Type, HashSet<string>>();

            memberFilters = new List<Func<MemberInfo, bool>>();

            foreach (var t in check_types)
            {
                MergeCfg(t, null, () => t);

                if (!t.IsAbstract || !t.IsSealed) continue;

                var fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    MergeCfg(field, field.FieldType, () => field.GetValue(null));
                }

                var props = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < props.Length; i++)
                {
                    var prop = props[i];
                    MergeCfg(prop, prop.PropertyType, () => prop.GetValue(null, null));
                }
            }
            LuaCallCSharp = LuaCallCSharp.Distinct()
                .Where(type=> IsPublic(type) && !isObsolete(type) && !type.IsGenericTypeDefinition)
                .Where(type => !typeof(Delegate).IsAssignableFrom(type))
                .Where(type => !IsUnnameable(type))
                .ToList();
            // Array types get no wrapper: every array uses the common array
            // metatable (ObjectTranslator.CreateArrayMetatable). The source
            // generator leaves them out (XLSG004); the migration tool reports them.
            LuaCallCSharp = LuaCallCSharp.Where(type => !type.IsArray).ToList();
            CSharpCallLua = CSharpCallLua.Distinct()
                .Where(type => IsPublic(type) && !isObsolete(type) && !type.IsGenericTypeDefinition)
                .Where(type => type != typeof(Delegate) && type != typeof(MulticastDelegate))
                .Where(type => !IsUnnameable(type))
                .Where(type => !isRefReadOnlyUnusable(type))
                .ToList();
            GCOptimizeList = GCOptimizeList.Distinct()
                .Where(type => IsPublic(type) && !isObsolete(type) && !type.IsGenericTypeDefinition)
                .Where(type => !IsUnnameable(type))
                .ToList();
            ReflectionUse = ReflectionUse.Distinct()
                .Where(type => !isObsolete(type) && !type.IsGenericTypeDefinition)
                .ToList();
        }

        private static bool isSupportedGenericMethod(MethodInfo method)
        {

            if (!method.ContainsGenericParameters)
                return true;
            var methodParameters = method.GetParameters();
            var _hasGenericParameter = false;
            for (var i = 0; i < methodParameters.Length; i++)
            {
                var parameterType = methodParameters[i].ParameterType;
                if (!isSupportGenericParameter(parameterType, true, ref _hasGenericParameter))
                    return false;
            }
            return _hasGenericParameter;
        }
        private static bool isSupportGenericParameter(Type parameterType,bool checkConstraint, ref bool _hasGenericParameter)
        {

            if (parameterType.IsGenericParameter)
            {
                if (!checkConstraint)
                    return false;
                var parameterConstraints = parameterType.GetGenericParameterConstraints();
                if (parameterConstraints.Length == 0) return false;
                foreach (var parameterConstraint in parameterConstraints)
                {
                    if (!parameterConstraint.IsClass || (parameterConstraint == typeof(ValueType)) || Generator.hasGenericParameter(parameterConstraint))
                        return false;
                }
                _hasGenericParameter = true;
            }
            else if(parameterType.IsGenericType)
            {
                foreach (var argument in parameterType.GetGenericArguments())
                {
                    if (!isSupportGenericParameter(argument,false, ref _hasGenericParameter))
                        return false;
                }
            }
            return true;
        }
    }
}
