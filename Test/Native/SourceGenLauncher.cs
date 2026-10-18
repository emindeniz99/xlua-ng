// Runs the managed suite (XLuaUnitTest.exe) compiled with the xLua source
// generator, for Test/Native/run_managed.py --source-gen
// (docs/design/source-generator.md, milestones M1 to M3).
//
// The generator wrote the registration, the name table, the class and enum
// wrappers of the [LuaCallCSharp] types (M2) and the delegate and interface
// bridges of the [CSharpCallLua] types (M3); GCOptimize structs still go
// through the general push and get, which box (M4).
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
// - the [CSharpCallLua] delegates and interfaces have generated bridges (M3):
//   their creators come from this unit, the delegates they make are closed
//   over the DelegateBridge with a method of the generated Bridges class, a
//   delegate type without configuration but with a matching signature gets
//   the same bridge method, and Gen_Flag is set, so CodeEmit is off.
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
        Check(DelegateBridge.Gen_Flag, "DelegateBridge.Gen_Flag is not set by a unit with delegate bridges");

        var delegates = (IDictionary)Field(registry, "delegates");
        foreach (var t in new[] { typeof(Coverage.IntFunc), typeof(TestDelegate), typeof(Action), typeof(Coverage.ParamsCallback) })
        {
            Check(delegates.Contains(t), "no delegate bridge for " + t);
            var creator = (Delegate)delegates[t];
            Check(creator.Method.DeclaringType.Assembly == tests && creator.Method.DeclaringType.FullName.StartsWith(bindings.Namespace + "."),
                "the delegate creator of " + t + " is " + creator.Method.DeclaringType.FullName);
        }
        var interfaces = (IDictionary)Field(registry, "interfaceBridges");
        Check(interfaces.Contains(typeof(Coverage.IIndexed)) && interfaces.Contains(typeof(Upstream.IManyArgs)), "interface bridges missing");
        int bridgeMethods;
        // Not disposed: the delegates made here may still be alive (Mono scans the stack conservatively).
        var env = new LuaEnv();
        {
            var f = (LuaFunction)env.DoString("return function(x) return x * 2 end")[0];
            var listed = f.Cast<Coverage.IntFunc>();
            var unlisted = f.Cast<Unlisted>();
            Check(listed(21) == 42 && unlisted(21) == 42, "delegates of a Lua function");
            foreach (var d in new Delegate[] { listed, unlisted })
            {
                Check(d.Target is DelegateBridge && d.Method.IsStatic && d.Method.DeclaringType.FullName == bindings.Namespace + ".Bridges",
                    d.GetType() + " uses " + d.Method.DeclaringType + "." + d.Method.Name);
            }
            Check(listed.Method == unlisted.Method, "an unconfigured delegate type with the same signature takes the configured one's bridge");
            bridgeMethods = listed.Method.DeclaringType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Count(m => m.Name.StartsWith("__Gen_Delegate_Imp"));

            var t = (LuaTable)env.DoString("return { Count = 3, Sum = function(self, ...) local s = 0 for _, v in ipairs({...}) do s = s + v end return s end }")[0];
            var indexed = t.Cast<Coverage.IIndexed>();
            Check(indexed.GetType().Assembly == tests && indexed.GetType().Namespace == bindings.Namespace, "the IIndexed bridge is " + indexed.GetType().FullName);
            Check(indexed.Count == 3 && indexed.Sum(1, 2, 3) == 6, "the IIndexed bridge calls Lua");
        }
        Console.WriteLine("XLUA_SOURCE_GEN_CHECKS_PASS: " + bindings.FullName + " registered " + names.Count + " names, "
            + loaders.Count + " wrappers, " + delegates.Count + " delegate types on " + bridgeMethods + " bridges and "
            + interfaces.Count + " interface bridges");
    }
}

// Not configured: it gets the bridge of Coverage.IntFunc, which has the same signature.
public delegate int Unlisted(int x);
