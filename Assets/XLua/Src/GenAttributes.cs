/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Reflection;

#if !XLUA_GENERAL
// SysGenConfig below in the form the source generator reads: it generates the
// pushers in every assembly that references xLua (and in xLua's own assembly
// when that has an asmdef), as the legacy generator does from the lists.
// Keep both forms in step.
[assembly: XLua.GCOptimize(typeof(UnityEngine.Vector2))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Vector3))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Vector4))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Color))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Quaternion))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Ray))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Bounds))]
[assembly: XLua.GCOptimize(typeof(UnityEngine.Ray2D))]
[assembly: XLua.AdditionalProperties(typeof(UnityEngine.Ray), "origin", "direction")]
[assembly: XLua.AdditionalProperties(typeof(UnityEngine.Ray2D), "origin", "direction")]
[assembly: XLua.AdditionalProperties(typeof(UnityEngine.Bounds), "center", "extents")]
#endif

namespace XLua
{
    public enum GenFlag
    {
        No = 0,
        [Obsolete("use GCOptimizeAttribute instead")]
        GCOptimize = 1
    }

    // The configuration attributes below work where they always did: on a type,
    // on a member (BlackList) or on a static configuration list. Each also has
    // a constructor that names the configured type, for use at assembly level,
    // also for types the project does not own:
    //
    //     [assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]
    //     [assembly: BlackList(typeof(UnityEngine.Light), "shadowAngle")]
    //
    // LuaCallCSharp, CSharpCallLua, GCOptimize, ReflectionUse and Hotfix also
    // take several types in one attribute; GCOptimize and Hotfix then take the
    // flags first, for all of them:
    //
    //     [assembly: LuaCallCSharp(typeof(UnityEngine.Transform), typeof(UnityEngine.GameObject))]
    //     [assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop), typeof(Game.Bag))]
    //
    // C# picks the constructor without the array whenever one fits ([X],
    // [X(flag)], [X(typeof(T))], [X(typeof(T), flag)]), so these compile as
    // they did before the array forms existed. Type is set only by the
    // one-type constructor; Types lists the configured types of both forms.
    //
    // The source generator reads all these forms (docs/design/source-generator.md,
    // section 4). The legacy generator ("Generate Code", XLuaGenerate) reads only
    // the forms without a type argument; the hotfix injector reads those and
    // [assembly: Hotfix(...)] in the injected assembly.
    //
    // AttributeTargets.All is what these attributes allowed before, when they
    // had no AttributeUsage; AllowMultiple lets one assembly list many types.

    //Use this attribute to generate wrappers for Lua calls into C#.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class LuaCallCSharpAttribute : Attribute
    {
        GenFlag flag;
        Type type;

        public GenFlag Flag {
            get
            {
                return flag;
            }
        }

        // The configured type of the assembly-level form; null in the other forms.
        public Type Type
        {
            get
            {
                return type;
            }
        }

        public LuaCallCSharpAttribute(GenFlag flag = GenFlag.No)
        {
            this.flag = flag;
        }

        public LuaCallCSharpAttribute(Type type)
        {
            this.type = type;
            types = new Type[] { type };
        }

        public LuaCallCSharpAttribute(params Type[] types)
        {
            this.types = types;
        }

        Type[] types;

        // The configured types of the assembly-level forms; null in the other forms.
        public Type[] Types { get { return types; } }
    }

    //Use this attribute to generate adapters for C# calls into Lua (delegates and interfaces).
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class CSharpCallLuaAttribute : Attribute
    {
        Type type;

        public Type Type
        {
            get
            {
                return type;
            }
        }

        public CSharpCallLuaAttribute()
        {
        }

        public CSharpCallLuaAttribute(Type type)
        {
            this.type = type;
            types = new Type[] { type };
        }

        public CSharpCallLuaAttribute(params Type[] types)
        {
            this.types = types;
        }

        Type[] types;

        public Type[] Types { get { return types; } }
    }

