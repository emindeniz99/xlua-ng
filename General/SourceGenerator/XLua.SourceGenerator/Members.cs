// The member model (docs/design/source-generator.md, milestone M2): which
// members of a [LuaCallCSharp] type Lua sees, in which overload order, with
// the rules of the legacy generator (Assets/XLua/Src/Editor/Generator.cs
// getClassInfo, GetExtensionMethods, isMethodInBlackList,
// isMemberInBlackList, OverloadCosting), applied to Roslyn symbols instead of
// reflection. Wrappers.cs writes the C# from this model.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    // One overload as the wrapper calls it; the same method appears again
    // for each trailing optional parameter it may leave out (DefaultCount).
    internal sealed class Overload
    {
        public IMethodSymbol Method;
        public int DefaultCount;
        public bool IsExtension;          // called as Ext.M(self, ...)
        public IParameterSymbol[] Parameters;   // Lua-visible parameters (the `this` parameter dropped for instance extension methods)
    }

    internal sealed class MethodGroup
    {
        public string Key;       // Name, plus "_xlua_st_" for static ones (the wrapper function's name)
        public string Name;      // the Lua name
        public bool IsStatic;
        public List<Overload> Overloads = new List<Overload>();
    }

    internal sealed class Accessor
    {
        public string Name;
        public bool IsStatic;
        public bool ReadOnly;      // static readonly or const field: registered as a value
        public ITypeSymbol Type;
    }

    internal sealed class EventInfo
    {
        public string Name;
        public bool IsStatic;
        public bool CanAdd;
        public bool CanRemove;
        public ITypeSymbol Type;
    }

    internal sealed class OperatorGroup
    {
        public string Name;
        public List<IMethodSymbol> Overloads = new List<IMethodSymbol>();
    }

    internal sealed class LazyMember
    {
        public string Index;       // Utils constant: METHOD_IDX, CLS_IDX, GETTER_IDX ...
        public string Name;
        public string MemberType;  // LazyMemberTypes member
        public bool IsStatic;
        public string Reason;      // a member the generator cannot wrap (section 5.5): why; null for DoNotGen
    }

    internal sealed class ClassInfo
    {
        public INamedTypeSymbol Type;
        public List<Overload> Constructors = new List<Overload>();
        public List<MethodGroup> Methods = new List<MethodGroup>();
        public List<Accessor> Getters = new List<Accessor>();
        public List<Accessor> Setters = new List<Accessor>();
        public List<OperatorGroup> Operators = new List<OperatorGroup>();
        public List<IMethodSymbol> Indexers = new List<IMethodSymbol>();      // get accessors
        public List<IMethodSymbol> NewIndexers = new List<IMethodSymbol>();   // set accessors
        public List<EventInfo> Events = new List<EventInfo>();
        public List<LazyMember> Lazy = new List<LazyMember>();
        public List<string> Dropped = new List<string>();   // "member: reason", for XLSG004
    }

    // A [BlackList] entry: {type, member, parameter types...} with
    // Type.ToString() names (GenAttributes.cs BlackListRules).
    internal sealed class BlackListRule
    {
        public string Type;
        public string Member;
        public string[] Parameters;   // null: no parameter list given
    }

    // What the member model needs besides the type itself.
    internal sealed class MemberContext
    {
        public Compilation Compilation;
        public bool ObsoleteErrorsOnly;
        // Roslyn before 4.10 cannot call members whose `ref readonly`
        // parameter carries modreq(InAttribute) (CS0570), and calls indexers
        // with a `ref readonly` key through their accessors (Generator.cs
        // compilerRejectsRefReadOnlyModreq, isRefReadOnlyIndexerAccessor).
        public bool OldCompiler;
        public List<BlackListRule> BlackList = new List<BlackListRule>();
        public Dictionary<string, HashSet<string>> DoNotGen = new Dictionary<string, HashSet<string>>();
        public HashSet<string> Wrapped = new HashSet<string>();                    // Lua names of the wrapped types
        public List<INamedTypeSymbol> ExtensionClasses = new List<INamedTypeSymbol>();
        public INamedTypeSymbol StringType;
        public string Fingerprint = "";   // the configuration and compiler facts above, for the model cache
        // GEN_CODE_MINIMIZE wrappers (Wrappers.GcmParameters): XLua was compiled
        // with the define, so its Utils registers CSharpWrapper delegates.
        public bool Gcm;

        public static readonly string[] OperatorNames =
        {
            "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Equality", "op_UnaryNegation", "op_LessThan",
            "op_LessThanOrEqual", "op_Modulus", "op_BitwiseAnd", "op_BitwiseOr", "op_ExclusiveOr", "op_OnesComplement",
            "op_LeftShift", "op_RightShift",
        };

        public bool IsDoNotGen(ITypeSymbol type, string name)
        {
            HashSet<string> names;
            return DoNotGen.TryGetValue(Names.Clr(type), out names) && names.Contains(name);
        }

        public bool IsObsolete(ISymbol symbol)
        {
            return Names.IsObsolete(symbol, ObsoleteErrorsOnly);
        }

        // ---- Generator.cs isMemberInBlackList / isMethodInBlackList ----

        static bool HasBlackListAttribute(ISymbol symbol)
        {
            return symbol != null && symbol.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == "XLua.BlackListAttribute");
        }

        // Pointers and ref structs cannot cross the bridge (Generator.cs isUnsupportedType).
        public static bool IsUnsupported(ITypeSymbol type)
        {
            return type is IPointerTypeSymbol || type.TypeKind == TypeKind.FunctionPointer || type.IsRefLikeType;
        }

        bool MatchesMember(BlackListRule rule, ISymbol member)
        {
            return member.ContainingType != null && ReflectionName(member) == rule.Member && Names.Clr(member.ContainingType) == rule.Type;
        }

        // MemberInfo.Name: an indexer is "Item" (or its [IndexerName]), which
        // Roslyn calls "this[]".
        public static string ReflectionName(ISymbol member)
        {
            return member is IPropertySymbol p && p.IsIndexer ? p.MetadataName : member.Name;
        }

        bool MatchesMethod(BlackListRule rule, IMethodSymbol method)
        {
            if (!MatchesMember(rule, method)) return false;
            var expected = rule.Parameters ?? new string[0];
            if (method.Parameters.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (ParameterTypeName(method.Parameters[i]) != expected[i]) return false;
            }
            return true;
        }

        // ParameterInfo.ParameterType.ToString(): by-ref types end in '&'.
        public static string ParameterTypeName(IParameterSymbol p)
        {
            return Names.Clr(p.Type) + (p.RefKind != RefKind.None ? "&" : "");
        }

        public bool IsMemberBlackListed(ISymbol member)
        {
            if (HasBlackListAttribute(member)) return true;
            if (member is IFieldSymbol f && IsUnsupported(f.Type)) return true;
            if (member is IPropertySymbol p)
            {
                if (IsUnsupported(p.Type)) return true;
                if (RefReadOnlyUnusable(p.GetMethod) | RefReadOnlyUnusable(p.SetMethod)) return true;
            }
            if (member is IMethodSymbol m && RefReadOnlyUnusable(m)) return true;
            return BlackList.Any(rule => MatchesMember(rule, member));
        }

        public bool IsMethodBlackListed(IMethodSymbol method)
        {
            return RefReadOnlyUnusable(method) || IsMethodExcluded(method);
        }

        // An accessor of an indexer, which also goes when [BlackList] or
        // [Obsolete] leaves out its indexer; accessors match [BlackList] by
        // name, as property accessors do (BlackListRules.IsListed), in every
        // binding mode.
        bool IsIndexerAccessorExcluded(IPropertySymbol indexer, IMethodSymbol accessor)
        {
            return IsObsolete(indexer) || IsObsolete(accessor) || IsMemberBlackListed(indexer) || IsMemberBlackListed(accessor)
                || indexer.Parameters.Any(p => IsUnsupported(p.Type));
        }

        // IsMethodBlackListed without the compiler limit: what reflection cannot
        // call either, or what the configuration excludes.
        public bool IsMethodExcluded(IMethodSymbol method)
        {
            if (HasBlackListAttribute(method)) return true;
            if (method.Parameters.Any(p => IsUnsupported(p.Type))) return true;
            if (method.MethodKind != MethodKind.Constructor && IsUnsupported(method.ReturnType)) return true;
            return BlackList.Any(rule => MatchesMethod(rule, method));
        }

        // ---- ref readonly and in parameters (XLua.Utils.IsInParameter) ----

        // C# 12 `ref readonly`: RefKind.RefReadOnlyParameter (4) in Roslyn 4.8
        // and later, a `ref` with [RequiresLocation] as older Roslyn reads it
        // from metadata.
        public static bool IsRefReadOnly(IParameterSymbol p)
        {
            if ((int)p.RefKind == 4) return true;
            return p.RefKind == RefKind.Ref && p.GetAttributes().Any(a => a.AttributeClass != null
                && a.AttributeClass.ToDisplayString() == "System.Runtime.CompilerServices.RequiresLocationAttribute");
        }

        // `in` or `ref readonly`: an input only, never copied back.
        public static bool IsInParam(IParameterSymbol p)
        {
            return p.RefKind == RefKind.In || IsRefReadOnly(p);
        }

        internal bool RefReadOnlyUnusable(IMethodSymbol method)
        {
            if (!OldCompiler || method == null) return false;
            return method.Parameters.Any(p => IsRefReadOnly(p)
                && p.RefCustomModifiers.Any(m => !m.IsOptional && m.Modifier.ToDisplayString() == "System.Runtime.InteropServices.InAttribute"));
        }

        // ---- generic methods (Generator.cs isSupportedGenericMethod) ----

        static bool HasGenericParameter(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array) return HasGenericParameter(array.ElementType);
            if (type is INamedTypeSymbol named && named.IsGenericType) return named.TypeArguments.Any(HasGenericParameter);
            return type is ITypeParameterSymbol;
        }

        // Type.GetGenericParameterConstraints(): the type constraints, with
        // System.ValueType for `struct`.
        static List<ITypeSymbol> Constraints(ITypeParameterSymbol p, Compilation c)
        {
            var list = new List<ITypeSymbol>(p.ConstraintTypes);
            if (p.HasValueTypeConstraint) list.Add(c.GetSpecialType(SpecialType.System_ValueType));
            return list;
        }

        static bool IsClass(ITypeSymbol t)
        {
            return t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Delegate;
        }

        bool SupportGenericParameter(ITypeSymbol type, RefKind refKind, bool checkConstraint, ref bool hasGenericParameter)
        {
            if (refKind != RefKind.None) return true; // T& is neither a generic parameter nor a generic type
            if (type is ITypeParameterSymbol p)
            {
                if (!checkConstraint) return false;
                var constraints = Constraints(p, Compilation);
                if (constraints.Count == 0) return false;
                foreach (var c in constraints)
                {
                    if (!IsClass(c) || c.SpecialType == SpecialType.System_ValueType || HasGenericParameter(c)) return false;
                }
                hasGenericParameter = true;
            }
            else if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                foreach (var argument in named.TypeArguments)
                {
                    if (!SupportGenericParameter(argument, RefKind.None, false, ref hasGenericParameter)) return false;
                }
            }
            return true;
        }

        // Every type parameter occurs in a parameter type, so a call without
        // type arguments compiles.
        public static bool TypeParametersInferable(IMethodSymbol method)
        {
            return method.TypeParameters.All(t => method.Parameters.Any(p => Mentions(p.Type, t)));
        }

        static bool Mentions(ITypeSymbol type, ITypeParameterSymbol parameter)
        {
            if (type is IArrayTypeSymbol array) return Mentions(array.ElementType, parameter);
            if (type is INamedTypeSymbol named && named.IsGenericType) return named.TypeArguments.Any(a => Mentions(a, parameter));
            return SymbolEqualityComparer.Default.Equals(type, parameter);
        }

        public bool IsSupportedGenericMethod(IMethodSymbol method)
        {
            if (!method.IsGenericMethod) return true;
            bool has = false;
            foreach (var p in method.Parameters)
            {
                if (!SupportGenericParameter(p.Type, p.RefKind, true, ref has)) return false;
            }
            return has;
        }

        bool IsAssignable(ITypeSymbol from, ITypeSymbol to)
        {
            if (HasGenericParameter(to)) return false;
            return IsAssignable(Compilation, from, to, true);
        }

        // Type.IsAssignableFrom: identity, an implicit reference conversion or, with boxing, a boxing one.
        public static bool IsAssignable(Compilation compilation, ITypeSymbol from, ITypeSymbol to, bool boxing)
        {
            var conversion = ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)compilation).ClassifyConversion(from, to);
            return conversion.IsIdentity || (conversion.IsImplicit && (conversion.IsReference || (boxing && conversion.IsBoxing)));
        }

        // Generator.cs isSupportedExtensionMethod.
        bool IsSupportedExtensionMethod(IMethodSymbol method, ITypeSymbol extended)
        {
            if (!method.IsExtensionMethod || method.Parameters.Length < 1) return false;
            bool has = false;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var p = method.Parameters[i];
                var tp = p.RefKind == RefKind.None ? p.Type as ITypeParameterSymbol : null;
                if (i == 0)
                {
                    if (tp != null)
                    {
                        var constraints = Constraints(tp, Compilation);
                        if (constraints.Count == 0) return false;
                        if (!constraints.Any(c => c.SpecialType != SpecialType.System_ValueType && IsAssignable(extended, c))) return false;
                        has = true;
                    }
                    else if (p.RefKind != RefKind.None || !SymbolEqualityComparer.Default.Equals(p.Type, extended))
                    {
                        return false;
                    }
                }
                // Generator.cs: the other parameters as IsSupportedGenericMethod
                // takes them, so no generic type over a type parameter
                // (Action<T>, Tencent/xLua#924).
                else if (!SupportGenericParameter(p.Type, p.RefKind, true, ref has))
                {
                    return false;
                }
            }
            return has || !method.IsGenericMethod;
        }

        // Public static methods of a type, as Type.GetMethods(Static | Public).
        public static IEnumerable<IMethodSymbol> PublicStaticMethods(INamedTypeSymbol type)
        {
            return type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public
                && m.MethodKind != MethodKind.StaticConstructor);
        }

        // Generator.cs GetExtensionMethods: the extension methods of `extended`
        // in the [LuaCallCSharp] static classes.
        public List<IMethodSymbol> ExtensionMethodsOf(ITypeSymbol extended)
        {
            var result = new List<IMethodSymbol>();
            foreach (var cls in ExtensionClasses)
            {
                if (!(cls.IsSealed || cls.IsStatic) || cls.IsGenericType || cls.ContainingType != null) continue;
                foreach (var m in PublicStaticMethods(cls))
                {
                    if (IsSupportedExtensionMethod(m, extended)) result.Add(m);
                }
            }
            return result;
        }

        // The overload with each count of trailing default values left out
        // (Generator.cs getClassInfo): 0, then 1, 2... while the parameters
        // from the end are optional (a params array counts when the parameter
        // before it is optional).
        static IEnumerable<int> DefaultCounts(IMethodSymbol m)
        {
            yield return 0;
            var ps = m.Parameters;
            int count = 0;
            for (int i = ps.Length - 1; i >= 0; i--)
            {
                if (ps[i].IsOptional || (ps[i].IsParams && i > 0 && ps[i - 1].IsOptional))
                {
                    count++;
                    yield return count;
                }
                else
                {
                    break;
                }
            }
        }

        // Generator.cs OverloadCosting: non-primitive input parameters first
        // (by-ref types are never primitive), then the parameter count.
        static int Costing(IMethodSymbol m)
        {
            int costing = m.IsStatic ? 0 : 1;
            foreach (var p in m.Parameters)
            {
                bool primitive = p.RefKind == RefKind.None && IsPrimitive(p.Type);
                bool isOut = p.RefKind == RefKind.Out;
                if (!primitive && !isOut) costing++;
            }
            return costing * 10000 + m.Parameters.Length + (m.IsStatic ? 0 : 1);
        }

        // Type.IsPrimitive.
        public static bool IsPrimitive(ITypeSymbol t)
        {
            switch (t.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Char:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsSpecialName(IMethodSymbol m)
        {
            switch (m.MethodKind)
            {
                case MethodKind.Ordinary:
                    return false;
                default:
                    return true;
            }
        }

        // Type.IsAssignableFrom(typeof(string)).
        bool AcceptsString(ITypeSymbol t)
        {
            return IsAssignable(StringType, t);
        }

        // Public methods the type declares (Type.GetMethods(Public | Instance | Static | DeclaredOnly)).
        static IEnumerable<IMethodSymbol> DeclaredMethods(INamedTypeSymbol type)
        {
            return type.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Public
                && m.MethodKind != MethodKind.Constructor && m.MethodKind != MethodKind.StaticConstructor
                && m.MethodKind != MethodKind.Destructor && !(m.IsStatic && m.IsAbstract));
        }

        static bool IsInterface(ITypeSymbol t)
        {
            return t.TypeKind == TypeKind.Interface;
        }

        // Generator.cs getClassInfo.
        public ClassInfo Build(INamedTypeSymbol type)
        {
            var info = new ClassInfo { Type = type };

            if (!type.IsAbstract && !type.IsStatic)
            {
                foreach (var ctor in type.InstanceConstructors)
                {
                    if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
                    if (ctor.IsImplicitlyDeclared && type.IsValueType) continue; // reflection does not list a struct's default constructor
                    if (IsMethodBlackListed(ctor) || IsObsolete(ctor)) continue;
                    foreach (int d in DefaultCounts(ctor))
                    {
                        info.Constructors.Add(new Overload { Method = ctor, DefaultCount = d, Parameters = ctor.Parameters.ToArray() });
                    }
                }
            }

            var extensionMethods = IsInterface(type) ? new List<IMethodSymbol>() : ExtensionMethodsOf(type);
            var extensionSet = new HashSet<IMethodSymbol>(extensionMethods, SymbolEqualityComparer.Default);

            var candidates = DeclaredMethods(type)
                .Where(m => !m.IsExtensionMethod || IsInterface(m.Parameters[0].Type))
                .Where(m => !IsSpecialName(m)
                    || (((m.Name == "get_Item" && m.Parameters.Length == 1) || (m.Name == "set_Item" && m.Parameters.Length == 2))
                        && AcceptsString(m.Parameters[0].Type)))
                .Where(m => IsSpecialName(m) || m.CanBeReferencedByName)
                .Concat(extensionMethods)
                .ToList();
            var fallbackMethods = new List<IMethodSymbol>();
            foreach (var m in candidates)
            {
                if (IsDoNotGen(type, m.Name)) continue;
                if (m.AssociatedSymbol is IPropertySymbol indexer && indexer.IsIndexer)
                {
                    // get_Item and set_Item of a string key are methods for Lua.
                    if (IsIndexerAccessorExcluded(indexer, m) || RefReadOnlyUnusable(m)) continue;
                }
                else
                if (IsMethodBlackListed(m) || IsObsolete(m))
                {
                    // Only the compiler cannot call it: reflection can (section 5.5).
                    if (!IsObsolete(m) && !IsMethodExcluded(m) && !extensionSet.Contains(m) && m.MethodKind == MethodKind.Ordinary
                        && (!m.IsGenericMethod || IsSupportedGenericMethod(m)))
                        fallbackMethods.Add(m);
                    continue;
                }
                if (m.IsGenericMethod && !extensionSet.Contains(m) && !IsSupportedGenericMethod(m)) continue;
                if (m.IsGenericMethod && !TypeParametersInferable(m))
                {
                    // The wrapper calls it without type arguments; the legacy
                    // generator writes the same call, which does not compile.
                    info.Dropped.Add(m.Name + ": a type parameter that no parameter uses, so C# cannot infer it (CS0411, which the legacy generator's call also gives)");
                    continue;
                }
                bool isStatic = m.IsStatic && (!m.IsExtensionMethod || IsInterface(m.Parameters[0].Type));
                string key = m.Name + (isStatic ? "_xlua_st_" : "");
                var group = info.Methods.FirstOrDefault(g => g.Key == key);
                if (group == null)
                {
                    group = new MethodGroup { Key = key, Name = m.Name, IsStatic = isStatic };
                    info.Methods.Add(group);
                }
                group.Overloads.Add(new Overload { Method = m });
            }
            foreach (var group in info.Methods)
            {
                // A stable sort, as Enumerable.OrderBy.
                var ordered = group.Overloads.Select((o, i) => new { o, i }).OrderBy(x => Costing(x.o.Method)).ThenBy(x => x.i).Select(x => x.o.Method).ToList();
                group.Overloads.Clear();
                foreach (var m in ordered)
                {
                    bool asExtension = m.IsExtensionMethod && !IsInterface(m.Parameters[0].Type);
                    var visible = asExtension ? m.Parameters.Skip(1).ToArray() : m.Parameters.ToArray();
                    foreach (int d in DefaultCounts(m))
                    {
                        group.Overloads.Add(new Overload { Method = m, DefaultCount = d, IsExtension = asExtension, Parameters = visible });
                    }
                }
            }

            var properties = type.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public || IsPublic(p.GetMethod) || IsPublic(p.SetMethod)).ToList();
            var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.DeclaredAccessibility == Accessibility.Public && f.CanBeReferencedByName).ToList();

            foreach (var p in properties)
            {
                if (p.Parameters.Length != 0 || !IsPublic(p.GetMethod) || p.Name == "Item" || (p.IsStatic && p.IsAbstract)) continue;
                if (IsObsolete(p) || IsObsolete(p.GetMethod) || IsMemberBlackListed(p) || IsMemberBlackListed(p.GetMethod)) continue;
                if (IsDoNotGen(type, p.Name)) continue;
                info.Getters.Add(new Accessor { Name = p.Name, IsStatic = p.GetMethod.IsStatic, Type = p.Type });
            }
            foreach (var f in fields)
            {
                if (IsObsolete(f) || IsMemberBlackListed(f) || IsDoNotGen(type, f.Name)) continue;
                info.Getters.Add(new Accessor { Name = f.Name, IsStatic = f.IsStatic, ReadOnly = f.IsReadOnly || f.IsConst, Type = f.Type });
            }
            foreach (var p in properties)
            {
                if (p.Parameters.Length != 0 || !IsPublic(p.SetMethod) || p.Name == "Item" || (p.IsStatic && p.IsAbstract)) continue;
                if (IsObsolete(p) || IsObsolete(p.SetMethod) || IsMemberBlackListed(p) || IsMemberBlackListed(p.SetMethod)) continue;
                if (IsDoNotGen(type, p.Name)) continue;
                if (p.SetMethod.IsInitOnly)
                {
                    // C# calls an init accessor only in an object initializer;
                    // reflection calls it like any setter.
                    bool s = p.SetMethod.IsStatic;
                    info.Lazy.Add(new LazyMember { Index = s ? "CLS_SETTER_IDX" : "SETTER_IDX", Name = p.Name, MemberType = "PropertySet", IsStatic = s,
                        Reason = "its setter is init-only, which C# calls only in an object initializer" });
                    continue;
                }
                info.Setters.Add(new Accessor { Name = p.Name, IsStatic = p.SetMethod.IsStatic, Type = p.Type });
            }
            foreach (var f in fields)
            {
                if (IsObsolete(f) || IsMemberBlackListed(f) || f.IsReadOnly || f.IsConst || IsDoNotGen(type, f.Name)) continue;
                info.Setters.Add(new Accessor { Name = f.Name, IsStatic = f.IsStatic, Type = f.Type });
            }

            foreach (var m in DeclaredMethods(type))
            {
                if (Array.IndexOf(OperatorNames, m.Name) < 0) continue;
                // As the emitted and reflection wrappers: [BlackList] (also an
                // entry with the parameter types) and [Obsolete] leave it out.
                if (IsMethodBlackListed(m) || IsObsolete(m)) continue;
                var group = info.Operators.FirstOrDefault(g => g.Name == m.Name);
                if (group == null)
                {
                    group = new OperatorGroup { Name = m.Name };
                    info.Operators.Add(group);
                }
                group.Overloads.Add(m);
            }
            foreach (var group in info.Operators)
            {
                var ordered = group.Overloads.Select((m, i) => new { m, i }).OrderBy(x => x.m.Parameters.Length).ThenBy(x => x.i).Select(x => x.m).ToList();
                group.Overloads = ordered;
            }

            var indexers = type.GetMembers().OfType<IPropertySymbol>().Where(p => p.IsIndexer && !p.IsStatic
                && (IsPublic(p.GetMethod) || IsPublic(p.SetMethod))).ToList();
            foreach (var p in indexers)
            {
                var get = p.GetMethod;
                if (IsPublic(get) && get.Parameters.Length == 1 && !AcceptsString(get.Parameters[0].Type) && !RefReadOnlyUnusable(get)
                    && !IsIndexerAccessorExcluded(p, get)) info.Indexers.Add(get);
            }
            foreach (var p in indexers)
            {
                var set = p.SetMethod;
                if (IsPublic(set) && set.Parameters.Length == 2 && !AcceptsString(set.Parameters[0].Type) && !RefReadOnlyUnusable(set)
                    && !IsIndexerAccessorExcluded(p, set)) info.NewIndexers.Add(set);
            }

            foreach (var e in type.GetMembers().OfType<IEventSymbol>())
            {
                bool add = IsPublic(e.AddMethod), remove = IsPublic(e.RemoveMethod);
                if (!add && !remove) continue;
                if (IsObsolete(e) || IsMemberBlackListed(e) || IsDoNotGen(type, e.Name)) continue;
                bool isStatic = add ? e.AddMethod.IsStatic : e.RemoveMethod.IsStatic;
                // As Generator.cs: CanAdd follows the remove accessor.
                info.Events.Add(new EventInfo { Name = e.Name, IsStatic = isStatic, CanAdd = remove, CanRemove = remove, Type = e.Type });
            }

            // A method name none of whose overloads could be wrapped goes to
            // reflection as a whole; overloads next to wrapped ones stay out.
            foreach (var m in fallbackMethods)
            {
                bool isStatic = m.IsStatic;
                string key = m.Name + (isStatic ? "_xlua_st_" : "");
                if (info.Methods.Any(g => g.Key == key))
                {
                    info.Dropped.Add(m.Name + ": an overload with ref readonly parameters, which this C# compiler cannot call (CS0570), next to overloads that are wrapped");
                    continue;
                }
                string index = isStatic ? "CLS_IDX" : "METHOD_IDX";
                if (info.Lazy.Any(l => l.Name == m.Name && l.Index == index)) continue;
                info.Lazy.Add(new LazyMember { Index = index, Name = m.Name, MemberType = "Method", IsStatic = isStatic,
                    Reason = "it has ref readonly parameters, which this C# compiler cannot call (CS0570)" });
            }

            BuildLazy(info, type, extensionMethods);
            return info;
        }

        static bool IsPublic(IMethodSymbol m)
        {
            return m != null && m.DeclaredAccessibility == Accessibility.Public;
        }

        // Members named by DoNotGen: registered lazily, wrapped by reflection on
        // first use. What [BlackList] or [Obsolete] leaves out stays out, as in
        // the emitted wrappers (Utils.LazyReflectionCall leaves out such
        // overloads of a method that is registered).
        void BuildLazy(ClassInfo info, INamedTypeSymbol type, List<IMethodSymbol> extensionMethods)
        {
            var seen = new HashSet<string>();
            foreach (var member in type.GetMembers())
            {
                if (!IsDoNotGen(type, member.Name)) continue;
                if (!IsReflectionPublicMember(member)) continue;
                if (IsObsolete(member)) continue;
                if (member is IMethodSymbol excluded && (IsMethodExcluded(excluded)
                    || (excluded.AssociatedSymbol is IPropertySymbol indexer && indexer.IsIndexer && IsIndexerAccessorExcluded(indexer, excluded)))) continue;
                if (!(member is IMethodSymbol) && IsMemberBlackListed(member)) continue;
                if (!seen.Add(member.Name)) continue; // GroupBy(Name).First()
                switch (member)
                {
                    case IMethodSymbol m:
                        info.Lazy.Add(new LazyMember { Index = m.IsStatic ? "CLS_IDX" : "METHOD_IDX", Name = m.Name, MemberType = "Method", IsStatic = m.IsStatic });
                        break;
                    case IEventSymbol e:
                        var accessor = IsPublic(e.AddMethod) ? e.AddMethod : IsPublic(e.RemoveMethod) ? e.RemoveMethod : null;
                        if (accessor == null) break;
                        info.Lazy.Add(new LazyMember { Index = accessor.IsStatic ? "CLS_IDX" : "METHOD_IDX", Name = e.Name, MemberType = "Event", IsStatic = accessor.IsStatic });
                        break;
                    case IFieldSymbol f:
                        info.Lazy.Add(new LazyMember { Index = f.IsStatic ? "CLS_GETTER_IDX" : "GETTER_IDX", Name = f.Name, MemberType = "FieldGet", IsStatic = f.IsStatic });
                        info.Lazy.Add(new LazyMember { Index = f.IsStatic ? "CLS_SETTER_IDX" : "SETTER_IDX", Name = f.Name, MemberType = "FieldSet", IsStatic = f.IsStatic });
                        break;
                    case IPropertySymbol p:
                        if (p.Name != "Item" || p.Parameters.Length == 0)
                        {
                            if (IsPublic(p.GetMethod) && !IsObsolete(p.GetMethod) && !IsMemberBlackListed(p.GetMethod))
                            {
                                bool s = p.GetMethod.IsStatic;
                                info.Lazy.Add(new LazyMember { Index = s ? "CLS_GETTER_IDX" : "GETTER_IDX", Name = p.Name, MemberType = "PropertyGet", IsStatic = s });
                            }
                            if (IsPublic(p.SetMethod) && !IsObsolete(p.SetMethod) && !IsMemberBlackListed(p.SetMethod))
                            {
                                bool s = p.SetMethod.IsStatic;
                                info.Lazy.Add(new LazyMember { Index = s ? "CLS_SETTER_IDX" : "SETTER_IDX", Name = p.Name, MemberType = "PropertySet", IsStatic = s });
                            }
                        }
                        break;
                }
            }
            // Extension methods named in DoNotGen: the lazy instance member
            // finds them in the extension method map.
            foreach (var name in extensionMethods.Where(m => IsDoNotGen(type, m.Name) && !IsMethodBlackListed(m) && !IsObsolete(m))
                .Select(m => m.Name).Distinct())
            {
                if (info.Lazy.Any(l => l.Name == name && l.Index == "METHOD_IDX")) continue;
                info.Lazy.Add(new LazyMember { Index = "METHOD_IDX", Name = name, MemberType = "Method", IsStatic = false });
            }
        }

        // What Type.GetMembers(Public | Instance | Static | DeclaredOnly) lists.
        static bool IsReflectionPublicMember(ISymbol member)
        {
            switch (member)
            {
                case IMethodSymbol m:
                    return m.DeclaredAccessibility == Accessibility.Public && m.MethodKind != MethodKind.StaticConstructor;
                case IPropertySymbol p:
                    return IsPublic(p.GetMethod) || IsPublic(p.SetMethod);
                case IEventSymbol e:
                    return IsPublic(e.AddMethod) || IsPublic(e.RemoveMethod);
                case INamedTypeSymbol t:
                    return t.DeclaredAccessibility == Accessibility.Public;
                default:
                    return member.DeclaredAccessibility == Accessibility.Public;
            }
        }
    }
}
