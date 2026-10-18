// The core-only player smoke (docs/design/core-luascript.md, milestone 11). prepare.py --core copies
// it to Assets/XluaSmoke.cs (the class SmokeBuild.cs builds the scene with) in a project that has xLua
// as the UPM package with the core library and XLUA_CORE_ONLY in the Player Settings, so the player
// has XLua.Core and no XLua assembly. It checks LuaScript's Load and Call, Register (a C# function
// called from Lua, and a C# exception becoming a Lua error), the memory, instruction and time limits,
// Interrupt, the buffer library and LuaBufferRef, and the sandbox (no io, os, debug or package, no
// bytecode), then measures how late TimeLimit and Interrupt stop a call (Latency below; one
// XLUA_NG_CORE_LATENCY line per case), then logs XLUA_NG_CORE_SMOKE_PASS or XLUA_NG_CORE_SMOKE_FAIL
// and writes xlua-smoke-result.txt under Application.persistentDataPath: the pass or fail line
// first, the latency lines after it. A player runs the checks on a worker thread, so a stop that
// comes seconds late does not block Unity's main thread.
//
// LuaScript compiles only with XLUA_CORE_ONLY or XLUA_LUASCRIPT. SmokeBuild.cs sets the define for the
// player build; until then the Editor compiles the fallback below.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
#if XLUA_CORE_ONLY || XLUA_LUASCRIPT
using XLua;
#endif

public class XluaSmoke : MonoBehaviour {
 public static string Run() {
#if XLUA_CORE_ONLY || XLUA_LUASCRIPT
  string checks = Checks();
#if XLUA_CORE_ONLY
  // The XLua assembly must not be in the player at all.
  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
   if (assembly.GetName().Name == "XLua") throw new Exception("the XLua assembly is loaded in a core-only player");
  }
  checks += ", no XLua assembly";
#endif
  var lines = new List<string>();
  string late = Latency(lines);
  string head = late == null ? "XLUA_NG_CORE_SMOKE_PASS: " + checks + ", stop latency p90 within " + StopBoundMs +
                               " ms, max within " + StopMaxMs + " ms"
                             : "XLUA_NG_CORE_SMOKE_FAIL: " + checks + "; stop latency: " + late;
  return head + "\n" + string.Join("\n", lines.ToArray());
#else
  throw new Exception("LuaScript is not compiled: define XLUA_CORE_ONLY or XLUA_LUASCRIPT");
#endif
 }

