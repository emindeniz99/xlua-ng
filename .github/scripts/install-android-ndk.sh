#!/usr/bin/env bash
# Select the Android NDK used by every xLua Android build and export it to later workflow steps.
#
# NDK r30 (30.0.16248370) is the current LTS release (developer.android.com/ndk/downloads, updated
# 2026-09-08). GitHub's ubuntu-26.04 image preinstalls 27.3, 28.2 and 29.0 only, so this script uses
# the sdk directory when the image gains r30 and otherwise downloads Google's Linux archive and
# verifies it against pinned checksums before unpacking. SHA-1 is the value Google publishes;
# SHA-256 was computed from that same archive.
set -euo pipefail

version="30.0.16248370"
archive="android-ndk-r30-linux.zip"
sha1="5107f898313790e449e87eee2183d9a20602dee9"
sha256="753611f410d002cfcd3f3dc2ef49aad532089d3180b436c060a90bf0fcb64df2"

sdk_root="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
ndk=""
if [[ -n "$sdk_root" && -f "$sdk_root/ndk/$version/build/cmake/android.toolchain.cmake" ]]; then
  ndk="$sdk_root/ndk/$version"
  echo "Using preinstalled NDK $ndk"
else
  dest="${RUNNER_TEMP:?RUNNER_TEMP is not set}/android-ndk"
  mkdir -p "$dest"
  curl --fail --silent --show-error --location --retry 3 \
    --output "$dest/$archive" "https://dl.google.com/android/repository/$archive"
  echo "$sha1  $dest/$archive" | sha1sum --check --strict -
  echo "$sha256  $dest/$archive" | sha256sum --check --strict -
  unzip -q "$dest/$archive" -d "$dest"
  rm "$dest/$archive"
  ndk="$dest/android-ndk-r30"
  echo "Installed NDK $ndk"
fi

test -f "$ndk/build/cmake/android.toolchain.cmake"
grep -q "^Pkg.Revision = $version$" "$ndk/source.properties"
{
  echo "ANDROID_NDK=$ndk"
  echo "ANDROID_NDK_ROOT=$ndk"
  echo "ANDROID_NDK_HOME=$ndk"
} >> "${GITHUB_ENV:?GITHUB_ENV is not set}"
