# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md).

## Verification that needs real hardware or licences

- Unity editor matrix (`unity-matrix.yml`): add the Unity secrets and run it;
  the commands are in [.github/CI.md](.github/CI.md#manual-run-checklist).
- Device runs with the current sources: an Android APK on a 16 KB page-size
  device, an iOS build, and a Unity WebGL player in a browser.
- LuaCATS generator: check the LuaJIT output. (The local EditMode test, bridge
  smoke plus LuaCATS generation, passes in Unity 2021.3.45f2, 2022.3.62f3,
  6000.3.21f1 and 6000.6.4f1 on macOS arm64. 2021.3 and 2022.3 need Rosetta 2
  there, because their Package Manager server is x86_64 only.)

## Dependencies and tooling

- Replace the `ltest` Lua test framework with
  [luaunit](https://github.com/bluebird75/luaunit). `ltest` (2013) has no
  upstream and no licence file. luaunit is MIT-licensed, maintained, and a
  single file, so it still runs inside Unity. The 277 managed test cases come
  from Tencent/xLua (2017) and need their assertions ported.
- Update the OpenHarmony native SDK (4.1) and verify its download with a
  checksum.
- Android builds of Lua do not define `LUA_USE_POSIX`, so `os.tmpname` uses
  the deprecated `tmpnam` (a compiler warning on bionic). Decide whether to
  build Lua with `LUA_USE_LINUX` on Android.
- C23: every C file builds as C17 (gnu17). Unity 2021.3's Emscripten 2.0.19
  (clang 13) cannot compile C23, so moving to it needs a support decision.

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Publish native plugins from `publish.yml` as release assets.
