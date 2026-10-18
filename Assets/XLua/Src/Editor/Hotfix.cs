/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#if HOTFIX_ENABLE
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

#if XLUA_GENERAL
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
#if !HOTFIX_SYMBOLS_DISABLE
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
#endif
#endif

#if !XLUA_GENERAL
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.Diagnostics;
#if UNITY_2019_1_OR_NEWER
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif
#endif

namespace XLua
{
    public static class HotfixConfig
    {
        //Return -1 when the attribute is absent.
        static int getHotfixType(MemberInfo memberInfo)
        {
            try
            {
                foreach (var ca in memberInfo.GetCustomAttributes(false))
                {
                    var ca_type = ca.GetType();
                    if (ca_type.ToString() == "XLua.HotfixAttribute")
                    {
                        return (int)(ca_type.GetProperty("Flag").GetValue(ca, null));
                    }
                }
            }
            catch { }
            return -1;
        }

        static void mergeConfig(MemberInfo test, Type cfg_type, Func<IEnumerable<Type>> get_cfg, Action<Type, int> on_cfg)
        {
            int hotfixType = getHotfixType(test);
            if (-1 == hotfixType || !typeof(IEnumerable<Type>).IsAssignableFrom(cfg_type))
            {
                return;
            }

            foreach (var type in get_cfg())
            {
                if (!type.IsDefined(typeof(ObsoleteAttribute), false)
                    && !type.IsEnum && !typeof(Delegate).IsAssignableFrom(type)
                    && (!type.IsGenericType || type.IsGenericTypeDefinition))
                {
                    if ((type.Namespace == null || (type.Namespace != "XLua" && !type.Namespace.StartsWith("XLua."))))
                    {
                        on_cfg(type, hotfixType);
                    }
                }
            }
        }

        public static void GetConfig(Dictionary<string, int> hotfixCfg, IEnumerable<Type> cfg_check_types)
        {
            if (cfg_check_types != null)
            {
                Action<Type, int> on_cfg = (type, hotfixType) =>
                {
                    string key = type.FullName.Replace('+', '/');
                    if (!hotfixCfg.ContainsKey(key))
                    {
                        hotfixCfg.Add(key, hotfixType);
                    }
                };
                foreach (var type in cfg_check_types.Where(t => !t.IsGenericTypeDefinition && t.IsAbstract && t.IsSealed))
                {
                    var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    foreach (var field in type.GetFields(flags))
                    {
                        mergeConfig(field, field.FieldType, () => field.GetValue(null) as IEnumerable<Type>, on_cfg);
                    }
                    foreach (var prop in type.GetProperties(flags))
                    {
                        mergeConfig(prop, prop.PropertyType, () => prop.GetValue(null, null) as IEnumerable<Type>, on_cfg);
                    }
                }
            }
        }

        // The project folder. Unity compiles every script assembly, with or
        // without an asmdef, into <project>/Library/ScriptAssemblies, xLua's
        // own among them unless it is precompiled. It was taken from
        // Assembly-CSharp's path, which does not exist when every script is in
        // an asmdef.
        public static string GetProjectPath()
        {
#if XLUA_GENERAL
            // XLuaHotfixInject is built without the xLua runtime.
            return Path.GetFullPath(".");
#else
            Match match = Regex.Match(typeof(LuaEnv).Module.FullyQualifiedName,
                @"^(.*)[\\/]Library[\\/]ScriptAssemblies[\\/][^\\/]+$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return Path.GetDirectoryName(Path.GetFullPath(UnityEngine.Application.dataPath));
#endif
        }

        // Whether the assembly was built from the project (script assemblies and
        // precompiled DLLs inside it), not from Unity or the .NET libraries.
        public static bool IsProjectAssembly(Assembly assembly, string projectPath)
        {
            try
            {
                return assembly.ManifestModule.FullyQualifiedName.IndexOf(projectPath) == 0;
            }
            catch
            {
                return false; // dynamic assemblies have no file
            }
        }

        public static List<Assembly> GetHotfixAssembly()
        {
            var projectPath = GetProjectPath();

            List<Type> types = new List<Type>();
            Action<Type, int> on_cfg = (type, hotfixType) =>
            {
                types.Add(type);
            };

            foreach (var assmbly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // [assembly: Hotfix(typeof(T), flags)], the form the source
                    // generator reads; the injector reads it too.
                    foreach (var ca in assmbly.GetCustomAttributes(false))
                    {
                        if (ca.GetType().ToString() != "XLua.HotfixAttribute") continue;
                        var configured = ca.GetType().GetProperty("Type") == null ? null : ca.GetType().GetProperty("Type").GetValue(ca, null) as Type;
                        if (configured != null) types.Add(configured);
                    }
                    foreach (var type in (from type in assmbly.GetTypes()
                                          where !type.IsGenericTypeDefinition
                                          select type))
                    {
                        if (getHotfixType(type) != -1)
                        {
                            types.Add(type);
                        }
                        else
                        {
                            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                            foreach (var field in type.GetFields(flags))
                            {
                                mergeConfig(field, field.FieldType, () => field.GetValue(null) as IEnumerable<Type>, on_cfg);
                            }
                            foreach (var prop in type.GetProperties(flags))
                            {
                                mergeConfig(prop, prop.PropertyType, () => prop.GetValue(null, null) as IEnumerable<Type>, on_cfg);
                            }
                        }
                    }
                }
                catch { } // Do not abort processing if a project contains an invalid DLL.
            }
            return types.Select(t => t.Assembly).Distinct()
                .Where(a => IsProjectAssembly(a, projectPath))
                .ToList();
        }

        public static List<string> GetHotfixAssemblyPaths()
        {
            return GetHotfixAssembly().Select(a => a.ManifestModule.FullyQualifiedName).Distinct().ToList();
        }
    }

    // With several hotfix assemblies the injector writes one IntKey id map per
    // assembly (hotfix_id_map_<assembly>.lua.txt), while Lua looks for one module,
    // hotfix_id_map (xlua.util.auto_id_map). Merge writes that module: a chunk that
    // holds every map and returns their union, so it needs no other module at run
    // time (the per-assembly names need not be loadable: a loader that maps dots
    // to folders cannot load hotfix_id_map_Game.Core).
    internal static class HotfixIdMap
    {
        // Returns the maps that do not exist; they are left out.
        public static List<string> Merge(string outputPath, IEnumerable<string> mapPaths)
        {
            var missing = new List<string>();
            using (var writer = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.Write)))
            {
                writer.WriteLine("-- Written by the hotfix injector: the IntKey id maps of the injected assemblies,");
                writer.WriteLine("-- merged so that require 'hotfix_id_map' (xlua.util.auto_id_map) finds them all.");
                writer.WriteLine("local merged = {}");
                writer.WriteLine("local function add(map)");
                writer.WriteLine("    for type_name, methods in pairs(map) do");
                writer.WriteLine("        merged[type_name] = methods");
                writer.WriteLine("    end");
                writer.WriteLine("end");
                foreach (var mapPath in mapPaths)
                {
                    if (!File.Exists(mapPath))
                    {
                        missing.Add(mapPath);
                        continue;
                    }
                    writer.WriteLine("-- " + Path.GetFileName(mapPath).Replace('\n', ' ').Replace('\r', ' '));
                    writer.WriteLine("add((function()");
                    writer.WriteLine(File.ReadAllText(mapPath).TrimEnd());
                    writer.WriteLine("end)())");
                }
                writer.WriteLine("return merged");
            }
            return missing;
        }
    }
}
#if XLUA_GENERAL

namespace XLua
{
    [Flags]
    enum HotfixFlagInTool
    {
        Stateless = 0,
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

    static class ExtentionMethods
    {
        public static bool HasFlag(this HotfixFlagInTool toCheck, HotfixFlagInTool flag)
        {
            return (toCheck != HotfixFlagInTool.Stateless) && ((toCheck & flag) == flag);
        }

