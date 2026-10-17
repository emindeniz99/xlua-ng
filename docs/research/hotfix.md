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
  measured. **[U]**
- **xLua, player size:** not measured. [SIZE.md](../../build/SIZE.md) was
  measured without `HOTFIX_ENABLE`.
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
   The benchmark captures the cost of the lock.
2. **Separate `HOTFIX_ENABLE` from `THREAD_SAFE` locking,** or document the
   cost.
3. **iOS flag:** check whether the IntKey P/Invoke (`HotfixFlags.cpp`) can
   become a managed array read.
4. **Assembly-CSharp:** `files.Single(... "Assembly-CSharp.dll")` at
   `Hotfix.cs:1633` throws in projects without an Assembly-CSharp. Inject the
   assemblies from `HotfixConfig` instead.
5. **UPM:** with xLua in its own asmdef, IntKey and direct bridge calls fall
   back to field mode without a warning (`Hotfix.cs:523`, `578`, `1221`).
   Handle this or warn.
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

## Also worth a look

[zlua](https://github.com/focus-creative-games/zlua) is a Lua solution from
the HybridCLR authors (MIT, active in 2026-10). It has not been analysed yet.

## Sources

- [ILRuntime](https://github.com/Ourpalm/ILRuntime), [ILRuntimeU3D](https://github.com/Ourpalm/ILRuntimeU3D)
- [HybridCLR](https://github.com/focus-creative-games/hybridclr), [hybridclr_unity](https://github.com/focus-creative-games/hybridclr_unity), [commercial editions](https://www.hybridclr.cn/docs/business/intro), [performance](https://www.hybridclr.cn/docs/basic/performance), [supported versions](https://www.hybridclr.cn/docs/basic/supportedplatformanduniyversion)
- [InjectFix](https://github.com/Tencent/InjectFix)
- [App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/), [Google Play policy](https://support.google.com/googleplay/android-developer/answer/9888379), [9to5mac 2017](https://9to5mac.com/2017/03/07/apple-cracks-down-on-hot-push-code-sdks/)
