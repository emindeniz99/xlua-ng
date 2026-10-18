#!/bin/bash
# Static analysis of xLua's own C files (build/xlua_core.c, build/xlua_core_limits.c, build/xlua.c,
# build/i64lib.c, build/memory_leak_checker.c) with clang-tidy (build/.clang-tidy) and the
# Clang Static Analyzer (clang --analyze). Vendored code under build/ is not
# analyzed. Each runtime is configured separately because the #if branches
# differ; only CMake configuration is needed, nothing is compiled or linked.
#
# Linux or macOS with clang, clang-tidy and cmake on PATH.
# Usage: Test/Native/analyze.sh <output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1 luajit).
# Exits non-zero when any runtime has a finding; logs are in <output dir>.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: analyze.sh <output dir>}" && cd "$1" && pwd)"
clang_bin="${CLANG:-clang}"
tidy_bin="${CLANG_TIDY:-clang-tidy}"
# Default analyzer checkers plus a few useful opt-in ones. The Annex K
# insecureAPI check is off for the reason given in build/.clang-tidy.
analyzer_flags=(
    --analyze --analyzer-output text
    -Xclang -analyzer-checker=security.ArrayBound,optin.portability.UnixAPI,optin.core.EnumCastOutOfRange
    -Xclang -analyzer-disable-checker=security.insecureAPI.DeprecatedOrUnsafeBufferHandling
)
status=0

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}; do
    build="$out/$version"
    files=("$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" "$repo/build/xlua.c" "$repo/build/i64lib.c")
    if [[ "$version" == luajit ]]; then
        # LuaJIT headers are used as-is; libluajit.a is not needed to configure.
        cmake -S "$repo/build" -B "$build" -DCMAKE_C_COMPILER="$clang_bin" \
            -DCMAKE_EXPORT_COMPILE_COMMANDS=ON -DUSING_LUAJIT=ON >/dev/null
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_C_COMPILER="$clang_bin" \
            -DCMAKE_EXPORT_COMPILE_COMMANDS=ON -DLUA_VERSION="$version" >/dev/null
        files+=("$repo/build/memory_leak_checker.c")
    fi

    tidy_log="$build/clang-tidy.log"
    if "$tidy_bin" -p "$build" --quiet "${files[@]}" >"$tidy_log" 2>&1; then
        echo "clang-tidy $version: clean"
    else
        echo "clang-tidy $version: findings (see $tidy_log)"
        grep -E '(warning|error):' "$tidy_log" || cat "$tidy_log"
        status=1
    fi

    analyzer_log="$build/clang-analyze.log"
    : >"$analyzer_log"
    for file in "${files[@]}"; do
        # Reuse the exact defines and include paths CMake chose for this file.
        flags=()
        while IFS= read -r flag; do flags+=("$flag"); done < <(python3 - "$build/compile_commands.json" "$file" <<'PY'
import json, os, shlex, sys
for entry in json.load(open(sys.argv[1])):
    if os.path.realpath(entry["file"]) == os.path.realpath(sys.argv[2]):
        args = entry.get("arguments") or shlex.split(entry["command"])
        skip = False
        for arg in args[1:]:
            if skip:
                skip = False
            elif arg in ("-o", "-c"):
                skip = arg == "-o"
            elif os.path.realpath(arg) != os.path.realpath(sys.argv[2]):
                print(arg)
        break
else:
    sys.exit("no compile command for " + sys.argv[2])
PY
        )
        (cd "$build" && "$clang_bin" "${flags[@]}" "${analyzer_flags[@]}" "$file" -o /dev/null) >>"$analyzer_log" 2>&1
    done
    if grep -qE '(warning|error):' "$analyzer_log"; then
        echo "clang --analyze $version: findings (see $analyzer_log)"
        grep -E '(warning|error):' "$analyzer_log"
        status=1
    else
        echo "clang --analyze $version: clean"
    fi
done

if [[ "$status" == 0 ]]; then
    echo "XLUA_ANALYZE_PASS: ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}"
fi
exit "$status"
