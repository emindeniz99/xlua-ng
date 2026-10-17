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

namespace XLua
{
    public enum GenFlag
    {
        No = 0,
        [Obsolete("use GCOptimizeAttribute instead")]
        GCOptimize = 1
    }

    //Use this attribute to generate wrappers for Lua calls into C#.
    public class LuaCallCSharpAttribute : Attribute
    {
        GenFlag flag;
        public GenFlag Flag {
            get
            {
                return flag;
            }
        }

        public LuaCallCSharpAttribute(GenFlag flag = GenFlag.No)
        {
            this.flag = flag;
        }
    }

    //Use this attribute to generate adapters for C# calls into Lua.
    //[AttributeUsage(AttributeTargets.Delegate | AttributeTargets.Interface)]
    public class CSharpCallLuaAttribute : Attribute
    {
    }

    //Use this attribute to exclude a property or method from code generation.
    public class BlackListAttribute : Attribute
    {

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
    public class GCOptimizeAttribute : Attribute
    {
        OptimizeFlag flag;
        public OptimizeFlag Flag
        {
            get
            {
                return flag;
            }
        }

        public GCOptimizeAttribute(OptimizeFlag flag = OptimizeFlag.Default)
        {
            this.flag = flag;
        }
    }

    //Use this attribute to preserve types and members accessed through reflection.
    public class ReflectionUseAttribute : Attribute
    {

    }

    //Only applies to fields or properties of type Dictionary<Type, List<string>>.
    public class DoNotGenAttribute : Attribute
    {
        
    }

    public class AdditionalPropertiesAttribute : Attribute
    {

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

    public class HotfixAttribute : Attribute
    {
        HotfixFlag flag;
        public HotfixFlag Flag
        {
            get
            {
                return flag;
            }
        }

        public HotfixAttribute(HotfixFlag e = HotfixFlag.Stateless)
        {
            flag = e;
        }
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


