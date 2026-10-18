using System;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEngine;

// compile_time.py: writes the UnityEngine types that the "Lua-only scripting"
// reference configuration in Assets/XLua/Editor/ExampleConfig.cs would list
// (namespace UnityEngine, its exclude list, no delegates, interfaces or
// enums), one C# name per line, sorted. Also left out: generic type
// definitions, obsolete types and compiler-generated ones (fixed buffers),
// which neither generator can name, and types of modules the project does
// not reference (built-in packages it does not enable; the Editor loads them
// all). With -xluaWide, every namespace under
// UnityEngine too, for a longer list than any real configuration.
// Usage: -executeMethod XluaCompileTiming.WriteTypeList -quit -xluaTypesOut <file> [-xluaWide]
public static class XluaCompileTiming
{
    static readonly string[] Exclude =
    {
        "HideInInspector", "ExecuteInEditMode", "AddComponentMenu", "ContextMenu", "RequireComponent",
        "DisallowMultipleComponent", "SerializeField", "AssemblyIsEditorAssembly", "Attribute", "Types",
        "UnitySurrogateSelector", "TrackedReference", "TypeInferenceRules", "FFTWindow", "RPC", "Network",
        "MasterServer", "BitStream", "HostData", "ConnectionTesterStatus", "GUI", "EventType", "EventModifiers",
        "FontStyle", "TextAlignment", "TextEditor", "TextEditorDblClickSnapping", "TextGenerator", "TextClipping",
        "Gizmos", "ADBannerView", "ADInterstitialAd", "Android", "Tizen", "jvalue", "iPhone", "iOS", "Windows",
        "CalendarIdentifier", "CalendarUnit", "ClusterInput", "FullScreenMovieControlMode",
        "FullScreenMovieScalingMode", "Handheld", "LocalNotification", "NotificationServices",
        "RemoteNotificationType", "RemoteNotification", "SamsungTV", "TextureCompressionQuality",
        "TouchScreenKeyboardType", "TouchScreenKeyboard", "MovieTexture", "UnityEngineInternal", "Terrain", "Tree",
        "SplatPrototype", "DetailPrototype", "DetailRenderMode", "MeshSubsetCombineUtility", "AOT", "Social",
        "Enumerator", "SendMouseEvents", "Cursor", "Flash", "ActionScript", "OnRequestRebuild", "Ping",
        "ShaderVariantCollection", "SimpleJson.Reflection", "CoroutineTween", "GraphicRebuildTracker",
        "Advertisements", "UnityEditor", "WSA", "EventProvider", "Apple", "Motion",
        "UnityEngine.UI.ReflectionMethodsCache", "NativeLeakDetection", "NativeLeakDetectionMode",
        "WWWAudioExtensions", "UnityEngine.Experimental",
    };

    static bool Obsolete(Type type)
    {
        for (var t = type; t != null; t = t.DeclaringType)
        {
            if (t.IsDefined(typeof(ObsoleteAttribute), false)) return true;
        }
        return false;
    }

    public static void WriteTypeList()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-xluaTypesOut");
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("-xluaTypesOut <file> is missing");
        bool wide = Array.IndexOf(args, "-xluaWide") >= 0;
        var player = CompilationPipeline.GetAssemblies(AssembliesType.Player).Single(x => x.name == "Assembly-CSharp");
        var referenced = player.allReferences.Select(Path.GetFileNameWithoutExtension).ToArray();
#pragma warning disable UAC0005
        var types = from assembly in AppDomain.CurrentDomain.GetAssemblies()
#pragma warning restore UAC0005
                    let name = assembly.GetName().Name
                    where !assembly.IsDynamic && (name == "UnityEngine" || name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                          && name.EndsWith("Module", StringComparison.Ordinal)) && referenced.Contains(name)
                    from type in assembly.GetExportedTypes()
                    where (type.Namespace == "UnityEngine" || wide && type.Namespace != null && type.Namespace.StartsWith("UnityEngine.", StringComparison.Ordinal))
                          && !Exclude.Any(e => type.FullName.Contains(e)) && !type.FullName.Contains("<")
                          && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                          && type.BaseType != typeof(MulticastDelegate) && !type.IsInterface && !type.IsEnum
                          && !type.ContainsGenericParameters && !Obsolete(type)
                    select type.FullName.Replace('+', '.');
        string[] names = types.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        File.WriteAllLines(args[i + 1], names);
        Debug.Log("XLUA_TIMING_TYPES: " + names.Length + " types in " + args[i + 1]);
    }
}