        public static MethodReference MakeGenericMethod(this MethodReference self, params TypeReference[] arguments)
        {
            if (self.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

            var instance = new GenericInstanceMethod(self);
            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        public static FieldReference GetGeneric(this FieldDefinition definition)
        {
            if (definition.DeclaringType.HasGenericParameters)
            {
                var declaringType = new GenericInstanceType(definition.DeclaringType);
                foreach (var parameter in definition.DeclaringType.GenericParameters)
                {
                    declaringType.GenericArguments.Add(parameter);
                }
                return new FieldReference(definition.Name, definition.FieldType, declaringType);
            }

            return definition;
        }

        public static TypeReference GetGeneric(this TypeDefinition definition)
        {
            if (definition.HasGenericParameters)
            {
                var genericInstanceType = new GenericInstanceType(definition);
                foreach (var parameter in definition.GenericParameters)
                {
                    genericInstanceType.GenericArguments.Add(parameter);
                }
                return genericInstanceType;
            }

            return definition;
        }

        public static TypeReference TryImport(this ModuleDefinition module, TypeReference type)
        {
            if (module.Assembly.FullName == type.Module.Assembly.FullName
                && module.FileName == type.Module.FileName)
            {
                return type;
            }
            else
            {
                return module.ImportReference(type);
            }
        }

        public static MethodReference TryImport(this ModuleDefinition module, MethodReference method)
        {
            if (module.Assembly.FullName == method.Module.Assembly.FullName
                && module.FileName == method.Module.FileName)
            {
                return method;
            }
            else
            {
                return module.ImportReference(method);
            }
        }
    }

    public class Hotfix
    {
        // [Obfuscation(Exclude = true)] on what the injector adds and Lua finds
        // by name (the __Hotfix fields of field-mode xlua.hotfix, the flag
        // type), for obfuscators that read the attribute and run after the
        // injector; obfuz reads its rule file instead (ObfuzRules.cs). The
        // attribute comes from the module's own core library.
        // docs/design/source-generator.md, milestone M8.
        internal static void ExcludeFromObfuscation(ModuleDefinition module, Mono.Cecil.ICustomAttributeProvider target)
        {
            var attributeType = new TypeReference("System.Reflection", "ObfuscationAttribute", module, module.TypeSystem.CoreLibrary);
            var constructor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
            var attribute = new CustomAttribute(constructor);
            attribute.Properties.Add(new Mono.Cecil.CustomAttributeNamedArgument("Exclude",
                new CustomAttributeArgument(module.TypeSystem.Boolean, true)));
            target.CustomAttributes.Add(attribute);
        }

        private TypeReference objType = null;
        private TypeReference delegateBridgeType = null;
        private AssemblyDefinition injectAssembly = null;

        private MethodReference delegateBridgeGetter = null;
        private MethodReference hotfixFlagGetter = null;
        private MethodReference invokeSessionStart = null;
        private MethodReference functionInvoke = null;
        private MethodReference invokeSessionEnd = null;
        private MethodReference invokeSessionEndWithResult = null;
        private MethodReference inParam = null;
        private MethodReference inParams = null;
        private MethodReference outParam = null;

        private Dictionary<string, int> hotfixCfg = null;
        private List<MethodDefinition> hotfixBridgesDef = null;
        private Dictionary<MethodDefinition, MethodDefinition> hotfixBridgeToDelegate = null;

        private List<MethodDefinition> bridgeIndexByKey = null;

        private bool isTheSameAssembly = false;

        // The source generator's XLua.Generated.<Id>.HotfixBridges of the injected
        // assembly (docs/design/source-generator.md, section 6): its bridges are
        // called directly, and its Get(id) holds the assembly's own IntKey slots,
        // so IntKey works in every assembly, not only in xLua's.
        private TypeDefinition localBridgesType = null;
        private MethodReference slotGetter = null;

        // IntKey types that get field mode because they are not in the xLua assembly.
        private List<string> intKeyFallbackTypes = null;
        private string xluaAssemblyName = null;

        // Whether the injected assembly numbers its IntKey ids in the process-wide
        // list (HotfixDelegateBridge.Get/Set), and the assembly that owns that list.
        private bool sharedIntKey = false;
        private string sharedIntKeyOwner = null;

        private int delegateId = 0;

        // Assemblies read only for their generated hotfix bridges (see Init).
        private List<AssemblyDefinition> bridgeAssemblies = new List<AssemblyDefinition>();

        static List<MethodDefinition> HotfixBridgesOf(IEnumerable<AssemblyDefinition> assemblies)
        {
            return (from assembly in assemblies
                    from type in assembly.MainModule.Types
                    where type.FullName == "XLua.CSObjectWrap.DelegatesGensBridge"
                    from method in type.Methods
                    where method.IsStatic && method.Name.StartsWith("__Gen_Delegate_Imp")
                        && method.Parameters.Count > 0
                        && method.Parameters[0].ParameterType.FullName == "XLua.DelegateBridge"
                    select method).ToList();
        }

        public void Init(AssemblyDefinition injectAssembly, AssemblyDefinition xluaAssembly, IEnumerable<string> searchDirectorys, Dictionary<string, int> hotfixCfg)
        {
            isTheSameAssembly = injectAssembly == xluaAssembly;
            intKeyFallbackTypes = new List<string>();
            xluaAssemblyName = xluaAssembly.Name.Name;
            this.injectAssembly = injectAssembly;
            this.hotfixCfg = hotfixCfg;
            var injectModule = injectAssembly.MainModule;
            objType = injectModule.TypeSystem.Object;

            var delegateBridgeTypeDef = xluaAssembly.MainModule.Types.Single(t => t.FullName == "XLua.DelegateBridge");
            delegateBridgeType = injectModule.TryImport(delegateBridgeTypeDef);
            delegateBridgeGetter = injectModule.TryImport(xluaAssembly.MainModule.Types.Single(t => t.FullName == "XLua.HotfixDelegateBridge")
                .Methods.Single(m => m.Name == "Get"));
            hotfixFlagGetter = injectModule.TryImport(xluaAssembly.MainModule.Types.Single(t => t.FullName == "XLua.HotfixDelegateBridge")
                .Methods.Single(m => m.Name == "xlua_get_hotfix_flag"));

            //luaFunctionType = assembly.MainModule.Types.Single(t => t.FullName == "XLua.LuaFunction");
            invokeSessionStart = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "InvokeSessionStart"));
            functionInvoke = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "Invoke"));
            invokeSessionEnd = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "InvokeSessionEnd"));
            invokeSessionEndWithResult = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "InvokeSessionEndWithResult"));
            inParam = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "InParam"));
            inParams = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "InParams"));
            outParam = injectModule.TryImport(delegateBridgeTypeDef.Methods.Single(m => m.Name == "OutParam"));

            // Generated bridges are static, `__Gen_Delegate_Imp<n>(this DelegateBridge, ...)`,
            // in a class of the generated code (XLua.CSObjectWrap.DelegatesGensBridge),
            // which compiles into the xLua assembly or the injected one.
            localBridgesType = injectModule.Types.FirstOrDefault(t => t.Name == "HotfixBridges" && t.Namespace.StartsWith("XLua.Generated."));
            var slotGetterDef = localBridgesType == null ? null : localBridgesType.Methods.FirstOrDefault(m => m.Name == "Get" && m.IsStatic
                && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32" && m.ReturnType.FullName == "XLua.DelegateBridge");
            slotGetter = slotGetterDef;
            // The injected assembly's own bridges (source generator) first, then the
            // legacy generator's (XLua.CSObjectWrap.DelegatesGensBridge).
            hotfixBridgesDef = localBridgesType == null ? new List<MethodDefinition>()
                : (from method in localBridgesType.Methods
                   where method.IsStatic && method.Name.StartsWith("__Gen_Delegate_Imp")
                       && method.Parameters.Count > 0
                       && method.Parameters[0].ParameterType.FullName == "XLua.DelegateBridge"
                   select method).ToList();
            hotfixBridgesDef.AddRange(HotfixBridgesOf(new[] { xluaAssembly, injectAssembly }.Distinct()));
            // With xLua's asmdef the generated code (Assets/XLua/Gen) compiles into
            // Assembly-CSharp, which for a game asmdef is neither of the two: take
            // the bridges from the script assemblies next to the injected one, as
            // the injected assembly took them from xLua's Assembly-CSharp before.
            if (hotfixBridgesDef.Count == 0)
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(injectAssembly.MainModule.FileName));
                foreach (var file in Directory.GetFiles(folder, "*.dll").Concat(Directory.GetFiles(folder, "*.exe")))
                {
                    var full = Path.GetFullPath(file);
                    if (full == Path.GetFullPath(injectAssembly.MainModule.FileName) || full == Path.GetFullPath(xluaAssembly.MainModule.FileName))
                    {
                        continue;
                    }
                    AssemblyDefinition other;
                    try
                    {
                        other = AssemblyDefinition.ReadAssembly(full, new ReaderParameters { InMemory = true });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    var bridges = HotfixBridgesOf(new[] { other });
                    if (bridges.Count == 0)
                    {
                        other.Dispose();
                        continue;
                    }
                    bridgeAssemblies.Add(other);
                    hotfixBridgesDef.AddRange(bridges);
                }
            }
            // The process-wide IntKey list belongs to one assembly, the one that holds
            // the legacy generated bridges (Generate Code output, DelegatesGensBridge):
            // xLua's own assembly when everything is in Assembly-CSharp, and
            // Assembly-CSharp (Assets/XLua/Gen) when xLua has its asmdefs. Ids number
            // from 0 in each injected assembly, so a second assembly in the same list
            // would collide. Without generated bridges anywhere (only Inline types)
            // xLua's assembly keeps it, as before.
            var legacyOwner = hotfixBridgesDef.Where(m => m.DeclaringType.FullName == "XLua.CSObjectWrap.DelegatesGensBridge")
                .Select(m => m.Module.Assembly).FirstOrDefault();
            sharedIntKeyOwner = legacyOwner != null ? legacyOwner.Name.Name : xluaAssemblyName;
            sharedIntKey = legacyOwner != null ? legacyOwner == injectAssembly : isTheSameAssembly;
            hotfixBridgeToDelegate = new Dictionary<MethodDefinition, MethodDefinition>();
            delegateId = 0;

            //hotfixBridges = hotfixBridgesDef.Select(m => injectModule.TryImport(m)).ToList();

            bridgeIndexByKey = new List<MethodDefinition>();

            var resolverOfInjectAssembly = injectAssembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
            var resolverOfXluaAssembly = xluaAssembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
            if (!isTheSameAssembly)
            {
                resolverOfXluaAssembly.AddSearchDirectory(Path.GetDirectoryName(injectAssembly.MainModule.FileName));
            }
            Action<string> addSearchDirectory = (string dir) =>
            {
                resolverOfInjectAssembly.AddSearchDirectory(dir);
                if (!isTheSameAssembly)
                {
                    resolverOfXluaAssembly.AddSearchDirectory(dir);
                }
            };
            addSearchDirectory("./Library/ScriptAssemblies/");
            foreach (var path in
                (from asm in AppDomain.CurrentDomain.GetAssemblies() select asm.ManifestModule.FullyQualifiedName)
                 .Distinct())
            {
                try
                {
                    addSearchDirectory(Path.GetDirectoryName(path));
                }
                catch(Exception)
                {

                }
            }

            if (searchDirectorys != null)
            {
                foreach(var directory in searchDirectorys.Distinct())
                {
                    addSearchDirectory(directory);
                }
            }

        }

        static string getAssemblyFullName(IMetadataScope scope)
        {
            if (scope == null) return null;
            switch(scope.MetadataScopeType)
            {
                case MetadataScopeType.ModuleDefinition:
                    {
                        ModuleDefinition md = scope as ModuleDefinition;
                        return md.Assembly.FullName;
                    }
                case MetadataScopeType.AssemblyNameReference:
                    {
                        AssemblyNameReference anr = scope as AssemblyNameReference;
                        return anr.FullName;
                    }
            }
            return null;
        }

        // Cecil twin of XLua.Utils.IsInParameter: a C# `in` parameter (by-ref
        // with IsReadOnlyAttribute) or C# 12 `ref readonly` parameter (by-ref
        // with RequiresLocationAttribute) is an input only, so a patch's extra
        // Lua results are never written back to it. [In] ref stays a normal ref.
        static CustomAttribute readOnlyByRefAttribute(ParameterDefinition parameter)
        {
            if (!parameter.ParameterType.IsByReference || parameter.IsOut) return null;
            return parameter.CustomAttributes.FirstOrDefault(ca =>
                ca.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"
                || ca.AttributeType.FullName == "System.Runtime.CompilerServices.RequiresLocationAttribute");
        }

        static bool isInParameter(ParameterDefinition parameter)
        {
            return readOnlyByRefAttribute(parameter) != null;
        }

        // A parameter for a method the injector creates from `source`: out stays
        // as before, and an `in` or `ref readonly` parameter keeps [In] and its
        // attribute, which XLua.Utils.IsParamsMatch needs to pair it with the
        // `in` bridge.
        static ParameterDefinition copyParameter(string name, ParameterDefinition source, TypeReference type, ModuleDefinition module)
        {
            var parameter = new ParameterDefinition(name, source.IsOut ? Mono.Cecil.ParameterAttributes.Out : Mono.Cecil.ParameterAttributes.None, type);
            var attribute = readOnlyByRefAttribute(source);
            if (attribute != null)
            {
                parameter.Attributes |= Mono.Cecil.ParameterAttributes.In;
                parameter.CustomAttributes.Add(new CustomAttribute(module.ImportReference(attribute.Constructor)));
            }
            return parameter;
        }

        static bool isSameType(TypeReference left, TypeReference right)
        {
            if (left.FullName != right.FullName)
            {
                return false;
            }

            //Do not compare TypeReference.Module.FullyQualifiedName.
            if (left.Module.Assembly.FullName == right.Module.Assembly.FullName)
            {
                return true;
            }
            else
            {
                var lafn = getAssemblyFullName(left.Scope);
                var rafn = getAssemblyFullName(right.Scope);
                if (lafn != null && lafn == rafn)
                {
                    return true;
                }
                var lr = left.Resolve();
                var rr = right.Resolve();
                if (lr == null || rr == null) return false;
                return lr.Module.Assembly.FullName == rr.Module.Assembly.FullName;
            }
        }

        MethodDefinition createDelegateFor(MethodDefinition method, AssemblyDefinition assembly, string delegateName, bool ignoreValueType)
        {
            var voidType = assembly.MainModule.TypeSystem.Void;
            var objectType = assembly.MainModule.TypeSystem.Object;
            var nativeIntType = assembly.MainModule.TypeSystem.IntPtr;
            var asyncResultType = assembly.MainModule.ImportReference(typeof(IAsyncResult));
            var asyncCallbackType = assembly.MainModule.ImportReference(typeof(AsyncCallback));

            Mono.Cecil.MethodAttributes delegateMethodAttributes = Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.VtableLayoutMask;

            var delegateDef = new TypeDefinition("XLua", delegateName, Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Public,
                    assembly.MainModule.ImportReference(typeof(MulticastDelegate)));
            List<TypeReference> argTypes = new List<TypeReference>();
            TypeReference self = null;
            if (!method.IsStatic)
            {
                self = (!ignoreValueType && method.DeclaringType.IsValueType) ? method.DeclaringType : objType;
            }
            foreach(var parameter in method.Parameters)
            {
                bool isparam = parameter.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Name == "ParamArrayAttribute") != null;
                argTypes.Add((isparam || parameter.ParameterType.IsByReference || (!ignoreValueType && parameter.ParameterType.IsValueType)) ? parameter.ParameterType : objType);
            }

            var constructor = new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, voidType);
            constructor.Parameters.Add(new ParameterDefinition("objectInstance", Mono.Cecil.ParameterAttributes.None, objectType));
            constructor.Parameters.Add(new ParameterDefinition("functionPtr", Mono.Cecil.ParameterAttributes.None, nativeIntType));
            constructor.ImplAttributes = Mono.Cecil.MethodImplAttributes.Runtime;
            delegateDef.Methods.Add(constructor);

            var beginInvoke = new MethodDefinition("BeginInvoke", delegateMethodAttributes, asyncResultType);
            if (self != null)
            {
                beginInvoke.Parameters.Add(new ParameterDefinition(self));
            }
            for (int i = 0; i < argTypes.Count; i++)
            {
                beginInvoke.Parameters.Add(copyParameter(method.Parameters[i].Name, method.Parameters[i], argTypes[i], assembly.MainModule));
            }
            beginInvoke.Parameters.Add(new ParameterDefinition("callback", Mono.Cecil.ParameterAttributes.None, asyncCallbackType));
            beginInvoke.Parameters.Add(new ParameterDefinition("object", Mono.Cecil.ParameterAttributes.None, objectType));
            beginInvoke.ImplAttributes = Mono.Cecil.MethodImplAttributes.Runtime;
            delegateDef.Methods.Add(beginInvoke);

            var endInvoke = new MethodDefinition("EndInvoke", delegateMethodAttributes, method.ReturnType);
            for (int i = 0; i < argTypes.Count; i++)
            {
                if (argTypes[i].IsByReference)
                {
                    endInvoke.Parameters.Add(copyParameter(method.Parameters[i].Name, method.Parameters[i], argTypes[i], assembly.MainModule));
                }
            }
            endInvoke.Parameters.Add(new ParameterDefinition("result", Mono.Cecil.ParameterAttributes.None, asyncResultType));
            endInvoke.ImplAttributes = Mono.Cecil.MethodImplAttributes.Runtime;
            delegateDef.Methods.Add(endInvoke);

            var invoke = new MethodDefinition("Invoke", delegateMethodAttributes, method.ReturnType);
            if (self != null)
            {
                invoke.Parameters.Add(new ParameterDefinition(self));
            }
            for(int i = 0; i < argTypes.Count; i++)
            {
                invoke.Parameters.Add(copyParameter(method.Parameters[i].Name, method.Parameters[i], argTypes[i], assembly.MainModule));
            }
            invoke.ImplAttributes = Mono.Cecil.MethodImplAttributes.Runtime;
            delegateDef.Methods.Add(invoke);

            assembly.MainModule.Types.Add(delegateDef);

            return invoke;
        }

        MethodDefinition getDelegateInvokeFor(MethodDefinition method, MethodDefinition bridgeDef, bool ignoreValueType)
        {
            MethodDefinition ret;
            if (!hotfixBridgeToDelegate.TryGetValue(bridgeDef, out ret))
            {
                ret = createDelegateFor(method, injectAssembly, ("__XLua_Gen_Delegate" + (delegateId++)), ignoreValueType);
                hotfixBridgeToDelegate.Add(bridgeDef, ret);
            }

            return ret;
        }

        bool findHotfixDelegate(MethodDefinition method, out MethodReference invoke, HotfixFlagInTool hotfixType)
        {
            bool ignoreValueType = hotfixType.HasFlag(HotfixFlagInTool.ValueTypeBoxing);

            bool isIntKey = hotfixType.HasFlag(HotfixFlagInTool.IntKey) && !method.DeclaringType.HasGenericParameters && intKeyAvailable;

            bool isAdaptByDelegate = !isIntKey && hotfixType.HasFlag(HotfixFlagInTool.AdaptByDelegate);

            for (int i = 0; i < hotfixBridgesDef.Count; i++)
            {
                MethodDefinition hotfixBridgeDef = hotfixBridgesDef[i];
                var returnType = method.ReturnType;
                if (isSameType(returnType, hotfixBridgeDef.ReturnType))
                {
                    var parametersOfDelegate = hotfixBridgeDef.Parameters;
                    // The first parameter of a static bridge is the DelegateBridge.
                    int bridgeOffset = hotfixBridgeDef.IsStatic ? 1 : 0;
                    int compareOffset = bridgeOffset;
                    if (!method.IsStatic)
                    {
                        var typeOfSelf = (!ignoreValueType && method.DeclaringType.IsValueType) ? method.DeclaringType : objType;
                        if ((parametersOfDelegate.Count == bridgeOffset) || parametersOfDelegate[bridgeOffset].ParameterType.IsByReference || !isSameType(typeOfSelf, parametersOfDelegate[bridgeOffset].ParameterType))
                        {
                            continue;
                        }
                        compareOffset++;
                    }
                    if (method.Parameters.Count != (parametersOfDelegate.Count - compareOffset))
                    {
                        continue;
                    }
                    bool paramMatch = true;
                    for (int j = 0; j < method.Parameters.Count; j++)
                    {
                        var param_left = method.Parameters[j];
                        var param_right = parametersOfDelegate[compareOffset++];
                        if (param_left.IsOut != param_right.IsOut
                            || param_left.ParameterType.IsByReference != param_right.ParameterType.IsByReference
                            || isInParameter(param_left) != isInParameter(param_right))
                        {
                            paramMatch = false;
                            break;
                        }
                        if (!ignoreValueType && param_left.ParameterType.IsValueType != param_right.ParameterType.IsValueType)
                        {
                            paramMatch = false;
                            break;
                        }
						bool isparam = param_left.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Name == "ParamArrayAttribute") != null;
						var type_left = (isparam || param_left.ParameterType.IsByReference || (!ignoreValueType && param_left.ParameterType.IsValueType)) ? param_left.ParameterType : objType;
                        if (!isSameType(type_left, param_right.ParameterType))
                        {
                            paramMatch = false;
                            break;
                        }
                    }

                    if (!paramMatch)
                    {
                        continue;
                    }
                    // A bridge in the injected assembly is called directly; one in
                    // another assembly through a delegate type created here.
                    bool local = hotfixBridgeDef.Module == injectAssembly.MainModule;
                    invoke = (local && !isAdaptByDelegate) ? hotfixBridgeDef : getDelegateInvokeFor(method, hotfixBridgeDef, ignoreValueType);
                    return true;
                }
            }
            invoke = null;
            return false;
        }

        // IntKey needs ids that index one table: the injected assembly's own
        // slots (HotfixBridges.Get), or the process-wide list when the injected
        // assembly owns it (sharedIntKey).
        bool intKeyAvailable
        {
            get { return slotGetter != null || sharedIntKey; }
        }

        static bool hasGenericParameter(TypeReference type)
        {
            if (type.HasGenericParameters)
            {
                return true;
            }
            if (type.IsByReference)
            {
                return hasGenericParameter(((ByReferenceType)type).ElementType);
            }
            if (type.IsArray)
            {
                return hasGenericParameter(((ArrayType)type).ElementType);
            }
            if (type.IsGenericInstance)
            {
                foreach (var typeArg in ((GenericInstanceType)type).GenericArguments)
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

        static bool hasGenericParameter(MethodDefinition method)
        {
            if (!method.IsStatic && hasGenericParameter(method.DeclaringType)) return true;
            return hasGenericParameterSkipDelaringType(method);
        }

        static bool hasGenericParameterSkipDelaringType(MethodDefinition method)
        {
            if (method.HasGenericParameters) return true;
            //if (!method.IsStatic && hasGenericParameter(method.DeclaringType)) return true;
            if (hasGenericParameter(method.ReturnType)) return true;
            foreach (var paramInfo in method.Parameters)
            {
                if (hasGenericParameter(paramInfo.ParameterType)) return true;
            }
            return false;
        }

        static bool isNoPublic(TypeReference type)
        {
            if (type.IsByReference)
            {
                return isNoPublic(((ByReferenceType)type).ElementType);
            }
            if (type.IsArray)
            {
                return isNoPublic(((ArrayType)type).ElementType);
            }
            else
            {
                if (type.IsGenericInstance)
                {
                    foreach (var typeArg in ((GenericInstanceType)type).GenericArguments)
                    {
                        if (isNoPublic(typeArg))
                        {
                            return true;
                        }
                    }
                }

                var resolveType = type.Resolve();
                if ((!type.IsNested && !resolveType.IsPublic) || (type.IsNested && !resolveType.IsNestedPublic))
                {
                    return true;
                }
                if (type.IsNested)
                {
                    var parent = type.DeclaringType;
                    while (parent != null)
                    {
                        var resolveParent = parent.Resolve();
                        if ((!parent.IsNested && !resolveParent.IsPublic) || (parent.IsNested && !resolveParent.IsNestedPublic))
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

        static bool genericInOut(MethodDefinition method, HotfixFlagInTool hotfixType)
        {
            bool ignoreValueType = hotfixType.HasFlag(HotfixFlagInTool.ValueTypeBoxing);

            if (hasGenericParameter(method.ReturnType) || isNoPublic(method.ReturnType))
            {
                return true;
            }
            var parameters = method.Parameters;

            if (!method.IsStatic 
                && (hasGenericParameter(method.DeclaringType) || ((!ignoreValueType && method.DeclaringType.IsValueType) && isNoPublic(method.DeclaringType))))
            {
                    return true;
            }
            for (int i = 0; i < parameters.Count; i++)
            {
                if ( hasGenericParameter(parameters[i].ParameterType) || (((!ignoreValueType && parameters[i].ParameterType.IsValueType) || parameters[i].ParameterType.IsByReference || parameters[i].CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.ParamArrayAttribute")) && isNoPublic(parameters[i].ParameterType)))
                {
                    return true;
                }
            }
            return false;
        }

        public bool InjectType(TypeReference hotfixAttributeType, TypeDefinition type)
        {
            foreach(var nestedTypes in type.NestedTypes)
            {
                if (!InjectType(hotfixAttributeType, nestedTypes))
                {
                    return false;
                }
            }
            if (type.Name.Contains("<") || type.IsInterface || type.Methods.Count == 0) // skip anonymous type and interface
            {
                return true;
            }
            // By name: in an assembly other than xLua's the attribute is a
            // reference to xLua's type, never the same object as its definition.
            CustomAttribute hotfixAttr = type.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.FullName == hotfixAttributeType.FullName);
            HotfixFlagInTool hotfixType;
            if (hotfixAttr != null)
            {
                // The flags are the last argument: [Hotfix(flags)] or [Hotfix(typeof(T), flags)].
                hotfixType = (HotfixFlagInTool)(int)hotfixAttr.ConstructorArguments[hotfixAttr.ConstructorArguments.Count - 1].Value;
            }
            else
            {
                if (!hotfixCfg.ContainsKey(type.FullName))
                {
                    return true;
                }
                hotfixType = (HotfixFlagInTool)hotfixCfg[type.FullName];
            }

            bool ignoreProperty = hotfixType.HasFlag(HotfixFlagInTool.IgnoreProperty);
            bool ignoreCompilerGenerated = hotfixType.HasFlag(HotfixFlagInTool.IgnoreCompilerGenerated);
            bool ignoreNotPublic = hotfixType.HasFlag(HotfixFlagInTool.IgnoreNotPublic);
            bool isInline = hotfixType.HasFlag(HotfixFlagInTool.Inline);
            bool isIntKey = hotfixType.HasFlag(HotfixFlagInTool.IntKey);
            bool noBaseProxy = hotfixType.HasFlag(HotfixFlagInTool.NoBaseProxy);
            if (ignoreCompilerGenerated && type.CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            {
                return true;
            }
            if (isIntKey && type.HasGenericParameters)
            {
                throw new InvalidOperationException(type.FullName + " is generic definition, can not be mark as IntKey!");
            }
            if (isIntKey && !intKeyAvailable)
            {
                // findHotfixDelegate, injectMethod and injectGenericMethod then use field mode.
                intKeyFallbackTypes.Add(type.FullName.Replace('/', '+'));
            }
            //isIntKey = !type.HasGenericParameters;

            foreach (var method in type.Methods)
            {
                if (ignoreNotPublic && !method.IsPublic)
                {
                    continue;
                }
                if (ignoreProperty && method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")))
                {
                    continue;
                }
                if (ignoreCompilerGenerated && method.CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
                {
                    continue;
                }
                if (method.Parameters.Any(pd => pd.ParameterType.IsPointer) || method.ReturnType.IsPointer)
                {
                    continue;
                }
                if (method.Name != ".cctor" && !method.IsAbstract && !method.IsPInvokeImpl && method.Body != null && !method.Name.Contains("<"))
                {
                    //Debug.Log(method);
                    if ((isInline || method.HasGenericParameters || genericInOut(method, hotfixType)) 
                        ? !injectGenericMethod(method, hotfixType) :
                        !injectMethod(method, hotfixType))
                    {
                        return false;
                    }
                }
            }

            if (!noBaseProxy)
            {
                List<MethodDefinition> toAdd = new List<MethodDefinition>();
                foreach (var method in type.Methods)
                {
                    if (ignoreNotPublic && !method.IsPublic)
                    {
                        continue;
                    }
                    if (ignoreProperty && method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")))
                    {
                        continue;
                    }
                    if (ignoreCompilerGenerated && method.CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
                    {
                        continue;
                    }
                    if (method.Parameters.Any(pd => pd.ParameterType.IsPointer) || method.ReturnType.IsPointer)
                    {
                        continue;
                    }
                    if (method.Name != ".cctor" && !method.IsAbstract && !method.IsPInvokeImpl && method.Body != null && !method.Name.Contains("<"))
                    {
                        var proxyMethod = tryAddBaseProxy(type, method);
                        if (proxyMethod != null) toAdd.Add(proxyMethod);
                    }
                }

                foreach (var md in toAdd)
                {
                    type.Methods.Add(md);
                }
            }

            return true;
        }

#if !XLUA_GENERAL
        [PostProcessScene]
        [MenuItem("XLua/Hotfix Inject In Editor", false, 3)]
        public static void HotfixInject()
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                return;
            }
            var hotfixCfg = new Dictionary<string, int>();
            HotfixConfig.GetConfig(hotfixCfg, Utils.GetAllTypes());
            var xluaAssemblyPath = typeof(LuaEnv).Module.FullyQualifiedName;
            var idMapFileName = CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/hotfix_id_map.lua.txt";
            var injectAssemblyPaths = HotfixConfig.GetHotfixAssemblyPaths();

            foreach (var injectAssemblyPath in injectAssemblyPaths)
            {
                Info("injecting " + injectAssemblyPath);
                if (injectAssemblyPaths.Count > 1)
                {
                    var injectAssemblyFileName = Path.GetFileName(injectAssemblyPath);
                    idMapFileName = CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/hotfix_id_map_" + injectAssemblyFileName.Substring(0, injectAssemblyFileName.Length - 4) + ".lua.txt";
                }
                HotfixInject(injectAssemblyPath, xluaAssemblyPath, null, idMapFileName, hotfixCfg);
            }
            AssetDatabase.Refresh();
        }
#endif

        // Reads the assembly and its symbols into memory so that both files can be
        // overwritten afterwards. Symbols are optional: if the .pdb (portable or
        // Windows) or .mdb next to the assembly cannot be read, inject without them.
        static AssemblyDefinition readAssembly(string assemblyPath)
        {
#if !HOTFIX_SYMBOLS_DISABLE
            var candidates = new KeyValuePair<string, ISymbolReaderProvider>[]
            {
                new KeyValuePair<string, ISymbolReaderProvider>(Path.ChangeExtension(assemblyPath, ".pdb"), new PdbReaderProvider()),
                new KeyValuePair<string, ISymbolReaderProvider>(assemblyPath + ".mdb", new MdbReaderProvider()),
            };
            foreach (var symbols in candidates.Where(c => File.Exists(c.Key)))
            {
                try
                {
                    return AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
                    {
                        InMemory = true,
                        ReadSymbols = true,
                        SymbolReaderProvider = symbols.Value,
                        SymbolStream = new MemoryStream(File.ReadAllBytes(symbols.Key)),
                    });
                }
                catch (Exception e)
                {
                    Info("ignoring debug symbols " + symbols.Key + ": " + e.Message);
                }
            }
#endif
            return AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { InMemory = true });
        }

        static void writeAssembly(AssemblyDefinition assembly, string assemblyPath)
        {
            // Rewrites the symbols in the format they were read in.
            assembly.Write(assemblyPath, new WriterParameters { WriteSymbols = assembly.MainModule.HasSymbols });
        }

        public static void HotfixInject(string injectAssemblyPath, string xluaAssemblyPath, IEnumerable<string> searchDirectorys, string idMapFilePath, Dictionary<string, int> hotfixConfig)
        {
            AssemblyDefinition injectAssembly = null;
            AssemblyDefinition xluaAssembly = null;
            Hotfix hotfix = null;
            try
            {
                injectAssembly = readAssembly(injectAssemblyPath);
                
                // injected flag check
                if (injectAssembly.MainModule.Types.Any(t => t.Name == "__XLUA_GEN_FLAG"))
                {
                    Info(injectAssemblyPath + " had injected!");
                    return;
                }
                var flagType = new TypeDefinition("__XLUA_GEN", "__XLUA_GEN_FLAG", Mono.Cecil.TypeAttributes.Class,
                    injectAssembly.MainModule.TypeSystem.Object);
                Hotfix.ExcludeFromObfuscation(injectAssembly.MainModule, flagType);
                injectAssembly.MainModule.Types.Add(flagType);

                xluaAssembly = (injectAssemblyPath == xluaAssemblyPath || injectAssembly.MainModule.FileName == xluaAssemblyPath) ? 
                    injectAssembly : readAssembly(xluaAssemblyPath);

                hotfix = new Hotfix();
                hotfix.Init(injectAssembly, xluaAssembly, searchDirectorys, hotfixConfig);

                //var hotfixDelegateAttributeType = assembly.MainModule.Types.Single(t => t.FullName == "XLua.HotfixDelegateAttribute");
                var hotfixAttributeType = xluaAssembly.MainModule.Types.Single(t => t.FullName == "XLua.HotfixAttribute");
                // [assembly: Hotfix(typeof(T), flags)] in the injected assembly, the
                // form the source generator reads (GenAttributes.cs).
                foreach (var ca in injectAssembly.CustomAttributes.Where(ca => ca.AttributeType.FullName == hotfixAttributeType.FullName))
                {
                    var configured = ca.ConstructorArguments.Count > 0 ? ca.ConstructorArguments[0].Value as TypeReference : null;
                    if (configured == null) continue;
                    var flags = ca.ConstructorArguments.Count > 1 ? (int)ca.ConstructorArguments[ca.ConstructorArguments.Count - 1].Value : 0;
                    if (!hotfixConfig.ContainsKey(configured.FullName)) hotfixConfig.Add(configured.FullName, flags);
                }
                var toInject = (from module in injectAssembly.Modules from type in module.Types select type).ToList();
                foreach (var type in toInject)
                {
                    if (!hotfix.InjectType(hotfixAttributeType, type))
                    {
                        return;
                    }
                }
                hotfix.warnIntKeyFallback();
                // A bare file name has no directory part: it goes to the working directory.
                var idMapDirectory = Path.GetDirectoryName(idMapFilePath);
                if (!string.IsNullOrEmpty(idMapDirectory))
                {
                    Directory.CreateDirectory(idMapDirectory);
                }
                hotfix.OutputIntKeyMapper(new FileStream(idMapFilePath, FileMode.Create, FileAccess.Write));
                File.Copy(idMapFilePath, idMapFilePath + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff"));

                writeAssembly(injectAssembly, injectAssemblyPath);
                Info(injectAssemblyPath + " inject finish!");
            }
            catch(Exception e)
            {
                Error(injectAssemblyPath + " inject Exception! " + e);
            }
            finally
            {
                if (injectAssembly != null)
                {
                    Clean(injectAssembly);
                }
                if (xluaAssembly != null && xluaAssembly != injectAssembly)
                {
                    Clean(xluaAssembly);
                }
                if (hotfix != null)
                {
                    foreach (var bridges in hotfix.bridgeAssemblies)
                    {
                        bridges.Dispose();
                    }
                }
            }
        }

        static void Info(string info)
        {
#if XLUA_GENERAL
            System.Console.WriteLine(info);
#else
            UnityEngine.Debug.Log(info);
#endif
        }

        static void Error(string info)
        {
#if XLUA_GENERAL
            System.Console.WriteLine("Error:" + info);
#else
            UnityEngine.Debug.LogError(info);
#endif
        }

        // The Editor logs the tool's "Warning:" lines as Unity warnings.
        static void Warning(string info)
        {
#if XLUA_GENERAL
            System.Console.WriteLine("Warning:" + info);
#else
            UnityEngine.Debug.LogWarning(info);
#endif
        }

        // Without its own slots, IntKey is used only in the assembly that owns the
        // process-wide list (DelegateBridge.DelegateBridgeList; see Init), since two
        // assemblies could not both number from 0. Other assemblies, such as a game
        // asmdef next to Assembly-CSharp, silently got field mode before this warning.
        void warnIntKeyFallback()
        {
            if (intKeyFallbackTypes.Count == 0)
            {
                return;
            }
            const int listed = 10;
            var names = string.Join(", ", intKeyFallbackTypes.Take(listed).ToArray())
                + (intKeyFallbackTypes.Count > listed ? " and " + (intKeyFallbackTypes.Count - listed) + " more" : "");
            Warning("IntKey falls back to field mode in " + Path.GetFileName(injectAssembly.MainModule.FileName)
                + " for " + intKeyFallbackTypes.Count + (intKeyFallbackTypes.Count == 1 ? " type: " : " types: ") + names + "."
                + " Without the source generator's hotfix bridges in that assembly, IntKey works only in the assembly that"
                + " holds the generated hotfix bridges (" + sharedIntKeyOwner + "), so these methods get a static __Hotfix field each and are not in"
                + " the hotfix id map; xlua.hotfix still patches them by name. To use IntKey, install the xLua source"
                + " generator for that assembly or move the types into " + sharedIntKeyOwner + "; to silence this warning,"
                + " remove HotfixFlag.IntKey from their Hotfix configuration.");
        }

        static void Clean(AssemblyDefinition assembly)
        {
            assembly.Dispose();
        }

        static OpCode[] ldargs = new OpCode[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };

        static readonly int MAX_OVERLOAD = 100;

        static string getDelegateName(MethodDefinition method)
        {
            string fieldName = method.Name;
            if (fieldName.StartsWith("."))
            {
                fieldName = fieldName.Substring(1);
            }
            string ccFlag = method.IsConstructor ? "_c" : "";
            string luaDelegateName = null;
            var type = method.DeclaringType;
            for (int i = 0; i < MAX_OVERLOAD; i++)
            {
                string tmp = ccFlag + "__Hotfix" + i + "_" + fieldName;
                if (!type.Fields.Any(f => f.Name == tmp)) // injected
                {
                    luaDelegateName = tmp;
                    break;
                }
            }
            return luaDelegateName;
        }

        static Instruction findNextRet(Mono.Collections.Generic.Collection<Instruction> instructions, Instruction pos)
        {
            bool posFound = false;
            for(int i = 0; i < instructions.Count; i++)
            {
                if (posFound && instructions[i].OpCode == OpCodes.Ret)
                {
                    return instructions[i];
                }
                else if (instructions[i] == pos)
                {
                    posFound = true;
                }
            }
            return null;
        }

        // Points branches to an old ret at the hotfix code inserted before it,
        // then uses short branch forms where they fit (Cecil computes the
        // offsets from the end of each instruction; Tencent/xLua#1003).
        void fixBranch(Mono.Cecil.Cil.MethodBody body, Dictionary<Instruction, Instruction> originToNewTarget, HashSet<Instruction> noCheck)
        {
            foreach(var instruction in body.Instructions)
            {
                Instruction target = instruction.Operand as Instruction;
                if (target != null && !noCheck.Contains(instruction))
                {
                    if (originToNewTarget.ContainsKey(target))
                    {
                        instruction.Operand = originToNewTarget[target];
                    }
                }
            }

            // A try block or handler that ended at an old ret (a try/finally or
            // try/catch at the end of the constructor) must end before the
            // hotfix code, or that code becomes the end of the handler and the
            // leave out of the try block jumps into it: IL2CPP then writes a
            // goto to a label it never declares (Tencent/xLua#1064).
            foreach (var handler in body.ExceptionHandlers)
            {
                if (handler.TryEnd != null && originToNewTarget.ContainsKey(handler.TryEnd))
                {
                    handler.TryEnd = originToNewTarget[handler.TryEnd];
                }
                if (handler.HandlerEnd != null && originToNewTarget.ContainsKey(handler.HandlerEnd))
                {
                    handler.HandlerEnd = originToNewTarget[handler.HandlerEnd];
                }
            }

            body.OptimizeMacros();
        }

        static MethodDefinition findOverride(TypeDefinition type, MethodReference vmethod)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name == vmethod.Name && method.IsVirtual && !method.IsAbstract && (method.ReturnType.FullName == vmethod.ReturnType.FullName) && method.Parameters.Count == vmethod.Parameters.Count)
                {
                    bool isParamsMatch = true;
                    for (int i = 0; i < method.Parameters.Count; i++)
                    {
                        if (method.Parameters[i].Attributes != vmethod.Parameters[i].Attributes
                            || (method.Parameters[i].ParameterType.FullName != vmethod.Parameters[i].ParameterType.FullName))
                        {
                            isParamsMatch = false;
                            break;
                        }
                    }
                    if (isParamsMatch) return method;
                }
            }
            return null;
        }

        static MethodReference _findBase(TypeReference type, MethodDefinition method)
        {
            TypeDefinition td = type.Resolve();
            if (td == null)
            {
                return null;
            }
            //if (type.IsGenericInstance && 
            //    (method.Module.Assembly.FullName != type.Module.Assembly.FullName || method.Module.Assembly.FullName != td.Module.Assembly.FullName
            //    || method.Module.FullyQualifiedName != type.Module.FullyQualifiedName || method.Module.FullyQualifiedName != td.Module.FullyQualifiedName))
            //{
            //    return _findBase(td.BaseType, method);
            //}
            var m = findOverride(td, method);
            if (m != null)
            {
                if (type.IsGenericInstance)
                {
                    var reference = new MethodReference(m.Name, tryImport(method.DeclaringType, m.ReturnType), tryImport(method.DeclaringType, type))
                    {
                        HasThis = m.HasThis,
                        ExplicitThis = m.ExplicitThis,
                        CallingConvention = m.CallingConvention
                    };
                    foreach (var parameter in m.Parameters)
                        reference.Parameters.Add(new ParameterDefinition(tryImport(method.DeclaringType, parameter.ParameterType)));
                    foreach (var generic_parameter in m.GenericParameters)
                        reference.GenericParameters.Add(new GenericParameter(generic_parameter.Name, reference));
                    return reference;
                }
                else
                {
                    return m;
                }
            }
            return _findBase(getBaseType(type), method);
        }

        static MethodReference findBase(TypeDefinition type, MethodDefinition method)
        {
            if (method.IsVirtual && !method.IsNewSlot) //Overrides a base method
            {
                try
                {
                    if (hasGenericParameter(method)) return null;
                    var b = _findBase(type.BaseType, method);
                    try
                    {
                        if (hasGenericParameterSkipDelaringType(b.Resolve())) return null;
                    }catch { }
                    return b;
                }
                catch { }
            }
            return null;
        }

        static TypeReference getBaseType(TypeReference typeReference)
        {
            var typeDefinition = typeReference.Resolve();
            var baseType = typeDefinition.BaseType;
            if (typeReference.IsGenericInstance && baseType.IsGenericInstance)
            {
                var genericType = typeReference as GenericInstanceType;
                var baseGenericType = baseType as GenericInstanceType;
                var genericInstanceType = new GenericInstanceType(tryImport(typeReference, baseGenericType.ElementType));
                foreach (var genericArgument in genericType.GenericArguments)
                {
                    genericInstanceType.GenericArguments.Add(genericArgument);
                }
                baseType = genericInstanceType;
            }
            return baseType;
        }

        const string BASE_RPOXY_PERFIX = "<>xLuaBaseProxy_";

        static TypeReference tryImport(TypeReference type, TypeReference toImport)
        {
            if (type.Module.Assembly.FullName == toImport.Module.Assembly.FullName
                && type.Module.FileName == toImport.Module.FileName)
            {
                return toImport;
            }
            else
            {
                return type.Module.ImportReference(toImport);
            }
        }

        static MethodReference tryImport(TypeReference type, MethodReference toImport)
        {
            if (type.Module.Assembly.FullName == toImport.Module.Assembly.FullName
                && type.Module.FileName == toImport.Module.FileName)
            {
                return toImport;
            }
            else
            {
                return type.Module.ImportReference(toImport);
            }
        }

        static MethodDefinition tryAddBaseProxy(TypeDefinition type, MethodDefinition method)
        {
            var mbase = findBase(type, method);
            if (mbase != null)
            {
                var proxyMethod = new MethodDefinition(BASE_RPOXY_PERFIX + method.Name, Mono.Cecil.MethodAttributes.Private, tryImport(type, method.ReturnType));
                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    proxyMethod.Parameters.Add(copyParameter("P" + i, method.Parameters[i], tryImport(type, method.Parameters[i].ParameterType), type.Module));
                }
                var instructions = proxyMethod.Body.Instructions;
                var processor = proxyMethod.Body.GetILProcessor();
                int paramCount = method.Parameters.Count + 1;
                for (int i = 0; i < paramCount; i++)
                {
                    if (i < ldargs.Length)
                    {
                        instructions.Add(processor.Create(ldargs[i]));
                    }
                    else if (i < 256)
                    {
                        instructions.Add(processor.Create(OpCodes.Ldarg_S, (byte)i));
                    }
                    else
                    {
                        instructions.Add(processor.Create(OpCodes.Ldarg, (short)i));
                    }
                    if (i == 0 && type.IsValueType)
                    {
                        instructions.Add(Instruction.Create(OpCodes.Ldobj, type));
                        instructions.Add(Instruction.Create(OpCodes.Box, type));
                    }
                }
                instructions.Add(Instruction.Create(OpCodes.Call, tryImport(type, mbase)));
                instructions.Add(Instruction.Create(OpCodes.Ret));
                return proxyMethod;
            }
            return null;
        }

        bool injectMethod(MethodDefinition method, HotfixFlagInTool hotfixType)
        {
            var type = method.DeclaringType;
            
            bool isFinalize = (method.Name == "Finalize" && method.IsSpecialName);

            MethodReference invoke = null;

            int param_count = method.Parameters.Count + (method.IsStatic ? 0 : 1);

            if (!findHotfixDelegate(method, out invoke, hotfixType))
            {
                Error("can not find delegate for " + method.DeclaringType + "." + method.Name + "! try re-genertate code"
                    + " (with the source generator: is it installed for " + injectAssembly.Name.Name + ", and did it run?).");
                return false;
            }

            if (invoke == null)
            {
                throw new Exception("unknow exception!");
            }

            invoke = injectAssembly.MainModule.ImportReference(invoke);

            FieldReference fieldReference = null;
            VariableDefinition injection = null;
            bool isIntKey = hotfixType.HasFlag(HotfixFlagInTool.IntKey) && !type.HasGenericParameters && intKeyAvailable;
            // IntKey through the assembly's own slots: one local holds the slot.
            VariableDefinition slot = null;
            if (isIntKey && slotGetter != null)
            {
                slot = new VariableDefinition(delegateBridgeType);
                method.Body.Variables.Add(slot);
            }

            // What the field holds: the DelegateBridge for a static generated
            // bridge, else the delegate whose Invoke is called.
            TypeReference injectionType = invoke.HasThis ? invoke.DeclaringType : invoke.Parameters[0].ParameterType;

            if (!isIntKey)
            {
                injection = new VariableDefinition(injectionType);
                method.Body.Variables.Add(injection);

                var luaDelegateName = getDelegateName(method);
                if (luaDelegateName == null)
                {
                    Error("too many overload!");
                    return false;
                }

                FieldDefinition fieldDefinition = new FieldDefinition(luaDelegateName, Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Private,
                    injectionType);
                ExcludeFromObfuscation(type.Module, fieldDefinition);
                type.Fields.Add(fieldDefinition);
                fieldReference = fieldDefinition.GetGeneric();
            }

            bool ignoreValueType = hotfixType.HasFlag(HotfixFlagInTool.ValueTypeBoxing);

            var insertPoint = method.Body.Instructions[0];
            var processor = method.Body.GetILProcessor();

            if (method.IsConstructor)
            {
                // The hotfix goes in before every ret, so branches across one
                // grow: use long forms until fixBranch shortens what fits.
                method.Body.SimplifyMacros();
                insertPoint = findNextRet(method.Body.Instructions, insertPoint);
            }

            Dictionary<Instruction, Instruction> originToNewTarget = new Dictionary<Instruction, Instruction>();
            HashSet<Instruction> noCheck = new HashSet<Instruction>();

            while (insertPoint != null)
            {
                Instruction firstInstruction;
                if (slot != null)
                {
                    firstInstruction = processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, slotGetter));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stloc, slot));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, slot));
                }
                else if (isIntKey)
                {
                    firstInstruction = processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, hotfixFlagGetter));
                }
                else
                {
                    firstInstruction = processor.Create(OpCodes.Ldsfld, fieldReference);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stloc, injection));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                }

                var jmpInstruction = processor.Create(OpCodes.Brfalse, insertPoint);
                processor.InsertBefore(insertPoint, jmpInstruction);

                if (slot != null)
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, slot));
                }
                else if (isIntKey)
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, delegateBridgeGetter));
                }
                else
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                }

                for (int i = 0; i < param_count; i++)
                {
                    if (i < ldargs.Length)
                    {
                        processor.InsertBefore(insertPoint, processor.Create(ldargs[i]));
                    }
                    else if (i < 256)
                    {
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg_S, (byte)i));
                    }
                    else
                    {
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg, (short)i));
                    }
                    if (i == 0 && !method.IsStatic && type.IsValueType)
                    {
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldobj, type));
                        
                    }
                    if (ignoreValueType)
                    {
                        TypeReference paramType;
                        if (method.IsStatic)
                        {
                            paramType = method.Parameters[i].ParameterType;
                        }
                        else
                        {
                            paramType = (i == 0) ? type : method.Parameters[i - 1].ParameterType;
                        }
                        if (paramType.IsValueType)
                        {
                            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Box, paramType));
                        }
                    }
                }

                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, invoke));

                if (!method.IsConstructor && !isFinalize)
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ret));
                }

                if (!method.IsConstructor)
                {
                    break;
                }
                else
                {
                    originToNewTarget[insertPoint] = firstInstruction;
                    noCheck.Add(jmpInstruction);
                }
                insertPoint = findNextRet(method.Body.Instructions, insertPoint);
            }

            if (method.IsConstructor)
            {
                fixBranch(method.Body, originToNewTarget, noCheck);
            }

            if (isFinalize)
            {
                if (method.Body.ExceptionHandlers.Count == 0)
                {
                    throw new InvalidProgramException("Finalize has not try-catch? Type :" + method.DeclaringType);
                }
                method.Body.ExceptionHandlers[0].TryStart = method.Body.Instructions[0];
            }
            if (isIntKey)
            {
                bridgeIndexByKey.Add(method);
            }
            return true;
        }

        bool injectGenericMethod(MethodDefinition method, HotfixFlagInTool hotfixType)
        {
            // It calls only DelegateBridge's public methods, so it works in any
            // assembly; it used to return here, without injecting, outside the
            // assembly that holds xLua (Inline types and generic methods there were
            // silently not hotfixable).
            var type = method.DeclaringType;

            bool isFinalize = (method.Name == "Finalize" && method.IsSpecialName);
            bool isIntKey = hotfixType.HasFlag(HotfixFlagInTool.IntKey) && !type.HasGenericParameters && intKeyAvailable;
            //isIntKey = !type.HasGenericParameters;

            FieldReference fieldReference = null;
            VariableDefinition injection = null;
            if (!isIntKey)
            {
                var luaDelegateName = getDelegateName(method);
                if (luaDelegateName == null)
                {
                    Error("too many overload!");
                    return false;
                }

                FieldDefinition fieldDefinition = new FieldDefinition(luaDelegateName, Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Private,
                    delegateBridgeType);
                ExcludeFromObfuscation(type.Module, fieldDefinition);
                type.Fields.Add(fieldDefinition);

                fieldReference = fieldDefinition.GetGeneric();
            }

            injection = new VariableDefinition(delegateBridgeType);
            method.Body.Variables.Add(injection);

            int param_start = method.IsStatic ? 0 : 1;
            int param_count = method.Parameters.Count + param_start;
            var insertPoint = method.Body.Instructions[0];
            var processor = method.Body.GetILProcessor();

            if (method.IsConstructor)
            {
                // The hotfix goes in before every ret, so branches across one
                // grow: use long forms until fixBranch shortens what fits.
                method.Body.SimplifyMacros();
                insertPoint = findNextRet(method.Body.Instructions, insertPoint);
            }

            Dictionary<Instruction, Instruction> originToNewTarget = new Dictionary<Instruction, Instruction>();
            HashSet<Instruction> noCheck = new HashSet<Instruction>();

            while (insertPoint != null)
            {
                Instruction firstInstruction;
                Instruction jmpInstruction;
                if (isIntKey && slotGetter != null)
                {
                    firstInstruction = processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, slotGetter));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stloc, injection));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                    jmpInstruction = processor.Create(OpCodes.Brfalse, insertPoint);
                    processor.InsertBefore(insertPoint, jmpInstruction);
                }
                else if (isIntKey)
                {
                    firstInstruction = processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, hotfixFlagGetter));
                    jmpInstruction = processor.Create(OpCodes.Brfalse, insertPoint);
                    processor.InsertBefore(insertPoint, jmpInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, bridgeIndexByKey.Count));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, delegateBridgeGetter));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stloc, injection));
                }
                else
                {
                    firstInstruction = processor.Create(OpCodes.Ldsfld, fieldReference);
                    processor.InsertBefore(insertPoint, firstInstruction);
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stloc, injection));
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                    jmpInstruction = processor.Create(OpCodes.Brfalse, insertPoint);
                    processor.InsertBefore(insertPoint, jmpInstruction);
                }

                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));

                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, invokeSessionStart));

                TypeReference returnType = method.ReturnType;

                bool isVoid = returnType.FullName == "System.Void";

                int outCout = 0;

                for (int i = 0; i < param_count; i++)
                {
                    if (i == 0 && !method.IsStatic)
                    {
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg_0));
                        if (type.IsValueType)
                        {
                            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldobj, method.DeclaringType.GetGeneric()));
                        }
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, inParam.MakeGenericMethod(method.DeclaringType.GetGeneric())));
                    }
                    else
                    {
                        var param = method.Parameters[i - param_start];
                        if (param.ParameterType.IsByReference && !isInParameter(param))
                        {
                            outCout++;
                        }
                        if (!param.IsOut)
                        {
                            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));

                            if (i < ldargs.Length)
                            {
                                processor.InsertBefore(insertPoint, processor.Create(ldargs[i]));
                            }
                            else if (i < 256)
                            {
                                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg_S, (byte)i));
                            }
                            else
                            {
                                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg, (short)i));
                            }

                            var paramType = param.ParameterType;

                            if (param.ParameterType.IsByReference)
                            {
                                paramType = ((ByReferenceType)paramType).ElementType;
                                if (paramType.IsValueType || paramType.IsGenericParameter)
                                {
                                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldobj, paramType));
                                }
                                else
                                {
                                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldind_Ref));
                                }
                            }
                            if (i == param_count - 1 && param.CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.ParamArrayAttribute"))
                            {
                                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, inParams.MakeGenericMethod(((ArrayType)paramType).ElementType)));
                            }
                            else
                            {
                                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, inParam.MakeGenericMethod(paramType)));
                            }
                        }
                    }
                }

                int outStart = (isVoid ? 0 : 1);

                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, outCout + outStart));
                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, functionInvoke));

                int outPos = outStart;
                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    if (method.Parameters[i].ParameterType.IsByReference && !isInParameter(method.Parameters[i]))
                    {
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, outPos));
                        int arg_pos = param_start + i;
                        if (arg_pos < ldargs.Length)
                        {
                            processor.InsertBefore(insertPoint, processor.Create(ldargs[arg_pos]));
                        }
                        else if (arg_pos < 256)
                        {
                            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg_S, (byte)arg_pos));
                        }
                        else
                        {
                            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldarg, (short)arg_pos));
                        }
                        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, outParam.MakeGenericMethod(
                            ((ByReferenceType)method.Parameters[i].ParameterType).ElementType)));
                        outPos++;
                    }
                }

                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldloc, injection));
                if (isVoid)
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, invokeSessionEnd));
                }
                else
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Callvirt, invokeSessionEndWithResult.MakeGenericMethod(returnType)));
                }

                if (!method.IsConstructor && !isFinalize)
                {
                    processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ret));
                }

                if (!method.IsConstructor)
                {
                    break;
                }
                else
                {
                    originToNewTarget[insertPoint] = firstInstruction;
                    noCheck.Add(jmpInstruction);
                }

                insertPoint = findNextRet(method.Body.Instructions, insertPoint);
            }

            if (method.IsConstructor)
            {
                fixBranch(method.Body, originToNewTarget, noCheck);
            }

            if (isFinalize)
            {
                method.Body.ExceptionHandlers[0].TryStart = method.Body.Instructions[0];
            }

            if (isIntKey)
            {
                bridgeIndexByKey.Add(method);
            }

            return true;
        }

        public void OutputIntKeyMapper(Stream output)
        {
            using (StreamWriter writer = new StreamWriter(output))
            {
                writer.WriteLine("return {");
                var data = bridgeIndexByKey
                    .Select((md, idx) => new { Method = md, Index = idx})
                    .GroupBy(info => info.Method.DeclaringType)
                    .ToDictionary(group => group.Key, group =>
                    {
                        return group.GroupBy(info => info.Method.Name).ToDictionary(group_by_name => group_by_name.Key, group_by_name => group_by_name.Select(info => info.Index.ToString()).ToArray());
                    });
                foreach(var kv in data)
                {
                    writer.WriteLine("    [\"" + kv.Key.FullName.Replace('/', '+') + "\"] = {");
                    if (slotGetter != null)
                    {
                        // The ids index this assembly's slots (xlua.util.auto_id_map).
                        writer.WriteLine("        [\"<assembly>\"] = \"" + injectAssembly.Name.Name + "\",");
                    }
                    foreach(var kv2 in kv.Value)
                    {
                        writer.WriteLine("        [\"" + kv2.Key + "\"] = {");
                        writer.WriteLine("            " + string.Join(",", kv2.Value));
                        writer.WriteLine("        },");
                    }
                    writer.WriteLine("    },");
                }
                writer.WriteLine("}");
            }
        }
    }
}
#else

