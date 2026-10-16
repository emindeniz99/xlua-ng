"""lua-language-server download and check mode, shared by the LuaCATS checks."""
import hashlib
import json
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile
import urllib.request

VERSION = "3.19.1"
SHA256 = {
    "linux-x64": "e9235d2d72ef55bc41cf8c99cda2ed64777682024b4bb81f5dea425060c5cbb8",
    "linux-arm64": "abd2572e8fc929dc838a81ffb8473c5bce0bf39bfe8edb4b120b3b623176ce83",
}


def download(out):
    """Download the Linux release into out and return its executable."""
    out = Path(out)
    arch = {"x86_64": "linux-x64", "aarch64": "linux-arm64", "arm64": "linux-arm64"}[platform.machine()]
    name = "lua-language-server-%s-%s.tar.gz" % (VERSION, arch)
    archive = out / name
    if not archive.exists():
        out.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve("https://github.com/LuaLS/lua-language-server/releases/download/%s/%s"
                                   % (VERSION, name), archive)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != SHA256[arch]:
        raise SystemExit("unexpected sha256 for %s: %s" % (name, digest))
    install = out / "luals"
    shutil.rmtree(install, ignore_errors=True)
    with tarfile.open(archive) as tar:
        tar.extractall(install)
    return install / "bin/lua-language-server"


def check(luals, lua_file, libraries, out, runtime="Lua 5.4"):
    """Run check mode on lua_file alone; return sorted (line, code, message)."""
    lua_file, out = Path(lua_file), Path(out)
    workspace = out / ("ws-" + lua_file.stem)
    shutil.rmtree(workspace, ignore_errors=True)
    workspace.mkdir(parents=True)
    shutil.copy2(lua_file, workspace / lua_file.name)
    (workspace / ".luarc.json").write_text(json.dumps({
        "runtime.version": runtime,
        "workspace.library": [str(lib) for lib in libraries],
        "workspace.checkThirdParty": False,
    }, indent=2))
    logs = out / ("log-" + lua_file.stem)
    shutil.rmtree(logs, ignore_errors=True)
    # LuaLS exits with 1 when it reports problems; the report decides.
    cmd = [str(luals), "--check=" + str(workspace), "--checklevel=Warning", "--check_format=json",
           "--logpath=" + str(logs)]
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, cwd=workspace)
    report = logs / "check.json"
    found = []
    if report.exists():
        data = json.loads(report.read_text())
        # LuaLS writes [] when it finds nothing, else {uri: [diagnostic]}.
        for diags in (data.values() if isinstance(data, dict) else []):
            for d in diags:
                found.append((d["range"]["start"]["line"] + 1, d["code"], d["message"].splitlines()[0]))
    return sorted(found)


def check_good(luals, good_file, libraries, out, runtime="Lua 5.4"):
    """good_file must have no diagnostics at Warning level or above."""
    good_file = Path(good_file)
    good = check(luals, good_file, libraries, out, runtime)
    for line in good:
        print("%s:%d: %s: %s" % ((good_file.name,) + line))
    if good:
        raise SystemExit("%s has %d diagnostics" % (good_file.name, len(good)))
    print("LUACATS_LUALS_GOOD_PASS: %s, 0 diagnostics" % good_file.name)


def check_bad(luals, bad_file, libraries, out, runtime="Lua 5.4"):
    """Each `-- expect: <code>` line of bad_file must get exactly that code, other lines none."""
    bad_file = Path(bad_file)
    want = sorted((i + 1, m.group(1)) for i, text in enumerate(bad_file.read_text().splitlines())
                  for m in [re.search(r"-- expect: ([\w-]+)", text)] if m)
    bad = check(luals, bad_file, libraries, out, runtime)
    for line in bad:
        print("%s:%d: %s: %s" % ((bad_file.name,) + line))
    got = sorted(set((line, code) for line, code, _ in bad))
    if got != want:
        raise SystemExit("%s diagnostics differ:\n  expected %s\n  got      %s" % (bad_file.name, want, got))
    print("LUACATS_LUALS_BAD_PASS: %s, %d expected diagnostics" % (bad_file.name, len(bad)))
