// size_report.py --gen copies this into Assets/Editor before running XLua/Generate Code.
// A small configuration: the probe API, a few common UnityEngine types and two delegates.
using System;
using System.Collections.Generic;
using XLua;

public static class SizeGenConfig {
 [LuaCallCSharp]
 public static List<Type> LuaCallCSharp = new List<Type> {
  typeof(SizeProbeApi),
  typeof(UnityEngine.Vector3),
  typeof(UnityEngine.GameObject),
  typeof(UnityEngine.Transform),
  typeof(UnityEngine.Debug),
 };

 [CSharpCallLua]
 public static List<Type> CSharpCallLua = new List<Type> {
  typeof(Func<int, int>),
  typeof(Action<string>),
 };
}
