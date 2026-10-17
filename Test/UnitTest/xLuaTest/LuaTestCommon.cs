using XLua;
#if !XLUA_GENERAL
using UnityEngine;
#endif

using System.Collections.Generic;
using System;
public class LuaEnvSingleton  {
	
	static private LuaEnv instance = null;
	static public LuaEnv Instance
	{
		get
		{
			if(instance == null)
			{
				instance = new LuaEnv();
#if XLUA_GENERAL
                instance.DoString("package.path = package.path..';../Test/UnitTest/xLuaTest/CSharpCallLua/Resources/?.lua.txt;../Test/UnitTest/StreamingAssets/?.lua'");
#endif
            }

            return instance;
		}
	}
}

[LuaCallCSharp]
public class LuaTestCommon
{
#if     UNITY_IOS || UNITY_IPHONE
    public static string resultPath = Application.persistentDataPath + "/";
	public static bool android_platform = false;
#elif   UNITY_ANDROID
    public static string resultPath = "/sdcard/luatest/";
	public static bool android_platform = true;
#elif   UNITY_EDITOR || UNITY_WSA
    public static string resultPath = Application.dataPath + "/xLuaTest/";
	public static bool android_platform = false;
#elif XLUA_GENERAL
    public static string resultPath = ".";
    public static bool android_platform = false;
#endif
    // A file that exists, for the io.open case in TCForTestCSCallLua.
    public static string existingFilePath =
#if XLUA_GENERAL
        "../Test/UnitTest/StreamingAssets/main.lua";
#else
        Application.streamingAssetsPath + "/main.lua";
#endif

    public static bool IsXLuaGeneral()
    {
#if XLUA_GENERAL
        return true;
#else
        return false;
#endif
    }

    // Whether the suite was built with XLUA_LOCK_METATABLES; build xLua with the same defines.
    public static bool LockMetatables()
    {
#if XLUA_LOCK_METATABLES
        return true;
#else
        return false;
#endif
    }

    // The feature defines the suite was built with; build xLua with the same defines.
    public static bool NoSignatureLoader()
    {
#if XLUA_NO_SIGNATURE_LOADER
        return true;
#else
        return false;
#endif
    }

    // True when LuaEnv leaves out the template library: XLUA_NO_TEMPLATE, outside the Editor.
    public static bool NoTemplate()
    {
#if XLUA_NO_TEMPLATE && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    public static bool NoUtil()
    {
#if XLUA_NO_UTIL
        return true;
#else
        return false;
#endif
    }

    // True when perf.profiler is left out: XLUA_NO_PROFILER, outside the Editor and Development Builds.
    public static bool NoProfiler()
    {
#if XLUA_NO_PROFILER && !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    // Whether the xLua assembly declares a type, e.g. "XLua.SignatureLoader".
    public static bool XLuaHasType(string name)
    {
        return typeof(LuaEnv).Assembly.GetType(name) != null;
    }

    // Calls LuaDLL.Lua.luaopen_socket_core, the former import, as a builtin loader in a new
    // LuaEnv: "table" with LuaSocket in the native library, otherwise the Lua error message.
    public static string LegacySocketImport()
    {
        using (var env = new LuaEnv())
        {
            env.AddBuildin("legacy.socket.core", LegacySocketCore);
            return (string)env.DoString("local ok, m = pcall(require, 'legacy.socket.core') "
                + "return ok and type(m.tcp) == 'function' and 'table' or tostring(m)")[0];
        }
    }

    [MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
    static int LegacySocketCore(System.IntPtr L)
    {
        return XLua.LuaDLL.Lua.luaopen_socket_core(L);
    }

    public static bool IsMacPlatform()
	{
#if UNITY_EDITOR
        string os = System.Environment.OSVersion.ToString();
        if (os.Contains("Unix"))
        {
            return true;
        }
        else
        {
            return false;
        }
#else
        return false;
#endif
	}

	public static bool IsIOSPlatform()
	{
#if UNITY_IOS || UNITY_IPHONE
		return true;
#else
		return false;
#endif
	}

    public static void Log(string str)
    {
#if XLUA_GENERAL
        System.Console.WriteLine(str);
#else
        UnityEngine.Debug.Log(str);
#endif
    }
}

#if !XLUA_GENERAL
//For application code, prefer attributes instead of adding configuration here.
public class TestCaseGenConfig
{

    //Configure C# types exposed to Lua, such as standard library, Unity API, or third-party types.
    [LuaCallCSharp]
    public List<Type> LuaCallCSharp
    {
        get
        {
            return new List<Type>()
            {
                typeof(UnityEngine.TextAsset),
            };
        }
    }
}
#endif
