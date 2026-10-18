using System;
using System.Linq;
using System.Reflection;
using CSObjectWrapEditor;
using System.IO;
using System.Collections.Generic;

namespace XLua
{
    public class XLuaGenerate
    {
        public static void Useage()
        {
            Console.WriteLine("XLuaGenerate --luacats [options] assmbly_path [output_path] [search_paths]");
            Console.WriteLine("XLuaGenerate --validate [options] assmbly_path [search_paths]");
            Console.WriteLine("XLuaGenerate --migrate [options] assmbly_path [search_paths]");
            Console.WriteLine("options: --strict, --suffix .d.lua, --luarc path, --no-luarc, --clear (--luacats);");
            Console.WriteLine("         --gen-path path, --luacats-path path, --hotfix tools_path, --hotfix-source Hotfix.cs,");
            Console.WriteLine("         --resources-root path (repeatable) (--validate);");
            Console.WriteLine("         --output dir, --bindings-host name, --report path, --define symbol (repeatable),");
            Console.WriteLine("         --source-root dir (repeatable), --lists-in assembly_name (repeatable), --group (--migrate)");
            Console.WriteLine("Generated bindings come from the xLua source generator (XLua.SourceGenerator.dll as a C# analyzer);");
            Console.WriteLine("XLuaGenerate no longer writes wrappers (docs/MIGRATING.md, \"Generate Code removed\").");
        }

        // XLuaGenerate --migrate: one <assembly>.XLuaConfig.cs per assembly that
        // receives configuration, in the output folder, and the report. The given
        // assemblies receive the configuration of their own types; that of other
        // types (.NET, the engine) goes into --bindings-host. --group writes the
        // types of a list that share an attribute, flags and namespace into one
        // attribute (MigrationOptions.Group). Exits with 1 when a list could not
        // be evaluated.
        static void Migrate(List<Type> types, List<Assembly> assemblies, string output, string bindingsHost, string reportPath,
            List<string> defines, List<string> sourceRoots, List<string> listsIn, bool group)
        {
            var sources = Generator.IndexClassSources(sourceRoots.SelectMany(root =>
                Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)));
            var options = new MigrationOptions
            {
                BindingsHost = bindingsHost,
                CanWrite = assembly => assemblies.Contains(assembly),
                ConvertListsIn = listsIn.Count == 0 ? null : (Func<Assembly, bool>)(assembly => listsIn.Contains(assembly.GetName().Name)),
                Defines = defines,
                SourceFile = type =>
                {
                    string path;
                    return sources.TryGetValue(type.Name, out path) ? path : null;
                },
                Tool = "XLuaGenerate --migrate" + (group ? " --group" : ""),
                Group = group,
            };
            var result = Generator.MigrateConfiguration(types, options);
            Directory.CreateDirectory(output);
            foreach (var file in result.Files)
            {
                File.WriteAllText(Path.Combine(output, file.Assembly + ".XLuaConfig.cs"), file.Text, new System.Text.UTF8Encoding(false));
            }
            File.WriteAllText(reportPath ?? Path.Combine(output, "XLuaMigrationReport.md"), result.Report, new System.Text.UTF8Encoding(false));
            Console.WriteLine(result.Summary);
            foreach (var error in result.Errors)
            {
                Console.WriteLine("error: " + error);
            }
            Environment.ExitCode = result.Errors.Count == 0 ? 0 : 1;
        }

