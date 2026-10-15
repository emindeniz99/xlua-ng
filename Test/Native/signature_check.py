"""Check script signing end to end: KeyPairsGen, FilesSignature and SignatureLoader."""
import argparse
from pathlib import Path
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--output", type=Path, required=True, help="empty scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
a = p.parse_args()
out = a.output.resolve()
tools = out / "tools"
work = out / "work"
(work / "src").mkdir(parents=True, exist_ok=True)

subprocess.run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools),
                "--compiler", a.compiler], check=True)
(work / "src/module.lua").write_text("return 'signed module'\n")
subprocess.run([a.mono, str(tools / "KeyPairsGen.exe")], cwd=work, check=True)
subprocess.run([a.mono, str(tools / "FilesSignature.exe"), "src", "signed"], cwd=work, check=True)
subprocess.run([a.compiler, "-out:" + str(tools / "SignatureCheck.exe"), "-r:" + str(tools / "XLua.Mini.dll"),
                str(repo / "Test/Native/SignatureCheck.cs")], check=True)
result = subprocess.run([a.mono, str(tools / "SignatureCheck.exe"), str(work)], cwd=tools)
sys.exit(result.returncode)
