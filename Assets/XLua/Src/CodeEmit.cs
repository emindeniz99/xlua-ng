/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// CodeEmit (System.Reflection.Emit) builds wrappers and CSharpCallLua bridges
// at run time in the editor and in XLUA_GENERAL builds. Unity's .NET Standard
// 2.0 profile lacks the emit APIs; .NET Standard 2.1 (which also defines
// NET_STANDARD_2_0) and .NET Framework have them. Players never emit.
// ObjectTranslator.cs and Utils.cs repeat this condition.
#if (UNITY_EDITOR || XLUA_GENERAL) && (!NET_STANDARD_2_0 || NET_STANDARD_2_1)
#define XLUA_CODE_EMIT
#endif
#if XLUA_CODE_EMIT
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System;
using System.Linq;

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
#endif

namespace XLua
{
    public class CodeEmit
    {
        // One XLuaCodeEmit assembly for the process, and the types emitted
        // into it, shared by every LuaEnv (each has its own CodeEmit). .NET
        // never unloads a dynamic assembly, so one per env added up with
        // domain reload disabled (Tencent/xLua#1118). The emitted code holds
        // no env: type wrappers find the translator from L on each call
        // (ObjectTranslatorPool.FindTranslator), delegate and interface
        // bridges are instances that carry their env (LuaBase.luaEnv). Envs on
        // other threads emit too, so emitLock guards the module, genID and
        // the caches.
        // LuaAPI (XLua.LuaDLL.Bridge) inherits the Lua C API of XLua.LuaDLL.Lua in
        // XLua.Core: FlattenHierarchy finds the static methods of both classes.
        const BindingFlags LuaApiFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        static MethodInfo LuaApiMethod(string name)
        {
            return typeof(LuaAPI).GetMethod(name, LuaApiFlags);
        }

        static MethodInfo LuaApiMethod(string name, Type[] types)
        {
            return typeof(LuaAPI).GetMethod(name, LuaApiFlags, null, types, null);
        }

        static readonly object emitLock = new object();
        static ModuleBuilder codeEmitModule = null;
        static ulong genID = 0;
        // By wrapped type, with the extension methods the wrapper registered
        // (Utils.GetExtensionMethodsOf), so that generated bindings registered
        // later, which replace that list, get a new wrapper.
        static readonly Dictionary<Type, KeyValuePair<IEnumerable<MethodInfo>, Type>> typeWraps
            = new Dictionary<Type, KeyValuePair<IEnumerable<MethodInfo>, Type>>();
        static readonly Dictionary<Type, Type> interfaceImpls = new Dictionary<Type, Type>();
        // By the CSharpCallLua delegate types, grouped by signature as
        // ObjectTranslator.initCSharpCallLua found them.
        static readonly List<KeyValuePair<Type[][], Type>> delegateImpls = new List<KeyValuePair<Type[][], Type>>();

        private static readonly MethodInfo LuaEnv_ThrowExceptionFromError = typeof(LuaEnv).GetMethod("ThrowExceptionFromError");
        private static readonly FieldInfo LuaBase_luaEnv = typeof(LuaBase).GetField("luaEnv", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo DelegateBridgeBase_errorFuncRef_getter = typeof(LuaBase).GetProperty("_errorFuncRef", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true);
        private static readonly MethodInfo LuaAPI_load_error_func = LuaApiMethod("load_error_func");
        private static readonly MethodInfo LuaBase_translator_getter  = typeof(LuaBase).GetProperty("_translator", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true);
        private static readonly FieldInfo LuaBase_luaReference = typeof(LuaBase).GetField("luaReference", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo LuaAPI_lua_getref = LuaApiMethod("lua_getref");
        private static readonly MethodInfo Type_GetTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) });
        private static readonly MethodInfo ObjectTranslator_PushAny = typeof(ObjectTranslator).GetMethod("PushAny");
        private static readonly MethodInfo ObjectTranslator_PushParams = typeof(ObjectTranslator).GetMethod("PushParams");
        private static readonly MethodInfo LuaBase_L_getter = typeof(LuaBase).GetProperty("_L", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true);
        private static readonly MethodInfo LuaAPI_lua_pcall = LuaApiMethod("lua_pcall");
        private static readonly MethodInfo LuaAPI_lua_type = LuaApiMethod("lua_type");
        private static readonly MethodInfo LuaAPI_lua_isfunction = LuaApiMethod("lua_isfunction");
        private static readonly MethodInfo ObjectTranslator_GetObject = typeof(ObjectTranslator).GetMethod("GetObject", new Type[] { typeof(RealStatePtr),
               typeof(int), typeof(Type)});
        // A parameter, field or property of a UnityEngine.Object type that Lua
        // sets: a table raises instead of converting (ObjectCasters.RefusesTables).
        private static readonly MethodInfo ObjectTranslator_GetUnityObjectArg = typeof(ObjectTranslator).GetMethod("GetUnityObjectArg");
        private static readonly MethodInfo ObjectTranslator_GetParams = typeof(ObjectTranslator).GetMethod("GetParams", new Type[] { typeof(RealStatePtr), typeof(int) });
        private static readonly MethodInfo ObjectTranslator_Update = typeof(ObjectTranslator).GetMethod("Update");
        private static readonly MethodInfo LuaAPI_lua_pushvalue = LuaApiMethod("lua_pushvalue");
        private static readonly MethodInfo LuaAPI_lua_remove = LuaApiMethod("lua_remove");
        private static readonly MethodInfo LuaAPI_lua_pushstring = LuaApiMethod("lua_pushstring", new Type[] { typeof(RealStatePtr), typeof(string)});
        private static readonly MethodInfo LuaAPI_lua_gettop = LuaApiMethod("lua_gettop");
        private static readonly MethodInfo LuaAPI_lua_checkstack = LuaApiMethod("lua_checkstack");
        private static readonly MethodInfo LuaAPI_xlua_pgettable = LuaApiMethod("xlua_pgettable");
        private static readonly MethodInfo LuaAPI_xlua_psettable = LuaApiMethod("xlua_psettable");
        private static readonly MethodInfo LuaAPI_lua_pop = LuaApiMethod("lua_pop");
        private static readonly MethodInfo LuaAPI_lua_settop = LuaApiMethod("lua_settop");
        private static readonly MethodInfo LuaAPI_luaL_error = LuaApiMethod("luaL_error");
        private static readonly MethodInfo LuaAPI_xlua_is_eq_str = LuaApiMethod("xlua_is_eq_str", new Type[] {
        typeof(RealStatePtr), typeof(int), typeof(string)});

        private static readonly MethodInfo LuaAPI_xlua_pushinteger = LuaApiMethod("xlua_pushinteger");
        private static readonly MethodInfo LuaAPI_lua_pushint64 = LuaApiMethod("lua_pushint64");
        private static readonly MethodInfo LuaAPI_lua_pushnumber = LuaApiMethod("lua_pushnumber");
        private static readonly MethodInfo LuaAPI_xlua_pushuint = LuaApiMethod("xlua_pushuint");
        private static readonly MethodInfo LuaAPI_lua_pushuint64 = LuaApiMethod("lua_pushuint64");
        private static readonly MethodInfo LuaAPI_lua_pushboolean = LuaApiMethod("lua_pushboolean");
        private static readonly MethodInfo LuaAPI_lua_pushbytes = LuaApiMethod("lua_pushstring", new Type[] { typeof(RealStatePtr), typeof(byte[]) });
        private static readonly MethodInfo LuaAPI_lua_pushlightuserdata = LuaApiMethod("lua_pushlightuserdata");
        private static readonly MethodInfo ObjectTranslator_PushDecimal = typeof(ObjectTranslator).GetMethod("PushDecimal");
        private static readonly MethodInfo ObjectTranslator_GetDecimal = typeof(ObjectTranslator).GetMethod("GetDecimal");

        private static readonly Dictionary<Type, MethodInfo> fixPush;

        private static readonly MethodInfo LuaAPI_lua_tonumber = LuaApiMethod("lua_tonumber");
        private static readonly MethodInfo LuaAPI_lua_tostring = LuaApiMethod("lua_tostring");
        private static readonly MethodInfo LuaAPI_lua_toboolean = LuaApiMethod("lua_toboolean");
        private static readonly MethodInfo LuaAPI_lua_tobytes = LuaApiMethod("lua_tobytes");
        private static readonly MethodInfo LuaAPI_lua_touserdata = LuaApiMethod("lua_touserdata");

        private static readonly Dictionary<Type, MethodInfo> typedCaster;
        private static readonly Dictionary<Type, MethodInfo> fixCaster;
        // Lua's integer rule (LuaDLL.cs): integer parameters, fields and
        // properties that Lua sets use xlua_check* (integerArgCaster), whose
        // error names the argument, as ObjectCasters.GetArgCaster and the
        // generated wrappers do; values C# reads from Lua (bridge results and
        // out values) use xlua_get* (fixCaster).
        private static readonly Dictionary<Type, MethodInfo> integerArgCaster = new Dictionary<Type, MethodInfo>()
        {
            {typeof(byte), LuaApiMethod("xlua_checkbyte")},
            {typeof(char), LuaApiMethod("xlua_checkchar")},
            {typeof(short), LuaApiMethod("xlua_checkint16")},
            {typeof(sbyte), LuaApiMethod("xlua_checksbyte")},
            {typeof(ushort), LuaApiMethod("xlua_checkuint16")},
            {typeof(int), LuaApiMethod("xlua_checkint32")},
            {typeof(uint), LuaApiMethod("xlua_checkuint32")},
            {typeof(long), LuaApiMethod("xlua_checkinteger")},
            {typeof(ulong), LuaApiMethod("xlua_checkuint64")},
        };

        public CodeEmit()
        {
        }

