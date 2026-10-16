using System;
using System.Collections.Generic;
using XLua;
using XluaRefReadonlyLib;

// Configures the types of the precompiled XluaRefReadonlyLib.dll for Lua.
// prepare.py --roslyn-sdk copies this file together with the DLL.
public static class XluaRefReadonlyLibConfig
{
    [LuaCallCSharp]
    public static List<Type> LuaCallCSharp = new List<Type>()
    {
        typeof(Gauge),
        typeof(GaugeImpl),
        typeof(IGauge),
    };

    [CSharpCallLua]
    public static List<Type> CSharpCallLua = new List<Type>()
    {
        typeof(Measure),
        typeof(IGauge),
    };
}
