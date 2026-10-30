"""Cross-library player bench: xlua-ng against PuerTS's Lua backend, zlua and MoonSharp-ng, with a subset of
zlua-benchmark's case list and the pure Lua workloads of Test/Native/flag_bench.c (VsBench/VsBench.cs;
docs/research/benchmark-vs-puerts-zlua.md, docs/research/benchmark-vs-moonsharp-ng.md).

  build   prepare and build the PuerTS, zlua and MoonSharp-ng players (Unity 6000.3, Android ARM64 IL2CPP, release,
          Minimal stripping, SmokeBuild.cs as for xlua-ng's players) from official release archives the caller
          downloaded and hash-checked, or for MoonSharp-ng the UPM package staged from the fork's source
          (tools/upm/stage-local-package.sh); the xlua-ng players come from size_report.py --configs vsbench
  device  install every player on the xlua16k emulator or the test phone and launch them in rotating order
  report  the median of the per-launch medians per case and library, and the APK sizes, as JSON and Markdown

The third-party packages go into projects under WORK, never into this repository. Their archives are data:
this script extracts them and lets Unity import them; it runs none of their scripts outside the Unity editor
(the packages' own Editor code runs there, as in any project that installs them).

Device rules (playground projects/moto-g-play-2024): every adb command names its serial and is logged with a
UTC timestamp to WORK/<target>/device-commands.log; a read-only snapshot comes first; Charge AOD is force-stopped
during the runs and restarted afterwards; no setting is changed on the phone; the test packages are uninstalled at
the end. The script stops when a Unity editor runs (Unity starts its own adb server). The adb server is left
running unless --kill-adb-server is given: other jobs' emulator and phone sessions share it.
"""
import argparse
import datetime
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import statistics
import subprocess
import sys
import tarfile
import threading
import time
import zipfile

here = Path(__file__).resolve().parent
ACTIVITY = "com.unity3d.player.UnityPlayerGameActivity"
EMU_SERIAL = "emulator-5554"
# Players: name, package, APK path below WORK.
PLAYERS = [
    ("xlua-ng plain", "org.xluang.vs.plain", "work/players/android/vsbench/ng-5.5+pkg+sg+vsbench/unity-android-smoke.apk"),
    ("xlua-ng gcm", "org.xluang.vs.gcm", "work/players/android/vsbench/ng-5.5+pkg+sg+gcm+vsbench/unity-android-smoke.apk"),
    ("xlua-ng strict-minimal", "org.xluang.vs.minimal", "work/players/android/vsbench/ng-5.5+minimal+strict+vsbench/unity-android-smoke.apk"),
    ("puerts-lua", "org.xluang.vs.puerts", "tp/players/puerts/unity-android-smoke.apk"),
    ("zlua", "org.xluang.vs.zlua", "tp/players/zlua/unity-android-smoke.apk"),
    ("moonsharp-ng sourcegen", "org.xluang.vs.moonsharp", "tp/players/moonsharp/unity-android-smoke.apk"),
]
BASELINE_APK = "work/players/android/vsbench/baseline/unity-android-smoke.apk"


def now():
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def unity_runs():
    return subprocess.run(["pgrep", "-x", "Unity"], stdout=subprocess.DEVNULL).returncode == 0


# ---------------------------------------------------------------- build

def extract(archive, dest, strip_root=True):
    """Extract a release archive (data) into dest, dropping its top-level folder; no links, no absolute paths."""
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    with tarfile.open(archive) as tar:
        members = tar.getmembers()
        for m in members:
            parts = Path(m.name).parts
            if m.issym() or m.islnk() or m.name.startswith("/") or ".." in parts:
                sys.exit("unsafe member %s in %s" % (m.name, archive))
        roots = {Path(m.name).parts[0] for m in members if Path(m.name).parts}
        for m in members:
            parts = Path(m.name).parts
            if strip_root and len(roots) == 1:
                parts = parts[1:]
            if not parts:
                continue
            target = dest.joinpath(*parts)
            if m.isdir():
                target.mkdir(parents=True, exist_ok=True)
            elif m.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                with tar.extractfile(m) as src, open(target, "wb") as out:
                    shutil.copyfileobj(src, out)


