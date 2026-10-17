// Consumer 2: a user asmdef that references XLuaTest.Runtime by name.
[assembly: XLuaTest.Export(typeof(UnityEngine.Time))]

namespace UserGame
{
    [XLuaTest.Export]
    public class UserGameThing
    {
        public static string Hello => "hello";
    }
}
