# Releasing xLua-NG

Status: prepared 2026-10-19, not run. Nothing has been released, tagged or
pushed to a `upm` branch. The owner starts every release by hand; agents
prepare it and never run `publish.yml` with `release` or `upm_branch`
([AGENTS.md](../../AGENTS.md#git)).

A release is one run of `.github/workflows/publish.yml` on `main`. It builds
every native plugin, assembles the UPM package `com.xlua-ng.xlua` from them,
checks both, and then, only when its inputs ask for it, creates the GitHub
release and pushes the package to the `upm` branch.

## What a release contains

| Release file | What it is | Who uses it |
| --- | --- | --- |
| `com.xlua-ng.xlua-<version>.tgz` | The UPM package: C#, the source generator, the Lua 5.5.1 plugins of Android (4 ABIs; `minimal` and `core` variants), iOS (device and simulator; `minimal` and `core` variants of both), macOS (universal bundle and arm64 dylib), Windows (x64, x86, ARM64), Linux (x64), the WebGL sources, the guides and two samples | Package Manager: "Add package from tarball", or a `file:` entry |
| `lua55_<tag>.tgz`, `lua54_<tag>.tgz`, `lua53_<tag>.tgz`, `luajit_<tag>.tgz` | `Plugins/` of each runtime, for the Assets layout, OpenHarmony included | Projects that copy `Assets/` or swap the runtime |
| `android-symbols_<tag>.tgz` | The linked Android libraries with symbols, every runtime and variant | `ndk-stack`, the Play Console, Unity's symbol upload |
| `xlua-tools_<tag>.tgz` | `Tools/XLuaHotfixInject.exe` with Mono.Cecil, and the other tools of `Tools/build_tools.py` | Projects that use hotfix: extract it into the project folder |
| `sbom.cdx.json` | The CycloneDX SBOM of the repository | Dependency audits |
| `SHA256SUMS.txt` | SHA-256 of every file above | Checking a download |

The git URL install takes the same package from the `upm` branch
(`https://github.com/emindeniz99/xlua-ng.git#upm`, the newest version) or
from the tag `upm/v<version>` (`...xlua-ng.git#upm/v0.1.0`, pinned).

## Inputs of `publish.yml`

| Input | Default | What it does |
| --- | --- | --- |
| `release` | off | Create the GitHub release `v<VERSION>` with the files above. Off: build and check everything, and leave the files as the run's artifacts `release-files` and `upm-package` (seven days) |
| `prerelease` | on | Mark the release as a prerelease |
| `draft` | on | Create the release as a draft. A draft is visible only to people with write access and has no git tag yet; GitHub creates `v<VERSION>` when you publish it |
| `upm_branch` | off | Push the package to the branch `upm` (one commit per version, on top of the previous one, no force push) and the tag `upm/v<VERSION>`. Public as soon as it runs, also when the release is a draft |

The version is the content of `VERSION` at the repository root; there is no
version input. With `release` or `upm_branch` the first job stops the run
before any build when:

- `VERSION` contains `-dev`;
- `CHANGELOG.md` has no section `## <VERSION>`;
- the run is not on `main`;
- the tag `v<VERSION>` or a release of that name (draft or not) exists, or,
  with `upm_branch`, the tag `upm/v<VERSION>` exists.

Permissions: the workflow reads the repository; only the `release` job
(creating the release) and the `upm-branch` job (pushing `upm` and its tag)
get `contents: write`. The rulesets protect `main` and `v*` tags from
deletion, force pushes and updates. They do not cover `upm` or `upm/v*`, so
the job can push them; but once you publish a release, its `v*` tag can never
be moved or deleted, so a wrong release needs a new version.

## Steps the owner runs

1. **Dry run.** Actions > publish > Run workflow on `main`, inputs left at
   their defaults (`release` off), or
   `gh workflow run publish.yml -R emindeniz99/xlua-ng --ref main`.
   Every job must pass except possibly the two reproducibility jobs (below).
   Download `upm-package` and check it in a Unity project (step 5).
2. **Bump the version** in a commit on `main`: set `VERSION` (for example
   `0.1.0`), rename `## 0.0.1-dev (unreleased)` in `CHANGELOG.md` to
   `## 0.1.0 (<date>)` and write what changed, and add an empty
   `## 0.1.1-dev (unreleased)` section above it only after the release.
   README's "Work in progress" note says nothing is published; change it
   in the same commit.
3. **Release run:** Run workflow on `main` with `release` on (keep
   `draft` and `prerelease` on for the first one):
   `gh workflow run publish.yml -R emindeniz99/xlua-ng --ref main -f release=true`.
4. **Check the draft** (step 5), then publish it on the releases page. That
   creates the tag `v<VERSION>` on the release commit.
5. **Verify** the published files:
   - `sha256sum -c SHA256SUMS.txt` over the downloaded files.
   - The package in a fresh project of the oldest and the newest editor:
     `python3 Test/UnityMatrix/prepare.py --output DIR --unity-version 2021.3.45f2
     --platform macos --editor-tools --source-gen DIR --fast-enter-play-mode
     --package-tarball com.xlua-ng.xlua-<version>.tgz --samples`, then
     `Test/UnityMatrix/editor_tests.py run` (Test/README.md, "UPM package
     layout"); the same with 6000.x.
   - An IL2CPP player from it:
     `python3 Test/MobileSmoke/prepare.py --output DIR --package-tarball
     com.xlua-ng.xlua-<version>.tgz --source-gen DIR`, `SmokeBuild.BuildAndroid`,
     run it on a device or the emulator (Test/MobileSmoke/README.md).
   - The run's summary: the package's SHA-256, `SHA256SUMS.txt`, and the
     tables of the reproducibility jobs.
6. **Git URL install (optional):** after the release is published, run the
   workflow again on the same commit with `upm_branch` on and `release` off:
   `gh workflow run publish.yml -R emindeniz99/xlua-ng --ref main -f upm_branch=true`.
   It rebuilds and pushes the package; compare its SHA-256 in the run summary
   with the release's. Then install
   `https://github.com/emindeniz99/xlua-ng.git#upm/v<VERSION>` in a fresh
   project ("Add package from git URL").
7. **After the release:** open `## <next>-dev (unreleased)` in
   `CHANGELOG.md`, set `VERSION` to it, and update ROADMAP.md.

## Reproducibility

What the workflow checks and what was measured (2026-10-19, Lua 5.5.1, NDK
r30, Xcode 27.1, on a Mac):

- **Packaging:** the `upm` job assembles the package twice, the second time
  with a fresh build of the source generator, and requires the same bytes;
  the release archives are packed twice and compared, as before. Locally,
  two tarballs from the same plugins with two generator builds had the same
  SHA-256 (`b1ce00af...`).
- **Native plugins:** the jobs `reproducibility-linux` (Android with its
  variants, Linux) and `reproducibility-apple` (macOS, iOS with its
  variants) build Lua 5.5.1 again on another runner, in the same checkout
  path, and compare every file with the release build. A difference is a
  warning in the summary and fails only that job (`continue-on-error`); the
  release goes on. Not run on hosted runners yet.
  - Android: a rebuild in the same build directory gave the same bytes for
    all four ABIs. In another directory only the 20-byte GNU build ID
    differed: it hashes the linked library, whose DWARF holds the absolute
    paths. The same checkout path on every runner avoids that.
  - macOS (bundle, dylib) and iOS (device, simulator): the same bytes from
    two build directories.
  - Windows: not checked. MSVC writes a timestamp into every DLL unless
    linked with `/Brepro`.
  - `xlua-tools`: not reproducible; `mcs` gives every assembly a new MVID.

## Symbols and stripping

- Android: the plugins are stripped (`llvm-strip --strip-unneeded`) and the
  linked libraries go to `android-symbols_<tag>.tgz` (since 2026-10-18).
- iOS: static libraries keep their symbols; Xcode strips the player when it
  links it.
- Windows: the Release DLLs carry no debug information and no PDB is
  shipped.
- macOS: not stripped. `strip -x` would save 29 KB of the 414 KB arm64
  dylib and 62 KB of the 823 KB universal bundle (7%), but it breaks the
  ad-hoc code signature, so the plugins would need `codesign -s -` again
  and a check in a player. Left for a decision.
- Linux: not stripped and not measured.

## Not in the release, and why

- **UWP (`Plugins/WSA`):** `make_uwp*.bat` ask for the Visual Studio 2019
  generator, which `windows-2022` does not have; the scripts go on after the
  failure and the run of 2026-10-18 (`build.yml` 39168098003) had no WSA
  DLLs. The package leaves UWP out until the scripts use Visual Studio 2022.
- **LuaJIT on Windows:** the same run's `plugin_luajit_window` and
  `plugin_luajit_window32` artifacts held no DLL, so `luajit_<tag>.tgz` has
  no Windows x64 or x86 LuaJIT.
- **OpenHarmony in the package:** `Assets/Plugins` has no import settings
  for it; the plugins are in the `lua5*_<tag>.tgz` archives.
- **Lua 5.4, 5.3 and LuaJIT in the package:** the package has Lua 5.5.1
  only. Another runtime means copying the package into `Packages/` (an
  embedded package) and replacing its plugins with an archive above.
- **The hotfix tools in the package:** the Editor runs
  `Tools/XLuaHotfixInject.exe` from the project folder
  (`Hotfix.cs`, `SetupValidator.CheckHotfixTools`), so they are a release
  file of their own. Shipping them in `Tools~/` of the package and looking
  there when `Tools/` has none would make hotfix work without the download.
- **`LuaScript` in the Editor from the package:** the default plugins are
  the legacy preset's, built without `XLUA_LUASCRIPT`, and the Editor loads
  the default plugin. A project with `XLUA_CORE_ONLY` therefore gets
  `LuaScript` in Android and iOS players (the `core` variant) but not in the
  Editor or desktop players, and a project with `XLUA_LUASCRIPT` next to the
  bridge gets it nowhere. This follows open question 16 of
  [core-luascript.md](core-luascript.md) (`XLUA_LUASCRIPT=ON` by default,
  about 7 KB native).

## Decisions taken here

- **The source generator is required in a release package**
  (`make_upm_package.py --release` refuses a package without it). A package
  without it binds everything through reflection and fails `[CSharpCallLua]`
  delegates in IL2CPP players; tests still build such packages on purpose.
- **One version file**, `VERSION`, read by `make_upm_package.py` (its default
  `--version`) and by `publish.yml` (the tag `v<VERSION>`); the workflow has
  no tag input any more.
- **`CHANGELOG.md`** at the repository root is the changelog; the package
  carries it as its `CHANGELOG.md`, the release notes are its section for the
  version, and the website shows it ("Changelog").
- **The package's plugin set** is `RELEASE_PLUGINS` in
  `Tools/make_upm_package.py`: the variants the plugin selector takes on the
  platforms whose build scripts build variants (Android, iOS). Desktop
  minimal projects use the default plugin, which runs the minimal C# too.
