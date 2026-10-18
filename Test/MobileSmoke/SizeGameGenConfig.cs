// size_report.py +gen+game copies this into Assets/Editor before running XLua/Generate Code: a
// game-sized configuration for the GCM question (docs/design/source-generator.md, M6 question 2).
// It is Test/Native/MigrationSample/ExampleConfigSample.cs (ExampleConfig.cs with its LINQ
// queries switched on: every exported class and struct of UnityEngine.CoreModule and
// UnityEngine.UI in the UnityEngine and UnityEngine.UI namespaces, the project's own types, and
// the delegates they use) without the Hotfix list, plus ExampleConfig.cs's BlackList, which the
// package layout does not ship (ExampleConfig.cs is in the Examples sample there).
// The +sg+game variant gets the same configuration as attributes: SizeGameSgConfig.cs, which
// XLua > Migrate Configuration to Attributes wrote from this file in Unity 6000.3.21f1.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XLua;

public static class SizeGameGenConfig
{
    static List<string> exclude = new List<string> {
        "HideInInspector", "ExecuteInEditMode",
        "AddComponentMenu", "ContextMenu",
        "RequireComponent", "DisallowMultipleComponent",
        "SerializeField", "AssemblyIsEditorAssembly",
        "Attribute", "Types",
        "UnitySurrogateSelector", "TrackedReference",
        "TypeInferenceRules", "FFTWindow",
        "RPC", "Network", "MasterServer",
        "BitStream", "HostData",
        "ConnectionTesterStatus", "GUI", "EventType",
        "EventModifiers", "FontStyle", "TextAlignment",
        "TextEditor", "TextEditorDblClickSnapping",
        "TextGenerator", "TextClipping", "Gizmos",
        "ADBannerView", "ADInterstitialAd",
        "Android", "Tizen", "jvalue",
        "iPhone", "iOS", "Windows", "CalendarIdentifier",
        "CalendarUnit", "CalendarUnit",
        "ClusterInput", "FullScreenMovieControlMode",
        "FullScreenMovieScalingMode", "Handheld",
        "LocalNotification", "NotificationServices",
        "RemoteNotificationType", "RemoteNotification",
        "SamsungTV", "TextureCompressionQuality",
        "TouchScreenKeyboardType", "TouchScreenKeyboard",
        "MovieTexture", "UnityEngineInternal",
        "Terrain", "Tree", "SplatPrototype",
        "DetailPrototype", "DetailRenderMode",
        "MeshSubsetCombineUtility", "AOT", "Social", "Enumerator",
        "SendMouseEvents", "Cursor", "Flash", "ActionScript",
        "OnRequestRebuild", "Ping",
        "ShaderVariantCollection", "SimpleJson.Reflection",
        "CoroutineTween", "GraphicRebuildTracker",
        "Advertisements", "UnityEditor", "WSA",
        "EventProvider", "Apple",
        "ClusterInput", "Motion",
        "UnityEngine.UI.ReflectionMethodsCache", "NativeLeakDetection",
        "NativeLeakDetectionMode", "WWWAudioExtensions", "UnityEngine.Experimental",
        // Unity 6000.3: Editor-only API in the Editor's UnityEngine.CoreModule (whole types).
        "DeviceFilterLists", "LightingSettings",
    };

    static readonly string[] unityModules = { "UnityEngine.CoreModule", "UnityEngine.UI" };

    static bool isExcluded(Type type)
    {
        var fullName = type.FullName;
        for (int i = 0; i < exclude.Count; i++)
        {
            if (fullName.Contains(exclude[i]))
            {
                return true;
            }
        }
        return false;
    }

    [LuaCallCSharp]
    public static IEnumerable<Type> LuaCallCSharp
    {
        get
        {
            List<string> namespaces = new List<string>() { "UnityEngine", "UnityEngine.UI" };
            var unityTypes = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                              where !(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
                                  && unityModules.Contains(assembly.GetName().Name)
                              from type in assembly.GetExportedTypes()
                              where type.Namespace != null && namespaces.Contains(type.Namespace) && !isExcluded(type)
                                      && type.BaseType != typeof(MulticastDelegate) && !type.IsInterface && !type.IsEnum
                              select type);
            var customTypes = (from type in Assembly.Load("Assembly-CSharp").GetExportedTypes()
                               where (type.Namespace == null || (type.Namespace != "XLua" && !type.Namespace.StartsWith("XLua.")))
                                       && type.BaseType != typeof(MulticastDelegate) && !type.IsInterface && !type.IsEnum
                               select type);
            return unityTypes.Concat(customTypes);
        }
    }

