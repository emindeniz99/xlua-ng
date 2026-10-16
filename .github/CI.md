# Native build and test artifacts

Use **Native build and test (no release)** (`native-build.yml`) for native trial
builds. Use **Unity editor matrix** (`unity-matrix.yml`) for the real Unity
editor checks.
Use **docs** (`docs.yml`) to build the Starlight site and deploy it to GitHub
Pages; set the Pages source to "GitHub Actions" first. **Renovate**
(`renovate.yml`) opens dependency update PRs; it needs the `RENOVATE_TOKEN`
secret (see the checklist below).
All workflows start only from **Actions → Run workflow** while the repository
is private.
It has `workflow_dispatch`, read-only repository permissions, and no tag or
release creation step. Artifacts expire after seven days.

After the workflow is pushed to your fork's default branch:

1. Open **Actions → Native build and test (no release) → Run workflow**.
2. Select the branch and run it.
3. Open the completed run and download the `xlua-lua-<version>-<platform>`
   artifacts. Linux managed test logs are uploaded separately, including on
   test failure when a log was produced.

The Unity workflow has five editor legs: 2021.3.45f2, 2022.3.62f3,
6000.0.84f1 (Unity 6.0 LTS), 6000.3.26f1 (Unity 6.3 LTS), and 6000.6.6f1
(Unity 6.6). Only releases a free Personal licence can activate are used; later
2021.3 and 2022.3 patches are paid Extended LTS. Each leg uses a digest-pinned GameCI image, builds a fresh Lua
5.5.1 Linux plugin, and runs the EditMode smoke suite. Unity tests require
repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD`;
`UNITY_SERIAL` is only needed for a serial-based Professional license. Set the
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
macOS ARM64, Android (four ABIs), and iOS ARM64 (deployment target 15).
macOS and Linux run native export, GC/profiler and native regression checks. Linux additionally
installs Mono 6.14.1 from Homebrew (the same bottle as macOS), compiles the
existing general C# suite and requires 280 passing tests in both normal and
coroutine modes, through reflection, generated wrappers and `GEN_CODE_MINIMIZE`
generated wrappers. No Unity license or signing secret is needed
for these native builds.

Every Android job (the matrix above and the Android jobs in `build.yml` and
`publish.yml`, LuaJIT included) uses Android NDK r30 (the current LTS) at API 21
on `ubuntu-26.04`. `.github/scripts/install-android-ndk.sh` uses the image's
copy when it has r30 and otherwise downloads the Linux archive and checks its
pinned SHA-1 and SHA-256. Each job then runs `Test/Native/check_android_elf.py`,
which checks the machine type per ABI, 16 KB LOAD-segment alignment, and every
symbol the managed side imports.

**CodeQL** (`codeql.yml`) analyzes C/C++ from a traced libxlua build for Lua
5.3.6, 5.4.9 and 5.5.1, and C# without a build. Code scanning on a private
repository needs GitHub Advanced Security, so the workflow is manual. Its
comment lists the push, pull request and weekly triggers to restore when the
repository is public. **Native analysis** (`analysis.yml`, manual) runs
`Test/Native/analyze.sh`, `Test/Native/coverage.sh` and `Test/Fuzz/run.sh`
(seconds per target as an input) and uploads logs, coverage HTML and crash
reproducers. Check workflow changes with
`mise exec zizmor@latest -- zizmor --offline .github/workflows/`, which must
report no findings.

The Unity editor matrix does not run mobile players. Device tests, UWP, OHOS, LuaJIT,
Windows x86, macOS Intel and old iOS deployment targets are outside this new
workflow's matrix. The original `build.yml` and `publish.yml` are legacy
upstream workflows. Their Android jobs now use NDK r30; the same scripts were
verified in a container (see `build/README.md`) but have not yet run on hosted
runners, and the other jobs have not been validated for the new runtime matrix.
In particular, **do not run `publish.yml` for trial artifacts**: it creates a
GitHub release (by default a draft prerelease, which stays hidden until it is
published by hand). Use `native-build.yml` instead.

Verification: `actionlint` passes; the managed runner passes against all three
rebuilt macOS plugins. Hosted native run
[`37694199598`](https://github.com/emindeniz99/xlua-ng/actions/runs/37694199598)
completed with all 15 matrix jobs passing, including Android NDK builds, and
its Lua 5.5.1 Android artifact was downloaded and checked for all four ABIs.
The Unity editor matrix is still pending because the private repository has no
`UNITY_LICENSE`, `UNITY_EMAIL`, or `UNITY_PASSWORD` secrets yet.

References: [manual workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch)
and [workflow artifacts](https://docs.github.com/en/actions/tutorials/store-and-share-data).

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
2. `gh workflow run unity-matrix.yml -R emindeniz99/xlua-ng --ref main`

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
Mono.Cecil (checksum pins) and vendored sources (Lua, LuaJIT, LuaSocket,
luaunit, ios-cmake) stay manual.

The same Unity smoke test runs locally without secrets:
`python3 Test/UnityMatrix/prepare.py --platform macos --editor-tools ...`, then
the editor in batch mode with `-runTests -testPlatform EditMode`.
