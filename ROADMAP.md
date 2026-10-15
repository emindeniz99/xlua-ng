# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md).

## Verification that needs real hardware or licences

- Run `native-build.yml` once: MSVC (Windows), macOS and iOS builds have only
  been checked locally or by cross-compiling.
- Unity editor matrix (`unity-matrix.yml`): add the `UNITY_LICENSE`,
  `UNITY_EMAIL` and `UNITY_PASSWORD` secrets and run it.
- Device runs with the current sources: an Android APK on a 16 KB page-size
  device, an iOS build, and a Unity WebGL player in a browser.
- LuaCATS generator: run the menu item in a Unity editor, and check the LuaJIT
  output.

## Dependencies and tooling

- Replace the `ltest` Lua test framework with
  [luaunit](https://github.com/bluebird75/luaunit). `ltest` (2013) has no
  upstream and no licence file. luaunit is MIT-licensed, maintained, and a
  single file, so it still runs inside Unity. The 277 managed test cases come
  from Tencent/xLua (2017) and need their assertions ported.
- Update Mono.Cecil 0.9.6 to 0.11.6 for the hotfix injector, and add a CI job
  that injects and runs a hotfixed assembly.
- Update the OpenHarmony native SDK (4.1) and verify its download with a
  checksum.
- Keep C sources at C99, the level Lua itself requires. Every supported
  compiler, including Unity 2021.3's Emscripten 2.0.19 (clang 13), accepts
  C11 and C17; C23 needs a support decision first.

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Publish native plugins from `publish.yml` as release assets.
