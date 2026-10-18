/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// Same XLUA_CODE_EMIT condition as in CodeEmit.cs; keep the copies identical.
#if (UNITY_EDITOR || XLUA_GENERAL) && (!NET_STANDARD_2_0 || NET_STANDARD_2_1)
#define XLUA_CODE_EMIT
#endif
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
#endif

namespace XLua
{
	public enum LazyMemberTypes
	{
		Method,
		FieldGet,
		FieldSet,
		PropertyGet,
		PropertySet,
		Event,
	}

	public static partial class Utils
	{
		/// <summary>
		/// The value that Lua's getmetatable returns for a bridge object, class table or
		/// other bridge-owned table when XLUA_LOCK_METATABLES is defined.
		/// </summary>
		public const string LockedMetatable = "locked";

		/// <summary>
		/// With XLUA_LOCK_METATABLES, sets __metatable on the metatable at <paramref name="idx"/>,
		/// so that getmetatable returns <see cref="LockedMetatable"/> and setmetatable fails.
		/// The C API (lua_getmetatable, lua_setmetatable) and the debug library ignore the field,
		/// so the bridge itself keeps working. Without the define the compiler removes every call.
		/// The condition is checked where the method is called, so the preset that implies the
		/// define (Tools/presets.py) is a condition too.
		/// </summary>
		[System.Diagnostics.Conditional("XLUA_LOCK_METATABLES")]
		[System.Diagnostics.Conditional("XLUA_PRESET_MINIMAL")]
		internal static void LockMetatable(RealStatePtr L, int idx)
		{
			idx = idx > 0 ? idx : LuaAPI.lua_gettop(L) + idx + 1;
			LuaAPI.xlua_pushasciistring(L, "__metatable");
			LuaAPI.xlua_pushasciistring(L, LockedMetatable);
			LuaAPI.lua_rawset(L, idx);
		}

		public static bool LoadField(RealStatePtr L, int idx, string field_name)
		{
			idx = idx > 0 ? idx : LuaAPI.lua_gettop(L) + idx + 1;// abs of index
			LuaAPI.xlua_pushasciistring(L, field_name);
			LuaAPI.lua_rawget(L, idx);
			return !LuaAPI.lua_isnil(L, -1);
		}

		public static RealStatePtr GetMainState(RealStatePtr L)
		{
			RealStatePtr ret = default(RealStatePtr);
			LuaAPI.xlua_pushasciistring(L, LuaEnv.MAIN_SHREAD);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			if (LuaAPI.lua_isthread(L, -1))
			{
				ret = LuaAPI.lua_tothread(L, -1);
			}
			LuaAPI.lua_pop(L, 1);
			return ret;
		}

#if (UNITY_WSA && !ENABLE_IL2CPP) && !UNITY_EDITOR
        public static List<Assembly> _assemblies;
        public static List<Assembly> GetAssemblies()
        {
            if (_assemblies == null)
            {
                System.Threading.Tasks.Task t = new System.Threading.Tasks.Task(() =>
                {
                    _assemblies = GetAssemblyList().Result;
                });
                t.Start();
                t.Wait();
            }
            return _assemblies;
            
        }
        public static async System.Threading.Tasks.Task<List<Assembly>> GetAssemblyList()
        {
            List<Assembly> assemblies = new List<Assembly>();
            //return assemblies;
            var files = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFilesAsync();
            if (files == null)
                return assemblies;

            foreach (var file in files)
            {
                if (file.FileType != ".dll" && file.FileType != ".exe") continue;
                try
                {
                    assemblies.Add(Assembly.Load(new AssemblyName(file.DisplayName)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }

            }
            return assemblies;
        }
        public static IEnumerable<Type> GetAllTypes(bool exclude_generic_definition = true)
        {
            var assemblies = GetAssemblies();
            List<Type> allTypes = new List<Type>();
            foreach (var assembly in assemblies)
            {
                if (assembly.IsDynamic) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (!exclude_generic_definition || !type.GetTypeInfo().IsGenericTypeDefinition)
                    {
                        allTypes.Add(type);
                    }
                }
            }
            return allTypes;
        }
#else
		public static List<Type> GetAllTypes(bool exclude_generic_definition = true)
		{
			List<Type> allTypes = new List<Type>();
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				try
				{
#if XLUA_CODE_EMIT
					if (!(assemblies[i].ManifestModule is System.Reflection.Emit.ModuleBuilder))
					{
#endif
						var types = assemblies[i].GetTypes();
						for (int j = 0; j < types.Length; j++)
						{
							if (!exclude_generic_definition || !types[j].IsGenericTypeDefinition())
							{
								allTypes.Add(types[j]);
							}
						}
#if XLUA_CODE_EMIT
					}
#endif
				}
				catch (Exception)
				{
				}
			}

			return allTypes;
		}
#endif

		static LuaCSFunction genFieldGetter(Type type, FieldInfo field)
		{
			if (field.IsStatic)
			{
				return (RealStatePtr L) =>
				{
					ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
					translator.PushAny(L, field.GetValue(null));
					return 1;
				};
			}
			else
			{
				return (RealStatePtr L) =>
				{
					ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
					object obj = translator.FastGetCSObj(L, 1);
					if (obj == null || !type.IsInstanceOfType(obj))
					{
						return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get field " + field);
					}

					translator.PushAny(L, field.GetValue(obj));
					return 1;
				};
			}
		}

		static LuaCSFunction genFieldSetter(Type type, FieldInfo field)
		{
			if (field.IsStatic)
			{
				return (RealStatePtr L) =>
				{
					ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
					object val = translator.GetArgObject(L, 1, field.FieldType);
					if (field.FieldType.IsValueType() && Nullable.GetUnderlyingType(field.FieldType) == null && val == null)
					{
						return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
					}
					field.SetValue(null, val);
					return 0;
				};
			}
			else
			{
				return (RealStatePtr L) =>
				{
					ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);

					object obj = translator.FastGetCSObj(L, 1);
					if (obj == null || !type.IsInstanceOfType(obj))
					{
						return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set field " + field);
					}

					object val = translator.GetArgObject(L, 2, field.FieldType);
					if (field.FieldType.IsValueType() && Nullable.GetUnderlyingType(field.FieldType) == null && val == null)
					{
						return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
					}
					field.SetValue(obj, val);
					if (type.IsValueType())
					{
						translator.Update(L, 1, obj);
					}
					return 0;
				};
			}
		}

