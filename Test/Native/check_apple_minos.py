"""Fail when an Apple plugin needs a newer OS than xlua-ng supports.

Reads the minimum OS version of every Mach-O image in each file with `otool -l`:
the LC_BUILD_VERSION `minos` (or the older LC_VERSION_MIN_* `version`) of each
architecture of a universal binary and of each object of a static library. A file
fails when any image needs a newer OS than the limit of its platform, has no
version at all, or is built for a platform without a limit.

The limits are the deployment targets of the release build (build/README.md,
"macOS and iOS deployment targets"): macOS 12.0 (build/CMakeLists.txt), iOS and
tvOS 15.0 (build/make_ios_*.sh), visionOS 1.0. Without a deployment target Xcode
and clang build for the OS of the build machine, which Unity players on older
systems cannot load.

usage: check_apple_minos.py [--macos 12.0] [--ios 15.0] [--tvos 15.0] [--visionos 1.0]
                            [--platform macos|ios|...] [--otool PATH] FILE...
--platform also fails an image built for another platform (an iOS library built
for the simulator, a macOS plugin built for Mac Catalyst).
"""
import argparse
import re
import shutil
import subprocess
import sys

# otool prints the platform by name; older versions print the number (mach-o/loader.h).
PLATFORMS = {"1": "MACOS", "2": "IOS", "3": "TVOS", "6": "MACCATALYST", "7": "IOSSIMULATOR",
             "8": "TVOSSIMULATOR", "11": "XROS", "12": "XROSSIMULATOR"}
VERSION_MIN = {"LC_VERSION_MIN_MACOSX": "MACOS", "LC_VERSION_MIN_IPHONEOS": "IOS", "LC_VERSION_MIN_TVOS": "TVOS"}
# Platform -> the option that holds its limit.
LIMIT_OF = {"MACOS": "macos", "IOS": "ios", "IOSSIMULATOR": "ios", "TVOS": "tvos", "TVOSSIMULATOR": "tvos",
            "XROS": "visionos", "XROSSIMULATOR": "visionos"}


def version(text):
    return tuple(int(x) for x in text.split("."))


def images(otool_output):
    """Yield (image name, platform, minos, sdk) for every image in `otool -arch all -l` output."""
    name, cmd, fields, found = None, None, {}, []

    def flush():
        if cmd == "LC_BUILD_VERSION":
            found.append((PLATFORMS.get(fields.get("platform"), fields.get("platform")), fields.get("minos"), fields.get("sdk")))
        elif cmd in VERSION_MIN:
            found.append((VERSION_MIN[cmd], fields.get("version"), fields.get("sdk")))

    for line in otool_output.splitlines():
        if line.startswith("Archive :"):
            continue
        if not line.startswith(" ") and line.endswith(":") and not line.startswith("Load command"):
            flush()
            if name is not None:
                yield name, found
            name, cmd, fields, found = line[:-1], None, {}, []
            continue
        if line.startswith("Load command") or line.startswith("Section"):
            flush()
            cmd, fields = None, {}
            continue
        m = re.match(r"\s+(\w+) (.*)$", line)
        if not m:
            continue
        key, value = m.group(1), m.group(2).strip()
        if key == "cmd":
            cmd = value
        elif cmd:
            fields[key] = value
    flush()
    if name is not None:
        yield name, found


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--macos", default="12.0")
    p.add_argument("--ios", default="15.0")
    p.add_argument("--tvos", default="15.0")
    p.add_argument("--visionos", default="1.0")
    p.add_argument("--platform", help="the platform every image must be built for (macos, ios, tvos, visionos, "
                                      "iossimulator, ...); default any platform with a limit")
    p.add_argument("--otool", default=shutil.which("otool") or shutil.which("llvm-otool") or "otool")
    p.add_argument("files", nargs="+")
    a = p.parse_args()
    problems = []
    for path in a.files:
        out = subprocess.run([a.otool, "-arch", "all", "-l", path], capture_output=True, text=True)
        if out.returncode != 0:
            problems.append("%s: otool failed: %s" % (path, out.stderr.strip()))
            continue
        count, newest = 0, {}
        for name, found in images(out.stdout):
            count += 1
            if not found:
                problems.append("%s: no LC_BUILD_VERSION or LC_VERSION_MIN_* load command" % name)
                continue
            for platform, minos, sdk in found:
                if a.platform and platform != a.platform.upper():
                    problems.append("%s: built for %s, expected %s" % (name, platform, a.platform.upper()))
                    continue
                limit_name = LIMIT_OF.get(platform)
                if not limit_name:
                    problems.append("%s: platform %s has no limit" % (name, platform))
                    continue
                limit = getattr(a, limit_name)
                if version(minos) > version(limit):
                    problems.append("%s: %s minos %s is above %s (sdk %s)" % (name, platform, minos, limit, sdk))
                if platform not in newest or version(minos) > version(newest[platform]):
                    newest[platform] = minos
        if count == 0:
            problems.append("%s: no Mach-O image found" % path)
        print("APPLE_MINOS: %s: %d images, newest minos %s" % (
            path, count, ", ".join("%s %s" % kv for kv in sorted(newest.items())) or "none"))
    if problems:
        print("\n".join(problems), file=sys.stderr)
        sys.exit(1)
    print("APPLE_MINOS_PASS")


if __name__ == "__main__":
    main()
