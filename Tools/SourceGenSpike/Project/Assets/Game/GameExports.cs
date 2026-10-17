// Consumer 1: Assembly-CSharp (no asmdef). XLuaTest.Runtime is auto-referenced.
[assembly: XLuaTest.Export(typeof(UnityEngine.Vector3))]

namespace Game
{
    [XLuaTest.Export]
    public class UserThing
    {
        public const int Version = 0;

        // The generated wrapper calls this directly.
        public static int Answer() => 42 + Version;

        // Nothing calls this; shows what stripping does to members only reflection would find.
        public static int OnlyReflected() => 7;
    }

    // Unsupported on purpose: the generator reports warning XLSG001 here.
    [XLuaTest.Export]
    public class GenericThing<T>
    {
    }
}
