"""LuaCATS from a real Unity editor against Test/LuaCATS/expected (source generator milestone M10).

    python3 Test/UnityMatrix/luacats_unity.py --unity /path/Unity.app/Contents/MacOS/Unity \\
        --unity-version 6000.3.27f2 --plugin BUILD/arm64/libxlua.dylib --output DIR [--no-ref-readonly]

Prepares an empty project in the asmdef layout (Assets/XLua/Core and Assets/XLua/Src with their Editor
folders and asmdefs, the macOS ARM64 Editor plugin with the repository's .meta), builds the xLua source
generator (Tools/build_source_generator.py) into DIR/source-gen-build and installs it in
Assets/XLua/Src/Analyzers, and puts Test/LuaCATS/Sample.cs (the Roslyn copy of
Test/Native/roslyn_fixtures.py) and SampleAttributes.cs into Assembly-CSharp. The editor then runs
XLua > Generate LuaCATS Definitions in batch mode, and the LuaTypes folder must equal what
Test/Native/luacats_check.py compares on the host:

- by default Test/LuaCATS/expected, with -langversion:12 in Assets/csc.rsp for the C# 12 `ref readonly`
  parameters of Sample.cs: an editor whose Roslyn knows C# 12 (Unity 6000.6);
- with --no-ref-readonly, Sample.cs compiled with LUACATS_NO_REF_READONLY, which leaves those members
  out, at the editor's own language version, against Test/LuaCATS/expected-no-ref-readonly over
  expected: Unity 2021.3 to 6000.3, whose Roslyn 4.3.1 cannot compile `ref readonly` parameters.

The .luarc.json the editor writes beside LuaTypes must equal Test/LuaCATS/expected-luarc.json (a Lua 5.5
plugin), and the log must say that the definitions come from the generator's manifest. One editor at a
time (wrap the command in the machine's Unity lock where there is one); the launch has a timeout that
kills the editor's process group. --keep-library keeps Library/ (about 1 GB) for a rerun.

--platform linux (unity-matrix.yml, Linux editors) installs the x86_64 libxlua.so as the Editor plugin
instead. --prepare-only stops after preparing the project, and --verify-only runs only the checks, on
DIR/build.log, which LuaCATSStep.Run writes with -xluaBuildLog (every message it logs; the compiler's
own messages are not in it). unity-matrix.yml launches the editor itself between the two (GameCI's
unity-builder, which holds the licence and sends the editor log to stdout):

    luacats_unity.py --prepare-only --platform linux --unity-version V --plugin SO --output DIR [--no-ref-readonly]
    Unity -projectPath DIR/project -buildTarget Linux64 -executeMethod LuaCATSStep.Run -quit -xluaBuildLog DIR/build.log
    luacats_unity.py --verify-only --output DIR [--no-ref-readonly]
"""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time

from unity_adb import unity_env

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / "Test/Native"))
from roslyn_fixtures import roslyn_sources  # noqa: E402

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, help="the editor executable (Unity.app/Contents/MacOS/Unity)")
p.add_argument("--unity-version", help="written to ProjectSettings/ProjectVersion.txt")
p.add_argument("--platform", choices=["macos", "linux"], default="macos", help="the Editor's platform (default macos)")
p.add_argument("--plugin", type=Path,
               help="the Editor plugin, Lua 5.5: macOS ARM64 libxlua.dylib, or x86_64 libxlua.so with --platform linux")
p.add_argument("--output", type=Path, required=True, help="an empty folder for the project, the generator build and logs")
p.add_argument("--no-ref-readonly", action="store_true",
               help="compile Sample.cs with LUACATS_NO_REF_READONLY (for Roslyn 4.3.1, Unity 2021.3 to 6000.3)")
p.add_argument("--timeout", type=int, default=1800, help="seconds for the editor launch")
p.add_argument("--keep-library", action="store_true", help="keep the project's Library folder")
mode = p.add_mutually_exclusive_group()
mode.add_argument("--prepare-only", action="store_true", help="prepare the project and stop")
mode.add_argument("--verify-only", action="store_true",
                  help="check DIR/build.log (-xluaBuildLog) and the definitions of a launch made elsewhere")
a = p.parse_args()
needed = [] if a.verify_only else ["unity_version", "plugin"] + ([] if a.prepare_only else ["unity"])
missing = ["--" + n.replace("_", "-") for n in needed if getattr(a, n) is None]
if missing:
    p.error("this mode needs " + ", ".join(missing))

out = a.output.resolve()
if not a.verify_only and out.exists() and any(out.iterdir()):
    p.error("--output must be empty")
project = out / "project"
assets = project / "Assets"


