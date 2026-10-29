"""Run the general Lua suite (Test/UnitTest, the luaunit cases that
Test/Native/run_managed.py runs on the host under Mono, EXPECTED_TESTS of them)
inside an Android IL2CPP player.

The project: Test/MobileSmoke/prepare.py --source-gen --no-smoke gives xLua in
its asmdefs, the Editor code (the plugin selector and the generator's link.xml
processor), the source generator, the plugin and the minimal preset's plugin
variant. On top of it come the suite's C# fixtures (Test/UnitTest/xLuaTest, in
Assembly-CSharp, which the generator binds), their configuration in the
attribute form (Test/Native/XLuaUnitTest.XLuaConfig.cs, which run_managed.py
checks against the migration tool), the Lua files in Assets/StreamingAssets,
and Il2cppSuite/XluaSuiteRunner.cs, which runs `require 'main'` as
Test/UnitTest/xLuaTest/Main.cs does and writes the log and the luaunit summary
to the app's files directory. Stripping stays on (Minimal); what the suite's C#
fixtures need is kept by the generated bindings and the generator's link.xml.

Configurations (CONFIGS): plain (the legacy preset with the generator), gcm
(GEN_CODE_MINIMIZE) and strict (XLUA_PRESET_MINIMAL without reflection, with
the minimal plugin variant; the cases in needs_reflection.lua must fail with
the error that says so, as on the host).

Steps, each a subcommand:

  prepare    the project (once)
  configure  XluaSuiteConfig.txt (defines, arch, stripping) and the
             XLUA_EXPECT_* variables of the configuration's native library
             (Resources/xlua_suite_env.txt, as run_managed.py sets them)
  build      configure, then two editor launches: XluaSuiteBuild.ApplyDefines
             and XluaSuiteBuild.BuildAndroid (-xluaSuiteApk)
  run        install the APK on an emulator (booted from --avd if no emulator
             of that AVD runs; stopped again afterwards), start it, wait for
             xlua-suite-result.txt, pull the log and check both luaunit modes
             ran and passed EXPECTED_TESTS (read from run_managed.py)
  all        prepare, build every --configs entry, then run them

Every adb command names the emulator's serial (-s), so nothing reaches another
device. --unity-lock and --emulator-lock take a lock file (flock, the lock of
/usr/bin/lockf) around each editor launch and around the whole emulator use.
--host-log CONFIG=LOG compares the per-case results with a run_managed.py log of
that configuration (plain: managed-suite-source-gen.log, gcm:
managed-suite-source-gen-minimize.log, strict: managed-suite-source-gen.log of a
run with --define XLUA_PRESET_MINIMAL).

Example (macOS, the xlua16k AVD):

  il2cpp_suite.py all --project P --out O --unity .../Unity.app/Contents/MacOS/Unity \\
      --android-plugins BUILD/plugin_lua55/Plugins/Android --avd xlua16k

unity-matrix.yml runs prepare and configure here, the two launches as GameCI
steps, and run on a KVM emulator in a job of its own.
"""
import argparse
import contextlib
import fcntl
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time

here = Path(__file__).resolve().parent
repo = here.parents[1]
sys.path.insert(0, str(repo / "Tools"))
import presets  # noqa: E402

PACKAGE = "org.xluang.suite"
ACTIVITY = PACKAGE + "/com.unity3d.player.UnityPlayerGameActivity"
RESULT = "xlua-suite-result.txt"
LOG = "xlua-suite.log"
# name: (Player Settings defines, the native preset whose plugin it loads)
CONFIGS = {
    "plain": ("", "legacy"),
    "gcm": ("GEN_CODE_MINIMIZE", "legacy"),
    "strict": ("XLUA_PRESET_MINIMAL", "minimal"),
}
ARCHS = {"arm64": ("ARM64", "arm64-v8a"), "x86_64": ("X86_64", "x86_64")}
SUMMARY = re.compile(r"\[luaunit (normal|coroutine)\] Ran (\d+) tests in \S+ seconds, (\d+) success\w*, (\d+) failure\w*"
                     r"(?:, (\d+) error\w*)?")
