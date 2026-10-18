"""Build the xLua command-line tools from source into this directory.

The Unity editor runs Tools/XLuaHotfixInject.exe after each player build when
HOTFIX_ENABLE is defined, so build the tools once before the first hotfix
inject. Requires Mono's mcs (and resgen for XLuaGenerate).

XLuaHotfixInject needs Mono.Cecil, which is not stored in the repository: the
script downloads the NuGet package once into a cache directory, checks its
SHA-512 and copies the net40 assemblies next to the tools. With --cecil-dir it
copies the assemblies of Unity's com.unity.nuget.mono-cecil package instead
(only the builds pinned in UNITY_CECIL); see docs/research/hotfix.md.
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
CECIL_DLLS = ["Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll"]
# SHA-256 of the assemblies in Unity's com.unity.nuget.mono-cecil package, as
# downloaded from download.packages.unity.com on 2026-10-18. 1.11.5 and 1.11.6
# contain the same files: Cecil 0.11.5 plus jbevain/cecil#914, built for
# netstandard2.0. Older package versions (1.10.x: Cecil 0.10.0, 1.11.4: 0.11.4)
# are not accepted.
UNITY_CECIL = {
    "com.unity.nuget.mono-cecil 1.11.5/1.11.6 (Mono.Cecil 0.11.5)": {
        "Mono.Cecil.dll": "0b71905f5995fc8c5224619c4eb40293e6d401ffce0ede5770a222da76c2120b",
        "Mono.Cecil.Mdb.dll": "80fab0ee33b71bfcbce31c73b1d7ba7a1a0d6b3302377212a0e07c94306949bc",
        "Mono.Cecil.Pdb.dll": "1d39ffeec5bd863ae1c68b93bc388219757e4882cb64d3ac1e3ac44cc0b31521",
        "Mono.Cecil.Rocks.dll": "82d04453cb9a2aa0afa6adfda24da50cbab470012c93c69e81ec2c36a50feca8",
    },
}

repo = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--output", type=Path, default=repo / "Tools")
p.add_argument("--compiler", default="mcs")
p.add_argument("--resgen", default="resgen")
p.add_argument("--define", action="append", default=[],
               help="extra symbol for XLua.Mini and XLuaGenerate, e.g. GEN_CODE_MINIMIZE")
p.add_argument("--debug", action="store_true",
               help="pass -debug to mcs so that each tool gets an .mdb (used for coverage)")
p.add_argument("--cache", type=Path, default=os.environ.get("XLUA_TOOLS_CACHE"),
               help="download cache for Mono.Cecil (default: $XLUA_TOOLS_CACHE or Tools/.cache)")
p.add_argument("--cecil-dir", type=Path,
               help="take Mono.Cecil from this com.unity.nuget.mono-cecil folder (for example "
                    "Library/PackageCache/com.unity.nuget.mono-cecil@...) instead of nuget.org")
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


def unity_cecil(folder):
    """Return the UNITY_CECIL build that the DLLs in folder match, or stop."""
    found = {}
    for name in CECIL_DLLS:
        path = folder / name
        if not path.is_file():
            raise SystemExit("--cecil-dir: %s is missing" % path)
        found[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    for build, hashes in UNITY_CECIL.items():
        if found == hashes:
            return build
    raise SystemExit("--cecil-dir: %s holds an unknown Mono.Cecil build (%s); accepted: %s"
                     % (folder, ", ".join("%s %s" % kv for kv in sorted(found.items())), ", ".join(UNITY_CECIL)))


# Extra references for every assembly compiled against Cecil.
cecil_refs = []
if a.cecil_dir:
    cecil_source = unity_cecil(a.cecil_dir.resolve())
    for name in CECIL_DLLS:
        (out / name).write_bytes((a.cecil_dir / name).read_bytes())
    # Unity's builds target netstandard2.0; mcs needs Mono's facade to compile against them.
    cecil_refs = ["Facades/netstandard.dll"]
else:
    cecil_source = "Mono.Cecil %s (nuget.org, net40)" % CECIL_VERSION
    with zipfile.ZipFile(fetch_cecil((a.cache or repo / "Tools/.cache").resolve())) as package:
        for name in CECIL_DLLS:
            (out / name).write_bytes(package.read("lib/net40/" + name))
cecil = [out / name for name in CECIL_DLLS] + cecil_refs


def mcs(target, name, defines, refs, sources, extra=()):
    subprocess.run([a.compiler, "-target:" + target, "-out:" + str(out / name), "-r:System.Core"]
                   + ["-define:" + d for d in defines] + ["-r:" + str(r) for r in refs]
                   + (["-debug"] if a.debug else []) + list(extra) + [str(s) for s in sources], check=True)


# XLuaHotfixInject loads Mono.Cecil from its own directory.
hotfix_source = repo / "Assets/XLua/Src/Editor/Hotfix.cs"
mcs("exe", "XLuaHotfixInject.exe", ["HOTFIX_ENABLE", "XLUA_GENERAL"], cecil,
    [hotfix_source, src / "XLuaHotfixInject.cs"])


def source_sha256(path):
    """SHA-256 of a source file without its UTF-8 BOM and carriage returns, as
    SetupValidator.HotfixSourceHash computes it, so line endings do not matter."""
    data = path.read_bytes()
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return hashlib.sha256(data.replace(b"\r", b"")).hexdigest()


# XLua/Validate Setup and the editor's hotfix step read this manifest: it lists
# the files the injector needs and the Hotfix.cs it was built from, so a Tools
# folder from an older build is reported instead of failing or injecting old code.
(out / "XLuaHotfixInject.manifest").write_text(
    "# Written by Tools/build_tools.py; read by XLua/Validate Setup and before hotfix injection.\n"
    "hotfix-source-sha256=%s\n" % source_sha256(hotfix_source)
    + "cecil=%s\n" % cecil_source
    + "".join("file=%s\n" % name for name in ["XLuaHotfixInject.exe"] + CECIL_DLLS))
mcs("exe", "FilesSignature.exe", [], [], [src / "FilesSignature.cs"])
mcs("exe", "KeyPairsGen.exe", [], [], [src / "KeyPairsGen.cs"])

# XLuaGenerate is used by the opt-in Unity 2018 external generator path and, with
# --luacats, writes LuaCATS definitions outside Unity.
mini_project = repo / "General/vs2013/XLua.Mini.csproj"
import xml.etree.ElementTree as ET
mini_sources = [(mini_project.parent / x.attrib["Include"].replace("\\", "/")).resolve()
                for x in ET.parse(mini_project).iter() if x.tag.endswith("}Compile")]
mcs("library", "XLua.Mini.dll", ["XLUA_GENERAL"] + a.define, [], mini_sources, ["-unsafe"])
with tempfile.TemporaryDirectory() as tmp:
    resources = Path(tmp) / "XLuaTemplates.resources"
    # The .resx references templates by paths relative to its own directory.
    subprocess.run([a.resgen, "XLuaTemplates.resx", str(resources)], cwd=src, check=True)
    mcs("exe", "XLuaGenerate.exe", ["XLUA_GENERAL", "XLUA_ALL_OBSOLETE"] + a.define, [out / "XLua.Mini.dll"],
        [repo / "Assets/XLua/Src/Editor/Generator.cs", repo / "Assets/XLua/Src/Editor/LuaCATSGenerator.cs",
         repo / "Assets/XLua/Src/Editor/SetupValidator.cs", repo / "Assets/XLua/Src/Editor/ConfigMigration.cs",
         src / "XLuaGenerate.cs", src / "XLuaTemplates.Designer.cs"],
        ["-resource:" + str(resources) + ",XLuaGenerate.Src.XLuaTemplates.resources"])
print("Built xLua tools in", out)
