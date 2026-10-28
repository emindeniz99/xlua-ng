// The xLua source generator (docs/design/source-generator.md).
//
// It reads the configuration attributes (section 4), reports diagnostics, and
// emits one Bindings class per assembly that registers the assembly with
// XLua.Binding and fills the name table (sections 5.1, 5.2 and 5.6, milestone
// M1), class and enum wrappers for the [LuaCallCSharp] types (Wrappers.cs, M2)
// and delegate and interface bridges for the [CSharpCallLua] types
// (Bridges.cs, M3). GCOptimize pushers are M4.
//
// Roslyn 4.3.1 APIs only: ForAttributeWithMetadataName, no
// RegisterPostInitializationOutput (the attributes live in the XLua assembly).
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace XLua.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class XLuaBindingGenerator : IIncrementalGenerator
    {
        // The XLua.Binding.BindingApi.Version this generator writes code for.
        public const int ApiVersion = 2;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<ImmutableArray<ConfigEntry>> all = Collect(context, ConfigKind.LuaCallCSharp);
            foreach (ConfigKind kind in Config.Kinds.Skip(1))
            {
                all = all.Combine(Collect(context, kind))
                    .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
            }
            IncrementalValueProvider<EquatableArray<ConfigEntry>> entries = all
                .Select(static (items, _) => new EquatableArray<ConfigEntry>(items))
                .WithTrackingName("Config");

            IncrementalValueProvider<AssemblyInfo> assembly = context.CompilationProvider
                .Select(static (c, _) => AssemblyInfo.From(c))
                .WithTrackingName("Assembly");

            // The member model re-runs on every compilation (a symbol walk over
            // the bound types); its output compares by value, so the files of
            // types whose model did not change are not written again.
            IncrementalValueProvider<WrapperSet> wrappers = context.CompilationProvider.Combine(entries)
                .Select(static (pair, ct) => Wrappers.Build(pair.Left, pair.Right, ct))
                .WithTrackingName("Wrappers");
            // One file per class wrapper and per interface bridge.
            IncrementalValuesProvider<TypeModel> types = wrappers.SelectMany(static (w, _) => w.Types.Items.AddRange(w.Interfaces.Items))
                .WithTrackingName("TypeModel");

            context.RegisterSourceOutput(types.Combine(assembly),
                static (spc, pair) => Emitter.EmitWrapper(spc, pair.Left, pair.Right));
            context.RegisterSourceOutput(assembly.Combine(entries).Combine(wrappers),
                static (spc, pair) => Emitter.Emit(spc, pair.Left.Left, pair.Left.Right, pair.Right));
        }

        static IncrementalValueProvider<ImmutableArray<ConfigEntry>> Collect(IncrementalGeneratorInitializationContext context, ConfigKind kind)
        {
            return context.SyntaxProvider
                .ForAttributeWithMetadataName(Config.AttributeMetadataName(kind),
                    static (_, _) => true,
                    (ctx, ct) => Config.Extract(kind, ctx, ct))
                .SelectMany(static (items, _) => items)
                .Collect();
        }
    }

    // What the generator needs to know about the assembly it runs in.
    internal sealed class AssemblyInfo : System.IEquatable<AssemblyInfo>
    {
        public string Name;
        public int? BindingApiVersion;   // null: XLua.Binding is not referenced
        public bool CoreMissing;         // XLua is referenced, XLua.Core (the Lua C API types) is not (XLSG012)
        public bool HasRuntimeInitialize;
        public bool HasPreserve;
        public bool HasEditorInitialize;

        public static AssemblyInfo From(Compilation c)
        {
            var info = new AssemblyInfo { Name = c.AssemblyName ?? "Unnamed" };
            var api = c.GetTypeByMetadataName("XLua.Binding.BindingApi");
            if (api != null)
            {
                var version = api.GetMembers("Version").OfType<IFieldSymbol>().FirstOrDefault();
                info.BindingApiVersion = version != null && version.HasConstantValue && version.ConstantValue is int v ? v : 0;
                info.CoreMissing = c.GetTypeByMetadataName("XLua.LuaDLL.lua_CSFunction") == null;
            }
            info.HasRuntimeInitialize = c.GetTypeByMetadataName("UnityEngine.RuntimeInitializeOnLoadMethodAttribute") != null
                && c.GetTypeByMetadataName("UnityEngine.RuntimeInitializeLoadType") != null;
            info.HasPreserve = c.GetTypeByMetadataName("UnityEngine.Scripting.PreserveAttribute") != null;
            info.HasEditorInitialize = c.GetTypeByMetadataName("UnityEditor.InitializeOnLoadMethodAttribute") != null;
            return info;
        }

        public bool Equals(AssemblyInfo o)
        {
            return o != null && Name == o.Name && BindingApiVersion == o.BindingApiVersion && CoreMissing == o.CoreMissing
                && HasRuntimeInitialize == o.HasRuntimeInitialize
                && HasPreserve == o.HasPreserve && HasEditorInitialize == o.HasEditorInitialize;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AssemblyInfo);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode() ^ (BindingApiVersion ?? -1);
        }
    }
}
