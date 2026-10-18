// Fixture for the Validate Setup checks of the source generator path in
// Test/Native/luacats_check.py (--source-gen): an assembly with xLua
// configuration that the generator did not compile (mcs builds it), as when
// warning CS9057 stops the generator. Compiled against XLua.Mini.dll; never run.
using XLua;

[assembly: CSharpCallLua(typeof(LuaCATSNoBindings.Callback))]

namespace LuaCATSNoBindings
{
    public delegate void Callback(int value);

    [LuaCallCSharp]
    public class Thing
    {
        public int Value;
    }
}
