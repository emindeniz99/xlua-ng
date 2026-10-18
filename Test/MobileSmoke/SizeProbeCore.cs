// The size probe of LuaScript (docs/design/core-luascript.md, section 6). size_report.py copies it to
// Assets/XluaSmoke.cs for the core-5.5 and core-5.5+nodefine variants (the class name SmokeBuild.cs
// builds the scene with), and next to SizeProbe.cs for mixed-5.5, which defines XLUA_SIZE_MIXED and
// calls SizeProbeCore.Run() from SizeProbe.cs. It does what a small LuaScript user does: create a script
// with limits and the buffer library, register one C# function, load a chunk, call Lua from C#, and
// pass a buffer. Nothing of the bridge (LuaEnv, XLua.dll) is referenced.
//
// LuaScript compiles only with XLUA_CORE_ONLY or XLUA_LUASCRIPT in the Player Settings (section 8.5).
// SmokeBuild.cs sets them just before the build, so the Editor first compiles this file without them.
using UnityEngine;
#if XLUA_CORE_ONLY || XLUA_LUASCRIPT
using XLua;
#endif

public static class SizeProbeCore {
 public static string Run() {
#if XLUA_CORE_ONLY || XLUA_LUASCRIPT
  var options = new LuaOptions { MemoryLimit = 1 << 20, InstructionLimit = 1000000, Libraries = LuaLibs.Safe | LuaLibs.Buffer };
  using (var lua = new LuaScript(options)) {
   lua.Register<long, long, long>("add", (a, b) => a + b);
   lua.Load("function twice(x) return add(x, x) end\n" +
            "function sum(b) local s = 0 for i = 0, buffer.len(b) - 1 do s = s + buffer.readu8(b, i) end return s end", "probe");
   long result = lua.Call<long>("twice", 20);
   using (var b = lua.NewBuffer(4)) {
    b.WriteInt32(0, 0x00000101);
    result += lua.Call<long>("sum", b);
   }
   return "XLUA_SIZE_PROBE_CORE: " + lua.Load<string>("return _VERSION", "version") + " " + result;
  }
#else
  return "XLUA_SIZE_PROBE_CORE: LuaScript is not compiled (neither XLUA_CORE_ONLY nor XLUA_LUASCRIPT)";
#endif
 }
}

#if !XLUA_SIZE_MIXED
public class XluaSmoke : MonoBehaviour {
 public static string Run() { return SizeProbeCore.Run(); }
 void Start() { Debug.Log(Run()); }
}
#endif
