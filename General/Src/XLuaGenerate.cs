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
            Console.WriteLine("XLuaGenerate assmbly_path");
            Console.WriteLine("XLuaGenerate --luacats [options] assmbly_path [output_path] [search_paths]");
            Console.WriteLine("XLuaGenerate --validate [options] assmbly_path [search_paths]");
            Console.WriteLine("options: --strict, --suffix .d.lua, --luarc path, --no-luarc, --clear (--luacats);");
            Console.WriteLine("         --gen-path path, --luacats-path path, --hotfix tools_path, --hotfix-source Hotfix.cs,");
            Console.WriteLine("         --resources-root path (repeatable) (--validate)");
        }

        public static void Main(string[] args)
        {
            // --luacats writes LuaCATS definition files instead of C# wrappers;
            // --validate reports setup problems and exits with 1 if there are any.
            bool luacats = args.Length > 0 && args[0] == "--luacats";
            bool validate = args.Length > 0 && args[0] == "--validate";
            bool clear = false;
            string hotfixTools = null;
            string hotfixSource = null;
            List<string> resourceRoots = null;
            if (luacats || validate)
            {
                args = args.Skip(1).ToArray();
                while (args.Length > 0 && args[0].StartsWith("--"))
                {
                    string option = args[0];
                    bool hasValue = option == "--suffix" || option == "--luarc" || option == "--gen-path"
                        || option == "--luacats-path" || option == "--hotfix" || option == "--hotfix-source"
                        || option == "--resources-root";
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
                        default:
                            Console.WriteLine("unknown option " + option);
                            Useage();
                            Environment.ExitCode = 2;
                            return;
                    }
                    args = args.Skip(hasValue ? 2 : 1).ToArray();
                }
            }

            if (args.Length == 0)
            {
                Useage();
                return;
            }

            List<string> assemblyPathList = args.TakeWhile(path =>
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();

            // --validate takes no output path: the rest are search paths.
            int searchStart = validate ? assemblyPathList.Count : assemblyPathList.Count + 1;
            if (!validate && args.Length > assemblyPathList.Count)
            {
                if (luacats)
                {
                    GeneratorConfig.luacats_path = args[assemblyPathList.Count];
                }
                else
                {
                    GeneratorConfig.common_path = args[assemblyPathList.Count];
                }
            }

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

            var allTypes = assemblyPathList.Select(path => Assembly.Load(File.ReadAllBytes(Path.GetFullPath(path))))
                .SelectMany(assembly => assembly.GetTypes());
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
                return;
            }
            Generator.GenAll(new XLuaTemplates()
            {
                LuaClassWrap = new XLuaTemplate()
                {
                    name = "LuaClassWrap",
#if GEN_CODE_MINIMIZE
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaClassWrapGCM_tpl,
#else
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaClassWrap_tpl,
#endif
                }, 
                LuaDelegateBridge = new XLuaTemplate()
                {
                    name = "LuaDelegateBridge",
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaDelegateBridge_tpl,
                },
                LuaDelegateWrap = new XLuaTemplate()
                {
                    name = "LuaDelegateWrap",
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaDelegateWrap_tpl,
                },
                LuaEnumWrap = new XLuaTemplate()
                {
                    name = "LuaEnumWrap",
#if GEN_CODE_MINIMIZE
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaEnumWrapGCM_tpl,
#else
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaEnumWrap_tpl,
#endif
                },
                LuaInterfaceBridge = new XLuaTemplate()
                {
                    name = "LuaInterfaceBridge",
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaInterfaceBridge_tpl,
                },
                LuaRegister = new XLuaTemplate()
                {
                    name = "LuaRegister",
#if GEN_CODE_MINIMIZE
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaRegisterGCM_tpl,
#else
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaRegister_tpl,
#endif
                },
                LuaWrapPusher = new XLuaTemplate()
                {
                    name = "LuaWrapPusher",
                    text = global::XLuaGenerate.Src.XLuaTemplates.LuaWrapPusher_tpl,
                },
                PackUnpack = new XLuaTemplate()
                {
                    name = "PackUnpack",
                    text = global::XLuaGenerate.Src.XLuaTemplates.PackUnpack_tpl,
                },
                TemplateCommon = new XLuaTemplate()
                {
                    name = "TemplateCommon",
                    text = global::XLuaGenerate.Src.XLuaTemplates.TemplateCommon_lua,
                },
            }, allTypes);
        }
    }
}
