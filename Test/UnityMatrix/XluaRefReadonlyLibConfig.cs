using XLua;

// Configures the types of the precompiled XluaRefReadonlyLib.dll for Lua, with
// C# 12 `ref readonly` parameters; prepare.py --roslyn-sdk copies this file
// together with the DLL, and the source generator (--source-gen) wraps the
// types in Assembly-CSharp. Its rules depend on the compiler that runs it
// (Members.cs OldCompiler): Roslyn before 4.10 (Unity 2021.3 to 6000.3) cannot
// call the virtual members (CS0570) and calls Dial's indexer through its
// accessors; Roslyn 4.10 (6000.6) uses them all. Until milestone M11 this was a
// configuration list for Generate Code.
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.Gauge))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.GaugeImpl))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.Dial))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.DialVirtual))]
// Before Roslyn 4.10 the generator leaves both out (info XLSG004).
[assembly: CSharpCallLua(typeof(XluaRefReadonlyLib.Measure))]
[assembly: CSharpCallLua(typeof(XluaRefReadonlyLib.IGauge))]
