// The PuerTS Lua backend adapter of the cross-library player bench (VsBench.cs), for PuerTS 3.0.3's Unity release
// (packages com.tencent.puerts.core and com.tencent.puerts.lua). On IL2CPP PuerTS uses its default xIl2cpp mode:
// VsBenchPuertsEditor.Generate writes the C++ glue with wrappers for the [Binding] types into Assets/Gen before the
// build. Lua reaches C# through require('csharp'); a Lua table cannot be returned to C# as a table, so the module
// is kept in a global and each function is fetched with Eval<T>.
using System;
using Puerts;

namespace VsBench
{
    public sealed class PuertsAdapter : ILuaAdapter
    {
        ScriptEnv env = new ScriptEnv(new BackendLua());

        public string Prelude
        {
            get
            {
                return "local CS = require('csharp')\n"
                    + "local TT, TC, TS, TE = CS.VsBench.TestTargets, CS.VsBench.TestClass, CS.VsBench.TestStruct, CS.VsBench.TestEnum\n"
                    + "local V3 = CS.UnityEngine.Vector3";
            }
        }

        public void Load(string text) { env.Eval("__vsbench = (function()\n" + text + "\nend)()", "vsbench"); }

        public T Get<T>(string name) where T : MulticastDelegate { return env.Eval<T>("return __vsbench['" + name + "']", "get"); }

        public Step TableRead() { return null; }  // no Lua table API in C# for the Lua backend

        public void Dispose() { env.Dispose(); }
    }
}
