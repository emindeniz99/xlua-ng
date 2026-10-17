// Types for the bridge hardening cases in StreamingAssets/luaBridgeHardening.lua
// (docs/research/bridge-audit.md, items 2, 5, 6, 7, 9, 10, 11 and 12). Every
// hardening is an opt-in C# define; each case checks the behaviour of the
// define the suite was built with (run_managed.py --define), so it passes with
// and without it. Build xLua with the same defines.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using XLua;

namespace BridgeHardening
{
    [LuaCallCSharp]
    public class Probe
    {
        public int Value;

        public Probe(int value)
        {
            Value = value;
        }

        public int GetValue()
        {
            return Value;
        }

        // Pushes this object again, which goes through the userdata cache.
        public Probe Self()
        {
            return this;
        }

        public static Probe Make(int value)
        {
            return new Probe(value);
        }

        public override string ToString()
        {
            return "Probe" + Value;
        }

        static int Secret()
        {
            return 42;
        }

        static int secretField = 7;

        public static T Echo<T>(T value)
        {
            return value;
        }

        static T Hidden<T>(T value)
        {
            return value;
        }

        // MethodBase values for xlua.tofunction.
        public static MethodInfo PublicMethod()
        {
            return typeof(Probe).GetMethod("Make");
        }

        public static MethodInfo SecretMethod()
        {
            return typeof(Probe).GetMethod("Secret", BindingFlags.NonPublic | BindingFlags.Static);
        }
    }

    // The defines the suite was built with.
    public static class Defines
    {
        public static bool CheckObjects()
        {
#if XLUA_CHECK_OBJECTS
            return true;
#else
            return false;
#endif
        }

        public static bool CheckStructAccessors()
        {
#if XLUA_CHECK_STRUCT_ACCESSORS
            return true;
#else
            return false;
#endif
        }

        public static bool RestrictGetMetatable()
        {
#if XLUA_RESTRICT_GETMETATABLE
            return true;
#else
            return false;
#endif
        }

        public static bool CheckObjectCache()
        {
#if XLUA_CHECK_OBJECT_CACHE
            return true;
#else
            return false;
#endif
        }

        public static bool LockXLuaTable()
        {
#if XLUA_LOCK_XLUA_TABLE
            return true;
#else
            return false;
#endif
        }

        public static bool RestrictPrivateAccess()
        {
#if XLUA_RESTRICT_PRIVATE_ACCESS
            return true;
#else
            return false;
#endif
        }

        public static bool HotfixEnabled()
        {
#if HOTFIX_ENABLE
            return true;
#else
            return false;
#endif
        }

        public static bool NoLoadAssembly()
        {
#if XLUA_NO_LOAD_ASSEMBLY
            return true;
#else
            return false;
#endif
        }
    }

    // C functions that build userdata a script cannot otherwise make: a block that
    // is not a C# object but carries the bytes the bridge reads from one.
    public static class Natives
    {
        // forge(obj): a full userdata without a metatable whose first int is the
        // object pool index of the C# object obj, as a foreign userdata (a socket
        // holding a small file descriptor, say) may have.
        public static XLua.LuaDLL.lua_CSFunction Forger()
        {
            return Forge;
        }

        [MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
        static int Forge(IntPtr L)
        {
            int index = XLua.LuaDLL.Lua.xlua_tocsobj_safe(L, 1);
            IntPtr block = XLua.LuaDLL.Lua.xlua_pushstruct(L, 0, -1); // registry[-1] is nil: no metatable
            Marshal.WriteInt32(block, index);
            return 1;
        }

        // fake(size): a full userdata without a metatable that has the struct header
        // (fake_id -1, len size) and size zero bytes, the only thing the
        // xlua.genaccessor accessors check without XLUA_CHECK_STRUCT_ACCESSORS.
        public static XLua.LuaDLL.lua_CSFunction FakeStruct()
        {
            return Fake;
        }

        [MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
        static int Fake(IntPtr L)
        {
            int size = XLua.LuaDLL.Lua.xlua_tointeger(L, 1);
            IntPtr block = XLua.LuaDLL.Lua.xlua_pushstruct(L, (uint)size, -1);
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(block, 8 + i, 0);
            }
            return 1;
        }

        // index(obj): the object pool index of a C# object, or -1.
        public static XLua.LuaDLL.lua_CSFunction Indexer()
        {
            return Index;
        }

        [MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
        static int Index(IntPtr L)
        {
            XLua.LuaDLL.Lua.xlua_pushinteger(L, XLua.LuaDLL.Lua.xlua_tocsobj_safe(L, 1));
            return 1;
        }
    }
}