		// Indexers whose key can be a string are left to the member lookup (Lua's t.name is t["name"]).
		static PropertyInfo[] withoutStringKeys(PropertyInfo[] props)
		{
			List<PropertyInfo> kept = new List<PropertyInfo>(props.Length);
			for (int i = 0; i < props.Length; i++)
			{
				if (!props[i].GetIndexParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
				{
					kept.Add(props[i]);
				}
			}
			return kept.ToArray();
		}

		// An `in` or `ref readonly` key is a by-reference parameter (Pair&); Lua
		// passes the value, and PropertyInfo.GetValue and SetValue take it boxed.
		// Without this, reflection matched no key and indexing with it failed
		// ("cannot set (null), no such field" for x[p] = v, Unity editor suite
		// with FORCE_REFLECTION, 2026-10-18).
		static Type indexKeyType(PropertyInfo prop)
		{
			Type key = prop.GetIndexParameters()[0].ParameterType;
			return key.IsByRef ? key.GetElementType() : key;
		}

		static LuaCSFunction genItemGetter(Type type, PropertyInfo[] props)
		{
			props = withoutStringKeys(props);
			if (props.Length == 0)
			{
				return null;
			}
			Type[] params_type = new Type[props.Length];
			for (int i = 0; i < props.Length; i++)
			{
				params_type[i] = indexKeyType(props[i]);
			}
			object[] arg = new object[1] { null };
			return (RealStatePtr L) =>
			{
				ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
				object obj = translator.FastGetCSObj(L, 1);
				if (obj == null || !type.IsInstanceOfType(obj))
				{
					return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get prop " + props[0].Name);
				}

				for (int i = 0; i < props.Length; i++)
				{
					if (!translator.Assignable(L, 2, params_type[i]))
					{
						continue;
					}
					else
					{
						PropertyInfo prop = props[i];
						try
						{
							object index = translator.GetArgObject(L, 2, params_type[i]);
							arg[0] = index;
							object ret = prop.GetValue(obj, arg);
							LuaAPI.lua_pushboolean(L, true);
							translator.PushAny(L, ret);
							return 2;
						}
						catch (Exception e)
						{
							return LuaAPI.luaL_error(L, "try to get " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
						}
					}
				}

				LuaAPI.lua_pushboolean(L, false);
				return 1;
			};
		}

		static LuaCSFunction genItemSetter(Type type, PropertyInfo[] props)
		{
			props = withoutStringKeys(props);
			if (props.Length == 0)
			{
				return null;
			}
			Type[] params_type = new Type[props.Length];
			for (int i = 0; i < props.Length; i++)
			{
				params_type[i] = indexKeyType(props[i]);
			}
			object[] arg = new object[1] { null };
			return (RealStatePtr L) =>
			{
				ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
				object obj = translator.FastGetCSObj(L, 1);
				if (obj == null || !type.IsInstanceOfType(obj))
				{
					return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set prop " + props[0].Name);
				}

				for (int i = 0; i < props.Length; i++)
				{
					if (!translator.Assignable(L, 2, params_type[i]))
					{
						continue;
					}
					else
					{
						PropertyInfo prop = props[i];
						try
						{
							arg[0] = translator.GetArgObject(L, 2, params_type[i]);
							object val = translator.GetArgObject(L, 3, prop.PropertyType);
							if (val == null)
							{
								return LuaAPI.luaL_error(L, type.Name + "." + prop.Name + " Expected type " + prop.PropertyType);
							}
							prop.SetValue(obj, val, arg);
							LuaAPI.lua_pushboolean(L, true);

							return 1;
						}
						catch (Exception e)
						{
							return LuaAPI.luaL_error(L, "try to set " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
						}
					}
				}

				LuaAPI.lua_pushboolean(L, false);
				return 1;
			};
		}

		static LuaCSFunction genEnumCastFrom(Type type)
		{
			return (RealStatePtr L) =>
			{
				try
				{
					ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
					return translator.TranslateToEnumToTop(L, type, 1);
				}
				catch (Exception e)
				{
					return LuaAPI.luaL_error(L, "cast to " + type + " exception:" + e);
				}
			};
		}

		internal static IEnumerable<MethodInfo> GetExtensionMethodsOf(Type type_to_be_extend)
		{
			if (InternalGlobals.extensionMethodMap == null)
			{
				List<Type> type_def_extention_method = new List<Type>();

				IEnumerator<Type> enumerator = GetAllTypes().GetEnumerator();

				while (enumerator.MoveNext())
				{
					Type type = enumerator.Current;
					if (type.IsDefined(typeof(ExtensionAttribute), false) && (
							type.IsDefined(typeof(ReflectionUseAttribute), false)
#if UNITY_EDITOR || XLUA_GENERAL
							|| type.IsDefined(typeof(LuaCallCSharpAttribute), false)
#endif
						))
					{
						type_def_extention_method.Add(type);
					}

					if (!type.IsAbstract() || !type.IsSealed()) continue;

					var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
					for (int i = 0; i < fields.Length; i++)
					{
						var field = fields[i];
						if ((field.IsDefined(typeof(ReflectionUseAttribute), false)
#if UNITY_EDITOR || XLUA_GENERAL
							|| field.IsDefined(typeof(LuaCallCSharpAttribute), false)
#endif
							) && (typeof(IEnumerable<Type>)).IsAssignableFrom(field.FieldType))
						{
							addExtensionTypes(type_def_extention_method, field.GetValue(null) as IEnumerable<Type>);
						}
					}

					var props = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
					for (int i = 0; i < props.Length; i++)
					{
						var prop = props[i];
						if ((prop.IsDefined(typeof(ReflectionUseAttribute), false)
#if UNITY_EDITOR || XLUA_GENERAL
							|| prop.IsDefined(typeof(LuaCallCSharpAttribute), false)
#endif
							) && (typeof(IEnumerable<Type>)).IsAssignableFrom(prop.PropertyType))
						{
							addExtensionTypes(type_def_extention_method, prop.GetValue(null, null) as IEnumerable<Type>);
						}
					}
				}
				enumerator.Dispose();

				// Extension methods by the type they extend, each list in declaration order.
				Dictionary<Type, IEnumerable<MethodInfo>> extensionMethodMap = new Dictionary<Type, IEnumerable<MethodInfo>>();
				for (int i = 0; i < type_def_extention_method.Count; i++)
				{
					var methods = type_def_extention_method[i].GetMethods(BindingFlags.Static | BindingFlags.Public);
					for (int j = 0; j < methods.Length; j++)
					{
						var method = methods[j];
						if (!method.IsDefined(typeof(ExtensionAttribute), false) || !IsSupportedMethod(method)) continue;
						Type extended = getExtendedType(method);
						IEnumerable<MethodInfo> list;
						if (!extensionMethodMap.TryGetValue(extended, out list))
						{
							list = new List<MethodInfo>();
							extensionMethodMap.Add(extended, list);
						}
						((List<MethodInfo>)list).Add(method);
					}
				}
				InternalGlobals.extensionMethodMap = extensionMethodMap;
			}
			IEnumerable<MethodInfo> ret = null;
			InternalGlobals.extensionMethodMap.TryGetValue(type_to_be_extend, out ret);
			return ret;
		}

		static void addExtensionTypes(List<Type> types, IEnumerable<Type> source)
		{
			if (source == null) throw new ArgumentNullException("source");
			foreach (var t in source)
			{
				if (t.IsDefined(typeof(ExtensionAttribute), false))
				{
					types.Add(t);
				}
			}
		}

		static bool hasEventNamed(EventInfo[] events, string name)
		{
			for (int i = 0; i < events.Length; i++)
			{
				if (events[i].Name == name) return true;
			}
			return false;
		}

		static bool hasUnsupportedParameter(ParameterInfo[] parameters)
		{
			for (int i = 0; i < parameters.Length; i++)
			{
				if (IsUnsupportedType(parameters[i].ParameterType)) return true;
			}
			return false;
		}

		// The [BlackList] configuration that generated and emitted wrappers
		// apply. Players do not read it (BlackListRules).
		static bool isListed(MemberInfo member)
		{
#if UNITY_EDITOR || XLUA_GENERAL
			return BlackListRules.IsListed(member);
#else
			return false;
#endif
		}

		static bool isListedMethod(MethodBase method)
		{
#if UNITY_EDITOR || XLUA_GENERAL
			return BlackListRules.IsListedMethod(method);
#else
			return false;
#endif
		}

		struct MethodKey
		{
			public string Name;
			public bool IsStatic;
		}

		static void makeReflectionWrap(RealStatePtr L, Type type, int cls_field, int cls_getter, int cls_setter,
			int obj_field, int obj_getter, int obj_setter, int obj_meta, out LuaCSFunction item_getter, out LuaCSFunction item_setter, BindingFlags access)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | access;
			FieldInfo[] fields = type.GetFields(flag);
			EventInfo[] all_events = type.GetEvents(flag | BindingFlags.Public | BindingFlags.NonPublic);

            LuaAPI.lua_checkstack(L, 2);

            for (int i = 0; i < fields.Length; ++i)
			{
				FieldInfo field = fields[i];
				string fieldName = field.Name;
				// skip hotfix inject field
				if (field.IsStatic && (field.Name.StartsWith("__Hotfix") || field.Name.StartsWith("_c__Hotfix")) && typeof(Delegate).IsAssignableFrom(field.FieldType))
				{
					continue;
				}
				if (IsUnsupportedType(field.FieldType) || isListed(field))
				{
					continue;
				}
				if (hasEventNamed(all_events, fieldName))
				{
					fieldName = "&" + fieldName;
				}

				if (field.IsStatic && (field.IsInitOnly || field.IsLiteral))
				{
					LuaAPI.xlua_pushasciistring(L, fieldName);
					translator.PushAny(L, field.GetValue(null));
					LuaAPI.lua_rawset(L, cls_field);
				}
				else
				{
					LuaAPI.xlua_pushasciistring(L, fieldName);
					translator.PushFixCSFunction(L, genFieldGetter(type, field));
					LuaAPI.lua_rawset(L, field.IsStatic ? cls_getter : obj_getter);

					LuaAPI.xlua_pushasciistring(L, fieldName);
					translator.PushFixCSFunction(L, genFieldSetter(type, field));
					LuaAPI.lua_rawset(L, field.IsStatic ? cls_setter : obj_setter);
				}
			}

			EventInfo[] events = type.GetEvents(flag);
			for (int i = 0; i < events.Length; ++i)
			{
				EventInfo eventInfo = events[i];
				if (isListed(eventInfo))
				{
					continue;
				}
				LuaAPI.xlua_pushasciistring(L, eventInfo.Name);
				translator.PushFixCSFunction(L, translator.methodWrapsCache.GetEventWrap(type, eventInfo.Name));
				bool is_static = (eventInfo.GetAddMethod(true) != null) ? eventInfo.GetAddMethod(true).IsStatic : eventInfo.GetRemoveMethod(true).IsStatic;
				LuaAPI.lua_rawset(L, is_static ? cls_field : obj_field);
			}

			List<PropertyInfo> items = new List<PropertyInfo>();
			PropertyInfo[] props = type.GetProperties(flag);
#if UNITY_EDITOR || XLUA_GENERAL
			// Property accessors are wrapped from the method list below; a
			// property left out by [BlackList] takes its accessors with it.
			HashSet<MethodInfo> listedAccessors = null;
#endif
			for (int i = 0; i < props.Length; ++i)
			{
				PropertyInfo prop = props[i];
#if UNITY_EDITOR || XLUA_GENERAL
				if (isListed(prop))
				{
					if (listedAccessors == null) listedAccessors = new HashSet<MethodInfo>();
					foreach (var accessor in prop.GetAccessors(true))
					{
						listedAccessors.Add(accessor);
					}
					continue;
				}
#endif
				if (prop.GetIndexParameters().Length > 0 && !IsUnsupportedType(prop.PropertyType)
					&& !hasUnsupportedParameter(prop.GetIndexParameters()))
				{
					items.Add(prop);
				}
			}

			var item_array = items.ToArray();
			item_getter = item_array.Length > 0 ? genItemGetter(type, item_array) : null;
			item_setter = item_array.Length > 0 ? genItemSetter(type, item_array) : null;
			MethodInfo[] methods = type.GetMethods(flag);
			if (access == BindingFlags.NonPublic)
			{
				// Every method, public or not, that has the name of a non-public one, once each, in the
				// order of the full list. (The Enumerable.Join this replaces repeated each method once per
				// non-public method of its name, so the wrappers held duplicate overloads.)
				HashSet<string> nonPublicNames = new HashSet<string>();
				for (int i = 0; i < methods.Length; i++)
				{
					nonPublicNames.Add(methods[i].Name);
				}
				MethodInfo[] all = type.GetMethods(flag | BindingFlags.Public);
				List<MethodInfo> joined = new List<MethodInfo>();
				for (int i = 0; i < all.Length; i++)
				{
					if (nonPublicNames.Contains(all[i].Name))
					{
						joined.Add(all[i]);
					}
				}
				methods = joined.ToArray();
			}
			Dictionary<MethodKey, List<MemberInfo>> pending_methods = new Dictionary<MethodKey, List<MemberInfo>>();
			for (int i = 0; i < methods.Length; ++i)
			{
				MethodInfo method = methods[i];
				string method_name = method.Name;
				if (HasUnsupportedType(method))
				{
					continue;
				}
#if UNITY_EDITOR || XLUA_GENERAL
				// As the generator: an accessor by its name, other methods by name
				// and parameter types.
				if (method.IsSpecialName && (method_name.StartsWith("get_", StringComparison.Ordinal) || method_name.StartsWith("set_", StringComparison.Ordinal)))
				{
					if ((listedAccessors != null && listedAccessors.Contains(method)) || isListed(method))
					{
						continue;
					}
				}
				else if (isListedMethod(method))
				{
					continue;
				}
#endif

				MethodKey method_key = new MethodKey { Name = method_name, IsStatic = method.IsStatic };
				List<MemberInfo> overloads;
				if (pending_methods.TryGetValue(method_key, out overloads))
				{
					overloads.Add(method);
					continue;
				}

				//indexer
				if (method.IsSpecialName && ((method.Name == "get_Item" && method.GetParameters().Length == 1) || (method.Name == "set_Item" && method.GetParameters().Length == 2)))
				{
					if (!method.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(string)))
					{
						continue;
					}
				}

				if ((method_name.StartsWith("add_") || method_name.StartsWith("remove_")) && method.IsSpecialName)
				{
					continue;
				}

				if (method_name.StartsWith("op_") && method.IsSpecialName) // Operators
				{
					if (InternalGlobals.supportOp.ContainsKey(method_name))
					{
						if (overloads == null)
						{
							overloads = new List<MemberInfo>();
							pending_methods.Add(method_key, overloads);
						}
						overloads.Add(method);
					}
					continue;
				}
				else if (method_name.StartsWith("get_") && method.IsSpecialName && method.GetParameters().Length != 1) // getter of property
				{
					string prop_name = method.Name.Substring(4);
					LuaAPI.xlua_pushasciistring(L, prop_name);
					translator.PushFixCSFunction(L, translator.methodWrapsCache._GenMethodWrap(method.DeclaringType, prop_name, new MethodBase[] { method }).Call);
					LuaAPI.lua_rawset(L, method.IsStatic ? cls_getter : obj_getter);
				}
				else if (method_name.StartsWith("set_") && method.IsSpecialName && method.GetParameters().Length != 2) // setter of property
				{
					string prop_name = method.Name.Substring(4);
					LuaAPI.xlua_pushasciistring(L, prop_name);
					translator.PushFixCSFunction(L, translator.methodWrapsCache._GenMethodWrap(method.DeclaringType, prop_name, new MethodBase[] { method }).Call);
					LuaAPI.lua_rawset(L, method.IsStatic ? cls_setter : obj_setter);
				}
				else if (method_name == ".ctor" && method.IsConstructor)
				{
					continue;
				}
				else
				{
					if (overloads == null)
					{
						overloads = new List<MemberInfo>();
						pending_methods.Add(method_key, overloads);
					}
					overloads.Add(method);
				}
			}


			// Extension methods are public: the public pass has added them, and
			// the non-public pass of xlua.private_accessible would replace the
			// overloads they share with a public method.
			IEnumerable<MethodInfo> extend_methods = access == BindingFlags.NonPublic ? null : GetExtensionMethodsOf(type);
			if (extend_methods != null)
			{
				foreach (var extend_method in extend_methods)
				{
					if (HasUnsupportedType(extend_method) || isListedMethod(extend_method))
					{
						continue;
					}
					MethodKey method_key = new MethodKey { Name = extend_method.Name, IsStatic = false };
					List<MemberInfo> overloads;
					if (pending_methods.TryGetValue(method_key, out overloads))
					{
						overloads.Add(extend_method);
						continue;
					}
					else
					{
						overloads = new List<MemberInfo>() { extend_method };
						pending_methods.Add(method_key, overloads);
					}
				}
			}

			foreach (var kv in pending_methods)
			{
				if (kv.Key.Name.StartsWith("op_")) // Operators
				{
					LuaAPI.xlua_pushasciistring(L, InternalGlobals.supportOp[kv.Key.Name]);
					translator.PushFixCSFunction(L,
						new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray()).Call));
					LuaAPI.lua_rawset(L, obj_meta);
				}
				else
				{
					LuaAPI.xlua_pushasciistring(L, kv.Key.Name);
					translator.PushFixCSFunction(L,
						new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray()).Call));
					LuaAPI.lua_rawset(L, kv.Key.IsStatic ? cls_field : obj_field);
				}
			}
		}

		public static void loadUpvalue(RealStatePtr L, Type type, string metafunc, int index)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			LuaAPI.xlua_pushasciistring(L, metafunc);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_rawget(L, -2);
			LuaAPI.lua_remove(L, -2);
			for (int i = 1; i <= index; i++)
			{
				LuaAPI.lua_getupvalue(L, -i, i);
				if (LuaAPI.lua_isnil(L, -1))
				{
					LuaAPI.lua_pop(L, 1);
					LuaAPI.lua_newtable(L);
					LuaAPI.lua_pushvalue(L, -1);
					LuaAPI.lua_setupvalue(L, -i - 2, i);
				}
			}
			for (int i = 0; i < index; i++)
			{
				LuaAPI.lua_remove(L, -2);
			}
		}

        public static void RegisterEnumType(RealStatePtr L, Type type)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            foreach (var name in Enum.GetNames(type))
            {
                RegisterObject(L, translator, Utils.CLS_IDX, name, Enum.Parse(type, name));
            }
        }


        public static void MakePrivateAccessible(RealStatePtr L, Type type)
		{
            LuaAPI.lua_checkstack(L, 20);

            int oldTop = LuaAPI.lua_gettop(L);

			LuaAPI.luaL_getmetatable(L, MetatableKey(type));
			if (LuaAPI.lua_isnil(L, -1))
			{
				LuaAPI.lua_settop(L, oldTop);
				throw new Exception("can not find the metatable for " + type);
			}
			int obj_meta = LuaAPI.lua_gettop(L);

			LoadCSTable(L, type);
			if (LuaAPI.lua_isnil(L, -1))
			{
				LuaAPI.lua_settop(L, oldTop);
				throw new Exception("can not find the class for " + type);
			}
			int cls_field = LuaAPI.lua_gettop(L);

			loadUpvalue(L, type, LuaIndexsFieldName, 2);
			int obj_getter = LuaAPI.lua_gettop(L);
			loadUpvalue(L, type, LuaIndexsFieldName, 1);
			int obj_field = LuaAPI.lua_gettop(L);

			loadUpvalue(L, type, LuaNewIndexsFieldName, 1);
			int obj_setter = LuaAPI.lua_gettop(L);

			loadUpvalue(L, type, LuaClassIndexsFieldName, 1);
			int cls_getter = LuaAPI.lua_gettop(L);

			loadUpvalue(L, type, LuaClassNewIndexsFieldName, 1);
			int cls_setter = LuaAPI.lua_gettop(L);

			LuaCSFunction item_getter;
			LuaCSFunction item_setter;
			makeReflectionWrap(L, type, cls_field, cls_getter, cls_setter, obj_field, obj_getter, obj_setter, obj_meta,
				out item_getter, out item_setter, BindingFlags.NonPublic);
			LuaAPI.lua_settop(L, oldTop);

			foreach (var nested_type in type.GetNestedTypes(BindingFlags.NonPublic))
			{
				if ((!nested_type.IsAbstract() && typeof(Delegate).IsAssignableFrom(nested_type))
					|| nested_type.IsGenericTypeDefinition())
				{
					continue;
				}
				ObjectTranslatorPool.Instance.Find(L).TryDelayWrapLoader(L, nested_type);
				MakePrivateAccessible(L, nested_type);
			}
		}

		[MonoPInvokeCallback(typeof(LuaCSFunction))]
		internal static int LazyReflectionCall(RealStatePtr L)
		{
			try
			{
				ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
				Type type;
				translator.Get(L, LuaAPI.xlua_upvalueindex(1), out type);
				LazyMemberTypes memberType = (LazyMemberTypes)LuaAPI.xlua_tointeger(L, LuaAPI.xlua_upvalueindex(2));
				string memberName = LuaAPI.lua_tostring(L, LuaAPI.xlua_upvalueindex(3));
				bool isStatic = LuaAPI.lua_toboolean(L, LuaAPI.xlua_upvalueindex(4));
				LuaCSFunction wrap = null;
				// A member the source generator left to reflection says why, once.
				Binding.BindingRegistry.ReportFallback(type, memberName);

				switch (memberType)
				{
					case LazyMemberTypes.Method:
						// As in the emitted and reflection wrappers: the static methods
						// for the class, the instance and extension methods for objects.
						// The generator registers an instance member for a name that
						// only extension methods have, too.
						var members = type.GetMember(memberName);
						List<MemberInfo> methods = new List<MemberInfo>();
						for (int i = 0; i < members.Length; i++)
						{
							var mb = members[i] as MethodBase;
							if (mb != null && mb.IsStatic == isStatic) methods.Add(mb);
						}
						if (!isStatic)
						{
							var extensionMethods = GetExtensionMethodsOf(type);
							if (extensionMethods != null)
							{
								foreach (var m in extensionMethods)
								{
									if (m.Name == memberName) methods.Add(m);
								}
							}
						}
						if (methods.Count == 0)
						{
							return LuaAPI.luaL_error(L, "can not find " + memberName + " for " + type);
						}
						wrap = new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, memberName, methods.ToArray()).Call);
						if (isStatic)
						{
							LoadCSTable(L, type);
						}
						else
						{
							loadUpvalue(L, type, LuaIndexsFieldName, 1);
						}
						if (LuaAPI.lua_isnil(L, -1))
						{
							return LuaAPI.luaL_error(L, "can not find the meta info for " + type);
						}
						break;
					case LazyMemberTypes.FieldGet:
					case LazyMemberTypes.FieldSet:
						var field = type.GetField(memberName);
						if (field == null)
						{
							return LuaAPI.luaL_error(L, "can not find " + memberName + " for " + type);
						}
						if (isStatic)
						{
							if (memberType == LazyMemberTypes.FieldGet)
							{
								loadUpvalue(L, type, LuaClassIndexsFieldName, 1);
							}
							else
							{
								loadUpvalue(L, type, LuaClassNewIndexsFieldName, 1);
							}
						}
						else
						{
							if (memberType == LazyMemberTypes.FieldGet)
							{
								loadUpvalue(L, type, LuaIndexsFieldName, 2);
							}
							else
							{
								loadUpvalue(L, type, LuaNewIndexsFieldName, 1);
							}
						}

						wrap = (memberType == LazyMemberTypes.FieldGet) ? genFieldGetter(type, field) : genFieldSetter(type, field);

						break;
					case LazyMemberTypes.PropertyGet:
					case LazyMemberTypes.PropertySet:
						var prop = type.GetProperty(memberName);
						if (prop == null)
						{
							return LuaAPI.luaL_error(L, "can not find " + memberName + " for " + type);
						}
						if (isStatic)
						{
							if (memberType == LazyMemberTypes.PropertyGet)
							{
								loadUpvalue(L, type, LuaClassIndexsFieldName, 1);
							}
							else
							{
								loadUpvalue(L, type, LuaClassNewIndexsFieldName, 1);
							}
						}
						else
						{
							if (memberType == LazyMemberTypes.PropertyGet)
							{
								loadUpvalue(L, type, LuaIndexsFieldName, 2);
							}
							else
							{
								loadUpvalue(L, type, LuaNewIndexsFieldName, 1);
							}
						}

						if (LuaAPI.lua_isnil(L, -1))
						{
							return LuaAPI.luaL_error(L, "can not find the meta info for " + type);
						}

						wrap = translator.methodWrapsCache._GenMethodWrap(prop.DeclaringType, prop.Name, new MethodBase[] { (memberType == LazyMemberTypes.PropertyGet) ? prop.GetGetMethod() : prop.GetSetMethod() }).Call;
						break;
					case LazyMemberTypes.Event:
						var eventInfo = type.GetEvent(memberName);
						if (eventInfo == null)
						{
							return LuaAPI.luaL_error(L, "can not find " + memberName + " for " + type);
						}
						if (isStatic)
						{
							LoadCSTable(L, type);
						}
						else
						{
							loadUpvalue(L, type, LuaIndexsFieldName, 1);
						}
						if (LuaAPI.lua_isnil(L, -1))
						{
							return LuaAPI.luaL_error(L, "can not find the meta info for " + type);
						}
						wrap = translator.methodWrapsCache.GetEventWrap(type, eventInfo.Name);
						break;
					default:
						return LuaAPI.luaL_error(L, "unsupport member type" + memberType);
				}

				LuaAPI.xlua_pushasciistring(L, memberName);
				translator.PushFixCSFunction(L, wrap);
				LuaAPI.lua_rawset(L, -3);
				LuaAPI.lua_pop(L, 1);
				return wrap(L);
			}
			catch (Exception e)
			{
				return LuaAPI.luaL_error(L, "c# exception in LazyReflectionCall:" + e);
			}
		}

		public static void ReflectionWrap(RealStatePtr L, Type type, bool privateAccessible)
		{
            LuaAPI.lua_checkstack(L, 20);

            int top_enter = LuaAPI.lua_gettop(L);
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			//create obj meta table
			LuaAPI.luaL_getmetatable(L, MetatableKey(type));
			if (LuaAPI.lua_isnil(L, -1))
			{
				LuaAPI.lua_pop(L, 1);
				LuaAPI.luaL_newmetatable(L, MetatableKey(type));
			}
			LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
			LuaAPI.lua_pushnumber(L, 1);
			LuaAPI.lua_rawset(L, -3);
			LockMetatable(L, -1);
			int obj_meta = LuaAPI.lua_gettop(L);

			LuaAPI.lua_newtable(L);
			LockMetatable(L, -1);
			int cls_meta = LuaAPI.lua_gettop(L);

			LuaAPI.lua_newtable(L);
			int obj_field = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int obj_getter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int obj_setter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int cls_field = LuaAPI.lua_gettop(L);
            //set cls_field to namespace
            SetCSTable(L, type, cls_field);
            //finish set cls_field to namespace
            LuaAPI.lua_newtable(L);
			int cls_getter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int cls_setter = LuaAPI.lua_gettop(L);

            LuaCSFunction item_getter;
			LuaCSFunction item_setter;
			makeReflectionWrap(L, type, cls_field, cls_getter, cls_setter, obj_field, obj_getter, obj_setter, obj_meta,
				out item_getter, out item_setter, privateAccessible ? (BindingFlags.Public | BindingFlags.NonPublic) : BindingFlags.Public);

			// init obj metatable
			LuaAPI.xlua_pushasciistring(L, "__gc");
			LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
			LuaAPI.lua_rawset(L, obj_meta);

			LuaAPI.xlua_pushasciistring(L, "__tostring");
			LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
			LuaAPI.lua_rawset(L, obj_meta);

			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, obj_field);
			LuaAPI.lua_pushvalue(L, obj_getter);
			translator.PushFixCSFunction(L, item_getter);
			translator.PushAny(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.lua_pushnil(L);
			LuaAPI.gen_obj_indexer(L);
			//store in lua indexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, obj_meta); // set __index

			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, obj_setter);
			translator.PushFixCSFunction(L, item_setter);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.lua_pushnil(L);
			LuaAPI.gen_obj_newindexer(L);
			//store in lua newindexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, obj_meta); // set __newindex
											//finish init obj metatable

			LuaAPI.xlua_pushasciistring(L, "UnderlyingSystemType");
			translator.PushAny(L, type);
			LuaAPI.lua_rawset(L, cls_field);

			if (type != null && type.IsEnum())
			{
				LuaAPI.xlua_pushasciistring(L, "__CastFrom");
				translator.PushFixCSFunction(L, genEnumCastFrom(type));
				LuaAPI.lua_rawset(L, cls_field);
			}

			//init class meta
			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, cls_getter);
			LuaAPI.lua_pushvalue(L, cls_field);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_indexer(L);
			//store in lua indexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, cls_meta); // set __index 

			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, cls_setter);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_newindexer(L);
			//store in lua newindexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, cls_meta); // set __newindex

			LuaCSFunction constructor = typeof(Delegate).IsAssignableFrom(type) ? translator.metaFunctions.DelegateCtor : translator.methodWrapsCache.GetConstructorWrap(type);
			if (constructor == null)
			{
				constructor = (RealStatePtr LL) =>
				{
					return LuaAPI.luaL_error(LL, "No constructor for " + type);
				};
			}

			LuaAPI.xlua_pushasciistring(L, "__call");
			translator.PushFixCSFunction(L, constructor);
			LuaAPI.lua_rawset(L, cls_meta);

			LuaAPI.lua_pushvalue(L, cls_meta);
			LuaAPI.lua_setmetatable(L, cls_field);

			LuaAPI.lua_pop(L, 8);

			System.Diagnostics.Debug.Assert(top_enter == LuaAPI.lua_gettop(L));
		}

		//meta: -4, method:-3, getter: -2, setter: -1
		public static void BeginObjectRegister(Type type, RealStatePtr L, ObjectTranslator translator, int meta_count, int method_count, int getter_count,
			int setter_count, int type_id = -1)
		{
			if (type == null)
			{
				if (type_id == -1) throw new Exception("Fatal: must provide a type of type_id");
				LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, type_id);
			}
			else
			{
				LuaAPI.luaL_getmetatable(L, MetatableKey(type));
				if (LuaAPI.lua_isnil(L, -1))
				{
					LuaAPI.lua_pop(L, 1);
					LuaAPI.luaL_newmetatable(L, MetatableKey(type));
				}
			}
			LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
			LuaAPI.lua_pushnumber(L, 1);
			LuaAPI.lua_rawset(L, -3);
			LockMetatable(L, -1);

			if ((type == null || !translator.HasCustomOp(type)) && type != typeof(decimal))
			{
				LuaAPI.xlua_pushasciistring(L, "__gc");
				LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
				LuaAPI.lua_rawset(L, -3);
			}

			LuaAPI.xlua_pushasciistring(L, "__tostring");
			LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
			LuaAPI.lua_rawset(L, -3);

			if (method_count == 0)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
				LuaAPI.lua_createtable(L, 0, method_count);
			}

			if (getter_count == 0)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
				LuaAPI.lua_createtable(L, 0, getter_count);
			}

			if (setter_count == 0)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
				LuaAPI.lua_createtable(L, 0, setter_count);
			}
		}

		static int abs_idx(int top, int idx)
		{
			return idx > 0 ? idx : top + idx + 1;
		}

		public const int OBJ_META_IDX = -4;
		public const int METHOD_IDX = -3;
		public const int GETTER_IDX = -2;
		public const int SETTER_IDX = -1;

