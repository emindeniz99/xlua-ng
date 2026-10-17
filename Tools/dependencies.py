#!/usr/bin/env python3
"""Generate the dependency inventory (DEPENDENCIES.md) and SBOM (sbom.cdx.json).

One source of truth: the component data below plus a few files that are read
at generation time so they cannot drift:

  * .github/workflows/*.yml    GitHub Actions, runner labels, Unity matrix
  * docs/source/package.json   docs site npm dependencies and pnpm
  * mise.toml                  pinned local build and test tools
  * vendored headers           Lua, LuaSocket and LuaJIT versions
  * General/vs2013/*.csproj    .NET target frameworks

"Latest" values are recorded by hand on CHECKED_ON (see the LATEST tables) and
never fetched, so the output is deterministic and `--check` is reproducible.
To refresh: re-verify each LATEST entry against its registry, bump CHECKED_ON,
run `python3 Tools/dependencies.py`, and commit both generated files.

    python3 Tools/dependencies.py           write both files
    python3 Tools/dependencies.py --check   exit 1 if committed files differ

A fact the script asserts about a file (`need`) fails with exit 2 when the file
no longer says it, so stale hand-written data is noticed instead of shipped.
"""
import argparse
import hashlib
import json
import re
import sys
import tomllib
import uuid
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
CHECKED_ON = "2026-10-15"

CATEGORIES = [
    ("runtime", "Vendored runtimes and libraries (C)"),
    ("helper", "Vendored build helpers"),
    ("native", "Native build tooling and platform SDKs"),
    ("managed", "Managed code: .NET, Mono, Unity"),
    ("devtools", "Local dev and test tooling"),
    ("ci", "CI: GitHub Actions, runner images, container images"),
    ("docs", "Docs site (npm)"),
]
STATUSES = {
    "current": "latest release, or the newest still-supported one in its line",
    "behind": "a newer release exists",
    "EOL": "upstream no longer maintains this release or series",
    "deprecated": "superseded or abandoned upstream, still works",
    "unknown": "not verified (see note)",
}
SPDX = {"MIT", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "PSF-2.0"}

# xLua's own code. Listed for orientation; not dependencies, not in the SBOM.
FIRST_PARTY = [
    ("Assets/XLua/Src", "C# bridge and code generator (Unity)"),
    ("General/Src", "Standalone tools: XLuaGenerate, XLuaHotfixInject, FilesSignature, KeyPairsGen"),
    ("build/xlua.c, build/i64lib.*, build/memory_leak_checker.c", "Native bridge, int64 library, leak checker"),
    ("build/CMakeLists.txt, build/make_*, build/luac", "Native build entry points"),
    ("Assets/Plugins/WebGL, Assets/Plugins/iOS/HotfixFlags.cpp", "Platform glue (WebGL compiles the build/ sources as one translation unit)"),
    ("Assets/XLua/Resources/**/*.lua.txt", "Lua helper modules (util, tdr, profiler, memory)"),
    ("build/lua-*/ patches", "xLua changes to vendored Lua, listed in build/LUA_PATCHES.md"),
]


# ---------------------------------------------------------------- helpers
def read(rel):
    p = REPO / rel
    if not p.is_file():
        sys.exit(f"dependencies.py: missing {rel}")
    return p.read_text(encoding="utf-8", errors="replace")


def need(rel, needle, wide=False):
    """Fail loudly when a hand-written fact is no longer true."""
    if wide:  # UTF-16 strings inside a .NET assembly
        ok = needle.encode("utf-16le") in (REPO / rel).read_bytes()
    else:
        ok = needle in read(rel)
    if not ok:
        sys.exit(f"dependencies.py: stale data, {rel} no longer contains {needle!r}")


def lua_version(rel):
    header = rel + "/src/lua.h" if (REPO / rel / "src/lua.h").is_file() else rel + "/lua.h"
    text = read(header)
    parts = []
    for part in ("MAJOR", "MINOR", "RELEASE"):
        m = re.search(rf'^#define LUA_VERSION_{part}(?:_N)?\s+"?(\d+)"?\s*$', text, re.M)
        parts.append(m.group(1))
    return ".".join(parts)


def row(cat, name, version, where, license, upstream, latest, status, note="", *,
        purl=None, ctype="library", vdisp=None, hashes=None, vcs=None):
    assert status in STATUSES, status
    return dict(cat=cat, name=name, version=version, vdisp=vdisp or version or "unversioned",
                where=where, license=license, upstream=upstream, latest=latest,
                status=status, note=note, purl=purl, ctype=ctype, hashes=hashes, vcs=vcs)


def compare(version, latest):
    return "current" if version == latest else "behind"


# ------------------------------------------------- LATEST tables (by hand)
# Verified on CHECKED_ON. Sources are named per table.

