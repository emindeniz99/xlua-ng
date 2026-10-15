"""Build the xLua command-line tools from source into this directory.

The Unity editor runs Tools/XLuaHotfixInject.exe after each player build when
HOTFIX_ENABLE is defined, so build the tools once before the first hotfix
inject. Requires Mono's mcs (and resgen for XLuaGenerate).

XLuaHotfixInject needs Mono.Cecil, which is not stored in the repository: the
script downloads the NuGet package once into a cache directory, checks its
SHA-512 and copies the net40 assemblies next to the tools.
"""
import argparse
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import urllib.request
import zipfile

CECIL_VERSION = "0.11.6"
# SHA-512 of Mono.Cecil.0.11.6.nupkg, as listed by the nuget.org catalog (packageHash).
CECIL_SHA512 = ("1c5932246b23c9f317690a25ce3e1416d168d885871063d2f604cf997ed9e99d"
                "41bccdd0e22d4be6e4a5ea729d06bd92cc543686dbbd75ab0199db60e0912fb4")
CECIL_URL = "https://www.nuget.org/api/v2/package/Mono.Cecil/" + CECIL_VERSION
# The package also has netstandard2.0 builds; net40 matches the .NET Framework
# profile that mcs and the Mono runtime use, so no netstandard facade is needed.
CECIL_DLLS = ["Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll"]

repo = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--output", type=Path, default=repo / "Tools")
p.add_argument("--compiler", default="mcs")
p.add_argument("--resgen", default="resgen")
p.add_argument("--cache", type=Path, default=os.environ.get("XLUA_TOOLS_CACHE"),
               help="download cache for Mono.Cecil (default: $XLUA_TOOLS_CACHE or <output>/.cache)")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
src = repo / "General/Src"


def fetch_cecil(cache):
    """Return the verified Mono.Cecil .nupkg, downloading it if it is not cached."""
    nupkg = cache / ("mono.cecil.%s.nupkg" % CECIL_VERSION)
    if nupkg.is_file() and hashlib.sha512(nupkg.read_bytes()).hexdigest() == CECIL_SHA512:
        return nupkg
    cache.mkdir(parents=True, exist_ok=True)
    print("Downloading", CECIL_URL, flush=True)
    with urllib.request.urlopen(CECIL_URL, timeout=60) as response:
        data = response.read()
    digest = hashlib.sha512(data).hexdigest()
    if digest != CECIL_SHA512:
        raise SystemExit("Mono.Cecil %s: SHA-512 mismatch, got %s" % (CECIL_VERSION, digest))
    partial = nupkg.with_suffix(".part")
    partial.write_bytes(data)
    partial.replace(nupkg)
    return nupkg


with zipfile.ZipFile(fetch_cecil((a.cache or out / ".cache").resolve())) as package:
    for name in CECIL_DLLS:
        (out / name).write_bytes(package.read("lib/net40/" + name))
cecil = [out / name for name in CECIL_DLLS]


def mcs(target, name, defines, refs, sources, extra=()):
    subprocess.run([a.compiler, "-target:" + target, "-out:" + str(out / name), "-r:System.Core"]
                   + ["-define:" + d for d in defines] + ["-r:" + str(r) for r in refs]
                   + list(extra) + [str(s) for s in sources], check=True)


# XLuaHotfixInject loads Mono.Cecil from its own directory.
mcs("exe", "XLuaHotfixInject.exe", ["HOTFIX_ENABLE", "XLUA_GENERAL"], cecil,
    [repo / "Assets/XLua/Src/Editor/Hotfix.cs", src / "XLuaHotfixInject.cs"])
mcs("exe", "FilesSignature.exe", [], [], [src / "FilesSignature.cs"])
mcs("exe", "KeyPairsGen.exe", [], [], [src / "KeyPairsGen.cs"])

# XLuaGenerate is used by the opt-in Unity 2018 external generator path and, with
# --luacats, writes LuaCATS definitions outside Unity.
mini_project = repo / "General/vs2013/XLua.Mini.csproj"
import xml.etree.ElementTree as ET
mini_sources = [(mini_project.parent / x.attrib["Include"].replace("\\", "/")).resolve()
                for x in ET.parse(mini_project).iter() if x.tag.endswith("}Compile")]
mcs("library", "XLua.Mini.dll", ["XLUA_GENERAL"], [], mini_sources, ["-unsafe"])
with tempfile.TemporaryDirectory() as tmp:
    resources = Path(tmp) / "XLuaTemplates.resources"
    # The .resx references templates by paths relative to its own directory.
    subprocess.run([a.resgen, "XLuaTemplates.resx", str(resources)], cwd=src, check=True)
    mcs("exe", "XLuaGenerate.exe", ["XLUA_GENERAL", "XLUA_ALL_OBSOLETE"], [out / "XLua.Mini.dll"],
        [repo / "Assets/XLua/Src/Editor/Generator.cs", repo / "Assets/XLua/Src/Editor/LuaCATSGenerator.cs",
         src / "XLuaGenerate.cs", src / "XLuaTemplates.Designer.cs"],
        ["-resource:" + str(resources) + ",XLuaGenerate.Src.XLuaTemplates.resources"])
print("Built xLua tools in", out)