        // The members the emitted code calls are looked up once for the
        // process (they belong to xLua and do not change), not once per
        // LuaEnv: every env makes a CodeEmit. They are only read afterwards,
        // so envs on several threads share them.
        static CodeEmit()
        {
            fixPush = new Dictionary<Type, MethodInfo>()
            {
                {typeof(byte), LuaAPI_xlua_pushinteger},
                {typeof(char), LuaAPI_xlua_pushinteger},
                {typeof(short), LuaAPI_xlua_pushinteger},
                {typeof(int), LuaAPI_xlua_pushinteger},
                {typeof(long), LuaAPI_lua_pushint64},
                {typeof(sbyte), LuaAPI_xlua_pushinteger},
                {typeof(float), LuaAPI_lua_pushnumber},
                {typeof(ushort), LuaAPI_xlua_pushinteger},
                {typeof(uint), LuaAPI_xlua_pushuint},
                {typeof(ulong), LuaAPI_lua_pushuint64},
                {typeof(double), LuaAPI_lua_pushnumber},
                {typeof(string), LuaAPI_lua_pushstring},
                {typeof(byte[]), LuaAPI_lua_pushbytes},
                {typeof(bool), LuaAPI_lua_pushboolean},
                {typeof(IntPtr), LuaAPI_lua_pushlightuserdata},
            };

            fixCaster = new Dictionary<Type, MethodInfo>()
            {
                {typeof(double), LuaAPI_lua_tonumber},
                {typeof(string), LuaAPI_lua_tostring},
                {typeof(bool), LuaAPI_lua_toboolean},
                {typeof(byte[]), LuaAPI_lua_tobytes},
                {typeof(IntPtr), LuaAPI_lua_touserdata},
                {typeof(byte), LuaApiMethod("xlua_getbyte")},
                {typeof(char), LuaApiMethod("xlua_getchar")},
                {typeof(short), LuaApiMethod("xlua_getint16")},
                {typeof(sbyte), LuaApiMethod("xlua_getsbyte")},
                {typeof(ushort), LuaApiMethod("xlua_getuint16")},
                {typeof(int), LuaApiMethod("xlua_getint32")},
                {typeof(uint), LuaApiMethod("xlua_getuint32")},
                {typeof(long), LuaApiMethod("xlua_getinteger")},
                {typeof(ulong), LuaApiMethod("xlua_getuint64")},
            };

            typedCaster = new Dictionary<Type, MethodInfo>()
            {
                {typeof(float), LuaAPI_lua_tonumber},
            };
        }