#if GEN_CODE_MINIMIZE
        public static void EndObjectRegister(Type type, RealStatePtr L, ObjectTranslator translator, CSharpWrapper csIndexer,
            CSharpWrapper csNewIndexer, Type base_type, CSharpWrapper arrayIndexer, CSharpWrapper arrayNewIndexer)
#else
		public static void EndObjectRegister(Type type, RealStatePtr L, ObjectTranslator translator, LuaCSFunction csIndexer,
			LuaCSFunction csNewIndexer, Type base_type, LuaCSFunction arrayIndexer, LuaCSFunction arrayNewIndexer)
#endif
		{
			int top = LuaAPI.lua_gettop(L);
			int meta_idx = abs_idx(top, OBJ_META_IDX);
			int method_idx = abs_idx(top, METHOD_IDX);
			int getter_idx = abs_idx(top, GETTER_IDX);
			int setter_idx = abs_idx(top, SETTER_IDX);

			//begin index gen
			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, method_idx);
			LuaAPI.lua_pushvalue(L, getter_idx);

			if (csIndexer == null)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
#if GEN_CODE_MINIMIZE
                translator.PushCSharpWrapper(L, csIndexer);
#else
				LuaAPI.lua_pushstdcallcfunction(L, csIndexer);
#endif
			}

			translator.Push(L, type == null ? base_type : type.BaseType());

			LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			if (arrayIndexer == null)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