CASE = re.compile(r"^(?:LUA: )?    (\w+\.\w+) \.\.\. (\w+)$")
NEEDS = re.compile(r"^(?:LUA: )?    (\w+\.\w+) needs reflection: (.*)$")


def expected_tests():
    text = (repo / "Test/Native/run_managed.py").read_text()
    return int(re.search(r"^EXPECTED_TESTS = (\d+)", text, re.M).group(1))


@contextlib.contextmanager
def locked(path, what):
    """Hold an exclusive flock on path (what /usr/bin/lockf takes) for the block."""
    if path is None:
        yield
        return
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "a") as f:
        start = time.time()
        fcntl.flock(f, fcntl.LOCK_EX)
        if time.time() - start > 1:
            print("waited %.0f s for %s (%s)" % (time.time() - start, path, what), flush=True)
        try:
            yield
        finally:
            fcntl.flock(f, fcntl.LOCK_UN)


def run_group(cmd, timeout, log=None, **kwargs):
    """Run cmd in its own process group; on a timeout kill the whole group (no editor or bee child survives)."""
    print("+", " ".join(str(c) for c in cmd), flush=True)
    out = open(log, "w") if log else None
    proc = subprocess.Popen([str(c) for c in cmd], start_new_session=True, stdout=out,
                            stderr=subprocess.STDOUT if out else None, **kwargs)
    try:
        return proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_IL2CPP_SUITE_FAIL: timed out after %d s and killed with its process group: %s"
                         % (timeout, " ".join(str(c) for c in cmd[:2])))
    except BaseException:
        os.killpg(proc.pid, signal.SIGKILL)
        raise
    finally:
        if out:
            out.close()


# ---------------------------------------------------------------- prepare

# Fixtures that only the host builds compile (#if XLUA_GENERAL): mcs needs the
# metadata of `in` and `ref readonly` spelled out as attributes, which Roslyn
# rejects (roslyn_fixtures.py), and the pointer return needs unsafe code, which
# this project allows. The project compiles them with the keywords instead:
# the rewrites of roslyn_fixtures.py from Unity 6000.6 on (C# 12); before it
# only `in` for [In, IsReadOnly] ref, since Roslyn 4.3 has no `ref readonly`
# but compiles the RequiresLocationAttribute that the fixture declares as an
# ordinary attribute, which xLua and the generator read as `ref readonly`. And
# the guard of each block opened.
UNITY_GUARDS = {
    "Test/UnitTest/xLuaTest/LuaCallCS/InParamsForLuaCallCS.cs": "#if XLUA_GENERAL\n// Real `in` parameters.",
    "Test/UnitTest/xLuaTest/LuaCallCS/UpstreamForLuaCallCS.cs": "#if XLUA_GENERAL\n    // Tencent/xLua#1167:",
}


def csharp12(unity_version):
    major, minor = (int(x) for x in re.match(r"(\d+)\.(\d+)", unity_version).groups())
    return (major, minor) >= (6000, 6)


def unity_fixtures(project, unity_version):
    sys.path.insert(0, str(repo / "Test/Native"))
    from roslyn_fixtures import ROSLYN_FIXTURES
    c12 = csharp12(unity_version)
    for rel, guard in UNITY_GUARDS.items():
        dest = project / "Assets/xLuaTest" / Path(rel).relative_to("Test/UnitTest/xLuaTest")
        text = dest.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
        for old, new, count in ROSLYN_FIXTURES.get(rel, []):
            # Before C# 12 only `in` for [In, IsReadOnly] ref; the callbacks keep their by-value
            # parameters, as under mcs.
            if not c12 and "IsReadOnly" not in old:
                continue
            if text.count(old) != count:
                raise SystemExit("%s: expected %d of %r, found %d" % (rel, count, old, text.count(old)))
            text = text.replace(old, new)
        if text.count(guard) != 1:
            raise SystemExit("%s: expected the guard %r once" % (rel, guard))
        text = text.replace(guard, guard.replace("#if XLUA_GENERAL", "#if true // il2cpp_suite.py"))
        dest.write_text(text, encoding="utf-8")
    if c12:
        with open(project / "Assets/csc.rsp", "a") as rsp:
            rsp.write("-langversion:12\n")


