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

- Update the OpenHarmony native SDK (4.1) and verify its download with a
  checksum.

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Publish native plugins from `publish.yml` as release assets.
