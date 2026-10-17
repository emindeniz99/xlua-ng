/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using System;
using System.Collections.Generic;

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
    // The source generator reads both forms (docs/design/source-generator.md,
    // section 4). The legacy generator ("Generate Code", XLuaGenerate) and the
    // hotfix injector read only the forms without a type argument.
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
        }
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
        }
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
        }
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
        }
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


