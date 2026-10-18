// Names the generated code needs, computed from symbols at compile time.
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    internal static class Names
    {
        // What System.Type.ToString() returns for the type at run time: the Lua
        // path of the type and its name table entry (BindingApi Type/Name; the
        // legacy generator records the same string). Nested types use '+',
        // generic types their arity and, when constructed, the arguments of the
        // whole containing chain in brackets: List`1[System.Int32],
        // Outer`1+Inner[System.Int32]. Open types list their parameter names.
        public static string Clr(ITypeSymbol type)
        {
            var sb = new StringBuilder();
            AppendClr(sb, type);
            return sb.ToString();
        }

        static void AppendClr(StringBuilder sb, ITypeSymbol type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    AppendClr(sb, array.ElementType);
                    sb.Append('[').Append(array.Rank > 1 ? new string(',', array.Rank - 1) : "").Append(']');
                    return;
                case IPointerTypeSymbol pointer:
                    AppendClr(sb, pointer.PointedAtType);
                    sb.Append('*');
                    return;
                case ITypeParameterSymbol parameter:
                    sb.Append(parameter.Name);
                    return;
                case IDynamicTypeSymbol _:
                    sb.Append("System.Object"); // dynamic is object at run time
                    return;
                case INamedTypeSymbol named:
                    AppendDefinition(sb, named);
                    var arguments = AllTypeArguments(named);
                    if (arguments.Length > 0)
                    {
                        sb.Append('[');
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            if (i > 0) sb.Append(',');
                            AppendClr(sb, arguments[i]);
                        }
                        sb.Append(']');
                    }
                    return;
                default:
                    sb.Append(type.ToDisplayString());
                    return;
            }
        }

        // Namespace.Outer`1+Inner, without type arguments.
        static void AppendDefinition(StringBuilder sb, INamedTypeSymbol type)
        {
            if (type.ContainingType != null)
            {
                AppendDefinition(sb, type.ContainingType);
                sb.Append('+');
            }
            else if (type.ContainingNamespace != null && !type.ContainingNamespace.IsGlobalNamespace)
            {
                sb.Append(type.ContainingNamespace.ToDisplayString()).Append('.');
            }
            sb.Append(type.MetadataName);
        }

        static ITypeSymbol[] AllTypeArguments(INamedTypeSymbol type)
        {
            var chain = new System.Collections.Generic.List<INamedTypeSymbol>();
            for (var t = type; t != null; t = t.ContainingType) chain.Insert(0, t);
            return chain.SelectMany(t => t.IsUnboundGenericType ? t.TypeParameters.Cast<ITypeSymbol>() : t.TypeArguments).ToArray();
        }

        // The name Compilation.GetTypeByMetadataName finds the type's definition by:
        // Namespace.Outer+Inner`1. Null for types without one (arrays, pointers).
        public static string MetadataName(ITypeSymbol type)
        {
            var named = type as INamedTypeSymbol;
            if (named == null) return null;
            var d = named.OriginalDefinition;
            var chain = new System.Collections.Generic.List<string>();
            for (var t = d; t != null; t = t.ContainingType) chain.Insert(0, t.MetadataName);
            string ns = d.ContainingNamespace == null || d.ContainingNamespace.IsGlobalNamespace ? "" : d.ContainingNamespace.ToDisplayString() + ".";
            return ns + string.Join("+", chain);
        }

        // Whether the type, its containing types or an array element type has an
        // unbound type parameter (a generic type definition or a type built on one).
        public static bool IsOpen(ITypeSymbol type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    return IsOpen(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return IsOpen(pointer.PointedAtType);
                case ITypeParameterSymbol _:
                    return true;
                case INamedTypeSymbol named:
                    if (named.IsUnboundGenericType) return true;
                    for (var t = named; t != null; t = t.ContainingType)
                    {
                        if (t.TypeArguments.Any(IsOpen)) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        internal static readonly SymbolDisplayFormat TypeofFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        // C# for the type, as typeof's operand, qualified with global::. A generic
        // type definition without generic containing types is written unbound
        // (global::Game.Pool<>), which is the one open form typeof accepts here.
        public static string Typeof(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.IsGenericType && SymbolEqualityComparer.Default.Equals(named, named.OriginalDefinition))
            {
                return named.ConstructUnboundGenericType().ToDisplayString(TypeofFormat);
            }
            if (type is INamedTypeSymbol unbound && unbound.IsUnboundGenericType)
            {
                return unbound.ToDisplayString(TypeofFormat);
            }
            return type.ToDisplayString(TypeofFormat);
        }

        // C# for a type in a declaration or an expression, qualified with global::.
        public static string Cs(ITypeSymbol type)
        {
            return type.ToDisplayString(TypeofFormat);
        }

        // A generic type definition whose containing types are not generic: the
        // open types that typeof can name and Hotfix accepts.
        public static bool IsSimpleDefinition(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named) || !named.IsGenericType) return false;
            if (!named.IsUnboundGenericType && !SymbolEqualityComparer.Default.Equals(named, named.OriginalDefinition)) return false;
            for (var t = named.ContainingType; t != null; t = t.ContainingType)
            {
                if (t.IsGenericType) return false;
            }
            return true;
        }

        // The namespace of an assembly's generated code: XLua.Generated.<name>_<hash>,
        // the name with every character that is not a letter, digit or '_' replaced
        // by '_', and the first 8 hex digits of the FNV-1a hash of the UTF-8 name,
        // so that My.Game and My_Game get different namespaces (section 5.1).
        public static string GeneratedNamespace(string assemblyName)
        {
            var sb = new StringBuilder("XLua.Generated.");
            if (assemblyName.Length == 0 || char.IsDigit(assemblyName[0])) sb.Append('_');
            foreach (char c in assemblyName)
            {
                sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_');
            }
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(assemblyName))
            {
                hash = unchecked((hash ^ b) * 16777619);
            }
            return sb.Append('_').Append(hash.ToString("x8")).ToString();
        }

        // Whether the type and every containing type are public (the legacy
        // generator's IsPublic): it wraps only those.
        public static bool IsPublic(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array) return IsPublic(array.ElementType);
            for (ITypeSymbol t = type; t != null; t = t.ContainingType)
            {
                if (t.DeclaredAccessibility != Accessibility.Public) return false;
            }
            return true;
        }

        // Generator.cs isObsolete: members and types marked [Obsolete] are left
        // out, or only those marked [Obsolete(..., true)] when the project
        // defines XLUA_JUST_EXCLUDE_ERROR. (The legacy generator also has an
        // errors-only mode for XLUA_GENERAL builds without XLUA_ALL_OBSOLETE,
        // but its command-line tool is always built with XLUA_ALL_OBSOLETE,
        // Tools/build_tools.py; a compilation's own XLUA_GENERAL does not
        // change what the source generator binds.)
        public static bool ObsoleteErrorsOnly(Compilation compilation)
        {
            return compilation.SyntaxTrees.Select(t => t.Options).OfType<Microsoft.CodeAnalysis.CSharp.CSharpParseOptions>()
                .Take(1).SelectMany(o => o.PreprocessorSymbolNames).Contains("XLUA_JUST_EXCLUDE_ERROR");
        }

        public static bool IsObsolete(ISymbol symbol, bool errorsOnly)
        {
            if (symbol == null) return false;
            foreach (var a in symbol.GetAttributes())
            {
                if (a.AttributeClass == null || a.AttributeClass.ToDisplayString() != "System.ObsoleteAttribute") continue;
                if (!errorsOnly) return true;
                return a.ConstructorArguments.Length >= 2 && a.ConstructorArguments[1].Value is bool error && error;
            }
            return false;
        }

        // Generator.cs isObsolete(Type): the type or a containing type.
        public static bool IsObsolete(ITypeSymbol type, bool errorsOnly)
        {
            for (ISymbol t = type; t != null; t = t.ContainingType)
            {
                if (IsObsolete(t, errorsOnly)) return true;
            }
            return false;
        }

        // A C# string literal.
        public static string Literal(string value)
        {
            var sb = new StringBuilder(value.Length + 2).Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
