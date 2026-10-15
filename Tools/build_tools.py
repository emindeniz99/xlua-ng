"""Build the xLua command-line tools from source into this directory.

The Unity editor runs Tools/XLuaHotfixInject.exe after each player build when
HOTFIX_ENABLE is defined, so build the tools once before the first hotfix
inject. Requires Mono's mcs (and resgen for XLuaGenerate).
"""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--output", type=Path, default=repo / "Tools")
p.add_argument("--compiler", default="mcs")
p.add_argument("--resgen", default="resgen")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
lib = repo / "General/Lib"
src = repo / "General/Src"
cecil = [lib / "Mono.Cecil.dll", lib / "Mono.Cecil.Mdb.dll", lib / "Mono.Cecil.Pdb.dll"]


def mcs(target, name, defines, refs, sources, extra=()):
    subprocess.run([a.compiler, "-target:" + target, "-out:" + str(out / name), "-r:System.Core"]
                   + ["-define:" + d for d in defines] + ["-r:" + str(r) for r in refs]
                   + list(extra) + [str(s) for s in sources], check=True)


# XLuaHotfixInject loads Mono.Cecil from its own directory.
for dll in cecil:
    shutil.copy2(dll, out / dll.name)
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
