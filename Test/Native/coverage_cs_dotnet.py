"""Measure branch and line coverage of xLua's C# code on .NET with coverlet.

Mono's coverage profiler (coverage_cs.py) counts sequence points only. This
script runs the same managed suite as run_managed.py --gen-code
--gen-code-minimize on .NET instead, under coverlet, which instruments the
assemblies with Mono.Cecil and counts each outcome of every conditional
branch:

  reflection        XLuaUnitTest with XLua.Mini (reflection and CodeEmit)
  gencode           XLuaUnitTestGenCode with wrappers that XLuaGenerate wrote
  gencode-minimize  the same with GEN_CODE_MINIMIZE
  generator         the two XLuaGenerate runs that write those wrappers, and
                    XLuaGenerate --luacats on the test assembly

Everything is compiled with the .NET SDK (Debug, portable PDBs) from the
source lists in General/vs2013, as SDK-style projects written into the
output directory; nothing in the checkout changes. The suite, the generator
and the generated wrappers all run on .NET, so the wrappers are generated for
.NET's base library. libxlua is loaded by P/Invoke from the assembly's
folder, as on Mono. XLUA_GENERAL compiles out every UnityEngine use, so no
Unity stubs are needed.

coverlet.console (a .NET tool package) is downloaded once from nuget.org into
the same cache as Tools/build_tools.py (Tools/.cache or $XLUA_TOOLS_CACHE),
checked against its SHA-512 and run with `dotnet coverlet.console.dll`; it is
not installed as a tool. The .NET SDK is the one pinned in mise.toml
(`mise install dotnet`; the script asks `mise which dotnet`) and must report
exactly that version, since mise checks no hash of the SDK archive (its
lockfile records none for dotnet). Without it, `dotnet` on PATH is used, or pass --dotnet; any SDK
8.0 or later works then.

Reports, in <output>/coverage/<name>.txt and summary.json: one per mode
above, `suite` (the three suite runs together) and `combined` (all six
processes). Files are grouped as runtime (Assets/XLua/Src outside Editor),
editor (Assets/XLua/Src/Editor) and generated (the wrappers). Test code is
excluded. branches.txt lists every runtime and editor branch that never ran,
by file and line, and the coverlet JSON and Cobertura files of each process
are kept next to the reports.

A branch is one outcome of a conditional IL branch (both sides of an `if`,
each case of a `switch`), as coverlet counts them. The modes compile the same
source with other symbols and into other assemblies, so `suite` and
`combined` match branches by file, line and their order on the line. A line
counts when it has a sequence point.
"""
import argparse
import collections
import hashlib
import html
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

from roslyn_fixtures import roslyn_sources

COVERLET_VERSION = "10.1.0"
# SHA-512 of coverlet.console.10.1.0.nupkg, as listed by the nuget.org catalog (packageHash).
COVERLET_SHA512 = ("7ed3abf1b61c44a60216355d3a4991faa963e768d987cfb47dc35a0cf0c6f69f"
                   "8ec2bd08344dd0466a8bbcc9aac1d6d63a458af1dd1fac6f1e618868fa0b4345")
COVERLET_URL = "https://www.nuget.org/api/v2/package/coverlet.console/" + COVERLET_VERSION
# The package has the tool for these target frameworks; the newest one that the
# installed runtime can run is used.
COVERLET_TFMS = [10, 9, 8]
EXPECTED_TESTS = 349  # as in run_managed.py

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, help="host-native libxlua (required unless --report-only)")
p.add_argument("--output", type=Path, required=True)
p.add_argument("--dotnet", help=".NET SDK 8.0 or later (default: the mise.toml pin, else dotnet on PATH)")
p.add_argument("--framework", help="target framework, e.g. net8.0 (default: the SDK's own version)")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua, the tools and the test assemblies (repeatable)")
p.add_argument("--cache", type=Path, default=os.environ.get("XLUA_TOOLS_CACHE"),
               help="download cache for coverlet.console (default: $XLUA_TOOLS_CACHE or Tools/.cache)")
