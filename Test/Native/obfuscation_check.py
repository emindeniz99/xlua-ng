"""Run xLua against renamed types, as after an obfuscator (docs/design/source-generator.md, milestone M8).

1. Builds XLua.Mini.dll (mcs, XLUA_GENERAL), the source generator
   (Tools/build_source_generator.py) and Mono.Cecil (Tools/build_tools.py).
2. Compiles Test/Native/ObfuscationFixture.cs, the game assembly, with the
   .NET SDK's csc and the generator against XLua.Mini.dll: xLua in an assembly
   of its own, as with the XLua asmdef the generator needs.
3. Writes the obfuz rule file for it with Test/Native/ObfuzRulesTool.cs, which
   uses the Editor adapter's code (Assets/XLua/Src/Editor/ObfuzRules.cs) on the
   generator's fragment.
4. Renames the fixture twice with Test/Native/ObfuscationRenamer.cs (Cecil):
   - rules: the obfuz rule file, as obfuz 4.0.0 applies it;
   - attributes: only [Obfuscation(Exclude = true)], as Obfuscar, ConfuserEx
     and other attribute-driven tools do.
5. Checks that the renaming with the rules kept every type and member name
   that the generator's link.xml fragment uses: UnityLinker reads that file
   after obfuz renamed the assemblies, and it names them by their original
   names.
6. Runs the plain and both renamed builds on Mono. Every check must pass,
   except those expected to fail: a type without configuration (not in the
   name table) after any renaming, and, without the rules, what Lua reaches
   by name through reflection.

Run it for the legacy preset (the default) and with --define
XLUA_PRESET_MINIMAL --define XLUA_REFLECTION: legacy keeps the [LuaCallCSharp]
types whole in link.xml, so the rules keep their names, and only the minimal
run renames them (the name table checks).

Usage: python3 Test/Native/obfuscation_check.py --plugin <libxlua> --output <dir>
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--mono-api", type=Path,
               help="the Mono assemblies the fixture compiles against (default: lib/mono/4.5 next to the mono executable)")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua and the fixture, e.g. XLUA_LOCK_METATABLES (repeatable)")
p.add_argument("--timeout", type=int, default=300, help="seconds per Mono run")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)

# What each run must report as failing; every other check must pass.
EXPECTED_FAILURES = {
    "plain": [],
    "rules": ["unbound"],
    "attributes": ["unbound", "reflection_use", "donotgen", "fallback", "enum_by_name"],
}


def run(cmd, timeout=600, **kw):
    """Run a command in its own process group; on a timeout, kill the group."""
    print("+", " ".join(str(c) for c in cmd), flush=True)
    proc = subprocess.Popen([str(c) for c in cmd], start_new_session=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, errors="backslashreplace", **kw)
    try:
        output, _ = proc.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.communicate()
        raise SystemExit("timed out after %d s: %s" % (timeout, " ".join(str(c) for c in cmd)))
    print(output, flush=True)
    if proc.returncode != 0:
        raise SystemExit("failed (%d): %s" % (proc.returncode, " ".join(str(c) for c in cmd)))
    return output


def sources(name):
    project = repo / "General/vs2013" / (name + ".csproj")
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


defines = ["XLUA_GENERAL"] + a.define
plain = out / "plain"
shutil.rmtree(plain, ignore_errors=True)
plain.mkdir()
mini = plain / "XLua.Mini.dll"
run([a.compiler, "-target:library", "-unsafe", "-define:" + ";".join(defines), "-out:" + str(mini)] + sources("XLua.Mini"))

# Mono.Cecil, pinned and hashed by build_tools.py.
tools = out / "tools"
run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools), "--compiler", a.compiler])
cecil = tools / "Mono.Cecil.dll"

sys.path.insert(0, str(repo / "Tools"))
import build_source_generator
analyzer = build_source_generator.build(out / "source-gen-build")
dotnet = build_source_generator.dotnet_path()
cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
api = a.mono_api or Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
if not cscs or not (api / "mscorlib.dll").is_file():
    raise SystemExit("needs the .NET SDK's csc.dll and Mono's lib/mono/4.5 (--mono-api)")

# The game assembly, with the generator, at C# 9 (Unity's default).
fixture = plain / "ObfuscationFixture.exe"
generated = plain / "generated"
generated.mkdir()
refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]
csc = run([dotnet, cscs[-1], "-nologo", "-noconfig", "-nostdlib", "-langversion:9", "-deterministic",
           "-define:" + ";".join(defines), "-analyzer:" + str(analyzer), "-generatedfilesout:" + str(generated),
           "-out:" + str(fixture), "-r:" + str(mini)] + ["-r:" + str(r) for r in refs]
          + [repo / "Test/Native/ObfuscationFixture.cs"])
if re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|(error|warning) XLSG", csc):
    raise SystemExit("analyzer load problem, or a generator error or warning")
if not list(generated.rglob("XLua.ObfuzRules.g.cs")):
    raise SystemExit("the generator wrote no obfuz rules for the fixture")

# The rule file, as the Editor adapter writes it.
rules_tool = tools / "ObfuzRulesTool.exe"
run([a.compiler, "-define:" + ";".join(defines), "-r:" + str(mini), "-r:System.Xml.dll", "-out:" + str(rules_tool),
     repo / "Test/Native/ObfuzRulesTool.cs", repo / "Assets/XLua/Src/Editor/ObfuzRules.cs"])
shutil.copy2(rules_tool, plain / rules_tool.name)
rules = out / "xlua-ng-rules.xml"
run([a.mono, plain / rules_tool.name, rules, fixture], cwd=plain)
text = rules.read_text()
for expected in ['<assembly name="ObfuscationFixture">', '<type name="Game.Reflected" obName="0" applyToMembers="*" />',
                 '<method name="Secret" obName="0" />', '<property name="X" obName="0" />', '<type name="Game.Big"',
                 '<field name="*" obName="0" />',
                 '<type name="XLua.*" obName="0" applyToMembers="*" />']:
    if expected not in text:
        raise SystemExit("the rule file lacks " + expected)

renamer = tools / "ObfuscationRenamer.exe"
run([a.compiler, "-r:" + str(cecil), "-r:System.Xml.dll", "-r:System.Core.dll", "-out:" + str(renamer),
     repo / "Test/Native/ObfuscationRenamer.cs"])


def check_link_xml_names(mapping):
    """Every type and member of the fixture that link.xml names kept its name."""
    files = list(generated.rglob("XLua.LinkXml.g.cs"))
    if len(files) != 1:
        raise SystemExit("expected one XLua.LinkXml.g.cs, found %d" % len(files))
    text = files[0].read_text()
    start = text.index('Xml = @"') + len('Xml = @"')
    linker = ET.fromstring(text[start:text.index('";', start)].replace('""', '"'))
    renamed = set()
    for line in mapping.read_text().splitlines():
        kind, _, rest = line.partition(" ")
        renamed.add((kind, rest.split(" -> ")[0]))
    named, problems = 0, []
    for assembly in linker.findall("assembly"):
        if assembly.get("fullname") != fixture.stem:
            continue
        for t in assembly.findall("type"):
            name = t.get("fullname")
            named += 1
            if ("type", name) in renamed:
                problems.append("type " + name)
            for m in t:
                if (m.tag, name + "::" + m.get("name")) in renamed:
                    problems.append(m.tag + " " + name + "::" + m.get("name"))
    if named == 0:
        raise SystemExit("link.xml names no type of " + fixture.stem)
    if problems:
        raise SystemExit("the obfuz rules renamed what link.xml names by its original name: " + ", ".join(problems))
    print("link.xml: the %d types of %s it names kept their names and named members" % (named, fixture.stem))


env = dict(os.environ)
for mode, extra in [("plain", None), ("rules", ["--rules", rules]), ("attributes", ["--attributes"])]:
    folder = out / mode
    if extra is not None:
        shutil.rmtree(folder, ignore_errors=True)
        folder.mkdir()
        shutil.copy2(mini, folder / mini.name)
        run([a.mono, renamer, fixture, folder / fixture.name, folder / "mapping.txt"] + extra + [plain, api, api / "Facades"],
            cwd=tools)
    shutil.copy2(a.plugin, folder / a.plugin.name)
    run_env = dict(env)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        run_env[key] = str(folder) + os.pathsep + env.get(key, "")
    if mode == "rules":
        check_link_xml_names(folder / "mapping.txt")
    log = run([a.mono, folder / fixture.name, mode] + EXPECTED_FAILURES[mode], timeout=a.timeout, cwd=folder, env=run_env)
    (out / ("fixture-" + mode + ".log")).write_text(log)
    if "XLUA_OBFUSCATION_FIXTURE_PASS" not in log:
        raise SystemExit("the fixture did not pass in mode " + mode)
print("XLUA_OBFUSCATION_CHECK_PASS: plain, renamed with the obfuz rules, renamed with [Obfuscation] only")
