// Extra configuration for the manifest run of Test/Native/luacats_check.py
// (--source-gen), compiled with Sample.cs and SampleAttributes.cs. Each entry
// changes what the source generator binds, and the definitions must follow
// the manifest, not the type's members:
//
// - BlackList entries on a wrapped type: Circle.Scale(int) is not declared
//   (on LuaJIT it was one entry with Scale(double) anyway), and neither is
//   Circle.Sum, whose only overload is listed;
// - a BlackList entry on a base class that only reflection reaches (it is in
//   the manifest's blackList): Shape.Name is not declared;
// - DoNotGen members, which the wrapper leaves to reflection (the manifest's
//   lazy list): Circle.TryGrow and Circle.Radius are still declared;
// - a constructed generic [ReflectionUse] type, found by its manifest name:
//   Dictionary<string, int> is declared in full;
// - a BlackList entry that names no member: the compiler reports XLSG003,
//   and Validate Setup does not report it again.
using XLua;

[assembly: BlackList(typeof(LuaCATSSample.Circle), "Scale", typeof(int))]
[assembly: BlackList(typeof(LuaCATSSample.Circle), "Sum", typeof(string), typeof(int[]))]
[assembly: BlackList(typeof(LuaCATSSample.Shape), "Name")]
[assembly: DoNotGen(typeof(LuaCATSSample.Circle), "TryGrow", "Radius")]
[assembly: ReflectionUse(typeof(System.Collections.Generic.Dictionary<string, int>))]
[assembly: BlackList(typeof(LuaCATSSample.Circle), "Gone")]
