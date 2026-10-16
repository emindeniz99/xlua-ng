#!/usr/bin/env bash
# Install the OpenHarmony native SDK used by the xLua OpenHarmony builds and export it to later
# workflow steps.
#
# OpenHarmony 7.0-Release (API 26), native SDK 26.0.0.38, from the 2026-08-29 respin on Huawei's
# mirror. The archive holds the full Windows and Linux SDK (3.4 GB); only the Linux native
# toolchain is unpacked. SHA-256 is the value the mirror publishes next to the archive.
set -euo pipefail

archive="ohos-sdk-windows_linux-public_20260829.tar.gz"
url="https://repo.huaweicloud.com/harmonyos/os/7.0-Release/$archive"
sha256="6bf6ae1efe8de0e8bd15ddbd7fac58bcb54d9620262a541b9b219439317a4c42"
native="native-linux-x64-26.0.0.38-Release.zip"

dest="${RUNNER_TEMP:?RUNNER_TEMP is not set}/ohos-sdk"
mkdir -p "$dest"
curl --fail --silent --show-error --location --retry 3 --output "$dest/$archive" "$url"
echo "$sha256  $dest/$archive" | sha256sum --check --strict -
tar xzf "$dest/$archive" -C "$dest" "ohos-sdk/linux/$native"
rm "$dest/$archive"
unzip -q "$dest/ohos-sdk/linux/$native" -d "$dest"
rm "$dest/ohos-sdk/linux/$native"

sdk="$dest/native"
test -f "$sdk/build/cmake/ohos.toolchain.cmake"
grep -q '"version": "26.0.0.38"' "$sdk/oh-uni-package.json"
echo "Installed OpenHarmony native SDK $sdk"
echo "OHOS_NDK_HOME=$sdk" >> "${GITHUB_ENV:?GITHUB_ENV is not set}"
