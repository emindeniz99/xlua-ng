using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CSObjectWrapEditor;
using NUnit.Framework;
using UnityEngine;
using XLua;
using XluaEditorTest;

// Runs Lua against Unity and test types, with or without generated wrappers.
// editor_tests.py runs these after XluaEditorTestSteps.GenerateCode (mode
// "generated") and again after ClearCode (mode "cleared"); a project that
// never generated code runs them in mode "reflection". The Lua results must
// be the same in every mode; only the call path differs: generated wrappers,
// or wrappers and bridges that CodeEmit builds at run time.
public sealed class XluaGeneratedCodeTests
{
    static readonly string[] WrapperNames =
    {
        "UnityEngineVector3Wrap", "UnityEngineGameObjectWrap", "UnityEngineTransformWrap",
        "XluaEditorTestTargetWrap", "XluaEditorTestPairWrap", "XluaEditorTestMoodWrap",
    };

    static readonly Type[] WrappedTypes =
    {
        typeof(Vector3), typeof(GameObject), typeof(Transform), typeof(Target), typeof(Pair), typeof(Mood),
    };

    static bool ExpectGenerated
    {
        get
        {
            string mode = XluaEditorTestSteps.ReadMode();
            CollectionAssert.Contains(new[] { XluaEditorTestSteps.Generated, XluaEditorTestSteps.Cleared, XluaEditorTestSteps.Reflection }, mode);
            return mode == XluaEditorTestSteps.Generated;
        }
    }

    // Generated code is compiled into the runtime assembly, next to XLua.
    static Type GeneratedType(string name)
    {
        return typeof(LuaEnv).Assembly.GetType("XLua.CSObjectWrap." + name, false);
    }