        public static void Main(string[] args)
        {
            // --luacats writes LuaCATS definition files from the source generator's
            // manifests; --validate reports setup problems and exits with 1 if there
            // are any. Without one of the three modes it only prints the usage: the
            // wrapper generation it did before (Generate Code's command-line form)
            // was removed with the legacy generator (source generator milestone M11).
            bool luacats = args.Length > 0 && args[0] == "--luacats";
            bool validate = args.Length > 0 && args[0] == "--validate";
            // --migrate writes the configuration lists as assembly-level
            // attributes, which the source generator reads (ConfigMigration.cs).
            bool migrate = args.Length > 0 && args[0] == "--migrate";
            string migrateOutput = "XLuaConfig";
            string bindingsHost = "XLuaBindings";
            string reportPath = null;
            var defines = new List<string>();
            var sourceRoots = new List<string>();
            var listsIn = new List<string>();
            bool clear = false;
            bool group = false;
            string hotfixTools = null;
            string hotfixSource = null;
            List<string> resourceRoots = null;
            if (!luacats && !validate && !migrate)
            {
                Useage();
                Environment.ExitCode = 2;
                return;
            }
            args = args.Skip(1).ToArray();
            while (args.Length > 0 && args[0].StartsWith("--"))
            {
                string option = args[0];
                bool hasValue = option == "--suffix" || option == "--luarc" || option == "--gen-path"
                    || option == "--luacats-path" || option == "--hotfix" || option == "--hotfix-source"
                    || option == "--resources-root" || option == "--output" || option == "--bindings-host"
                    || option == "--report" || option == "--define" || option == "--source-root" || option == "--lists-in";
                if (hasValue && args.Length < 2)
                {
                    Useage();
                    Environment.ExitCode = 2;
                    return;
                }
                string value = hasValue ? args[1] : null;
                switch (option)
                {
                    case "--strict": GeneratorConfig.luacats_strict = true; break;
                    case "--suffix": GeneratorConfig.luacats_file_suffix = value; break;
                    case "--luarc": GeneratorConfig.luacats_luarc_path = value; break;
                    case "--no-luarc": GeneratorConfig.luacats_luarc_path = ""; break;
                    case "--clear": clear = true; break;
                    case "--gen-path": GeneratorConfig.common_path = value; break;
                    case "--luacats-path": GeneratorConfig.luacats_path = value; break;
                    case "--hotfix": hotfixTools = value; break;
                    case "--hotfix-source": hotfixSource = value; break;
                    case "--resources-root": (resourceRoots = resourceRoots ?? new List<string>()).Add(value); break;
                    case "--output": migrateOutput = value; break;
                    case "--bindings-host": bindingsHost = value; break;
                    case "--report": reportPath = value; break;
                    case "--define": defines.Add(value); break;
                    case "--source-root": sourceRoots.Add(value); break;
                    case "--lists-in": listsIn.Add(value); break;
                    case "--group": group = true; break;
                    default:
                        Console.WriteLine("unknown option " + option);
                        Useage();
                        Environment.ExitCode = 2;
                        return;
                }
                args = args.Skip(hasValue ? 2 : 1).ToArray();
            }

            if (args.Length == 0)
            {
                Useage();
                return;
            }

            List<string> assemblyPathList = args.TakeWhile(path =>
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();

            // --validate and --migrate take no output path: the rest are search paths.
            int searchStart = validate || migrate ? assemblyPathList.Count : assemblyPathList.Count + 1;
            if (luacats && args.Length > assemblyPathList.Count)
            {
                GeneratorConfig.luacats_path = args[assemblyPathList.Count];
            }

            // The given assemblies are loaded from bytes, so a configuration that
            // loads one of them by name (Assembly.Load("Assembly-CSharp"), as in
            // ExampleConfig.cs) would get a second copy from the search paths, with
            // types that are not the ones the generator reads. Answer with the
            // loaded one.
            var inputs = new Dictionary<string, Assembly>();
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler((object sender, ResolveEventArgs rea) =>
            {
                Assembly loaded;
                return inputs.TryGetValue(new AssemblyName(rea.Name).Name, out loaded) ? loaded : null;
            });
            if (args.Length > searchStart)
            {
                List<string> search_paths = args.Skip(searchStart).ToList();
                AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler((object sender, ResolveEventArgs rea) =>
                {
                    foreach (var search_path in search_paths)
                    {
                        string assemblyPath = Path.Combine(search_path, new AssemblyName(rea.Name).Name + ".dll");
                        if (File.Exists(assemblyPath))
                        {
                            return Assembly.Load(File.ReadAllBytes(assemblyPath));
                        }
                    }
                    return null;
                });
            }

            var assemblies = assemblyPathList.Select(path => Assembly.Load(File.ReadAllBytes(Path.GetFullPath(path)))).ToList();
            foreach (var assembly in assemblies)
            {
                if (!inputs.ContainsKey(assembly.GetName().Name)) inputs.Add(assembly.GetName().Name, assembly);
            }
            // In the Editor every assembly is loaded before the configuration is
            // read. Load what the given assemblies reference here too, so that
            // lists computed from AppDomain.CurrentDomain.GetAssemblies() see the
            // same assemblies however often and in whatever order they run.
            var pending = new Queue<Assembly>(assemblies);
            var seen = new HashSet<string>(assemblies.Select(x => x.GetName().Name));
            while (pending.Count > 0)
            {
                foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
                {
                    if (!seen.Add(reference.Name)) continue;
                    try
                    {
                        pending.Enqueue(Assembly.Load(reference));
                    }
                    catch (Exception)
                    {
                        // Not found in the search paths; it loads (or fails) when a type needs it.
                    }
                }
            }
            var allTypes = assemblies.SelectMany(assembly => assembly.GetTypes());
            if (migrate)
            {
                Migrate(allTypes.ToList(), assemblies, migrateOutput, bindingsHost, reportPath, defines, sourceRoots, listsIn, group);
                return;
            }
            if (validate)
            {
                var problems = SetupValidator.Validate(allTypes.ToList(), hotfixTools != null, hotfixTools, hotfixSource, resourceRoots);
                foreach (var problem in problems)
                {
                    Console.WriteLine("problem: " + problem);
                }
                Console.WriteLine(problems.Count + " setup problem(s)");
                Environment.ExitCode = problems.Count == 0 ? 0 : 1;
                return;
            }
            if (luacats)
            {
                if (clear)
                {
                    Console.WriteLine("Removed " + Generator.ClearLuaCATS(GeneratorConfig.luacats_path) + " generated LuaCATS files");
                }
                try
                {
                    Generator.GenLuaCATS(allTypes, GeneratorConfig.luacats_path);
                }
                catch (ArgumentException e)
                {
                    Console.WriteLine(e.Message);
                    Environment.ExitCode = 1;
                }
            }
        }
    }
}
