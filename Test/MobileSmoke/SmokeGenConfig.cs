// prepare.py --gen copies this into Assets/Editor; XLua/Generate Code
// (CSObjectWrapEditor.Generator.GenAll) then generates a wrapper for SmokeApi and
// the delegate bridges XluaSmoke.cs checks.
using System;
using System.Collections.Generic;
using XLua;

public static class SmokeGenConfig {
 [LuaCallCSharp]
 public static List<Type> LuaCallCSharp = new List<Type> { typeof(SmokeApi) };

 [CSharpCallLua]
 public static List<Type> CSharpCallLua = new List<Type> { typeof(Func<int, int>), typeof(Func<string, string>) };
}
