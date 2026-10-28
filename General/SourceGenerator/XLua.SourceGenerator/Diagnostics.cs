// Diagnostics of the xLua source generator (docs/design/source-generator.md,
// section 5.8). Milestone M1 reports XLSG001, 002, 003, 005, 006, 007, 009 and
// 011; M2 adds XLSG004 (members left out) and XLSG008, M4 XLSG010; XLSG012
// came with the XLua.Core assembly (core LuaScript M6), XLSG013 with the
// GEN_CODE_MINIMIZE mode (since 2026-10-19 also the minimal preset's), XLSG014
// with the owner's decision of 2026-10-28 on [GCOptimize] in strict minimal.
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
            "Assembly '{0}' references XLua but not XLua.Core, which defines the Lua C API types that the generated bindings use (the base class of XLua.LuaDLL.Bridge, lua_CSFunction, MonoPInvokeCallbackAttribute), and Unity does not pass XLua's references on: add \"XLua.Core\" to the \"references\" of the asmdef '{0}', as in \"references\": [\"XLua\", \"XLua.Core\"]. No bindings are generated for it until then",
            DiagnosticSeverity.Error);

        // GEN_CODE_MINIMIZE and XLUA_PRESET_MINIMAL, which implies it unless
        // XLUA_NO_GCM is defined, are project-wide defines in Unity; a define set
        // for one assembly only (csc.rsp, a precompiled XLua) splits the two.
        public static readonly DiagnosticDescriptor GcmMismatch = Make("XLSG013",
            "GEN_CODE_MINIMIZE mode differs from XLua's",
            "The GEN_CODE_MINIMIZE wrapper mode is {0} in assembly '{1}' but XLua was compiled {2} it; the wrappers follow XLua, since only XLua's mode compiles and runs. GEN_CODE_MINIMIZE turns the mode on, and so does XLUA_PRESET_MINIMAL unless XLUA_NO_GCM is defined: set these defines for the whole project (Player Settings, Scripting Define Symbols)",
            DiagnosticSeverity.Warning);

        // Strict minimal (XLUA_NO_REFLECTION, or XLUA_PRESET_MINIMAL without
        // XLUA_REFLECTION): otherwise the first field access fails at run time.
        public static readonly DiagnosticDescriptor UnboundStruct = Make("XLSG014",
            "GCOptimize struct without generated bindings",
            "GCOptimize struct '{0}' has no generated bindings, and this build does not reach C# through reflection (XLUA_NO_REFLECTION, part of XLUA_PRESET_MINIMAL): Lua gets its values without boxing, but reading or setting its fields ({1}) raises '{0} has no generated bindings' at run time. Mark it [LuaCallCSharp], or add [assembly: XLua.LuaCallCSharp(typeof({2}))] for a type you cannot change",
            DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor ConfigurationList = Make("XLSG011",
            "Configuration list, not read by the source generator",
            "[{0}] on '{1}' is a configuration list, which the source generator does not read; it reads [{0}] on types and [assembly: {0}(typeof(T))]. Convert the list with XLua > Migrate Configuration to Attributes (XLuaGenerate --migrate), then delete it",
            DiagnosticSeverity.Info);
    }
}
