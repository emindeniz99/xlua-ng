// The configuration the generator reads: one ConfigEntry per configuration
// attribute, made of strings and flags only, so the incremental pipeline can
// compare it with the previous run's (section 7). Everything that needs a
// symbol (names, accessibility, member lookups) is decided here, while the
// symbols are at hand.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace XLua.SourceGenerator
{
    internal enum ConfigKind
    {
        LuaCallCSharp,
        CSharpCallLua,
        GCOptimize,
        ReflectionUse,
        Hotfix,
        BlackList,
        DoNotGen,
        AdditionalProperties,
    }

    internal enum ConfigForm
    {
        Type,      // [X] on a type declaration
        Typed,     // [X(typeof(T), ...)] or [X(typeof(A), typeof(B)...)], normally at assembly level
        ByName,    // [assembly: BlackList("Type", "member", ...)]
        Member,    // [BlackList] on a member
        List,      // [X] on a static configuration list (legacy generator only)
    }

    // Why a configured type cannot be bound; None when it can.
    internal enum TypeProblem
    {
        None,
        Open,            // a generic type definition or a type built on one
        ClosedGeneric,   // Hotfix on a constructed generic type
        NotAccessible,
        NotDelegateOrInterface,
        OtherAssembly,   // assembly-level Hotfix of a type from another assembly
    }

    internal sealed class ConfigEntry : IEquatable<ConfigEntry>
    {
        public ConfigKind Kind;
        public ConfigForm Form;
        public string TypeofExpr;   // C# for typeof(...), or null
        public string LuaName;      // Type.ToString() at run time
        public string DisplayName;  // for messages
        public string DefiningAssembly;
        public string MetadataName; // of the type definition, for Compilation.GetTypeByMetadataName (Namespace.Outer+Inner`1)
        public TypeProblem Problem;
        public int Flags;           // GCOptimize, Hotfix and LuaCallCSharp flags
        public string Member;       // BlackList member, or the list's member name for Form.List
        public string MissingMembers; // comma-separated names that match nothing (BlackList, DoNotGen, AdditionalProperties)
        public string Parameters;   // BlackList: the parameter types (Type.ToString() names) joined by '|', null for none
        public string Names;        // DoNotGen and AdditionalProperties: the member names joined by '|'
        public bool Public;         // the type and its containing types are public (the legacy generator wraps only those)
        public bool Obsolete;       // the type or a containing type is [Obsolete] (the legacy generator skips it)
        public string Path;
        public TextSpan Span;
        public LinePositionSpan Lines;

        public Location GetLocation()
        {
            return Path == null ? Location.None : Location.Create(Path, Span, Lines);
        }

        // Types that the name table lists (section 5.6).
        public bool Named
        {
            get
            {
                return Problem == TypeProblem.None && TypeofExpr != null
                    && (Kind == ConfigKind.LuaCallCSharp || Kind == ConfigKind.CSharpCallLua || Kind == ConfigKind.GCOptimize
                        || Kind == ConfigKind.ReflectionUse || Kind == ConfigKind.Hotfix);
            }
        }

        public bool Equals(ConfigEntry o)
        {
            return o != null && Kind == o.Kind && Form == o.Form && TypeofExpr == o.TypeofExpr && LuaName == o.LuaName
                && DisplayName == o.DisplayName && DefiningAssembly == o.DefiningAssembly && MetadataName == o.MetadataName && Problem == o.Problem
                && Flags == o.Flags && Member == o.Member && MissingMembers == o.MissingMembers && Parameters == o.Parameters
                && Names == o.Names && Public == o.Public && Obsolete == o.Obsolete && Path == o.Path
                && Span.Equals(o.Span) && Lines.Equals(o.Lines);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ConfigEntry);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ (TypeofExpr ?? Member ?? "").GetHashCode() ^ Span.GetHashCode();
        }
    }

    internal static class Config
    {
        public static readonly ConfigKind[] Kinds = (ConfigKind[])Enum.GetValues(typeof(ConfigKind));

        public static string AttributeMetadataName(ConfigKind kind)
        {
            return "XLua." + kind + "Attribute";
        }

        public static ImmutableArray<ConfigEntry> Extract(ConfigKind kind, GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var result = ImmutableArray.CreateBuilder<ConfigEntry>();
            Compilation compilation = ctx.SemanticModel.Compilation;
            foreach (AttributeData attribute in ctx.Attributes)
            {
                ct.ThrowIfCancellationRequested();
                SyntaxNode where = attribute.ApplicationSyntaxReference?.GetSyntax(ct) ?? ctx.TargetNode;
                var args = attribute.ConstructorArguments;
                ConfigEntry entry;
                int array = TakesTypes(kind) ? TypeArrayArgument(args) : -1;
                if (array >= 0)
                {
                    // [X(typeof(A), typeof(B)...)] and [X(flags, typeof(A)...)]: one
                    // entry per type, as if each had its own [X(typeof(T), flags)].
                    var values = args[array].Values;
                    int flags = FlagsOf(args);
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (!(values[i].Value is ITypeSymbol element)) continue;
                        entry = Describe(kind, ConfigForm.Typed, element, compilation);
                        entry.Flags = flags;
                        SetLocation(entry, ElementSyntax(where, array, values.Length, i));
                        result.Add(entry);
                    }
                    continue;
                }
                if (args.Length > 0 && args[0].Kind == TypedConstantKind.Type && args[0].Value is ITypeSymbol configured)
                {
                    entry = Describe(kind, ConfigForm.Typed, configured, compilation);
                    entry.Flags = args.Length > 1 ? FlagsOf(args) : 0;
                    CheckMembers(entry, kind, configured, args, compilation);
                }
                else if (kind == ConfigKind.BlackList && args.Length >= 2 && args[0].Value is string typeName)
                {
                    entry = new ConfigEntry { Kind = kind, Form = ConfigForm.ByName, DisplayName = typeName, Member = args[1].Value as string };
                    CheckByName(entry, typeName, args, compilation);
                }
                else
                {
                    entry = DescribeTarget(kind, ctx.TargetSymbol, compilation);
                    if (entry == null) continue;
                    entry.Flags = FlagsOf(args);
                }
                SetLocation(entry, where);
                result.Add(entry);
            }
            return result.ToImmutable();
        }

        // The flags of GCOptimize, Hotfix and LuaCallCSharp: their only enum
        // argument, last in [X(flags)] and [X(typeof(T), flags)], first in
        // [X(flags, typeof(A), typeof(B)...)].
        internal static int FlagsOf(ImmutableArray<TypedConstant> args)
        {
            foreach (var arg in args)
            {
                if (arg.Kind != TypedConstantKind.Array && arg.Value is int flags) return flags;
            }
            return 0;
        }

        // The attributes with the params Type[] constructors (GenAttributes.cs).
        internal static bool TakesTypes(ConfigKind kind)
        {
            return kind == ConfigKind.LuaCallCSharp || kind == ConfigKind.CSharpCallLua || kind == ConfigKind.GCOptimize
                || kind == ConfigKind.ReflectionUse || kind == ConfigKind.Hotfix;
        }

        // The System.Type[] argument of [X(typeof(A), typeof(B)...)] or
        // [X(flags, typeof(A)...)], or -1. BlackList's parameter types are the
        // only other Type[] argument; callers ask only for TakesTypes kinds.
        internal static int TypeArrayArgument(ImmutableArray<TypedConstant> args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Kind == TypedConstantKind.Array && args[i].Type is IArrayTypeSymbol array
                    && array.ElementType.ToDisplayString() == "System.Type")
                    return i;
            }
            return -1;
        }

        // The configured types of an attribute of a TakesTypes kind in its
        // assembly-level forms: [X(typeof(T), ...)] or the Type[] forms; empty
        // for [X] and [X(flags)]. For attributes of referenced assemblies,
        // which only their metadata describes.
        internal static IEnumerable<ITypeSymbol> ConfiguredTypes(AttributeData attribute)
        {
            var args = attribute.ConstructorArguments;
            int array = TypeArrayArgument(args);
            if (array >= 0) return args[array].Values.Select(v => v.Value as ITypeSymbol).Where(t => t != null);
            if (args.Length > 0 && args[0].Kind == TypedConstantKind.Type && args[0].Value is ITypeSymbol type) return new[] { type };
            return Enumerable.Empty<ITypeSymbol>();
        }

        // The ConfigKind of an xLua configuration attribute class, or null.
        internal static ConfigKind? KindOf(AttributeData attribute)
        {
            if (attribute.AttributeClass == null) return null;
            string name = attribute.AttributeClass.ToDisplayString();
            foreach (var kind in Kinds)
            {
                if (name == AttributeMetadataName(kind)) return kind;
            }
            return null;
        }

        // The syntax of the element-th type of a Type[] argument: the argument
        // itself when C# passed the types one by one (params), else the attribute.
        static SyntaxNode ElementSyntax(SyntaxNode where, int array, int count, int element)
        {
            var attribute = where as Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax;
            var arguments = attribute == null || attribute.ArgumentList == null ? default(SeparatedSyntaxList<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeArgumentSyntax>) : attribute.ArgumentList.Arguments;
            return arguments.Count == array + count ? arguments[array + element] : where;
        }

        // The no-argument forms: on a type, on a member or on a configuration list.
        static ConfigEntry DescribeTarget(ConfigKind kind, ISymbol target, Compilation compilation)
        {
            if (target is INamedTypeSymbol type)
            {
                return Describe(kind, ConfigForm.Type, type, compilation);
            }
            if (target is IFieldSymbol || target is IPropertySymbol)
            {
                ITypeSymbol memberType = target is IFieldSymbol field ? field.Type : ((IPropertySymbol)target).Type;
                if (target.IsStatic && IsConfigurationList(kind, memberType))
                {
                    return new ConfigEntry { Kind = kind, Form = ConfigForm.List, DisplayName = target.ContainingType.ToDisplayString() + "." + target.Name, Member = target.Name };
                }
            }
            if (kind == ConfigKind.BlackList && !(target is IAssemblySymbol))
            {
                return new ConfigEntry { Kind = kind, Form = ConfigForm.Member, DisplayName = target.ToDisplayString(), Member = target.Name };
            }
            return null; // other targets were never read by any generator
        }

        // The member types of the static lists the legacy generator evaluated
        // (Generator.cs MergeCfg, which the migration tool still evaluates them with): IEnumerable<Type>, List<List<string>>,
        // Func<MemberInfo, bool>, Dictionary<Type, List<string>>.
        static bool IsConfigurationList(ConfigKind kind, ITypeSymbol type)
        {
            string name = type.OriginalDefinition.ToDisplayString();
            if (kind == ConfigKind.BlackList)
                return name == "System.Collections.Generic.List<T>" || name == "System.Func<T, TResult>";
            if (kind == ConfigKind.DoNotGen || kind == ConfigKind.AdditionalProperties)
                return name == "System.Collections.Generic.Dictionary<TKey, TValue>";
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) return true;
            return type.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        }

        static ConfigEntry Describe(ConfigKind kind, ConfigForm form, ITypeSymbol type, Compilation compilation)
        {
            var entry = new ConfigEntry
            {
                Kind = kind,
                Form = form,
                DisplayName = type.ToDisplayString(),
                DefiningAssembly = type.ContainingAssembly?.Name,
            };
            bool open = Names.IsOpen(type);
            if (kind == ConfigKind.Hotfix)
            {
                // Hotfix patches type definitions: open generic definitions are
                // allowed, constructed generic types are not (Hotfix.cs mergeConfig).
                if (open && !Names.IsSimpleDefinition(type)) entry.Problem = TypeProblem.Open;
                else if (!open && type is INamedTypeSymbol n && n.IsGenericType) entry.Problem = TypeProblem.ClosedGeneric;
                else if (form == ConfigForm.Typed && !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
                    entry.Problem = TypeProblem.OtherAssembly;
            }
            else if (open)
            {
                entry.Problem = TypeProblem.Open;
            }
            else if (kind == ConfigKind.CSharpCallLua && type.TypeKind != TypeKind.Delegate && type.TypeKind != TypeKind.Interface)
            {
                entry.Problem = TypeProblem.NotDelegateOrInterface;
            }
            if (entry.Problem == TypeProblem.None && !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                entry.Problem = TypeProblem.NotAccessible;
            }
            if (entry.Problem == TypeProblem.None)
            {
                entry.TypeofExpr = Names.Typeof(type);
                entry.LuaName = Names.Clr(type);
                entry.MetadataName = Names.MetadataName(type);
                entry.Public = Names.IsPublic(type);
                entry.Obsolete = Names.IsObsolete(type, Names.ObsoleteErrorsOnly(compilation));
            }
            return entry;
        }

        static void CheckMembers(ConfigEntry entry, ConfigKind kind, ITypeSymbol type, ImmutableArray<TypedConstant> args, Compilation compilation)
        {
            if (kind == ConfigKind.BlackList)
            {
                string member = args.Length > 1 ? args[1].Value as string : null;
                entry.Member = member;
                var parameterTypes = args.Length > 2 && args[2].Kind == TypedConstantKind.Array
                    ? args[2].Values.Select(v => v.Value as ITypeSymbol).ToArray() : new ITypeSymbol[0];
                var parameterNames = parameterTypes.Select(t => t == null ? "" : Names.Clr(t)).ToArray();
                entry.Parameters = parameterNames.Length == 0 ? null : string.Join("|", parameterNames);
                bool found = member != null && Members(type, member).Any(m => MatchesBlackList(m, parameterNames));
                if (!found) entry.MissingMembers = member ?? "";
            }
            else if (kind == ConfigKind.DoNotGen || kind == ConfigKind.AdditionalProperties)
            {
                var names = args.Length > 1 && args[1].Kind == TypedConstantKind.Array
                    ? args[1].Values.Select(v => v.Value as string).Where(v => v != null).ToArray() : new string[0];
                entry.Names = string.Join("|", names);
                var missing = names.Where(n => !Members(type, n).Any(m => kind == ConfigKind.DoNotGen || m is IPropertySymbol || m is IFieldSymbol)
                    && !(kind == ConfigKind.DoNotGen && HasExtensionMethod(compilation, type, n))).ToArray();
                if (missing.Length > 0) entry.MissingMembers = string.Join(", ", missing);
            }
        }

        static void CheckByName(ConfigEntry entry, string typeName, ImmutableArray<TypedConstant> args, Compilation compilation)
        {
            var type = compilation.GetTypeByMetadataName(typeName);
            string member = entry.Member;
            var parameterNames = args.Length > 2 && args[2].Kind == TypedConstantKind.Array
                ? args[2].Values.Select(v => v.Value as string).ToArray() : new string[0];
            entry.LuaName = typeName;
            entry.Parameters = parameterNames.Length == 0 ? null : string.Join("|", parameterNames);
            bool found = type != null && member != null && Members(type, member).Any(m => MatchesBlackList(m, parameterNames));
            if (!found) entry.MissingMembers = member ?? "";
        }

        // A [DoNotGen] name may name only extension methods of the type: the legacy
        // generator then registers a lazy member for them (Generator.cs). Only
        // extension methods declared in this compilation are found.
        static bool HasExtensionMethod(Compilation compilation, ITypeSymbol type, string name)
        {
            return compilation.GetSymbolsWithName(name, SymbolFilter.Member).OfType<IMethodSymbol>()
                .Any(m => m.IsExtensionMethod && m.Parameters.Length > 0
                    && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type));
        }

        // Members by name; ".ctor" names the instance constructors, and an
        // indexer has its reflection name ("Item", or its [IndexerName]).
        static IEnumerable<ISymbol> Members(ITypeSymbol type, string name)
        {
            if (name == ".ctor" && type is INamedTypeSymbol named) return named.InstanceConstructors;
            return type.GetMembers(name).Concat(type.GetMembers().OfType<IPropertySymbol>().Where(p => p.IsIndexer && p.MetadataName == name));
        }

        // As Generator.cs isMemberInBlackList/isMethodInBlackList: fields,
        // properties, events and property accessors by name, other methods by
        // their exact parameter types.
        static bool MatchesBlackList(ISymbol member, string[] parameterTypes)
        {
            if (member is IMethodSymbol method)
            {
                // A property accessor matches by name (BlackListRules.IsListed).
                if (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet) return true;
                if (method.Parameters.Length != parameterTypes.Length) return false;
                for (int i = 0; i < parameterTypes.Length; i++)
                {
                    ITypeSymbol p = method.Parameters[i].Type;
                    string name = Names.Clr(p) + (method.Parameters[i].RefKind != RefKind.None ? "&" : "");
                    if (name != parameterTypes[i]) return false;
                }
                return true;
            }
            return member is IFieldSymbol || member is IPropertySymbol || member is IEventSymbol;
        }

        static void SetLocation(ConfigEntry entry, SyntaxNode where)
        {
            if (where == null) return;
            entry.Path = where.SyntaxTree.FilePath;
            entry.Span = where.Span;
            entry.Lines = where.SyntaxTree.GetLineSpan(where.Span).Span;
        }
    }
}