p.add_argument("--timeout", type=int, default=900, help="seconds for each build or test process")
p.add_argument("--report-only", action="store_true",
               help="reuse the coverlet results already in <output>/coverage")
a = p.parse_args()
out = a.output.resolve()
cov = out / "coverage"
src = (repo / "Assets/XLua/Src").resolve()
editor = src / "Editor"
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
           DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", MSBUILDDISABLENODEREUSE="1",
           DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
           # Mono runs the suite in en-US whatever the system locale; .NET follows
           # it, and tostring() of a C# decimal or float uses the current culture
           # (a comma in Turkish, German, ...). The invariant culture keeps the
           # suite's expected strings, and needs no ICU on Linux.
           DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
# NuGet's restore needs a package folder even though the projects reference no packages.
env.setdefault("NUGET_PACKAGES", str(out / "nuget-packages"))


def find_dotnet():
    """--dotnet, else the SDK pinned in mise.toml if mise installed it, else dotnet on PATH."""
    if a.dotnet:
        return a.dotnet, "--dotnet"
    mise = shutil.which("mise")
    if mise:
        found = subprocess.run([mise, "which", "dotnet"], cwd=repo, stdout=subprocess.PIPE,
                               stderr=subprocess.DEVNULL, text=True).stdout.strip()
        if found and Path(found).is_file():
            # The SDK's own folder, not a DOTNET_ROOT left in the environment.
            env["DOTNET_ROOT"] = str(Path(found).parent)
            return found, "mise.toml"
    found = shutil.which("dotnet")
    if found:
        return found, "PATH"
    raise SystemExit("no .NET SDK: run `mise install dotnet` for the version in mise.toml, "
                     "put dotnet 8.0 or later on PATH, or pass --dotnet")


def run(cmd, cwd=None, log=None, check=True):
    """Runs cmd in its own process group; a timeout kills the whole group."""
    cmd = [str(c) for c in cmd]
    print("+", " ".join(cmd), flush=True)
    stdout = open(log, "w") if log else None
    try:
        process = subprocess.Popen(cmd, cwd=cwd, env=env, stdout=stdout, stderr=subprocess.STDOUT if log else None,
                                   start_new_session=True)
        try:
            code = process.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise SystemExit("timed out after %d s: %s" % (a.timeout, " ".join(cmd)))
    finally:
        if stdout:
            stdout.close()
    if check and code:
        if log:
            print(Path(log).read_text(errors="replace")[-4000:])
        raise SystemExit("exit code %d: %s" % (code, " ".join(cmd)))
    return code


def sources(name):
    """The project's sources, with the mcs-only fixtures rewritten for Roslyn (roslyn_fixtures.py)."""
    project = repo / "General/vs2013" / (name + ".csproj")
    files = [(project.parent / x.attrib["Include"].replace("\\", "/")).resolve()
             for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]
    return roslyn_sources(files, repo, out / "roslyn-fixtures")


def fetch_coverlet(cache):
    """Returns the folder of the verified coverlet.console package, downloading it once."""
    nupkg = cache / ("coverlet.console.%s.nupkg" % COVERLET_VERSION)
    if not (nupkg.is_file() and hashlib.sha512(nupkg.read_bytes()).hexdigest() == COVERLET_SHA512):
        cache.mkdir(parents=True, exist_ok=True)
        print("Downloading", COVERLET_URL, flush=True)
        with urllib.request.urlopen(COVERLET_URL, timeout=60) as response:
            data = response.read()
        digest = hashlib.sha512(data).hexdigest()
        if digest != COVERLET_SHA512:
            raise SystemExit("coverlet.console %s: SHA-512 mismatch, got %s" % (COVERLET_VERSION, digest))
        partial = nupkg.with_suffix(".part")
        partial.write_bytes(data)
        partial.replace(nupkg)
    tool = out / ("coverlet.console-" + COVERLET_VERSION)
    if not tool.is_dir():
        with zipfile.ZipFile(nupkg) as package:
            for name in package.namelist():
                if name.startswith("tools/") and not name.endswith("/"):
                    target = tool / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(package.read(name))
    return tool