    //Use this attribute to exclude a property or method from code generation.
    //The assembly-level forms name the member. Fields, properties and events match
    //by name. A method or constructor (".ctor") matches when its parameter types are
    //exactly the ones listed, so listing none means the overload without
    //parameters, as in the legacy List<List<string>> form.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class BlackListAttribute : Attribute
    {
        Type type;
        string typeName;
        string member;
        Type[] parameterTypes;
        string[] parameterTypeNames;

        public Type Type { get { return type; } }

        // The by-name form: the type as Type.ToString() writes it, for members
        // that do not exist on every build target.
        public string TypeName { get { return typeName; } }

        public string Member { get { return member; } }

        public Type[] ParameterTypes { get { return parameterTypes; } }

        // The by-name form: Type.ToString() names, as in the legacy lists.
        public string[] ParameterTypeNames { get { return parameterTypeNames; } }

        public BlackListAttribute()
        {
        }

        public BlackListAttribute(Type type, string member, params Type[] parameterTypes)
        {
            this.type = type;
            this.member = member;
            this.parameterTypes = parameterTypes;
        }

        public BlackListAttribute(string typeName, string member, params string[] parameterTypeNames)
        {
            this.typeName = typeName;
            this.member = member;
            this.parameterTypeNames = parameterTypeNames;
        }
    }

    // How [BlackList] configuration matches members. The generator
    // (Editor/Generator.cs), the emitted wrappers (CodeEmit.cs) and the
    // reflection wrappers (Utils.makeReflectionWrap) all use these rules, so
    // that every binding mode leaves out the same members.
    public static class BlackListRules
    {
        // An entry {type, member} matches the members of that name that the
        // type declares; type names are Type.ToString() names.
        public static bool MatchesMember(List<string> entry, MemberInfo member)
        {
            return entry != null && entry.Count >= 2 && member.DeclaringType != null
                && member.Name == entry[1] && member.DeclaringType.ToString() == entry[0];
        }

        // For a method or constructor the entry also lists the parameter
        // types ({type, method, parameter types...}); {type, method} matches
        // only an overload without parameters.
        public static bool MatchesMethod(List<string> entry, MethodBase method)
        {
            if (!MatchesMember(entry, method)) return false;
            var parameters = method.GetParameters();
            if (parameters.Length != entry.Count - 2) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType.ToString() != entry[i + 2]) return false;
            }
            return true;
        }

