# 32-bit targets in 2026

Researched 2026-10-17. Confidence: **[C]** confirmed from a primary source,
**[S]** secondary, **[U]** unconfirmed or inferred.

## Summary

| Platform | State | xlua-ng today | Recommendation |
|---|---|---|---|
| Android armeabi-v7a | Google Play still serves 32-bit devices (Android Go, TV, old phones) but only alongside 64-bit; 64-bit-only devices since Pixel 7, and Arm's 2023+ cores are AArch64-only. No reliable share figure; estimated low single-digit percent and falling. **[C/S/U]** | built | Keep for now; decide with real Play Console "installs by ABI" data |
| Android x86 | Removed from Unity entirely **[C]** | built by default (`build/android_common.sh:18`) | Drop |
| Android x86_64 | Removed in Unity 6.5 (Magic Leap 2 only) **[C]**; still buildable in 2021.3–6000.3, used by emulators and ChromeOS | built by default | Keep while supported Unity versions build it; drop once the minimum Unity is 6.5 |
| iOS | 64-bit only since iOS 11; Unity 6 needs A8+ **[C]** | 64-bit only | Nothing to do |
| Windows x86 | Steam survey shows no 32-bit Windows row (under 0.1%); Windows 11 is 64-bit only; Unity 6.6 still offers "Intel 32-bit" **[C]** | `make_win32_*`, `make_win_lua5*` | Drop for Lua 5.x; keep LuaJIT x86 only on request |
| Windows ARM64 | A live Unity target **[C]** | **no build** | Consider adding |
| Linux x86 | No Unity Linux x86 player since 2019.2 **[S]**; Unity 6 is x64 only **[C]** | `make_linux32_*` | Drop |
| OpenHarmony armeabi-v7a | Tuanjie manuals still list ARMv7, but DevEco reportedly no longer compiles armeabi-v7a and HarmonyOS NEXT phones are arm64 **[C/S]** | built | Drop unless asked; keep arm64 and x86_64 (emulator) |
| WebGL | wasm32 default; Unity 6.6 adds WebAssembly64 for heaps above 4 GB **[C]** | compiled in the player | Keep wasm32; wasm64 on request |
| Consoles | All 64-bit | `make_nx64_*` | Nothing to do |

## Sources

- Google Play 64-bit requirement: <https://developer.android.com/google/play/requirements/64-bit>
- 64-bit-only devices (Pixel 7): <https://android-developers.googleblog.com/2022/10/64-bit-only-devices.html>
- Google TV / Wear OS 64-bit (2026), "no changes to 32-bit support":
  <https://android-developers.googleblog.com/2025/08/64-bit-app-compatibility-for-google-tv-android-tv.html>,
  <https://android-developers.googleblog.com/2026/04/get-your-wear-os-apps-ready-for-64-bit-requirement.html>
- Unity 6 system requirements: <https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html>
- Unity 6.5 breaking changes (x86_64 Android removed, minimum API 26):
  <https://discussions.unity.com/t/planned-breaking-changes-in-unity-6-5-updated-2026-03-27/1694205>
- Unity ChromeOS and x86 Android: <https://discussions.unity.com/t/the-future-of-chromebook-and-chromeos-runtime-support/943516>
- Steam Hardware Survey: <https://store.steampowered.com/hwsurvey/>
- Unity Windows architectures (6.6): <https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/windows/standalone-binaries>
- Tuanjie system requirements: <https://docs.unity.cn/cn/tuanjiemanual/Manual/system-requirements.html>
- Unity WebAssembly64: <https://docs.unity3d.com/Manual/wasm-64bit-support.html>

## Open points

- The Android 32-bit share needs real customer data (Play Console, installs by ABI).
- The default ABIs of a new Unity 6 Android project were not confirmed.
- Whether Tuanjie really builds armeabi-v7a for HarmonyOS NEXT phones is
  unconfirmed beyond its manual tables.
