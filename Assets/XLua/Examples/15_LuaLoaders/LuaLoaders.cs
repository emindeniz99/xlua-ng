// Sample loaders for LuaEnv.AddLoader, one per place Lua sources can live.
// Assets/XLua/Doc/Lua_Loaders.md explains when to use which. require is
// synchronous: a loader must return the bytes at once, so sources that only
// arrive asynchronously (Android StreamingAssets, WebGL, Addressables) are
// read before the first require and served from memory with FromDictionary.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using XLua;

namespace XLuaTest
{
    public static class LuaLoaders
    {
        // require 'a.b' asks for module "a.b"; the samples store it as a/b plus an extension.
        public static string ModulePath(string module, string extension)
        {
            return module.Replace('.', '/') + extension;
        }

        // Files in a directory: Application.persistentDataPath (downloaded files, hot
        // update), or Application.streamingAssetsPath where it is a real directory
        // (Editor, Windows, macOS, Linux and iOS players; not Android or WebGL).
        public static LuaEnv.CustomLoader FromDirectory(string root, string extension = ".lua")
        {
            return (ref string filepath) =>
            {
                var path = Path.Combine(root, ModulePath(filepath, extension));
                if (!File.Exists(path))
                {
                    return null;
                }
                filepath = path; // chunk name for error messages and debuggers
                return File.ReadAllBytes(path);
            };
        }

        // TextAssets under a Resources folder: prefix "Lua/" finds module "a.b" in
        // Resources/Lua/a/b.lua.txt. xLua's own Resources searcher covers the root only.
        public static LuaEnv.CustomLoader FromResources(string prefix, string extension = ".lua")
        {
            return (ref string filepath) =>
            {
                var asset = Resources.Load<TextAsset>(prefix + ModulePath(filepath, extension));
                if (asset == null)
                {
                    return null;
                }
                var bytes = asset.bytes;
                Resources.UnloadAsset(asset);
                return bytes;
            };
        }

        // TextAssets in a loaded AssetBundle, by asset path: prefix "Assets/Lua/" finds
        // module "a.b" in Assets/Lua/a/b.lua.txt. Load the bundle (asynchronously if you
        // like) before the first require, and keep it loaded while modules may be required.
        public static LuaEnv.CustomLoader FromAssetBundle(AssetBundle bundle, string prefix, string extension = ".lua.txt")
        {
            return (ref string filepath) =>
            {
                var asset = bundle.LoadAsset<TextAsset>(prefix + ModulePath(filepath, extension));
                if (asset == null)
                {
                    return null;
                }
                var bytes = asset.bytes;
                Resources.UnloadAsset(asset);
                return bytes;
            };
        }

        // Sources already in memory, keyed by module name ("a.b"): preloaded from
        // StreamingAssets with PreloadStreamingAssets, or from Addressables.
        public static LuaEnv.CustomLoader FromDictionary(IDictionary<string, byte[]> sources)
        {
            return (ref string filepath) =>
            {
                byte[] bytes;
                return sources.TryGetValue(filepath, out bytes) ? bytes : null;
            };
        }

        // Reads modules from StreamingAssets into sources with UnityWebRequest, which works
        // on every platform: on Android the folder is inside the APK and on WebGL it is a
        // URL, so File cannot read it there. A folder inside the APK cannot be listed, so
        // the caller names the modules (for example from a list file written at build
        // time). Run it as a coroutine and wait for it before the first require.
        public static IEnumerator PreloadStreamingAssets(IEnumerable<string> modules, IDictionary<string, byte[]> sources,
            string extension = ".lua")
        {
            foreach (var module in modules)
            {
                var url = Application.streamingAssetsPath + "/" + ModulePath(module, extension);
                if (!url.Contains("://"))
                {
                    url = "file://" + url; // Editor, desktop and iOS give a plain path
                }
                using (var request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        sources[module] = request.downloadHandler.data;
                    }
                    else
                    {
                        Debug.LogWarning("Lua module " + module + " not read from " + url + ": " + request.error);
                    }
                }
            }
        }
    }
}
