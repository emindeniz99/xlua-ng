// Members reached by reflection, for the managed code stripper
// (docs/design/source-generator.md, section 5.7, milestone M5). Wrapped
// members need nothing: the generated code calls them directly. What Lua
// reaches by reflection is listed in a link.xml fragment per assembly:
//
// - [ReflectionUse] types: preserve="all", as the legacy LinkXmlGen does
//   (Lua may also reach their non-public members, xlua.private_accessible);
// - [LuaCallCSharp] types: preserve="all" too where the build can reach their
//   other members through reflection (PreserveBoundTypes: the legacy preset,
//   and minimal with HOTFIX_ENABLE), as LinkXmlGen did for every one; strict
//   minimal keeps only what the wrappers call (owner decision Q75 of
//   2026-10-29);
// - lazy members of wrapped types: [DoNotGen] members and the members the
//   generator left to reflection (section 5.5), one element per member name.
//   A type kept whole needs no member entries, so LinkXmlDocument drops them.
//
// The fragment is the constant LinkXml.Xml of the generated namespace. The
// Editor's IUnityLinkerProcessor (XLua.Editor SourceGenChecks.cs) collects the
// fragments of the loaded assemblies into one file for UnityLinker.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace XLua.SourceGenerator
{
    internal static partial class Wrappers
    {
        public const string LinkAll = "*";

        // The name link.xml uses for a type definition: Namespace.Outer/Inner`1.
        public static string LinkName(INamedTypeSymbol t)
        {
            var d = t.OriginalDefinition;
            var chain = new List<string>();
            for (var x = d; x != null; x = x.ContainingType) chain.Insert(0, x.MetadataName);
            string ns = d.ContainingNamespace == null || d.ContainingNamespace.IsGlobalNamespace ? "" : d.ContainingNamespace.ToDisplayString() + ".";
            return ns + string.Join("/", chain);
        }

        static string LinkLine(INamedTypeSymbol t, string element, string name)
        {
            return (t.OriginalDefinition.ContainingAssembly?.Name ?? "") + "|" + LinkName(t) + "|" + element + "|" + name;
        }

        // The lazy members of a wrapped type, by the element link.xml uses for
        // them, when the type declares a member of that name (an instance lazy
        // member may name only extension methods, which the extension method map
        // keeps through delegates).
        static string LazyLinkLines(INamedTypeSymbol type, ClassInfo info)
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var l in info.Lazy)
            {
                var members = type.GetMembers(l.Name);
                string element = null;
                switch (l.MemberType)
                {
                    case "Method": if (members.OfType<IMethodSymbol>().Any()) element = "method"; break;
                    case "FieldGet":
                    case "FieldSet": if (members.OfType<IFieldSymbol>().Any()) element = "field"; break;
                    case "PropertyGet":
                    case "PropertySet": if (members.OfType<IPropertySymbol>().Any()) element = "property"; break;
                    case "Event": if (members.OfType<IEventSymbol>().Any()) element = "event"; break;
                }
                if (element != null) lines.Add(LinkLine(type, element, l.Name));
            }
            return string.Join("\n", lines);
        }

        // The [ReflectionUse] types of the configuration.
        static List<string> ReflectionUseLinkLines(Compilation compilation, EquatableArray<ConfigEntry> entries, Dictionary<string, ITypeSymbol> typed)
        {
            var lines = new List<string>();
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.ReflectionUse && x.Problem == TypeProblem.None && x.LuaName != null))
            {
                ITypeSymbol symbol;
                if (!typed.TryGetValue(e.LuaName, out symbol)) symbol = compilation.GetTypeByMetadataName(e.LuaName);
                if (symbol is INamedTypeSymbol n) lines.Add(LinkLine(n, LinkAll, ""));
            }
            return lines;
        }

        // The [LuaCallCSharp] types of the configuration, kept whole when
        // PreserveBoundTypes says so: the list the wrappers come from, types a
        // referenced assembly wraps included (Lua reaches them the same way).
        static List<string> BoundTypeLinkLines(Compilation compilation, IEnumerable<INamedTypeSymbol> luaCallCSharp)
        {
            var lines = new List<string>();
            if (!PreserveBoundTypes(compilation)) return lines;
            foreach (var t in luaCallCSharp) lines.Add(LinkLine(t, LinkAll, ""));
            return lines;
        }

        // ---- which builds keep [LuaCallCSharp] types whole ----

        // Owner decision Q75 of 2026-10-29: wherever the build can reach members
        // of a wrapped type that its wrapper does not call, through reflection,
        // the type gets preserve="all", as upstream's link.xml gave every
        // [LuaCallCSharp] type. Under stripping such a member would otherwise
        // be gone and read as nil in the player only. Read from the
        // compilation's symbols like GcmDefined, since the Player Settings
        // defines reach every assembly:
        //
        // - the legacy preset (no XLUA_PRESET_MINIMAL): always. It has
        //   xlua.private_accessible, xlua.access and non-public methods through
        //   xlua.get_generic_method and xlua.tofunction (unless the project
        //   opts into XLUA_RESTRICT_PRIVATE_ACCESS), the reflection fallback,
        //   and projects that relied on upstream's link.xml for their own C#
        //   reflection;
        // - minimal with HOTFIX_ENABLE: xlua.private_accessible and xlua.access
        //   exist (ObjectTranslator registers them when XLUA_RESTRICT_PRIVATE_ACCESS,
        //   which the preset implies, is defined with HOTFIX_ENABLE);
        // - minimal with XLUA_REFLECTION and without HOTFIX_ENABLE: no. The
        //   reflection fallback then wraps only types without generated
        //   bindings (ObjectTranslator.TryDelayWrapLoader takes the generated
        //   loader first), XLUA_RESTRICT_PRIVATE_ACCESS still leaves out the
        //   private member helpers, and xlua.get_generic_method and
        //   xlua.tofunction take public methods only, as in strict minimal. So
        //   Lua reaches the same members of a [LuaCallCSharp] type as in strict
        //   minimal, and keeping the type whole would not help the unbound
        //   types the fallback reaches;
        // - strict minimal (XLUA_NO_REFLECTION) and core: no (core has no
        //   bridge and no generated bindings).
        internal static bool PreserveBoundTypes(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.Options is CSharpParseOptions o) return PreserveBoundTypes(o.PreprocessorSymbolNames);
            }
            return true;
        }

        internal static bool PreserveBoundTypes(IEnumerable<string> symbols)
        {
            var set = new HashSet<string>(symbols);
            return !set.Contains("XLUA_PRESET_MINIMAL") || set.Contains("HOTFIX_ENABLE");
        }

        // The link.xml fragment: assemblies and types in name order.
        public static string LinkXmlDocument(IEnumerable<string> lines)
        {
            var byAssembly = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split('|');
                SortedDictionary<string, SortedSet<string>> types;
                if (!byAssembly.TryGetValue(parts[0], out types)) byAssembly.Add(parts[0], types = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal));
                SortedSet<string> members;
                if (!types.TryGetValue(parts[1], out members)) types.Add(parts[1], members = new SortedSet<string>(StringComparer.Ordinal));
                members.Add(parts[2] == LinkAll ? LinkAll : parts[2] + "|" + parts[3]);
            }
            if (byAssembly.Count == 0) return "";
            var sb = new StringBuilder("<linker>\n");
            foreach (var a in byAssembly)
            {
                sb.Append("  <assembly fullname=\"").Append(Xml(a.Key)).Append("\">\n");
                foreach (var t in a.Value)
                {
                    sb.Append("    <type fullname=\"").Append(Xml(t.Key)).Append('"');
                    if (t.Value.Contains(LinkAll))
                    {
                        sb.Append(" preserve=\"all\"/>\n");
                        continue;
                    }
                    sb.Append(">\n");
                    foreach (var m in t.Value)
                    {
                        var kv = m.Split('|');
                        sb.Append("      <").Append(kv[0]).Append(" name=\"").Append(Xml(kv[1])).Append("\"/>\n");
                    }
                    sb.Append("    </type>\n");
                }
                sb.Append("  </assembly>\n");
            }
            return sb.Append("</linker>\n").ToString();
        }

        static string Xml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