def prepare(a, lib, project):
    """An empty project with SmokeBuild.cs, the bench, the host shim and the library's packages embedded."""
    if project.exists():
        shutil.rmtree(project)
    for d in ["Assets/Editor", "Packages", "ProjectSettings"]:
        (project / d).mkdir(parents=True)
    version = re.search(r"/(\d+\.\d+\.\d+[abfp]\d+)/", str(a.unity))
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + version.group(1) + "\n")
    # The same module list as size_report.py's projects, plus what the library's package needs.
    deps = {"com.unity.modules.unitywebrequestwww": "1.0.0", "com.unity.modules.androidjni": "1.0.0"}
    if lib == "puerts":
        for name, archive in [("com.tencent.puerts.core", a.puerts_core), ("com.tencent.puerts.lua", a.puerts_lua)]:
            extract(archive, project / "Packages" / name)
    elif lib == "moonsharp":
        # The fork's own package layout (Runtime/ with the interpreter sources, link.xml and the source generator
        # in Runtime/Analyzers), embedded as a local package.
        if not a.moonsharp or not (a.moonsharp / "package.json").is_file():
            sys.exit("--moonsharp must be a staged org.moonsharp.moonsharp folder: %s" % a.moonsharp)
        shutil.copytree(a.moonsharp, project / "Packages/org.moonsharp.moonsharp")
    else:
        extract(a.zlua, project / "Packages/com.code-philosophy.zlua")
        # zlua's installer reuses LuaSrcCache/downloads/lua-X.Y.Z.tar.gz when it is there (LuaSourceCache.cs:241-243);
        # the caller's archive is hash-checked against lua.org's published SHA-256, so nothing is downloaded.
        cache = project / "Library/ZLua/LuaSrcCache/downloads"
        cache.mkdir(parents=True)
        shutil.copy2(a.lua_tarball, cache / Path(a.lua_tarball).name)
        deps["com.unity.modules.jsonserialize"] = "1.0.0"
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": deps}, indent=1) + "\n")
    shutil.copy2(here / "SmokeBuild.cs", project / "Assets/Editor/SmokeBuild.cs")
    shutil.copy2(here / "VsBench/VsBench.cs", project / "Assets/VsBench.cs")
    adapter = {"puerts": "VsBenchPuerts.cs", "zlua": "VsBenchZLua.cs", "moonsharp": "VsBenchMoonSharp.cs"}[lib]
    shutil.copy2(here / "VsBench" / adapter, project / "Assets" / adapter)
    (project / "Assets/csc.rsp").write_text("-define:VSBENCH_%s\n" % lib.upper())
    shutil.copy2(here / "VsBench/VsBenchHost.cs", project / "Assets/XluaSmoke.cs")
    editor = here / "VsBench" / ("VsBench%sEditor.cs" % {"puerts": "Puerts", "zlua": "ZLua", "moonsharp": "MoonSharp"}[lib])
    if editor.is_file():
        shutil.copy2(editor, project / "Assets/Editor" / editor.name)


