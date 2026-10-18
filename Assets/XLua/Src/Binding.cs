/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The binding API: what generated code (the "Generate Code" output today, the
// source generator later) calls instead of extending ObjectTranslator,
// DelegateBridge, InternalGlobals, StaticLuaCallbacks or CopyByValue as partial
// classes. Generated code can therefore live in an assembly other than XLua's.
// Design: docs/design/source-generator.md, section 3.
//
// Generated code fills one AssemblyBindings per generated unit and passes it to
// BindingApi.Register, from [RuntimeInitializeOnLoadMethod] and, in the Editor,
// [InitializeOnLoadMethod]. Hosts without Unity find the units through
// [assembly: GeneratedBindings] when the first LuaEnv starts (EnsureRegistered).
//
// Registration is meant to run before LuaEnvs use the bindings: Register takes
// a lock, but lookups do not. A LuaEnv that already exists does not see a later
// registration's struct pushers and initers, as with LuaEnv.AddIniter.

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
#else
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;
#endif

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace XLua.Binding
{
    // Reads a value that generated code pushes as a struct or table (GCOptimize).
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate void StructGetter<T>(ObjectTranslator translator, RealStatePtr L, int index, out T value);

    // Pushes array[index] without boxing.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate void ArrayElementPusher<T>(ObjectTranslator translator, RealStatePtr L, T[] array, int index);

    // Sets array[arrayIndex] from the Lua value at valueIndex without boxing.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate void ArrayElementSetter<T>(ObjectTranslator translator, RealStatePtr L, T[] array, int arrayIndex, int valueIndex);

    // Marks an assembly that holds generated bindings: `bindings` has a static
    // parameterless Register method. Used where Unity does not call
    // [RuntimeInitializeOnLoadMethod] (XLUA_GENERAL tools and tests). `types`
    // lists the types the unit wraps; the source generator of an assembly
    // that references this one reads it at compile time and does not wrap
    // them again.
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GeneratedBindingsAttribute : Attribute
    {
        readonly Type bindings;
        readonly int apiVersion;
        readonly Type[] types;

        public GeneratedBindingsAttribute(Type bindings, int apiVersion)
            : this(bindings, apiVersion, null)
        {
        }

        public GeneratedBindingsAttribute(Type bindings, int apiVersion, Type[] types)
        {
            this.bindings = bindings;
            this.apiVersion = apiVersion;
            this.types = types ?? new Type[0];
        }

        public Type Bindings { get { return bindings; } }

        public int ApiVersion { get { return apiVersion; } }

        public Type[] Types { get { return (Type[])types.Clone(); } }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class BindingApi
    {
        // Generated code passes this to AssemblyBindings; Register rejects
        // another value. Removing or changing a member of this API means a new
        // version.
        public const int Version = 1;

        // How many values generated code may push without EnsureStack (Tencent/xLua#1153).
        public const int UncheckedPushes = Utils.UncheckedPushes;

        public static void Register(AssemblyBindings bindings)
        {
            BindingRegistry.Register(bindings);
        }

        // A process-wide slot for a struct or enum type that generated code
        // pushes. The same type always gets the same slot.
        public static int AllocateSlot(Type type)
        {
            return BindingRegistry.AllocateSlot(type);
        }

        public static object FastGetCSObj(ObjectTranslator translator, RealStatePtr L, int index)
        {
            return translator.FastGetCSObj(L, index);
        }

        public static ObjectCast GetCaster(ObjectTranslator translator, Type type)
        {
            return translator.objectCasters.GetCaster(type);
        }

        // The type id of the slot's type in this translator; it replaces the
        // <T>_TypeID fields that generated code added to ObjectTranslator.
        public static int TypeId(ObjectTranslator translator, RealStatePtr L, int slot)
        {
            int[] ids = translator.bindingTypeIds;
            if (slot < ids.Length)
            {
                int id = ids[slot];
                if (id != -1) return id;
            }
            return BindingRegistry.TypeIdSlow(translator, L, slot);
        }

        // The registry reference of the cache table for the slot's enum type
        // (value -> userdata); it replaces the <T>_EnumRef fields. Scripts
        // cannot reach it.
        public static int EnumCacheRef(ObjectTranslator translator, RealStatePtr L, int slot)
        {
            int[] refs = translator.bindingEnumRefs;
            if (slot < refs.Length)
            {
                int r = refs[slot];
                if (r != -1) return r;
            }
            return BindingRegistry.EnumCacheRefSlow(translator, L, slot);
        }

        public static int RegistryIndex
        {
            get { return LuaIndexes.LUA_REGISTRYINDEX; }
        }

        public static void EnsureStack(RealStatePtr L, int extra)
        {
            Utils.EnsureStack(L, extra);
        }

        // The Lua state of a bridge's LuaEnv; throws when the env is disposed.
        public static RealStatePtr State(LuaBase o)
        {
            return o.Env.L;
        }

        // The Lua state without the disposed check: the delegate bridges' hot path.
        public static RealStatePtr RawState(LuaBase o)
        {
            return o.Env.rawL;
        }

        public static ObjectTranslator Translator(LuaBase o)
        {
            return o.Env.translator;
        }

        public static int ErrorFuncRef(LuaBase o)
        {
            return o.Env.errorFuncRef;
        }

        public static int Reference(LuaBase o)
        {
            return o.LuaReference;
        }

        // The object bridges lock under THREAD_SAFE or HOTFIX_ENABLE.
        public static object Lock(LuaBase o)
        {
#if THREAD_SAFE || HOTFIX_ENABLE
            return o.Env.luaEnvLock;
#else
            return o.Env;
#endif
        }

        public static void ThrowError(LuaBase o, int oldTop)
        {
            o.Env.ThrowExceptionFromError(oldTop);
        }

        // A member the source generator could not wrap, registered as a lazy
        // reflection member (Utils.RegisterLazyFunc, as for DoNotGen). The first
        // use logs the reason once per member, in the Editor, development
        // builds and hosts without Unity; generated code passes a null reason
        // elsewhere.
        public static void RegisterFallback(RealStatePtr L, int idx, string name, Type type, LazyMemberTypes memberType, bool isStatic, string reason)
        {
            if (reason != null) BindingRegistry.AddFallbackReason(type, name, reason);
            Utils.RegisterLazyFunc(L, idx, name, type, memberType, isStatic);
        }
    }

    // What one generated unit binds. Generated code creates it, fills it and
    // passes it to BindingApi.Register; Register ignores a name it has seen.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class AssemblyBindings
    {
        internal readonly string name;
        internal readonly int apiVersion;
        internal readonly List<KeyValuePair<Type, Action<RealStatePtr>>> loaders = new List<KeyValuePair<Type, Action<RealStatePtr>>>();
        internal readonly List<KeyValuePair<Type, string>> names = new List<KeyValuePair<Type, string>>();
        internal readonly List<KeyValuePair<Type, Func<int, LuaEnv, LuaBase>>> interfaceBridges = new List<KeyValuePair<Type, Func<int, LuaEnv, LuaBase>>>();
        internal readonly List<KeyValuePair<Type, Func<DelegateBridge, Delegate>>> delegates = new List<KeyValuePair<Type, Func<DelegateBridge, Delegate>>>();
        internal readonly List<Type> bridgeClasses = new List<Type>();
        internal readonly List<KeyValuePair<Type, Action<LuaEnv, ObjectTranslator>>> structs = new List<KeyValuePair<Type, Action<LuaEnv, ObjectTranslator>>>();
        internal readonly List<Action<LuaEnv, ObjectTranslator>> initers = new List<Action<LuaEnv, ObjectTranslator>>();
        internal readonly List<KeyValuePair<Type, MethodInfo[]>> extensionMethods = new List<KeyValuePair<Type, MethodInfo[]>>();
        internal readonly List<KeyValuePair<Type, BindingRegistry.ArrayGet>> arrayGetters = new List<KeyValuePair<Type, BindingRegistry.ArrayGet>>();
        internal readonly List<KeyValuePair<Type, BindingRegistry.ArraySet>> arraySetters = new List<KeyValuePair<Type, BindingRegistry.ArraySet>>();
        internal Action<int, DelegateBridge> hotfixSlots;
        internal Type hotfixBridgeClass;

        // assemblyName: a key unique to the generated unit, normally the name
        // of the assembly it is compiled into.
        public AssemblyBindings(string assemblyName, int apiVersion)
        {
            if (assemblyName == null) throw new ArgumentNullException("assemblyName");
            name = assemblyName;
            this.apiVersion = apiVersion;
        }

        public string AssemblyName { get { return name; } }

        // Whether the unit binds anything besides names. Only such units
        // replace the reflection scan for extension methods.
        internal bool BindsCode
        {
            get
            {
                return loaders.Count > 0 || interfaceBridges.Count > 0 || delegates.Count > 0 || bridgeClasses.Count > 0
                    || structs.Count > 0 || initers.Count > 0 || extensionMethods.Count > 0
                    || arrayGetters.Count > 0 || arraySetters.Count > 0 || hotfixSlots != null;
            }
        }

        // Whether the unit bridges C# calls into Lua: delegates of Lua functions
        // or interfaces implemented by Lua tables. Only such units set
        // DelegateBridge.Gen_Flag, which turns CodeEmit off: CodeEmit then no
        // longer makes delegate bridges, nor wrappers for types without one.
        // The legacy generator's units always include their delegate bridges,
        // and the source generator's units have bridges for every
        // [CSharpCallLua] type of their assembly (milestone M3). A unit with
        // wrappers only leaves CodeEmit on for delegates and unwrapped types.
        internal bool BindsDelegates
        {
            get { return delegates.Count > 0 || bridgeClasses.Count > 0 || interfaceBridges.Count > 0; }
        }

        // A class wrapper: loader registers the type's metatables when Lua first
        // uses it. luaName is the type's Lua path (Type.ToString(), the name
        // table entry).
        public void Type(Type type, string luaName, Action<RealStatePtr> loader)
        {
            loaders.Add(new KeyValuePair<Type, Action<RealStatePtr>>(type, loader));
            Name(type, luaName);
        }

        // A name table entry only; a null luaName adds none.
        public void Name(Type type, string luaName)
        {
            if (luaName == null) return;
            names.Add(new KeyValuePair<Type, string>(type, luaName));
        }

        public void InterfaceBridge(Type type, Func<int, LuaEnv, LuaBase> create)
        {
            interfaceBridges.Add(new KeyValuePair<Type, Func<int, LuaEnv, LuaBase>>(type, create));
        }

        // How to make a delegate of delegateType that calls a Lua function:
        // create gets the function's bridge. Generated code passes a delegate
        // closed over the bridge (`x => new D(x.__Gen_Delegate_Imp<n>)`); a
        // delegate type without an entry of its own but with the same
        // parameters then gets the same bridge method.
        public void Delegate(Type delegateType, Func<DelegateBridge, Delegate> create)
        {
            delegates.Add(new KeyValuePair<Type, Func<DelegateBridge, Delegate>>(delegateType, create));
        }

        // A class of static bridge methods, `__Gen_Delegate_Imp<n>(this
        // DelegateBridge, ...)`. A delegate type without a Delegate entry gets
        // the first one whose parameters match (the legacy lookup by signature;
        // hotfix delegates created by the injector use it).
        public void DelegateBridges(Type bridgeClass)
        {
            bridgeClasses.Add(bridgeClass);
        }

        // Push, get and update of a GCOptimize struct or enum; registered in every
        // LuaEnv through ObjectTranslator.RegisterPushAndGetAndUpdate.
        public void Struct<T>(Action<ObjectTranslator, RealStatePtr, T> push, StructGetter<T> get, Action<ObjectTranslator, RealStatePtr, int, T> update)
        {
            structs.Add(new KeyValuePair<Type, Action<LuaEnv, ObjectTranslator>>(typeof(T), (env, translator) =>
            {
                translator.RegisterPushAndGetAndUpdate<T>(
                    (RealStatePtr L, T value) => push(translator, L, value),
                    (RealStatePtr L, int index, out T value) => get(translator, L, index, out value),
                    (RealStatePtr L, int index, T value) => update(translator, L, index, value));
            }));
        }

        // A caster for a GCOptimize type that Lua passes as a table; registered
        // in every LuaEnv through ObjectTranslator.RegisterCaster.
        public void Caster<T>(StructGetter<T> get)
        {
            structs.Add(new KeyValuePair<Type, Action<LuaEnv, ObjectTranslator>>(typeof(T), (env, translator) =>
            {
                translator.RegisterCaster<T>((RealStatePtr L, int index, out T value) => get(translator, L, index, out value));
            }));
        }

        // Element access for T[] without boxing; get may be null.
        public void ArrayAccessor<T>(ArrayElementPusher<T> get, ArrayElementSetter<T> set)
        {
            if (get != null)
            {
                arrayGetters.Add(new KeyValuePair<Type, BindingRegistry.ArrayGet>(typeof(T[]), (RealStatePtr L, ObjectTranslator translator, object array, int index) =>
                {
                    get(translator, L, (T[])array, index);
                }));
            }
            if (set != null)
            {
                arraySetters.Add(new KeyValuePair<Type, BindingRegistry.ArraySet>(typeof(T[]), (RealStatePtr L, ObjectTranslator translator, object array, int arrayIndex, int valueIndex) =>
                {
                    set(translator, L, (T[])array, arrayIndex, valueIndex);
                }));
            }
        }

        // Extension methods of `extended` that Lua sees as its members. Take the
        // MethodInfos from delegates (`new D(Ext.M).Method`), not by name.
        public void ExtensionMethods(Type extended, MethodInfo[] methods)
        {
            extensionMethods.Add(new KeyValuePair<Type, MethodInfo[]>(extended, methods));
        }

        // Runs in every LuaEnv, at the point where LuaEnv.AddIniter initers run.
        // The IntKey slots of the assembly's hotfix bridges (section 6): set
        // stores the patch of method id, which the injected code reads from the
        // assembly's own array. HotfixDelegateBridge.SetSlot(assembly, id, bridge)
        // reaches it by the unit's assembly name.
        // bridgeClass holds the hotfix bridges, `__Gen_Delegate_Imp<n>(this
        // DelegateBridge, ...)`: a delegate type the injector creates for a
        // method of this assembly (HotfixFlag.AdaptByDelegate, or field mode
        // without IntKey) finds its bridge there by signature, as with
        // DelegateBridges, without turning CodeEmit off.
        public void HotfixSlots(Action<int, DelegateBridge> set, Type bridgeClass)
        {
            if (set == null) throw new ArgumentNullException("set");
            hotfixSlots = set;
            hotfixBridgeClass = bridgeClass;
        }

        public void Initer(Action<LuaEnv, ObjectTranslator> init)
        {
            initers.Add(init);
        }
    }

    // The process-wide tables behind BindingApi; the runtime reads them.
    internal static class BindingRegistry
    {
        internal delegate void ArrayGet(RealStatePtr L, ObjectTranslator translator, object array, int index);
        internal delegate void ArraySet(RealStatePtr L, ObjectTranslator translator, object array, int arrayIndex, int valueIndex);

        static readonly object sync = new object();
        static readonly HashSet<string> registered = new HashSet<string>();
        static volatile bool scanned = false;

        static readonly Dictionary<Type, Action<RealStatePtr>> loaders = new Dictionary<Type, Action<RealStatePtr>>();
        static readonly Dictionary<Type, string> names = new Dictionary<Type, string>();
        // The reverse of names, for CS.A.B and the other lookups by name
        // (docs/design/source-generator.md, section 5.6): the first type
        // registered under a name keeps it. A generic type also gives its
        // definition's name ("A.G`1", which xlua.import_generic_type looks up).
        // The names are compile-time literals, so they still find a type an
        // obfuscator renamed after compilation.
        static readonly Dictionary<string, Type> typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        static readonly Dictionary<Type, Func<int, LuaEnv, LuaBase>> interfaceBridges = new Dictionary<Type, Func<int, LuaEnv, LuaBase>>();
        static readonly Dictionary<Type, Func<DelegateBridge, Delegate>> delegates = new Dictionary<Type, Func<DelegateBridge, Delegate>>();
        // The same entries in registration order, for the lookup by signature.
        static readonly List<KeyValuePair<Type, Func<DelegateBridge, Delegate>>> delegateOrder = new List<KeyValuePair<Type, Func<DelegateBridge, Delegate>>>();
        static readonly List<Type> bridgeClasses = new List<Type>();
        static volatile MethodInfo[] bridgeMethods = null;
        static readonly HashSet<Type> structTypes = new HashSet<Type>();
        static readonly List<Action<LuaEnv, ObjectTranslator>> initers = new List<Action<LuaEnv, ObjectTranslator>>();
        static readonly Dictionary<Type, ArrayGet> arrayGetters = new Dictionary<Type, ArrayGet>();
        static readonly Dictionary<Type, ArraySet> arraySetters = new Dictionary<Type, ArraySet>();
        static readonly Dictionary<string, Action<int, DelegateBridge>> hotfixSlots = new Dictionary<string, Action<int, DelegateBridge>>();
        static readonly List<Type> hotfixBridgeClasses = new List<Type>(); // internal classes of generated code

        static readonly Dictionary<Type, int> slotOfType = new Dictionary<Type, int>();
        static readonly List<Type> slotTypes = new List<Type>();

        internal static bool HasRegistrations
        {
            get { return registered.Count > 0; }
        }

        internal static void Register(AssemblyBindings b)
        {
            if (b == null) throw new ArgumentNullException("bindings");
            if (b.apiVersion != BindingApi.Version)
            {
                throw new InvalidOperationException("generated bindings " + b.name + " were made for binding API version "
                    + b.apiVersion + ", this xLua has version " + BindingApi.Version + "; generate them again");
            }
            lock (sync)
            {
                if (!registered.Add(b.name)) return;

                // The first registration of a type wins.
                foreach (var kv in b.loaders)
                {
                    if (!loaders.ContainsKey(kv.Key)) loaders.Add(kv.Key, kv.Value);
                }
                foreach (var kv in b.names)
                {
                    if (!names.ContainsKey(kv.Key)) names.Add(kv.Key, kv.Value);
                    addTypeName(kv.Value, kv.Key);
                }
                foreach (var kv in b.interfaceBridges)
                {
                    if (!interfaceBridges.ContainsKey(kv.Key)) interfaceBridges.Add(kv.Key, kv.Value);
                }
                foreach (var kv in b.delegates)
                {
                    if (delegates.ContainsKey(kv.Key)) continue;
                    delegates.Add(kv.Key, kv.Value);
                    delegateOrder.Add(kv);
                }
                if (b.bridgeClasses.Count > 0)
                {
                    bridgeClasses.AddRange(b.bridgeClasses);
                    bridgeMethods = null;
                }
                foreach (var kv in b.structs)
                {
                    if (structTypes.Add(kv.Key)) initers.Add(kv.Value);
                }
                initers.AddRange(b.initers);
                foreach (var kv in b.arrayGetters)
                {
                    if (!arrayGetters.ContainsKey(kv.Key)) arrayGetters.Add(kv.Key, kv.Value);
                }
                if (b.hotfixSlots != null && !hotfixSlots.ContainsKey(b.name)) hotfixSlots.Add(b.name, b.hotfixSlots);
                if (b.hotfixBridgeClass != null)
                {
                    hotfixBridgeClasses.Add(b.hotfixBridgeClass);
                    bridgeMethods = null;
                }
                foreach (var kv in b.arraySetters)
                {
                    if (!arraySetters.ContainsKey(kv.Key)) arraySetters.Add(kv.Key, kv.Value);
                }
                if (arrayGetters.Count > 0) InternalGlobals.genTryArrayGetPtr = TryArrayGet;
                if (arraySetters.Count > 0) InternalGlobals.genTryArraySetPtr = TryArraySet;

                // A unit with only name table entries (the source generator
                // before it writes wrappers) binds no code: the types stay on
                // reflection, or CodeEmit, as without any registration.
                if (!b.BindsCode) return;

                // As with the legacy generated register file, generated code
                // replaces the reflection scan for extension methods, even
                // when it lists none (Utils.GetExtensionMethodsOf).
                var extensionMethodMap = InternalGlobals.extensionMethodMap;
                var merged = extensionMethodMap == null ? new Dictionary<Type, IEnumerable<MethodInfo>>()
                    : new Dictionary<Type, IEnumerable<MethodInfo>>(extensionMethodMap);
                foreach (var kv in b.extensionMethods)
                {
                    IEnumerable<MethodInfo> list;
                    if (merged.TryGetValue(kv.Key, out list))
                    {
                        var all = new List<MethodInfo>(list);
                        all.AddRange(kv.Value);
                        merged[kv.Key] = all;
                    }
                    else
                    {
                        merged.Add(kv.Key, new List<MethodInfo>(kv.Value));
                    }
                }
                InternalGlobals.extensionMethodMap = merged;

                if (b.BindsDelegates) DelegateBridge.Gen_Flag = true;
            }
        }

        // Hosts without [RuntimeInitializeOnLoadMethod]: the first LuaEnv
        // registers every [assembly: GeneratedBindings] unit it finds. Nothing is
        // scanned once something registered.
        internal static void EnsureRegistered()
        {
            if (scanned || registered.Count > 0) return;
            lock (sync)
            {
                if (scanned) return;
                scanned = true;
            }
            ScanAssemblies();
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            // The run-time layer of docs/design/source-generator.md section 5.3:
            // a development player without any registration binds every type by
            // reflection. The Editor reports a missing registration per assembly
            // (XLua.Editor SourceGenChecks) and builds check it too.
            if (registered.Count == 0)
            {
                UnityEngine.Debug.LogWarning("xLua: no generated bindings are registered; C# types are bound through reflection. "
                    + "Generate code, or check that the xLua source generator ran (warning CS9057 means the compiler is older than it needs)");
            }
#endif
        }

        // Registers the [assembly: GeneratedBindings] units of the loaded
        // assemblies; Register ignores those it has seen.
        static void ScanAssemblies()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                object[] attributes;
                try
                {
                    attributes = assembly.GetCustomAttributes(typeof(GeneratedBindingsAttribute), false);
                }
                catch (Exception)
                {
                    continue; // dynamic assemblies (CodeEmit) may not support it
                }
                for (int i = 0; i < attributes.Length; i++)
                {
                    var bindings = ((GeneratedBindingsAttribute)attributes[i]).Bindings;
                    if (bindings == null) continue;
                    var register = bindings.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, System.Type.EmptyTypes, null);
                    if (register != null) register.Invoke(null, null);
                }
            }
        }

        // HotfixDelegateBridge.SetSlot(assembly, id, bridge).
        internal static void SetHotfixSlot(string assembly, int id, DelegateBridge bridge)
        {
            EnsureRegistered();
            Action<int, DelegateBridge> set;
            lock (sync)
            {
                hotfixSlots.TryGetValue(assembly, out set);
            }
            if (set == null)
            {
                // An assembly loaded after the first LuaEnv (hosts without Unity).
                ScanAssemblies();
                lock (sync)
                {
                    hotfixSlots.TryGetValue(assembly, out set);
                }
            }
            if (set == null)
            {
                throw new InvalidOperationException("no generated hotfix bridges are registered for assembly " + assembly
                    + "; its IntKey ids need the source generator's HotfixBridges and an injection with them");
            }
            set(id, bridge);
        }

        // Members the source generator left to reflection, with the reason
        // (BindingApi.RegisterFallback); each logs once, on first use.
        static readonly Dictionary<Type, Dictionary<string, string>> fallbackReasons = new Dictionary<Type, Dictionary<string, string>>();

        internal static void AddFallbackReason(Type type, string name, string reason)
        {
            lock (sync)
            {
                Dictionary<string, string> byName;
                if (!fallbackReasons.TryGetValue(type, out byName)) fallbackReasons.Add(type, byName = new Dictionary<string, string>());
                byName[name] = reason;
            }
        }

        // Called by Utils.LazyReflectionCall when a lazy member is wrapped.
        internal static void ReportFallback(Type type, string name)
        {
            string reason;
            lock (sync)
            {
                Dictionary<string, string> byName;
                if (!fallbackReasons.TryGetValue(type, out byName) || !byName.TryGetValue(name, out reason) || reason == null) return;
                byName[name] = null; // once per member
            }
            string message = "xLua: " + type + "." + name + " is reached through reflection: " + reason;
#if XLUA_GENERAL
            Console.WriteLine("Warning: " + message);
#else
            UnityEngine.Debug.LogWarning(message);
#endif
        }

        // Runs the struct registrations and initers in a new LuaEnv.
        internal static void ApplyTo(LuaEnv env, ObjectTranslator translator)
        {
            for (int i = 0; i < initers.Count; i++)
            {
                initers[i](env, translator);
            }
        }

        internal static bool TryGetLoader(Type type, out Action<RealStatePtr> loader)
        {
            return loaders.TryGetValue(type, out loader);
        }

        internal static bool TryGetName(Type type, out string name)
        {
            return names.TryGetValue(type, out name);
        }

        static void addTypeName(string name, Type type)
        {
            if (!typesByName.ContainsKey(name)) typesByName.Add(name, type);
            if (type.IsGenericType)
            {
                int bracket = name.IndexOf('[');
                if (bracket > 0)
                {
                    string definition = name.Substring(0, bracket);
                    if (!typesByName.ContainsKey(definition)) typesByName.Add(definition, type.GetGenericTypeDefinition());
                }
            }
        }

        // The type registered under a name, or null.
        internal static Type TypeByName(string name)
        {
            Type type;
            return name != null && typesByName.TryGetValue(name, out type) ? type : null;
        }

        // The name table entry of a type, when the name is the type's own: null
        // when the type has none, or when another type registered the same name
        // first (the same type name in two assemblies), so that two types never
        // share a metatable key or a CS path.
        internal static string LuaNameOf(Type type)
        {
            string name;
            Type owner;
            if (type != null && names.TryGetValue(type, out name) && typesByName.TryGetValue(name, out owner) && owner == type) return name;
            return null;
        }

        internal static bool TryGetInterfaceBridge(Type type, out Func<int, LuaEnv, LuaBase> creator)
        {
            return interfaceBridges.TryGetValue(type, out creator);
        }

        internal static bool TryGetDelegateCreator(Type type, out Func<DelegateBridge, Delegate> creator)
        {
            return delegates.TryGetValue(type, out creator);
        }

        // For a delegate type without a Delegate entry: the bridge of the first
        // registered delegate type whose Invoke has the same parameters
        // (Utils.IsParamsMatch, the rule of the legacy lookup by signature), or
        // null. The bridge method comes from a delegate the registered creator
        // makes (Delegate.Method), not from a lookup by name, so it survives
        // renaming. Generated creators make delegates closed over the bridge;
        // for another kind of creator the result is null.
        internal static Func<DelegateBridge, Delegate> CreatorBySignature(Type delegateType, MethodInfo invoke)
        {
            KeyValuePair<Type, Func<DelegateBridge, Delegate>>[] registeredDelegates;
            lock (sync)
            {
                registeredDelegates = delegateOrder.ToArray();
            }
            foreach (var kv in registeredDelegates)
            {
                if (!Utils.IsParamsMatch(invoke, kv.Key.GetMethod("Invoke"))) continue;
                var create = kv.Value;
                MethodInfo method = null;
                return (DelegateBridge bridge) =>
                {
                    if (method == null)
                    {
                        Delegate sample = create(bridge);
                        if (sample == null || !ReferenceEquals(sample.Target, bridge)) return null;
                        method = sample.Method;
                    }
                    return Delegate.CreateDelegate(delegateType, bridge, method);
                };
            }
            return null;
        }

        // The registered bridge methods, for the lookup by signature.
        internal static MethodInfo[] BridgeMethods()
        {
            var methods = bridgeMethods;
            if (methods != null) return methods;
            lock (sync)
            {
                var found = new List<MethodInfo>();
                var classes = new List<Type>(bridgeClasses);
                classes.AddRange(hotfixBridgeClasses);
                foreach (var bridgeClass in classes)
                {
                    foreach (var method in bridgeClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsGenericMethodDefinition || !method.Name.StartsWith("__Gen_Delegate_Imp")) continue;
                        var parameters = method.GetParameters();
                        if (parameters.Length > 0 && parameters[0].ParameterType == typeof(DelegateBridge))
                        {
                            found.Add(method);
                        }
                    }
                }
                methods = found.ToArray();
                bridgeMethods = methods;
                return methods;
            }
        }

        static bool TryArrayGet(Type type, RealStatePtr L, ObjectTranslator translator, object obj, int index)
        {
            ArrayGet get;
            if (!arrayGetters.TryGetValue(type, out get)) return false;
            get(L, translator, obj, index);
            return true;
        }

        static bool TryArraySet(Type type, RealStatePtr L, ObjectTranslator translator, object obj, int arrayIndex, int valueIndex)
        {
            ArraySet set;
            if (!arraySetters.TryGetValue(type, out set)) return false;
            set(L, translator, obj, arrayIndex, valueIndex);
            return true;
        }

        internal static int AllocateSlot(Type type)
        {
            if (type == null) throw new ArgumentNullException("type");
            lock (sync)
            {
                int slot;
                if (!slotOfType.TryGetValue(type, out slot))
                {
                    slot = slotTypes.Count;
                    slotTypes.Add(type);
                    slotOfType.Add(type, slot);
                }
                return slot;
            }
        }

        static Type slotType(int slot)
        {
            lock (sync)
            {
                return slotTypes[slot];
            }
        }

        static int[] grow(int[] values, int slot)
        {
            if (slot < values.Length) return values;
            var grown = new int[Math.Max(slot + 1, Math.Max(slotTypes.Count, values.Length * 2))];
            Array.Copy(values, grown, values.Length);
            for (int i = values.Length; i < grown.Length; i++) grown[i] = -1;
            return grown;
        }

        internal static int TypeIdSlow(ObjectTranslator translator, RealStatePtr L, int slot)
        {
            Type type = slotType(slot);
            bool is_first;
            int id = translator.getTypeId(L, type, out is_first);
            translator.bindingTypeIds = grow(translator.bindingTypeIds, slot);
            translator.bindingTypeIds[slot] = id;
            return id;
        }

        // The cache is a table of its own in the registry. It used to be the
        // enum's class table (CS.E), keyed by the value, where a script could
        // replace an entry with rawset(CS.E, 1, x) and change what C# pushes
        // (docs/design/bridge-hardening.md, finding D).
        internal static int EnumCacheRefSlow(ObjectTranslator translator, RealStatePtr L, int slot)
        {
            LuaAPI.lua_newtable(L);
            int r = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            translator.bindingEnumRefs = grow(translator.bindingEnumRefs, slot);
            translator.bindingEnumRefs[slot] = r;
            return r;
        }
    }
}
