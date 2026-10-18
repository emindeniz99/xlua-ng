// xLua configuration of assembly XLuaUnitTest, in the attribute form
// that the xLua source generator reads, converted from the configuration
// lists named below, which the generator does not read. Written by the migration tool:
//
//     XLuaGenerate --migrate --group
//
// Delete those lists once this file is in place and edit it by hand from then
// on; the tool rewrites it only while the lists exist. See docs/MIGRATING.md,
// "Converting configuration lists".
//
// xlua-migrated-list: Coverage.MultiTypeConfig.LuaCallCSharp
// xlua-migrated-list: GenClass.mymodule_lua_call_cs_list
// xlua-migrated-list: Coverage.MultiTypeConfig.CSharpCallLua
// xlua-migrated-list: Upstream.UnnameableConfig.Delegates
// xlua-migrated-list: Coverage.MultiTypeConfig.GCOptimize
// xlua-migrated-list: Coverage.MultiTypeConfig.ReflectionUse
// xlua-migrated-list: Coverage.BlackListConfig.BlackList
// xlua-migrated-list: Coverage.BlackListConfig.Filter
// xlua-migrated-list: Coverage.LazyConfig.DoNotGen
// xlua-migrated-list: Coverage.LazyExtendedConfig.DoNotGen

// Coverage.MultiTypeConfig.LuaCallCSharp
[assembly: XLua.LuaCallCSharp(
    typeof(Coverage.MultiListedA),
    typeof(Coverage.MultiListedB))]

// GenClass.mymodule_lua_call_cs_list
[assembly: XLua.CSharpCallLua(typeof(System.Action))]

// Coverage.MultiTypeConfig.CSharpCallLua
[assembly: XLua.CSharpCallLua(
    typeof(Coverage.MultiListedCallback),
    typeof(Coverage.IMultiListed))]

// Upstream.UnnameableConfig.Delegates
[assembly: XLua.CSharpCallLua(typeof(System.Func<int, int>))]

// Coverage.MultiTypeConfig.GCOptimize
[assembly: XLua.GCOptimize(
    typeof(Coverage.MultiListedVec),
    typeof(Coverage.MultiListedPair))]

// Coverage.MultiTypeConfig.ReflectionUse
[assembly: XLua.ReflectionUse(
    typeof(Coverage.MultiReflected),
    typeof(Coverage.MultiReflectedToo))]

// Coverage.BlackListConfig.BlackList
[assembly: XLua.BlackList(typeof(Coverage.BlackListed), "Hidden")]
[assembly: XLua.BlackList(typeof(Coverage.BlackListed), "HiddenMethod")]
[assembly: XLua.BlackList(typeof(Coverage.BlackListed), "Overload", typeof(string))]

// Coverage.BlackListConfig.Filter (filter, evaluated)
[assembly: XLua.BlackList(typeof(Coverage.Filtered), ".ctor", typeof(int))]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenMethod")]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenStatic")]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "Pick", typeof(string))]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenProperty")]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenField")]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenStaticField")]
[assembly: XLua.BlackList(typeof(Coverage.Filtered), "HiddenEvent")]
[assembly: XLua.BlackList(typeof(Coverage.MemberExtensions), "HiddenExtension", typeof(Coverage.Filtered))]
[assembly: XLua.BlackList(typeof(Coverage.MemberExtensions), "HiddenExtension", typeof(Coverage.FilteredReflected))]

// Coverage.LazyConfig.DoNotGen
[assembly: XLua.DoNotGen(typeof(Coverage.Lazy), "Field", "StaticField", "Property", "StaticProperty", "Method", "StaticMethod", "Changed", "StaticChanged")]

// Coverage.LazyExtendedConfig.DoNotGen
[assembly: XLua.DoNotGen(typeof(Coverage.LazyExtended), "Method", "StaticName", "ExtensionOnly")]
