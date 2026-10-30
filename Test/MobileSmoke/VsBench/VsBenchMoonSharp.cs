// The MoonSharp-ng adapter of the cross-library player bench (VsBench.cs), for the owner's MoonSharp fork
// (github.com/emindeniz99/moonsharp, package org.moonsharp.moonsharp from tools/upm/stage-local-package.sh; see
// docs/research/benchmark-vs-moonsharp-ng.md). MoonSharp is a Lua 5.2 interpreter written in C#: no native library.
//
// Configuration, the fastest one its docs give for IL2CPP (docs/source-generator.md in the fork): the package's
// Roslyn source generator writes userdata descriptors for the types listed with [assembly: MoonSharpUserDataTypes],
// and typed factories for the listed delegate types, so neither member calls nor Lua functions turned into
// delegates go through reflection or MakeGenericMethod. GeneratedUserData is internal to this assembly, so the
// adapter registers it, once per process, before the first Script. These are the types and delegates xlua-ng's
// adapter binds (VsBenchXLua.cs).
//
// Lua reaches the types through globals holding static userdata (UserData.CreateStatic), the way MoonSharp exposes
// a type. MoonSharp has no call metamethod on a type: a constructor is the static member __new, so the prelude keeps
// TC, TS and V3 as the constructors themselves (TC.__new), which is what TC(1, 2, 3) calls in the other libraries.
using System;
using MoonSharp.Interpreter;
using VsBench;

[assembly: MoonSharpUserDataTypes(
    typeof(TestTargets), typeof(TestClass), typeof(TestStruct), typeof(TestEnum), typeof(UnityEngine.Vector3),
    typeof(Step), typeof(Void0), typeof(Str0), typeof(VoidInt3), typeof(RetInt3), typeof(RetLong3), typeof(RetDouble3),
    typeof(RetString3), typeof(RetObject3), typeof(RetClass3), typeof(RetVector33), typeof(RetStruct3), typeof(Work),
    typeof(Num0))]

namespace VsBench
{
    public sealed class MoonSharpAdapter : ILuaAdapter
    {
        static bool registered;
        Script script;
        Table module;

        public MoonSharpAdapter()
        {
            if (!registered)
            {
                MoonSharp.Generated.GeneratedUserData.RegisterAll();
                registered = true;
            }
            script = new Script();
            script.Globals["TT"] = UserData.CreateStatic<TestTargets>();
            script.Globals["TC"] = UserData.CreateStatic<TestClass>();
            script.Globals["TS"] = UserData.CreateStatic<TestStruct>();
            script.Globals["TE"] = UserData.CreateStatic<TestEnum>();
            script.Globals["V3"] = UserData.CreateStatic<UnityEngine.Vector3>();
        }

        public string Prelude
        {
            get { return "local TT, TE = TT, TE\nlocal TC, TS, V3 = TC.__new, TS.__new, V3.__new"; }
        }

        public void Load(string text) { module = script.DoString(text, null, "vsbench").Table; }

        public T Get<T>(string name) where T : MulticastDelegate { return module.Get(name).Function.ToDelegate<T>(); }

        public Step TableRead()
        {
            var tbl = module.Get("tbl").Table;
            return n => { int s = 0; for (int i = 0; i < n; i++) s += (int)tbl.Get("x").Number; Sink = s; };
        }

        public static int Sink;

        public void Dispose()
        {
            module = null;
            script = null;  // a Script has no Dispose; the collector reclaims it
        }
    }
}
