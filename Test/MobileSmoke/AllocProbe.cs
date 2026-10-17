// size_report.py copies this file to Assets/XluaSmoke.cs for the +alloc variants; SmokeBuild.CheckEditor
// runs it in the Editor. It measures the managed allocations and the time of the reflection-mode paths:
// new LuaEnv(), the first access from Lua to GameObject, Transform, Vector3 and a class with overloads,
// an indexer, extension methods and an event, the Lua functions that become delegates, the same chunks
// again, a hot loop, and all of it once more in a second LuaEnv.
//
// Bytes: GC.GetAllocatedBytesForCurrentThread where it counts; Unity's Editor Mono (Boehm) returns 0 and
// IL2CPP does not implement it. Otherwise GC.GetTotalMemory(false) before and after the step, which grows by
// what was allocated, rounded up to the GC's size classes, unless a collection ran in between. A player
// turns the GC off (GarbageCollector.GCMode) for each step; the Editor cannot, so every row counts the
// collections during the step ("gcs") and a row with one has no valid byte count.
// In the Editor the probe sets DelegateBridge.Gen_Flag, as generated code does: xLua then wraps types and
// creates delegates by reflection, as in a player, instead of emitting code with System.Reflection.Emit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
using XLua;

namespace XluaAllocProbe {
 [Preserve]
 public class Target {
  public int Field;
  public int Value { get; set; }
  public event Action<string> Changed;
  readonly Dictionary<int, int> items = new Dictionary<int, int>();
  public void Fire(string s) { if (Changed != null) Changed(s); }
  public int Add(int a, int b) { return a + b; }
  public int Add(int a, int b, int c) { return a + b + c; }
  public double Add(double a, double b) { return a + b; }
  public string Add(string a, string b) { return a + b; }
  public static int Twice(int x) { return 2 * x; }
  public static string Twice(string s) { return s + s; }
  public int this[int i] { get { int v; return items.TryGetValue(i, out v) ? v : 0; } set { items[i] = value; } }
 }

 [Preserve, ReflectionUse]
 public static class TargetExtensions {
  public static int Triple(this Target t, int x) { return 3 * x; }
  public static string Describe(this Target t) { return "target " + t.Field; }
 }
}

public class XluaSmoke : MonoBehaviour {
 static readonly string[][] Steps = {
  new[] { "GameObject: first access", "GO = CS.UnityEngine.GameObject" },
  new[] { "GameObject: constructor", "go = GO('alloc-probe')" },
  new[] { "Transform: first access (go.transform)", "tr = go.transform" },
  new[] { "Vector3: first access, constructor", "V3 = CS.UnityEngine.Vector3; v = V3(1, 2, 3)" },
  new[] { "Transform: property set and get", "tr.position = v; p = tr.position" },
  new[] { "Target: first access", "T = CS.XluaAllocProbe.Target" },
  new[] { "Target: constructor, overloads, static", "t = T(); a = t:Add(1, 2) + t:Add(1, 2, 3); s = t:Add('a', 'b'); d = t:Add(1.5, 2.25); x = T.Twice(4) .. T.Twice('y')" },
  new[] { "Target: indexer", "t[1] = 5; i = t[1]" },
  new[] { "Target: extension methods", "e = t:Triple(3); ds = t:Describe()" },
  new[] { "Target: field, property", "t.Field = 3; t.Value = t.Field" },
  new[] { "Target: event (Lua function to Action<string>)", "t:Changed('+', function(s) last = s end); t:Fire('x')" },
 };
 const string HotLoop = "for n = 1, 1000 do local q = go.transform; q.position = v; local r = q.position; " +
                        "local u = t:Add(n, 2); t[1] = n; local w = t[1]; local e = t:Triple(n) end";
 const int WarmRuns = 5;

 class Row { public string Name; public long Bytes; public double Ms; public int Gcs; }

 static bool threadCounter;

 // GetAllocatedBytesForCurrentThread returns 0 in Unity's Editor (Mono with Boehm) and is not implemented
 // in IL2CPP; use it only where it counts.
 static void ChooseCounter() {
#if !(ENABLE_IL2CPP && !UNITY_EDITOR)
  long a = GC.GetAllocatedBytesForCurrentThread();
  var junk = new byte[4096];
  GC.KeepAlive(junk);
  threadCounter = GC.GetAllocatedBytesForCurrentThread() - a >= 4096;
#endif
 }

 static long Allocated() {
  return threadCounter ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalMemory(false);
 }

 static Row Measure(string name, Action action) {
  var sw = new Stopwatch();
#if ENABLE_IL2CPP && !UNITY_EDITOR
  UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
#endif
  int gcs = GC.CollectionCount(0);
  long before = Allocated();
  sw.Start();
  action();
  sw.Stop();
  long bytes = Allocated() - before;
  gcs = GC.CollectionCount(0) - gcs;
#if ENABLE_IL2CPP && !UNITY_EDITOR
  UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
#endif
  return new Row { Name = name, Bytes = bytes, Ms = sw.Elapsed.TotalMilliseconds, Gcs = gcs };
 }

