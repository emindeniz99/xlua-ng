using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting;
using XLua;

// Called from Lua: the integer rule for C# integer parameters, and a delegate parameter
// that Lua passes a function to. [Preserve] keeps the members for reflection wrappers.
[Preserve]
public static class SmokeApi {
 [Preserve] public static int Twice(int x) { return x * 2; }
 [Preserve] public static string Wrap(Func<string, string> f, string s) { return f(s); }
#if XLUA_SMOKE_GEN
 [Preserve] public static int Apply(Func<int, int> f, int x) { return f(x); }
#endif
}

public class XluaSmoke : MonoBehaviour {
#if XLUA_SMOKE_LUA53
 const string ExpectedVersion = "Lua 5.3";
#elif XLUA_SMOKE_LUA55
 const string ExpectedVersion = "Lua 5.5";
#else
 const string ExpectedVersion = "Lua 5.4";
#endif
 public static string Run() {
  var env = new LuaEnv();
  string features;
  try {
   features = Checks(env);
   Release(env);
  } catch {
   DisposeAfterFailure(env);
   throw;
  }
  env.Dispose();
  return Pass(features);
 }

 // Every check except LuaEnv.Dispose; returns what was found.
 static string Checks(LuaEnv env) {
  Check((string)env.DoString("return _VERSION")[0] == ExpectedVersion, "Lua version");
  Check((bool)env.DoString("return os.execute == nil")[0], "os.execute disabled");
  int expectedPause = ExpectedVersion == "Lua 5.5" ? 212 : 224;
  int expectedStepmul = ExpectedVersion == "Lua 5.5" ? 237 : 240;
  env.GcPause = 224;
  Check(Convert.ToInt32(env.DoString("return _VERSION == 'Lua 5.5' and collectgarbage('param', 'pause', 224) or collectgarbage('setpause', 224)")[0]) == expectedPause, "GC pause setter");
  Check(env.GcPause == expectedPause, "GC pause getter");
  env.GcStepmul = 240;
  Check(Convert.ToInt32(env.DoString("return _VERSION == 'Lua 5.5' and collectgarbage('param', 'stepmul', 240) or collectgarbage('setstepmul', 240)")[0]) == expectedStepmul, "GC multiplier setter");
  Check(env.GcStepmul == expectedStepmul, "GC multiplier getter");
#if !XLUA_SMOKE_LUA53
  Check((bool)env.DoString("local closed=false; do local x <close> = setmetatable({}, {__close=function() closed=true end}) end; return closed")[0], "Lua 5.4 close semantics");
#endif
  Check((bool)env.DoString("local c=coroutine.create(function() coroutine.yield(42); return 84 end); local a,b=coroutine.resume(c); local d,e=coroutine.resume(c); return a and b==42 and d and e==84")[0], "Coroutine");
  IntegerRule(env);
  string bridge = Delegates(env);
  string features = FeatureDefines(env);
  env.DoString("upper = nil; twice = nil");
  env.StopGc();
  Check(!(bool)env.DoString("return collectgarbage('isrunning')")[0], "GC stop");
  env.GcStep(1);
  Check(!(bool)env.DoString("return collectgarbage('isrunning')")[0], "GC step preserves stop");
  env.RestartGc();
  Check((bool)env.DoString("return collectgarbage('isrunning')")[0], "GC restart");
  env.GcStep(0);
  Check(env.Memroy >= 0, "GC memory count");
  env.FullGc();
  return "integer rule, delegates " + bridge + ", " + features;
 }

 // LuaEnv.Dispose refuses to run while a delegate made from a Lua function is alive.
 static void Release(LuaEnv env) {
  GC.Collect();
  GC.WaitForPendingFinalizers();
  GC.Collect();
  env.Tick();
 }

 // Dispose throws while a delegate holds Lua; keep the first failure's message.
 static void DisposeAfterFailure(LuaEnv env) {
  try { env.Dispose(); } catch (Exception) { }
 }

 static string Pass(string features) {
  return "XLUA_NG_SMOKE_PASS: " + ExpectedVersion + ", GC setters/getters, coroutine, " + features + ", disposal";
 }

 // Lua 5.3's rule for C# integer parameters (docs/MIGRATING.md): reflection wrappers
 // without generated code, generated wrappers with it.
 static void IntegerRule(LuaEnv env) {
  Check(Convert.ToInt64(env.DoString("return CS.SmokeApi.Twice(21)")[0]) == 42, "integer argument");
  Check(Convert.ToInt64(env.DoString("return CS.SmokeApi.Twice(3.0)")[0]) == 6, "float with an integer value");
  Check(Convert.ToInt64(env.DoString("return CS.SmokeApi.Twice('12')")[0]) == 24, "numeric string argument");
  var error = (string)env.DoString("local ok, e = pcall(CS.SmokeApi.Twice, 2.75); return ok and 'no error' or tostring(e)")[0];
  Check(error.Contains("number has no integer representation"), "integer rule error for 2.75: " + error);
 }

