#!/usr/bin/env bash
# Homebrew's Mono bottle (6.14.1, the managed tests' Mono) for a Linux CI job, installed while
# the job's first steps run: about 60 s, of which some 45 s pour Mono and its 17 dependencies
# (run 39170081830), so a download cache would save little and overlapping saves the most.
#
#   .github/scripts/mono.sh start   starts `brew install mono` in the background
#   .github/scripts/mono.sh wait    waits for it, prints its log, fails when it failed, and
#                                   puts Homebrew's bin on PATH for the steps after it
#
# Homebrew's bin stays off PATH until `wait`: the bottle brings python@3.15, whose python3
# would otherwise appear half-installed in the steps that run meanwhile.
set -euo pipefail

state="$RUNNER_TEMP/brew-mono"
brew="${XLUA_BREW:-/home/linuxbrew/.linuxbrew/bin/brew}"  # XLUA_BREW: for a test of this script
case "${1:-}" in
    start)
        mkdir -p "$state"
        # setsid: the install outlives this step; the runner ends it with the job at the latest.
        HOMEBREW_NO_AUTO_UPDATE=1 setsid bash -c \
            "'$brew' install mono > '$state/log' 2>&1; echo \$? > '$state/status'" < /dev/null &
        echo "brew install mono runs in the background (log: $state/log)"
        ;;
    wait)
        # The job's timeout ends a hung install; the bottle needs about a minute.
        while [[ ! -s "$state/status" ]]; do sleep 2; done
        cat "$state/log"
        code="$(cat "$state/status")"
        if [[ "$code" != 0 ]]; then
            echo "::error::brew install mono failed with exit code $code"
            exit 1
        fi
        prefix="$("$brew" --prefix)"
        echo "$prefix/bin" >> "$GITHUB_PATH"
        "$prefix/bin/mono" --version | head -n 1
        ;;
    *) echo "usage: mono.sh start|wait" >&2; exit 2 ;;
esac
