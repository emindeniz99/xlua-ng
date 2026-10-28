// The .NET types the suite reaches from Lua through xLua's reflection fallback
// whose members no C# code references. Unity's linker removes such members
// from the class libraries even with Minimal stripping, so in an IL2CPP player
// CS.System.Int64.MaxValue read nil and System.IO.Path.GetExtension and
// TypeInfo.GetDeclaredField were missing (docs/research/il2cpp-suite.md). A game
// that uses them from Lua preserves them the documented way
// (Assets/XLua/Doc/Configure_EN.md, ReflectionUse): the source generator lists
// the types for link.xml. The host suite (run_managed.py) needs none of this,
// so the list lives in the IL2CPP suite's project only.
[assembly: XLua.ReflectionUse(
    typeof(System.Int32),
    typeof(System.Int64),
    typeof(System.UInt64),
    typeof(System.IO.Path),
    typeof(System.Convert),
    typeof(System.BitConverter),
    typeof(System.Reflection.TypeInfo))]
