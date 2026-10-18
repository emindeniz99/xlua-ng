using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using XLua;
using XluaRefReadonlyLib;

// The source generator's `ref readonly` rules in Unity (XluaSourceGenRefReadonlyConfig.cs).
// Members the editor's Roslyn cannot call are left out of the wrappers, so Lua
// cannot reach them there; the others go through the generated wrappers.
public sealed class XluaSourceGenRefReadonlyTests
{
#if UNITY_6000_6_OR_NEWER
    static readonly bool CompilerUsesThem = true;
#else
    static readonly bool CompilerUsesThem = false;
#endif

    static Type LoaderClass(Type type)
    {
        Type registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);
        var loaders = (IDictionary)registry.GetField("loaders", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var loader = (Delegate)loaders[type];
        Assert.IsNotNull(loader, "no registered loader for " + type);
        return loader.Method.DeclaringType;
    }

    [Test]
    public void WrappersFollowTheEditorsCompiler()
    {
        foreach (Type type in new[] { typeof(Gauge), typeof(GaugeImpl), typeof(Dial), typeof(DialVirtual) })
        {
            Type wrapper = LoaderClass(type);
            StringAssert.StartsWith("XLua.Generated.", wrapper.Namespace, type + " wrapper");
            Assert.AreEqual("Assembly-CSharp", wrapper.Assembly.GetName().Name, type + " wrapper assembly");
        }
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local G = CS.XluaRefReadonlyLib.Gauge
                local g = G(5)
                local d = CS.XluaRefReadonlyLib.Dial()
                local before = d[3]
                d[3] = 10
                local scaled = pcall(function() return g:Scaled(2) end)
                local v = CS.XluaRefReadonlyLib.DialVirtual()
                local dialed = pcall(function() v[3] = 1; return v[3] end)
                return g:Plain(3), G.Twice(4), CS.XluaRefReadonlyLib.GaugeImpl():Read(2), before, d[3], d.Offset,
                    scaled, scaled and g:Scaled(2) or 0, dialed and v[3] or nil");
            TestContext.WriteLine("XLUA_SOURCEGEN_REF_READONLY: scaled=" + r[6] + " dialVirtual=" + r[8]);
            Assert.AreEqual(8.0, Convert.ToDouble(r[0]), 1e-5, "Gauge.Plain(ref readonly int)");
            Assert.AreEqual(8.0, Convert.ToDouble(r[1]), 1e-5, "Gauge.Twice(ref readonly int)");
            Assert.AreEqual(2.5, Convert.ToDouble(r[2]), 1e-5, "GaugeImpl.Read(ref readonly int)");
            Assert.AreEqual(3.0, Convert.ToDouble(r[3]), 1e-5, "Dial[ref readonly int] before the set");
            Assert.AreEqual(10.0, Convert.ToDouble(r[4]), 1e-5, "Dial[ref readonly int] after the set");
            Assert.AreEqual(7, Convert.ToInt32(r[5]), "Dial.Offset after the set");
            Assert.AreEqual(CompilerUsesThem, r[6], "Gauge.Scaled in the wrapper");
            Assert.AreEqual(CompilerUsesThem, r[8] != null, "DialVirtual's indexer in the wrapper");
            if (CompilerUsesThem)
            {
                Assert.AreEqual(25.0, Convert.ToDouble(r[7]), 1e-5, "Gauge.Scaled(ref readonly int)");
                Assert.AreEqual(34, Convert.ToInt32(r[8]), "DialVirtual[ref readonly int]");
            }
        }
    }
}
