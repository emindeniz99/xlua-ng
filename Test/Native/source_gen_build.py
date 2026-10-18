"""Compile C# with the xLua source generator, for the host checks.

Mono's mcs cannot run a source generator and Mono's csc is Roslyn 3.9, too old
for a generator built against Roslyn 4.3.1, so the checks compile with the .NET
SDK's csc (mise.toml) and -analyzer:XLua.SourceGenerator.dll, against the Mono
assemblies that mcs compiles against (lib/mono/4.5), and run the result on Mono.
run_managed.py --source-gen does the same for the managed suite.

    import source_gen_build
    gen = source_gen_build.Builder(out / "source-gen-build")
    log = gen.compile(dest, sources, refs=[mini], defines=["XLUA_GENERAL"])
"""
from pathlib import Path
import re
import shutil
import subprocess
import sys

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools"))
import build_source_generator  # noqa: E402

LOAD_PROBLEMS = r"warning (CS8032|CS8784|CS8785|CS9057)"


class Builder:
    def __init__(self, build_dir, mono="mono", mono_api=None):
        self.analyzer = build_source_generator.build(Path(build_dir))
        self.dotnet = build_source_generator.dotnet_path()
        cscs = sorted(Path(self.dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
        api = Path(mono_api) if mono_api else Path(shutil.which(mono)).resolve().parents[1] / "lib/mono/4.5"
        if not cscs or not (api / "mscorlib.dll").is_file():
            raise SystemExit("compiling with the source generator needs the .NET SDK's csc.dll and Mono's lib/mono/4.5")
        self.csc = cscs[-1]
        self.refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]

    def compile(self, dest, sources, refs=(), defines=(), library=False, langversion="9", debug=False,
                allow_warnings=False, extra=()):
        """Compiles sources into dest with the generator; its files go to <dest stem>.generated next to dest.
        Fails on a compile error, an analyzer load warning and, unless allow_warnings, a generator warning
        or error. Returns csc's output."""
        dest = Path(dest)
        dest.parent.mkdir(parents=True, exist_ok=True)
        generated = dest.parent / (dest.stem + ".generated")
        shutil.rmtree(generated, ignore_errors=True)
        generated.mkdir()
        lines = ["-nologo", "-noconfig", "-nostdlib", "-unsafe", "-langversion:" + langversion, "-deterministic",
                 "-nowarn:0618,0649,0168,0219,0414,0067,0109,0162,8632",
                 "-define:" + ";".join(defines), "-analyzer:" + str(self.analyzer),
                 "-generatedfilesout:" + str(generated), "-out:" + str(dest)] \
            + (["-target:library"] if library else []) + (["-debug:portable"] if debug else []) \
            + ["-r:" + str(r) for r in self.refs + [Path(x) for x in refs]] + list(extra) + [str(s) for s in sources]
        rsp = dest.parent / (dest.stem + ".rsp")
        rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
        result = subprocess.run([self.dotnet, str(self.csc), "@" + str(rsp)], stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True)
        (dest.parent / (dest.stem + ".csc.log")).write_text(result.stdout)
        if result.returncode != 0 or re.search(LOAD_PROBLEMS, result.stdout) \
                or (not allow_warnings and re.search(r"(error|warning) XLSG", result.stdout)):
            print(result.stdout)
            raise SystemExit("%s does not compile with the source generator, or the generator did not load or warned; see %s"
                             % (dest.name, dest.parent / (dest.stem + ".csc.log")))
        return result.stdout

    @staticmethod
    def generated(dest):
        """The folder of the files the generator wrote for dest."""
        dest = Path(dest)
        return dest.parent / (dest.stem + ".generated")
