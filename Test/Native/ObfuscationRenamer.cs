// A small symbol renamer for Test/Native/obfuscation_check.py
// (docs/design/source-generator.md, milestone M8): it renames the types,
// namespaces, methods, fields, properties and events of an assembly after
// compilation, as an obfuscator does, so that the check can run xLua against
// renamed types. It is not an obfuscator; it does what matters for xLua's name
// lookups and nothing else.
//
// Usage: ObfuscationRenamer.exe <in.dll|exe> <out> <mapping.txt> (--rules <obfuz rules.xml> | --attributes) [search dir...]
//
// --rules applies an obfuz rule file the way obfuz 4.0.0 does
//   (ConfigurableRenamePolicy.cs: type rules by full name, wildcards,
//   obName, applyToMembers, applyToNestedTypes, field, method, property and
//   event rules; the last rule that sets obName wins) and ignores
//   [Obfuscation], as obfuz does;
// --attributes keeps what [System.Reflection.Obfuscation(Exclude = true)]
//   marks (on a type with ApplyToMembers, the default, its members too), as
//   Obfuscar, ConfuserEx and other attribute-driven tools do, and strips it.
//
// Both keep what obfuz's own policy keeps and a renamer must keep to produce
// a loadable assembly: <Module> and compiler-generated names (with '<'),
// constructors, members of delegates, the instance field of enums, extern
// methods, and virtual methods (renaming them would need the whole override
// and interface group renamed together, which obfuz does and this does not).
// New names are $N<k> for namespaces (one per namespace, as obfuz's
// consistent namespaces) and $<k> for other symbols, like obfuz's default
// prefix. References inside the assembly follow: member references through
// generic instances, and type names in custom attribute blobs (typeof
// arguments), which Cecil re-encodes from the renamed definitions once read.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Mono.Cecil;

static class ObfuscationRenamer
{
    sealed class Rules
    {
        public readonly Dictionary<TypeDefinition, bool?> Types = new Dictionary<TypeDefinition, bool?>();
        public readonly Dictionary<IMemberDefinition, bool?> Members = new Dictionary<IMemberDefinition, bool?>();

        public bool Keeps(TypeDefinition t)
        {
            bool? ob;
            return Types.TryGetValue(t, out ob) && ob == false;
        }

        public bool Keeps(IMemberDefinition m)
        {
            bool? ob;
            return Members.TryGetValue(m, out ob) && ob == false;
        }

        public void Set(IMemberDefinition m, bool? ob)
        {
            if (ob != null) Members[m] = ob;
        }
    }

    // obfuz's ObfuzScope bits as ConfigurableRenamePolicy parses applyToMembers.
    const int Field = 0x2, MethodName = 0x4, Method = 0x1c, PropertyName = 0x20, GetterSetter = 0x40,
        EventName = 0x100, AddRemoveFire = 0x200, All = 0x1 | Field | Method | PropertyName | GetterSetter | EventName | GetterSetter;

