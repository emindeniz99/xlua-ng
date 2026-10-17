#!/usr/bin/env python3
"""Build the xLua source generator, run its unit tests, or install it into a Unity project.

    python3 Tools/build_source_generator.py --output DIR                build DIR/bin/XLua.SourceGenerator.dll
    python3 Tools/build_source_generator.py --output DIR --test         also run the unit tests
    python3 Tools/build_source_generator.py --output DIR --install Assets/XLua/Src/Analyzers

The generator (General/SourceGenerator/XLua.SourceGenerator) is a netstandard2.0
Roslyn component built against Microsoft.CodeAnalysis.CSharp 4.3.1, the
compiler of Unity 2021.3 and later. It is built with the .NET SDK pinned in
mise.toml. NuGet packages come from nuget.org only (General/SourceGenerator/
nuget.config) and are restored in locked mode against each project's
packages.lock.json, which pins every package, also transitive ones, with its
SHA-512; Tools/dependencies.py lists them. --update-lock restores without
locked mode and rewrites the lock files after a version change.

The packages go to Tools/.cache/nuget (or $XLUA_TOOLS_CACHE/nuget), the build
output under --output; nothing is written into the checkout except the lock
files with --update-lock. --install copies the DLL into a Unity project folder
with the .meta that makes Unity load it as a Roslyn analyzer: the
RoslynAnalyzer label and no platform. Unity then runs it in the assemblies that
reference the asmdef of that folder (build/SOURCEGEN_SPIKE.md, "Shipping").
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[1]
root = repo / "General/SourceGenerator"
GENERATOR = root / "XLua.SourceGenerator/XLua.SourceGenerator.csproj"
TESTS = root / "XLua.SourceGenerator.Tests/XLua.SourceGenerator.Tests.csproj"
DLL = "XLua.SourceGenerator.dll"

# Fixed GUIDs, so that reinstalling keeps Unity's references to the files.
FOLDER_GUID = "6b0f6c1f3a9d4e2f8c5a0b7d9e1f2a3c"
DLL_GUID = "c3a1e5f7b9d2486a8e0c4f6a2b8d0e1f"
FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
# As in Tools/SourceGenSpike/run_spike.py: the RoslynAnalyzer label, and the
# DLL is not loaded on any platform (Unity hands it to the compiler only).
DLL_META = """fileFormatVersion: 2
guid: {guid}
labels:
- RoslynAnalyzer
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Editor: 1
        Exclude Linux64: 1
        Exclude OSXUniversal: 1
        Exclude Win: 1
        Exclude Win64: 1
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def dotnet_path():
    """$XLUA_DOTNET, else the SDK that mise.toml pins, else dotnet on PATH."""
    if os.environ.get("XLUA_DOTNET"):
        return os.environ["XLUA_DOTNET"]
    try:
        return subprocess.run(["mise", "which", "dotnet"], cwd=repo, stdout=subprocess.PIPE,
                              stderr=subprocess.DEVNULL, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        found = shutil.which("dotnet")
        if found:
            return found
    sys.exit("no dotnet: run `mise install dotnet` (mise.toml pins the SDK)")


def environment(out):
    env = dict(os.environ)
    cache = Path(os.environ.get("XLUA_TOOLS_CACHE", repo / "Tools/.cache"))
    env.update({
        "NUGET_PACKAGES": str(cache / "nuget"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_NOLOGO": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_CLI_HOME": str(out / "dotnet-home"),
        "MSBUILDDISABLENODEREUSE": "1",
    })
    return env


def run(cmd, env, timeout):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run([str(c) for c in cmd], env=env, check=True, timeout=timeout)


def restore_and_build(dotnet, project, out, env, update_lock, configuration="Release"):
    """Restore (locked unless update_lock) and build one project; returns its output folder."""
    # Directory.Build.props puts obj/ and bin/ of each project under XLuaBuildRoot.
    bin_dir = out / "bin" / project.stem
    props = ["-p:XLuaBuildRoot=" + str(out) + "/", "-p:Configuration=" + configuration]
    restore = [dotnet, "restore", project, "--configfile", root / "nuget.config"] + props
    if not update_lock:
        restore.append("--locked-mode")
    else:
        restore.append("--force-evaluate")
    run(restore, env, 900)
    run([dotnet, "build", project, "--no-restore", "-nologo", "-v:minimal"] + props, env, 900)
    return bin_dir


def build(out, update_lock=False):
    """Builds the generator into out/bin/XLua.SourceGenerator; returns the DLL path."""
    out = out.resolve()
    env = environment(out)
    dotnet = dotnet_path()
    bin_dir = restore_and_build(dotnet, GENERATOR, out, env, update_lock)
    dll = bin_dir / DLL
    if not dll.is_file():
        sys.exit("the build wrote no " + str(dll))
    # Unity loads the analyzer next to its own Roslyn: nothing else may be copied.
    extra = sorted(p.name for p in bin_dir.glob("*.dll") if p.name != DLL)
    if extra:
        sys.exit("unexpected assemblies next to the generator: " + ", ".join(extra))
    return dll


def test(out, update_lock=False):
    out = out.resolve()
    env = environment(out)
    dotnet = dotnet_path()
    bin_dir = restore_and_build(dotnet, TESTS, out, env, update_lock)
    exe = bin_dir / "XLua.SourceGenerator.Tests.dll"
    print("+", dotnet, exe, flush=True)
    result = subprocess.run([dotnet, str(exe), str(repo)], env=env, timeout=900)
    if result.returncode != 0:
        sys.exit("source generator unit tests failed")


def install(dll, folder):
    folder = folder.resolve()
    folder.mkdir(parents=True, exist_ok=True)
    meta = folder.parent / (folder.name + ".meta")
    if not meta.exists():
        meta.write_text(FOLDER_META.format(guid=FOLDER_GUID))
    shutil.copy2(dll, folder / DLL)
    (folder / (DLL + ".meta")).write_text(DLL_META.format(guid=DLL_GUID))
    print("installed", folder / DLL)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--output", type=Path, required=True, help="build folder, outside the checkout")
    p.add_argument("--test", action="store_true", help="also build and run the unit tests")
    p.add_argument("--install", type=Path, help="copy the DLL and its Unity .meta into this folder")
    p.add_argument("--update-lock", action="store_true", help="restore without locked mode and rewrite packages.lock.json")
    a = p.parse_args()
    dll = build(a.output, a.update_lock)
    print("XLUA_SOURCEGEN_BUILD:", dll)
    if a.test:
        test(a.output, a.update_lock)
    if a.install:
        install(dll, a.install)


if __name__ == "__main__":
    main()
