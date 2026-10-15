#!/usr/bin/env bash
# LuaJIT 2.1 for Android: armeabi-v7a, arm64-v8a, x86, x86_64. See android_common.sh for the environment.
# The 32-bit ABIs need a 32-bit host C compiler (gcc-multilib).
set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/android_common.sh"
android_build_luajit
