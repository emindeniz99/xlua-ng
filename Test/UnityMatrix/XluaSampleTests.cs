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
    // Editor, so the imported sample failed with "module 'signatured1' not found".
    [Test]
    public void SignatureLoaderExampleRunsFromTheImportedSample()
    {
        var go = new GameObject("SignatureLoaderTest");
        try
        {
            var example = go.AddComponent<XLuaTest.SignatureLoaderTest>();
            string folder = XLuaTest.SignatureLoaderTest.ExampleFolder(example);
            StringAssert.Contains("/Assets/Samples/", folder.Replace('\\', '/'));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "signatured1.lua")), folder);
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
