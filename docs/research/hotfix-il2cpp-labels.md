# IL2CPP "use of undeclared label" after hotfix injection (2026-10-18)

The ROADMAP item "Upstream follow-ups" asked whether
[Tencent/xLua#1064](https://github.com/Tencent/xLua/issues/1064) (IL2CPP
fails with `error: use of undeclared label 'IL_019c'` after hotfix
injection) shares the cause of
[Tencent/xLua#1003](https://github.com/Tencent/xLua/issues/1003) (short
branches out of range after constructor injection). It does not. The cause
is a different injector bug, reproduced here on Unity 2021.3.45f2 and
6000.3.21f1 and fixed.

Marks: [C] confirmed here, [S] secondary, [U] unconfirmed.

## Sources

- Tencent/xLua#1064 and its comments, read 2026-10-18. The reporter hit it
  after moving from Unity 2019 to 2021; excluding the failing class from the
  hotfix list made the error move to another class. Two commenters found the
  injected constructor bridge "in the wrong place" in decompiled code: in a
  constructor that ends in an `if` with a `foreach` inside, the hotfix call
  sits inside the `if`, not at the start. [S]
- Tencent/xLua#1003 (fixed earlier, `Hotfix.cs` `fixBranch` now calls
  `OptimizeMacros`). [C]
- ECMA-335, partition I 12.4.2.8 (exception handling blocks), partition III
  3.46 `leave` and 3.56 `ret`: control may not enter a handler, leave a
  protected block or handler other than by `leave`/`endfinally`/`throw`, or
  `ret` inside one; no block may fall through at its end. [S]
- The C++ that IL2CPP wrote in these runs (below). [C]

## Cause

xLua injects a constructor hotfix before every `ret` of the constructor (the
hotfix runs after the original body), `Assets/XLua/Src/Editor/Hotfix.cs`
`injectMethod` and `injectGenericMethod` (`findNextRet` at :1530 and :1698,
the insertion loop up to :1631 and :1868). It then points branches that
targeted the old `ret` at the inserted code (`fixBranch`, :1263). [C]

When a constructor ends in a try/finally or try/catch (a `foreach` over a
`List<T>` or an `IEnumerable<T>`, `using`, `lock`, an explicit `try`), the
handler's exclusive end (`HandlerEnd`) is that `ret`. Cecil's
`ILProcessor.InsertBefore` does not move handler boundaries, so the inserted
hotfix code became the last part of the handler. The injected IL then has
three verifier errors per constructor: the `leave` out of the try block (and
any `br` to the old `ret`, such as the `if` that skips the `foreach`) jumps
into the handler; the hotfix's own `brfalse` jumps out of the handler without
`leave`; and the handler ends with a `call` that falls through. [C]

Mono's JIT runs that IL with the expected results, so the host tests never
saw it (prototype in the scratch run: `TailForeach 8`, `TailCatch 20 -1`,
the original values). [C]

IL2CPP writes a finally handler as a C++ lambda,
`auto __finallyBlock = il2cpp::utils::Finally([&] { FINALLY_xxxx: ... });`,
and the try block after it. With the hotfix code inside the handler, its
label (`IL_005c` for `TailForeach`) is declared inside the lambda while the
`goto IL_005c` is in the enclosing function, and the hotfix's
`goto IL_006d` inside the lambda names a label of the enclosing function.
Labels do not cross a lambda boundary, so clang reports both as undeclared.
Saved: `evidence/6000.3.21f1-unfixed-release/TailForeach.cpp.txt` and
`il2cpp-GenericTail-unfixed.cpp.txt` in the run folder. [C]

A catch or filter handler is written inline (`catch(Il2CppExceptionWrapper&)`
then `goto CATCH_xxxx`), so the same broken IL compiles there and happens to
run. [C] That would explain why only some classes failed in #1064 and why
excluding one class moved the error to another. [U] Why Unity 2019 accepted
the same assemblies was not checked; the lambda form of finally blocks is
the likely change. [U]

Whether the shape occurs depends on the compiler settings: in a Development
player Unity compiles scripts without optimizations, Roslyn puts a `nop`
between `endfinally` and the `ret`, the handler ends at the `nop`, and
`TailForeach` was not affected; `GenericTail<T>` (a `foreach` over
`IEnumerable<T>`), the struct and `LongSwitch` still were. A release player
is affected in every shape that ends in a handler. [C]

