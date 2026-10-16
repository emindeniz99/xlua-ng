using System;
using System.Reflection;
using CSObjectWrapEditor;
using NUnit.Framework;
using XLua;
using XluaEditorTest;

// C# 12 `ref readonly` parameters (XluaRefReadonlyTarget.cs). prepare.py
// copies this file only with --csharp12, and editor_tests.py then requires
// these tests in the generated and cleared runs.
public sealed class XluaRefReadonlyTests
{
    // Roslyn marks `ref readonly` with RequiresLocationAttribute and embeds the
    // attribute in the assembly when the profile's BCL lacks it.
    [Test]
    public void RefReadonlyParametersAreInParameters()
    {
        ParameterInfo p = typeof(RefReadonlyTarget).GetMethod("Length").GetParameters()[0];
        Assert.IsTrue(p.ParameterType.IsByRef, "Length(ref readonly Pair) is by-ref");
        Assert.IsTrue(Utils.IsInParameter(p), "IsInParameter(ref readonly)");
        Assert.IsTrue(Utils.IsRefReadOnlyParameter(p), "IsRefReadOnlyParameter(ref readonly)");
        foreach (object attribute in p.GetCustomAttributes(false))
        {
            Type type = attribute.GetType();
            if (type.FullName == "System.Runtime.CompilerServices.RequiresLocationAttribute")
            {
                TestContext.WriteLine("XLUA_EDITOR_REQUIRES_LOCATION_ASSEMBLY: " + type.Assembly.GetName().Name);
            }
        }

        ParameterInfo inParam = typeof(Target).GetMethod("Length").GetParameters()[0];
        Assert.IsTrue(Utils.IsInParameter(inParam), "IsInParameter(in)");
        Assert.IsFalse(Utils.IsRefReadOnlyParameter(inParam), "IsRefReadOnlyParameter(in)");

        Type wrap = typeof(LuaEnv).Assembly.GetType("XLua.CSObjectWrap.XluaEditorTestRefReadonlyTargetWrap", false);
        Assert.AreEqual(XluaEditorTestSteps.ReadMode() == XluaEditorTestSteps.Generated, wrap != null, "generated RefReadonlyTarget wrapper");
    }

    // A call returns no extra value for a ref readonly parameter, and a Lua
    // callback returns only the result and the ref parameter.
    [Test]
    public void LuaPassesRefReadonlyParametersByValue()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local T, P = CS.XluaEditorTest.RefReadonlyTarget, CS.XluaEditorTest.Pair
                local function count(...) return select('#', ...) end
                local p = P(2, 0.5)
                local scaled = { T.Scale(3, 5) }
                local t = T(4)
                local measured = T.CallMeasure(function(...)
                    local q, calls = ...
                    return q.A + q.B + select('#', ...), calls + 1
                end)
                local measurer = T.CallMeasurer({ Measure = function(self, q, calls) return q.A * q.B, calls + 2 end })
                return count(T.Length(p)), T.Length(p), #scaled, scaled[1], scaled[2], scaled[3],
                    count(T.Product(p)), T.Product(p), count(T(4)), t.Offset, count(t:Add(p)), t:Add(p),
                    measured, measurer, count(CS.XluaEditorTest.RefReadonlyPlain():Plus(6)),
                    CS.XluaEditorTest.RefReadonlyPlain():Plus(6)");
            Assert.AreEqual(1, Convert.ToInt32(r[0]), "results of RefReadonlyTarget.Length(ref readonly Pair)");
            Assert.AreEqual(2.5, Convert.ToDouble(r[1]), 1e-5);
            Assert.AreEqual(3, Convert.ToInt32(r[2]), "results of Scale(ref readonly int, ref int, out int)");
            Assert.AreEqual(3, Convert.ToInt32(r[3]));
            Assert.AreEqual(15, Convert.ToInt32(r[4]));
            Assert.AreEqual(5, Convert.ToInt32(r[5]));
            Assert.AreEqual(1, Convert.ToInt32(r[6]), "results of Product(Pair) / Product(ref readonly Pair)");
            Assert.AreEqual(1.0, Convert.ToDouble(r[7]), 1e-5);
            Assert.AreEqual(1, Convert.ToInt32(r[8]), "results of new RefReadonlyTarget(ref readonly int)");
            Assert.AreEqual(4, Convert.ToInt32(r[9]));
            Assert.AreEqual(1, Convert.ToInt32(r[10]), "results of Add(ref readonly Pair)");
            Assert.AreEqual(6.5, Convert.ToDouble(r[11]), 1e-5);
            // The callback saw two arguments (2 + 0.5 + 2) and p stayed (2, 0.5).
            Assert.AreEqual(4.5f + ",2,2," + 0.5f, r[12], "MeasureRefReadonly(ref readonly Pair, ref int) bridge");
            Assert.AreEqual(1f + ",3,2," + 0.5f, r[13], "IRefReadonlyMeasure.Measure(ref readonly Pair, ref int) bridge");
            Assert.AreEqual(1, Convert.ToInt32(r[14]), "results of the Plus(ref readonly int) extension method");
            Assert.AreEqual(7, Convert.ToInt32(r[15]));
        }
    }

    [Test]
    public void LuaCATSTreatsRefReadonlyParametersAsArguments()
    {
        Generator.GenLuaCATS();
        XluaEditorToolsTests.Has(XluaEditorToolsTests.Definitions("XluaEditorTest.RefReadonlyTarget.lua"), "XluaEditorTest.RefReadonlyTarget.lua",
            "---@param p CS.XluaEditorTest.Pair|table\n---@return number\nfunction CS.XluaEditorTest.RefReadonlyTarget.Length(p) end\n",
            "---@param factor integer\n---@param value integer\n---@return integer\n---@return integer value\n---@return integer old\n"
            + "function CS.XluaEditorTest.RefReadonlyTarget.Scale(factor, value) end\n",
            "---@param p CS.XluaEditorTest.Pair|table\n---@return number\nfunction CS.XluaEditorTest.RefReadonlyTarget:Add(p) end\n");
        XluaEditorToolsTests.Has(XluaEditorToolsTests.Definitions("CS.lua"), "CS.lua",
            "---@alias CS.XluaEditorTest.MeasureRefReadonly fun(p: CS.XluaEditorTest.Pair, calls: integer): number, integer\n");
    }
}
