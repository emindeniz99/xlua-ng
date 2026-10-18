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

    public struct CustomGenTask
    {
        public LuaTable Data;
        public TextWriter Output;
    }

    public struct UserConfig
    {
        public IEnumerable<Type> LuaCallCSharp;
        public IEnumerable<Type> CSharpCallLua;
        public IEnumerable<Type> ReflectionUse;
    }

    public class GenCodeMenuAttribute : Attribute
    {

    }

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

    public struct XLuaTemplate
    {
        public string name;
        public string text;
    }

    public struct XLuaTemplates
    {
        public XLuaTemplate LuaClassWrap;
        public XLuaTemplate LuaDelegateBridge;
        public XLuaTemplate LuaDelegateWrap;
        public XLuaTemplate LuaEnumWrap;
        public XLuaTemplate LuaInterfaceBridge;
        public XLuaTemplate LuaRegister;
        public XLuaTemplate LuaWrapPusher;
        public XLuaTemplate PackUnpack;
        public XLuaTemplate TemplateCommon;
    }

    public static partial class Generator
    {
        static LuaEnv luaenv = new LuaEnv();
        static List<string> OpMethodNames = new List<string>() { "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Equality", "op_UnaryNegation", "op_LessThan", "op_LessThanOrEqual", "op_Modulus",
            "op_BitwiseAnd", "op_BitwiseOr", "op_ExclusiveOr", "op_OnesComplement", "op_LeftShift", "op_RightShift"};
        private static XLuaTemplates templateRef;

        static Generator()
        {
#if !XLUA_GENERAL
            TemplateRef template_ref = ScriptableObject.CreateInstance<TemplateRef>();

            templateRef = new XLuaTemplates()
            {
#if GEN_CODE_MINIMIZE
                LuaClassWrap = { name = template_ref.LuaClassWrapGCM.name, text = template_ref.LuaClassWrapGCM.text },
#else
                LuaClassWrap = { name = template_ref.LuaClassWrap.name, text = template_ref.LuaClassWrap.text },
#endif
                LuaDelegateBridge = { name = template_ref.LuaDelegateBridge.name, text = template_ref.LuaDelegateBridge.text },
                LuaDelegateWrap = { name = template_ref.LuaDelegateWrap.name, text = template_ref.LuaDelegateWrap.text },
#if GEN_CODE_MINIMIZE
                LuaEnumWrap = { name = template_ref.LuaEnumWrapGCM.name, text = template_ref.LuaEnumWrapGCM.text },
#else
                LuaEnumWrap = { name = template_ref.LuaEnumWrap.name, text = template_ref.LuaEnumWrap.text },
#endif
                LuaInterfaceBridge = { name = template_ref.LuaInterfaceBridge.name, text = template_ref.LuaInterfaceBridge.text },
#if GEN_CODE_MINIMIZE
                LuaRegister = { name = template_ref.LuaRegisterGCM.name, text = template_ref.LuaRegisterGCM.text },
#else
                LuaRegister = { name = template_ref.LuaRegister.name, text = template_ref.LuaRegister.text },
#endif
                LuaWrapPusher = { name = template_ref.LuaWrapPusher.name, text = template_ref.LuaWrapPusher.text },
                PackUnpack = { name = template_ref.PackUnpack.name, text = template_ref.PackUnpack.text },
                TemplateCommon = { name = template_ref.TemplateCommon.name, text = template_ref.TemplateCommon.text },
            };
#endif
            luaenv.AddLoader((ref string filepath) =>
            {
                if (filepath == "TemplateCommon")
                {
                    return Encoding.UTF8.GetBytes(templateRef.TemplateCommon.text);
                }
                else
                {
                    return null;
                }
            });
        }

        static bool IsOverride(MethodBase method)
        {
            var m = method as MethodInfo;
            return m != null && !m.IsConstructor && m.IsVirtual && (m.GetBaseDefinition().DeclaringType != m.DeclaringType);
        }

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
                else if (parameterType.IsGenericParameter)
                {
                    var parameterConstraints = parameterType.GetGenericParameterConstraints();
                    if (parameterConstraints.Length == 0) return false;
                    foreach (var parameterConstraint in parameterConstraints)
                    {
                        if (!parameterConstraint.IsClass || (parameterConstraint == typeof(ValueType)) || Generator.hasGenericParameter(parameterConstraint))
                            return false;
                    }
                    hasValidGenericParameter = true;
                }
            }
            return hasValidGenericParameter || !method.ContainsGenericParameters;
        }

        static bool IsDoNotGen(Type type, string name)
        {
            return DoNotGen.ContainsKey(type) && DoNotGen[type].Contains(name);
        }

        static void getClassInfo(Type type, LuaTable parameters)
        {
            parameters.Set("type", type);

            var constructors = new List<MethodBase>();
            var constructor_def_vals = new List<int>();
            if (!type.IsAbstract)
            {
                foreach (var con in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase).Cast<MethodBase>()
                    .Where(constructor => !isMethodInBlackList(constructor) && !isObsolete(constructor)))
                {
                    int def_count = 0;
                    constructors.Add(con);
                    constructor_def_vals.Add(def_count);

                    var ps = con.GetParameters();
                    for (int i = ps.Length - 1; i >= 0; i--)
                    {
                        if (ps[i].IsOptional ||
                            (ps[i].IsDefined(typeof(ParamArrayAttribute), false) && i > 0 && ps[i - 1].IsOptional))
                        {
                            def_count++;
                            constructors.Add(con);
                            constructor_def_vals.Add(def_count);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            parameters.Set("constructors", constructors);
            parameters.Set("constructor_def_vals", constructor_def_vals);

            List<string> extension_methods_namespace = new List<string>();
            var extension_methods = type.IsInterface ? new MethodInfo[0]:GetExtensionMethods(type).ToArray();
            foreach(var extension_method in extension_methods)
            {
                if (extension_method.DeclaringType.Namespace != null
                    && extension_method.DeclaringType.Namespace != "System.Collections.Generic"
                    && extension_method.DeclaringType.Namespace != "XLua")
                {
                    extension_methods_namespace.Add(extension_method.DeclaringType.Namespace);
                }
            }
            parameters.Set("namespaces", extension_methods_namespace.Distinct().ToList());

            List<LazyMemberInfo> lazyMemberInfos = new List<LazyMemberInfo>();

            //warnning: filter all method start with "op_"  "add_" "remove_" may  filter some ordinary method
            parameters.Set("methods", type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => !isDefined(method, typeof (ExtensionAttribute)) || method.GetParameters()[0].ParameterType.IsInterface || method.DeclaringType != type)
                .Where(method => !method.IsSpecialName 
                    || (
                         ((method.Name == "get_Item" && method.GetParameters().Length == 1) || (method.Name == "set_Item" && method.GetParameters().Length == 2)) 
                         && method.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(string))
                       )
                 ) 
                .Concat(extension_methods)
                .Where(method => !IsDoNotGen(type, method.Name))
                .Where(method => !isMethodInBlackList(method) && (!method.IsGenericMethod || extension_methods.Contains(method) || isSupportedGenericMethod(method)) && !isObsolete(method))
                .GroupBy(method => (method.Name + ((method.IsStatic && (!isDefined(method, typeof (ExtensionAttribute)) || method.GetParameters()[0].ParameterType.IsInterface)) ? "_xlua_st_" : "")), (k, v) =>
                {
                    var overloads = new List<MethodBase>();
                    List<int> def_vals = new List<int>();
                    bool isOverride = false;
                    foreach (var overload in v.Cast<MethodBase>().OrderBy(mb => OverloadCosting(mb)))
                    {
                        int def_count = 0;
                        overloads.Add(overload);
                        def_vals.Add(def_count);

                        if (!isOverride)
                        {
                            isOverride = IsOverride(overload);
                        }

                        var ps = overload.GetParameters();
                        for (int i = ps.Length - 1; i >=0; i--)
                        {
                            if(ps[i].IsOptional ||
                            (ps[i].IsDefined(typeof(ParamArrayAttribute), false) && i > 0 && ps[i - 1].IsOptional))
                            {
                                def_count++;
                                overloads.Add(overload);
                                def_vals.Add(def_count);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    return new {
                        Name = k,
                        IsStatic = overloads[0].IsStatic && (!isDefined(overloads[0], typeof(ExtensionAttribute)) || overloads[0].GetParameters()[0].ParameterType.IsInterface),
                        Overloads = overloads,
                        DefaultValues = def_vals
                    };
                }).ToList());

            parameters.Set("getters", type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                
                .Where(prop => prop.GetIndexParameters().Length == 0 && prop.CanRead && (prop.GetGetMethod() != null)  && prop.Name != "Item" && !isObsolete(prop) && !isObsolete(prop.GetGetMethod()) && !isMemberInBlackList(prop) && !isMemberInBlackList(prop.GetGetMethod())).Select(prop => new { prop.Name, IsStatic = prop.GetGetMethod().IsStatic, ReadOnly = false, Type = prop.PropertyType })
                .Concat(
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                    .Where(field => !isObsolete(field) && !isMemberInBlackList(field))
                    .Select(field => new { field.Name, field.IsStatic, ReadOnly = field.IsInitOnly || field.IsLiteral, Type = field.FieldType })
                ).Where(info => !IsDoNotGen(type, info.Name))/*.Where(getter => !typeof(Delegate).IsAssignableFrom(getter.Type))*/.ToList());

            parameters.Set("setters", type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(prop => prop.GetIndexParameters().Length == 0 && prop.CanWrite && (prop.GetSetMethod() != null) && prop.Name != "Item" && !isObsolete(prop) && !isObsolete(prop.GetSetMethod()) && !isMemberInBlackList(prop) && !isMemberInBlackList(prop.GetSetMethod())).Select(prop => new { prop.Name, IsStatic = prop.GetSetMethod().IsStatic, Type = prop.PropertyType, IsProperty = true })
                .Concat(
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                    .Where(field => !isObsolete(field) && !isMemberInBlackList(field) && !field.IsInitOnly && !field.IsLiteral)
                    .Select(field => new { field.Name, field.IsStatic, Type = field.FieldType, IsProperty = false })
                ).Where(info => !IsDoNotGen(type, info.Name))/*.Where(setter => !typeof(Delegate).IsAssignableFrom(setter.Type))*/.ToList());

            parameters.Set("operators", type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => OpMethodNames.Contains(method.Name))
                .GroupBy(method => method.Name, (k, v) => new { Name = k, Overloads = v.Cast<MethodBase>().OrderBy(mb => mb.GetParameters().Length).ToList() }).ToList());

            var indexers = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(prop => prop.GetIndexParameters().Length > 0);

            parameters.Set("indexers", indexers.Where(prop => prop.CanRead && (prop.GetGetMethod() != null)).Select(prop => prop.GetGetMethod())
                .Where(method => method.GetParameters().Length == 1 && !method.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
                .Where(method => !isRefReadOnlyUnusable(method))
                .ToList());

            parameters.Set("newindexers", indexers.Where(prop => prop.CanWrite && (prop.GetSetMethod() != null)).Select(prop => prop.GetSetMethod())
                .Where(method => method.GetParameters().Length == 2 && !method.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
                .Where(method => !isRefReadOnlyUnusable(method))
                .ToList());
            // How the class wrapper templates call indexers with a ref readonly key.
            parameters.Set("ref_readonly_indexer_by_accessor", refReadOnlyIndexerByAccessor);

            parameters.Set("events", type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly).Where(e => !isObsolete(e) && !isMemberInBlackList(e))
                .Where(ev=> ev.GetAddMethod() != null || ev.GetRemoveMethod() != null)
                .Where(ev => !IsDoNotGen(type, ev.Name))
                .Select(ev => new { IsStatic = ev.GetAddMethod() != null? ev.GetAddMethod().IsStatic: ev.GetRemoveMethod().IsStatic, ev.Name,
                    CanSet = false, CanAdd = ev.GetRemoveMethod() != null, CanRemove = ev.GetRemoveMethod() != null, Type = ev.EventHandlerType})
                .ToList());
            
            parameters.Set("lazymembers", lazyMemberInfos);
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(m => IsDoNotGen(type, m.Name))
                .GroupBy(m=>m.Name).Select(g => g.First())
                )
            {
                switch(member.MemberType)
                {
                    case MemberTypes.Method:
                        MethodBase mb = member as MethodBase;
                        lazyMemberInfos.Add(new LazyMemberInfo
                        {
                            Index = mb.IsStatic ? "CLS_IDX" : "METHOD_IDX",
                            Name = member.Name,
                            MemberType = "LazyMemberTypes.Method",
                            IsStatic = mb.IsStatic ? "true" : "false"
                        });
                        break;
                    case MemberTypes.Event:
                        EventInfo ev = member as EventInfo;
                        if (ev.GetAddMethod() == null && ev.GetRemoveMethod() == null) break;
                        bool eventIsStatic = ev.GetAddMethod() != null ? ev.GetAddMethod().IsStatic : ev.GetRemoveMethod().IsStatic;
                        lazyMemberInfos.Add(new LazyMemberInfo {
                            Index = eventIsStatic ? "CLS_IDX" : "METHOD_IDX",
                            Name = member.Name,
                            MemberType = "LazyMemberTypes.Event",
                            IsStatic = eventIsStatic ? "true" : "false"
                        });
                        break;
                    case MemberTypes.Field:
                        FieldInfo field = member as FieldInfo;
                        lazyMemberInfos.Add(new LazyMemberInfo
                        {
                            Index = field.IsStatic ? "CLS_GETTER_IDX" : "GETTER_IDX",
                            Name = member.Name,
                            MemberType = "LazyMemberTypes.FieldGet",
                            IsStatic = field.IsStatic ? "true" : "false"
                        });
                        lazyMemberInfos.Add(new LazyMemberInfo
                        {
                            Index = field.IsStatic ? "CLS_SETTER_IDX" : "SETTER_IDX",
                            Name = member.Name,
                            MemberType = "LazyMemberTypes.FieldSet",
                            IsStatic = field.IsStatic ? "true" : "false"
                        });
                        break;
                    case MemberTypes.Property:
                        PropertyInfo prop = member as PropertyInfo;
                        if (prop.Name != "Item" || prop.GetIndexParameters().Length == 0)
                        {
                            if (prop.CanRead && prop.GetGetMethod() != null)
                            {
                                var isStatic = prop.GetGetMethod().IsStatic;
                                lazyMemberInfos.Add(new LazyMemberInfo
                                {
                                    Index = isStatic ? "CLS_GETTER_IDX" : "GETTER_IDX",
                                    Name = member.Name,
                                    MemberType = "LazyMemberTypes.PropertyGet",
                                    IsStatic = isStatic ? "true" : "false"
                                });
                            }
                            if (prop.CanWrite && prop.GetSetMethod() != null)
                            {
                                var isStatic = prop.GetSetMethod().IsStatic;
                                lazyMemberInfos.Add(new LazyMemberInfo
                                {
                                    Index = isStatic ? "CLS_SETTER_IDX" : "SETTER_IDX",
                                    Name = member.Name,
                                    MemberType = "LazyMemberTypes.PropertySet",
                                    IsStatic = isStatic ? "true" : "false"
                                });
                            }
                        }
                        break;
                }
            }

            // Extension methods named in [DoNotGen] are left out of the wrapper
            // above. The emitted and reflection wrappers list them with the
            // type's own methods of that name, so the lazy instance member
            // wraps them too: Utils.LazyReflectionCall finds them in the
            // extension method map (GenLuaRegister).
            foreach (var name in extension_methods
                .Where(method => IsDoNotGen(type, method.Name) && !isMethodInBlackList(method) && !isObsolete(method))
                .Select(method => method.Name).Distinct())
            {
                if (lazyMemberInfos.Any(info => info.Name == name && info.Index == "METHOD_IDX")) continue;
                lazyMemberInfos.Add(new LazyMemberInfo
                {
                    Index = "METHOD_IDX",
                    Name = name,
                    MemberType = "LazyMemberTypes.Method",
                    IsStatic = "false"
                });
            }
        }

        class LazyMemberInfo
        {
            public string Index;
            public string Name;
            public string MemberType;
            public string IsStatic;
        }

        static void getInterfaceInfo(Type type, LuaTable parameters)
        {
            parameters.Set("type", type);

            var itfs = new Type[] { type }.Concat(type.GetInterfaces());
            parameters.Set("methods", itfs.SelectMany(i => i.GetMethods())
                .Where(method => !method.IsSpecialName && !method.IsGenericMethod && !method.Name.StartsWith("op_") && !method.Name.StartsWith("add_") && !method.Name.StartsWith("remove_")) //GenericMethod can not be invoke becuase not static info available!
                    .ToList());

            parameters.Set("propertys", itfs.SelectMany(i => i.GetProperties())
                .Where(prop => (prop.CanRead || prop.CanWrite) && prop.Name != "Item")
                    .ToList());

            parameters.Set("events", itfs.SelectMany(i => i.GetEvents()).ToList());

            parameters.Set("indexers", itfs.SelectMany(i => i.GetProperties())
                .Where(prop => (prop.CanRead || prop.CanWrite) && prop.Name == "Item")
                    .ToList());
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
        // Indexers with `in` keys take a plain argument everywhere.
#if UNITY_EDITOR && !UNITY_6000_4_OR_NEWER
        static readonly bool refReadOnlyIndexerByAccessor = true;
        static readonly bool refReadOnlyIndexerUnknown = false;
#elif UNITY_EDITOR && !UNITY_6000_6_OR_NEWER
        static readonly bool refReadOnlyIndexerByAccessor = false;
        static readonly bool refReadOnlyIndexerUnknown = true;
#else
        static readonly bool refReadOnlyIndexerByAccessor = false;
        static readonly bool refReadOnlyIndexerUnknown = false;
#endif

        static bool isRefReadOnlyIndexerAccessor(MethodBase method)
        {
            if (!method.IsSpecialName || method.GetParameters().Length == 0 || !XLua.Utils.IsRefReadOnlyParameter(method.GetParameters()[0])) return false;
            return method.DeclaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(p => p.GetIndexParameters().Length > 0 && (p.GetGetMethod(true) == method || p.GetSetMethod(true) == method));
        }

        // Members already reported by isRefReadOnlyUnusable. GenAll and
        // GenLuaCATS clear it (not GetGenConfig, which custom generators such
        // as LinkXmlGen call again), so each generation warns once per member.
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
                string what = owner == null
                    ? method.DeclaringType.FullName + "." + method.Name
                    : "[CSharpCallLua] " + owner.FullName + " (its " + method.DeclaringType.FullName + "." + method.Name + ")";
#if UNITY_EDITOR
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

        static Dictionary<string, LuaFunction> templateCache = new Dictionary<string, LuaFunction>();
        static void GenOne(Type type, Action<Type, LuaTable> type_info_getter, XLuaTemplate templateAsset, StreamWriter textWriter)
        {
            if (isObsolete(type)) return;
            LuaFunction template;
            if (!templateCache.TryGetValue(templateAsset.name, out template))
            {
                template = XLua.TemplateEngine.LuaTemplate.Compile(luaenv, templateAsset.text);
                templateCache[templateAsset.name] = template;
            }

            LuaTable type_info = luaenv.NewTable();
            LuaTable meta = luaenv.NewTable();
            meta.Set("__index", luaenv.Global);
            type_info.SetMetaTable(meta);
            meta.Dispose();

            type_info_getter(type, type_info);

            try
            {
                string genCode = XLua.TemplateEngine.LuaTemplate.Execute(template, type_info);
                //string filePath = save_path + type.ToString().Replace("+", "").Replace(".", "").Replace("`", "").Replace("&", "").Replace("[", "").Replace("]", "").Replace(",", "") + file_suffix + ".cs";
                textWriter.Write(genCode);
                textWriter.Flush();
            }
            catch (Exception e)
            {
#if XLUA_GENERAL
                System.Console.WriteLine("Error: gen wrap file fail! err=" + e.Message + ", stack=" + e.StackTrace);
#else
                Debug.LogError("gen wrap file fail! err=" + e.Message + ", stack=" + e.StackTrace);
#endif
            }
            finally
            {
                type_info.Dispose();
            }
        }

        static void GenEnumWrap(IEnumerable<Type> types, string save_path)
        {
            string filePath = save_path + "EnumWrap.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            
            GenOne(null, (type, type_info) =>
            {
                var type2fields = luaenv.NewTable();
                foreach(var _type in types)
                    type2fields.Set(_type, _type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => !isMemberInBlackList(x)).ToArray());
                type_info.Set("type2fields", type2fields);
                type_info.Set("types", types.ToList());
            }, templateRef.LuaEnumWrap, textWriter);

            textWriter.Close();
        }

        static string NonmalizeName(string name)
        {
            return name.Replace("+", "_").Replace(".", "_").Replace("`", "_").Replace("&", "_").Replace("[", "_").Replace("]", "_").Replace(",", "_");
        }

        static void GenInterfaceBridge(IEnumerable<Type> types, string save_path)
        {
            foreach (var wrap_type in types)
            {
                if (!wrap_type.IsInterface) continue;

                string filePath = save_path + NonmalizeName(wrap_type.ToString()) + "Bridge.cs";
                StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
                GenOne(wrap_type, (type, type_info) =>
                {
                    getInterfaceInfo(type, type_info);
                }, templateRef.LuaInterfaceBridge, textWriter);
                textWriter.Close();
            }
        }

        class ParameterInfoSimulation
        {
            public string Name;
            public bool IsOut;
            public bool IsIn;
            public Type ParameterType;
            public bool IsParamArray;
            // XLua.Utils.IsInParameter of the original parameter; read by
            // TemplateCommon's IsInParam.
            public bool IsInParam;
            // XLua.Utils.IsRefReadOnlyParameter, for delegate parameters (a
            // hotfix bridge is called from IL, so its ref kind never
            // matters); read by TemplateCommon's IsRefReadOnlyParam.
            public bool IsRefReadOnlyParam;
        }

        class MethodInfoSimulation
        {
            public Type ReturnType;
            public ParameterInfoSimulation[] ParameterInfos;

            public int HashCode;

            public ParameterInfoSimulation[]  GetParameters()
            {
                return ParameterInfos;
            }

            public Type DeclaringType = null;
            public string DeclaringTypeName = null;
        }

        static MethodInfoSimulation makeMethodInfoSimulation(MethodInfo method)
        {
            int hashCode = method.ReturnType.GetHashCode();

            List<ParameterInfoSimulation> paramsExpect = new List<ParameterInfoSimulation>();

            foreach (var param in method.GetParameters())
            {
                if (param.IsOut)
                {
                    hashCode++;
                }
                hashCode += param.ParameterType.GetHashCode();
                paramsExpect.Add(new ParameterInfoSimulation()
                {
                    Name = param.Name,
                    IsOut = param.IsOut,
                    IsIn = param.IsIn,
                    ParameterType = param.ParameterType,
                    IsParamArray = param.IsDefined(typeof(System.ParamArrayAttribute), false),
                    IsInParam = XLua.Utils.IsInParameter(param),
                    IsRefReadOnlyParam = XLua.Utils.IsRefReadOnlyParameter(param)
                });
            }

            return new MethodInfoSimulation()
            {
                ReturnType = method.ReturnType,
                HashCode = hashCode,
                ParameterInfos = paramsExpect.ToArray(),
                DeclaringType = method.DeclaringType
            };
        }

        static bool isNotPublic(Type type)
        {
            if (type.IsByRef || type.IsArray)
            {
                return isNotPublic(type.GetElementType());
            }
            else
            {
                if ((!type.IsNested && !type.IsPublic) || (type.IsNested && !type.IsNestedPublic))
                {
                    return true;
                }
                if (type.IsGenericType)
                {
                    foreach (var ga in type.GetGenericArguments())
                    {
                        if (isNotPublic(ga))
                        {
                            return true;
                        }
                    }
                }
                if (type.IsNested)
                {
                    var parent = type.DeclaringType;
                    while (parent != null)
                    {
                        if ((!parent.IsNested && !parent.IsPublic) || (parent.IsNested && !parent.IsNestedPublic))
                        {
                            return true;
                        }
                        if (parent.IsNested)
                        {
                            parent = parent.DeclaringType;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                return false;
            }
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

        static MethodInfoSimulation makeHotfixMethodInfoSimulation(MethodBase hotfixMethod, HotfixFlag hotfixType)
        {
            bool ignoreValueType = hotfixType.HasFlag(HotfixFlag.ValueTypeBoxing);
            //ignoreValueType = true;

            Type retTypeExpect = (hotfixMethod.IsConstructor ? typeof(void) : (hotfixMethod as MethodInfo).ReturnType);
            int hashCode = retTypeExpect.GetHashCode();
            List<ParameterInfoSimulation> paramsExpect = new List<ParameterInfoSimulation>();
            if (!hotfixMethod.IsStatic) // add self
            {
                paramsExpect.Add(new ParameterInfoSimulation()
                {
                    Name = "self",
                    IsOut = false,
                    IsIn = true,
                    ParameterType = (hotfixMethod.DeclaringType.IsValueType && !ignoreValueType) ? hotfixMethod.DeclaringType : typeof(object),
                    IsParamArray = false
                });
                hashCode += paramsExpect[0].ParameterType.GetHashCode();
            }

            foreach (var param in hotfixMethod.GetParameters())
            {
                var paramExpect = new ParameterInfoSimulation()
                {
                    Name = param.Name,
                    IsOut = param.IsOut,
                    IsIn = param.IsIn,
                    ParameterType = (param.ParameterType.IsByRef || (param.ParameterType.IsValueType && !ignoreValueType)
                      || param.IsDefined(typeof(System.ParamArrayAttribute), false)) ? param.ParameterType : typeof(object),
                    IsParamArray = param.IsDefined(typeof(System.ParamArrayAttribute), false),
                    IsInParam = XLua.Utils.IsInParameter(param)
                };
                if (param.IsOut)
                {
                    hashCode++;
                }
                hashCode += paramExpect.ParameterType.GetHashCode();
                paramsExpect.Add(paramExpect);
            }

            return new MethodInfoSimulation()
            {
                HashCode = hashCode,
                ReturnType = retTypeExpect,
                ParameterInfos = paramsExpect.ToArray()
            };
        }

        class MethodInfoSimulationComparer : IEqualityComparer<MethodInfoSimulation>
        {
            public bool Equals(MethodInfoSimulation x, MethodInfoSimulation y)
            {
                if (object.ReferenceEquals(x, y)) return true;
                if (x == null || y == null)
                {
                    return false;
                }
                if (x.ReturnType != y.ReturnType)
                {
                    return false;
                }
                var xParams = x.GetParameters();
                var yParams = y.GetParameters();
                if (xParams.Length != yParams.Length)
                {
                    return false;
                }

                for (int i = 0; i < xParams.Length; i++)
                {
                    if (xParams[i].ParameterType != yParams[i].ParameterType || xParams[i].IsOut != yParams[i].IsOut)
                    {
                        return false;
                    }
                    // `in` and `ref` bridges differ in write-back and in their C# modifier.
                    if (xParams[i].IsInParam != yParams[i].IsInParam)
                    {
                        return false;
                    }
                }

                var lastPos = xParams.Length - 1;
                return lastPos < 0 || xParams[lastPos].IsParamArray == yParams[lastPos].IsParamArray;
            }
            public int GetHashCode(MethodInfoSimulation obj)
            {
                return obj.HashCode;
            }
        }

        static bool injectByGeneric(MethodBase method, HotfixFlag hotfixType)
        {
            bool ignoreValueType = hotfixType.HasFlag(HotfixFlag.ValueTypeBoxing);
            //ignoreValueType = true;

            if (!method.IsConstructor && (isNotPublic((method as MethodInfo).ReturnType) || hasGenericParameter((method as MethodInfo).ReturnType))) return true;

            if (!method.IsStatic 
                &&(((method.DeclaringType.IsValueType && !ignoreValueType) && isNotPublic(method.DeclaringType)) || hasGenericParameter(method.DeclaringType)))
            {
                return true;
            }

            foreach (var param in method.GetParameters())
            {
                if ((((param.ParameterType.IsValueType && !ignoreValueType) 
                    || param.ParameterType.IsByRef || param.IsDefined(typeof(System.ParamArrayAttribute), false)) && isNotPublic(param.ParameterType)) 
                    || hasGenericParameter(param.ParameterType))
                    return true;
            }
            return false;
        }

        static bool HasFlag(this HotfixFlag toCheck, HotfixFlag flag)
        {
            return (toCheck != HotfixFlag.Stateless) && ((toCheck & flag) == flag);
        }

        // The bridge groups of the last GenDelegateBridge (delegates and hotfix
        // signatures that share one bridge), for BindingSurface.
        static List<List<MethodInfoSimulation>> delegateGroups = new List<List<MethodInfoSimulation>>();

        static void GenDelegateBridge(IEnumerable<Type> types, string save_path, IEnumerable<Type> hotfix_check_types)
        {
            string filePath = save_path + "DelegatesGensBridge.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            types = types.Where(type => !type.GetMethod("Invoke").GetParameters().Any(paramInfo => paramInfo.ParameterType.IsGenericParameter));
            var hotfxDelegates = new List<MethodInfoSimulation>();
            var comparer = new MethodInfoSimulationComparer();

            var bindingAttrOfMethod = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.NonPublic;
            var bindingAttrOfConstructor = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.NonPublic;
            foreach (var type in (from type in hotfix_check_types where isDefined(type, typeof(HotfixAttribute)) select type))
            {
                var ca = GetCustomAttribute(type, typeof(HotfixAttribute));
#if XLUA_GENERAL
                var hotfixType = (HotfixFlag)Convert.ToInt32(ca.GetType().GetProperty("Flag").GetValue(ca, null));
#else
                var hotfixType = (ca as HotfixAttribute).Flag;
#endif
                HotfixCfg[type] = hotfixType;
            }
            foreach (var kv in HotfixCfg)
            {
                if (kv.Key.Name.Contains("<") || kv.Value.HasFlag(HotfixFlag.Inline))
                {
                    continue;
                }
                bool ignoreProperty = kv.Value.HasFlag(HotfixFlag.IgnoreProperty);
                bool ignoreNotPublic = kv.Value.HasFlag(HotfixFlag.IgnoreNotPublic);
                bool ignoreCompilerGenerated = kv.Value.HasFlag(HotfixFlag.IgnoreCompilerGenerated);
                if (ignoreCompilerGenerated && isDefined(kv.Key, typeof(CompilerGeneratedAttribute)))
                {
                    continue;
                }
                //ignoreProperty = true;
                hotfxDelegates.AddRange(kv.Key.GetMethods(bindingAttrOfMethod)
                    .Where(method => method.GetMethodBody() != null)
                    .Where(method => !method.Name.Contains("<"))
                    .Where(method => !ignoreCompilerGenerated || !isDefined(method, typeof(CompilerGeneratedAttribute)))
                    .Where(method => !ignoreNotPublic || method.IsPublic)
                    .Where(method => !ignoreProperty || !method.IsSpecialName || (!method.Name.StartsWith("get_") && !method.Name.StartsWith("set_")))
                    .Where(method => !method.GetParameters().Any(pInfo => isUnsupportedType(pInfo.ParameterType)))
                    .Where(method => !isUnsupportedType(method.ReturnType))
                    .Cast<MethodBase>()
                    .Concat(kv.Key.GetConstructors(bindingAttrOfConstructor).Cast<MethodBase>())
                    .Where(method => !injectByGeneric(method, kv.Value))
                    .Select(method => makeHotfixMethodInfoSimulation(method, kv.Value)));
            }
            hotfxDelegates = hotfxDelegates.Distinct(comparer).ToList();
            for(int i = 0; i < hotfxDelegates.Count; i++)
            {
                hotfxDelegates[i].DeclaringTypeName = "__Gen_Hotfix_Delegate" + i;
            }

            var delegates_groups = types.Select(delegate_type => makeMethodInfoSimulation(delegate_type.GetMethod("Invoke")))
                .Where(d => d.DeclaringType.FullName != null)
                .Concat(hotfxDelegates)
                .GroupBy(d => d, comparer).Select((group) => new { Key = group.Key, Value = group.ToList()});
            delegateGroups = delegates_groups.Select(g => g.Value).ToList();
            GenOne(typeof(DelegateBridge), (type, type_info) =>
            {
                type_info.Set("delegates_groups", delegates_groups.ToList());
            }, templateRef.LuaDelegateBridge, textWriter);
            textWriter.Close();
        }

        static void GenWrapPusher(IEnumerable<Type> types, string save_path)
        {
            string filePath = save_path + "WrapPusher.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            var emptyMap = new Dictionary<Type, Type>();
            GenOne(typeof(ObjectTranslator), (type, type_info) =>
            {
                type_info.Set("purevaluetypes", types
                     .Where(t => t.IsEnum || (!t.IsPrimitive && SizeOf(t) != -1))
                     .Select(t => new {
                         Type = t,
                         Size = SizeOf(t),
                         Flag = t.IsEnum ? OptimizeFlag.Default : OptimizeCfg[t],
                         FieldInfos = (t.IsEnum || OptimizeCfg[t] == OptimizeFlag.Default) ? null : getXluaTypeInfo(t, emptyMap).FieldInfos
                     }).ToList());
                type_info.Set("tableoptimzetypes", types.Where(t => !t.IsEnum && SizeOf(t) == -1)
                     .Select(t => new { Type = t, Fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) })
                     .ToList());
            }, templateRef.LuaWrapPusher, textWriter);
            textWriter.Close();
        }

        static void GenWrap(IEnumerable<Type> types, string save_path)
        {
            types = types.Where(type=>!type.IsEnum);

#if GENERIC_SHARING
            types = types.GroupBy(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t).Select(g => g.Key);
#endif

            var typeMap = types.ToDictionary(type => {
                //Debug.Log("type:" + type);
                return type.ToString();
            });

            foreach (var wrap_type in types)
            {
                string filePath = save_path + NonmalizeName(wrap_type.ToString()) + "Wrap.cs";
                StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
                if (wrap_type.IsEnum)
                {
                    GenOne(wrap_type, (type, type_info) =>
                    {
                        type_info.Set("type", type);
                        type_info.Set("fields", type.GetFields(BindingFlags.GetField | BindingFlags.Public | BindingFlags.Static)
                            .Where(field => !isObsolete(field))
                            .ToList());
                    }, templateRef.LuaEnumWrap, textWriter);
                }
                else if (typeof(Delegate).IsAssignableFrom(wrap_type))
                {


                    GenOne(wrap_type, (type, type_info) =>
                    {
                        type_info.Set("type", type);
                        type_info.Set("delegate", type.GetMethod("Invoke"));
                    }, templateRef.LuaDelegateWrap, textWriter);

                }
                else
                {
                    GenOne(wrap_type, (type, type_info) =>
                    {
                        if (type.BaseType != null && typeMap.ContainsKey(type.BaseType.ToString()))
                        {
                            type_info.Set("base", type.BaseType);
                        }
                        getClassInfo(type, type_info);
                    }, templateRef.LuaClassWrap, textWriter);
                }
                textWriter.Close();
            }
        }

#if !XLUA_GENERAL
        static void clear(string path)
        {
            if (Directory.Exists(path))
            {
                // DeleteAsset also removes the folder's .meta file; it needs a
                // project-relative "Assets/..." path. Folders outside Assets are
                // deleted directly.
                string full = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
                string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
                bool inAssets = full == assets || full.StartsWith(assets + "/", StringComparison.Ordinal);
                if (!inAssets || !AssetDatabase.DeleteAsset("Assets" + full.Substring(assets.Length)))
                {
                    Directory.Delete(path, true);
                }

                AssetDatabase.Refresh();
            }
        }
#endif

        class DelegateByMethodDecComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                return XLua.Utils.IsParamsMatch(x.GetMethod("Invoke"), y.GetMethod("Invoke"));
            }
            public int GetHashCode(Type obj)
            {
                int hc = 0;
                var method = obj.GetMethod("Invoke");
                hc += method.ReturnType.GetHashCode();
                foreach (var pi in method.GetParameters())
                {
                    hc += pi.ParameterType.GetHashCode();
                }
                return hc;
            }
        }

        public static void GenDelegateBridges(IEnumerable<Type> hotfix_check_types)
        {
            var delegate_types = CSharpCallLua.Where(type => typeof(Delegate).IsAssignableFrom(type));

            GenDelegateBridge(delegate_types, GeneratorConfig.common_path, hotfix_check_types);
        }

        public static void GenEnumWraps()
        {
            var enum_types = LuaCallCSharp.Where(type => type.IsEnum).Distinct();

            GenEnumWrap(enum_types, GeneratorConfig.common_path);
        }

        static MethodInfo makeGenericMethodIfNeeded(MethodInfo method)
        {
            if (!method.ContainsGenericParameters) return method;

            var genericArguments = method.GetGenericArguments();
            var constraintedArgumentTypes = new Type[genericArguments.Length];
            for (var i = 0; i < genericArguments.Length; i++)
            {
                var argumentType = genericArguments[i];
                var parameterConstraints = argumentType.GetGenericParameterConstraints();
                Type parameterConstraint = parameterConstraints[0];
                foreach(var type in argumentType.GetGenericParameterConstraints())
                {
                    if (parameterConstraint.IsAssignableFrom(type))
                    {
                        parameterConstraint = type;
                    }
                }
                
                constraintedArgumentTypes[i] = parameterConstraint;
            }
            return method.MakeGenericMethod(constraintedArgumentTypes);
        }

        public static void GenLuaRegister(bool minimum = false)
        {
            var wraps = minimum ? new List<Type>() : LuaCallCSharp;

            var itf_bridges = CSharpCallLua.Where(t => t.IsInterface);

            string filePath = GeneratorConfig.common_path + "XLuaGenAutoRegister.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);

            var extension_methods = registerExtensionMethods();
            GenOne(typeof(DelegateBridgeBase), (type, type_info) =>
            {
#if GENERIC_SHARING
                type_info.Set("wraps", wraps.Where(t=>!t.IsGenericType).ToList());
                var genericTypeGroups = wraps.Where(t => t.IsGenericType).GroupBy(t => t.GetGenericTypeDefinition());

                var typeToArgsList = luaenv.NewTable();
                foreach (var genericTypeGroup in genericTypeGroups)
                {
                    var argsList = luaenv.NewTable();
                    int i = 1;
                    foreach(var genericType in genericTypeGroup)
                    {
                        argsList.Set(i++, genericType.GetGenericArguments());
                    }
                    typeToArgsList.Set(genericTypeGroup.Key, argsList);
                    argsList.Dispose();
                }

                type_info.Set("generic_wraps", typeToArgsList);
                typeToArgsList.Dispose();
#else
                type_info.Set("wraps", wraps.ToList());
#endif

                type_info.Set("itf_bridges", itf_bridges.ToList());
                type_info.Set("extension_methods", extension_methods.ToList());
            }, templateRef.LuaRegister, textWriter);
            textWriter.Close();
        }

        // The extension methods of the register file's map
        // (InternalGlobals.extensionMethodMap).
        static IEnumerable<MethodInfo> registerExtensionMethods()
        {
            var lookup = LuaCallCSharp.Distinct().ToDictionary(t => t);

            var extension_methods_from_lcs = (from t in LuaCallCSharp
                                    where isDefined(t, typeof(ExtensionAttribute))
                                    from method in t.GetMethods(BindingFlags.Static | BindingFlags.Public)
                                    where isDefined(method, typeof(ExtensionAttribute)) && !isObsolete(method)
                                    where !method.ContainsGenericParameters || isSupportedGenericMethod(method)
                                    select makeGenericMethodIfNeeded(method))
                                    // A generated type has its extension methods in its wrapper, except
                                    // those named in [DoNotGen], which its lazy member finds here.
                                    .Where(method => !lookup.ContainsKey(method.GetParameters()[0].ParameterType)
                                        || (IsDoNotGen(method.GetParameters()[0].ParameterType, method.Name) && !isMethodInBlackList(method)));

            return (from t in ReflectionUse
                    where isDefined(t, typeof(ExtensionAttribute))
                    from method in t.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    where isDefined(method, typeof(ExtensionAttribute)) && !isObsolete(method)
                    where !method.ContainsGenericParameters || isSupportedGenericMethod(method)
                    select makeGenericMethodIfNeeded(method)).Concat(extension_methods_from_lcs);
        }

        public static void AllSubStruct(Type type, Action<Type> cb)
        {
            if (!type.IsPrimitive && type != typeof(decimal))
            {
                cb(type);
                foreach(var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    AllSubStruct(fieldInfo.FieldType, cb);
                }

                foreach(var propInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if ((AdditionalProperties.ContainsKey(type) && AdditionalProperties[type].Contains(propInfo.Name))
                        || isDefined(propInfo, typeof(AdditionalPropertiesAttribute)))
                    {
                        AllSubStruct(propInfo.PropertyType, cb);
                    }
                }
            }
        }

        class XluaFieldInfo
        {
            public string Name;
            public Type Type;
            public bool IsField;
            public int Size;
        }

        class XluaTypeInfo
        {
            public Type Type;
            public List<XluaFieldInfo> FieldInfos;
            public List<List<XluaFieldInfo>> FieldGroup;
            public bool IsRoot;
        }

        static XluaTypeInfo getXluaTypeInfo(Type t, Dictionary<Type, Type> set)
        {
            var fs = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(fi => new XluaFieldInfo { Name = fi.Name, Type = fi.FieldType, IsField = true, Size = SizeOf(fi.FieldType) })
                        .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(prop => {
                            return (AdditionalProperties.ContainsKey(t) && AdditionalProperties[t].Contains(prop.Name))
                                || isDefined(prop, typeof(AdditionalPropertiesAttribute));
                        })
                        .Select(prop => new XluaFieldInfo { Name = prop.Name, Type = prop.PropertyType, IsField = false, Size = SizeOf(prop.PropertyType) }));
            int float_field_count = 0;
            bool only_float = true;
            foreach (var f in fs)
            {
                if (f.Type == typeof(float))
                {
                    float_field_count++;
                }
                else
                {
                    only_float = false;
                    break;
                }
            }
            List<List<XluaFieldInfo>> grouped_field = null;
            if (only_float && float_field_count > 1)
            {
                grouped_field = new List<List<XluaFieldInfo>>();
                List<XluaFieldInfo> group = null;
                foreach (var f in fs)
                {
                    if (group == null) group = new List<XluaFieldInfo>();
                    group.Add(f);
                    if (group.Count >= 6)
                    {
                        grouped_field.Add(group);
                        group = null;
                    }
                }
                if (group != null) grouped_field.Add(group);
            }
            return new XluaTypeInfo { Type = t, FieldInfos = fs.ToList(), FieldGroup = grouped_field, IsRoot = set.ContainsKey(t) };
        }

        public static void GenPackUnpack(IEnumerable<Type> types, string save_path)
        {
            var set = types.ToDictionary(type => type);
            List<Type> all_types = new List<Type>();

            foreach(var type in types)
            {
                AllSubStruct(type, (t) =>
                {
                    all_types.Add(t);
                });
            }

            string filePath = save_path + "PackUnpack.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            GenOne(typeof(CopyByValue), (type, type_info) =>
            {
                type_info.Set("type_infos", all_types.Distinct().Select(t => getXluaTypeInfo(t, set)).ToList());
            }, templateRef.PackUnpack, textWriter);
            textWriter.Close();
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

        public static Dictionary<Type, HotfixFlag> HotfixCfg = null;

        public static Dictionary<Type, OptimizeFlag> OptimizeCfg = null;

        public static Dictionary<Type, HashSet<string>> DoNotGen = null;

        public static List<string> assemblyList = null;

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
            if (isDefined(test, typeof(HotfixAttribute)))
            {
                object cfg = get_cfg();
                if (cfg is IEnumerable<Type>)
                {
                    var ca = GetCustomAttribute(test, typeof(HotfixAttribute));
#if XLUA_GENERAL
                    var hotfixType = (HotfixFlag)Convert.ToInt32(ca.GetType().GetProperty("Flag").GetValue(ca, null));
#else
                    var hotfixType = (ca as HotfixAttribute).Flag;
#endif
                    foreach (var type in cfg as IEnumerable<Type>)
                    {
                        if (!HotfixCfg.ContainsKey(type) && !isObsolete(type) 
                            && !type.IsEnum && !typeof(Delegate).IsAssignableFrom(type)
                            && (!type.IsGenericType || type.IsGenericTypeDefinition) 
                            && (type.Namespace == null || (type.Namespace != "XLua" && !type.Namespace.StartsWith("XLua.")))
                            && (assemblyList.Contains(type.Module.Assembly.GetName().Name)))
                        {
                            HotfixCfg.Add(type, hotfixType);
                        }
                    }
                }
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

            HotfixCfg = new Dictionary<Type, HotfixFlag>();

            OptimizeCfg = new Dictionary<Type, OptimizeFlag>();

            DoNotGen = new Dictionary<Type, HashSet<string>>();

#if UNITY_EDITOR && HOTFIX_ENABLE
            assemblyList = HotfixConfig.GetHotfixAssembly().Select(a => a.GetName().Name).ToList();
#else
            assemblyList = new List<string>();
#endif
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
                .Where(type => !type.Name.Contains("<"))
                .ToList();
            CSharpCallLua = CSharpCallLua.Distinct()
                .Where(type => IsPublic(type) && !isObsolete(type) && !type.IsGenericTypeDefinition)
                .Where(type => type != typeof(Delegate) && type != typeof(MulticastDelegate))
                .Where(type => !isRefReadOnlyUnusable(type))
                .ToList();
            GCOptimizeList = GCOptimizeList.Distinct()
                .Where(type => IsPublic(type) && !isObsolete(type) && !type.IsGenericTypeDefinition)
                .ToList();
            ReflectionUse = ReflectionUse.Distinct()
                .Where(type => !isObsolete(type) && !type.IsGenericTypeDefinition)
                .ToList();
        }

        static Dictionary<Type, int> type_size = new Dictionary<Type, int>()
        {
            { typeof(byte), 1 },
            { typeof(sbyte), 1 },
            { typeof(short), 2 },
            { typeof(ushort), 2 },
            { typeof(int), 4 },
            { typeof(uint), 4 },
            { typeof(long), 8 },
            { typeof(ulong), 8 },
            { typeof(float), 4 },
            { typeof(double), 8 },
            { typeof(decimal), 16 }
        };

        static int SizeOf(Type type)
        {
            if (type_size.ContainsKey(type))
            {
                return type_size[type];
            }

            if (!CopyByValue.IsStruct(type))
            {
                return -1;
            }

            int size = 0;
            foreach(var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                int t_size = SizeOf(fieldInfo.FieldType);
                if (t_size == -1)
                {
                    size = -1;
                    break;
                }
                size += t_size;
            }
            if (size != -1)
            {
                foreach (var propInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if ((AdditionalProperties.ContainsKey(type) && AdditionalProperties[type].Contains(propInfo.Name)) || isDefined(propInfo, typeof(AdditionalPropertiesAttribute)))
                    {
                        int t_size = SizeOf(propInfo.PropertyType);
                        if (t_size == -1)
                        {
                            size = -1;
                            break;
                        }
                        size += t_size;
                    }
                }
            }

            if (!type_size.ContainsKey(type))
            {
                type_size.Add(type, size);
            }

            return size;
        }

        public static void Gen(IEnumerable<Type> wraps, IEnumerable<Type> gc_optimze_list, IEnumerable<Type> itf_bridges, string save_path)
        {
            templateCache.Clear();
            Directory.CreateDirectory(save_path);
            GenWrap(wraps, save_path);
            GenWrapPusher(gc_optimze_list.Concat(wraps.Where(type=>type.IsEnum)).Distinct(), save_path);
            GenPackUnpack(gc_optimze_list.Where(type => !type.IsPrimitive && SizeOf(type) != -1), save_path);
            GenInterfaceBridge(itf_bridges, save_path);
        }

        public static void GenCodeForClass(bool minimum = false)
        {
            var warp_types = minimum ? new List<Type>() : LuaCallCSharp;
            var itf_bridges_types = CSharpCallLua.Where(type => type.IsInterface).Distinct();

            Gen(warp_types, GCOptimizeList, itf_bridges_types, GeneratorConfig.common_path);
        }

#if XLUA_GENERAL
        static bool IsExtensionMethod(MethodInfo method)
        {
            return isDefined(method, typeof(ExtensionAttribute));
        }

        static bool IsDelegate(Type type)
        {
            return type.BaseType != null && type.BaseType.ToString() == "System.MulticastDelegate";
        }

        public static void GenAll(XLuaTemplates templates, IEnumerable<Type> all_types)
        {
            var start = DateTime.Now;
            refReadOnlyReported.Clear();
            Directory.CreateDirectory(GeneratorConfig.common_path);
            templateRef = templates;
            GetGenConfig(all_types.Where(type => !type.IsGenericTypeDefinition));
            luaenv.DoString("require 'TemplateCommon'");
            luaenv.Global.Set("IsExtensionMethod", new Func<MethodInfo, bool>(IsExtensionMethod));
            luaenv.Global.Set("IsDelegate", new Func<Type, bool>(IsDelegate));
            var gen_push_types_setter = luaenv.Global.Get<LuaFunction>("SetGenPushAndUpdateTypes");
            gen_push_types_setter.Call(GCOptimizeList.Where(t => !t.IsPrimitive && SizeOf(t) != -1).Concat(LuaCallCSharp.Where(t => t.IsEnum)).Distinct().ToList());
            luaenv.Global.Get<LuaFunction>("SetGenTableGetTypes").Call(GCOptimizeList.Where(t => !t.IsEnum && SizeOf(t) == -1).Distinct().ToList());
            var xlua_classes_setter = luaenv.Global.Get<LuaFunction>("SetXLuaClasses");
            xlua_classes_setter.Call(XLua.Utils.GetAllTypes().Where(t => t.Namespace == "XLua").ToList());
            GenDelegateBridges(all_types);
            GenEnumWraps();
            GenCodeForClass();
            GenLuaRegister();
            // Test only: the binding surface for Test/Native/surface_diff.py,
            // which compares it with the source generator's manifest.
            string manifest = Environment.GetEnvironmentVariable("XLUA_GEN_MANIFEST");
            if (!string.IsNullOrEmpty(manifest)) File.WriteAllText(manifest, BindingSurface(), new UTF8Encoding(false));
            Console.WriteLine("finished! use " + (DateTime.Now - start).TotalMilliseconds + " ms");
            luaenv.Dispose();
        }

        // ---- the binding surface as JSON, in the format of the source
        // generator's manifest (XLua.SourceGenerator Wrappers.cs) ----

        static string jsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string jsonArray(IEnumerable<string> items)
        {
            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        static string surfaceSignature(MethodBase m, int defaults)
        {
            var mi = m as MethodInfo;
            if (mi != null && mi.IsGenericMethod && !mi.IsGenericMethodDefinition) m = mi.GetGenericMethodDefinition();
            string name = m.IsConstructor ? ".ctor" : m.Name;
            string arity = m.IsGenericMethodDefinition ? "`" + m.GetGenericArguments().Length : "";
            return m.DeclaringType.ToString() + "::" + name + arity + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.ToString()).ToArray()) + ")"
                + (defaults > 0 ? " -" + defaults : "");
        }

        static object member(object o, string name)
        {
            return o.GetType().GetProperty(name).GetValue(o, null);
        }

        static IEnumerable<object> items(object list)
        {
            return ((System.Collections.IEnumerable)list).Cast<object>();
        }

        static string classSurface(Type type)
        {
            LuaTable info = luaenv.NewTable();
            try
            {
                getClassInfo(type, info);
                var ctors = items(info.Get<object>("constructors")).Cast<MethodBase>().ToList();
                var ctorDefaults = items(info.Get<object>("constructor_def_vals")).Cast<int>().ToList();
                var parts = new List<string>();
                parts.Add("\"type\":" + jsonString(type.ToString()));
                parts.Add("\"kind\":\"class\"");
                parts.Add("\"constructors\":" + jsonArray(ctors.Select((c, i) => jsonString(surfaceSignature(c, ctorDefaults[i])))));
                parts.Add("\"methods\":" + jsonArray(items(info.Get<object>("methods")).Select(g =>
                {
                    var overloads = items(member(g, "Overloads")).Cast<MethodBase>().ToList();
                    var defaults = items(member(g, "DefaultValues")).Cast<int>().ToList();
                    string key = (string)member(g, "Name");
                    string name = key.EndsWith("_xlua_st_") ? key.Substring(0, key.Length - "_xlua_st_".Length) : key;
                    return "{\"name\":" + jsonString(name) + ",\"static\":" + ((bool)member(g, "IsStatic") ? "true" : "false")
                        + ",\"overloads\":" + jsonArray(overloads.Select((o, i) => jsonString(surfaceSignature(o, defaults[i])))) + "}";
                })));
                parts.Add("\"getters\":" + jsonArray(items(info.Get<object>("getters")).Select(g => jsonString((string)member(g, "Name")
                    + ((bool)member(g, "IsStatic") ? " static" : "") + ((bool)member(g, "ReadOnly") ? " readonly" : "")))));
                parts.Add("\"setters\":" + jsonArray(items(info.Get<object>("setters")).Select(s => jsonString((string)member(s, "Name")
                    + ((bool)member(s, "IsStatic") ? " static" : "")))));
                parts.Add("\"operators\":" + jsonArray(items(info.Get<object>("operators")).Select(o => "{\"name\":" + jsonString((string)member(o, "Name"))
                    + ",\"overloads\":" + jsonArray(items(member(o, "Overloads")).Cast<MethodBase>().Select(m => jsonString(surfaceSignature(m, 0)))) + "}")));
                parts.Add("\"indexers\":" + jsonArray(items(info.Get<object>("indexers")).Cast<MethodBase>().Select(m => jsonString(surfaceSignature(m, 0)))));
                parts.Add("\"newindexers\":" + jsonArray(items(info.Get<object>("newindexers")).Cast<MethodBase>().Select(m => jsonString(surfaceSignature(m, 0)))));
                parts.Add("\"events\":" + jsonArray(items(info.Get<object>("events")).Select(e => jsonString((string)member(e, "Name")
                    + ((bool)member(e, "IsStatic") ? " static" : "") + ((bool)member(e, "CanAdd") ? " add" : "") + ((bool)member(e, "CanRemove") ? " remove" : "")))));
                parts.Add("\"lazy\":" + jsonArray(items(info.Get<object>("lazymembers")).Cast<LazyMemberInfo>().Select(l => jsonString(l.Index + " " + l.Name + " "
                    + l.MemberType.Replace("LazyMemberTypes.", "") + (l.IsStatic == "true" ? " static" : "")))));
                return "{" + string.Join(",", parts.ToArray()) + "}";
            }
            finally
            {
                info.Dispose();
            }
        }

        static string enumSurface(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => !isMemberInBlackList(x)).ToArray();
            var toGen = fields.Where(f => !f.IsDefined(typeof(ObsoleteAttribute), false)).ToArray();
            return "{\"type\":" + jsonString(type.ToString()) + ",\"kind\":\"enum\",\"fields\":" + jsonArray(toGen.Select(f => jsonString(f.Name)))
                + ",\"fieldCount\":" + fields.Length + ",\"registerEnumType\":" + (toGen.Length > 20 ? "true" : "false") + "}";
        }

        // The key the source generator's SignatureKey (XLua.SourceGenerator
        // Bridges.cs) gives a bridge group: Ret(Mode Type[...],...) with
        // Type.ToString() names, "in " for IsInParam, "..." for params.
        static string bridgeKey(MethodInfoSimulation m)
        {
            var parameters = m.GetParameters().Select(p =>
            {
                string mode = p.IsOut && p.ParameterType.IsByRef ? "out " : p.IsInParam ? "in " : p.ParameterType.IsByRef ? "ref " : "";
                Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                return mode + t.ToString() + (p.IsParamArray ? "..." : "");
            });
            return (m.ReturnType == typeof(void) ? "void" : m.ReturnType.ToString()) + "(" + string.Join(",", parameters.ToArray()) + ")";
        }

        // Each bridge with the delegate types it serves; "hotfix" counts the
        // hotfix signatures that share it (the source generator writes those
        // from milestone M7).
        static string delegateBridgesSurface()
        {
            return jsonArray(delegateGroups
                .Select(g => new
                {
                    Key = bridgeKey(g[0]),
                    Delegates = g.Where(d => d.DeclaringType != null).Select(d => d.DeclaringType.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    Hotfix = g.Count(d => d.DeclaringType == null),
                })
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => "{\"key\":" + jsonString(g.Key) + ",\"delegates\":" + jsonArray(g.Delegates.Select(jsonString)) + ",\"hotfix\":" + g.Hotfix + "}"));
        }

        static string accessorFlags(PropertyInfo p)
        {
            return (p.CanRead ? " get" : "") + (p.CanWrite ? " set" : "");
        }

        // The members an interface bridge implements (getInterfaceInfo).
        static string interfaceSurface(Type type)
        {
            LuaTable info = luaenv.NewTable();
            try
            {
                getInterfaceInfo(type, info);
                Func<IEnumerable<string>, string> sorted = list => jsonArray(list.OrderBy(x => x, StringComparer.Ordinal).Select(jsonString));
                var parts = new List<string>();
                parts.Add("\"type\":" + jsonString(type.ToString()));
                parts.Add("\"methods\":" + sorted(items(info.Get<object>("methods")).Cast<MethodInfo>().Select(m => surfaceSignature(m, 0))));
                parts.Add("\"properties\":" + sorted(items(info.Get<object>("propertys")).Cast<PropertyInfo>()
                    .Select(p => p.DeclaringType.ToString() + "::" + p.Name + accessorFlags(p))));
                parts.Add("\"events\":" + sorted(items(info.Get<object>("events")).Cast<EventInfo>().Select(e => e.DeclaringType.ToString() + "::" + e.Name)));
                parts.Add("\"indexers\":" + sorted(items(info.Get<object>("indexers")).Cast<PropertyInfo>()
                    .Select(p => p.DeclaringType.ToString() + "::this[" + p.GetIndexParameters()[0].ParameterType.ToString() + "]" + accessorFlags(p))));
                return "{" + string.Join(",", parts.ToArray()) + "}";
            }
            finally
            {
                info.Dispose();
            }
        }

        static string BindingSurface()
        {
            var types = LuaCallCSharp.Distinct().Where(t => !isObsolete(t)).OrderBy(t => t.ToString(), StringComparer.Ordinal)
                .Select(t => t.IsEnum ? enumSurface(t) : classSurface(t));
            var extensions = registerExtensionMethods().Select(m => jsonString(m.GetParameters()[0].ParameterType.ToString() + " <- " + surfaceSignature(m, 0)));
            var interfaces = CSharpCallLua.Where(t => t.IsInterface && !isObsolete(t)).Distinct().OrderBy(t => t.ToString(), StringComparer.Ordinal)
                .Select(interfaceSurface);
            return "{\"generator\":\"legacy\",\"types\":" + jsonArray(types) + ",\"extensionMethods\":" + jsonArray(extensions)
                + ",\"delegateBridges\":" + delegateBridgesSurface() + ",\"interfaceBridges\":" + jsonArray(interfaces) + "}\n";
        }
#endif

#if !XLUA_GENERAL
        static void callCustomGen()
        {
            foreach (var method in (from type in XLua.Utils.GetAllTypes()
                               from method in type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                               where method.IsDefined(typeof(GenCodeMenuAttribute), false) select method))
            {
                method.Invoke(null, new object[] { });
            }
        }

        [MenuItem("XLua/Generate Code", false, 1)]
        public static void GenAll()
        {
#if UNITY_2018 && (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX)
            if (File.Exists("./Tools/MonoBleedingEdge/bin/mono.exe"))
            {
                GenUsingCLI();
                return;
            }
#endif
            var start = DateTime.Now;
            refReadOnlyReported.Clear();
            Directory.CreateDirectory(GeneratorConfig.common_path);
            GetGenConfig(XLua.Utils.GetAllTypes());
            luaenv.DoString("require 'TemplateCommon'");
            var gen_push_types_setter = luaenv.Global.Get<LuaFunction>("SetGenPushAndUpdateTypes");
            gen_push_types_setter.Call(GCOptimizeList.Where(t => !t.IsPrimitive && SizeOf(t) != -1).Concat(LuaCallCSharp.Where(t => t.IsEnum)).Distinct().ToList());
            luaenv.Global.Get<LuaFunction>("SetGenTableGetTypes").Call(GCOptimizeList.Where(t => !t.IsEnum && SizeOf(t) == -1).Distinct().ToList());
            var xlua_classes_setter = luaenv.Global.Get<LuaFunction>("SetXLuaClasses");
            xlua_classes_setter.Call(XLua.Utils.GetAllTypes().Where(t => t.Namespace == "XLua").ToList());
            GenDelegateBridges(XLua.Utils.GetAllTypes(false));
            GenEnumWraps();
            GenCodeForClass();
            GenLuaRegister();
            callCustomGen();
            Debug.Log("finished! use " + (DateTime.Now - start).TotalMilliseconds + " ms");
            AssetDatabase.Refresh();
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
        public static void GenUsingCLI()
        {
#if UNITY_EDITOR_OSX
            var monoPath = "./Tools/MonoBleedingEdge/bin/mono";
#else
            var monoPath = "./Tools/MonoBleedingEdge/bin/mono.exe";
#endif

            var args = new List<string>()
            {
                "./Tools/XLuaGenerate.exe",
                "./Library/ScriptAssemblies/Assembly-CSharp.dll",
                "./Library/ScriptAssemblies/Assembly-CSharp-Editor.dll",
                GeneratorConfig.common_path
            };

            var searchPaths = new List<string>();
            foreach (var path in
                (from asm in AppDomain.CurrentDomain.GetAssemblies() select asm.ManifestModule.FullyQualifiedName)
                 .Distinct())
            {
                try
                {
                    searchPaths.Add(Path.GetDirectoryName(path));
                }
                catch { }
            }
            args.AddRange(searchPaths.Distinct());

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo.FileName = monoPath;
            process.StartInfo.Arguments = "\"" + string.Join("\" \"", args.ToArray()) + "\"";
            process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            while (!process.StandardError.EndOfStream)
            {
                Debug.LogError(process.StandardError.ReadLine());
            }

            while (!process.StandardOutput.EndOfStream)
            {
                Debug.Log(process.StandardOutput.ReadLine());
            }

            process.WaitForExit();
            GetGenConfig(XLua.Utils.GetAllTypes());
            callCustomGen();
            AssetDatabase.Refresh();
        }
#endif

        [MenuItem("XLua/Clear Generated Code", false, 2)]
        public static void ClearAll()
        {
            clear(GeneratorConfig.common_path);
        }

        public delegate IEnumerable<CustomGenTask> GetTasks(LuaEnv lua_env, UserConfig user_cfg);

        public static void CustomGen(string template_src, GetTasks get_tasks)
        {
            GetGenConfig(XLua.Utils.GetAllTypes());

            LuaFunction template = XLua.TemplateEngine.LuaTemplate.Compile(luaenv,
                template_src);
            foreach (var gen_task in get_tasks(luaenv, new UserConfig() {
                LuaCallCSharp = LuaCallCSharp,
                CSharpCallLua = CSharpCallLua,
                ReflectionUse = ReflectionUse
            }))
            {
                LuaTable meta = luaenv.NewTable();
                meta.Set("__index", luaenv.Global);
                gen_task.Data.SetMetaTable(meta);
                meta.Dispose();

                try
                {
                    string genCode = XLua.TemplateEngine.LuaTemplate.Execute(template, gen_task.Data);
                    gen_task.Output.Write(genCode);
                    gen_task.Output.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError("gen file fail! template=" + template_src + ", err=" + e.Message + ", stack=" + e.StackTrace);
                }
                finally
                {
                    gen_task.Output.Close();
                }
            }
        }

#endif

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
#if !XLUA_GENERAL
        [UnityEditor.Callbacks.PostProcessBuild(1)]
        public static void CheckGenerate(BuildTarget target, string pathToBuiltProject)
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                return;
            }
            if (!DelegateBridge.Gen_Flag)
            {
                throw new InvalidOperationException("Code has not been generated, may be not work in phone!");
            }
        }
#endif
    }
}
