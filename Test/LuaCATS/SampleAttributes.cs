// The configuration of Sample.cs (its Config.LuaCallCSharp list) in the
// attribute form the source generator reads, for the manifest run of
// Test/Native/luacats_check.py (--source-gen). The legacy generator reads the
// list and ignores these; the two must name the same types in the same order,
// so that both runs give the files in Test/LuaCATS/expected.
using XLua;

[assembly: LuaCallCSharp(typeof(LuaCATSSample.Color))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.IShape))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Circle))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Circle.Style))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Point))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Ledger))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.ShapeExtensions))]
[assembly: LuaCallCSharp(typeof(System.Collections.Generic.List<int>))]