# GitHub releases/tags API (gh api repos/<r>/releases, .../commits/<tag> to
# confirm the pinned SHA). value: (latest tag, license, note)
ACTIONS_LATEST = {
    "actions/cache": ("v6.1.0", "MIT", ""),
    "actions/checkout": ("v7.0.1", "MIT", ""),
    "actions/download-artifact": ("v8.0.2", "MIT", ""),
    "actions/upload-artifact": ("v7.0.2", "MIT", ""),
    "github/codeql-action": ("v4.39.0", "MIT", "v4.39.0 was published on 2026-10-15; v3.39.0 is the parallel v3 line."),
    "game-ci/unity-builder": ("v6.0.0", "MIT", "Checked 2026-10-16: v6.0.0 (2026-09-10) hands the run to the GameCI CLI; v5.0.1 keeps the build.sh that the -executeMethod steps of unity-matrix.yml were written against."),
    "game-ci/unity-test-runner": ("v4.4.0", "MIT", "v4.4.0 was published 10 minutes before v4.3.2 on 2026-09-09, so the releases/latest API still answers v4.3.2."),
    "ilammy/msvc-dev-cmd": ("v1.13.0", "MIT", "No release since 2024-01-01."),
    "microsoft/setup-msbuild": ("v3", "MIT", "Release tag is the floating-style name v3 (same commit as v3.0.0)."),
    "renovatebot/github-action": ("v46.3.8", "AGPL-3.0", "Checked 2026-10-16: v46.3.8 (2026-10-12) is still inside the seven-day window."),
}

# Renovate CLI, npm dist-tags.latest checked 2026-10-16. The workflow pins a
# release past the seven-day window, so this row normally reads "behind".
RENOVATE_LATEST = "44.164.7"

# npm registry dist-tags.latest. value: (latest, license)
# Latest npm versions, checked 2026-10-15. Newer releases inside the docs'
# seven-day quarantine show as "behind" until they age in.
NPM_LATEST = {
    "astro": ("7.4.0", "MIT"), "@astrojs/starlight": ("0.42.6", "MIT"),
    "@astrojs/markdown-remark": ("7.3.3", "MIT"),
}

# SHA-256 of luaunit.lua from github.com/bluebird75/luaunit tag LUAUNIT_V3_5
# (raw.githubusercontent.com, 2026-10-16); the vendored file must match it.
LUAUNIT_SHA256 = "150ed14606ea66c6a02b3142dde02bdf6d48764fcd2af6bf85bfecd7d98a7d0b"

# mise tools: name -> (license, upstream, latest, purl prefix, note). Latest from
# GitHub releases (cmake, ninja), nodejs.org/dist (node), endoflife.date (python).
MISE_INFO = {
    "cmake": ("BSD-3-Clause", "https://cmake.org/", "4.4.4", "pkg:github/Kitware/CMake@v", "4.5.0-rc1 exists; Homebrew also ships 4.4.4."),
    "ninja": ("Apache-2.0", "https://ninja-build.org/", "1.13.2", "pkg:github/ninja-build/ninja@v", ""),
    "node": ("MIT", "https://nodejs.org/", "24.22.0", "pkg:github/nodejs/node@v", "Compared against the newest 24.x LTS (Krypton, supported to 2028-04-30). Node 26.12.0 is the newest line."),
    "python": ("PSF-2.0", "https://www.python.org/", "3.14.8", "pkg:github/python/cpython@v", "Compared against the 3.14 line. Python 3.15.0 is out."),
}

# unity: series -> (latest patch, series status, note). endoflife.date/unity.
UNITY_SERIES = {
    "2021.3": ("2021.3.45f2", "EOL", "Standard support ended 2025-02-18. This is the last patch free Personal licences can activate."),
    "2022.3": ("2022.3.62f3", "EOL", "Standard support ended 2025-05-07. This is the last patch free Personal licences can activate."),
    "6000.0": ("6000.0.85f1", None, "Unity 6.0 LTS support ends 2026-10-16."),
    "6000.3": ("6000.3.26f1", None, "Unity 6.3 LTS, supported to 2027-12-04."),
    "6000.6": ("6000.6.6f1", None, "Newest Unity 6 release stream, not LTS."),
}
UNITY_LATEST_TEST_FRAMEWORK = "1.4.6"  # packages.unity.com dist-tags.latest
GAMECI_DOCKER_LATEST = "3.2.2"  # github.com/game-ci/docker releases

# GitHub-hosted runner images. actions/runner-images README. value: (status, note)
RUNNERS = {
    "ubuntu-24.04": ("current", "ubuntu-26.04 is also available."),
    "macos-15": ("current", "macos-26 is also available. Xcode version follows the image default and is not pinned."),
    "windows-2022": ("current", "windows-2025 is also available. Provides Visual Studio 2022."),
    "windows-11-arm": ("current", "Windows 11 on Arm64, a standard runner for private repositories too. Builds and checks the Windows ARM64 plugins natively."),
}


# ------------------------------------------------------ dynamic readers
def workflow_files():
    return sorted((REPO / ".github/workflows").glob("*.yml"))


def parse_actions():
    found = {}
    for wf in workflow_files():
        for line in wf.read_text(encoding="utf-8").splitlines():
            m = re.match(r"\s*(?:-\s+)?uses:\s*([\w.-]+/[\w.-]+)(?:/\S+)?@(\S+?)(?:\s+#\s*(\S+))?\s*$", line)
            if m:
                repo, ref, ver = m.groups()
                e = found.setdefault((repo, ref, ver), Counter())
                e[wf.name] += 1
    return found


def parse_runners():
    found = {}
    for wf in workflow_files():
        for line in wf.read_text(encoding="utf-8").splitlines():
            m = re.match(r"\s*(?:runs-on|runner):\s*([\w.-]+)\s*$", line)
            if m:
                found.setdefault(m.group(1), set()).add(wf.name)
    return found


