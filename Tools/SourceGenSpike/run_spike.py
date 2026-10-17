#!/usr/bin/env python3
"""Source generator compatibility spike runner.

usage: run_spike.py <unity-version> <step> [<step> ...] [--variant scoped|rootlabel] [--gen 4.3.1|3.8]

Steps: prepare, compile, error, incremental, mono, ios, android.
Every Unity or player launch goes through locked_run.sh (shared lock, own process
group, timeout, disk guard). Output: /Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike.
"""
import argparse
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = "/Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike"
EDITOR_ROOTS = ["/Volumes/T7Data/Unity/Editors", "/Applications/Unity/Hub/Editor"]
LOCKED = os.path.join(HERE, "locked_run.sh")

PLUGIN_META = """fileFormatVersion: 2
guid: {guid}
labels:
{labels}
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

IMPORTERS = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
           "  executionOrder: 0\n  icon: {instanceID: 0}\n",
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n",
    ".json": "PackageManifestImporter:\n  externalObjects: {}\n",
}

VARIANTS = {
    # DLL next to the runtime asmdef: Unity scopes it to that assembly and its referencers.
    "scoped": ("Runtime/Analyzers", ["RoslynAnalyzer"]),
    # DLL in a package folder without asmdef, with the extra label.
    "rootlabel": ("Analyzers", ["RoslynAnalyzer", "RunOnlyOnAssembliesWithReference"]),
    # Control for rootlabel: same place, RoslynAnalyzer label only.
    "rootplain": ("Analyzers", ["RoslynAnalyzer"]),
}


def guid(path):
    return hashlib.md5(("sgspike/" + path).encode()).hexdigest()


def meta_for(rel, is_dir):
    head = "fileFormatVersion: 2\nguid: %s\n" % guid(rel)
    if is_dir:
        return head + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    imp = IMPORTERS[os.path.splitext(rel)[1]]
    return head + imp + "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def build_package(variant, gen):
    """Write the package with metas and the generator DLL, then pack it as a tarball."""
    dll = os.path.join(BASE, "gen-out", gen, "XLuaTest.Generator.dll")
    if not os.path.exists(dll):
        sys.exit("missing %s (run build_generator.sh)" % dll)
    src = os.path.join(HERE, "Package", "com.xlua-ng.sgspike")
    files = {}  # rel path -> bytes
    for root, dirs, names in os.walk(src):
        dirs.sort()
        for d in dirs:
            rel = os.path.relpath(os.path.join(root, d), src)
            files[rel + ".meta"] = meta_for(rel, True).encode()
        for n in sorted(names):
            if n.startswith(".") or n.endswith(".meta"):
                continue
            rel = os.path.relpath(os.path.join(root, n), src)
            with open(os.path.join(root, n), "rb") as f:
                files[rel] = f.read()
            files[rel + ".meta"] = meta_for(rel, False).encode()
    folder, labels = VARIANTS[variant]
    parts = folder.split("/")
    for i in range(len(parts)):
        rel = "/".join(parts[: i + 1])
        if rel + ".meta" not in files:
            files[rel + ".meta"] = meta_for(rel, True).encode()
    rel = folder + "/XLuaTest.Generator.dll"
    with open(dll, "rb") as f:
        files[rel] = f.read()
    files[rel + ".meta"] = PLUGIN_META.format(
        guid=guid("generator.dll"), labels="\n".join("- " + l for l in labels)).encode()

    os.makedirs(os.path.join(BASE, "packages"), exist_ok=True)
    tgz = os.path.join(BASE, "packages", "com.xlua-ng.sgspike-%s-%s.tgz" % (variant, gen))
    with tarfile.open(tgz, "w:gz") as tar:
        for rel in sorted(files):
            info = tarfile.TarInfo("package/" + rel)
            info.size = len(files[rel])
            info.mtime = 1760000000
            tar.addfile(info, io.BytesIO(files[rel]))
    return tgz


def editor(version):
    for r in EDITOR_ROOTS:
        exe = os.path.join(r, version, "Unity.app", "Contents", "MacOS", "Unity")
        if os.path.exists(exe):
            return exe
    sys.exit("no editor for " + version)


class Spike:
    def __init__(self, version, variant, gen):
        self.version, self.variant, self.gen = version, variant, gen
        tag = version + "-" + variant + ("" if gen == "4.3.1" else "-gen" + gen)
        self.proj = os.path.join(BASE, "projects", tag)
        self.logs = os.path.join(BASE, "logs", tag)
        os.makedirs(self.logs, exist_ok=True)
        self.results_path = os.path.join(BASE, "results", tag + ".json")
        os.makedirs(os.path.dirname(self.results_path), exist_ok=True)
        self.results = {}
        if os.path.exists(self.results_path):
            with open(self.results_path) as f:
                self.results = json.load(f)

    def save(self):
        with open(self.results_path, "w") as f:
            json.dump(self.results, f, indent=2, sort_keys=True)

    def prepare(self):
        tgz = build_package(self.variant, self.gen)
        template = os.path.join(HERE, "Project")
        shutil.copytree(os.path.join(template, "Assets"), os.path.join(self.proj, "Assets"), dirs_exist_ok=True)
        pk = os.path.join(self.proj, "Packages")
        os.makedirs(pk, exist_ok=True)
        mpath = os.path.join(pk, "manifest.json")
        manifest = {"dependencies": {}}
        if os.path.exists(mpath):
            with open(mpath) as f:
                manifest = json.load(f)
        manifest["dependencies"]["com.xlua-ng.sgspike"] = "file:" + tgz
        with open(mpath, "w") as f:
            json.dump(manifest, f, indent=2)
        lock = os.path.join(pk, "packages-lock.json")
        if os.path.exists(lock):
            os.remove(lock)
        cache = os.path.join(self.proj, "Library", "PackageCache")
        if os.path.isdir(cache):
            for n in os.listdir(cache):
                if n.startswith("com.xlua-ng.sgspike"):
                    shutil.rmtree(os.path.join(cache, n))
        self.results["prepare"] = {"tarball": tgz}
        print("prepared", self.proj)

    def locked(self, timeout, cmd, out):
        free = shutil.disk_usage("/").free / 2**30
        if free < 25:
            sys.exit("root disk below 25 GB (%.1f GB); stopping" % free)
        with open(out, "w") as f:
            p = subprocess.run(["bash", LOCKED, str(timeout)] + cmd, stdout=f, stderr=subprocess.STDOUT)
        with open(out) as f:
            tail = f.read()
        leftovers = subprocess.run(["pgrep", "-ilf", "projectpath " + self.proj], capture_output=True, text=True).stdout
        if leftovers.strip():
            print("WARNING leftover processes:\n" + leftovers)
        return p.returncode, tail

    def unity(self, step, args, timeout=1200, build_target=None):
        log = os.path.join(self.logs, step + ".log")
        cmd = [editor(self.version), "-batchmode", "-nographics", "-projectPath", self.proj, "-logFile", log]
        if build_target:
            cmd += ["-buildTarget", build_target]
        t0 = time.time()
        code, runner = self.locked(timeout, cmd + args, os.path.join(self.logs, step + ".runner.txt"))
        secs = int(time.time() - t0)
        text = open(log, errors="replace").read() if os.path.exists(log) else ""
        return code, secs, text, runner

    @staticmethod
    def scan(text):
        lines = text.splitlines()
        def uniq(pat):
            seen = []
            for l in lines:
                if re.search(pat, l) and l.strip() not in seen:
                    seen.append(l.strip())
            return seen
        return {
            "sg": [l for l in uniq(r"XLUA_SG_") if not l.endswith(": no-generator")],
            "scope_no_generator": len([l for l in uniq(r"XLUA_SG_SCOPE") if l.endswith(": no-generator")]),
            "xlsg": uniq(r"(warning|error) XLSG"),
            "analyzer_problems": uniq(r"CS8032|CS8784|CS8785|CS9057|CS8033|CS8034|Could not load file or assembly 'Microsoft\.CodeAnalysis"),
            "cs_errors": uniq(r"error CS\d+"),
            "cs0436": uniq(r"CS0436"),
            "compile_failed": "Scripts have compiler errors" in text or "compilation errors" in text,
        }

    def record(self, step, code, secs, text, runner, extra=None):
        r = {"exit": code, "secs": secs, "timeout": code == 124}
        r.update(self.scan(text))
        if code == 124:
            starts = [l for l in text.splitlines() if "XLUA_SG_" in l or "Compiling" in l or "Starting" in l]
            r["last_lines"] = starts[-5:] + text.splitlines()[-10:]
        if extra:
            r.update(extra)
        r["runner"] = runner.strip().splitlines()[-3:]
        self.results[step] = r
        self.save()
        print(json.dumps({step: r}, indent=2))
        return r

    def compile(self):
        code, secs, text, runner = self.unity("compile", ["-executeMethod", "SpikeProbe.Run"], timeout=1500)
        self.record("compile", code, secs, text, runner, {"pass": "XLUA_SG_PASS" in text})

    def error(self):
        dst = os.path.join(self.proj, "Assets", "UserGame", "ForceError.cs")
        shutil.copy(os.path.join(HERE, "ForceError.cs.txt"), dst)
        try:
            code, secs, text, runner = self.unity("error", ["-quit"], timeout=1200)
        finally:
            for p in (dst, dst + ".meta"):
                if os.path.exists(p):
                    os.remove(p)
        self.record("error", code, secs, text, runner,
                    {"pass": code != 0 and bool(re.search(r"error XLSG002", text))})

    def incremental(self):
        code, secs, text, runner = self.unity("incremental", ["-executeMethod", "SpikeProbe.RunIncremental"], timeout=1200)
        times = {}
        for k in ("EDITED", "RELOADED"):
            m = re.search(r"XLUA_SG_%s .* at (\S+)|XLUA_SG_%s at (\S+)" % (k, k), text)
            if m:
                times[k] = m.group(1) or m.group(2)
        self.record("incremental", code, secs, text, runner,
                    {"pass": "XLUA_SG_INCREMENTAL_PASS" in text, "times": times})

    def mono(self, stripping="High"):
        out = os.path.join(BASE, "players", os.path.basename(self.proj), "mac-mono-" + stripping, "SGSpike.app")
        if os.path.exists(os.path.dirname(out)):
            shutil.rmtree(os.path.dirname(out))
        code, secs, text, runner = self.unity("mono-build-" + stripping, [
            "-executeMethod", "SpikeProbe.BuildMacMono", "-sgOut", out, "-sgStripping", stripping],
            timeout=2400, build_target="OSXUniversal")
        r = self.record("mono-build-" + stripping, code, secs, text, runner,
                        {"pass": "XLUA_SG_BUILD_RESULT Succeeded" in text})
        if not r["pass"]:
            return
        exe = os.path.join(out, "Contents", "MacOS", "SGSpike")
        plog = os.path.join(self.logs, "mono-player-" + stripping + ".log")
        t0 = time.time()
        pcode, prunner = self.locked(180, [exe, "-batchmode", "-nographics", "-logFile", plog],
                                     os.path.join(self.logs, "mono-player-" + stripping + ".runner.txt"))
        ptext = open(plog, errors="replace").read() if os.path.exists(plog) else ""
        arch = subprocess.run(["file", exe], capture_output=True, text=True).stdout.strip()
        self.record("mono-player-" + stripping, pcode, int(time.time() - t0), ptext, prunner,
                    {"pass": "XLUA_SG_PLAYER_PASS" in ptext, "arch": arch})

    def ios(self):
        out = os.path.join(BASE, "players", os.path.basename(self.proj), "ios-sim")
        if os.path.exists(out):
            shutil.rmtree(out)
        code, secs, text, runner = self.unity("ios-build", [
            "-executeMethod", "SpikeProbe.BuildIosSimulator", "-sgOut", out, "-sgStripping", "High"],
            timeout=3600, build_target="iOS")
        self.record("ios-build", code, secs, text, runner, {"pass": "XLUA_SG_BUILD_RESULT Succeeded" in text, "out": out})

    def android(self):
        out = os.path.join(BASE, "players", os.path.basename(self.proj), "android-export")
        if os.path.exists(out):
            shutil.rmtree(out)
        code, secs, text, runner = self.unity("android-build", [
            "-executeMethod", "SpikeProbe.BuildAndroidExport", "-sgOut", out, "-sgStripping", "High"],
            timeout=3600, build_target="Android")
        self.record("android-build", code, secs, text, runner, {"pass": "XLUA_SG_BUILD_RESULT Succeeded" in text, "out": out})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("version")
    ap.add_argument("steps", nargs="+")
    ap.add_argument("--variant", default="scoped", choices=sorted(VARIANTS))
    ap.add_argument("--gen", default="4.3.1")
    ap.add_argument("--stripping", default="High")
    a = ap.parse_args()
    s = Spike(a.version, a.variant, a.gen)
    for step in a.steps:
        if step == "mono":
            s.mono(a.stripping)
        else:
            getattr(s, step)()


if __name__ == "__main__":
    main()
