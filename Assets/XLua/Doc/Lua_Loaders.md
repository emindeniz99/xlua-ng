# Loading Lua sources

`require 'a.b'` asks each searcher in `package.searchers` for module `a.b`
and stops at the first one that returns a chunk. `require` is synchronous: a
loader has to return the source bytes when Lua calls it. Sources that you can
only read asynchronously (StreamingAssets on WebGL, remote AssetBundles,
Addressables) have to be in memory before the first `require` that needs
them.

`LuaEnv` sets up the searchers in this order:

1. `package.preload` (Lua). The native library puts LuaSocket (`socket`,
   `socket.core`) and `mime` (`mime`, `mime.core`) there when it is built
   with them, the default; see
   [build/README.md](../../../build/README.md#luasocket-and-mime).
2. xLua's built-in libraries (`AddBuildin`), such as `CS`.
3. Your loaders, in the order you passed them to `LuaEnv.AddLoader`.
4. `Resources`: `Resources.Load("a/b.lua")`, which finds `Resources/a/b.lua.txt`.
5. The Lua modules embedded in xLua's assembly: `xlua.util` and `perf.profiler`.
   They need no file, so they load with any of the searchers above removed.
   A loader or a `Resources` file with the same module name wins. The
   [`XLUA_NO_UTIL`](XLua_API_EN.md#xlua_no_util) and
   [`XLUA_NO_PROFILER`](XLua_API_EN.md#xlua_no_profiler) defines leave them out.
6. Lua's own file and C library searchers (`package.path`, `package.cpath`).
7. StreamingAssets: `Application.streamingAssetsPath + "/a/b.lua"`, read by
   `StreamingAssetsLoader` ([below](#streamingassets)). Upstream marked it
   obsolete and logged a warning on every file it loaded; xlua-ng keeps it and
   logs nothing. The
   [`XLUA_NO_STREAMING_ASSETS_LOADER`](#xlua_no_streaming_assets_loader)
   define removes it, and the minimal preset leaves it out.

A loader is a `LuaEnv.CustomLoader`:

```csharp
public delegate byte[] CustomLoader(ref string filepath);
```

`filepath` arrives as the `require` argument (`"a.b"`). Return the source (or
bytecode) as `byte[]`, or `null` to let the next loader try. If you set
`filepath` to the file's path, Lua uses it in error messages and debuggers use
it to find the file; see the [FAQ](Faq_EN.md#how-should-customloader-set-filepath-for-debugging).
Read files as bytes, not as text, so bytecode and UTF-8 survive.

`filepath` is also the loader data of the module, as the path is for Lua's own
file searcher. On Lua 5.4 and 5.5, `require` returns it as its second value and
the module's chunk gets it as its second argument:

```lua
-- a/b.lua, loaded by a loader that set filepath to "lua/a/b.lua"
local name, path = ...          -- "a.b", "lua/a/b.lua"
local m, where = require "a.b"  -- where is "lua/a/b.lua" on Lua 5.4 and 5.5
```

xLua's other searchers give their own: `Resources` and StreamingAssets the path
they read (`a/b.lua`), the embedded modules their file name (`xlua/util.lua`)
and `AddBuildin` libraries `":preload:"`, as `package.preload` does. Lua 5.3's
`require` passes the value to the chunk but returns only the module; LuaJIT's
passes only the name. Upstream xLua's searchers gave no loader data
([Tencent/xLua#1182](https://github.com/Tencent/xLua/pull/1182)).

[Examples/15_LuaLoaders](../Examples/15_LuaLoaders/) has a loader for each
place below (`LuaLoaders.cs`) and a scene that registers three of them
(`LuaLoadersExample.cs`). The Addressables code on this page is not part of
it.

## Where the sources can live

| Location | Read with | Synchronous `require` | Notes |
| --- | --- | --- | --- |
| `Resources` | `Resources.Load<TextAsset>` | yes | xLua's default searcher; files need `.lua.txt` or `.bytes` |
| StreamingAssets, Editor, desktop, iOS | `File.ReadAllBytes` | yes | a real directory (`Data/Raw` on iOS) |
| StreamingAssets, Android | `StreamingAssetsLoader` (Android's `AssetManager`), or `UnityWebRequest` | yes | inside the APK; `File` cannot open it. In the OBB of a "Split Application Binary" APK, `WWW` (below) |
| StreamingAssets, WebGL | `UnityWebRequest` | preload only | a URL; the browser cannot block on it |
| `persistentDataPath` | `File.ReadAllBytes` | yes | downloaded files, hot update |
| AssetBundle | `AssetBundle.LoadAsset<TextAsset>` | yes, once the bundle is loaded | load the bundle first |
| Addressables | `Addressables.LoadAssetAsync<TextAsset>` | preload, or `WaitForCompletion` | optional package |

### Resources

The default. Put `a/b.lua.txt` under any `Resources` folder and
`require 'a.b'` finds it with no loader. Unity does not import `.lua` files as
`TextAsset`, which is why the examples use `.lua.txt`; see the
[FAQ](Faq_EN.md#must-lua-source-files-have-a-txt-extension). To keep Lua under a
subfolder such as `Resources/Lua/`, add `LuaLoaders.FromResources("Lua/")`.
Everything under `Resources` ships in the player and cannot change without a
new build.

### StreamingAssets

Unity copies `Assets/StreamingAssets` into the player as plain files.
`XLua.StreamingAssetsLoader` reads them synchronously wherever that is
possible. The legacy preset's StreamingAssets searcher uses it, and a project
without the searcher (the minimal preset, or
[`XLUA_NO_STREAMING_ASSETS_LOADER`](#xlua_no_streaming_assets_loader)) adds
it with one line:

```csharp
luaenv.AddLoader(StreamingAssetsLoader.Load);   // require 'a.b' reads StreamingAssets/a/b.lua
```

`Load` is a `CustomLoader`: it returns `null` when there is no such file and
sets `filepath` to the path below StreamingAssets (`a/b.lua`).
`StreamingAssetsLoader.ReadAllBytes("data/x.json")` reads any other file
there, or returns `null`. Both work on any thread. By platform:

- Editor, Windows, macOS, Linux and iOS: `Application.streamingAssetsPath` is a
  directory, read with `File.ReadAllBytes`.
  `LuaLoaders.FromDirectory(Application.streamingAssetsPath)` does the same.
- Android: the files sit inside the APK, the base module of an AAB or an
  install-time asset pack, and `streamingAssetsPath` is a `jar:file://` URL
  that `File` cannot open. `StreamingAssetsLoader` reads them through Android's
  `AssetManager` over `AndroidJNI`, which every Android player has; the
  project needs the AndroidJNI built-in package (`com.unity.modules.androidjni`,
  in the Hub templates; xLua's UPM package depends on it). Before
  2026-10-29 xLua's searcher read them with `UnityEngine.WWW`: it kept the
  UnityWebRequest and TLS engine modules in every legacy Android player
  (573,164 bytes of APK), spun until each request finished and then slept
  50 ms.
- Android with "Split Application Binary" (an APK with an OBB file; Google
  Play no longer accepts OBB files for new apps): Unity puts StreamingAssets
  into the OBB, which the `AssetManager` does not see, and
  `Application.dataPath` names the OBB. There `StreamingAssetsLoader` reads with
  `UnityEngine.WWW`, as xLua did before, and a build of an APK with the
  setting on keeps `WWW` when the StreamingAssets searcher is compiled in
  (`StreamingAssetsObbLinkXml`, Editor). A project without the searcher that
  calls `StreamingAssetsLoader` in such a build keeps `WWW` with a link.xml:
  `<assembly fullname="UnityEngine.UnityWebRequestWWWModule"><type fullname="UnityEngine.WWW" preserve="all"/></assembly>`.
  Without it `Load` throws an exception that says so. An AAB with the setting
  puts StreamingAssets into an install-time asset pack, which the
  `AssetManager` reads.
- WebGL: `streamingAssetsPath` is a URL on the server. Only `UnityWebRequest`
  can read it, and only asynchronously, which a synchronous `require` cannot
  wait for. The searcher adds "StreamingAssets cannot be read synchronously on
  WebGL" to `require`'s error message, and `StreamingAssetsLoader.Load` and
  `ReadAllBytes` throw `NotSupportedException`. Upstream's searcher looked for
  a file with `File.Exists` there and silently found nothing.

For WebGL, read the modules before the first `require` with
`LuaLoaders.PreloadStreamingAssets` and serve them with
`LuaLoaders.FromDictionary`, or ship the Lua in `Resources`. You cannot list a
folder inside a player, so write the module list at build time (a text file
next to the sources works):

```csharp
var sources = new Dictionary<string, byte[]>();
yield return LuaLoaders.PreloadStreamingAssets(moduleNames, sources);
luaenv.AddLoader(LuaLoaders.FromDictionary(sources));
luaenv.DoString("require 'main'");
```

### persistentDataPath and hot update

Files your game downloads go to `Application.persistentDataPath`, which you
read with `File`. Add that loader first, so a
downloaded `a/b.lua` replaces the one shipped in `Resources`:

```csharp
var root = Path.Combine(Application.persistentDataPath, "lua");
luaenv.AddLoader(LuaLoaders.FromDirectory(root));
```

xLua does not download anything. Fetch the files with `UnityWebRequest`
(`DownloadHandlerFile` writes them straight to disk) before you create the
`LuaEnv` or before the next `require`. If the files come from a server, sign
them and wrap the loader with [`SignatureLoader`](signature.md); a loader that
reads writable storage runs whatever it finds there.

### AssetBundle

Mark the `.lua.txt` (or `.bytes`) files for a bundle, load the bundle
(`AssetBundle.LoadFromFileAsync`, or from a download), then register
`LuaLoaders.FromAssetBundle(bundle, "Assets/Lua/")`. Module `a.b` then maps to
the asset `Assets/Lua/a/b.lua.txt`. `LoadAsset` is synchronous once the bundle
is in memory. Keep the bundle loaded while modules may still be required, or
copy the bytes into a dictionary and unload it.

### Addressables

xLua does not depend on the Addressables package, and the sample folder does
not use it, so projects without Addressables compile. The code below needs
`com.unity.addressables` 1.17 or later (for `WaitForCompletion`); put it in
your own scripts. It has not been compiled or run as part of xlua-ng's tests.

Give each Lua `TextAsset` its module name as address (`a.b`) and a label such
as `lua`. Then either preload everything with the label at startup:

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

public static class AddressablesLua
{
    // Reads every TextAsset with the label into sources, keyed by address.
    public static IEnumerator Preload(string label, IDictionary<string, byte[]> sources)
    {
        var locations = Addressables.LoadResourceLocationsAsync(label, typeof(TextAsset));
        yield return locations;
        foreach (IResourceLocation location in locations.Result)
        {
            var asset = Addressables.LoadAssetAsync<TextAsset>(location);
            yield return asset;
            sources[location.PrimaryKey] = asset.Result.bytes;
            Addressables.Release(asset);
        }
        Addressables.Release(locations);
    }
}

// At startup, before the first require:
//   var sources = new Dictionary<string, byte[]>();
//   yield return AddressablesLua.Preload("lua", sources);
//   luaenv.AddLoader(LuaLoaders.FromDictionary(sources));
```

or load each module when Lua asks for it and block on the handle:

```csharp
luaenv.AddLoader((ref string filepath) =>
{
    var locations = Addressables.LoadResourceLocationsAsync(filepath, typeof(TextAsset)).WaitForCompletion();
    if (locations == null || locations.Count == 0)
    {
        return null; // not an Addressables module; let the next loader try
    }
    var handle = Addressables.LoadAssetAsync<TextAsset>(locations[0]);
    var bytes = handle.WaitForCompletion().bytes;
    Addressables.Release(handle);
    return bytes;
});
```

`WaitForCompletion` stalls the frame while a remote bundle downloads, and
WebGL does not support it. Preloading by label suits WebGL and remote content.

## XLUA_NO_STREAMING_ASSETS_LOADER

Add `XLUA_NO_STREAMING_ASSETS_LOADER` to the scripting define symbols (Player
Settings, or `-define:` in `Assets/csc.rsp`) to leave out xLua's
StreamingAssets searcher. `LuaEnv` then does not register
`StaticLuaCallbacks.LoadFromStreamingAssetsPath`. The minimal preset defines
it. Without the define nothing changes.

What you gain: since 2026-10-29 little. The searcher reads Android's
StreamingAssets through the `AssetManager` (`StreamingAssetsLoader`), so it no
longer keeps the UnityWebRequest engine modules in Android players. In a
release ARM64 APK (Unity 6000.3.21f1, Lua 5.5.1, Minimal stripping) the
define now saves 1,356 bytes, and the change saved 559,920 bytes of every
legacy Android APK ([build/SIZE.md](../../../build/SIZE.md#the-streamingassets-searcher-through-the-assetmanager-2026-10-29)).
Until then the define saved 569,964 bytes of a release ARM64 APK, the
`UnityEngine.WWW` that the searcher used on Android
([build/README.md](../../../build/README.md#player-size-2026-10-16)).

What you lose: `require` no longer looks in StreamingAssets. If your Lua files
are there, add `luaenv.AddLoader(StreamingAssetsLoader.Load)`, or another
loader from the [StreamingAssets](#streamingassets) section.
