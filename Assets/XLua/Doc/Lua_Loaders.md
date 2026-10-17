# Loading Lua sources

`require 'a.b'` asks each searcher in `package.searchers` for module `a.b`
and stops at the first one that returns a chunk. `require` is synchronous: a
loader has to return the source bytes when Lua calls it. Sources that you can
only read asynchronously (StreamingAssets on Android and WebGL, remote
AssetBundles, Addressables) have to be in memory before the first `require`
that needs them.

`LuaEnv` sets up the searchers in this order:

1. `package.preload` (Lua).
2. xLua's built-in libraries (`AddBuildin`), such as `socket.core`.
3. Your loaders, in the order you passed them to `LuaEnv.AddLoader`.
4. `Resources`: `Resources.Load("a/b.lua")`, which finds `Resources/a/b.lua.txt`.
5. The Lua modules embedded in xLua's assembly: `xlua.util` and `perf.profiler`.
   They need no file, so they load with any of the searchers above removed.
   A loader or a `Resources` file with the same module name wins. The
   [`XLUA_NO_UTIL`](XLua_API_EN.md#xlua_no_util) and
   [`XLUA_NO_PROFILER`](XLua_API_EN.md#xlua_no_profiler) defines leave them out.
6. Lua's own file and C library searchers (`package.path`, `package.cpath`).
7. StreamingAssets: `Application.streamingAssetsPath + "/a/b.lua"`. Upstream
   marks it obsolete and logs a warning on every file it loads. The
   [`XLUA_NO_STREAMING_ASSETS_LOADER`](#xlua_no_streaming_assets_loader)
   define removes it.

A loader is a `LuaEnv.CustomLoader`:

```csharp
public delegate byte[] CustomLoader(ref string filepath);
```

`filepath` arrives as the `require` argument (`"a.b"`). Return the source (or
bytecode) as `byte[]`, or `null` to let the next loader try. If you set
`filepath` to the file's path, Lua uses it in error messages and debuggers use
it to find the file; see the [FAQ](Faq_EN.md#how-should-customloader-set-filepath-for-debugging).
Read files as bytes, not as text, so bytecode and UTF-8 survive.

[Examples/15_LuaLoaders](../Examples/15_LuaLoaders/) has a loader for each
place below (`LuaLoaders.cs`) and a scene that registers three of them
(`LuaLoadersExample.cs`). The Addressables code on this page is not part of
it.

## Where the sources can live

| Location | Read with | Synchronous `require` | Notes |
| --- | --- | --- | --- |
| `Resources` | `Resources.Load<TextAsset>` | yes | xLua's default searcher; files need `.lua.txt` or `.bytes` |
| StreamingAssets, Editor, desktop, iOS | `File.ReadAllBytes` | yes | a real directory (`Data/Raw` on iOS) |
| StreamingAssets, Android | `UnityWebRequest`, or `AssetManager` through Java | preload, or block | inside the APK; `File` cannot open it |
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

Unity copies `Assets/StreamingAssets` into the player as plain files. How you
read them depends on the platform:

- Editor, Windows, macOS, Linux and iOS: `Application.streamingAssetsPath` is a
  directory. `LuaLoaders.FromDirectory(Application.streamingAssetsPath)` reads
  it with `File`.
- Android: the files sit inside the APK (or an asset pack of the AAB), and
  `streamingAssetsPath` is a `jar:file://` URL. Read them with
  `UnityWebRequest` or through Android's `AssetManager` from a Java or Kotlin
  plugin. xLua's own StreamingAssets searcher uses `UnityEngine.WWW` here and
  blocks the main thread until the read finishes.
- WebGL: `streamingAssetsPath` is a URL on the server. Only `UnityWebRequest`
  can read it, and only asynchronously. xLua's StreamingAssets searcher calls
  `File.Exists` there and finds nothing.

For Android and WebGL, read the modules before the first `require` with
`LuaLoaders.PreloadStreamingAssets` and serve them with
`LuaLoaders.FromDictionary`. You cannot list a folder inside an APK, so write
the module list at build time (a text file next to the sources works):

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
`StaticLuaCallbacks.LoadFromStreamingAssetsPath`, and the method, with its
`UnityEngine.WWW` code for Android, is not compiled. Without the define nothing
changes.

What you gain: on Android, that `WWW` reference is the only thing in xLua that
makes Unity's engine code stripping keep the UnityWebRequest modules in
`libunity.so`. In a release ARM64 APK built with Unity 6000.3.21f1 and Lua
5.5.1, the define saved 569,964 bytes (`libunity.so` 539,624 bytes compressed in the APK, 1,090,800 uncompressed; IL2CPP code 22,905; metadata 7,433). If your project
calls `UnityWebRequest` itself, Unity keeps those modules anyway and the define
saves 15,116 bytes. iOS and WebGL builds have no `WWW` reference to drop.
The numbers and the method are in [build/README.md](../../../build/README.md#player-size-2026-10-16).

What you lose: `require` no longer looks in StreamingAssets. If your Lua files
are there, add a loader from the [StreamingAssets](#streamingassets) section.
