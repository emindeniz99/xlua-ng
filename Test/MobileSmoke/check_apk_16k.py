"""Check that an Android APK or app bundle runs on 16 KB page devices without page size compat mode.

    check_apk_16k.py [--unity-libs-ok] APK_OR_AAB...

Android 16 decides at install time whether an app runs in page size compatibility mode, and shows
"This app isn't 16 KB compatible" when it starts it (frameworks/base, android16-release:
core/jni/com_android_internal_content_NativeLibraryHelper.cpp, checkLoadSegmentAlignment and
checkAlignment). For every native library of the device's 64-bit ABI it checks:

- ELF alignment: a PT_LOAD segment with p_align 0x1000 sets PAGE_SIZE_APP_COMPAT_FLAG_ELF_NOT_ALIGNED
  ("ELF alignment check failed"). This script fails any PT_LOAD alignment below 0x4000, and offsets
  that are not congruent to their addresses modulo 16 KB, which the loader needs as well.
- APK alignment: with extractNativeLibs=false the libraries are mapped from the APK, so a stored
  library whose data does not start on a 16 KB boundary sets
  PAGE_SIZE_APP_COMPAT_FLAG_UNCOMPRESSED_LIBS_NOT_ALIGNED ("APK alignment check failed"). This script
  fails every stored library that is not 16 KB aligned in the zip (zipalign -P 16), whatever the
  manifest says; Unity's APKs compress their libraries and extract them (extractNativeLibs=true).

Every library under lib/<abi>/ (APK) or base/lib/<abi>/ (app bundle) of a 64-bit ABI is checked; the
32-bit ones are listed but do not fail, as Android 16 skips them. --unity-libs-ok reports a failing
library of Unity's own (UNITY_LIBS: Unity 2021.3.45f2's libunity.so, libmain.so and the libil2cpp.so
it links are aligned to 4 KB) as a note instead of a failure, so a check of an old editor's player fails only on the
project's own plugins, such as libxlua.so.

Not checked: what the device remembers. Android 16 ORs the flags of every install of a package into
its PackageSetting (PackageSetting.setPageSizeAppCompatFlags), so an update keeps the compat mode and
the dialog of an earlier 4 KB build until the app is uninstalled; `adb shell dumpsys package PKG`
prints the flags as pageSizeCompat (4: ELF, 2: APK alignment). Test/MobileSmoke/README.md,
"Android emulator with 16 KB pages".

Exit status 1 when an APK fails. Run with python3 -I on downloaded files.
"""
import argparse
from pathlib import Path
import struct
import sys
import zipfile

PAGE = 0x4000
ABIS_64 = {"arm64-v8a", "x86_64", "riscv64"}
# Libraries a Unity player brings itself (Unity 2021.3 to 6000.6, IL2CPP).
UNITY_LIBS = {"libunity.so", "libmain.so", "libil2cpp.so", "libc++_shared.so", "libgame.so",
              "libswappywrapper.so", "libmonobdwgc-2.0.so", "libMonoPosixHelper.so"}


def load_segments(data):
    """[(p_offset, p_vaddr, p_align)] of the PT_LOAD segments, or None if data is not an ELF file."""
    if data[:4] != b"\x7fELF" or data[4] not in (1, 2) or data[5] not in (1, 2):
        return None
    end = "<" if data[5] == 1 else ">"
    segments = []
    if data[4] == 2:
        phoff, = struct.unpack_from(end + "Q", data, 0x20)
        phentsize, phnum = struct.unpack_from(end + "HH", data, 0x36)
        for i in range(phnum):
            p_type, _, p_offset, p_vaddr, _, _, _, p_align = struct.unpack_from(end + "IIQQQQQQ", data,
                                                                                phoff + i * phentsize)
            if p_type == 1:
                segments.append((p_offset, p_vaddr, p_align))
    else:
        phoff, = struct.unpack_from(end + "I", data, 0x1c)
        phentsize, phnum = struct.unpack_from(end + "HH", data, 0x2a)
        for i in range(phnum):
            p_type, p_offset, p_vaddr, _, _, _, _, p_align = struct.unpack_from(end + "IIIIIIII", data,
                                                                                phoff + i * phentsize)
            if p_type == 1:
                segments.append((p_offset, p_vaddr, p_align))
    return segments


def data_offset(path, info):
    """Where the entry's data starts in the zip file: after its local header, name and extra field."""
    with open(path, "rb") as f:
        f.seek(info.header_offset)
        header = f.read(30)
    name_len, extra_len = struct.unpack_from("<HH", header, 26)
    return info.header_offset + 30 + name_len + extra_len


def check(path, unity_libs_ok=False):
    """(rows, problems, notes): a row per native library, and what fails."""
    rows, problems, notes = [], [], []
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            parts = info.filename.split("/")
            if parts[0] == "base":
                parts = parts[1:]
            if len(parts) != 3 or parts[0] != "lib" or not parts[2].endswith(".so"):
                continue
            abi, name = parts[1], parts[2]
            with z.open(info) as f:
                head = f.read(65536)
            try:
                segments = load_segments(head)
            except struct.error:  # program headers past the first 64 KB
                segments = load_segments(z.read(info))
            if segments is None:
                continue
            stored = info.compress_type == zipfile.ZIP_STORED
            offset = data_offset(path, info)
            found = []
            aligns = sorted({a for _, _, a in segments})
            if not segments:
                found.append("no PT_LOAD segment")
            elif min(aligns) < PAGE:
                found.append("PT_LOAD alignment %s below 0x4000 (ELF alignment)" % ", ".join(hex(a) for a in aligns))
            elif any((o - v) % PAGE for o, v, _ in segments):
                found.append("PT_LOAD offsets not congruent to their addresses modulo 16 KB")
            if stored and offset % PAGE:
                found.append("stored at zip offset %d, not 16 KB aligned (APK alignment; zipalign -P 16)" % offset)
            rows.append((info.filename, "stored" if stored else "deflated", offset % PAGE,
                         ", ".join(hex(a) for a in aligns), found))
            for problem in found:
                line = "%s: %s" % (info.filename, problem)
                if abi not in ABIS_64:
                    notes.append(line + " (32-bit, which Android 16 does not check)")
                elif unity_libs_ok and name in UNITY_LIBS:
                    notes.append(line + " (Unity's own library)")
                else:
                    problems.append(line)
    return rows, problems, notes


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--unity-libs-ok", action="store_true",
                   help="report failures of Unity's own libraries (UNITY_LIBS) as notes")
    p.add_argument("apks", nargs="+", type=Path)
    a = p.parse_args()
    failed = False
    for apk in a.apks:
        rows, problems, notes = check(apk, a.unity_libs_ok)
        for name, storage, mod, aligns, found in rows:
            print("  %-48s %-8s zip offset mod 16 KB %5d, PT_LOAD align %s%s"
                  % (name, storage, mod, aligns, "" if not found else "  <- " + "; ".join(found)))
        for note in notes:
            print("  note:", note)
        for problem in problems:
            print("  -", problem)
        print("XLUA_APK_16K_%s: %s, %d native libraries, %d problem(s)%s"
              % ("FAIL" if problems else "PASS", apk, len(rows), len(problems),
                 ", %d note(s)" % len(notes) if notes else ""))
        failed |= bool(problems) or not rows
        if not rows:
            print("  - no native library under lib/<abi>/")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
