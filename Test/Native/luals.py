"""lua-language-server download and check mode, shared by the LuaCATS checks."""
import hashlib
import json
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request

VERSION = "3.19.1"
# SHA-256 of the release archives lua-language-server-<VERSION>-<platform>.tar.gz,
# as the GitHub release lists them (asset digest) and as downloaded on
# 2026-10-18. Tools/dependencies.py reads this table.
SHA256 = {
    "darwin-arm64": "0bc077f4447f076b4c92c14e9fd303f5b569eda2ec74b4dca2b55f75fae2e90c",
    "darwin-x64": "eb373c159cbe556711d7cd316315de2dce969bfd54b31edb7eb9cab2937f2cca",
    "linux-arm64": "abd2572e8fc929dc838a81ffb8473c5bce0bf39bfe8edb4b120b3b623176ce83",
    "linux-x64": "e9235d2d72ef55bc41cf8c99cda2ed64777682024b4bb81f5dea425060c5cbb8",
}
URL = "https://github.com/LuaLS/lua-language-server/releases/download/%s/lua-language-server-%s-%s.tar.gz"


def host_platform():
    """The release platform name for this machine (Linux or macOS)."""
    system = {"linux": "linux", "darwin": "darwin"}.get(sys.platform)
    arch = {"x86_64": "x64", "amd64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(platform.machine().lower())
    if system is None or arch is None:
        raise SystemExit("--download-luals supports Linux and macOS on x64 and arm64, not %s %s; pass --luals"
                         % (sys.platform, platform.machine()))
    return system + "-" + arch


def download(out):
    """Download the release for this machine into out, check its SHA-256 and return its executable."""
    out = Path(out)
    host = host_platform()
    url = URL % (VERSION, VERSION, host)
    archive = out / url.rsplit("/", 1)[1]
    if not archive.exists():
        out.mkdir(parents=True, exist_ok=True)
        print("Downloading", url, flush=True)
        partial = archive.with_suffix(".part")
        urllib.request.urlretrieve(url, partial)
        partial.replace(archive)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != SHA256[host]:
        archive.unlink()
        raise SystemExit("unexpected sha256 for %s: %s (expected %s)" % (archive.name, digest, SHA256[host]))
    install = out / "luals"
    shutil.rmtree(install, ignore_errors=True)
    with tarfile.open(archive) as tar:
        tar.extractall(install)
    return install / "bin/lua-language-server"


def check(luals, lua_file, libraries, out, runtime="Lua 5.4", project=None):
    """Run check mode on lua_file alone; return sorted (line, code, message).

    With project, the workspace is a copy of that folder (for example the
    generated LuaTypes and .luarc.json) and its own .luarc.json is used;
    libraries and runtime are then ignored."""
    lua_file, out = Path(lua_file), Path(out)
    workspace = out / ("ws-" + lua_file.stem)
    shutil.rmtree(workspace, ignore_errors=True)
    if project is not None:
        shutil.copytree(project, workspace)
    else:
        workspace.mkdir(parents=True)
        (workspace / ".luarc.json").write_text(json.dumps({
            "runtime.version": runtime,
            "workspace.library": [str(lib) for lib in libraries],
            "workspace.checkThirdParty": False,
        }, indent=2))
    shutil.copy2(lua_file, workspace / lua_file.name)
    logs = out / ("log-" + lua_file.stem)
    shutil.rmtree(logs, ignore_errors=True)
    # LuaLS exits with 1 when it reports problems; the report decides.
    cmd = [str(luals), "--check=" + str(workspace), "--checklevel=Warning", "--check_format=json",
           "--logpath=" + str(logs)]
    print("+", " ".join(cmd), flush=True)
    code = subprocess.run(cmd, cwd=workspace).returncode
    report = logs / "check.json"
    # LuaLS writes [] when it finds nothing, else {uri: [diagnostic]}. No
    # report means it did not run (a missing binary or an unconfigured
    # version-manager shim), which must not pass as "no diagnostics".
    if not report.exists():
        raise SystemExit("lua-language-server wrote no %s (exit %d): %s" % (report, code, " ".join(cmd)))
    found = []
    data = json.loads(report.read_text())
    for diags in (data.values() if isinstance(data, dict) else []):
        for d in diags:
            found.append((d["range"]["start"]["line"] + 1, d["code"], d["message"].splitlines()[0]))
    return sorted(found)


def check_good(luals, good_file, libraries, out, runtime="Lua 5.4", project=None):
    """good_file must have no diagnostics at Warning level or above."""
    good_file = Path(good_file)
    good = check(luals, good_file, libraries, out, runtime, project)
    for line in good:
        print("%s:%d: %s: %s" % ((good_file.name,) + line))
    if good:
        raise SystemExit("%s has %d diagnostics" % (good_file.name, len(good)))
    print("LUACATS_LUALS_GOOD_PASS: %s, 0 diagnostics" % good_file.name)


def check_bad(luals, bad_file, libraries, out, runtime="Lua 5.4", project=None):
    """Each `-- expect: <code>` line of bad_file must get exactly that code, other lines none."""
    bad_file = Path(bad_file)
    want = sorted((i + 1, m.group(1)) for i, text in enumerate(bad_file.read_text().splitlines())
                  for m in [re.search(r"-- expect: ([\w-]+)", text)] if m)
    bad = check(luals, bad_file, libraries, out, runtime, project)
    for line in bad:
        print("%s:%d: %s: %s" % ((bad_file.name,) + line))
    got = sorted(set((line, code) for line, code, _ in bad))
    if got != want:
        raise SystemExit("%s diagnostics differ:\n  expected %s\n  got      %s" % (bad_file.name, want, got))
    print("LUACATS_LUALS_BAD_PASS: %s, %d expected diagnostics" % (bad_file.name, len(bad)))
