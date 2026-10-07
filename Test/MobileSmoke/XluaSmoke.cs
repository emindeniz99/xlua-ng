using System;
using System.IO;
using UnityEngine;
using XLua;
public class XluaSmoke : MonoBehaviour {
 public static string Run() {
  using (var env = new LuaEnv()) {
   Check((string)env.DoString("return _VERSION")[0] == "Lua 5.4", "Lua version");
   Check((bool)env.DoString("return os.execute == nil")[0], "os.execute disabled");
   env.GcPause = 224;
   Check(Convert.ToInt32(env.DoString("return collectgarbage('setpause', 224)")[0]) == 224, "GC pause setter");
   Check(env.GcPause == 224, "GC pause getter");
   env.GcStepmul = 240;
   Check(Convert.ToInt32(env.DoString("return collectgarbage('setstepmul', 240)")[0]) == 240, "GC multiplier setter");
   Check(env.GcStepmul == 240, "GC multiplier getter");
   Check((bool)env.DoString("local closed=false; do local x <close> = setmetatable({}, {__close=function() closed=true end}) end; return closed")[0], "Lua 5.4 close semantics");
   Check((bool)env.DoString("local c=coroutine.create(function() coroutine.yield(42); return 84 end); local a,b=coroutine.resume(c); local d,e=coroutine.resume(c); return a and b==42 and d and e==84")[0], "Coroutine");
   env.FullGc();
  }
  return "XLUA_NG_SMOKE_PASS: Lua 5.4, GC setters/getters, close semantics, coroutine, disposal";
 }
 static void Check(bool value, string name) { if (!value) throw new Exception("XLUA_NG_SMOKE_FAIL: " + name); }
 void Start() {
  string result;
  try { result=Run(); } catch(Exception e) { result="XLUA_NG_SMOKE_FAIL: " + e; }
  Debug.Log(result);
  File.WriteAllText(Path.Combine(Application.persistentDataPath,"xlua-smoke-result.txt"),result);
 }
}
