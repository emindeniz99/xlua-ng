// size_report.py +sg copies this into Assets: the configuration of SizeGenConfig.cs
// in the assembly attributes the xLua source generator reads, so that the
// legacy Generate Code variants (+gen, +gcm) and the source generator variant
// bind the same types and delegates.
using System;
using XLua;

[assembly: LuaCallCSharp(typeof(SizeProbeApi))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Vector3))]
[assembly: LuaCallCSharp(typeof(UnityEngine.GameObject))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Debug))]
[assembly: CSharpCallLua(typeof(Func<int, int>))]
[assembly: CSharpCallLua(typeof(Action<string>))]
