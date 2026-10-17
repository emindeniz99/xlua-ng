// size_report.py copies this file to Assets/XluaSmoke.cs (the class name SmokeBuild.cs builds the scene with).
// It does just enough that managed stripping keeps xLua: create a LuaEnv, run a chunk, call a C# static
// method from Lua and a Lua function from C#. The baseline project compiles it with XLUA_SIZE_BASELINE
// and has no xLua sources.
// With XLUA_SIZE_UWR (the +uwr variants) it also creates one UnityWebRequest, as a project that
// downloads anything does, so engine code stripping keeps the UnityWebRequest modules in every case.
using UnityEngine;
using UnityEngine.Scripting;
#if !XLUA_SIZE_BASELINE
using XLua;
#endif

[Preserve]
public static class SizeProbeApi {
 [Preserve] public static int Add(int a, int b) { return a + b; }
}

public class XluaSmoke : MonoBehaviour {
 public static string Run() {
#if XLUA_SIZE_UWR
  UnityEngine.Networking.UnityWebRequest.Get(Application.streamingAssetsPath).Dispose();
#endif
#if XLUA_SIZE_BASELINE
  return "XLUA_SIZE_PROBE: baseline " + SizeProbeApi.Add(20, 1) * 2;
#else
  using (var env = new LuaEnv()) {
   env.DoString("function twice(x) return x * 2 end\nsum = CS.SizeProbeApi.Add(20, 1)");
   var twice = env.Global.Get<LuaFunction>("twice");
   var result = twice.Call(env.Global.Get<int>("sum"))[0];
   twice.Dispose();
   return "XLUA_SIZE_PROBE: " + env.DoString("return _VERSION")[0] + " " + result;
  }
#endif
 }
 void Start() { Debug.Log(Run()); }
}
