# The general suite in an IL2CPP player

Date: 2026-10-28. Backlog item "Run the test suite inside an IL2CPP player,
automated" (2026-10-17 status review).

Marks: [C] confirmed here (a run, a log line or source with `file:line`),
[S] from secondary sources, [U] unconfirmed.

## What ran

`Test/UnityMatrix/il2cpp_suite.py` builds an Android player that runs the
same 522 luaunit cases as `Test/Native/run_managed.py` (`Test/UnitTest`), with
the C# fixtures compiled into Assembly-CSharp and bound by the source
generator, and compares the per-case results with a host log of the same
configuration ([Test/README.md](../../Test/README.md#the-general-suite-in-an-il2cpp-player)).

- Source: `a9e5425e` (main) with this branch's changes.
- Unity 6000.3.21f1, Android ARM64, IL2CPP, release (no Development flag),
  Minimal managed stripping, code generation OptimizeSpeed, compiler
  configuration Release.
- Lua 5.5.1 plugins built from the tree with `build/make_android_lua55.sh`
  (NDK r30), with and without `XLUA_VARIANT=minimal XLUA_PRESET=minimal`.
- The `xlua16k` AVD (Android 16, API 36, 16 KB pages), headless.
- Host baselines: Mono 6.14.1 on macOS arm64, `run_managed.py --source-gen
  --source-gen-minimize` with the legacy library and `--define
  XLUA_PRESET_MINIMAL --source-gen` with the minimal one, from the same tree.

| Configuration | Defines | Player | Mono | Per-case differences | APK (bytes) | Suite time in the player |
|---|---|---:|---:|---|---:|---|
| plain | none (legacy preset, the generator) | 522/522 in both modes | 522/522 | none | 15,445,466 | 2 × 10.4 s |
| gcm | `GEN_CODE_MINIMIZE` | 522/522 | 522/522 | none | 15,278,326 | 2 × 10.4 s |
| strict | `XLUA_PRESET_MINIMAL` (no reflection, minimal plugin) | 522/522, 218 through `needs_reflection.lua` | 522/522, 220 | 8 per mode, all cases that apply to one build only (below) | 14,956,086 | 2 × 4.8 s |

The host takes 3.7 to 4.3 s per mode. [C]

After merging main at `9f2e51d2` (a lock-free translator lookup and a
523rd case) the three players passed 523/523 again, with the same
differences, and the host passed every pass (APKs 15,449,014, 15,282,514
and 14,962,382 bytes). [C]

## Differences from Mono, and what was done

The first run of plain and gcm gave 498/522: 24 cases per mode failed, the
same in both. The first strict run gave 506/522. None was a bridge bug:

1. **Fixtures only the host compiled** [C]. `InParamsForLuaCallCS.cs:150`
   (`in` and `ref readonly` parameters) and `UpstreamForLuaCallCS.cs:135`
   (a pointer return) are `#if XLUA_GENERAL`, because mcs needs the
   attribute spellings that Roslyn rejects; 16 InParams cases and
   `CasePointerReturn` failed with "No such type". `CasePointerReturn`'s guard
   (`if not T then return end`) could never skip: `CS.Upstream.PointerReturns`
   is a namespace table when the type is missing. Fixed in the harness:
   `il2cpp_suite.py prepare` applies `roslyn_fixtures.py`'s rewrites to the
   project's copies (only `[In, IsReadOnly] ref` → `in` before Unity 6000.6,
   whose Roslyn 4.3 has no `ref readonly` but compiles the fixture's own
   `RequiresLocationAttribute`) and opens the two guards, so the cases run
   in the player. The dead guard is gone.

2. **Stripping** [C]. `CS.System.Int64.MaxValue` and `MinValue` and
   `UInt64.MaxValue` read nil (3 integer-rule cases),
   `System.IO.Path.GetExtension` was missing (`CaseSpanOverloadsBcl`) and
   `TypeInfo.GetDeclaredField` was missing (`CaseRestrictPrivateAccess`).
   The stripped `mscorlib.dll`
   (`Library/Bee/artifacts/Android/ManagedStripped`) has one field each in
   Int16, Int32 and Int64 (`m_value`): the constants are gone, because C#
   inlines constants, so nothing references the fields. Minimal stripping
   still strips the class libraries. This is the documented behaviour
   (Assets/XLua/Doc/Configure_EN.md:63, Faq_EN.md:35): a .NET type reached
   only through the reflection fallback needs `[ReflectionUse]` or
   `[LuaCallCSharp]`. The suite does that now in its IL2CPP project only
   (`Test/UnityMatrix/Il2cppSuite/XluaSuitePreserve.cs`: `[assembly:
   ReflectionUse]` for Int32, Int64, UInt64, Path, Convert, BitConverter and
   TypeInfo), and the three cases pass, which also shows that the
   generator's link.xml reaches UnityLinker for class-library types. Not a
   new user-facing problem, but one sharp edge: a stripped constant reads as
   nil without an error. Side effect worth knowing: in a stripped player
   `typeof(T):GetDeclaredField` is not there, so the reflection reach that
   bridge-audit.md item 12 describes is smaller in players than on the host.

3. **Generic methods in reflection wrappers** [C]. `CaseConstrainedGenericMethod`
   failed with "invalid arguments to Coverage.Generics.Constrained":
   `MethodWarpsCache.cs:181-185` skips generic method definitions under
   `ENABLE_IL2CPP` instead of closing them over their constraints
   (upstream commit 8878b6d5, "il2cpp does not support reflective calls of
   generic methods"). With the guard removed in the project's copy, the
   reflection wrapper closed `Constrained<Ref>` and the whole plain suite
   still passed 522/522 on 6000.3.21f1 (an experiment, not committed). Unity
   6 keeps a fully shared body of a generic method for instances created at
   run time ([UNITY_SPIKE.md](../../build/UNITY_SPIKE.md#q2-il2cpp-generic-sharing)) [C];
   2021.3 predates full generic sharing [S] and could throw
   `ExecutionEngineException` for an instance IL2CPP did not compile [U]. Kept as upstream does, the case now
   expects the error in IL2CPP players (`LuaTestCommon.Il2Cpp()`); owner
   question below. (2026-10-29: the guard is gone, and the case expects
   the result everywhere; see
   [Generic methods in IL2CPP reflection wrappers](#generic-methods-in-il2cpp-reflection-wrappers-2026-10-29).)

4. **[BlackList] lists and filters in players** [C]. `CaseBlackListFilterReflected`
   expected the filter of a static class to hide members, but players read
   only the attribute forms (`GenAttributes.cs:254`, `#if (UNITY_EDITOR ||
   XLUA_GENERAL) && !XLUA_TEST_PLAYER_BLACKLIST`). `LuaTestCommon.PlayerBlackList()`
   now says true in players too, so the case skips there as it does with
   `XLUA_TEST_PLAYER_BLACKLIST` on the host. Behaviour as designed.

5. **The reduced debug library of release players** [C]. Strict is the
   minimal preset in a release player, so `XLUA_REDUCED_DEBUG` leaves
   `debug` with traceback and getinfo only (`LuaEnv.cs:415`); six cases use
   `debug.getmetatable` or `debug.setupvalue` as a tool and failed with
   "attempt to call a nil value". The host never sees this (XLUA_GENERAL
   keeps the whole library). They skip without the whole library now, and
   `needs_reflection.lua` counts their entries only where it is there
   (`"debug"`). Behaviour as designed.

6. **Cases that apply in Unity only, or outside it only** [C].
   `CaseTestImplicit` (two suites) and `CaseGetType3` return early outside
   Unity, so `needs_reflection.lua` never listed them; in the strict player
   they need reflection. `CaseNoBridgeValueResult` needs reflection outside
   Unity only. New entry conditions `"unity"` and `"general"`
   (`luaunit_compat.lua`, `NEEDS_ALSO`). These are the 8 per-mode
   differences left in the strict comparison: the case runs on one side
   only.

7. **`Environment.GetEnvironmentVariable` of an empty variable** [C]. With
   `XLUA_EXPECT_NATIVE_MODULES=` (the minimal library has no LuaSocket) the
   strict player expected LuaSocket. IL2CPP returns null for a variable set
   to the empty string (the runner's probe logs
   `XLUA_SUITE_EMPTY_ENV: ... gives null`), Mono returns "". The minimal
   `os` has no `getenv`, so the suite reads it through C#. Not an xLua
   difference; the harness writes `,` (an empty list) instead.

8. **No StreamingAssets searcher in the minimal preset** [C]. The two
   `testRequire2LoadLua` cases need `testlua.B` from StreamingAssets in
   `LuaEnvSingletonForTest`'s env, which only the main env's loader reached.
   The runner now puts the copied StreamingAssets on `package.path` of both
   envs, as `run_managed.py` does on the host; Lua's file searcher comes
   after the Resources searcher, so `D` still comes from Resources.

9. **Paths on Android** [C]. `LuaTestCommon.resultPath` was
   `/sdcard/luatest/`, which an app cannot write on Android 11 and later;
   with the minimal preset (no `io`) the case lists are written through
   C#, which would throw there [U]. Now `Application.persistentDataPath`. `existingFilePath`
   pointed into the APK; it now names the runner's copy of `main.lua`.

No crash, hang or different error text in any configuration; the
`needs_reflection` messages of strict match the host's for every case that
runs on both. [C]

## Running it on the emulator

- [C] After `adb uninstall`, the AVD kept `/sdcard/Android/data/org.xluang.suite`
  owned by the old uid; the shell cannot delete it ("Permission denied"),
  the next install cannot write it and the player never starts its scene
  (the IL2CPP resource extraction fails, as Test/MobileSmoke/README.md
  describes). `il2cpp_suite.py run` therefore replaces the app in place
  (`install -r`, same uid) and empties its data with `pm clear`, which
  removed the stale folder.
- [C] Unity's Android build restarts the adb server ("daemon not running;
  starting now"), which drops other adb connections for a moment
  (memory note "Unity adb vs phone bench").

## CI

`unity-matrix.yml` has two jobs ([CI.md](../../.github/CI.md)):
`il2cpp-suite-build` (GameCI's Android image of 6000.3.26f1, digest-pinned;
X86_64 APKs, since hosted runners are x86_64) and `il2cpp-suite-run`
(`reactivecircus/android-emulator-runner` v2.38.0, an API 34 x86_64
google_apis image with KVM). Neither has run [U]: the repository has no
Unity secrets yet, and the owner starts costly workflows. An X86_64 strict
APK was built locally with the same commands (`--arch x86_64`); this Mac
cannot run an x86_64 emulator.

## Generic methods in IL2CPP reflection wrappers (2026-10-29)

The coordinator's brief of 2026-10-29 (Q78): remove the `#if
!ENABLE_IL2CPP` guard of owner question 1, test it in Android IL2CPP
release players of several Unity versions, including an instance IL2CPP
may lack code for, and keep the guard only where that crashes.

**What reflection closes.** `OverloadSignature.Prepare`
(`MethodWarpsCache.cs:178`) closes a generic method definition with
`tryMakeGenericMethod`, which takes each type parameter's first
constraint; `Utils.IsSupportedMethod` (`Utils.cs:2577`) accepts only
methods whose generic parameters all have class constraints (not
`ValueType`, not interfaces) and appear among the parameters. So the
instance is always over classes, and IL2CPP runs it on its shared code for
reference types. Value-type instances come only from
`xlua.get_generic_method`, which the guard never covered. **[C]** by reading.

**Players** (Android ARM64, IL2CPP release, Minimal stripping, code
generation OptimizeSpeed, compiler configuration Release; the plain
configuration of `il2cpp_suite.py`; Lua 5.5.1 from the tree, NDK r30; the
`xlua16k` AVD, Android 16, 16 KB pages). Two rounds: the guard removed with
the old test expectation and a probe (below), then the final code and
tests, again with the probe. **[C]**

| Unity | Suite, final code | `Constrained<Ref>` through reflection | `xlua.get_generic_method` with a value type (`Echo<int>`, `<long>`, `<Point>`, `<Guid>`, `Pair<int, string>`) | APK (bytes) |
|---|---|---|---|---:|
| 2021.3.45f2 | 523/523 in both modes, 0 differences from Mono | works | catchable `ExecutionEngineException`: "Attempting to call method 'Coverage.Generics::Echo<System.Int32>' for which no ahead of time (AOT) code was generated" | 10,967,246 |
| 2022.3.62f3 | 523/523, 0 differences | works | works (full generic sharing) | 11,133,633 |
| 6000.3.21f1 | 523/523, 0 differences | works | works | 15,450,910 |
| 6000.6.4f1 | 523/523, 0 differences | works | works | 15,733,974 |

"0 differences" compares every case with `run_managed.py --source-gen` of
the same tree on the host (`il2cpp_suite.py check --host-log`). 6000.6.6f1,
which the brief named, has no Android Build Support on this machine, so
6000.6.4f1 stood in for the 6000.6 line. In the first round (the old
expectation) the guard's removal was the only cause of
`CaseConstrainedGenericMethod` failing ("expected: false, actual: true") on
2021.3 and 2022.3; 2021.3 also failed `CaseGetGenericMethod`,
`CaseGetGenericMethodIndex` and `CaseRestrictPrivateAccess`, the value-type
instances above, which the guard does not touch.

**The probe** (project copies only, not committed: a block at the top of
`main.lua` that `pcall`s each call and prints the outcome, and a method
`ConstrainedUsed<T>` that C# calls once with another class): in 2021.3 the
reflection wrapper ran `Constrained<Ref>`, which no C# code calls, as well
as `ConstrainedUsed<Ref>`, so IL2CPP 2021.3 keeps the shared body of a
generic method that is never instantiated with a class; every value-type
instance failed with the exception above, `pcall` caught it, and the probe
went on ("after the probes: ok"). No crash and no hang in any version.
**[C]**

**Decision.** The guard is removed for every Unity version, with no version
check: the reflected call works in all four versions, and the only failure
mode seen, a missing instance, is a catchable exception with a clear
message (the same one `xlua.get_generic_method` already gave). The suite
now expects `Constrained` to work everywhere, and `ASSERT_VALUE_TYPE_GENERIC`
(`luaunit_compat.lua`) expects the exception for the value-type calls in
IL2CPP players before Unity 2022.1
(`LuaTestCommon.Il2CppNoFullGenericSharing`, `ENABLE_IL2CPP &&
!UNITY_2022_1_OR_NEWER`; full generic sharing arrived in 2022.1 [S], and
only 2021.3 and 2022.3 were run). `CaseGetGenericMethodIndex` uses `string`
instead of `int`. MIGRATING.md has the row.

**Harness changes** needed to run the other versions: `XluaSuiteBuild.cs`
reads the code generation setting from `EditorUserBuildSettings` before
2022.1; `il2cpp_suite.py run` resolves the launcher activity
(`UnityPlayerActivity` before Unity 6) and presses the positive button of
Android 16's page-size dialog ("This app isn't 16 KB compatible ... page
size compatible mode"), which took the focus so that the 2021.3 player
never loaded its scene; and `prepare` writes the `UnityEngine.ContentLoadModule`
link.xml for 6000.6 and later, without which the 6000.6.4f1 player crashed
at start (SIGSEGV in `libil2cpp.so` called from `libunity.so`, before the
scene), as the WebGL smoke player did. The dialog also came up for the
6000.3 and 6000.6 APKs, whose libraries all have 16 KB `LOAD` alignment
(`llvm-readelf`). Its cause there (traced the same day, [C]): Android 16
keeps the page size compat flag of the earlier 2021.3 install of
`org.xluang.suite` through every `install -r`
([android-16kb-dialog.md](android-16kb-dialog.md)); `run` now reinstalls
after an uninstall when it finds such a stale flag.

Not run: the gcm and strict configurations on these versions, 6000.0,
6000.6.6f1, a phone, iOS, High stripping, Development players, other Lua
versions.

## Questions for the owner

1. **Generic methods in IL2CPP reflection wrappers.** Remove the
   `#if !ENABLE_IL2CPP` in `MethodWarpsCache.cs:182` so that reflection
   closes generic methods over their constraints in IL2CPP players, as it
   does in the Editor and on Mono? It worked on 6000.3.21f1 (above); 2021.3
   is untested and may throw where IL2CPP has no code for the
   instantiation. Cost: none measured in size; a 2021.3 player run to
   check. Today players silently lose such methods ("invalid arguments").

   **Done 2026-10-29 (coordinator's brief Q78, under the owner's 'do what
   is best'): removed in every Unity version, no version check.** It works
   in all four tested versions, and where a player has no code for an
   instance the call raises a catchable `ExecutionEngineException`; no
   crash was seen
   ([below](#generic-methods-in-il2cpp-reflection-wrappers-2026-10-29)).
2. **Silent nil for stripped members.** A stripped constant or method of a
   reflected type reads as nil. Should the reflection path warn once in
   Development builds when a type's member list looks stripped? (No cheap
   way to detect it is known [U].)

   **Coordinator's decision under the owner's 'do what is best',
   2026-10-29: document it prominently, no warning.** The FAQ entry on
   "attempt to call a nil value" is now "Why is a C# member nil in an
   IL2CPP player when the Editor has it?", and Configure_EN.md has a note
   in the `LuaCallCSharp` section: a stripped member of a reflected type
   reads as nil with no error, and `[ReflectionUse]` (or `LuaCallCSharp`,
   `link.xml`, `[Preserve]`) keeps it.
