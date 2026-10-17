// The managed suite's configuration lists in the attribute form the source
// generator reads, for Test/Native/run_managed.py --source-gen. The lists stay
// where they are for the legacy generator (--gen-code), which does not read
// these; the two must name the same types.
//
//     TCForTestCSCallLua.cs  GenClass.mymodule_lua_call_cs_list ([CSharpCallLua])
using XLua;

[assembly: CSharpCallLua(typeof(System.Action))]