def parse_unity_matrix():
    text = read(".github/workflows/unity-matrix.yml")
    pat = r"unity-version:\s*(\S+)\s+test-framework:\s*(\S+)\s+editor-image:\s*unityci/editor@sha256:([0-9a-f]{64})"
    return re.findall(pat, text)


# ------------------------------------------------------------ the inventory
def build():
    rows = []
    add = rows.append

    # ---- vendored runtimes
    # Versions come from the vendored headers. Licenses are the MIT notices in
    # lua.h / luajit COPYRIGHT and the list in LICENSE.TXT. Latest: lua.org/ftp
    # directory listing and versions.html, tags on github.com/lua/lua.
    v55, v54, v53 = (lua_version(f"build/lua-{v}") for v in ("5.5.1", "5.4.9", "5.3.6"))
    assert (v55, v54, v53) == ("5.5.1", "5.4.9", "5.3.6")
    need("Assets/Plugins/WebGL/xlua_webgl.cpp", "build/lua-5.5.1/src/")
    add(row("runtime", "Lua", v55, "`build/lua-5.5.1`", "MIT", "https://www.lua.org/",
            "5.5.1 (2026-07-24)", "current",
            "Default runtime, and the only one the Unity WebGL build compiles (Assets/Plugins/WebGL/xlua_webgl.cpp). lua.org versions.html carries a hidden note that 5.5.2 is being prepared. Integration notes: build/LUA55.md.",
            purl="pkg:github/lua/lua@v5.5.1", vcs="https://github.com/lua/lua"))
    add(row("runtime", "Lua", v54, "`build/lua-5.4.9`", "MIT", "https://www.lua.org/",
            "5.4.9 (2026-08-10)", "current",
            "Latest 5.4 release. endoflife.date marks the 5.4 series end of life since 2026-08-25. xLua patches: build/LUA_PATCHES.md.",
            purl="pkg:github/lua/lua@v5.4.9", vcs="https://github.com/lua/lua"))
    add(row("runtime", "Lua", v53, "`build/lua-5.3.6`, `build/luac`", "MIT", "https://www.lua.org/",
            "5.3.6 (2020-09-14)", "EOL",
            "Final 5.3 release; the series ended 2020-09-25. Carries the opt-in LUAC_COMPATIBLE_FORMAT bytecode patch (build/LUA_PATCHES.md).",
            purl="pkg:github/lua/lua@v5.3.6", vcs="https://github.com/lua/lua"))
    need("LICENSE.TXT", "2a8e017e")
    relver = read("build/luajit-2.1/.relver").strip()
    assert relver == "1791988261"  # = commit time 2026-10-14T14:31:01Z
    add(row("runtime", "LuaJIT", f"2.1.{relver}", "`build/luajit-2.1`", "MIT", "https://luajit.org/",
            "v2.1 branch head 2a8e017e (2026-10-14)", "current",
            "Rolling v2.1 branch at upstream commit 2a8e017ef1ece6a95b983ffca073d30609dce7c4; the version suffix is .relver (commit time). Upstream publishes no release tags for 2.1. The iOS LuaJIT script is legacy (see native tooling); the Android script follows LuaJIT's NDK clang cross-compile instructions.",
            purl="pkg:github/LuaJIT/LuaJIT@2a8e017ef1ece6a95b983ffca073d30609dce7c4",
            vdisp=f"2.1.{relver} (2a8e017e)", vcs="https://github.com/LuaJIT/LuaJIT"))
    sock = re.search(r'LUASOCKET_VERSION\s+"LuaSocket ([^"]+)"', read("build/luasocket/luasocket.h")).group(1)
    # The v3.1.0 release still says 3.0.0 in luasocket.h; compat.c is new in 3.1.0.
    assert sock == "3.0.0"
    need("build/luasocket/compat.c", "luasocket")
    add(row("runtime", "LuaSocket", "3.1.0", "`build/luasocket`", "MIT", "https://github.com/lunarmodules/luasocket",
            "3.1.0 (2022-07-27)", "current",
            "Unmodified upstream v3.1.0 sources for the C core (14 files in CMake); the Lua helper modules are not shipped. luasocket.h in that release still reports 3.0.0. master is ahead of 3.1.0 (2026-09-02) without a release.",
            purl="pkg:github/lunarmodules/luasocket@v3.1.0", vcs="https://github.com/lunarmodules/luasocket"))

    # ---- vendored build helpers
    # Vendored unmodified from bluebird75/luaunit tag LUAUNIT_V3_5 (release published 2026-03-26).
    luaunit = "Test/UnitTest/StreamingAssets/luaunit.lua"
    need(luaunit, "M.VERSION='3.5'")
    luaunit_sha = hashlib.sha256((REPO / luaunit).read_bytes()).hexdigest()
    if luaunit_sha != LUAUNIT_SHA256:
        sys.exit(f"dependencies.py: stale data, {luaunit} SHA-256 is {luaunit_sha}, expected the upstream 3.5 file")
    add(row("helper", "luaunit (Lua unit-test framework)", "3.5", f"`{luaunit}`", "BSD-2-Clause",
            "https://github.com/bluebird75/luaunit", "3.5 (2026-03-26)", "current",
            f"Unmodified upstream luaunit.lua (SHA-256 {LUAUNIT_SHA256[:12]}). Test-only: runs the managed suite through "
            "Test/UnitTest/StreamingAssets/luaunit_compat.lua, which keeps the suite's TestCase/ASSERT_EQ API. "
            "Pure Lua; upstream supports Lua 5.1 to 5.5 and LuaJIT.",
            purl="pkg:github/bluebird75/luaunit@LUAUNIT_V3_5", vcs="https://github.com/bluebird75/luaunit",
            hashes=[{"alg": "SHA-256", "content": LUAUNIT_SHA256}]))
    # Vendored unmodified from leetal/ios-cmake tag 4.6.1; visionOS support only exists in 4.x.
    need("build/cmake/ios.toolchain.cmake", "VISIONOS")
    add(row("helper", "ios-cmake toolchain", "4.6.1", "`build/cmake/ios.toolchain.cmake`", "BSD-3-Clause",
            "https://github.com/leetal/ios-cmake", "4.6.1 (2026-09-24)", "current",
            "Unmodified upstream file. Scripts pass PLATFORM=OS64 and DEPLOYMENT_TARGET=15.0.",
            purl="pkg:github/leetal/ios-cmake@4.6.1", vcs="https://github.com/leetal/ios-cmake"))

    # ---- native build tooling
    for rel in ("build/CMakeLists.txt", "build/luac/CMakeLists.txt"):
        need(rel, "cmake_minimum_required(VERSION 3.22...4.4)")
    add(row("native", "CMake (minimum required)", "3.22 (policies up to 4.4)", "`build/CMakeLists.txt`, `build/luac/CMakeLists.txt`", "BSD-3-Clause",
            "https://cmake.org/", "4.4.4 (2026-10-02)", "current",
            "3.22 is the CMake in Ubuntu 22.04; the range opts into policies through 4.4. Local builds use the mise pin; CI uses the runner image's CMake.", ctype="application"))
    add(row("native", "Android NDK", "r30 (30.0.16248370)",
            "`.github/scripts/install-android-ndk.sh`, `.github/workflows/native-build.yml`, `.github/workflows/build.yml`, `.github/workflows/publish.yml`, `build/android_common.sh`",
            "Apache-2.0 and component licenses",
            "https://developer.android.com/ndk", "r30 (2026-09-08)", "current",
            "Current LTS. Builds Lua 5.3.6/5.4.9/5.5.1 and LuaJIT for four ABIs at API 21. The ubuntu-26.04 image preinstalls only 27.3, 28.2 and 29.0, so the installer downloads android-ndk-r30-linux.zip and checks pinned SHA-1 and SHA-256 (it uses the image copy once r30 is preinstalled). Unity 6 bundles r27c, 2022.3 r23b, 2021.3 r21d; native plugins do not need to match.",
            ctype="application", vcs="https://github.com/android/ndk"))
    need(".github/scripts/install-android-ndk.sh", "30.0.16248370")
    need(".github/scripts/install-android-ndk.sh", "5107f898313790e449e87eee2183d9a20602dee9")
    need(".github/workflows/native-build.yml", "install-android-ndk.sh")
    need(".github/workflows/build.yml", "install-android-ndk.sh")
    need(".github/workflows/publish.yml", "install-android-ndk.sh")
    add(row("native", "Android API levels", "21",
            "`build/android_common.sh`, `.github/workflows/native-build.yml`",
            "n/a", "https://developer.android.com/guide/topics/manifest/uses-sdk-element#api-level-table", "not checked", "unknown",
            "Policy value, not a package. API 21 is the lowest level NDK r30 supports (meta/platforms.json). Unity requires 22 (2021.3, 2022.3), 23 (6000.0) or 25 (6000.3), so a plugin built for 21 loads on every supported player.", ctype="data"))
    need("build/android_common.sh", 'ANDROID_API_LEVEL:-21')
    add(row("native", "Apple deployment targets", "iOS 15.0; macOS 12.0 (LuaJIT)",
            "`build/make_ios_*.sh`, `.github/workflows/native-build.yml`, `build/make_osx_*luajit.sh`",
            "n/a", "https://developer.apple.com/support/xcode/", "iOS 27.0.1 is current", "unknown",
            "Policy value, not a package. Every iOS script passes 15.0 to ios-cmake; macOS 12.0 is the lowest target Xcode 27 accepts.", ctype="data"))
    need("build/make_ios_lua55.sh", "IOS_DEPLOYMENT_TARGET:-15.0")
    need("build/make_osx_luajit.sh", "MACOSX_DEPLOYMENT_TARGET=12.0")
    add(row("native", "Xcode (CMake -G Xcode, xcodebuild)", "27.1 (local validation); CI follows the macos-26 image",
            "`build/make_osx_*.sh`, `build/make_ios_*.sh`, `build/README.md`", "Proprietary (Apple)",
            "https://developer.apple.com/xcode/", "not checked", "unknown",
            "Latest Xcode could not be verified from a public API. Every macOS and iOS script uses the Xcode generator.", ctype="application"))
    need("build/README.md", "Xcode 27.1")
    add(row("native", "OpenHarmony native SDK", "26.0.0.38 (7.0-Release, API 26)",
            "`.github/scripts/install-ohos-sdk.sh`, `.github/workflows/build.yml`, `.github/workflows/publish.yml`, `build/make_ohos_*.sh`",
            "Apache-2.0 and component licenses (NOTICE.txt in the SDK)", "https://www.openharmony.cn/", "7.0-Release (2026-08-29 respin)", "current",
            "The installer downloads ohos-sdk-windows_linux-public_20260829.tar.gz from repo.huaweicloud.com, checks the SHA-256 the mirror publishes and unpacks only the Linux native toolchain (LLVM 15). Builds arm64-v8a, armeabi-v7a and x86_64 (emulator). 6.1-LTS (API 23) is the long-term branch.",
            ctype="application"))
    need(".github/scripts/install-ohos-sdk.sh", "native-linux-x64-26.0.0.38-Release.zip")
    need(".github/scripts/install-ohos-sdk.sh", "6bf6ae1efe8de0e8bd15ddbd7fac58bcb54d9620262a541b9b219439317a4c42")
    need(".github/workflows/build.yml", "install-ohos-sdk.sh")
    need(".github/workflows/publish.yml", "install-ohos-sdk.sh")
    need("build/make_uwp_lua55.bat", "Visual Studio 16 2019")
    add(row("native", "Visual Studio 2019 (MSVC v142)", "16", "`build/make_uwp*.bat`, `build/make_win32_luajit.bat`, `build/make_win64_luajit.bat`",
            "Proprietary (Microsoft)", "https://visualstudio.microsoft.com/", "VS 2026 18.10.4", "behind",
            "Named by the UWP and the x86/x64 LuaJIT scripts; support to 2029-04-10. The other Windows scripts let CMake pick the newest installed Visual Studio. CI runs on windows-2022 (VS 2022 17.x) and windows-11-arm.", ctype="application"))
    need("build/make_win64_luajit_gc64.bat", "Visual Studio 15 2017")
    add(row("native", "Visual Studio 2017 (MSVC v141)", "15", "`build/make_win64_luajit_gc64.bat`",
            "Proprietary (Microsoft)", "https://visualstudio.microsoft.com/", "VS 2026 18.10.4", "behind",
            "Support to 2027-04-13.", ctype="application"))
    need("build/make_nx64_lua53.bat", "NINTENDO_SDK_ROOT")
    add(row("native", "Nintendo Switch SDK toolchain", None, "`build/make_nx64_lua53.bat`, `build/make_nx64_luajit_gc64.bat`",
            "Proprietary (Nintendo)", "https://developer.nintendo.com/", "not public", "unknown",
            "Reached through NINTENDO_SDK_ROOT; no version recorded. The LuaJIT script also needs Cygwin64.", ctype="application", vdisp="unspecified"))

    # ---- managed
    tools = read("Tools/build_tools.py")
    cecil = re.search(r'CECIL_VERSION = "([\d.]+)"', tools).group(1)
    cecil_sha512 = "".join(re.search(r'CECIL_SHA512 = \(([^)]*)\)', tools).group(1).replace('"', "").split())
    add(row("managed", "Mono.Cecil (+ Mdb, Pdb)", cecil, "`Tools/build_tools.py` (NuGet package, net40 assemblies)", "MIT",
            "https://github.com/jbevain/cecil", "0.11.6 (NuGet, 2024-10-02)", compare(cecil, "0.11.6"),
            "Used by XLuaHotfixInject. build_tools.py downloads the .nupkg from nuget.org, checks the pinned SHA-512 and copies "
            "the assemblies next to the tool; no binaries are committed. The GitHub releases page still shows 0.11.5.",
            purl=f"pkg:nuget/Mono.Cecil@{cecil}", vcs="https://github.com/jbevain/cecil",
            hashes=[{"alg": "SHA-512", "content": cecil_sha512}]))
    fw = Counter(re.findall(r"<TargetFrameworkVersion>(v[\d.]+)</TargetFrameworkVersion>",
                            "\n".join(p.read_text(encoding="utf-8-sig") for p in sorted((REPO / "General/vs2013").glob("*.csproj")))))
    fw_info = {
        "v3.5": ("deprecated", "Visual Studio 2013 era project files; Tools/build_tools.py builds the tools with Mono mcs instead."),
        "v4.0": ("EOL", "Microsoft ended support for .NET Framework 4.0 in 2016 (from memory; not re-verified)."),
    }
    for v, n in sorted(fw.items()):
        status, note = fw_info.get(v, ("unknown", "Not classified."))
        add(row("managed", f".NET Framework target {v}", v, f"`General/vs2013/*.csproj` ({n} projects, ToolsVersion 12.0 / VS 2013)", "Proprietary (Microsoft)",
                "https://dotnet.microsoft.com/", ".NET 10 LTS (10.0.13)", status, note, ctype="platform"))
    need("Test/Native/run_managed.py", '"--mono-version", default="6.14.1"')
    add(row("managed", "Mono (mcs, runtime)", "6.14.1", "`Test/Native/run_managed.py`, `Test/README.md`, `.github/workflows/native-build.yml`, `.github/workflows/analysis.yml`; Homebrew bottle", "MIT and others; see upstream",
            "https://www.mono-project.com/", "6.14.1 (WineHQ source mirror and Homebrew)", "current",
            "run_managed.py refuses other versions. Mono is now maintained by the Wine project; mono/mono on GitHub has seen no push since 2024-08.",
            purl="pkg:generic/mono@6.14.1?download_url=https://dl.winehq.org/mono/sources/mono/mono-6.14.1.tar.xz", ctype="application"))

    matrix = parse_unity_matrix()
    assert matrix, "unity-matrix.yml pattern changed"
    frameworks = {}
    for uver, tf, digest in matrix:
        latest, sstatus, snote = UNITY_SERIES[".".join(uver.split(".")[:2])]
        status = sstatus or ("current" if uver == latest else "behind")
        add(row("managed", "Unity Editor", uver, "`.github/workflows/unity-matrix.yml`", "Proprietary (Unity)",
                "https://unity.com/releases/editor/archive", f"{latest}", status, snote, ctype="application"))
        frameworks.setdefault(tf, []).append(uver)
    local = re.search(r'"--unity-version", default="([^"]+)"', read("Test/Examples/prepare.py")).group(1)
    need("Test/MobileSmoke/prepare.py", f'default="{local}"')
    add(row("managed", "Unity Editor (local mobile smoke tests)", local, "`Test/Examples/prepare.py`, `Test/MobileSmoke/prepare.py`, READMEs", "Proprietary (Unity)",
            "https://unity.com/releases/editor/archive", UNITY_SERIES["6000.3"][0], compare(local, UNITY_SERIES["6000.3"][0]),
            "Default for the device smoke scripts (6.3 LTS).", ctype="application"))
    for tf, users in sorted(frameworks.items()):
        add(row("managed", "Unity Test Framework (com.unity.test-framework)", tf, "`.github/workflows/unity-matrix.yml`, `Test/UnityMatrix/prepare.py`", "Proprietary (Unity)",
                "https://docs.unity3d.com/Packages/com.unity.test-framework@latest", UNITY_LATEST_TEST_FRAMEWORK,
                compare(tf, UNITY_LATEST_TEST_FRAMEWORK), f"Used by Unity {', '.join(users)}." + (" Older line chosen for the 2021.3 editor." if tf != UNITY_LATEST_TEST_FRAMEWORK else ""),
                ctype="library"))
    ugui = re.search(r'"com.unity.ugui": "([^"]+)"', read("Test/Examples/prepare.py")).group(1)
    add(row("managed", "Unity UI (com.unity.ugui)", ugui, "`Test/Examples/prepare.py`", "Proprietary (Unity)",
            "https://docs.unity3d.com/Packages/com.unity.ugui@latest", "registry lists only 3.0.0-exp.4", "unknown",
            "Example project only. The package registry exposes just experimental builds, so a stable latest could not be confirmed.", ctype="library"))

    # ---- local dev tooling (mise.toml)
    mise = tomllib.loads(read("mise.toml"))["tools"]
    for tool, ver in sorted(mise.items()):
        lic, up, latest, purl, note = MISE_INFO.get(tool, ("unknown", "", "not checked", None, "New tool in mise.toml: add it to MISE_INFO."))
        status = "unknown" if tool not in MISE_INFO else compare(ver, latest)
        add(row("devtools", tool, ver, "`mise.toml`", lic, up or "https://mise.jdx.dev/", latest, status, note,
                purl=(purl + ver) if purl else None, ctype="application"))
    pkg = json.loads(read("docs/source/package.json"))
    pm = pkg.get("packageManager", "")
    if pm.startswith("pnpm@"):
        pv = pm.split("@", 1)[1]
        add(row("devtools", "pnpm", pv, "`docs/source/package.json` (packageManager)", "MIT", "https://pnpm.io/",
                "12.11.2 (npm latest, 2026-10-09)", compare(pv, "12.11.2"),
                "Selected through mise idiomatic version files. endoflife.date lists 12.14.0 tags; npm dist-tag latest is 12.11.2.",
                purl=f"pkg:npm/pnpm@{pv}", ctype="application"))
    need("mise.toml", "Mono is not in the mise registry")
    add(row("devtools", "Homebrew (mono bottle)", None, "`Test/README.md`, `.github/workflows/native-build.yml`, `.github/workflows/analysis.yml`", "BSD-2-Clause",
            "https://brew.sh/", "n/a", "unknown",
            "Installs Mono 6.14.1 on macOS and Linux runners. The formula's mono version is the one run_managed.py requires.", ctype="application", vdisp="unpinned"))
    need("Test/Native/analyze.sh", "clang-tidy")
    need("Test/Native/coverage.sh", "llvm-cov")
    need("Test/Fuzz/run.sh", "-fsanitize=fuzzer")
    add(row("devtools", "Clang/LLVM (clang-tidy, clang --analyze, llvm-cov, libFuzzer)", None,
            "`Test/Native/analyze.sh`, `Test/Native/coverage.sh`, `Test/Fuzz/run.sh`, `.github/workflows/analysis.yml`",
            "Apache-2.0 WITH LLVM-exception", "https://github.com/llvm/llvm-project", "23.1.3 (2026-10-06)", "unknown",
            "Ubuntu apt packages, unpinned. Local runs used the ubuntu:26.04 default, 21.1.8; build/.clang-tidy needs clang-tidy 17+ for its list-form Checks.",
            ctype="application", vdisp="distribution default"))
    need(".github/CI.md", "zizmor@latest")
    add(row("devtools", "zizmor", None, "`.github/CI.md` (workflow audit, `mise exec zizmor@latest`)", "MIT",
            "https://github.com/zizmorcore/zizmor", "v1.31.0 (2026-10-15)", "unknown",
            "Not pinned. On 2026-10-15 mise resolved zizmor@latest to 1.30.1, a day behind the GitHub release.",
            ctype="application", vdisp="latest via mise"))

    # Lua's official test suites, downloaded and hash-checked by lua_tests.py.
    # Latest: the file list on https://www.lua.org/tests/ (checked 2026-10-17).
    suites = re.findall(r'^\s*"(5\.\d\.\d)": \("(lua-([\d.]+)-tests\.tar\.gz)", (\d+), "([0-9a-f]{64})"\),$',
                        read("Test/Native/lua_tests.py"), re.M)
    assert [s[0] for s in suites] == ["5.3.6", "5.4.9", "5.5.1"], suites
    need(".github/workflows/analysis.yml", "Test/Native/lua_tests.py")
    suite_dates = {"5.3.4": "2016-12-22", "5.4.9": "2026-08-10", "5.5.1": "2026-07-24"}
    for runtime, archive, sver, size, digest in suites:
        note = (f"Test data, not shipped: {archive} ({size} bytes, SHA-256 {digest[:12]}), run by "
                f"Test/Native/lua_tests.py against the vendored Lua {runtime}.")
        if sver != runtime:
            note += f" lua.org has no {runtime} suite; {sver} is the newest {sver.rsplit('.', 1)[0]} suite, as its page advises."
        add(row("devtools", "Lua test suite (lua.org)", sver,
                "`Test/Native/lua_tests.py`, `.github/workflows/analysis.yml`", "MIT", "https://www.lua.org/tests/",
                f"{sver} ({suite_dates[sver]})", "current", note,
                purl=f"pkg:generic/lua-tests@{sver}?download_url=https://www.lua.org/tests/{archive}",
                ctype="file", hashes=[{"alg": "SHA-256", "content": digest}]))

    # ---- CI
    for (repo, ref, ver), files in sorted(parse_actions().items()):
        latest, lic, note = ACTIONS_LATEST.get(repo, (None, "unknown", "New action: add it to ACTIONS_LATEST."))
        status = "unknown" if latest is None else compare(ver, latest)
        n = sum(files.values())
        name = f"{repo}"
        where = ", ".join(f"`.github/workflows/{f}`" for f in sorted(files)) + f" ({n} uses)"
        add(row("ci", name, ver, where, lic, f"https://github.com/{repo}", latest or "not checked", status,
                f"Pinned to {ref}." + (f" {note}" if note else ""),
                purl=f"pkg:githubactions/{repo}@{ref}", ctype="application", vdisp=f"{ver} ({ref[:7]})", vcs=f"https://github.com/{repo}"))
    renovate = re.search(r"^\s*renovate-version:\s*(\S+)\s*$", read(".github/workflows/renovate.yml"), re.M).group(1)
    add(row("ci", "Renovate CLI (ghcr.io/renovatebot/renovate image)", renovate,
            "`.github/workflows/renovate.yml`, `.github/renovate.json`", "AGPL-3.0", "https://docs.renovatebot.com/",
            f"{RENOVATE_LATEST} (npm, 2026-10-16)", compare(renovate, RENOVATE_LATEST),
            "Dependency update bot, manual runs only. The pin is a release older than seven days; Renovate bumps it using npm release dates.",
            purl=f"pkg:docker/renovatebot/renovate@{renovate}?repository_url=ghcr.io", ctype="container",
            vcs="https://github.com/renovatebot/renovate"))
    for label, files in sorted(parse_runners().items()):
        status, note = RUNNERS.get(label, ("unknown", "New runner label: add it to RUNNERS."))
        add(row("ci", f"Runner image {label}", None, ", ".join(f"`.github/workflows/{f}`" for f in sorted(files)), "n/a",
                "https://github.com/actions/runner-images", "see note", status, note, ctype="platform", vdisp=label))
    for uver, tf, digest in matrix:
        add(row("ci", "unityci/editor (GameCI image)", f"ubuntu-{uver}-base-{GAMECI_DOCKER_LATEST}", "`.github/workflows/unity-matrix.yml`", "MIT (image recipe); contains proprietary Unity",
                "https://github.com/game-ci/docker", f"base-{GAMECI_DOCKER_LATEST}", "current",
                f"Digest-pinned sha256:{digest[:12]}. Digest matched the Docker Hub tag on {CHECKED_ON}.",
                purl=f"pkg:docker/unityci/editor@sha256%3A{digest}", ctype="container", vdisp=f"ubuntu-{uver}-base-{GAMECI_DOCKER_LATEST}",
                hashes=[{"alg": "SHA-256", "content": digest}]))

    # ---- docs site (read from package.json at generation time)
    for section in ("dependencies", "devDependencies"):
        for name, spec in sorted(pkg.get(section, {}).items()):
            ver = spec.lstrip("^~")
            latest, lic = NPM_LATEST.get(name, (None, "unknown"))
            status = "unknown" if latest is None else compare(ver, latest)
            enc = name.replace("@", "%40", 1) if name.startswith("@") else name
            note = "" if latest else "Not in NPM_LATEST: add it after checking the registry."
            if spec != ver:
                note = (note + f" Range in package.json is {spec}.").strip()
            add(row("docs", name, ver, f"`docs/source/package.json` ({section})", lic, f"https://www.npmjs.com/package/{name}",
                    latest or "not checked", status, note, purl=f"pkg:npm/{enc}@{ver}"))
    return rows


