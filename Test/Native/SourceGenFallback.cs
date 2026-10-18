// A member the xLua source generator leaves to reflection
// (docs/design/source-generator.md, section 5.5, milestone M5), for
// Test/Native/run_managed.py --source-gen. Only the source generator's build of
// the suite compiles it: mcs, which builds the other modes, has no init
// accessors, and the legacy generator drops such a setter. run_managed.py lists
// the type as an allowed difference of the surface diff.
//
// SourceGenLauncher.cs sets Locked from Lua, which goes through
// BindingApi.RegisterFallback and logs the reason once.

namespace System.Runtime.CompilerServices
{
    // Not in the .NET Framework profile the suite compiles against.
    internal static class IsExternalInit
    {
    }
}

[XLua.LuaCallCSharp]
public class SourceGenFallback
{
    public int Locked { get; init; }

    public int Plain;
}
