// SPIKE (spike/descriptor-wrappers, not a product feature; owner Q89,
// 2026-10-19; docs/research/descriptor-wrappers.md).
//
// XLUA_SPIKE_SUBSET (in the compilation's preprocessor symbols): every mode
// (plain, GEN_CODE_MINIMIZE) wraps only the members the descriptor prototype
// covers, so the three modes bind the same member set.
//
// XLUA_SPIKE_DESC (with GEN_CODE_MINIMIZE and an XLua that has XLua.DescType,
// Assets/XLua/Src/SpikeDescriptorCore.cs): descriptor wrappers. Per wrapped
// type one descriptor string, one switch method with a case per overload, and
// a registration that hands both to the runtime's erased core.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    internal static partial class Wrappers
    {
        const int K_VOID = 0, K_BOOL = 1, K_I8 = 2, K_U8 = 3, K_I16 = 4, K_U16 = 5, K_CHAR = 6, K_I32 = 7, K_U32 = 8,
            K_I64 = 9, K_U64 = 10, K_F32 = 11, K_F64 = 12, K_STR = 13, K_OBJ = 14, K_UOBJ = 15, K_ANY = 16, K_STRUCT = 17,
            K_RINT = 18, K_RPUSHANY = 19;
        const int F_STATIC = 1, F_CTOR = 2, F_STRUCT_SELF = 4, F_UPDATE = 8;
        const int MaxArgs = 8;

        internal static bool Defined(Compilation compilation, string symbol)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.Options is Microsoft.CodeAnalysis.CSharp.CSharpParseOptions o) return o.PreprocessorSymbolNames.Contains(symbol);
            }
            return false;
        }

        static bool HasTypeParameter(ITypeSymbol t)
        {
            if (t.TypeKind == TypeKind.TypeParameter) return true;
            if (t is IArrayTypeSymbol a) return HasTypeParameter(a.ElementType);
            if (t is INamedTypeSymbol n)
            {
                foreach (var x in n.TypeArguments) if (HasTypeParameter(x)) return true;
                if (n.ContainingType != null && HasTypeParameter(n.ContainingType)) return true;
            }
            return false;
        }

        // The kind of a parameter, field or property Lua sets; -1: not covered.
        static int ArgKind(ITypeSymbol t, MemberContext ctx)
        {
            if (t == null || t.TypeKind == TypeKind.Error || t.TypeKind == TypeKind.Pointer || t.TypeKind == TypeKind.FunctionPointer
                || HasTypeParameter(t) || t.IsRefLikeType) return -1;
            switch (t.SpecialType)
            {
                case SpecialType.System_Boolean: return K_BOOL;
                case SpecialType.System_SByte: return K_I8;
                case SpecialType.System_Byte: return K_U8;
                case SpecialType.System_Int16: return K_I16;
                case SpecialType.System_UInt16: return K_U16;
                case SpecialType.System_Char: return K_CHAR;
                case SpecialType.System_Int32: return K_I32;
                case SpecialType.System_UInt32: return K_U32;
                case SpecialType.System_Int64: return K_I64;
                case SpecialType.System_UInt64: return K_U64;
                case SpecialType.System_Single: return K_F32;
                case SpecialType.System_Double: return K_F64;
                case SpecialType.System_String: return K_STR;
                case SpecialType.System_Object: return K_ANY;
                case SpecialType.System_Decimal: case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr: return -1;
            }
            if (IsDelegate(t)) return -1;
            if (t is IArrayTypeSymbol arr)
            {
                return arr.ElementType.SpecialType == SpecialType.System_Byte ? -1 : K_OBJ;
            }
            string full = Names.Clr(t);
            if (full.StartsWith("XLua.") || full.StartsWith("System.TypedReference") || full.StartsWith("System.ArgIterator")) return -1;
            if (t.TypeKind == TypeKind.Interface) return K_OBJ;
            if (t.IsValueType)
            {
                if (t.OriginalDefinition != null && t.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return -1;
                var s = StructsOf(ctx.Compilation);
                if (s.GetterOf(t) != null || s.PushedType(t) != null) return -1;
                return K_STRUCT;
            }
            if (t.TypeKind == TypeKind.Class) return RefusesTables(t) ? K_UOBJ : K_OBJ;
            return -1;
        }

        // The kind of a result; -1: not covered.
        static int RetKind(ITypeSymbol t, MemberContext ctx)
        {
            if (t.SpecialType == SpecialType.System_Void) return K_VOID;
            int k = ArgKind(t, ctx);
            switch (k)
            {
                case -1: return -1;
                case K_I8: case K_U8: case K_I16: case K_U16: case K_CHAR: case K_I32: return K_RINT;
                case K_F32: return K_F64;
                case K_ANY: return K_RPUSHANY;
                case K_UOBJ: case K_STRUCT: return K_OBJ;
                case K_OBJ: return t.TypeKind == TypeKind.Interface ? K_RPUSHANY : K_OBJ;
                default: return k;
            }
        }

        static bool OverloadCovered(Overload o, MemberContext ctx)
        {
            var ps = o.Parameters;
            if (ps.Length > 0 && ps[ps.Length - 1].IsParams) return false;
            int real = ps.Length - o.DefaultCount;
            if (real > MaxArgs) return false;
            for (int i = 0; i < real; i++)
            {
                if (ps[i].RefKind != RefKind.None || ArgKind(ps[i].Type, ctx) < 0) return false;
            }
            return true;
        }

        // XLUA_SPIKE_SUBSET: the members the descriptor prototype covers, in every mode.
        static void SpikeFilter(ClassInfo info, MemberContext ctx)
        {
            var type = info.Type;
            bool gcoSelf = type.IsValueType && (StructsOf(ctx.Compilation).GetterOf(type) != null || StructsOf(ctx.Compilation).PushedType(type) != null);
            bool ctorOk = RetKind(type, ctx) >= 0;
            info.Constructors.RemoveAll(o => !ctorOk || !OverloadCovered(o, ctx));
            foreach (var g in info.Methods)
            {
                g.Overloads.RemoveAll(o => o.IsExtension || o.Method.IsExtensionMethod || o.Method.IsGenericMethod || o.Method.ReturnsByRef
                    || o.Method.ReturnsByRefReadonly || o.Method.Name == "get_Item" || o.Method.Name == "set_Item"
                    || (!g.IsStatic && gcoSelf) || !OverloadCovered(o, ctx) || (!o.Method.ReturnsVoid && RetKind(o.Method.ReturnType, ctx) < 0));
            }
            info.Methods.RemoveAll(g => g.Overloads.Count == 0);
            info.Getters.RemoveAll(g => RetKind(g.Type, ctx) < 0 || (!g.IsStatic && gcoSelf));
            info.Setters.RemoveAll(s => ArgKind(s.Type, ctx) < 0 || (!s.IsStatic && gcoSelf));
            info.Operators.Clear();
            info.Indexers.Clear();
            info.NewIndexers.Clear();
            info.Events.Clear();
        }

        // Lua-visible functions and their overloads after the filter (the per-member slope's N).
        static string SpikeCounts(ClassInfo info)
        {
            int groups = 0, overloads = 0;
            if (info.Constructors.Count > 0 || info.Type.IsValueType) { groups++; overloads += info.Constructors.Count; }
            foreach (var m in info.Methods) { groups++; overloads += m.Overloads.Count; }
            foreach (var g in info.Getters.Where(g => !(g.IsStatic && g.ReadOnly))) { groups++; overloads++; }
            foreach (var s in info.Setters) { groups++; overloads++; }
            return groups + "," + overloads;
        }

        static string ArgExpr(int kind, ITypeSymbol t, int i, Compilation c)
        {
            switch (kind)
            {
                case K_BOOL: return "(a.l" + i + " != 0)";
                case K_I8: return "(sbyte)a.l" + i;
                case K_U8: return "(byte)a.l" + i;
                case K_I16: return "(short)a.l" + i;
                case K_U16: return "(ushort)a.l" + i;
                case K_CHAR: return "(char)a.l" + i;
                case K_I32: return "(int)a.l" + i;
                case K_U32: return "(uint)a.l" + i;
                case K_I64: return "a.l" + i;
                case K_U64: return "(ulong)a.l" + i;
                case K_F32: return "(float)a.d" + i;
                case K_F64: return "a.d" + i;
                case K_STR: return "(string)a.o" + i;
                case K_ANY: return "a.o" + i;
                default: return "(" + Names.Cs(t) + ")a.o" + i;
            }
        }

        static string Assign(int ret, string expr)
        {
            switch (ret)
            {
                case K_VOID: return expr + ";";
                case K_BOOL: return "a.rl = (" + expr + ") ? 1 : 0;";
                case K_U64: return "a.rl = (long)(" + expr + ");";
                case K_RINT: case K_U32: case K_I64: return "a.rl = " + expr + ";";
                case K_F64: return "a.rd = " + expr + ";";
                default: return "a.ro = " + expr + ";";
            }
        }

        sealed class DescBuilder
        {
            public readonly List<int> Data = new List<int>();
            public readonly List<ITypeSymbol> Types = new List<ITypeSymbol>();
            public readonly List<string> Cases = new List<string>();
            public MemberContext Ctx;

            public int TypeIndex(ITypeSymbol t)
            {
                for (int i = 0; i < Types.Count; i++) if (SymbolEqualityComparer.Default.Equals(Types[i], t)) return i;
                Types.Add(t);
                return Types.Count - 1;
            }

            public void Group(int slot, int flags, string name, int count)
            {
                Data.Add(slot);
                Data.Add(flags);
                foreach (char ch in name) Data.Add(ch);
                Data.Add(0);
                Data.Add(count);
            }

            // One overload: its case, argument kinds and result kind.
            public void Overload(IEnumerable<ITypeSymbol> args, int ret, string statement)
            {
                Data.Add(Cases.Count);
                var list = args.ToList();
                Data.Add(list.Count);
                foreach (var t in list)
                {
                    int k = ArgKind(t, Ctx);
                    Data.Add(k);
                    if (k == K_OBJ || k == K_UOBJ || k == K_STRUCT) Data.Add(TypeIndex(t));
                }
                Data.Add(ret);
                Cases.Add(statement);
            }

            public string Literal()
            {
                var sb = new StringBuilder("\"");
                foreach (int v in Data)
                {
                    if (v >= 0x20 && v < 0x7f && v != '"' && v != '\\') sb.Append((char)v);
                    else sb.Append("\\u").Append(v.ToString("x4"));
                }
                return sb.Append('"').ToString();
            }
        }

        static string Args(IParameterSymbol[] ps, int real, MemberContext ctx)
        {
            var parts = new List<string>();
            for (int i = 0; i < real; i++) parts.Add(ArgExpr(ArgKind(ps[i].Type, ctx), ps[i].Type, i, ctx.Compilation));
            return string.Join(", ", parts);
        }

        static TypeModel DescClassModel(INamedTypeSymbol type, ClassInfo info, MemberContext ctx)
        {
            var c = ctx.Compilation;
            string luaName = Names.Clr(type);
            string cs = Names.Cs(type);
            string className = ClassName(luaName);
            bool vt = type.IsValueType;
            var w = new Writer { Indent = 1, Gcm = true };
            var d = new DescBuilder { Ctx = ctx };

            int objMethods = 0, objGetters = 0, objSetters = 0, clsFields = 1, clsGetters = 0, clsSetters = 0;
            foreach (var m in info.Methods) { if (m.IsStatic) clsFields++; else objMethods++; }
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
            string self = "((" + cs + ")a.self)";

            foreach (var g in info.Methods)
            {
                int flags = g.IsStatic ? F_STATIC : (vt ? F_STRUCT_SELF | F_UPDATE : 0);
                d.Group(g.IsStatic ? 3 : 0, flags, g.Name, g.Overloads.Count);
                foreach (var o in g.Overloads)
                {
                    var m = o.Method;
                    var ps = o.Parameters;
                    int real = ps.Length - o.DefaultCount;
                    int ret = m.ReturnsVoid ? K_VOID : RetKind(m.ReturnType, ctx);
                    string args = Args(ps, real, ctx) + ExplicitDefaults(o, real);
                    string stmt;
                    if (g.IsStatic) stmt = Assign(ret, Names.Cs(m.ContainingType) + "." + Escape(m.Name) + "(" + args + ")");
                    else if (vt) stmt = "{ var s = " + self + "; " + Assign(ret, "s." + Escape(m.Name) + "(" + args + ")") + " a.self = s; }";
                    else stmt = Assign(ret, self + "." + Escape(m.Name) + "(" + args + ")");
                    d.Overload(ps.Take(real).Select(p => p.Type), ret, stmt);
                }
            }
            foreach (var g in info.Getters.Where(g => !(g.IsStatic && g.ReadOnly)))
            {
                int ret = RetKind(g.Type, ctx);
                d.Group(g.IsStatic ? 4 : 1, g.IsStatic ? F_STATIC : (vt ? F_STRUCT_SELF : 0), g.Name, 1);
                d.Overload(Enumerable.Empty<ITypeSymbol>(), ret, Assign(ret, (g.IsStatic ? cs : self) + "." + Escape(g.Name)));
            }
            foreach (var s in info.Setters)
            {
                string value = ArgExpr(ArgKind(s.Type, ctx), s.Type, 0, c);
                string stmt = s.IsStatic ? cs + "." + Escape(s.Name) + " = " + value + ";"
                    : vt ? "{ var s = " + self + "; s." + Escape(s.Name) + " = " + value + "; a.self = s; }"
                    : self + "." + Escape(s.Name) + " = " + value + ";";
                d.Group(s.IsStatic ? 5 : 2, s.IsStatic ? F_STATIC : (vt ? F_STRUCT_SELF | F_UPDATE : 0), s.Name, 1);
                d.Overload(new[] { s.Type }, K_VOID, stmt);
            }
            bool descCreator = RetKind(type, ctx) >= 0 && (info.Constructors.Count > 0 || vt);
            if (descCreator)
            {
                bool hasZero = info.Constructors.Any(o => o.Parameters.Length == 0);
                bool addDefault = vt && !hasZero;
                d.Group(6, F_CTOR, "", info.Constructors.Count + (addDefault ? 1 : 0));
                foreach (var o in info.Constructors)
                {
                    var ps = o.Parameters;
                    int real = ps.Length - o.DefaultCount;
                    d.Overload(ps.Take(real).Select(p => p.Type), K_OBJ, "a.ro = new " + cs + "(" + Args(ps, real, ctx) + ExplicitDefaults(o, real) + ");");
                }
                if (addDefault) d.Overload(Enumerable.Empty<ITypeSymbol>(), K_OBJ, "a.ro = default(" + cs + ");");
            }

            w.Line("// " + luaName + " (spike: descriptor wrapper, " + d.Cases.Count + " cases)");
            w.Open("internal static class " + className);
            w.Open("internal static void __Register(global::System.IntPtr L)");
            w.Line(FindTranslator);
            w.Line("global::System.Type type = typeof(" + cs + ");");
            w.Line("var gen_desc = new global::XLua.DescType(translator, type, " + Names.Literal(Display(type)) + ", " + d.Literal()
                + ", new global::System.Type[] { " + string.Join(", ", d.Types.Select(t => "typeof(" + Names.Cs(t) + ")")) + " }, translator.__DescInvoke);");
            w.Line(Utils + ".BeginObjectRegister(type, L, translator, 0, " + objMethods + ", " + objGetters + ", " + objSetters + ");");
            w.Line("gen_desc.Register(L, false);");
            foreach (var l in info.Lazy.Where(l => !l.IsStatic)) LazyRegistration(w, l);
            w.Line(Utils + ".EndObjectRegister(type, L, translator, null, null, null, null, null);");
            w.Line();
            w.Line(Utils + ".BeginClassRegister(type, L, " + (descCreator ? "gen_desc.Creator" : "translator.__CreateInstance") + ", " + clsFields + ", " + clsGetters + ", " + clsSetters + ");");
            w.Line("gen_desc.Register(L, true);");
            foreach (var g in info.Getters.Where(g => g.IsStatic && g.ReadOnly))
                w.Line(Utils + ".RegisterObject(L, translator, " + Utils + ".CLS_IDX, " + Names.Literal(g.Name) + ", " + cs + "." + Escape(g.Name) + ");");
            foreach (var l in info.Lazy.Where(l => l.IsStatic)) LazyRegistration(w, l);
            w.Line(Utils + ".EndClassRegister(type, L, translator);");
            w.Close();
            if (!descCreator) Constructor(w, info, c);
            w.Line();
            w.Open("static void __DescInvoke(this " + Translator + " translator, int id, ref global::XLua.DescArgs a)");
            w.Open("switch (id)");
            for (int i = 0; i < d.Cases.Count; i++) w.Line("case " + i + ": " + d.Cases[i] + " return;");
            w.Close();
            w.Close();
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
                SpikeCounts = SpikeCounts(info),
            };
        }
    }
}
