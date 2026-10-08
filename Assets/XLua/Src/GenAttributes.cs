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


