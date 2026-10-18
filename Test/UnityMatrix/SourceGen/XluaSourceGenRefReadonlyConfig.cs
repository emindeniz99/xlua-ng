using XLua;

// prepare.py --source-gen with --roslyn-sdk: the types of the precompiled
// XluaRefReadonlyLib.dll, with C# 12 `ref readonly` parameters, wrapped by the
// source generator in Assembly-CSharp. Its rules depend on the compiler that
// runs it (Members.cs OldCompiler): Roslyn before 4.10 (Unity 2021.3 to
// 6000.3) cannot call the virtual members (CS0570) and calls Dial's indexer
// through its accessors; Roslyn 4.10 (6000.6) uses them all. The generated
// code compiling at all is the main check.
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.Gauge))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.GaugeImpl))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.Dial))]
[assembly: LuaCallCSharp(typeof(XluaRefReadonlyLib.DialVirtual))]
