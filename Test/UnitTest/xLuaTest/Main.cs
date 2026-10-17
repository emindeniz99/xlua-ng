#if !XLUA_GENERAL
using UnityEngine;
using System.Collections;
using XLua;

public class Main : MonoBehaviour {
	LuaEnv luaenv;
	// Use this for initialization
	void Start () {
		luaenv = LuaEnvSingleton.Instance;
#if XLUA_NO_STREAMING_ASSETS_LOADER
		// The suite's Lua files are in StreamingAssets; without xLua's StreamingAssets
		// searcher, read them directly (editor and desktop players, not Android or WebGL).
		luaenv.AddLoader((ref string filepath) => {
			var path = System.IO.Path.Combine(Application.streamingAssetsPath, filepath.Replace('.', '/') + ".lua");
			if (!System.IO.File.Exists(path)) return null;
			filepath = path;
			return System.IO.File.ReadAllBytes(path);
		});
#endif
		luaenv.DoString ("require 'main'");
	}
	
	// Update is called once per frame
	void Update () {
		luaenv.GC ();
	}
}
#endif