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
        public string StructUses = "";  // "struct CLR name|member" lines: the structs the bound members pass, the first member each (XLSG014)

        public string HintName
        {
            get { return ClassName + ".g.cs"; }
        }

        public bool Equals(TypeModel o)
        {
            return o != null && LuaName == o.LuaName && TypeofExpr == o.TypeofExpr && ClassName == o.ClassName && Source == o.Source
                && Manifest == o.Manifest && Dropped == o.Dropped && Fallbacks == o.Fallbacks && LinkXml == o.LinkXml
                && ObfuzLines == o.ObfuzLines && StructUses == o.StructUses;
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
        public string UnboundStructs = "";     // "type|C# name|fields" lines: GCOptimize structs Lua cannot read without reflection (XLSG014)
        public string ExposedStructs = "";     // "type|C# name|fields|member|assembly" lines: the same for a referenced assembly's struct that a bound member passes (XLSG014)
        public string LinkXml = "";            // the link.xml fragment (LinkXml.cs), or ""
        public string HotfixBridges = "";      // the members of the HotfixBridges class (HotfixBridges.cs), or ""
        public string HotfixManifest = "[]";   // JSON: the SignatureKey of every hotfix bridge
        public string ObfuzRules = "";         // the obfuz rule fragment (ObfuzRules.cs), or ""
        public string GcmMismatch = "";        // "define|runtime" when the GEN_CODE_MINIMIZE mode differs between this assembly and XLua (XLSG013)
        public bool Gcm;                       // the wrappers are GEN_CODE_MINIMIZE wrappers

        public bool Equals(WrapperSet o)
        {
            return o != null && Types.Equals(o.Types) && Interfaces.Equals(o.Interfaces) && ExtensionDelegates == o.ExtensionDelegates
                && ExtensionRegistration == o.ExtensionRegistration && ExtensionManifest == o.ExtensionManifest
                && BoundElsewhere == o.BoundElsewhere && NotWrapped == o.NotWrapped && DelegateBridges == o.DelegateBridges
                && DelegatePragmas == o.DelegatePragmas && DelegateRegistration == o.DelegateRegistration
                && DelegateManifest == o.DelegateManifest && InterfaceManifest == o.InterfaceManifest
                && StructsSource == o.StructsSource && PackSource == o.PackSource && StructRegistration == o.StructRegistration
                && StructManifest == o.StructManifest && PackManifest == o.PackManifest && NotPackable == o.NotPackable
                && UnboundStructs == o.UnboundStructs && ExposedStructs == o.ExposedStructs
                && LinkXml == o.LinkXml && HotfixBridges == o.HotfixBridges && HotfixManifest == o.HotfixManifest
                && ObfuzRules == o.ObfuzRules && GcmMismatch == o.GcmMismatch && Gcm == o.Gcm;
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
        // GEN_CODE_MINIMIZE (LuaClassWrapGCM.tpl.txt): a member is a private
        // extension method of the translator, bound as a CSharpWrapper delegate
        // closed over it. Lua calls the native trampoline
        // (xlua_push_csharp_wrapper), whose single C# entry
        // (StaticLuaCallbacks.CSharpWrapperCallerImpl) finds the translator,
        // passes lua_gettop and catches exceptions. Private, so that the
        // _m_<name> of one wrapper class is not a candidate in another's
        // registration (CS0121).
        const string GcmParameters = "(this " + Translator + " translator, global::System.IntPtr L, int gen_param_count)";

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
                Gcm = RuntimeIsGcm(compilation),
            };
            bool gcmDefined = GcmDefined(compilation);
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
            fingerprint.Append(ctx.ObsoleteErrorsOnly).Append(ctx.OldCompiler).Append(XLuaBindingGenerator.ApiVersion).Append(ctx.Gcm ? "|gcm" : "");
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
            // Not in xLua's own assembly (an asmdef in Unity): its UnityEngine defaults
            // (GenAttributes.cs) are not the project's configuration, and nothing there
            // could bind them.
            bool runtimeItself = SymbolEqualityComparer.Default.Equals(
                compilation.GetTypeByMetadataName("XLua.Binding.BindingApi").ContainingAssembly, compilation.Assembly);
            bool strictMinimal = NoReflectionDefined(compilation) && !runtimeItself;
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
                Gcm = ctx.Gcm,
                GcmMismatch = gcmDefined != ctx.Gcm ? (gcmDefined ? "on" : "off") + "|" + (ctx.Gcm ? "with" : "without") : "",
            };
            ExtensionMap(set2, compilation, entries, typed, luaCallCSharp, ctx);
            var callLuaUses = new List<string>();
            CallLuaBridges(set2, compilation, entries, typed, ctx, notWrapped, callLuaUses, ct);
            // XLSG014: before the struct set is trimmed to what the wrappers
            // use (a Nullable<T> or an array element is pushed boxed).
            var unbound = strictMinimal ? UnboundStructs(structs, ctx, elsewhere, callLuaUses) : new List<string>();
            var exposed = strictMinimal ? ExposedStructs(structs, ctx, elsewhere, models, callLuaUses) : new List<string>();
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
            set2.UnboundStructs = string.Join("\n", unbound);
            set2.ExposedStructs = string.Join("\n", exposed);
            var reflectionUse = ReflectionUseLinkLines(compilation, entries, typed);
            set2.LinkXml = LinkXmlDocument(set2.Types.SelectMany(t => t.LinkXml.Split('\n')).Concat(reflectionUse)
                .Concat(BoundTypeLinkLines(compilation, luaCallCSharp, LinkAll)));
            set2.ObfuzRules = ObfuzRulesDocument(set2.Types.SelectMany(t => t.ObfuzLines.Split('\n')).Concat(reflectionUse)
                .Concat(BoundTypeLinkLines(compilation, luaCallCSharp, KeepName)));
            return set2;
        }

        // ---- GEN_CODE_MINIMIZE ----

        // The mode follows the define (Player Settings scripting defines or
        // csc.rsp), as the legacy generator's GCM templates do. The minimal
        // preset implies it unless XLUA_NO_GCM is defined: the rule of the
        // <xlua-preset> blocks that Tools/presets.py writes into xLua's files
        // (#if XLUA_PRESET_MINIMAL / #if !XLUA_NO_GCM / #define
        // GEN_CODE_MINIMIZE), so that this assembly and xLua agree.
        internal static bool GcmDefined(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.Options is CSharpParseOptions o) return GcmDefined(o.PreprocessorSymbolNames);
            }
            return false;
        }

        internal static bool GcmDefined(IEnumerable<string> symbols)
        {
            var set = new HashSet<string>(symbols);
            return set.Contains("GEN_CODE_MINIMIZE") || (set.Contains("XLUA_PRESET_MINIMAL") && !set.Contains("XLUA_NO_GCM"));
        }

        // ---- XLUA_NO_REFLECTION ----

        // Strict minimal: XLUA_NO_REFLECTION, which XLUA_PRESET_MINIMAL implies
        // unless XLUA_REFLECTION is defined (the rule of the <xlua-preset>
        // blocks, as in GcmDefined). xLua then reaches no member of a type
        // without generated bindings.
        internal static bool NoReflectionDefined(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.Options is CSharpParseOptions o) return NoReflectionDefined(o.PreprocessorSymbolNames);
            }
            return false;
        }

        internal static bool NoReflectionDefined(IEnumerable<string> symbols)
        {
            var set = new HashSet<string>(symbols);
            return set.Contains("XLUA_NO_REFLECTION") || (set.Contains("XLUA_PRESET_MINIMAL") && !set.Contains("XLUA_REFLECTION"));
        }

        // XLSG014 (owner decision 2026-10-28): [GCOptimize] gives a struct its
        // pusher and reader, not a wrapper, so without reflection Lua cannot
        // read or set its fields unless the struct is also [LuaCallCSharp], here
        // or in a referenced assembly with generated bindings. A packable
        // PackAsTable struct is pushed as a Lua table, whose fields need no
        // wrapper. Only the structs this assembly configures: a referenced
        // assembly reports its own. Reported whether or not anything passes the
        // struct; since owner decision Q58 of 2026-10-28 the warning names the
        // first [CSharpCallLua] delegate or interface that passes it, if any
        // (callLuaUses, CallLuaStructUses), as the last part of the line.
        static List<string> UnboundStructs(StructSet structs, MemberContext ctx, Dictionary<string, string> elsewhere, List<string> callLuaUses)
        {
            var users = FirstUsers(callLuaUses);
            return structs.Values.Concat(structs.Tables)
                .Where(s => s.Own && !s.IsEnum && s.Type.IsValueType && s.Fields.Count > 0 && !(s.Flag == 1 && s.Size != -1)
                    && !ctx.Wrapped.Contains(s.Clr) && !elsewhere.ContainsKey(s.Clr))
                .Select(s =>
                {
                    string user;
                    return s.Clr + "|" + s.Type.ToDisplayString() + "|" + string.Join(", ", s.Fields.Select(f => f.Name))
                        + "|" + (users.TryGetValue(s.Clr, out user) ? user : "");
                })
                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        // "CLR name|user" lines as a map, the first line of each struct.
        static Dictionary<string, string> FirstUsers(IEnumerable<string> lines)
        {
            var users = new Dictionary<string, string>();
            foreach (var line in lines)
            {
                int bar = line.IndexOf('|');
                string clr = line.Substring(0, bar);
                if (!users.ContainsKey(clr)) users.Add(clr, line.Substring(bar + 1));
            }
            return users;
        }

        // XLSG014 for the structs a referenced assembly configures (owner
        // decision Q41 of 2026-10-28), such as xLua's UnityEngine defaults in
        // GenAttributes.cs: this assembly pushes them without boxing, but
        // without reflection Lua reads their fields only through a wrapper.
        // Reported only where a member this assembly binds passes one between
        // Lua and C# (its parameters, result, property or field type), naming
        // the first such member: a project that never exposes Vector3 hears
        // nothing about it. Since owner decision Q58 of 2026-10-28 also where a
        // [CSharpCallLua] delegate or interface of this assembly passes one
        // (callLuaUses), named when no bound member passes it.
        static List<string> ExposedStructs(StructSet structs, MemberContext ctx, Dictionary<string, string> elsewhere, List<TypeModel> models,
            List<string> callLuaUses)
        {
            var candidates = new Dictionary<string, StructType>();
            foreach (var s in structs.Values.Concat(structs.Tables))
            {
                if (!s.Own && !s.IsEnum && s.Type.IsValueType && s.Fields.Count > 0 && !(s.Flag == 1 && s.Size != -1)
                    && !ctx.Wrapped.Contains(s.Clr) && !elsewhere.ContainsKey(s.Clr) && !candidates.ContainsKey(s.Clr))
                {
                    candidates.Add(s.Clr, s);
                }
            }
            var first = new Dictionary<string, string>();
            if (candidates.Count == 0) return new List<string>();
            foreach (var m in models.OrderBy(m => m.LuaName, StringComparer.Ordinal))
            {
                foreach (var line in m.StructUses.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (candidates.ContainsKey(parts[0]) && !first.ContainsKey(parts[0])) first.Add(parts[0], "'" + parts[1] + "'");
                }
            }
            foreach (var kv in FirstUsers(callLuaUses))
            {
                if (candidates.ContainsKey(kv.Key) && !first.ContainsKey(kv.Key)) first.Add(kv.Key, kv.Value);
            }
            return first.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
            {
                var s = candidates[kv.Key];
                return s.Clr + "|" + s.Type.ToDisplayString() + "|" + string.Join(", ", s.Fields.Select(f => f.Name)) + "|" + kv.Value
                    + "|" + (s.Type.ContainingAssembly == null ? "" : s.Type.ContainingAssembly.Name);
            }).ToList();
        }

        // The struct types (not primitives, enums, decimal or Nullable<T>, but
        // the T of a Nullable<T> or an array) that the bound members of a type
        // pass between Lua and C#, with the first member that does, as
        // "CLR name|C# member" lines (XLSG014, Q41).
        static string StructUses(INamedTypeSymbol type, ClassInfo info)
        {
            var first = new Dictionary<string, string>();
            string owner = type.ToDisplayString();
            Action<ITypeSymbol, string> use = (t, member) => UseStruct(first, t, owner + "." + member);
            Action<IMethodSymbol, IEnumerable<IParameterSymbol>, string> method = (m, parameters, member) =>
            {
                use(m.ReturnType, member);
                foreach (var p in parameters) use(p.Type, member);
            };
            foreach (var o in info.Constructors) method(o.Method, o.Parameters, "ctor");
            foreach (var g in info.Methods) foreach (var o in g.Overloads) method(o.Method, o.Parameters, g.Name);
            foreach (var g in info.Operators) foreach (var o in g.Overloads) method(o, o.Parameters, g.Name);
            foreach (var a in info.Getters) use(a.Type, a.Name);
            foreach (var a in info.Setters) use(a.Type, a.Name);
            foreach (var i in info.Indexers) method(i, i.Parameters, "this[]");
            foreach (var i in info.NewIndexers) method(i, i.Parameters, "this[]");
            return string.Join("\n", first.Select(kv => kv.Key + "|" + kv.Value));
        }

        // Records user as the first that passes t, when t is a struct (not a
        // primitive, enum, decimal or Nullable<T>; the T of a Nullable<T> or an
        // array) that first does not have yet (XLSG014).
        static void UseStruct(Dictionary<string, string> first, ITypeSymbol t, string user)
        {
            while (t is IArrayTypeSymbol array) t = array.ElementType;
            if (t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) t = n.TypeArguments[0];
            if (t == null || t.TypeKind != TypeKind.Struct || MemberContext.IsPrimitive(t) || t.SpecialType == SpecialType.System_Decimal) return;
            string clr = Names.Clr(t);
            if (!first.ContainsKey(clr)) first.Add(clr, user);
        }

        // XLSG014 for [CSharpCallLua] delegates and interfaces (owner decision
        // Q58 of 2026-10-28): the structs that the bridged delegates (Invoke's
        // parameters and result) and interfaces (their methods' parameters and
        // results, their properties' and indexers' types) pass between Lua and
        // C#, as "CLR name|user" lines, the first user each, delegates and
        // interfaces in name order. user is the phrase the warning prints.
        static List<string> CallLuaStructUses(List<INamedTypeSymbol> delegates, List<KeyValuePair<INamedTypeSymbol, List<ISymbol>>> interfaces)
        {
            var first = new Dictionary<string, string>();
            foreach (var d in delegates.OrderBy(d => d.ToDisplayString(), StringComparer.Ordinal))
            {
                var invoke = d.DelegateInvokeMethod;
                string user = "the [CSharpCallLua] delegate '" + d.ToDisplayString() + "'";
                if (!invoke.ReturnsVoid) UseStruct(first, invoke.ReturnType, user);
                foreach (var p in invoke.Parameters) UseStruct(first, p.Type, user);
            }
            foreach (var kv in interfaces.OrderBy(kv => kv.Key.ToDisplayString(), StringComparer.Ordinal))
            {
                foreach (var member in kv.Value)
                {
                    string user = "the [CSharpCallLua] interface member '" + kv.Key.ToDisplayString() + "."
                        + (member is IPropertySymbol ip && ip.IsIndexer ? "this[]" : member.Name) + "'";
                    if (member is IMethodSymbol m)
                    {
                        if (!m.ReturnsVoid) UseStruct(first, m.ReturnType, user);
                        foreach (var p in m.Parameters) UseStruct(first, p.Type, user);
                    }
                    else if (member is IPropertySymbol prop)
                    {
                        UseStruct(first, prop.Type, user);
                        foreach (var p in prop.Parameters) UseStruct(first, p.Type, user);
                    }
                }
            }
            return first.Select(kv => kv.Key + "|" + kv.Value).ToList();
        }

        // XLua compiled with GEN_CODE_MINIMIZE declares XLua.CSharpWrapper, and
        // its Utils.RegisterFunc and the class register helpers take it instead
        // of lua_CSFunction. The wrappers follow XLua when the define of this
        // assembly differs (XLSG013): only XLua's mode compiles and runs.
        internal static bool RuntimeIsGcm(Compilation compilation)
        {
            var wrapper = compilation.GetTypeByMetadataName("XLua.CSharpWrapper");
            return wrapper != null && wrapper.TypeKind == TypeKind.Delegate;
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
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, HashSet<string>> referencedNames
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, HashSet<string>>();
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
            // The assemblies the type's assembly references that this
            // compilation does not: their types are error types here, so the
            // same DLL gives another model in an assembly that references them
            // (a struct parameter read as an object, for example).
            var referenced = referencedNames.GetValue(compilation, c => new HashSet<string>(c.ReferencedAssemblyNames.Select(a => a.Name)));
            foreach (var module in assembly.Modules)
            {
                foreach (var name in module.ReferencedAssemblies.Select(a => a.Name).Where(n => !referenced.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
                    sb.Append('-').Append(name);
            }
            sb.Append('|');
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

        // The types of [assembly: X(typeof(T), ...)] and [assembly: X(typeof(A), typeof(B)...)] by Lua name.
        static Dictionary<string, ITypeSymbol> TypedConfiguration(Compilation compilation)
        {
            var result = new Dictionary<string, ITypeSymbol>();
            foreach (var a in compilation.Assembly.GetAttributes())
            {
                if (a.AttributeClass == null || a.AttributeClass.ContainingNamespace == null || a.AttributeClass.ContainingNamespace.ToDisplayString() != "XLua") continue;
                var kind = Config.KindOf(a);
                IEnumerable<ITypeSymbol> types = kind.HasValue && Config.TakesTypes(kind.Value) ? Config.ConfiguredTypes(a)
                    : a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Kind == TypedConstantKind.Type && a.ConstructorArguments[0].Value is ITypeSymbol t
                        ? new[] { t } : Enumerable.Empty<ITypeSymbol>();
                foreach (var type in types)
                {
                    string name = Names.Clr(type);
                    if (!result.ContainsKey(name)) result.Add(name, type);
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

        // ObjectCasters.IsStrictArg: a struct or enum that Lua must pass as one.
        // An argument of such a type without a generated reader is read with
        // BindingApi.GetStructArg, which raises for nil (and, for a struct, a
        // number) as the emitted and reflection wrappers do (the generated
        // readers of GCOptimize types and [LuaCallCSharp] enums raise the same,
        // Structs.cs). Enums since owner decision Q34 of 2026-10-28.
        internal static bool IsStrictArg(ITypeSymbol t)
        {
            return IsStrictStruct(t) || (t.TypeKind == TypeKind.Enum && t.IsValueType);
        }

        // ObjectCasters.IsStrictStruct: a struct other than a primitive, an
        // enum, decimal or Nullable<T>.
        internal static bool IsStrictStruct(ITypeSymbol t)
        {
            return t.IsValueType && !MemberContext.IsPrimitive(t) && t.TypeKind != TypeKind.Enum
                && t.SpecialType != SpecialType.System_Decimal && t.SpecialType != SpecialType.System_IntPtr
                && t.SpecialType != SpecialType.System_UIntPtr
                && (t.OriginalDefinition == null || t.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T);
        }

        // A Nullable<S> of a strict struct S (ObjectCasters.IsStrictResult
        // without S itself).
        internal static bool IsNullableStrictStruct(ITypeSymbol t)
        {
            return t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && IsStrictStruct(n.TypeArguments[0]);
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
            // translator.Assignable<object> in one call: true for every value
            // unless a checker was registered for object.
            if (name == "System.Object")
                body = "global::XLua.Binding.BindingApi.ObjectCheck(translator, L, " + i + ")";
            else if (name == "System.String" || name == "System.Byte[]")
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
            if (isStruct && !luaResult && IsStrictArg(Effective(t, c)) && StructsOf(c).GetterOf(Effective(t, c)) == null)
                return statement + "global::XLua.Binding.BindingApi.GetStructArg(translator, L, " + i + ", out " + variable + ")";
            // A Nullable<S> argument of a strict struct S: BindingApi.GetNullableStructArg,
            // as the emitted and reflection wrappers read it: null for nil or
            // nothing, StructExpected for a value that is no S (owner decision
            // Q62 of 2026-10-28; it was GetObject, which gave null for it).
            // translator.Get reads values C# reads back (Q59).
            if (isStruct && !luaResult && IsNullableStrictStruct(Effective(t, c)))
                return statement + "global::XLua.Binding.BindingApi.GetNullableStructArg(translator, L, " + i + ", out " + variable + ")";
            if (isStruct)
                return statement + StructGet(Effective(t, c), i, variable, c, luaResult);
            // A class, interface or array type: BindingApi.GetObject<T>, which is
            // (T)translator.GetObject(L, i, typeof(T)) without the typeof for a C#
            // object (IL2CPP looks each Type object up under a lock), and
            // GetUnityObjectArg<T> the same way. A value type that gets here (a
            // primitive without a reader, such as UIntPtr) keeps GetObject.
            if (!luaResult && RefusesTables(Effective(t, c)))
                return statement + "global::XLua.Binding.BindingApi.GetUnityObjectArg<" + Cs(t, c) + ">(translator, L, " + i + ")";
            if (!Effective(t, c).IsReferenceType)
                return statement + "(" + Cs(t, c) + ")translator.GetObject(L, " + i + ", typeof(" + Cs(t, c) + "))";
            return statement + "global::XLua.Binding.BindingApi.GetObject<" + Cs(t, c) + ">(translator, L, " + i + ")";
        }

        // ObjectCasters.RefusesTables: a Lua table never converts to
        // UnityEngine.Object or a class derived from it
        // (https://github.com/Tencent/xLua/issues/918). Found by name, as at run time.
        internal static bool RefusesTables(ITypeSymbol t)
        {
            if (t == null || t.TypeKind != TypeKind.Class) return false;
            for (ITypeSymbol b = t; b != null; b = b.BaseType)
            {
                if (b.ToDisplayString() == "UnityEngine.Object") return true;
            }
            return false;
        }

        // TemplateCommon StructGetName: the generated reader of a GCOptimize
        // type or [LuaCallCSharp] enum (Structs.cs), else ObjectTranslator.Get<T>.
        // luaResult: a value C# reads back from Lua, for which the reader gives
        // default(T) where Lua has no value instead of raising (owner decision
        // Q35 of 2026-10-28).
        static string StructGet(ITypeSymbol t, string idx, string variable, Compilation c, bool luaResult = false)
        {
            var s = StructsOf(c).GetterOf(t);
            return s != null ? "Structs.Get_" + s.Id + "(translator, L, " + idx + ", out " + variable + (luaResult ? ", true)" : ")")
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
            public bool Gcm;   // GEN_CODE_MINIMIZE wrappers (GcmParameters)

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
            var w = new Writer { Indent = 1, Gcm = ctx.Gcm };

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
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".OBJ_META_IDX, \"" + OpMeta[op.Name].Replace("Meta", "").ToLowerInvariant() + "\", " + Member(w, OpMeta[op.Name]) + ");");
            foreach (var m in info.Methods.Where(m => !m.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".METHOD_IDX, " + Names.Literal(m.Name) + ", " + Member(w, "_m_" + m.Key) + ");");
            foreach (var e in info.Events.Where(e => !e.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".METHOD_IDX, " + Names.Literal(e.Name) + ", " + Member(w, "_e_" + e.Name) + ");");
            foreach (var g in info.Getters.Where(g => !g.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".GETTER_IDX, " + Names.Literal(g.Name) + ", " + Member(w, "_g_get_" + g.Name) + ");");
            foreach (var s in info.Setters.Where(s => !s.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".SETTER_IDX, " + Names.Literal(s.Name) + ", " + Member(w, "_s_set_" + s.Name) + ");");
            foreach (var l in info.Lazy.Where(l => !l.IsStatic)) LazyRegistration(w, l);
            w.Line(Utils + ".EndObjectRegister(type, L, translator, " + (hasIndexer ? Member(w, "__CSIndexer") : "null") + ", " + (hasNewIndexer ? Member(w, "__NewIndexer") : "null") + ", null, null, null);");
            w.Line();
            w.Line(Utils + ".BeginClassRegister(type, L, " + Member(w, "__CreateInstance") + ", " + clsFields + ", " + clsGetters + ", " + clsSetters + ");");
            foreach (var m in info.Methods.Where(m => m.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, " + Names.Literal(m.Name) + ", " + Member(w, "_m_" + m.Key) + ");");
            foreach (var e in info.Events.Where(e => e.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, " + Names.Literal(e.Name) + ", " + Member(w, "_e_" + e.Name) + ");");
            foreach (var g in info.Getters.Where(g => g.IsStatic && g.ReadOnly))
                w.Line(Utils + ".RegisterObject(L, translator, " + Utils + ".CLS_IDX, " + Names.Literal(g.Name) + ", " + cs + "." + Escape(g.Name) + ");");
            foreach (var g in info.Getters.Where(g => g.IsStatic && !g.ReadOnly))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_GETTER_IDX, " + Names.Literal(g.Name) + ", " + Member(w, "_g_get_" + g.Name) + ");");
            foreach (var s in info.Setters.Where(s => s.IsStatic))
                w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_SETTER_IDX, " + Names.Literal(s.Name) + ", " + Member(w, "_s_set_" + s.Name) + ");");
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
                StructUses = StructUses(type, info),
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

        // The per-member try/catch; GCM wrappers leave it to the C# entry.
        static void Try(Writer w)
        {
            if (w.Gcm) return;
            w.Line("try");
            w.Line("{");
            w.Indent++;
        }

        static void Catch(Writer w)
        {
            if (w.Gcm) return;
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
            if (w.Gcm)
            {
                w.Open("static int " + name + GcmParameters);
                return;
            }
            w.Line(Callback);
            w.Open(visibility + " int " + name + "(global::System.IntPtr L)");
        }

        // A member function as the registration names it: GCM binds it to the translator.
        static string Member(Writer w, string name)
        {
            return w.Gcm ? "translator." + name : name;
        }

        // The translator lookup at the top of a member function; the GCM entry passes it.
        static void FindTranslatorLine(Writer w)
        {
            if (!w.Gcm) w.Line(FindTranslator);
        }

        // The number of arguments: lua_gettop, or the GCM entry's parameter.
        static string Top(Writer w)
        {
            return w.Gcm ? "gen_param_count" : LuaAPI + ".lua_gettop(L)";
        }

        static void ParamCount(Writer w)
        {
            if (!w.Gcm) w.Line("int gen_param_count = " + LuaAPI + ".lua_gettop(L);");
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

        // Overload selection (ObjectCasters.cs OverloadScore, owner decision
        // 2026-10-19): when two candidates can match the same arguments with
        // different costs, every matching candidate is scored with
        // BindingApi.ArgScore and the cheapest is called; equal costs keep this
        // order, a cost of 0 is taken at once. Otherwise the candidates are
        // tried in order, as before. The kinds and the analysis below are
        // OverloadScore's, on Roslyn symbols.
        sealed class Candidate
        {
            public string Head;                  // the checks before Types': the count or the self, or null
            public int Start;                    // the Lua stack position of the first type
            public List<ITypeSymbol> Types = new List<ITypeSymbol>();   // checked types by position from Start
            public bool[] Scored;                // which of Types are scored
            public int Required;                 // Types without a params element
            public bool HasParams;
            public bool ParamsElement;           // the last of Types is a params element: every element is scored
            public bool IgnoresExtra;            // a params array left out with the defaults: extra arguments are dropped
            public string Top;                   // the argument count expression
            public bool DefaultUsed;
            public Action Body;
        }

        const int KindOther = 0, KindInt32 = 1, KindInt64 = 2, KindUInt64 = 3, KindSmallInteger = 4, KindDouble = 5,
            KindSingle = 6, KindDecimal = 7, KindObject = 8, KindString = 9, KindBytes = 10, KindBoolean = 11, KindNullable = 16;
        const int MaskNil = 1, MaskBoolean = 2, MaskLightUserdata = 4, MaskNumber = 8, MaskString = 16, MaskTable = 32,
            MaskFunction = 64, MaskUserdata = 128, MaskAll = 511;
        const int MaskScored = MaskBoolean | MaskLightUserdata | MaskNumber | MaskString | MaskUserdata;

        static ITypeSymbol NullableArgument(ITypeSymbol t)
        {
            return t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? n.TypeArguments[0] : null;
        }

        static int ScoreKind(ITypeSymbol t, Compilation c)
        {
            t = Effective(t, c);
            var inner = NullableArgument(t);
            if (inner != null) return ScoreKind(inner, c) | KindNullable;
            switch (t.SpecialType)
            {
                case SpecialType.System_Int32: return KindInt32;
                case SpecialType.System_Int64: return KindInt64;
                case SpecialType.System_UInt64: return KindUInt64;
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_Char: return KindSmallInteger;
                case SpecialType.System_Double: return KindDouble;
                case SpecialType.System_Single: return KindSingle;
                case SpecialType.System_Decimal: return KindDecimal;
                case SpecialType.System_Object: return KindObject;
                case SpecialType.System_String: return KindString;
                case SpecialType.System_Boolean: return KindBoolean;
            }
            if (t is IArrayTypeSymbol a && a.Rank == 1 && a.ElementType.SpecialType == SpecialType.System_Byte) return KindBytes;
            return KindOther;
        }

        // BindingApi.ArgScore for one argument, or ArgScores for the
        // arguments from idx to last (the elements of a params array).
        static bool IsNumericKind(int kind)
        {
            kind &= KindNullable - 1;
            return kind >= KindInt32 && kind <= KindDecimal;
        }

        static string ScoreCall(ITypeSymbol t, int idx, Compilation c, string last = null)
        {
            int kind = ScoreKind(t, c);
            var underlying = NullableArgument(Effective(t, c)) ?? t;
            bool other = (kind & (KindNullable - 1)) == KindOther;
            // Only a C# object's type needs the translator.
            string type = other ? "typeof(" + Cs(underlying, c) + ")" : "null", translator = other ? "translator" : "null";
            if (last != null) return "global::XLua.Binding.BindingApi.ArgScores(" + translator + ", L, " + idx + ", " + last + ", " + kind + ", " + type + ")";
            return "global::XLua.Binding.BindingApi.ArgScore(" + translator + ", L, " + idx + ", " + kind + ", " + type + ")";
        }

        static int Mask(ITypeSymbol t, Compilation c)
        {
            if (t == null) return MaskAll;
            t = Effective(t, c);
            var inner = NullableArgument(t);
            if (inner != null) return MaskNil | Mask(inner, c);
            switch (ScoreKind(t, c))
            {
                case KindInt32:
                case KindSmallInteger:
                case KindDouble:
                case KindSingle:
                    return MaskNumber;
                case KindInt64:
                case KindUInt64:
                case KindDecimal:
                    return MaskNumber | MaskUserdata;
                case KindObject:
                    return MaskAll;
                case KindString:
                    return MaskString | MaskNil;
                case KindBytes:
                    return MaskString | MaskNil | MaskUserdata;
                case KindBoolean:
                    return MaskBoolean;
            }
            if (t.SpecialType == SpecialType.System_IntPtr) return MaskLightUserdata;
            return MaskNil | MaskTable | MaskFunction | MaskUserdata;
        }

        static bool Related(ITypeSymbol a, ITypeSymbol b, Compilation c)
        {
            if (a == null || b == null) return true;
            a = Effective(a, c);
            b = Effective(b, c);
            a = NullableArgument(a) ?? a;
            b = NullableArgument(b) ?? b;
            return SymbolEqualityComparer.Default.Equals(a, b) || ScoreKind(a, c) != KindOther || ScoreKind(b, c) != KindOther
                || a.TypeKind == TypeKind.Interface || b.TypeKind == TypeKind.Interface
                || a.TypeKind == TypeKind.TypeParameter || b.TypeKind == TypeKind.TypeParameter
                || c.HasImplicitConversion(a, b) || c.HasImplicitConversion(b, a);
        }

        static ITypeSymbol ScoredAt(Candidate k, int pos)
        {
            int i = pos - k.Start;
            return i >= 0 && i < k.Types.Count && k.Scored[i] ? k.Types[i] : null;
        }

        static bool Accepts(Candidate k, int top)
        {
            int n = top - k.Start + 1;
            return n >= k.Required && (k.HasParams || n <= k.Required);
        }

        static int Penalty(Candidate k)
        {
            return (k.HasParams ? 2 : 0) + (k.DefaultUsed ? 1 : 0);
        }

        static bool NeedsScore(List<Candidate> ks, Compilation c)
        {
            for (int i = 0; i < ks.Count; i++)
            {
                for (int j = i + 1; j < ks.Count; j++)
                {
                    Candidate a = ks[i], b = ks[j];
                    int last = Math.Max(a.Start + a.Types.Count, b.Start + b.Types.Count);
                    for (int top = 0; top <= last; top++)
                    {
                        if (!Accepts(a, top) || !Accepts(b, top)) continue;
                        // Params elements after the first are scored but not checked.
                        if (a.HasParams || b.HasParams) return true;
                        bool differ = Penalty(a) != Penalty(b), match = true;
                        for (int pos = 1; pos <= top && match; pos++)
                        {
                            ITypeSymbol ta = ScoredAt(a, pos), tb = ScoredAt(b, pos);
                            int m = Mask(ta, c) & Mask(tb, c);
                            if (!Related(ta, tb, c)) m &= ~MaskUserdata;
                            if (m == 0) match = false;
                            else if (!SymbolEqualityComparer.Default.Equals(ta, tb) && (m & MaskScored) != 0) differ = true;
                        }
                        if (match && differ) return true;
                    }
                }
            }
            return false;
        }

        static bool Scores(List<Candidate> ks, Compilation c)
        {
            return ks.Count > 1 && NeedsScore(ks, c);
        }

        // Whether Dispatch's scores need the translator: for C# objects and object.
        static bool ScoresNeedTranslator(List<Candidate> ks, Compilation c)
        {
            if (!Scores(ks, c)) return false;
            foreach (var k in ks)
            {
                for (int t = 0; t < k.Types.Count; t++)
                {
                    int kind = ScoreKind(k.Types[t], c) & (KindNullable - 1);
                    if (k.Scored[t] && (kind == KindOther || kind == KindObject)) return true;
                }
            }
            return false;
        }

        // A candidate's checks: Head, then each of Types at its position.
        // scored: in a scored group, where the checks of the scored positions
        // read what the costs need anyway (ScoredCheck).
        static string CondFor(Candidate k, Compilation c, bool scored)
        {
            var parts = new List<string>();
            if (k.Head != null) parts.Add(k.Head);
            for (int t = 0; t < k.Types.Count; t++)
            {
                bool isParams = k.ParamsElement && t == k.Types.Count - 1;
                parts.Add((scored && k.Scored[t] && !isParams ? ScoredCheck(k.Types[t], k.Start + t, c) : null) ?? Check(k.Types[t], k.Start + t, isParams, c));
            }
            return string.Join(" && ", parts);
        }

        // Whether a scored C# object parameter is checked and costed in one
        // call (BindingApi.CheckedArgScore): object, or a type that Check
        // tests with translator.Assignable and the costs see as itself (kind
        // 0, not Nullable<>).
        static bool CheckedObject(ITypeSymbol t, Compilation c)
        {
            int kind = ScoreKind(t, c);
            return kind == KindObject || (kind == KindOther && Check(t, 0, false, c) == "translator.Assignable<" + Cs(t, c) + ">(L, 0)");
        }

        // Check in a scored group, or null where Check stays: a number's
        // check from its number kind (gen_n<idx>, read here at the first
        // use), which NumberScore needs anyway, and a C# object's check from
        // CheckedArgScore, whose cost it keeps in gen_s<idx>. Each replaces
        // Check's exact test: xlua_number_kind is 0 for anything but a number,
        // 1 for a number without an integer value (xlua_isinteger_number
        // false), 2 to 5 for one with an integer value.
        static string ScoredCheck(ITypeSymbol t, int idx, Compilation c)
        {
            string i = idx.ToString(), n = "gen_n" + i;
            string number = "(" + n + " < 0 ? (" + n + " = " + LuaAPI + ".xlua_number_kind(L, " + i + ")) : " + n + ")";
            string check = Check(t, idx, false, c);
            switch (ScoreKind(t, c))
            {
                case KindInt32:
                case KindSmallInteger:
                    if (check == LuaAPI + ".xlua_isinteger_number(L, " + i + ")") return number + " >= 2";
                    break;
                case KindInt64:
                case KindUInt64:
                    string big = ScoreKind(t, c) == KindInt64 ? "lua_isint64" : "lua_isuint64";
                    if (check == "(" + LuaAPI + ".xlua_isinteger_number(L, " + i + ") || " + LuaAPI + "." + big + "(L, " + i + "))")
                        return "(" + number + " >= 2 || " + LuaAPI + "." + big + "(L, " + i + "))";
                    break;
                case KindDouble:
                case KindSingle:
                    if (check == LuaTypes + ".LUA_TNUMBER == " + LuaAPI + ".lua_type(L, " + i + ")") return number + " != 0";
                    break;
                case KindDecimal:
                    if (check == "(" + LuaTypes + ".LUA_TNUMBER == " + LuaAPI + ".lua_type(L, " + i + ") || translator.IsDecimal(L, " + i + "))")
                        return "(" + number + " != 0 || translator.IsDecimal(L, " + i + "))";
                    break;
                case KindObject:
                case KindOther:
                    if (CheckedObject(t, c))
                        return "(gen_s" + i + " = global::XLua.Binding.BindingApi.CheckedArgScore(translator, L, " + i + ", typeof(" + Cs(t, c) + "), ref gen_t" + i + ")) >= 0";
                    break;
            }
            return null;
        }

        // Whether ScoredCheck passes only for a number: an int, a smaller
        // integer type, double or float, whose number kind it read.
        static bool NumberFound(ITypeSymbol t, int idx, Compilation c)
        {
            int kind = ScoreKind(t, c);
            return (kind == KindInt32 || kind == KindSmallInteger || kind == KindDouble || kind == KindSingle) && ScoredCheck(t, idx, c) != null;
        }

        static void Dispatch(Writer w, List<Candidate> ks, Compilation c)
        {
            if (!Scores(ks, c))
            {
                foreach (var k in ks)
                {
                    w.Open("if (" + CondFor(k, c, false) + ")");
                    k.Body();
                    w.Close();
                }
                return;
            }
            w.Line("int gen_pick = -1, gen_best = int.MaxValue, gen_score;");
            // The number kind of an argument, read once for all candidates,
            // and the cost of a C# object, which its check returns.
            var numbers = new SortedSet<int>();
            var objects = new SortedSet<int>();
            foreach (var k in ks)
            {
                for (int t = 0; t < k.Types.Count; t++)
                {
                    if (!k.Scored[t] || (k.ParamsElement && t == k.Types.Count - 1)) continue;
                    if (IsNumericKind(ScoreKind(k.Types[t], c))) numbers.Add(k.Start + t);
                    else if (CheckedObject(k.Types[t], c)) objects.Add(k.Start + t);
                }
            }
            if (numbers.Count > 0) w.Line("int " + string.Join(", ", numbers.Select(n => "gen_n" + n + " = -1")) + ";");
            // A C# object's cost (gen_s) and the lua_type its checks read once (gen_t).
            if (objects.Count > 0) w.Line("int " + string.Join(", ", objects.Select(n => "gen_s" + n + " = 0, gen_t" + n + " = -2")) + ";");
            w.Open("do");
            for (int i = 0; i < ks.Count; i++)
            {
                var k = ks[i];
                var parts = new List<string>();
                for (int t = 0; t < k.Types.Count; t++)
                {
                    if (!k.Scored[t]) continue;
                    if (k.ParamsElement && t == k.Types.Count - 1) parts.Add(ScoreCall(k.Types[t], k.Start + t, c, k.Top));
                    else if (IsNumericKind(ScoreKind(k.Types[t], c)))
                    {
                        int kind = ScoreKind(k.Types[t], c);
                        string n = "gen_n" + (k.Start + t);
                        // A number its check found (ScoredCheck): the number kind is not 0.
                        if (NumberFound(k.Types[t], k.Start + t, c)) parts.Add("global::XLua.Binding.BindingApi.NumberCost(" + n + ", " + kind + ")");
                        else parts.Add("global::XLua.Binding.BindingApi.NumberScore(L, " + (k.Start + t) + ", " + kind + ", ref " + n + ")");
                    }
                    else if (CheckedObject(k.Types[t], c)) parts.Add("gen_s" + (k.Start + t));
                    else parts.Add(ScoreCall(k.Types[t], k.Start + t, c));
                }
                // An argument the call leaves out costs 100, as a value of the wrong type.
                if (k.IgnoresExtra) parts.Add("global::System.Math.Max(0, " + k.Top + " - " + (k.Start + k.Types.Count - 1) + ") * 100");
                string sum = parts.Count == 0 ? "" : (parts.Count == 1 ? parts[0] : "(" + string.Join(" + ", parts) + ")") + " * 4";
                int penalty = Penalty(k);
                string expr = sum.Length == 0 ? penalty.ToString() : (penalty == 0 ? sum : sum + " + " + penalty);
                w.Open("if (" + CondFor(k, c, true) + ")");
                w.Line("gen_score = " + expr + ";");
                w.Open("if (gen_score < gen_best)");
                w.Line("gen_best = gen_score;");
                w.Line("gen_pick = " + i + ";");
                w.Line("if (gen_score == 0) break;");
                w.Close();
                w.Close();
            }
            w.Close(" while (false);");
            for (int i = 0; i < ks.Count; i++)
            {
                w.Open("if (gen_pick == " + i + ")");
                ks[i].Body();
                w.Close();
            }
        }

        // A candidate's count check and its parameter checks, as Method and
        // Constructor write them.
        static Candidate CandidateFor(Overload o, int offset, string top, Compilation c)
        {
            var ps = o.Parameters;
            int inNum = CountIn(ps);
            int real = ps.Length - o.DefaultCount;
            bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
            var k = new Candidate
            {
                Start = offset + 1, HasParams = hasParams, DefaultUsed = o.DefaultCount > 0, Top = top,
                ParamsElement = hasParams && real == ps.Length, IgnoresExtra = hasParams && real < ps.Length,
            };
            k.Head = top + " " + (hasParams ? ">=" : "==") + " " + (inNum + offset - o.DefaultCount - (hasParams ? 1 : 0));
            var scored = new List<bool>();
            for (int pi = 0; pi < real; pi++)
            {
                var p = ps[pi];
                bool isV = hasParams && pi == ps.Length - 1;
                var t = isV ? ((IArrayTypeSymbol)p.Type).ElementType : p.Type;
                if (p.RefKind != RefKind.Out)
                {
                    // checked at position Start + Types.Count, a params element when isV (ParamsElement)
                    k.Types.Add(t);
                    scored.Add(true);
                }
            }
            k.Scored = scored.ToArray();
            k.Required = inNum - o.DefaultCount - (hasParams ? 1 : 0);
            return k;
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
            FindTranslatorLine(w);
            bool hasZero = false;
            var candidates = new List<Candidate>();
            foreach (var o in info.Constructors)
            {
                var overload = o;
                var ps = o.Parameters;
                if (ps.Length == 0) hasZero = true;
                var k = CandidateFor(o, 1, Top(w), c);
                k.Body = () =>
                {
                    int real = ps.Length - overload.DefaultCount;
                    bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
                    int outNum = CountOut(ps);
                    for (int pi = 0; pi < real; pi++)
                    {
                        var p = ps[pi];
                        w.Line(Cast(p.Type, pi + 2, Local(p), true, hasParams && pi == ps.Length - 1, c) + ";");
                    }
                    w.Line("var gen_ret = new " + cs + "(" + Arguments(ps, real) + ExplicitDefaults(overload, real) + ");");
                    if (outNum + 1 > 10) w.Line("global::XLua.Binding.BindingApi.EnsureStack(L, " + (outNum + 1) + ");");
                    w.Line(Push(type, "gen_ret", c) + ";");
                    PushResults(w, ps, real, 1, c);
                    w.Line("return " + (outNum + 1) + ";");
                };
                candidates.Add(k);
            }
            Dispatch(w, candidates, c);
            if (!hasZero && type.IsValueType)
            {
                w.Open("if (" + Top(w) + " == 1)");
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
            FindTranslatorLine(w);
            var candidates = new List<Candidate>();
            foreach (var indexer in info.Indexers)
            {
                var getter = indexer;
                var key = indexer.Parameters[0];
                candidates.Add(new Candidate
                {
                    Head = Check(type, 1, false, c),
                    Start = 2, Types = { key.Type }, Scored = new[] { true }, Required = 1,
                    Body = () =>
                    {
                        w.Line(Self(type, c) + ";");
                        w.Line(Cast(key.Type, 2, "index", true, false, c) + ";");
                        w.Line(LuaAPI + ".lua_pushboolean(L, true);");
                        w.Line(Push(getter.ReturnType, IndexerGet(getter, "gen_to_be_invoked", "index", c), c) + ";");
                        w.Line("return 2;");
                    },
                });
            }
            Dispatch(w, candidates, c);
            Catch(w);
            w.Line(LuaAPI + ".lua_pushboolean(L, false);");
            w.Line("return 1;");
            w.Close();
        }

        static void NewIndexer(Writer w, ClassInfo info, Compilation c)
        {
            var type = info.Type;
            Function(w, "__NewIndexer", "internal static");
            FindTranslatorLine(w);
            Try(w);
            var candidates = new List<Candidate>();
            foreach (var indexer in info.NewIndexers)
            {
                var setter = indexer;
                var key = setter.Parameters[0];
                var valueType = setter.Parameters[1].Type;
                // Chosen by the key, as reflection does; the value is only checked.
                candidates.Add(new Candidate
                {
                    Head = Check(type, 1, false, c),
                    Start = 2, Types = { key.Type, valueType }, Scored = new[] { true, false }, Required = 2,
                    Body = () =>
                    {
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
                    },
                });
            }
            Dispatch(w, candidates, c);
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
                FindTranslatorLine(w);
                var candidates = new List<Candidate>();
                foreach (var o in op.Overloads)
                {
                    var method = o;
                    var left = o.Parameters[0].Type;
                    var right = o.Parameters[1].Type;
                    candidates.Add(new Candidate
                    {
                        Start = 1, Types = { left, right }, Scored = new[] { true, true }, Required = 2,
                        Body = () =>
                        {
                            w.Line(Cast(left, 1, "leftside", true, false, c) + ";");
                            w.Line(Cast(right, 2, "rightside", true, false, c) + ";");
                            w.Line(Push(method.ReturnType, "leftside " + OpSymbol[op.Name] + " rightside", c) + ";");
                            w.Line("return 1;");
                        },
                    });
                }
                Dispatch(w, candidates, c);
                // The operand that is not of this type: Lua calls the metamethod of either one.
                w.Line("return " + LuaAPI + ".luaL_error(L, \"invalid arguments to \" + (" + Check(type, 1, false, c) + " ? \"right\" : \"left\") + "
                    + Names.Literal(" hand of " + OpSymbol[op.Name] + " operator, need " + Display(type) + "!") + ");");
                Catch(w);
            }
            else
            {
                FindTranslatorLine(w);
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
            bool many = group.Overloads.Count > 1;
            int offset = group.IsStatic ? 0 : 1;
            var candidates = new List<Candidate>();
            foreach (var o in group.Overloads)
            {
                var overload = o;
                var k = CandidateFor(o, offset, "gen_param_count", c);
                k.Body = () => MethodBody(w, type, group, overload, offset, c);
                candidates.Add(k);
            }
            Function(w, "_m_" + group.Key);
            Try(w);
            if (!group.IsStatic || group.Overloads.Any(o => CallNeedsTranslator(o.Method, c)) || (many && ScoresNeedTranslator(candidates, c))) FindTranslatorLine(w);
            if (!group.IsStatic) w.Line(Self(type, c) + ";");
            if (many)
            {
                ParamCount(w);
                Dispatch(w, candidates, c);
            }
            else
            {
                w.Line("{");
                w.Indent++;
                candidates[0].Body();
                w.Close();
            }
            Catch(w);
            if (many) w.Line("return " + LuaAPI + ".luaL_error(L, " + Names.Literal("invalid arguments to " + Display(type) + "." + group.Overloads[0].Method.Name + "!") + ");");
            w.Close();
        }

        // The call of one overload whose arguments matched, and its results.
        static void MethodBody(Writer w, INamedTypeSymbol type, MethodGroup group, Overload o, int offset, Compilation c)
        {
            var m = o.Method;
            var ps = o.Parameters;
            int outNum = CountOut(ps);
            bool hasReturn = !m.ReturnsVoid;
            int real = ps.Length - o.DefaultCount;
            bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
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
        }

        static void Getter(Writer w, ClassInfo info, Accessor g, Compilation c)
        {
            var type = info.Type;
            Function(w, "_g_get_" + g.Name);
            Try(w);
            if (!g.IsStatic || !JustLuaType(g.Type, c)) FindTranslatorLine(w);
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
            if (!s.IsStatic || !JustLuaType(s.Type, c)) FindTranslatorLine(w);
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
            FindTranslatorLine(w);
            ParamCount(w);
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
                // A struct's handler goes into the copy in gen_to_be_invoked: store it back
                // into the Lua value, as the field and property setters do.
                string update = NeedUpdate(type, c) ? Update(type, 1, "gen_to_be_invoked", c) + ";" : null;
                w.Open("if (gen_param_count == 3)");
                if (e.CanAdd)
                {
                    w.Open("if (" + LuaAPI + ".xlua_is_eq_str(L, 2, \"+\"))");
                    w.Line(target + "." + Escape(e.Name) + " += gen_delegate;");
                    if (update != null) w.Line(update);
                    w.Line("return 0;");
                    w.Close();
                }
                if (e.CanRemove)
                {
                    w.Open("if (" + LuaAPI + ".xlua_is_eq_str(L, 2, \"-\"))");
                    w.Line(target + "." + Escape(e.Name) + " -= gen_delegate;");
                    if (update != null) w.Line(update);
                    w.Line("return 0;");
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
            var w = new Writer { Indent = 1, Gcm = ctx.Gcm };
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
            w.Line(Utils + ".RegisterFunc(L, " + Utils + ".CLS_IDX, \"__CastFrom\", " + Member(w, "__CastFrom") + ");");
            w.Line(Utils + ".EndClassRegister(typeof(" + cs + "), L, translator);");
            w.Close();

            Function(w, "__CastFrom");
            FindTranslatorLine(w);
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
                    w.Line("try");
                    w.Line("{");
                    w.Indent++;
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