namespace XLua
{
#if UNITY_2019_1_OR_NEWER
    class MyCustomBuildProcessor : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder { get { return 0; } }
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            // BuildReport.files is an obsolete error in Unity 6.3; GetFiles() replaces it.
#if UNITY_2022_1_OR_NEWER
            var files = report.GetFiles();
#else
            var files = report.files;
#endif
            // The folder of the player's script assemblies, found through xLua's
            // own: with every script in an asmdef there is no Assembly-CSharp.dll.
            var xluaFileName = Path.GetFileName(typeof(LuaEnv).Module.FullyQualifiedName);
            var xluaFile = files.FirstOrDefault(file => Path.GetFileName(file.path) == xluaFileName);
            if (xluaFile.path == null)
            {
                UnityEngine.Debug.LogError("hotfix: " + xluaFileName + " is not among the player's script assemblies; nothing is injected");
                return;
            }
            Hotfix.HotfixInject(Path.GetDirectoryName(xluaFile.path));
        }
    }
#endif

    public static class Hotfix
    {
        static bool ContainNotAsciiChar(string s)
        {
            for (int i = 0; i < s.Length; ++i)
            {
                if (s[i] > 127)
                {
                    return true;
                }
            }
            return false;
        }

#if !UNITY_2019_1_OR_NEWER
        [PostProcessScene]
#endif
        [MenuItem("XLua/Hotfix Inject In Editor", false, 3)]
        public static void HotfixInject()
        {
            HotfixInject("./Library/ScriptAssemblies");
        }

        public static void HotfixInject(string assemblyDir)
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (EditorApplication.isCompiling)
            {
                UnityEngine.Debug.LogError("You can't inject before the compilation is done");
                return;
            }

#if UNITY_EDITOR_OSX
			var mono_path = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
				"../MonoBleedingEdge/bin/mono");
			if(!File.Exists(mono_path))
			{
				mono_path = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
					"../../MonoBleedingEdge/bin/mono");
			}
			if(!File.Exists(mono_path))
			{
				UnityEngine.Debug.LogError("can not find mono!");
			}
