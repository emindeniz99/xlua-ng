// alloc_probe.py general compiles this with Assets/XLua/Src (XLUA_GENERAL) and runs it on Mono with the
// SGen GC, where GC.GetAllocatedBytesForCurrentThread counts every managed allocation of the thread
// exactly. It takes the same reflection-mode paths as AllocProbe.cs with class library types instead of
// UnityEngine ones: System.Text.StringBuilder (many overloads), System.Collections.Generic.List<int>
// (an indexer), System.Version, and the same Target class with overloads, an indexer, extension methods
// and an event. DelegateBridge.Gen_Flag is set so that xLua wraps by reflection, as in a player, instead
// of emitting code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using XLua;

namespace XluaAllocProbe {
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

 [ReflectionUse]
 public static class TargetExtensions {
  public static int Triple(this Target t, int x) { return 3 * x; }
  public static string Describe(this Target t) { return "target " + t.Field; }
 }

 static class Program {
  static readonly string[][] Steps = {
   new[] { "StringBuilder: first access", "SB = CS.System.Text.StringBuilder" },
   new[] { "StringBuilder: constructor, overloads", "sb = SB(); sb:Append('a'); sb:Append(1); sb:Append(2.5); sb:Append(true); str = sb:ToString()" },
   new[] { "List<int>: first access, indexer", "L = CS.System.Collections.Generic['List`1[System.Int32]']; l = L(); l:Add(1); li = l[0]; l[0] = 2" },
   new[] { "Version: first access, properties", "ver = CS.System.Version(1, 2, 3); vm = ver.Major + ver.Minor" },
   new[] { "Target: first access", "T = CS.XluaAllocProbe.Target" },
   new[] { "Target: constructor, overloads, static", "t = T(); a = t:Add(1, 2) + t:Add(1, 2, 3); s = t:Add('a', 'b'); d = t:Add(1.5, 2.25); x = T.Twice(4) .. T.Twice('y')" },
   new[] { "Target: indexer", "t[1] = 5; i = t[1]" },
   new[] { "Target: extension methods", "e = t:Triple(3); ds = t:Describe()" },
   new[] { "Target: field, property", "t.Field = 3; t.Value = t.Field" },
   new[] { "Target: event (Lua function to Action<string>)", "t:Changed('+', function(s) last = s end); t:Fire('x')" },
  };
  const string HotLoop = "for n = 1, 1000 do sb:Append(n); local c = l[0]; l[0] = n; " +
                         "local u = t:Add(n, 2); t[1] = n; local w = t[1]; local e = t:Triple(n) end";
  const int WarmRuns = 5;

  class Row { public string Name; public long Bytes; public double Ms; }

  static Row Measure(string name, Action action) {
   var sw = new Stopwatch();
   long before = GC.GetAllocatedBytesForCurrentThread();
   sw.Start();
   action();
   sw.Stop();
   return new Row { Name = name, Bytes = GC.GetAllocatedBytesForCurrentThread() - before, Ms = sw.Elapsed.TotalMilliseconds };
  }

  static void Close(LuaEnv env) {
   env.DoString("sb, l, t, T, SB, L, ver, last = nil; collectgarbage(); collectgarbage()");
   for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); env.Tick(); env.FullGc(); }
   try { env.Dispose(); } catch (InvalidOperationException) { }
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

  static int Main() {
   DelegateBridge.Gen_Flag = true;
   var rows = new List<Row>();
   LuaEnv env = null;
   rows.Add(Measure("new LuaEnv() (first in process)", () => env = new LuaEnv()));
   rows.AddRange(Session(env, "first env: "));
   long againBytes = 0; double againMs = 0;
   foreach (var step in Steps) {
    var chunk = step[1];
    var r = Measure("again", () => env.DoString(chunk));
    againBytes += r.Bytes; againMs += r.Ms;
   }
   rows.Add(new Row { Name = "first env, again: all steps", Bytes = againBytes, Ms = againMs });
   rows.Add(Measure("first env: hot loop, 1000 iterations", () => env.DoString(HotLoop)));
   Close(env);
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
    rows.Add(new Row { Name = warm[0][i].Name, Bytes = warm[WarmRuns - 1][i].Bytes, Ms = ms[WarmRuns / 2] });
   }
   var sb = new StringBuilder();
   sb.Append("{\"mode\":\"general-mono-sgen\",\"counter\":\"GetAllocatedBytesForCurrentThread\",\"rows\":[");
   for (int i = 0; i < rows.Count; i++) {
    if (i > 0) sb.Append(',');
    sb.Append("{\"name\":\"").Append(rows[i].Name).Append("\",\"bytes\":").Append(rows[i].Bytes).Append(",\"ms\":")
      .Append(rows[i].Ms.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(",\"gcs\":0}");
   }
   sb.Append("]}");
   Console.WriteLine("XLUA_ALLOC_RESULT: " + sb);
   return 0;
  }
 }
}