#1003 is not involved: every short branch in the injected assemblies fits
(`ShortBranches.exe --check`, below). [C]

## Fix

`fixBranch` now also moves `TryEnd` and `HandlerEnd` from an old `ret` to the
first instruction of the hotfix code inserted before it
(`Hotfix.cs:1276-1292`), so the hotfix runs after the handler, as it does
after a constructor without one. Starts of regions cannot be a `ret` in valid
IL and are left alone. Hotfix behaviour does not change: on Mono the
misplaced code already ran after the handler. [C]

## Tests

- `Test/Native/HotfixRegions.cs`: constructors that end in a try/finally
  (`foreach` over `List<int>` behind an `if`, as in #1064, with a field
  initialiser before the base constructor call), a try/catch, a try/finally
  inside a try/catch with a filter, a struct constructor with a switch, a
  generic class (`injectGenericMethod`), a switch with an early `return` and
  more than 127 bytes of IL; methods whose first instruction is a try start
  or a loop head, and a switch inside a try. `RegionShapes.Check` runs them
  before patching, patched from Lua and restored. Compiled into the
  `hotfix_check.py` fixture (mcs, and Roslyn in step 7) and the IL2CPP player.
- `Test/Native/ShortBranches.cs --check`: the region rules above plus short
  branch range, for every method of the `HotfixFixture` namespace.
  `hotfix_check.py` runs it before and after each injection.
- `Test/UnityMatrix/hotfix_il2cpp.py`: an Android ARM64 IL2CPP player
  (release by default) with these shapes; fails on "undeclared label" and,
  with `--check-tool`, on invalid injected IL.

## Results

Lua 5.5.1 plugins built from this branch (macOS arm64 CMake Release; Android
arm64-v8a with `make_android_lua55.sh`, NDK r30, API 23). The output was kept
outside the repository.

| Run | Injector | Result |
|---|---|---|
| `hotfix_check.py` (macOS, Mono 6.14.1) | before the fix | fails: `CHECKED 117 methods, 16 problems` after injection (all five constructor shapes that end in a handler); the fixture itself passes under Mono [C] |
| `hotfix_check.py` | fixed | `HOTFIX_CHECK_PASS`, every `--check` 0 problems, 153 checks per patch run [C] |
| 6000.3.21f1 IL2CPP, Development | before | build fails, `undeclared label` `IL_003e`, `IL_0068` (`GenericTail<T>..ctor`, `Generics__6.cpp`) [C] |
| 6000.3.21f1 IL2CPP, release | before | build fails, `IL_005c`, `IL_006d` (TailForeach), `IL_0067`, `IL_007d` (TailStruct), `IL_014e`, `IL_015f` (LongSwitch); `--check` 19 problems [C] |
| 2021.3.45f2 IL2CPP, release | before | build fails, the same six labels plus `IL_0038`, `IL_0062` (GenericTail) [C] |
| 6000.3.21f1 IL2CPP, release and Development | fixed | builds; `--check` 0 problems; on the `xlua16k` emulator (Android 16, 16 KB pages) `XLUA_HOTFIX_REGIONS_PASS: 51 of 51 checks` [C] |
| 2021.3.45f2 IL2CPP, release | fixed | builds; `--check` 0 problems; on the emulator, after its "isn't 16 KB compatible" dialog, `51 of 51 checks` [C] |

The first fixed emulator run failed 6 of 51 checks, all of
`new TailCatch(0)` and `new TailNested(0)`: IL2CPP leaves out the
divide-by-zero check by default (`Il2CppSetOption` `DivideByZeroChecks`) and
ARM64 `sdiv` returns 0, so `100 / 0` did not throw. The shapes now throw
`DivideByZeroException` themselves (`RegionHits.Divide`). That is IL2CPP
behaviour, not a hotfix problem. [C]

Not run: the test phone (the emulator covers the IL2CPP conversion, which is
what failed), iOS IL2CPP, macOS IL2CPP (no module in the local editors), and
Unity 2022.3 and 6000.6. [C]
