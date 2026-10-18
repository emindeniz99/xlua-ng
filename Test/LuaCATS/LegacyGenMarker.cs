// luacats_check.py compiles this with Sample.cs: the [assembly: GeneratedBindings]
// markers that Generate Code output carries since source generator M0
// (Template/LuaRegister.tpl.txt, LuaDelegateBridge.tpl.txt, LuaWrapPusher.tpl.txt),
// with their bindings in XLua.CSObjectWrap. Such an assembly has no source
// generator manifest: LuaCATS and Validate Setup must read its configuration
// lists, as without the markers. Before the fix, LuaCATS reported "no manifest"
// and left the assembly's types out (Unity editor suite, 2026-10-18).
[assembly: XLua.Binding.GeneratedBindings(typeof(XLua.CSObjectWrap.XLua_Gen_Initer_Register__), XLua.Binding.BindingApi.Version)]
[assembly: XLua.Binding.GeneratedBindings(typeof(XLua.CSObjectWrap.WrapPusher), XLua.Binding.BindingApi.Version)]

namespace XLua.CSObjectWrap
{
    public static class XLua_Gen_Initer_Register__ { }
    public static class WrapPusher { }
}