#if UNITY_EDITOR || XLUA_GENERAL
        // The configuration the emitted and reflection wrappers apply: the
        // [BlackList] fields and properties (List<List<string>> and
        // Func<MemberInfo, bool>) of static classes, read once from the
        // loaded assemblies. Players do not read it (the configuration
        // usually lives in an Editor folder), so their reflection wrappers
        // show every member, as before.
        static Dictionary<string, Dictionary<string, List<List<string>>>> entries;
        static List<Func<MemberInfo, bool>> filters;
        static readonly object loadLock = new object();

        static void load()
        {
            if (filters != null) return;
            lock (loadLock)
            {
                if (filters != null) return;
                var byType = new Dictionary<string, Dictionary<string, List<List<string>>>>();
                var funcs = new List<Func<MemberInfo, bool>>();
                foreach (var t in Utils.GetAllTypes(true))
                {
                    if (!t.IsAbstract() || !t.IsSealed()) continue;
                    var fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];
                        if (isConfigType(field.FieldType) && field.IsDefined(typeof(BlackListAttribute), false))
                        {
                            addConfig(byType, funcs, field.GetValue(null));
                        }
                    }
                    var props = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];
                        if (isConfigType(prop.PropertyType) && prop.IsDefined(typeof(BlackListAttribute), false))
                        {
                            addConfig(byType, funcs, prop.GetValue(null, null));
                        }
                    }
                }
                entries = byType;
                filters = funcs;
            }
        }

        static bool isConfigType(Type type)
        {
            return typeof(List<List<string>>).IsAssignableFrom(type) || typeof(Func<MemberInfo, bool>).IsAssignableFrom(type);
        }

        static void addConfig(Dictionary<string, Dictionary<string, List<List<string>>>> byType, List<Func<MemberInfo, bool>> funcs, object config)
        {
            var filter = config as Func<MemberInfo, bool>;
            if (filter != null)
            {
                funcs.Add(filter);
                return;
            }
            var list = config as List<List<string>>;
            if (list == null) return;
            foreach (var entry in list)
            {
                if (entry == null || entry.Count < 2) continue;
                Dictionary<string, List<List<string>>> byMember;
                if (!byType.TryGetValue(entry[0], out byMember))
                {
                    byMember = new Dictionary<string, List<List<string>>>();
                    byType.Add(entry[0], byMember);
                }
                List<List<string>> same;
                if (!byMember.TryGetValue(entry[1], out same))
                {
                    same = new List<List<string>>();
                    byMember.Add(entry[1], same);
                }
                same.Add(entry);
            }
        }

        static List<List<string>> entriesFor(MemberInfo member)
        {
            Dictionary<string, List<List<string>>> byMember;
            List<List<string>> same;
            if (member.DeclaringType != null && entries.TryGetValue(member.DeclaringType.ToString(), out byMember)
                && byMember.TryGetValue(member.Name, out same))
            {
                return same;
            }
            return null;
        }

        // The emitted and reflection wrappers ask about members of more types
        // than the generator does (every type Lua uses, not only the
        // configured ones). A filter that throws for one of them does not
        // stop the type from loading: the member stays and the first
        // failure of each filter is logged.
        static HashSet<Func<MemberInfo, bool>> failedFilters = new HashSet<Func<MemberInfo, bool>>();

        static bool filtered(MemberInfo member)
        {
            for (int i = 0; i < filters.Count; i++)
            {
                try
                {
                    if (filters[i](member)) return true;
                }
                catch (Exception e)
                {
                    bool first;
                    lock (failedFilters) first = failedFilters.Add(filters[i]);
                    if (first)
                    {
                        string message = "[BlackList] filter " + filters[i].Method + " threw for " + member.DeclaringType + "." + member.Name + ": " + e;
#if XLUA_GENERAL
                        Console.Error.WriteLine(message);
#else
                        UnityEngine.Debug.LogWarning(message);
#endif
                    }
                }
            }
            return false;
        }

        // A field, property, event or property accessor that the generator
        // would leave out ([BlackList] on it, an entry or a filter).
        internal static bool IsListed(MemberInfo member)
        {
            if (member == null) return false;
            if (member.IsDefined(typeof(BlackListAttribute), false)) return true;
            load();
            var same = entriesFor(member);
            if (same != null)
            {
                for (int i = 0; i < same.Count; i++)
                {
                    if (MatchesMember(same[i], member)) return true;
                }
            }
            return filtered(member);
        }

        // A method or constructor that the generator would leave out.
        internal static bool IsListedMethod(MethodBase method)
        {
            if (method.IsDefined(typeof(BlackListAttribute), false)) return true;
            load();
            var same = entriesFor(method);
            if (same != null)
            {
                for (int i = 0; i < same.Count; i++)
                {
                    if (MatchesMethod(same[i], method)) return true;
                }
            }
            return filtered(method);
        }
