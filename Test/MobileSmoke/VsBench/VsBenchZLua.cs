// The zlua adapter of the cross-library player bench (VsBench.cs), for zlua 1.1.0 (com.code-philosophy.zlua). zlua
// has one Lua state per process, no DoString and no table API in C#: the module is served by the loader that
// LuaAppDomain.Initialize takes, and each function comes from LuaAppDomain.GetFunction<T>. Types are reached by
// assembly and full name (CSharp['Assembly-CSharp']['VsBench.TestTargets']).
using System;
using ZLua;

namespace VsBench
{
    public sealed class ZLuaAdapter : ILuaAdapter
    {
        static string text;

        public string Prelude
        {
            get
            {
                return "local AC = CSharp['Assembly-CSharp']\n"
                    + "local TT, TC, TS, TE = AC['VsBench.TestTargets'], AC['VsBench.TestClass'], AC['VsBench.TestStruct'], AC['VsBench.TestEnum']\n"
                    + "local V3 = CSharp['UnityEngine.CoreModule']['UnityEngine.Vector3']";
            }
        }

        public void Load(string module)
        {
            text = module;
            LuaAppDomain.Initialize(name => name == "vsbench" ? text : null);
        }

        public T Get<T>(string name) where T : MulticastDelegate { return LuaAppDomain.GetFunction<T>("vsbench", name); }

        public Step TableRead() { return null; }  // no table API in C#

        public void Dispose() { }  // one state per process; LuaAppDomain has no shutdown
    }
}
