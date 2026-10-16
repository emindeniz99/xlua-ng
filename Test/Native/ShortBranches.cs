// Used by hotfix_check.py: Mono's mcs writes only long branches, while Roslyn
// (and so Unity) writes short ones where they fit. Rewrites the constructors
// of the fixture's LoopN and FwdN types with short forms, keeping the
// assembly's debug symbols. Both modes print each branch of those
// constructors as "BRANCH <type> <opcode> <displacement>".
//
//   ShortBranches.exe <input assembly> <output assembly>
//   ShortBranches.exe --list <assembly>
using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

public static class ShortBranches
{
    public static void Main(string[] args)
    {
        bool list = args[0] == "--list";
        string input = list ? args[1] : args[0];
        bool symbols = !list && (File.Exists(Path.ChangeExtension(input, ".pdb")) || File.Exists(input + ".mdb"));
        using (var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadSymbols = symbols }))
        {
            foreach (var type in assembly.MainModule.Types)
            {
                if (type.Namespace != "HotfixFixture" || !(type.Name.StartsWith("Loop") || type.Name.StartsWith("Fwd")))
                {
                    continue;
                }
                foreach (var method in type.Methods)
                {
                    if (!method.IsConstructor || !method.HasBody) continue;
                    if (!list) method.Body.OptimizeMacros();
                    int offset = 0;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        instruction.Offset = offset;
                        offset += instruction.GetSize();
                    }
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var target = instruction.Operand as Instruction;
                        if (target != null)
                        {
                            Console.WriteLine("BRANCH " + type.Name + " " + instruction.OpCode.Name + " "
                                + (target.Offset - instruction.Offset - instruction.GetSize()));
                        }
                    }
                }
            }
            if (!list)
            {
                assembly.Write(args[1], new WriterParameters { WriteSymbols = symbols });
            }
        }
    }
}