# ------------------------------------------------------------------ output
def cell(text):
    return str(text).replace("|", "\\|").replace("\n", " ")


def render_markdown(rows):
    out = [
        "# Dependencies",
        "",
        "<!-- Generated by Tools/dependencies.py. Do not edit; edit the script and rerun it. -->",
        "",
        f"Inventory of everything xlua-ng vendors, builds with or runs on. \"Latest\" was checked on {CHECKED_ON}.",
        "A machine-readable CycloneDX 1.6 SBOM with the same components is in [sbom.cdx.json](sbom.cdx.json).",
        "",
        "Regenerate with `python3 Tools/dependencies.py`; CI can run `python3 Tools/dependencies.py --check`.",
        "Rows for GitHub Actions, runner labels, the Unity matrix, the Renovate pin, mise tools, pnpm and the docs npm packages",
        "are read from the repository on every run. Everything else is data in the script.",
        "",
        "Status values: " + "; ".join(f"**{k}**: {v}" for k, v in STATUSES.items()) + ".",
        "",
    ]
    counts = Counter(r["status"] for r in rows)
    out.append("Summary: " + ", ".join(f"{counts[s]} {s}" for s in STATUSES if counts[s]) + f" ({len(rows)} components).")
    out.append("")
    for key, title in CATEGORIES:
        group = [r for r in rows if r["cat"] == key]
        if not group:
            continue
        out += [f"## {title}", "",
                f"| Name | Version in repo | Where | License | Upstream | Latest (checked {CHECKED_ON}) | Status | Note |",
                "| --- | --- | --- | --- | --- | --- | --- | --- |"]
        for r in group:
            up = f"<{r['upstream']}>" if r["upstream"] else ""
            out.append("| " + " | ".join(cell(x) for x in (
                r["name"], r["vdisp"], r["where"], r["license"], up, r["latest"], r["status"], r["note"])) + " |")
        out.append("")
    out += ["## First-party code (not dependencies)", "", "| Path | What |", "| --- | --- |"]
    out += [f"| `{p}` | {cell(w)} |" for p, w in FIRST_PARTY]
    out += ["", "xLua itself is MIT licensed (Tencent); see [LICENSE.TXT](LICENSE.TXT) for the third-party notices.", ""]
    return "\n".join(out)