def prepare(a):
    plugins = a.android_plugins.resolve()
    default = plugins / "libs/arm64-v8a/libxlua.so"
    minimal = plugins / "xlua-variants/minimal/libs/arm64-v8a/libxlua.so"
    if not default.is_file():
        raise SystemExit("--android-plugins needs libs/arm64-v8a/libxlua.so (build/make_android_lua55.sh)")
    cmd = [sys.executable, str(repo / "Test/MobileSmoke/prepare.py"), "--output", str(a.project),
           "--unity-version", a.unity_version, "--android-plugin", str(default),
           "--source-gen", str(a.source_gen.resolve()), "--no-smoke"]
    if minimal.is_file():
        cmd += ["--android-minimal-plugin", str(minimal)]
    else:
        print("no minimal plugin variant in %s: the strict configuration will not build" % plugins)
    subprocess.run(cmd, check=True)
    project = a.project.resolve()
    # The other ABIs (x86_64 for an emulator on an x86_64 host), as prepare.py places arm64-v8a.
    for lib in sorted(plugins.glob("libs/*/libxlua.so")):
        abi = lib.parent.name
        if abi == "arm64-v8a":
            continue
        dest = project / "Assets/Plugins/Android/libs" / abi / "libxlua.so"
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(lib, dest)
        shutil.copy2(repo / "Assets/Plugins/Android/libs" / abi / "libxlua.so.meta", str(dest) + ".meta")
        variant = plugins / "xlua-variants/minimal/libs" / abi / "libxlua.so"
        if variant.is_file():
            # Without a .meta: XluaSuiteBuild runs Copy Import Settings, as SmokeBuild does.
            dest = project / "Assets/Plugins/Android/xlua-variants/minimal/libs" / abi / "libxlua.so"
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(variant, dest)
    # The suite's C# fixtures (Main.cs and TestCSCallLua.cs included; the scenes stay out).
    shutil.copytree(repo / "Test/UnitTest/xLuaTest", project / "Assets/xLuaTest",
                    ignore=shutil.ignore_patterns("*.unity", "*.unity.meta"))
    shutil.copy2(repo / "Test/Native/XLuaUnitTest.XLuaConfig.cs", project / "Assets/xLuaTest/XLuaUnitTest.XLuaConfig.cs")
    unity_fixtures(project, a.unity_version)
    if csharp12(a.unity_version):
        # As prepare.py --webgl and size_report.py do for WebGL: Unity 6000.6.4f1 strips
        # ContentLoadingSystem.ProcessResults from this project, and the Android player crashed
        # (SIGSEGV in libil2cpp, called from libunity) before its scene started.
        (project / "Assets/link.xml").write_text(
            '<linker>\n  <assembly fullname="UnityEngine.ContentLoadModule" preserve="all"/>\n</linker>\n')
    streaming = project / "Assets/StreamingAssets"
    shutil.copytree(repo / "Test/UnitTest/StreamingAssets", streaming)
    suite = project / "Assets/XluaSuite"
    (suite / "Resources").mkdir(parents=True)
    shutil.copy2(here / "Il2cppSuite/XluaSuiteRunner.cs", suite / "XluaSuiteRunner.cs")
    shutil.copy2(here / "Il2cppSuite/XluaSuitePreserve.cs", suite / "XluaSuitePreserve.cs")
    shutil.copy2(here / "Il2cppSuite/XluaSuiteBuild.cs", project / "Assets/Editor/XluaSuiteBuild.cs")
    files = sorted(f.relative_to(streaming).as_posix() for f in streaming.rglob("*") if f.is_file() and f.suffix != ".meta")
    (suite / "Resources/xlua_suite_files.txt").write_text("\n".join(files) + "\n")
    print("XLUA_IL2CPP_SUITE_PREPARED: %s (%d Lua files)" % (project, len(files)))


