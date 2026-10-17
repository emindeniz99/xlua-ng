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