def unity(a, project, method, log, env):
    cmd = [a.unity, "-batchmode", "-nographics", "-quit", "-projectPath", project, "-buildTarget", "Android",
           "-executeMethod", method, "-logFile", log]
    print(now(), "+", " ".join(str(c) for c in cmd), flush=True)
    with open(str(log) + ".stdout", "ab") as out:
        while unity_runs():
            print("  another Unity editor runs; waiting", flush=True)
            time.sleep(30)
        proc = subprocess.Popen([str(c) for c in cmd], env=env, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            rc = proc.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, 9)
            proc.wait()
            sys.exit("timed out after %ds: %s (see %s)" % (a.timeout, method, log))
    # Unity leaves its Gradle daemon running; stop the ones in our GRADLE_USER_HOME.
    android = Path(a.unity).resolve().parents[3] / "PlaybackEngines/AndroidPlayer"
    launcher = sorted(android.glob("Tools/gradle/lib/gradle-launcher-*.jar"))
    if launcher:
        subprocess.run([str(android / "OpenJDK/bin/java"), "-cp", str(launcher[-1]), "org.gradle.launcher.GradleMain", "--stop"],
                       env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if rc != 0:
        sys.exit("Unity exited with %d: %s" % (rc, log))


# Unity launches per library before SmokeBuild.BuildAndroid; see VsBench/VsBench*Editor.cs.
STEPS = {
    "puerts": ["VsBenchPuertsEditor.Generate"],
    "zlua": ["VsBenchZLuaEditor.Install", "VsBenchZLuaEditor.Generate"],
    "moonsharp": [],  # its source generator runs in the build's own compilation
}


def build(a):
    work = a.work.resolve()
    env = dict(os.environ, TMPDIR=a.tmpdir, GRADLE_USER_HOME=str(work / "tp/gradle-home"), XLUA_SMOKE_RELEASE="1",
               XLUA_SMOKE_STRIPPING="Minimal")
    for lib in a.libs.split(","):
        project = work / "tp/projects" / lib
        out = work / "tp/players" / lib
        if (out / "build-info.json").is_file():
            continue
        if not a.keep_project or not project.is_dir():
            prepare(a, lib, project)
        shutil.rmtree(out, ignore_errors=True)
        out.mkdir(parents=True)
        benv = dict(env, XLUA_SMOKE_OUTPUT_ROOT=str(out), XLUA_SMOKE_APP_ID="org.xluang.vs." + lib)
        started = time.time()
        for i, step in enumerate(STEPS[lib]):
            unity(a, project, step, out / ("step%d.log" % i), benv)
        unity(a, project, "SmokeBuild.BuildAndroid", out / "unity.log", benv)
        text = (out / "unity.log").read_text(errors="replace")
        line = re.search(r"XLUA_NG_BUILD_SETTINGS: .*", text)
        (out / "build-info.json").write_text(json.dumps({
            "library": lib, "unity_settings": line.group(0) if line else None, "seconds": round(time.time() - started),
            "archives": {k: str(v) for k, v in [("puerts_core", a.puerts_core), ("puerts_lua", a.puerts_lua),
                                                 ("zlua", a.zlua), ("lua", a.lua_tarball), ("moonsharp", a.moonsharp)]
                         if v}}, indent=1) + "\n")


# ---------------------------------------------------------------- device

def device(a):
    work = a.work.resolve()
    sdk = a.sdk
    adb_exe = str(sdk / "platform-tools/adb")
    out_dir = work / a.target
    out_dir.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, ANDROID_SDK_ROOT=str(sdk), ANDROID_HOME=str(sdk), ANDROID_USER_HOME=str(sdk / "user-home"),
               ANDROID_AVD_HOME=str(sdk / "user-home/avd"))
    logf = open(out_dir / "device-commands.log", "a")
    lock = threading.Lock()

    def note(text):
        with lock:
            print(now(), "#", text, file=logf, flush=True)
        print(now(), text, flush=True)

    def run(args, timeout=180):
        with lock:
            print(now(), " ".join(str(x) for x in args), file=logf, flush=True)
        try:
            r = subprocess.run([str(x) for x in args], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired:
            with lock:
                print(now(), "  timed out after %ds" % timeout, file=logf, flush=True)
            return ""
        return r.stdout

    if unity_runs():
        sys.exit("a Unity editor runs; no device work meanwhile")
    players = [(n, pkg, work / rel) for n, pkg, rel in PLAYERS if not a.only or re.search(a.only, n)]
    for n, pkg, apk in players:
        if not apk.is_file():
            sys.exit("missing APK " + str(apk))
    emu_proc = None
    run([adb_exe, "version"])
    devices = run([adb_exe, "devices", "-l"])
    if a.target == "emu":
        serial = EMU_SERIAL
        if EMU_SERIAL not in devices:
            note("booting xlua16k")
            emu_proc = subprocess.Popen([str(sdk / "emulator/emulator"), "-avd", "xlua16k", "-no-window", "-no-audio",
                                         "-no-boot-anim", "-no-snapshot", "-port", "5554"], env=env, start_new_session=True,
                                        stdout=open(out_dir / "emulator.log", "w"), stderr=subprocess.STDOUT)
    else:
        time.sleep(5)
        devices = run([adb_exe, "devices", "-l"])
        serial = a.serial
        for line in devices.splitlines()[1:]:
            parts = line.split()
            if not a.serial and len(parts) >= 2 and parts[1] == "device" and not parts[0].startswith("emulator"):
                serial = parts[0]
        if not serial or not any(l.split()[:2] == [serial, "device"] for l in devices.splitlines()[1:]):
            sys.exit("no phone %s in adb devices:\n%s" % (serial or "", devices))

    def adb(*args, timeout=180):
        return run([adb_exe, "-s", serial] + list(args), timeout=timeout)

    def thermal():
        text = adb("shell", "dumpsys", "thermalservice")
        status = next((l.strip() for l in text.splitlines() if "Thermal Status" in l), "?")
        temps = re.findall(r"mValue=([\d.]+), mName=(\w+), mType=(\d+)", text)
        cpu = [float(v) for v, n, t in temps if t == "0"]  # TYPE_CPU
        return status, (max(cpu) if cpu else None)

    stop = threading.Event()
    summary = {"target": a.target, "serial": serial, "rounds": a.rounds, "players": [n for n, _, _ in players], "launches": []}
    try:
        if a.target == "emu":
            for _ in range(120):
                time.sleep(5)
                if emu_proc and emu_proc.poll() is not None:
                    sys.exit("the emulator exited; see " + str(out_dir / "emulator.log"))
                if adb("shell", "getprop", "sys.boot_completed").strip() == "1":
                    break
            else:
                sys.exit("the emulator did not boot")
            time.sleep(20)
            avd = (adb("emu", "avd", "name").splitlines() or [""])[0].strip()
            if avd != "xlua16k":
                sys.exit("unexpected AVD on %s: %r" % (serial, avd))
            imm = adb("shell", "settings", "get", "secure", "immersive_mode_confirmations").strip()
            note("emulator %s model %s PAGE_SIZE %s cpus %s immersive_mode_confirmations=%s" % (
                avd, adb("shell", "getprop", "ro.product.model").strip(), adb("shell", "getconf", "PAGE_SIZE").strip(),
                adb("shell", "nproc").strip(), imm))
            if imm != "confirmed":
                # Emulator only (Test/MobileSmoke/README.md): otherwise the full-screen tip takes focus and the player stops.
                adb("shell", "settings", "put", "secure", "immersive_mode_confirmations", "confirmed")
        else:
            model = adb("shell", "getprop", "ro.product.model").strip()
            if "moto g play" not in model:
                sys.exit("unexpected device on %s: %r" % (serial, model))
            snap = out_dir / "snapshot"
            snap.mkdir(exist_ok=True)
            for ns in ["system", "secure", "global"]:
                (snap / ("settings-%s.txt" % ns)).write_text(adb("shell", "settings", "list", ns))
            (snap / "packages.txt").write_text(adb("shell", "pm", "list", "packages", "-f", "-i", "-U"))
            (snap / "packages-disabled.txt").write_text(adb("shell", "pm", "list", "packages", "-d"))
            (snap / "getprop.txt").write_text(adb("shell", "getprop"))
            note("phone %s snapshot taken; PAGE_SIZE %s" % (model, adb("shell", "getconf", "PAGE_SIZE").strip()))
            adb("shell", "am", "force-stop", "com.emindeniz99.chargeaod")
            adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
            adb("shell", "wm", "dismiss-keyguard")
            adb("shell", "input", "keyevent", "KEYCODE_HOME")

            def keep_awake():
                while not stop.wait(20):
                    adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
            threading.Thread(target=keep_awake, daemon=True).start()
        note("before: " + (adb("shell", "pm", "list", "packages", "org.xluang").strip().replace("\n", " ") or "no org.xluang packages"))
        for n, pkg, apk in players:
            adb("uninstall", pkg)
            adb("shell", "rm", "-rf", "/sdcard/Android/data/" + pkg)
            res = adb("install", "--no-streaming", "-r", apk, timeout=900)
            if "Success" not in res:
                raise SystemExit("install of %s failed: %s" % (n, res[-500:]))
        note("installed: " + adb("shell", "pm", "list", "packages", "org.xluang.vs").strip().replace("\n", " "))
        time.sleep(10)
        for r in range(a.rounds):
            k = r % len(players)
            for n, pkg, apk in players[k:] + players[:k]:
                if unity_runs():
                    raise SystemExit("a Unity editor started; stopping")
                status, cpu = thermal()
                adb("logcat", "-c")
                if a.target == "phone":
                    adb("shell", "input", "keyevent", "KEYCODE_WAKEUP")
                    adb("shell", "wm", "dismiss-keyguard")
                started = time.time()
                adb("shell", "am", "start", "-W", "-n", pkg + "/" + ACTIVITY)
                if a.target == "emu":
                    # Android 16's "isn't 16 KB compatible" dialog (a library with 4 KB PT_LOAD alignment) takes the
                    # focus and the player waits behind it; OK is at (891, 1509) on xlua16k's 1080x2400 screen.
                    time.sleep(4)
                    if re.search(r"mCurrentFocus=Window\{\w+ u0 android\}", adb("shell", "dumpsys", "window")):
                        note("%s: system dialog in front (16 KB page size compatibility); tapping OK" % pkg)
                        adb("shell", "input", "tap", "891", "1509")
                lines, done = [], False
                for _ in range(a.launch_timeout // 5):
                    time.sleep(5)
                    lines = [l for l in adb("logcat", "-d", "-s", "Unity:V").splitlines() if "XLUA_VSBENCH" in l]
                    if any("XLUA_VSBENCH_DONE" in l for l in lines):
                        done = True
                        break
                    if not adb("shell", "pidof", pkg).strip():
                        break
                name = "r%d-%s" % (r, pkg.rsplit(".", 1)[1])
                (out_dir / ("logcat-%s.txt" % name)).write_text(adb("logcat", "-d"))
                if not done:
                    shot = out_dir / ("screen-%s.png" % name)
                    with lock:
                        print(now(), adb_exe, "-s", serial, "exec-out screencap -p >", shot, file=logf, flush=True)
                    shot.write_bytes(subprocess.run([adb_exe, "-s", serial, "exec-out", "screencap", "-p"], env=env,
                                                    stdout=subprocess.PIPE, timeout=60).stdout)
                    subprocess.run(["sips", "-Z", "800", str(shot)], stdout=subprocess.DEVNULL)
                adb("shell", "am", "force-stop", pkg)
                status2, cpu2 = thermal()
                res = [re.sub(r"^.*?(XLUA_VSBENCH)", r"\1", l) for l in lines]
                (out_dir / ("result-%s.txt" % name)).write_text("\n".join(res) + "\n")
                summary["launches"].append({"round": r, "player": n, "done": done, "seconds": round(time.time() - started),
                                            "thermal_before": status, "cpu_c_before": cpu, "thermal_after": status2,
                                            "cpu_c_after": cpu2})
                (out_dir / "launches.json").write_text(json.dumps(summary, indent=1))
                note("%s %s in %ds (%s, cpu %s -> %s C)" % (name, "done" if done else "NO RESULT", time.time() - started,
                                                           status2, cpu, cpu2))
                time.sleep(a.cooldown)
    finally:
        stop.set()
        for n, pkg, apk in players:
            adb("shell", "am", "force-stop", pkg)
            adb("uninstall", pkg)
        if a.target == "phone":
            adb("shell", "am", "start", "-n", "com.emindeniz99.chargeaod/.MainActivity")
            time.sleep(3)
            adb("shell", "input", "keyevent", "KEYCODE_HOME")
            aod = adb("shell", "dumpsys", "activity", "services", "com.emindeniz99.chargeaod")
            note("chargeaod after: AodService foreground=%s" % ("AodService" in aod and "isForeground=true" in aod))
        note("after: " + (adb("shell", "pm", "list", "packages", "org.xluang").strip().replace("\n", " ") or "no org.xluang packages"))
        if emu_proc:
            adb("emu", "kill")
            try:
                emu_proc.wait(timeout=60)
            except subprocess.TimeoutExpired:
                os.killpg(emu_proc.pid, signal.SIGKILL)
        if a.kill_adb_server:
            run([adb_exe, "kill-server"])


# ---------------------------------------------------------------- report

def load_align(data):
    """The smallest PT_LOAD alignment of a 64-bit little-endian ELF file (16384 is ready for 16 KB pages)."""
    import struct
    if data[:4] != b"\x7fELF" or data[4] != 2:
        return None
    phoff, = struct.unpack_from("<Q", data, 0x20)
    phentsize, phnum = struct.unpack_from("<HH", data, 0x36)
    aligns = [struct.unpack_from("<Q", data, phoff + k * phentsize + 0x30)[0] for k in range(phnum)
              if struct.unpack_from("<I", data, phoff + k * phentsize)[0] == 1]
    return min(aligns) if aligns else None


def apk_sizes(apk):
    """APK bytes, and the stored and compressed sizes of the native libraries (with their PT_LOAD alignment) and
    of the IL2CPP metadata."""
    z = zipfile.ZipFile(apk)
    libs = {}
    for i in z.infolist():
        if i.filename.startswith("lib/arm64-v8a/") or i.filename.endswith("global-metadata.dat"):
            e = {"bytes": i.file_size, "compressed": i.compress_size}
            if i.filename.endswith(".so"):
                e["load_align"] = load_align(z.read(i))
            libs[i.filename.rsplit("/", 1)[1]] = e
    return {"apk": apk.stat().st_size, "entries": libs}


# Case groups of the summary table: the geometric mean of the cases' medians, and its ratio to the first player.
GROUPS = [
    ("Lua -> C#: static calls, property and field (int)", ["lua2cs.static.method.0arg.void", "lua2cs.static.method.0arg.ret",
                                                          "lua2cs.static.prop.get.int", "lua2cs.static.field.set.int"]),
    ("Lua -> C#: instance calls (int, long, double)", ["lua2cs.instance.method.0arg.void", "lua2cs.instance.method.3arg.ret",
                                                      "lua2cs.instance.method.6arg.ret", "lua2cs.instance.method.long.3arg.ret",
                                                      "lua2cs.instance.method.double.3arg.ret"]),
    ("Lua -> C#: instance properties and fields (int, enum)", ["lua2cs.instance.prop.get.int", "lua2cs.instance.prop.set.int",
                                                              "lua2cs.instance.field.get.int", "lua2cs.instance.field.set.int",
                                                              "lua2cs.instance.prop.get.enum"]),
    ("Lua -> C#: strings", ["lua2cs.instance.method.string.3arg.ret", "lua2cs.instance.prop.get.string",
                            "lua2cs.instance.prop.set.string"]),
    ("Lua -> C#: Vector3 and struct", ["lua2cs.ctor.Vector3.full", "lua2cs.instance.method.Vector3.3arg.ret",
                                       "lua2cs.instance.prop.get.Vector3", "lua2cs.instance.prop.set.Vector3",
                                       "lua2cs.instance.field.get.Vector3", "lua2cs.instance.method.struct.3arg.ret"]),
    ("Lua -> C#: objects (new class, object and class arguments)", ["lua2cs.ctor.class.3int", "lua2cs.instance.method.object.3arg.ret",
                                                                   "lua2cs.instance.method.class.3arg.ret", "lua2cs.instance.prop.get.class"]),
    ("C# -> Lua: int, long, double", ["cs2lua.int.arity0.void", "cs2lua.int.3arg.void", "cs2lua.int.3arg.ret",
                                      "cs2lua.long.3arg.ret", "cs2lua.double.3arg.ret"]),
    ("C# -> Lua: string", ["cs2lua.string.3arg.ret"]),
    ("C# -> Lua: Vector3 and struct", ["cs2lua.Vector3.3arg.ret", "cs2lua.struct.3arg.ret"]),
    ("C# -> Lua: object and class", ["cs2lua.object.3arg.ret", "cs2lua.class.3arg.ret"]),
    ("Lua alone (loop, table)", ["lua.loop", "lua.table.rw"]),
    ("Pure Lua workloads (flag_bench.c's 18)", ["pure.empty_loop", "pure.function_call", "pure.method_call",
                                               "pure.table_array_insert", "pure.table_array_read", "pure.table_hash_set_get",
                                               "pure.string_concat", "pure.string_format", "pure.closure_create_call",
                                               "pure.int_math", "pure.fib_recursive", "pure.float_math", "pure.string_pattern",
                                               "pure.table_sort", "pure.table_concat", "pure.coroutine_switch",
                                               "pure.gc_small_tables", "pure.oop_inherit"]),
]


def report(a):
    work = a.work.resolve()
    results = {}
    for target in ["phone", "emu"]:
        folder = work / target
        if not folder.is_dir():
            continue
        per = {}
        meta = {}
        skips = {}
        checks = {}
        allocs = {}
        pure_checks = {}
        for f in sorted(folder.glob("result-*.txt")):
            lib = None
            for line in f.read_text().splitlines():
                kind, _, rest = line.partition(" ")
                if kind == "XLUA_VSBENCH_META":
                    m = json.loads(rest)
                    lib = m["library"] + " " + m["mode"]
                    meta[lib] = m
                elif kind == "XLUA_VSBENCH" and lib:
                    d = json.loads(rest)
                    per.setdefault(lib, {}).setdefault(d["name"], []).append(d["median"])
                    if "check" in d:
                        pure_checks.setdefault(lib, {}).setdefault(d["name"], set()).add((d["check"], d["check_ok"]))
                elif kind == "XLUA_VSBENCH_ALLOC" and lib:
                    d = json.loads(rest)
                    if not d["error"] and d["gcs"] == 0:
                        allocs.setdefault(lib, {}).setdefault(d["name"], []).append(
                            (d["managed_bytes_per_op"], d["lua_bytes_per_op"]))
                elif kind == "XLUA_VSBENCH_SKIP":
                    d = json.loads(rest)
                    skips.setdefault(f.name.split("-", 2)[2][:-4], {})[d["name"]] = d["error"]
                elif kind == "XLUA_VSBENCH_CHECK" and lib:
                    checks[lib] = json.loads(rest)
        table = {lib: {case: {"median": round(statistics.median(v), 1), "min": round(min(v), 1), "launches": len(v)}
                       for case, v in cases.items()} for lib, cases in per.items()}
        alloc_table = {lib: {case: {"managed": statistics.median(m for m, _ in v), "lua": statistics.median(l for _, l in v),
                                    "launches": len(v)} for case, v in cases.items()} for lib, cases in allocs.items()}
        results[target] = {"meta": meta, "cases": table, "skips": skips, "checks": checks, "allocs": alloc_table,
                           "pure_checks": {lib: {c: sorted([list(x) for x in v], key=str) for c, v in cs.items()}
                                           for lib, cs in pure_checks.items()}}
    sizes = {n: apk_sizes(work / rel) for n, _, rel in PLAYERS if (work / rel).is_file()}
    if (work / BASELINE_APK).is_file():
        sizes["empty project"] = apk_sizes(work / BASELINE_APK)
    results["sizes"] = sizes
    (work / "vs-report.json").write_text(json.dumps(results, indent=1) + "\n")
    order = [n for n, _, _ in PLAYERS]
    md = []
    for target in ["phone", "emu"]:
        if target not in results:
            continue
        r = results[target]
        libs = [l for l in sorted(r["cases"], key=lambda l: next((i for i, n in enumerate(order) if l.startswith(n.split()[0]) and l.endswith(n.split()[-1])), 99))]
        md.append("### %s\n" % target)
        md.append("| case | " + " | ".join(libs) + " |")
        md.append("| --- |" + " ---: |" * len(libs))
        cases = []
        for l in libs:
            for c in r["cases"][l]:
                if c not in cases:
                    cases.append(c)
        for c in cases:
            cells = []
            for l in libs:
                v = r["cases"][l].get(c)
                cells.append("%.1f (%.1f)" % (v["median"], v["min"]) if v else "n/a")
            md.append("| `%s` | %s |" % (c, " | ".join(cells)))
        md.append("")
        md.append("| group | " + " | ".join(libs) + " |")
        md.append("| --- |" + " ---: |" * len(libs))
        for g, cs in GROUPS:
            means = []
            for l in libs:
                vals = [r["cases"][l][c]["median"] for c in cs if c in r["cases"][l]]
                means.append(math.exp(sum(math.log(v) for v in vals) / len(vals)) if len(vals) == len(cs) else None)
            cells = ["%.0f (%.2fx)" % (m, m / means[0]) if m and means[0] else "n/a" for m in means]
            md.append("| %s | %s |" % (g, " | ".join(cells)))
        md.append("")
        if r["allocs"]:
            md.append("Bytes per operation with both collectors stopped (managed / Lua heap), the median of the launches:\n")
            md.append("| case | " + " | ".join(libs) + " |")
            md.append("| --- |" + " ---: |" * len(libs))
            for c in cases:
                cells = []
                for l in libs:
                    v = r["allocs"].get(l, {}).get(c)
                    cells.append("%.0f / %.0f" % (v["managed"], v["lua"]) if v else "n/a")
                md.append("| `%s` | %s |" % (c, " | ".join(cells)))
            md.append("")
    md.append("### sizes\n")
    md.append("| player | APK | libil2cpp.so | global-metadata.dat | other native libraries |")
    md.append("| --- | ---: | ---: | ---: | --- |")
    for n, s in sizes.items():
        e = s["entries"]
        other = ", ".join("%s %d" % (k, v["bytes"]) for k, v in sorted(e.items()) if k not in ("libil2cpp.so", "global-metadata.dat", "libunity.so", "libmain.so"))
        md.append("| %s | %d | %d | %d | %s |" % (n, s["apk"], e.get("libil2cpp.so", {}).get("bytes", 0),
                                                e.get("global-metadata.dat", {}).get("bytes", 0), other))
    (work / "vs-report.md").write_text("\n".join(md) + "\n")
    print("\n".join(md))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build")
    b.add_argument("--work", type=Path, required=True)
    b.add_argument("--unity", required=True)
    b.add_argument("--libs", default="puerts,zlua")
    b.add_argument("--puerts-core", type=Path, help="PuerTS_Core_<v>.tar.gz of the PuerTS Unity release")
    b.add_argument("--puerts-lua", type=Path, help="PuerTS_Lua_<v>.tar.gz of the same release")
    b.add_argument("--zlua", type=Path, help="the source archive of a zlua release tag")
    b.add_argument("--lua-tarball", type=Path, help="lua-X.Y.Z.tar.gz from lua.org for zlua's installer cache")
    b.add_argument("--moonsharp", type=Path, help="a staged org.moonsharp.moonsharp package folder (MoonSharp-ng's "
                   "tools/upm/stage-local-package.sh)")
    b.add_argument("--tmpdir", default=os.environ.get("XLUA_SIZE_TMPDIR", "/tmp"),
                   help="a short TMPDIR: Unity's build IPC sockets need paths under 104 bytes")
    b.add_argument("--timeout", type=int, default=2400)
    b.add_argument("--keep-project", action="store_true", help="reuse a prepared project")
    d = sub.add_parser("device")
    d.add_argument("target", choices=["emu", "phone"])
    d.add_argument("--work", type=Path, required=True)
    d.add_argument("--sdk", type=Path, required=True)
    d.add_argument("--rounds", type=int, default=8)
    d.add_argument("--cooldown", type=int, default=15)
    d.add_argument("--only", help="regex on the player names")
    d.add_argument("--serial", help="the phone's adb serial (default: the only non-emulator device)")
    d.add_argument("--launch-timeout", type=int, default=600, help="seconds one launch may take")
    d.add_argument("--kill-adb-server", action="store_true", help="stop the adb server at the end")
    r = sub.add_parser("report")
    r.add_argument("--work", type=Path, required=True)
    a = p.parse_args()
    {"build": build, "device": device, "report": report}[a.cmd](a)


if __name__ == "__main__":
    main()
