#!/bin/bash
# Builds the spike generator(s) into $BASE/gen-out/<roslyn version>/.
set -eu
HERE=$(cd "$(dirname "$0")" && pwd)
BASE=/Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike
export TMPDIR=$BASE/tmp DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p "$TMPDIR"
dotnet build "$HERE/Generator/XLuaTest.Generator.csproj" -c Release -o "$BASE/gen-out/4.3.1" \
  -p:BaseIntermediateOutputPath="$BASE/obj/gen-4.3.1/"
if [ -d "$HERE/Generator38" ]; then
  dotnet build "$HERE/Generator38/XLuaTest.Generator38.csproj" -c Release -o "$BASE/gen-out/3.8" \
    -p:BaseIntermediateOutputPath="$BASE/obj/gen-3.8/"
fi
ls -l "$BASE"/gen-out/*/XLuaTest.Generator.dll
