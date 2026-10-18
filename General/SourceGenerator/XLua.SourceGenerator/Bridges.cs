// Delegate and interface bridges (docs/design/source-generator.md, section 2
// rows 4 and 5, section 5.4, milestone M3): the C# of the legacy templates
// LuaDelegateBridge.tpl.txt and LuaInterfaceBridge.tpl.txt, for the
// [CSharpCallLua] delegates and interfaces of the assembly.
//
// - Delegate bridges are static methods __Gen_Delegate_Imp<k>(this
//   DelegateBridge, ...) of an internal Bridges class, one per signature
//   (SignatureKey), registered per delegate type as a delegate closed over the
//   Lua function's bridge: b.Delegate(typeof(D), x => new D(x.__Gen_Delegate_Imp<k>)).
//   Delegate.Target is therefore the DelegateBridge, as with the legacy bridges.
// - Interface bridges are internal sealed classes deriving from LuaBase that
//   implement the interface explicitly, registered with b.InterfaceBridge.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    internal enum KeyPurpose
    {
        DelegateBridge,   // the legacy delegate bridge comparer (Generator.cs MethodInfoSimulationComparer)
        Hotfix,           // the legacy hotfix reduction first (Generator.cs makeHotfixMethodInfoSimulation); used from milestone M7
    }

    // The one signature comparison of the generator (section 5.4): two methods
    // share a bridge when their keys are equal.
    //
    //     Key   := Ret "(" [Param {"," Param}] ")"
    //     Ret   := "void" | ["ref " | "ref readonly "] Type
    //     Param := Mode Type ["..."]               ; "..." marks a params array
    //     Mode  := "" | "ref " | "out " | "in "    ; "in" also covers C# 12 ref readonly
    //
    // Type is the name System.Type.ToString() gives the type at run time
    // (Names.Clr), so the legacy generator writes the same key from reflection
    // (Generator.cs bridgeKey) and Test/Native/surface_diff.py can compare the
    // bridge sets of both. Nullable annotations and tuple element names do not
    // appear in it, and dynamic is System.Object.
    internal static class SignatureKey
    {
        const int ValueTypeBoxing = 2;   // HotfixFlag.ValueTypeBoxing

        public static string Of(IMethodSymbol m)
        {
            return Of(m, KeyPurpose.DelegateBridge, 0);
        }

        // hotfixFlags: the HotfixFlag of the method's type (KeyPurpose.Hotfix only).
        public static string Of(IMethodSymbol m, KeyPurpose purpose, int hotfixFlags)
        {
            bool hotfix = purpose == KeyPurpose.Hotfix;
            bool boxing = hotfix && (hotfixFlags & ValueTypeBoxing) != 0;
            var sb = new StringBuilder();
            if (m.MethodKind == MethodKind.Constructor || m.ReturnsVoid)
            {
                sb.Append("void");
            }
            else
            {
                if (m.ReturnsByRefReadonly) sb.Append("ref readonly ");
                else if (m.ReturnsByRef) sb.Append("ref ");
                sb.Append(Names.Clr(m.ReturnType));
            }
            sb.Append('(');
            bool first = true;
            if (hotfix && !m.IsStatic)
            {
                // self comes first: the value type itself unless boxed, else object.
                sb.Append(IsValueType(m.ContainingType) && !boxing ? Names.Clr(m.ContainingType) : "System.Object");
                first = false;
            }
            foreach (var p in m.Parameters)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Mode(p));
                // The hotfix reduction keeps by-ref, params and (unless boxed)
                // value-type parameters; the others become object.
                bool keep = !hotfix || p.RefKind != RefKind.None || p.IsParams || (IsValueType(p.Type) && !boxing);
                sb.Append(keep ? Names.Clr(p.Type) : "System.Object");
                if (p.IsParams) sb.Append("...");
            }
            return sb.Append(')').ToString();
        }

        public static string Mode(IParameterSymbol p)
        {
            if (p.RefKind == RefKind.Out) return "out ";
            if (MemberContext.IsInParam(p)) return "in ";
            if (p.RefKind == RefKind.Ref) return "ref ";
            return "";
        }

        // Type.IsValueType: false for a generic parameter, whatever its constraints.
        static bool IsValueType(ITypeSymbol t)
        {
            return t.TypeKind != TypeKind.TypeParameter && t.IsValueType;
        }
    }

    internal static partial class Wrappers
    {
        // ---- [CSharpCallLua]: which types get bridges ----

        static void CallLuaBridges(WrapperSet set, Compilation compilation, EquatableArray<ConfigEntry> entries,
            Dictionary<string, ITypeSymbol> typed, MemberContext ctx, List<string> notBound, CancellationToken ct)
        {
            var delegates = new List<INamedTypeSymbol>();
            var interfaces = new List<KeyValuePair<INamedTypeSymbol, List<ISymbol>>>();
            var seen = new HashSet<string>();
            foreach (var e in entries.Where(x => x.Kind == ConfigKind.CSharpCallLua && x.Problem == TypeProblem.None && x.LuaName != null)
                .OrderBy(x => x.Path ?? "", StringComparer.Ordinal).ThenBy(x => x.Span.Start))
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(e.LuaName)) continue;
                ITypeSymbol symbol;
                if (!typed.TryGetValue(e.LuaName, out symbol)) symbol = compilation.GetTypeByMetadataName(e.LuaName);
                var named = symbol as INamedTypeSymbol;
                if (named == null) continue;
                // Generator.cs GetGenConfig: public and not obsolete.
                if (!Names.IsPublic(named) || Names.IsObsolete(named, ctx.ObsoleteErrorsOnly)) continue;
                string why = null;
                List<ISymbol> members = null;
                if (named.TypeKind == TypeKind.Delegate) why = DelegateProblem(named, ctx);
                else if (named.TypeKind == TypeKind.Interface) why = InterfaceMembers(named, ctx, out members);
                else continue; // XLSG006 reports it
                if (why != null)
                {
                    notBound.Add(e.LuaName + "|no bridge is generated: " + why);
                    continue;
                }
                if (named.TypeKind == TypeKind.Delegate) delegates.Add(named);
                else interfaces.Add(new KeyValuePair<INamedTypeSymbol, List<ISymbol>>(named, members));
            }
            DelegateBridges(set, delegates, ctx.Compilation);
            var models = interfaces.Select(kv => InterfaceModel(kv.Key, kv.Value, ctx.Compilation)).OrderBy(m => m.LuaName, StringComparer.Ordinal).ToList();
            set.Interfaces = new EquatableArray<TypeModel>(models.ToImmutableArray());
            var manifest = new Json().BeginArray();
            foreach (var m in models) manifest.Raw(m.Manifest);
            set.InterfaceManifest = manifest.EndArray().ToString();
        }

        // Why a delegate type cannot have a generated bridge, or null.
        static string DelegateProblem(INamedTypeSymbol type, MemberContext ctx)
        {
            var invoke = type.DelegateInvokeMethod;
            if (invoke == null) return "it has no Invoke method";
            return SignatureProblem(invoke, ctx);
        }

        static string SignatureProblem(IMethodSymbol m, MemberContext ctx)
        {
            // Generator.cs isRefReadOnlyUnusable(Type): Roslyn before 4.10 cannot
            // use these members at all (CS0570).
            if (ctx.RefReadOnlyUnusable(m)) return "'" + m.Name + "' has ref readonly parameters, which this C# compiler cannot use (CS0570)";
            if (m.ReturnsByRef || m.ReturnsByRefReadonly) return "'" + m.Name + "' returns by reference";
            if ((!m.ReturnsVoid && MemberContext.IsUnsupported(m.ReturnType)) || m.Parameters.Any(p => MemberContext.IsUnsupported(p.Type)))
                return "'" + m.Name + "' uses a pointer or ref struct, which cannot cross the bridge";
            string unreferenced = MemberContext.UnreferencedProblem(m);
            if (unreferenced != null) return "'" + m.Name + "': " + unreferenced;
            return null;
        }

        // The members an interface bridge implements (Generator.cs getInterfaceInfo:
        // those of the interface and of every interface it extends), or why the
        // interface cannot be bridged. The legacy generator left out generic
        // methods and wrote a bridge that did not compile; here such an
        // interface gets no bridge.
        static string InterfaceMembers(INamedTypeSymbol type, MemberContext ctx, out List<ISymbol> members)
        {
            members = new List<ISymbol>();
            foreach (var itf in new[] { type }.Concat(type.AllInterfaces))
            {
                foreach (var member in itf.GetMembers())
                {
                    if (member.IsStatic)
                    {
                        if (member.IsAbstract) return "'" + member.Name + "' is a static abstract member";
                        continue;
                    }
                    // Only members a class implements: abstract ones and default
                    // implementations that can be overridden.
                    if (member.DeclaredAccessibility != Accessibility.Public || !(member.IsAbstract || member.IsVirtual)) continue;
                    switch (member)
                    {
                        case IMethodSymbol m:
                            if (m.MethodKind != MethodKind.Ordinary) continue;
                            if (m.IsGenericMethod) return "'" + m.Name + "' is a generic method";
                            string why = SignatureProblem(m, ctx);
                            if (why != null) return why;
                            members.Add(m);
                            break;
                        case IPropertySymbol p:
                            if (p.IsIndexer && p.Parameters.Length != 1) return "an indexer with " + p.Parameters.Length + " parameters";
                            if (p.SetMethod != null && p.SetMethod.IsInitOnly) return "'" + p.Name + "' has an init-only setter";
                            if (p.ReturnsByRef || p.ReturnsByRefReadonly) return "'" + p.Name + "' returns by reference";
                            if (MemberContext.IsUnsupported(p.Type) || p.Parameters.Any(x => MemberContext.IsUnsupported(x.Type)))
                                return "'" + p.Name + "' uses a pointer or ref struct, which cannot cross the bridge";
                            if (ctx.RefReadOnlyUnusable(p.GetMethod) || ctx.RefReadOnlyUnusable(p.SetMethod))
                                return "'" + p.Name + "' has ref readonly parameters, which this C# compiler cannot use (CS0570)";
                            string unreferenced = MemberContext.UnreferencedProblem(new[] { p.Type }.Concat(p.Parameters.Select(x => x.Type)));
                            if (unreferenced != null) return "'" + p.Name + "': " + unreferenced;
                            members.Add(p);
                            break;
                        case IEventSymbol ev:
                            string evUnreferenced = MemberContext.UnreferencedProblem(new[] { ev.Type });
                            if (evUnreferenced != null) return "'" + ev.Name + "': " + evUnreferenced;
                            members.Add(ev);
                            break;
                    }
                }
            }
            return null;
        }

        // ---- delegate bridges (LuaDelegateBridge.tpl.txt) ----

        static void DelegateBridges(WrapperSet set, List<INamedTypeSymbol> delegates, Compilation c)
        {
            var groups = new SortedDictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
            foreach (var d in delegates)
            {
                string key = SignatureKey.Of(d.DelegateInvokeMethod);
                List<INamedTypeSymbol> list;
                if (!groups.TryGetValue(key, out list)) groups.Add(key, list = new List<INamedTypeSymbol>());
                list.Add(d);
            }
            var w = new Writer { Indent = 2 };
            var registration = new StringBuilder();
            var manifest = new Json().BeginArray();
            bool refReadOnly = false;
            int k = 0;
            foreach (var g in groups)
            {
                var list = g.Value.OrderBy(t => Names.Clr(t), StringComparer.Ordinal).ToList();
                string method = "__Gen_Delegate_Imp" + k++;
                DelegateBridge(w, method, list[0].DelegateInvokeMethod, c);
                manifest.BeginObject().Key("key").Value(g.Key).Key("delegates").BeginArray();
                foreach (var d in list)
                {
                    registration.Append("            b.Delegate(typeof(").Append(Names.Typeof(d)).Append("), __gen_bridge => new ")
                        .Append(Names.Cs(d)).Append("(__gen_bridge.").Append(method).Append("));\n");
                    manifest.Value(Names.Clr(d));
                    if (d.DelegateInvokeMethod.Parameters.Any(MemberContext.IsRefReadOnly)) refReadOnly = true;
                }
                manifest.EndArray().EndObject();
            }
            set.DelegateBridges = w.ToString();
            set.DelegateRegistration = registration.ToString();
            set.DelegateManifest = manifest.EndArray().ToString();
            // The bridges declare ref readonly parameters with `in`, which
            // compiles at every language version; C# 12 warns about the
            // delegate conversion (TemplateCommon RefReadOnlyPragma).
            set.DelegatePragmas = refReadOnly ? "#pragma warning disable CS9198 // `ref readonly` parameters bound to `in` bridges\n" : "";
        }

        // `in` for both `in` and `ref readonly`, as the legacy bridges declare them.
        static string BridgeModifier(IParameterSymbol p)
        {
            if (p.RefKind == RefKind.Out) return "out ";
            if (MemberContext.IsInParam(p)) return "in ";
            if (p.RefKind == RefKind.Ref) return "ref ";
            return "";
        }

        static bool IsResult(IParameterSymbol p)
        {
            return p.RefKind != RefKind.None && !MemberContext.IsInParam(p);
        }

        // TemplateCommon CallNeedTranslator(m, "") of a bridge: the return value or
        // a parameter needs the translator.
        static bool BridgeNeedsTranslator(IMethodSymbol m, Compilation c)
        {
            if (!m.ReturnsVoid && !JustLuaType(m.ReturnType, c)) return true;
            return m.Parameters.Any(p => p.IsParams || !JustLuaType(p.Type, c));
        }

        static void PushArgument(Writer w, IParameterSymbol p, string variable, bool isParams, Compilation c)
        {
            if (isParams)
            {
                var element = ((IArrayTypeSymbol)p.Type).ElementType;
                w.Line("if (" + variable + " != null) { for (int __gen_i = 0; __gen_i < " + variable + ".Length; ++__gen_i) "
                    + Push(element, variable + "[__gen_i]", c) + "; }");
            }
            else
            {
                w.Line(Push(p.Type, variable, c) + ";");
            }
        }

        static void LockOpen(Writer w, string target)
        {
            w.Line("#if THREAD_SAFE || HOTFIX_ENABLE");
            w.Line("lock (global::XLua.Binding.BindingApi.Lock(" + target + "))");
            w.Line("{");
            w.Line("#endif");
        }

        static void LockClose(Writer w)
        {
            w.Line("#if THREAD_SAFE || HOTFIX_ENABLE");
            w.Line("}");
            w.Line("#endif");
        }

        // A bridge's signature: the return type (null for void) and the
        // parameters after the DelegateBridge, as the delegate or the hotfix
        // reduction (SignatureKey, KeyPurpose.Hotfix) gives them.
        internal sealed class BridgeParam
        {
            public ITypeSymbol Type;
            public RefKind Kind;     // None, Ref, Out or In (in and ref readonly)
            public bool IsParams;
        }

        internal sealed class BridgeSignature
        {
            public ITypeSymbol Return;
            public List<BridgeParam> Parameters = new List<BridgeParam>();

            public static BridgeSignature Of(IMethodSymbol m)
            {
                var s = new BridgeSignature { Return = m.ReturnsVoid ? null : m.ReturnType };
                foreach (var p in m.Parameters)
                {
                    s.Parameters.Add(new BridgeParam { Type = p.Type, IsParams = p.IsParams,
                        Kind = p.RefKind == RefKind.Out ? RefKind.Out : MemberContext.IsInParam(p) ? RefKind.In : p.RefKind == RefKind.Ref ? RefKind.Ref : RefKind.None });
                }
                return s;
            }
        }

        static void DelegateBridge(Writer w, string name, IMethodSymbol invoke, Compilation c)
        {
            DelegateBridge(w, name, BridgeSignature.Of(invoke), c);
        }

        static void DelegateBridge(Writer w, string name, BridgeSignature sig, Compilation c)
        {
            var ps = sig.Parameters;
            bool hasReturn = sig.Return != null;
            int inNum = ps.Count(p => p.Kind != RefKind.Out);
            Func<BridgeParam, bool> isResult = p => p.Kind == RefKind.Ref || p.Kind == RefKind.Out;
            int outNum = ps.Count(isResult) + (hasReturn ? 1 : 0);
            bool hasParams = ps.Count > 0 && ps[ps.Count - 1].IsParams;
            string last = "p" + (ps.Count - 1);
            var decl = new StringBuilder("internal static ").Append(hasReturn ? Names.Cs(sig.Return) : "void").Append(' ').Append(name)
                .Append("(this global::XLua.DelegateBridge __gen_bridge");
            for (int i = 0; i < ps.Count; i++)
            {
                string mode = ps[i].Kind == RefKind.Out ? "out " : ps[i].Kind == RefKind.In ? "in " : ps[i].Kind == RefKind.Ref ? "ref " : "";
                decl.Append(", ").Append(mode).Append(Names.Cs(ps[i].Type)).Append(" p").Append(i);
            }
            w.Line();
            w.Open(decl.Append(')').ToString());
            LockOpen(w, "__gen_bridge");
            w.Line("global::System.IntPtr L = global::XLua.Binding.BindingApi.RawState(__gen_bridge);");
            // pcall_prepare pushes two values; more than BindingApi.UncheckedPushes need room first.
            if (hasParams)
            {
                w.Line("int __gen_push = " + (inNum + 1) + " + (" + last + " == null ? 0 : " + last + ".Length);");
                w.Line("if (__gen_push > global::XLua.Binding.BindingApi.UncheckedPushes) global::XLua.Binding.BindingApi.EnsureStack(L, __gen_push);");
            }
            else if (inNum + 2 > 10)
            {
                w.Line("global::XLua.Binding.BindingApi.EnsureStack(L, " + (inNum + 2) + ");");
            }
            w.Line("int errFunc = " + LuaAPI + ".pcall_prepare(L, global::XLua.Binding.BindingApi.ErrorFuncRef(__gen_bridge), global::XLua.Binding.BindingApi.Reference(__gen_bridge));");
            bool needsTranslator = (hasReturn && !JustLuaType(sig.Return, c)) || ps.Any(p => p.IsParams || !JustLuaType(p.Type, c));
            if (needsTranslator) w.Line("global::XLua.ObjectTranslator translator = global::XLua.Binding.BindingApi.Translator(__gen_bridge);");
            for (int i = 0; i < ps.Count; i++)
            {
                if (ps[i].Kind == RefKind.Out) continue;
                string variable = "p" + i;
                if (hasParams && i == ps.Count - 1)
                {
                    var element = ((IArrayTypeSymbol)ps[i].Type).ElementType;
                    w.Line("if (" + variable + " != null) { for (int __gen_i = 0; __gen_i < " + variable + ".Length; ++__gen_i) "
                        + Push(element, variable + "[__gen_i]", c) + "; }");
                }
                else
                {
                    w.Line(Push(ps[i].Type, variable, c) + ";");
                }
            }
            string nargs = hasParams ? (inNum - 1) + " + (" + last + " == null ? 0 : " + last + ".Length)" : inNum.ToString();
            w.Line("__gen_bridge.PCall(L, " + nargs + ", " + outNum + ", errFunc);");
            int outIdx = hasReturn ? 2 : 1;
            // As LuaDelegateBridge.tpl.txt: the results are read in try/finally,
            // so a conversion that throws (Lua's integer rule) restores the stack.
            if (outNum > 0) w.Open("try");
            for (int i = 0; i < ps.Count; i++)
            {
                if (!isResult(ps[i])) continue;
                w.Line(Cast(ps[i].Type, "errFunc + " + outIdx, "p" + i, false, false, c, true) + ";");
                outIdx++;
            }
            if (hasReturn) w.Line(Cast(sig.Return, "errFunc + 1", "__gen_ret", true, false, c, true) + ";");
            if (hasReturn) w.Line("return __gen_ret;");
            if (outNum > 0) w.Close().Open("finally");
            w.Line(LuaAPI + ".lua_settop(L, errFunc - 1);");
            if (outNum > 0) w.Close();
            LockClose(w);
            w.Close();
        }

        // ---- interface bridges (LuaInterfaceBridge.tpl.txt) ----

        public static string BridgeClassName(string luaName)
        {
            return "B" + ClassName(luaName).Substring(1);
        }

        static TypeModel InterfaceModel(INamedTypeSymbol type, List<ISymbol> members, Compilation c)
        {
            string luaName = Names.Clr(type);
            string className = BridgeClassName(luaName);
            var w = new Writer { Indent = 1 };
            bool refReadOnly = members.Any(m => (m is IMethodSymbol x && x.Parameters.Any(MemberContext.IsRefReadOnly))
                || (m is IPropertySymbol p && p.Parameters.Any(MemberContext.IsRefReadOnly)));
            // Explicit implementations declare ref readonly parameters with `in`
            // (TemplateCommon RefReadOnlyPragma).
            if (refReadOnly) w.Line("#pragma warning disable CS9196 // `ref readonly` parameters implemented with `in`");
            w.Line("// " + luaName);
            w.Open("internal sealed class " + className + " : global::XLua.LuaBase, " + Names.Cs(type));
            w.Open("internal static global::XLua.LuaBase __Create(int reference, global::XLua.LuaEnv luaenv)");
            w.Line("return new " + className + "(reference, luaenv);");
            w.Close();
            w.Line();
            w.Open("internal " + className + "(int reference, global::XLua.LuaEnv luaenv) : base(reference, luaenv)");
            w.Close();

            var methods = new List<string>();
            var properties = new List<string>();
            var events = new List<string>();
            var indexers = new List<string>();
            foreach (var member in members)
            {
                string owner = Names.Cs(member.ContainingType);
                switch (member)
                {
                    case IMethodSymbol m:
                        InterfaceMethod(w, owner + "." + Escape(m.Name), m.Name, m, null, c);
                        methods.Add(Signature(m, 0));
                        break;
                    case IPropertySymbol p when p.IsIndexer:
                        Indexer(w, owner, p, c);
                        indexers.Add(Names.Clr(p.ContainingType) + "::this[" + MemberContext.ParameterTypeName(p.Parameters[0]) + "]"
                            + (p.GetMethod != null ? " get" : "") + (p.SetMethod != null ? " set" : ""));
                        break;
                    case IPropertySymbol p:
                        Property(w, owner, p, c);
                        properties.Add(Names.Clr(p.ContainingType) + "::" + p.Name + (p.GetMethod != null ? " get" : "") + (p.SetMethod != null ? " set" : ""));
                        break;
                    case IEventSymbol e:
                        InterfaceEvent(w, owner, e, c);
                        events.Add(Names.Clr(e.ContainingType) + "::" + e.Name);
                        break;
                }
            }
            w.Close();

            var json = new Json().BeginObject().Key("type").Value(luaName);
            foreach (var list in new[] { new KeyValuePair<string, List<string>>("methods", methods), new KeyValuePair<string, List<string>>("properties", properties),
                new KeyValuePair<string, List<string>>("events", events), new KeyValuePair<string, List<string>>("indexers", indexers) })
            {
                json.Key(list.Key).BeginArray();
                foreach (var s in list.Value.OrderBy(x => x, StringComparer.Ordinal)) json.Value(s);
                json.EndArray();
            }
            json.EndObject();
            return new TypeModel
            {
                LuaName = luaName,
                TypeofExpr = Names.Typeof(type),
                ClassName = className,
                Source = w.ToString(),
                Manifest = json.ToString(),
                Dropped = "",
            };
        }

        // Pushes the Lua table's function `luaName` and the table as self, as the
        // legacy interface bridges do; on error the stack goes back to err_func - 1.
        static void GetFunction(Writer w, string luaName)
        {
            w.Line(LuaAPI + ".lua_getref(L, luaReference);");
            w.Line(LuaAPI + ".xlua_pushasciistring(L, " + Names.Literal(luaName) + ");");
            w.Open("if (0 != " + LuaAPI + ".xlua_pgettable(L, -2))");
            w.Line("global::XLua.Binding.BindingApi.ThrowError(this, err_func - 1);");
            w.Close();
            w.Open("if (!" + LuaAPI + ".lua_isfunction(L, -1))");
            w.Line(LuaAPI + ".xlua_pushasciistring(L, " + Names.Literal("no such function " + luaName) + ");");
            w.Line("global::XLua.Binding.BindingApi.ThrowError(this, err_func - 1);");
            w.Close();
            w.Line(LuaAPI + ".lua_pushvalue(L, -2);");
            w.Line(LuaAPI + ".lua_remove(L, -3);");
        }

        // A call of the Lua table's function `luaName` with the table as self.
        // header: the declaration, or null inside an accessor (then names
        // gives the accessor's parameter names). An interface method checks
        // the stack for its arguments; accessors push few values.
        static void InterfaceMethod(Writer w, string header, string luaName, IMethodSymbol m, string[] names, Compilation c)
        {
            var ps = m.Parameters.ToArray();
            names = names ?? ps.Select((p, i) => "p" + i).ToArray();
            bool hasReturn = !m.ReturnsVoid;
            int inNum = CountIn(ps);
            int outNum = ps.Count(IsResult) + (hasReturn ? 1 : 0);
            bool hasParams = ps.Length > 0 && ps[ps.Length - 1].IsParams;
            string last = ps.Length > 0 ? names[ps.Length - 1] : null;
            bool accessor = header == null;
            if (!accessor)
            {
                var decl = new StringBuilder(hasReturn ? Names.Cs(m.ReturnType) : "void").Append(' ').Append(header).Append('(');
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) decl.Append(", ");
                    decl.Append(BridgeModifier(ps[i])).Append(Names.Cs(ps[i].Type)).Append(' ').Append(names[i]);
                }
                w.Line();
                w.Open(decl.Append(')').ToString());
            }
            LockOpen(w, "this");
            w.Line("global::System.IntPtr L = global::XLua.Binding.BindingApi.State(this);");
            if (!accessor)
            {
                // Up to four values besides the arguments.
                if (hasParams)
                {
                    w.Line("int __gen_push = " + (inNum + 3) + " + (" + last + " == null ? 0 : " + last + ".Length);");
                    w.Line("if (__gen_push > global::XLua.Binding.BindingApi.UncheckedPushes) global::XLua.Binding.BindingApi.EnsureStack(L, __gen_push);");
                }
                else if (inNum + 4 > 10)
                {
                    w.Line("global::XLua.Binding.BindingApi.EnsureStack(L, " + (inNum + 4) + ");");
                }
            }
            w.Line("int err_func = " + LuaAPI + ".load_error_func(L, global::XLua.Binding.BindingApi.ErrorFuncRef(this));");
            if (BridgeNeedsTranslator(m, c)) w.Line("global::XLua.ObjectTranslator translator = global::XLua.Binding.BindingApi.Translator(this);");
            GetFunction(w, luaName);
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].RefKind != RefKind.Out) PushArgument(w, ps[i], names[i], hasParams && i == ps.Length - 1, c);
            }
            string nargs = hasParams ? inNum + " + (" + last + " == null ? 0 : " + last + ".Length)" : (inNum + 1).ToString();
            w.Line("int __gen_error = " + LuaAPI + ".lua_pcall(L, " + nargs + ", " + outNum + ", err_func);");
            w.Line("if (__gen_error != 0) global::XLua.Binding.BindingApi.ThrowError(this, err_func - 1);");
            int outIdx = hasReturn ? 2 : 1;
            // As LuaInterfaceBridge.tpl.txt: the results are read in try/finally.
            if (outNum > 0) w.Open("try");
            for (int i = 0; i < ps.Length; i++)
            {
                if (!IsResult(ps[i])) continue;
                w.Line(Cast(ps[i].Type, "err_func + " + outIdx, names[i], false, false, c, true) + ";");
                outIdx++;
            }
            if (hasReturn) w.Line(Cast(m.ReturnType, "err_func + 1", "__gen_ret", true, false, c, true) + ";");
            if (hasReturn) w.Line("return __gen_ret;");
            if (outNum > 0) w.Close().Open("finally");
            w.Line(LuaAPI + ".lua_settop(L, err_func - 1);");
            if (outNum > 0) w.Close();
            LockClose(w);
            if (!accessor) w.Close();
        }

        static void Property(Writer w, string owner, IPropertySymbol p, Compilation c)
        {
            w.Line();
            w.Open(Names.Cs(p.Type) + " " + owner + "." + Escape(p.Name));
            if (p.GetMethod != null)
            {
                w.Open("get");
                LockOpen(w, "this");
                w.Line("global::System.IntPtr L = global::XLua.Binding.BindingApi.State(this);");
                w.Line("int oldTop = " + LuaAPI + ".lua_gettop(L);");
                if (!JustLuaType(p.Type, c)) w.Line("global::XLua.ObjectTranslator translator = global::XLua.Binding.BindingApi.Translator(this);");
                w.Line(LuaAPI + ".lua_getref(L, luaReference);");
                w.Line(LuaAPI + ".xlua_pushasciistring(L, " + Names.Literal(p.Name) + ");");
                w.Open("if (0 != " + LuaAPI + ".xlua_pgettable(L, -2))");
                w.Line("global::XLua.Binding.BindingApi.ThrowError(this, oldTop);");
                w.Close();
                w.Open("try");
                w.Line(Cast(p.Type, "-1", "__gen_ret", true, false, c, true) + ";");
                w.Line("return __gen_ret;");
                w.Close().Open("finally");
                w.Line(LuaAPI + ".lua_settop(L, oldTop);");
                w.Close();
                LockClose(w);
                w.Close();
            }
            if (p.SetMethod != null)
            {
                w.Open("set");
                LockOpen(w, "this");
                w.Line("global::System.IntPtr L = global::XLua.Binding.BindingApi.State(this);");
                w.Line("int oldTop = " + LuaAPI + ".lua_gettop(L);");
                if (!JustLuaType(p.Type, c)) w.Line("global::XLua.ObjectTranslator translator = global::XLua.Binding.BindingApi.Translator(this);");
                w.Line(LuaAPI + ".lua_getref(L, luaReference);");
                w.Line(LuaAPI + ".xlua_pushasciistring(L, " + Names.Literal(p.Name) + ");");
                w.Line(Push(p.Type, "value", c) + ";");
                w.Open("if (0 != " + LuaAPI + ".xlua_psettable(L, -3))");
                w.Line("global::XLua.Binding.BindingApi.ThrowError(this, oldTop);");
                w.Close();
                w.Line(LuaAPI + ".lua_pop(L, 1);");
                LockClose(w);
                w.Close();
            }
            w.Close();
        }

        static void Indexer(Writer w, string owner, IPropertySymbol p, Compilation c)
        {
            var key = p.Parameters[0];
            w.Line();
            w.Open(Names.Cs(p.Type) + " " + owner + ".this[" + BridgeModifier(key) + Names.Cs(key.Type) + " p0]");
            if (p.GetMethod != null)
            {
                w.Open("get");
                InterfaceMethod(w, null, p.GetMethod.Name, p.GetMethod, new[] { "p0" }, c);
                w.Close();
            }
            if (p.SetMethod != null)
            {
                w.Open("set");
                InterfaceMethod(w, null, p.SetMethod.Name, p.SetMethod, new[] { "p0", "value" }, c);
                w.Close();
            }
            w.Close();
        }

        static void InterfaceEvent(Writer w, string owner, IEventSymbol e, Compilation c)
        {
            w.Line();
            w.Open("event " + Names.Cs(e.Type) + " " + owner + "." + Escape(e.Name));
            foreach (var accessor in new[] { "add", "remove" })
            {
                w.Open(accessor);
                LockOpen(w, "this");
                w.Line("global::System.IntPtr L = global::XLua.Binding.BindingApi.State(this);");
                w.Line("int err_func = " + LuaAPI + ".load_error_func(L, global::XLua.Binding.BindingApi.ErrorFuncRef(this));");
                w.Line("global::XLua.ObjectTranslator translator = global::XLua.Binding.BindingApi.Translator(this);");
                GetFunction(w, accessor + "_" + e.Name);
                w.Line(Push(e.Type, "value", c) + ";");
                w.Line("int __gen_error = " + LuaAPI + ".lua_pcall(L, 2, 0, err_func);");
                w.Line("if (__gen_error != 0) global::XLua.Binding.BindingApi.ThrowError(this, err_func - 1);");
                w.Line(LuaAPI + ".lua_settop(L, err_func - 1);");
                LockClose(w);
                w.Close();
            }
            w.Close();
        }
    }
}
