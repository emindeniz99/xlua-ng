// Runs the managed suite (XLuaUnitTest.exe) with the generated bindings in an
// assembly of their own, XLuaGen.dll, for Test/Native/run_managed.py
// --gen-assembly. That is the layout of XLua in its own asmdef: generated code
// reaches xLua only through public members and XLua.Binding, never as partial
// classes of xLua's types (docs/design/source-generator.md, milestone M0).
//
//     mcs -r:XLua.Mini.dll -r:XLuaUnitTest.exe -target:library -out:XLuaGen.dll Gen/*.cs
//     mcs -r:XLua.Mini.dll -r:XLuaUnitTest.exe -r:XLuaGen.dll GenAssemblyLauncher.cs
//
// After the suite it checks that the bindings came from XLuaGen.dll: the
// first LuaEnv registered them through [assembly: GeneratedBindings], and a
// CSharpCallLua delegate and a class wrapper are the generated ones.
using System;
using System.Reflection;
using XLua;

delegate void Unlisted();

static class GenAssemblyLauncher
{
    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("GenAssemblyLauncher: " + what);
    }

    static void Main()
    {
        Assembly gen = typeof(XLua.CSObjectWrap.XLua_Gen_Initer_Register__).Assembly;
        Check(gen != typeof(LuaEnv).Assembly && gen != typeof(XLuaUnitTest).Assembly,
            "the generated code must be in its own assembly");

        XLuaUnitTest.Main();

        Check(DelegateBridge.Gen_Flag, "DelegateBridge.Gen_Flag is not set: no generated bindings registered");
        // Not disposed: the delegate bridge made below may still be alive.
        var env = new LuaEnv();
        var f = (LuaFunction)env.DoString("return function() end")[0];
        Action action = f.Cast<Action>();
        Check(action.Method.DeclaringType.Assembly == gen && action.Method.Name.StartsWith("__Gen_Delegate_Imp"),
            "System.Action did not come from the generated bridges: " + action.Method.DeclaringType + "." + action.Method.Name);
        Check(action.Target is DelegateBridge, "the generated delegate is not closed over its DelegateBridge");

        // A delegate type that no configuration lists: the runtime finds a
        // generated bridge with its signature (BindingRegistry.BridgeMethods).
        Unlisted unlisted = f.Cast<Unlisted>();
        Check(unlisted.Method.DeclaringType.Assembly == gen && unlisted.Target is DelegateBridge,
            "Unlisted did not get a generated bridge: " + unlisted.Method.DeclaringType + "." + unlisted.Method.Name);
        unlisted();

        // The loader that the runtime uses for a configured type.
        var registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);
        var tryGetLoader = registry.GetMethod("TryGetLoader", BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { typeof(LuaTestObj), null };
        Check((bool)tryGetLoader.Invoke(null, args), "no generated loader for LuaTestObj");
        var loader = (Delegate)args[1];
        Check(loader.Method.DeclaringType.Assembly == gen, "the LuaTestObj loader is not in the generated assembly");
        Console.WriteLine("XLUA_GEN_ASSEMBLY_CHECKS_PASS: bindings registered from " + gen.GetName().Name);
    }
}
