#!/usr/bin/env bash
# Print the path of a 32-bit Python 3 from the runner's tool cache (GitHub's Windows images
# carry x86 builds next to the x64 ones), for loading x86 DLLs with ctypes. Fails when there
# is none. Usage: py="$(.github/scripts/find-python-x86.sh)"
set -euo pipefail
shopt -s nullglob
cache="$(cygpath -u "${RUNNER_TOOL_CACHE:?RUNNER_TOOL_CACHE is not set}")"
found=("$cache"/Python/3.*/x86/python.exe)
if (( ${#found[@]} == 0 )); then
  echo "No x86 Python in $cache/Python" >&2
  exit 1
fi
py="$(printf '%s\n' "${found[@]}" | sort -V | tail -n 1)"
"$py" -c "import struct, sys; print(sys.version, struct.calcsize('P') * 8, 'bit', file=sys.stderr); sys.exit(0 if struct.calcsize('P') == 4 else 'not a 32-bit Python')"
echo "$py"
