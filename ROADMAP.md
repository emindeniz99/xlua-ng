# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md).

## Verification that needs real hardware or licences

- Unity editor matrix (`unity-matrix.yml`): add the Unity secrets and run it;
  the commands are in [.github/CI.md](.github/CI.md#manual-run-checklist).
  (The local EditMode test, bridge smoke plus LuaCATS generation, passes in
  Unity 2021.3.45f2, 2022.3.62f3, 6000.3.21f1 and 6000.6.4f1 on macOS arm64.
  2021.3 and 2022.3 need Rosetta 2 there, because their Package Manager server
  is x86_64 only.)
- Device runs with the current sources: an Android APK on a 16 KB page-size
  device, an iOS build, and a Unity WebGL player in a browser.

## Dependencies and tooling

- OpenHarmony: load the plugin on a device or the emulator, and run
  armeabi-v7a somewhere (`Test/Native/ohos_check.sh` covers x86_64 and
  arm64-v8a with a static build). The x86_64 static binary crashes under
  Rosetta on macOS, so it is only checked on the x86_64 CI runner.

## Editor tests and LuaCATS

- Unity editor tests beyond "the files exist": generate wrappers in the editor
  and run the bridge through them, and check LuaCATS output for real Unity
  types (`Vector3`, `GameObject`, `List<T>`) with lua-language-server.
- LuaCATS: generic method inference, an opt-in strict `CS.*` mode (the default
  stays `---@field [string] any`, which matches reflection), configurable file
  names, a `.luarc.json` that matches the plugin's runtime, and "Clear and
  Generate" plus "Validate Setup" menu commands. Each needs golden files and
  lua-language-server cases. The generator already shares Generator's member
  filters, so no separate export model is planned.
- LuaCATS on LuaJIT built with `UINT_ESPECIALLY=ON`: ulong is then a separate
  unsigned kind that `xlua.Int64` does not model. Delegate returns and long
  indexer keys still use the read type.
- LuaJIT: every `LuaTable.Get`/`Set` allocates a 48-byte C closure, because
  `xlua_pgettable` and `xlua_psettable` in `build/xlua.c` call
  `lua_pushcfunction` per call (Lua 5.2+ uses light C functions). Found by
  `Test/Native/benchmark.py`; cache the function once per state.
- Audit deprecated Lua C API use (compatibility macros) for each runtime.
- WebGL player check in Unity 2021.3 and 2022.3 (the WebGL module is only
  installed for 6000.6 locally).

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Protect `main` (no force pushes or deletion) and release tags.
- Ship a versioned UPM package and the `Assets` layout from one source, keeping
  the Unity `.meta` files and native import settings.
- Publish native plugins from `publish.yml`: prerelease releases, one archive
  per runtime, `sbom.cdx.json` next to `SHA256SUMS.txt`, and a rebuild that
  checks the archives are reproducible.

## Optional features, after the above is stable

- A JSON module, evaluating yyjson: exact 64-bit integers, a null sentinel,
  strict UTF-8, depth and size limits. Behind a build option, so apps that do
  not use it do not pay for its size.
- A signed, versioned script loader built on `SignatureLoader`: a narrow C#
  capability API, a script cache, rollback and error reporting, and a
  `LuaComputed<T>` with dependency invalidation. A normal `LuaEnv` is not a
  sandbox (see [Security.md](Assets/XLua/Doc/Security.md)).
- A Teal `.d.tl` generator that uses the generator's member filters directly,
  not LuaCATS output.
- APK, IPA and WebGL size measurements once player builds run in CI.
