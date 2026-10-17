// Spike: does an IIncrementalGenerator built against Microsoft.CodeAnalysis 4.3.1 load
// and run in Unity's compiler? It mimics the shape the xLua wrapper generator would have:
// read [assembly: Export(typeof(T))] and [Export] on user types, then emit one internal
// static class per assembly that calls each exported type directly and registers itself
// through [RuntimeInitializeOnLoadMethod]. No RegisterPostInitializationOutput: the
// attribute types live in the runtime assembly.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace XLuaTest.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ExportGenerator : IIncrementalGenerator
    {
        const string AttributeName = "XLuaTest.ExportAttribute";
        const string RegistryName = "XLuaTest.Registry";

        static readonly DiagnosticDescriptor GenericNotSupported = new DiagnosticDescriptor(
            "XLSG001", "Generic type definition not exported",
            "[Export] on generic type definition '{0}' is not supported; the type is skipped",
            "XLuaTest", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        static readonly DiagnosticDescriptor NotAccessible = new DiagnosticDescriptor(
            "XLSG002", "Exported type not accessible",
            "[Export] type '{0}' is not accessible from generated code",
            "XLuaTest", DiagnosticSeverity.Error, isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<ImmutableArray<ExportItem>> exports = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    AttributeName,
                    static (node, _) => node is CompilationUnitSyntax || node is TypeDeclarationSyntax,
                    static (ctx, ct) => Extract(ctx, ct))
                .SelectMany(static (items, _) => items)
                .Collect();

            IncrementalValueProvider<AssemblyInfo> assembly = context.CompilationProvider.Select(
                static (c, _) => new AssemblyInfo(c.AssemblyName ?? "Unnamed", c.GetTypeByMetadataName(RegistryName) != null));

            context.RegisterSourceOutput(assembly.Combine(exports), static (spc, pair) => Emit(spc, pair.Left, pair.Right));
        }

        static ImmutableArray<ExportItem> Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var result = ImmutableArray.CreateBuilder<ExportItem>();
            Compilation compilation = ctx.SemanticModel.Compilation;
            if (ctx.TargetSymbol is IAssemblySymbol)
            {
                foreach (AttributeData a in ctx.Attributes)
                {
                    if (a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is INamedTypeSymbol t)
                        result.Add(Describe(t, a.ApplicationSyntaxReference?.GetSyntax(ct), compilation));
                }
            }
            else if (ctx.TargetSymbol is INamedTypeSymbol type)
            {
                SyntaxNode where = ctx.Attributes.Length > 0 ? ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(ct) : null;
                result.Add(Describe(type, where ?? ctx.TargetNode, compilation));
            }
            return result.ToImmutable();
        }

        static ExportItem Describe(INamedTypeSymbol t, SyntaxNode where, Compilation compilation)
        {
            int problem = 0;
            if (t.IsUnboundGenericType || (t.TypeParameters.Length > 0 && SymbolEqualityComparer.Default.Equals(t, t.OriginalDefinition)))
                problem = 1;
            else if (!compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
                problem = 2;

            string memberExpr = null, memberLabel = null;
            if (problem == 0)
            {
                string typeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var members = new List<ISymbol>(t.GetMembers());
                members.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                foreach (ISymbol m in members)
                {
                    if (!m.IsStatic || m.DeclaredAccessibility != Accessibility.Public || IsObsolete(m))
                        continue;
                    if (m is IPropertySymbol p && p.GetMethod != null && p.Parameters.Length == 0)
                    {
                        memberExpr = typeName + "." + p.Name;
                        memberLabel = p.Name;
                        break;
                    }
                    if (m is IMethodSymbol meth && meth.MethodKind == MethodKind.Ordinary && meth.Parameters.Length == 0
                        && !meth.ReturnsVoid && !meth.IsGenericMethod)
                    {
                        memberExpr = typeName + "." + meth.Name + "()";
                        memberLabel = meth.Name + "()";
                        break;
                    }
                }
            }

            string path = null;
            TextSpan span = default;
            LinePositionSpan lines = default;
            if (where != null)
            {
                path = where.SyntaxTree.FilePath;
                span = where.Span;
                lines = where.SyntaxTree.GetLineSpan(where.Span).Span;
            }
            return new ExportItem(
                t.ToDisplayString(), t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                memberExpr, memberLabel, problem, path, span, lines);
        }

        static bool IsObsolete(ISymbol s)
        {
            foreach (AttributeData a in s.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")
                    return true;
            return false;
        }

        static void Emit(SourceProductionContext spc, AssemblyInfo asm, ImmutableArray<ExportItem> items)
        {
            string id = Sanitize(asm.Name);
            var ok = new SortedDictionary<string, ExportItem>(StringComparer.Ordinal);
            foreach (ExportItem item in items)
            {
                if (item.Problem != 0)
                {
                    DiagnosticDescriptor d = item.Problem == 1 ? GenericNotSupported : NotAccessible;
                    spc.ReportDiagnostic(Diagnostic.Create(d, item.GetLocation(), item.DisplayName));
                    continue;
                }
                ok[item.DisplayName] = item;
            }

            var names = new StringBuilder();
            foreach (string n in ok.Keys)
                names.Append(names.Length == 0 ? "" : ";").Append(n);

            // The marker shows in which assemblies the generator ran at all (scope check).
            spc.AddSource("XLuaSGMarker.g.cs", SourceText.From(
                "// <auto-generated/>\n" +
                "namespace XLuaTest.Generated\n{\n" +
                "    internal static class XLuaSGMarker_" + id + "\n    {\n" +
                "        public const string Exports = \"" + names + "\";\n" +
                "        public const bool HasRuntime = " + (asm.HasRegistry ? "true" : "false") + ";\n" +
                "    }\n}\n", Encoding.UTF8));

            if (ok.Count == 0 || !asm.HasRegistry)
                return;

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>\n");
            sb.Append("namespace XLuaTest.Generated\n{\n");
            sb.Append("    internal static class XLuaGen_").Append(id).Append("\n    {\n");
            sb.Append("        public const string AssemblyName = \"").Append(asm.Name).Append("\";\n");
            sb.Append("        public const string Exports = \"").Append(names).Append("\";\n\n");
            sb.Append("        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]\n");
            sb.Append("        private static void Register()\n        {\n");
            sb.Append("            global::XLuaTest.Registry.Register(AssemblyName, Exports, Invoke);\n        }\n\n");
            sb.Append("        internal static string Invoke()\n        {\n");
            sb.Append("            string s = \"\";\n");
            foreach (ExportItem item in ok.Values)
            {
                if (item.MemberExpr != null)
                    sb.Append("            s += \"").Append(item.DisplayName).Append('.').Append(item.MemberLabel)
                      .Append("=\" + ").Append(item.MemberExpr).Append(" + \";\";\n");
                else
                    sb.Append("            s += \"").Append(item.DisplayName).Append("=\" + typeof(")
                      .Append(item.FullName).Append(").Name + \";\";\n");
            }
            sb.Append("            return s;\n        }\n    }\n}\n");
            spc.AddSource("XLuaGen.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        sealed class AssemblyInfo : IEquatable<AssemblyInfo>
        {
            public readonly string Name;
            public readonly bool HasRegistry;
            public AssemblyInfo(string name, bool hasRegistry) { Name = name; HasRegistry = hasRegistry; }
            public bool Equals(AssemblyInfo o) => o != null && o.Name == Name && o.HasRegistry == HasRegistry;
            public override bool Equals(object o) => Equals(o as AssemblyInfo);
            public override int GetHashCode() => Name.GetHashCode() ^ (HasRegistry ? 1 : 0);
        }

        // Value-equatable so the incremental pipeline can cache it.
        sealed class ExportItem : IEquatable<ExportItem>
        {
            public readonly string DisplayName, FullName, MemberExpr, MemberLabel, Path;
            public readonly int Problem;
            public readonly TextSpan Span;
            public readonly LinePositionSpan Lines;

            public ExportItem(string displayName, string fullName, string memberExpr, string memberLabel,
                int problem, string path, TextSpan span, LinePositionSpan lines)
            {
                DisplayName = displayName; FullName = fullName; MemberExpr = memberExpr; MemberLabel = memberLabel;
                Problem = problem; Path = path; Span = span; Lines = lines;
            }

            public Location GetLocation() => Path == null ? Location.None : Location.Create(Path, Span, Lines);

            public bool Equals(ExportItem o) => o != null && o.DisplayName == DisplayName && o.FullName == FullName
                && o.MemberExpr == MemberExpr && o.MemberLabel == MemberLabel && o.Problem == Problem
                && o.Path == Path && o.Span.Equals(Span) && o.Lines.Equals(Lines);
            public override bool Equals(object o) => Equals(o as ExportItem);
            public override int GetHashCode() => (DisplayName ?? "").GetHashCode() ^ Span.GetHashCode();
        }
    }
}
