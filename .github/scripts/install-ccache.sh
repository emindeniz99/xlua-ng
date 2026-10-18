#!/usr/bin/env bash
# Installs ccache (pinned release archive, SHA-256 checked) for the CMake builds of a CI job
# and makes CMake use it: CMAKE_C_COMPILER_LAUNCHER and CMAKE_CXX_COMPILER_LAUNCHER from the
# environment (CMake 3.17 and later read them when a build folder is configured). The cache
# folder is $RUNNER_TEMP/ccache, which actions/cache restores and saves (.github/CI.md,
# "Caches"). Writes "compiler=<hash of the C and C++ compilers' --version>" to $GITHUB_OUTPUT
# for the cache key. ccache does not cache a failed compile and replays a cached compile's
# warnings, so XLUA_WERROR=ON fails as without it. Linux x86_64 and macOS only.
set -euo pipefail

VERSION=4.14.1  # 2026-09-27; 4.14.2 was inside the seven-day window on 2026-10-19
case "$(uname -s)-$(uname -m)" in
    Linux-x86_64)
        archive="ccache-$VERSION-linux-x86_64-glibc.tar.xz"
        sha256=ad63d19f5d09ea13f749651653a561c98a59994e673504a4266617b8754218f7 ;;
    Darwin-*)
        archive="ccache-$VERSION-darwin.tar.gz"  # universal: arm64 and x86_64
        sha256=c279fa81e2e806b4d9e64d4a06cb569a84ef54151feef796e5d4f4e95a464563 ;;
    *) echo "install-ccache.sh: no pinned ccache for $(uname -s) $(uname -m)" >&2; exit 1 ;;
esac

dest="$RUNNER_TEMP/ccache-$VERSION"
mkdir -p "$dest"
curl -sSfL --retry 3 -o "$dest/$archive" "https://github.com/ccache/ccache/releases/download/v$VERSION/$archive"
if command -v sha256sum >/dev/null; then
    echo "$sha256  $dest/$archive" | sha256sum -c -
else
    echo "$sha256  $dest/$archive" | shasum -a 256 -c -
fi
tar -xf "$dest/$archive" -C "$dest" --strip-components=1
rm "$dest/$archive"
"$dest/ccache" --version | head -n 1
echo "$dest" >> "$GITHUB_PATH"
{
    echo "CCACHE_DIR=$RUNNER_TEMP/ccache"
    echo "CCACHE_MAXSIZE=${CCACHE_MAXSIZE:-300M}"
    # Release builds store no paths, but RelWithDebInfo (sanitize.sh) does: relative ones
    # below the checkout, so a hit does not depend on the runner's workspace path.
    echo "CCACHE_BASEDIR=$GITHUB_WORKSPACE"
    echo "CMAKE_C_COMPILER_LAUNCHER=ccache"
    echo "CMAKE_CXX_COMPILER_LAUNCHER=ccache"
} >> "$GITHUB_ENV"
compilers="$("${CC:-cc}" --version 2>&1; "${CXX:-c++}" --version 2>&1)"
if command -v sha256sum >/dev/null; then
    hash="$(printf '%s' "$compilers" | sha256sum | cut -c1-16)"
else
    hash="$(printf '%s' "$compilers" | shasum -a 256 | cut -c1-16)"
fi
echo "compiler=$hash" >> "$GITHUB_OUTPUT"
