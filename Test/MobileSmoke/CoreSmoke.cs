// The core-only player smoke (docs/design/core-luascript.md, milestone 11). prepare.py --core copies
// it to Assets/XluaSmoke.cs (the class SmokeBuild.cs builds the scene with) in a project that has xLua
// as the UPM package with the core library and XLUA_CORE_ONLY in the Player Settings, so the player
// has XLua.Core and no XLua assembly. It checks LuaScript's Load and Call, Register (a C# function
// called from Lua, and a C# exception becoming a Lua error), the memory, instruction and time limits,
// Interrupt, the buffer library and LuaBufferRef, and the sandbox (no io, os, debug or package, no
// bytecode), then logs XLUA_NG_CORE_SMOKE_PASS or XLUA_NG_CORE_SMOKE_FAIL and writes
// xlua-smoke-result.txt under Application.persistentDataPath.
//
// LuaScript compiles only with XLUA_CORE_ONLY or XLUA_LUASCRIPT. SmokeBuild.cs sets the define for the
// player build; until then the Editor compiles the fallback below.
using System;
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
  return "XLUA_NG_CORE_SMOKE_PASS: " + checks;
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
#endif

 static string ResultPath() { return Path.Combine(Application.persistentDataPath, "xlua-smoke-result.txt"); }

 void Start() {
  string line;
  try { line = Run(); }
  catch (Exception e) { line = "XLUA_NG_CORE_SMOKE_FAIL: " + e; }
  try { File.WriteAllText(ResultPath(), line + "\n"); } catch (Exception) { }
  if (line.StartsWith("XLUA_NG_CORE_SMOKE_PASS")) Debug.Log(line); else Debug.LogError(line);
  if (!Application.isEditor && Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer) {
   Application.Quit(line.StartsWith("XLUA_NG_CORE_SMOKE_PASS") ? 0 : 1);
  }
 }
}
