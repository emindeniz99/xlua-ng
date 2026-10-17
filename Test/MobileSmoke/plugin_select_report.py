"""Which libxlua.so each baseline+select / baseline+constraints APK of size_report.py contains.

The two candidates differ in size (Lua 5.5 "full" and Lua 5.3 "minimal"), so the size of
lib/arm64-v8a/libxlua.so in the APK names the one that was included. Prints the build settings line,
what PluginVariantSelector.cs logged, and the build error of a failed build.
"""
import argparse
from pathlib import Path
import re
import zipfile


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--work", required=True, type=Path)
    p.add_argument("--full", type=int, required=True, help="uncompressed size of the full libxlua.so")
    p.add_argument("--minimal", type=int, required=True, help="uncompressed size of the minimal libxlua.so")
    a = p.parse_args()
    names = {a.full: "full (Lua 5.5)", a.minimal: "minimal (Lua 5.3)"}
    print("| variant | config | development | defines | libxlua.so in the APK | selector log |")
    print("| --- | --- | --- | --- | --- | --- |")
    for d in sorted((a.work / "players/android").glob("select-*/baseline+*")):
        log = (d / "unity.log").read_text(errors="replace") if (d / "unity.log").exists() else ""
        settings = re.search(r"XLUA_NG_BUILD_SETTINGS: .*", log)
        release = re.search(r"release=(\w+)", settings.group(0)).group(1) if settings else "?"
        defines = re.search(r"defines=(\S*)", settings.group(0)).group(1) if settings else "?"
        apk = d / "unity-android-smoke.apk"
        if apk.is_file():
            with zipfile.ZipFile(apk) as z:
                libs = [i.file_size for i in z.infolist() if i.filename.endswith("/libxlua.so")]
            got = ", ".join(names.get(n, "%d bytes" % n) for n in libs) or "none"
        else:
            err = re.search(r"Exception: (Cannot include plugin .*?\))\.", log) or re.search(r"Exception: (.*)", log)
            got = "build failed: " + (err.group(1)[:160] if err else "see unity.log")
        sel = sorted(set(re.findall(r"XLUA_PLUGIN_SELECT: (\S+) include=(\w+)", log)))
        sel_text = "; ".join("%s=%s" % (Path(path).parent.name, inc) for path, inc in sel) or "-"
        print("| %s | %s | %s | %s | %s | %s |" % (d.name, d.parent.name, "no" if release == "True" else "yes",
                                                  defines, got, sel_text))


if __name__ == "__main__":
    main()
