"""Build libxlua with different compiler and linker options, then measure size and speed.

A measurement tool: it never changes the release build. Each variant configures
build/CMakeLists.txt with the NDK toolchain like build/android_common.sh, and adds
its options through CMake cache variables (CMAKE_C_FLAGS_RELEASE, linker flags)
and, for per-file optimisation levels, a compiler launcher (this script's `cc`
command). Variants that need a source change (-fvisibility=hidden) build from a
patched copy of build/ in the output directory.

Commands:
  android   build the Android variants, write sizes.json and a size table
  bench     run Test/Native/flag_bench.c on a device against each built variant,
            interleaved over several rounds, and write bench-<abi><tag>.json
  report    print the size and speed tables from sizes.json and bench-<abi><tag>.json
  ios       build libxlua.a for iOS per variant and measure __TEXT and a dead-stripped link
  host      build host (macOS or Linux) variants; --test runs the native suites on them
  profile   record the baseline on the device with simpleperf and rank source files
  list      print the variants

Sizes per Android variant: stripped bytes (llvm-strip --strip-unneeded, what Unity
packs), gzip -9 of the stripped file, and "apk": zip deflate (level 6, what the
Android Gradle plugin uses for native libraries in a compressed APK) of the stripped file.
Device rules: files go only to --device-dir (default /data/local/tmp/xlua-bench), the
directory is removed at the end, and every adb command is logged to adb-commands.log.
"""
import argparse
import datetime
import gzip
import io
import json
import os
from pathlib import Path
import random
import re
import shlex
import shutil
import statistics
import subprocess
import sys
import time
import zipfile

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import managed_imports  # noqa: E402

# Files of the Lua 5.5 interpreter's hot paths: the VM loop, calls, tables, GC,
# strings, the C API, closures and number conversion. `profile` checks this list.
HOT_FILES = ["lvm.c", "ldo.c", "ltable.c", "lgc.c", "lstring.c", "lapi.c", "lfunc.c", "lobject.c"]
# HOT_FILES plus what the device profile of flag_bench ranks next: string patterns and
# formatting, the auxiliary library (luaL_Buffer), allocation, metamethods and table.sort.
HOT_FILES_WIDE = HOT_FILES + ["lstrlib.c", "lauxlib.c", "lmem.c", "ltm.c", "ltablib.c"]
# HOT_FILES_WIDE plus the compiler (lexer, parser, code generator) and the bytecode loader,
# for projects that load Lua source or bytecode in time-critical places.
HOT_FILES_LOAD = HOT_FILES_WIDE + ["llex.c", "lparser.c", "lcode.c", "lundump.c", "lzio.c"]

OUTLINER = "-mllvm -enable-machine-outliner"
# -Oz turns the machine outliner on for AArch64. Every outlined sequence gets its own
# unwind entry (.eh_frame, .eh_frame_hdr), which costs more than the code it saves.
OZ = "-Oz -mllvm -enable-machine-outliner=never"


def V(cflags="-O3", hot=None, ldflags="", api=None, hidden=False, lto=None, note="", hot_files=None, cmake=None):
    return {"cflags": cflags, "hot": hot, "ldflags": ldflags, "api": api, "hidden": hidden, "lto": lto, "note": note,
            "hot_files": hot_files or HOT_FILES, "cmake": cmake or []}


