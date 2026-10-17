using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace XLua
{
    public class XLuaHotfixInject
    {
        public static void Useage()
        {
            Console.WriteLine("XLuaHotfixInject assmbly_path id_map_file_path [cfg_assmbly2_path] [search_path1, search_path2 ...]");
            Console.WriteLine("XLuaHotfixInject --merge-id-maps output_path id_map_file_path1 [id_map_file_path2 ...]");
        }

        public static void Main(string[] args)
        {
            // After injecting several assemblies, each with its own id map: write
            // one hotfix_id_map module with all of them, as the Editor does.
            if (args.Length > 0 && args[0] == "--merge-id-maps")
            {
                if (args.Length < 3)
                {
                    Useage();
                    return;
                }
                foreach (var missing in HotfixIdMap.Merge(args[1], args.Skip(2)))
                {
                    Console.WriteLine("Warning:hotfix id map " + missing + " is missing; " + args[1] + " is written without it.");
                }
                return;
            }
            if (args.Length < 2)
            {
                Useage();
                return;
            }

            try
            {
                var injectAssmblyPath = Path.GetFullPath(args[0]);
                var xluaAssmblyPath = Path.GetFullPath(args[1]);
                string cfg_append = null;
                if (args.Length > 3)
                {
                    cfg_append = Path.GetFullPath(args[3]);
                    if (!cfg_append.EndsWith(".data"))
                    {
                        cfg_append = null;
                    }
                }
                AppDomain currentDomain = AppDomain.CurrentDomain;
                List<string> search_paths = args.Skip(cfg_append == null ? 3 : 4).ToList();
                currentDomain.AssemblyResolve += new ResolveEventHandler((object sender, ResolveEventArgs rea) =>
                {
                    // .exe too: outside Unity the assembly that holds xLua can be the program.
                    foreach (var search_path in search_paths)
                    {
                        foreach (var extension in new[] { ".dll", ".exe" })
                        {
                            string assemblyPath = Path.Combine(search_path, new AssemblyName(rea.Name).Name + extension);
                            if (File.Exists(assemblyPath))
                            {
                                return Assembly.Load(File.ReadAllBytes(assemblyPath));
                            }
                        }
                    }
                    return null;
                });
                var assembly = Assembly.Load(File.ReadAllBytes(injectAssmblyPath));
                var hotfixCfg = new Dictionary<string, int>();
                HotfixConfig.GetConfig(hotfixCfg, assembly.GetTypes());
                if (cfg_append != null)
                {
                    using (BinaryReader reader = new BinaryReader(File.Open(cfg_append, FileMode.Open)))
                    {
                        int count = reader.ReadInt32();
                        for(int i = 0; i < count; i++)
                        {
                            string k = reader.ReadString();
                            int v = reader.ReadInt32();
                            if (!hotfixCfg.ContainsKey(k))
                            {
                                hotfixCfg.Add(k, v);
                            }
                        }
                    }
                }
                Hotfix.HotfixInject(injectAssmblyPath, xluaAssmblyPath, args.Skip(cfg_append == null ? 3 : 3), args[2], hotfixCfg);
            }
            catch(Exception e)
            {
                Console.WriteLine("Exception in hotfix inject: " + e);
            }
        }
    }
}
