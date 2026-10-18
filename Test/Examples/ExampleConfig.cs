using System;
using XLua;

// The device example harness's configuration, in the forms the xLua source
// generator reads (assembly attributes); the scenes' own types carry
// [LuaCallCSharp] and [CSharpCallLua] themselves. Until milestone M11 these
// were configuration lists for Generate Code with a BlackList filter for
// networkView, byref-like members (Span<T>) and Light's Editor-only members;
// the generator skips byref-like members itself and compiles each platform with
// that platform's UnityEngine, where those members are absent, so the filter
// has no attribute form here.
[assembly: CSharpCallLua(typeof(Action))]
[assembly: CSharpCallLua(typeof(Action<string>))]
[assembly: CSharpCallLua(typeof(UnityEngine.Events.UnityAction))]
[assembly: LuaCallCSharp(typeof(UnityEngine.GameObject))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Component))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Object))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Vector3))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Color))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Mathf))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Time))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Light))]
[assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]
[assembly: LuaCallCSharp(typeof(UnityEngine.UI.Button))]
[assembly: LuaCallCSharp(typeof(UnityEngine.UI.InputField))]
