using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSObjectWrapEditor;
using NUnit.Framework;
using UnityEngine;
using XLua;
using XluaRefReadonlyLib;

// The precompiled XluaRefReadonlyLib.dll (XluaRefReadonlyLib.cs) has virtual,
// interface and delegate members with C# 12 `ref readonly` parameters. Before
// Unity 6000.6 the editor's Roslyn cannot use them (CS0570), so Generate Code
// and LuaCATS skip them with a warning; 6000.6 keeps them. prepare.py copies
// this file only with --roslyn-sdk, and editor_tests.py then requires it.
public sealed class XluaRefReadonlyLibTests
{
#if UNITY_6000_6_OR_NEWER
    static readonly bool CompilerUsesThem = true;
#else
    static readonly bool CompilerUsesThem = false;
#endif

    static readonly string[] Skipped =
    {
        "xLua skipped [CSharpCallLua] XluaRefReadonlyLib.Measure (its XluaRefReadonlyLib.Measure.Invoke)",
        "xLua skipped [CSharpCallLua] XluaRefReadonlyLib.IGauge (its XluaRefReadonlyLib.IGauge.Read)",
        "xLua skipped XluaRefReadonlyLib.Gauge.Scaled",
        "xLua skipped XluaRefReadonlyLib.IGauge.Read",
        "xLua skipped XluaRefReadonlyLib.DialVirtual.get_Item",
        "xLua skipped XluaRefReadonlyLib.DialVirtual.set_Item",
    };

    const string Reason = ": the C# compiler in this Unity version cannot call members with ref readonly parameters (CS0570)";

    static bool Generated
    {
        get { return XluaEditorTestSteps.ReadMode() == XluaEditorTestSteps.Generated; }
    }

    // LuaCATS runs the same member checks as Generate Code; each skipped
    // member is reported once per generation.
    [Test]
    public void GenerationSkipsMembersTheCompilerCannotUse()
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

        string gauge = XluaEditorToolsTests.Definitions("XluaRefReadonlyLib.Gauge.lua");
        XluaEditorToolsTests.Has(gauge, "XluaRefReadonlyLib.Gauge.lua",
            "function CS.XluaRefReadonlyLib.Gauge:Plain(x) end\n",
            "function CS.XluaRefReadonlyLib.Gauge.Twice(x) end\n");
        XluaEditorToolsTests.Has(XluaEditorToolsTests.Definitions("XluaRefReadonlyLib.GaugeImpl.lua"), "XluaRefReadonlyLib.GaugeImpl.lua",
            "function CS.XluaRefReadonlyLib.GaugeImpl:Read(x) end\n");
        Assert.AreEqual(warnings.Count, warnings.Distinct().Count(), "a skipped member was reported more than once");
        if (CompilerUsesThem)
        {
            CollectionAssert.IsEmpty(warnings);
            StringAssert.Contains("function CS.XluaRefReadonlyLib.Gauge:Scaled(x) end\n", gauge);
            CollectionAssert.Contains(Generator.CSharpCallLua, typeof(Measure));
            CollectionAssert.Contains(Generator.CSharpCallLua, typeof(IGauge));
        }
        else
        {
            foreach (string skipped in Skipped)
            {
                CollectionAssert.Contains(warnings, skipped + Reason);
            }
            StringAssert.DoesNotContain("Scaled", gauge);
            CollectionAssert.DoesNotContain(Generator.CSharpCallLua, typeof(Measure));
            CollectionAssert.DoesNotContain(Generator.CSharpCallLua, typeof(IGauge));
        }
    }

    [Test]
    public void GeneratedCodeHasOnlyUsableMembers()
    {
        if (!Generated)
        {
            Assert.Pass("no generated code in mode " + XluaEditorTestSteps.ReadMode());
        }
        string dir = GeneratorConfig.common_path;
        string wrap = File.ReadAllText(Path.Combine(dir, "XluaRefReadonlyLib_GaugeWrap.cs"));
        StringAssert.Contains(".Plain(", wrap);
        StringAssert.Contains("XluaRefReadonlyLib.Gauge.Twice(", wrap);
        string bridges = File.ReadAllText(Path.Combine(dir, "DelegatesGensBridge.cs"));
        Assert.AreEqual(CompilerUsesThem, wrap.Contains(".Scaled("), "Gauge.Scaled in the wrapper");
        Assert.AreEqual(CompilerUsesThem, bridges.Contains("typeof(XluaRefReadonlyLib.Measure)"), "Measure delegate bridge");
        Assert.AreEqual(CompilerUsesThem, File.Exists(Path.Combine(dir, "XluaRefReadonlyLib_IGaugeBridge.cs")), "IGauge interface bridge");
        // Dial's indexer: this[ref x] for Roslyn 4.10, its accessors for Roslyn 4.3.
        string dial = File.ReadAllText(Path.Combine(dir, "XluaRefReadonlyLib_DialWrap.cs"));
        StringAssert.Contains(CompilerUsesThem ? "gen_to_be_invoked[ref index]" : "gen_to_be_invoked.get_Item(ref index)", dial);
        StringAssert.Contains(CompilerUsesThem ? "gen_to_be_invoked[ref key] = gen_value;" : "gen_to_be_invoked.set_Item(ref key, gen_value);", dial);
        string dialVirtual = File.ReadAllText(Path.Combine(dir, "XluaRefReadonlyLib_DialVirtualWrap.cs"));
        Assert.AreEqual(CompilerUsesThem, dialVirtual.Contains("gen_to_be_invoked[ref index]"), "DialVirtual getter in the wrapper");
        Assert.AreEqual(CompilerUsesThem, dialVirtual.Contains("gen_to_be_invoked[ref key]"), "DialVirtual setter in the wrapper");
    }

    // Non-virtual ref readonly members work everywhere. The skipped ones still
    // work through reflection and emitted code, but not through generated code.
    [Test]
    public void LuaCallsUsableRefReadonlyMembers()
    {
        bool all = CompilerUsesThem || !Generated;
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