 // Lua functions cast to C# delegates. With generated code (XLUA_SMOKE_GEN) these are the
 // static bridges of XLua.CSObjectWrap.DelegatesGensBridge bound as delegates closed over the
 // DelegateBridge; without it, IL2CPP has no Reflection.Emit and DelegateBridge's generic
 // Func methods serve reference-type signatures only. The delegates live only in this frame,
 // so that a collection can release their bridges before LuaEnv.Dispose.
 [MethodImpl(MethodImplOptions.NoInlining)]
 static string Delegates(LuaEnv env) {
  env.DoString("function upper(s) return string.upper(s) .. '!' end; function twice(x) return x * 2 end");
  Check((string)env.DoString("return CS.SmokeApi.Wrap(function(s) return s .. s end, 'ab')")[0] == "abab", "Lua function as a delegate argument");
  var upper = env.Global.Get<Func<string, string>>("upper");
  Check(upper("lua") == "LUA!", "Func<string, string> from Lua");
  Check(upper.Target is DelegateBridge, "delegate target is a DelegateBridge: " + upper.Target);
#if XLUA_SMOKE_GEN
  Check(Convert.ToInt64(env.DoString("return CS.SmokeApi.Apply(function(x) return x + 1 end, 41)")[0]) == 42, "Lua function as a Func<int, int> argument");
  var twice = env.Global.Get<Func<int, int>>("twice");
  Check(twice(21) == 42, "Func<int, int> from Lua");
  foreach (Delegate d in new Delegate[] { upper, twice }) {
   Check(d.Method.DeclaringType.FullName == "XLua.CSObjectWrap.DelegatesGensBridge", "generated bridge class: " + d.Method.DeclaringType);
   Check(d.Method.IsStatic, "generated bridge is static: " + d.Method);
   Check(d.Method.Name.StartsWith("__Gen_Delegate_Imp"), "generated bridge method: " + d.Method.Name);
   Check(d.Target is DelegateBridge, "generated bridge closed over its DelegateBridge: " + d.Target);
  }
  return "generated";
#else
  // Reference-type signatures use DelegateBridge's generic Func methods.
  Check(upper.Method.DeclaringType == typeof(DelegateBridge), "generic bridge class: " + upper.Method.DeclaringType);
  // Value types need generated code, or in the Editor a [CSharpCallLua] entry for
  // CodeEmit.cs; this project lists none.
  string error = null;
  try { env.Global.Get<Func<int, int>>("twice"); } catch (Exception e) { error = e.ToString(); }
  Check(error != null && error.Contains("CSharpCallLua"), "Func<int, int> without generated code: " + error);
  return "generic";
#endif
 }

 // The C# feature defines (docs/MIGRATING.md, "Opt-in defines") take effect in players only.
 static string FeatureDefines(LuaEnv env) {
  bool template = (bool)env.DoString("return type(template) == 'table'")[0];
  bool util = (bool)env.DoString("return (pcall(require, 'xlua.util'))")[0];
  bool profiler = (bool)env.DoString("return (pcall(require, 'perf.profiler'))")[0];
  bool signature = typeof(LuaEnv).Assembly.GetType("XLua.SignatureLoader") != null;
#if XLUA_NO_TEMPLATE && !UNITY_EDITOR
  Check(!template, "XLUA_NO_TEMPLATE leaves out the template library");
#else
  Check(template, "template library");
#endif
#if XLUA_NO_UTIL
  Check(!util, "XLUA_NO_UTIL leaves out xlua.util");
#else
  Check(util, "embedded xlua.util");
  Check((bool)env.DoString("local util = require 'xlua.util'; return type(util.cs_generator) == 'function'")[0], "xlua.util.cs_generator");
#endif
#if XLUA_NO_PROFILER && !UNITY_EDITOR && !DEVELOPMENT_BUILD
  Check(!profiler, "XLUA_NO_PROFILER leaves out perf.profiler");
#else
  Check(profiler, "embedded perf.profiler");
#endif
#if XLUA_NO_SIGNATURE_LOADER
  Check(!signature, "XLUA_NO_SIGNATURE_LOADER leaves out SignatureLoader");
#else
  Check(signature, "SignatureLoader");
#endif
  return "template " + (template ? "on" : "off") + ", xlua.util " + (util ? "on" : "off") +
   ", perf.profiler " + (profiler ? "on" : "off") + ", SignatureLoader " + (signature ? "on" : "off");
 }

 static void Check(bool value, string name) { if (!value) throw new Exception("XLUA_NG_SMOKE_FAIL: " + name); }
 // The player disposes the LuaEnv one frame after the checks: WebGL collects garbage only
 // between frames, when the stack is empty, so a GC.Collect in the frame that dropped the
 // delegates leaves their bridges alive and LuaEnv.Dispose throws.
 IEnumerator Start() {
  string result = null;
  var env = new LuaEnv();
  string features = null;
  try { features = Checks(env); } catch (Exception e) { result = "XLUA_NG_SMOKE_FAIL: " + e; DisposeAfterFailure(env); }
  if (result == null) {
   yield return null;
   try { Release(env); env.Dispose(); result = Pass(features); }
   catch (Exception e) { result = "XLUA_NG_SMOKE_FAIL: " + e; }
  }
  Debug.Log(result);
  File.WriteAllText(Path.Combine(Application.persistentDataPath,"xlua-smoke-result.txt"),result);
 }
}