def configure(a, config):
    defines, preset = CONFIGS[config]
    project = a.project.resolve()
    (project / "XluaSuiteConfig.txt").write_text("defines=%s\narch=%s\nstripping=%s\n"
                                                 % (defines, ARCHS[a.arch][0], a.stripping))
    # As run_managed.py: the XLUA_EXPECT_* variables only for a preset define.
    env = presets.expect_env(preset) if preset != "legacy" else {}
    # IL2CPP's Environment.GetEnvironmentVariable gives null for a variable set to "" (Mono gives
    # ""), which the cases read as unset: an empty list is written as a lone separator.
    env = {k: v if v != "" else "," for k, v in env.items()}
    (project / "Assets/XluaSuite/Resources/xlua_suite_env.txt").write_text(
        "".join("%s=%s\n" % kv for kv in sorted(env.items())))
    print("configured %s: defines=%r arch=%s stripping=%s env=%r" % (config, defines, a.arch, a.stripping, env))


def build(a, config):
    configure(a, config)
    out = a.out.resolve()
    (out / "logs").mkdir(parents=True, exist_ok=True)
    apk = out / ("xlua-suite-%s-%s.apk" % (config, a.arch))
    if apk.exists():
        apk.unlink()
    base = [a.unity, "-batchmode", "-nographics", "-quit", "-projectPath", a.project.resolve(), "-buildTarget", "Android"]
    for step, extra in [("defines", ["-executeMethod", "XluaSuiteBuild.ApplyDefines"]),
                        ("build", ["-executeMethod", "XluaSuiteBuild.BuildAndroid", "-xluaSuiteApk", apk])]:
        log = out / "logs" / ("%s-%s.log" % (config, step))
        start = time.time()
        with locked(a.unity_lock, "Unity"):
            code = run_group(base + extra + ["-logFile", log], a.build_timeout)
        print("  %s %s: exit %d in %.0f s" % (config, step, code, time.time() - start), flush=True)
        text = log.read_text(errors="replace") if log.exists() else ""
        marker = "XLUA_SUITE_DEFINES:" if step == "defines" else "XLUA_SUITE_BUILD_PASS:"
        if code or marker not in text:
            for line in text.splitlines():
                if re.search(r"error CS\d+|error XLSG|Exception|BuildFailed|Error building", line):
                    print("  " + line)
            raise SystemExit("XLUA_IL2CPP_SUITE_FAIL: %s %s failed; see %s" % (config, step, log))
        if step == "build":
            for line in text.splitlines():
                if "XLUA_SUITE_BUILD_SETTINGS" in line or "XLua plugin variants" in line or "warning XLSG" in line:
                    print("  " + line.strip())
    print("XLUA_IL2CPP_SUITE_APK: %s %s %d bytes" % (config, apk, apk.stat().st_size), flush=True)
    return apk


# ---------------------------------------------------------------- emulator


