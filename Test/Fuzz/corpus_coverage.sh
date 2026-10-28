#!/bin/bash
# What the fuzz corpora reach. Builds the libFuzzer targets with Clang source-based
# coverage instead of the sanitizers (run.sh and run_bridge.sh with
# FUZZ_INSTRUMENT=coverage: -fprofile-instr-generate -fcoverage-mapping on xLua's
# own files and Lua's C API checks, as Test/Native/coverage.sh builds them),
# replays a corpus through each target once (-runs=0) and prints llvm-cov's
# function and branch report for xlua.c, xlua_core*.c and i64lib.c, with the
# functions no input reached, largest first. Measures only; there is no gate.
#
# Linux with clang, llvm-profdata and llvm-cov (the Test/Docker image). Usage:
#   Test/Fuzz/corpus_coverage.sh <output dir> [TARGET=DIR ...]
# TARGET=DIR replays DIR instead of the target's seeds (what run.sh and
# run_bridge.sh start from: Test/Fuzz/corpus/<target>, for core also the lua
# corpus, for bridge also the 16 random seeds); TARGET is lua, i64, struct,
# path, core or bridge. The weekly job passes the corpus it grew and merged.
# Environment:
#   TARGETS      subset of: lua i64 struct path core bridge (default: run.sh's
#                targets for LUA_VERSION, and bridge)
#   LUA_VERSION, XLUA_PRESET (bridge), FUZZ_TRICKS (bridge), CC: as in run.sh and
#                run_bridge.sh
#   LLVM_PROFDATA, LLVM_COV  tools (default: llvm-profdata, llvm-cov)
# An input that crashes, hangs or runs out of memory is skipped: the corpus is
# first replayed in one process, and when that fails, one process per input;
# the skipped inputs are listed in <target>/skipped.txt and in the summary.
# Writes per target <output dir>/<target>/: report.txt, functions.txt,
# branches.txt (an untaken side shows as "True: 0" or "False: 0"),
# summary.json, unreached.txt; and <output dir>/summary.txt. With more than
# one of lua, i64, struct and path, <output dir>/unit/ is their union (the same
# objects). Test/README.md, "Static analysis, coverage and fuzzing".
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: corpus_coverage.sh <output dir> [TARGET=DIR ...]}" && cd "$1" && pwd)"
shift
version="${LUA_VERSION:-5.5.1}"
preset="${XLUA_PRESET:-legacy}"
profdata="${LLVM_PROFDATA:-llvm-profdata}"
cov="${LLVM_COV:-llvm-cov}"
declare -A corpus_of=()
for arg in "$@"; do
    [[ "$arg" == *=* ]] || { echo "expected TARGET=DIR, got $arg" >&2; exit 2; }
    dir="$(cd "${arg#*=}" && pwd)"
    corpus_of["${arg%%=*}"]="$dir"
done
case "$version" in
    5.4.*|5.5.*) default_targets="lua i64 struct path core bridge" ;;
    luajit*) default_targets="i64 bridge" ;;
    *) default_targets="lua i64 struct path bridge" ;;
esac
targets="${TARGETS:-$default_targets}"
unit_targets=""
for t in $targets; do
    case "$t" in
        lua|i64|struct|path|core) unit_targets+="$t " ;;
        bridge) ;;
        *) echo "unknown target $t" >&2; exit 2 ;;
    esac
done
build="$out/build"
if [[ -n "$unit_targets" ]]; then
    FUZZ_OUT="$build" FUZZ_INSTRUMENT=coverage TARGETS="${unit_targets% }" "$repo/Test/Fuzz/run.sh" build
fi
if [[ " $targets " == *" bridge "* ]]; then
    FUZZ_OUT="$build" FUZZ_INSTRUMENT=coverage "$repo/Test/Fuzz/run_bridge.sh" build
fi
# build/CMakeLists.txt records the sources as build/... (coverage.sh does the same).
cov_paths=(-path-equivalence="build,$repo/build")

