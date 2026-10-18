#!/usr/bin/env bash
# compare-builds.sh REFERENCE REBUILD: compare every file under REFERENCE (an
# artifact of a publish.yml build job) with the same path under REBUILD (the
# same build script run again on another runner, in the same checkout path).
# Writes a table to the job summary and fails when a file differs or is missing.
# publish.yml's reproducibility jobs use it (docs/design/release.md).
set -euo pipefail

reference="${1:?reference folder}"
rebuild="${2:?rebuild folder}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
status=0
{
  echo "| File | Release build (SHA-256) | Rebuild | |"
  echo "| --- | --- | --- | --- |"
} >> "$summary"
while IFS= read -r -d '' file; do
  rel="${file#"$reference"/}"
  a="$(shasum -a 256 "$file" | cut -c1-16)"
  if [[ -f "$rebuild/$rel" ]]; then
    b="$(shasum -a 256 "$rebuild/$rel" | cut -c1-16)"
  else
    b="missing"
  fi
  if [[ "$a" == "$b" ]]; then
    verdict="same"
  else
    verdict="**differs**"
    status=1
  fi
  echo "| \`$rel\` | \`$a\` | \`$b\` | $verdict |" >> "$summary"
done < <(find "$reference" -type f ! -name .DS_Store -print0 | sort -z)
if [[ "$status" != 0 ]]; then
  echo "::warning::rebuilt plugins differ from the release build; see the job summary"
fi
exit "$status"