        private void emitPush(ILGenerator il, Type type, short dataPos, bool isParam, LocalBuilder L, LocalBuilder translator, bool isArg)
        {
            var paramElemType = type.IsByRef ? type.GetElementType() : type;
            var ldd = isArg ? OpCodes.Ldarg : OpCodes.Ldloc;
            MethodInfo pusher;
            if (fixPush.TryGetValue(paramElemType, out pusher))
            {
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(ldd, dataPos);

                if (type.IsByRef)
                {
                    if (paramElemType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldobj, paramElemType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldind_Ref);
                    }
                }

                il.Emit(OpCodes.Call, pusher);
            }
            else if (paramElemType == typeof(decimal))
            {
                il.Emit(OpCodes.Ldloc, translator);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(ldd, dataPos);
                if (type.IsByRef)
                {
                    il.Emit(OpCodes.Ldobj, paramElemType);
                }
                il.Emit(OpCodes.Callvirt, ObjectTranslator_PushDecimal);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, translator);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(ldd, dataPos);
                if (type.IsByRef)
                {
                    if (paramElemType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldobj, paramElemType);
                        il.Emit(OpCodes.Box, paramElemType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldind_Ref);
                    }
                }
                else if (type.IsValueType)
                {
                    il.Emit(OpCodes.Box, type);
                }
                if (isParam)
                {
                    il.Emit(OpCodes.Callvirt, ObjectTranslator_PushParams);
                }
                else
                {
                    il.Emit(OpCodes.Callvirt, ObjectTranslator_PushAny);
                }
            }
        }

        private ModuleBuilder CodeEmitModule
        {
            get
            {
                if (codeEmitModule == null)
                {
                    var assemblyName = new AssemblyName();
                    assemblyName.Name = "XLuaCodeEmit";
// .NET Standard 2.1 and .NET 5+ have no AppDomain.DefineDynamicAssembly.
#if NET5_0_OR_GREATER || NETSTANDARD2_1
                    codeEmitModule = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run)
#else
                    codeEmitModule = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run)
#endif
                        .DefineDynamicModule("XLuaCodeEmit");
                }
                return codeEmitModule;
            }
        }

        // Whether this is the XLuaCodeEmit assembly, once it exists. On .NET 5
        // and later AppDomain.GetAssemblies() lists the runtime assembly behind
        // the AssemblyBuilder, not the builder, hence the name of a dynamic
        // assembly too.
        internal static bool IsEmittedAssembly(Assembly assembly)
        {
            var module = codeEmitModule;
            if (module == null || !assembly.IsDynamic) return false;
            return ReferenceEquals(assembly, module.Assembly) || assembly.FullName == module.Assembly.FullName;
        }

        public Type EmitDelegateImpl(IEnumerable<IGrouping<MethodInfo, Type>> groups)
        {
            var groupList = groups.ToList();
            var key = groupList.Select(g => g.ToArray()).ToArray();
            lock (emitLock)
            {
                foreach (var entry in delegateImpls)
                {
                    if (entry.Key.Length == key.Length && entry.Key.Zip(key, (a, b) => a.SequenceEqual(b)).All(same => same))
                    {
                        return entry.Value;
                    }
                }
                var impl = emitDelegateImpl(groupList);
                delegateImpls.Add(new KeyValuePair<Type[][], Type>(key, impl));
                return impl;
            }
        }

        private Type emitDelegateImpl(IEnumerable<IGrouping<MethodInfo, Type>> groups)
        {
            TypeBuilder impl_type_builder = CodeEmitModule.DefineType("XLuaGenDelegateImpl" + (genID++), TypeAttributes.Public, typeof(DelegateBridge));

            MethodBuilder get_deleate_by_type = impl_type_builder.DefineMethod("GetDelegateByType", MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot
                    | MethodAttributes.Virtual
                    | MethodAttributes.Final,
                    typeof(System.Delegate), new Type[] { typeof(System.Type) });

            ILGenerator get_deleate_by_type_il = get_deleate_by_type.GetILGenerator();

            foreach (var group in groups)
            {
                var to_be_impl = group.Key;

                var method_builder = defineImplementMethod(impl_type_builder, to_be_impl, to_be_impl.Attributes, "__Gen_Delegate_Imp" + (genID++));

                emitMethodImpl(to_be_impl, method_builder.GetILGenerator(), false);

                foreach(var dt in group)
                {
                    Label end_of_if = get_deleate_by_type_il.DefineLabel();
                    get_deleate_by_type_il.Emit(OpCodes.Ldarg_1);
                    get_deleate_by_type_il.Emit(OpCodes.Ldtoken, dt);
                    get_deleate_by_type_il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
                    get_deleate_by_type_il.Emit(OpCodes.Bne_Un, end_of_if);

                    get_deleate_by_type_il.Emit(OpCodes.Ldarg_0);
                    get_deleate_by_type_il.Emit(OpCodes.Ldftn, method_builder);
                    get_deleate_by_type_il.Emit(OpCodes.Newobj, dt.GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));
                    get_deleate_by_type_il.Emit(OpCodes.Ret);
                    get_deleate_by_type_il.MarkLabel(end_of_if);
                }
            }

            // Constructor
            var ctor_param_types = new Type[] { typeof(int), typeof(LuaEnv) };
            ConstructorInfo parent_ctor = typeof(DelegateBridge).GetConstructor(ctor_param_types);
            var ctor_builder = impl_type_builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, ctor_param_types);
            var ctor_il = ctor_builder.GetILGenerator();
            ctor_il.Emit(OpCodes.Ldarg_0);
            ctor_il.Emit(OpCodes.Ldarg_1);
            ctor_il.Emit(OpCodes.Ldarg_2);
            ctor_il.Emit(OpCodes.Call, parent_ctor);
            ctor_il.Emit(OpCodes.Ret);

            // end of GetDelegateByType
            get_deleate_by_type_il.Emit(OpCodes.Ldnull);
            get_deleate_by_type_il.Emit(OpCodes.Ret);

            impl_type_builder.DefineMethodOverride(get_deleate_by_type, typeof(DelegateBridgeBase).GetMethod("GetDelegateByType"));


            return impl_type_builder.CreateType();
        }

        // luaResult: the value is a result C# reads from Lua (a bridge return
        // or out value), not an argument: integers use xlua_get* (fixCaster).
        private void EmitGetObject(ILGenerator il, int offset, Type type, LocalBuilder L, LocalBuilder translator, LocalBuilder offsetBase, bool isParam = false, bool luaResult = false)
        {
            if (!fixCaster.ContainsKey(type) && !typedCaster.ContainsKey(type))
            {
                il.Emit(OpCodes.Ldloc, translator); // translator
            }
            il.Emit(OpCodes.Ldloc, L); // L
            if (offsetBase != null)
            {
                il.Emit(OpCodes.Ldloc, offsetBase); // err_func
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, offset);
            }

            MethodInfo caster;

            if (!luaResult && integerArgCaster.TryGetValue(type, out caster))
            {
                il.Emit(OpCodes.Call, caster);
            }
            else if (fixCaster.TryGetValue(type, out caster))
            {
                il.Emit(OpCodes.Call, caster);
            }
            else if (typedCaster.TryGetValue(type, out caster))
            {
                il.Emit(OpCodes.Call, caster);
                if (type == typeof(float))
                {
                    il.Emit(OpCodes.Conv_R4);
                }
                else
                {
                    throw new InvalidProgramException(type + " is not a type need cast");
                }
            }
            else if (type == typeof(decimal))
            {
                il.Emit(OpCodes.Callvirt, ObjectTranslator_GetDecimal);
            }
            else
            {
                if (isParam)
                {
                    il.Emit(OpCodes.Callvirt, ObjectTranslator_GetParams.MakeGenericMethod(new Type[] { type.GetElementType() }));
                }
                else
                {
                    il.Emit(OpCodes.Ldtoken, type);
                    il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
                    il.Emit(OpCodes.Callvirt, !luaResult && ObjectCasters.RefusesTables(type)
                        ? ObjectTranslator_GetUnityObjectArg : ObjectTranslator_GetObject);
                }
                if (type.IsValueType)
                {
                    Label not_null = il.DefineLabel();
                    Label null_done = il.DefineLabel();
                    LocalBuilder local_new = il.DeclareLocal(type);

                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue_S, not_null);

                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldloca, local_new);
                    il.Emit(OpCodes.Initobj, type);
                    il.Emit(OpCodes.Ldloc, local_new);
                    il.Emit(OpCodes.Br_S, null_done);

                    il.MarkLabel(not_null);
                    il.Emit(OpCodes.Unbox_Any, type);
                    il.MarkLabel(null_done);
                }
                else if (type != typeof(object))
                {
                    il.Emit(OpCodes.Castclass, type);
                }
            }
        }

        HashSet<Type> gen_interfaces = new HashSet<Type>();

        public void SetGenInterfaces(List<Type> gen_interfaces)
        {
            gen_interfaces.ForEach((item) =>
            {
                if (!this.gen_interfaces.Contains(item))
                {
                    this.gen_interfaces.Add(item);
                }
            });
        }

        public Type EmitInterfaceImpl(Type to_be_impl)
        {
            if (!to_be_impl.IsInterface)
            {
                throw new InvalidOperationException("interface expected, but got " + to_be_impl);
            }

            // A bridge cannot implement a generic method: Lua has no type
            // arguments to call it with. The source generator gives such an
            // interface no bridge ("'M' is a generic method"), so this is
            // checked before the CSharpCallLua list, which is empty when
            // generated bindings are registered. Before 2026-10-19 the
            // emitted type failed to load (TypeLoadException).
            foreach (var itf in new Type[] { to_be_impl }.Concat(to_be_impl.GetInterfaces()))
            {
                foreach (var method in itf.GetMethods())
                {
                    if (method.IsGenericMethodDefinition && !method.IsStatic)
                    {
                        throw new InvalidCastException(to_be_impl.GetFriendlyName() + " cannot be bridged to Lua: '"
                            + (itf == to_be_impl ? "" : itf.GetFriendlyName() + ".") + method.Name
                            + "' is a generic method, which an interface bridge cannot implement");
                    }
                }
            }

            if (!gen_interfaces.Contains(to_be_impl))
            {
                throw new InvalidCastException("This type must add to CSharpCallLua: " + to_be_impl.GetFriendlyName());
            }

            lock (emitLock)
            {
                Type impl;
                if (!interfaceImpls.TryGetValue(to_be_impl, out impl))
                {
                    impl = emitInterfaceImpl(to_be_impl);
                    interfaceImpls.Add(to_be_impl, impl);
                }
                return impl;
            }
        }

        private Type emitInterfaceImpl(Type to_be_impl)
        {
            TypeBuilder impl_type_builder = CodeEmitModule.DefineType("XLuaGenInterfaceImpl" + (genID++), TypeAttributes.Public | TypeAttributes.Class, typeof(LuaBase), new Type[] { to_be_impl});

            foreach(var member in (new Type[] { to_be_impl }.Concat(to_be_impl.GetInterfaces()).SelectMany(i=> i.GetMembers())))
            {
                if (member.MemberType == MemberTypes.Method)
                {
                    MethodInfo method = member as MethodInfo;
                    // Property and event accessors are emitted with their
                    // property or event. An ordinary method may also be
                    // called get_X or add_X; skipping those by name left them
                    // unimplemented and the type failed to load.
                    if (method.IsSpecialName)
                    {
                        continue;
                    }

                    var method_builder = defineImplementMethod(impl_type_builder, method,
                        MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual);

                    emitMethodImpl(method, method_builder.GetILGenerator(), true);
                }
                else if (member.MemberType == MemberTypes.Property)
                {
                    PropertyInfo property = member as PropertyInfo;
                    PropertyBuilder prop_builder = impl_type_builder.DefineProperty(property.Name, property.Attributes, property.PropertyType, Type.EmptyTypes);
                    // An indexer, whatever its name ([IndexerName]): Lua gets
                    // its accessors' names, get_Item and set_Item by default.
                    // A property called Item without parameters is a plain one.
                    if (property.GetIndexParameters().Length > 0)
                    {
                        if (property.CanRead)
                        {
                            var getter_buildler = defineImplementMethod(impl_type_builder, property.GetGetMethod(), 
                                MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                            emitMethodImpl(property.GetGetMethod(), getter_buildler.GetILGenerator(), true);
                            prop_builder.SetGetMethod(getter_buildler);
                        }
                        if (property.CanWrite)
                        {
                            var setter_buildler = defineImplementMethod(impl_type_builder, property.GetSetMethod(),
                                MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                            emitMethodImpl(property.GetSetMethod(), setter_buildler.GetILGenerator(), true);
                            prop_builder.SetSetMethod(setter_buildler);
                        }
                        continue;
                    }
                    if (property.CanRead)
                    {
                        MethodBuilder getter_buildler = impl_type_builder.DefineMethod("get_" + property.Name, 
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                            property.PropertyType, Type.EmptyTypes);

                        ILGenerator il = getter_buildler.GetILGenerator();

                        LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
                        LocalBuilder oldTop = il.DeclareLocal(typeof(int));
                        LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
                        LocalBuilder ret = il.DeclareLocal(property.PropertyType);
                        LocalBuilder[] bridgeLock = beginBridgeLock(il);

                        // L = LuaBase.L;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Callvirt, LuaBase_L_getter);
                        il.Emit(OpCodes.Stloc, L);

                        //oldTop = LuaAPI.lua_gettop(L);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Call, LuaAPI_lua_gettop);
                        il.Emit(OpCodes.Stloc, oldTop);

                        //translator = LuaBase.translator;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Callvirt, LuaBase_translator_getter);
                        il.Emit(OpCodes.Stloc, translator);

                        //LuaAPI.lua_getref(L, luaReference);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_luaReference);
                        il.Emit(OpCodes.Call, LuaAPI_lua_getref);

                        //LuaAPI.lua_pushstring(L, "xxx");
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldstr, property.Name);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                        //LuaAPI.xlua_pgettable(L, -2)
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                        il.Emit(OpCodes.Call, LuaAPI_xlua_pgettable);
                        Label gettable_no_exception = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, gettable_no_exception);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_luaEnv);
                        il.Emit(OpCodes.Ldloc, oldTop);
                        il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                        il.MarkLabel(gettable_no_exception);

                        // try { ret = <value>; } finally { LuaAPI.lua_settop(L, oldTop); }:
                        // a conversion that throws (Lua's integer rule) leaves the stack as it was.
                        il.BeginExceptionBlock();
                        EmitGetObject(il, -1, property.PropertyType, L, translator, null, false, true);
                        il.Emit(OpCodes.Stloc, ret);
                        il.BeginFinallyBlock();
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldloc, oldTop);
                        il.Emit(OpCodes.Call, LuaAPI_lua_settop);
                        il.EndExceptionBlock();
                        endBridgeLock(il, bridgeLock);

                        il.Emit(OpCodes.Ldloc, ret);
                        il.Emit(OpCodes.Ret);

                        prop_builder.SetGetMethod(getter_buildler);
                    }
                    if (property.CanWrite)
                    {
                        MethodBuilder setter_builder = impl_type_builder.DefineMethod("set_" + property.Name, 
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, 
                            null, new Type[] { property.PropertyType });

                        ILGenerator il = setter_builder.GetILGenerator();

                        LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
                        LocalBuilder oldTop = il.DeclareLocal(typeof(int));
                        LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
                        LocalBuilder[] bridgeLock = beginBridgeLock(il);

                        // L = LuaBase.L;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Callvirt, LuaBase_L_getter);
                        il.Emit(OpCodes.Stloc, L);

                        //oldTop = LuaAPI.lua_gettop(L);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Call, LuaAPI_lua_gettop);
                        il.Emit(OpCodes.Stloc, oldTop);

                        //translator = LuaBase.translator;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Callvirt, LuaBase_translator_getter);
                        il.Emit(OpCodes.Stloc, translator);

                        //LuaAPI.lua_getref(L, luaReference);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_luaReference);
                        il.Emit(OpCodes.Call, LuaAPI_lua_getref);

                        //LuaAPI.lua_pushstring(L, "xxx");
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldstr, property.Name);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                        //translator.Push(L, value);
                        emitPush(il, property.PropertyType, 1, false, L, translator, true);

                        //LuaAPI.xlua_psettable(L, -2)
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-3);
                        il.Emit(OpCodes.Call, LuaAPI_xlua_psettable);
                        Label settable_no_exception = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, settable_no_exception);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_luaEnv);
                        il.Emit(OpCodes.Ldloc, oldTop);
                        il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                        il.MarkLabel(settable_no_exception);

                        //LuaAPI.lua_pop(L, 1);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)1);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pop);
                        endBridgeLock(il, bridgeLock);

                        il.Emit(OpCodes.Ret);

                        prop_builder.SetSetMethod(setter_builder);

                    }
                }
                else if(member.MemberType == MemberTypes.Event)
                {
                    
                    EventInfo event_info = member as EventInfo;
                    EventBuilder event_builder = impl_type_builder.DefineEvent(event_info.Name, event_info.Attributes, event_info.EventHandlerType);
                    if (event_info.GetAddMethod() != null)
                    {
                        var add_buildler = defineImplementMethod(impl_type_builder, event_info.GetAddMethod(),
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                        emitMethodImpl(event_info.GetAddMethod(), add_buildler.GetILGenerator(), true);
                        event_builder.SetAddOnMethod(add_buildler);
                    }
                    if (event_info.GetRemoveMethod() != null)
                    {
                        var remove_buildler = defineImplementMethod(impl_type_builder, event_info.GetRemoveMethod(),
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                        emitMethodImpl(event_info.GetRemoveMethod(), remove_buildler.GetILGenerator(), true);
                        event_builder.SetRemoveOnMethod(remove_buildler);
                    }
                }
            }
            

            // Constructor
            var ctor_param_types = new Type[] { typeof(int), typeof(LuaEnv) };
            ConstructorInfo parent_ctor = typeof(LuaBase).GetConstructor(ctor_param_types);
            var ctor_builder = impl_type_builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, ctor_param_types);
            var ctor_il = ctor_builder.GetILGenerator();
            ctor_il.Emit(OpCodes.Ldarg_0);
            ctor_il.Emit(OpCodes.Ldarg_1);
            ctor_il.Emit(OpCodes.Ldarg_2);
            ctor_il.Emit(OpCodes.Call, parent_ctor);
            ctor_il.Emit(OpCodes.Ret);

            return impl_type_builder.CreateType();
        }

        private void emitEmptyMethod(ILGenerator il, Type returnType)
        {
            if(returnType != typeof(void))
            {
                if (returnType.IsValueType)
                {
                    LocalBuilder local_new = il.DeclareLocal(returnType);
                    il.Emit(OpCodes.Ldloca, local_new);
                    il.Emit(OpCodes.Initobj, returnType);
                    il.Emit(OpCodes.Ldloc, local_new);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
            }
            il.Emit(OpCodes.Ret);
        }

        private MethodBuilder defineImplementMethod(TypeBuilder type_builder, MethodInfo to_be_impl, MethodAttributes attributes, string methodName = null)
        {
            var parameters = to_be_impl.GetParameters();

            Type[] param_types = new Type[parameters.Length];
            Type[][] required_modifiers = new Type[parameters.Length][];
            Type[][] optional_modifiers = new Type[parameters.Length][];
            bool has_modifiers = false;
            for (int i = 0; i < parameters.Length; ++i)
            {
                param_types[i] = parameters[i].ParameterType;
                required_modifiers[i] = parameters[i].GetRequiredCustomModifiers();
                optional_modifiers[i] = parameters[i].GetOptionalCustomModifiers();
                has_modifiers = has_modifiers || required_modifiers[i].Length > 0 || optional_modifiers[i].Length > 0;
            }

            var name = methodName == null ? to_be_impl.Name : methodName;
            // An interface method with an `in` parameter carries modreq(InAttribute);
            // the override must repeat it or the type fails to load.
            var method_builder = has_modifiers
                ? type_builder.DefineMethod(name, attributes, CallingConventions.Standard, to_be_impl.ReturnType, null, null,
                    param_types, required_modifiers, optional_modifiers)
                : type_builder.DefineMethod(name, attributes, to_be_impl.ReturnType, param_types);
            for (int i = 0; i < parameters.Length; ++i)
            {
                method_builder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
            }
            return method_builder;
        }

        ConstructorInfo decimalConstructor = typeof(decimal).GetConstructor(new Type[] {
            typeof(int), typeof(int), typeof(int), typeof(bool), typeof(byte)
        });

        private void emitLiteralLoad(ILGenerator il, Type type, object obj, int localIndex)
        {
            Type nullableOf = Nullable.GetUnderlyingType(type);
            if (!type.IsValueType && ReferenceEquals(obj, null))
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (nullableOf != null && obj != null && !(obj is Missing) && nullableOf != typeof(IntPtr)
                && nullableOf != typeof(UIntPtr) && (nullableOf.IsPrimitive || nullableOf.IsEnum() || nullableOf == typeof(decimal)))
            {
                // `int? x = 0`: the default is a boxed int (Tencent/xLua#991).
                emitLiteralLoad(il, nullableOf, obj, localIndex);
                il.Emit(OpCodes.Newobj, type.GetConstructor(new Type[] { nullableOf }));
            }
            else if(type.IsPrimitive || type.IsEnum())
            {
                if (type.IsEnum())
                {
                    type = Enum.GetUnderlyingType(type);
                }
                if (typeof(bool) == type)
                {
                    if ((bool)obj == true)
                    {
                        il.Emit(OpCodes.Ldc_I4_1);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4_0);
                    }
                }
                else if (typeof(uint) == type)
                {
                    il.Emit(OpCodes.Ldc_I4, (int)Convert.ToUInt32(obj));
                }
                else if(typeof(byte) == type || typeof(sbyte) == type || typeof(short) == type ||
                    typeof(ushort) == type || typeof(int) == type || typeof(char) == type)
                {
                    il.Emit(OpCodes.Ldc_I4, Convert.ToInt32(obj));
                }
                else if (typeof(long) == type)
                {
                    il.Emit(OpCodes.Ldc_I8, Convert.ToInt64(obj));
                }
                else if (typeof(ulong) == type)
                {
                    il.Emit(OpCodes.Ldc_I8, (long)Convert.ToUInt64(obj));
                }
                else if (typeof(IntPtr) == type || typeof(UIntPtr) == type)
                {
                    il.Emit(OpCodes.Ldloca, localIndex);
                    il.Emit(OpCodes.Initobj, type);
                    il.Emit(OpCodes.Ldloc, localIndex);
                }
                else if (typeof(float) == type)
                {
                    il.Emit(OpCodes.Ldc_R4, Convert.ToSingle(obj));
                }
                else if (typeof(double) == type)
                {
                    il.Emit(OpCodes.Ldc_R8, Convert.ToDouble(obj));
                }
                else
                {
                    throw new Exception(type + " is not primitive or enum!");
                }
            }
            else if (type == typeof(string))
            {
                il.Emit(OpCodes.Ldstr, obj as string);
            }
            else if (type == typeof(decimal))
            {
                var buffer = decimal.GetBits(Convert.ToDecimal(obj));
                il.Emit(OpCodes.Ldc_I4, buffer[0]);
                il.Emit(OpCodes.Ldc_I4, buffer[1]);
                il.Emit(OpCodes.Ldc_I4, buffer[2]);
                //UnityEngine.Debug.Log(string.Format("{0}.{1}.{2}.{3}--{4}", buffer[0], buffer[1], buffer[2], buffer[3], obj));
                il.Emit(OpCodes.Ldc_I4, (buffer[3] & 0x80000000) == 0 ? 0 : 1);
                il.Emit(OpCodes.Ldc_I4, (buffer[3] >> 16) & 0xFF);
                il.Emit(OpCodes.Newobj, decimalConstructor);
            }
            else if (type.IsValueType) 
            {
                il.Emit(OpCodes.Ldloca, localIndex);
                il.Emit(OpCodes.Initobj, type);
                il.Emit(OpCodes.Ldloc, localIndex);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        // Under THREAD_SAFE or HOTFIX_ENABLE every emitted bridge body (delegate
        // bridges, interface methods, property and indexer accessors, event
        // add and remove) runs in lock (BindingApi.Lock(this)) { ... }, as the
        // source generator's bridges and the hand-written DelegateBridge
        // methods do, so that threads calling Lua through emitted bridges of
        // one env are serialized. Before 2026-10-19 the emitted bridges took
        // no lock. The IL is the C# compiler's for lock: Monitor.Enter(o, ref
        // taken) inside try, Monitor.Exit(o) in finally when taken.
#if THREAD_SAFE || HOTFIX_ENABLE
        private static readonly MethodInfo BindingApi_Lock = typeof(XLua.Binding.BindingApi).GetMethod("Lock");
        private static readonly MethodInfo Monitor_Enter = typeof(System.Threading.Monitor).GetMethod("Enter",
            new Type[] { typeof(object), typeof(bool).MakeByRefType() });
        private static readonly MethodInfo Monitor_Exit = typeof(System.Threading.Monitor).GetMethod("Exit", new Type[] { typeof(object) });
#endif

        // Opens the lock at the start of a bridge body and returns its locals
        // (the lock object, taken); null without the defines.
        private static LocalBuilder[] beginBridgeLock(ILGenerator il)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            LocalBuilder target = il.DeclareLocal(typeof(object));
            LocalBuilder taken = il.DeclareLocal(typeof(bool));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, BindingApi_Lock);
            il.Emit(OpCodes.Stloc, target);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(OpCodes.Ldloca, taken);
            il.Emit(OpCodes.Call, Monitor_Enter);
            return new LocalBuilder[] { target, taken };
#else
            return null;
#endif
        }

        // Closes it before the final ldloc ret; ret, which cannot be in the try.
        private static void endBridgeLock(ILGenerator il, LocalBuilder[] locals)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            il.BeginFinallyBlock();
            Label notTaken = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, locals[1]);
            il.Emit(OpCodes.Brfalse_S, notTaken);
            il.Emit(OpCodes.Ldloc, locals[0]);
            il.Emit(OpCodes.Call, Monitor_Exit);
            il.MarkLabel(notTaken);
            il.EndExceptionBlock();
