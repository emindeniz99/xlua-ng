#!/bin/bash
# usage: locked_run.sh <timeout-secs> <cmd> [args...]
#
# Runs one Unity editor or native player under the shared native-test lock:
# - waits for the mkdir lock, refuses to start while another Unity editor runs;
# - starts the command in its own process group and times it out;
# - after exit or timeout, kills what is left of the group, then `pkill -x xctest`;
# - releases the lock only once no process of the group is alive;
# - aborts (exit 90) when the startup disk drops below MIN_FREE_GB (default 25).
# Exit code: the command's, 124 on timeout.
set -u
T=$1; shift
LOCK=${SG_LOCK:-/Volumes/T7Data/ext-ai-projects/xlua-ng/native-test.lock}
MIN_FREE_GB=${MIN_FREE_GB:-25}
export TMPDIR=${SG_TMPDIR:-/Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike/tmp}
mkdir -p "$TMPDIR"

free_gb() { df -g / | awk 'NR==2 {print $4}'; }

if [ "$(free_gb)" -lt "$MIN_FREE_GB" ]; then
  echo "SG_RUN root disk below ${MIN_FREE_GB} GB: $(free_gb) GB free"; exit 90
fi

waited=0
until mkdir "$LOCK" 2>/dev/null; do
  [ $((waited % 300)) -eq 0 ] && echo "SG_RUN waiting for $LOCK ($(cat "$LOCK/owner" 2>/dev/null))"
  sleep 15; waited=$((waited + 15))
  if [ $waited -ge 14400 ]; then echo "SG_RUN gave up waiting for the lock"; exit 91; fi
done
echo "sourcegen-spike pid=$$ $(date '+%F %T') $1" > "$LOCK/owner"

PGID=""
cleanup() {
  if [ -n "$PGID" ]; then
    kill -TERM -- "-$PGID" 2>/dev/null
    for _ in $(seq 1 20); do pgrep -g "$PGID" >/dev/null || break; sleep 1; done
    kill -KILL -- "-$PGID" 2>/dev/null
    for _ in $(seq 1 30); do pgrep -g "$PGID" >/dev/null || break; sleep 1; done
    if pgrep -g "$PGID" >/dev/null; then
      echo "SG_RUN processes of group $PGID still alive; keeping the lock"; pgrep -lg "$PGID"; return
    fi
  fi
  rm -f "$LOCK/owner"; rmdir "$LOCK" 2>/dev/null
}
trap 'cleanup; exit 130' INT TERM HUP

# -x matches the process name; -f on the path also matched drivers that carry the editor path in their arguments.
if pgrep -x Unity >/dev/null; then
  echo "SG_RUN another Unity editor is running:"; pgrep -lx Unity
  cleanup; exit 92
fi

start=$(date +%s)
# perl setpgrp gives the child its own process group, so the whole tree can be killed.
perl -e 'setpgrp(0, 0); exec @ARGV or die "exec: $!"' "$@" &
PGID=$!
timed_out=0
while kill -0 "$PGID" 2>/dev/null; do
  sleep 2
  now=$(date +%s)
  if [ $((now - start)) -ge "$T" ]; then
    echo "SG_RUN TIMEOUT after ${T}s: $*"; timed_out=1; break
  fi
  if [ $(((now - start) % 30)) -lt 2 ] && [ "$(free_gb)" -lt "$MIN_FREE_GB" ]; then
    echo "SG_RUN root disk below ${MIN_FREE_GB} GB during run; killing"; timed_out=2; break
  fi
done
if [ $timed_out -eq 0 ]; then
  wait "$PGID"; code=$?
else
  kill -TERM -- "-$PGID" 2>/dev/null; sleep 5
  code=124; [ $timed_out -eq 2 ] && code=90
fi
# Unity leaves helpers (bee_backend, VBCSCompiler, AssetImportWorker) in the group.
sleep 3
cleanup
pkill -x xctest 2>/dev/null
echo "SG_RUN exit=$code secs=$(( $(date +%s) - start )) free=$(free_gb)GB"
exit $code
