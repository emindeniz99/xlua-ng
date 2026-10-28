# Native build and test artifacts

Use **Native build and test (no release)** (`native-build.yml`) for native trial
builds. Use **Unity editor matrix** (`unity-matrix.yml`) for the real Unity
editor checks.
Use **Windows host checks** (`windows-host.yml`) for what only a Windows host
shows (below).
Use **docs** (`docs.yml`) to build the Starlight site and deploy it to GitHub
Pages; set the Pages source to "GitHub Actions" first. **Renovate**
(`renovate.yml`) opens dependency update PRs; it needs the `RENOVATE_TOKEN`
secret (see the checklist below).
Since the repository went public (2026-10-19), `build.yml` runs on pushes that
touch `build/`, `native-build.yml` on pull requests, `codeql.yml` on pushes to
`main`, pull requests and weekly, `unity-matrix.yml` on matching pushes, pull
requests and weekly (its Unity steps skip without the licence secrets), and
`docs.yml` on documentation pushes, `windows-host.yml` (since 2026-10-28) on
pull requests that change what it reads. `analysis.yml`, `publish.yml` and
`renovate.yml` stay manual (Renovate needs the `RENOVATE_TOKEN` secret first).
Every workflow can also be started from **Actions → Run workflow**.
It has `workflow_dispatch`, read-only repository permissions, and no tag or
release creation step. Artifacts expire after seven days.

After the workflow is pushed to your fork's default branch:

1. Open **Actions → Native build and test (no release) → Run workflow**.
2. Select the branch and run it.
3. Open the completed run and download the `xlua-lua-<version>-<platform>`
   artifacts. Linux managed test logs are uploaded separately, including on
   test failure when a log was produced, and so are the Android libraries with
   their symbols (`xlua-lua-<version>-android-symbols`).

