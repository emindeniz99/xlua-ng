// The managed suite's configuration lists in the attribute form the source
// generator reads, for Test/Native/run_managed.py --source-gen. The lists stay
// where they are for the legacy generator (--gen-code), which does not read
// these; the two must name the same types and members.
//
//     TCForTestCSCallLua.cs     GenClass.mymodule_lua_call_cs_list   [CSharpCallLua]
//     CoverageForLuaCallCS.cs   LazyConfig.DoNotGen                  [DoNotGen]
//                               BlackListConfig.BlackList            [BlackList]
//                               LazyExtendedConfig.DoNotGen          [DoNotGen]
//
// BlackListConfig.Filter, a Func<MemberInfo, bool>, has no attribute form: the
// migration tool (milestone M9) evaluates such filters and writes the BlackList
// entries they match. The entries after it are what it matches in the
// [LuaCallCSharp] types (Coverage.Filtered and the extension methods of
// Coverage.MemberExtensions); the reflection wrappers of FilteredReflected
// still apply the filter itself (BlackListRules).
using XLua;

[assembly: CSharpCallLua(typeof(System.Action))]

[assembly: DoNotGen(typeof(Coverage.Lazy), "Field", "StaticField", "Property", "StaticProperty",
    "Method", "StaticMethod", "Changed", "StaticChanged")]
[assembly: BlackList(typeof(Coverage.BlackListed), "Hidden")]
[assembly: BlackList(typeof(Coverage.BlackListed), "HiddenMethod")]
[assembly: BlackList(typeof(Coverage.BlackListed), "Overload", typeof(string))]
[assembly: DoNotGen(typeof(Coverage.LazyExtended), "Method", "StaticName", "ExtensionOnly")]

// What BlackListConfig.Filter matches in the wrapped types.
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenField")]
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenProperty")]
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenMethod")]
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenStatic")]
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenStaticField")]
[assembly: BlackList(typeof(Coverage.Filtered), "HiddenEvent")]
[assembly: BlackList(typeof(Coverage.Filtered), "Pick", typeof(string))]
[assembly: BlackList(typeof(Coverage.Filtered), ".ctor", typeof(int))]
[assembly: BlackList(typeof(Coverage.MemberExtensions), "HiddenExtension", typeof(Coverage.Filtered))]
