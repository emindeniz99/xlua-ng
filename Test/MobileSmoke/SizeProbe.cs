// size_report.py copies this file to Assets/XluaSmoke.cs (the class name SmokeBuild.cs builds the scene with).
// It does just enough that managed stripping keeps xLua: create a LuaEnv, run a chunk, call a C# static
// method from Lua and a Lua function from C#. The baseline project compiles it with XLUA_SIZE_BASELINE
// and has no xLua sources.
// With XLUA_SIZE_UWR (the +uwr variants) it also creates one UnityWebRequest, as a project that
// downloads anything does, so engine code stripping keeps the UnityWebRequest modules in every case.
// With XLUA_SIZE_MIXED (mixed-5.5) it also runs SizeProbeCore.Run(), LuaScript in the same player.
// With XLUA_SIZE_GAME (the +game variants) it also times Lua calls into wrapped UnityEngine types
// (XLUA_SIZE_TIMING lines: median ns per call of five runs, the Lua loop included), so players built
// with the source generator, with and without GEN_CODE_MINIMIZE, show the cost of each call path.
using UnityEngine;
using UnityEngine.Scripting;
#if !XLUA_SIZE_BASELINE
using XLua;
#endif
#if XLUA_SIZE_GAME && !XLUA_SIZE_BASELINE
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
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
   return "XLUA_SIZE_PROBE: " + env.DoString("return _VERSION")[0] + " " + result
#if XLUA_SIZE_MIXED
    + "; " + SizeProbeCore.Run()  // mixed-5.5: LuaScript next to LuaEnv (SizeProbeCore.cs)
#endif
#if XLUA_SIZE_GAME
    + "; " + GameCalls(env)
#endif
    ;
  }
#endif
 }
#if XLUA_SIZE_GAME && !XLUA_SIZE_BASELINE
 const string GameCases = @"
local V = CS.UnityEngine.Vector3
local Max = CS.UnityEngine.Mathf.Max
local go = CS.UnityEngine.GameObject('xlua_size_probe')
local t = go.transform
local v = V(1, 2, 3)
return {
 {'static_method', function(n) for i = 1, n do Max(i, 2) end end},
 {'struct_ctor', function(n) for i = 1, n do V(i, 2, 3) end end},
 {'struct_field', function(n) local s = 0 for i = 1, n do s = s + v.x end end},
 {'instance_get', function(n) for i = 1, n do local p = t.position end end},
 {'instance_set', function(n) for i = 1, n do t.position = v end end},
 {'instance_method', function(n) for i = 1, n do go:GetInstanceID() end end},
}";

 static string GameCalls(LuaEnv env) {
  const int n = 100000;
  var cases = (LuaTable)env.DoString(GameCases)[0];
  var summary = new List<string>();
  for (int c = 1; c <= cases.Length; c++) {
   var entry = cases.Get<int, LuaTable>(c);
   string name = entry.Get<int, string>(1);
   var run = entry.Get<int, LuaFunction>(2);
   run.Call(n / 10);
   var samples = new List<double>();
   for (int r = 0; r < 5; r++) {
    var watch = Stopwatch.StartNew();
    run.Call(n);
    samples.Add(watch.Elapsed.TotalMilliseconds * 1e6 / n);
   }
   samples.Sort();
   Debug.Log("XLUA_SIZE_TIMING " + name + " " + samples[2].ToString("F1") + " ns");
   summary.Add(name + "=" + samples[2].ToString("F1"));
   run.Dispose();
   entry.Dispose();
  }
  cases.Dispose();
  return string.Join(" ", summary.ToArray());
 }
#endif
 void Start() { Debug.Log(Run()); }
}
