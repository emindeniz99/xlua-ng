// Used by hotfix_check.py: Mono's mcs writes only long branches, while Roslyn
// (and so Unity) writes short ones where they fit. Rewrites the constructors
// of the fixture's LoopN and FwdN types with short forms, keeping the
// assembly's debug symbols. Both modes print each branch of those
// constructors as "BRANCH <type> <opcode> <displacement>".
//
// --check checks the IL of every method of the types in the HotfixFixture
// namespace as a verifier would, the rules that Mono's JIT does not enforce
// but IL2CPP needs (Tencent/xLua#1064; ECMA-335 partition I 12.4.2.8 and
// partition III 3.46, 3.47, 3.56): every short branch fits; ret only outside
// protected regions and handlers; a branch stays in its region (or enters a
// try block at its first instruction); leave only to the same region or an
// enclosing one; no try block, handler or filter ends with an instruction
// that falls through. It prints "PROBLEM <method>: <what>" for each problem
// and exits 1 if there is one.
//
//   ShortBranches.exe <input assembly> <output assembly>
//   ShortBranches.exe --list <assembly>
//   ShortBranches.exe --check <assembly>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

public static class ShortBranches
{
    public static int Main(string[] args)
    {
        if (args[0] == "--check")
        {
            return Check(args[1]);
        }
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
                    ComputeOffsets(method.Body);
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
        return 0;
    }

    static void ComputeOffsets(MethodBody body)
    {
        int offset = 0;
        foreach (var instruction in body.Instructions)
        {
            instruction.Offset = offset;
            offset += instruction.GetSize();
        }
    }

    class Region
    {
        public string Name;
        public int Start, End; // instruction indices, End exclusive
        public bool IsTry;
    }

    static int Check(string path)
    {
        int methods = 0, problems = 0;
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
        {
            var types = assembly.MainModule.GetTypes().Where(t => t.Namespace == "HotfixFixture"
                || (t.DeclaringType != null && t.DeclaringType.Namespace == "HotfixFixture"));
            foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody))
            {
                methods++;
                foreach (var problem in CheckBody(method.Body))
                {
                    problems++;
                    Console.WriteLine("PROBLEM " + method.FullName + ": " + problem);
                }
            }
        }
        Console.WriteLine("CHECKED " + methods + " methods, " + problems + " problems");
        return problems == 0 ? 0 : 1;
    }

    static IEnumerable<string> CheckBody(MethodBody body)
    {
        var instructions = body.Instructions;
        ComputeOffsets(body);
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < instructions.Count; i++) index[instructions[i]] = i;
        Func<Instruction, int> at = x => x == null ? instructions.Count : (index.ContainsKey(x) ? index[x] : -1);

        var regions = new List<Region>();
        for (int h = 0; h < body.ExceptionHandlers.Count; h++)
        {
            var handler = body.ExceptionHandlers[h];
            var bounds = new[] { handler.TryStart, handler.TryEnd, handler.HandlerStart, handler.HandlerEnd, handler.FilterStart };
            if (bounds.Any(b => b != null && at(b) < 0))
            {
                yield return "handler " + h + " has a boundary that is not in the body";
                yield break;
            }
            regions.Add(new Region { Name = "try " + h, Start = at(handler.TryStart), End = at(handler.TryEnd), IsTry = true });
            regions.Add(new Region { Name = handler.HandlerType.ToString().ToLowerInvariant() + " " + h,
                                     Start = at(handler.HandlerStart), End = at(handler.HandlerEnd) });
            if (handler.HandlerType == ExceptionHandlerType.Filter)
            {
                regions.Add(new Region { Name = "filter " + h, Start = at(handler.FilterStart), End = at(handler.HandlerStart) });
            }
        }
        Func<int, HashSet<Region>> inside = i => new HashSet<Region>(regions.Where(r => r.Start <= i && i < r.End));

        foreach (var region in regions)
        {
            if (region.End <= region.Start)
            {
                yield return region.Name + " is empty";
                continue;
            }
            var last = instructions[region.End - 1];
            var flow = last.OpCode.FlowControl;
            if (flow != FlowControl.Branch && flow != FlowControl.Return && flow != FlowControl.Throw)
            {
                yield return region.Name + " ends with " + last.OpCode.Name + " at IL_" + last.Offset.ToString("x4") + ", which falls through";
            }
        }

        for (int i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            string where = instruction.OpCode.Name + " at IL_" + instruction.Offset.ToString("x4");
            var from = inside(i);
            if (instruction.OpCode.Code == Code.Ret && from.Count > 0)
            {
                yield return where + " is inside " + string.Join(", ", from.Select(r => r.Name));
            }
            var targets = instruction.Operand is Instruction ? new[] { (Instruction)instruction.Operand }
                : instruction.Operand as Instruction[];
            if (targets == null) continue;
            foreach (var target in targets)
            {
                int t = at(target);
                if (t < 0 || t >= instructions.Count)
                {
                    yield return where + " targets an instruction that is not in the body";
                    continue;
                }
                if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                {
                    int displacement = target.Offset - instruction.Offset - instruction.GetSize();
                    if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
                    {
                        yield return where + " has displacement " + displacement + ", which does not fit a short branch";
                    }
                }
                var to = inside(t);
                // Entering a try block at its first instruction is allowed.
                var entered = to.Where(r => !from.Contains(r)).ToList();
                var bad = entered.Where(r => !(r.IsTry && r.Start == t)).ToList();
                bool leave = instruction.OpCode.Code == Code.Leave || instruction.OpCode.Code == Code.Leave_S;
                // leave may exit regions; any other branch must stay in its own.
                var left = from.Where(r => !to.Contains(r)).ToList();
                if (bad.Count > 0)
                {
                    yield return where + " jumps into " + string.Join(", ", bad.Select(r => r.Name)) + " at IL_" + target.Offset.ToString("x4");
                }
                else if (!leave && left.Count > 0)
                {
                    yield return where + " jumps out of " + string.Join(", ", left.Select(r => r.Name)) + " to IL_" + target.Offset.ToString("x4")
                        + " without leave";
                }
            }
        }
    }
}
