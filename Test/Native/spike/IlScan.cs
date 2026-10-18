// Scans assemblies with Mono.Cecil for generic virtual methods (declared and called)
// and auto-properties. usage: mono IlScan.exe DIR_OR_DLL... [--only Name1,Name2]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class IlScan
{
    static int Main(string[] args)
    {
        var files = new List<string>();
        HashSet<string> only = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--only") { only = new HashSet<string>(args[++i].Split(',')); continue; }
            if (Directory.Exists(args[i])) files.AddRange(Directory.GetFiles(args[i], "*.dll"));
            else files.Add(args[i]);
        }
        var resolver = new DefaultAssemblyResolver();
        foreach (var d in files.Select(Path.GetDirectoryName).Distinct()) resolver.AddSearchDirectory(d);
        var rp = new ReaderParameters { AssemblyResolver = resolver };
        int totalGvmCalls = 0;
        foreach (var f in files.OrderBy(x => x))
        {
            AssemblyDefinition asm;
            try { asm = AssemblyDefinition.ReadAssembly(f, rp); } catch (Exception e) { Console.WriteLine("SKIP " + f + " " + e.Message); continue; }
            string name = asm.Name.Name;
            bool detail = only == null || only.Contains(name);
            var decl = new List<string>();
            var calls = new Dictionary<string, int>();
            var autoProps = new List<string>();
            int methods = 0;
            foreach (var t in asm.MainModule.GetTypes())
            {
                foreach (var m in t.Methods)
                {
                    methods++;
                    if (m.HasGenericParameters && (m.IsVirtual || m.IsAbstract || t.IsInterface))
                        decl.Add(t.FullName + "::" + m.Name);
                    if (!m.HasBody) continue;
                    foreach (var ins in m.Body.Instructions)
                    {
                        if (ins.OpCode.Code != Code.Callvirt && ins.OpCode.Code != Code.Call && ins.OpCode.Code != Code.Ldvirtftn) continue;
                        var mr = ins.Operand as MethodReference;
                        if (mr != null && Environment.GetEnvironmentVariable("CALLERS") != null && mr.Name == Environment.GetEnvironmentVariable("CALLERS"))
                            Console.WriteLine("  CALLER " + name + " " + t.FullName + "::" + m.Name + " -> " + mr.FullName);
                        var gim = ins.Operand as GenericInstanceMethod;
                        if (gim == null) continue;
                        MethodDefinition target;
                        try { target = gim.Resolve(); } catch { target = null; }
                        if (target == null) continue;
                        if (!(target.IsVirtual || target.DeclaringType.IsInterface)) continue;
                        if (ins.OpCode.Code == Code.Call && !target.DeclaringType.IsInterface) continue; // non-virtual call of a virtual method
                        string key = target.DeclaringType.FullName + "::" + target.Name + "  <- " + t.FullName + "::" + m.Name;
                        int n;
                        calls.TryGetValue(key, out n);
                        calls[key] = n + 1;
                    }
                }
                foreach (var p in t.Properties)
                {
                    var acc = p.GetMethod ?? p.SetMethod;
                    if (acc == null) continue;
                    bool auto = acc.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute")
                        && t.Fields.Any(fd => fd.Name == "<" + p.Name + ">k__BackingField");
                    if (!auto) continue;
                    string vis = acc.IsPublic && t.IsPublic ? "public" : acc.IsPublic ? "public-in-nonpublic-type" : "nonpublic";
                    autoProps.Add(vis + " " + t.FullName + "::" + p.Name + (p.GetMethod != null ? " get" : "") + (p.SetMethod != null ? " set" : ""));
                }
            }
            totalGvmCalls += calls.Values.Sum();
            Console.WriteLine("ASSEMBLY " + name + " methods=" + methods + " gvm_decl=" + decl.Count + " gvm_callsites=" + calls.Values.Sum() + " auto_props=" + autoProps.Count);
            if (!detail) continue;
            foreach (var d in decl) Console.WriteLine("  GVM_DECL " + d);
            foreach (var kv in calls.OrderBy(k => k.Key)) Console.WriteLine("  GVM_CALL " + kv.Key + " x" + kv.Value);
            foreach (var a in autoProps) Console.WriteLine("  AUTO_PROP " + a);
        }
        Console.WriteLine("TOTAL gvm_callsites=" + totalGvmCalls);
        return 0;
    }
}
