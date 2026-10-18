"""Run an exported iOS smoke player in the iOS Simulator and report its result.

Compiles the Xcode project that `SmokeBuild.BuildIos` exported with
`XLUA_SMOKE_IOS_SDK=SimulatorSDK` (`unity-ios-smoke`) with `xcodebuild -sdk
iphonesimulator`, unsigned, into --derived-data; installs the app on a booted
simulator, or boots --device (default: the newest iPhone of the newest iOS
runtime that has a device); launches it with its stdout and stderr copied to
--logs afterwards; and waits for `xlua-smoke-result.txt` in the app's Documents folder
(Application.persistentDataPath). The result line (`XLUA_NG_SMOKE_PASS`,
`XLUA_NG_CORE_SMOKE_PASS` or a `..._FAIL`) is printed; the exit status is 0 for
a pass. A simulator this script booted is shut down at the end.

    python3 Test/MobileSmoke/ios_sim_smoke.py --xcode-project OUT/unity-ios-smoke \\
        --derived-data WORK/DerivedData-legacy --logs WORK/logs-legacy

The native plugin must be a simulator build (`XLUA_IOS_SDK=simulator
build/make_ios_lua55.sh`, the `simulator` plugin variant); see
Test/MobileSmoke/README.md, "iOS Simulator".
"""
import argparse
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time

BUNDLE = "org.xluang.smoke"
RESULT = re.compile(r"XLUA_NG_(CORE_)?SMOKE_(PASS|FAIL)")


def simctl(*args, check=True):
    r = subprocess.run(["xcrun", "simctl"] + list(args), capture_output=True, text=True)
    if check and r.returncode:
        sys.exit("simctl %s failed (%d): %s" % (" ".join(args), r.returncode, r.stderr.strip()))
    return r.stdout


def run(cmd, log, timeout):
    """Run cmd with its output in log; on timeout kill its process group and stop."""
    print("+", " ".join(str(c) for c in cmd), flush=True)
    with open(log, "wb") as out:
        proc = subprocess.Popen([str(c) for c in cmd], stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, signal.SIGKILL)
            proc.wait()
            sys.exit("timed out after %ds: %s (see %s)" % (timeout, cmd[0], log))


def build(a):
    a.derived_data.mkdir(parents=True, exist_ok=True)
    cmd = ["xcodebuild", "-project", a.xcode_project / "Unity-iPhone.xcodeproj", "-scheme", "Unity-iPhone",
           "-configuration", a.configuration, "-sdk", "iphonesimulator", "-destination", "generic/platform=iOS Simulator",
           "-derivedDataPath", a.derived_data, "CODE_SIGNING_ALLOWED=NO"] + \
          (["LD_GENERATE_MAP_FILE=YES"] if a.link_map else []) + ["build"]
    log = a.logs / "xcodebuild.log"
    if run(cmd, log, a.build_timeout) != 0:
        tail = log.read_text(errors="replace").splitlines()[-30:]
        sys.exit("xcodebuild failed; see %s\n%s" % (log, "\n".join(tail)))
    for found in a.derived_data.glob("Build/Intermediates.noindex/**/UnityFramework-LinkMap-*-arm64.txt"):
        (a.logs / "UnityFramework-LinkMap-arm64.txt").write_bytes(found.read_bytes())
    apps = list((a.derived_data / ("Build/Products/%s-iphonesimulator" % a.configuration)).glob("*.app"))
    if len(apps) != 1:
        sys.exit("expected one .app under " + str(a.derived_data))
    return apps[0]


def device(a):
    """(udid, booted here) of the simulator to use."""
    devices = json.loads(simctl("list", "devices", "-j"))["devices"]
    booted = [d for ds in devices.values() for d in ds if d["state"] == "Booted"]
    if booted and not a.device:
        return booted[0]["udid"], False
    runtimes = sorted((r for r in json.loads(simctl("list", "runtimes", "-j"))["runtimes"]
                       if r["platform"] == "iOS" and r["isAvailable"]), key=lambda r: [int(x) for x in r["version"].split(".")])
    for runtime in reversed(runtimes):
        candidates = [d for d in devices.get(runtime["identifier"], []) if d["isAvailable"]
                      and (d["name"] == a.device or d["udid"] == a.device or not a.device and d["name"].startswith("iPhone"))]
        if candidates:
            udid = candidates[-1]["udid"]
            if candidates[-1]["state"] == "Booted":
                return udid, False
            simctl("boot", udid)
            simctl("bootstatus", udid, "-b")
            return udid, True
    sys.exit("no available iOS simulator device%s; create one with xcrun simctl create" % (" named " + a.device if a.device else ""))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--xcode-project", required=True, type=Path, help="the exported unity-ios-smoke folder")
    p.add_argument("--derived-data", required=True, type=Path, help="xcodebuild -derivedDataPath (on an external disk)")
    p.add_argument("--logs", required=True, type=Path, help="folder for xcodebuild.log, the app's stdout/stderr and the result")
    p.add_argument("--configuration", default="Release", choices=["Debug", "Release"])
    p.add_argument("--device", help="simulator name or UDID (default: a booted one, else the newest iPhone)")
    p.add_argument("--link-map", action="store_true",
                   help="write UnityFramework's linker map (LD_GENERATE_MAP_FILE) into --logs")
    p.add_argument("--build-timeout", type=int, default=3600)
    p.add_argument("--timeout", type=int, default=180, help="seconds to wait for the result after the launch")
    p.add_argument("--keep", action="store_true", help="leave the app installed and the simulator booted")
    a = p.parse_args()
    a.logs.mkdir(parents=True, exist_ok=True)
    app = build(a)
    udid, booted_here = device(a)
    line = None
    console = {}
    try:
        simctl("uninstall", udid, BUNDLE, check=False)
        simctl("install", udid, str(app))
        data = Path(simctl("get_app_container", udid, BUNDLE, "data").strip())
        result = data / "Documents/xlua-smoke-result.txt"
        result.unlink(missing_ok=True)
        # The app's own tmp folder: launchd_sim fails the spawn with EPERM ("Launchd job spawn
        # failed") when --stdout names a file on an external volume.
        console = {name: data / "tmp" / (name + ".log") for name in ("stdout", "stderr")}
        print("+ simctl launch", udid, BUNDLE, flush=True)
        simctl("launch", "--terminate-running-process", "--stdout=" + str(console["stdout"]),
               "--stderr=" + str(console["stderr"]), udid, BUNDLE)
        deadline = time.time() + a.timeout
        while time.time() < deadline and not (result.is_file() and RESULT.search(result.read_text(errors="replace"))):
            time.sleep(2)
        if result.is_file():
            line = result.read_text(errors="replace").strip()
            (a.logs / "xlua-smoke-result.txt").write_text(line + "\n")
        simctl("terminate", udid, BUNDLE, check=False)
        for path in console.values():
            if path.is_file():
                (a.logs / path.name).write_bytes(path.read_bytes())
        if not a.keep:
            simctl("uninstall", udid, BUNDLE, check=False)
    finally:
        if booted_here and not a.keep:
            simctl("shutdown", udid, check=False)
    if line is None:
        sys.exit("no xlua-smoke-result.txt after %ds; see %s" % (a.timeout, a.logs))
    print(line)
    return 0 if "_SMOKE_PASS" in line.split(":")[0] else 1


if __name__ == "__main__":
    sys.exit(main())
