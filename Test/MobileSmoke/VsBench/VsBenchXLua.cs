// The xlua-ng adapter of the cross-library player bench (VsBench.cs). size_report.py +vsbench copies it into
// Assets with VsBench.cs; SizeProbe.cs starts the bench (XLUA_SIZE_VS_BENCH). The configuration matches what
// zlua-benchmark's xLua project gives xLua (xlua-benchmark/Assets/Benchmark/Editor/BenchmarkGenConfig*.cs and
// Shared/TestStruct.XLua.cs at ef18f1e9): the test types and UnityEngine.Vector3 bound, TestStruct [GCOptimize],
// and every C# -> Lua delegate configured, here as source generator assembly attributes.
using System;
using XLua;
using VsBench;

[assembly: LuaCallCSharp(typeof(TestTargets))]
[assembly: LuaCallCSharp(typeof(TestClass))]
[assembly: LuaCallCSharp(typeof(TestStruct))]
[assembly: LuaCallCSharp(typeof(TestEnum))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Vector3))]
[assembly: GCOptimize(typeof(TestStruct))]
[assembly: CSharpCallLua(typeof(Step))]
[assembly: CSharpCallLua(typeof(Void0))]
[assembly: CSharpCallLua(typeof(Str0))]
[assembly: CSharpCallLua(typeof(VoidInt3))]
[assembly: CSharpCallLua(typeof(RetInt3))]
[assembly: CSharpCallLua(typeof(RetLong3))]
[assembly: CSharpCallLua(typeof(RetDouble3))]
[assembly: CSharpCallLua(typeof(RetString3))]
[assembly: CSharpCallLua(typeof(RetObject3))]
[assembly: CSharpCallLua(typeof(RetClass3))]
[assembly: CSharpCallLua(typeof(RetVector33))]
[assembly: CSharpCallLua(typeof(RetStruct3))]

namespace VsBench
{
    public sealed class XLuaAdapter : ILuaAdapter
    {
        LuaEnv env = new LuaEnv();
        LuaTable module;

        public string Prelude
        {
            get
            {
                return "local TT, TC, TS, TE = CS.VsBench.TestTargets, CS.VsBench.TestClass, CS.VsBench.TestStruct, CS.VsBench.TestEnum\n"
                    + "local V3 = CS.UnityEngine.Vector3";
            }
        }

        public void Load(string text) { module = (LuaTable)env.DoString(text, "vsbench")[0]; }

        public T Get<T>(string name) where T : MulticastDelegate { return module.Get<string, T>(name); }

        public Step TableRead()
        {
            var tbl = module.Get<string, LuaTable>("tbl");
            return n => { int s = 0; for (int i = 0; i < n; i++) s += tbl.Get<string, int>("x"); Sink = s; };
        }

        public static int Sink;

        public void Dispose()
        {
            module.Dispose();
            env.Dispose();
        }

        public static string Mode()
        {
#if XLUA_PRESET_MINIMAL
            return "strict-minimal";
#elif GEN_CODE_MINIMIZE
            return "gcm";
#else
            return "plain";
#endif
        }
    }
}
