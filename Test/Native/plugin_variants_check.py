#!/usr/bin/env python3
"""Test the native plugin variant rules of Assets/XLua/Src/Editor/PluginVariants.cs
without Unity.

The file's selection logic (class XLua.PluginVariants) compiles without UnityEditor
when XLUA_PLUGIN_VARIANTS_TEST is defined. This script compiles it with mcs together
with the checks below and runs them with mono. The Unity side (the include-in-build
delegate, the build callbacks and the menu items) is compiled by
editor_compile_check.py and needs a Unity build to run.

    python3 Test/Native/plugin_variants_check.py --output /tmp/pvc
"""
import argparse
from pathlib import Path
import subprocess

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "Assets/XLua/Src/Editor/PluginVariants.cs"

CHECKS = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using XLua;

static class PluginVariantsCheck
{
    static int failures;

    static void Expect(bool ok, string what)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL: " + what); }
    }

    static void Parse(string path, bool ok, string variant, string key, bool problem)
    {
        string v, k, p;
        bool got = PluginVariants.TryParse(path, out v, out k, out p);
        Expect(got == ok && v == variant && k == key && (p != null) == problem,
            "TryParse(" + path + ") = " + got + ", " + (v ?? "null") + ", " + (k ?? "null") + ", " + (p ?? "no problem"));
    }

    static string Pick(string[] paths, string[] defines, bool development)
    {
        var problems = new List<string>();
        var groups = PluginVariants.Groups(paths, problems);
        Expect(problems.Count == 0, "problems: " + string.Join("; ", problems.ToArray()));
        string error;
        var preference = PluginVariants.Preference(defines, development, out error);
        Expect(error == null, "error: " + error);
        return string.Join(",", groups.OrderBy(g => g.Key).Select(g => PluginVariants.Choose(g.Value, preference) ?? "none").ToArray());
    }

    static int Main()
    {
        const string arm64 = "Assets/Plugins/Android/libs/arm64-v8a/libxlua.so";
        const string arm64Dev = "Assets/Plugins/Android/xlua-variants/development/libs/arm64-v8a/libxlua.so";
        Parse(arm64, true, null, arm64, false);
        Parse(arm64Dev, true, "development", arm64, false);
        Parse("Assets/Plugins/iOS/xlua-variants/nosocket/libxlua.a", true, "nosocket", "Assets/Plugins/iOS/libxlua.a", false);
        Parse("Assets/Plugins/xlua-variants/dev2/xlua.bundle", true, "dev2", "Assets/Plugins/xlua.bundle", false);
        Parse("Assets\\Plugins\\x86_64\\xlua-variants\\core\\xlua.dll", true, "core", "Assets/Plugins/x86_64/xlua.dll", false);
        Parse("Assets/Plugins/Android/libs/arm64-v8a/libother.so", false, null, null, false);
        Parse("Assets/Plugins/WebGL/xlua_webgl.cpp", false, null, null, false);
        Parse("Assets/Plugins/iOS/xlua-variants/NoSocket/libxlua.a", false, null, null, true);
        Parse("Assets/Plugins/iOS/xlua-variants/a/xlua-variants/b/libxlua.a", false, null, null, true);
        Parse("Assets/Plugins/iOS/xlua-variants/libxlua.a", false, null, null, true);
        Parse("Assets/Plugins/iOS/xlua-variants/1st/libxlua.a", false, null, null, true);

        string error;
        var order = PluginVariants.Preference(new[] { "UNITY_POST_PROCESSING", "XLUA_PLUGIN_NOSOCKET", "XLUA_CORE_ONLY" }, true, out error);
        Expect(error == null && string.Join(",", order.Select(o => o ?? "default").ToArray()) == "nosocket,core,development,default",
            "preference: " + string.Join(",", order.Select(o => o ?? "default").ToArray()));
        order = PluginVariants.Preference(new string[0], false, out error);
        Expect(error == null && order.Count == 1 && order[0] == null, "no defines: default only");
        order = PluginVariants.Preference(new[] { "XLUA_PLUGIN_A", "XLUA_PLUGIN_B" }, false, out error);
        Expect(error != null && error.Contains("XLUA_PLUGIN_A") && error.Contains("XLUA_PLUGIN_B") && order.Count == 1, "two defines: " + error);
        order = PluginVariants.Preference(new[] { "XLUA_PLUGIN_" }, true, out error);
        Expect(error != null && order.Count == 2 && order[0] == "development", "empty variant name: " + error);
        order = PluginVariants.Preference(new[] { " XLUA_PLUGIN_DEVELOPMENT ", "" }, true, out error);
        Expect(error == null && order.Count == 2 && order[0] == "development", "variant define equal to the development variant");

        string[] android = {
            arm64, arm64Dev,
            "Assets/Plugins/Android/libs/armeabi-v7a/libxlua.so",
            "Assets/Plugins/Android/xlua-variants/nosocket/libs/armeabi-v7a/libxlua.so",
        };
        Expect(Pick(android, new string[0], false) == arm64 + ",Assets/Plugins/Android/libs/armeabi-v7a/libxlua.so", "release: defaults");
        Expect(Pick(android, new string[0], true) == arm64Dev + ",Assets/Plugins/Android/libs/armeabi-v7a/libxlua.so",
            "development build: development where it exists, else default: " + Pick(android, new string[0], true));
        Expect(Pick(android, new[] { "XLUA_PLUGIN_NOSOCKET" }, true)
            == arm64Dev + ",Assets/Plugins/Android/xlua-variants/nosocket/libs/armeabi-v7a/libxlua.so",
            "define first, then development: " + Pick(android, new[] { "XLUA_PLUGIN_NOSOCKET" }, true));
        // Only variants, no default plugin: nothing for a release build.
        string[] onlyVariant = { "Assets/Plugins/iOS/xlua-variants/development/libxlua.a" };
        Expect(Pick(onlyVariant, new string[0], false) == "none", "no default: none");
        Expect(Pick(onlyVariant, new string[0], true) == "Assets/Plugins/iOS/xlua-variants/development/libxlua.a", "no default, development");

        // Presets: XLUA_PRESET_MINIMAL picks "minimal" after a named variant, core and development.
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL" }, false, out error);
        Expect(error == null && string.Join(",", order.Select(o => o ?? "default").ToArray()) == "minimal,default",
            "minimal preset: " + string.Join(",", order.Select(o => o ?? "default").ToArray()));
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL", "XLUA_PLUGIN_NOSOCKET", "XLUA_CORE_ONLY" }, true, out error);
        Expect(error == null && string.Join(",", order.Select(o => o ?? "default").ToArray()) == "nosocket,core,development,minimal,default",
            "minimal preset after the others: " + string.Join(",", order.Select(o => o ?? "default").ToArray()));
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL", "XLUA_PLUGIN_MINIMAL" }, false, out error);
        Expect(error == null && order.Count == 2 && order[0] == "minimal", "preset and the same named variant");
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_LEGACY" }, true, out error);
        Expect(error == null && order.Count == 2 && order[0] == "development", "legacy preset define: the default plugin");
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_CORE" }, false, out error);
        Expect(error != null && error.Contains("XLUA_PRESET_CORE") && order.Count == 1, "unknown preset: " + error);
        order = PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL", "XLUA_PRESET_LEGACY" }, false, out error);
        Expect(error != null && error.Contains("more than one preset") && order.Count == 1, "two presets: " + error);
        string[] withMinimal = {
            arm64, arm64Dev,
            "Assets/Plugins/Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so",
            "Assets/Plugins/iOS/libxlua.a",
            "Assets/Plugins/iOS/xlua-variants/minimal/libxlua.a",
        };
        Expect(Pick(withMinimal, new[] { "XLUA_PRESET_MINIMAL" }, false)
            == "Assets/Plugins/Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so,Assets/Plugins/iOS/xlua-variants/minimal/libxlua.a",
            "minimal release build: " + Pick(withMinimal, new[] { "XLUA_PRESET_MINIMAL" }, false));
        Expect(Pick(withMinimal, new[] { "XLUA_PRESET_MINIMAL" }, true)
            == arm64Dev + ",Assets/Plugins/iOS/xlua-variants/minimal/libxlua.a",
            "minimal development build: development where it exists: " + Pick(withMinimal, new[] { "XLUA_PRESET_MINIMAL" }, true));
        Expect(Pick(withMinimal, new string[0], false) == arm64 + ",Assets/Plugins/iOS/libxlua.a", "legacy release build: defaults");

        var problems = new List<string>();
        var groups = PluginVariants.Groups(new[] { arm64, arm64, "Assets/Plugins/x/xlua-variants/Bad/xlua.dll" }, problems);
        Expect(groups.Count == 1 && groups[arm64].Count == 1 && problems.Count == 2, "duplicates and bad names: " + string.Join("; ", problems.ToArray()));

        Console.WriteLine(failures == 0 ? "XLUA_PLUGIN_VARIANTS_PASS" : failures + " failures");
        return failures == 0 ? 0 : 1;
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mono", default="mono")
    parser.add_argument("--compiler", default="mcs")
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    checks = out / "PluginVariantsCheck.cs"
    checks.write_text(CHECKS)
    exe = out / "PluginVariantsCheck.exe"
    subprocess.run([args.compiler, "-define:XLUA_PLUGIN_VARIANTS_TEST", "-warnaserror+", "-out:" + str(exe),
                    str(SOURCE), str(checks)], check=True)
    subprocess.run([args.mono, str(exe)], check=True, timeout=120)


if __name__ == "__main__":
    main()
