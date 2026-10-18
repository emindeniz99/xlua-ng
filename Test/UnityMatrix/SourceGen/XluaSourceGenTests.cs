using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;
using XLua.Binding;

// The xLua source generator loaded by Unity (prepare.py --source-gen: the
// asmdef layout, the generator in Assets/XLua/Src/Analyzers, SgLocal in
// Assembly-CSharp and SgGame.Shop in the asmdef SgGame). The generator runs in
// both assemblies; its registration runs from [InitializeOnLoadMethod] in edit
// mode and from [RuntimeInitializeOnLoadMethod] when play mode starts, and Lua
// then calls the types through the generated wrappers, not through reflection.
// editor_tests.py runs these once, in mode "reflection" (no Generate Code).
public sealed class XluaSourceGenTests
{
    static readonly Type Registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);

    static object RegistryField(string name)
    {
        FieldInfo field = Registry.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "BindingRegistry." + name);
        return field.GetValue(null);
    }

    static HashSet<string> Registered
    {
        get { return (HashSet<string>)RegistryField("registered"); }
    }

    static IDictionary Loaders
    {
        get { return (IDictionary)RegistryField("loaders"); }
    }

    static GeneratedBindingsAttribute Unit(Assembly assembly)
    {
        var units = assembly.GetCustomAttributes(typeof(GeneratedBindingsAttribute), false);
        Assert.AreEqual(1, units.Length, "[assembly: GeneratedBindings] in " + assembly.GetName().Name);
        return (GeneratedBindingsAttribute)units[0];
    }

    // The class that the registered loader of a type belongs to.
    static Type LoaderClass(Type type)
    {
        var loader = (Delegate)Loaders[type];
        Assert.IsNotNull(loader, "no registered loader for " + type);
        return loader.Method.DeclaringType;
    }

    static void AssertGeneratedWrapper(Type type, Assembly assembly)
    {
        Type wrapper = LoaderClass(type);
        TestContext.WriteLine("XLUA_SOURCEGEN_WRAPPER: " + type + " -> " + wrapper.FullName + " in " + wrapper.Assembly.GetName().Name);
        StringAssert.StartsWith("XLua.Generated.", wrapper.Namespace, type + " wrapper namespace");
        StringAssert.StartsWith("W_", wrapper.Name, type + " wrapper name");
        Assert.AreEqual(assembly, wrapper.Assembly, type + " wrapper assembly");
    }

    // Lua calls into both assemblies and the UnityEngine type; every call
    // must come from a source-generated wrapper.
    static string[] CallFromLua()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local shop = CS.SgGame.Shop(4)
                shop.Level = CS.SgGame.Tier.High
                local s = CS.SgLocal('lua')
                local hidden = s.Hidden
                local rect = CS.UnityEngine.Rect(1, 2, 3, 4)
                return CS.SgLocal.Caller(), CS.SgGame.Shop.Caller(),
                    shop:Price(2), shop:Price(2, 'half'), shop.Stock, s:Total(shop, 1), s.Name,
                    s:Length(CS.UnityEngine.Vector3(3, 4, 0)), tostring(hidden), rect.width * rect.height,
                    rect:Contains(CS.UnityEngine.Vector2(2, 3))");
            TestContext.WriteLine("XLUA_SOURCEGEN_CALLERS: " + r[0] + ", " + r[1]);
            Assert.AreEqual(40, Convert.ToInt32(r[2]), "Shop.Price(int)");
            Assert.AreEqual(20, Convert.ToInt32(r[3]), "Shop.Price(int, string)");
            Assert.AreEqual(4, Convert.ToInt32(r[4]), "Shop.Stock");
            Assert.AreEqual(21, Convert.ToInt32(r[5]), "SgLocal.Total(Shop, int)");
            Assert.AreEqual("lua", r[6], "SgLocal(string)");
            Assert.AreEqual(5.0, Convert.ToDouble(r[7]), 1e-5, "SgLocal.Length(in Vector3)");
            Assert.AreEqual("nil", r[8], "BlackList: the wrapper has no SgLocal.Hidden");
            Assert.AreEqual(12.0, Convert.ToDouble(r[9]), 1e-5, "Rect.width * Rect.height");
            Assert.AreEqual(true, r[10], "Rect.Contains(Vector2)");
            return new[] { (string)r[0], (string)r[1] };
        }
    }

    static void AssertCallersAreWrappers(string[] callers)
    {
        foreach (string caller in callers)
        {
            StringAssert.StartsWith("XLua.Generated.", caller, "Lua reached the type through " + caller);
            StringAssert.Contains(".W_", caller);
        }
        StringAssert.Contains(".W_SgLocal_", callers[0]);
        StringAssert.Contains(".W_SgGame_Shop_", callers[1]);
    }

    [Test]
    public void BindingsAreGeneratedInEachAssembly()
    {
        Assembly local = typeof(SgLocal).Assembly, game = typeof(SgGame.Shop).Assembly;
        Assert.AreEqual("Assembly-CSharp", local.GetName().Name);
        Assert.AreEqual("SgGame", game.GetName().Name);
        foreach (Assembly assembly in new[] { local, game })
        {
            GeneratedBindingsAttribute unit = Unit(assembly);
            TestContext.WriteLine("XLUA_SOURCEGEN_UNIT: " + assembly.GetName().Name + " " + unit.Bindings.FullName + " ["
                                  + string.Join(", ", unit.Types.Select(t => t.FullName).ToArray()) + "]");
            Assert.AreEqual(BindingApi.Version, unit.ApiVersion);
            Assert.AreEqual("Bindings", unit.Bindings.Name);
            StringAssert.StartsWith("XLua.Generated.", unit.Bindings.Namespace);
            Assert.AreEqual(assembly, unit.Bindings.Assembly);
            MethodInfo register = unit.Bindings.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(register, "Bindings.Register");
            var runtime = (RuntimeInitializeOnLoadMethodAttribute)register.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Single();
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, runtime.loadType);
            Assert.IsTrue(unit.Bindings.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Any(m => m.GetCustomAttributes(typeof(InitializeOnLoadMethodAttribute), false).Length == 1), "[InitializeOnLoadMethod]");
        }
        // With --roslyn-sdk Assembly-CSharp also wraps the XluaRefReadonlyLib types.
        CollectionAssert.IsSupersetOf(Unit(local).Types, new[] { typeof(SgLocal), typeof(Rect) });
        CollectionAssert.DoesNotContain(Unit(local).Types, typeof(SgGame.Shop), "XLSG008: SgGame wraps Shop");
        CollectionAssert.AreEquivalent(new[] { typeof(SgGame.Shop), typeof(SgGame.Tier) }, Unit(game).Types);
        Assert.AreEqual(0, typeof(LuaEnv).Assembly.GetCustomAttributes(typeof(GeneratedBindingsAttribute), false).Length,
            "the generator emits nothing in XLua itself");
    }

    // [InitializeOnLoadMethod] registered both units after the last domain
    // reload, before any LuaEnv: the first-LuaEnv scan never ran.
    [Test]
    public void RegisteredInEditMode()
    {
        Assert.IsFalse((bool)Registry.GetField("scanned", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null),
            "BindingRegistry.EnsureRegistered scanned the assemblies");
        CollectionAssert.IsSupersetOf(Registered, new[] { "Assembly-CSharp", "SgGame" });
        AssertGeneratedWrapper(typeof(SgLocal), typeof(SgLocal).Assembly);
        AssertGeneratedWrapper(typeof(Rect), typeof(SgLocal).Assembly);
        AssertGeneratedWrapper(typeof(SgGame.Shop), typeof(SgGame.Shop).Assembly);
        AssertGeneratedWrapper(typeof(SgGame.Tier), typeof(SgGame.Shop).Assembly);
    }

    [Test]
    public void LuaCallsThroughTheGeneratedWrappers()
    {
        AssertCallersAreWrappers(CallFromLua());
    }

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

    // Removes the fixture's registrations, as if the domain were new.
    static void Forget()
    {
        Registered.Remove("Assembly-CSharp");
        Registered.Remove("SgGame");
        foreach (Type type in new[] { typeof(SgLocal), typeof(Rect), typeof(SgGame.Shop), typeof(SgGame.Tier) })
        {
            Loaders.Remove(type);
        }
    }

    // Domain reload off: statics survive, and [RuntimeInitializeOnLoadMethod]
    // (SubsystemRegistration) runs Register at every start of play mode. The
    // first session starts with the registrations removed, so only that call
    // can bring them back; the second keeps them, so Register runs again for
    // units that are already registered, which must be a quiet no-op.
    [UnityTest]
    public IEnumerator RegisteredAtEachPlayModeStartWithoutDomainReload()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        var errors = new List<string>();
        Application.LogCallback collect = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        Application.logMessageReceived += collect;
        try
        {
            for (int session = 1; session <= 2; session++)
            {
                if (session == 1)
                {
                    Forget();
                    Assert.IsFalse(Loaders.Contains(typeof(SgLocal)), "registration removed");
                }
                yield return new EnterPlayMode(false);
                Assert.IsTrue(EditorApplication.isPlaying);
                CollectionAssert.IsSupersetOf(Registered, new[] { "Assembly-CSharp", "SgGame" }, "session " + session);
                AssertGeneratedWrapper(typeof(SgLocal), typeof(SgLocal).Assembly);
                AssertGeneratedWrapper(typeof(SgGame.Shop), typeof(SgGame.Shop).Assembly);
                AssertCallersAreWrappers(CallFromLua());
                yield return new ExitPlayMode();
            }
        }
        finally
        {
            Application.logMessageReceived -= collect;
        }
        CollectionAssert.IsEmpty(errors);
    }

    const string ReloadKey = "XluaSourceGenTests.Reload";

    // Domain reload on: the new domain registers again, and Lua in play mode
    // still goes through the generated wrappers.
    [UnityTest]
    public IEnumerator RegisteredInPlayModeAfterADomainReload()
    {
        if (SessionState.GetInt(ReloadKey, 0) == 0)
        {
            EditorSettings.enterPlayModeOptionsEnabled = false;
            SessionState.SetInt(ReloadKey, 1);
            yield return new EnterPlayMode();
        }
        if (SessionState.GetInt(ReloadKey, 0) == 1)
        {
            SessionState.SetInt(ReloadKey, 2);
            Assert.IsTrue(EditorApplication.isPlaying);
            CollectionAssert.IsSupersetOf(Registered, new[] { "Assembly-CSharp", "SgGame" });
            AssertGeneratedWrapper(typeof(SgLocal), typeof(SgLocal).Assembly);
            AssertCallersAreWrappers(CallFromLua());
            yield return new ExitPlayMode();
        }
        SessionState.EraseInt(ReloadKey);
    }
}