#elif UNITY_EDITOR_WIN
            var mono_path = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                "Data/MonoBleedingEdge/bin/mono.exe");
#endif
            var inject_tool_path = "./Tools/XLuaHotfixInject.exe";
            // A missing injector or Cecil file stops injection; tools built from an
            // older Hotfix.cs or without a manifest still run, with a warning.
            bool tools_unusable;
            foreach (var problem in CSObjectWrapEditor.SetupValidator.CheckHotfixTools("./Tools/",
                CSObjectWrapEditor.SetupValidator.FindHotfixSource(), out tools_unusable))
            {
                if (tools_unusable) UnityEngine.Debug.LogError(problem);
                else UnityEngine.Debug.LogWarning(problem);
            }
            if (tools_unusable)
            {
                return;
            }

            // xLua's assembly as built in assemblyDir: Assembly-CSharp.dll when xLua is
            // compiled with the project's scripts, XLua.dll with its asmdef. From
            // Unity 2019.1 assemblyDir holds the player build, whose xLua was
            // compiled without UNITY_EDITOR.
            var xlua_assembly_path = Path.Combine(assemblyDir, Path.GetFileName(typeof(LuaEnv).Module.FullyQualifiedName));
            var id_map_file_path = CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/hotfix_id_map.lua.txt";
            var hotfix_cfg_in_editor = CSObjectWrapEditor.GeneratorConfig.common_path + "hotfix_cfg_in_editor.data";

            // Configuration that the injector cannot read from the player
            // assemblies: [Hotfix] lists in editor code. That code was in xLua's
            // editor assembly (Assembly-CSharp-Editor); with asmdefs it is in
            // any editor assembly of the project. Lists in player assemblies are
            // read again by the injector, which keeps the first entry per type.
            Dictionary<string, int> editor_cfg = new Dictionary<string, int>();
            var project_path = HotfixConfig.GetProjectPath();
            HotfixConfig.GetConfig(editor_cfg, Utils.GetAllTypes().Where(t => HotfixConfig.IsProjectAssembly(t.Assembly, project_path)));

            if (!Directory.Exists(CSObjectWrapEditor.GeneratorConfig.common_path))
            {
                Directory.CreateDirectory(CSObjectWrapEditor.GeneratorConfig.common_path);
            }

            using (BinaryWriter writer = new BinaryWriter(new FileStream(hotfix_cfg_in_editor, FileMode.Create, FileAccess.Write)))
            {
                writer.Write(editor_cfg.Count);
                foreach (var kv in editor_cfg)
                {
                    writer.Write(kv.Key);
                    writer.Write(kv.Value);
                }
            }

