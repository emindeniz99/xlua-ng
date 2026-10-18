// Hotfix bridges per assembly (docs/design/source-generator.md, section 6,
// option B, owner decision 8, milestone M7). An assembly with [Hotfix] types
// gets an internal static class HotfixBridges with:
//
// - one __Gen_Delegate_Imp<k>(this DelegateBridge, ...) per signature of its
//   injectable methods, grouped by SignatureKey (KeyPurpose.Hotfix), with the
//   method selection of Generator.cs GenDelegateBridge (the flags
//   IgnoreProperty, IgnoreNotPublic, IgnoreCompilerGenerated, Inline, and
//   injectByGeneric). The injector (Hotfix.cs) finds them in the injected
//   assembly itself and calls them directly;
// - the assembly's IntKey slots: Get(id), which the injected code calls at
//   each method entry (a managed array read), and Set(id, bridge), registered
//   through AssemblyBindings.HotfixSlots so that
//   HotfixDelegateBridge.SetSlot(assembly, id, f) reaches it. Ids number from 0 in
//   each assembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XLua.SourceGenerator
{
    internal static partial class Wrappers
    {
        // HotfixFlag values (GenAttributes.cs).
        const int ValueTypeBoxing = 2, IgnoreProperty = 4, IgnoreNotPublic = 8, Inline = 16, IgnoreCompilerGenerated = 128;

        // Generator.cs HasFlag: Stateless (0) has none.
        static bool HasHotfixFlag(int flags, int flag)
        {
            return flags != 0 && (flags & flag) == flag;
        }

        static void HotfixBridges(WrapperSet set, Compilation compilation, EquatableArray<ConfigEntry> entries, MemberContext ctx)
        {
            var types = new List<KeyValuePair<INamedTypeSymbol, int>>();
            var seen = new HashSet<string>();
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.Hotfix && x.Problem == TypeProblem.None && x.MetadataName != null)
                .OrderBy(x => x.Path ?? "", StringComparer.Ordinal).ThenBy(x => x.Span.Start))
            {
                var t = compilation.GetTypeByMetadataName(e.MetadataName);
                if (t == null || !SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly)) continue;
                if (!seen.Add(e.MetadataName)) continue;
                types.Add(new KeyValuePair<INamedTypeSymbol, int>(t, e.Flags));
            }
            if (types.Count == 0) return;

            var keys = new SortedDictionary<string, BridgeSignature>(StringComparer.Ordinal);
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            foreach (var kv in types)
            {
                var type = kv.Key;
                int flags = kv.Value;
                if (type.MetadataName.Contains("<") || HasHotfixFlag(flags, Inline)) continue;
                bool ignoreCompilerGenerated = HasHotfixFlag(flags, IgnoreCompilerGenerated);
                if (ignoreCompilerGenerated && HasCompilerGeneratedAttribute(type)) continue;
                foreach (var m in HotfixMethods(type, flags))
                {
                    if (InjectByGeneric(m, flags)) continue;
                    string key = SignatureKey.Of(m, KeyPurpose.Hotfix, flags);
                    if (!keys.ContainsKey(key)) keys.Add(key, HotfixSignature(m, flags, objectType));
                }
            }

            var w = new Writer { Indent = 2 };
            w.Line("static readonly object sync = new object();");
            w.Line("static global::XLua.DelegateBridge[] slots = new global::XLua.DelegateBridge[0];");
            w.Line();
            w.Line("// The patch of IntKey method id, or null: the injected code calls this at the method's entry.");
            w.Open("internal static global::XLua.DelegateBridge Get(int id)");
            w.Line("var s = slots;");
            w.Line("return (uint)id < (uint)s.Length ? s[id] : null;");
            w.Close();
            w.Line();
            w.Line("// AssemblyBindings.HotfixSlots: HotfixDelegateBridge.SetSlot(assembly, id, bridge).");
            w.Open("internal static void Set(int id, global::XLua.DelegateBridge bridge)");
            w.Open("lock (sync)");
            w.Line("var s = slots;");
            w.Open("if (id >= s.Length)");
            w.Line("var grown = new global::XLua.DelegateBridge[global::System.Math.Max(id + 1, s.Length * 2)];");
            w.Line("global::System.Array.Copy(s, grown, s.Length);");
            w.Line("s = grown;");
            w.Close();
            w.Line("s[id] = bridge;");
            w.Line("slots = s;");
            w.Close();
            w.Close();
            int k = 0;
            var manifest = new Json().BeginArray();
            foreach (var kv in keys)
            {
                DelegateBridge(w, "__Gen_Delegate_Imp" + k++, kv.Value, compilation);
                manifest.Value(kv.Key);
            }
            set.HotfixBridges = w.ToString();
            set.HotfixManifest = manifest.EndArray().ToString();
        }

        // Type.GetMethods(Public | NonPublic | Instance | Static | DeclaredOnly)
        // with a body, then the instance constructors, as Generator.cs
        // GenDelegateBridge selects them.
        static IEnumerable<IMethodSymbol> HotfixMethods(INamedTypeSymbol type, int flags)
        {
            bool ignoreProperty = HasHotfixFlag(flags, IgnoreProperty);
            bool ignoreNotPublic = HasHotfixFlag(flags, IgnoreNotPublic);
            bool ignoreCompilerGenerated = HasHotfixFlag(flags, IgnoreCompilerGenerated);
            foreach (var m in type.GetMembers().OfType<IMethodSymbol>())
            {
                switch (m.MethodKind)
                {
                    case MethodKind.Constructor:
                    case MethodKind.StaticConstructor:
                    case MethodKind.DelegateInvoke:
                        continue;
                }
                if (m.IsAbstract || m.IsExtern || (m.IsPartialDefinition && m.PartialImplementationPart == null)) continue; // no body
                if (m.MetadataName.Contains("<")) continue;
                if (ignoreCompilerGenerated && IsCompilerGenerated(m)) continue;
                if (ignoreNotPublic && m.DeclaredAccessibility != Accessibility.Public) continue;
                if (ignoreProperty && (m.MethodKind == MethodKind.PropertyGet || m.MethodKind == MethodKind.PropertySet)) continue;
                if (m.Parameters.Any(p => MemberContext.IsUnsupported(p.Type)) || (!m.ReturnsVoid && MemberContext.IsUnsupported(m.ReturnType))) continue;
                if (m.ReturnsByRef || m.ReturnsByRefReadonly) continue; // no bridge can return by reference
                yield return m;
            }
            foreach (var c in type.InstanceConstructors)
            {
                if (c.IsImplicitlyDeclared && type.IsValueType) continue; // not in metadata
                yield return c;
            }
        }

        static bool HasCompilerGeneratedAttribute(ISymbol s)
        {
            return s.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        // What carries [CompilerGenerated] in metadata: members the compiler
        // writes (record members, field-like event accessors) and the accessors of
        // auto-properties.
        static bool IsCompilerGenerated(IMethodSymbol m)
        {
            if (m.IsImplicitlyDeclared || HasCompilerGeneratedAttribute(m)) return true;
            if (m.AssociatedSymbol is IPropertySymbol p)
            {
                foreach (var r in p.DeclaringSyntaxReferences)
                {
                    if (r.GetSyntax() is PropertyDeclarationSyntax syntax && syntax.ExpressionBody == null && syntax.AccessorList != null
                        && syntax.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null))
                        return true;
                }
            }
            return false;
        }

        // Generator.cs hasGenericParameter.
        static bool HasGenericParameter(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol a) return HasGenericParameter(a.ElementType);
            if (t is ITypeParameterSymbol) return true;
            return t is INamedTypeSymbol n && Names.IsOpen(n);
        }

        // Generator.cs isNotPublic.
        static bool IsNotPublic(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol a) return IsNotPublic(a.ElementType);
            if (t is ITypeParameterSymbol) return true;
            if (!(t is INamedTypeSymbol n)) return false;
            if (n.TypeArguments.Any(IsNotPublic)) return true;
            return !Names.IsPublic(n);
        }

        static bool IsValueTypeForHotfix(ITypeSymbol t)
        {
            return t.TypeKind != TypeKind.TypeParameter && t.IsValueType;
        }

        // Generator.cs injectByGeneric: methods the injector patches through the
        // generic DelegateBridge calls (InParam<T> ...), which need no bridge.
        static bool InjectByGeneric(IMethodSymbol m, int flags)
        {
            bool boxing = HasHotfixFlag(flags, ValueTypeBoxing);
            bool ctor = m.MethodKind == MethodKind.Constructor;
            if (!ctor && !m.ReturnsVoid && (IsNotPublic(m.ReturnType) || HasGenericParameter(m.ReturnType))) return true;
            var type = m.ContainingType;
            if (!m.IsStatic && ((IsValueTypeForHotfix(type) && !boxing && IsNotPublic(type)) || HasGenericParameter(type))) return true;
            foreach (var p in m.Parameters)
            {
                if ((((IsValueTypeForHotfix(p.Type) && !boxing) || p.RefKind != RefKind.None || p.IsParams) && IsNotPublic(p.Type)) || HasGenericParameter(p.Type))
                    return true;
            }
            return false;
        }

        // The bridge of SignatureKey.Of(m, Hotfix, flags): self first, value types
        // kept unless boxed, other plain parameters as object.
        static BridgeSignature HotfixSignature(IMethodSymbol m, int flags, ITypeSymbol objectType)
        {
            bool boxing = HasHotfixFlag(flags, ValueTypeBoxing);
            var sig = new BridgeSignature { Return = m.MethodKind == MethodKind.Constructor || m.ReturnsVoid ? null : m.ReturnType };
            if (!m.IsStatic)
            {
                sig.Parameters.Add(new BridgeParam { Type = IsValueTypeForHotfix(m.ContainingType) && !boxing ? m.ContainingType : objectType, Kind = RefKind.None });
            }
            foreach (var p in BridgeSignature.Of(m).Parameters.Zip(m.Parameters, (bp, ps) => new { bp, ps }))
            {
                bool keep = p.ps.RefKind != RefKind.None || p.ps.IsParams || (IsValueTypeForHotfix(p.ps.Type) && !boxing);
                if (!keep) p.bp.Type = objectType;
                sig.Parameters.Add(p.bp);
            }
            return sig;
        }
    }
}
