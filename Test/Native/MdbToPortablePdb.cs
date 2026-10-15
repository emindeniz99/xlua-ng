// Used by hotfix_check.py: Mono's mcs writes .mdb symbols only, while Roslyn
// (and so Unity) writes portable PDBs. Copies an assembly and converts its .mdb
// to a portable .pdb with Mono.Cecil.
//
//   MdbToPortablePdb.exe <input assembly with .mdb> <output assembly>
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;

public static class MdbToPortablePdb
{
    public static void Main(string[] args)
    {
        var reader = new ReaderParameters { ReadSymbols = true, SymbolReaderProvider = new MdbReaderProvider() };
        using (var assembly = AssemblyDefinition.ReadAssembly(args[0], reader))
        {
            assembly.Write(args[1], new WriterParameters
            {
                WriteSymbols = true,
                SymbolWriterProvider = new PortablePdbWriterProvider(),
            });
        }
    }
}