#endif
        }

        // lua_checkstack(L, count + the length of the params array argument, if
        // any, + Utils.PushSlack); throws LuaException when Lua has no room
        // (Tencent/xLua#1153).
        private void emitEnsureStack(ILGenerator il, LocalBuilder L, int count, short paramsArg, string what)
        {
            il.Emit(OpCodes.Ldloc, L);
            il.Emit(OpCodes.Ldc_I4, count + Utils.PushSlack);
            if (paramsArg >= 0)
            {
                Label noArray = il.DefineLabel();
                il.Emit(OpCodes.Ldarg, paramsArg);
                il.Emit(OpCodes.Brfalse, noArray);
                il.Emit(OpCodes.Ldarg, paramsArg);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Add);
                il.MarkLabel(noArray);
            }
            il.Emit(OpCodes.Call, LuaAPI_lua_checkstack);
            Label hasRoom = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, hasRoom);
            il.Emit(OpCodes.Ldstr, "Lua stack overflow: no room for " + what);
            il.Emit(OpCodes.Newobj, typeof(LuaException).GetConstructor(new Type[] { typeof(string) }));
            il.Emit(OpCodes.Throw);
            il.MarkLabel(hasRoom);
        }

        private void emitMethodImpl(MethodInfo to_be_impl, ILGenerator il, bool isObj)
        {
            var parameters = to_be_impl.GetParameters();

            LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));//RealStatePtr L;  0
            LocalBuilder err_func = il.DeclareLocal(typeof(int));//int err_func; 1
            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));//ObjectTranslator translator; 2
            LocalBuilder ret = null;
            bool has_return = to_be_impl.ReturnType != typeof(void);
            if (has_return)
            {
                ret = il.DeclareLocal(to_be_impl.ReturnType); //ReturnType ret; 3
            }
            LocalBuilder[] bridgeLock = beginBridgeLock(il);

            // L = LuaBase.L;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, LuaBase_L_getter);
            il.Emit(OpCodes.Stloc, L);

            // Up to four values besides the arguments; more than
            // Utils.UncheckedPushes need room first (Tencent/xLua#1153).
            int fixedPushes = 4;
            bool hasParamArray = false;
            foreach (var pinfo in parameters)
            {
                if (pinfo.IsOut) continue;
                if (pinfo.IsDefined(typeof(ParamArrayAttribute), false)) hasParamArray = true;
                else fixedPushes++;
            }
            if (hasParamArray || fixedPushes > Utils.UncheckedPushes)
            {
                emitEnsureStack(il, L, fixedPushes, hasParamArray ? (short)parameters.Length : (short)-1,
                    "the arguments of " + to_be_impl.Name);
            }

            //err_func =LuaAPI.load_error_func(L, errorFuncRef);
            il.Emit(OpCodes.Ldloc, L);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, DelegateBridgeBase_errorFuncRef_getter);
            il.Emit(OpCodes.Call, LuaAPI_load_error_func);
            il.Emit(OpCodes.Stloc, err_func);

            //translator = LuaBase.translator;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, LuaBase_translator_getter);
            il.Emit(OpCodes.Stloc, translator);

            //LuaAPI.lua_getref(L, luaReference);
            il.Emit(OpCodes.Ldloc, L);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, LuaBase_luaReference);
            il.Emit(OpCodes.Call, LuaAPI_lua_getref);

            if (isObj)
            {
                //LuaAPI.lua_pushstring(L, "xxx");
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldstr, to_be_impl.Name);
                il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                //LuaAPI.xlua_pgettable(L, -2)
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                il.Emit(OpCodes.Call, LuaAPI_xlua_pgettable);
                Label gettable_no_exception = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, gettable_no_exception);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, LuaBase_luaEnv);
                il.Emit(OpCodes.Ldloc, err_func);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                il.MarkLabel(gettable_no_exception);

                // if (!LuaAPI.lua_isfunction(L, -1)) raise "no such function <name>",
                // as the generated and source-generated bridges do (before
                // 2026-10-19 the pcall raised "attempt to call a nil value").
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Call, LuaAPI_lua_isfunction);
                Label is_function = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, is_function);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldstr, "no such function " + to_be_impl.Name);
                il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, LuaBase_luaEnv);
                il.Emit(OpCodes.Ldloc, err_func);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                il.MarkLabel(is_function);

                //LuaAPI.lua_pushvalue(L, -2);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                il.Emit(OpCodes.Call, LuaAPI_lua_pushvalue);

                //LuaAPI.lua_remove(L, -3);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)-3);
                il.Emit(OpCodes.Call, LuaAPI_lua_remove);
            }

            int in_param_count = 0;
            int out_param_count = 0;
            bool has_params = false;
            //translator.PushAny(L, param_in)
            for (int i = 0; i < parameters.Length; ++i)
            {
                var pinfo = parameters[i];
                if (!pinfo.IsOut)
                {
                    var ptype = pinfo.ParameterType;
                    bool isParam = pinfo.IsDefined(typeof(ParamArrayAttribute), false);
                    emitPush(il, ptype, (short)(i + 1), isParam, L, translator, true);
                    if (isParam)
                    {
                        has_params = true;
                    }
                    else
                    {
                        ++in_param_count;
                    }
                }

                if (pinfo.ParameterType.IsByRef && !Utils.IsInParameter(pinfo))
                {
                    ++out_param_count;
                }
            }

            il.Emit(OpCodes.Ldloc, L);
            il.Emit(OpCodes.Ldc_I4, in_param_count + (isObj ? 1 : 0));
            if (has_params)
            {
                Label l1 = il.DefineLabel();

                il.Emit(OpCodes.Ldarg, (short)parameters.Length);
                il.Emit(OpCodes.Brfalse, l1);

                il.Emit(OpCodes.Ldarg, (short)parameters.Length);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Add);
                il.MarkLabel(l1);
            }
            il.Emit(OpCodes.Ldc_I4, out_param_count + (has_return ? 1 : 0));
            il.Emit(OpCodes.Ldloc, err_func);
            il.Emit(OpCodes.Call, LuaAPI_lua_pcall);
            Label no_exception = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, no_exception);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, LuaBase_luaEnv);
            il.Emit(OpCodes.Ldloc, err_func);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
            il.MarkLabel(no_exception);

            // The results are read in try { } finally { LuaAPI.lua_settop(L, err_func - 1); },
            // so a conversion that throws (Lua's integer rule) leaves the stack as it was.
            bool has_results = has_return || out_param_count > 0;
            if (has_results)
            {
                il.BeginExceptionBlock();
            }
            int offset = 1;
            if (has_return)
            {
                EmitGetObject(il, offset++, to_be_impl.ReturnType, L, translator, err_func, false, true);
                il.Emit(OpCodes.Stloc, ret);
            }

            for (int i = 0; i < parameters.Length; ++i)
            {
                var pinfo = parameters[i];
                var ptype = pinfo.ParameterType;
                if (ptype.IsByRef && !Utils.IsInParameter(pinfo))
                {
                    il.Emit(OpCodes.Ldarg, (short)(i + 1));
                    var pelemtype = ptype.GetElementType();
                    EmitGetObject(il, offset++, pelemtype, L, translator, err_func, false, true);
                    if (pelemtype.IsValueType)
                    {
                        il.Emit(OpCodes.Stobj, pelemtype);
                    }
                    else
                    {
                        il.Emit(OpCodes.Stind_Ref);
                    }

                }
            }

            if (has_results)
            {
                il.BeginFinallyBlock();
            }

            //LuaAPI.lua_settop(L, err_func - 1);
            il.Emit(OpCodes.Ldloc, L);
            il.Emit(OpCodes.Ldloc, err_func);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Call, LuaAPI_lua_settop);

            if (has_results)
            {
                il.EndExceptionBlock();
            }
            endBridgeLock(il, bridgeLock);

            if (has_return)
            {
                il.Emit(OpCodes.Ldloc, ret);
            }

            il.Emit(OpCodes.Ret);
        }

        private static readonly MethodInfo ObjectTranslatorPool_FindTranslator = typeof(ObjectTranslatorPool).GetMethod("FindTranslator");
        private Type[] parameterTypeOfWrap = new Type[] { typeof(RealStatePtr) };
        private static readonly MethodInfo ObjectTranslator_Assignable = typeof(ObjectTranslator).GetMethod("Assignable", new Type[] { typeof(RealStatePtr),
               typeof(int), typeof(Type)});

        private static readonly MethodInfo Utils_BeginObjectRegister = typeof(Utils).GetMethod("BeginObjectRegister");
        private static readonly MethodInfo Utils_EndObjectRegister = typeof(Utils).GetMethod("EndObjectRegister");
        private static readonly MethodInfo Utils_BeginClassRegister = typeof(Utils).GetMethod("BeginClassRegister");
        private static readonly MethodInfo Utils_EndClassRegister = typeof(Utils).GetMethod("EndClassRegister");
        private static readonly MethodInfo Utils_RegisterFunc = typeof(Utils).GetMethod("RegisterFunc");
        private static readonly MethodInfo Utils_RegisterObject = typeof(Utils).GetMethod("RegisterObject");

        private static readonly ConstructorInfo LuaCSFunction_Constructor = typeof(LuaCSFunction).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) });

        private static readonly MethodInfo String_Concat = typeof(string).GetMethod("Concat", new Type[] { typeof(object), typeof(object) });

        void checkType(ILGenerator il, Type type, LocalBuilder translator, int argPos, Label endOfBlock, bool isVParam, bool isDefault)
        {
            Label endOfCheckType = il.DefineLabel();

            if (isVParam || isDefault)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, argPos);
                il.Emit(OpCodes.Call, LuaAPI_lua_type);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Beq, endOfCheckType);
            }

            il.Emit(OpCodes.Ldloc, translator);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, argPos);
            il.Emit(OpCodes.Ldtoken, isVParam ? type.GetElementType() : type);
            il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(DeclaringType)
            il.Emit(OpCodes.Callvirt, ObjectTranslator_Assignable);
            il.Emit(OpCodes.Brfalse, endOfBlock);

            il.MarkLabel(endOfCheckType);
        }

        void emitRegisterFunc(ILGenerator il, MethodBuilder method, int index, string name)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, method);
            il.Emit(OpCodes.Newobj, LuaCSFunction_Constructor);
            il.Emit(OpCodes.Call, Utils_RegisterFunc);
        }

        void emitCatchBlock(ILGenerator il, LocalBuilder ex, LocalBuilder wrapRet, Label retPoint, Label exceptionBlock)
        {
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Stloc, ex);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "c# exception:");
            il.Emit(OpCodes.Ldloc, ex);
            il.Emit(OpCodes.Call, String_Concat);
            il.Emit(OpCodes.Call, LuaAPI_luaL_error);
            il.Emit(OpCodes.Stloc, wrapRet);
            il.Emit(OpCodes.Leave, retPoint);
            il.Emit(OpCodes.Leave, exceptionBlock);

            il.EndExceptionBlock();
        }

        public MethodBuilder emitFieldWrap(TypeBuilder typeBuilder, FieldInfo field, bool genGetter)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(field.Name + (genID++), MethodAttributes.Static, typeof(int), parameterTypeOfWrap);
            methodBuilder.DefineParameter(1, ParameterAttributes.None, "L");

            ILGenerator il = methodBuilder.GetILGenerator();

            LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
            LocalBuilder fieldStore = il.DeclareLocal(field.FieldType);
            LocalBuilder wrapRet = il.DeclareLocal(typeof(int));
            LocalBuilder ex = il.DeclareLocal(typeof(Exception));

            Label exceptionBlock = il.BeginExceptionBlock();
            Label retPoint = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, L);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ObjectTranslatorPool_FindTranslator);
            il.Emit(OpCodes.Stloc, translator);

            if (genGetter)
            {
                if (!field.IsStatic)
                {
                    EmitGetObject(il, 1, field.DeclaringType, L, translator, null);
                    il.Emit(OpCodes.Ldfld, field);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, field);
                }
                il.Emit(OpCodes.Stloc, fieldStore);
                emitPush(il, field.FieldType, 2, false, L, translator, false);
            }
            else
            {
                if (!field.IsStatic)
                {
                    EmitGetObject(il, 1, field.DeclaringType, L, translator, null);
                    LocalBuilder self = null;
                    if (field.DeclaringType.IsValueType)
                    {
                        self = il.DeclareLocal(field.DeclaringType);
                        il.Emit(OpCodes.Stloc, self);
                        il.Emit(OpCodes.Ldloca, self);
                    }
                    EmitGetObject(il, 2, field.FieldType, L, translator, null);
                    il.Emit(OpCodes.Stfld, field);
                    if (self != null)
                    {
                        emitUpdateIfNeeded(il, L, translator, field.DeclaringType, 1, self.LocalIndex);
                    }
                }
                else
                {
                    EmitGetObject(il, 1, field.FieldType, L, translator, null);
                    il.Emit(OpCodes.Stsfld, field);
                }
            }

            il.Emit(OpCodes.Leave, exceptionBlock);

            emitCatchBlock(il, ex, wrapRet, retPoint, exceptionBlock);

            il.Emit(genGetter ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(retPoint);
            il.Emit(OpCodes.Ldloc, wrapRet);
            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        public MethodBuilder emitPropertyWrap(TypeBuilder typeBuilder, PropertyInfo prop, MethodInfo op, bool genGetter)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(prop.Name + (genID++), MethodAttributes.Static, typeof(int), parameterTypeOfWrap);
            methodBuilder.DefineParameter(1, ParameterAttributes.None, "L");

            ILGenerator il = methodBuilder.GetILGenerator();

            LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
            LocalBuilder propStore = il.DeclareLocal(prop.PropertyType);
            LocalBuilder wrapRet = il.DeclareLocal(typeof(int));
            LocalBuilder ex = il.DeclareLocal(typeof(Exception));

            Label exceptionBlock = il.BeginExceptionBlock();
            Label retPoint = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, L);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ObjectTranslatorPool_FindTranslator);
            il.Emit(OpCodes.Stloc, translator);

            if (genGetter)
            {
                if (!op.IsStatic)
                {
                    EmitGetObject(il, 1, prop.DeclaringType, L, translator, null);
                    if (prop.DeclaringType.IsValueType)
                    {
                        var self = il.DeclareLocal(prop.DeclaringType);
                        il.Emit(OpCodes.Stloc, self);
                        il.Emit(OpCodes.Ldloca, self);
                        il.Emit(OpCodes.Call, op);
                        emitUpdateIfNeeded(il, L, translator, prop.DeclaringType, 1, self.LocalIndex);
                    }
                    else
                    {
                        il.Emit(OpCodes.Callvirt, op);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Call, op);
                }
                il.Emit(OpCodes.Stloc, propStore);
                emitPush(il, prop.PropertyType, (short)propStore.LocalIndex, false, L, translator, false);
            }
            else
            {
                if (!op.IsStatic)
                {
                    EmitGetObject(il, 1, prop.DeclaringType, L, translator, null);
                    LocalBuilder self = null;
                    if (prop.DeclaringType.IsValueType)
                    {
                        self = il.DeclareLocal(prop.DeclaringType);
                        il.Emit(OpCodes.Stloc, self);
                        il.Emit(OpCodes.Ldloca, self);
                    }
                    EmitGetObject(il, 2, prop.PropertyType, L, translator, null);
                    il.Emit(prop.DeclaringType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, op);
                    if (self != null)
                    {
                        emitUpdateIfNeeded(il, L, translator, prop.DeclaringType, 1, self.LocalIndex);
                    }
                }
                else
                {
                    EmitGetObject(il, 1, prop.PropertyType, L, translator, null);
                    il.Emit(OpCodes.Call, op);
                }
            }

            il.Emit(OpCodes.Leave, exceptionBlock);

            emitCatchBlock(il, ex, wrapRet, retPoint, exceptionBlock);

            il.Emit(genGetter ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(retPoint);
            il.Emit(OpCodes.Ldloc, wrapRet);
            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        static bool isMemberInBlackList(MemberInfo mb)
        {
            if (mb is FieldInfo && Utils.IsUnsupportedType((mb as FieldInfo).FieldType)) return true;
            if (mb is PropertyInfo && Utils.IsUnsupportedType((mb as PropertyInfo).PropertyType)) return true;
            if (mb is PropertyInfo && (mb as PropertyInfo).GetIndexParameters().Any(p => Utils.IsUnsupportedType(p.ParameterType))) return true;
            if (mb is MethodBase && Utils.HasUnsupportedType(mb as MethodBase)) return true;

            if (mb.IsDefined(typeof(ObsoleteAttribute), false)) return true;

            // [BlackList] on the member, entries and filters, as in the generator.
            return BlackListRules.IsListed(mb);
        }

        static bool isMethodInBlackList(MethodBase mb)
        {
            if (Utils.HasUnsupportedType(mb)) return true;

            if (mb.IsDefined(typeof(ObsoleteAttribute), false)) return true;

            return BlackListRules.IsListedMethod(mb);
        }

        public Type EmitTypeWrap(Type toBeWrap)
        {
            lock (emitLock)
            {
                var extensionMethods = Utils.GetExtensionMethodsOf(toBeWrap);
                KeyValuePair<IEnumerable<MethodInfo>, Type> cached;
                if (typeWraps.TryGetValue(toBeWrap, out cached) && ReferenceEquals(cached.Key, extensionMethods))
                {
                    return cached.Value;
                }
                var wrap = emitTypeWrap(toBeWrap, extensionMethods);
                typeWraps[toBeWrap] = new KeyValuePair<IEnumerable<MethodInfo>, Type>(extensionMethods, wrap);
                return wrap;
            }
        }

        private Type emitTypeWrap(Type toBeWrap, IEnumerable<MethodInfo> extensionMethods)
        {
            TypeBuilder wrapTypeBuilder = CodeEmitModule.DefineType(toBeWrap.Name + "Wrap" + (genID++), TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

            var methodBuilder = wrapTypeBuilder.DefineMethod("__Register", MethodAttributes.Static | MethodAttributes.Public, null, parameterTypeOfWrap);
            methodBuilder.DefineParameter(1, ParameterAttributes.None, "L");

            ILGenerator il = methodBuilder.GetILGenerator();

            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ObjectTranslatorPool_FindTranslator);
            il.Emit(OpCodes.Stloc, translator);

            var instanceFlag = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var staticFlag = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

            var instanceFields = toBeWrap.GetFields(instanceFlag).Where(m => !isMemberInBlackList(m));
            var instanceProperties = toBeWrap.GetProperties(instanceFlag).Where(m => !isMemberInBlackList(m));
            extensionMethods = (extensionMethods == null) ? Enumerable.Empty<MethodInfo>() : extensionMethods.Where(m => !isMethodInBlackList(m));
            var instanceMethods = toBeWrap.GetMethods(instanceFlag)
                .Where(m => !isMethodInBlackList(m))
                .Concat(extensionMethods)
                .Where(m => Utils.IsSupportedMethod(m))
                .Where(m => !m.IsSpecialName
                    || (
                         ((m.Name == "get_Item" && m.GetParameters().Length == 1) || (m.Name == "set_Item" && m.GetParameters().Length == 2))
                         && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(string))
                       )
                ).GroupBy(m => m.Name).ToList();
            var supportOperators = toBeWrap.GetMethods(staticFlag)
                .Where(m => !isMethodInBlackList(m))
                .Where(m => m.IsSpecialName && InternalGlobals.supportOp.ContainsKey(m.Name))
                .GroupBy(m => m.Name);

            //begin obj
            il.Emit(OpCodes.Ldtoken, toBeWrap);
            il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, translator);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Call, Utils_BeginObjectRegister);

            foreach(var field in instanceFields)
            {
                emitRegisterFunc(il, emitFieldWrap(wrapTypeBuilder, field, true), Utils.GETTER_IDX, field.Name);
                emitRegisterFunc(il, emitFieldWrap(wrapTypeBuilder, field, false), Utils.SETTER_IDX, field.Name);
            }

            List<MethodBase> itemGetter = new List<MethodBase>();
            List<MethodBase> itemSetter = new List<MethodBase>();

            foreach(var prop in instanceProperties)
            {
                var getter = prop.GetGetMethod();
                if (getter != null && getter.IsPublic && !BlackListRules.IsListed(getter))
                {
                    if (prop.GetIndexParameters().Length > 0)
                    {
                        if (!prop.GetIndexParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
                        {
                            itemGetter.Add(getter);
                        }
                    }
                    else
                    {
                        emitRegisterFunc(il, emitPropertyWrap(wrapTypeBuilder, prop, getter, true), Utils.GETTER_IDX, prop.Name);
                    }
                }

                var setter = prop.GetSetMethod();
                if (setter != null && setter.IsPublic && !BlackListRules.IsListed(setter))
                {
                    if (prop.GetIndexParameters().Length > 0)
                    {
                        if (!prop.GetIndexParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
                        {
                            itemSetter.Add(setter);
                        }
                    }
                    else
                    {
                        emitRegisterFunc(il, emitPropertyWrap(wrapTypeBuilder, prop, setter, false), Utils.SETTER_IDX, prop.Name);
                    }
                }
            }

            foreach (var group in instanceMethods)
            {
                emitRegisterFunc(il, emitMethodWrap(wrapTypeBuilder, group.Cast<MethodBase>().ToList(), false, toBeWrap), Utils.METHOD_IDX, group.Key);
            }

            foreach (var group in supportOperators)
            {
                emitRegisterFunc(il, emitMethodWrap(wrapTypeBuilder, group.Cast<MethodBase>().ToList(), false, toBeWrap), Utils.OBJ_META_IDX, InternalGlobals.supportOp[group.Key]);
            }

            foreach (var ev in toBeWrap.GetEvents(instanceFlag).Where(m => !isMemberInBlackList(m)))
            {
                emitRegisterFunc(il, emitEventWrap(wrapTypeBuilder, ev), Utils.METHOD_IDX, ev.Name);
            }

            //end obj
            il.Emit(OpCodes.Ldtoken, toBeWrap);
            il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, translator);
            il.Emit(OpCodes.Ldnull);
            if (itemGetter.Count > 0)
            {
                il.Emit(OpCodes.Ldftn, emitMethodWrap(wrapTypeBuilder, itemGetter, true, toBeWrap));
                il.Emit(OpCodes.Newobj, LuaCSFunction_Constructor);
            }
            il.Emit(OpCodes.Ldnull);
            if (itemSetter.Count > 0)
            {
                il.Emit(OpCodes.Ldftn, emitMethodWrap(wrapTypeBuilder, itemSetter, true, toBeWrap));
                il.Emit(OpCodes.Newobj, LuaCSFunction_Constructor);
            }
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, Utils_EndObjectRegister);

            // begin class
            il.Emit(OpCodes.Ldtoken, toBeWrap);
            il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, emitMethodWrap(wrapTypeBuilder,
                toBeWrap.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                .Where(m => !isMethodInBlackList(m))
                .Cast<MethodBase>().ToList(), false, toBeWrap, toBeWrap.ToString() + " constructor"));
            il.Emit(OpCodes.Newobj, LuaCSFunction_Constructor);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, Utils_BeginClassRegister);

            var staticMethods = toBeWrap.GetMethods(staticFlag)
                .Where(m => !isMethodInBlackList(m))
                .Where(m => Utils.IsSupportedMethod(m))
                .Where(m => !m.IsSpecialName).GroupBy(m => m.Name);

            var staticFields = toBeWrap.GetFields(staticFlag).Where(m => !isMemberInBlackList(m));

            var staticProperties = toBeWrap.GetProperties(staticFlag).Where(m => !isMemberInBlackList(m));

            foreach (var group in staticMethods)
            {
                emitRegisterFunc(il, emitMethodWrap(wrapTypeBuilder, group.Cast<MethodBase>().ToList(), false, toBeWrap), Utils.CLS_IDX, group.Key);
            }

            foreach (var ev in toBeWrap.GetEvents(staticFlag).Where(m => !isMemberInBlackList(m)))
            {
                emitRegisterFunc(il, emitEventWrap(wrapTypeBuilder, ev), Utils.CLS_IDX, ev.Name);
            }

            foreach (var prop in staticProperties)
            {
                var getter = prop.GetGetMethod();
                if (getter != null && getter.IsPublic && !BlackListRules.IsListed(getter))
                {
                    emitRegisterFunc(il, emitPropertyWrap(wrapTypeBuilder, prop, getter, true), Utils.GETTER_IDX, prop.Name);
                }

                var setter = prop.GetSetMethod();
                if (setter != null && setter.IsPublic && !BlackListRules.IsListed(setter))
                {
                    emitRegisterFunc(il, emitPropertyWrap(wrapTypeBuilder, prop, setter, false), Utils.SETTER_IDX, prop.Name);
                }
            }

            foreach (var field in staticFields)
            {
                if (field.IsInitOnly || field.IsLiteral)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldloc, translator);
                    il.Emit(OpCodes.Ldc_I4, Utils.CLS_IDX);
                    il.Emit(OpCodes.Ldstr, field.Name);
                    if (field.IsLiteral)
                    {
                        LocalBuilder literalStore = il.DeclareLocal(field.FieldType);
                        emitLiteralLoad(il, field.FieldType, field.GetValue(null), literalStore.LocalIndex);
                        il.Emit(OpCodes.Stloc, literalStore);
                        il.Emit(OpCodes.Ldloc, literalStore);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldsfld, field);
                    }
                    if (field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Box, field.FieldType);
                    }
                    il.Emit(OpCodes.Call, Utils_RegisterObject);
                }
                else
                {
                    emitRegisterFunc(il, emitFieldWrap(wrapTypeBuilder, field, true), Utils.CLS_GETTER_IDX, field.Name);
                    emitRegisterFunc(il, emitFieldWrap(wrapTypeBuilder, field, false), Utils.CLS_SETTER_IDX, field.Name);
                }
            }

            //end class
            il.Emit(OpCodes.Ldtoken, toBeWrap);
            il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, translator);
            il.Emit(OpCodes.Call, Utils_EndClassRegister);

            il.Emit(OpCodes.Ret);

            return wrapTypeBuilder.CreateType();
        }

        MethodBuilder emitEventWrap(TypeBuilder typeBuilder, EventInfo ev)
        {
            var addEvent = ev.GetAddMethod();
            var removeEvent = ev.GetRemoveMethod();

            if (addEvent == null && removeEvent == null)
            {
                return null;
            }

            bool isStatic = addEvent != null ? addEvent.IsStatic : removeEvent.IsStatic;

            var methodBuilder = typeBuilder.DefineMethod(ev.Name + (genID++), MethodAttributes.Static, typeof(int), parameterTypeOfWrap);
            methodBuilder.DefineParameter(1, ParameterAttributes.None, "L");

            ILGenerator il = methodBuilder.GetILGenerator();

            LocalBuilder wrapRet = il.DeclareLocal(typeof(int));
            LocalBuilder ex = il.DeclareLocal(typeof(Exception));
            LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
            LocalBuilder callback = il.DeclareLocal(ev.EventHandlerType);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, L);

            Label exceptionBlock = il.BeginExceptionBlock();
            Label retPoint = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ObjectTranslatorPool_FindTranslator);
            il.Emit(OpCodes.Stloc, translator);

            EmitGetObject(il, isStatic ? 2 : 3, ev.EventHandlerType, L, translator, null);
            il.Emit(OpCodes.Stloc, callback);
            il.Emit(OpCodes.Ldloc, callback);
            Label ifBlock = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, ifBlock);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, string.Format("#{0}, need {1}", isStatic ? 2 : 3, ev.EventHandlerType));
            il.Emit(OpCodes.Call, LuaAPI_luaL_error);
            il.Emit(OpCodes.Stloc, wrapRet);
            il.Emit(OpCodes.Leave, retPoint);
            il.MarkLabel(ifBlock);

            if (addEvent != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(isStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Ldstr, "+");
                il.Emit(OpCodes.Call, LuaAPI_xlua_is_eq_str);
                ifBlock = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, ifBlock);

                LocalBuilder self = null;
                if (!isStatic)
                {
                    EmitGetObject(il, 1, ev.DeclaringType, L, translator, null);
                    if (ev.DeclaringType.IsValueType)
                    {
                        self = il.DeclareLocal(ev.DeclaringType);
                        il.Emit(OpCodes.Stloc, self);
                        il.Emit(OpCodes.Ldloca, self);
                    }
                }
                il.Emit(OpCodes.Ldloc, callback);
                il.Emit(OpCodes.Call, addEvent);
                if (self != null)
                {
                    // The handler went into a copy of the struct: store it back into the Lua value.
                    emitUpdateIfNeeded(il, L, translator, ev.DeclaringType, 1, self.LocalIndex);
                }
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Leave, retPoint);
                il.MarkLabel(ifBlock);
            }

            if (removeEvent != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(isStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Ldstr, "-");
                il.Emit(OpCodes.Call, LuaAPI_xlua_is_eq_str);
                ifBlock = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, ifBlock);

                LocalBuilder self = null;
                if (!isStatic)
                {
                    EmitGetObject(il, 1, ev.DeclaringType, L, translator, null);
                    if (ev.DeclaringType.IsValueType)
                    {
                        self = il.DeclareLocal(ev.DeclaringType);
                        il.Emit(OpCodes.Stloc, self);
                        il.Emit(OpCodes.Ldloca, self);
                    }
                }
                il.Emit(OpCodes.Ldloc, callback);
                il.Emit(OpCodes.Call, removeEvent);
                if (self != null)
                {
                    // The handler went into a copy of the struct: store it back into the Lua value.
                    emitUpdateIfNeeded(il, L, translator, ev.DeclaringType, 1, self.LocalIndex);
                }
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Leave, retPoint);
                il.MarkLabel(ifBlock);
            }

            il.Emit(OpCodes.Leave, exceptionBlock);

            emitCatchBlock(il, ex, wrapRet, retPoint, exceptionBlock);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "invalid arguments to " + ev.DeclaringType + "." + ev.Name + "!");
            il.Emit(OpCodes.Call, LuaAPI_luaL_error);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(retPoint);
            il.Emit(OpCodes.Ldloc, wrapRet);
            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        void emitUpdateIfNeeded(ILGenerator il, LocalBuilder L, LocalBuilder translator, Type type, int luaIndex, int localIndex)
        {
            if (type.IsValueType && !type.IsPrimitive && !type.IsEnum() && type != typeof(decimal))
            {
                //UnityEngine.Debug.LogWarning("-----------------emit update:" + type);
                il.Emit(OpCodes.Ldloc, translator);
                il.Emit(OpCodes.Ldloc, L);
                il.Emit(OpCodes.Ldc_I4, luaIndex);
                il.Emit(OpCodes.Ldloc, localIndex);
                il.Emit(OpCodes.Box, type);
                il.Emit(OpCodes.Callvirt, ObjectTranslator_Update);
            }
        }

        //private MethodInfo UnityEngine_Debug_Log = typeof(UnityEngine.Debug).GetMethod("Log", new Type[] { typeof(object)});
        int firstDefaultValue(MethodBase method)
        {
            var parameters = method.GetParameters();
            for(int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOptional) return i;
            }
            return -1;
        }

        MethodBuilder emitMethodWrap(TypeBuilder typeBuilder, List<MethodBase> methodsToCall, bool isIndexer, Type declaringType, string methodDesciption = null)
        {
            string wrapName = (methodsToCall.Count > 0 ? methodsToCall[0].Name : "Constructor");
            var methodBuilder = typeBuilder.DefineMethod(wrapName + (genID++), MethodAttributes.Static, typeof(int), parameterTypeOfWrap);
            methodBuilder.DefineParameter(1,  ParameterAttributes.None, "L");

            bool needCheckParameterType = (methodsToCall.Count > 1)  || isIndexer;

            if (methodsToCall.Count == 0 || methodsToCall[0].IsConstructor)
            {
                needCheckParameterType = true;
            }

            if (methodsToCall.Count == 1 && firstDefaultValue(methodsToCall[0]) != -1)
            {
                needCheckParameterType = true;
            }

            ILGenerator il = methodBuilder.GetILGenerator();

            LocalBuilder wrapRet = il.DeclareLocal(typeof(int));
            LocalBuilder ex = il.DeclareLocal(typeof(Exception));
            LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
            LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
            LocalBuilder top = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, L);

            Label exceptionBlock = il.BeginExceptionBlock();
            Label retPoint = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ObjectTranslatorPool_FindTranslator);
            il.Emit(OpCodes.Stloc, translator);

            if (needCheckParameterType)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, LuaAPI_lua_gettop);
                il.Emit(OpCodes.Stloc, top);
            }

            for (int i = 0; i < methodsToCall.Count; i++)
            {
                var method = methodsToCall[i];
                if ((method is MethodInfo) && method.ContainsGenericParameters)
                {
                    method = Utils.MakeGenericMethodWithConstraints(method as MethodInfo);
                }
                bool isStatic = method.IsStatic;
                var paramInfos = method.GetParameters();
                int minInParamCount = 0;
                int maxInParamCount = 0;
                int outParamCount = 0;
                bool hasParams = paramInfos.Length > 0 && paramInfos[paramInfos.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
                bool hasOptional = false;

                LocalBuilder methodReturn = null;

                for (int j = 0; j < paramInfos.Length; j++)
                {
                    if (!paramInfos[j].IsOut)
                    {
                        if (!paramInfos[j].IsOptional && (!hasParams || j != paramInfos.Length - 1))
                        {
                            minInParamCount++;
                        }
                        maxInParamCount++;
                    }
                    if (paramInfos[j].IsOptional)
                    {
                        hasOptional = true;
                    }
                    if (paramInfos[j].ParameterType.IsByRef && !Utils.IsInParameter(paramInfos[j]))
                    {
                        outParamCount++;
                    }
                }

                Label endOfBlock = il.DefineLabel();

                if (needCheckParameterType)
                {
                    il.Emit(OpCodes.Ldloc, top);
                    il.Emit(OpCodes.Ldc_I4, minInParamCount + (isStatic ? 0 : 1));
                    il.Emit((hasParams || hasOptional) ? OpCodes.Blt : OpCodes.Bne_Un, endOfBlock);

                    if (hasOptional && !hasParams)
                    {
                        il.Emit(OpCodes.Ldloc, top);
                        il.Emit(OpCodes.Ldc_I4, maxInParamCount + (isStatic ? 0 : 1));
                        il.Emit(OpCodes.Bgt, endOfBlock);
                    }
                    
                    if (!isStatic && !method.IsConstructor)
                    {
                        checkType(il, method.DeclaringType, translator, 1, endOfBlock, false, false);
                    }

                    int argPos = isStatic ? 1 : 2;

                    for (int j = 0; j < paramInfos.Length; j++)
                    {
                        var paramInfo = paramInfos[j];
                        if (!paramInfo.IsOut)
                        {
                            var rawParamType = paramInfo.ParameterType;
                            if (rawParamType.IsByRef)
                            {
                                rawParamType = rawParamType.GetElementType();
                            }
                            checkType(il, rawParamType, translator, argPos++, endOfBlock, 
                                hasParams && (j == paramInfos.Length - 1), paramInfo.IsOptional);
                        }
                    }
                }

                int luaPos = isStatic ? 1 : 2;

                int argStoreStart = -1;

                for (int j = 0; j < paramInfos.Length; j++)
                {
                    var paramInfo = paramInfos[j];
                    var paramRawType = paramInfo.ParameterType.IsByRef ? paramInfo.ParameterType.GetElementType() :
                        paramInfo.ParameterType;
                    var argStore = il.DeclareLocal(paramRawType);
                    if (paramInfo.IsOptional)
                    {
                        //UnityEngine.Debug.Log(paramInfo.Name + "," + paramRawType + "," + paramInfo.DefaultValue);
                        emitLiteralLoad(il, paramRawType, paramInfo.DefaultValue, argStore.LocalIndex);
                        il.Emit(OpCodes.Stloc, argStore);
                    }
                    //UnityEngine.Debug.LogWarning(declaringType.Name + "." + method.Name + "." + paramInfos[j].Name + " pos(d):" + argStore.LocalIndex + ", pt:" + paramRawType + ", j:" + j);
                    if (argStoreStart == -1)
                    {
                        argStoreStart = argStore.LocalIndex;
                    }
                    
                }
                for (int j = 0; j < paramInfos.Length; j++)
                {
                    var paramInfo = paramInfos[j];
                    var paramRawType = paramInfo.ParameterType.IsByRef ? paramInfo.ParameterType.GetElementType() :
                        paramInfo.ParameterType;
                    if (!paramInfo.IsOut)
                    {
                        Label endOfGetValue = il.DefineLabel();
                        if (paramInfo.IsOptional)
                        {
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldc_I4, luaPos);
                            il.Emit(OpCodes.Call, LuaAPI_lua_type);
                            il.Emit(OpCodes.Ldc_I4_M1);
                            il.Emit(OpCodes.Beq, endOfGetValue);
                        }
                        EmitGetObject(il, luaPos++, paramRawType, L, translator, null, hasParams && (j == paramInfos.Length - 1));
                        il.Emit(OpCodes.Stloc, argStoreStart + j);
                        il.MarkLabel(endOfGetValue);
                    }
                }

                LocalBuilder valueTypeTmp = null;

                if (!isStatic && (!method.IsConstructor || method.DeclaringType.IsValueType))
                {
                    if (!method.IsConstructor)
                    {
                        EmitGetObject(il, 1, method.DeclaringType, L, translator, null);
                    }
                    if (method.DeclaringType.IsValueType)
                    {
                        if (method.IsConstructor)
                        {
                            methodReturn = il.DeclareLocal(method.DeclaringType);
                            il.Emit(OpCodes.Ldloca, methodReturn);
                        }
                        else
                        {
                            valueTypeTmp = il.DeclareLocal(method.DeclaringType);
                            il.Emit(OpCodes.Stloc, valueTypeTmp);
                            il.Emit(OpCodes.Ldloca, valueTypeTmp);
                        }
                    }
                }

                for (int j = 0; j < paramInfos.Length; j++)
                {
                    //UnityEngine.Debug.LogWarning(declaringType.Name + "." + method.Name + "." + paramInfos[j].Name +" pos:" + (argStoreStart + j) + ", op:" + (paramInfos[j].ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc) + ", j:" + j );
                    il.Emit(paramInfos[j].ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc, argStoreStart + j);
                }

                if (method.IsConstructor)
                {
                    if (method.DeclaringType.IsValueType)
                    {
                        il.Emit(OpCodes.Call, method as ConstructorInfo);
                    }
                    else
                    {
                        il.Emit(OpCodes.Newobj, method as ConstructorInfo);
                    }
                }
                else
                {
                    il.Emit(isStatic ? OpCodes.Call : OpCodes.Callvirt, method as MethodInfo);
                }

                if (valueTypeTmp != null)
                {
                    emitUpdateIfNeeded(il, L, translator, method.DeclaringType, 1, valueTypeTmp.LocalIndex);
                }

                if (isIndexer)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Call, LuaAPI_lua_pushboolean);
                }

                bool hasReturn = false;

                MethodInfo methodInfo = method as MethodInfo;
                Type returnType = null;
                if (methodInfo == null || methodInfo.ReturnType != typeof(void))
                {
                    hasReturn = true;
                    returnType = methodInfo == null ? method.DeclaringType : methodInfo.ReturnType;
                    if (methodReturn == null)
                    {
                        methodReturn = il.DeclareLocal(returnType);
                        il.Emit(OpCodes.Stloc, methodReturn);
                    }
                }
                if (outParamCount + (hasReturn ? 1 : 0) > Utils.UncheckedPushes)
                {
                    emitEnsureStack(il, L, outParamCount + (hasReturn ? 1 : 0), -1, "the results of " + method.Name);
                }
                if (hasReturn)
                {
                    emitPush(il, returnType, (short)methodReturn.LocalIndex, false, L, translator, false);
                }

                int luaIndex = isStatic ? 1 : 2;
                for (int j = 0; j < paramInfos.Length; j++)
                {
                    if (paramInfos[j].ParameterType.IsByRef && !Utils.IsInParameter(paramInfos[j]))
                    {
                        var rawParamType = paramInfos[j].ParameterType.GetElementType();
                        emitPush(il, rawParamType,
                            (short)(argStoreStart + j), false, L, translator, false);
                        if (!paramInfos[j].IsOut)
                        {
                            emitUpdateIfNeeded(il, L, translator, rawParamType, luaIndex, argStoreStart + j);
                        }
                    }
                    if (!paramInfos[j].IsOut)
                    {
                        luaIndex++;
                    }
                }

                il.Emit(OpCodes.Ldc_I4, outParamCount + (hasReturn ? 1 : 0) + (isIndexer ? 1 : 0));
                il.Emit(OpCodes.Stloc, wrapRet);
                il.Emit(OpCodes.Leave, retPoint);
                //il.Emit(OpCodes.Ret);

                if (needCheckParameterType)
                {
                    il.MarkLabel(endOfBlock);
                }
            }

            if (declaringType.IsValueType && (methodsToCall.Count == 0 || methodsToCall[0].IsConstructor))
            {
                Label endOfBlock = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, top);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Bne_Un, endOfBlock);

                var methodReturn = il.DeclareLocal(declaringType);

                il.Emit(OpCodes.Ldloca, methodReturn);
                il.Emit(OpCodes.Initobj, declaringType);
                emitPush(il, declaringType, (short)methodReturn.LocalIndex, false, L, translator, false);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stloc, wrapRet);
                il.Emit(OpCodes.Leave_S, retPoint);
                il.MarkLabel(endOfBlock);
            }

            il.Emit(OpCodes.Leave, exceptionBlock);
            emitCatchBlock(il, ex, wrapRet, retPoint, exceptionBlock);

            if (needCheckParameterType)
            {
                if (isIndexer)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Call, LuaAPI_lua_pushboolean);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Ret);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_0);
                    if (methodDesciption == null)
                    {
                        if (methodsToCall.Count > 0)
                        {
                            methodDesciption = declaringType + "." + methodsToCall[0].Name;
                        }
                        else
                        {
                            methodDesciption = "unknow method in " + declaringType;
                        }
                    }
                    il.Emit(OpCodes.Ldstr, "invalid arguments to " + methodDesciption + "!");
                    il.Emit(OpCodes.Call, LuaAPI_luaL_error);
                    il.Emit(OpCodes.Ret);
                }
            }

            il.MarkLabel(retPoint);
            il.Emit(OpCodes.Ldloc, wrapRet);
            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }
    }
}

#endif
