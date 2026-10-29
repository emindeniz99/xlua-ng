// Rules for obfuz (https://github.com/focus-creative-games/obfuz, v4.0.0)
// for what Lua reaches by name and the name table does not cover
// (docs/design/source-generator.md, sections 5.6 and 9, milestone M8).
// obfuz does not read [Obfuscation]; it reads rule files (its manual,
// "Symbol Obfuscation", "Rule Files"). The name table already keeps CS.A.B,
// metatable keys and xlua.typename working for renamed types, and wrappers
// name members with literals, so the rules list only:
//
// - [ReflectionUse] types: the type and all its members keep their names
//   (obName="0" applyToMembers="*"); Lua reaches them by reflection, and the
//   link.xml fragment preserves them by name;
// - lazy members of wrapped types ([DoNotGen] and the members left to
//   reflection, section 5.5): those member names, and the type's name, which
//   the link.xml fragment names (it is written at link time, with original
//   names); nested types keep their own rules (applyToNestedTypes="0");
// - enums whose wrapper reads the value names by reflection (more than 20
//   values: Utils.RegisterEnumType and Enum.Parse in __CastFrom): the names
//   of their fields;
// - [LuaCallCSharp] types that the link.xml fragment keeps whole (the legacy
//   preset, and minimal with HOTFIX_ENABLE; LinkXml.cs PreserveBoundTypes):
//   the type's name only (KeepName), since link.xml names it, and its members
//   are kept by preserve="all" whatever their names.
//
// The fragment is the constant ObfuzRules.Xml of the generated namespace, for
// Editor and host builds only. The Editor adapter (XLua.Editor
// ObfuzAdapter.cs) merges the fragments of the loaded assemblies, keeps the
// assemblies obfuz obfuscates (it refuses rules for others) and adds the
// rules for xLua's own code and the injected hotfix fields.
using System;
using System.Collections.Generic;
using System.Text;

namespace XLua.SourceGenerator
{
    internal static partial class Wrappers
    {
        public const string EnumFields = "enumfields";

        // The type's name only, for a type the link.xml fragment keeps whole.
        public const string KeepName = "typename";

        // The rule document of "assembly|type|element|name" lines (LinkLine):
        // element "*" for a whole type, EnumFields for the value names of an
        // enum, KeepName for the type's name, or method, field, property or
        // event.
        public static string ObfuzRulesDocument(IEnumerable<string> lines)
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
                members.Add(parts[2] == LinkAll || parts[2] == EnumFields || parts[2] == KeepName ? parts[2] : parts[2] + "|" + parts[3]);
            }
            if (byAssembly.Count == 0) return "";
            var sb = new StringBuilder("<obfuz>\n");
            foreach (var a in byAssembly)
            {
                sb.Append("  <assembly name=\"").Append(Xml(a.Key)).Append("\">\n");
                foreach (var t in a.Value)
                {
                    sb.Append("    <type name=\"").Append(Xml(t.Key)).Append('"');
                    if (t.Value.Contains(LinkAll))
                    {
                        sb.Append(" obName=\"0\" applyToMembers=\"*\"/>\n");
                        continue;
                    }
                    var members = new List<string>();
                    bool named = false;
                    foreach (var m in t.Value)
                    {
                        if (m == KeepName)
                        {
                            named = true; // link.xml keeps the type whole, by this name
                            continue;
                        }
                        if (m == EnumFields)
                        {
                            members.Add("      <field name=\"*\" obName=\"0\"/>\n");
                            continue;
                        }
                        var kv = m.Split('|');
                        members.Add("      <" + kv[0] + " name=\"" + Xml(kv[1]) + "\" obName=\"0\"/>\n");
                        named = true; // the link.xml fragment names the type
                    }
                    if (named) sb.Append(" obName=\"0\" applyToNestedTypes=\"0\"");
                    if (members.Count == 0)
                    {
                        sb.Append("/>\n");
                        continue;
                    }
                    sb.Append(">\n");
                    foreach (var m in members) sb.Append(m);
                    sb.Append("    </type>\n");
                }
                sb.Append("  </assembly>\n");
            }
            return sb.Append("</obfuz>\n").ToString();
        }
    }
}
