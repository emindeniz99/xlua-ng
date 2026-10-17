#!/usr/bin/env bash
# Shared by the LuaJIT build scripts (make_*_luajit.sh, android_common.sh). Source
# this file; do not run it.
#
# XLUA_HARDEN reaches build/CMakeLists.txt through the environment, but LuaJIT's
# libluajit.a is built by LuaJIT's own Makefile, which adds -U_FORTIFY_SOURCE and
# -fno-stack-protector (luajit-2.1/src/Makefile). With XLUA_HARDEN on, the
# scripts pass the flags below as TARGET_CFLAGS, which the Makefile puts after
# its own, so they win. They match what CMake adds to the rest of the library.

# True when XLUA_HARDEN holds one of CMake's true constants (1, ON, YES, TRUE,
# Y or another non-zero number), as CMakeLists.txt reads it.
xlua_harden_on() {
    local value
    value="$(printf '%s' "${XLUA_HARDEN:-}" | tr '[:lower:]' '[:upper:]')"
    case "${value}" in
        ON|YES|TRUE|Y) return 0 ;;
    esac
    [[ "${value}" =~ ^[0-9]+$ && "${value}" =~ [1-9] ]]
}

# usage: xlua_cc_accepts "<compiler command>" <flag>
xlua_cc_accepts() {
    # The compiler command may carry options ("gcc -m32"), so it is split on purpose.
    # shellcheck disable=SC2086
    echo 'int xlua_probe;' | $1 -Werror "$2" -x c -c - -o /dev/null 2>/dev/null
}

# Prints LuaJIT's TARGET_CFLAGS for XLUA_HARDEN, or nothing when it is off.
# usage: xlua_luajit_harden_cflags "<target compiler command>" <target>
#   target: arm64-elf (Android, OpenHarmony and Linux arm64), x86_64-linux, or
#   other (Apple, 32-bit ABIs).
xlua_luajit_harden_cflags() {
    local cc="$1" target="$2" flags="" flag
    xlua_harden_on || return 0
    for flag in -fstack-protector-strong -fno-delete-null-pointer-checks -ftrivial-auto-var-init=zero; do
        if xlua_cc_accepts "${cc}" "${flag}"; then
            flags+=" ${flag}"
        fi
    done
    # After LuaJIT's -U_FORTIFY_SOURCE. Bionic and Apple support no higher level.
    flags+=" -D_FORTIFY_SOURCE=2"
    case "${target}" in
        arm64-elf)
            # PAC and BTI in the C code, and BTI landing pads in LuaJIT's
            # interpreter and JIT code (lj_arch.h: LUAJIT_ENABLE_CET_BR with
            # __ARM_FEATURE_BTI_DEFAULT), so the linker can mark the library BTI.
            if xlua_cc_accepts "${cc}" -mbranch-protection=standard; then
                flags+=" -mbranch-protection=standard -DLUAJIT_ENABLE_CET_BR"
            fi ;;
        x86_64-linux)
            # CET: shadow stack, and indirect branch tracking in the interpreter
            # (lj_arch.h says IBT slows the interpreter on every x64 CPU).
            for flag in -fcf-protection -fstack-clash-protection; do
                if xlua_cc_accepts "${cc}" "${flag}"; then
                    flags+=" ${flag}"
                fi
            done
            [[ "${flags}" == *-fcf-protection* ]] && flags+=" -DLUAJIT_ENABLE_CET_BR" ;;
    esac
    printf '%s' "${flags# }"
}
