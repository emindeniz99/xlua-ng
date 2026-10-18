// Used by coverage_cs.py: Mono's coverage profiler lists only the methods that
// were compiled, so a method that never ran is missing from its report. This
// tool reads an assembly's debug symbols and prints every sequence point of
// every method, so the never-run methods count as uncovered. Symbols are the
// .mdb that mcs -debug writes (read with Mono.CompilerServices.SymbolWriter,
// part of Mono) or, for what the .NET SDK's csc compiles (the source generator
// passes, -debug:portable), the portable .pdb, read with Mono.Cecil: the
// pinned, hash-checked copy that Tools/build_tools.py puts next to the tools
// (Mono's own System.Reflection.Metadata is missing from some Linux packages).
//
//   CoverageLines.exe <assembly with .mdb or portable .pdb>
// prints one line per sequence point:
//   method token <TAB> IL offset <TAB> line <TAB> file <TAB> Type::Method
using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.CompilerServices.SymbolWriter;

public static class CoverageLines
{
    // The hidden line of both formats (a sequence point without source).
    const int HiddenLine = 0xfeefee;

    public static int Main(string[] args)
    {
        Module module = null;
        try { module = Assembly.LoadFrom(args[0]).ManifestModule; }
        catch (Exception) { }
        string mdb = args[0] + ".mdb";
        string pdb = Path.ChangeExtension(args[0], ".pdb");
        using (var output = new StreamWriter(Console.OpenStandardOutput()))
        {
            if (File.Exists(mdb))
            {
                Mdb(mdb, module, output);
            }
            else if (File.Exists(pdb))
            {
                PortablePdb(args[0], module, output);
            }
            else
            {
                Console.Error.WriteLine("CoverageLines: neither " + mdb + " nor " + pdb + " exists");
                return 1;
            }
        }
        return 0;
    }

    // Type::Method as System.Reflection names it, so both formats give the same names.
    static string Name(Module module, int token)
    {
        try
        {
            var info = module == null ? null : module.ResolveMethod(token);
            if (info != null) return info.DeclaringType.FullName + "::" + info.Name;
        }
        catch (Exception) { }
        return "?";
    }

    static void Mdb(string path, Module module, TextWriter output)
    {
        using (var symbols = MonoSymbolFile.ReadSymbolFile(path))
        {
            foreach (var method in symbols.Methods)
            {
                string name = Name(module, method.Token);
                foreach (var point in method.GetLineNumberTable().LineNumbers)
                {
                    if (point.IsHidden || point.Row <= 0 || point.Row == HiddenLine) continue;
                    var file = symbols.GetSourceFile(point.File);
                    if (file == null) continue;
                    output.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}", method.Token, point.Offset, point.Row, file.FileName, name);
                }
            }
        }
    }

    static void PortablePdb(string assemblyPath, Module module, TextWriter output)
    {
        var parameters = new ReaderParameters { ReadSymbols = true, SymbolReaderProvider = new PortablePdbReaderProvider() };
        using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, parameters))
        {
            foreach (var type in assembly.MainModule.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody || !method.DebugInformation.HasSequencePoints) continue;
                    int token = method.MetadataToken.ToInt32();
                    string name = Name(module, token);
                    foreach (var point in method.DebugInformation.SequencePoints)
                    {
                        if (point.IsHidden || point.StartLine <= 0 || point.StartLine == HiddenLine || point.Document == null) continue;
                        output.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}", token, point.Offset, point.StartLine, point.Document.Url, name);
                    }
                }
            }
        }
    }
}