#if GEN_CODE_MINIMIZE
                translator.PushCSharpWrapper(L, arrayIndexer);
#else
				LuaAPI.lua_pushstdcallcfunction(L, arrayIndexer);
#endif
			}

			LuaAPI.gen_obj_indexer(L);

			if (type != null)
			{
				LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
				LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
				translator.Push(L, type);
				LuaAPI.lua_pushvalue(L, -3);
				LuaAPI.lua_rawset(L, -3);
				LuaAPI.lua_pop(L, 1);
			}

			LuaAPI.lua_rawset(L, meta_idx);
			//end index gen

			//begin newindex gen
			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, setter_idx);

			if (csNewIndexer == null)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
#if GEN_CODE_MINIMIZE
                translator.PushCSharpWrapper(L, csNewIndexer);
#else
				LuaAPI.lua_pushstdcallcfunction(L, csNewIndexer);
#endif
			}

			translator.Push(L, type == null ? base_type : type.BaseType());

			LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

			if (arrayNewIndexer == null)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
#if GEN_CODE_MINIMIZE
                translator.PushCSharpWrapper(L, arrayNewIndexer);
#else
				LuaAPI.lua_pushstdcallcfunction(L, arrayNewIndexer);
#endif
			}

			LuaAPI.gen_obj_newindexer(L);

			if (type != null)
			{
				LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
				LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
				translator.Push(L, type);
				LuaAPI.lua_pushvalue(L, -3);
				LuaAPI.lua_rawset(L, -3);
				LuaAPI.lua_pop(L, 1);
			}

			LuaAPI.lua_rawset(L, meta_idx);
			//end new index gen
			LuaAPI.lua_pop(L, 4);
		}

