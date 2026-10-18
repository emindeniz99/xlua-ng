using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;
using XluaEditorTest;

// xLua across play sessions, with Unity's Enter Play Mode Options
// (EditorSettings.enterPlayModeOptions). With DisableDomainReload, static
// fields and the native Lua states they point to outlive each session.
// prepare.py --fast-enter-play-mode copies these tests and PlayModeHost;
// editor_tests.py runs them in the mode of the project: with the source
// generator's wrappers and bridges, or with emitted ones (reflection).
public sealed class XluaPlayModeTests
{
    const int Sessions = 3;
    const string Expected = "describe=int:7 sum=2.5 length=2.5 apply=34 y=2 value={0} delegate=43";

    bool savedEnabled;
    EnterPlayModeOptions savedOptions;

    [SetUp]
    public void SaveSettings()
    {
        savedEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        savedOptions = EditorSettings.enterPlayModeOptions;
    }

    [TearDown]
    public void RestoreSettings()
    {
        EditorSettings.enterPlayModeOptionsEnabled = savedEnabled;
        EditorSettings.enterPlayModeOptions = savedOptions;
    }

    static IDictionary Translators()
    {
        FieldInfo field = typeof(ObjectTranslatorPool).GetField("translators", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "ObjectTranslatorPool.translators");
        return (IDictionary)field.GetValue(ObjectTranslatorPool.Instance);
    }

    // CodeEmit's dynamic assemblies, which no Unity API lists. Unity 6.6
    // warns (UAC0005) that GetAssemblies may include unloaded assemblies.
#pragma warning disable UAC0005
    static int EmitAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies().Count(a => a.IsDynamic && a.GetName().Name == "XLuaCodeEmit");
    }
