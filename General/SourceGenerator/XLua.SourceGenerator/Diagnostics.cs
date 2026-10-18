// Diagnostics of the xLua source generator (docs/design/source-generator.md,
// section 5.8). Milestone M1 reports XLSG001, 002, 003, 005, 006, 007, 009 and
// 011; M2 adds XLSG004 (members left out) and XLSG008, M4 XLSG010; XLSG012
// came with the XLua.Core assembly (core LuaScript M6).
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

        public static readonly DiagnosticDescriptor MemberDropped = Make("XLSG004",
            "Member not bound",
            "'{0}': {1}; Lua does not see it, as with the legacy generator",
            DiagnosticSeverity.Info);

        public static readonly DiagnosticDescriptor MemberFallback = Make("XLSG004",
            "Member left to reflection",
            "'{0}': {1}; Lua reaches it through reflection (the legacy generator dropped it), and the first use logs this in the Editor and development builds",
            DiagnosticSeverity.Info);

        public static readonly DiagnosticDescriptor TypeNotWrapped = Make("XLSG004",
            "Type not wrapped",
            "'{0}': {1}",
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

        public static readonly DiagnosticDescriptor BoundElsewhere = Make("XLSG008",
            "Type already bound by a referenced assembly",
            "'{0}' is already wrapped by the referenced assembly '{1}'; its wrapper is used here too",
            DiagnosticSeverity.Info);

        public static readonly DiagnosticDescriptor HotfixOtherAssembly = Make("XLSG009",
            "Hotfix type from another assembly",
            "[assembly: Hotfix(typeof({0}))] must be in assembly '{1}', which defines the type; it is skipped here",
            DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor NotPackable = Make("XLSG010",
            "GCOptimize struct not packable",
            "GCOptimize struct '{0}' has a public field that is not a primitive, decimal or packable struct; Lua tables convert to it field by field, but it is pushed boxed, as with the legacy generator",
            DiagnosticSeverity.Warning);

        // Since core LuaScript M6 the Lua C API types that generated code names
        // (XLua.LuaDLL.lua_CSFunction, the base class of XLua.LuaDLL.Bridge,
        // MonoPInvokeCallbackAttribute) are in XLua.Core in the asmdef and package
        // layouts, and Unity does not pass XLua's references on to an asmdef.
        public static readonly DiagnosticDescriptor CoreNotReferenced = Make("XLSG012",
            "XLua.Core not referenced",
            "Assembly '{0}' references XLua but not XLua.Core, which defines the Lua C API types that the generated bindings use (XLua.LuaDLL.lua_CSFunction); add XLua.Core to the references of its asmdef. No bindings are generated for it",
            DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor ConfigurationList = Make("XLSG011",
            "Configuration list read only by the legacy generator",
            "[{0}] on '{1}' is a configuration list, which only the legacy generator evaluates; the source generator reads [{0}] on types and [assembly: {0}(typeof(T))]",
            DiagnosticSeverity.Info);
    }
}
