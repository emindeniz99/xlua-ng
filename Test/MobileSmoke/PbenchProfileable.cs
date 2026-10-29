// size_report.py copies this into Assets/Editor of the +pbench projects: it marks the player bench APKs
// <profileable android:shell="true"/> (Android 10 and later), so simpleperf can sample a release player
// from adb without making it debuggable and without any setting on the device. The attribute changes
// nothing at run time. IPostGenerateGradleAndroidProject is Unity's documented build callback for the
// generated Gradle project; the path it gets is the unityLibrary module.
#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEngine;

public class PbenchProfileable : IPostGenerateGradleAndroidProject
{
    public int callbackOrder { get { return 0; } }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        var text = File.ReadAllText(manifest);
        if (text.Contains("<profileable")) return;
        int open = text.IndexOf("<application");
        if (open < 0) throw new System.Exception("PbenchProfileable: no <application> in " + manifest);
        int close = text.IndexOf('>', open);
        const string element = "<profileable android:shell=\"true\" />";
        text = text[close - 1] == '/'
            ? text.Substring(0, close - 1) + ">" + element + "</application>" + text.Substring(close + 1)
            : text.Substring(0, close + 1) + element + text.Substring(close + 1);
        File.WriteAllText(manifest, text);
        Debug.Log("XLUA_PBENCH_PROFILEABLE: " + manifest);
    }
}
#endif