class Emulator:
    def __init__(self, a):
        self.a = a
        sdk = Path(a.sdk) if a.sdk else None
        self.adb = a.adb or (str(sdk / "platform-tools/adb") if sdk else shutil.which("adb"))
        self.emulator = str(sdk / "emulator/emulator") if sdk else shutil.which("emulator")
        self.serial = a.serial
        self.proc = None

    def cmd(self, *args, timeout=120, check=True, capture=True):
        if not self.serial:
            raise SystemExit("no emulator serial")
        result = subprocess.run([self.adb, "-s", self.serial] + [str(x) for x in args], timeout=timeout,
                                stdout=subprocess.PIPE if capture else None, stderr=subprocess.STDOUT, text=True,
                                errors="replace")
        if check and result.returncode:
            raise SystemExit("adb -s %s %s failed: %s" % (self.serial, " ".join(str(x) for x in args), result.stdout))
        return result.stdout or ""

    def devices(self):
        out = subprocess.run([self.adb, "devices"], stdout=subprocess.PIPE, text=True, timeout=60).stdout
        return [l.split()[0] for l in out.splitlines()[1:] if l.strip() and l.split()[0].startswith("emulator-")]

    def find(self):
        """The serial of a running emulator of --avd, or None."""
        for serial in self.devices():
            out = subprocess.run([self.adb, "-s", serial, "emu", "avd", "name"], stdout=subprocess.PIPE,
                                 stderr=subprocess.DEVNULL, text=True, timeout=60).stdout
            if out.splitlines() and out.splitlines()[0].strip() == self.a.avd:
                return serial
        return None

    def start(self):
        if self.serial:
            if not self.serial.startswith("emulator-"):
                raise SystemExit("--serial must name an emulator (emulator-NNNN), never a phone")
            print("using the emulator %s" % self.serial, flush=True)
        elif self.a.avd and self.find():
            self.serial = self.find()
            print("using the running emulator %s (%s)" % (self.serial, self.a.avd), flush=True)
        else:
            if not self.a.avd or not self.emulator:
                raise SystemExit("pass --serial of a running emulator, or --avd and --sdk to boot one")
            used = set(self.devices())
            port = next(p for p in range(5554, 5684, 2) if "emulator-%d" % p not in used)
            self.serial = "emulator-%d" % port
            env = dict(os.environ)
            if self.a.avd_home:
                env["ANDROID_AVD_HOME"] = str(self.a.avd_home)
            log = open(self.a.out.resolve() / "logs/emulator.log", "w")
            cmd = [self.emulator, "-avd", self.a.avd, "-port", str(port), "-no-window", "-no-audio", "-no-boot-anim",
                   "-no-snapshot"]
            print("+", " ".join(cmd), flush=True)
            self.proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, start_new_session=True, env=env)
        deadline = time.time() + self.a.boot_timeout
        while time.time() < deadline:
            if self.proc and self.proc.poll() is not None:
                raise SystemExit("the emulator exited with %d; see %s" % (self.proc.returncode,
                                                                         self.a.out.resolve() / "logs/emulator.log"))
            r = subprocess.run([self.adb, "-s", self.serial, "shell", "getprop", "sys.boot_completed"],
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=60)
            if r.stdout.strip() == "1":
                break
            time.sleep(5)
        else:
            raise SystemExit("the emulator %s did not boot in %d s" % (self.serial, self.a.boot_timeout))
        info = [self.cmd("shell", "getprop", p).strip() for p in
                ["ro.product.cpu.abi", "ro.build.version.release", "ro.build.version.sdk"]]
        page = self.cmd("shell", "getconf", "PAGE_SIZE").strip()
        print("emulator %s: abi %s, Android %s (API %s), page size %s" % (self.serial, info[0], info[1], info[2], page),
              flush=True)
        # Otherwise the "Viewing full screen" tip takes the focus and the player stops before
        # the scene starts (Test/MobileSmoke/README.md).
        self.cmd("shell", "settings", "put", "secure", "immersive_mode_confirmations", "confirmed")

    def stop(self):
        if self.proc is None:
            return
        subprocess.run([self.adb, "-s", self.serial, "emu", "kill"], stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, timeout=60)
        try:
            self.proc.wait(timeout=120)
        except subprocess.TimeoutExpired:
            os.killpg(self.proc.pid, signal.SIGKILL)
            self.proc.wait()
        print("stopped the emulator %s" % self.serial, flush=True)
        self.proc = None


