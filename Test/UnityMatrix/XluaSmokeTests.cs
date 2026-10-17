using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;

public sealed class XluaSmokeTests
{
    [Test]
    public void Lua55BridgeRunsInTheEditor()
    {
        var result = XluaSmoke.Run();
        StringAssert.StartsWith("XLUA_NG_SMOKE_PASS: Lua 5.5", result);
    }

    // require finds a module that exists only in StreamingAssets through the last
    // searcher, LoadFromStreamingAssetsPath. XLUA_NO_STREAMING_ASSETS_LOADER
    // (prepare.py --no-streaming-assets-loader) leaves that searcher out, so the
    // same require fails with Lua's "not found" error.
    [Test]
    public void StreamingAssetsSearcherFollowsTheDefine()
    {
        const string module = "xlua_ng_streaming_assets_probe";
        var dir = Application.streamingAssetsPath;
        var createdDir = !Directory.Exists(dir);
        var file = Path.Combine(dir, module + ".lua");
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, "return 'from StreamingAssets'");
        try
        {
            using (var env = new LuaEnv())
            {
#if !XLUA_NO_STREAMING_ASSETS_LOADER
                LogAssert.Expect(LogType.Warning, new Regex("load lua file from StreamingAssets is obsolete"));
#endif
                var result = env.DoString("return pcall(require, '" + module + "')");
#if XLUA_NO_STREAMING_ASSETS_LOADER
                Assert.AreEqual(false, result[0]);
                StringAssert.Contains("module '" + module + "' not found", (string)result[1]);
                StringAssert.DoesNotContain("streamingAssetsPath", (string)result[1]);
#else
                Assert.AreEqual(true, result[0], result[1] as string);
                Assert.AreEqual("from StreamingAssets", result[1]);
#endif
            }
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".meta");
            if (createdDir)
            {
                Directory.Delete(dir, true);
                File.Delete(dir + ".meta");
            }
        }
    }
}
