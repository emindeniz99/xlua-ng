// Diagnostics of the xLua source generator (docs/design/source-generator.md,
// section 5.8). Milestone M1 reports XLSG001, 002, 003, 005, 006, 007, 009 and
// 011; XLSG004, 008 and 010 need the member model and come with M2 to M5.
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    internal static class Diagnostics
    {
        const string Category = "XLua";

        static DiagnosticDescriptor Make(string id, string title, string message, DiagnosticSeverity severity)
        {
            return new DiagnosticDescriptor(id, title, message, Category, severity, isEnabledByDefault: true);
        }

        public static readonly DiagnosticDescriptor GenericType = Make("XLSG001",
            "Generic type not bound",
            "{0} '{1}' is {2}; it is skipped",
            DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor NotAccessible = Make("XLSG002",
            "Configured type not accessible",
            "{0} type '{1}' is not accessible from code generated in assembly '{2}'",
            DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor NoSuchMember = Make("XLSG003",
            "Configuration names no member",
            "{0} names '{1}', which matches no member of '{2}'",
            DiagnosticSeverity.Warning);

        // A by-name BlackList may name a type or member that exists only on some
        // build targets, so a miss there is information, not a warning.
        public static readonly DiagnosticDescriptor NoSuchMemberByName = Make("XLSG003",
            "Configuration names no member",
            "{0} names '{1}', which matches no member of '{2}' in this compilation",
            DiagnosticSeverity.Info);

        public static readonly DiagnosticDescriptor ConflictingGCOptimize = Make("XLSG005",
            "Conflicting GCOptimize flags",
            "GCOptimize flags for '{0}' conflict: {1} here, {2} elsewhere",
            DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor NotDelegateOrInterface = Make("XLSG006",
            "CSharpCallLua type is not a delegate or an interface",
            "CSharpCallLua type '{0}' is not a delegate or an interface; it is skipped",
            DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor ApiVersion = Make("XLSG007",
            "Binding API version mismatch",
            "The xLua source generator was built for binding API version {0}, but the referenced xLua has version {1}; use the generator that comes with this xLua",
            DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor HotfixOtherAssembly = Make("XLSG009",
            "Hotfix type from another assembly",
            "[assembly: Hotfix(typeof({0}))] must be in assembly '{1}', which defines the type; it is skipped here",
            DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor ConfigurationList = Make("XLSG011",
            "Configuration list read only by the legacy generator",
            "[{0}] on '{1}' is a configuration list, which only the legacy generator evaluates; the source generator reads [{0}] on types and [assembly: {0}(typeof(T))]",
            DiagnosticSeverity.Info);
    }
}