#if XLUA_CORE_ONLY || XLUA_LUASCRIPT
 const long Backstop = 3000000000;

 static void Check(bool ok, string what) { if (!ok) throw new Exception("check failed: " + what); }

 static T Throws<T>(Action action, string what) where T : Exception {
  try { action(); }
  catch (T e) { return e; }
  catch (Exception e) { throw new Exception(what + ": expected " + typeof(T).Name + ", got " + e.GetType().Name + ": " + e.Message); }
  throw new Exception(what + ": expected " + typeof(T).Name + ", nothing was thrown");
 }

 static string Checks() {
  string version;
  var options = new LuaOptions { MemoryLimit = 4 << 20, InstructionLimit = 10000000, TimeLimit = TimeSpan.FromSeconds(2),
                                 Libraries = LuaLibs.Safe | LuaLibs.Buffer };
  using (var lua = new LuaScript(options)) {
   // Load and Call.
   version = lua.Load<string>("return _VERSION", "version");
   Check(version == "Lua 5.5" || version == "Lua 5.4", "version " + version);
   lua.Load("function add3(a, b, c) return a + b + c end\n" +
            "function greet(s) return 'hello ' .. s end\n" +
            "function divmod(a, b) return a // b, a % b end\n" +
            "function big() return math.maxinteger end", "smoke");
   Check(lua.Call<long>("add3", 1, 2, 39) == 42, "Call<long>");
   Check(lua.Call<double>("add3", 0.5, 0.25, 1) == 1.75, "Call<double>");
   Check(lua.Call<string>("greet", "core") == "hello core", "Call<string>");
   Check(lua.Call<long>("big") == long.MaxValue, "64-bit integers");
   var results = new LuaValue[2];
   Check(lua.Call("divmod", new LuaValue[] { 17, 5 }, results) == 2 && results[0].AsLong() == 3 && results[1].AsLong() == 2,
         "multiple results");
   var runtime = Throws<LuaRuntimeException>(() => lua.Call("missing"), "missing function");
   Check(runtime.Message.Contains("missing"), "missing function message: " + runtime.Message);
   Throws<LuaSyntaxException>(() => lua.Load("return +", "bad"), "syntax error");

   // Register: a typed shorthand, a LuaCallback and a C# exception as a Lua error.
   lua.Register<long, long, long>("mul", (a, b) => a * b);
   lua.Register("shout", (ref LuaCall call) => call.Return(call.GetString(0).ToUpperInvariant() + "!"));
   lua.Register("fail", () => { throw new InvalidOperationException("from C#"); });
   lua.Load("function viaHost(x) return mul(x, 6) .. shout('ok') end\n" +
            "function caught() local ok, e = pcall(fail) return ok, tostring(e) end", "register");
   Check(lua.Call<string>("viaHost", 7) == "42OK!", "Register");
   var caught = new LuaValue[2];
   lua.Call("caught", ReadOnlySpan<LuaValue>.Empty, caught);
   Check(!caught[0].AsBool() && caught[1].AsString().Contains("from C#"), "C# exception as a Lua error: " + caught[1]);

   // The sandbox.
   Check(lua.Load<string>("return type(io) .. type(os) .. type(debug) .. type(package) .. type(dofile) .. type(print)", "libs")
         == "nilnilnilnilnilnil", "sandbox libraries");
   Check(!lua.Load<bool>("return getmetatable('') ~= false", "strmeta"), "string metatable locked");
   Throws<LuaSyntaxException>(() => lua.Load("\x1bLua", "bytecode"), "bytecode refused");
   Check(lua.Load<bool>("return load('\\27Lua', 'b', 'b') == nil", "loadb"), "load of bytecode refused");

   // The buffer library and LuaBufferRef.
   lua.Load("function fill(b) for i = 0, buffer.len(b) - 1 do buffer.writeu8(b, i, i * 3) end end\n" +
            "function make() local b = buffer.create(8) buffer.writei32(b, 0, -2) buffer.writef32(b, 4, 1.5) return b end",
            "buffer");
   using (var b = lua.NewBuffer(16)) {
    lua.Call("fill", b);
    Check(b.ReadByte(5) == 15 && b.AsSpan()[15] == 45, "Lua writes seen through LuaBufferRef");
   }
   using (var made = lua.Call<LuaBufferRef>("make")) {
    Check(made.Length == 8 && made.ReadInt32(0) == -2 && made.ReadSingle(4) == 1.5f, "buffer result");
   }
   Check(lua.LiveHandles == 0, "buffer handles released");
  }

  // The limits, each in a fresh script.
  using (var lua = new LuaScript(new LuaOptions { MemoryLimit = 1 << 20 })) {
   var e = Throws<LuaMemoryLimitException>(() => lua.Load("local t = {} for i = 1, 1e7 do t[i] = ('x'):rep(64) .. i end", "mem"),
                                           "memory limit");
   Check(e.MemoryUsed <= (1 << 20) + 16384, "memory used " + e.MemoryUsed);
   Check(lua.Load<bool>("local ok = pcall(function() return ('x'):rep(1 << 24) end) return not ok", "pcall-mem"),
         "pcall catches the memory error");
  }
  using (var lua = new LuaScript(new LuaOptions { InstructionLimit = 100000 })) {
   var e = Throws<LuaInstructionLimitException>(() => lua.Load("while true do end", "spin"), "instruction limit");
   Check(e.Limit == LuaLimit.Instructions, "instruction limit kind");
   Throws<LuaInstructionLimitException>(() => lua.Load("while true do pcall(function() while true do end end) end", "swallow"),
                                        "instruction limit through pcall");
  }
  var watch = System.Diagnostics.Stopwatch.StartNew();
  using (var lua = new LuaScript(new LuaOptions { TimeLimit = TimeSpan.FromMilliseconds(100), InstructionLimit = Backstop })) {
   Throws<LuaTimeLimitException>(() => lua.Load("while true do end", "time"), "time limit");
  }
  long timeMs = watch.ElapsedMilliseconds;
  Check(timeMs < 5000, "time limit took " + timeMs + " ms");
  // An instruction limit far beyond the time limit and the interrupt keeps a broken check from hanging.
  using (var lua = new LuaScript(new LuaOptions { InstructionLimit = Backstop })) {
   var timer = new Timer(_ => lua.Interrupt(), null, 100, Timeout.Infinite);
   Throws<LuaInterruptedException>(() => lua.Load("while true do end", "interrupt"), "Interrupt from another thread");
   timer.Dispose();
  }
  return version + ", Load/Call, Register, sandbox, buffer, memory/instruction/time limits (time " + timeMs + " ms), Interrupt";
 }

 // Stop latency (docs/research/core-stop-latency.md, "On target"): how long after its deadline a
 // 100 ms TimeLimit stops `while true do end`, and how long after Interrupt() from another thread
 // (50 ms into the call) the call returns, with and without the other limits. Up to Runs runs per
 // case, and no new run after CaseBudgetMs, so that a stop seconds late (as before the fix of
 // 2026-10-18) does not make the smoke take minutes. A case fails when its 90th percentile is later
 // than StopBoundMs or one stop is later than StopMaxMs, or when a call ends without the expected
 // exception or with another one. Single stops on the emulator of a loaded host came up to 316 ms
 // late after the fix, also in cases the fix does not touch (a time limit alone: 130 ms); before
 // it, a lost stop waited for the end of a count slice, 2 to 12 s on the emulator and the Simulator.
 const int Runs = 20;
 const long CaseBudgetMs = 20000;
 const double StopBoundMs = 100;
 const double StopMaxMs = 1000;
 const long Huge = 100000000000;

 sealed class LatencyCase {
  public string Name; public bool Interrupt; public long Instructions; public double TimeLimitMs; public string Code;
 }

 static readonly LatencyCase[] LatencyCases = {
  new LatencyCase { Name = "time-100ms", TimeLimitMs = 100, Code = "while true do end" },
  new LatencyCase { Name = "time-100ms+instr-3e9", TimeLimitMs = 100, Instructions = Backstop, Code = "while true do end" },
  new LatencyCase { Name = "time-100ms+instr-1e11", TimeLimitMs = 100, Instructions = Huge, Code = "while true do end" },
  // Without a limit nothing else ends the call: a loop that ends by itself after seconds, so that a lost
  // Interrupt is counted instead of hanging the player.
  new LatencyCase { Name = "interrupt-nolimit", Interrupt = true, Code = "for i = 1, 3e9 do end" },
  new LatencyCase { Name = "interrupt+instr-3e9", Interrupt = true, Instructions = Backstop, Code = "while true do end" },
  new LatencyCase { Name = "interrupt+time-30s", Interrupt = true, TimeLimitMs = 30000, Code = "while true do end" },
  new LatencyCase { Name = "interrupt+time-30s+instr-1e11", Interrupt = true, TimeLimitMs = 30000, Instructions = Huge,
                    Code = "while true do end" },
 };

 static string Ms(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

 // Nearest rank.
 static double Rank(List<double> sorted, double p) {
  int i = (int)Math.Ceiling(p * sorted.Count) - 1;
  return sorted[Math.Max(0, Math.Min(sorted.Count - 1, i))];
 }

 // Appends one XLUA_NG_CORE_LATENCY line per case; returns null, or what was late or missed.
 static string Latency(List<string> lines) {
  var problems = new List<string>();
  double tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
  foreach (var c in LatencyCases) {
   var options = new LuaOptions { InstructionLimit = c.Instructions, TimeLimit = TimeSpan.FromMilliseconds(c.TimeLimitMs) };
   var stops = new List<double>();
   var missed = new List<string>();
   var budget = System.Diagnostics.Stopwatch.StartNew();
   using (var lua = new LuaScript(options)) {
    for (int run = 0; run < Runs && budget.ElapsedMilliseconds < CaseBudgetMs; run++) {
     var watch = System.Diagnostics.Stopwatch.StartNew();
     long interruptedAt = -1;
     Thread interrupter = null;
     if (c.Interrupt) {
      interrupter = new Thread(() => {
       Thread.Sleep(50);
       Interlocked.Exchange(ref interruptedAt, watch.ElapsedTicks);
       lua.Interrupt();
      });
      interrupter.Start();
     }
     string outcome = null;
     try { lua.Load(c.Code, "latency"); outcome = "no exception"; }
     catch (LuaInterruptedException) { if (!c.Interrupt) outcome = "LuaInterruptedException"; }
     catch (LuaTimeLimitException) { if (c.Interrupt) outcome = "LuaTimeLimitException"; }
     catch (LuaLimitException e) { outcome = e.GetType().Name; }
     long ended = watch.ElapsedTicks;
     if (interrupter != null) interrupter.Join();
     if (outcome != null) {
      // Without a limit nothing stops a lost Interrupt but the end of the loop: do not wait for more.
      missed.Add(outcome + " after " + Ms(ended * tick) + " ms");
      if (c.Instructions == 0 && c.TimeLimitMs == 0) break;
      continue;
     }
     double from = c.Interrupt ? Interlocked.Read(ref interruptedAt) * tick : c.TimeLimitMs;
     stops.Add(ended * tick - from);
    }
   }
   stops.Sort();
   string line = "XLUA_NG_CORE_LATENCY " + c.Name + ": n=" + stops.Count;
   if (stops.Count > 0) {
    double sum = 0;
    foreach (var v in stops) sum += v;
    line += " min=" + Ms(stops[0]) + " p50=" + Ms(Rank(stops, 0.5)) + " p90=" + Ms(Rank(stops, 0.9)) +
            " max=" + Ms(stops[stops.Count - 1]) + " mean=" + Ms(sum / stops.Count) + " ms";
    if (Rank(stops, 0.9) > StopBoundMs) problems.Add(c.Name + " p90 " + Ms(Rank(stops, 0.9)) + " ms");
    if (stops[stops.Count - 1] > StopMaxMs) problems.Add(c.Name + " max " + Ms(stops[stops.Count - 1]) + " ms");
   }
   if (missed.Count > 0) {
    line += " missed=" + missed.Count + " (" + string.Join("; ", missed.ToArray()) + ")";
    problems.Add(c.Name + " missed " + missed.Count);
   }
   line += " all=[" + string.Join(" ", stops.ConvertAll(Ms).ToArray()) + "]";
   lines.Add(line);
  }
  return problems.Count == 0 ? null : string.Join(", ", problems.ToArray());
 }
#endif

 static string ResultPath() { return Path.Combine(Application.persistentDataPath, "xlua-smoke-result.txt"); }

 IEnumerator Start() {
  string path = ResultPath();
  string text = null;
  var worker = new Thread(() => {
   try { text = Run(); }
   catch (Exception e) { text = "XLUA_NG_CORE_SMOKE_FAIL: " + e; }
  }, 16 << 20);
  worker.Start();
  while (worker.IsAlive) yield return null;
  try { File.WriteAllText(path, text + "\n"); } catch (Exception) { }
  bool pass = text.StartsWith("XLUA_NG_CORE_SMOKE_PASS");
  // One log entry per line, so that logcat and the Xcode console keep each one whole.
  foreach (var line in text.Split('\n')) {
   if (pass) Debug.Log(line); else Debug.LogError(line);
  }
  if (!Application.isEditor && Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer) {
   Application.Quit(pass ? 0 : 1);
  }
 }
}