#if GEN_CODE_MINIMIZE
        public static void RegisterFunc(RealStatePtr L, int idx, string name, CSharpWrapper func)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            idx = abs_idx(LuaAPI.lua_gettop(L), idx);
            LuaAPI.xlua_pushasciistring(L, name);
            translator.PushCSharpWrapper(L, func);
            LuaAPI.lua_rawset(L, idx);
        }
#else
		public static void RegisterFunc(RealStatePtr L, int idx, string name, LuaCSFunction func)
		{
			idx = abs_idx(LuaAPI.lua_gettop(L), idx);
			LuaAPI.xlua_pushasciistring(L, name);
			LuaAPI.lua_pushstdcallcfunction(L, func);
			LuaAPI.lua_rawset(L, idx);
		}
#endif

		public static void RegisterLazyFunc(RealStatePtr L, int idx, string name, Type type, LazyMemberTypes memberType, bool isStatic)
		{
			idx = abs_idx(LuaAPI.lua_gettop(L), idx);
			LuaAPI.xlua_pushasciistring(L, name);

			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			translator.PushAny(L, type);
			LuaAPI.xlua_pushinteger(L, (int)memberType);
			LuaAPI.lua_pushstring(L, name);
			LuaAPI.lua_pushboolean(L, isStatic);
			LuaAPI.lua_pushstdcallcfunction(L, InternalGlobals.LazyReflectionWrap, 4);
			LuaAPI.lua_rawset(L, idx);
		}

		public static void RegisterObject(RealStatePtr L, ObjectTranslator translator, int idx, string name, object obj)
		{
			idx = abs_idx(LuaAPI.lua_gettop(L), idx);
			LuaAPI.xlua_pushasciistring(L, name);
			translator.PushAny(L, obj);
			LuaAPI.lua_rawset(L, idx);
		}

