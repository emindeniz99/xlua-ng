// Helpers for the memory diagnostics cases in luaCallCs.lua (CaseMemory*). Each check returns ""
// when it passes and a description of the failure otherwise, so that the Lua case can assert it.
// Without the memory diagnostics (no UNITY_EDITOR, DEVELOPMENT_BUILD or XLUA_MEMORY_DIAGNOSTICS)
// only Enabled and HasSnapshotImport exist, and the cases check that nothing was compiled in.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using XLua;

public static class MemoryDiagnosticsHelpers
{
    public static bool Enabled()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_MEMORY_DIAGNOSTICS
        return true;
#else
        return false;
#endif
    }

    // Whether LuaDLL declares the native snapshot import (compiled out with the C# side).
    public static bool HasSnapshotImport()
    {
        return typeof(XLua.LuaDLL.Lua).GetMethod("xlua_memory_snapshot", BindingFlags.Public | BindingFlags.Static) != null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_MEMORY_DIAGNOSTICS
    static LuaEnv Env
    {
        get { return LuaEnvSingleton.Instance; }
    }

    public static object Take()
    {
        return Env.TakeMemorySnapshot();
    }

    // The Lua path of the StringBuilder with this text among the C# objects Lua holds.
    public static string PathOfHeldStringBuilder(string text)
    {
        var snapshot = Env.TakeMemorySnapshot();
        foreach (var c in snapshot.CSharpObjects)
        {
            var sb = c.Target as StringBuilder;
            if (sb != null && sb.ToString() == text)
            {
                if (c.LuaObject == null || !c.LuaObject.Is(LuaMemoryObjectFlags.CSharpObject) || c.LuaObject.CSharpObjectIndex != c.Index)
                {
                    return "no userdata for pool index " + c.Index;
                }
                return c.Path;
            }
        }
        return "not held";
    }

    static List<string> Paths<T>(IEnumerable<T> items, Func<T, string> path)
    {
        var result = new List<string>();
        foreach (var item in items) result.Add(path(item));
        return result;
    }

    // CaseMemorySnapshotDiff: between the snapshots, memtest_grow got 10 entries and memtest_leak
    // was created with 50 tables and three StringBuilders.
    public static string CheckDiff(object before, object after)
    {
        var diff = LuaMemoryDiagnostics.Diff((LuaMemorySnapshot)before, (LuaMemorySnapshot)after);
        var problems = new StringBuilder();
        bool grown = false;
        foreach (var g in diff.GrownTables)
        {
            if (g.After.Path == "_G.memtest_grow" && g.CountDelta == 10) grown = true;
        }
        if (!grown) problems.Append("memtest_grow not among the grown tables: ").Append(string.Join(", ", Paths(diff.GrownTables, g => g.ToString()).ToArray())).Append("; ");
        int leakTables = 0;
        bool leakRoot = false;
        foreach (var o in diff.Added)
        {
            string path = o.Path;
            if (path == "_G.memtest_leak") leakRoot = true;
            if (o.Type == LuaMemoryObjectType.Table && path.StartsWith("_G.memtest_leak[", StringComparison.Ordinal)) leakTables++;
        }
        if (!leakRoot || leakTables != 50) problems.Append("added: memtest_leak ").Append(leakRoot).Append(", its tables ").Append(leakTables).Append("; ");
        var held = new List<string>();
        foreach (var c in diff.AddedCSharpObjects)
        {
            var sb = c.Target as StringBuilder;
            if (sb != null && sb.ToString().StartsWith("memtest ", StringComparison.Ordinal)) held.Add(c.Path);
        }
        held.Sort(StringComparer.Ordinal);
        string expected = "_G.memtest_leak.objects[1], _G.memtest_leak.objects[2], _G.memtest_leak.objects[3]";
        if (string.Join(", ", held.ToArray()) != expected) problems.Append("added C# objects: ").Append(string.Join(", ", held.ToArray())).Append("; ");
        string report = diff.Report(1000);
        if (report.IndexOf("_G.memtest_grow", StringComparison.Ordinal) < 0 || report.IndexOf("System.Text.StringBuilder", StringComparison.Ordinal) < 0)
        {
            problems.Append("report: ").Append(report);
        }
        return problems.ToString();
    }

    // After memtest_leak is gone: its tables and the three StringBuilders are released. The C#
    // collector may have collected the StringBuilders since, so they are matched by the
    // description the earlier snapshot recorded.
    public static string CheckReleased(object earlier)
    {
        var diff = LuaMemoryDiagnostics.Diff((LuaMemorySnapshot)earlier, Env.TakeMemorySnapshot());
        int released = 0, tables = 0;
        foreach (var c in diff.RemovedCSharpObjects)
        {
            if (c.Type == typeof(StringBuilder) && c.Description != null
                && c.Description.StartsWith("memtest ", StringComparison.Ordinal)) released++;
        }
        foreach (var o in diff.Removed)
        {
            if (o.Type == LuaMemoryObjectType.Table) tables++;
        }
        return released == 3 && tables >= 51 ? "" : "released C# objects " + released + ", removed tables " + tables;
    }

    // Called from a __gc finalizer while the collector runs, so no full collection of its own.
    public static int CountObjectsWithoutGc()
    {
        var options = new LuaMemorySnapshotOptions();
        options.FullGc = false;
        return Env.TakeMemorySnapshot(options).Objects.Count;
    }

    // A LuaFunction that C# holds is a registry reference to that function; Dispose releases it.
    public static string CheckRegistryReference()
    {
        var fn = (LuaFunction)Env.DoString("return function() return 'memtest' end", "memtest_registry_chunk")[0];
        string found = null;
        int reference = -1;
        try
        {
            foreach (var r in Env.TakeMemorySnapshot().RegistryReferences)
            {
                if (r.Object.Type == LuaMemoryObjectType.Function && r.Object.Source != null
                    && r.Object.Source.IndexOf("memtest_registry_chunk", StringComparison.Ordinal) >= 0)
                {
                    found = r.Holder ?? "LuaBase";
                    reference = r.Reference;
                }
            }
        }
        finally
        {
            fn.Dispose();
        }
        if (found != "LuaBase") return "the LuaFunction's reference: " + (found ?? "not found");
        foreach (var r in Env.TakeMemorySnapshot().RegistryReferences)
        {
            if (r.Reference == reference && r.Object.Source != null && r.Object.Source.IndexOf("memtest_registry_chunk", StringComparison.Ordinal) >= 0)
            {
                return "registry[" + reference + "] still holds the function after Dispose";
            }
        }
        return "";
    }
#endif
}
