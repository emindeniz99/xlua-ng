# Native build and test artifacts

Use **Native build and test (no release)** (`native-build.yml`) for native trial
builds. Use **Unity editor matrix** (`unity-matrix.yml`) for the real Unity
editor checks.
It has `workflow_dispatch`, read-only repository permissions, and no tag or
release creation step. Artifacts expire after seven days.

After the workflow is pushed to your fork's default branch:

1. Open **Actions → Native build and test (no release) → Run workflow**.
2. Select the branch and run it.
3. Open the completed run and download the `xlua-lua-<version>-<platform>`
   artifacts. Linux managed test logs are uploaded separately, including on
   test failure when a log was produced.

The Unity workflow has five editor legs: 2021.3.45f2, 2022.3.62f3,
6000.0.82f1 (Unity 6.0 LTS), 6000.3.22f1 (Unity 6.3 LTS), and 6000.6.4f1
(Unity 6.6). Each leg uses a digest-pinned GameCI image, builds a fresh Lua
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
macOS and Linux run native export and GC/profiler checks. Linux additionally
compiles the existing general C# suite and requires 275 passing tests in
both normal and coroutine modes. No Unity license or signing secret is needed
for these native builds.

The Unity editor matrix does not run mobile players. Device tests, UWP, OHOS, LuaJIT,
Windows x86, macOS Intel and old iOS deployment targets are outside this new
workflow's matrix. The original `build.yml` and `publish.yml` are legacy
upstream workflows and have not been validated for the new runtime matrix.
In particular, **do not run `publish.yml` for trial artifacts**: it creates a
public release. Use `native-build.yml` instead.

Local verification: `actionlint` passes; the managed runner passes against all
three rebuilt macOS plugins. A GitHub-hosted run has not yet been executed:
the local checkout still points to Tencent/xLua and has not been pushed to a
user-owned fork. Hosted native and Unity editor builds remain unverified until
those runs.

References: [manual workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch)
and [workflow artifacts](https://docs.github.com/en/actions/tutorials/store-and-share-data).
