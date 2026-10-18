using System;
using System.Collections.Generic;
using CSObjectWrapEditor;
using NUnit.Framework;
using UnityEngine;
using XLua;
using XluaRefReadonlyLib;

// The precompiled XluaRefReadonlyLib.dll (XluaRefReadonlyLib.cs) has virtual,
// interface and delegate members with C# 12 `ref readonly` parameters, which
// the editor's Roslyn before Unity 6000.6 cannot use (CS0570). prepare.py
// copies this file only with --roslyn-sdk, and editor_tests.py then requires
// it. The source generator (XluaRefReadonlyLibConfig.cs) leaves out what the
// compiler cannot use (XLSG004 at compile time; XluaSourceGenRefReadonlyTests
// checks its wrappers). The skip warnings and the files of Generate Code,
// which this file checked before milestone M11, went with it.
public sealed class XluaRefReadonlyLibTests
{
#if UNITY_6000_6_OR_NEWER
    static readonly bool CompilerUsesThem = true;
#else
    static readonly bool CompilerUsesThem = false;
#endif

    // Wrapped by the source generator (mode "sourcegen").
    static bool Wrapped
    {
        get
        {
            Type registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);
            var loaders = (System.Collections.IDictionary)registry.GetField("loaders",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            return loaders.Contains(typeof(Gauge));
        }
    }

    // LuaCATS describes the library from Assembly-CSharp's manifest: the
    // members the wrappers bind, with no warnings of its own about skipped
    // members (it no longer reads configuration lists). Without the generator
    // it declares none of these types.
    [Test]
    public void LuaCATSDescribesTheLibraryFromTheManifest()
    {
        var warnings = new List<string>();
        Application.LogCallback collect = (message, stack, type) =>
        {
            if (type == LogType.Warning && message.StartsWith("xLua skipped")) warnings.Add(message);
        };
        Application.logMessageReceived += collect;
        try
        {
            Generator.GenLuaCATS();
        }
        finally
        {
            Application.logMessageReceived -= collect;
        }
        TestContext.WriteLine("XLUA_EDITOR_REF_READONLY_WARNINGS:\n  " + string.Join("\n  ", warnings.ToArray()));
        CollectionAssert.IsEmpty(warnings, "LuaCATS reported skipped members");
        Assert.AreEqual(XluaEditorTestSteps.ReadMode() == XluaEditorTestSteps.SourceGen, Wrapped, "Gauge wrapped by the source generator");
        if (!XluaEditorToolsTests.DeclaresConfiguredTypes("XluaRefReadonlyLib.Gauge.lua"))
        {
            return;
        }
        string gauge = XluaEditorToolsTests.Definitions("XluaRefReadonlyLib.Gauge.lua");
        XluaEditorToolsTests.Has(gauge, "XluaRefReadonlyLib.Gauge.lua",
            "function CS.XluaRefReadonlyLib.Gauge:Plain(x) end\n",
            "function CS.XluaRefReadonlyLib.Gauge.Twice(x) end\n");
        XluaEditorToolsTests.Has(XluaEditorToolsTests.Definitions("XluaRefReadonlyLib.GaugeImpl.lua"), "XluaRefReadonlyLib.GaugeImpl.lua",
            "function CS.XluaRefReadonlyLib.GaugeImpl:Read(x) end\n");
    }

    // Non-virtual ref readonly members work everywhere. Before Roslyn 4.10 the
    // generator leaves the others out of its wrappers; through reflection and
    // emitted bridges they work.
    [Test]
    public void LuaCallsUsableRefReadonlyMembers()
    {
        bool all = CompilerUsesThem || !Wrapped;
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local G = CS.XluaRefReadonlyLib.Gauge
                local g = G(5)
                local scaled, called, dialed = 0, 0, 0
                if " + (all ? "true" : "false") + @" then
                    scaled = g:Scaled(2)
                    called = G.Call(function(x) return x + 1 end, 3)
                    local v = CS.XluaRefReadonlyLib.DialVirtual()
                    v[3] = 1
                    dialed = v[3]
                end
                local d = CS.XluaRefReadonlyLib.Dial()
                local before = d[3]
                d[3] = 10
                return g:Plain(3), G.Twice(4), CS.XluaRefReadonlyLib.GaugeImpl():Read(2), scaled, called,
                    before, d[3], d.Offset, dialed");
            Assert.AreEqual(8.0, Convert.ToDouble(r[0]), 1e-5, "Gauge.Plain(ref readonly int)");
            Assert.AreEqual(8.0, Convert.ToDouble(r[1]), 1e-5, "Gauge.Twice(ref readonly int)");
            Assert.AreEqual(2.5, Convert.ToDouble(r[2]), 1e-5, "GaugeImpl.Read(ref readonly int)");
            Assert.AreEqual(3.0, Convert.ToDouble(r[5]), 1e-5, "Dial[ref readonly int] before the set");
            Assert.AreEqual(10.0, Convert.ToDouble(r[6]), 1e-5, "Dial[ref readonly int] after the set");
            Assert.AreEqual(7, Convert.ToInt32(r[7]), "Dial.Offset after the set");
            if (all)
            {
                Assert.AreEqual(25.0, Convert.ToDouble(r[3]), 1e-5, "Gauge.Scaled(ref readonly int)");
                Assert.AreEqual(4.0, Convert.ToDouble(r[4]), 1e-5, "Measure delegate bridge");
                Assert.AreEqual(34, Convert.ToInt32(r[8]), "DialVirtual[ref readonly int]");
            }
        }
    }
}
