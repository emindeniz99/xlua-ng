using UnityEngine;
using XLua;
using XluaEditorTest;

// prepare.py --source-gen: the editor suite's configuration lists
// (XluaEditorTest.Config in XluaEditorTestTarget.cs) in the forms the source
// generator reads, as the migration tool (milestone M9) will write them. The
// generator ignores the lists (info XLSG011), and its bridges set Gen_Flag,
// which turns CodeEmit off, so without these the list-only types would fall
// back to reflection. Camera's nested types (a computed list) are left out.
[assembly: LuaCallCSharp(typeof(Vector3))]
[assembly: LuaCallCSharp(typeof(GameObject))]
[assembly: LuaCallCSharp(typeof(Transform))]
[assembly: LuaCallCSharp(typeof(Camera))]
[assembly: LuaCallCSharp(typeof(Target))]
[assembly: LuaCallCSharp(typeof(Pair))]
[assembly: LuaCallCSharp(typeof(Mood))]
[assembly: LuaCallCSharp(typeof(Text))]
[assembly: LuaCallCSharp(typeof(Names))]
[assembly: LuaCallCSharp(typeof(NameEnum))]
[assembly: LuaCallCSharp(typeof(InIndexed))]
[assembly: GCOptimize(typeof(Pair))]
[assembly: GCOptimize(typeof(Vector3))]
