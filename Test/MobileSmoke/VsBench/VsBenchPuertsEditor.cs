// vs_bench.py copies this into the PuerTS project's Assets/Editor. Generate runs before the build in a launch of its
// own: PuerTS's xIl2cpp glue for Android with wrappers for the [Binding] types and bridges for the bench's
// delegates (CSharpFileExporter.GenAll with onlyConfigure, as the "Minimal Bridge" menu item but with wrappers, so the
// bound types get generated code as in xlua-ng's players), the extension method infos and link.xml.
using System;
using System.Collections.Generic;
using System.IO;
using Puerts;
using Puerts.Editor.Generator;
using UnityEditor;
using UnityEngine;
using VsBench;

[Configure]
public class VsBenchPuertsConfig
{
    [Binding]
    public static IEnumerable<Type> Bindings
    {
        get
        {
            return new[] {
                typeof(TestTargets), typeof(TestClass), typeof(TestStruct), typeof(TestEnum), typeof(Vector3),
                typeof(Step), typeof(Void0), typeof(Str0), typeof(VoidInt3), typeof(RetInt3), typeof(RetLong3),
                typeof(RetDouble3), typeof(RetString3), typeof(RetObject3), typeof(RetClass3), typeof(RetVector33),
                typeof(RetStruct3), typeof(Work), typeof(Num0),
            };
        }
    }
}

public static class VsBenchPuertsEditor
{
    public static void Generate()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("generate with -buildTarget Android: the glue header bakes the target's defines");
        var saveTo = Puerts.Editor.Generator.PathHelper.GetIl2cppPluginPath();
        Directory.CreateDirectory(saveTo);
        CSharpFileExporter.GenAll(saveTo, true, false);
        var codeOutputDir = Configure.GetCodeOutputDirectory();
        Directory.CreateDirectory(codeOutputDir);
        CSharpFileExporter.GenExtensionMethodInfos(codeOutputDir);
        CSharpFileExporter.GenLinkXml(codeOutputDir);
        AssetDatabase.Refresh();
        Debug.Log("XLUA_VSBENCH_GENERATED: " + saveTo);
    }
}
