#!/usr/bin/env bash
# Lua 5.5.1 for Android: armeabi-v7a, arm64-v8a, x86, x86_64. See android_common.sh for the environment.
set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/android_common.sh"
android_build_lua 5.5.1 plugin_lua55 build55
