// Class and enum wrappers (docs/design/source-generator.md, section 2 rows 1
// and 2, milestone M2). The C# is that of the legacy templates
// (Assets/XLua/Src/Editor/Template/LuaClassWrap.tpl.txt, LuaEnumWrap.tpl.txt
// and the helpers in TemplateCommon.lua.txt), written from the member model
// (Members.cs) in internal static classes of the assembly's generated
// namespace. Struct and enum values go through ObjectTranslator's general
// Push/Get/Update until GCOptimize pushers are generated (M4).
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace XLua.SourceGenerator
{
    // The output for one wrapped type. The text is rendered while the
    // symbols are at hand, so the model compares as strings (section 7).
    internal sealed class TypeModel : IEquatable<TypeModel>
    {
        public string LuaName;
        public string TypeofExpr;
        public string ClassName;     // the wrapper class, in the generated namespace
        public string Source;        // the wrapper file without its namespace header
        public string Manifest;      // the type's manifest entry (JSON)
        public string Dropped;       // "member: reason" lines, for XLSG004
        public string Fallbacks = "";   // "member: reason" lines of members left to reflection, for XLSG004
        public string LinkXml = "";     // "assembly|type|element|name" lines: members reached by reflection (LinkXml.cs)
        public string ObfuzLines = "";  // the same lines for the obfuz rules (ObfuzRules.cs), and enums read by name

        public string HintName
        {
            get { return ClassName + ".g.cs"; }
        }

        public bool Equals(TypeModel o)
        {
            return o != null && LuaName == o.LuaName && TypeofExpr == o.TypeofExpr && ClassName == o.ClassName && Source == o.Source
                && Manifest == o.Manifest && Dropped == o.Dropped && Fallbacks == o.Fallbacks && LinkXml == o.LinkXml
                && ObfuzLines == o.ObfuzLines;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TypeModel);
        }

        public override int GetHashCode()
        {
            return (LuaName ?? "").GetHashCode() ^ (Source ?? "").Length;
        }
    }

    // Everything the member model produced for an assembly.
    internal sealed class WrapperSet : IEquatable<WrapperSet>
    {
        public static readonly WrapperSet Empty = new WrapperSet
        {
            Types = EquatableArray<TypeModel>.Empty, Interfaces = EquatableArray<TypeModel>.Empty,
            ExtensionDelegates = "", ExtensionRegistration = "", ExtensionManifest = "[]",
        };

        public EquatableArray<TypeModel> Types;
        public EquatableArray<TypeModel> Interfaces;   // interface bridges (Bridges.cs), one file each
        public string ExtensionDelegates;      // delegate declarations for the extension method map
        public string ExtensionRegistration;   // b.ExtensionMethods(...) statements
        public string ExtensionManifest;       // JSON
        public string BoundElsewhere = "";     // "type|assembly" lines: wrapped by a referenced assembly (XLSG008)
        public string NotWrapped = "";         // "type|reason" lines: configured types without a wrapper or bridge (XLSG004)
        public string DelegateBridges = "";    // the members of the Bridges class (Bridges.cs)
        public string DelegatePragmas = "";    // #pragma lines the Bridges file needs
        public string DelegateRegistration = "";   // b.Delegate(...) statements
        public string DelegateManifest = "[]";     // JSON: the bridges by SignatureKey with their delegate types
        public string InterfaceManifest = "[]";    // JSON: the interface bridges with the members they implement
        public string StructsSource = "";      // the members of the Structs class (Structs.cs)
        public string PackSource = "";         // the members of the PackUnpack class
        public string StructRegistration = ""; // b.Struct, b.Caster and b.ArrayAccessor statements
        public string StructManifest = "[]";   // JSON: the GCOptimize types and their layout
        public string PackManifest = "[]";     // JSON: the types with Pack/UnPack
        public string NotPackable = "";        // GCOptimize structs read from tables only (XLSG010), one per line
        public string LinkXml = "";            // the link.xml fragment (LinkXml.cs), or ""
        public string HotfixBridges = "";      // the members of the HotfixBridges class (HotfixBridges.cs), or ""
        public string HotfixManifest = "[]";   // JSON: the SignatureKey of every hotfix bridge
        public string ObfuzRules = "";         // the obfuz rule fragment (ObfuzRules.cs), or ""

        public bool Equals(WrapperSet o)
        {
            return o != null && Types.Equals(o.Types) && Interfaces.Equals(o.Interfaces) && ExtensionDelegates == o.ExtensionDelegates
                && ExtensionRegistration == o.ExtensionRegistration && ExtensionManifest == o.ExtensionManifest
                && BoundElsewhere == o.BoundElsewhere && NotWrapped == o.NotWrapped && DelegateBridges == o.DelegateBridges
                && DelegatePragmas == o.DelegatePragmas && DelegateRegistration == o.DelegateRegistration
                && DelegateManifest == o.DelegateManifest && InterfaceManifest == o.InterfaceManifest
                && StructsSource == o.StructsSource && PackSource == o.PackSource && StructRegistration == o.StructRegistration
                && StructManifest == o.StructManifest && PackManifest == o.PackManifest && NotPackable == o.NotPackable
                && LinkXml == o.LinkXml && HotfixBridges == o.HotfixBridges && HotfixManifest == o.HotfixManifest
                && ObfuzRules == o.ObfuzRules;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WrapperSet);
        }

        public override int GetHashCode()
        {
            return Types.Length ^ ExtensionRegistration.Length;
        }
    }

    internal static partial class Wrappers
    {
        const string LuaAPI = "global::XLua.LuaDLL.Bridge";
        const string LuaTypes = "global::XLua.LuaTypes";
        const string Utils = "global::XLua.Utils";
        const string Translator = "global::XLua.ObjectTranslator";
        const string Callback = "[global::XLua.MonoPInvokeCallback(typeof(global::XLua.LuaDLL.lua_CSFunction))]";
        const string FindTranslator = "global::XLua.ObjectTranslator translator = global::XLua.ObjectTranslatorPool.Instance.Find(L);";

        // ---- the configuration ----

        public static WrapperSet Build(Compilation compilation, EquatableArray<ConfigEntry> entries, System.Threading.CancellationToken ct)
        {
            if (compilation.GetTypeByMetadataName("XLua.Binding.BindingApi") == null) return WrapperSet.Empty;
            var ctx = new MemberContext
            {
                Compilation = compilation,
                ObsoleteErrorsOnly = Names.ObsoleteErrorsOnly(compilation),
                OldCompiler = typeof(Compilation).Assembly.GetName().Version < new Version(4, 10),
                StringType = compilation.GetSpecialType(SpecialType.System_String),
            };
            foreach (var e in entries)
            {
                if (e.Kind == ConfigKind.BlackList && e.Member != null && e.LuaName != null)
                {
                    ctx.BlackList.Add(new BlackListRule { Type = e.LuaName, Member = e.Member, Parameters = e.Parameters == null ? null : e.Parameters.Split('|') });
                }
                else if (e.Kind == ConfigKind.DoNotGen && e.LuaName != null && !string.IsNullOrEmpty(e.Names))
                {
                    HashSet<string> set;
                    if (!ctx.DoNotGen.TryGetValue(e.LuaName, out set)) ctx.DoNotGen.Add(e.LuaName, set = new HashSet<string>());
                    set.UnionWith(e.Names.Split('|'));
                }
            }

            // The [LuaCallCSharp] types in the order the configuration lists
            // them (the order of extension classes breaks overload ties, as the
            // legacy generator's list order does).
            var typed = TypedConfiguration(compilation);
            var luaCallCSharp = new List<INamedTypeSymbol>();
            var notWrapped = new List<string>();
            var seen = new HashSet<string>();
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.LuaCallCSharp && x.Problem == TypeProblem.None && x.LuaName != null)
                .OrderBy(x => x.Path ?? "", StringComparer.Ordinal).ThenBy(x => x.Span.Start))
            {
                if (!seen.Add(e.LuaName)) continue;
                ITypeSymbol symbol;
                if (!typed.TryGetValue(e.LuaName, out symbol)) symbol = compilation.GetTypeByMetadataName(e.LuaName);
                var named = symbol as INamedTypeSymbol;
                if (named == null)
                {
                    // The legacy generator wraps array types with the members
                    // reflection reports for them (Get, Set, Address), which
                    // Roslyn does not model; they use reflection here.
                    if (symbol is IArrayTypeSymbol) notWrapped.Add(e.LuaName + "|array types are not wrapped yet; Lua reaches them through reflection");
                    continue;
                }
                // Generator.cs GetGenConfig: public, not obsolete, not a delegate, no compiler-generated name.
                if (!Names.IsPublic(named) || Names.IsObsolete(named, ctx.ObsoleteErrorsOnly) || named.TypeKind == TypeKind.Delegate
                    || named.MetadataName.Contains("<")) continue;
                luaCallCSharp.Add(named);
            }
            foreach (var t in luaCallCSharp)
            {
                ctx.Wrapped.Add(Names.Clr(t));
                if (MemberContext.PublicStaticMethods(t).Any(m => m.IsExtensionMethod)) ctx.ExtensionClasses.Add(t);
            }
            // What the member model reads from the configuration and the compiler.
            var fingerprint = new StringBuilder();
            fingerprint.Append(ctx.ObsoleteErrorsOnly).Append(ctx.OldCompiler).Append(XLuaBindingGenerator.ApiVersion);
            foreach (var r in ctx.BlackList) fingerprint.Append('|').Append(r.Type).Append(':').Append(r.Member).Append(':').Append(r.Parameters == null ? "-" : string.Join(",", r.Parameters));
            foreach (var kv in ctx.DoNotGen.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                fingerprint.Append('|').Append(kv.Key).Append('=').Append(string.Join(",", kv.Value.OrderBy(x => x, StringComparer.Ordinal)));
            // The GCOptimize types decide how wrappers push and read values (M4).
            var built = BuildStructs(compilation, entries, typed, luaCallCSharp, ctx);
            var structs = structSets.GetValue(compilation, _ => built); // the same for the same compilation
            fingerprint.Append(structs.Fingerprint);
            ctx.Fingerprint = fingerprint.ToString();

            // Types a referenced assembly already wraps (its [assembly:
            // GeneratedBindings] list, section 5.1) keep that wrapper. They
            // still count as wrapped for the member model and the map.
            var elsewhere = BoundByReferences(compilation);
            var models = new List<TypeModel>();
            var skipped = new List<string>();
            foreach (var t in luaCallCSharp)
            {
                ct.ThrowIfCancellationRequested();
                string other;
                if (elsewhere.TryGetValue(Names.Clr(t), out other))
                {
                    skipped.Add(Names.Clr(t) + "|" + other);
                    continue;
                }
                models.Add(ModelOf(t, ctx, compilation));
            }
            var set2 = new WrapperSet
            {
                Types = new EquatableArray<TypeModel>(models.OrderBy(m => m.LuaName, StringComparer.Ordinal).ToImmutableArray()),
                BoundElsewhere = string.Join("\n", skipped.OrderBy(x => x, StringComparer.Ordinal)),
                NotWrapped = string.Join("\n", notWrapped.OrderBy(x => x, StringComparer.Ordinal)),
            };
            ExtensionMap(set2, compilation, entries, typed, luaCallCSharp, ctx);
            CallLuaBridges(set2, compilation, entries, typed, ctx, notWrapped, ct);
            HotfixBridges(set2, compilation, entries, ctx);
            set2.NotWrapped = string.Join("\n", notWrapped.OrderBy(x => x, StringComparer.Ordinal));
            var trimmed = structs.Copy();
            TrimStructs(trimmed, string.Concat(set2.Types.Select(t => t.Source)) + string.Concat(set2.Interfaces.Select(t => t.Source))
                + set2.DelegateBridges, compilation);
            structs = trimmed;
            set2.StructsSource = StructsSource(structs, compilation);
            set2.PackSource = PackSource(structs, compilation);
            set2.StructRegistration = StructRegistration(structs);
            set2.StructManifest = StructManifest(structs);
            set2.PackManifest = PackManifest(structs);
            set2.NotPackable = string.Join("\n", structs.NotPackable);
            var reflectionUse = ReflectionUseLinkLines(compilation, entries, typed);
            set2.LinkXml = LinkXmlDocument(set2.Types.SelectMany(t => t.LinkXml.Split('\n')).Concat(reflectionUse));
            set2.ObfuzRules = ObfuzRulesDocument(set2.Types.SelectMany(t => t.ObfuzLines.Split('\n')).Concat(reflectionUse));
            return set2;
        }

        // ---- the cache of models of types from metadata (section 7) ----

        // Types from referenced assemblies (UnityEngine, the BCL, packages) do
        // not change between edits of the assembly being compiled, but their
        // models are rebuilt on every compilation: about 2 ms per type
        // (build/SOURCEGEN.md). The cache keeps them, keyed by the module
        // version id of the type's assembly, the type, the configuration the
        // member model reads and the extension methods that apply to the type
        // (those can come from source). Unity's compiler server keeps the
        // generator loaded, so the cache outlives one compilation.
        static readonly Dictionary<string, TypeModel> cache = new Dictionary<string, TypeModel>();
        static readonly object cacheLock = new object();
        const int CacheLimit = 20000;

        internal static int CacheHits;   // for the timing probe

        internal static void ClearCache()
        {
            lock (cacheLock) cache.Clear();
        }

        static TypeModel ModelOf(INamedTypeSymbol t, MemberContext ctx, Compilation compilation)
        {
            string key = CacheKey(t, ctx, compilation);
            TypeModel model;
            if (key != null)
            {
                lock (cacheLock)
                {
                    if (cache.TryGetValue(key, out model))
                    {
                        CacheHits++;
                        return model;
                    }
                }
            }
            model = t.TypeKind == TypeKind.Enum ? EnumModel(t, ctx) : ClassModel(t, ctx);
            if (key != null)
            {
                lock (cacheLock)
                {
                    if (cache.Count >= CacheLimit) cache.Clear();
                    cache[key] = model;
                }
            }
            return model;
        }

        static string CacheKey(INamedTypeSymbol t, MemberContext ctx, Compilation compilation)
        {
            // Type arguments may come from other assemblies; only cache types
            // whose whole chain is in one metadata assembly.
            var assembly = t.ContainingAssembly;
            if (assembly == null || SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)) return null;
            for (var c = t; c != null; c = c.ContainingType)
            {
                if (c.TypeArguments.Any(a => !SymbolEqualityComparer.Default.Equals(a.ContainingAssembly, assembly) && a.SpecialType == SpecialType.None)) return null;
            }
            var reference = compilation.GetMetadataReference(assembly) as PortableExecutableReference;
            if (reference == null) return null;
            Guid mvid;
            try
            {
                var metadata = reference.GetMetadata() as AssemblyMetadata;
                if (metadata == null) return null;
                mvid = metadata.GetModules()[0].GetModuleVersionId();
            }
            catch (Exception)
            {
                return null;
            }
            var sb = new StringBuilder();
            sb.Append(mvid).Append('|').Append(Names.Clr(t)).Append('|').Append(ctx.Fingerprint).Append('|');
            if (t.TypeKind != TypeKind.Interface)
            {
                foreach (var m in ctx.ExtensionMethodsOf(t))
                {
                    sb.Append(Signature(m, 0)).Append(m.ReturnType.ToDisplayString()).Append(';');
                }
            }
            return sb.ToString();
        }

        // The types that referenced assemblies wrap, by Lua name, with the assembly's name.
        static Dictionary<string, string> BoundByReferences(Compilation compilation)
        {
            var result = new Dictionary<string, string>();
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                foreach (var a in assembly.GetAttributes())
                {
                    if (a.AttributeClass == null || a.AttributeClass.ToDisplayString() != "XLua.Binding.GeneratedBindingsAttribute") continue;
                    if (a.ConstructorArguments.Length < 3 || a.ConstructorArguments[2].Kind != TypedConstantKind.Array) continue;
                    foreach (var v in a.ConstructorArguments[2].Values)
                    {
                        if (v.Value is ITypeSymbol t && !result.ContainsKey(Names.Clr(t))) result.Add(Names.Clr(t), assembly.Name);
                    }
                }
            }
            return result;
        }

        // The types of [assembly: X(typeof(T), ...)] by Lua name.
        static Dictionary<string, ITypeSymbol> TypedConfiguration(Compilation compilation)
        {
            var result = new Dictionary<string, ITypeSymbol>();
            foreach (var a in compilation.Assembly.GetAttributes())
            {
                if (a.AttributeClass == null || a.AttributeClass.ContainingNamespace == null || a.AttributeClass.ContainingNamespace.ToDisplayString() != "XLua") continue;
                if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Kind == TypedConstantKind.Type && a.ConstructorArguments[0].Value is ITypeSymbol t)
                {
                    string name = Names.Clr(t);
                    if (!result.ContainsKey(name)) result.Add(name, t);
                }
            }
            return result;
        }

        // Generator.cs GenLuaRegister: the extension methods Lua finds through
        // the map (InternalGlobals.extensionMethodMap): those of ReflectionUse
        // classes, and those of [LuaCallCSharp] classes for types without a
        // wrapper, or named in DoNotGen (the lazy member of the wrapper finds
        // them there).
        static void ExtensionMap(WrapperSet set, Compilation compilation, EquatableArray<ConfigEntry> entries, Dictionary<string, ITypeSymbol> typed,
            List<INamedTypeSymbol> luaCallCSharp, MemberContext ctx)
        {
            var reflectionUse = new List<INamedTypeSymbol>();
            var seen = new HashSet<string>();
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.ReflectionUse && x.Problem == TypeProblem.None && x.LuaName != null)
                .OrderBy(x => x.Path ?? "", StringComparer.Ordinal).ThenBy(x => x.Span.Start))
            {
                if (!seen.Add(e.LuaName)) continue;
                ITypeSymbol symbol;
                if (!typed.TryGetValue(e.LuaName, out symbol)) symbol = compilation.GetTypeByMetadataName(e.LuaName);
                if (symbol is INamedTypeSymbol n && !Names.IsObsolete(n, ctx.ObsoleteErrorsOnly)) reflectionUse.Add(n);
            }

            var methods = new List<IMethodSymbol>();
            foreach (var t in reflectionUse.Where(t => t.MightContainExtensionMethods))
            {
                methods.AddRange(Closed(MemberContext.PublicStaticMethods(t).Where(m => m.IsExtensionMethod && !ctx.IsObsolete(m)), ctx));
            }
            foreach (var t in luaCallCSharp.Where(t => t.MightContainExtensionMethods))
            {
                foreach (var m in Closed(MemberContext.PublicStaticMethods(t).Where(m => m.IsExtensionMethod && !ctx.IsObsolete(m)), ctx))
                {
                    var extended = m.Parameters[0].Type;
                    if (!ctx.Wrapped.Contains(Names.Clr(extended)) || (ctx.IsDoNotGen(extended, m.Name) && !ctx.IsMethodBlackListed(m.OriginalDefinition)))
                    {
                        methods.Add(m);
                    }
                }
            }

            var delegates = new StringBuilder();
            var registration = new StringBuilder();
            var manifest = new Json();
            manifest.BeginArray();
            var byType = new List<KeyValuePair<ITypeSymbol, List<int>>>();
            for (int i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                delegates.Append("        delegate ").Append(Names.Cs(m.ReturnType)).Append(" __GEN_DELEGATE").Append(i).Append('(')
                    .Append(string.Join(", ", m.Parameters.Select(p => Modifier(p, true) + Names.Cs(p.Type) + " p" + p.Ordinal))).Append(");\n");
                var extended = m.Parameters[0].Type;
                var slot = byType.FirstOrDefault(kv => SymbolEqualityComparer.Default.Equals(kv.Key, extended));
                if (slot.Key == null)
                {
                    slot = new KeyValuePair<ITypeSymbol, List<int>>(extended, new List<int>());
                    byType.Add(slot);
                }
                slot.Value.Add(i);
            }
            foreach (var kv in byType)
            {
                registration.Append("            b.ExtensionMethods(typeof(").Append(Names.Cs(kv.Key)).Append("), new global::System.Reflection.MethodInfo[] {\n");
                foreach (int i in kv.Value)
                {
                    var m = methods[i];
                    registration.Append("                new __GEN_DELEGATE").Append(i).Append('(').Append(Names.Cs(m.ContainingType)).Append('.')
                        .Append(Escape(m.Name)).Append(").Method,\n");
                    manifest.Value(Names.Clr(kv.Key) + " <- " + Signature(m.OriginalDefinition, 0));
                }
                registration.Append("            });\n");
            }
            manifest.EndArray();
            // __GEN_DELEGATE declares a `ref readonly` parameter with `ref`, what
            // Roslyn before 4.10 sees; Roslyn 4.10 (Unity 6000.6) warns CS9198 on
            // the conversion. As the legacy template (TemplateCommon RefReadOnlyPragma),
            // disable it only where such a parameter occurs.
            if (methods.Any(m => m.Parameters.Any(MemberContext.IsRefReadOnly)))
            {
                delegates.Insert(0, "#pragma warning disable CS9198 // `ref readonly` parameters declared with `ref`\n");
            }
            set.ExtensionDelegates = delegates.ToString();
            set.ExtensionRegistration = registration.ToString();
            set.ExtensionManifest = manifest.ToString();
        }

        // Generator.cs makeGenericMethodIfNeeded: a generic method closed over
        // the most derived constraint of each type parameter.
        static IEnumerable<IMethodSymbol> Closed(IEnumerable<IMethodSymbol> methods, MemberContext ctx)
        {
            foreach (var m in methods)
            {
                if (!m.IsGenericMethod)
                {
                    yield return m;
                    continue;
                }
                if (!ctx.IsSupportedGenericMethod(m)) continue;
                var arguments = new ITypeSymbol[m.TypeParameters.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    var constraints = m.TypeParameters[i].ConstraintTypes;
                    ITypeSymbol chosen = constraints[0];
                    foreach (var c in constraints)
                    {
                        if (MemberContext.IsAssignable(ctx.Compilation, c, chosen, false)) chosen = c;
                    }
                    arguments[i] = chosen;
                }
                yield return m.Construct(arguments);
            }
        }

        // ---- names ----

        // The wrapper class: the Lua name with every character that is not a
        // letter, digit or '_' replaced, and a hash of the Lua name, so that
        // A.B_C and A_B.C get different classes.
        public static string ClassName(string luaName)
        {
            var sb = new StringBuilder("W_");
            foreach (char c in luaName) sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_');
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(luaName)) hash = unchecked((hash ^ b) * 16777619);
            return sb.Append('_').Append(hash.ToString("x8")).ToString();
        }

        // A member name as C# code: @name for keywords (TemplateCommon UnK).
        static string Escape(string name)
        {
            return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
        }

        // The name of a type in error messages (TemplateCommon CsFullTypeName without global::).
        static string Display(ITypeSymbol t)
        {
            return t.ToDisplayString();
        }

        static string Local(IParameterSymbol p)
        {
            return "_" + (string.IsNullOrEmpty(p.Name) ? "arg" + p.Ordinal : p.Name);
        }

        static string Modifier(IParameterSymbol p, bool declaration)
        {
            switch (p.RefKind)
            {
                case RefKind.Out: return "out ";
                case RefKind.Ref: return "ref ";
                case RefKind.In: return declaration ? "in " : "";
                default: return (int)p.RefKind == 4 ? "ref " : ""; // ref readonly, passed with ref
            }
        }

        // ---- types (TemplateCommon) ----

        // A method's type parameter stands for its class constraint
        // (TemplateCommon _CsFullTypeName of a generic parameter: its BaseType).
        static ITypeSymbol Effective(ITypeSymbol t, Compilation c)
        {
            if (t is ITypeParameterSymbol p)
            {
                var cls = p.ConstraintTypes.FirstOrDefault(x => x.TypeKind == TypeKind.Class);
                return cls ?? t;
            }
            if (t is IArrayTypeSymbol a)
            {
                var element = Effective(a.ElementType, c);
                return SymbolEqualityComparer.Default.Equals(element, a.ElementType) ? t : c.CreateArrayTypeSymbol(element, a.Rank);
            }
            return t;
        }

        static string Cs(ITypeSymbol t, Compilation c)
        {
            return Names.Cs(Effective(t, c));
        }

        // TemplateCommon getSafeFullName: the type's FullName.
        static string Full(ITypeSymbol t, Compilation c)
        {
            return Names.Clr(Effective(t, c));
        }

        static bool IsStruct(ITypeSymbol t, Compilation c)
        {
            t = Effective(t, c);
            return t.IsValueType && !MemberContext.IsPrimitive(t);
        }

        static bool NeedUpdate(ITypeSymbol t, Compilation c)
        {
            t = Effective(t, c);
            return t.IsValueType && !MemberContext.IsPrimitive(t) && t.TypeKind != TypeKind.Enum && t.SpecialType != SpecialType.System_Decimal;
        }

        static bool IsDelegate(ITypeSymbol t)
        {
            return t.TypeKind == TypeKind.Delegate || t.SpecialType == SpecialType.System_Delegate || t.SpecialType == SpecialType.System_MulticastDelegate;
        }

        static readonly Dictionary<string, string> FixChecker = new Dictionary<string, string>
        {
            { "System.Boolean", LuaTypes + ".LUA_TBOOLEAN == " + LuaAPI + ".lua_type" },
            { "System.Byte", LuaAPI + ".xlua_isinteger_number" },
            { "System.Char", LuaAPI + ".xlua_isinteger_number" },
            { "System.Double", LuaTypes + ".LUA_TNUMBER == " + LuaAPI + ".lua_type" },
            { "System.Int16", LuaAPI + ".xlua_isinteger_number" },
            { "System.Int32", LuaAPI + ".xlua_isinteger_number" },
            { "System.SByte", LuaAPI + ".xlua_isinteger_number" },
            { "System.Single", LuaTypes + ".LUA_TNUMBER == " + LuaAPI + ".lua_type" },
            { "System.UInt16", LuaAPI + ".xlua_isinteger_number" },
            { "System.UInt32", LuaAPI + ".xlua_isinteger_number" },
            { "System.IntPtr", LuaTypes + ".LUA_TLIGHTUSERDATA == " + LuaAPI + ".lua_type" },
        };

        // TemplateCommon integerArgCaster: Lua's integer rule (LuaDLL.cs) for
        // arguments, whose error names the argument; results use the xlua_get*
        // readers in FixCaster.
        static readonly Dictionary<string, string> IntegerArgCaster = new Dictionary<string, string>
        {
            { "System.Byte", LuaAPI + ".xlua_checkbyte" },
            { "System.Char", LuaAPI + ".xlua_checkchar" },
            { "System.Int16", LuaAPI + ".xlua_checkint16" },
            { "System.Int32", LuaAPI + ".xlua_checkint32" },
            { "System.Int64", LuaAPI + ".xlua_checkinteger" },
            { "System.SByte", LuaAPI + ".xlua_checksbyte" },
            { "System.UInt16", LuaAPI + ".xlua_checkuint16" },
            { "System.UInt32", LuaAPI + ".xlua_checkuint32" },
            { "System.UInt64", LuaAPI + ".xlua_checkuint64" },
        };

        static readonly Dictionary<string, string> TypedCaster = new Dictionary<string, string>
        {
            { "System.Single", LuaAPI + ".lua_tonumber" },
        };

        static readonly Dictionary<string, string> FixCaster = new Dictionary<string, string>
        {
            { "System.Double", LuaAPI + ".lua_tonumber" },
            { "System.String", LuaAPI + ".lua_tostring" },
            { "System.Boolean", LuaAPI + ".lua_toboolean" },
            { "System.Byte[]", LuaAPI + ".lua_tobytes" },
            { "System.IntPtr", LuaAPI + ".lua_touserdata" },
            { "System.Byte", LuaAPI + ".xlua_getbyte" },
            { "System.Char", LuaAPI + ".xlua_getchar" },
            { "System.Int16", LuaAPI + ".xlua_getint16" },
            { "System.SByte", LuaAPI + ".xlua_getsbyte" },
            { "System.UInt16", LuaAPI + ".xlua_getuint16" },
            { "System.UInt32", LuaAPI + ".xlua_getuint32" },
            { "System.UInt64", LuaAPI + ".xlua_getuint64" },
            { "System.Int32", LuaAPI + ".xlua_getint32" },
            { "System.Int64", LuaAPI + ".xlua_getinteger" },
        };

        static readonly Dictionary<string, string> FixPush = new Dictionary<string, string>
        {
            { "System.Byte", LuaAPI + ".xlua_pushinteger" },
            { "System.Char", LuaAPI + ".xlua_pushinteger" },
            { "System.Int16", LuaAPI + ".xlua_pushinteger" },
            { "System.Int32", LuaAPI + ".xlua_pushinteger" },
            { "System.Int64", LuaAPI + ".lua_pushint64" },
            { "System.SByte", LuaAPI + ".xlua_pushinteger" },
            { "System.Single", LuaAPI + ".lua_pushnumber" },
            { "System.UInt16", LuaAPI + ".xlua_pushinteger" },
            { "System.UInt32", LuaAPI + ".xlua_pushuint" },
            { "System.UInt64", LuaAPI + ".lua_pushuint64" },
            { "System.Double", LuaAPI + ".lua_pushnumber" },
            { "System.String", LuaAPI + ".lua_pushstring" },
            { "System.Byte[]", LuaAPI + ".lua_pushstring" },
            { "System.Boolean", LuaAPI + ".lua_pushboolean" },
            { "System.IntPtr", LuaAPI + ".lua_pushlightuserdata" },
            { "System.Decimal", "translator.PushDecimal" },
            { "System.Object", "translator.PushAny" },
        };

        static readonly HashSet<string> NoTranslator = new HashSet<string>
        {
            "System.Byte", "System.Char", "System.Int16", "System.Int32", "System.Int64", "System.SByte", "System.Single", "System.UInt16",
            "System.UInt32", "System.UInt64", "System.Double", "System.String", "System.Boolean", "System.Void", "System.IntPtr", "System.Byte[]",
        };

        static string Check(ITypeSymbol t, int idx, bool isParams, Compilation c)
        {
            string start = isParams ? "(" + LuaTypes + ".LUA_TNONE == " + LuaAPI + ".lua_type(L, " + idx + ") || " : "";
            string end = isParams ? ")" : "";
            string name = Full(t, c);
            string i = idx.ToString();
            string body;
            if (name == "System.String" || name == "System.Byte[]")
                body = "(" + LuaAPI + ".lua_isnil(L, " + i + ") || " + LuaAPI + ".lua_type(L, " + i + ") == " + LuaTypes + ".LUA_TSTRING)";
            else if (name == "System.Int64")
                body = "(" + LuaAPI + ".xlua_isinteger_number(L, " + i + ") || " + LuaAPI + ".lua_isint64(L, " + i + "))";
            else if (name == "System.UInt64")
                body = "(" + LuaAPI + ".xlua_isinteger_number(L, " + i + ") || " + LuaAPI + ".lua_isuint64(L, " + i + "))";
            else if (name == "System.Decimal")
                body = "(" + LuaTypes + ".LUA_TNUMBER == " + LuaAPI + ".lua_type(L, " + i + ") || translator.IsDecimal(L, " + i + "))";
            else if (name == "XLua.LuaTable")
                body = "(" + LuaAPI + ".lua_isnil(L, " + i + ") || " + LuaAPI + ".lua_type(L, " + i + ") == " + LuaTypes + ".LUA_TTABLE)";
            else if (name == "XLua.LuaFunction")
                body = "(" + LuaAPI + ".lua_isnil(L, " + i + ") || " + LuaAPI + ".lua_type(L, " + i + ") == " + LuaTypes + ".LUA_TFUNCTION)";
            else
            {
                string checker;
                body = (FixChecker.TryGetValue(name, out checker) ? checker : "translator.Assignable<" + Cs(t, c) + ">") + "(L, " + i + ")";
            }
            return start + body + end;
        }

        // TemplateCommon GetCasterStatement (lua_result false: an argument).
        static string Cast(ITypeSymbol t, int idx, string variable, bool declare, bool isParams, Compilation c)
        {
            return Cast(t, idx.ToString(), variable, declare, isParams, c, false);
        }

        // luaResult: a value C# reads back from Lua (a delegate or interface
        // bridge's return value or out/ref result), not an argument: integers
        // use the xlua_get* readers (TemplateCommon GetResultCasterStatement).
        static string Cast(ITypeSymbol t, string idx, string variable, bool declare, bool isParams, Compilation c, bool luaResult)
        {
            string name = Full(t, c);
            bool isStruct = IsStruct(t, c);
            string statement = "";
            if (declare)
            {
                statement = Cs(t, c) + " " + variable + (isStruct && !TypedCaster.ContainsKey(name) && !FixCaster.ContainsKey(name) ? ";" : " = ");
            }
            else if (!isStruct)
            {
                statement = variable + " = ";
            }
            string i = idx;
            string caster;
            if (isParams)
                return statement + "translator.GetParams<" + Cs(((IArrayTypeSymbol)Effective(t, c)).ElementType, c) + ">(L, " + i + ")";
            if (!luaResult && IntegerArgCaster.TryGetValue(name, out caster))
                return statement + caster + "(L, " + i + ")";
            if (TypedCaster.TryGetValue(name, out caster))
                return statement + "(" + Cs(t, c) + ")" + caster + "(L, " + i + ")";
            if (IsDelegate(Effective(t, c)))
                return statement + "translator.GetDelegate<" + Cs(t, c) + ">(L, " + i + ")";
            if (FixCaster.TryGetValue(name, out caster))
                return statement + caster + "(L, " + i + ")";
            if (name == "System.Object")
                return statement + "translator.GetObject(L, " + i + ", typeof(object))";
            if (isStruct)
                return statement + StructGet(Effective(t, c), i, variable, c);
            return statement + "(" + Cs(t, c) + ")translator.GetObject(L, " + i + ", typeof(" + Cs(t, c) + "))";
        }

        // TemplateCommon StructGetName: the generated reader of a GCOptimize
        // type or [LuaCallCSharp] enum (Structs.cs), else ObjectTranslator.Get<T>.
        static string StructGet(ITypeSymbol t, string idx, string variable, Compilation c)
        {
            var s = StructsOf(c).GetterOf(t);
            return s != null ? "Structs.Get_" + s.Id + "(translator, L, " + idx + ", out " + variable + ")"
                : "translator.Get(L, " + idx + ", out " + variable + ")";
        }

        static string Push(ITypeSymbol t, string variable, Compilation c)
        {
            var e = Effective(t, c);
            string push;
            if (FixPush.TryGetValue(Names.Clr(e), out push)) return push + "(L, " + variable + ")";
            // TemplateCommon genPushAndUpdateTypes: without boxing (Structs.cs).
            var s = StructsOf(c).PushedType(e);
            if (s != null) return "Structs.Push_" + s.Id + "(translator, L, " + variable + ")";
            if (e.TypeKind == TypeKind.Interface || (e.OriginalDefinition != null && e.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
                return "translator.PushAny(L, " + variable + ")";
            return "translator.Push(L, " + variable + ")";
        }

        static string Update(ITypeSymbol t, int idx, string variable, Compilation c)
        {
            var s = StructsOf(c).PushedType(Effective(t, c));
            if (s != null) return "Structs.Update_" + s.Id + "(translator, L, " + idx + ", " + variable + ")";
            return "translator.Update(L, " + idx + ", " + variable + ")";
        }

        static string Self(ITypeSymbol type, Compilation c)
        {
            string cs = Names.Cs(type);
            if (IsStruct(type, c)) return cs + " gen_to_be_invoked;" + StructGet(type, "1", "gen_to_be_invoked", c);
            if (type.SpecialType == SpecialType.System_Object) return "object gen_to_be_invoked = global::XLua.Binding.BindingApi.FastGetCSObj(translator, L, 1)";
            return cs + " gen_to_be_invoked = (" + cs + ")global::XLua.Binding.BindingApi.FastGetCSObj(translator, L, 1)";
        }

        static bool JustLuaType(ITypeSymbol t, Compilation c)
        {
            return NoTranslator.Contains(Full(t, c));
        }

        // TemplateCommon CallNeedTranslator over the method's own parameters.
        static bool CallNeedsTranslator(IMethodSymbol m, Compilation c)
        {
            if (!m.IsStatic) return true;
            if (!m.ReturnsVoid && !JustLuaType(m.ReturnType, c)) return true;
            return m.Parameters.Any(p => p.IsParams || !JustLuaType(p.Type, c));
        }

        // ---- class wrappers (LuaClassWrap.tpl.txt) ----

        static readonly Dictionary<string, string> OpMeta = new Dictionary<string, string>
        {
            { "op_Addition", "__AddMeta" }, { "op_Subtraction", "__SubMeta" }, { "op_Multiply", "__MulMeta" }, { "op_Division", "__DivMeta" },
            { "op_Equality", "__EqMeta" }, { "op_UnaryNegation", "__UnmMeta" }, { "op_LessThan", "__LTMeta" }, { "op_LessThanOrEqual", "__LEMeta" },
            { "op_Modulus", "__ModMeta" }, { "op_BitwiseAnd", "__BandMeta" }, { "op_BitwiseOr", "__BorMeta" }, { "op_ExclusiveOr", "__BxorMeta" },
            { "op_OnesComplement", "__BnotMeta" }, { "op_LeftShift", "__ShlMeta" }, { "op_RightShift", "__ShrMeta" },
        };

        static readonly Dictionary<string, string> OpSymbol = new Dictionary<string, string>
        {
            { "op_Addition", "+" }, { "op_Subtraction", "-" }, { "op_Multiply", "*" }, { "op_Division", "/" }, { "op_Equality", "==" },
            { "op_UnaryNegation", "-" }, { "op_LessThan", "<" }, { "op_LessThanOrEqual", "<=" }, { "op_Modulus", "%" }, { "op_BitwiseAnd", "&" },
            { "op_BitwiseOr", "|" }, { "op_ExclusiveOr", "^" }, { "op_OnesComplement", "~" }, { "op_LeftShift", "<<" }, { "op_RightShift", ">>" },
        };

        sealed class Writer
        {
            readonly StringBuilder sb = new StringBuilder();
            public int Indent;

            public Writer Line(string text = "")
            {
                if (text.Length > 0) sb.Append(' ', Indent * 4).Append(text);
                sb.Append('\n');
                return this;
            }

            public Writer Open(string text)
            {
                Line(text);
                Line("{");
                Indent++;
                return this;
            }

            public Writer Close(string suffix = "")
            {
                Indent--;
                return Line("}" + suffix);
            }

            public override string ToString()
            {
                return sb.ToString();
            }
        }

        static TypeModel ClassModel(INamedTypeSymbol type, MemberContext ctx)
        {
            var c = ctx.Compilation;
            var info = ctx.Build(type);
            string luaName = Names.Clr(type);
            string cs = Names.Cs(type);
            string className = ClassName(luaName);
            var w = new Writer { Indent = 1 };

            int objMethods = 0, objGetters = 0, objSetters = 0, clsFields = 1, clsGetters = 0, clsSetters = 0;
            foreach (var m in info.Methods) { if (m.IsStatic) clsFields++; else objMethods++; }
            foreach (var e in info.Events) { if (e.IsStatic) clsFields++; else objMethods++; }
            foreach (var g in info.Getters) { if (g.IsStatic) { if (g.ReadOnly) clsFields++; else clsGetters++; } else objGetters++; }
            foreach (var s in info.Setters) { if (s.IsStatic) clsSetters++; else objSetters++; }
            foreach (var l in info.Lazy)
            {
                switch (l.Index)
                {
                    case "CLS_IDX": clsFields++; break;
                    case "CLS_GETTER_IDX": clsGetters++; break;
                    case "CLS_SETTER_IDX": clsSetters++; break;
                    case "METHOD_IDX": objMethods++; break;
                    case "GETTER_IDX": objGetters++; break;
                    case "SETTER_IDX": objSetters++; break;
                }
            }
            bool isArray = false; // only named types are wrapped
            bool hasIndexer = isArray || info.Indexers.Count > 0;
            bool hasNewIndexer = isArray || info.NewIndexers.Count > 0;

            w.Line("// " + luaName);
            w.Open("internal static class " + className);
            w.Open("internal static void __Register(global::System.IntPtr L)");
            w.Line(FindTranslator);
            w.Line("global::System.Type type = typeof(" + cs + ");");
            w.Line(Utils + ".BeginObjectRegister(type, L, translator, " + info.Operators.Count + ", " + objMethods + ", " + objGetters + ", " + objSetters + ");");
            foreach (var op in info.Operators)
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".OBJ_META_IDX, \"" + OpMeta[op.Name].Replace("Meta", "").ToLowerInvariant() + "\", " + OpMeta[op.Name] + ");");
            foreach (var m in info.Methods.Where(m => !m.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".METHOD_IDX, " + Names.Literal(m.Name) + ", _m_" + m.Key + ");");
            foreach (var e in info.Events.Where(e => !e.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".METHOD_IDX, " + Names.Literal(e.Name) + ", _e_" + e.Name + ");");
            foreach (var g in info.Getters.Where(g => !g.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".GETTER_IDX, " + Names.Literal(g.Name) + ", _g_get_" + g.Name + ");");
            foreach (var s in info.Setters.Where(s => !s.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".SETTER_IDX, " + Names.Literal(s.Name) + ", _s_set_" + s.Name + ");");
            foreach (var l in info.Lazy.Where(l => !l.IsStatic)) LazyRegistration(w, l);
            w.Line(Utils + ".EndObjectRegister(type, L, translator, " + (hasIndexer ? "__CSIndexer" : "null") + ", " + (hasNewIndexer ? "__NewIndexer" : "null") + ", null, null, null);");
            w.Line();
            w.Line(Utils + ".BeginClassRegister(type, L, __CreateInstance, " + clsFields + ", " + clsGetters + ", " + clsSetters + ");");
            foreach (var m in info.Methods.Where(m => m.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, " + Names.Literal(m.Name) + ", _m_" + m.Key + ");");
            foreach (var e in info.Events.Where(e => e.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, " + Names.Literal(e.Name) + ", _e_" + e.Name + ");");
            foreach (var g in info.Getters.Where(g => g.IsStatic && g.ReadOnly))
                w.Line(Utils + ".RegisterObject(L, translator, " + Utils + ".CLS_IDX, " + Names.Literal(g.Name) + ", " + cs + "." + Escape(g.Name) + ");");
            foreach (var g in info.Getters.Where(g => g.IsStatic && !g.ReadOnly))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_GETTER_IDX, " + Names.Literal(g.Name) + ", _g_get_" + g.Name + ");");
            foreach (var s in info.Setters.Where(s => s.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_SETTER_IDX, " + Names.Literal(s.Name) + ", _s_set_" + s.Name + ");");
            foreach (var l in info.Lazy.Where(l => l.IsStatic)) LazyRegistration(w, l);
            w.Line(Utils + ".EndClassRegister(type, L, translator);");
            w.Close();

            Constructor(w, info, c);
            if (hasIndexer) Indexer(w, info, c);
            if (hasNewIndexer) NewIndexer(w, info, c);
            foreach (var op in info.Operators) Operator(w, info, op, c);
            foreach (var m in info.Methods) Method(w, info, m, ctx);
            foreach (var g in info.Getters.Where(g => !(g.IsStatic && g.ReadOnly))) Getter(w, info, g, c);
            foreach (var s in info.Setters) Setter(w, info, s, c);
            foreach (var e in info.Events) Event(w, info, e, c);
            w.Close();

            return new TypeModel
            {
                LuaName = luaName,
                TypeofExpr = Names.Typeof(type),
                ClassName = className,
                Source = w.ToString(),
                Manifest = ClassManifest(info, luaName),
                Dropped = string.Join("\n", info.Dropped),
                Fallbacks = string.Join("\n", info.Lazy.Where(l => l.Reason != null).Select(l => l.Name + ": " + l.Reason)),
                LinkXml = LazyLinkLines(type, info),
                ObfuzLines = LazyLinkLines(type, info),
            };
        }

        // A DoNotGen member, or a member left to reflection with its reason
        // (BindingApi.RegisterFallback, section 5.5): the reason text is
        // compiled only where the first use logs it.
        static void LazyRegistration(Writer w, LazyMember l)
        {
            string args = "L, " + Utils + "." + l.Index + ", " + Names.Literal(l.Name) + ", type, global::XLua.LazyMemberTypes." + l.MemberType + ", "
                + (l.IsStatic ? "true" : "false");
            if (l.Reason == null)
            {
                w.Line(Utils + ".RegisterLazyFunc(" + args + ");");
                return;
            }
            w.Line("#if UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_GENERAL");
            w.Line("global::XLua.Binding.BindingApi.RegisterFallback(" + args + ", " + Names.Literal(l.Reason) + ");");
            w.Line("#else");
            w.Line("global::XLua.Binding.BindingApi.RegisterFallback(" + args + ", null);");
            w.Line("#endif");
        }

        static void Try(Writer w)
        {
            w.Line("try");
            w.Line("{");
            w.Indent++;
        }

        static void Catch(Writer w)
        {
            w.Indent--;
            w.Line("}");
            w.Line("catch (global::System.Exception gen_e)");
            w.Line("{");
            w.Line("    return " + LuaAPI + ".luaL_error(L, \"c# exception:\" + gen_e);");
            w.Line("}");
        }

        static void Function(Writer w, string name, string visibility = "static")
        {
            w.Line();
            w.Line(Callback);
            w.Open(visibility + " int " + name + "(global::System.IntPtr L)");
        }

        static int CountIn(IParameterSymbol[] ps)
        {
            return ps.Count(p => p.RefKind != RefKind.Out);
        }

        static int CountOut(IParameterSymbol[] ps)
        {
            return ps.Count(p => p.RefKind != RefKind.None && !MemberContext.IsInParam(p));
        }

        static string Arguments(IParameterSymbol[] ps, int count)
        {
            return string.Join(", ", ps.Take(count).Select(p => Modifier(p, false) + Local(p)));
        }

        // The arguments for the trailing optional parameters a call leaves out
        // (Generator.cs explicitDefaultArguments), when leaving them out is
        // ambiguous: another method or constructor of that name takes the same
        // leading parameter types and fills the rest with defaults too, such as
        // Unity 6000.6's PhysicsScene2D.GetRayIntersection(Ray, float, int = -5)
        // next to (Ray, float, int = -5, Allocator = ...) (CS0121). Written out,
        // the call binds the method that takes them all. "" when the call is not
        // ambiguous, for extension methods, and when a default cannot be written
        // as a constant.
        static string ExplicitDefaults(Overload o, int real)
        {
            var m = o.Method;
            if (o.DefaultCount <= 0 || o.IsExtension || m.IsExtensionMethod) return "";
            var ps = m.Parameters;
            var others = new List<IMethodSymbol>();
            if (m.MethodKind == MethodKind.Constructor)
            {
                others.AddRange(m.ContainingType.InstanceConstructors.Where(x => x.DeclaredAccessibility == Accessibility.Public));
            }
            else
            {
                // Like reflection's GetMember with FlattenHierarchy: the public
                // methods of the type and its base types, without the ones an
                // override in the chain replaces.
                var overridden = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                for (var t = m.ContainingType; t != null; t = t.BaseType)
                {
                    foreach (var x in t.GetMembers(m.Name).OfType<IMethodSymbol>())
                    {
                        if (x.MethodKind != MethodKind.Ordinary || x.DeclaredAccessibility != Accessibility.Public) continue;
                        if (overridden.Contains(x)) continue;
                        for (var b = x.OverriddenMethod; b != null; b = b.OverriddenMethod) overridden.Add(b);
                        others.Add(x);
                    }
                }
            }
            bool ambiguous = false;
            foreach (var other in others)
            {
                if (SymbolEqualityComparer.Default.Equals(other, m)) continue;
                var ops = other.Parameters;
                if (ops.Length <= real) continue;
                bool same = true;
                for (int i = 0; i < real && same; i++) same = SymbolEqualityComparer.Default.Equals(ops[i].Type, ps[i].Type) && (ops[i].RefKind == RefKind.None) == (ps[i].RefKind == RefKind.None);
                for (int i = real; i < ops.Length && same; i++) same = ops[i].IsOptional || ops[i].IsParams;
                if (same)
                {
                    ambiguous = true;
                    break;
                }
            }
            if (!ambiguous) return "";
            var args = new List<string>();
            for (int i = real; i < ps.Length; i++)
            {
                string literal = DefaultLiteral(ps[i]);
                if (literal == null) return "";
                args.Add(literal);
            }
            return (real > 0 ? ", " : "") + string.Join(", ", args);
        }

        // A parameter's default value as a C# expression of its type, or null.
        static string DefaultLiteral(IParameterSymbol p)
        {
            if (!p.IsOptional || p.IsParams || p.RefKind != RefKind.None || !p.HasExplicitDefaultValue) return null;
            string name = LiteralTypeName(p.Type);
            if (name == null) return null;
            object v = p.ExplicitDefaultValue;
            if (v == null) return "default(" + name + ")";
            string text = ConstantText(v);
            return text == null ? null : "(" + name + ")(" + text + ")";
        }

        static string ConstantText(object v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (v)
            {
                case bool b: return b ? "true" : "false";
                case string s: return Names.Literal(s);
                case char ch: return ((int)ch).ToString(inv);
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _: case ulong _:
                    return ((IFormattable)v).ToString(null, inv);
                case float f:
                    if (float.IsNaN(f)) return "float.NaN";
                    if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
                    if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
                    return f.ToString("R", inv) + "F";
                case double d:
                    if (double.IsNaN(d)) return "double.NaN";
                    if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
                    if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
                    return d.ToString("R", inv) + "D";
                case decimal m: return m.ToString(inv) + "M";
                default: return null;
            }
        }

        // The C# name of a type for a cast or default(...): keywords, T? and
        // the global:: names of non-generic types; null for anything else.
        static string LiteralTypeName(ITypeSymbol t)
        {
            if (t is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                string inner = LiteralTypeName(nullable.TypeArguments[0]);
                return inner == null ? null : inner + "?";
            }
            if (!(t is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error || named.IsGenericType) return null;
            for (var d = named.ContainingType; d != null; d = d.ContainingType)
            {
                if (d.IsGenericType) return null;
            }
            return Names.Cs(named);
        }

        // After the call: by-ref results are pushed, ref structs updated in place.
        static void PushResults(Writer w, IParameterSymbol[] ps, int count, int offset, Compilation c)
        {
            int inPos = 0;
            for (int pi = 0; pi < count; pi++)
            {
                var p = ps[pi];
                if (p.RefKind != RefKind.Out) inPos++;
                if (p.RefKind != RefKind.None && !MemberContext.IsInParam(p))
                {
                    w.Line(Push(p.Type, Local(p), c) + ";");
                    if (p.RefKind != RefKind.Out && NeedUpdate(p.Type, c)) w.Line(Update(p.Type, inPos + offset, Local(p), c) + ";");
                }
            }
        }

        static void Constructor(Writer w, ClassInfo info, Compilation c)
        {
            var type = info.Type;
            string cs = Names.Cs(type);
            Function(w, "__CreateInstance");
            if (info.Constructors.Count == 0 && !type.IsValueType)
            {
                w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal(Display(type) + " does not have a constructor!") + ");");
                w.Close();
                return;
            }
            Try(w);
            w.Line(FindTranslator);
            bool hasZero = false;
            foreach (var o in info.Constructors)
            {
                var ps = o.Parameters;
                if (ps.Length == 0) hasZero = true;
                int real = ps.Length - o.DefaultCount;
                bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
                int inNum = CountIn(ps), outNum = CountOut(ps);
                var cond = new StringBuilder(LuaAPI + ".lua_gettop(L) " + (hasParams ? ">=" : "==") + " " + (inNum + 1 - o.DefaultCount - (hasParams ? 1 : 0)));
                int inPos = 0;
                for (int pi = 0; pi < real; pi++)
                {
                    var p = ps[pi];
                    bool isV = hasParams && pi == ps.Length - 1;
                    var t = isV ? ((IArrayTypeSymbol)p.Type).ElementType : p.Type;
                    if (p.RefKind != RefKind.Out)
                    {
                        inPos++;
                        cond.Append(" && ").Append(Check(t, inPos + 1, isV, c));
                    }
                }
                w.Open("if (" + cond + ")");
                for (int pi = 0; pi < real; pi++)
                {
                    var p = ps[pi];
                    w.Line(Cast(p.Type, pi + 2, Local(p), true, hasParams && pi == ps.Length - 1, c) + ";");
                }
                w.Line("var gen_ret = new " + cs + "(" + Arguments(ps, real) + ExplicitDefaults(o, real) + ");");
                if (outNum + 1 > 10) w.Line("global::XLua.Binding.BindingApi.EnsureStack(L, " + (outNum + 1) + ");");
                w.Line(Push(type, "gen_ret", c) + ";");
                PushResults(w, ps, real, 1, c);
                w.Line("return " + (outNum + 1) + ";");
                w.Close();
            }
            if (!hasZero && type.IsValueType)
            {
                w.Open("if (" + LuaAPI + ".lua_gettop(L) == 1)");
                w.Line(Push(type, "default(" + cs + ")", c) + ";");
                w.Line("return 1;");
                w.Close();
            }
            Catch(w);
            w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to " + Display(type) + " constructor!") + ");");
            w.Close();
        }

        static string IndexerGet(IMethodSymbol getter, string obj, string key, Compilation c)
        {
            if (!MemberContext.IsRefReadOnly(getter.Parameters[0])) return obj + "[" + key + "]";
            if (OldCompilerFor(c)) return obj + "." + getter.Name + "(ref " + key + ")";
            return obj + "[ref " + key + "]";
        }

        static string IndexerSet(IMethodSymbol setter, string obj, string key, string value, Compilation c)
        {
            if (MemberContext.IsRefReadOnly(setter.Parameters[0]) && OldCompilerFor(c)) return obj + "." + setter.Name + "(ref " + key + ", " + value + ")";
            return IndexerGet(setter, obj, key, c) + " = " + value;
        }

        static bool OldCompilerFor(Compilation c)
        {
            return typeof(Compilation).Assembly.GetName().Version < new Version(4, 10);
        }

        static void Indexer(Writer w, ClassInfo info, Compilation c)
        {
            var type = info.Type;
            Function(w, "__CSIndexer", "internal static");
            Try(w);
            w.Line(FindTranslator);
            foreach (var indexer in info.Indexers)
            {
                var key = indexer.Parameters[0];
                w.Open("if (" + Check(type, 1, false, c) + " && " + Check(key.Type, 2, false, c) + ")");
                w.Line(Self(type, c) + ";");
                w.Line(Cast(key.Type, 2, "index", true, false, c) + ";");
                w.Line(LuaAPI + ".lua_pushboolean(L, true);");
                w.Line(Push(indexer.ReturnType, IndexerGet(indexer, "gen_to_be_invoked", "index", c), c) + ";");
                w.Line("return 2;");
                w.Close();
            }
            Catch(w);
            w.Line(LuaAPI + ".lua_pushboolean(L, false);");
            w.Line("return 1;");
            w.Close();
        }

        static void NewIndexer(Writer w, ClassInfo info, Compilation c)
        {
            var type = info.Type;
            Function(w, "__NewIndexer", "internal static");
            w.Line(FindTranslator);
            Try(w);
            foreach (var setter in info.NewIndexers)
            {
                var key = setter.Parameters[0];
                var valueType = setter.Parameters[1].Type;
                w.Open("if (" + Check(type, 1, false, c) + " && " + Check(key.Type, 2, false, c) + " && " + Check(valueType, 3, false, c) + ")");
                w.Line(Self(type, c) + ";");
                w.Line(Cast(key.Type, 2, "key", true, false, c) + ";");
                if (IsStruct(valueType, c) || MemberContext.IsRefReadOnly(key))
                {
                    w.Line(Cast(valueType, 3, "gen_value", true, false, c) + ";");
                    w.Line(IndexerSet(setter, "gen_to_be_invoked", "key", "gen_value", c) + ";");
                }
                else
                {
                    w.Line(Cast(valueType, 3, "gen_to_be_invoked[key]", false, false, c) + ";");
                }
                w.Line(LuaAPI + ".lua_pushboolean(L, true);");
                w.Line("return 1;");
                w.Close();
            }
            Catch(w);
            w.Line(LuaAPI + ".lua_pushboolean(L, false);");
            w.Line("return 1;");
            w.Close();
        }

        static void Operator(Writer w, ClassInfo info, OperatorGroup op, Compilation c)
        {
            var type = info.Type;
            Function(w, OpMeta[op.Name]);
            if (op.Name != "op_UnaryNegation" && op.Name != "op_OnesComplement")
            {
                Try(w);
                w.Line(FindTranslator);
                foreach (var o in op.Overloads)
                {
                    var left = o.Parameters[0].Type;
                    var right = o.Parameters[1].Type;
                    w.Open("if (" + Check(left, 1, false, c) + " && " + Check(right, 2, false, c) + ")");
                    w.Line(Cast(left, 1, "leftside", true, false, c) + ";");
                    w.Line(Cast(right, 2, "rightside", true, false, c) + ";");
                    w.Line(Push(o.ReturnType, "leftside " + OpSymbol[op.Name] + " rightside", c) + ";");
                    w.Line("return 1;");
                    w.Close();
                }
                Catch(w);
                w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to right hand of " + OpSymbol[op.Name] + " operator, need " + Display(type) + "!") + ");");
            }
            else
            {
                w.Line(FindTranslator);
                Try(w);
                w.Line(Cast(type, 1, "rightside", true, false, c) + ";");
                w.Line(Push(op.Overloads[0].ReturnType, OpSymbol[op.Name] + " rightside", c) + ";");
                Catch(w);
                w.Line("return 1;");
            }
            w.Close();
        }

        static void Method(Writer w, ClassInfo info, MethodGroup group, MemberContext ctx)
        {
            var c = ctx.Compilation;
            var type = info.Type;
            string cs = Names.Cs(type);
            Function(w, "_m_" + group.Key);
            Try(w);
            if (!group.IsStatic || group.Overloads.Any(o => CallNeedsTranslator(o.Method, c))) w.Line(FindTranslator);
            if (!group.IsStatic) w.Line(Self(type, c) + ";");
            bool many = group.Overloads.Count > 1;
            if (many) w.Line("int gen_param_count = " + LuaAPI + ".lua_gettop(L);");
            int offset = group.IsStatic ? 0 : 1;
            foreach (var o in group.Overloads)
            {
                var m = o.Method;
                var ps = o.Parameters;
                int inNum = CountIn(ps), outNum = CountOut(ps);
                bool hasReturn = !m.ReturnsVoid;
                int real = ps.Length - o.DefaultCount;
                bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
                if (many)
                {
                    var cond = new StringBuilder("gen_param_count " + (hasParams ? ">=" : "==") + " " + (inNum + offset - o.DefaultCount - (hasParams ? 1 : 0)));
                    int inPos = 0;
                    for (int pi = 0; pi < real; pi++)
                    {
                        var p = ps[pi];
                        bool isV = hasParams && pi == ps.Length - 1;
                        var t = isV ? ((IArrayTypeSymbol)p.Type).ElementType : p.Type;
                        if (p.RefKind != RefKind.Out)
                        {
                            inPos++;
                            cond.Append(" && ").Append(Check(t, inPos + offset, isV, c));
                        }
                    }
                    w.Open("if (" + cond + ")");
                }
                else
                {
                    w.Line("{");
                    w.Indent++;
                }
                if (m.Name == "get_Item" && m.MethodKind == MethodKind.PropertyGet)
                {
                    w.Line(Cast(m.Parameters[0].Type, 2, "key", true, false, c) + ";");
                    w.Line(Push(m.ReturnType, "gen_to_be_invoked[key]", c) + ";");
                }
                else if (m.Name == "set_Item" && m.MethodKind == MethodKind.PropertySet)
                {
                    w.Line(Cast(m.Parameters[0].Type, 2, "key", true, false, c) + ";");
                    w.Line(Cast(m.Parameters[1].Type, 3, "gen_value", true, false, c) + ";");
                    w.Line("gen_to_be_invoked[key] = gen_value;");
                }
                else
                {
                    int inPos = 0;
                    for (int pi = 0; pi < real; pi++)
                    {
                        var p = ps[pi];
                        if (p.RefKind != RefKind.Out)
                        {
                            inPos++;
                            w.Line(Cast(p.Type, inPos + offset, Local(p), true, hasParams && pi == ps.Length - 1, c) + ";");
                        }
                        else
                        {
                            w.Line(Cs(p.Type, c) + " " + Local(p) + ";");
                        }
                    }
                    string call;
                    if (o.IsExtension)
                    {
                        string args = Arguments(ps, real);
                        call = Names.Cs(m.ContainingType) + "." + Escape(m.Name) + "(gen_to_be_invoked" + (args.Length > 0 ? ", " + args : "") + ")";
                    }
                    else if (group.IsStatic)
                    {
                        call = Names.Cs(m.ContainingType) + "." + Escape(m.Name) + "(" + Arguments(ps, real) + ExplicitDefaults(o, real) + ")";
                    }
                    else
                    {
                        call = "gen_to_be_invoked." + Escape(m.Name) + "(" + Arguments(ps, real) + ExplicitDefaults(o, real) + ")";
                    }
                    w.Line((hasReturn ? "var gen_ret = " : "") + call + ";");
                    int results = outNum + (hasReturn ? 1 : 0);
                    if (results > 10) w.Line("global::XLua.Binding.BindingApi.EnsureStack(L, " + results + ");");
                    if (hasReturn) w.Line(Push(m.ReturnType, "gen_ret", c) + ";");
                    PushResults(w, ps, real, offset, c);
                }
                if (NeedUpdate(type, c) && !group.IsStatic) w.Line(Update(type, 1, "gen_to_be_invoked", c) + ";");
                w.Line("return " + (outNum + (hasReturn ? 1 : 0)) + ";");
                w.Close();
            }
            Catch(w);
            if (many) w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to " + Display(type) + "." + group.Overloads[0].Method.Name + "!") + ");");
            w.Close();
        }

        static void Getter(Writer w, ClassInfo info, Accessor g, Compilation c)
        {
            var type = info.Type;
            Function(w, "_g_get_" + g.Name);
            Try(w);
            if (!g.IsStatic || !JustLuaType(g.Type, c)) w.Line(FindTranslator);
            if (!g.IsStatic)
            {
                w.Line(Self(type, c) + ";");
                w.Line(Push(g.Type, "gen_to_be_invoked." + Escape(g.Name), c) + ";");
            }
            else
            {
                w.Line(Push(g.Type, Names.Cs(type) + "." + Escape(g.Name), c) + ";");
            }
            Catch(w);
            w.Line("return 1;");
            w.Close();
        }

        static void Setter(Writer w, ClassInfo info, Accessor s, Compilation c)
        {
            var type = info.Type;
            bool isStruct = IsStruct(s.Type, c);
            Function(w, "_s_set_" + s.Name);
            Try(w);
            if (!s.IsStatic || !JustLuaType(s.Type, c)) w.Line(FindTranslator);
            if (!s.IsStatic)
            {
                w.Line(Self(type, c) + ";");
                if (isStruct)
                {
                    w.Line(Cast(s.Type, 2, "gen_value", true, false, c) + ";");
                    w.Line("gen_to_be_invoked." + Escape(s.Name) + " = gen_value;");
                }
                else
                {
                    w.Line(Cast(s.Type, 2, "gen_to_be_invoked." + Escape(s.Name), false, false, c) + ";");
                }
                if (NeedUpdate(type, c)) w.Line(Update(type, 1, "gen_to_be_invoked", c) + ";");
            }
            else if (isStruct)
            {
                w.Line(Cast(s.Type, 1, "gen_value", true, false, c) + ";");
                w.Line(Names.Cs(type) + "." + Escape(s.Name) + " = gen_value;");
            }
            else
            {
                w.Line(Cast(s.Type, 1, Names.Cs(type) + "." + Escape(s.Name), false, false, c) + ";");
            }
            Catch(w);
            w.Line("return 0;");
            w.Close();
        }

        static void Event(Writer w, ClassInfo info, EventInfo e, Compilation c)
        {
            var type = info.Type;
            string target = e.IsStatic ? Names.Cs(type) : "gen_to_be_invoked";
            Function(w, "_e_" + e.Name);
            Try(w);
            w.Line(FindTranslator);
            w.Line("int gen_param_count = " + LuaAPI + ".lua_gettop(L);");
            int argument = e.IsStatic ? 2 : 3;
            if (!e.IsStatic) w.Line(Self(type, c) + ";");
            w.Line(Cast(e.Type, argument, "gen_delegate", true, false, c) + ";");
            w.Open("if (gen_delegate == null)");
            w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("#" + argument + " need " + Display(e.Type) + "!") + ");");
            w.Close();
            if (e.IsStatic)
            {
                if (e.CanAdd)
                {
                    w.Open("if (gen_param_count == 2 && " + LuaAPI + ".xlua_is_eq_str(L, 1, \"+\"))");
                    w.Line(target + "." + Escape(e.Name) + " += gen_delegate;").Line("return 0;");
                    w.Close();
                }
                if (e.CanRemove)
                {
                    w.Open("if (gen_param_count == 2 && " + LuaAPI + ".xlua_is_eq_str(L, 1, \"-\"))");
                    w.Line(target + "." + Escape(e.Name) + " -= gen_delegate;").Line("return 0;");
                    w.Close();
                }
                Catch(w);
                w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to " + Display(type) + "." + e.Name + "!") + ");");
            }
            else
            {
                w.Open("if (gen_param_count == 3)");
                if (e.CanAdd)
                {
                    w.Open("if (" + LuaAPI + ".xlua_is_eq_str(L, 2, \"+\"))");
                    w.Line(target + "." + Escape(e.Name) + " += gen_delegate;").Line("return 0;");
                    w.Close();
                }
                if (e.CanRemove)
                {
                    w.Open("if (" + LuaAPI + ".xlua_is_eq_str(L, 2, \"-\"))");
                    w.Line(target + "." + Escape(e.Name) + " -= gen_delegate;").Line("return 0;");
                    w.Close();
                }
                w.Close();
                Catch(w);
                w.Line(LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to " + Display(type) + "." + e.Name + "!") + ");");
                w.Line("return 0;");
            }
            w.Close();
        }

        // ---- enum wrappers (LuaEnumWrap.tpl.txt) ----

        static TypeModel EnumModel(INamedTypeSymbol type, MemberContext ctx)
        {
            var c = ctx.Compilation;
            string luaName = Names.Clr(type);
            string cs = Names.Cs(type);
            string className = ClassName(luaName);
            var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsStatic && f.DeclaredAccessibility == Accessibility.Public && !ctx.IsMemberBlackListed(f)).ToList();
            // Any [Obsolete] here, also outside Unity (LuaEnumWrap.tpl.txt IsObsolute).
            var toGen = fields.Where(f => !Names.IsObsolete(f, false)).ToList();
            bool listed = toGen.Count <= 20;
            var w = new Writer { Indent = 1 };
            w.Line("// " + luaName);
            w.Open("internal static class " + className);
            w.Open("internal static void __Register(global::System.IntPtr L)");
            w.Line(FindTranslator);
            w.Line(Utils + ".BeginObjectRegister(typeof(" + cs + "), L, translator, 0, 0, 0, 0);");
            w.Line(Utils + ".EndObjectRegister(typeof(" + cs + "), L, translator, null, null, null, null, null);");
            w.Line(Utils + ".BeginClassRegister(typeof(" + cs + "), L, null, " + (fields.Count + 1) + ", 0, 0);");
            if (listed)
            {
                foreach (var f in toGen)
                    w.Line(Utils + ".RegisterObject(L, translator, " + Utils + ".CLS_IDX, " + Names.Literal(f.Name) + ", " + cs + "." + Escape(f.Name) + ");");
            }
            else
            {
                w.Line(Utils + ".RegisterEnumType(L, typeof(" + cs + "));");
            }
            w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, \"__CastFrom\", __CastFrom);");
            w.Line(Utils + ".EndClassRegister(typeof(" + cs + "), L, translator);");
            w.Close();

            Function(w, "__CastFrom");
            w.Line(FindTranslator);
            w.Line(LuaTypes + " lua_type = " + LuaAPI + ".lua_type(L, 1);");
            w.Open("if (lua_type == " + LuaTypes + ".LUA_TNUMBER)");
            // Lua's integer rule for the underlying type, which throws for a
            // float without an integer value (LuaEnumWrap.tpl.txt).
            Try(w);
            w.Line(Push(type, "(" + cs + ")" + IntegerArgCaster[Names.Clr(type.EnumUnderlyingType)] + "(L, 1)", c) + ";");
            Catch(w);
            w.Close();
            if (toGen.Count > 0)
            {
                w.Open("else if (lua_type == " + LuaTypes + ".LUA_TSTRING)");
                if (listed)
                {
                    bool first = true;
                    foreach (var f in toGen)
                    {
                        w.Open((first ? "" : "else ") + "if (" + LuaAPI + ".xlua_is_eq_str(L, 1, " + Names.Literal(f.Name) + "))");
                        w.Line(Push(type, cs + "." + Escape(f.Name), c) + ";");
                        w.Close();
                        first = false;
                    }
                    w.Open("else");
                    w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid string for " + Display(type) + "!") + ");");
                    w.Close();
                }
                else
                {
                    Try(w);
                    w.Line("translator.TranslateToEnumToTop(L, typeof(" + cs + "), 1);");
                    w.Indent--;
                    w.Line("}");
                    w.Line("catch (global::System.Exception e)");
                    w.Line("{");
                    w.Line("    return " + LuaAPI + ".luaL_error(L, \"cast to \" + typeof(" + cs + ") + \" exception:\" + e);");
                    w.Line("}");
                }
                w.Close();
            }
            w.Open("else");
            w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid lua type for " + Display(type) + "! Expect number or string, got + ") + " + lua_type);");
            w.Close();
            w.Line("return 1;");
            w.Close();
            w.Close();

            var json = new Json();
            json.BeginObject();
            json.Key("type").Value(luaName);
            json.Key("kind").Value("enum");
            json.Key("fields").BeginArray();
            foreach (var f in toGen) json.Value(f.Name);
            json.EndArray();
            json.Key("fieldCount").Value(fields.Count);
            json.Key("registerEnumType").Value(!listed);
            json.EndObject();

            // Utils.RegisterEnumType and TranslateToEnumToTop read the value names by reflection.
            string obfuz = listed || toGen.Count == 0 ? "" : LinkLine(type, EnumFields, "");
            return new TypeModel { LuaName = luaName, TypeofExpr = Names.Typeof(type), ClassName = className, Source = w.ToString(), Manifest = json.ToString(), Dropped = "", ObfuzLines = obfuz };
        }

        // ---- the manifest (section 2 row 12) ----

        // A method as the manifest lists it: Type::Name`arity(parameter types), and
        // the number of trailing default values the overload leaves out. A
        // constructed generic method is listed as its definition, but in the
        // type that declares it as wrapped: a member of List<int> is
        // List`1[System.Int32]::Add(System.Int32), as the legacy dump writes it
        // (Generator.cs surfaceSignature), not List`1[T]::Add(T).
        public static string Signature(IMethodSymbol m, int defaults)
        {
            var d = m.IsGenericMethod ? m.ConstructedFrom : m;
            string name = d.MethodKind == MethodKind.Constructor ? ".ctor" : d.Name;
            return Names.Clr(d.ContainingType) + "::" + name + (d.IsGenericMethod ? "`" + d.TypeParameters.Length : "")
                + "(" + string.Join(",", d.Parameters.Select(MemberContext.ParameterTypeName)) + ")" + (defaults > 0 ? " -" + defaults : "");
        }

        static string ClassManifest(ClassInfo info, string luaName)
        {
            var json = new Json();
            json.BeginObject();
            json.Key("type").Value(luaName);
            json.Key("kind").Value("class");
            json.Key("constructors").BeginArray();
            foreach (var o in info.Constructors) json.Value(Signature(o.Method, o.DefaultCount));
            json.EndArray();
            json.Key("methods").BeginArray();
            foreach (var g in info.Methods.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                json.BeginObject().Key("name").Value(g.Name).Key("static").Value(g.IsStatic).Key("overloads").BeginArray();
                foreach (var o in g.Overloads) json.Value(Signature(o.Method, o.DefaultCount));
                json.EndArray().EndObject();
            }
            json.EndArray();
            json.Key("getters").BeginArray();
            foreach (var g in info.Getters.OrderBy(g => g.Name, StringComparer.Ordinal))
                json.Value(g.Name + (g.IsStatic ? " static" : "") + (g.ReadOnly ? " readonly" : ""));
            json.EndArray();
            json.Key("setters").BeginArray();
            foreach (var s in info.Setters.OrderBy(s => s.Name, StringComparer.Ordinal)) json.Value(s.Name + (s.IsStatic ? " static" : ""));
            json.EndArray();
            json.Key("operators").BeginArray();
            foreach (var op in info.Operators.OrderBy(o => o.Name, StringComparer.Ordinal))
            {
                json.BeginObject().Key("name").Value(op.Name).Key("overloads").BeginArray();
                foreach (var m in op.Overloads) json.Value(Signature(m, 0));
                json.EndArray().EndObject();
            }
            json.EndArray();
            json.Key("indexers").BeginArray();
            foreach (var m in info.Indexers) json.Value(Signature(m, 0));
            json.EndArray();
            json.Key("newindexers").BeginArray();
            foreach (var m in info.NewIndexers) json.Value(Signature(m, 0));
            json.EndArray();
            json.Key("events").BeginArray();
            foreach (var e in info.Events.OrderBy(e => e.Name, StringComparer.Ordinal))
                json.Value(e.Name + (e.IsStatic ? " static" : "") + (e.CanAdd ? " add" : "") + (e.CanRemove ? " remove" : ""));
            json.EndArray();
            json.Key("lazy").BeginArray();
            foreach (var l in info.Lazy.Where(l => l.Reason == null).OrderBy(l => l.Name, StringComparer.Ordinal).ThenBy(l => l.Index, StringComparer.Ordinal))
                json.Value(l.Index + " " + l.Name + " " + l.MemberType + (l.IsStatic ? " static" : ""));
            json.EndArray();
            // Members the legacy generator drops and this one leaves to reflection.
            json.Key("fallback").BeginArray();
            foreach (var l in info.Lazy.Where(l => l.Reason != null).OrderBy(l => l.Name, StringComparer.Ordinal).ThenBy(l => l.Index, StringComparer.Ordinal))
                json.Value(l.Index + " " + l.Name + " " + l.MemberType + (l.IsStatic ? " static" : ""));
            json.EndArray();
            json.EndObject();
            return json.ToString();
        }
    }

    // A small JSON writer: deterministic, no dependencies (netstandard2.0).
    internal sealed class Json
    {
        readonly StringBuilder sb = new StringBuilder();
        bool needComma;

        void Separator()
        {
            if (needComma) sb.Append(',');
            needComma = false;
        }

        public Json BeginObject() { Separator(); sb.Append('{'); return this; }
        public Json EndObject() { sb.Append('}'); needComma = true; return this; }
        public Json BeginArray() { Separator(); sb.Append('['); return this; }
        public Json EndArray() { sb.Append(']'); needComma = true; return this; }

        public Json Key(string key)
        {
            Separator();
            String(key);
            sb.Append(':');
            return this;
        }

        public Json Value(string value) { Separator(); String(value); needComma = true; return this; }
        public Json Value(bool value) { Separator(); sb.Append(value ? "true" : "false"); needComma = true; return this; }
        public Json Value(int value) { Separator(); sb.Append(value); needComma = true; return this; }

        public Json Raw(string json) { Separator(); sb.Append(json); needComma = true; return this; }

        void String(string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public override string ToString() { return sb.ToString(); }
    }
}