    // The delegates of the LuaCallCSharp types' fields, parameters and results, so Lua functions can be callbacks.
    [CSharpCallLua]
    public static List<Type> CSharpCallLua
    {
        get
        {
            var delegate_types = new List<Type>();
            var flag = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly;
            foreach (var field in LuaCallCSharp.SelectMany(type => type.GetFields(flag)))
            {
                if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    delegate_types.Add(field.FieldType);
                }
            }
            foreach (var method in LuaCallCSharp.SelectMany(type => type.GetMethods(flag)))
            {
                if (typeof(Delegate).IsAssignableFrom(method.ReturnType))
                {
                    delegate_types.Add(method.ReturnType);
                }
                foreach (var param in method.GetParameters())
                {
                    var paramType = param.ParameterType.IsByRef ? param.ParameterType.GetElementType() : param.ParameterType;
                    if (typeof(Delegate).IsAssignableFrom(paramType))
                    {
                        delegate_types.Add(paramType);
                    }
                }
            }
            return delegate_types.Where(t => t.BaseType == typeof(MulticastDelegate) && !hasGenericParameter(t) && !delegateHasEditorRef(t)).Distinct().ToList();
        }
    }

    static bool hasGenericParameter(Type type)
    {
        if (type.IsGenericTypeDefinition) return true;
        if (type.IsGenericParameter) return true;
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
        }
        return false;
    }

    static bool typeHasEditorRef(Type type)
    {
        if (type.Namespace != null && (type.Namespace == "UnityEditor" || type.Namespace.StartsWith("UnityEditor.")))
        {
            return true;
        }
        if (type.IsNested)
        {
            return typeHasEditorRef(type.DeclaringType);
        }
        if (type.IsByRef || type.IsArray)
        {
            return typeHasEditorRef(type.GetElementType());
        }
        if (type.IsGenericType)
        {
            foreach (var typeArg in type.GetGenericArguments())
            {
                if (!typeArg.IsGenericParameter && typeHasEditorRef(typeArg))
                {
                    return true;
                }
            }
        }
        return false;
    }

    static bool delegateHasEditorRef(Type delegateType)
    {
        if (typeHasEditorRef(delegateType)) return true;
        var method = delegateType.GetMethod("Invoke");
        if (method == null)
        {
            return false;
        }
        if (typeHasEditorRef(method.ReturnType)) return true;
        return method.GetParameters().Any(pinfo => typeHasEditorRef(pinfo.ParameterType));
    }

    // ExampleConfig.cs's BlackList: members Generate Code would otherwise call although some
    // platforms or Unity versions do not have them.
    [BlackList]
    public static List<List<string>> BlackList = new List<List<string>>()  {
        new List<string>(){"UnityEngine.WWW", "movie"},
        new List<string>(){"UnityEngine.Texture2D", "alphaIsTransparency"},
        new List<string>(){"UnityEngine.Security", "GetChainOfTrustValue"},
        new List<string>(){"UnityEngine.CanvasRenderer", "onRequestRebuild"},
        new List<string>(){"UnityEngine.Light", "areaSize"},
        new List<string>(){"UnityEngine.Light", "lightmapBakeType"},
        new List<string>(){"UnityEngine.WWW", "MovieTexture"},
        new List<string>(){"UnityEngine.WWW", "GetMovieTexture"},
        new List<string>(){"UnityEngine.AnimatorOverrideController", "PerformOverrideClipListCleanup"},
        new List<string>(){"UnityEngine.Application", "ExternalEval"},
        new List<string>(){"UnityEngine.GameObject", "networkView"},
        new List<string>(){"UnityEngine.Component", "networkView"},
        new List<string>(){"UnityEngine.MonoBehaviour", "runInEditMode"},
    };

    // Unity 6000.3: members (every overload) that the Editor's UnityEngine.CoreModule and
    // UnityEngine.UI have and an Android player does not, or, for DefaultControls.factory and
    // LightProbeGroup.probePositions, have a setter only in the Editor. Generate Code reflects on
    // the Editor's assemblies, so its wrappers for them do not compile in the player.
    static readonly HashSet<string> editorOnly = new HashSet<string> {
        "UnityEngine.Caching.ResetNoBackupFlag", "UnityEngine.Caching.SetNoBackupFlag",
        "UnityEngine.DrivenRectTransformTracker.StartRecordingUndo", "UnityEngine.DrivenRectTransformTracker.StopRecordingUndo",
        "UnityEngine.UI.Graphic.OnRebuildRequested", "UnityEngine.UI.Text.OnRebuildRequested",
        "UnityEngine.UI.DefaultControls.factory",
        "UnityEngine.Light.SetLightDirty", "UnityEngine.Light.shadowAngle", "UnityEngine.Light.shadowRadius",
        "UnityEngine.LightProbeGroup.dering", "UnityEngine.LightProbeGroup.probePositions",
        "UnityEngine.Material.ApplyPropertyOverride", "UnityEngine.Material.IsChildOf", "UnityEngine.Material.IsPropertyLocked",
        "UnityEngine.Material.IsPropertyLockedByAncestor", "UnityEngine.Material.IsPropertyOverriden",
        "UnityEngine.Material.RevertAllPropertyOverrides", "UnityEngine.Material.RevertPropertyOverride",
        "UnityEngine.Material.SetPropertyLock", "UnityEngine.Material.isVariant", "UnityEngine.Material.parent",
        "UnityEngine.MeshRenderer.globalIlluminationMeshLod", "UnityEngine.MeshRenderer.receiveGI",
        "UnityEngine.MeshRenderer.scaleInLightmap", "UnityEngine.MeshRenderer.stitchLightmapSeams",
        "UnityEngine.QualitySettings.GetActiveQualityLevelsForPlatform", "UnityEngine.QualitySettings.GetActiveQualityLevelsForPlatformCount",
        "UnityEngine.QualitySettings.GetAllRenderPipelineAssetsForPlatform", "UnityEngine.QualitySettings.IsPlatformIncluded",
        "UnityEngine.QualitySettings.TryExcludePlatformAt", "UnityEngine.QualitySettings.TryIncludePlatformAt",
        "UnityEngine.Texture.imageContentsHash",
        "UnityEngine.TextureMipmapLimitGroups.CreateGroup", "UnityEngine.TextureMipmapLimitGroups.RemoveGroup",
    };

    [BlackList]
    public static Func<MemberInfo, bool> EditorOnlyFilter = (memberInfo) =>
        memberInfo.DeclaringType != null && editorOnly.Contains(memberInfo.DeclaringType.FullName + "." + memberInfo.Name);

    // ExampleConfig's filter, with the Dictionary it matches.
    [BlackList]
    public static Func<MemberInfo, bool> MethodFilter = (memberInfo) =>
    {
        if (memberInfo.DeclaringType.IsGenericType && memberInfo.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            if (memberInfo.MemberType == MemberTypes.Constructor)
            {
                var parameterInfos = ((ConstructorInfo)memberInfo).GetParameters();
                if (parameterInfos.Length > 0 && typeof(System.Collections.IEnumerable).IsAssignableFrom(parameterInfos[0].ParameterType))
                {
                    return true;
                }
            }
            else if (memberInfo.MemberType == MemberTypes.Method)
            {
                var methodInfo = (MethodInfo)memberInfo;
                if (methodInfo.Name == "TryAdd" || methodInfo.Name == "Remove" && methodInfo.GetParameters().Length == 2)
                {
                    return true;
                }
            }
        }
        return false;
    };

    [LuaCallCSharp]
    static List<Type> Dictionaries = new List<Type> { typeof(Dictionary<string, int>) };

    [DoNotGen]
    static Dictionary<Type, List<string>> DoNotGen = new Dictionary<Type, List<string>>
    {
        { typeof(UnityEngine.GameObject), new List<string> { "SendMessage", "BroadcastMessage" } },
    };

    [GCOptimize(OptimizeFlag.PackAsTable)]
    static List<Type> PackedAsTable = new List<Type> { typeof(UnityEngine.Rect) };

    [AdditionalProperties]
    static Dictionary<Type, List<string>> AdditionalProperties = new Dictionary<Type, List<string>>
    {
        { typeof(UnityEngine.Rect), new List<string> { "x", "y", "width", "height" } },
    };
}
