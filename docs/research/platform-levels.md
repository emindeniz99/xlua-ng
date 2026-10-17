# Platform levels: Android API, bionic loader features, HarmonyOS

Researched 2026-10-17. Confidence: **[C]** confirmed from a primary source,
**[S]** secondary, **[U]** unconfirmed.

## Unity's own Android minimum

From each version's "Android requirements and compatibility" manual page. **[C]**

| Unity | Minimum Android |
|---|---|
| 2021.3 LTS, 2022.3 LTS | 5.1, API 22 |
| 6000.0 LTS, 6000.1, 6000.2 | 6.0, API 23 |
| 6000.3, 6000.4 | 7.1, API 25 |
| 6000.5 | 8.0, API 26 |

## Google Play

- Play sets a target API, not a minimum: since 2026-08-31 new apps and
  updates must target API 36
  ([requirements](https://developer.android.com/google/play/requirements/target-sdk)). **[C]**
- Google Play services v24.28+ requires API 23 (July 2024)
  ([apilevels.com](https://apilevels.com)). **[C]**
- Cumulative device share from apilevels.com (Statcounter, April 2026), as
  the share of devices at or above each API level:
  - API 22+: 98.2%
  - API 23+: 98.0%
  - API 24+: not listed separately
  - API 26+: 96.1%
  - API 28+: 93.5%

  **[C]**

## Bionic loader features

Source: [android-changes-for-ndk-developers.md](https://android.googlesource.com/platform/bionic/+/main/docs/android-changes-for-ndk-developers.md). **[C]**

| Feature | First API |
|---|---|
| `DT_GNU_HASH` (`--hash-style=gnu`) | 23 |
| Android packed relocations, APS2 (`--pack-dyn-relocs=android`) | 23 |
| RELR with Android-private tags (`--use-android-relr-tags`) | 28 |
| RELR with official `DT_RELR` | 30 |
| Text relocations refused | 23 |

## Decision

The minimum Android API is **23 for every preset**, legacy included. At 23,
APS2 and gnu-hash can be used everywhere. Unity 6000.0 and later need 23 or
more anyway. Unity 2021.3 and 2022.3 users who build with minSdk 22 lose about
0.2% of devices, and Play services already requires 23. RELR (28+) is not
used. Builds with minSdk 22 must be documented as unsupported.

## HarmonyOS / OpenHarmony

- **API levels:** HarmonyOS NEXT starts at API 12. 6.0 is API 20, 6.1 is
  API 23 and 24, and 7.0 is listed as 26.0.0
  ([Wikipedia](https://en.wikipedia.org/wiki/HarmonyOS_version_history)). **[S]**
- **Device share, 2026-10-07**, from Huawei figures reported by
  [IT之家](https://www.ithome.com/1/010/727.htm). **[S]**
  - 7.0: 18.98%
  - 6.1.1: 75.02%
  - 6.1.0: 4.11%
  - 6.0.x: 1.62%
  - 5.x: 0.27%
- **Stability:** a new API level still ships every two to four months, so the
  API is not stable. The installed base is stable: about 98% of devices run
  API 23 or newer.
- **Tuanjie** (Unity China) supports OpenHarmony 5.0.1 (API 13) and later
  ([manual](https://docs.unity.cn/cn/tuanjiemanual/Manual/openharmony-requirements-and-compatibility.html)). **[C]**
- xlua-ng builds OHOS with SDK 7.0 (API 26). See
  [native builds](../../build/README.md).

## Selecting native plugin variants in Unity

- **Define Constraints:** these apply to managed plugins only. Unity ignores
  them on native `.so` and `.a` files and closed the bug as By Design
  ([Plugin Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/plug-in-inspector.html)). **[C]**
  - With two native variants, both are included and the build fails with
    "same name and architecture". This was measured; see
    [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md).
- **`PluginImporter.SetIncludeInBuildDelegate`:** this works. It chose the
  right variant by scripting define and by Development Build in 6000.3.21f1
  and 2021.3.45f2. It reads the Player Settings defines, not `csc.rsp`.
- **Source plugins:** C and C++ files are compiled into the player by IL2CPP
  and are imported with `__Internal`. IL2CPP's `-DFORCE_PINVOKE_xlua_INTERNAL`
  gives the same result with no C# change. Measurements are in
  [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md).