# Name -> variant. cflags replace CMAKE_C_FLAGS_RELEASE's "-O3" (-DNDEBUG is kept);
# hot is the flag set for HOT_FILES (the other files get cflags); cmake lists extra
# -D options of build/CMakeLists.txt.
VARIANTS = {
    "O3": V(note="release default (NDK Release)"),
    "O2": V("-O2"),
    "Os": V("-Os"),
    "Oz": V("-Oz"),
    "hybrid-O3": V("-Oz", hot="-O3", note="HOT_FILES -O3, the rest -Oz"),
    "hybrid-O2": V("-Oz", hot="-O2", note="HOT_FILES -O2, the rest -Oz"),
    "hybridw-O3": V("-Oz", hot="-O3", hot_files=HOT_FILES_WIDE, note="HOT_FILES_WIDE -O3, the rest -Oz"),
    "hybridOs-O3": V("-Os", hot="-O3", note="HOT_FILES -O3, the rest -Os"),
    "Oz-noout": V("-Oz -mllvm -enable-machine-outliner=never", note="-Oz without the machine outliner it enables on AArch64"),
    "hybridn-O3": V(OZ, hot="-O3", note="HOT_FILES -O3, the rest -Oz without the outliner"),
    "hybridn-O2": V(OZ, hot="-O2", note="HOT_FILES -O2, the rest -Oz without the outliner"),
    "hybridnw-O3": V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, note="HOT_FILES_WIDE -O3, the rest -Oz without the outliner"),
    "thinlto-O3": V("-O3", lto="thin"),
    "fulllto-O3": V("-O3", lto="full"),
    "thinlto-hybrid": V("-Oz", hot="-O3", lto="thin", ldflags="-Wl,--lto-O3"),
    "fulllto-hybrid": V("-Oz", hot="-O3", lto="full", ldflags="-Wl,--lto-O3"),
    "outliner-O3": V("-O3 " + OUTLINER),
    "outliner-O2": V("-O2 " + OUTLINER),
    "icf": V(ldflags="-Wl,--icf=all"),
    "icf-safe": V(ldflags="-Wl,--icf=safe", note="folds only functions whose address is not taken (clang's address-significance table)"),
    "vscript": V(ldflags="@vscript", note="export only the LuaDLL.cs imports"),
    "hidden": V(hidden=True, note="-fvisibility=hidden, LUA_API and LUASOCKET_API default"),
    "hidden-vscript": V(hidden=True, ldflags="@vscript"),
    "api23": V(api=23, note="API 23 changes the driver default to --hash-style=gnu"),
    "api23-aps2": V(api=23, ldflags="-Wl,--pack-dyn-relocs=android"),
    "api24": V(api=24, note="32-bit ABIs get a 64-bit off_t (CMakeLists drops _FILE_OFFSET_BITS=32)"),
    "api24-aps2": V(api=24, ldflags="-Wl,--pack-dyn-relocs=android"),
    "api21-gnuhash": V(ldflags="-Wl,--hash-style=gnu", note="API 21 with only the GNU hash: not loadable below API 23"),
    "api28-relr": V(api=28, note="reference only: the driver adds --pack-dyn-relocs=relr at API 28"),
    "best21": V("-Oz", hot="-O3", hidden=True, ldflags="@vscript -Wl,--icf=all"),
    "best23": V("-Oz", hot="-O3", hidden=True, api=23, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    "bestnwc21": V(OZ, hot="-O3", hot_files=HOT_FILES_LOAD, hidden=True, ldflags="@vscript -Wl,--icf=all"),
    "bestnwc23": V(OZ, hot="-O3", hot_files=HOT_FILES_LOAD, hidden=True, api=23, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    "hybridnw-hidden": V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, note="the compiler half of bestnw*, for host tests"),
    "bestn21": V(OZ, hot="-O3", hidden=True, ldflags="@vscript -Wl,--icf=all"),
    "bestn23": V(OZ, hot="-O3", hidden=True, api=23, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    "bestnw21": V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, ldflags="@vscript -Wl,--icf=all"),
    "bestnw23": V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, api=23, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    "bestnw24": V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, api=24, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    # Layout controls: the same objects as O3 / bestnw23 with lld's sections in a shuffled order.
    # Their spread is the speed change that comes only from where the hot code lands.
    **{"O3-shuf%d" % i: V(ldflags="-Wl,--shuffle-sections=*=%d" % i, note="layout control") for i in range(1, 5)},
    **{"bestnw23-shuf%d" % i: V(OZ, hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, api=23,
                                 ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android -Wl,--shuffle-sections=*=%d" % i,
                                 note="layout control") for i in range(1, 3)},
    "bestw21": V("-Oz", hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, ldflags="@vscript -Wl,--icf=all"),
    "bestw23": V("-Oz", hot="-O3", hot_files=HOT_FILES_WIDE, hidden=True, api=23, ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android"),
    # XLUA_HARDEN and its flags one at a time (the NDK already passes -fstack-protector-strong
    # and -D_FORTIFY_SOURCE=2, so on Android XLUA_HARDEN adds the other three).
    "O3-harden": V(cmake=["-DXLUA_HARDEN=ON"], note="XLUA_HARDEN=ON"),
    "bestnwc23-harden": V(OZ, hot="-O3", hot_files=HOT_FILES_LOAD, hidden=True, api=23,
                          ldflags="@vscript -Wl,--icf=all -Wl,--pack-dyn-relocs=android", cmake=["-DXLUA_HARDEN=ON"]),
    "O3-bti": V("-O3 -mbranch-protection=standard", note="arm64 only: PAC and BTI"),
    "O3-autoinit": V("-O3 -ftrivial-auto-var-init=zero"),
    "O3-nulldel": V("-O3 -fno-delete-null-pointer-checks"),
}


def run(cmd, log=None, **kw):
    if log:
        log.write("$ " + " ".join(shlex.quote(str(c)) for c in cmd) + "\n")
        log.flush()
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def ndk_bin(ndk):
    hosts = list((Path(ndk) / "toolchains/llvm/prebuilt").iterdir())
    return hosts[0] / "bin"


# ---------------------------------------------------------------- compiler launcher

def cc_launch(argv):
    """CMAKE_C_COMPILER_LAUNCHER: swap the -O flags of HOT_FILES for the hot set.

    argv: <hot flags> <hot files, comma separated> <compiler command...>
    """
    hot_flags, hot_files, cmd = argv[0].split(), set(argv[1].split(",")), argv[2:]
    src = cmd[cmd.index("-c") + 1] if "-c" in cmd else ""
    if os.path.basename(src) in hot_files:
        cmd = [c for c in cmd if not re.fullmatch(r"-O[0-3sz]|-Ofast", c)]
        # Drop an outliner option of the cold set too, so the hot files compile exactly as hot_flags says.
        out = []
        skip = False
        for c in cmd:
            if skip:
                skip = False
                continue
            if c == "-mllvm":
                skip = True
                continue
            out.append(c)
        cmd = out + hot_flags
    os.execvp(cmd[0], cmd)


# ---------------------------------------------------------------- sources

def patched_source(out):
    """A copy of build/ where LUA_API exports with default visibility, for -fvisibility=hidden."""
    dst = out / "src-hidden"
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(REPO / "build", dst, ignore=shutil.ignore_patterns("build*.*", "build_*", "plugin_*", "luajit-2.1"))
    for conf in dst.glob("lua-*/src/luaconf.h*"):
        text = conf.read_text()
        new = re.sub(r"#define LUA_API\t\textern", '#define LUA_API\t\t__attribute__((visibility("default"))) extern', text)
        assert new != text, conf
        conf.write_text(new)
    return dst


def version_script(out, exports):
    path = out / "exports.map"
    path.write_text("{\n  global:\n" + "".join("    %s;\n" % n for n in exports) + "  local:\n    *;\n};\n")
    return path


# ---------------------------------------------------------------- sizes

def strip(tool, src, dst):
    shutil.copy2(src, dst)
    run([tool, "--strip-unneeded", dst])


def sizes_of(path):
    data = Path(path).read_bytes()
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        z.writestr("lib/arm64-v8a/libxlua.so", data)
    with zipfile.ZipFile(buf) as z:
        apk = z.infolist()[0].compress_size
    return {"stripped": len(data), "gzip": len(gzip.compress(data, 9, mtime=0)), "apk": apk}


def sections(readelf, path):
    out = subprocess.run([str(readelf), "-SW", str(path)], capture_output=True, text=True, check=True).stdout
    found = {}
    for m in re.finditer(r"\]\s+(\.\S+)\s+\S+\s+[0-9a-f]+\s+[0-9a-f]+\s+([0-9a-f]+)", out):
        found[m.group(1)] = int(m.group(2), 16)
    return found


# ---------------------------------------------------------------- android

def build_android(a):
    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    bin_dir = ndk_bin(a.ndk)
    toolchain = Path(a.ndk) / "build/cmake/android.toolchain.cmake"
    exports = managed_imports({"UNITY_ANDROID"})
    vscript = version_script(out, exports)
    hidden_src = None
    names = a.variants.split(",") if a.variants else list(VARIANTS)
    log = open(out / "build.log", "a")
    results = json.loads((out / "sizes.json").read_text()) if (out / "sizes.json").exists() else {}
    for abi in a.abis.split(","):
        for name in names:
            v = VARIANTS[name]
            api = v["api"] or a.api
            src = REPO / "build"
            if v["hidden"]:
                hidden_src = hidden_src or patched_source(out)
                src = hidden_src
            bdir = out / "android" / abi / name
            cflags = v["cflags"] + " -DNDEBUG"
            common = ""
            if v["hidden"]:
                common += " -fvisibility=hidden"
            if v["lto"]:
                common += " -flto=thin" if v["lto"] == "thin" else " -flto"
            ld = v["ldflags"].replace("@vscript", "-Wl,--version-script=" + str(vscript))
            if v["lto"] and OUTLINER in cflags:
                ld += " -Wl,-mllvm,-enable-machine-outliner"
            cmd = ["cmake", "-S", src, "-B", bdir, "-G", "Ninja", "-DLUA_VERSION=" + a.lua,
                   "-DANDROID_ABI=" + abi, "-DANDROID_PLATFORM=android-%d" % api,
                   "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_TOOLCHAIN_FILE=" + str(toolchain),
                   "-DCMAKE_C_FLAGS_RELEASE=" + cflags, "-DCMAKE_C_FLAGS=" + common.strip(),
                   "-DCMAKE_SHARED_LINKER_FLAGS=" + ld.strip()] + v["cmake"]
            if v["hot"]:
                cmd.append("-DCMAKE_C_COMPILER_LAUNCHER=%s;%s;cc;%s;%s" % (sys.executable, Path(__file__).resolve(), v["hot"], ",".join(v["hot_files"])))
            if bdir.exists():
                shutil.rmtree(bdir)
            print("==>", abi, name, flush=True)
            run(cmd, log, stdout=log, stderr=subprocess.STDOUT)
            run(["cmake", "--build", bdir, "--parallel", str(a.jobs)], log, stdout=log, stderr=subprocess.STDOUT)
            lib_dir = out / "android" / abi / "libs"
            lib_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(bdir / "libxlua.so", lib_dir / (name + ".unstripped.so"))
            stripped = lib_dir / (name + ".so")
            strip(bin_dir / "llvm-strip", bdir / "libxlua.so", stripped)
            entry = sizes_of(stripped)
            entry["api"] = api
            entry["sections"] = sections(bin_dir / "llvm-readelf", stripped)
            dyn = subprocess.run([str(bin_dir / "llvm-nm"), "-D", "--defined-only", str(stripped)], capture_output=True, text=True, check=True).stdout
            entry["exports"] = len([l for l in dyn.splitlines() if re.search(r" [TDBRW] ", l)])
            results.setdefault(abi, {})[name] = entry
            (out / "sizes.json").write_text(json.dumps(results, indent=1) + "\n")
    print_sizes(results)


def print_sizes(results, bench=None):
    for abi, variants in results.items():
        base = variants.get("O3")
        print("\n%s\n%-16s %10s %9s %10s %9s %10s %9s %6s" % (abi, "variant", "stripped", "delta", "gzip -9", "delta", "apk", "delta", "api"))
        for name, e in variants.items():
            d = lambda k: "%+d" % (e[k] - base[k]) if base else "-"
            print("%-16s %10d %9s %10d %9s %10d %9s %6s" % (name, e["stripped"], d("stripped"), e["gzip"], d("gzip"), e["apk"], d("apk"), e["api"]))


# ---------------------------------------------------------------- runner

def build_runner(a, abi, dest):
    bin_dir = ndk_bin(a.ndk)
    triple = {"arm64-v8a": "aarch64-linux-android", "armeabi-v7a": "armv7a-linux-androideabi"}[abi]
    run([bin_dir / "clang", "--target=%s%d" % (triple, a.api), "-O2", "-fPIE", "-pie", "-Wall",
         REPO / "Test/Native/flag_bench.c", "-o", dest, "-ldl"])


class Device:
    def __init__(self, a, log_path):
        self.adb = a.adb
        self.serial = a.serial
        self.dir = a.device_dir.rstrip("/")
        assert self.dir.startswith("/data/local/tmp/"), "device files must stay under /data/local/tmp"
        self.log = open(log_path, "a")

    def cmd(self, *args, check=True, capture=True):
        for attempt in range(20):
            full = [self.adb] + (["-s", self.serial] if self.serial else []) + list(args)
            self.log.write("%s $ %s\n" % (datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"), " ".join(shlex.quote(x) for x in full)))
            self.log.flush()
            r = subprocess.run(full, capture_output=capture, text=True)
            err = (r.stderr or "") + (r.stdout or "")
            if r.returncode != 0 and "more than one device" in err and not self.serial:
                # mDNS and adb connect can list the same phone twice; use one transport.
                devices = subprocess.run([self.adb, "devices"], capture_output=True, text=True).stdout.splitlines()[1:]
                serials = [l.split()[0] for l in devices if l.endswith("device")]
                if serials:
                    self.serial = serials[0]
                    self.log.write("  (several transports, using %s)\n" % self.serial)
                    continue
            if not (r.returncode != 0 and re.search(r"no devices|device offline|device '.*' not found|daemon not running|closed", err)):
                break
            # The adb server restarted or wireless debugging dropped. Reconnect to the
            # address mDNS reports (adb connect only; never pair from here).
            self.log.write("  (device lost, waiting: %s)\n" % err.strip()[:200])
            time.sleep(15)
            self.reconnect()
        if check and r.returncode != 0:
            raise SystemExit("adb failed: %s\n%s%s" % (" ".join(full), r.stdout, r.stderr))
        return (r.stdout + r.stderr) if capture else ""

    def reconnect(self):
        out = subprocess.run([self.adb, "mdns", "services"], capture_output=True, text=True).stdout
        for addr in re.findall(r"_adb-tls-connect\._tcp\.?\s+(\d+\.\d+\.\d+\.\d+:\d+)", out):
            r = subprocess.run([self.adb, "connect", addr], capture_output=True, text=True)
            self.log.write("%s $ %s connect %s -> %s\n" % (datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
                                                       self.adb, addr, (r.stdout + r.stderr).strip()))
            if self.serial and self.serial != addr and "connected" in r.stdout:
                self.serial = addr

    def shell(self, text, check=True):
        """Run a shell command; a reply without the exit marker (wireless adb dropped
        the session) is retried, so only idempotent commands go through here."""
        for attempt in range(5):
            out = self.cmd("shell", text + "; echo XLUA_EXIT=$?", check=False)
            m = re.search(r"XLUA_EXIT=(\d+)\s*$", out)
            if m:
                out = out[:m.start()]
                if check and m.group(1) != "0":
                    raise SystemExit("adb shell failed (%s): %s\n%s" % (m.group(1), text, out[-2000:]))
                return out
            self.log.write("  (no exit marker, retrying)\n")
            time.sleep(10)
        raise SystemExit("adb shell gave no reply 5 times: " + text)


def thermal(dev, zones):
    out = dev.shell("cat " + " ".join("/sys/class/thermal/thermal_zone%d/temp" % z for z in zones), check=False)
    vals = [int(x) for x in out.split() if x.strip().lstrip("-").isdigit()]
    return max(vals) / 1000.0 if vals else None


def throttle_state(dev):
    out = dev.shell("for d in /sys/class/thermal/cooling_device*; do t=$(cat $d/type); case $t in cpufreq-*|cpu-cluster*) echo $t $(cat $d/cur_state);; esac; done", check=False)
    return dict(l.split() for l in out.splitlines() if len(l.split()) == 2)


def snapshot(dev):
    snap = {"time": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")}
    for prop in ["ro.product.model", "ro.board.platform", "ro.build.version.release", "ro.build.version.sdk", "ro.soc.model"]:
        snap[prop] = dev.shell("getprop " + prop).strip()
    snap["governors"] = dev.shell("cat /sys/devices/system/cpu/cpufreq/policy*/scaling_governor").split()
    snap["cur_freq"] = dev.shell("cat /sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq").split()
    status = re.search(r"Thermal Status: (\d+)", dev.shell("dumpsys thermalservice", check=False))
    snap["thermal_status"] = int(status.group(1)) if status else None
    snap["cooling"] = throttle_state(dev)
    return snap


def run_detached(dev, cmd, poll=5, limit=1800):
    """Run a command on the device in its own session with its output in a file, then poll.

    A dropped adb connection (another adb server version restarting the server, Wi-Fi)
    then only interrupts the polling, not the run. A plain `( ... ) &` is not enough:
    adbd keeps the shell open until such a job ends, so it needs setsid.
    """
    out = dev.dir + "/run.txt"
    assert '"' not in cmd
    dev.shell('rm -f %s; setsid sh -c "%s > %s 2>&1; echo XLUA_RUN_EXIT=\\$? >> %s" </dev/null >/dev/null 2>&1 & true' % (out, cmd, out, out))
    start = time.time()
    # Each poll also reads the runner's allowed CPUs and the CPU it last ran on
    # (field 39 of /proc/<pid>/stat), to show that the pinning held.
    probe = ("; for p in $(pidof flag_bench); do echo XLUA_PROC $(grep Cpus_allowed_list /proc/$p/status | cut -f2)"
             " $(cut -d' ' -f39 /proc/$p/stat); done")
    procs = []
    while time.time() - start < limit:
        time.sleep(poll)
        text = dev.shell("cat " + out + probe, check=False)
        procs += re.findall(r"^XLUA_PROC (\S+) (\d+)\s*$", text, re.M)
        text = re.sub(r"^XLUA_PROC .*\n?", "", text, flags=re.M)
        if "XLUA_RUN_EXIT=" in text:
            return text, procs
    return dev.shell("cat " + out, check=False) + "\n(timed out)", procs


def bench(a):
    out = a.output.resolve()
    abi = a.abis.split(",")[0]
    lib_dir = out / "android" / abi / "libs"
    names = a.variants.split(",") if a.variants else [p.stem for p in sorted(lib_dir.glob("*.so")) if ".unstripped" not in p.name]
    if "O3" in names:
        names.remove("O3")
    names.insert(0, "O3")
    runner = out / "android" / abi / "flag_bench"
    build_runner(a, abi, runner)
    dev = Device(a, out / "adb-commands.log")
    result = {"abi": abi, "variants": names, "rounds": [], "settings": vars(a).copy(), "started": None}
    result["settings"] = {k: str(v) for k, v in result["settings"].items()}
    result["before"] = snapshot(dev)
    result["started"] = result["before"]["time"]
    dev.shell("mkdir -p " + dev.dir)
    try:
        dev.cmd("push", str(runner), dev.dir + "/flag_bench")
        for n in names:
            dev.cmd("push", str(lib_dir / (n + ".so")), "%s/%s.so" % (dev.dir, n))
        dev.shell("chmod 755 %s/flag_bench" % dev.dir)
        # Smoke every variant first: a library that fails here is not timed.
        for n in names:
            text = dev.shell("cd %s && ./flag_bench ./%s.so --smoke" % (dev.dir, n), check=False)
            ok = "XLUA_FLAG_SMOKE_OK" in text
            result.setdefault("smoke", {})[n] = text.strip()
            print("smoke", n, "ok" if ok else "FAILED: " + text.strip(), flush=True)
            if not ok:
                raise SystemExit("smoke failed for " + n)
        zones = [int(z) for z in a.thermal_zones.split(",")]
        rng = random.Random(a.seed)
        for r in range(a.rounds):
            order = names[:]
            rng.shuffle(order)
            for n in order:
                t = thermal(dev, zones)
                waited = 0
                while t is not None and t > a.max_temp and waited < 600:
                    time.sleep(10)
                    waited += 10
                    t = thermal(dev, zones)
                time.sleep(a.cooldown)
                t_before = thermal(dev, zones)
                cmd = "cd %s && ./flag_bench ./%s.so --runs %d --warmup %d --scale %s --cpus %s" % (dev.dir, n, a.runs, a.warmup, a.scale, a.cpus)
                if a.filter:
                    cmd += " --filter " + a.filter
                t0 = time.time()
                # Wireless adb can drop a long shell session; retry such a run, but a
                # runner that fails twice is reported.
                for attempt in range(3):
                    text, procs = run_detached(dev, cmd)
                    if "XLUA_FLAG_BENCH_DONE" in text:
                        break
                    result.setdefault("retries", []).append({"round": r, "variant": n, "tail": text[-500:]})
                    print("retrying %s: %s" % (n, text[-300:]), flush=True)
                    time.sleep(10)
                else:
                    raise SystemExit("bench failed for %s:\n%s" % (n, text[-2000:]))
                cases = [json.loads(m) for m in re.findall(r"^XLUA_FLAG_BENCH (\{.+\})\s*$", text, re.M)]
                entry = {"round": r, "variant": n, "temp_before": t_before, "temp_after": thermal(dev, zones),
                         "throttle": throttle_state(dev), "seconds": round(time.time() - t0, 1), "cases": cases,
                         "affinity": sorted(set(x[0] for x in procs) | set(re.findall(r"^XLUA_FLAG_AFFINITY (\S*)", text, re.M))),
                         "seen_cpus": sorted(set(int(x[1]) for x in procs)
                                             | set(int(c) for case in cases for c in case.get("cpus", "").split(",") if c))}
                result["rounds"].append(entry)
                print("round %d %-16s %5.1fs temp %s -> %s" % (r, n, entry["seconds"], t_before, entry["temp_after"]), flush=True)
                (out / ("bench-%s%s.json" % (abi, a.tag))).write_text(json.dumps(result, indent=1) + "\n")
    finally:
        dev.shell("rm -rf " + dev.dir, check=False)
        result["after"] = snapshot(dev)
        result["ended"] = result["after"]["time"]
        (out / ("bench-%s%s.json" % (abi, a.tag))).write_text(json.dumps(result, indent=1) + "\n")
    report(a)


# ---------------------------------------------------------------- report

def geomean(xs):
    import math
    return math.exp(sum(math.log(x) for x in xs) / len(xs))


def speed_table(bench_data):
    """variant -> case -> (median of per-round medians, spread %); checks equal results."""
    per = {}
    checks = {}
    for e in bench_data["rounds"]:
        for c in e["cases"]:
            per.setdefault(e["variant"], {}).setdefault(c["name"], []).append(c["median_ns"])
            checks.setdefault(c["name"], {}).setdefault(c["check"], set()).add(e["variant"])
    bad = {k: v for k, v in checks.items() if len(v) > 1}
    table = {}
    for v, cases in per.items():
        table[v] = {}
        for name, xs in cases.items():
            med = statistics.median(xs)
            table[v][name] = (med, (max(xs) - min(xs)) * 100.0 / med, len(xs))
    return table, bad


def report(a):
    out = a.output.resolve()
    sizes = json.loads((out / "sizes.json").read_text())
    for abi in sizes:
        bpath = out / ("bench-%s%s.json" % (abi, a.tag))
        bench_data = json.loads(bpath.read_text()) if bpath.exists() else None
        variants = sizes[abi]
        base = variants["O3"]
        table, bad = speed_table(bench_data) if bench_data else ({}, {})
        if bad:
            print("RESULT MISMATCH between variants:", bad)
        names = list(table.get("O3", {}))
        print("\n## %s\n" % abi)
        print("| variant | stripped | gzip -9 | APK (deflate) | speed vs O3, geomean | worst case | best case |")
        print("| --- | --- | --- | --- | --- | --- | --- |")
        for v, e in variants.items():
            row = "| %s | %d (%+d) | %d (%+d) | %d (%+d) |" % (v, e["stripped"], e["stripped"] - base["stripped"], e["gzip"],
                                                          e["gzip"] - base["gzip"], e["apk"], e["apk"] - base["apk"])
            if v in table and names:
                ratios = {n: table["O3"][n][0] / table[v][n][0] for n in names if n in table[v]}
                g = (geomean(list(ratios.values())) - 1) * 100
                worst = min(ratios, key=ratios.get)
                best = max(ratios, key=ratios.get)
                row += " %+.1f%% | %s %+.1f%% | %s %+.1f%% |" % (g, worst, (ratios[worst] - 1) * 100, best, (ratios[best] - 1) * 100)
            else:
                row += " - | - | - |"
            print(row)
        if table:
            vs = [v for v in variants if v in table]
            print("\nPer case: speed vs O3 in percent (+ is faster), median of %d rounds; O3 column is ns/op and spread (max-min)/median.\n"
                  % max(x[2] for x in table["O3"].values()))
            print("| case | O3 ns/op | " + " | ".join(vs[1:]) + " |")
            print("| --- | --- |" + " --- |" * (len(vs) - 1))
            for n in names:
                cells = ["%+.1f" % ((table["O3"][n][0] / table[v][n][0] - 1) * 100) for v in vs[1:]]
                print("| %s | %.1f ±%.0f%% | %s |" % (n, table["O3"][n][0], table["O3"][n][1] / 2, " | ".join(cells)))
            print("\n| spread (max-min)/median, median over cases | " + " | ".join(
                "%s %.1f%%" % (v, statistics.median(x[1] for x in table[v].values())) for v in vs) + " |")


# ---------------------------------------------------------------- profile

def profile(a):
    out = a.output.resolve()
    abi = a.abis.split(",")[0]
    lib_dir = out / "android" / abi / "libs"
    runner = out / "android" / abi / "flag_bench"
    build_runner(a, abi, runner)
    dev = Device(a, out / "adb-commands.log")
    dev.shell("mkdir -p " + dev.dir)
    try:
        dev.cmd("push", str(runner), dev.dir + "/flag_bench")
        dev.cmd("push", str(lib_dir / "O3.unstripped.so"), dev.dir + "/libxlua.so")
        dev.shell("chmod 755 %s/flag_bench" % dev.dir)
        dev.shell("cd %s && simpleperf record -o perf.data -e cpu-clock -f 4000 ./flag_bench ./libxlua.so --runs 1 --warmup 0 --scale %s --cpus %s >/dev/null"
                  % (dev.dir, a.scale, a.cpus))
        text = dev.shell("cd %s && simpleperf report -i perf.data --dsos %s/libxlua.so --sort symbol -n --percent-limit 0.05" % (dev.dir, dev.dir))
        # Samples on the pinned CPUs against all samples, to check the --cpus pinning.
        # The phone's simpleperf has no --sort cpu, so --cpu filters the report instead.
        report = "cd %s && simpleperf report -i perf.data --sort dso -n" % dev.dir
        (out / "profile-cpus.txt").write_text(
            "== all CPUs\n" + dev.shell(report, check=False)
            + "\n== --cpu %s\n" % a.cpus + dev.shell(report + " --cpu " + a.cpus, check=False))
    finally:
        dev.shell("rm -rf " + dev.dir, check=False)
    (out / "profile.txt").write_text(text)
    # Map symbols to the object files that define them.
    nm = ndk_bin(a.ndk) / "llvm-nm"
    owner = {}
    for obj in (out / "android" / abi / "O3").rglob("*.c.o"):
        for line in subprocess.run([str(nm), "--defined-only", str(obj)], capture_output=True, text=True).stdout.splitlines():
            parts = line.split()
            if len(parts) == 3 and parts[1] in "tT":
                owner.setdefault(parts[2], obj.name[:-2])
    by_file = {}
    for line in text.splitlines():
        m = re.match(r"\s*([\d.]+)%\s+\d+\s+(\S+)", line)
        if m:
            f = owner.get(m.group(2), "?")
            by_file[f] = by_file.get(f, 0) + float(m.group(1))
    total = sum(by_file.values())
    print("share of samples in libxlua by source file:")
    for f, p in sorted(by_file.items(), key=lambda x: -x[1]):
        print("  %-24s %5.1f%%%s" % (f, p * 100 / total, "  (hot list)" if f in HOT_FILES else ""))


# ---------------------------------------------------------------- iOS

IOS_VARIANTS = {
    # name: (flags, flags of the hot files or None, LTO flag or None[, hot file list])
    "O3": ("-O3", None, None),
    "O2": ("-O2", None, None),
    "Os": ("-Os", None, None),
    "Oz": ("-Oz", None, None),
    "Oz-noout": (OZ, None, None),
    "hybrid-O3": ("-Oz", "-O3", None),
    "hybrid-O2": ("-Oz", "-O2", None),
    "hybridn-O3": (OZ, "-O3", None),
    "hybridn-O2": (OZ, "-O2", None),
    "hybridw-O3": ("-Oz", "-O3", None, HOT_FILES_WIDE),
    "hybridnw-O3": (OZ, "-O3", None, HOT_FILES_WIDE),
    "hybridwc-O3": ("-Oz", "-O3", None, HOT_FILES_LOAD),
    "outliner-O3": ("-O3 " + OUTLINER, None, None),
    # Reference only: LTO objects in a .a are bitcode tied to this clang version.
    "thinlto-O3": ("-O3", None, "-flto=thin"),
    "fulllto-O3": ("-O3", None, "-flto"),
    "fulllto-hybridn": (OZ, "-O3", "-flto"),
}


def ios_sources(lua):
    src = REPO / "build" / ("lua-%s/src" % lua)
    lua_files = sorted(p for p in src.glob("*.c") if p.name not in ("lua.c", "luac.c"))
    sock = ["auxiliar", "buffer", "compat", "except", "inet", "io", "luasocket", "mime", "options", "select", "tcp", "timeout", "udp", "usocket"]
    return lua_files, [REPO / "build/luasocket" / (s + ".c") for s in sock], [REPO / "build" / f for f in ("i64lib.c", "xlua.c", "memory_leak_checker.c")]


def macho_text(path):
    """Sum of the __TEXT sections of a Mach-O file or of every member of an archive."""
    sz = subprocess.run(["xcrun", "size", "-m", str(path)], capture_output=True, text=True, check=True).stdout
    total = sum(int(x) for x in re.findall(r"Section \(__TEXT, \w+\): (\d+)", sz))
    if not total:  # linked images print "Segment __TEXT:" followed by its sections
        seg = re.search(r"Segment __TEXT: \d+\n((?:\tSection .*\n)+)", sz)
        total = sum(int(x) for x in re.findall(r": (\d+)", seg.group(1))) if seg else 0
    return total


def ios(a):
    """Static libraries like build/make_ios_lua55.sh (Xcode Release, arm64), compiled directly with
    the flags of its Xcode project, then linked with -dead_strip into a stub that references every
    function LuaDLL.cs imports on iOS (what IL2CPP's P/Invoke references keep)."""
    out = a.output.resolve() / "ios"
    out.mkdir(parents=True, exist_ok=True)
    sdk = subprocess.run(["xcrun", "--sdk", "iphoneos", "--show-sdk-path"], capture_output=True, text=True, check=True).stdout.strip()
    clang = subprocess.run(["xcrun", "--sdk", "iphoneos", "--find", "clang"], capture_output=True, text=True, check=True).stdout.strip()
    target = ["-target", "arm64-apple-ios" + a.ios_min, "-isysroot", sdk]
    # The Xcode project's response file: -fpascal-strings -O3 -DLUA_IDSIZE=120 -DLUA_USE_IOS -DNDEBUG -std=gnu17,
    # plus -fstrict-aliasing on the command line.
    base = [clang] + target + ["-fpascal-strings", "-fstrict-aliasing", "-std=gnu17", "-DNDEBUG", "-DLUA_USE_IOS",
                               "-DLUA_IDSIZE=120", "-I", REPO / "build", "-I", REPO / "build" / ("lua-%s/src" % a.lua), "-c"]
    lua_files, sock_files, xlua_files = ios_sources(a.lua)
    exports = managed_imports({"UNITY_IOS"})
    stub = out / "link_test.c"
    stub.write_text("".join("extern void %s(void);\n" % n for n in exports)
                    + "void *const xlua_used[] = {\n" + "".join("    (void *)%s,\n" % n for n in exports) + "};\n"
                    + "int main(int argc, char **argv) { (void)argv; return ((char *)xlua_used[argc])[0]; }\n")
    stub_only = out / "stub_only"
    run([clang] + target + ["-Wl,-dead_strip", "-Wl,-undefined,dynamic_lookup", stub, "-o", stub_only], stderr=subprocess.DEVNULL)
    stub_text = macho_text(stub_only)
    names = a.variants.split(",") if a.variants else list(IOS_VARIANTS)
    results = json.loads((out / "ios-sizes.json").read_text()) if (out / "ios-sizes.json").exists() else {}
    for name in names:
        cflags, hot, lto = IOS_VARIANTS[name][:3]
        hot_files = IOS_VARIANTS[name][3] if len(IOS_VARIANTS[name]) > 3 else HOT_FILES
        vdir = out / name
        if vdir.exists():
            shutil.rmtree(vdir)
        vdir.mkdir()
        objs, procs = [], []
        for f in lua_files + sock_files + xlua_files:
            flags = (hot if hot and f.name in hot_files else cflags).split()
            extra = ["-DLUA_COMPAT_APIINTCASTS", "-DLUA_LIB"] if f in sock_files else []
            obj = vdir / (f.parent.name + "_" + f.stem + ".o")
            objs.append(obj)
            procs.append(subprocess.Popen([str(x) for x in base + flags + (lto.split() if lto else []) + extra + [f, "-o", obj]]))
            if len(procs) >= a.jobs:
                assert procs.pop(0).wait() == 0
        for p in procs:
            assert p.wait() == 0
        lib = vdir / "libxlua.a"
        run(["xcrun", "libtool", "-static", "-o", lib] + objs, stderr=subprocess.DEVNULL)
        exe = vdir / "link_test"
        link = [clang] + target + ["-Wl,-dead_strip", "-Wl,-map," + str(vdir / "link.map")]
        if lto:
            link += ["-O3" if hot else cflags.split()[0]]
        run(link + [stub, lib, "-o", exe])
        entry = {"archive_bytes": lib.stat().st_size, "archive_text": None if lto else macho_text(lib),
                 "linked_text": macho_text(exe) - stub_text, "lto": bool(lto)}
        results[name] = entry
        (out / "ios-sizes.json").write_text(json.dumps(results, indent=1) + "\n")
    base_r = results.get("O3")
    print("%-16s %12s %12s %9s %12s %9s" % ("variant", "archive", "archive TEXT", "delta", "linked TEXT", "delta"))
    for name, e in results.items():
        at = e["archive_text"]
        print("%-16s %12d %12s %9s %12d %9s" % (name, e["archive_bytes"], at if at is not None else "(bitcode)",
              "%+d" % (at - base_r["archive_text"]) if base_r and at is not None else "-", e["linked_text"],
              "%+d" % (e["linked_text"] - base_r["linked_text"]) if base_r else "-"))


# ---------------------------------------------------------------- host

HOST_VARIANTS = ["O3", "O2", "Os", "Oz", "Oz-noout", "hybridn-O3", "hybridnw-O3", "thinlto-O3", "fulllto-O3",
                 "thinlto-hybrid", "fulllto-hybrid", "outliner-O3", "outliner-O2", "hidden", "hybridnw-hidden"]


def host(a):
    """Host shared libraries per compiler variant; with --test, run the native suites on each.

    Only compiler options apply: the linker options of the Android variants are lld-only.
    The suites hold a mkdir lock (--lock) so that they do not run beside another agent's.
    """
    out = a.output.resolve() / "host"
    names = a.variants.split(",") if a.variants else HOST_VARIANTS
    hidden_src = None
    libs = {}
    for name in names:
        v = VARIANTS[name]
        src = REPO / "build"
        if v["hidden"]:
            hidden_src = hidden_src or patched_source(a.output.resolve())
            src = hidden_src
        bdir = out / name
        common = (" -fvisibility=hidden" if v["hidden"] else "") + ({"thin": " -flto=thin", "full": " -flto"}.get(v["lto"], ""))
        ld = "-Wl,-mllvm,-enable-machine-outliner" if (v["lto"] and OUTLINER in v["cflags"]) else ""
        if v["hot"] and v["lto"]:
            ld += " -O3"
        cmd = ["cmake", "-S", src, "-B", bdir, "-G", "Ninja", "-DLUA_VERSION=" + a.lua, "-DCMAKE_BUILD_TYPE=Release",
               "-DCMAKE_C_FLAGS_RELEASE=" + v["cflags"] + " -DNDEBUG", "-DCMAKE_C_FLAGS=" + common.strip(),
               "-DCMAKE_SHARED_LINKER_FLAGS=" + ld.strip(), "-DCMAKE_MODULE_LINKER_FLAGS=" + ld.strip()] + v["cmake"]
        if sys.platform == "darwin":
            cmd += ["-DBUILD_SILICON=ON", "-DCMAKE_OSX_DEPLOYMENT_TARGET=12.0"]
        if v["hot"]:
            cmd.append("-DCMAKE_C_COMPILER_LAUNCHER=%s;%s;cc;%s;%s" % (sys.executable, Path(__file__).resolve(), v["hot"], ",".join(v["hot_files"])))
        lib = bdir / ("libxlua.dylib" if sys.platform == "darwin" else "libxlua.so")
        libs[name] = lib
        if a.test and lib.exists():
            continue  # already built; --test reuses it
        if bdir.exists():
            shutil.rmtree(bdir)
        print("==> host", name, flush=True)
        run(cmd, stdout=subprocess.DEVNULL)
        run(["cmake", "--build", bdir, "--parallel", str(a.jobs)], stdout=subprocess.DEVNULL)
    if not a.test:
        return
    runner = out / "flag_bench"
    run(["cc", "-O2", "-Wall", REPO / "Test/Native/flag_bench.c", "-o", runner])
    lock = Path(a.lock)
    while True:
        try:
            lock.mkdir()
            break
        except FileExistsError:
            print("waiting for", lock, flush=True)
            time.sleep(30)
    results = {}
    try:
        for name, lib in libs.items():
            res = {}
            logs = out / "logs" / name
            logs.mkdir(parents=True, exist_ok=True)
            checks = [("smoke", [runner, lib, "--smoke"]),
                      ("bench_checks", [runner, lib, "--runs", "1", "--warmup", "0", "--scale", "0.2"]),
                      ("check_exports", [sys.executable, REPO / "Test/Native/check_exports.py", lib]),
                      ("native_regression", [sys.executable, REPO / "Test/Native/native_regression.py", lib]),
                      ("run_managed", [sys.executable, REPO / "Test/Native/run_managed.py", "--plugin", lib, "--output", out / "managed" / name]
                       + (["--gen-code"] if a.gen_code else []))]
            for check, cmd in checks:
                print("==> test", name, check, flush=True)
                with open(logs / (check + ".log"), "w") as f:
                    # Own process group, so a timeout kills the suite's mono child too (and nobody else's).
                    proc = subprocess.Popen([str(c) for c in cmd], stdout=f, stderr=subprocess.STDOUT, start_new_session=True)
                    try:
                        res[check] = proc.wait(timeout=a.test_timeout)
                    except subprocess.TimeoutExpired:
                        os.killpg(proc.pid, 9)
                        proc.wait()
                        res[check] = "timeout"
                if check == "bench_checks":
                    text = (logs / (check + ".log")).read_text()
                    res["checks"] = {m["name"]: m["check"] for m in (json.loads(x) for x in re.findall(r"^XLUA_FLAG_BENCH (\{.+\})$", text, re.M))}
            results[name] = res
            print("   ", name, {k: v for k, v in res.items() if k != "checks"}, flush=True)
            (out / "host-tests.json").write_text(json.dumps(results, indent=1) + "\n")
    finally:
        lock.rmdir()
    ref = results.get("O3", {}).get("checks")
    for name, res in results.items():
        same = res.get("checks") == ref
        print("%-16s %s results %s" % (name, " ".join("%s=%s" % (k, v) for k, v in res.items() if k != "checks"),
                                        "same as O3" if same else "DIFFER from O3"))


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "cc":
        cc_launch(sys.argv[2:])
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=["android", "bench", "report", "ios", "host", "profile", "list"])
    p.add_argument("--output", type=Path, required=True, help="work directory (build trees, libraries, results)")
    p.add_argument("--ndk", default=os.environ.get("ANDROID_NDK") or os.environ.get("ANDROID_NDK_HOME"))
    p.add_argument("--lua", default="5.5.1")
    p.add_argument("--abis", default="arm64-v8a", help="comma-separated; bench and profile use the first")
    p.add_argument("--api", type=int, default=21, help="API level of variants that do not set one")
    p.add_argument("--variants", help="comma-separated variant names (default all)")
    p.add_argument("--jobs", type=int, default=4)
    p.add_argument("--adb", default=shutil.which("adb") or "adb")
    p.add_argument("--serial")
    p.add_argument("--device-dir", default="/data/local/tmp/xlua-bench")
    p.add_argument("--rounds", type=int, default=5)
    p.add_argument("--runs", type=int, default=5, help="timed runs per case in one runner process")
    p.add_argument("--warmup", type=int, default=1)
    p.add_argument("--scale", default="1")
    p.add_argument("--filter")
    p.add_argument("--cpus", default="4,5,6,7", help="CPUs the runner pins itself to (the big cluster of the SM6225)")
    p.add_argument("--cooldown", type=float, default=5, help="seconds of idle before each variant run")
    p.add_argument("--max-temp", type=float, default=45, help="wait while the CPU zones are hotter (degrees C)")
    p.add_argument("--thermal-zones", default="7,8,9,10", help="thermal_zone numbers to watch (7-10 are the big cores, 11-13 the CPU subsystem)")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--tag", default="", help="suffix of the bench result file, for separate bench runs")
    p.add_argument("--ios-min", default="15.0")
    p.add_argument("--test", action="store_true", help="host: run the smoke, check_exports, native_regression and run_managed on each variant")
    p.add_argument("--gen-code", action="store_true", help="host --test: also pass --gen-code to run_managed.py")
    p.add_argument("--lock", default="/Volumes/T7Data/ext-ai-projects/xlua-ng/native-test.lock", help="host --test: mkdir lock held while the suites run")
    p.add_argument("--test-timeout", type=int, default=1800, help="host --test: seconds per suite")
    a = p.parse_args()
    if a.command == "list":
        for n, v in VARIANTS.items():
            print("%-16s %s" % (n, v))
        return
    if a.command in ("android", "bench", "profile") and not a.ndk:
        p.error("--ndk or ANDROID_NDK is required")
    {"android": build_android, "bench": bench, "report": report, "ios": ios, "host": host, "profile": profile}[a.command](a)


if __name__ == "__main__":
    main()
