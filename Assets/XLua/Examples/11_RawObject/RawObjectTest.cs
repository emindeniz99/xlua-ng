using UnityEngine;
using XLua;

namespace XLuaTest
{
    public class RawObjectTest : MonoBehaviour
    {
        public static void PrintType(object o)
        {
            Debug.Log("type:" + o.GetType() + ", value:" + o);
        }

        // Use this for initialization
        void Start()
        {
            LuaEnv luaenv = new LuaEnv();
            //When passing 1234 directly to an object parameter, xLua chooses long to preserve integer precision.
            luaenv.DoString("CS.XLuaTest.RawObjectTest.PrintType(1234)");
            //Use a RawObject subclass to explicitly pass an int.
            luaenv.DoString("CS.XLuaTest.RawObjectTest.PrintType(CS.XLua.Cast.Int32(1234))");
            luaenv.Dispose();
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