def render_sbom(rows):
    comps = []
    for r in rows:
        c = {"type": r["ctype"], "bom-ref": f"{r['cat']}/{r['name']}@{r['vdisp']}".replace(" ", "_"), "name": r["name"]}
        if r["version"]:
            c["version"] = r["version"]
        if r["purl"]:
            c["purl"] = r["purl"]
        lic = r["license"]
        if lic in SPDX:
            c["licenses"] = [{"license": {"id": lic}}]
        elif lic and lic not in ("n/a", "see upstream"):
            c["licenses"] = [{"license": {"name": lic}}]
        refs = []
        if r["upstream"]:
            refs.append({"type": "website", "url": r["upstream"]})
        if r["vcs"]:
            refs.append({"type": "vcs", "url": r["vcs"]})
        if refs:
            c["externalReferences"] = refs
        if r["hashes"]:
            c["hashes"] = r["hashes"]
        c["properties"] = [
            {"name": "xlua-ng:category", "value": r["cat"]},
            {"name": "xlua-ng:where", "value": r["where"].replace("`", "")},
            {"name": "xlua-ng:latest", "value": f"{r['latest']} (checked {CHECKED_ON})"},
            {"name": "xlua-ng:status", "value": r["status"]},
        ]
        comps.append(c)
    digest = hashlib.sha256(json.dumps(comps, sort_keys=True).encode()).hexdigest()
    bom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.6",
        "serialNumber": "urn:uuid:" + str(uuid.uuid5(uuid.NAMESPACE_URL, "https://github.com/emindeniz99/xlua-ng/sbom/" + digest)),
        "version": 1,
        "metadata": {
            "timestamp": f"{CHECKED_ON}T00:00:00Z",
            "tools": {"components": [{"type": "application", "name": "Tools/dependencies.py"}]},
            "component": {
                "type": "library", "bom-ref": "xlua-ng", "name": "xlua-ng",
                "description": "Lua scripting bridge for Unity and C# (fork of Tencent/xLua)",
                "licenses": [{"license": {"id": "MIT"}}],
                "externalReferences": [{"type": "vcs", "url": "https://github.com/emindeniz99/xlua-ng"},
                                       {"type": "website", "url": "https://github.com/Tencent/xLua"}],
            },
        },
        "components": comps,
    }
    return json.dumps(bom, indent=2, ensure_ascii=False) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="exit 1 if the committed files differ from the generated ones")
    args = ap.parse_args()
    rows = build()
    outputs = {"DEPENDENCIES.md": render_markdown(rows), "sbom.cdx.json": render_sbom(rows)}
    stale = []
    for name, text in outputs.items():
        path = REPO / name
        if args.check:
            if not path.is_file() or path.read_text(encoding="utf-8") != text:
                stale.append(name)
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
            print("wrote", name)
    if stale:
        print("out of date: " + ", ".join(stale) + "\nrun: python3 Tools/dependencies.py", file=sys.stderr)
        return 1
    if args.check:
        print("dependency inventory is up to date")
    return 0


if __name__ == "__main__":
    sys.exit(main())