#if UNITY_2019_1_OR_NEWER
            List<string> args = new List<string>() { xlua_assembly_path, xlua_assembly_path, id_map_file_path, hotfix_cfg_in_editor };
#else
            List<string> args = new List<string>() { xlua_assembly_path, typeof(LuaEnv).Module.FullyQualifiedName, id_map_file_path, hotfix_cfg_in_editor };
#endif

            foreach (var path in
                (from asm in AppDomain.CurrentDomain.GetAssemblies() select asm.ManifestModule.FullyQualifiedName)
                 .Distinct())
            {
                try
                {
                    args.Add(System.IO.Path.GetDirectoryName(path));
                }
                catch (Exception)
                {

                }
            }
            var injectAssemblyPaths = HotfixConfig.GetHotfixAssemblyPaths();
            var idMapFileNames = new List<string>();
            foreach (var injectAssemblyPath in injectAssemblyPaths)
            {
                args[0] = Path.Combine(assemblyDir, Path.GetFileName(injectAssemblyPath));
                if (ContainNotAsciiChar(args[0]))
                {
                    throw new Exception("project path must contain only ascii characters");
                }

                if (injectAssemblyPaths.Count > 1)
                {
                    var injectAssemblyFileName = Path.GetFileName(injectAssemblyPath);
                    args[2] = CSObjectWrapEditor.GeneratorConfig.common_path + "Resources/hotfix_id_map_" + injectAssemblyFileName.Substring(0, injectAssemblyFileName.Length - 4) + ".lua.txt";
                    idMapFileNames.Add(args[2]);
                }
                Process hotfix_injection = new Process();
                hotfix_injection.StartInfo.FileName = mono_path;
#if UNITY_5_6_OR_NEWER
                hotfix_injection.StartInfo.Arguments = "--runtime=v4.0.30319 " + inject_tool_path + " \"" + String.Join("\" \"", args.ToArray()) + "\"";
#else
                hotfix_injection.StartInfo.Arguments = inject_tool_path + " \"" + String.Join("\" \"", args.ToArray()) + "\"";
#endif
                hotfix_injection.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                hotfix_injection.StartInfo.RedirectStandardOutput = true;
                hotfix_injection.StartInfo.UseShellExecute = false;
                hotfix_injection.StartInfo.CreateNoWindow = true;
                hotfix_injection.Start();
                var injection_log = hotfix_injection.StandardOutput.ReadToEnd();
                UnityEngine.Debug.Log(injection_log);
                foreach (var line in injection_log.Split('\n'))
                {
                    if (line.StartsWith("Warning:"))
                    {
                        UnityEngine.Debug.LogWarning(line.Substring("Warning:".Length).TrimEnd());
                    }
                }
                hotfix_injection.WaitForExit();
            }

            // auto_id_map and patch loaders require 'hotfix_id_map'; with several
            // assemblies it becomes the union of the per-assembly maps, so it is
            // never left over from an earlier single-assembly injection.
            if (idMapFileNames.Count > 0)
            {
                foreach (var missing in HotfixIdMap.Merge(id_map_file_path, idMapFileNames))
                {
                    UnityEngine.Debug.LogWarning("hotfix id map " + missing + " is missing (its injection failed?); "
                        + id_map_file_path + " is written without it.");
                }
            }

            File.Delete(hotfix_cfg_in_editor);
            AssetDatabase.Refresh();
        }
    }
}
#endif
#endif
