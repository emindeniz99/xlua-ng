// Runs the managed suite (XLuaUnitTest.exe) compiled with the xLua source
// generator, for Test/Native/run_managed.py --source-gen
// (docs/design/source-generator.md, milestones M1 and M2).
//
// The generator wrote the registration, the name table and the class and
// enum wrappers of the [LuaCallCSharp] types (M2); delegates, interfaces and
// GCOptimize structs still go through CodeEmit and reflection (M3, M4).
// Around the suite, this checks that:
// - XLuaUnitTest.exe carries [assembly: GeneratedBindings] for an internal
//   static Bindings class in XLua.Generated.XLuaUnitTest_<hash>;
// - nothing was registered before the first LuaEnv, which then registered the
//   unit through the attribute (BindingRegistry.EnsureRegistered, the path of
//   hosts without Unity);
// - every name table entry is Type.ToString() of its type, and the types
//   configured on types and at assembly level are in it;
// - every [LuaCallCSharp] type the legacy generator wraps has a loader from
//   this unit, which Lua used: the type's wrapper class is in
//   XLua.Generated.XLuaUnitTest_<hash>, and types without configuration have
//   none;
// - DelegateBridge.Gen_Flag stays false: without generated delegate bridges
//   CodeEmit keeps making them.
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
        var loaders = (IDictionary)Field(registry, "loaders");
        foreach (var t in new[] { typeof(LuaTestObj), typeof(TestStruct), typeof(Coverage.Lazy), typeof(LuaTestType) })
        {
            Check(loaders.Contains(t), "no loader for " + t);
            var loader = (Delegate)loaders[t];
            Check(loader.Method.DeclaringType.Assembly == tests && loader.Method.DeclaringType.Namespace == bindings.Namespace
                && loader.Method.Name == "__Register", "the loader of " + t + " is " + loader.Method.DeclaringType.FullName + "." + loader.Method.Name);
        }
        Check(!loaders.Contains(typeof(Pedding)) && !loaders.Contains(typeof(Coverage.FilteredReflected)), "a loader for a type without configuration");
        Check(!DelegateBridge.Gen_Flag, "DelegateBridge.Gen_Flag is set by a unit without delegate bridges");
        Console.WriteLine("XLUA_SOURCE_GEN_CHECKS_PASS: " + bindings.FullName + " registered " + names.Count + " names and "
            + loaders.Count + " wrappers"); 
    }
}