#if GEN_CODE_MINIMIZE
        public static void BeginClassRegister(Type type, RealStatePtr L, CSharpWrapper creator, int class_field_count,
            int static_getter_count, int static_setter_count)
#else
		public static void BeginClassRegister(Type type, RealStatePtr L, LuaCSFunction creator, int class_field_count,
			int static_getter_count, int static_setter_count)
#endif
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			LuaAPI.lua_createtable(L, 0, class_field_count);

			LuaAPI.xlua_pushasciistring(L, "UnderlyingSystemType");
			translator.PushAny(L, type);
			LuaAPI.lua_rawset(L, -3);

			int cls_table = LuaAPI.lua_gettop(L);

			SetCSTable(L, type, cls_table);

			LuaAPI.lua_createtable(L, 0, 3);
			int meta_table = LuaAPI.lua_gettop(L);
			LockMetatable(L, meta_table);
			if (creator != null)
			{
				LuaAPI.xlua_pushasciistring(L, "__call");
#if GEN_CODE_MINIMIZE
                translator.PushCSharpWrapper(L, creator);
#else
				LuaAPI.lua_pushstdcallcfunction(L, creator);
#endif
				LuaAPI.lua_rawset(L, -3);
			}

			if (static_getter_count == 0)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
				LuaAPI.lua_createtable(L, 0, static_getter_count);
			}

			if (static_setter_count == 0)
			{
				LuaAPI.lua_pushnil(L);
			}
			else
			{
				LuaAPI.lua_createtable(L, 0, static_setter_count);
			}
			LuaAPI.lua_pushvalue(L, meta_table);
			LuaAPI.lua_setmetatable(L, cls_table);
		}

		public const int CLS_IDX = -4;
		public const int CLS_META_IDX = -3;
		public const int CLS_GETTER_IDX = -2;
		public const int CLS_SETTER_IDX = -1;

		public static void EndClassRegister(Type type, RealStatePtr L, ObjectTranslator translator)
		{
			int top = LuaAPI.lua_gettop(L);
			int cls_idx = abs_idx(top, CLS_IDX);
			int cls_getter_idx = abs_idx(top, CLS_GETTER_IDX);
			int cls_setter_idx = abs_idx(top, CLS_SETTER_IDX);
			int cls_meta_idx = abs_idx(top, CLS_META_IDX);

			//begin cls index
			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, cls_getter_idx);
			LuaAPI.lua_pushvalue(L, cls_idx);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_indexer(L);

			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);

			LuaAPI.lua_rawset(L, cls_meta_idx);
			//end cls index

			//begin cls newindex
			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, cls_setter_idx);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_newindexer(L);

			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);

			LuaAPI.lua_rawset(L, cls_meta_idx);
			//end cls newindex

			LuaAPI.lua_pop(L, 4);
		}

		// The name Lua knows a type by: its name table entry (the original name,
		// a literal written by the generator, so it survives renaming by an
		// obfuscator), or Type.ToString() for a type without one. xlua.typename
		// returns it, and getPathOfType places the class table at it.
		public static string LuaTypeName(Type type)
		{
			return Binding.BindingRegistry.LuaNameOf(type) ?? type.ToString();
		}

		static readonly Dictionary<Type, string> metatableKeys = new Dictionary<Type, string>();

		// The registry key of a type's object metatable: the name table entry, or
		// Type.FullName (as before the table) for a type without one. The key is
		// fixed on first use, so a registration that comes later cannot give a
		// type whose metatable exists a second key.
		internal static string MetatableKey(Type type)
		{
			lock (metatableKeys)
			{
				string key;
				if (!metatableKeys.TryGetValue(type, out key))
				{
					key = Binding.BindingRegistry.LuaNameOf(type) ?? type.FullName;
					metatableKeys.Add(type, key);
				}
				return key;
			}
		}

		static List<string> getPathOfType(Type type)
		{
			string luaName = Binding.BindingRegistry.LuaNameOf(type);
			if (luaName != null) return pathOfName(luaName, type.IsNested);

			List<string> path = new List<string>();

			if (type.Namespace != null)
			{
				path.AddRange(type.Namespace.Split(new char[] { '.' }));
			}

			string class_name = type.ToString().Substring(type.Namespace == null ? 0 : type.Namespace.Length + 1);

			if (type.IsNested)
			{
				path.AddRange(class_name.Split(new char[] { '+' }));
			}
			else
			{
				path.Add(class_name);
			}
			return path;
		}

		// The CS path of a name table entry (Type.ToString() of the original
		// type), split as above: the namespace ends at the last '.' before the
		// first '+' or '[', and a nested type's names are split at '+'.
		static List<string> pathOfName(string name, bool nested)
		{
			List<string> path = new List<string>();
			int outer = name.IndexOfAny(new char[] { '+', '[' });
			int dot = name.LastIndexOf('.', outer < 0 ? name.Length - 1 : outer);
			if (dot > 0)
			{
				path.AddRange(name.Substring(0, dot).Split(new char[] { '.' }));
			}
			string class_name = name.Substring(dot + 1);
			if (nested)
			{
				path.AddRange(class_name.Split(new char[] { '+' }));
			}
			else
			{
				path.Add(class_name);
			}
			return path;
		}

		public static void LoadCSTable(RealStatePtr L, Type type)
		{
			int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

            List<string> path = getPathOfType(type);

			for (int i = 0; i < path.Count; ++i)
			{
				LuaAPI.xlua_pushasciistring(L, path[i]);
				if (0 != LuaAPI.xlua_pgettable(L, -2))
				{
					LuaAPI.lua_settop(L, oldTop);
					LuaAPI.lua_pushnil(L);
					return;
				}
				if (!LuaAPI.lua_istable(L, -1) && i < path.Count - 1)
				{
					LuaAPI.lua_settop(L, oldTop);
					LuaAPI.lua_pushnil(L);
					return;
				}
				LuaAPI.lua_remove(L, -2);
			}
		}

		public static void SetCSTable(RealStatePtr L, Type type, int cls_table)
		{
			int oldTop = LuaAPI.lua_gettop(L);
			cls_table = abs_idx(oldTop, cls_table);
            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

            List<string> path = getPathOfType(type);

			for (int i = 0; i < path.Count - 1; ++i)
			{
				LuaAPI.xlua_pushasciistring(L, path[i]);
				if (0 != LuaAPI.xlua_pgettable(L, -2))
				{
					var err = LuaAPI.lua_tostring(L, -1);
					LuaAPI.lua_settop(L, oldTop);
					throw new Exception("SetCSTable for [" + type + "] error: " + err);
				}
				if (LuaAPI.lua_isnil(L, -1))
				{
					LuaAPI.lua_pop(L, 1);
					LuaAPI.lua_createtable(L, 0, 0);
					LuaAPI.xlua_pushasciistring(L, path[i]);
					LuaAPI.lua_pushvalue(L, -2);
					LuaAPI.lua_rawset(L, -4);
				}
				else if (!LuaAPI.lua_istable(L, -1))
				{
					LuaAPI.lua_settop(L, oldTop);
					throw new Exception("SetCSTable for [" + type + "] error: ancestors is not a table!");
				}
				LuaAPI.lua_remove(L, -2);
			}

			LuaAPI.xlua_pushasciistring(L, path[path.Count - 1]);
			LuaAPI.lua_pushvalue(L, cls_table);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);

            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            ObjectTranslatorPool.Instance.Find(L).PushAny(L, type);
			LuaAPI.lua_pushvalue(L, cls_table);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
		}

		public const string LuaIndexsFieldName = "LuaIndexs";

		public const string LuaNewIndexsFieldName = "LuaNewIndexs";

		public const string LuaClassIndexsFieldName = "LuaClassIndexs";

		public const string LuaClassNewIndexsFieldName = "LuaClassNewIndexs";

		const string IsReadOnlyAttributeName = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
		const string RequiresLocationAttributeName = "System.Runtime.CompilerServices.RequiresLocationAttribute";

		// The attribute that makes a by-ref parameter readonly, or null:
		// IsReadOnlyAttribute for `in`, RequiresLocationAttribute for C# 12
		// `ref readonly`. Checked by name because older profiles define
		// neither type; Roslyn embeds a copy in the assembly when the BCL
		// lacks it.
		static string readOnlyByRefAttribute(ParameterInfo parameter)
		{
			if (!parameter.ParameterType.IsByRef || parameter.IsOut) return null;
			foreach (var attribute in parameter.GetCustomAttributes(false))
			{
				string name = attribute.GetType().FullName;
				if (name == IsReadOnlyAttributeName || name == RequiresLocationAttributeName) return name;
			}
			return null;
		}

		// A C# `in` or `ref readonly` parameter (readonly by reference) is a
		// plain input for Lua: it is never copied back and adds no return
		// value, unlike `ref`. A by-ref parameter with only the [In] flag is a
		// normal `ref` ([In] is a marshaling hint).
		public static bool IsInParameter(ParameterInfo parameter)
		{
			return readOnlyByRefAttribute(parameter) != null;
		}

		// The `ref readonly` subset of IsInParameter. Generated C# passes it
		// with `ref`, which every compiler accepts (one that predates C# 12
		// sees a plain `ref`), where an `in` parameter takes a plain argument.
		public static bool IsRefReadOnlyParameter(ParameterInfo parameter)
		{
			return readOnlyByRefAttribute(parameter) == RequiresLocationAttributeName;
		}

		public static bool IsParamsMatch(MethodInfo delegateMethod, MethodInfo bridgeMethod)
		{
			return IsParamsMatch(delegateMethod, bridgeMethod, 0);
		}

		// bridgeOffset: leading bridge parameters that the delegate does not have,
		// 1 for a static bridge whose first parameter is the DelegateBridge.
		public static bool IsParamsMatch(MethodInfo delegateMethod, MethodInfo bridgeMethod, int bridgeOffset)
		{
			if (delegateMethod == null || bridgeMethod == null)
			{
				return false;
			}
			if (delegateMethod.ReturnType != bridgeMethod.ReturnType)
			{
				return false;
			}
			ParameterInfo[] delegateParams = delegateMethod.GetParameters();
			ParameterInfo[] bridgeParams = bridgeMethod.GetParameters();
			if (delegateParams.Length + bridgeOffset != bridgeParams.Length)
			{
				return false;
			}

			for (int i = 0; i < delegateParams.Length; i++)
			{
				var bridgeParam = bridgeParams[i + bridgeOffset];
				if (delegateParams[i].ParameterType != bridgeParam.ParameterType || delegateParams[i].IsOut != bridgeParam.IsOut)
				{
					return false;
				}
				// A `ref` bridge writes Lua's extra results back; an `in` bridge does not.
				if (delegateParams[i].ParameterType.IsByRef && IsInParameter(delegateParams[i]) != IsInParameter(bridgeParam))
				{
					return false;
				}
			}

            var lastPos = delegateParams.Length - 1;
            return lastPos < 0 || delegateParams[lastPos].IsDefined(typeof(ParamArrayAttribute), false) == bridgeParams[lastPos + bridgeOffset].IsDefined(typeof(ParamArrayAttribute), false);
		}

		// Pointers and ref structs (Span<T>, ReadOnlySpan<T>) cannot be boxed, so
		// emitted and reflection wrappers leave out members that use them: the
		// rule of isUnsupportedType in Editor/Generator.cs (Tencent/xLua#1114,
		// #1167). Type.IsByRefLike is missing from Unity's .NET Framework
		// profile, so the attribute is checked by name.
		internal static bool IsUnsupportedType(Type type)
		{
			if (type.IsByRef) type = type.GetElementType();
			if (type.IsPointer) return true;
			if (!type.IsValueType() || type.IsPrimitive) return false;
			var attributes = type.GetCustomAttributes(false);
			for (int i = 0; i < attributes.Length; i++)
			{
				if (attributes[i].GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
			}
			return false;
		}

		// Lua guarantees only LUA_MINSTACK (20) free stack slots when it calls a
		// C function, and C# entry points get no more. Code that may push more
		// than UncheckedPushes values asks for room first (Tencent/xLua#1153).
		internal const int UncheckedPushes = 10;

		// Slots one push may use besides the slot it fills: xlua_pushcsobj
		// holds the userdata, the userdata cache table and a copy of the
		// userdata at once. EnsureStack adds this to the count it is given.
		internal const int PushSlack = 5;

		// Room a type registration asks for before it starts (luaL_newmetatable,
		// Begin/EndObjectRegister, Begin/EndClassRegister, ReflectionWrap). A
		// registration can start while a wrapper is pushing its results, on top
		// of the values already pushed. Nested registrations ask again.
		internal const int RegisterStack = 20;

		internal static void EnsureStack(RealStatePtr L, int extra)
		{
			if (!LuaAPI.lua_checkstack(L, extra + PushSlack))
			{
				throw new LuaException("Lua stack overflow: no room for " + extra + " more values");
			}
		}

		internal static bool HasUnsupportedType(MethodBase method)
		{
			if (method is MethodInfo && IsUnsupportedType((method as MethodInfo).ReturnType)) return true;
			return hasUnsupportedParameter(method.GetParameters());
		}

		public static bool IsSupportedMethod(MethodInfo method)
		{
			if (!method.ContainsGenericParameters)
				return true;
			var methodParameters = method.GetParameters();
			var returnType = method.ReturnType;
			var hasValidGenericParameter = false;
			var returnTypeValid = !returnType.IsGenericParameter;
			for (var i = 0; i < methodParameters.Length; i++)
			{
				var parameterType = methodParameters[i].ParameterType;
				if (parameterType.IsGenericParameter)
				{
					var parameterConstraints = parameterType.GetGenericParameterConstraints();
					if (parameterConstraints.Length == 0) return false;
					foreach (var parameterConstraint in parameterConstraints)
					{
						if (!parameterConstraint.IsClass() || (parameterConstraint == typeof(ValueType)))
							return false;
					}
					hasValidGenericParameter = true;
					if (!returnTypeValid)
					{
						if (parameterType == returnType)
						{
							returnTypeValid = true;
						}
					}
				}
			}
			return hasValidGenericParameter && returnTypeValid;
		}

		public static MethodInfo MakeGenericMethodWithConstraints(MethodInfo method)
		{
			try
			{
				var genericArguments = method.GetGenericArguments();
				var constraintedArgumentTypes = new Type[genericArguments.Length];
				for (var i = 0; i < genericArguments.Length; i++)
				{
					var argumentType = genericArguments[i];
					var parameterConstraints = argumentType.GetGenericParameterConstraints();
					constraintedArgumentTypes[i] = parameterConstraints[0];
				}
				return method.MakeGenericMethod(constraintedArgumentTypes);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Type getExtendedType(MethodInfo method)
		{
			var type = method.GetParameters()[0].ParameterType;
			if (!type.IsGenericParameter)
				return type;
			var parameterConstraints = type.GetGenericParameterConstraints();
			if (parameterConstraints.Length == 0)
				throw new InvalidOperationException();

			var firstParameterConstraint = parameterConstraints[0];
			if (!firstParameterConstraint.IsClass())
				throw new InvalidOperationException();
			return firstParameterConstraint;
		}

		public static bool IsStaticPInvokeCSFunction(LuaCSFunction csFunction)
		{
#if UNITY_WSA && !UNITY_EDITOR
            return csFunction.GetMethodInfo().IsStatic && csFunction.GetMethodInfo().GetCustomAttribute<MonoPInvokeCallbackAttribute>() != null;
#else
			return csFunction.Method.IsStatic && Attribute.IsDefined(csFunction.Method, typeof(MonoPInvokeCallbackAttribute));
#endif
		}

		public static bool IsPublic(Type type)
		{
			if (type.IsNested)
			{
				if (!type.IsNestedPublic()) return false;
				return IsPublic(type.DeclaringType);
			}
			if (type.IsGenericType())
			{
				var gas = type.GetGenericArguments();
				for (int i = 0; i < gas.Length; i++)
				{
					if (!IsPublic(gas[i]))
					{
						return false;
					}
				}
			}
			return type.IsPublic();
		}
	}
}
