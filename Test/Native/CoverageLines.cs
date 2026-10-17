// Used by coverage_cs.py: Mono's coverage profiler lists only the methods that
// were compiled, so a method that never ran is missing from its report. This
// tool reads an assembly's .mdb (written by mcs -debug) and prints every
// sequence point of every method, so the never-run methods count as uncovered.
// Mono.CompilerServices.SymbolWriter ships with Mono; nothing is downloaded.
//
//   CoverageLines.exe <assembly with .mdb>
// prints one line per sequence point:
//   method token <TAB> IL offset <TAB> line <TAB> file <TAB> Type::Method
using System;
using System.IO;
using System.Reflection;
using Mono.CompilerServices.SymbolWriter;

public static class CoverageLines
{
    public static void Main(string[] args)
    {
        Module module = null;
        try { module = Assembly.LoadFrom(args[0]).ManifestModule; }
        catch (Exception) { }
        using (var output = new StreamWriter(Console.OpenStandardOutput()))
        using (var symbols = MonoSymbolFile.ReadSymbolFile(args[0] + ".mdb"))
        {
            foreach (var method in symbols.Methods)
            {
                string name = "?";
                try
                {
                    var info = module == null ? null : module.ResolveMethod(method.Token);
                    if (info != null) name = info.DeclaringType.FullName + "::" + info.Name;
                }
                catch (Exception) { }
                foreach (var point in method.GetLineNumberTable().LineNumbers)
                {
                    if (point.IsHidden || point.Row <= 0 || point.Row == 0xfeefee) continue;
                    var file = symbols.GetSourceFile(point.File);
                    if (file == null) continue;
                    output.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}", method.Token, point.Offset, point.Row, file.FileName, name);
                }
            }
        }
    }
}