def prepare():
    for folder in ["Assets/Editor", "Packages", "ProjectSettings"]:
        (project / folder).mkdir(parents=True, exist_ok=True)
    (project / "Packages/manifest.json").write_text('{"dependencies": {"com.unity.modules.unitywebrequestwww": "1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + a.unity_version + "\n")

    # The asmdef layout with the Editor code, as Test/MobileSmoke/prepare.py --source-gen copies it.
    src = assets / "XLua/Src"
    src.mkdir(parents=True)
    for f in (repo / "Assets/XLua/Src").iterdir():
        if f.is_file() and (f.suffix == ".cs" or f.name.startswith("XLua.asmdef")):
            shutil.copy2(f, src / f.name)
    shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", src / "TemplateEngine")
    shutil.copytree(repo / "Assets/XLua/Src/Editor", src / "Editor")
    shutil.copytree(repo / "Assets/XLua/Core", assets / "XLua/Core")
    plugin_rel = "x86_64/libxlua.so" if a.platform == "linux" else "arm64/libxlua.dylib"
    plugin = assets / "Plugins" / plugin_rel
    plugin.parent.mkdir(parents=True)
    shutil.copy2(a.plugin, plugin)
    shutil.copy2(repo / "Assets/Plugins" / (plugin_rel + ".meta"), str(plugin) + ".meta")
    subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output", str(out / "source-gen-build"),
                    "--install", str(src / "Analyzers")], check=True)

    # The fixture and its configuration in Assembly-CSharp.
    for f in roslyn_sources([repo / "Test/LuaCATS/Sample.cs"], repo, out / "roslyn-fixtures") + \
            [repo / "Test/LuaCATS/SampleAttributes.cs"]:
        shutil.copy2(f, assets / Path(f).name)
    (assets / "csc.rsp").write_text("-define:LUACATS_NO_REF_READONLY\n" if a.no_ref_readonly else "-langversion:12\n")
    # -xluaBuildLog <file> also writes every message logged during the step to that file, for a
    # launch whose editor log goes to stdout (GameCI's unity-builder in unity-matrix.yml).
    (assets / "Editor/LuaCATSStep.cs").write_text("""using System;
using System.IO;
using UnityEngine;

public static class LuaCATSStep
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int l = Array.IndexOf(args, "-xluaBuildLog");
        StreamWriter log = l >= 0 && l + 1 < args.Length ? new StreamWriter(args[l + 1], false) { AutoFlush = true } : null;
        Application.LogCallback write = (message, stack, type) =>
        {
            lock (log)
            {
                log.WriteLine(message);
                if (type == LogType.Error || type == LogType.Exception) log.WriteLine(stack);
            }
        };
        if (log != null) Application.logMessageReceivedThreaded += write;
        try
        {
            CSObjectWrapEditor.Generator.GenLuaCATS();
            Debug.Log("XLUA_LUACATS_UNITY_DONE");
        }
        finally
        {
            if (log != null)
            {
                Application.logMessageReceivedThreaded -= write;
                log.Dispose();
            }
        }
    }
}
""")

if a.verify_only:
    # The launch ran elsewhere with -xluaBuildLog DIR/build.log; its exit status was checked there.
    log = out / "build.log"
    if not log.is_file():
        sys.exit("XLUA_LUACATS_UNITY_FAIL: no %s (-xluaBuildLog)" % log)
    code = 0
else:
    prepare()
    if a.prepare_only:
        print("XLUA_LUACATS_UNITY_PREPARED: %s" % project)
        sys.exit(0)
    log = out / "editor.log"
    cmd = [str(a.unity), "-batchmode", "-nographics", "-quit", "-projectPath", str(project), "-logFile", str(log),
           "-executeMethod", "LuaCATSStep.Run"]
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True, env=unity_env())
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        sys.exit("XLUA_LUACATS_UNITY_TIMEOUT after %ds (log %s)" % (a.timeout, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    if not a.keep_library:
        shutil.rmtree(project / "Library", ignore_errors=True)

text = log.read_text(errors="replace") if log.is_file() else ""
problems = []
if code != 0:
    problems.append("the editor exited with %d" % code)
for line in text.splitlines():
    if "error CS" in line or "XLSG" in line and ("warning" in line or "error" in line):
        problems.append(line.strip())
if "XLUA_LUACATS_UNITY_DONE" not in text:
    problems.append("no XLUA_LUACATS_UNITY_DONE in the log")
if "comes from the xLua source generator's manifest" not in text:
    problems.append("the log does not say that the definitions come from the generator's manifest")

# The expected tree: expected, with the overlay's files in place of theirs.
expected = repo / "Test/LuaCATS/expected"
overlay = repo / "Test/LuaCATS/expected-no-ref-readonly" if a.no_ref_readonly else None


def files(root):
    return {str(f.relative_to(root)) for f in root.rglob("*") if f.is_file()} if root.is_dir() else set()


wanted = {name: expected / name for name in files(expected)}
if overlay:
    wanted.update({name: overlay / name for name in files(overlay)})
types = project / "LuaTypes"
got = files(types)
if got != set(wanted):
    problems.append("file lists differ: only generated %s, only expected %s"
                    % (sorted(got - set(wanted)), sorted(set(wanted) - got)))
for name in sorted(got & set(wanted)):
    if not filecmp.cmp(types / name, wanted[name], shallow=False):
        diff = subprocess.run(["diff", "-u", str(wanted[name]), str(types / name)], stdout=subprocess.PIPE, text=True).stdout
        print(diff)
        problems.append(name + " differs")
luarc = project / ".luarc.json"
if not luarc.is_file() or luarc.read_text() != (repo / "Test/LuaCATS/expected-luarc.json").read_text():
    problems.append(".luarc.json differs from Test/LuaCATS/expected-luarc.json")
for line in text.splitlines():
    if "LuaCATS" in line and ("manifest" in line or "written to" in line):
        print("  " + line.strip())
fixture = "Sample.cs without ref readonly (LUACATS_NO_REF_READONLY)" if a.no_ref_readonly else "Sample.cs, -langversion:12"
if problems:
    sys.exit("XLUA_LUACATS_UNITY_FAIL: Unity %s, %s: %s (log %s)" % (a.unity_version or "", fixture, "; ".join(problems), log))
print("XLUA_LUACATS_UNITY_PASS: Unity %s, %s: %d files equal %s, .luarc.json equal"
      % (a.unity_version or "", fixture, len(got), "expected + expected-no-ref-readonly" if overlay else "expected"))
