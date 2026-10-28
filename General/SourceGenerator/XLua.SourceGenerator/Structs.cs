// GCOptimize (docs/design/source-generator.md, section 2 rows 6 to 8,
// milestone M4): push, get and update of structs and enums without boxing, the
// pack and unpack of struct fields with the float grouping, the readers of
// table-optimised types and the array accessors. The C# is that of the legacy
// templates LuaWrapPusher.tpl.txt and PackUnpack.tpl.txt, the selection that of
// Generator.cs (GenWrapPusher, GenPackUnpack, getXluaTypeInfo, SizeOf), in two
// internal static classes, Structs and PackUnpack, of the assembly's generated
// namespace. Registration goes through AssemblyBindings.Struct and Caster,
// one Ops_ method per type; the per-LuaEnv type ids and enum caches through
// the slots of BindingApi.AllocateSlot, TypeId and EnumCacheRef.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    // A struct field or an [AdditionalProperties] property (Generator.cs XluaFieldInfo).
    internal sealed class StructField
    {
        public string Name;
        public ITypeSymbol Type;
        public bool IsField;
        public int Size;
    }

    // A GCOptimize type or a [LuaCallCSharp] enum (Generator.cs purevaluetypes,
    // tableoptimzetypes and the PackUnpack type infos).
    internal sealed class StructType
    {
        public INamedTypeSymbol Type;
        public string Clr;
        public string Id;            // the suffix of the generated member names
        public int Flag;             // OptimizeFlag: 0 Default, 1 PackAsTable
        public bool IsEnum;
        public int Size;             // SizeOf, -1 when not packable
        public List<StructField> Fields;
        public List<List<StructField>> Groups;   // float fields packed together, or null
        public bool IsRoot;          // configured (not only a nested field type): has the table reader
        public bool Own;             // configured by this assembly (or a [LuaCallCSharp] enum): always written and registered
    }

    // What the GCOptimize configuration of a compilation selects.
    internal sealed class StructSet
    {
        public static readonly StructSet Empty = new StructSet();

        public readonly List<StructType> Values = new List<StructType>();   // pushed as userdata or table (purevaluetypes)
        public readonly List<StructType> Tables = new List<StructType>();   // read from tables only (tableoptimzetypes)
        public readonly List<StructType> Packed = new List<StructType>();   // Pack/UnPack overloads, with nested struct types
        public readonly HashSet<string> Pushed = new HashSet<string>();     // wrappers push and update through Structs (genPushAndUpdateTypes)
        public readonly Dictionary<string, StructType> Getters = new Dictionary<string, StructType>();   // wrappers read through Structs (StructGetName)
        public readonly List<string> NotPackable = new List<string>();       // XLSG010: GCOptimize structs read from tables only
        public string Fingerprint = "";

        public StructSet Copy()
        {
            var copy = new StructSet { Fingerprint = Fingerprint };
            copy.Values.AddRange(Values);
            copy.Tables.AddRange(Tables);
            copy.Packed.AddRange(Packed);
            copy.Pushed.UnionWith(Pushed);
            foreach (var kv in Getters) copy.Getters.Add(kv.Key, kv.Value);
            copy.NotPackable.AddRange(NotPackable);
            return copy;
        }

        public StructType PushedType(ITypeSymbol t)
        {
            StructType s;
            return Getters.TryGetValue(Names.Clr(t), out s) && Pushed.Contains(s.Clr) ? s : null;
        }

        public StructType GetterOf(ITypeSymbol t)
        {
            StructType s;
            return Getters.TryGetValue(Names.Clr(t), out s) ? s : null;
        }
    }

    internal static partial class Wrappers
    {
        // The StructSet of the compilation the wrappers are written for; Push,
        // Cast, Update and Self read it.
        static readonly ConditionalWeakTable<Compilation, StructSet> structSets = new ConditionalWeakTable<Compilation, StructSet>();

        static StructSet StructsOf(Compilation c)
        {
            StructSet set;
            return structSets.TryGetValue(c, out set) ? set : StructSet.Empty;
        }

        static readonly Dictionary<SpecialType, int> PrimitiveSize = new Dictionary<SpecialType, int>
        {
            { SpecialType.System_Byte, 1 }, { SpecialType.System_SByte, 1 }, { SpecialType.System_Int16, 2 }, { SpecialType.System_UInt16, 2 },
            { SpecialType.System_Int32, 4 }, { SpecialType.System_UInt32, 4 }, { SpecialType.System_Int64, 8 }, { SpecialType.System_UInt64, 8 },
            { SpecialType.System_Single, 4 }, { SpecialType.System_Double, 8 }, { SpecialType.System_Decimal, 16 },
            // Generator.cs type_size: IntPtr and UIntPtr take eight bytes on every platform.
            { SpecialType.System_Boolean, 1 }, { SpecialType.System_Char, 2 }, { SpecialType.System_IntPtr, 8 }, { SpecialType.System_UIntPtr, 8 },
        };

        sealed class GcConfig
        {
            public readonly List<KeyValuePair<INamedTypeSymbol, int>> Types = new List<KeyValuePair<INamedTypeSymbol, int>>();
            public readonly HashSet<string> Seen = new HashSet<string>();
            public readonly HashSet<string> Own = new HashSet<string>();
            public readonly Dictionary<string, HashSet<string>> Additional = new Dictionary<string, HashSet<string>>();
            public readonly Dictionary<string, int> Sizes = new Dictionary<string, int>();

            public void Add(ITypeSymbol t, int flag)
            {
                var named = t as INamedTypeSymbol;
                if (named == null || Names.IsOpen(named) || !Names.IsPublic(named) || !Seen.Add(Names.Clr(named))) return;
                Types.Add(new KeyValuePair<INamedTypeSymbol, int>(named, flag));
            }

            public void AddProperties(ITypeSymbol t, IEnumerable<string> names)
            {
                string key = Names.Clr(t);
                HashSet<string> set;
                if (!Additional.TryGetValue(key, out set)) Additional.Add(key, set = new HashSet<string>());
                set.UnionWith(names);
            }
        }

        static bool IsXLuaAttribute(AttributeData a, string name)
        {
            return a.AttributeClass != null && a.AttributeClass.ToDisplayString() == "XLua." + name;
        }

        // Generator.cs isDefined(prop, AdditionalPropertiesAttribute) or the
        // configured names of the type.
        static bool IsAdditional(IPropertySymbol p, GcConfig cfg)
        {
            HashSet<string> names;
            if (cfg.Additional.TryGetValue(Names.Clr(p.ContainingType), out names) && names.Contains(p.Name)) return true;
            return p.GetAttributes().Any(a => IsXLuaAttribute(a, "AdditionalPropertiesAttribute"));
        }

        static IEnumerable<IFieldSymbol> PublicFields(ITypeSymbol t)
        {
            return t.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst && f.DeclaredAccessibility == Accessibility.Public);
        }

        static IEnumerable<IPropertySymbol> AdditionalProperties(ITypeSymbol t, GcConfig cfg)
        {
            return t.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public
                && IsAdditional(p, cfg));
        }

        // CopyByValue.IsStruct: a value type that is neither an enum nor primitive.
        static bool IsCopyStruct(ITypeSymbol t)
        {
            return t.IsValueType && t.TypeKind != TypeKind.Enum && t.TypeKind != TypeKind.TypeParameter && !MemberContext.IsPrimitive(t);
        }

        // Generator.cs SizeOf: the packed size, or -1 when a field is not a
        // primitive, decimal or packable struct.
        static int SizeOf(ITypeSymbol t, GcConfig cfg)
        {
            int size;
            if (PrimitiveSize.TryGetValue(t.SpecialType, out size)) return size;
            if (!IsCopyStruct(t)) return -1;
            string key = Names.Clr(t);
            if (cfg.Sizes.TryGetValue(key, out size)) return size;
            cfg.Sizes[key] = -1; // a struct cannot contain itself; this also ends cycles
            size = 0;
            foreach (var f in PublicFields(t))
            {
                int s = SizeOf(f.Type, cfg);
                if (s == -1) return -1;
                size += s;
            }
            foreach (var p in AdditionalProperties(t, cfg))
            {
                int s = SizeOf(p.Type, cfg);
                if (s == -1) return -1;
                size += s;
            }
            cfg.Sizes[key] = size;
            return size;
        }

        // Generator.cs getXluaTypeInfo: the fields, the [AdditionalProperties]
        // properties, and groups of up to six floats when every one is a float.
        static StructType Describe(INamedTypeSymbol t, int flag, GcConfig cfg)
        {
            var fields = PublicFields(t).Select(f => new StructField { Name = f.Name, Type = f.Type, IsField = true, Size = SizeOf(f.Type, cfg) })
                .Concat(AdditionalProperties(t, cfg).Select(p => new StructField { Name = p.Name, Type = p.Type, IsField = false, Size = SizeOf(p.Type, cfg) }))
                .ToList();
            List<List<StructField>> groups = null;
            if (fields.Count > 1 && fields.All(f => f.Type.SpecialType == SpecialType.System_Single))
            {
                groups = new List<List<StructField>>();
                for (int i = 0; i < fields.Count; i += 6) groups.Add(fields.Skip(i).Take(6).ToList());
            }
            string clr = Names.Clr(t);
            return new StructType
            {
                Type = t, Clr = clr, Id = ClassName(clr).Substring(2), Flag = flag, IsEnum = t.TypeKind == TypeKind.Enum,
                Size = t.TypeKind == TypeKind.Enum ? 4 : SizeOf(t, cfg), Fields = fields, Groups = groups,
            };
        }

        // The XLua runtime and the assemblies that reference it: only those can
        // carry xLua configuration attributes.
        static IEnumerable<IAssemblySymbol> ConfiguredReferences(Compilation compilation)
        {
            var api = compilation.GetTypeByMetadataName("XLua.Binding.BindingApi");
            if (api == null) yield break;
            var runtime = api.ContainingAssembly;
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (SymbolEqualityComparer.Default.Equals(assembly, runtime)
                    || assembly.Modules.Any(m => m.ReferencedAssemblySymbols.Any(r => SymbolEqualityComparer.Default.Equals(r, runtime))))
                {
                    yield return assembly;
                }
            }
        }

        static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
            {
                foreach (var x in WithNested(t)) yield return x;
            }
            foreach (var n in ns.GetNamespaceMembers())
            {
                foreach (var x in PublicTypes(n)) yield return x;
            }
        }

        static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol t)
        {
            if (t.DeclaredAccessibility != Accessibility.Public) yield break;
            yield return t;
            foreach (var n in t.GetTypeMembers())
            {
                foreach (var x in WithNested(n)) yield return x;
            }
        }

        // Generator.cs GetGenConfig for GCOptimize and AdditionalProperties, plus
        // the [LuaCallCSharp] enums (Generator.cs Gen and SetGenPushAndUpdateTypes).
        // The configuration of this assembly comes first, then that of the
        // referenced assemblies that can configure xLua (their assembly-level
        // attributes, such as xLua's own UnityEngine defaults, and [GCOptimize]
        // on their public types): a struct they configure is pushed without
        // boxing here too, through this assembly's own helpers, as the legacy
        // generator, which sees every assembly, does.
        static StructSet BuildStructs(Compilation compilation, EquatableArray<ConfigEntry> entries, Dictionary<string, ITypeSymbol> typed,
            List<INamedTypeSymbol> luaCallCSharp, MemberContext ctx)
        {
            var cfg = new GcConfig();
            Func<string, ITypeSymbol> resolve = name =>
            {
                ITypeSymbol symbol;
                return typed.TryGetValue(name, out symbol) ? symbol : compilation.GetTypeByMetadataName(name);
            };
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.AdditionalProperties && x.Problem == TypeProblem.None && x.LuaName != null && !string.IsNullOrEmpty(x.Names)))
            {
                var t = resolve(e.LuaName);
                if (t != null) cfg.AddProperties(t, e.Names.Split('|'));
            }
            var references = ConfiguredReferences(compilation).ToList();
            foreach (var assembly in references)
            {
                foreach (var a in assembly.GetAttributes().Where(a => IsXLuaAttribute(a, "AdditionalPropertiesAttribute")))
                {
                    var args = a.ConstructorArguments;
                    if (args.Length > 1 && args[0].Value is ITypeSymbol t && args[1].Kind == TypedConstantKind.Array)
                        cfg.AddProperties(t, args[1].Values.Select(v => v.Value as string).Where(v => v != null));
                }
            }
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.GCOptimize && x.Problem == TypeProblem.None && x.LuaName != null)
                .OrderBy(x => x.Path ?? "", StringComparer.Ordinal).ThenBy(x => x.Span.Start))
            {
                var t = resolve(e.LuaName);
                if (t != null && !Names.IsObsolete(t, ctx.ObsoleteErrorsOnly))
                {
                    cfg.Add(t, e.Flags);
                    cfg.Own.Add(Names.Clr(t));
                }
            }
            foreach (var assembly in references)
            {
                foreach (var a in assembly.GetAttributes().Where(a => IsXLuaAttribute(a, "GCOptimizeAttribute")))
                {
                    // [assembly: GCOptimize(typeof(T), flags)] and [assembly: GCOptimize(flags, typeof(A), typeof(B)...)].
                    int flags = Config.FlagsOf(a.ConstructorArguments);
                    foreach (var t in Config.ConfiguredTypes(a))
                    {
                        if (!Names.IsObsolete(t, ctx.ObsoleteErrorsOnly)) cfg.Add(t, flags);
                    }
                }
                foreach (var t in PublicTypes(assembly.GlobalNamespace))
                {
                    var a = t.GetAttributes().FirstOrDefault(x => IsXLuaAttribute(x, "GCOptimizeAttribute"));
                    if (a == null || Names.IsObsolete(t, ctx.ObsoleteErrorsOnly)) continue;
                    cfg.Add(t, Config.FlagsOf(a.ConstructorArguments));
                }
            }

            var set = new StructSet();
            var gc = cfg.Types.Select(kv => Describe(kv.Key, kv.Value, cfg)).ToList();
            var enums = luaCallCSharp.Where(t => t.TypeKind == TypeKind.Enum).Select(t => Describe(t, 0, cfg)).ToList();
            foreach (var s in gc) s.Own = cfg.Own.Contains(s.Clr);
            foreach (var s in enums) s.Own = true;
            var all = new List<StructType>();
            var seen = new HashSet<string>();
            foreach (var s in gc.Concat(enums))
            {
                if (MemberContext.IsPrimitive(s.Type) || !seen.Add(s.Clr)) continue;
                all.Add(s);
            }
            // genPushAndUpdateTypes: the packable GCOptimize types and the [LuaCallCSharp] enums.
            foreach (var s in gc.Where(s => !s.IsEnum && s.Size != -1)) set.Pushed.Add(s.Clr);
            foreach (var s in enums) set.Pushed.Add(s.Clr);
            foreach (var s in all.OrderBy(s => s.Clr, StringComparer.Ordinal))
            {
                if (s.IsEnum || s.Size != -1)
                {
                    // A GCOptimize enum that is not [LuaCallCSharp] gets registered
                    // pushers, but wrappers box it (StructGetName).
                    set.Values.Add(s);
                    if (set.Pushed.Contains(s.Clr)) set.Getters[s.Clr] = s;
                }
                else
                {
                    set.Tables.Add(s);
                    set.Getters[s.Clr] = s;
                    if (s.Type.IsValueType && s.Own) set.NotPackable.Add(s.Clr);
                }
            }

            // Generator.cs GenPackUnpack: the packable GCOptimize structs and the
            // struct types of their fields (AllSubStruct).
            var packed = new Dictionary<string, StructType>();
            var order = new List<string>();
            Action<ITypeSymbol> walk = null;
            walk = t =>
            {
                if (MemberContext.IsPrimitive(t) || t.SpecialType == SpecialType.System_Decimal || !(t is INamedTypeSymbol n)) return;
                string clr = Names.Clr(t);
                if (packed.ContainsKey(clr)) return;
                var info = Describe(n, 0, cfg);
                packed.Add(clr, info);
                order.Add(clr);
                foreach (var f in info.Fields) walk(f.Type);
            };
            var roots = new HashSet<string>();
            foreach (var s in gc.Where(s => !s.IsEnum && s.Size != -1 && !MemberContext.IsPrimitive(s.Type)))
            {
                roots.Add(s.Clr);
                walk(s.Type);
            }
            foreach (var clr in order.OrderBy(x => x, StringComparer.Ordinal))
            {
                packed[clr].IsRoot = roots.Contains(clr);
                set.Packed.Add(packed[clr]);
            }

            var fp = new StringBuilder();
            foreach (var s in set.Values) fp.Append("|v").Append(s.Clr).Append(set.Pushed.Contains(s.Clr) ? "+" : "-");
            foreach (var s in set.Tables) fp.Append("|t").Append(s.Clr);
            set.Fingerprint = fp.ToString();
            return set;
        }

        // ---- Structs: push, get, update (LuaWrapPusher.tpl.txt) ----

        const string Api = "global::XLua.Binding.BindingApi";

        static string StructsSource(StructSet set, Compilation c)
        {
            var w = new Writer { Indent = 2 };
            foreach (var s in set.Values) w.Line("static readonly int s_" + s.Id + " = " + Api + ".AllocateSlot(typeof(" + Names.Typeof(s.Type) + "));");
            foreach (var s in set.Values) ValuePushers(w, s, c);
            foreach (var s in set.Tables) TableReader(w, s, c);
            foreach (var s in set.Values) Ops(w, s, true);
            foreach (var s in set.Tables) Ops(w, s, false);
            return w.ToString();
        }

        // What AssemblyBindings.Struct (pushed types) and Caster (types Lua
        // passes as tables) register, in one method per type (XLua.Binding's
        // StructOps): the boxed push, get and update, the array element
        // access, and the typed delegates of ObjectTranslator.PushByType and
        // Get<T>. xLua then has no generic code per type, which IL2CPP would
        // compile for each value type with its delegate types, as for the
        // Struct<T>, Caster<T> and ArrayAccessor<T> of binding API version 1;
        // each case does what the delegates those registered did. Value_ and
        // Push_ (an extension method) are the typed delegates' targets,
        // closed over the translator without a closure class.
        static void Ops(Writer w, StructType s, bool value)
        {
            string cs = Names.Cs(s.Type);
            const string Op = "global::XLua.Binding.StructOp.";
            if (value)
            {
                w.Line();
                w.Open("static " + cs + " Value_" + s.Id + "(this global::XLua.ObjectTranslator translator, global::System.IntPtr L, int index)");
                w.Line(cs + " val;");
                // ObjectTranslator.Get<T> reads results (result: true, Q35).
                w.Line("Get_" + s.Id + "(translator, L, index, out val, true);");
                w.Line("return val;");
                w.Close();
            }
            w.Line();
            w.Open("internal static object Ops_" + s.Id + "(int op, global::XLua.ObjectTranslator translator, global::System.IntPtr L, int index, object value, int arrayIndex)");
            w.Open("switch (op)");
            if (value)
            {
                w.Line("case " + Op + "Push:");
                w.Line("    Push_" + s.Id + "(translator, L, (" + cs + ")value);");
                w.Line("    return null;");
            }
            w.Line("case " + Op + "Get:");
            w.Line("{");
            w.Indent++;
            w.Line(cs + " val;");
            // The custom get and the caster of GetObject, which reads results
            // too (Q35); arguments are checked before it (ObjectCasters.GetArgCaster).
            w.Line("Get_" + s.Id + "(translator, L, index, out val, true);");
            w.Line("return val;");
            w.Close();
            if (value)
            {
                w.Line("case " + Op + "Update:");
                w.Line("    Update_" + s.Id + "(translator, L, index, (" + cs + ")value);");
                w.Line("    return null;");
                w.Line("case " + Op + "ArrayGet:");
                w.Line("    Push_" + s.Id + "(translator, L, ((" + cs + "[])value)[arrayIndex]);");
                w.Line("    return null;");
            }
            w.Line("case " + Op + "ArraySet:");
            w.Line("    Get_" + s.Id + "(translator, L, index, out ((" + cs + "[])value)[arrayIndex]);");
            w.Line("    return null;");
            if (value)
            {
                w.Line("case " + Op + "PushDelegate:");
                w.Line("    return new global::System.Action<global::System.IntPtr, " + cs + ">(translator.Push_" + s.Id + ");");
                w.Line("case " + Op + "GetDelegate:");
                w.Line("    return new global::System.Func<global::System.IntPtr, int, " + cs + ">(translator.Value_" + s.Id + ");");
            }
            w.Line("default:");
            w.Line("    return null;");
            w.Close();
            w.Close();
        }

        static void ValuePushers(Writer w, StructType s, Compilation c)
        {
            string cs = Names.Cs(s.Type);
            string slot = "s_" + s.Id;
            // The type as the errors name it; BindingApi builds the messages.
            string name = Names.Literal(Display(s.Type));
            string pack = s.IsEnum ? "global::XLua.CopyByValue" : "PackUnpack";
            string value = s.IsEnum ? "(int)val" : "val";
            bool table = !s.IsEnum && s.Flag == 1;

            w.Line();
            w.Open("internal static void Push_" + s.Id + "(this global::XLua.ObjectTranslator translator, global::System.IntPtr L, " + cs + " val)");
            w.Line("int type_id = " + Api + ".TypeId(translator, L, " + slot + ");");
            if (s.IsEnum)
            {
                w.Line("int enum_ref = " + Api + ".EnumCacheRef(translator, L, " + slot + ");");
                w.Open("if (" + LuaAPI + ".xlua_tryget_cachedud(L, (int)val, enum_ref) == 1)");
                w.Line("return;");
                w.Close();
            }
            if (table)
            {
                w.Line(LuaAPI + ".xlua_pushcstable(L, " + s.Fields.Count + ", type_id);");
                foreach (var f in s.Fields)
                {
                    w.Line(LuaAPI + ".xlua_pushasciistring(L, " + Names.Literal(f.Name) + ");");
                    w.Line(Push(f.Type, "val." + Escape(f.Name), c) + ";");
                    w.Line(LuaAPI + ".lua_rawset(L, -3);");
                }
            }
            else
            {
                w.Line("global::System.IntPtr buff = " + LuaAPI + ".xlua_pushstruct(L, " + s.Size + ", type_id);");
                w.Open("if (!" + pack + ".Pack(buff, 0, " + value + "))");
                w.Line("throw " + Api + ".StructPackFailed(" + name + ", val, true);");
                w.Close();
            }
            if (s.IsEnum)
            {
                w.Line(LuaAPI + ".lua_getref(L, enum_ref);");
                w.Line(LuaAPI + ".lua_pushvalue(L, -2);");
                w.Line(LuaAPI + ".xlua_rawseti(L, -2, (int)val);");
                w.Line(LuaAPI + ".lua_pop(L, 1);");
            }
            w.Close();

            w.Line();
            // result: a value C# reads back from Lua (a bridge's result, a get
            // function of ObjectTranslator.Get<T>), where a struct gives
            // default(T) for nil or nothing (owner decision Q35 of 2026-10-28);
            // an argument raises (Q22, Q34).
            w.Open("internal static void Get_" + s.Id + "(global::XLua.ObjectTranslator translator, global::System.IntPtr L, int index, out " + cs + " val, bool result = false)");
            w.Line(LuaTypes + " type = " + LuaAPI + ".lua_type(L, index);");
            w.Open("if (type == " + LuaTypes + ".LUA_TUSERDATA)");
            // Another type's userdata, such as a C# object of another class,
            // raises in StructBuffer as in the other binding modes (owner
            // decision Q36); it threw Exception("invalid userdata for T").
            w.Line("global::System.IntPtr buff = " + Api + ".StructBuffer(translator, L, index, " + slot + ", " + name + ");");
            if (s.IsEnum)
            {
                w.Line("int e;");
                w.Open("if (!" + pack + ".UnPack(buff, 0, out e))");
            }
            else
            {
                w.Open("if (!" + pack + ".UnPack(buff, 0, out val))");
            }
            w.Line("throw " + Api + ".StructUnpackFailed(" + name + ");");
            w.Close();
            if (s.IsEnum) w.Line("val = (" + cs + ")e;");
            w.Close();
            if (!s.IsEnum)
            {
                w.Open("else if (type == " + LuaTypes + ".LUA_TTABLE)");
                w.Line("PackUnpack.UnPack(translator, L, index, out val);");
                w.Close();
            }
            w.Open("else");
            if (s.IsEnum)
            {
                // An argument that is no number or value name raises, as in the
                // other binding modes (owner decision Q34; it threw
                // NullReferenceException or "invalid value for enum T"); a
                // result goes through the enum's caster as before.
                w.Open("if (!result && type != " + LuaTypes + ".LUA_TNUMBER && type != " + LuaTypes + ".LUA_TSTRING)");
                w.Line("throw " + Api + ".StructExpected(L, index, typeof(" + cs + "));");
                w.Close();
                w.Line("val = (" + cs + ")" + Api + ".GetCaster(translator, typeof(" + cs + "))(L, index, null);");
            }
            else
            {
                // nil, a number, a string or nothing raises, as in the other
                // binding modes (ObjectCasters.StructExpected); it threw
                // NullReferenceException. A result where Lua has no value is
                // default(T) (Q35).
                w.Open("if (result && (type == " + LuaTypes + ".LUA_TNIL || type == " + LuaTypes + ".LUA_TNONE))");
                w.Line("val = default(" + cs + ");");
                w.Line("return;");
                w.Close();
                w.Line("object obj = " + Api + ".GetCaster(translator, typeof(" + cs + "))(L, index, null);");
                w.Line("if (obj == null) throw " + Api + ".StructExpected(L, index, typeof(" + cs + "));");
                w.Line("val = (" + cs + ")obj;");
            }
            w.Close();
            w.Close();

            w.Line();
            w.Open("internal static void Update_" + s.Id + "(global::XLua.ObjectTranslator translator, global::System.IntPtr L, int index, " + cs + " val)");
            if (table)
            {
                w.Open("if (" + LuaAPI + ".lua_type(L, index) == " + LuaTypes + ".LUA_TTABLE)");
                w.Line("return;");
                w.Close();
            }
            else
            {
                w.Open("if (" + LuaAPI + ".lua_type(L, index) == " + LuaTypes + ".LUA_TUSERDATA)");
                w.Line("global::System.IntPtr buff = " + Api + ".StructBuffer(translator, L, index, " + slot + ", " + name + ");");
                w.Open("if (!" + pack + ".Pack(buff, 0, " + value + "))");
                w.Line("throw " + Api + ".StructPackFailed(" + name + ", val, false);");
                w.Close();
                w.Close();
            }
            w.Open("else");
            w.Line("throw " + Api + ".StructUpdateFailed(L, index);");
            w.Close();
            w.Close();
        }

        // A type that Lua passes as a table, field by field (the "table cast
        // optimze" part of LuaWrapPusher.tpl.txt).
        static void TableReader(Writer w, StructType s, Compilation c)
        {
            string cs = Names.Cs(s.Type);
            w.Line();
            // result: as for the packed readers above.
            w.Open("internal static void Get_" + s.Id + "(global::XLua.ObjectTranslator translator, global::System.IntPtr L, int index, out " + cs + " val, bool result = false)");
            w.Line(LuaTypes + " type = " + LuaAPI + ".lua_type(L, index);");
            w.Open("if (type == " + LuaTypes + ".LUA_TUSERDATA)");
            if (s.Type.IsValueType)
            {
                // A C# object of another class raises (owner decision Q36); the
                // cast threw the runtime's InvalidCastException.
                w.Line("object obj = " + Api + ".FastGetCSObj(translator, L, index);");
                w.Line("if (!(obj is " + cs + ")) throw " + Api + ".StructExpected(L, index, typeof(" + cs + "));");
                w.Line("val = (" + cs + ")obj;");
            }
            else
            {
                w.Line("val = (" + cs + ")" + Api + ".FastGetCSObj(translator, L, index);");
            }
            w.Close();
            w.Open("else if (type == " + LuaTypes + ".LUA_TTABLE)");
            w.Line("val = new " + cs + "();");
            w.Line("int top = " + LuaAPI + ".lua_gettop(L);");
            var fields = PublicFields(s.Type).ToList();
            // A struct table that sets none of the fields raises (Utils.StructTableSetNoField, xLua#751).
            bool countSet = s.Type.IsValueType && fields.Count > 0;
            if (countSet) w.Line("int __xlua_set = 0;");
            foreach (var f in fields)
            {
                w.Open("if (" + Utils + ".LoadField(L, index, " + Names.Literal(f.Name) + "))");
                if (countSet) w.Line("__xlua_set++;");
                w.Line(FieldGet(f.Type, "top + 1", "val." + Escape(f.Name), c) + ";");
                w.Close();
                w.Line(LuaAPI + ".lua_pop(L, 1);");
            }
            if (countSet) w.Line("if (__xlua_set == 0) " + Utils + ".StructTableSetNoField(typeof(" + cs + "));");
            w.Close();
            if (!s.Type.IsValueType)
            {
                w.Open("else if (type == " + LuaTypes + ".LUA_TNIL || type == " + LuaTypes + ".LUA_TNONE)");
                w.Line("val = null;");
                w.Close();
            }
            if (s.Type.IsValueType)
            {
                // A result where Lua has no value is default(T) (Q35).
                w.Open("else if (result && (type == " + LuaTypes + ".LUA_TNIL || type == " + LuaTypes + ".LUA_TNONE))");
                w.Line("val = default(" + cs + ");");
                w.Close();
            }
            w.Open("else");
            if (s.Type.IsValueType)
            {
                // As for the packed structs above (ObjectCasters.StructExpected).
                w.Line("throw " + Api + ".StructExpected(L, index, typeof(" + cs + "));");
            }
            else
            {
                w.Line("throw new global::System.Exception(\"can not cast \" + " + LuaAPI + ".lua_type(L, index) + \" to \" + typeof(" + cs + "));");
            }
            w.Close();
            w.Close();
        }

        // translator.<StructGetName>(L, idx, out target): the generated reader of
        // a GCOptimize type, or ObjectTranslator.Get<T>.
        static string FieldGet(ITypeSymbol t, string idx, string target, Compilation c)
        {
            var s = StructsOf(c).GetterOf(t);
            return s != null ? "Structs.Get_" + s.Id + "(translator, L, " + idx + ", out " + target + ")"
                : "translator.Get<" + Names.Cs(t) + ">(L, " + idx + ", out " + target + ")";
        }

        // ---- PackUnpack (PackUnpack.tpl.txt) ----

        static string PackSource(StructSet set, Compilation c)
        {
            var w = new Writer { Indent = 2 };
            foreach (var s in set.Packed)
            {
                string cs = Names.Cs(s.Type);
                if (s.IsRoot) RootUnPack(w, s, c);

                w.Line();
                w.Open("internal static bool Pack(global::System.IntPtr buff, int offset, " + cs + " field)");
                int offset = 0;
                if (s.Groups == null)
                {
                    foreach (var f in s.Fields)
                    {
                        w.Open("if (!" + PackClass(f.Type) + "Pack(buff, " + Offset(offset) + ", field." + Escape(f.Name) + "))");
                        w.Line("return false;");
                        w.Close();
                        offset += f.Size;
                    }
                }
                else
                {
                    foreach (var g in s.Groups)
                    {
                        w.Open("if (!" + LuaAPI + ".xlua_pack_float" + (g.Count == 1 ? "" : g.Count.ToString()) + "(buff, " + Offset(offset)
                            + string.Concat(g.Select(f => ", field." + Escape(f.Name))) + "))");
                        w.Line("return false;");
                        w.Close();
                        offset += g.Count * 4;
                    }
                }
                w.Line("return true;");
                w.Close();

                w.Line();
                w.Open("internal static bool UnPack(global::System.IntPtr buff, int offset, out " + cs + " field)");
                w.Line("field = default(" + cs + ");");
                offset = 0;
                if (s.Groups == null)
                {
                    foreach (var f in s.Fields)
                    {
                        string unpack = PackClass(f.Type) + "UnPack";
                        if (f.IsField)
                        {
                            w.Open("if (!" + unpack + "(buff, " + Offset(offset) + ", out field." + Escape(f.Name) + "))");
                            w.Line("return false;");
                            w.Close();
                        }
                        else
                        {
                            w.Line("var __gen_" + f.Name + " = field." + Escape(f.Name) + ";");
                            w.Open("if (!" + unpack + "(buff, " + Offset(offset) + ", out __gen_" + f.Name + "))");
                            w.Line("return false;");
                            w.Close();
                            w.Line("field." + Escape(f.Name) + " = __gen_" + f.Name + ";");
                        }
                        offset += f.Size;
                    }
                }
                else
                {
                    foreach (var g in s.Groups)
                    {
                        foreach (var f in g) w.Line("float __gen_" + f.Name + " = default(float);");
                        w.Open("if (!" + LuaAPI + ".xlua_unpack_float" + (g.Count == 1 ? "" : g.Count.ToString()) + "(buff, " + Offset(offset)
                            + string.Concat(g.Select(f => ", out __gen_" + f.Name)) + "))");
                        w.Line("return false;");
                        w.Close();
                        foreach (var f in g) w.Line("field." + Escape(f.Name) + " = __gen_" + f.Name + ";");
                        offset += g.Count * 4;
                    }
                }
                w.Line("return true;");
                w.Close();
            }
            return w.ToString();
        }

        // A configured struct from a Lua table, field by field.
        static void RootUnPack(Writer w, StructType s, Compilation c)
        {
            string cs = Names.Cs(s.Type);
            w.Line();
            w.Open("internal static void UnPack(global::XLua.ObjectTranslator translator, global::System.IntPtr L, int idx, out " + cs + " val)");
            w.Line("val = new " + cs + "();");
            w.Line("int top = " + LuaAPI + ".lua_gettop(L);");
            w.Line("int __xlua_set = 0;");
            foreach (var f in s.Fields)
            {
                w.Open("if (" + Utils + ".LoadField(L, idx, " + Names.Literal(f.Name) + "))");
                w.Line("__xlua_set++;");
                if (f.IsField)
                {
                    w.Line(FieldGet(f.Type, "top + 1", "val." + Escape(f.Name), c) + ";");
                }
                else
                {
                    w.Line("var __gen_" + f.Name + " = val." + Escape(f.Name) + ";");
                    w.Line(FieldGet(f.Type, "top + 1", "__gen_" + f.Name, c) + ";");
                    w.Line("val." + Escape(f.Name) + " = __gen_" + f.Name + ";");
                }
                w.Close();
                w.Line(LuaAPI + ".lua_pop(L, 1);");
            }
            if (s.Fields.Count > 0)
            {
                // A table that sets none of the fields raises (Utils.StructTableSetNoField, xLua#751).
                w.Line("if (__xlua_set == 0) " + Utils + ".StructTableSetNoField(typeof(" + cs + "));");
            }
            w.Close();
        }

        static readonly System.Text.RegularExpressions.Regex StructCall = new System.Text.RegularExpressions.Regex(@"\bStructs\.(?:Push|Get|Update)_(\w+)\(");

        // Types configured only by a referenced assembly (its own registration
        // covers them there) are written here only when this assembly's code
        // uses them: `used` is the generated wrappers and bridges, and the
        // helpers of the types kept pull in the types they use in turn.
        static void TrimStructs(StructSet set, string used, Compilation c)
        {
            var byId = set.Values.Concat(set.Tables).ToDictionary(s => s.Id);
            var keep = new HashSet<string>();
            var pending = new Queue<string>();
            Action<string> scan = text =>
            {
                foreach (System.Text.RegularExpressions.Match m in StructCall.Matches(text))
                {
                    string id = m.Groups[1].Value;
                    if (byId.ContainsKey(id) && keep.Add(id)) pending.Enqueue(id);
                }
            };
            foreach (var s in byId.Values.Where(s => s.Own))
            {
                if (keep.Add(s.Id)) pending.Enqueue(s.Id);
            }
            scan(used);
            while (pending.Count > 0)
            {
                var s = byId[pending.Dequeue()];
                var w = new Writer();
                if (set.Tables.Contains(s))
                {
                    TableReader(w, s, c);
                }
                else
                {
                    ValuePushers(w, s, c);
                    if (!s.IsEnum) RootUnPack(w, s, c);
                }
                scan(w.ToString());
            }
            set.Values.RemoveAll(s => !keep.Contains(s.Id));
            set.Tables.RemoveAll(s => !keep.Contains(s.Id));
            // Pack/UnPack for the packable structs kept and their struct fields.
            var packed = set.Packed.ToDictionary(s => s.Clr);
            var reach = new HashSet<string>();
            Action<string> walk = null;
            walk = clr =>
            {
                StructType p;
                if (!packed.TryGetValue(clr, out p) || !reach.Add(clr)) return;
                foreach (var f in p.Fields) walk(Names.Clr(f.Type));
            };
            foreach (var s in set.Values.Where(s => !s.IsEnum)) walk(s.Clr);
            set.Packed.RemoveAll(s => !reach.Contains(s.Clr));
            set.NotPackable.RemoveAll(clr => !set.Tables.Any(s => s.Clr == clr));
        }

        static string Offset(int offset)
        {
            return offset == 0 ? "offset" : "offset + " + offset;
        }

        // Primitive and decimal fields go through XLua.CopyByValue, struct fields
        // through the overloads of this class.
        static string PackClass(ITypeSymbol t)
        {
            return MemberContext.IsPrimitive(t) || t.SpecialType == SpecialType.System_Decimal ? "global::XLua.CopyByValue." : "";
        }

        // ---- registration and manifest ----

        static string StructRegistration(StructSet set)
        {
            var sb = new StringBuilder();
            // One call and one delegate per type (Ops above).
            foreach (var s in set.Values)
            {
                string cs = Names.Cs(s.Type);
                sb.Append("            b.Struct(typeof(").Append(cs).Append("), typeof(").Append(cs).Append("[]), Structs.Ops_").Append(s.Id).Append(");\n");
            }
            foreach (var s in set.Tables)
            {
                string cs = Names.Cs(s.Type);
                sb.Append("            b.Caster(typeof(").Append(cs).Append("), typeof(").Append(cs).Append("[]), Structs.Ops_").Append(s.Id).Append(");\n");
            }
            return sb.ToString();
        }

        // The GCOptimize part of the manifest, for Test/Native/surface_diff.py:
        // the pushed and table-read types with their layout, and the types with
        // Pack/UnPack. Generator.cs StructSurface writes the same.
        static string StructManifest(StructSet set)
        {
            var json = new Json().BeginArray();
            foreach (var s in set.Values.Concat(set.Tables).OrderBy(s => s.Clr, StringComparer.Ordinal))
            {
                bool value = s.IsEnum || s.Size != -1;
                json.BeginObject().Key("type").Value(s.Clr).Key("kind").Value(s.IsEnum ? "enum" : value ? "value" : "table")
                    .Key("pushed").Value(set.Pushed.Contains(s.Clr)).Key("flag").Value(s.IsEnum ? 0 : s.Flag).Key("size").Value(value ? s.Size : -1);
                json.Key("fields").BeginArray();
                if (!s.IsEnum)
                {
                    var fields = value ? s.Fields.Select(f => f.Name + " " + Names.Clr(f.Type) + (f.IsField ? "" : " property"))
                        : PublicFields(s.Type).Select(f => f.Name + " " + Names.Clr(f.Type));
                    foreach (var f in fields) json.Value(f);
                }
                json.EndArray();
                json.Key("groups").BeginArray();
                if (value && !s.IsEnum && s.Groups != null) foreach (var g in s.Groups) json.Value(g.Count);
                json.EndArray();
                json.EndObject();
            }
            json.EndArray();
            return json.ToString();
        }

        static string PackManifest(StructSet set)
        {
            var json = new Json().BeginArray();
            foreach (var s in set.Packed) json.Value(s.Clr + (s.IsRoot ? " root" : ""));
            return json.EndArray().ToString();
        }
    }
}