def dismiss_dialog(emu):
    """Press the positive button of a system dialog in front of the player; 1 if there was one.

    Android 16 shows one (PageSizeMismatchDialog, "Showing PageSizeMismatchDialog" in the
    logcat) when the APK's native libraries have 4 KB aligned segments, as Unity 2021.3.45f2
    builds them, on a device with 16 KB pages such as the xlua16k AVD. The app runs in
    compatibility mode, but the dialog takes the focus and the player waits for it before
    it loads the scene."""
    emu.cmd("shell", "uiautomator", "dump", "/sdcard/xlua-suite-ui.xml", check=False, timeout=60)
    ui = emu.cmd("shell", "cat", "/sdcard/xlua-suite-ui.xml", check=False)
    for node in re.findall(r"<node [^>]*>", ui):
        if 'resource-id="android:id/button1"' in node and 'package="android"' in node:
            bounds = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', node)
            if bounds:
                x1, y1, x2, y2 = (int(v) for v in bounds.groups())
                texts = re.findall(r'text="([^"]*)"', ui)
                print("dismissing a system dialog (%s)" % "; ".join(t for t in texts if t)[:300], flush=True)
                emu.cmd("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2), check=False)
                return 1
    return 0


def run_apk(a, emu, config, apk):
    out = a.out.resolve()
    (out / "logs").mkdir(parents=True, exist_ok=True)
    data = "/sdcard/Android/data/" + PACKAGE
    files = data + "/files"
    # Replace the app in place, which keeps its uid: after an uninstall the AVD (Android 16)
    # kept the app's folder in /sdcard/Android/data, owned by the old uid, which the shell
    # cannot delete and the new install cannot write; IL2CPP then fails to extract its
    # resources and the scene never starts (Test/MobileSmoke/README.md).
    installed = emu.cmd("install", "--no-streaming", "-r", apk, timeout=600, check=False)
    if "Success" not in installed:
        print("install -r: %s; installing again after an uninstall" % installed.strip(), flush=True)
        emu.cmd("uninstall", PACKAGE, check=False)
        emu.cmd("install", "--no-streaming", "-r", apk, timeout=600)
    # pm clear empties the app's data, external files included, as the system.
    emu.cmd("shell", "pm", "clear", PACKAGE, check=False)
    left = emu.cmd("shell", "ls", "-A", data, check=False).strip()
    if left and "No such file" not in left:
        emu.cmd("shell", "rm", "-rf", data, check=False)
        left = emu.cmd("shell", "ls", "-A", data, check=False).strip()
        if left and "No such file" not in left:
            raise SystemExit("XLUA_IL2CPP_SUITE_FAIL: %s holds files the app cannot replace (%s); wipe the "
                             "AVD's data (emulator -wipe-data) or remove them as root" % (data, left.replace("\n", " ")))
    emu.cmd("logcat", "-c", check=False)
    logcat_path = out / "logs" / ("%s.logcat" % config)
    logcat_file = open(logcat_path, "w")
    logcat = subprocess.Popen([emu.adb, "-s", emu.serial, "logcat", "-v", "threadtime"], stdout=logcat_file,
                              stderr=subprocess.STDOUT, start_new_session=True)
    started = time.time()
    result = ""
    status = "timeout"
    try:
        # The launcher activity: UnityPlayerGameActivity in Unity 6, UnityPlayerActivity before.
        resolved = emu.cmd("shell", "cmd", "package", "resolve-activity", "--brief", PACKAGE, check=False).split()
        activity = resolved[-1] if resolved and resolved[-1].startswith(PACKAGE + "/") else ACTIVITY
        emu.cmd("shell", "am", "start", "-n", activity)
        seen = False
        dialogs = 0
        while time.time() - started < a.run_timeout:
            time.sleep(5)
            if dialogs < 3 and "No such file" in emu.cmd("shell", "ls", files + "/" + LOG, check=False):
                dialogs += dismiss_dialog(emu)
            result = emu.cmd("shell", "cat", files + "/" + RESULT, check=False)
            if "XLUA_SUITE_DONE" in result:
                status = "done"
                break
            alive = emu.cmd("shell", "pidof", PACKAGE, check=False).strip()
            seen = seen or bool(alive)
            if seen and not alive:
                status = "crashed"
                break
            # The runner creates its log when the scene starts.
            if time.time() - started > 120 and "No such file" in emu.cmd("shell", "ls", files + "/" + LOG,
                                                                           check=False):
                status = "never started"
                break
        elapsed = time.time() - started
        log_path = out / "logs" / ("%s-suite.log" % config)
        emu.cmd("pull", files + "/" + LOG, log_path, check=False, timeout=300)
    finally:
        emu.cmd("shell", "am", "force-stop", PACKAGE, check=False)
        time.sleep(2)
        logcat.kill()
        logcat.wait()
        logcat_file.close()
    text = log_path.read_text(errors="replace") if log_path.exists() else ""
    if status != "done":
        # A hang is a finding: name the last case that finished and the lines after it.
        cases = [l for l in text.splitlines() if CASE.match(l)]
        print("XLUA_IL2CPP_SUITE_%s after %.0f s: %s; last finished case: %s"
              % (status.upper().replace(" ", "_"), elapsed, config, cases[-1] if cases else "none"))
        print("\n".join(text.splitlines()[-15:]))
        crash = [l for l in logcat_path.read_text(errors="replace").splitlines()
                 if re.search(r"\bF DEBUG\b|SIGSEGV|SIGABRT|SIGTRAP|FATAL EXCEPTION|CRASH", l)]
        if crash:
            print("\n".join(crash[:40]))
    return status, elapsed, text, result


def parse(text):
    """{mode: {case: status}}, {mode: (ran, ok, failures, errors)}, {mode: {case: needs-reflection message}}."""
    cases, needs, summary = {}, {}, {}
    block, block_needs = {}, {}
    for line in text.splitlines():
        m = CASE.match(line)
        if m:
            block[m.group(1)] = m.group(2)
            continue
        m = NEEDS.match(line)
        if m:
            block_needs[m.group(1)] = m.group(2)
            continue
        m = SUMMARY.search(line)
        if m:
            mode = m.group(1)
            summary[mode] = tuple(int(x or 0) for x in m.group(2, 3, 4, 5))
            cases[mode], needs[mode] = block, block_needs
            block, block_needs = {}, {}
    return cases, summary, needs


def compare(host_text, player_text):
    host, _, host_needs = parse(host_text)
    player, _, player_needs = parse(player_text)
    lines = []
    for mode in ("coroutine", "normal"):
        h, p = host.get(mode, {}), player.get(mode, {})
        for case in sorted(set(h) | set(p)):
            if h.get(case) != p.get(case):
                lines.append("%s %s: host %s, player %s" % (mode, case, h.get(case, "absent"), p.get(case, "absent")))
        hn, pn = host_needs.get(mode, {}), player_needs.get(mode, {})
        for case in sorted(set(hn) | set(pn)):
            if hn.get(case) != pn.get(case):
                lines.append("%s %s needs reflection: host %r, player %r" % (mode, case, hn.get(case), pn.get(case)))
    return lines


def report(a, config, status, elapsed, text, host_log=None):
    expected = expected_tests()
    cases, summary, needs = parse(text)
    ok = status == "done" and "XLUA_SUITE_DONE ok" in text
    for mode in ("coroutine", "normal"):
        ran, passed, failures, errors = summary.get(mode, (0, 0, 0, 0))
        print("[%s] %s: %d/%d passed (%d failures, %d errors; expected %d)%s"
              % (config, mode, passed, ran, failures, errors, expected,
                 ", %d needed reflection" % len(needs.get(mode, {})) if needs.get(mode) else ""))
        ok = ok and ran == expected and passed == expected
        for case, result in sorted(cases.get(mode, {}).items()):
            if result != "Ok":
                print("    %s %s" % (result, case))
    if host_log:
        diff = compare(Path(host_log).read_text(errors="replace"), text)
        print("[%s] differences from %s: %d" % (config, host_log, len(diff)))
        for line in diff:
            print("    " + line)
    print("XLUA_IL2CPP_SUITE_%s: %s (%s, %.0f s)" % ("PASS" if ok else "FAIL", config, status, elapsed), flush=True)
    return ok


def host_logs(a):
    """--host-log CONFIG=PATH entries; a bare PATH belongs to --config."""
    logs = {}
    for entry in a.host_log or []:
        config, sep, path = entry.partition("=")
        if not sep:
            config, path = getattr(a, "config", None), entry
        logs[config] = Path(path)
    return logs


def run_configs(a, apks):
    emu = Emulator(a)
    results = {}
    with locked(a.emulator_lock, "emulator"):
        try:
            emu.start()
            for config, apk in apks:
                status, elapsed, text, _ = run_apk(a, emu, config, apk)
                results[config] = report(a, config, status, elapsed, text, host_logs(a).get(config))
        finally:
            if not a.keep_emulator:
                emu.stop()
    return all(results.values())


# ---------------------------------------------------------------- command line


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="step", required=True)

    def common(s, project=True):
        if project:
            s.add_argument("--project", type=Path, required=True, help="the Unity project (prepare makes it)")
        s.add_argument("--out", type=Path, default=Path("il2cpp-suite-out"), help="APKs and logs")

    def project_args(s):
        s.add_argument("--android-plugins", type=Path, required=True,
                       help="the Plugins/Android folder of build/make_android_lua55.sh (plugin_lua55/Plugins/Android): "
                            "libs/<abi>/libxlua.so and, for strict, xlua-variants/minimal/libs/<abi>/libxlua.so")
        s.add_argument("--unity-version", default="6000.3.21f1")
        s.add_argument("--source-gen", type=Path, default=None,
                       help="where Tools/build_source_generator.py builds the generator (default: <out>/source-generator)")

    def config_args(s):
        s.add_argument("--arch", choices=sorted(ARCHS), default="arm64",
                       help="arm64 (devices, an arm64 host's emulator) or x86_64 (an emulator on an x86_64 host)")
        s.add_argument("--stripping", default="Minimal", help="managed stripping level (default Minimal)")

    def build_args(s):
        s.add_argument("--unity", type=Path, required=True, help="the Unity editor executable")
        s.add_argument("--unity-lock", type=Path, help="hold this lock file around each editor launch")
        s.add_argument("--build-timeout", type=int, default=3600, help="seconds per editor launch")

    def run_args(s):
        s.add_argument("--sdk", default=os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT"),
                       help="Android SDK with emulator/ and platform-tools/ (default: $ANDROID_HOME)")
        s.add_argument("--adb", help="adb executable (default: the SDK's)")
        s.add_argument("--avd", help="boot this AVD unless an emulator of it runs (it is stopped afterwards)")
        s.add_argument("--avd-home", type=Path, help="ANDROID_AVD_HOME for the emulator")
        s.add_argument("--serial", help="use this running emulator (emulator-5554); never a phone")
        s.add_argument("--emulator-lock", type=Path, help="hold this lock file while using the emulator")
        s.add_argument("--keep-emulator", action="store_true", help="leave an emulator this script booted running")
        s.add_argument("--boot-timeout", type=int, default=600)
        s.add_argument("--run-timeout", type=int, default=1800, help="seconds the suite may run in the player")
        s.add_argument("--host-log", action="append", metavar="[CONFIG=]LOG",
                       help="compare the cases with this run_managed.py log of the configuration (repeatable)")

    s = sub.add_parser("prepare", help="make the project")
    common(s)
    project_args(s)
    s = sub.add_parser("configure", help="write the configuration of the next build into the project")
    common(s)
    s.add_argument("--config", choices=sorted(CONFIGS), required=True)
    config_args(s)
    s = sub.add_parser("build", help="configure and build one configuration's APK")
    common(s)
    s.add_argument("--config", choices=sorted(CONFIGS), required=True)
    config_args(s)
    build_args(s)
    s = sub.add_parser("run", help="run an APK on an emulator and check the summary")
    common(s, project=False)
    s.add_argument("--config", choices=sorted(CONFIGS), required=True, help="names the logs")
    s.add_argument("--apk", type=Path, required=True)
    run_args(s)
    s = sub.add_parser("check", help="check a pulled xlua-suite.log")
    s.add_argument("--config", choices=sorted(CONFIGS), required=True)
    s.add_argument("--log", type=Path, required=True)
    s.add_argument("--host-log", action="append", metavar="[CONFIG=]LOG")
    s = sub.add_parser("all", help="prepare, build every configuration, run them")
    common(s)
    project_args(s)
    config_args(s)
    build_args(s)
    run_args(s)
    s.add_argument("--configs", default="plain,gcm,strict")
    a = p.parse_args()

    if a.step == "check":
        text = a.log.read_text(errors="replace")
        sys.exit(0 if report(a, a.config, "done", 0, text, host_logs(a).get(a.config)) else 1)
    a.out = a.out.resolve()
    (a.out / "logs").mkdir(parents=True, exist_ok=True)
    if a.step in ("prepare", "all"):
        a.source_gen = a.source_gen or a.out / "source-generator"
        prepare(a)
    if a.step == "configure":
        configure(a, a.config)
    elif a.step == "build":
        build(a, a.config)
    elif a.step == "run":
        sys.exit(0 if run_configs(a, [(a.config, a.apk.resolve())]) else 1)
    elif a.step == "all":
        configs = [c.strip() for c in a.configs.split(",") if c.strip()]
        unknown = [c for c in configs if c not in CONFIGS]
        if unknown:
            p.error("unknown configurations: " + ", ".join(unknown))
        apks = [(c, build(a, c)) for c in configs]
        sys.exit(0 if run_configs(a, apks) else 1)


if __name__ == "__main__":
    main()
