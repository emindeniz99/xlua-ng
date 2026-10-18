using System;
using System.IO;
using System.Linq;
using UnityEngine;

// The mode of the editor suite and batch-mode entry points for the harness
// (-executeMethod ... -quit). prepare.py --editor-tools writes the mode into
// XluaEditorTestMode.txt: "sourcegen" when it installed the xLua source
// generator (--source-gen), which binds the suite's types at compile time, or
// "reflection" without it, where the Editor emits wrappers and [CSharpCallLua]
// bridges at run time (CodeEmit). Without that file the tests expect
// reflection. Generate Code and Clear Generated Code, the two steps this file
// had before, went with the legacy generator (milestone M11).
public static class XluaEditorTestSteps
{
    public const string Reflection = "reflection";
    public const string SourceGen = "sourcegen";

    public static string ModeFile
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "XluaEditorTestMode.txt")); }
    }

    public static string ReadMode()
    {
        return File.Exists(ModeFile) ? File.ReadAllText(ModeFile).Trim() : Reflection;
    }

    // Logs one XLUA_GENERATED_BINDINGS line for each [assembly:
    // XLua.Binding.GeneratedBindings] unit in the loaded assemblies (the source
    // generator's output), and XLUA_GENERATED_BINDINGS_DONE with their number.
    // layout_switch.py checks with it which assemblies the generator compiled.
    public static void ReportBindings()
    {
        int units = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            if (assembly.IsDynamic) continue;
            object[] attributes;
            try
            {
                attributes = assembly.GetCustomAttributes(false);
            }
            catch (Exception)
            {
                continue;
            }
            foreach (var attribute in attributes.Where(a => a.GetType().FullName == "XLua.Binding.GeneratedBindingsAttribute"))
            {
                var type = attribute.GetType();
                var bindings = (Type)type.GetProperty("Bindings").GetValue(attribute, null);
                var types = (Type[])type.GetProperty("Types").GetValue(attribute, null);
                Debug.Log("XLUA_GENERATED_BINDINGS: " + assembly.GetName().Name + " " + (bindings == null ? "(null)" : bindings.FullName)
                          + " " + types.Length + " [" + string.Join(", ", types.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray()) + "]");
                units++;
            }
        }
        Debug.Log("XLUA_GENERATED_BINDINGS_DONE: " + units + " units");
    }
}