    static IDictionary DelayedWrappers(LuaEnv env)
    {
        object translator = typeof(LuaEnv).GetField("translator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env);
        FieldInfo field = translator.GetType().GetField("delayWrap", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "ObjectTranslator.delayWrap");
        return (IDictionary)field.GetValue(translator);
    }

    [Test]
    public void GeneratedCodeMatchesMode()
    {
        bool generated = ExpectGenerated;
        TestContext.WriteLine("XLUA_EDITOR_MODE: " + XluaEditorTestSteps.ReadMode());
        Assert.AreEqual(generated, File.Exists(Path.Combine(GeneratorConfig.common_path, "XLuaGenAutoRegister.cs")), "XLuaGenAutoRegister.cs");
        Assert.AreEqual(generated, XluaEditorTestSteps.GeneratedFiles().Length != 0, "C# files in " + GeneratorConfig.common_path);
        Assert.AreEqual(generated, GeneratedType("XLua_Gen_Initer_Register__") != null, "XLua_Gen_Initer_Register__");
        Assert.AreEqual(generated, DelegateBridge.Gen_Flag, "DelegateBridge.Gen_Flag");
        foreach (string name in WrapperNames)
        {
            Type wrap = GeneratedType(name);
            Assert.AreEqual(generated, wrap != null, name);
            if (wrap != null)
            {
                Assert.IsNotNull(wrap.GetMethod("__Register", BindingFlags.Static | BindingFlags.Public), name + ".__Register");
            }
        }

        // The generated register queues one loader per configured type; Lua
        // consumes a loader the first time it touches that type.
        using (var env = new LuaEnv())
        {
            IDictionary pending = DelayedWrappers(env);
            foreach (Type type in WrappedTypes)
            {
                Assert.AreEqual(generated, pending.Contains(type), "queued wrapper for " + type);
            }
            env.DoString("local _ = CS.UnityEngine.Vector3.zero; local _ = CS.XluaEditorTest.Target");
            Assert.IsFalse(pending.Contains(typeof(Vector3)), "Vector3 wrapper still queued after use");
            Assert.IsFalse(pending.Contains(typeof(Target)), "Target wrapper still queued after use");
            Assert.AreEqual(generated, pending.Contains(typeof(GameObject)), "GameObject wrapper loaded before use");
        }
    }

    [Test]
    public void LuaUsesVector3()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local V = CS.UnityEngine.Vector3
                local a, b = V(1, 2, 3), V(4, 5, 6)
                local sum, cross, scaled = a + b, V.Cross(a, b), a * 2
                local moved = V(1, 2, 3)
                moved.x = 10
                return sum, cross, scaled, V.Dot(a, b), V.Distance(a, b), a == V(1, 2, 3), V.up.y, moved, a.x, a.magnitude");
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);
            Assert.AreEqual(a + b, (Vector3)r[0]);
            Assert.AreEqual(Vector3.Cross(a, b), (Vector3)r[1]);
            Assert.AreEqual(a * 2, (Vector3)r[2]);
            Assert.AreEqual(Vector3.Dot(a, b), Convert.ToSingle(r[3]), 1e-5);
            Assert.AreEqual(Vector3.Distance(a, b), Convert.ToSingle(r[4]), 1e-5);
            Assert.AreEqual(true, r[5]);
            Assert.AreEqual(1.0, Convert.ToDouble(r[6]));
            Assert.AreEqual(new Vector3(10, 2, 3), (Vector3)r[7]);
            Assert.AreEqual(1.0, Convert.ToDouble(r[8]));
            Assert.AreEqual(a.magnitude, Convert.ToSingle(r[9]), 1e-5);
        }
    }

    // Unity 6.3 adds `in` overloads of Vector3.Distance and Cross next to the
    // by-value ones; older editors have only the by-value ones.
    internal static bool Vector3HasInOverloads
    {
        get
        {
            return typeof(Vector3).GetMethods().Any(m => m.Name == "Distance"
                && m.GetParameters().Any(p => p.ParameterType.IsByRef && p.IsIn));
        }
    }

    // C# `in` parameters are plain inputs: a call returns no extra value for
    // them, and a Lua callback returns only the result and ref parameters.
    [Test]
    public void LuaPassesInParametersByValue()
    {
        TestContext.WriteLine("XLUA_EDITOR_VECTOR3_IN_OVERLOADS: " + Vector3HasInOverloads);
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local T, P, V = CS.XluaEditorTest.Target, CS.XluaEditorTest.Pair, CS.UnityEngine.Vector3
                local function count(...) return select('#', ...) end
                local p, t = P(2, 0.5), T(3)
                local a, b = V(1, 2, 3), V(4, 5, 6)
                local scaled, factor = t:Scaled(p, 4)
                local measured = T.CallMeasure(function(...)
                    local q, calls = ...
                    return q.A + q.B + select('#', ...), calls + 1
                end)
                local measurer = T.CallMeasurer({ Measure = function(self, q, calls) return q.A * q.B, calls + 2 end })
                return count(T.Length(p)), T.Length(p), count(t:Scaled(p, 4)), scaled, factor,
                    count(V.Distance(a, b)), V.Distance(a, b), count(V.Cross(a, b)), measured, measurer");
            Assert.AreEqual(1, Convert.ToInt32(r[0]), "results of Target.Length(in Pair)");
            Assert.AreEqual(2.5, Convert.ToDouble(r[1]), 1e-5);
            Assert.AreEqual(2, Convert.ToInt32(r[2]), "results of Target.Scaled(in Pair, ref int)");
            Assert.AreEqual(7.5, Convert.ToDouble(r[3]), 1e-5);
            Assert.AreEqual(5, Convert.ToInt32(r[4]));
            Assert.AreEqual(1, Convert.ToInt32(r[5]), "results of Vector3.Distance");
            Assert.AreEqual(Vector3.Distance(new Vector3(1, 2, 3), new Vector3(4, 5, 6)), Convert.ToSingle(r[6]), 1e-5);
            Assert.AreEqual(1, Convert.ToInt32(r[7]), "results of Vector3.Cross");
            // The callback saw two arguments (2 + 0.5 + 2) and p stayed (2, 0.5).
            Assert.AreEqual(4.5f + ",2,2," + 0.5f, r[8], "MeasurePair(in Pair, ref int) bridge");
            Assert.AreEqual(1f + ",3,2," + 0.5f, r[9], "IPairMeasure.Measure(in Pair, ref int) bridge");
        }
    }

    [Test]
    public void LuaCreatesAndDestroysGameObjects()
    {
        const string name = "xlua-editor-test-root";
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local GameObject, Vector3 = CS.UnityEngine.GameObject, CS.UnityEngine.Vector3
                local root = GameObject('" + name + @"')
                local child = GameObject('child')
                child.transform:SetParent(root.transform, false)
                root.transform.position = Vector3(1, 2, 3)
                child.transform.localPosition = Vector3(0, 1, 0)
                local found = root.transform:Find('child')
                return root, child, root.transform.childCount, found == child.transform, child.transform.position, root.name");
            var root = (GameObject)r[0];
            var child = (GameObject)r[1];
            Assert.AreSame(root.transform, child.transform.parent);
            Assert.AreEqual(1, Convert.ToInt32(r[2]));
            Assert.AreEqual(true, r[3]);
            Assert.AreEqual(new Vector3(1, 3, 3), (Vector3)r[4]);
            Assert.AreEqual(name, r[5]);
            Assert.AreSame(root, GameObject.Find(name));

            env.Global.Set("root", root);
            env.DoString("CS.UnityEngine.Object.DestroyImmediate(root); root = nil");
            Assert.IsTrue(root == null, "root destroyed");
            Assert.IsTrue(child == null, "child destroyed with its parent");
            Assert.IsNull(GameObject.Find(name));
        }
    }

    [Test]
    public void LuaCallsOverloadsRefOutStructsAndEnums()
    {
        bool generated = ExpectGenerated;
        Target.Reset();
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local T, Mood = CS.XluaEditorTest.Target, CS.XluaEditorTest.Mood
                local t = T(5)
                local q, rem = T.Div(17, 5)
                local pair = t:MakePair(2, 0.5)
                local a, b, sum = pair.A, pair.B, pair:Sum()
                pair.A = 9
                return t:Describe(7), t:Describe('x'), t:Describe(1, 2), q, rem, t:Twice(21),
                    a, b, sum, pair.A,
                    t:Next(Mood.Busy), Mood.__CastFrom('Lost'), t:Next(Mood.Lost) == Mood.Calm, t.Value");
            Assert.AreEqual("int:7", r[0]);
            Assert.AreEqual("string:x", r[1]);
            Assert.AreEqual("sum:3", r[2]);
            Assert.AreEqual(3, Convert.ToInt32(r[3]));
            Assert.AreEqual(2, Convert.ToInt32(r[4]));
            Assert.AreEqual(42, Convert.ToInt32(r[5]));
            Assert.AreEqual(2, Convert.ToInt32(r[6]));
            Assert.AreEqual(0.5, Convert.ToDouble(r[7]));
            Assert.AreEqual(2.5, Convert.ToDouble(r[8]));
            Assert.AreEqual(9, Convert.ToInt32(r[9]));
            Assert.AreEqual(Mood.Lost, r[10]);
            Assert.AreEqual(Mood.Lost, r[11]);
            Assert.AreEqual(true, r[12]);
            Assert.AreEqual(5, Convert.ToInt32(r[13]));
            Assert.AreEqual(7, Target.Calls, "instance calls that reached Target");

            TestContext.WriteLine("XLUA_EDITOR_CALLER: " + Target.LastCaller);
            if (generated)
            {
                Assert.AreEqual("XLua.CSObjectWrap.XluaEditorTestTargetWrap", Target.LastCaller);
            }
            else if (EmitsTypeWrappers)
            {
                // CodeEmit.EmitTypeWrap names the wrapper <type>Wrap<id>.
                StringAssert.IsMatch(@"^TargetWrap\d+ \[dynamic\]$", Target.LastCaller ?? "", "emitted wrapper");
            }
            else
            {
                StringAssert.DoesNotStartWith("XLua.CSObjectWrap.", Target.LastCaller ?? "");
                StringAssert.DoesNotEndWith("[dynamic]", Target.LastCaller ?? "");
            }
        }
    }

    // Without generated code the editor builds type wrappers and [CSharpCallLua]
    // bridges with System.Reflection.Emit (CodeEmit, the ObjectTranslator.ce
    // field). The runtime assembly compiles it in for Unity's .NET Standard 2.1
    // and .NET Framework profiles; this test runs in an editor assembly, which
    // Unity always compiles against .NET Framework, so it cannot ask its own
    // #if and looks for the field instead.
    static bool CodeEmitCompiled
    {
        get { return typeof(ObjectTranslator).GetField("ce", BindingFlags.Instance | BindingFlags.NonPublic) != null; }
    }

    // ObjectTranslator also skips emitted type wrappers for these defines; the
    // csc.rsp and scripting backend defines are the same for both assemblies.
    static bool EmitsTypeWrappers
    {
        get
        {
#if GEN_CODE_MINIMIZE || ENABLE_IL2CPP || FORCE_REFLECTION
            return false;
#else
            return CodeEmitCompiled;
#endif
        }
    }

    [Test]
    public void CodeEmitIsCompiledIntoTheEditor()
    {
        TestContext.WriteLine("XLUA_EDITOR_CODE_EMIT: " + CodeEmitCompiled + ", type wrappers " + EmitsTypeWrappers);
        Assert.IsTrue(CodeEmitCompiled, "ObjectTranslator.ce: CodeEmit compiled out of the editor runtime assembly");
    }

    [Test]
    public void LuaFunctionsBecomeCSharpCallLuaDelegates()
    {
        bool generated = ExpectGenerated;
        using (var env = new LuaEnv())
        {
            env.DoString("t = CS.XluaEditorTest.Target(5); function combine(a, b) return a * 10 + #b end");

            // Lua passes a function where C# takes the delegate.
            Assert.AreEqual(34, Convert.ToInt32(env.DoString("return t:Apply(combine, 3, 'abcd')")[0]));

            // C# calls a Lua function through the delegate.
            var combine = env.Global.Get<Combine>("combine");
            Assert.AreEqual(43, combine(4, "abc"));
            TestContext.WriteLine("XLUA_EDITOR_DELEGATE: " + combine.Method.DeclaringType + "." + combine.Method.Name);
            Type bridge = combine.Method.DeclaringType;
            if (generated)
            {
                Assert.AreEqual(typeof(DelegateBridge), bridge, "generated delegate bridge");
            }
            else
            {
                // CodeEmit.EmitDelegateImpl derives XLuaGenDelegateImpl<id> from DelegateBridge.
                Assert.IsTrue(bridge.Assembly.IsDynamic, "emitted delegate bridge: " + bridge.Assembly);
                StringAssert.StartsWith("XLuaGenDelegateImpl", bridge.Name);
                Assert.AreEqual(typeof(DelegateBridge), bridge.BaseType);
            }
            StringAssert.StartsWith("__Gen_Delegate_Imp", combine.Method.Name);

            // LuaEnv.Dispose refuses to run while a delegate still holds Lua.
            combine = null;
            env.DoString("t = nil; combine = nil");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            env.Tick();
        }
    }
}