#pragma warning restore UAC0005

    static string Errors()
    {
        return string.Join("\n", PlayModeState.Errors.Select(e => e.ToString()).ToArray());
    }

    static IEnumerator RunSessions(EnterPlayModeOptions options, bool dispose)
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = options;
        PlayModeState.Reset();
        if (PlayModeState.Shared == null)
        {
            PlayModeState.Shared = new LuaEnv();
            PlayModeState.Shared.DoString("sessions = 0");
        }
        int translators = Translators().Count;
        int emitted = EmitAssemblies();
        int undisposed = PlayModeState.Undisposed.Count;
        for (int s = 1; s <= Sessions; s++)
        {
            yield return new EnterPlayMode(false);
            Assert.IsTrue(EditorApplication.isPlaying, "session " + s);

            // What the previous session left: a disposed env refuses calls,
            // an undisposed one still answers.
            if (PlayModeState.StaleFunction != null)
            {
                var e = Assert.Throws<ObjectDisposedException>(() => PlayModeState.StaleFunction.Call(1, "x"));
                StringAssert.Contains("disposed", e.Message);
                Assert.Throws<ObjectDisposedException>(() => PlayModeState.StaleTable.Get<int>("session"));
                PlayModeState.StaleFunction.Dispose();
                PlayModeState.StaleTable.Dispose();
                PlayModeState.StaleFunction = null;
                PlayModeState.StaleTable = null;
            }
            if (PlayModeState.UndisposedDelegate != null)
            {
                Assert.AreEqual(12, PlayModeState.UndisposedDelegate(1, "ab"), "delegate into the previous session's env");
            }

            var host = new GameObject("xlua-playmode-host").AddComponent<PlayModeHost>();
            host.Session = s;
            host.DisposeOnDestroy = dispose;
            for (int frame = 0; frame < 300 && (!host.Started || PlayModeProbe.CoroutineSteps < 3); frame++)
            {
                yield return null;
            }
            Assert.IsEmpty(PlayModeState.Errors, Errors());
            Assert.IsTrue(host.Started, "session " + s + ": Start did not run");
            Assert.AreEqual(3, PlayModeProbe.CoroutineSteps, "session " + s + ": Lua coroutine steps");
            Assert.AreEqual("session=" + s + " " + string.Format(Expected, s), PlayModeState.Results[PlayModeState.Results.Count - 2]);
            StringAssert.EndsWith(" value=" + s, PlayModeState.Results[PlayModeState.Results.Count - 1]);

            yield return new ExitPlayMode();
            Assert.IsFalse(EditorApplication.isPlaying, "session " + s);
            Assert.IsEmpty(PlayModeState.Errors, Errors());
            TestContext.WriteLine("XLUA_PLAYMODE_SESSION {0}: translators={1} emitted={2} undisposed={3}",
                s, Translators().Count - translators, EmitAssemblies() - emitted, PlayModeState.Undisposed.Count - undisposed);
            // One more translator for each env left undisposed.
            Assert.AreEqual(dispose ? 0 : s, Translators().Count - translators, "session " + s + ": live translators");
            // The shared env emitted into the one XLuaCodeEmit assembly the
            // later envs reuse (Tencent/xLua#1118); with generated code none
            // is emitted.
            Assert.AreEqual(0, EmitAssemblies() - emitted, "session " + s + ": new XLuaCodeEmit assemblies");
        }
        foreach (string line in PlayModeState.Results)
        {
            TestContext.WriteLine("XLUA_PLAYMODE: " + line);
        }
        StringAssert.Contains("shared sessions=", PlayModeState.Results.Last());
    }

    [UnityTest]
    public IEnumerator FastEnterPlayModeDisposesEachSession()
    {
        return RunSessions(EnterPlayModeOptions.DisableDomainReload, true);
    }

    [UnityTest]
    public IEnumerator FastEnterPlayModeWithoutSceneReload()
    {
        return RunSessions(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload, true);
    }

    [UnityTest]
    public IEnumerator FastEnterPlayModeLeavesEnvsUndisposed()
    {
        return RunSessions(EnterPlayModeOptions.DisableDomainReload, false);
    }

    // The same session with the domain reloaded on entering play mode, the
    // default. The reload restarts this enumerator after each yield with new
    // locals and fresh statics, so the session number lives in SessionState.
    const string ReloadKey = "XluaPlayModeTests.Reload";

    [UnityTest]
    public IEnumerator DomainReloadEachSession()
    {
        EditorSettings.enterPlayModeOptionsEnabled = false;
        SessionState.SetInt(ReloadKey, 0);
        while (SessionState.GetInt(ReloadKey, 0) < 2)
        {
            yield return new EnterPlayMode();
            int s = SessionState.GetInt(ReloadKey, 0) + 1;
            Assert.AreEqual(0, Translators().Count, "session " + s + ": translators after the reload");
            var host = new GameObject("xlua-playmode-host").AddComponent<PlayModeHost>();
            host.Session = s;
            for (int frame = 0; frame < 300 && (!host.Started || PlayModeProbe.CoroutineSteps < 3); frame++)
            {
                yield return null;
            }
            Assert.IsEmpty(PlayModeState.Errors, Errors());
            Assert.AreEqual(3, PlayModeProbe.CoroutineSteps, "session " + s + ": Lua coroutine steps");
            Assert.AreEqual("session=" + s + " " + string.Format(Expected, s), PlayModeState.Results[0]);
            Assert.AreEqual("shared sessions=1 value=" + s, PlayModeState.Results[1]);
            SessionState.SetInt(ReloadKey, s);
            yield return new ExitPlayMode();
            Assert.IsEmpty(PlayModeState.Errors, Errors());
        }
        SessionState.EraseInt(ReloadKey);
    }

    // A script change reloads the domain in edit mode: an env that was alive
    // before is gone from C#, and its native state is never closed. New envs
    // after the reload work.
    const string RecompileKey = "XluaPlayModeTests.Recompile";
    const string ProbeScript = "Assets/XluaRecompileProbe.cs";

    [UnityTest]
    public IEnumerator ScriptRecompileWithALiveEnv()
    {
        if (SessionState.GetInt(RecompileKey, 0) == 0)
        {
            SessionState.SetInt(RecompileKey, 1);
            PlayModeState.Shared = new LuaEnv();
            PlayModeState.Shared.DoString("function combine(a, b) return a + #b end");
            PlayModeState.UndisposedDelegate = PlayModeState.Shared.Global.Get<Combine>("combine");
            Assert.AreEqual(3, PlayModeState.UndisposedDelegate(1, "ab"));
            File.WriteAllText(ProbeScript, "public static class XluaRecompileProbe { public const int Stamp = " + DateTime.Now.Ticks % 100000 + "; }\n");
            yield return new RecompileScripts();
        }
        if (SessionState.GetInt(RecompileKey, 0) == 1)
        {
            SessionState.SetInt(RecompileKey, 2);
            Assert.IsNull(PlayModeState.Shared, "statics after the reload");
            Assert.AreEqual(0, Translators().Count, "translators after the reload");
            using (var env = new LuaEnv())
            {
                env.DoString("function combine(a, b) return a * 10 + #b end");
                var combine = env.Global.Get<Combine>("combine");
                Assert.AreEqual(43, combine(4, "abc"));
                object[] r = env.DoString("local t = CS.XluaEditorTest.Target(5); return t:MakePair(2, 0.5):Sum(), t:Describe(1)");
                Assert.AreEqual(2.5, Convert.ToDouble(r[0]), 1e-6);
                Assert.AreEqual("int:1", r[1]);
                combine = null;
                env.DoString("combine = nil");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                env.Tick();
            }
            AssetDatabase.DeleteAsset(ProbeScript);
            yield return new RecompileScripts();
        }
        SessionState.EraseInt(RecompileKey);
        Assert.IsFalse(File.Exists(ProbeScript));
    }
}