# replay <target> <binary> <corpus> <profile dir> <libFuzzer flags...>
replay() {
    local target="$1" bin="$2" corpus="$3" prof="$4"
    shift 4
    rm -rf "$prof"
    mkdir -p "$prof"
    : >"$out/$target/skipped.txt"
    if LLVM_PROFILE_FILE="$prof/all-%p.profraw" "$bin" "$@" -runs=0 "$corpus" >"$out/$target/replay.log" 2>&1; then
        return
    fi
    # A crash ends the process before it writes its profile: one process per input.
    rm -f "$prof"/*.profraw
    local f
    while IFS= read -r -d '' f; do
        if ! LLVM_PROFILE_FILE="$prof/each-%4m.profraw" "$bin" "$@" "$f" >"$out/$target/last.log" 2>&1; then
            echo "$f" >>"$out/$target/skipped.txt"
        fi
    done < <(find "$corpus" -type f -print0 | sort -z)
}

# report <name> <profile dir...> -- <binary> [-object <binary>...]
report() {
    local name="$1" dir="$out/$1"
    shift
    local profiles=()
    while [[ "$1" != -- ]]; do profiles+=("$1"/*.profraw); shift; done
    shift
    mkdir -p "$dir"
    "$profdata" merge -sparse "${profiles[@]}" -o "$dir/corpus.profdata"
    local show=("$@" -instr-profile="$dir/corpus.profdata" "${cov_paths[@]}")
    # The files the binary has among xlua.c, xlua_core*.c and i64lib.c.
    local sources=()
    mapfile -t sources < <("$cov" export -summary-only "${show[@]}" | python3 -c '
import json, os, re, sys
for f in json.load(sys.stdin)["data"][0]["files"]:
    if re.fullmatch(r"xlua\.c|xlua_core\w*\.c|i64lib\.c", os.path.basename(f["filename"])):
        print(f["filename"])')
    show+=("${sources[@]}")
    "$cov" report "${show[@]}" >"$dir/report.txt"
    "$cov" report -show-functions "${show[@]}" >"$dir/functions.txt"
    "$cov" show -show-branches=count "${show[@]}" >"$dir/branches.txt"
    "$cov" export -summary-only "${show[@]}" >"$dir/summary.json"
    # Functions with no call, by branch count, then lines.
    "$cov" export "${show[@]}" | python3 -c '
import json, os, re, sys
data = json.load(sys.stdin)["data"][0]
rows = {}
for fn in data["functions"]:
    regions = [r for r in fn["regions"] if r[5] == 0]
    own = re.fullmatch(r"xlua\.c|xlua_core\w*\.c|i64lib\.c", os.path.basename(fn["filenames"][0]))
    if fn["count"] == 0 and regions and own:
        lines = max(r[2] for r in regions) - min(r[0] for r in regions) + 1
        key = (os.path.basename(fn["filenames"][0]), fn["name"].split(":")[-1])
        rows[key] = (len(fn["branches"]), lines) + key
rows = sorted(rows.values(), key=lambda r: (-r[0], -r[1], r[3]))
rows.sort(key=lambda r: (-r[0], -r[1], r[3]))
print("%-8s %-6s %-22s %s" % ("branches", "lines", "file", "function"))
for b, l, f, n in rows:
    print("%-8d %-6d %-22s %s" % (b, l, f, n))' >"$dir/unreached.txt"
    python3 - "$dir/summary.json" "$name" <<'EOF' | tee -a "$out/summary.txt"
import json, os, sys
for f in json.load(open(sys.argv[1]))["data"][0]["files"]:
    s = f["summary"]
    fn, br = s["functions"], s["branches"]
    print("XLUA_CORPUS_COVERAGE: %-14s %-22s functions %4d of %4d (%5.1f%%)  branches %5d of %5d (%5.1f%%)" % (
        sys.argv[2], os.path.basename(f["filename"]), fn["covered"], fn["count"], fn["percent"],
        br["covered"], br["count"], br["percent"]))
EOF
}

: >"$out/summary.txt"
unit_profiles=()
unit_bins=()
for target in $unit_targets; do
    mkdir -p "$out/$target"
    corpus="${corpus_of[$target]:-$build/$target/seeds}"
    flags=(-timeout=10 -rss_limit_mb=2048)
    if [[ "$target" == lua || "$target" == core ]]; then flags+=(-max_len=4096); fi
    bin="$build/bin/fuzz_$target"
    replay "$target" "$bin" "$corpus" "$build/profiles-$target" "${flags[@]}"
    echo "$target: $(find "$corpus" -type f | wc -l) inputs from $corpus, $(wc -l <"$out/$target/skipped.txt") skipped" \
        | tee -a "$out/summary.txt"
    report "$target" "$build/profiles-$target" -- "$bin"
    if [[ "$target" != core ]]; then
        unit_profiles+=("$build/profiles-$target")
        if [[ ${#unit_bins[@]} == 0 ]]; then unit_bins=("$bin"); else unit_bins+=(-object "$bin"); fi
    fi
done
if [[ ${#unit_profiles[@]} -gt 1 ]]; then
    echo "unit: the union of $(( (${#unit_bins[@]} + 1) / 2 )) targets" | tee -a "$out/summary.txt"
    report unit "${unit_profiles[@]}" -- "${unit_bins[@]}"
fi
if [[ " $targets " == *" bridge "* ]]; then
    name="bridge-$preset-$version"
    mkdir -p "$out/$name"
    corpus="${corpus_of[bridge]:-$build/$name/seeds}"
    bin="$build/bin/fuzz_$name"
    export FUZZ_BRIDGE_TRICKS="${FUZZ_TRICKS:-0}"
    replay "$name" "$bin" "$corpus" "$build/profiles-$name" \
        -timeout=20 -rss_limit_mb=2048 -max_len=2048
    echo "$name: $(find "$corpus" -type f | wc -l) inputs from $corpus, $(wc -l <"$out/$name/skipped.txt") skipped" \
        | tee -a "$out/summary.txt"
    report "$name" "$build/profiles-$name" -- "$build/build-$preset-$version/libxlua.so"
fi
for dir in "$out"/*/; do
    [[ -f "$dir/unreached.txt" ]] || continue
    echo "Largest unreached functions ($(basename "$dir")):"
    sed -n '2,11p' "$dir/unreached.txt" | sed 's/^/  /'
done
echo "XLUA_CORPUS_COVERAGE_DONE: $out/summary.txt"
