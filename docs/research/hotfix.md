# C# hot update and hotfix approaches for Unity

Researched 2026-10-17. Sources: the ILRuntime, HybridCLR (with hybridclr_unity)
and InjectFix repositories, read but not built, plus vendor and store docs.
Confidence: **[C]** confirmed in source or primary docs, **[S]** secondary or
vendor claim, **[U]** unconfirmed or inferred.

## Comparison

| | xLua hotfix (xlua-ng) | ILRuntime (incl. HybridPatch) | HybridCLR | InjectFix |
|---|---|---|---|---|
| Mechanism | Cecil injects a check at method entry that calls Lua | Pure C# IL interpreter; HybridPatch (2025) adds Cecil entry checks | Modified libil2cpp with an IL interpreter | Cecil entry checks that redirect to its own C# IL VM |
| Patch language | Lua | C# | C# | C# |
| Cost when unpatched | Static field load + branch per method; `HOTFIX_ENABLE` also turns on `THREAD_SAFE` locking | HybridPatch: field load + branch | Zero for AOT code; hot assemblies fully interpreted (DHE is paid) | `IsPatched(id)` call + branch |
| New types | No (state via `util.state`) | Yes | Yes | Yes, non-generic, not derived from native types |
| Constructors | Runs after the original; no static constructors | Yes | Yes | No |
| Generics | Yes, per closed type | Yes | Yes (AOT generics need supplementary metadata) | No generic methods |
| async | Only the outer method | Yes | Yes | Broken (issues #271, #386, #400) |
| Build hook | `IPostBuildPlayerScriptDLLs` + external Mono/Cecil tool | `IPostBuildPlayerScriptDLLs` | Replaces libil2cpp (`UNITY_IL2CPP_PATH`, MonoHook) | `PostProcessScene` + per-platform compile templates |
| Unity versions | 2021.3, 2022.3, 6000.3, 6000.6 (tested here) | Version independent | 2022.3, 6000.0, 6000.3 (6000.5 on a branch); **rejects 6000.6** | Fails in 2022.3 player builds (#417, #420) |
| Licence | MIT | MIT | MIT core + paid Pro/Ultimate/Hot Reload | MIT |
| Activity (2026-10) | Upstream last push 2025-11 | v2.1.0 (2022); commits to 2026-09 | Very active, v9.0.3 on 2026-10-13 | No releases; last code commit 2022-04 |

## How the xLua hotfix works

- **Default injection** (`Assets/XLua/Src/Editor/Hotfix.cs:1243-1336`): the
  method gets `ldsfld __HotfixN_<name>; brfalse original;`, then a call to a
  generated bridge `DelegateBridge.__Gen_Delegate_Imp*`.
- **IntKey mode:** the method gets `ldc.i4 id; call
  HotfixDelegateBridge.xlua_get_hotfix_flag`. On iOS that call is a P/Invoke
  into `Assets/Plugins/iOS/HotfixFlags.cpp`.
- **Constructors:** the check goes before every `ret`.
- **Bridges:** they come from Generate Code (`Generator.cs:1089-1139`), and
  the injector finds them by name (`Hotfix.cs:303-305`).
- **Lua side:** `xlua.hotfix` sets the fields through `xlua.access`
  (`LuaEnv.cs:555-575`).
- **Skipped:**
  - static constructors;
  - abstract and P/Invoke methods;
  - interfaces;
  - compiler-generated members: lambdas, iterator and async state machines.

## Performance and size

- **xLua, unpatched code:** about four IL instructions and one static field
  per method. IntKey on iOS adds one P/Invoke per method entry.
  `HOTFIX_ENABLE` also makes every lock-guarded `LuaEnv` call take
  `Monitor.Enter` (`LuaEnv.cs:50`, `DelegateBridge.cs:142-173`). This is not
  measured. **[U]** (2026-10-28: measured, see
  [Measured cost](#measured-cost-2026-10-28). IntKey uses the P/Invoke only
  in the narrow case described there.)
- **xLua, player size:** not measured. [SIZE.md](../../build/SIZE.md) was
  measured without `HOTFIX_ENABLE`. (2026-10-28: measured, see
  [SIZE.md](../../build/SIZE.md#hotfix-hotfix_enable-2026-10-28).)
- **HybridCLR (vendor numbers, OnePlus 9R):** native AOT is 4–90× faster
  than the community interpreter. DHE runs unchanged code at native speed. Hot
  assemblies take about 4.7× their DLL size in memory. **[S]**
- **ILRuntime:** the vendor says calls without CLR binding are "tens of times"
  slower, and that register mode beats xLua on Lua 5.3 in 10+ tests. **[S]**

## Store policy

- **Apple:** guideline 2.5.2 forbids downloaded code that "introduces or
  changes features or functionality". The Developer Program License Agreement
  allows interpreted code that does not change the app's primary purpose.
  Apple warned JSPatch and Rollout users in 2017. **[C/S]**
- **Google Play:** downloading executable code (dex, JAR, `.so`) is
  forbidden, but "does not apply to code that runs in a virtual machine or an
  interpreter". **[C]**
- **All four approaches:** they interpret downloaded code and generate no
  machine code. That keeps them inside both exceptions. Staying within "bug
  fixes, not new features" is the developer's job.

## Recommendations for xlua-ng

**Keep the Lua hotfix.** It is the only free option that patches AOT code
without forking libil2cpp. It works on every platform xlua-ng supports,
including Unity 6000.6, which HybridCLR rejects.

Improvements, in this order:

1. **Measure:** add a `--hotfix` variant to `size_report.py` (APK and IPA),
   and a benchmark of an unpatched method with `HOTFIX_ENABLE` on and off.
   The benchmark captures the cost of the lock. (2026-10-28: done for
   Android and the host, [below](#measured-cost-2026-10-28).)
2. **Separate `HOTFIX_ENABLE` from `THREAD_SAFE` locking,** or document the
   cost. (2026-10-28: the translator lookup no longer locks; the env lock
   stays, with options for the owner, [below](#the-env-lock-and-hotfix).)
3. **iOS flag:** check whether the IntKey P/Invoke (`HotfixFlags.cpp`) can
   become a managed array read. (2026-10-28: measured, not changed,
   [below](#the-ios-intkey-flag).)
4. **Assembly-CSharp:** `files.Single(... "Assembly-CSharp.dll")` at
   `Hotfix.cs:1633` throws in projects without an Assembly-CSharp. Inject the
   assemblies from `HotfixConfig` instead. (2026-10-18: fixed in `fd359e5a`,
   which finds the player's script folder through xLua's own assembly.
   2026-10-28 [C]: reproduced in Unity 6000.3.21f1 by putting the old line
   back, with every runtime script in an asmdef: `InvalidOperationException:
   Sequence contains no matching element` from
   `MyCustomBuildProcessor.OnPostBuildPlayerScriptDLLs`, and the player was
   not injected; `hotfix_player.py --no-assembly-csharp` passes with the fix.
   Owner decision of 2026-10-28: `unity-matrix.yml` runs
   `hotfix_player.py --platform linux` with `--no-assembly-csharp` and with
   `--package` on 6000.3.26f1, a Linux Mono player; skipped without the Unity
   secrets, so not run yet.)
5. **UPM:** with xLua in its own asmdef, IntKey and direct bridge calls fall
   back to field mode without a warning (`Hotfix.cs:523`, `578`, `1221`).
   Handle this or warn. (2026-10-18: the injector now warns for IntKey; see
   the [backlog](../design/backlog.md#hotfix).)
6. **ILPostProcessor:** postpone. The API is internal. The hotfix config is
   evaluated by editor reflection (`HotfixConfig.GetConfig`, LINQ
   properties), which cannot run inside an ILPostProcessor. Its future under
   Unity's CoreCLR move is unknown. ILRuntime's choice of
   `IPostBuildPlayerScriptDLLs` confirms that the current hook is the
   mainstream one.

**Write a HybridCLR integration guide.** It should say:

- xLua's core, its generated wrappers and LuaDLL's P/Invoke stay in AOT
  assemblies; HybridCLR v9 forbids `extern` in hot-update DLLs.
- `CSharpCallLua` bridges should use AOT delegate types. **[U]**
- The Lua hotfix is redundant for hot-update assemblies.

**Presets:**

- **minimal and core:** no `HOTFIX_ENABLE`, no hotfix bridges and no
  `HotfixFlags.cpp`; opt-in where needed.
- **legacy:** keeps upstream behaviour.
- A preset must never change `HOTFIX_ENABLE` silently, because it changes the
  locking of every Lua call.

**Source-generated bindings:** a generator in the XLua assembly cannot see
`[Hotfix]` types in game assemblies, which are compiled later. Two options:

- Use the existing `Inline` injection path, which needs no generated bridges
  (`Hotfix.cs:1364-1550`). The injected method bodies are larger.
- Generate a bridge class in each game assembly.

## Measured cost (2026-10-28)

The three hotfix items of the [backlog](../design/backlog.md#hotfix).
Sources: `Test/Native/hotfix_bench.py` (`HotfixBench.cs`),
`Test/Native/benchmark.py`, `Test/MobileSmoke/size_report.py --configs
hotfix`, the upstream history in this repository, and the files cited. Lua
5.5.1 plugins built from this tree (`build/make_osx_silicon_lua55.sh`,
`build/make_android_lua55.sh` with NDK r30); the C# at `a4750b51`.

**Machines.** Host: macOS 27 arm64 (M2 Pro), Mono 6.14.1, the release-like
`XLUA_NO_BRIDGE_CHECKS` in every build (the owner-thread check is off under
`HOTFIX_ENABLE`, so it would otherwise count against the build without it).
Player: Unity 6000.3.21f1, Android ARM64 IL2CPP release, Minimal stripping,
xLua from the UPM package with the source generator, run on the `xlua16k`
emulator (Android 16, 16 KB pages, arm64 on Apple silicon). No phone and no
iOS device were used. The emulator's runs scatter (a case's slowest run is up
to twice its fastest), so the tables give the lowest median of six runs over
two boots; read differences under 1 ns there as noise.

### Unpatched methods

ns per call, the loop included. "Off" is a build without `HOTFIX_ENABLE`
(nothing injected); "on" is injected. The methods are `int Add(int, int)`;
"inlinable" lets the JIT or the C++ compiler inline it, "not inlined" has
`[MethodImpl(NoInlining)]`. **[C]**

| method | host off | host on | IL2CPP off | IL2CPP on |
|---|---:|---:|---:|---:|
| never injected, inlinable | 0.32 | 0.32 | 0.00 | 0.00 |
| never injected, not inlined | 0.90 | 0.89 | 0.93 | 0.93 |
| `[Hotfix]` (field mode), inlinable | 0.32 | 1.65 | 0.00 | 1.68 |
| `[Hotfix]`, not inlined | 0.90 | 1.68 | 0.92 | 1.68 |
| `[Hotfix(HotfixFlag.IntKey)]`, inlinable | 0.31 | 1.75 | 0.00 | 5.66 |
| `[Hotfix(HotfixFlag.IntKey)]`, not inlined | 0.90 | 1.76 | 0.93 | 5.54 |

- **Field mode** costs about 0.8 ns per call on both (a static field load
  and a branch), and a small method that was inlined is no longer: the
  injected body is too large. A method of 1.7 ns stays 1.7 ns.
- **IntKey** costs the same as field mode on Mono, whose JIT inlines the
  call to the assembly's `HotfixBridges.Get(id)`, but about 4.6 ns per call
  on IL2CPP, which calls it.
- **Patched** (`HOTFIX_ENABLE`, the method replaced by a Lua function that
  adds its arguments): 210 ns on the host in both modes, 349 ns on IL2CPP
  (field mode). A delegate call into Lua costs 89 and 61 ns there, so the
  hotfix session (`InvokeSessionStart`, `InParam`, `Invoke`,
  `InvokeSessionEndWithResult`, `DelegateBridge.cs:162`) costs two to
  six times a generated delegate bridge. **[C]**

### What `HOTFIX_ENABLE` costs code that is not patched

`HOTFIX_ENABLE` turns on everything `THREAD_SAFE` turns on
(`#if THREAD_SAFE || HOTFIX_ENABLE`): the env lock on every C# -> Lua entry,
a lock in the translator lookup of Lua -> C# calls (until this change), a new
byte array for every Lua string that C# reads instead of the shared buffer
(`LuaDLL.cs:144`), and no owner-thread check. `benchmark.py --modes
sourcegen`, lowest median of three rounds per build, ns per call: **[C]**

| case | off | `HOTFIX_ENABLE` before | after `a4750b51` | `THREAD_SAFE` after |
|---|---:|---:|---:|---:|
| Lua -> C# static call, ints | 47.8 | 48.2 | 47.6 | 47.1 |
| Lua -> C# instance call | 81.4 | 126.8 | 82.0 | 81.5 |
| Lua -> C# property get | 90.6 | 166.2 | 90.9 | 90.3 |
| Lua -> C# `int[]` element get | 100.8 | 176.9 | 99.9 | 100.7 |
| Lua -> C# string argument | 81.0 | 88.2 | 86.5 | 87.4 |
| C# -> Lua delegate | 76.3 | 87.4 | 88.0 | 87.0 |
| C# -> Lua interface method | 199.6 | 223.4 | 222.6 | 222.0 |
| `LuaTable.Get<int, int>` | 186.9 | 207.4 | 199.3 | 202.0 |
| `LuaFunction.Call` | 308.4 | 320.8 | 314.3 | 319.6 |

On the IL2CPP player (`HotfixBench.cs`; "before" is the same player built
from the tree before `a4750b51`): **[C]**

| case | off | `HOTFIX_ENABLE` before | after `a4750b51` |
|---|---:|---:|---:|
| `lock (o) {}`, uncontended | 28.3 | 28.6 | 28.1 |
| C# -> Lua delegate | 45.2 | 68.7 | 61.2 |
| `LuaTable.Get<int, int>` | 178.0 | 204.9 | 211.9 |
| Lua -> C# call with an object argument | 84.4 | 104.4 | 94.3 |
| Lua -> C# instance call | 65.2 | 77.5 | 65.2 |

- **Lua -> C# calls** paid 40 to 77 ns on the host (12 to 20 ns on IL2CPP)
  for the lock in `ObjectTranslatorPool.Find`, which every call that needs
  the env's translator runs. The lookup now reads without a lock
  (`a4750b51`); with either define those calls cost what they cost without
  it on the host, and the instance call on IL2CPP too. The object argument
  is still 10 ns slower on IL2CPP with the define; not investigated. **[C]**
- **C# -> Lua entries** pay the env lock: 11 to 26 ns per entry on the
  host, 16 to 34 ns on IL2CPP. An uncontended `lock` costs 16 ns on the host
  and 28 ns on IL2CPP. **[C]**
- **Strings** that C# reads from Lua cost 5 to 8 ns more and allocate,
  because the shared decode buffer is off. **[C]** A `[ThreadStatic]` buffer
  would keep that without sharing; not done.

### Size

Android ARM64, Minimal stripping, APK delta and `libil2cpp.so` compressed /
uncompressed; [SIZE.md](../../build/SIZE.md#hotfix-hotfix_enable-2026-10-28)
has the table. **[C]**

- The define alone: +13,128 bytes (the hotfix half of `DelegateBridge`,
  `xlua.hotfix` and the lock code).
- Injection, per method, from 500 methods: field mode 42 bytes of APK, 194
  bytes of uncompressed code and 30 bytes of metadata (the static field);
  IntKey 38, 183 and 0. IntKey saves the metadata, not the code.

### The env lock and hotfix

**Why `HOTFIX_ENABLE` locks.** **[C]** Until upstream `426d6a5f`, the
injected code itself took `Monitor.Enter` on a static
`DelegateBridge.DelegateBridgeLock` around the patched call. `426d6a5f`
(2016-12-31, "move thread safety into the XLua namespace")
replaced that with `luaEnvLock` in `LuaEnv`, `LuaTable`, `LuaFunction` and
the bridges, under `#if THREAD_SAFE || HOTFIX_ENABLE`, and `67f74aff`
(2017-01-02) narrowed it to the hotfix session. So upstream meant patched
methods to be callable from any thread: the session locks the env, and that
lock protects the env only if every other entry takes it too.

**Can hotfix without `THREAD_SAFE` skip it?** Only if patched methods run on
the env's owner thread. Unity calls `Update`, coroutines and
`UnitySynchronizationContext` continuations on the main thread, but a
`[Hotfix]` type's methods also run from `Task.Run` and thread-pool
callbacks, `async` code after `ConfigureAwait(false)`, socket and timer
callbacks, `OnAudioFilterRead` (the audio thread) and
`Application.logMessageReceivedThreaded`. **[S]** (Unity manual and API
pages.) Without the lock such a call would corrupt the Lua heap in a
release player, where the owner-thread check does not run. That changes a
guarantee upstream users may rely on, so this is an owner question; nothing
was changed.

**Cost per patched call:** one lock pair, 16 ns of 210 ns on the host (8%)
and 28 ns of 349 ns on IL2CPP (8%). The larger cost of the define is the
lock on every C# -> Lua entry (above), which code that is never patched
pays too.

**Options for the owner:**

1. **Keep it (legacy today, every preset).** Cost as measured: 11 to 34 ns
   per C# -> Lua entry, 5 to 8 ns and an allocation per string read.
2. **Separate them in the non-legacy presets.** `HOTFIX_ENABLE` alone takes
   no lock; a patched method must run on the env's owner thread, and
   `InvokeSessionStart` checks that in every build (a thread-static read and
   a compare, about 2 ns, against 210 to 349 ns per patched call), throwing
   `InvalidOperationException` instead of corrupting the heap. `THREAD_SAFE`
   gives back the any-thread behaviour. `legacy` keeps the lock. Gain: the
   C# -> Lua lock, the string buffer and the debug-build owner check under
   `HOTFIX_ENABLE`. Cost: a preset difference to document and test (a
   `run_managed.py` pass), and a hotfix that runs off the main thread fails
   loudly in those presets. Recommended.
3. **Separate them everywhere, `legacy` included.** The same, but breaking
   for upstream projects that patch methods called off the main thread.
4. **Lock only the hotfix session.** Not safe: a patched call on a worker
   thread would still run Lua while the main thread does, unless every
   entry locks.

### The iOS IntKey flag

**Where the P/Invoke is left.** **[C]** Since source generator milestone M7
an assembly with generated `HotfixBridges` numbers its IntKey ids in its own
slots, and the injected check calls `HotfixBridges.Get(id)`, a managed array
read, on iOS too (`Hotfix.cs:1518-1522`, `HotfixBridges.cs:72`). The
process-wide list and its iOS P/Invoke
(`HotfixDelegateBridge.xlua_get_hotfix_flag`, `DelegateBridge.cs:88-100`, into
`Assets/Plugins/iOS/HotfixFlags.cpp`) serve only xLua's own assembly when it
has no `HotfixBridges` (`sharedIntKey`, `Hotfix.cs:453`): xLua compiled into
Assembly-CSharp with only `Inline` IntKey types.

**Measured** (hand-written checks in front of the same body, ns per call):
**[C]**

| check | host (Mono) | IL2CPP |
|---|---:|---:|
| process-wide list, managed (`xlua_get_hotfix_flag` off iOS) | 2.09 | 3.35 |
| process-wide list, the iOS P/Invoke into `HotfixFlags.cpp` | 8.68 | 3.06 |
| a `bool[]` read in the method | 1.19 | 2.16 |
| a call to a copy of `HotfixBridges.Get` | 2.00 | 3.68 |
| the same read inline in the method | 1.49 | 2.14 |

On the Android player the P/Invoke goes through `__Internal` into
`HotfixFlags.cpp` compiled into the player, as on iOS. IL2CPP calls such a
function directly, so it costs no more than the managed list; only Mono's
P/Invoke is slow, and iOS never runs Mono. An iOS device was not measured.
**[C]** Android; **[U]** iOS.

**Result.** Replacing the P/Invoke with the managed list gains nothing on
IL2CPP, so it was not changed. What would gain is an inline array read in
the injected code: 2.1 ns instead of the 5.5 ns that IntKey through
`HotfixBridges.Get` costs now. That needs the injector to read the slots
array itself, with a bounds check, in every injected method: more IL and
C++ per method, which works against IntKey's purpose (less code and no
metadata per method). Not done; an owner question.

`HotfixFlags.cpp` has a race of its own: `xlua_set_hotfix_flag` grows the
array with `realloc` (`HotfixFlags.cpp:24`) while another thread may read
it in `xlua_get_hotfix_flag`. A patch installed while a worker thread runs
an injected IntKey method in xLua's own assembly can read freed memory. It
needs an `Inline` IntKey type in xLua's assembly and a patch installed
while another thread runs one. **[C]** by reading; not reproduced.

**Options for the owner:**

1. **Remove the process-wide list and the iOS flag** (M11 question 4 in
   [source-generator.md](../design/source-generator.md)), making IntKey need
   `HotfixBridges`. These numbers remove the speed argument for keeping the
   native flag, and the realloc race goes with it. Recommended.
2. **Keep the list, drop only the native flag:** iOS uses the managed
   check (0.3 ns slower on IL2CPP), the race goes, `HotfixFlags.cpp` and
   the iOS-only public `HotfixDelegateBridge.xlua_set_hotfix_flag` go
   (a MIGRATING row).
3. **An inline slot read for IntKey** (above): about 3.4 ns per call less
   on IL2CPP, more code per method. Measure the code size before deciding.

## Also worth a look

[zlua](https://github.com/focus-creative-games/zlua) is a Lua solution from
the HybridCLR authors (MIT, active in 2026-10). It has not been analysed yet.

## Unity's Cecil package (spike, 2026-10-18)

Question: can the injector use Unity's `com.unity.nuget.mono-cecil` instead
of the Mono.Cecil 0.11.6 that `Tools/build_tools.py` downloads from
nuget.org? Sources:

- the Unity registry metadata (`packages.unity.com/com.unity.nuget.mono-cecil`);
- the 1.10.1, 1.10.2, 1.11.4, 1.11.5 and 1.11.6 tarballs, each checked against
  the registry's SHA-1;
- the package folders of the seven editors installed here (2021.3.45f2 to
  6000.6.6f1);
- the Cecil 0.11.5 and 0.11.6 NuGet packages, checked against the nuget.org
  catalog's SHA-512;
- Cecil's tags and pull requests on GitHub.

The DLLs were read with `monodis`, and nothing from the packages was run
except Cecil itself. The work files were kept outside the repository.

### What the package contains

| Package | Published | `unity` | Cecil inside | Target | Tarball SHA-1 |
|---|---|---|---|---|---|
| 1.10.1 | 2020-12-16 | 2018.4 | 0.10.0.0 | .NET 3.5 (`mscorlib` 2.0) | `8c750c91…` |
| 1.10.2 | 2021-10-11 | 2018.4 | 0.10.0.0, same DLLs as 1.10.1 | .NET 3.5 | `e899d8ba…` |
| 1.11.4 | 2022-04-11 | 2018.4 | 0.11.4.0 | netstandard2.0 | `d6f9955a…` |
| 1.11.5 | 2025-01-20 | 2019.4 | 0.11.5.0 + [jbevain/cecil#914](https://github.com/jbevain/cecil/pull/914) | netstandard2.0 | `d78732e8…` |
| 1.11.6 | 2025-10-02 | 2019.4 | same DLLs as 1.11.5 | netstandard2.0 | `ecb9724e…` |

- **Cecil version:** the table comes from each DLL's assembly version,
  `AssemblyFileVersion` and `TargetFramework`. The 1.10.x packages say
  "1.10" but contain Cecil 0.10.0. **[C]**
- **1.11.5 and 1.11.6 are the same DLLs:** all four have the same SHA-256 in
  both packages. 1.11.6 adds only `.attestation.p7m` and package.json
  changes. **[C]**
- **The #914 patch:** normalised IL of the package's `Mono.Cecil.dll`
  differs from NuGet's netstandard2.0 0.11.5 in `ReadListRange`, which now
  has #914's `0xffff` check. #914 fixes reading modules with exactly 65535
  parameters and is still open upstream. The other differences are codegen
  from a newer compiler (string switches, `beforefieldinit` on interfaces).
  None of the 0.11.6 changes are in the package: no
  `Instruction.GetPrototype`, no `ReverseEndianness`, no
  `AllowByRefLikeConstraint`. **[C]**
- **Files:** all four DLLs that the injector needs are in the package
  (`Mono.Cecil`, `.Mdb`, `.Pdb`, `.Rocks`). They are Editor-only.
  `Mono.Cecil.dll` is auto-referenced; Rocks, Mdb and Pdb have
  `isExplicitlyReferenced: 1` (`Mono.Cecil.Rocks.dll.meta`). **[C]**
- **Strong name:** the same public key token as NuGet's (`50cebf1cceb9d05e`).
  **[C]**
- **Licence:** the Cecil DLLs are MIT; the package around them is under the
  Unity Companion License (`Third Party Notices.md`, `LICENSE.md`). **[C]**
- **Support:** "intended for internal Unity use only" and "not supported.
  Use at your own risk" (package.json description, `Documentation~/index.md`).
  The same description also asks people who use Cecil to use this package
  "to avoid assembly clashes". **[C]**

### Which editors have it

Every editor bundles the package for offline use
(`Contents/Resources/PackageManager/Editor/com.unity.nuget.mono-cecil-*.tgz`,
with the same SHA-1 as the registry). The editor manifest
(`PackageManager/Editor/manifest.json`) sets the minimum and default
version: **[C]**

| Editor | Bundled and minimum version | Cecil | Bundled packages that depend on it |
|---|---|---|---|
| 2021.3.45f2 | 1.10.1 | 0.10.0 | netcode.gameobjects 1.11.0, multiplayer.tools 1.0.0 |
| 2022.3.62f3 | 1.11.4 | 0.11.4 | netcode.gameobjects 1.12.0, multiplayer.tools 1.0.0 |
| 6000.0.85f1 | 1.11.6 | 0.11.5 | collections 2.7.1, entities 1.5.0, netcode, multiplayer.tools |
| 6000.3.21f1, 6000.3.26f1 | 1.11.6 | 0.11.5 | collections 2.6.8, entities 1.4.8, netcode, multiplayer.tools, project-auditor |
| 6000.6.4f1, 6000.6.6f1 | 1.11.6 | 0.11.5 | built-in collections 6.6.0 and entities 6.6.0, netcode |

- **The built-in shim:** each editor also has a built-in shim,
  `nuget.mono-cecil` 1.0.0, which depends on 1.10.1. **[C]**
- **Not in every project:** the package is only there when a project uses
  one of these packages or asks for it. None of the Unity test projects in
  this repository have it in `packages-lock.json`. **[C]**
- **Asking for a newer version:** a project or package on 2021.3 can ask for
  1.11.6, because its `unity` field is 2019.4. Unity then installs the
  highest requested version. That also moves netcode and the multiplayer
  tools from Cecil 0.10.0 to 0.11.5. **[S]** (Unity manual, "Package
  dependency and resolution").
- **Unity's own Cecil:** the editor uses a renamed copy (`Unity.Cecil.dll`
  in `Contents/Managed`), so the package does not clash with it. **[C]**

### Cecil 0.11.5 against 0.11.6, for the injector

The diff between tags 0.11.5 and 0.11.6 has 13 commits. Each one, by its
effect on the injector:

| Change | Effect on the injector | |
|---|---|---|
| [#958](https://github.com/jbevain/cecil/pull/958) `WriteCompressedInt32` | Portable PDBs whose sequence points go back by 8192 lines or more inside one method get wrong line numbers. See below. | **[C]** reproduced |
| [#948](https://github.com/jbevain/cecil/pull/948) custom debug information on types | 0.11.5 drops type-level custom debug information, such as Roslyn's `TypeDefinitionDocuments`, when it rewrites a portable PDB. This affects debugger navigation to types without method bodies, not line numbers. | **[S]** PR text |
| [#949](https://github.com/jbevain/cecil/pull/949) `.dll` before `.exe` | `BaseAssemblyResolver` tries `.exe` first in 0.11.5. The injector resolves through `BaseAssemblyResolver` (`Hotfix.cs:316-327`). Unity's script assemblies are `.dll`, so this only matters when a search directory has both `Foo.exe` and `Foo.dll`. | **[C]** diff |
| [#943](https://github.com/jbevain/cecil/pull/943) custom modifiers in `Import(System.Reflection.*)` | The injector imports Cecil references, not reflection members, so no effect. | **[C]** grep |
| [#916](https://github.com/jbevain/cecil/pull/916) `privatescope` resolution | Only for compiler-controlled methods with duplicate names. | **[C]** diff |
| [#915](https://github.com/jbevain/cecil/pull/915), [#945](https://github.com/jbevain/cecil/pull/945) new API | The injector uses neither. It compiles against Cecil 0.11.5 (and against 0.11.4 and 0.10.0) without changes. | **[C]** built |
| [#944](https://github.com/jbevain/cecil/pull/944), [#955](https://github.com/jbevain/cecil/pull/955) | Strong-name signing and Windows native PDB writing after an error. Not used. | **[C]** diff |

**The #958 reproduction.** A Roslyn (Mono `csc` 3.9) library has one method
whose `#line` directives go 20000 → 100 → 8292 → 100 → 50. The test reads it
with symbols, writes it with `WriteSymbols = true` as `Hotfix.cs:885` does,
and reads the sequence points back:

- **Cecil 0.11.5 from the package:** line 100 becomes 268435556
  (2^28 + 100), and every later line in that method is wrong too.
- **Cecil 0.11.6:** the lines are unchanged.
- **Other methods:** unaffected with both versions.

The old encoder mishandles signed deltas of −8192 and below. Such jumps come
from `#line` in generated code or from very long files. Unity raised the fix
upstream ("a Unity codebase") after its 1.11.5 build had been made, and
1.11.6 did not take it. **[C]**

### Prototype

- **New option:** `Tools/build_tools.py --cecil-dir <folder>` copies the four
  DLLs from a `com.unity.nuget.mono-cecil` folder instead of downloading
  NuGet's package.
- **Pinned builds only:** it accepts only the SHA-256 set of the 1.11.5/1.11.6
  build (`Tools/build_tools.py:35`, `82`) and stops on any other. 1.11.4 was
  refused as expected.
- **Compiling:** it adds Mono's `Facades/netstandard.dll`, which `mcs` needs
  for netstandard2.0 references.
- **Manifest:** it writes the Cecil it used to a new `cecil=` line. The
  Editor check ignores lines it does not know (`SetupValidator.cs:280-291`).
- **The check:** `Test/Native/hotfix_check.py --cecil-dir` passes the option
  on and builds its helpers the same way.

Results on macOS 27 arm64, Mono 6.14.1, a Lua 5.5.1 plugin built from this
branch:

- `hotfix_check.py --cecil-dir …/com.unity.nuget.mono-cecil-1.11.6/package`:
  `HOTFIX_CHECK_PASS`. That covers portable PDB, MDB, no symbols, an
  unreadable PDB, the second injection being refused, and the short-branch
  widening. **[C]**
- The same check with the default NuGet 0.11.6: `HOTFIX_CHECK_PASS`. **[C]**
- With no symbols, the two injected `HotfixFixture.exe` files are
  byte-identical, and so are their `hotfix_id_map.lua` files. With symbols,
  the outputs differ in bytes and were not compared further. The PDB
  converter is itself built against the Cecil under test. **[C]**/**[U]**
- The fixture has no backward line jump, so this check does not see #958.

### How the Editor could use the package

The injector runs out of process today. `Hotfix.cs:1691-1767` starts
`Tools/XLuaHotfixInject.exe` under Unity's MonoBleedingEdge. The Cecil code
is compiled only into that tool (`#if XLUA_GENERAL`, `Hotfix.cs:161-1617`),
and the tool sits at the project root, outside `Assets`. So xlua-ng never
imports a `Mono.Cecil.dll` into the project, and the clash that the
package's description warns about cannot happen with the current layout.
**[C]**

Three ways to use the package:

1. **Build time only (prototyped).** `build_tools.py --cecil-dir` takes the
   DLLs from `Library/PackageCache/com.unity.nuget.mono-cecil@…` or from an
   editor's bundled tarball and copies them to `Tools/` as before.
   - The manifest check is unchanged, and the injector always runs with the
     Cecil it was compiled against.
   - What it saves: the nuget.org download (offline or firewalled machines).
   - What it does not save: `mcs`, the `Tools/` copy or the manifest.
   - Unity's MonoBleedingEdge has `lib/mono/4.5/Facades/netstandard.dll`
     (2021.3.45f2 in `Contents/MonoBleedingEdge`, 6000.6.6f1 in
     `Contents/Resources/Scripting/MonoBleedingEdge`), so the netstandard
     build should load. **[C]** file present, **[U]** not run.
2. **Run time from the PackageCache.** The Editor would find the package with
   `Path.GetFullPath("Packages/com.unity.nuget.mono-cecil")`, which the Unity
   manual documents, and run the injector with `MONO_PATH` pointing there.
   - This drops the copy, but the Cecil at run time could then differ from
     the build-time one. A 2022.3 project with netcode gets 1.11.4 (Cecil
     0.11.4), lower than the version the injector references. **[U]**
   - It saves only four files, so it is not worth that risk.
3. **Inject inside the Editor.** Compile the Cecil code into an Editor
   assembly and drop `XLuaHotfixInject.exe`, `mcs` and the MonoBleedingEdge
   lookup. This needs:
   - **an asmdef.** Predefined assemblies (`Assembly-CSharp-Editor`) can only
     use auto-referenced plugins (Unity manual, "Assembly definitions"), and
     the injector needs Rocks (`Hotfix.cs:1028`, `1251`, `1407`) and the
     Pdb/Mdb providers (`Hotfix.cs:856-859`). The asmdef would set
     `overrideReferences` and `precompiledReferences` for the four DLLs.
     xLua's `Assets` layout has no asmdefs today; the planned UPM package
     has them ([presets.md](../design/presets.md)). **[C]**
   - **a version define** such as `{"name": "com.unity.nuget.mono-cecil",
     "expression": "[1.11.5,)", "define": "XLUA_UNITY_CECIL"}`, plus a
     `defineConstraints` entry, so that a project without the package still
     compiles and falls back to the tool. The UPM package could instead
     declare `"com.unity.nuget.mono-cecil": "1.11.6"` as a dependency, and
     the package would always be there. **[S]** manual.
   - **a new way to read the configuration.** The tool loads the *player*
     build of each injected assembly with `Assembly.Load(bytes)` and runs
     `HotfixConfig.GetConfig` on its types (`XLuaHotfixInject.cs:46-53`).
     Inside the Editor that would load a second `Assembly-CSharp` (compiled
     without `UNITY_EDITOR`) into the Editor domain, run its static code
     there, and keep it until the next domain reload. Using the
     Editor-compiled types instead can give different members and IntKey
     ids. This is the same obstacle as the ILPostProcessor item above.
     **[U]**, not tried.

### Unity checks for a later run

Run these one editor at a time, never during a phone benchmark: Unity starts
its own `adb`.

1. In a 2021.3.45f2 and a 6000.3.26f1 copy of the UnityMatrix project, add
   `"com.unity.nuget.mono-cecil": "1.11.6"` to `Packages/manifest.json`.
   Check that `packages-lock.json` resolves 1.11.6, and that
   `Library/PackageCache/com.unity.nuget.mono-cecil@*/` has the four DLLs
   with the pinned SHA-256.
2. Run `python3 Tools/build_tools.py --cecil-dir <that folder>`, then
   XLua/Validate Setup: it must report no hotfix problem.
3. Build a player with `HOTFIX_ENABLE` and check the build log for
   `inject finish!`. This shows that Unity's MonoBleedingEdge runs the
   netstandard Cecil build.
4. Run the editor suite (`Test/UnityMatrix/editor_tests.py`) with the
   package present, to rule out a clash with the Test Framework or other
   packages.
5. On 2021.3 with netcode for GameObjects installed, check that asking for
   1.11.6 moves netcode to Cecil 0.11.5 and that netcode's own weaver still
   works.
6. Optional, for option 3: an asmdef with `versionDefines` on the package
   compiles with and without it. Check this with
   `CompilationPipeline.GetAssemblies()` and the defines in
   `Library/ScriptAssemblies`.

## Sources

- [ILRuntime](https://github.com/Ourpalm/ILRuntime), [ILRuntimeU3D](https://github.com/Ourpalm/ILRuntimeU3D)
- [HybridCLR](https://github.com/focus-creative-games/hybridclr), [hybridclr_unity](https://github.com/focus-creative-games/hybridclr_unity), [commercial editions](https://www.hybridclr.cn/docs/business/intro), [performance](https://www.hybridclr.cn/docs/basic/performance), [supported versions](https://www.hybridclr.cn/docs/basic/supportedplatformanduniyversion)
- [InjectFix](https://github.com/Tencent/InjectFix)
- Cecil package: [registry metadata](https://packages.unity.com/com.unity.nuget.mono-cecil), [Cecil 0.11.5...0.11.6](https://github.com/jbevain/cecil/compare/0.11.5...0.11.6), [Mono.Cecil on NuGet](https://www.nuget.org/packages/Mono.Cecil/), Unity manual: [Assembly definitions](https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html), [Accessing package assets](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-assets.html), [Package dependency and resolution](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-dependencies.html)
- [App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/), [Google Play policy](https://support.google.com/googleplay/android-developer/answer/9888379), [9to5mac 2017](https://9to5mac.com/2017/03/07/apple-cracks-down-on-hot-push-code-sdks/)
