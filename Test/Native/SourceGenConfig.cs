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
// entries they match. Until wrappers are generated (M2) these entries only
// feed the generator's checks (XLSG003); the suite does not depend on them.
using XLua;

[assembly: CSharpCallLua(typeof(System.Action))]

[assembly: DoNotGen(typeof(Coverage.Lazy), "Field", "StaticField", "Property", "StaticProperty",
    "Method", "StaticMethod", "Changed", "StaticChanged")]
[assembly: BlackList(typeof(Coverage.BlackListed), "Hidden")]
[assembly: BlackList(typeof(Coverage.BlackListed), "HiddenMethod")]
[assembly: BlackList(typeof(Coverage.BlackListed), "Overload", typeof(string))]
[assembly: DoNotGen(typeof(Coverage.LazyExtended), "Method", "StaticName", "ExtensionOnly")]