#endif
    }

    [Flags]
    public enum OptimizeFlag
    {
        Default = 0,
        PackAsTable = 1
    }

    //Use this attribute to generate allocation-free marshaling code for a struct.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class GCOptimizeAttribute : Attribute
    {
        OptimizeFlag flag;
        Type type;

        public OptimizeFlag Flag
        {
            get
            {
                return flag;
            }
        }

        public Type Type
        {
            get
            {
                return type;
            }
        }

        public GCOptimizeAttribute(OptimizeFlag flag = OptimizeFlag.Default)
        {
            this.flag = flag;
        }

        public GCOptimizeAttribute(Type type, OptimizeFlag flag = OptimizeFlag.Default)
        {
            this.type = type;
            this.flag = flag;
            types = new Type[] { type };
        }

        public GCOptimizeAttribute(params Type[] types)
        {
            this.types = types;
        }

        public GCOptimizeAttribute(OptimizeFlag flag, params Type[] types)
        {
            this.flag = flag;
            this.types = types;
        }

        Type[] types;

        public Type[] Types { get { return types; } }
    }

    //Use this attribute to preserve types and members accessed through reflection.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class ReflectionUseAttribute : Attribute
    {
        Type type;

        public Type Type
        {
            get
            {
                return type;
            }
        }

        public ReflectionUseAttribute()
        {
        }

        public ReflectionUseAttribute(Type type)
        {
            this.type = type;
            types = new Type[] { type };
        }

        public ReflectionUseAttribute(params Type[] types)
        {
            this.types = types;
        }

        Type[] types;

        public Type[] Types { get { return types; } }
    }

    //Without arguments it only applies to fields or properties of type Dictionary<Type, List<string>>.
    //The assembly-level form names the type and the members that stay on reflection.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class DoNotGenAttribute : Attribute
    {
        Type type;
        string[] members;

        public Type Type { get { return type; } }

        public string[] Members { get { return members; } }

        public DoNotGenAttribute()
        {
        }

        public DoNotGenAttribute(Type type, params string[] members)
        {
            this.type = type;
            this.members = members;
        }
    }

    //Without arguments it only applies to fields or properties of type Dictionary<Type, List<string>>.
    //The assembly-level form names the type and its properties.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class AdditionalPropertiesAttribute : Attribute
    {
        Type type;
        string[] members;

        public Type Type { get { return type; } }

        public string[] Members { get { return members; } }

        public AdditionalPropertiesAttribute()
        {
        }

        public AdditionalPropertiesAttribute(Type type, params string[] members)
        {
            this.type = type;
            this.members = members;
        }
    }

    [Flags]
    public enum HotfixFlag
    {
        Stateless = 0,
        [Obsolete("use xlua.util.state instead!", true)]
        Stateful = 1,
        ValueTypeBoxing = 2,
        IgnoreProperty = 4,
        IgnoreNotPublic = 8,
        Inline = 16,
        IntKey = 32,
        AdaptByDelegate = 64,
        IgnoreCompilerGenerated = 128,
        NoBaseProxy = 256,
    }

    //The assembly-level form belongs in the assembly that defines the type: the
    //hotfix bridges are generated in the assembly that is injected.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class HotfixAttribute : Attribute
    {
        HotfixFlag flag;
        Type type;

        public HotfixFlag Flag
        {
            get
            {
                return flag;
            }
        }

        public Type Type
        {
            get
            {
                return type;
            }
        }

        public HotfixAttribute(HotfixFlag e = HotfixFlag.Stateless)
        {
            flag = e;
        }

        public HotfixAttribute(Type type, HotfixFlag flag = HotfixFlag.Stateless)
        {
            this.type = type;
            this.flag = flag;
            types = new Type[] { type };
        }

        public HotfixAttribute(params Type[] types)
        {
            this.types = types;
        }

        public HotfixAttribute(HotfixFlag flag, params Type[] types)
        {
            this.flag = flag;
            this.types = types;
        }

        Type[] types;

        public Type[] Types { get { return types; } }
    }

    [AttributeUsage(AttributeTargets.Delegate)]
    internal class HotfixDelegateAttribute : Attribute
    {
    }

#if !XLUA_GENERAL
    public static class SysGenConfig
    {
        [GCOptimize]
        static List<Type> GCOptimize
        {
            get
            {
                return new List<Type>() {
                    typeof(UnityEngine.Vector2),
                    typeof(UnityEngine.Vector3),
                    typeof(UnityEngine.Vector4),
                    typeof(UnityEngine.Color),
                    typeof(UnityEngine.Quaternion),
                    typeof(UnityEngine.Ray),
                    typeof(UnityEngine.Bounds),
                    typeof(UnityEngine.Ray2D),
                };
            }
        }

        [AdditionalProperties]
        static Dictionary<Type, List<string>> AdditionalProperties
        {
            get
            {
                return new Dictionary<Type, List<string>>()
                {
                    { typeof(UnityEngine.Ray), new List<string>() { "origin", "direction" } },
                    { typeof(UnityEngine.Ray2D), new List<string>() { "origin", "direction" } },
                    { typeof(UnityEngine.Bounds), new List<string>() { "center", "extents" } },
                };
            }
        }
    }
#endif
}