if not a.report_only:
    if a.plugin is None:
        p.error("--plugin is required unless --report-only")
    plugin = a.plugin.resolve()
    a.dotnet, source = find_dotnet()
    out.mkdir(parents=True, exist_ok=True)
    # Run where the projects are built, so that the same global.json (if any) applies.
    sdk = subprocess.run([a.dotnet, "--version"], cwd=out, stdout=subprocess.PIPE, text=True, check=True,
                         env=env).stdout.strip()
    pin = re.search(r'^dotnet = "([^"]+)"$', (repo / "mise.toml").read_text(), re.M)
    if source == "mise.toml" and (pin is None or sdk != pin.group(1)):
        raise SystemExit("%s reports SDK %s, but mise.toml pins %s" % (a.dotnet, sdk, pin and pin.group(1)))
    if source != "mise.toml" and pin and sdk != pin.group(1):
        print("Note: SDK %s is not the %s pinned in mise.toml (`mise install dotnet`)" % (sdk, pin.group(1)), flush=True)
    major = int(sdk.split(".")[0])
    if major < 8:
        raise SystemExit(".NET SDK 8.0 or later is needed, found " + sdk)
    framework = a.framework or "net%d.0" % major
    print("XLUA_DOTNET_SDK:", sdk, framework, "from", source, a.dotnet, flush=True)
    tool_tfm = next(t for t in COVERLET_TFMS if t <= major)
    coverlet = fetch_coverlet((a.cache or repo / "Tools/.cache").resolve()) / ("tools/net%d.0/any/coverlet.console.dll" % tool_tfm)
    shutil.rmtree(cov, ignore_errors=True)
    cov.mkdir(parents=True)
    projects = out / "projects"
    shutil.rmtree(projects, ignore_errors=True)
    projects.mkdir()

    def build(name, output_type, files, defines, out_dir, references=(), resources=None):
        """Writes an SDK-style project for the given sources and builds it into out_dir."""
        folder = projects / out_dir.name / name
        folder.mkdir(parents=True)
        compile_items = "".join('    <Compile Include="%s" />\n' % html.escape(str(f)) for f in files)
        reference_items = "".join('    <Reference Include="%s" />\n' % html.escape(str(r)) for r in references)
        resource_items = ('    <EmbeddedResource Include="%s" LogicalName="%s" />\n' % resources) if resources else ""
        (folder / (name + ".csproj")).write_text("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>%s</TargetFramework>
    <OutputType>%s</OutputType>
    <AssemblyName>%s</AssemblyName>
    <RootNamespace></RootNamespace>
    <EnableDefaultItems>false</EnableDefaultItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>%s</DefineConstants>
    <Configuration>Debug</Configuration>
    <Optimize>false</Optimize>
    <DebugType>portable</DebugType>
    <Deterministic>true</Deterministic>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS0108;CS0114;CS0162;CS0168;CS0169;CS0219;CS0414;CS0618;CS0649;CS0659;CS0660;CS0661;CS0672;CS1717;CS8981;SYSLIB0050;SYSLIB0051</NoWarn>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
    <UseAppHost>false</UseAppHost>
    <OutputPath>%s/</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  <ItemGroup>
%s%s%s  </ItemGroup>
</Project>
""" % (framework, output_type, name, ";".join(defines), html.escape(str(out_dir)),
       compile_items, reference_items, resource_items))
        run([a.dotnet, "build", folder / (name + ".csproj"), "-nologo", "-v:q", "-clp:ErrorsOnly", "-nodeReuse:false"],
            log=out / ("build-%s-%s.log" % (out_dir.name, name)))
        return out_dir / (name + ".dll")

    # XLuaGenerate reads its templates from XLuaTemplates.resx, whose entries
    # are file references in gb2312, which .NET's resource compiler cannot
    # read. The templates are ASCII, so they go inline; &#13; keeps the CRLF
    # line endings that resgen would keep.
    resx = projects / "XLuaTemplates.resx"
    entries = []
    for data in ET.parse(repo / "General/Src/XLuaTemplates.resx").getroot().iter("data"):
        if data.get("type", "").startswith("System.Resources.ResXFileRef"):
            path = data.find("value").text.split(";")[0].replace("\\", "/")
            text = (repo / "General/Src" / path).read_text(encoding="ascii")
            value = html.escape(text, quote=False).replace("\r", "&#13;")
            entries.append('  <data name="%s" xml:space="preserve"><value>%s</value></data>\n' % (data.get("name"), value))
    resx.write_text('<?xml version="1.0" encoding="utf-8"?>\n<root>\n'
                    '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>\n'
                    '  <resheader name="version"><value>2.0</value></resheader>\n'
                    '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>\n'
                    '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>\n'
                    + "".join(entries) + "</root>\n")

    base = ["XLUA_GENERAL"] + a.define
    runs = []
    # The test data, laid out as LuaTestCommon expects: the suite runs in General/.
    shutil.copytree(repo / "Test/UnitTest", out / "Test/UnitTest", dirs_exist_ok=True)
    (out / "General").mkdir(exist_ok=True)

    def coverlet_run(name, folder, target_args, include_test_assembly, cwd, log_name, test_assembly=None):
        """Runs `dotnet target_args` under coverlet, instrumenting the assemblies in folder."""
        result = cov / name
        exclude = ["**/Test/**", "**/General/Src/**"]
        cmd = [a.dotnet, coverlet, test_assembly or folder, "--target", a.dotnet,
               "--targetargs", " ".join('"%s"' % x for x in target_args),
               "--format", "json", "--format", "cobertura", "--output", str(result) + "/",
               "--exclude-assemblies-without-sources", "None"]
        if include_test_assembly:
            cmd.append("--include-test-assembly")
        for pattern in exclude:
            cmd += ["--exclude-by-file", pattern]
        run(cmd, cwd=cwd, log=out / log_name)
        # coverlet reports 0% instead of failing when it instrumented nothing.
        modules = json.loads((result / "coverage.json").read_text())
        if not any(hits for module in modules.values() for classes in module.values()
                   for methods in classes.values() for method in methods.values()
                   for hits in method["Lines"].values()):
            raise SystemExit("%s: coverlet recorded no hits; see %s" % (name, out / log_name))
        runs.append({"name": name, "json": str(result / "coverage.json")})
        (cov / "runs.json").write_text(json.dumps(runs, indent=1) + "\n")
        return (out / log_name).read_text(errors="backslashreplace")

    def check_suite(log, name):
        counts = {mode: (ran, ok) for mode, ran, ok in re.findall(
            r"\[luaunit (normal|coroutine)\] Ran (\d+) tests in \S+ seconds, (\d+) success", log)}
        expected = (str(EXPECTED_TESTS), str(EXPECTED_TESTS))
        if counts != {"coroutine": expected, "normal": expected}:
            raise SystemExit("%s: expected %d passing tests in both modes on .NET: %r" % (name, EXPECTED_TESTS, counts))
        print("XLUA_DOTNET_SUITE_PASS: %s, %d tests in each of two modes" % (name, EXPECTED_TESTS), flush=True)

    # Reflection.
    reflection = out / "reflection"
    mini = build("XLua.Mini", "Library", sources("XLua.Mini"), base, reflection)
    test = build("XLuaUnitTest", "Exe", sources("XLuaUnitTest"), base, reflection, [mini])
    shutil.copy2(plugin, reflection / plugin.name)
    log = coverlet_run("managed-suite", reflection, [test], False, out / "General", "managed-suite.log", test)
    check_suite(log, "reflection")

    # Generated code, with and without GEN_CODE_MINIMIZE.
    def gen_code(defines, suffix):
        defines = base + defines
        tools = out / ("tools" + suffix)
        tools_mini = build("XLua.Mini", "Library", sources("XLua.Mini"), defines, tools)
        generator_sources = [repo / "Assets/XLua/Src/Editor" / f for f in
                             ["Generator.cs", "LuaCATSGenerator.cs", "SetupValidator.cs"]]
        generator_sources += [repo / "General/Src/XLuaGenerate.cs", repo / "General/Src/XLuaTemplates.Designer.cs"]
        generate = build("XLuaGenerate", "Exe", generator_sources, defines + ["XLUA_ALL_OBSOLETE"], tools,
                         [tools_mini], (html.escape(str(resx)), "XLuaGenerate.Src.XLuaTemplates.resources"))
        shutil.copy2(plugin, tools / plugin.name)
        gen = out / ("Gen" + suffix)
        shutil.rmtree(gen, ignore_errors=True)
        coverlet_run("xluagenerate" + suffix, tools, [generate, test, str(gen) + "/"], True, tools,
                     "xluagenerate%s.log" % suffix, generate)
        generated = sorted(gen.glob("*.cs"))
        if not generated:
            raise SystemExit("XLuaGenerate wrote no wrappers")
        folder = out / ("gencode" + suffix)
        project_sources = [f for f in sources("XLuaUnitTestGenCode") if "/General/Gen2/" not in str(f)]
        exe = build("XLuaUnitTestGenCode", "Exe", project_sources + generated, defines, folder)
        shutil.copy2(plugin, folder / plugin.name)
        log = coverlet_run("managed-suite-gencode" + suffix, folder, [exe], True, out / "General",
                           "managed-suite-gencode%s.log" % suffix, exe)
        check_suite(log, "gencode" + suffix)
        return tools, generate

    tools, generate = gen_code([], "")
    gen_code(["GEN_CODE_MINIMIZE"], "-minimize")
    coverlet_run("xluagenerate-luacats", tools, [generate, "--luacats", test, out / "LuaTypes"], True, tools,
                 "xluagenerate-luacats.log", generate)
    subprocess.run([a.dotnet, "build-server", "shutdown"], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def category(path):
    path = Path(path)
    if path.is_relative_to(editor):
        return "editor"
    if path.is_relative_to(src):
        return "runtime"
    if path.is_relative_to(out) and path.parent.name.startswith("Gen"):
        return "generated"
    return None


def load_run(run_info):
    """{(file, line): hits} and {(file, line, k): hits} for one coverlet result;
    k is the branch's position among the branches of its line."""
    lines = collections.Counter()
    branches = collections.Counter()
    data = json.loads(Path(run_info["json"]).read_text())
    for module in data.values():
        for document, classes in module.items():
            document = str(Path(document).resolve())
            if category(document) is None:
                continue
            per_line = collections.defaultdict(list)
            for methods in classes.values():
                for method in methods.values():
                    for line, hits in method["Lines"].items():
                        key = (document, int(line))
                        lines[key] = max(lines.get(key, 0), hits)
                    for branch in method["Branches"]:
                        per_line[branch["Line"]].append(branch)
            for line, found in per_line.items():
                found.sort(key=lambda b: (b["Offset"], b["Path"], b["EndOffset"]))
                for k, branch in enumerate(found):
                    key = (document, line, k)
                    branches[key] = max(branches.get(key, 0), branch["Hits"])
    return lines, branches


runs = json.loads((cov / "runs.json").read_text())
data = {r["name"]: load_run(r) for r in runs}
suite = {"managed-suite": "reflection", "managed-suite-gencode": "gencode",
         "managed-suite-gencode-minimize": "gencode-minimize"}
reports = {mode: [name] for name, mode in suite.items() if name in data}
reports["suite"] = [name for name in suite if name in data]
reports["generator"] = [name for name in data if name.startswith("xluagenerate")]
reports["combined"] = list(data)


def merge(names):
    lines, branches = collections.Counter(), collections.Counter()
    for name in names:
        run_lines, run_branches = data[name]
        for key, hits in run_lines.items():
            lines[key] = max(lines.get(key, 0), hits)
        for key, hits in run_branches.items():
            branches[key] = max(branches.get(key, 0), hits)
    return lines, branches


def percent(covered, total):
    return round(100.0 * covered / total, 1) if total else 0.0


def display(file):
    path = Path(file)
    return str(path.relative_to(repo)) if path.is_relative_to(repo) else str(path.relative_to(out))


summary = {}
for report, names in reports.items():
    if not names:
        continue
    lines, branches = merge(names)
    files = collections.defaultdict(lambda: collections.defaultdict(lambda: [0, 0, 0, 0]))
    for (file, _), hits in lines.items():
        row = files[category(file)][file]
        row[1] += 1
        row[0] += hits > 0
    for (file, _, _), hits in branches.items():
        row = files[category(file)][file]
        row[3] += 1
        row[2] += hits > 0
    text = ["%s: %s" % (report, ", ".join(names)), ""]
    summary[report] = {}
    for group in ["runtime", "editor", "generated"]:
        if group not in files:
            continue
        rows = files[group]
        total = [sum(r[i] for r in rows.values()) for i in range(4)]
        summary[report][group] = {
            "lines": total[1], "covered_lines": total[0], "line_percent": percent(total[0], total[1]),
            "branches": total[3], "covered_branches": total[2], "branch_percent": percent(total[2], total[3]),
            "files": {display(f): {"lines": r[1], "covered_lines": r[0], "line_percent": percent(r[0], r[1]),
                                   "branches": r[3], "covered_branches": r[2], "branch_percent": percent(r[2], r[3])}
                      for f, r in rows.items()}}
        text.append("%-58s %15s %7s %15s %7s" % (group, "lines", "", "branches", ""))
        if group != "generated":
            for f, r in sorted(rows.items(), key=lambda x: display(x[0])):
                text.append("%-58s %7d/%-7d %6.1f%% %7d/%-7d %6.1f%%" % (
                    display(f), r[0], r[1], percent(r[0], r[1]), r[2], r[3], percent(r[2], r[3])))
        text.append("%-58s %7d/%-7d %6.1f%% %7d/%-7d %6.1f%%" % (
            "TOTAL " + group + " (%d files)" % len(rows), total[0], total[1], percent(total[0], total[1]),
            total[2], total[3], percent(total[2], total[3])))
        text.append("")
    (cov / (report + ".txt")).write_text("\n".join(text) + "\n")
    print("\n".join(text))
(cov / "summary.json").write_text(json.dumps(summary, indent=1, sort_keys=True) + "\n")

# Branches that never ran in any process, per runtime or editor line.
_, branches = merge(reports["combined"])
missed = collections.defaultdict(lambda: [0, 0])
for (file, line, _), hits in branches.items():
    if category(file) in ("runtime", "editor"):
        missed[(display(file), line)][1] += 1
        missed[(display(file), line)][0] += hits == 0
(cov / "branches.txt").write_text("missed/branches  line\n" + "".join(
    "%3d/%-3d %s:%d\n" % (m, t, f, line) for (f, line), (m, t) in sorted(missed.items()) if m))


def stat(report, group, kind):
    return summary.get(report, {}).get(group, {}).get(kind + "_percent", 0.0)


print("XLUA_CS_BRANCH_COVERAGE: runtime branches %.1f%% in the suite (reflection %.1f%%, gencode %.1f%%, "
      "gencode-minimize %.1f%%), %.1f%% combined; runtime lines %.1f%% in the suite; report in %s" % (
          stat("suite", "runtime", "branch"), stat("reflection", "runtime", "branch"),
          stat("gencode", "runtime", "branch"), stat("gencode-minimize", "runtime", "branch"),
          stat("combined", "runtime", "branch"), stat("suite", "runtime", "line"), cov))
