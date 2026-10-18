// Writes the obfuz rule file for assemblies outside Unity, with the code the
// Editor's adapter uses (Assets/XLua/Src/Editor/ObfuzRules.cs), for
// Test/Native/obfuscation_check.py (docs/design/source-generator.md, M8).
//
// Usage: ObfuzRulesTool.exe <rules.xml> [--hotfix] <obfuscated assembly>...
//
// The assemblies are loaded as the Editor has them loaded, and all of them
// count as obfuscated (obfuz's AssemblySettings).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

static class ObfuzRulesTool
{
    static int Main(string[] args)
    {
        bool hotfix = args.Contains("--hotfix");
        var assemblies = args.Skip(1).Where(a => a != "--hotfix").Select(p => Assembly.LoadFrom(Path.GetFullPath(p))).ToList();
        var names = new HashSet<string>(assemblies.Select(a => a.GetName().Name), StringComparer.Ordinal);
        var xlua = new HashSet<string>(assemblies.Where(XLua.ObfuzRules.HasXLuaCode).Select(a => a.GetName().Name), StringComparer.Ordinal);
        string rules = XLua.ObfuzRules.Build(XLua.ObfuzRules.Fragments(assemblies), names, xlua, hotfix);
        File.WriteAllText(args[0], rules);
        Console.Write(rules);
        return 0;
    }
}
