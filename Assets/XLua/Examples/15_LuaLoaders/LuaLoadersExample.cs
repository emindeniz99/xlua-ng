// Registers loaders for a downloaded file, preloaded StreamingAssets sources and a
// Resources subfolder, then requires one module from each. See LuaLoaders.cs.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using XLua;

namespace XLuaTest
{
    public class LuaLoadersExample : MonoBehaviour
    {
        LuaEnv luaenv;

        IEnumerator Start()
        {
            // Stand-in for a hot update: a file a download would leave in persistentDataPath.
            var downloads = Path.Combine(Application.persistentDataPath, "xlua_loaders_example");
            Directory.CreateDirectory(downloads);
            File.WriteAllText(Path.Combine(downloads, "loaders_downloaded.lua"),
                "return 'loaders_downloaded: read from persistentDataPath'");

            // Android and WebGL can only read StreamingAssets asynchronously, so read the
            // modules first. This example ships none in StreamingAssets; list yours here.
            var preloaded = new Dictionary<string, byte[]>();
            yield return LuaLoaders.PreloadStreamingAssets(new string[0], preloaded);

            luaenv = new LuaEnv();
            // require tries custom loaders in the order they were added, before xLua's
            // Resources searcher, so downloaded files replace the ones shipped in the player.
            luaenv.AddLoader(LuaLoaders.FromDirectory(downloads));
            luaenv.AddLoader(LuaLoaders.FromDictionary(preloaded));
            luaenv.AddLoader(LuaLoaders.FromResources("XLuaLoadersExample/"));
            luaenv.DoString(@"
                print(require 'loaders_downloaded')
                print(require 'loaders_resources')
            ");
        }

        void Update()
        {
            if (luaenv != null)
            {
                luaenv.Tick();
            }
        }

        void OnDestroy()
        {
            if (luaenv != null)
            {
                luaenv.Dispose();
            }
        }
    }
}
