# Android 16's "isn't 16 KB compatible" dialog on our players

Date: 2026-10-29. Coordinator's brief of 2026-10-29, finding 1: on the
`xlua16k` AVD the dialog came up for players built with Unity 6000.3.21f1
and 6000.6.4f1, although every xLua library is 16 KB aligned
([il2cpp-suite.md](il2cpp-suite.md#generic-methods-in-il2cpp-reflection-wrappers-2026-10-29)
left the cause open).

Marks: [C] confirmed here (a run, a log line or source with `file:line`),
[S] from secondary sources, [U] unconfirmed.

## Answer

The dialog did not come from those APKs. **Android 16 keeps the page size
compat flags of every earlier install of a package**, and the AVD had run a
Unity 2021.3.45f2 player of the same package (`org.xluang.suite`) before.
Unity 2021.3.45f2's own `libunity.so` and `libmain.so` have 4 KB `PT_LOAD`
alignment; that install set `PAGE_SIZE_APP_COMPAT_FLAG_ELF_NOT_ALIGNED`,
and each later `adb install -r` of a 16 KB aligned 2022.3, 6000.3 or 6000.6
player kept it. An uninstall clears it. Neither xLua, our build settings nor
Unity 6000.x causes the dialog. [C]

## How Android 16 decides

Source: AOSP `platform/frameworks/base`, branch `android16-release`, read
2026-10-29 (`main` has the same code). [C] by reading.

1. At install, for every library of the device's 64-bit ABI,
   `core/jni/com_android_internal_content_NativeLibraryHelper.cpp`
   `checkAlignment` (line 705) checks the extracted file when
   `extractNativeLibs` is true (line 713), otherwise the stored entry: a
   compressed one is an error, one whose data does not start on a page
   boundary sets `PAGE_SIZE_APP_COMPAT_FLAG_UNCOMPRESSED_LIBS_NOT_ALIGNED`
   (2, line 733). `checkLoadSegmentAlignment` (line 641) sets
   `PAGE_SIZE_APP_COMPAT_FLAG_ELF_NOT_ALIGNED` (4) when a `PT_LOAD`
   segment has `p_align == 0x1000` (line 665). 32-bit libraries are
   skipped (line 645).
2. `services/core/java/com/android/server/pm/ScanPackageUtils.java:425-452`
   stores the result with `PackageSetting.setPageSizeAppCompatFlags`,
   which **ORs** it into the flags it has (`PackageSetting.java:1634`,
   "OR assignment is used here to avoid overriding the mode set by the
   manifest"). An update starts from the old `PackageSetting`, flags included
   (`copyPackageSetting`, `PackageSetting.java:803` and 869), so nothing clears bit 4 or 2 until the
   package is uninstalled.
3. `services/core/java/com/android/server/wm/AppWarnings.java:259-285`
   shows the dialog at each activity start on a 16 KB device when
   `PackageSetting.getPageSizeCompatWarningMessage` (line 1847) returns a
   text: "ELF alignment check failed" for bit 4, "APK alignment" for bit 2,
   both for both. OK in the dialog sets `FLAG_HIDE_PAGE_SIZE_MISMATCH`
   for the package (`PageSizeMismatchDialog.java:62-66`); clearing the
   app's data or uninstalling it removes that again (`AppWarnings.java:302-315`),
   so `pm clear`, which `il2cpp_suite.py run` does after each install,
   brings the dialog back.
4. `isPageSizeAppCompatEnabled` (`PackageSetting.java:1825`) runs the app
   in compat mode for the same bit 4, so a stale flag is not only a
   dialog: the app keeps running in page size compatibility mode.
5. `adb shell dumpsys package PKG` prints the flags as `pageSizeCompat=`
   (`Settings.java:5225`).

## What ran

The `xlua16k` AVD (`google/sdk_gphone16k_arm64/emu64a16k:16/BE2A.250530.026.F3`,
security patch 2025-07-05, `getconf PAGE_SIZE` 16384), headless, under
the emulator lock. `dialog_probe` (scratch script): install, read
`pageSizeCompat`, start the launcher activity, read the dialog's text from
`uiautomator dump` and the logcat.

**The suite package as the 2026-10-29 runs left it** [C]: `dumpsys package
org.xluang.suite` gave `pageSizeCompat=4`, `extractNativeLibs=true`,
`firstInstallTime=2026-10-28 21:47:48`, `lastUpdateTime=2026-10-29
02:52:54` (the 2022.3.62f3 player, the last one installed). Its extracted
libraries (`/data/app/.../lib/arm64`, pulled as root) all have `PT_LOAD`
alignment 0x4000: `libil2cpp.so`, `libmain.so`, `libunity.so`,
`libxlua.so`.

**The logcats of the earlier runs** [C]: the three 6000.3.21f1 suite players
of 2026-10-28 (`il2cpp-suite-2026-10-28/logs/{plain,gcm,strict}.logcat`),
installed before any 2021.3 player of the package, have no
`PageSizeMismatchDialog` line. Every run of 2026-10-29 after the first
2021.3.45f2 install (01:38) has "Showing PageSizeMismatchDialog for package
org.xluang.suite", the 6000.3 and 6000.6 players included, and the dialog
said "ELF alignment check failed".

**Reproduction with one APK** [C]: a 6000.3.21f1 xLua smoke APK
(`ng-5.5+nosig` of `size_report.py`, 2026-10-16, package `org.xluang.smoke`,
`extractNativeLibs=true`, every library compressed and 0x4000 aligned),
re-signed with the AVD's debug key (`a16k`), and the same APK with
`libmain.so` replaced by Unity 2021.3.45f2's (0x1000) (`a4k`):

| Step | Install | `pageSizeCompat` after | Dialog |
|---|---|---:|---|
| s1 | `a16k` after an uninstall | 0 | none |
| s2 | `a4k` with `install -r` | 4 ("Setting page size compat mode PAGE_SIZE_APP_COMPAT_FLAG_ELF_NOT_ALIGNED" in the logcat) | "ELF alignment check failed" |
| s5, s6 | `a4k`, then `a16k`, each with `install -r` and `pm clear` | 4, then **4** (no "Setting page size compat mode" line for `a16k`) | both: "ELF alignment check failed" |
| s7 | `a16k` after an uninstall | 0 | none |

Without `pm clear` (s3) the flag stayed 4 but the dialog did not come back,
because OK had been pressed (`FLAG_HIDE_PAGE_SIZE_MISMATCH`).

**Empty projects against xLua players, per Unity version** [C]. Built
2026-10-29 with `size_report.py run --configs default --variants
baseline,ng-5.5` (`baseline`: the smoke project without xLua; `ng-5.5`:
this tree's xLua with the Lua 5.5.1 arm64-v8a plugin of `make_android_lua55.sh`,
NDK r30, sha256 `cb2109d0...`), release, ARM64, IL2CPP, Minimal stripping,
package `org.xluang.smoke`, the same settings for every editor
(`XLUA_NG_BUILD_SETTINGS` lines). `check_apk_16k.py` and `zipalign -c -P 16
4` (build-tools 36.0.0) on every APK, then each APK installed on the AVD
after an uninstall:

| APK | Bytes | Libraries | `extractNativeLibs` | `zipalign -c -P 16` | Below 16 KB | `pageSizeCompat` | Dialog |
|---|---:|---:|---|---|---|---:|---|
| 2021.3.45f2 empty | 7,872,684 | 3 | true | ok | `libil2cpp.so`, `libmain.so`, `libunity.so` (0x1000) | 4 | ELF alignment |
| 2021.3.45f2 xLua | 9,163,082 | 4 | true | ok | the same three; `libxlua.so` 0x4000 | 4 | ELF alignment |
| 2022.3.62f3 empty | 8,095,491 | 3 | true | ok | none | 0 | none |
| 2022.3.62f3 xLua | 9,422,733 | 4 | true | ok | none | 0 | none |
| 6000.3.21f1 empty | 12,279,060 | 6 | true | ok | none | 0 | none |
| 6000.3.21f1 xLua | 13,748,214 | 7 | true | ok | none | 0 | none |
| 6000.6.4f1 empty | 12,497,340 | 6 | true | ok | none | 0 | none |
| 6000.6.4f1 xLua | 13,917,706 | 7 | true | ok | none | 0 | none |

Every library is compressed in these APKs, so zip alignment does not
apply (`zipalign` reports "OK - compressed"); Android checks the extracted
files. Then the update path with the same APKs: the 2021.3.45f2 xLua APK
after an uninstall (`pageSizeCompat=4`, dialog), the 6000.3.21f1 xLua APK
with `install -r` (**4**, dialog "ELF alignment check failed"), the
6000.6.4f1 empty APK with `install -r` (**4**, dialog), the same 6000.6.4f1
APK after an uninstall (0, no dialog). So an empty 6000.6 project shows the
dialog too when it updates a 2021.3 install, and no player shows it on a
clean install unless Unity's own libraries are 4 KB aligned.

## What changed

- `Test/MobileSmoke/check_apk_16k.py`: Android 16's install-time check for
  an APK or app bundle without a device (every `lib/<abi>/*.so`: `PT_LOAD`
  alignment, offsets congruent modulo 16 KB, and the zip offset of stored
  libraries, as `zipalign -c -P 16` checks it). It names the file that
  fails. `--unity-libs-ok` reports Unity's own libraries as notes.
- `il2cpp_suite.py run` runs it before each install and fails on a
  library that is not Unity's own; after the install it reads
  `pageSizeCompat`, and when the APK is aligned but the flag is set, it
  uninstalls and installs again (and fails if the flag stays).
- `size_report.py report` records the check per Android player
  (`page_16k` in `size-report.json`) and prints a failing library.

## What users must do

- **Use a Unity version whose own libraries are 16 KB aligned.** Measured
  in the editors' Android players (`PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/Libs/arm64-v8a`):
  2021.3.45f2 0x1000 for `libunity.so` and `libmain.so`, and the
  `libil2cpp.so` it links for the project is 0x1000 as well; 2022.3.62f3,
  6000.3.21f1 and 6000.6.4f1 0x4000 for all three. [C] A 2021.3.45f2 player
  always runs in compat mode on a 16 KB device, with or without xLua. Later 2021.3 releases were not checked
  [U]. Google Play requires 16 KB support for new apps and updates that
  target Android 15 and later [S] (developer.android.com/16kb-page-size).
- **Check every native plugin** with `check_apk_16k.py` on the built APK
  or app bundle; xLua's libraries pass, other plugins may not (PuerTS's
  `libPapiLua.so` has 0x1000,
  [benchmark-vs-puerts-zlua.md](benchmark-vs-puerts-zlua.md)).
- **Test on a clean install.** A device that ever had a 4 KB build of the
  package keeps compat mode and the dialog after updates until the app is
  uninstalled. That is the AOSP code above and was seen with `adb install -r`
  on the emulator [C]; whether updates from Google Play on real devices
  keep it the same way was not tested [U], but they go through the same
  `PackageSetting`.

## Not run

A physical 16 KB device, an app bundle installed through bundletool or
Google Play, Android 15 (which has no compat mode), extractNativeLibs=false
players, Unity 6000.0, x86_64 players.
