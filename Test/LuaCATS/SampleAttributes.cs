// The configuration of Sample.cs in the attribute form the source generator
// reads; LuaCATS writes the files in Test/LuaCATS/expected from the manifest
// the generator writes for it (Test/Native/luacats_check.py). Until the legacy
// generator was removed (milestone M11) Sample.cs also had this configuration
// as its Config.LuaCallCSharp list, in the same order, and both forms gave the
// same files.
using XLua;

[assembly: LuaCallCSharp(typeof(LuaCATSSample.Color))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.IShape))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Circle))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Circle.Style))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Point))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.Ledger))]
[assembly: LuaCallCSharp(typeof(LuaCATSSample.ShapeExtensions))]
[assembly: LuaCallCSharp(typeof(System.Collections.Generic.List<int>))]
