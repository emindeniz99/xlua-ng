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
        "UnityEngineVector3Wrap", "UnityEngineGameObjectWrap", "UnityEngineTransformWrap", "UnityEngineCameraWrap",
        "XluaEditorTestTargetWrap", "XluaEditorTestPairWrap", "XluaEditorTestMoodWrap",
    };

    static readonly Type[] WrappedTypes =
    {
        typeof(Vector3), typeof(GameObject), typeof(Transform), typeof(Camera), typeof(Target), typeof(Pair), typeof(Mood),
    };

    // Camera's nested types that Unity marks [Obsolete(error: true)] (2022.3 and
    // later: RenderRequest, RenderRequestMode, RenderRequestOutputSpace).
    static Type[] ObsoleteErrorCameraTypes
    {
        get
        {
            return typeof(Camera).GetNestedTypes().Where(t => t.GetCustomAttributes(typeof(ObsoleteAttribute), false)
                .Cast<ObsoleteAttribute>().Any(a => a.IsError)).ToArray();
        }
    }

    static bool ExpectGenerated
    {
        get
        {
            string mode = XluaEditorTestSteps.ReadMode();
            CollectionAssert.Contains(new[] { XluaEditorTestSteps.Generated, XluaEditorTestSteps.Cleared, XluaEditorTestSteps.Reflection }, mode);
            return mode == XluaEditorTestSteps.Generated;
        }
    }

    // Generated code is compiled into the runtime assembly, next to XLua
    // (Assets layout); it reaches XLua only through public members and XLua.Binding.
    static Type GeneratedType(string name)
    {
        return typeof(LuaEnv).Assembly.GetType("XLua.CSObjectWrap." + name, false);
    }

    // The loader that generated code registered for a type (XLua.Binding), or null.
    static Delegate GeneratedLoader(Type type)
    {
        Type registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);
        MethodInfo tryGetLoader = registry.GetMethod("TryGetLoader", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(tryGetLoader, "BindingRegistry.TryGetLoader");
        object[] args = { type, null };
        tryGetLoader.Invoke(null, args);
        return (Delegate)args[1];
    }

    // The types whose wrappers a LuaEnv has loaded.
    static IDictionary LoadedTypes(LuaEnv env)
    {
        object translator = typeof(LuaEnv).GetField("translator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env);
        FieldInfo field = translator.GetType().GetField("loaded_types", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "ObjectTranslator.loaded_types");
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

        // The generated code registers one loader per configured type
        // (XLua.Binding); a LuaEnv runs it the first time Lua touches that type.
        foreach (Type type in WrappedTypes)
        {
            Assert.AreEqual(generated, GeneratedLoader(type) != null, "generated loader for " + type);
        }
        using (var env = new LuaEnv())
        {
            IDictionary loaded = LoadedTypes(env);
            env.DoString("local _ = CS.UnityEngine.Vector3.zero; local _ = CS.XluaEditorTest.Target");
            Assert.IsTrue(loaded.Contains(typeof(Vector3)), "Vector3 wrapper not loaded after use");
            Assert.IsTrue(loaded.Contains(typeof(Target)), "Target wrapper not loaded after use");
            if (generated)
            {
                Assert.IsFalse(loaded.Contains(typeof(GameObject)), "GameObject wrapper loaded before use");
            }
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

    // Indexers with an `in` key: a class wrapper passes the key as a plain
    // argument, and a Lua table implements the interface's indexer with
    // get_Item and set_Item, which see only the key (and the value).
    [Test]
    public void LuaUsesIndexersWithInKeys()
    {
        Assert.AreEqual(ExpectGenerated, GeneratedType("XluaEditorTestInIndexedWrap") != null, "generated InIndexed wrapper");
        Assert.AreEqual(ExpectGenerated, GeneratedType("XluaEditorTestIPairIndexedBridge") != null, "generated IPairIndexed bridge");
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local p = CS.XluaEditorTest.Pair(2, 0.5)
                local x = CS.XluaEditorTest.InIndexed()
                local before = x[p]
                x[p] = 4
                local indexed = CS.XluaEditorTest.Target.CallIndexed({
                    scale = 1,
                    get_Item = function(self, q, ...) return (q.A + q.B) * self.scale + select('#', ...) end,
                    set_Item = function(self, q, v, ...) self.scale = v - q.A + select('#', ...) end,
                })
                return before, x[p], x.Scale, p.A, indexed");
            Assert.AreEqual(2.5, Convert.ToDouble(r[0]), 1e-5, "InIndexed[in Pair] before the set");
            Assert.AreEqual(5.0, Convert.ToDouble(r[1]), 1e-5, "InIndexed[in Pair] after the set");
            Assert.AreEqual(2.0, Convert.ToDouble(r[2]), 1e-5, "InIndexed.Scale after the set");
            Assert.AreEqual(2, Convert.ToInt32(r[3]), "the Pair key is unchanged");
            Assert.AreEqual(2.5f + "," + 12.5f + ",2," + 0.5f, r[4], "IPairIndexed.this[in Pair] bridge");
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

    // Camera has obsolete members and, from Unity 2022.3, nested types marked
    // [Obsolete(error: true)] (https://github.com/Tencent/xLua/issues/1216).
    // Config lists Camera and all its nested types.
    [Test]
    public void LuaUsesCamera()
    {
        const string name = "xlua-editor-test-camera";
        bool generated = ExpectGenerated;
        Type[] obsolete = ObsoleteErrorCameraTypes;
        TestContext.WriteLine("XLUA_EDITOR_CAMERA_OBSOLETE_ERROR_TYPES: " + string.Join(", ", obsolete.Select(t => t.Name).ToArray()));
        if (generated)
        {
            // Naming one of them is error CS0619, so no generated file may.
            foreach (string file in XluaEditorTestSteps.GeneratedFiles())
            {
                string text = File.ReadAllText(file);
                foreach (Type type in obsolete)
                {
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"\bUnityEngine\.Camera\." + type.Name + @"\b"),
                        file + " names " + type);
                }
            }
        }
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local GameObject, Camera, Vector3 = CS.UnityEngine.GameObject, CS.UnityEngine.Camera, CS.UnityEngine.Vector3
                local go = GameObject('" + name + @"')
                local cam = go:AddComponent(typeof(Camera))
                cam.fieldOfView = 42
                cam.nearClipPlane = 0.25
                cam.farClipPlane = 500
                cam.depth = 7
                cam.clearFlags = CS.UnityEngine.CameraClearFlags.SolidColor
                cam.orthographic = true
                cam.orthographicSize = 3.5
                cam.gateFit = Camera.GateFitMode.Fill
                go.transform.position = Vector3(1, 2, 3)
                return go, cam, go:GetComponent(typeof(Camera)) == cam, cam.fieldOfView, cam.nearClipPlane,
                    cam.farClipPlane, cam.depth, cam.clearFlags, cam.orthographic, cam.orthographicSize,
                    cam.gateFit, cam.transform.position, Camera.MonoOrStereoscopicEye.Mono");
            var go = (GameObject)r[0];
            try
            {
                var cam = (Camera)r[1];
                Assert.AreSame(go.GetComponent<Camera>(), cam);
                Assert.AreEqual(true, r[2]);
                Assert.AreEqual(42.0, Convert.ToDouble(r[3]), 1e-4);
                Assert.AreEqual(0.25, Convert.ToDouble(r[4]), 1e-6);
                Assert.AreEqual(500.0, Convert.ToDouble(r[5]), 1e-4);
                Assert.AreEqual(7.0, Convert.ToDouble(r[6]));
                Assert.AreEqual(CameraClearFlags.SolidColor, r[7]);
                Assert.AreEqual(true, r[8]);
                Assert.AreEqual(3.5, Convert.ToDouble(r[9]), 1e-6);
                Assert.AreEqual(Camera.GateFitMode.Fill, r[10]);
                Assert.AreEqual(new Vector3(1, 2, 3), (Vector3)r[11]);
                Assert.AreEqual(Camera.MonoOrStereoscopicEye.Mono, r[12]);
                Assert.AreEqual(42f, cam.fieldOfView, 1e-4f);
                Assert.IsTrue(cam.orthographic);

                // Lua still reaches the obsolete nested enum, through reflection.
                Type mode = typeof(Camera).GetNestedType("RenderRequestMode");
                if (mode != null)
                {
                    string first = Enum.GetNames(mode)[0];
                    object v = env.DoString("return CS.UnityEngine.Camera.RenderRequestMode." + first)[0];
                    Assert.AreEqual(Enum.Parse(mode, first), v);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
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

    // Lua's integer rule for C# integer parameters and fields (docs/MIGRATING.md):
    // an integer, a float with an integer value or a numeric string is accepted,
    // a fraction raises and leaves a field unchanged. The managed suite checks
    // every integer type; this checks the same rule through the wrappers that
    // Unity's compiler builds from the templates and those CodeEmit builds in
    // Unity's Mono, including a field of a [GCOptimize] struct. Overloaded
    // members (the constructors, Describe) reject a fraction in their overload
    // check, which raises "invalid arguments to ..." instead.
    [Test]
    public void LuaIntegerArgumentsFollowLuasRule()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local T, P = CS.XluaEditorTest.Target, CS.XluaEditorTest.Pair
                local rule = 'number has no integer representation'
                local function raises(text, f)
                    local ok, err = pcall(f)
                    if ok then return 'returned' end
                    return string.find(tostring(err), text, 1, true) ~= nil or tostring(err)
                end
                local function rejects(f)
                    local r = raises(rule, f)
                    if r == true then return r end
                    return raises('invalid arguments to', f)
                end
                local t = T(3.0)
                local q, rem = T.Div(7.0, '2')
                t.Value = 6.0
                local p = P(1, 0.5)
                p.A = 4.0
                return t.Value, q, rem,
                    rejects(function() local x = T(2.5); return x end),
                    raises(""bad argument #2 to 'Div' ("" .. rule .. "")"", function() local x = T.Div(7, 2.5); return x end),
                    raises(rule, function() t.Value = 1.5 end), t.Value,
                    rejects(function() local x = P(1.5, 0.5); return x end),
                    raises(rule, function() p.A = 2.5 end), p.A,
                    raises('invalid arguments to', function() local x = t:Describe(0/0, 1); return x end)");
            Assert.AreEqual(6, Convert.ToInt32(r[0]), "Target.Value after t.Value = 6.0");
            Assert.AreEqual(3, Convert.ToInt32(r[1]), "Target.Div(7.0, '2')");
            Assert.AreEqual(1, Convert.ToInt32(r[2]), "remainder of Target.Div(7.0, '2')");
            Assert.AreEqual(true, r[3], "Target(2.5)");
            Assert.AreEqual(true, r[4], "Target.Div(7, 2.5)");
            Assert.AreEqual(true, r[5], "t.Value = 1.5");
            Assert.AreEqual(6, Convert.ToInt32(r[6]), "Target.Value after the rejected 1.5");
            Assert.AreEqual(true, r[7], "Pair(1.5, 0.5)");
            Assert.AreEqual(true, r[8], "pair.A = 2.5");
            Assert.AreEqual(4, Convert.ToInt32(r[9]), "Pair.A after the rejected 2.5");
            Assert.AreEqual(true, r[10], "t:Describe(NaN, 1)");
        }
    }

    // System.IO.Path, Int32, Convert, BitConverter and String have overloads
    // that take or return Span<T> or ReadOnlySpan<T>, which cannot be boxed.
    // They are not in LuaCallCSharp: wrappers are emitted without generated
    // code and built by reflection with it. Emitted wrappers threw "Cannot box
    // IsByRefLike type" (https://github.com/Tencent/xLua/issues/1114).
    [Test]
    public void LuaCallsBclMethodsWithSpanOverloads()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"
                local Path = CS.System.IO.Path
                return Path.GetExtension('a.txt'), Path.GetFileName('dir/b.lua'), Path.HasExtension('c.txt'),
                    Path.GetFileNameWithoutExtension('d.json'), CS.System.Int32.Parse('42'),
                    CS.System.Convert.ToBase64String('xyz'), CS.System.Convert.FromBase64String('eHl6'),
                    CS.System.BitConverter.ToInt32('\1\0\0\0', 0), CS.System.String.Concat('a', 'b'),
                    Path.GetExtension == nil, Path.Join == nil or Path.Join('e', 'f')");
            Assert.AreEqual(".txt", r[0]);
            Assert.AreEqual("b.lua", r[1]);
            Assert.AreEqual(true, r[2]);
            Assert.AreEqual("d", r[3]);
            Assert.AreEqual(42, Convert.ToInt32(r[4]));
            Assert.AreEqual("eHl6", r[5]);
            Assert.AreEqual("xyz", r[6]); // byte[] results are Lua strings
            Assert.AreEqual(1, Convert.ToInt32(r[7]));
            Assert.AreEqual("ab", r[8]);
            Assert.AreEqual(false, r[9]);
            TestContext.WriteLine("XLUA_EDITOR_PATH_JOIN: " + r[10]);
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
