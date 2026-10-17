// Runs the managed suite (XLuaUnitTest.exe) compiled with the xLua source
// generator, for Test/Native/run_managed.py --source-gen
// (docs/design/source-generator.md, milestone M1).
//
// The generator wrote only the registration and the name table, no wrappers,
// so the suite runs on reflection with a registration in place. Around it,
// this checks that:
// - XLuaUnitTest.exe carries [assembly: GeneratedBindings] for an internal
//   static Bindings class in XLua.Generated.XLuaUnitTest_<hash>;
// - nothing was registered before the first LuaEnv, which then registered the
//   unit through the attribute (BindingRegistry.EnsureRegistered, the path of
//   hosts without Unity);
// - every name table entry is Type.ToString() of its type, and the types
//   configured on types and at assembly level are in it;
// - no loader is registered and DelegateBridge.Gen_Flag stays false: a unit
//   with names only leaves the runtime as it was.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XLua;

static class SourceGenLauncher
{
    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("SourceGenLauncher: " + what);
    }

    static object Field(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    }

    static void Main()
    {
        Assembly tests = typeof(XLuaUnitTest).Assembly;
        var marker = tests.GetCustomAttributes(typeof(XLua.Binding.GeneratedBindingsAttribute), false)
            .Cast<XLua.Binding.GeneratedBindingsAttribute>().ToArray();
        Check(marker.Length == 1, "expected one [assembly: GeneratedBindings] in XLuaUnitTest, found " + marker.Length);
        Type bindings = marker[0].Bindings;
        Check(bindings.Assembly == tests && bindings.Name == "Bindings" && bindings.Namespace.StartsWith("XLua.Generated.XLuaUnitTest_")
            && !bindings.IsPublic && bindings.IsAbstract && bindings.IsSealed, "unexpected Bindings class " + bindings.FullName);
        Check(marker[0].ApiVersion == XLua.Binding.BindingApi.Version, "binding API version " + marker[0].ApiVersion);

        Type registry = typeof(LuaEnv).Assembly.GetType("XLua.Binding.BindingRegistry", true);
        var registered = (ICollection<string>)Field(registry, "registered");
        Check(registered.Count == 0, "something registered before the first LuaEnv");

        XLuaUnitTest.Main();

        Check(registered.Contains("XLuaUnitTest"), "the first LuaEnv did not register XLuaUnitTest through GeneratedBindings");
        var names = (IDictionary)Field(registry, "names");
        foreach (DictionaryEntry e in names)
        {
            Check(((Type)e.Key).ToString() == (string)e.Value, "name table entry " + e.Value + " for " + e.Key);
        }
        foreach (var t in new[] { typeof(LuaTestObj), typeof(TestStruct), typeof(Action), typeof(Pedding) })
        {
            bool expected = t != typeof(Pedding); // Pedding has no configuration
            Check(names.Contains(t) == expected, t + (expected ? " is missing from" : " should not be in") + " the name table");
        }
        var tryGetLoader = registry.GetMethod("TryGetLoader", BindingFlags.Static | BindingFlags.NonPublic);
        Check(!(bool)tryGetLoader.Invoke(null, new object[] { typeof(LuaTestObj), null }), "a loader is registered for LuaTestObj");
        Check(!DelegateBridge.Gen_Flag, "DelegateBridge.Gen_Flag is set by a unit without code");
        Console.WriteLine("XLUA_SOURCE_GEN_CHECKS_PASS: " + bindings.FullName + " registered " + names.Count + " names, no wrappers");
    }
}