 static int disposeFailures;

 // Drop what Lua holds, let both GCs run so the delegate bridges are released, then dispose.
 static void Close(LuaEnv env) {
  env.DoString("CS.UnityEngine.Object.DestroyImmediate(go); go, tr, t, T, GO, V3, v, p, last = nil; collectgarbage(); collectgarbage()");
  for (int i = 0; i < 3; i++) {
   GC.Collect();
   GC.WaitForPendingFinalizers();
   env.Tick();
   env.FullGc();
  }
  try { env.Dispose(); } catch (InvalidOperationException) { disposeFailures++; }
 }

 static List<Row> Session(LuaEnv env, string prefix) {
  var rows = new List<Row>();
  foreach (var step in Steps) {
   var chunk = step[1];
   rows.Add(Measure(prefix + step[0], () => env.DoString(chunk)));
  }
  env.DoString("function f(s) last = s end; function g(s) return s .. s end");
  rows.Add(Measure(prefix + "delegate: Get<Action<string>>", () => env.Global.Get<Action<string>>("f")("z")));
  rows.Add(Measure(prefix + "delegate: Get<Func<string, string>>", () => env.Global.Get<Func<string, string>>("g")("z")));
  return rows;
 }

 public static string Run() {
#if UNITY_EDITOR
  DelegateBridge.Gen_Flag = true;
  string mode = "editor-mono";
#elif ENABLE_IL2CPP
  string mode = "player-il2cpp";
#else
  string mode = "player-mono";
#endif
  ChooseCounter();
  var rows = new List<Row>();
  LuaEnv env = null;
  rows.Add(Measure("new LuaEnv() (first in process)", () => env = new LuaEnv()));
  rows.AddRange(Session(env, "first env: "));
  var again = new List<Row>();
  foreach (var step in Steps) {
   var chunk = step[1];
   again.Add(Measure("first env, again: " + step[0], () => env.DoString(chunk)));
  }
  long againBytes = 0; double againMs = 0; int againGcs = 0;
  foreach (var r in again) { againBytes += r.Bytes; againMs += r.Ms; againGcs += r.Gcs; }
  rows.Add(new Row { Name = "first env, again: all steps", Bytes = againBytes, Ms = againMs, Gcs = againGcs });
  rows.Add(Measure("first env: hot loop, 1000 iterations", () => env.DoString(HotLoop)));
  Close(env);

  // Second and later LuaEnvs: process-wide caches (extension methods, Type objects) are warm.
  var warm = new List<List<Row>>();
  for (int run = 0; run < WarmRuns; run++) {
   var list = new List<Row>();
   list.Add(Measure("new LuaEnv() (warm)", () => env = new LuaEnv()));
   list.AddRange(Session(env, "warm env: "));
   Close(env);
   warm.Add(list);
  }
  for (int i = 0; i < warm[0].Count; i++) {
   var ms = new List<double>();
   foreach (var list in warm) ms.Add(list[i].Ms);
   ms.Sort();
   // Bytes from the last run without a collection during the step; time is the median of the runs.
   Row pick = warm[WarmRuns - 1][i];
   foreach (var list in warm) if (list[i].Gcs == 0) pick = list[i];
   rows.Add(new Row { Name = warm[0][i].Name, Bytes = pick.Bytes, Ms = ms[WarmRuns / 2], Gcs = pick.Gcs });
  }

  var sb = new StringBuilder();
  sb.Append("{\"mode\":\"").Append(mode).Append("\",\"disposeFailures\":\"").Append(disposeFailures).Append("\",\"counter\":\"").Append(threadCounter ? "GetAllocatedBytesForCurrentThread" : "GetTotalMemory").Append("\",\"unity\":\"").Append(Application.unityVersion).Append("\",\"rows\":[");
  for (int i = 0; i < rows.Count; i++) {
   if (i > 0) sb.Append(',');
   sb.Append("{\"name\":\"").Append(rows[i].Name.Replace("\"", "'")).Append("\",\"bytes\":").Append(rows[i].Bytes)
     .Append(",\"ms\":").Append(rows[i].Ms.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
     .Append(",\"gcs\":").Append(rows[i].Gcs).Append('}');
  }
  sb.Append("]}");
  var json = sb.ToString();
  try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "xlua-alloc-result.json"), json); } catch (Exception) { }
  return "XLUA_ALLOC_RESULT: " + json;
 }

 void Start() {
  string result;
  try { result = Run(); } catch (Exception e) { result = "XLUA_ALLOC_FAIL: " + e; }
  UnityEngine.Debug.Log(result);
  Console.WriteLine(result);
 }
}
