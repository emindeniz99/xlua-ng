using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The package's samples, imported by prepare.py --samples as Package Manager
// imports them (Assets/Samples/<displayName>/<version>/<sample>). They compile
// into Assembly-CSharp, which this test assembly references.
public sealed class XluaSampleTests
{
    // SignatureLoaderTest read its signed Lua files from Assets/XLua/Examples in the
    // Editor. Imported, its loader found nothing there, and require fell through to the
    // Resources loader, which returned the signed bytes unchecked: "signatured1.lua:1:
    // syntax error near '}'".
    [Test]
    public void SignatureLoaderExampleRunsFromTheImportedSample()
    {
        var go = new GameObject("SignatureLoaderTest");
        try
        {
            var example = go.AddComponent<XLuaTest.SignatureLoaderTest>();
            string script = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.MonoScript.FromMonoBehaviour(example));
            StringAssert.StartsWith("Assets/Samples/", script);
            Assert.IsFalse(Directory.Exists(Path.Combine(Application.dataPath, "XLua/Examples")), "the old location exists");
            LogAssert.Expect(LogType.Log, "LUA: signatured3: this source was signatured!");
            LogAssert.Expect(LogType.Log, "LUA: signatured1: this source was signatured!");
            LogAssert.Expect(LogType.Log, "LUA: signatured2: this source was signatured!");
            typeof(XLuaTest.SignatureLoaderTest).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(example, null);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