The Unity workflow has ten editor legs: 2021.3.45f2, 2022.3.62f3,
6000.0.84f1 (Unity 6.0 LTS), 6000.3.26f1 (Unity 6.3 LTS), and 6000.6.6f1
(Unity 6.6), two hotfix legs (`prepare.py --hotfix`, so the project
compiles with `HOTFIX_ENABLE`) on 2021.3.45f2 and 6000.6.6f1, a 2021.3.45f2
leg without xLua's asmdefs (`prepare.py --no-asmdef`, the layout of XLua >
Project Layout > Use Assembly-CSharp), and, since source generator milestone
M11 (2026-10-18), a 2021.3.45f2 leg with `GEN_CODE_MINIMIZE` and one without
the source generator (mode reflection); the other legs use the asmdefs, as
the repository does. Every leg but the reflection one installs the source
generator (`prepare.py --source-gen`, built with the .NET SDK of `mise.toml`).
Only releases a free Personal licence can activate are used; later
2021.3 and 2022.3 patches are paid Extended LTS. Every leg prepares the
project with `--fast-enter-play-mode` (the play mode tests: Enter Play Mode
Options with and without domain and scene reload, and a script recompile) and
`--roslyn-sdk` (the precompiled `XluaRefReadonlyLib.dll`); the 6000.6 legs
add `--csharp12`, which needs a Roslyn that knows C# 12. `--roslyn-sdk` gets
the .NET SDK pinned in `mise.toml` (jdx/mise-action) plus the
NETStandard.Library.Ref 2.1.0 targeting pack from NuGet, checked against its
SHA-512 (the 10.0.401 SDK has no `packs/NETStandard.Library.Ref`); the DLL it
builds has the same IL as one built with Unity 6000.6's `DotNetSdk`. The
workflow requires `XluaRefReadonlyLibTests`; the XLSG diagnostics in the
editor log are checked only by `editor_tests.py run`, since the workflow's
test runner sends the log to stdout. The editor job's timeout is 90 minutes.
Each leg uses a digest-pinned GameCI image and runs these steps: build a fresh
Lua 5.5.1 Linux plugin; install the .NET SDK of `mise.toml` and add the
targeting pack; prepare the project; compile the xLua sources for every define
set of `Test/Native/editor_compile_check.py` with the image's Roslyn and Unity
DLLs (no editor launch, so it fails early); then one editor launch, the single
test step (GameCI's unity-test-runner): the EditMode tests, in the mode
`prepare.py` wrote into `XluaEditorTestMode.txt` (sourcegen with the generator,
or reflection). `editor_tests.py verify` then checks the results file, and
after a sourcegen run lua-language-server checks
`Test/UnityMatrix/luacats/good.lua` and `bad.lua` against the LuaCATS files the
tests wrote. (Until M11 removed Generate Code there were four launches:
Generate Code, the tests, Clear Generated Code, the tests on reflection; there
is no Generate Code or Clear Generated Code step any more.) The test artifacts
(`unity-<version>-<variant>-results`) are kept 14 days.

Without the repository secrets `UNITY_LICENSE`, `UNITY_EMAIL` and
`UNITY_PASSWORD` (and on pull requests from forks, which cannot receive them),
a first job notices that and every Unity job is skipped, so the workflow
passes with a notice; a manual `workflow_dispatch` fails there instead. Pull
request runs cancel older runs of the same ref.

A second job, **hotfix Android IL2CPP build** (since 2026-10-18),
runs `Test/UnityMatrix/hotfix_il2cpp.py` on 2021.3.45f2 and 6000.6.6f1 with
GameCI's digest-pinned Android images (`ubuntu-<version>-android-3.2.2`):
the script prepares a project with `HOTFIX_ENABLE` and the method shapes of
`Test/Native/HotfixRegions.cs` and the source generator, which writes the
hotfix bridges (`--prepare-only --platform linux --source-gen`), one
unity-builder step builds a release ARM64 IL2CPP APK
(`HotfixRegionsSteps.BuildAndroid`, which writes what it logs to
`build.log` because the builder sends the editor log to stdout), and
`--verify-only` checks that the injection ran, that IL2CPP's C++ has no
"undeclared label" ([Tencent/xLua#1064](https://github.com/Tencent/xLua/issues/1064))
and, with `ShortBranches.exe --check`, the injected IL. It builds about three
minutes per player and does not run the APK (job timeout 60 minutes; it is
skipped without the secrets too). `UNITY_SERIAL` is only needed for a
serial-based Professional license. Set the
repository variable `UNITY_MAX_PARALLEL` to control license concurrency; the
default is two.

The preparation script also accepts `--platform macos` for a local Apple
Silicon Editor check; CI uses its default Linux project because GameCI runs the
matrix in Linux editor containers.

CLI equivalents (replace `OWNER/REPO` and `BRANCH`):

```sh
gh workflow run native-build.yml --repo OWNER/REPO --ref BRANCH
gh run list --repo OWNER/REPO --workflow native-build.yml
gh run download RUN_ID --repo OWNER/REPO --dir ./trial-artifacts
```

The native matrix builds Lua 5.3.6, 5.4.9 and 5.5.1 for Linux x64, Windows x64,
macOS ARM64, Android (four ABIs, with `build/make_android_<lua>.sh`), and iOS
ARM64 (deployment target 15, with `build/make_ios_<lua>.sh`). Since 2026-10-18
the iOS jobs also build the arm64 iOS Simulator library
(`XLUA_IOS_SDK=simulator`) into `Plugins/iOS/xlua-variants/simulator/`, checked
with `check_apple_minos.py --platform iossimulator`, and the Android jobs
upload the stripped `libxlua.so` as the plugin and the linked library with its
symbols as a separate artifact ([build/README.md](../build/README.md#stripped-libraries-and-symbols)).
macOS and Linux run native export, GC/profiler and native regression checks. Linux additionally
installs Mono 6.14.1 from Homebrew (the same bottle as macOS), compiles the
existing general C# suite and requires every test (`EXPECTED_TESTS` in
`Test/Native/run_managed.py`) to pass in both normal and coroutine modes,
through reflection and the source generator's plain, `GEN_CODE_MINIMIZE` and
per-member fallback bindings (`--source-gen --source-gen-minimize
--source-gen-fallback`, with the .NET SDK of `mise.toml`). Linux also checks script signing and the LuaCATS definitions with
lua-language-server, and on Lua 5.5.1 hotfix injection and whether
`DEPENDENCIES.md` and `sbom.cdx.json` are current. A separate
`linux / ASan + UBSan` job runs `Test/Native/sanitize.sh` on Lua 5.3.6, 5.4.9,
5.5.1 and LuaJIT, then checks the LuaCATS definitions on LuaJIT. Since
2026-10-18 a parallel job, `linux / ASan + UBSan (core, minimal, LuaScript,
luajit-uint)`, runs `sanitize.sh` for `core-5.4.9 core-5.5.1`, `luajit-uint`,
`XLUA_PRESET=minimal` (the four default versions) and `XLUA_LUASCRIPT=ON` on
5.4.9 and 5.5.1 (about 25 minutes for all four on a laptop; the job timeout
is 45). Since 2026-10-19 a `core + LuaScript` job (`<platform> / core +
LuaScript / Lua <version>`, Linux and macOS, Lua 5.4.9 and 5.5.1, 45 minutes
with a timeout per step) runs what the sanitizer jobs do not: they build the
same libraries but never run `Test/Core`. It builds the core preset twice on
Linux (all exports and the managed export list; macOS has only the first,
`XLUA_EXPORTS=managed` needs an ELF library) and runs `check_exports.py
--preset core`, `core_smoke.c` against `Test/Native/core_sandbox_cases.txt`,
`native_regression.py` (which hands off to `core_regression.py` for a core
library) and `Test/Native/run_core.py` (84 tests on Mono 6.14.1 and on .NET,
compiled with the SDK of `mise.toml`). On 5.5.1 it also builds a legacy
library with `XLUA_LUASCRIPT=ON` (`check_exports.py`, `native_regression.py`
and `run_core.py --bridge`: `LuaEnv` and `LuaScript` in one process) and a
bytecode-only library with the library that compiles its scripts
(`native_regression.py` and `bytecode_luaenv.py` in its four modes), neither of
which any other job ran. The stop-latency checks allow 100 ms locally; on
hosted runners single stops came 101 to 110 ms late (macOS, run 39170081830,
also in the retry), so the workflow sets `XLUA_STOP_SLACK_MS=250` for every
job (`stop_latency.py` and `Test/Core`'s `StopSlackMs` read it and refuse
values outside 100 to 1000, so a lost stop, 2 to 5 s late before the fix,
still fails). A hosted runner can still miss the bound once in a while: the
job sets `XLUA_RETRY_STOP_LATENCY=1` and passes `run_core.py
--retry-stop-latency`, so a failed stop-latency check runs once more and
prints a `::warning::` line (shown in the run's summary; a second failure
fails the job, and so does any other failing test). The test logs are uploaded as `core-tests-lua-<version>-<platform>`.
The `linux / WebGL compile` job runs `Test/Native/webgl_compile_check.py
--package` with the official Emscripten 4.0.20 release archive (the version of
Unity 6000.6's WebGL module, the file emsdk installs, pinned by its release
hash and SHA-256), so the WebGL source and the UPM package's copy compile
without a Unity editor. These jobs and the native jobs
set `XLUA_WERROR=ON`, so a warning in xLua's own C files fails the build; the
Windows jobs leave it off until their MSVC `/W4` output has been checked
([build/README.md](../build/README.md#warnings-hardening-and-test-options)).
No Unity license or signing secret is needed for these native builds.

Every Android job (the matrix above and the Android jobs in `build.yml` and
`publish.yml`, LuaJIT included) uses Android NDK r30 (the current LTS) at API 23
on `ubuntu-26.04`. `.github/scripts/install-android-ndk.sh` uses the image's
copy when it has r30 and otherwise downloads the Linux archive and checks its
pinned SHA-1 and SHA-256. Each job then runs `Test/Native/check_android_elf.py`,
which checks the machine type per ABI, 16 KB LOAD-segment alignment, and every
symbol the managed side imports.

**CodeQL** (`codeql.yml`) analyzes C/C++ from a traced libxlua build for Lua
5.3.6, 5.4.9 and 5.5.1, and C# without a build. Code scanning on a private
repository needs GitHub Advanced Security, so the workflow was manual until
the repository went public; it now runs on pushes to `main`, pull requests
and weekly, and uploads its results to code scanning. **Native analysis** (`analysis.yml`, manual) runs
`Test/Native/analyze.sh`, `Test/Native/coverage.sh` and `Test/Fuzz/run.sh`
(seconds per target as an input) and uploads logs, coverage HTML and crash
reproducers. Its `coverage` job fails when `xlua.c`'s branch coverage drops
below `coverage.sh`'s `MIN_BRANCH_COVERAGE` (100%, Test/README.md). Its `lua-tests` job runs lua.org's official test suites through
`Test/Native/lua_tests.py` for Lua 5.3.6 (also with `LUAC_COMPATIBLE_FORMAT`),
5.4.9 and 5.5.1, plain and under ASan and UBSan; the script downloads the
pinned archives and checks their SHA-256. Check workflow changes with
`mise exec zizmor@latest -- zizmor --offline .github/workflows/`, which must
report no findings.

**Windows host checks** (`windows-host.yml`, since 2026-10-28;
[docs/research/windows-host.md](../docs/research/windows-host.md)). Five legs
on `windows-2022` (Lua 5.3.6, 5.4.9, 5.5.1, LuaJIT GC64 and LuaJIT without
GC64, the mode of `build/make_win64_luajit.bat`), 45 minutes each, about 2 to
3 minutes in practice. Each builds `xlua.dll` with MSVC and `XLUA_APICHECK`
(for LuaJIT, `lua51.lib` from `msvcbuild_mt.bat` with `CL=/DLUA_USE_APICHECK`,
checked for `lj_assert_fail`), runs `apicheck_probe.py`, `check_exports.py`,
`profiler_smoke.py` (Lua 5.x) and `native_regression.py`, then the managed
suite's first pass on the .NET Framework (`run_managed.py --runtime netfx`,
Visual Studio's Roslyn csc) and on Mono 6.12.0.206 for Windows (`mcs`), each
check with its own 15-minute timeout and run even after another failed. On
5.5.1, `ansi_control.py` turns the UTF-8 imports of `LuaDLLBridge.cs`, then of
`LuaDLL.cs`, back to ANSI and requires the matching UTF-8 case to fail, which
shows that the .NET Framework run exercised the ANSI marshalling of
[Tencent/xLua#1081](https://github.com/Tencent/xLua/issues/1081). The logs are
uploaded as `windows-host-<lua>`. Runs (all from pushes to `ci/windows-host`,
since `gh workflow run` refuses a workflow that is not on `main` yet):
[40641746628](https://github.com/emindeniz99/xlua-ng/actions/runs/40641746628)
(Roslyn rejected the fixtures: Windows paths in `roslyn_fixtures.py`, `Span`),
[40642149114](https://github.com/emindeniz99/xlua-ng/actions/runs/40642149114)
(LuaJIT without GC64 failed in `LuaEnv`, two scan-cache cases and a LuaJIT
`require` assumption in the tests), [40645040895](https://github.com/emindeniz99/xlua-ng/actions/runs/40645040895)
(the .NET Framework passed on all legs; Mono's version string and the
control's check failed), [40645581045](https://github.com/emindeniz99/xlua-ng/actions/runs/40645581045)
(everything passed), [40646541080](https://github.com/emindeniz99/xlua-ng/actions/runs/40646541080)
(after merging `main`: configure stopped at the `/pathmap` check of the
reproducible builds, an invalid CMake escape, fixed in `build/CMakeLists.txt`)
and [40646848333](https://github.com/emindeniz99/xlua-ng/actions/runs/40646848333)
(everything passed again).

The Unity editor matrix does not run mobile players. Device tests, UWP, OHOS,
Windows x86, macOS Intel and old iOS deployment targets are outside this new
workflow's matrix; LuaJIT runs only in its Linux ASan/UBSan job. The original `build.yml` is a legacy
upstream workflow, and `publish.yml` started as one. Their Android jobs now use NDK r30; the same scripts were
verified in a container (see `build/README.md`) but have not yet run on hosted
runners, and the other jobs have not been validated for the new runtime matrix.
Use `native-build.yml` for trial artifacts. **`publish.yml`** is the release
workflow (rewritten 2026-10-19; [docs/design/release.md](../docs/design/release.md)
has the steps and inputs). With its default inputs it creates nothing: it
builds every native plugin (Lua 5.5.1 with the `minimal` and `core` variants
on Android and iOS, the iOS Simulator libraries, the other runtimes as
before), the UPM package (`Tools/make_upm_package.py --release`, assembled
twice and compared), the hotfix tools, the release archives with
`android-symbols_<tag>.tgz` and `SHA256SUMS.txt`, and leaves them as the
artifacts `release-files` and `upm-package`; two jobs rebuild Lua 5.5.1 on
other runners and compare the plugins (`.github/scripts/compare-builds.sh`,
`continue-on-error`). The input `release` creates the GitHub release
`v<VERSION>` (a draft prerelease unless `draft` and `prerelease` are turned
off), and `upm_branch` pushes the package to the `upm` branch and the tag
`upm/v<VERSION>`; only those two jobs get `contents: write`. Either input
stops the run before any build unless `VERSION` is not a `-dev` version,
`CHANGELOG.md` has its section, the run is on `main` and the tags do not
exist yet. The workflow has not run on hosted runners in this form.

Verification: `actionlint` passes; the managed runner passes against all three
rebuilt macOS plugins. Hosted native run
[`37694199598`](https://github.com/emindeniz99/xlua-ng/actions/runs/37694199598)
completed with all 15 matrix jobs passing, including Android NDK builds, and
its Lua 5.5.1 Android artifact was downloaded and checked for all four ABIs.
The Unity editor matrix is still pending because the private repository has no
`UNITY_LICENSE`, `UNITY_EMAIL`, or `UNITY_PASSWORD` secrets yet.

References: [manual workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch)
and [workflow artifacts](https://docs.github.com/en/actions/tutorials/store-and-share-data).

## Path filters, concurrency, timeouts and caches

Policy since 2026-10-19. A workflow runs only when one of its inputs changes,
superseded pull request runs are cancelled, every job has a timeout, and
caches hold only downloads or build output that is checked or rebuilt on use.
The release workflow (`publish.yml`) has no cache.

**Path filters.** A path filter lists everything the jobs read, not only the
sources they are about. A check is never skipped for a change that could break
it.

- `native-build.yml` (pull requests) runs for these paths:
  - the native and C# sources: `build/`, `Assets/`, `General/`;
  - the scripts: `Test/`, `Tools/`;
  - the pins: `mise.toml` and `.github/scripts/`;
  - what `Tools/dependencies.py --check` reads: every workflow, this file,
    `DEPENDENCIES.md`, `sbom.cdx.json`, `LICENSE.TXT`,
    `docs/source/package.json`;
  - what `webgl_compile_check.py --package` (`Tools/make_upm_package.py`)
    puts in the package: `Assets/`, `VERSION`, `CHANGELOG.md`.
- `build.yml` (pushes) runs for `build/`, the NDK and OpenHarmony SDK
  installers, the checks of its jobs (`check_android_elf.py` with
  `managed_imports.py` and the C# it reads, `check_apple_minos.py`,
  `ohos_check.sh` and its C files), and the workflow itself.
- `meta.yml` (pushes and pull requests) runs `Tools/check_meta.py` for
  `.meta` files, `Assets/`, `Test/UnitTest`, `Test/PrefTest`,
  `check_meta.py`, `make_upm_package.py` and itself (Test/README.md "Unity
  .meta files"). It takes seconds, so a `.meta`-only change does not start
  `build.yml`'s multi-platform build.
- `unity-matrix.yml` runs for everything `prepare.py` and `hotfix_il2cpp.py`
  copy or build: `Assets/` except the guides in `Assets/XLua/Doc`, `General/`,
  `Tools/`, `Test/`, `build/`, `mise.toml` and the NDK installer.
- `codeql.yml` skips pushes and pull requests that change only Markdown,
  `docs/` or `Assets/XLua/Doc`; it analyzes C, C++ and C# only, and its weekly
  run covers everything.
- `windows-host.yml` (pull requests) runs for `build/`, `Assets/XLua/` except
  the guides, `General/`, `Test/UnitTest/`, `Test/Native/`,
  `Tools/presets.py` (run_managed.py imports it), its Mono installer and
  itself.
- `docs.yml` keeps its list of page sources. Pages also link to other files
  in the repository; `sync.mjs` only checks that the targets exist, so a
  deleted or renamed link target shows up in the next docs run, not in the
  push that removed it.

**Concurrency.** `native-build.yml`, `codeql.yml`, `build.yml`,
`unity-matrix.yml` and `windows-host.yml` cancel the older run of the same pull request or branch.
They never cancel a run on `main`. Manual `native-build.yml` and `windows-host.yml` runs get a group
of their own, so they are neither cancelled nor queued.
`docs.yml` (Pages) and `publish.yml` (releases) never cancel.

**Timeouts.** Every job has `timeout-minutes`. `build.yml` jobs have 30
minutes. `publish.yml` build jobs have 45; its other jobs had timeouts already.

**Caches.** All caches use `actions/cache` (v6.1.0, pinned by SHA) or
mise-action's own cache. Keys are exact, and a restore key falls back only to
content that is checked again:

| Cache | Jobs | Key | Why it is safe |
|---|---|---|---|
| mise (the .NET SDK) | `native-build.yml` Linux, sanitizer and core jobs; both `unity-matrix.yml` jobs | mise-action's: platform, mise version, `install_args`, hash of `mise.toml` | a new pin is a new key |
| `Tools/.cache` | the same jobs | hash of `General/SourceGenerator/**/packages.lock.json`, `Tools/build_tools.py`, `Test/Native/coverage_cs_dotnet.py` | NuGet's locked mode checks every package against the lock file's SHA-512; Mono.Cecil and coverlet.console are checked against their pinned SHA-512 on every use |
| ccache (`$RUNNER_TEMP/ccache`, 300 MB) | both sanitizer jobs, the core jobs | job, platform, Lua version, a hash of the compilers' `--version`, hash of `build/**` (and `sanitize.sh`) | ccache hashes the compiler, flags and preprocessed source of every object; a failed compile is never cached, and cached warnings are replayed, so `XLUA_WERROR` works as without it |
| Unity `Library` | `unity-matrix.yml` editor legs | editor version, variant, hash of the prepared `Packages`/`ProjectSettings`, the xLua sources, the source generator and `Test/UnityMatrix` | Unity reimports what changed |
| pnpm store | `docs.yml` | hash of `docs/source/pnpm-lock.yaml` | pnpm checks every package against the lock file's integrity |
| Mono for Windows MSI (169 MB) | `windows-host.yml` | hash of `.github/scripts/install-mono-windows.ps1`, which pins the version and SHA-256 | the script checks the MSI's SHA-256 on every use and deletes a wrong file |

ccache 4.14.1 comes from the official release archive. The installer
`.github/scripts/install-ccache.sh` checks its SHA-256 and sets
`CMAKE_C_COMPILER_LAUNCHER` and `CMAKE_CXX_COMPILER_LAUNCHER` for CMake.
LuaJIT's own `make` builds do not use it.

These were left out on purpose:

- **ccache in `windows-host.yml`.** The MSVC builds take about 20 s (LuaJIT
  about 25 s more), and ccache needs extra setup for MSVC.
- **ccache in the native matrix and `build.yml`.** Their CMake builds take 4
  to 5 s on Linux, so restoring and saving a cache costs about as much as it
  saves. The iOS builds take 62 to 126 s, but they use the Xcode generator.
- **A cache of Homebrew downloads.** On Linux, `brew install mono` takes 56 to
  63 s, and only about 12 s of that is downloading; the rest pours Mono and
  its 17 dependencies. Instead, `.github/scripts/mono.sh start` runs the
  install in the background while the job's first steps run, and `mono.sh
  wait` comes before the first step that needs Mono. Homebrew's `bin` (and
  its `python3`) is added to `PATH` only after the wait. On macOS the install
  takes 12 to 13 s and stays in the foreground.
- **mise's cache in `docs.yml` and caches in `publish.yml`.** The release and
  its reproducibility rebuilds must start from the pinned downloads and an
  empty build. `docs.yml` needs only Node.js and pnpm, which download in
  about 2 s.

**Measured and expected time.** Run
[`39170081830`](https://github.com/emindeniz99/xlua-ng/actions/runs/39170081830)
started cold: no caches, Mono in the foreground. All figures below are runner
time; the warm ones are estimates from its step times, to be checked in the
next run.

- **Mono overlap**, in every run: about 3 minutes in total.
  - About 15 to 25 s in each Linux native leg (the plugin build, the export
    checks and the SDK install run meanwhile).
  - About 35 s in each Linux core leg.
  - About 55 s in the extra sanitizer job, whose core-preset step (75 s) needs
    no Mono.
  - A few seconds in the main sanitizer job.
- **ccache**, warm, when `build/` is unchanged: about 2.5 to 3 minutes. Cold,
  it costs about 5 s per job.
  - The sanitizer builds take about 10.5 s each (11 of them in the extra job,
    4 in the main one). With hits they take 2 to 3 s: about 90 s saved in the
    extra job and 35 s in the main one.
  - 10 to 20 s per core leg.
- **mise and `Tools/.cache`**, warm: about 5 s per Linux job and 10 s per
  macOS job (the SDK install takes 8 to 11 s and 15 to 17 s), plus the NuGet,
  Mono.Cecil and coverlet downloads. About 2 minutes in all.
- **Path filters and cancelling**: whole runs.
  - A Markdown-only push or pull request no longer starts CodeQL's two
    analyses.
  - A new push to a pull request cancels the older `native-build.yml` run,
    about 35 runner-minutes (25 jobs) if it had just started.

A warm run should take about 8 to 10 runner-minutes less than a cold one. On
the critical path, the extra sanitizer job (the longest) should end about 2.5
minutes sooner.

Caches written by pull request runs are visible only to that pull request.
A manual `native-build.yml` run on `main` writes caches that every run can
read.

## Manual run checklist

Actions is enabled, but every workflow starts only by hand. The first
`native-build.yml` run on `main`
([`38809281337`](https://github.com/emindeniz99/xlua-ng/actions/runs/38809281337))
passed all 16 jobs, including MSVC and the sanitizer job.

Unity editor matrix:

1. Add the secrets from a machine with an activated Unity licence. Each command
   reads the value from stdin or a prompt, so nothing lands in shell history:
   ```sh
   gh secret set UNITY_LICENSE -R emindeniz99/xlua-ng < "/Library/Application Support/Unity/Unity_lic.ulf"
   gh secret set UNITY_EMAIL -R emindeniz99/xlua-ng
   gh secret set UNITY_PASSWORD -R emindeniz99/xlua-ng
   ```
2. The workflow now also runs on matching pushes, pull requests and weekly;
   start it by hand with
   `gh workflow run unity-matrix.yml -R emindeniz99/xlua-ng --ref main`.
   Without the secrets those automatic runs skip the Unity jobs, a manual run
   fails.

Docs site:

1. GitHub Pages on a private repository needs a paid plan (Pro, Team or
   Enterprise); otherwise wait until the repository is public.
2. Enable Pages with the Actions source:
   `gh api -X POST repos/emindeniz99/xlua-ng/pages -f build_type=workflow`
3. `gh workflow run docs.yml -R emindeniz99/xlua-ng --ref main`

Renovate (dependency updates, `renovate.yml`, policy in `.github/renovate.json`):

1. Create a fine-grained personal access token limited to `emindeniz99/xlua-ng`
   with these repository permissions: Contents, Pull requests, Issues, Commit
   statuses and Workflows read and write; Dependabot alerts read-only. Metadata
   read-only is added automatically. `GITHUB_TOKEN` does not work: workflows
   do not run on PRs it opens and it cannot change workflow files.
2. Store it without echoing it: `gh secret set RENOVATE_TOKEN -R emindeniz99/xlua-ng`.
   The workflow fails at its first step when the secret is missing.
3. Optional: turn on Dependabot alerts in the repository settings so Renovate
   sees GitHub advisories; it also queries osv.dev.
4. `gh workflow run renovate.yml -R emindeniz99/xlua-ng --ref main`, or pass
   `-f dry-run=lookup` to only log the updates it would make.

Renovate opens PRs only for releases at least seven days old; security fixes
skip that wait. Majors (and a new Python minor) wait for a tick on the
Dependency Dashboard issue, and nothing automerges. It covers the pinned
actions (SHA plus version comment), the Renovate and mise versions passed to
actions, `docs/source` (Astro, Starlight, pnpm, lockfile) and `mise.toml`; Node
in `mise.toml` and `devEngines` move in one PR. Each PR reruns
`python3 Tools/dependencies.py` so `--check` passes; run it by hand if a PR
reports an artifact error. Runner labels, GameCI image digests, the NDK and
Mono.Cecil and coverlet.console (checksum pins) and vendored sources (Lua, LuaJIT, LuaSocket,
luaunit, ios-cmake) stay manual.

The same Unity tests run locally without secrets:
`python3 Test/UnityMatrix/prepare.py --platform macos --editor-tools ...`, then
`python3 Test/UnityMatrix/editor_tests.py run --unity <Unity executable>
--project <prepared project> --out <log directory> --luals <lua-language-server>`.
It makes the same single batch-mode launch, in the mode `prepare.py` wrote
(`--source-gen BUILD_DIR` for sourcegen, reflection without it), and checks
the log. A single `-runTests -testPlatform EditMode` launch in a fresh
project also works.
`prepare.py --csharp12` (Unity 6000.6 or later, whose Roslyn knows C# 12) adds
`-langversion:12` to `Assets/csc.rsp` and a fixture with C# 12 `ref readonly`
parameters; `editor_tests.py` then also requires `XluaRefReadonlyTests`.
`prepare.py --roslyn-sdk <.NET SDK>` (Unity 6000.6's
`Contents/Resources/Scripting/DotNetSdk`, or any SDK with C# 12 and the
NETStandard.Library.Ref 2.1.0 pack, as in the workflow) compiles
`Test/UnityMatrix/XluaRefReadonlyLib.cs` into a precompiled DLL with virtual,
interface and delegate members that have `ref readonly` parameters, for any
editor. `editor_tests.py` then requires `XluaRefReadonlyLibTests`. The workflow
runs both (`--csharp12` on 6000.6 only). `prepare.py --langversion <value>` adds
`-langversion:<value>` to `Assets/csc.rsp` (with `--csharp12`, in place of
12), so generated code is also checked under newer language rules: `latest` (C# 12) in 6000.6, `10` in the editors with Roslyn
4.3 (2021.3 to 6000.3). `prepare.py --hotfix` compiles the project with
`HOTFIX_ENABLE`, so the editor-side hotfix code is built too; the workflow
runs it on 2021.3 and 6000.6. `prepare.py --no-streaming-assets-loader` compiles it with
`XLUA_NO_STREAMING_ASSETS_LOADER`; `XluaSmokeTests.StreamingAssetsSearcherFollowsTheDefine`
then expects `require` of a StreamingAssets-only module to fail instead of
load. The workflow does not run that variant either. `prepare.py --define NAME` (repeatable)
adds any scripting define; `XluaSmokeTests.FeatureDefinesInTheEditor` checks
`XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and
`XLUA_NO_PROFILER` as the Editor sees them (template and profiler stay).