    static int? Scope(XmlElement e)
    {
        string s = e.GetAttribute("applyToMembers");
        if (string.IsNullOrWhiteSpace(s)) return null;
        int scope = 0;
        foreach (var part in s.Split(','))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "none": break;
                case "field": scope |= Field; break;
                case "eventname": scope |= EventName; break;
                case "eventaddremovefirename": scope |= AddRemoveFire; break;
                case "event": scope |= EventName | GetterSetter; break;
                case "methodname": case "method": scope |= MethodName; break;
                case "propertyname": scope |= PropertyName; break;
                case "propertygettersettername": scope |= GetterSetter; break;
                case "property": scope |= PropertyName | GetterSetter; break;
                case "all": case "*": scope |= All; break;
                default: throw new Exception("applyToMembers " + part);
            }
        }
        return scope;
    }

    static bool? Bool(XmlElement e, string name)
    {
        string s = e.GetAttribute(name);
        if (string.IsNullOrEmpty(s)) return null;
        if (s == "0" || s == "false") return false;
        if (s == "1" || s == "true") return true;
        throw new Exception(name + "=" + s);
    }

    static Func<string, bool> Matcher(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) pattern = "*";
        if (!pattern.Contains("*") && !pattern.Contains("?")) return s => s == pattern;
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$");
        return s => regex.IsMatch(s);
    }

    static readonly string[] Unsupported = { "modifier", "classType", "inherit", "hasCustomAttributes" };

    static void CheckSupported(XmlElement e)
    {
        foreach (var a in Unsupported)
        {
            if (e.HasAttribute(a)) throw new Exception("ObfuscationRenamer does not implement the rule attribute " + a + ": " + e.OuterXml);
        }
    }

    // ConfigurableRenamePolicy.BuildTypeRuleResult.
    static void ApplyTypeRule(Rules rules, XmlElement spec, TypeDefinition t)
    {
        bool? ob = Bool(spec, "obName");
        int? scope = Scope(spec);
        if (ob != null) rules.Types[t] = ob;
        var children = spec.ChildNodes.OfType<XmlElement>().ToList();
        foreach (var c in children) CheckSupported(c);
        Func<string, IEnumerable<XmlElement>> specs = kind => children.Where(c => c.Name == kind);

        foreach (var f in t.Fields)
        {
            if (scope != null && (scope & Field) != 0 && ob != null) rules.Set(f, ob);
            foreach (var fs in specs("field")) if (Matcher(fs.GetAttribute("name"))(f.Name)) rules.Set(f, Bool(fs, "obName"));
        }
        foreach (var m in t.Methods)
        {
            if (scope != null && (scope & Method) != 0 && ob != null) rules.Set(m, ob);
        }
        foreach (var e in t.Events)
        {
            if (scope != null && (scope & EventName) != 0 && ob != null) rules.Set(e, ob);
            foreach (var es in specs("event"))
            {
                if (!Matcher(es.GetAttribute("name"))(e.Name)) continue;
                if (ob != null && scope != null && (scope & AddRemoveFire) != 0) Accessors(rules, e, ob);
                bool? eob = Bool(es, "obName");
                if (eob != null)
                {
                    rules.Set(e, eob);
                    int? escope = Scope(es);
                    if (escope != null && (escope & AddRemoveFire) != 0) Accessors(rules, e, eob);
                }
            }
        }
        foreach (var p in t.Properties)
        {
            if (scope != null && (scope & PropertyName) != 0 && ob != null) rules.Set(p, ob);
            foreach (var ps in specs("property"))
            {
                if (!Matcher(ps.GetAttribute("name"))(p.Name)) continue;
                if (ob != null && scope != null && (scope & GetterSetter) != 0) Accessors(rules, p, ob);
                bool? pob = Bool(ps, "obName");
                if (pob != null)
                {
                    rules.Set(p, pob);
                    int? pscope = Scope(ps);
                    if (pscope != null && (pscope & GetterSetter) != 0) Accessors(rules, p, pob);
                }
            }
        }
        foreach (var m in t.Methods)
        {
            foreach (var ms in specs("method")) if (Matcher(ms.GetAttribute("name"))(m.Name)) rules.Set(m, Bool(ms, "obName"));
        }
        if ((Bool(spec, "applyToNestedTypes") ?? true))
        {
            foreach (var n in t.NestedTypes) ApplyTypeRule(rules, spec, n);
        }
    }

    static void Accessors(Rules rules, PropertyDefinition p, bool? ob)
    {
        if (p.GetMethod != null) rules.Set(p.GetMethod, ob);
        if (p.SetMethod != null) rules.Set(p.SetMethod, ob);
    }

    static void Accessors(Rules rules, EventDefinition e, bool? ob)
    {
        if (e.AddMethod != null) rules.Set(e.AddMethod, ob);
        if (e.RemoveMethod != null) rules.Set(e.RemoveMethod, ob);
        if (e.InvokeMethod != null) rules.Set(e.InvokeMethod, ob);
    }

    static Rules LoadRules(string path, ModuleDefinition module)
    {
        var rules = new Rules();
        var doc = new XmlDocument();
        doc.Load(path);
        if (doc.DocumentElement.Name != "obfuz") throw new Exception("not an obfuz rule file: " + path);
        var all = module.GetTypes().ToList();
        foreach (XmlElement a in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
        {
            if (a.Name != "assembly") throw new Exception("unknown element " + a.Name);
            if (a.GetAttribute("name") != module.Assembly.Name.Name) continue;
            foreach (XmlElement spec in a.ChildNodes.OfType<XmlElement>())
            {
                if (spec.Name != "type") throw new Exception("unknown element " + spec.Name);
                CheckSupported(spec);
                var match = Matcher(spec.GetAttribute("name"));
                foreach (var t in all.Where(x => match(x.FullName))) ApplyTypeRule(rules, spec, t);
            }
        }
        return rules;
    }

    static bool HasExclude(ICustomAttributeProvider p, out bool applyToMembers)
    {
        applyToMembers = false;
        foreach (var ca in p.CustomAttributes)
        {
            if (ca.AttributeType.FullName != "System.Reflection.ObfuscationAttribute") continue;
            bool exclude = true;
            applyToMembers = true;
            foreach (var na in ca.Properties)
            {
                if (na.Name == "Exclude") exclude = (bool)na.Argument.Value;
                if (na.Name == "ApplyToMembers") applyToMembers = (bool)na.Argument.Value;
            }
            if (exclude) return true;
        }
        return false;
    }

    static void StripObfuscationAttributes(ICustomAttributeProvider p)
    {
        for (int i = p.CustomAttributes.Count - 1; i >= 0; i--)
        {
            var ca = p.CustomAttributes[i];
            if (ca.AttributeType.FullName != "System.Reflection.ObfuscationAttribute") continue;
            bool strip = true;
            foreach (var na in ca.Properties) if (na.Name == "StripAfterObfuscation") strip = (bool)na.Argument.Value;
            if (strip) p.CustomAttributes.RemoveAt(i);
        }
    }

    static bool Generated(string name)
    {
        return name.Contains("<");
    }

    static IEnumerable<ICustomAttributeProvider> Providers(ModuleDefinition module)
    {
        yield return module.Assembly;
        yield return module;
        foreach (var t in module.GetTypes())
        {
            yield return t;
            foreach (var m in t.Methods)
            {
                yield return m;
                foreach (var p in m.Parameters) yield return p;
                yield return m.MethodReturnType;
            }
            foreach (var f in t.Fields) yield return f;
            foreach (var p in t.Properties) yield return p;
            foreach (var e in t.Events) yield return e;
            foreach (var g in t.GenericParameters) yield return g;
        }
    }

    static int Main(string[] args)
    {
        string input = args[0], output = args[1], mappingPath = args[2], mode = args[3];
        string rulesPath = mode == "--rules" ? args[4] : null;
        if (mode != "--rules" && mode != "--attributes") throw new Exception("mode " + mode);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
        foreach (var dir in args.Skip(rulesPath == null ? 4 : 5)) resolver.AddSearchDirectory(dir);
        var module = ModuleDefinition.ReadModule(input, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });

        // Read every custom attribute blob now: Cecil then writes typeof
        // arguments from the (renamed) definitions instead of the old strings.
        int arguments = 0;
        foreach (var p in Providers(module).ToList())
        {
            foreach (var ca in p.CustomAttributes) arguments += ca.ConstructorArguments.Count + ca.Properties.Count + ca.Fields.Count;
        }
        // Member references to this module's members (through generic
        // instances) are matched by name, so resolve them before renaming.
        // They are the operands of the method bodies, the instances Cecil writes.
        var references = new List<KeyValuePair<MemberReference, IMemberDefinition>>();
        var operands = new List<MemberReference>();
        foreach (var t in module.GetTypes())
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var r = i.Operand as MemberReference;
                    var spec = r as MethodSpecification;
                    if (spec != null) r = spec.ElementMethod;
                    if (r is MethodReference && !(r is MethodDefinition) || r is FieldReference && !(r is FieldDefinition)) operands.Add(r);
                }
            }
        }
        foreach (var r in operands.Distinct())
        {
            var element = r.DeclaringType == null ? null : r.DeclaringType.GetElementType() as TypeDefinition;
            if (element == null || element.Module != module) continue;
            IMemberDefinition d = r is MethodReference ? (IMemberDefinition)((MethodReference)r).Resolve() : r is FieldReference ? ((FieldReference)r).Resolve() : null;
            if (d == null) throw new Exception("cannot resolve " + r.FullName);
            references.Add(new KeyValuePair<MemberReference, IMemberDefinition>(r, d));
        }

        var rules = rulesPath != null ? LoadRules(rulesPath, module) : null;
        Func<TypeDefinition, bool> keepType;
        Func<IMemberDefinition, bool> keepMember;
        if (rules != null)
        {
            keepType = t => rules.Keeps(t);
            keepMember = m => rules.Keeps(m);
        }
        else
        {
            var keptTypes = new HashSet<TypeDefinition>();
            var keptMembers = new HashSet<IMemberDefinition>();
            foreach (var t in module.GetTypes())
            {
                bool members;
                bool typeExcluded = HasExclude(t, out members);
                if (typeExcluded) keptTypes.Add(t);
                bool all = typeExcluded && members;
                foreach (IMemberDefinition m in t.Methods.Cast<IMemberDefinition>().Concat(t.Fields).Concat(t.Properties).Concat(t.Events))
                {
                    bool unused;
                    if (all || HasExclude((ICustomAttributeProvider)m, out unused)) keptMembers.Add(m);
                }
            }
            keepType = t => keptTypes.Contains(t);
            keepMember = m => keptMembers.Contains(m);
        }

        int counter = 0;
        var namespaces = new Dictionary<string, string>();
        var mapping = new List<string>();
        Func<string> next = () => "$" + (counter++).ToString();
        foreach (var t in module.GetTypes().ToList())
        {
            bool isDelegate = t.BaseType != null && t.BaseType.FullName == "System.MulticastDelegate";
            foreach (var m in t.Methods)
            {
                if (m.IsConstructor || m.IsVirtual || m.IsPInvokeImpl || m.IsInternalCall || isDelegate || Generated(m.Name) || keepMember(m)) continue;
                string name = next();
                mapping.Add("method " + t.FullName + "::" + m.Name + " -> " + name);
                m.Name = name;
            }
            foreach (var f in t.Fields)
            {
                if (isDelegate || (t.IsEnum && !f.IsStatic) || f.IsSpecialName || Generated(f.Name) || keepMember(f)) continue;
                string name = next();
                mapping.Add("field " + t.FullName + "::" + f.Name + " -> " + name);
                f.Name = name;
            }
            foreach (var p in t.Properties)
            {
                if (isDelegate || Generated(p.Name) || keepMember(p)) continue;
                string name = next();
                mapping.Add("property " + t.FullName + "::" + p.Name + " -> " + name);
                p.Name = name;
            }
            foreach (var e in t.Events)
            {
                if (isDelegate || Generated(e.Name) || keepMember(e)) continue;
                string name = next();
                mapping.Add("event " + t.FullName + "::" + e.Name + " -> " + name);
                e.Name = name;
            }
        }
        // Types last, so that the mapping above shows original type names.
        var original = module.GetTypes().ToDictionary(t => t, t => t.FullName);
        foreach (var t in module.GetTypes().ToList())
        {
            if (t.Name == "<Module>" || Generated(t.Name) || keepType(t)) continue;
            string before = original[t];
            if (!t.IsNested && !string.IsNullOrEmpty(t.Namespace))
            {
                string ns;
                if (!namespaces.TryGetValue(t.Namespace, out ns)) namespaces.Add(t.Namespace, ns = "$N" + namespaces.Count);
                t.Namespace = ns;
            }
            t.Name = next();
            mapping.Add("type " + before + " -> " + t.FullName);
        }
        foreach (var kv in references)
        {
            if (kv.Key.Name != kv.Value.Name) kv.Key.Name = kv.Value.Name;
        }
        if (rules == null)
        {
            foreach (var p in Providers(module).ToList()) StripObfuscationAttributes(p);
        }
        module.Write(output);
        File.WriteAllLines(mappingPath, mapping);
        Console.WriteLine("ObfuscationRenamer: renamed " + mapping.Count + " symbols of " + module.Assembly.Name.Name + " (" + mode.Substring(2)
            + "; " + arguments + " attribute arguments re-encoded)");
        return 0;
    }
}
