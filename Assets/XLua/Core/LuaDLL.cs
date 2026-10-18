/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The native imports of the XLua.Core assembly (docs/design/core-luascript.md,
// section 2.2, and the owner's decision 1): the Lua C API functions that every
// build of the native library has (Lua's own lapi.c and lauxlib.c, also in the
// core preset's library) and the xlua_core_* layer. The imports that only the
// C# bridge needs (the functions of xlua.c and i64lib.c, and the Lua API
// shims xlua.c defines for macros, such as lua_pcall and lua_insert) are in
// XLua.LuaDLL.Bridge (Assets/XLua/Src/LuaDLLBridge.cs), which derives from
// this class, so that the bridge and generated code reach both through it.
namespace XLua.LuaDLL
{

    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using XLua;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || XLUA_GENERAL || (UNITY_WSA && !UNITY_EDITOR)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int lua_CSFunction(IntPtr L);
#else
    public delegate int lua_CSFunction(IntPtr L);
#endif

    // Mirrors of the structs in build/xlua_core.h; xlua_core_version() names the layout.
    [StructLayout(LayoutKind.Sequential)]
    public struct xlua_core_config
    {
        public long memory_limit;
        public uint seed;
        public int has_seed;
        public int libs;
        public int sandbox;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct xlua_core_stats
    {
        public long memory_used;
        public long memory_peak;
        public long memory_limit;
        public long refusals;
        public long overdrafts;
        public long host_oom;
        public long instructions;
        public long stop_reason;
        public long instruction_limit;
        public long time_limit_ns;
    }

    // xlua_value: 16 bytes on every ABI; the union is at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct xlua_value
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(4)] public int len;
        [FieldOffset(8)] public long i;
        [FieldOffset(8)] public double n;
        [FieldOffset(8)] public IntPtr s;
    }

    // xlua_core_call_t: the 64-bit field, then the pointers, then the 32-bit fields.
    [StructLayout(LayoutKind.Sequential)]
    public struct xlua_core_call
    {
        public long instructions;
        public IntPtr name;
        public IntPtr code;
        public IntPtr args;
        public IntPtr results;
        public IntPtr error;
        public IntPtr traceback;
        public int kind;
        public int env;
        public int fn;
        public int name_len;
        public int code_len;
        public int nargs;
        public int results_cap;
        public int nresults;
        public int error_len;
        public int traceback_len;
        public int error_ref;
        public int mode;  // XLUA_CORE_MODE_*: 0 is text
        public int flags; // out: XLUA_CALLF_* (core version 4)
    }

#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
    // xlua_core_frame: one call of a host function (LuaScript.Register); pointers, then 32-bit fields.
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct xlua_core_frame
    {
        public IntPtr L;
        public xlua_value* args;
        public xlua_value* results;
        public IntPtr error;
        public int nargs;
        public int results_cap;
        public int nresults;
        public int status;
        public int error_len;
        public int error_arg;
        public int yieldable;
        public int id;
    }

    // xlua_core_source: what the resolver of require reports.
    [StructLayout(LayoutKind.Sequential)]
    public struct xlua_core_source
    {
        public IntPtr code;
        public int code_len;
        public IntPtr chunkname;
        public int chunkname_len;
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || XLUA_GENERAL || (UNITY_WSA && !UNITY_EDITOR)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int xlua_core_resolver(IntPtr host, IntPtr name, int name_len, xlua_core_source* result);
#else
    public unsafe delegate int xlua_core_resolver(IntPtr host, IntPtr name, int name_len, xlua_core_source* result);
#endif

    // The process-wide dispatcher of host functions (LuaScript.Register): host is the
    // LuaScript's weak GCHandle, fn_id the function's id.
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || XLUA_GENERAL || (UNITY_WSA && !UNITY_EDITOR)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void xlua_core_dispatch(IntPtr host, int fn_id, xlua_core_frame* frame);
#else
    public unsafe delegate void xlua_core_dispatch(IntPtr host, int fn_id, xlua_core_frame* frame);
#endif
#endif

    internal static class CoreGlobals
    {
#if !THREAD_SAFE && !HOTFIX_ENABLE
        // The UTF-8 buffer of short strings that lua_tostring decodes and the
        // bridge's lua_pushstring encodes (one thread at a time).
        internal static byte[] strBuff = new byte[256];
#endif
    }

    public partial class Lua
	{
        // XLUA_ANDROID_SOURCE_PLUGIN: the Lua and xLua C sources are under Assets/Plugins/Android and
        // IL2CPP links them into libil2cpp.so, so there is no libxlua.so to load. Off by default.
#if (UNITY_IPHONE || UNITY_TVOS || UNITY_WEBGL || UNITY_SWITCH || (UNITY_ANDROID && XLUA_ANDROID_SOURCE_PLUGIN)) && !UNITY_EDITOR
        protected const string LUADLL = "__Internal";
#else
        protected const string LUADLL = "xlua";
#endif

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr lua_tothread(IntPtr L, int index);

        [DllImport(LUADLL, EntryPoint = "xlua_gc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int lua_gc(IntPtr L, LuaGCOptions what, int data);

        // The core layer (build/xlua_core.c, xlua_core.h): states with a memory
        // limit and a hash seed, for LuaEnv(LuaOptions).
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_version();

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr xlua_core_newstate(ref xlua_core_config config, out int status);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void xlua_core_close(IntPtr L);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_set_memory_limit(IntPtr L, long bytes);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_get_stats(IntPtr L, out xlua_core_stats stats);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_set_limits(IntPtr L, long instructions, long time_ns, int flags);

        // Any thread: Interrupt and the watchdog (LuaWatchdog.cs).
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_interrupt(IntPtr L);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern long xlua_core_watch(IntPtr L);

#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
        // LuaScript's sandbox (build/xlua_core_sandbox.c): in the core preset's library and in a
        // bridge library built with XLUA_LUASCRIPT=ON. Only compiled with the scripting define
        // XLUA_LUASCRIPT or XLUA_CORE_ONLY, so that a player without LuaScript references none
        // of them (an iOS or WebGL link would fail on a library without the sandbox).
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr xlua_core_open(ref xlua_core_config config, IntPtr host, out int status);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int xlua_core_call(IntPtr L, xlua_core_call* call);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_newenv(IntPtr L, out int env);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_unref(IntPtr L, int handle);

        // require: the process-wide resolver (LuaScript.ModuleResolver) and require per state.
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void xlua_core_set_resolver(IntPtr resolver);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_set_require(IntPtr L, int enabled);

        // Host functions (LuaScript.Register): the process-wide dispatcher, and a name in the
        // base or a library of a state (fn_id 0 removes it).
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void xlua_core_set_dispatcher(IntPtr dispatcher);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_register(IntPtr L, int env, IntPtr name, int name_len, int fn_id, out int old_id);
#endif

#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
        // LuaScript's buffers (build/xlua_core_buffer.c, milestone 10): with the sandbox, also when
        // CMake XLUA_BUFFER leaves the library out (they then report XLUA_ERRCONFIG and NULL).
        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlua_core_newbuffer(IntPtr L, int len, out int handle, out IntPtr data);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr xlua_core_tobuffer(IntPtr L, int handle, out int len);
#endif

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr lua_getupvalue(IntPtr L, int funcindex, int n);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr lua_setupvalue(IntPtr L, int funcindex, int n);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern int lua_pushthread(IntPtr L);

		public static bool lua_isfunction(IntPtr L, int stackPos)
		{
			return lua_type(L, stackPos) == LuaTypes.LUA_TFUNCTION;
		}

		public static bool lua_islightuserdata(IntPtr L, int stackPos)
		{
			return lua_type(L, stackPos) == LuaTypes.LUA_TLIGHTUSERDATA;
		}

		public static bool lua_istable(IntPtr L, int stackPos)
		{
			return lua_type(L, stackPos) == LuaTypes.LUA_TTABLE;
		}

		public static bool lua_isthread(IntPtr L, int stackPos)
		{
			return lua_type(L, stackPos) == LuaTypes.LUA_TTHREAD;
		}

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr luaL_newstate();

		[DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern void lua_close(IntPtr L);

		[DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern void lua_createtable(IntPtr L, int narr, int nrec);//[-0, +0, m]

        public static void lua_newtable(IntPtr L)//[-0, +0, m]
        {
			lua_createtable(L, 0, 0);
		}

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_settop(IntPtr L, int newTop);

		public static void lua_pop(IntPtr L, int amount)
		{
			lua_settop(L, -(amount) - 1);
		}

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int lua_rawget(IntPtr L, int index);

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_rawset(IntPtr L, int index);//[-2, +0, m]

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int lua_setmetatable(IntPtr L, int objIndex);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int lua_rawequal(IntPtr L, int index1, int index2);

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_pushvalue(IntPtr L, int index);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void lua_pushcclosure(IntPtr L, IntPtr fn, int n);//[-n, +1, m]

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int lua_gettop(IntPtr L);

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern LuaTypes lua_type(IntPtr L, int index);

		public static bool lua_isnil(IntPtr L, int index)
		{
			return (lua_type(L,index)==LuaTypes.LUA_TNIL);
		}

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern bool lua_isnumber(IntPtr L, int index);

		public static bool lua_isboolean(IntPtr L, int index)
		{
			return lua_type(L,index)==LuaTypes.LUA_TBOOLEAN;
		}

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int luaL_ref(IntPtr L, int registryIndex);

        public static int luaL_ref(IntPtr L)//[-1, +0, m]
        {
			return luaL_ref(L,LuaIndexes.LUA_REGISTRYINDEX);
		}

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void luaL_unref(IntPtr L, int registryIndex, int reference);

		public static void lua_unref(IntPtr L, int reference)
		{
			luaL_unref(L,LuaIndexes.LUA_REGISTRYINDEX,reference);
		}

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern bool lua_isstring(IntPtr L, int index);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool lua_isinteger(IntPtr L, int index);

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_pushnil(IntPtr L);

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern bool lua_toboolean(IntPtr L, int index);

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr lua_topointer(IntPtr L, int index);

        [DllImport(LUADLL,CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr lua_tolstring(IntPtr L, int index, out IntPtr strLen);//[-0, +0, m]

        public static string lua_tostring(IntPtr L, int index)
		{
            IntPtr strlen;

            IntPtr str = lua_tolstring(L, index, out strlen);
            if (str != IntPtr.Zero)
			{
                return DecodeLuaString(str, strlen.ToInt32());
            }
            else
			{
                return null;
			}
		}

        // Lua strings are bytes with an explicit length. Every build decodes
        // them here, as UTF-8: invalid bytes (GBK text, binary data) become
        // U+FFFD instead of throwing, and embedded NULs are kept.
        // Marshal.PtrToStringAnsi threw ArgumentException for invalid UTF-8
        // in Unity's Mono (https://github.com/Tencent/xLua/issues/1203).
        static string DecodeLuaString(IntPtr str, int len)
        {
            if (len == 0)
            {
                return string.Empty;
            }
#if !THREAD_SAFE && !HOTFIX_ENABLE
            // Short strings reuse the buffer that lua_pushstring encodes into.
            byte[] buffer = len <= CoreGlobals.strBuff.Length ? CoreGlobals.strBuff : new byte[len];
#else
            byte[] buffer = new byte[len];
#endif
            Marshal.Copy(str, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer, 0, len);
        }

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr lua_atpanic(IntPtr L, lua_CSFunction panicf);

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_pushnumber(IntPtr L, double number);

		[DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_pushboolean(IntPtr L, bool value);

        public static byte[] lua_tobytes(IntPtr L, int index)//[-0, +0, m]
        {
            if (lua_type(L, index) == LuaTypes.LUA_TSTRING)
            {
                IntPtr strlen;
                IntPtr str = lua_tolstring(L, index, out strlen);
                if (str != IntPtr.Zero)
                {
                    int buff_len = strlen.ToInt32();
                    byte[] buffer = new byte[buff_len];
                    Marshal.Copy(str, buffer, 0, buff_len);
                    return buffer;
                }
            }
            return null;
        }

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int luaL_newmetatable(IntPtr L, string meta);//[-0, +1, m]

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
		public static extern bool lua_checkstack(IntPtr L,int extra);//[-0, +0, m]

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern int lua_next(IntPtr L,int index);//[-1, +(2|0), e]

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
		public static extern void lua_pushlightuserdata(IntPtr L, IntPtr udata);

        [DllImport(LUADLL,CallingConvention=CallingConvention.Cdecl)]
        public static extern void luaL_where (IntPtr L, int level);//[-0, +1, m]

        [DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr lua_touserdata(IntPtr L, int idx);
    }
}
