// Unit tests of the xLua source generator (docs/design/source-generator.md,
// section 8, "Generator unit tests"). Usage: XLua.SourceGenerator.Tests <repo>
//
// The generator runs in CSharpGeneratorDriver 4.3.1, the Roslyn it is built
// against, over test sources that reference the real xLua runtime: the
// XLua.Mini sources (General/vs2013/XLua.Mini.csproj) compiled in memory with
// XLUA_GENERAL. The name table test then loads the output and the runtime into
// this process and calls the generated Register (managed code only; no Lua).
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XLua.SourceGenerator;

static class Program
{
    static string repo;
    static int failures;

    static int Main(string[] args)
    {
        repo = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        if (args.Length > 1 && args[1] == "--timing") return Timing.Run(args.Skip(2).Select(int.Parse).ToArray(), false);
        if (args.Length > 1 && args[1] == "--timing-gcm") return Timing.Run(args.Skip(2).Select(int.Parse).ToArray(), true);
        var tests = typeof(Tests).GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.MetadataToken).ToArray();
        foreach (var test in tests)
        {
            try
            {
                test.Invoke(null, null);
                Console.WriteLine("ok   " + test.Name);
            }
            catch (TargetInvocationException e)
            {
                failures++;
                Console.WriteLine("FAIL " + test.Name + ": " + e.InnerException.Message);
            }
        }
        Console.WriteLine(failures == 0 ? "XLUA_SOURCEGEN_TESTS_PASS: " + tests.Length + " tests"
            : "XLUA_SOURCEGEN_TESTS_FAIL: " + failures + " of " + tests.Length + " failed");
        return failures == 0 ? 0 : 1;
    }

    internal static string Repo { get { return repo; } }
}

// What one run of the generator produced.
sealed class Run
{
    public CSharpCompilation Input;
    public Compilation Output;
    public GeneratorDriver Driver;
    public GeneratorRunResult Result;
    public ImmutableArray<Diagnostic> OutputDiagnostics;

    public string Source(string hint)
    {
        var s = Result.GeneratedSources.FirstOrDefault(g => g.HintName == hint);
        return s.SourceText == null ? null : s.SourceText.ToString();
    }

    public IEnumerable<string> Ids(DiagnosticSeverity severity)
    {
        return Result.Diagnostics.Where(d => d.Severity == severity).Select(d => d.Id);
    }
}

static class Harness
{
    public static readonly CSharpParseOptions Parse = new CSharpParseOptions(LanguageVersion.CSharp9);

    static ImmutableArray<MetadataReference> bcl;
    public static ImmutableArray<MetadataReference> Bcl
    {
        get
        {
            if (bcl.IsDefault)
            {
                var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
                bcl = tpa.Where(p => { var n = Path.GetFileName(p); return n.StartsWith("System.") || n.StartsWith("Microsoft.Win32.") || n == "mscorlib.dll" || n == "netstandard.dll"; })
                    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray();
            }
            return bcl;
        }
    }

    static byte[] runtimeImage;
    static MetadataReference runtime;

    // The xLua runtime, compiled from the same sources as XLua.Mini.dll.
    public static MetadataReference Runtime
    {
        get
        {
            if (runtime == null)
            {
                var project = Path.Combine(Program.Repo, "General/vs2013/XLua.Mini.csproj");
                XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
                var files = XDocument.Load(project).Descendants(ns + "Compile")
                    .Select(c => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), c.Attribute("Include").Value.Replace('\\', '/'))));
                var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "XLUA_GENERAL", "NET5_0_OR_GREATER" });
                var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f));
                var c = CSharpCompilation.Create("XLua.Mini", trees, Bcl,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
                runtimeImage = Emit(c);
                runtime = MetadataReference.CreateFromImage(runtimeImage);
            }
            return runtime;
        }
    }

    static MetadataReference runtimeGcm;

    // The same runtime compiled with GEN_CODE_MINIMIZE, as a project with the
    // define compiles it: Utils registers CSharpWrapper delegates.
    public static MetadataReference RuntimeGcm
    {
        get
        {
            if (runtimeGcm == null)
            {
                var project = Path.Combine(Program.Repo, "General/vs2013/XLua.Mini.csproj");
                XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
                var files = XDocument.Load(project).Descendants(ns + "Compile")
                    .Select(c => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), c.Attribute("Include").Value.Replace('\\', '/'))));
                var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "XLUA_GENERAL", "NET5_0_OR_GREATER", "GEN_CODE_MINIMIZE" });
                var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f));
                var c = CSharpCompilation.Create("XLua.Mini", trees, Bcl,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
                runtimeGcm = MetadataReference.CreateFromImage(Emit(c));
            }
            return runtimeGcm;
        }
    }

    // A compilation with GEN_CODE_MINIMIZE, against RuntimeGcm.
    public static readonly CSharpParseOptions GcmParse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "GEN_CODE_MINIMIZE" });

    static readonly Dictionary<string, MetadataReference> runtimes = new Dictionary<string, MetadataReference>();

    // The runtime compiled with the given project defines, such as
    // XLUA_PRESET_MINIMAL, which the <xlua-preset> blocks of its files expand.
    public static MetadataReference RuntimeWith(params string[] defines)
    {
        var key = string.Join(";", defines);
        MetadataReference found;
        if (runtimes.TryGetValue(key, out found)) return found;
        var project = Path.Combine(Program.Repo, "General/vs2013/XLua.Mini.csproj");
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var files = XDocument.Load(project).Descendants(ns + "Compile")
            .Select(c => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), c.Attribute("Include").Value.Replace('\\', '/'))));
        var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "XLUA_GENERAL", "NET5_0_OR_GREATER" }.Concat(defines));
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f));
        var c = CSharpCompilation.Create("XLua.Mini", trees, Bcl,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        found = MetadataReference.CreateFromImage(Emit(c));
        runtimes.Add(key, found);
        return found;
    }

    // A compilation with the given project defines.
    public static CSharpParseOptions ParseWith(params string[] defines)
    {
        return new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: defines);
    }

    public static byte[] RuntimeImage
    {
        get { var unused = Runtime; return runtimeImage; }
    }

    public static byte[] Emit(Compilation c)
    {
        using (var ms = new MemoryStream())
        {
            var result = c.Emit(ms);
            if (!result.Success)
            {
                throw new Exception("compilation of " + c.AssemblyName + " failed:\n" + string.Join("\n",
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));
            }
            return ms.ToArray();
        }
    }

    public static CSharpCompilation Compile(string assemblyName, IEnumerable<string> sources, IEnumerable<MetadataReference> extra = null,
        CSharpParseOptions parse = null)
    {
        parse = parse ?? Parse;
        int i = 0;
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parse, "/src/File" + (i++) + ".cs"));
        return CSharpCompilation.Create(assemblyName, trees, Bcl.AddRange(extra ?? new[] { Runtime }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    public static GeneratorDriver Driver(CSharpParseOptions parse = null)
    {
        return CSharpGeneratorDriver.Create(new[] { new XLuaBindingGenerator().AsSourceGenerator() },
            parseOptions: parse ?? Parse,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    public static Run Generate(CSharpCompilation input, GeneratorDriver driver = null)
    {
        driver = (driver ?? Driver((CSharpParseOptions)input.SyntaxTrees.First().Options)).RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        var run = new Run { Input = input, Output = output, Driver = driver, Result = driver.GetRunResult().Results.Single() };
        if (run.Result.Exception != null) throw new Exception("the generator threw: " + run.Result.Exception);
        run.OutputDiagnostics = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return run;
    }

    public static Run Generate(string assemblyName, params string[] sources)
    {
        return Generate(Compile(assemblyName, sources));
    }
}

static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(what + ": expected <" + expected + ">, got <" + actual + ">");
    }

    public static void Diagnostics(Run run, params string[] expected)
    {
        var actual = run.Result.Diagnostics.Select(d => d.Id + ":" + d.Severity).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var want = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        True(actual.SequenceEqual(want), "diagnostics: expected [" + string.Join(", ", want) + "], got ["
            + string.Join(", ", run.Result.Diagnostics.Select(d => d.Id + ":" + d.Severity + " " + d.GetMessage())) + "]");
    }

    public static void Compiles(Run run)
    {
        True(run.OutputDiagnostics.Length == 0, "the generated code does not compile:\n" + string.Join("\n", run.OutputDiagnostics.Take(10))
            + "\n" + string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString())));
    }
}

static class Tests
{
    const string Usings = "using System; using System.Collections.Generic; using XLua;\n";

    const string NameTableSource = Usings + @"
[assembly: LuaCallCSharp(typeof(List<int>))]
[assembly: LuaCallCSharp(typeof(Dictionary<string, List<int>>))]
[assembly: LuaCallCSharp(typeof(Game.Outer.Inner))]
[assembly: LuaCallCSharp(typeof(Game.Outer<int>.Inner))]
[assembly: LuaCallCSharp(typeof(Game.Outer<int>.Deep<string>))]
[assembly: LuaCallCSharp(typeof(int[]))]
[assembly: LuaCallCSharp(typeof(System.Text.StringBuilder))]
[assembly: CSharpCallLua(typeof(Action<float>))]
[assembly: CSharpCallLua(typeof(Func<int, (int, string)>))]
[assembly: GCOptimize(typeof(Game.Pair), OptimizeFlag.PackAsTable)]
[assembly: ReflectionUse(typeof(Game.Settings))]
[assembly: Hotfix(typeof(Game.Pool<>))]
[assembly: Hotfix(typeof(Game.Shop), HotfixFlag.IntKey)]
[assembly: LuaCallCSharp(typeof(Game.Player))]
namespace Game
{
    public class Outer { public class Inner { } }
    public class Outer<T> { public class Inner { } public class Deep<U> { } }
    public struct Pair { public int a, b; }
    public class Settings { }
    public class Pool<T> { }
    public class Shop { }
    [LuaCallCSharp] public class Player { public void Run() { } }
    [CSharpCallLua] public delegate int Score(Player p);
    [CSharpCallLua] public interface IScript { void Run(); }
    [LuaCallCSharp] public enum Mode { A }
    [GCOptimize] public struct HitInfo { public float x, y; }
    [Hotfix] public class Inventory { }
    class Hidden { [LuaCallCSharp] internal class InternalNested { } }
}
[LuaCallCSharp] public class GlobalType { }
";

    // The 21 types above (Player twice), by the name Type.ToString() gives them.
    static readonly string[] NameTableExpected =
    {
        "GlobalType",
        "Game.Hidden+InternalNested",
        "Game.HitInfo",
        "Game.IScript",
        "Game.Inventory",
        "Game.Mode",
        "Game.Outer+Inner",
        "Game.Outer`1+Deep`1[System.Int32,System.String]",
        "Game.Outer`1+Inner[System.Int32]",
        "Game.Pair",
        "Game.Player",
        "Game.Pool`1[T]",
        "Game.Score",
        "Game.Settings",
        "Game.Shop",
        "System.Action`1[System.Single]",
        "System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.Int32]]",
        "System.Collections.Generic.List`1[System.Int32]",
        "System.Func`2[System.Int32,System.ValueTuple`2[System.Int32,System.String]]",
        "System.Int32[]",
        "System.Text.StringBuilder",
    };

    // The registration runs, and every name equals Type.ToString() of its type
    // at run time (the string the legacy generator records, section 5.6).
    public static void NameTableMatchesTypeToString()
    {
        var run = Harness.Generate("Game", NameTableSource);
        Assert.Diagnostics(run, "XLSG004:Info"); // int[]: array types are not wrapped yet
        Assert.Compiles(run);
        var source = run.Source(Emitter.BindingsFile);
        Assert.True(source != null, "no " + Emitter.BindingsFile);

        var context = new AssemblyLoadContext("nametable", isCollectible: true);
        try
        {
            var xlua = context.LoadFromStream(new MemoryStream(Harness.RuntimeImage));
            context.Resolving += (c, name) => name.Name == "XLua.Mini" ? xlua : null;
            var game = context.LoadFromStream(new MemoryStream(Harness.Emit(run.Output)));
            var bindings = game.GetType(Names.GeneratedNamespace("Game") + ".Bindings", true);
            var attribute = game.GetCustomAttributesData().Single(a => a.AttributeType.Name == "GeneratedBindingsAttribute");
            Assert.Equal(bindings, (Type)attribute.ConstructorArguments[0].Value, "GeneratedBindings names the Bindings class");
            Assert.True(!bindings.IsPublic && bindings.IsAbstract && bindings.IsSealed, "Bindings is internal static");
            bindings.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

            var registry = xlua.GetType("XLua.Binding.BindingRegistry", true);
            var names = (System.Collections.IDictionary)registry.GetField("names", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var registered = (ICollection<string>)registry.GetField("registered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.True(registered.Contains("Game"), "BindingApi.Register did not record the unit 'Game'");
            var actual = new List<string>();
            foreach (System.Collections.DictionaryEntry e in names)
            {
                var type = (Type)e.Key;
                Assert.Equal(type.ToString(), (string)e.Value, "name of " + type);
                actual.Add((string)e.Value);
            }
            actual.Sort(StringComparer.Ordinal);
            var expected = NameTableExpected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.True(actual.SequenceEqual(expected), "name table: expected\n  " + string.Join("\n  ", expected) + "\ngot\n  " + string.Join("\n  ", actual));
            var flag = xlua.GetType("XLua.DelegateBridge", true).GetField("Gen_Flag").GetValue(null);
            Assert.Equal(true, (bool)flag, "a unit with delegate bridges sets DelegateBridge.Gen_Flag");
        }
        finally
        {
            context.Unload();
        }
    }

    public static void OutputIsDeterministic()
    {
        var a = Harness.Generate("Game", NameTableSource).Source(Emitter.BindingsFile);
        var b = Harness.Generate("Game", NameTableSource).Source(Emitter.BindingsFile);
        Assert.Equal(a, b, "two runs");
        // The same configuration split over two files, in the other order.
        int cut = NameTableSource.IndexOf("namespace Game", StringComparison.Ordinal);
        var head = NameTableSource.Substring(0, cut);
        var tail = Usings + NameTableSource.Substring(cut);
        var c = Harness.Generate("Game", tail, head).Source(Emitter.BindingsFile);
        Assert.Equal(a, c, "the same configuration in other files");
    }

    public static void GeneratedNamespaceKeepsSimilarNamesApart()
    {
        string a = Names.GeneratedNamespace("My.Game"), b = Names.GeneratedNamespace("My_Game");
        Assert.True(a != b, "My.Game and My_Game share " + a);
        Assert.True(a.StartsWith("XLua.Generated.My_Game_") && a.Length == "XLua.Generated.My_Game_".Length + 8, a);
        Assert.Equal("XLua.Generated.Assembly_CSharp_", Names.GeneratedNamespace("Assembly-CSharp").Substring(0, 31), "Assembly-CSharp");
        Assert.True(Names.GeneratedNamespace("1st").StartsWith("XLua.Generated._1st_"), "a name that starts with a digit");
        Assert.True(SyntaxFacts.IsValidIdentifier(Names.GeneratedNamespace("Ünï.cödé").Substring("XLua.Generated.".Length)), "non-ASCII names");
    }

    // Unity's attributes are emitted when the compilation has them.
    public static void UnityRegistrationAttributes()
    {
        const string unity = @"
namespace UnityEngine { public enum RuntimeInitializeLoadType { SubsystemRegistration = 4 }
    public class RuntimeInitializeOnLoadMethodAttribute : System.Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } } }
namespace UnityEngine.Scripting { public class PreserveAttribute : System.Attribute { } }
namespace UnityEditor { public class InitializeOnLoadMethodAttribute : System.Attribute { } }";
        var stubs = Harness.Compile("UnityStubs", new[] { unity }, new MetadataReference[0]);
        var stubRef = MetadataReference.CreateFromImage(Harness.Emit(stubs));
        foreach (var editor in new[] { false, true })
        {
            var parse = Harness.Parse.WithPreprocessorSymbols(editor ? new[] { "UNITY_EDITOR" } : new string[0]);
            var input = Harness.Compile("Game", new[] { Usings + "[LuaCallCSharp] public class Player { }" }, new[] { Harness.Runtime, stubRef }, parse);
            var run = Harness.Generate(input);
            Assert.Diagnostics(run);
            Assert.Compiles(run);
            var source = run.Source(Emitter.BindingsFile);
            Assert.True(source.Contains("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]"), "RuntimeInitializeOnLoadMethod");
            Assert.True(source.Contains("[global::UnityEngine.Scripting.Preserve]"), "Preserve");
            Assert.True(source.Contains("#if UNITY_EDITOR") && source.Contains("[global::UnityEditor.InitializeOnLoadMethod]"), "InitializeOnLoadMethod");
        }
        var plain = Harness.Generate("Game", Usings + "[LuaCallCSharp] public class Player { }").Source(Emitter.BindingsFile);
        Assert.True(!plain.Contains("UnityEngine") && !plain.Contains("UnityEditor"), "no Unity attributes without Unity");
    }

    public static void NothingWithoutXLua()
    {
        const string local = "namespace XLua { public class LuaCallCSharpAttribute : System.Attribute { } }\n[XLua.LuaCallCSharp] public class Player { }";
        var run = Harness.Generate(Harness.Compile("Unrelated", new[] { local }, new MetadataReference[0]));
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources without XLua.Binding");
        Assert.Diagnostics(run);
    }

    public static void NothingWithoutConfiguration()
    {
        var run = Harness.Generate("Game", Usings + "public class Player { [BlackList] public void Hidden() { } }");
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources without configured types");
        Assert.Diagnostics(run);
    }

    // XLSG001: generic type definitions are skipped; Hotfix takes definitions, not constructed types.
    public static void Xlsg001OpenGenericTypes()
    {
        var run = Harness.Generate("Game", Usings + @"
[assembly: LuaCallCSharp(typeof(List<>))]
[assembly: Hotfix(typeof(Pool<int>))]
[LuaCallCSharp] public class G<T> { }
public class Pool<T> { }
[LuaCallCSharp] public class Ok { }");
        Assert.Diagnostics(run, "XLSG001:Warning", "XLSG001:Warning", "XLSG001:Warning");
        Assert.Compiles(run);
        Assert.True(!run.Source(Emitter.BindingsFile).Contains("List"), "List<> is not named");
    }

    public static void Xlsg002InaccessibleType()
    {
        var run = Harness.Generate("Game", Usings + "public class Outer { [LuaCallCSharp] private class Secret { } [LuaCallCSharp] protected internal class Visible { } }");
        Assert.Diagnostics(run, "XLSG002:Error");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("Outer.Secret"), run.Result.Diagnostics[0].GetMessage());
        Assert.True(run.Result.Diagnostics[0].Location.GetLineSpan().StartLinePosition.Line == 1, "the location is the attribute");
    }

    public static void Xlsg003NoSuchMember()
    {
        var run = Harness.Generate("Game", Usings + @"
[assembly: BlackList(typeof(Player), ""Run"")]
[assembly: BlackList(typeof(Player), ""Jump"", typeof(int), typeof(string))]
[assembly: BlackList(typeof(Player), "".ctor"", typeof(int))]
[assembly: BlackList(typeof(Player), ""speed"")]
[assembly: BlackList(typeof(Player), ""Nope"")]
[assembly: BlackList(typeof(Player), ""Run"", typeof(int))]
[assembly: BlackList(""Player"", ""Run"")]
[assembly: BlackList(""Player"", ""Jump"", ""System.Int32"", ""System.String"")]
[assembly: BlackList(""UnityEngine.MonoBehaviour"", ""runInEditMode"")]
[assembly: DoNotGen(typeof(Player), ""Run"", ""Missing"")]
[assembly: DoNotGen(typeof(Player), ""Ext"")]
[assembly: AdditionalProperties(typeof(Player), ""speed"", ""Run"")]
[LuaCallCSharp] public class Player { public float speed; public Player(int x) { } public void Run() { } public void Jump(int a, string b) { } }
public static class PlayerExtensions { public static void Ext(this Player p) { } }");
        // Nope; Run(int); DoNotGen Missing; AdditionalProperties Run (a method); the by-name MonoBehaviour (info).
        // DoNotGen Ext names an extension method, which the legacy generator binds lazily: no warning.
        Assert.Diagnostics(run, "XLSG003:Warning", "XLSG003:Warning", "XLSG003:Warning", "XLSG003:Warning", "XLSG003:Info");
        Assert.Compiles(run);
    }

    public static void Xlsg005ConflictingGCOptimize()
    {
        var run = Harness.Generate("Game", Usings + @"
[assembly: GCOptimize(typeof(Pair))]
[assembly: GCOptimize(typeof(Pair), OptimizeFlag.PackAsTable)]
[assembly: GCOptimize(typeof(Other))]
[GCOptimize] public struct Other { public int a; }
public struct Pair { public int a, b; }");
        Assert.Diagnostics(run, "XLSG005:Error");
    }

    public static void Xlsg006NotDelegateOrInterface()
    {
        var run = Harness.Generate("Game", Usings + "[assembly: CSharpCallLua(typeof(Player))]\n[CSharpCallLua] public class Player { }\n[CSharpCallLua] public delegate void D();");
        Assert.Diagnostics(run, "XLSG006:Warning", "XLSG006:Warning");
        Assert.True(run.Source(Emitter.BindingsFile).Contains("typeof(global::D)"), "the delegate is still named");
    }

    // XLSG007: a generator built for another binding API version writes nothing.
    public static void Xlsg007ApiVersion()
    {
        const string stub = @"
namespace XLua { public class LuaCallCSharpAttribute : System.Attribute { } }
namespace XLua.Binding { public static class BindingApi { public const int Version = 1; } }";
        var xlua = Harness.Compile("XLua", new[] { stub }, new MetadataReference[0]);
        var input = Harness.Compile("Game", new[] { "[XLua.LuaCallCSharp] public class Player { }" },
            new[] { MetadataReference.CreateFromImage(Harness.Emit(xlua)) });
        var run = Harness.Generate(input);
        Assert.Diagnostics(run, "XLSG007:Error");
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("version 1") && run.Result.Diagnostics[0].GetMessage().Contains("version 2"),
            run.Result.Diagnostics[0].GetMessage());
    }

    // XLSG012: an asmdef that references XLua but not XLua.Core (where the Lua C
    // API types are since core LuaScript M6) gets one error instead of a CS0234
    // for every generated wrapper method, and no sources.
    public static void Xlsg012CoreNotReferenced()
    {
        const string stub = @"
namespace XLua { public class LuaCallCSharpAttribute : System.Attribute { } }
namespace XLua.Binding { public static class BindingApi { public const int Version = 2; } }";
        var xlua = Harness.Compile("XLua", new[] { stub }, new MetadataReference[0]);
        var input = Harness.Compile("Game", new[] { "[XLua.LuaCallCSharp] public class Player { public int Hp; }" },
            new[] { MetadataReference.CreateFromImage(Harness.Emit(xlua)) });
        var run = Harness.Generate(input);
        Assert.Diagnostics(run, "XLSG012:Error");
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources");
        var message = run.Result.Diagnostics[0].GetMessage();
        Assert.True(message.Contains("'Game'") && message.Contains("add \"XLua.Core\" to the \"references\" of the asmdef 'Game'")
            && message.Contains("\"references\": [\"XLua\", \"XLua.Core\"]"), message);
    }

    // The model cache keeps the models of metadata types across compilations
    // (Unity's compiler server). A type's model depends on which of the
    // assemblies its own assembly references the compilation has: without
    // Dep, Lib.Shop.Buy's parameter Dep.Coin is an error type, not a struct.
    // Two asmdefs that reference Lib, one also Dep, must not share a model
    // (2026-10-18: the key was the assembly's MVID and the configuration only).
    public static void ModelCacheKeysTheMissingReferences()
    {
        var dep = Harness.Compile("Dep", new[] { "namespace Dep { public struct Coin { public int Value; } }" });
        var depRef = MetadataReference.CreateFromImage(Harness.Emit(dep));
        var lib = Harness.Compile("Lib", new[] { "namespace Lib { public class Shop { public void Buy(Dep.Coin coin) { } } }" },
            new[] { Harness.Runtime, depRef });
        var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib));
        const string game = Usings + "[assembly: LuaCallCSharp(typeof(Lib.Shop))]";
        Func<string> withDep = () => Harness.Generate(Harness.Compile("Game", new[] { game }, new[] { Harness.Runtime, libRef, depRef }))
            .Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Lib_Shop_")).SourceText.ToString();
        Wrappers.ClearCache();
        string fresh = withDep();
        Assert.True(!fresh.Contains("translator.GetObject(L, 2"), "Dep.Coin is read as a struct, not with GetObject:\n" + fresh);
        Wrappers.ClearCache();
        Harness.Generate(Harness.Compile("Game", new[] { game }, new[] { Harness.Runtime, libRef })); // without Dep first
        Assert.Equal(fresh, withDep(), "the wrapper of Lib.Shop after a compilation without Dep");
    }

    public static void Xlsg009HotfixOfAnotherAssembly()
    {
        var run = Harness.Generate("Game", Usings + "[assembly: Hotfix(typeof(System.Text.StringBuilder))]\n[assembly: Hotfix(typeof(Shop))]\npublic class Shop { }");
        Assert.Diagnostics(run, "XLSG009:Warning");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("System.Runtime") || run.Result.Diagnostics[0].GetMessage().Contains("System.Text"),
            run.Result.Diagnostics[0].GetMessage());
    }

    public static void Xlsg011ConfigurationLists()
    {
        var run = Harness.Generate("Game", Usings + @"
public static class Config
{
    [LuaCallCSharp] public static List<Type> LuaCallCSharp = new List<Type> { typeof(int) };
    [CSharpCallLua] static IEnumerable<Type> Bridges { get { yield return typeof(Action); } }
    [BlackList] public static List<List<string>> BlackList = new List<List<string>>();
    [DoNotGen] public static Dictionary<Type, List<string>> DoNotGen = new Dictionary<Type, List<string>>();
}");
        Assert.Diagnostics(run, "XLSG011:Info", "XLSG011:Info", "XLSG011:Info", "XLSG011:Info");
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources");
    }

    // Section 8: a whitespace edit and an edit to an unrelated file emit
    // nothing new; an edit to the configuration re-emits the file.
    public static void IncrementalEditsAreCached()
    {
        var input = Harness.Compile("Game", new[] { NameTableSource, "public class Unrelated { }" });
        var first = Harness.Generate(input);

        var unrelated = input.SyntaxTrees.ElementAt(1);
        var edited = input.ReplaceSyntaxTree(unrelated, unrelated.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From("public class Unrelated { public int x; }")))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Added { }", Harness.Parse, "/src/Added.cs"));
        var second = Harness.Generate(edited, first.Driver);
        AssertOutputReasons(second, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged);

        var config = edited.SyntaxTrees.First();
        var withConfig = edited.ReplaceSyntaxTree(config, config.WithChangedText(config.GetText().WithChanges(
            new Microsoft.CodeAnalysis.Text.TextChange(new Microsoft.CodeAnalysis.Text.TextSpan(Usings.Length, 0), "[assembly: LuaCallCSharp(typeof(Version))]\n"))));
        var third = Harness.Generate(withConfig, second.Driver);
        // The registration changes, a new wrapper is added; the other wrappers stay cached.
        AssertOutputReasons(third, IncrementalStepRunReason.Modified, IncrementalStepRunReason.New, IncrementalStepRunReason.Cached);
        var reasons = third.Result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs).Select(o => o.Reason).ToArray();
        Assert.True(reasons.Count(r => r != IncrementalStepRunReason.Cached) >= 2,
            "the registration is written again and the new wrapper added: " + string.Join(", ", reasons));
        Assert.True(third.Source(Emitter.BindingsFile).Contains("typeof(global::System.Version)"), "the new type is emitted");
    }

    static void AssertOutputReasons(Run run, params IncrementalStepRunReason[] allowed)
    {
        var steps = run.Result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs).ToArray();
        Assert.True(steps.Length > 0, "no tracked output steps");
        foreach (var output in steps)
        {
            Assert.True(allowed.Contains(output.Reason), "output step reason " + output.Reason + ", expected " + string.Join(" or ", allowed));
        }
    }

    // Milestone M2: one fixture with every member kind the class wrappers
    // handle. The manifest is the member model the wrappers are written from;
    // the expected lists follow Generator.cs getClassInfo.
    const string WrapperSource = Usings + @"
[assembly: BlackList(typeof(Game.Player), ""Secret"")]
[assembly: BlackList(typeof(Game.Player), ""Foo"", typeof(long))]
[assembly: DoNotGen(typeof(Game.Player), ""Lazy"", ""Later"")]
[assembly: ReflectionUse(typeof(Game.OtherExt))]
[assembly: LuaCallCSharp(typeof(Game.Box<int>))]
namespace Game
{
    public class Base { }
    public class Derived : Base { }
    [LuaCallCSharp]
    public class Player
    {
        public Player(int a, string b = ""x"") { }
        public Player() { }
        [Obsolete] public Player(double d) { }
        public void Foo(int a) { }
        public void Foo(string a) { }
        public void Foo(ref int a) { }
        public void Foo(object a, int b = 1) { }
        public void Foo(long a) { }
        public static int Foo(int a, int b) { return a + b; }
        public int Out(out int a, ref string b, in int c) { a = 1; return c; }
        public int Sum(params int[] values) { return values.Length; }
        public string Pick<T>(T x) where T : Base { return x.ToString(); }
        public string Any<T>(T x) { return x.ToString(); }
        [BlackList] public void Hidden() { }
        public void Secret() { }
        public void Lazy() { }
        public int Later { get; set; }
        public int Count { get; private set; }
        public int Init { get; init; }
        public static readonly int Max = 3;
        public const int Const = 4;
        public static int Shared;
        public int this[int i] { get { return i; } set { } }
        public int this[string s] { get { return 0; } set { } }
        public event Action Changed;
        public static event Action Global;
        public static Player operator +(Player a, Player b) { return a; }
        public static Player operator -(Player a) { return a; }
        public int @class;
    }
    [LuaCallCSharp]
    public static class PlayerExt
    {
        public static int Ext(this Player p, int x) { return x; }
        public static int Lazy(this Player p, int x) { return x; }
        public static int Down<T>(this T p) where T : Base { return 0; }
    }
    public static class OtherExt
    {
        public static int Twice(this Base b, int x) { return 2 * x; }
    }
    [LuaCallCSharp] public static class Holder { public static int Make() { return 1; } }
    public class Box<T> { public Box(T v) { } public void Put(T v) { } }
    [LuaCallCSharp]
    public struct Pt
    {
        public int X;
        public Pt(int x) { X = x; }
        public int Len() { return X; }
    }
    [LuaCallCSharp] public enum Small { A, [Obsolete] B, C }
    [LuaCallCSharp] public enum Big { V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20 }
    [LuaCallCSharp] public interface IShape { int Area(); }
    [LuaCallCSharp] internal class NotPublic { }
}
[LuaCallCSharp] public static class ExtHost { public static int Ext2(this Game.Player p) { return 0; } }
[LuaCallCSharp(typeof(Game.Derived))] public static class Unused { }
";

    static System.Text.Json.JsonElement ManifestType(Run run, string name)
    {
        var source = run.Source(Emitter.ManifestFile);
        Assert.True(source != null, "no " + Emitter.ManifestFile);
        int start = source.IndexOf("@\"", StringComparison.Ordinal) + 2, end = source.IndexOf("\";", start, StringComparison.Ordinal);
        var json = System.Text.Json.JsonDocument.Parse(source.Substring(start, end - start).Replace("\"\"", "\"")).RootElement;
        if (name == null) return json;
        foreach (var t in json.GetProperty("types").EnumerateArray())
        {
            if (t.GetProperty("type").GetString() == name) return t;
        }
        throw new Exception(name + " is not in the manifest");
    }

    static string[] Strings(System.Text.Json.JsonElement e)
    {
        return e.EnumerateArray().Select(x => x.GetString()).ToArray();
    }

    static void Same(string[] expected, string[] actual, string what)
    {
        Assert.True(expected.SequenceEqual(actual), what + ": expected\n  " + string.Join("\n  ", expected) + "\ngot\n  " + string.Join("\n  ", actual));
    }

    static string[] Overloads(System.Text.Json.JsonElement type, string name, bool isStatic)
    {
        foreach (var m in type.GetProperty("methods").EnumerateArray())
        {
            if (m.GetProperty("name").GetString() == name && m.GetProperty("static").GetBoolean() == isStatic) return Strings(m.GetProperty("overloads"));
        }
        return new string[0];
    }

    public static void WrappersFollowTheLegacyMemberModel()
    {
        var run = Harness.Generate("Game", WrapperSource);
        Assert.Diagnostics(run, "XLSG004:Info");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("Init"), run.Result.Diagnostics[0].GetMessage());
        Assert.Compiles(run);

        var player = ManifestType(run, "Game.Player");
        Same(new[] { "Game.Player::.ctor(System.Int32,System.String)", "Game.Player::.ctor(System.Int32,System.String) -1", "Game.Player::.ctor()" },
            Strings(player.GetProperty("constructors")), "constructors (obsolete one left out)");
        // OverloadCosting: non-primitive inputs first (by-ref counts), then the
        // parameter count; ties keep declaration order. Foo(long) is blacklisted.
        Same(new[] { "Game.Player::Foo(System.Int32)", "Game.Player::Foo(System.String)", "Game.Player::Foo(System.Int32&)",
            "Game.Player::Foo(System.Object,System.Int32)", "Game.Player::Foo(System.Object,System.Int32) -1" },
            Overloads(player, "Foo", false), "Foo");
        Same(new[] { "Game.Player::Foo(System.Int32,System.Int32)" }, Overloads(player, "Foo", true), "static Foo (_xlua_st_)");
        Same(new[] { "Game.Player::Pick`1(T)" }, Overloads(player, "Pick", false), "constrained generic method");
        Assert.True(Overloads(player, "Any", false).Length == 0, "unconstrained generic method left out");
        Assert.True(Overloads(player, "Hidden", false).Length == 0 && Overloads(player, "Secret", false).Length == 0, "BlackList");
        Same(new[] { "Game.PlayerExt::Ext(Game.Player,System.Int32)" }, Overloads(player, "Ext", false), "extension method");
        Assert.True(Overloads(player, "Ext2", false).Length == 1, "extension method of a [LuaCallCSharp] class outside a namespace");
        Same(new[] { "Game.Player::get_Item(System.String)" }, Overloads(player, "get_Item", false), "string-keyed get_Item is a method");
        Same(new[] { "Game.Player::get_Item(System.Int32)" }, Strings(player.GetProperty("indexers")), "indexers");
        Same(new[] { "Game.Player::set_Item(System.Int32,System.Int32)" }, Strings(player.GetProperty("newindexers")), "newindexers");
        Same(new[] { "Const static readonly", "Count", "Init", "Max static readonly", "Shared static", "class" },
            Strings(player.GetProperty("getters")).OrderBy(x => x, StringComparer.Ordinal).ToArray(), "getters");
        Same(new[] { "Shared static", "class" }, Strings(player.GetProperty("setters")).OrderBy(x => x, StringComparer.Ordinal).ToArray(), "setters");
        Same(new[] { "Changed add remove", "Global static add remove" }, Strings(player.GetProperty("events")), "events");
        Same(new[] { "GETTER_IDX Later PropertyGet", "SETTER_IDX Later PropertySet", "METHOD_IDX Lazy Method" },
            Strings(player.GetProperty("lazy")), "DoNotGen members, including the extension method of the same name once");
        Assert.True(player.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("name").GetString())
            .SequenceEqual(new[] { "op_Addition", "op_UnaryNegation" }), "operators");

        var pt = ManifestType(run, "Game.Pt");
        Same(new[] { "Game.Pt::.ctor(System.Int32)" }, Strings(pt.GetProperty("constructors")), "a struct's implicit constructor is not listed");
        var small = ManifestType(run, "Game.Small");
        Same(new[] { "A", "C" }, Strings(small.GetProperty("fields")), "obsolete enum value left out");
        Assert.Equal(3, small.GetProperty("fieldCount").GetInt32(), "field count");
        Assert.Equal(true, ManifestType(run, "Game.Big").GetProperty("registerEnumType").GetBoolean(), "more than 20 values");
        ManifestType(run, "Game.IShape");
        Same(new[] { "Game.PlayerExt::Down`1(T)" }, Overloads(ManifestType(run, "Game.Derived"), "Down", false), "a generic extension method for a type its constraint accepts");
        ManifestType(run, "Game.Holder");
        // A closed generic type lists its members as wrapped, as the legacy dump
        // does (Generator.cs surfaceSignature), not those of the definition.
        var box = ManifestType(run, "Game.Box`1[System.Int32]");
        Same(new[] { "Game.Box`1[System.Int32]::.ctor(System.Int32)" }, Strings(box.GetProperty("constructors")), "closed generic constructors");
        Same(new[] { "Game.Box`1[System.Int32]::Put(System.Int32)" }, Overloads(box, "Put", false), "closed generic methods");
        var types = ManifestType(run, null).GetProperty("types").EnumerateArray().Select(t => t.GetProperty("type").GetString()).ToArray();
        Assert.True(!types.Contains("Game.NotPublic"), "non-public types are not wrapped, as with the legacy generator");
        // The map: extension methods of ReflectionUse classes, and of
        // [LuaCallCSharp] classes for types without a wrapper.
        Same(new[] { "Game.Base <- Game.OtherExt::Twice(Game.Base,System.Int32)", "Game.Base <- Game.PlayerExt::Down`1(T)",
            "Game.Player <- Game.PlayerExt::Lazy(Game.Player,System.Int32)" },
            Strings(ManifestType(run, null).GetProperty("extensionMethods")), "extension method map");

        var bindings = run.Source(Emitter.BindingsFile);
        Assert.True(bindings.Contains("b.Type(typeof(global::Game.Player), \"Game.Player\", W_Game_Player_"), "Player registers its wrapper");
        Assert.True(bindings.Contains("b.ExtensionMethods(typeof(global::Game.Base)"), "the extension method map");
        foreach (var s in run.Result.GeneratedSources.Where(s => s.HintName.StartsWith("W_")))
        {
            Assert.True(s.SourceText.ToString().Contains("internal static class " + s.HintName.Substring(0, s.HintName.Length - 5)), s.HintName);
        }

        // The registration loads and registers a loader per wrapped type.
        var context = new AssemblyLoadContext("wrappers", isCollectible: true);
        try
        {
            var xlua = context.LoadFromStream(new MemoryStream(Harness.RuntimeImage));
            context.Resolving += (c, name) => name.Name == "XLua.Mini" ? xlua : null;
            var game = context.LoadFromStream(new MemoryStream(Harness.Emit(run.Output)));
            game.GetType(Names.GeneratedNamespace("Game") + ".Bindings", true).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var registry = xlua.GetType("XLua.Binding.BindingRegistry", true);
            var loaders = (System.Collections.IDictionary)registry.GetField("loaders", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.Equal(types.Length, loaders.Count, "loaders");
            Assert.True(loaders.Contains(game.GetType("Game.Player", true)), "a loader for Player");
        }
        finally
        {
            context.Unload();
        }
    }

    // [BlackList] and [Obsolete] leave out operators, indexers and lazy
    // ([DoNotGen]) members as they leave out the other members, and as the
    // emitted and reflection wrappers leave them out (2026-10-28; before, the
    // operators and indexers were wrapped and the lazy members registered).
    public static void BlackListLeavesOutOperatorsIndexersAndLazyMembers()
    {
        var run = Harness.Generate("Game", Usings + @"
[assembly: BlackList(typeof(Game.Ops), ""op_Multiply"", typeof(Game.Ops), typeof(int))]
[assembly: BlackList(typeof(Game.ByName), ""Item"")]
[assembly: BlackList(typeof(Game.ByAccessorName), ""get_Item"")]
[assembly: DoNotGen(typeof(Game.Lazy), ""Both"", ""Mixed"", ""Field"", ""Prop"", ""Getter"", ""Ev"", ""Old"", ""Kept"")]
namespace Game
{
    [LuaCallCSharp]
    public class Ops
    {
        public static Ops operator +(Ops a, Ops b) { return a; }
        [BlackList] public static Ops operator -(Ops a, Ops b) { return a; }
        public static Ops operator *(Ops a, Ops b) { return a; }
        public static Ops operator *(Ops a, int b) { return a; }
        [Obsolete] public static Ops operator /(Ops a, Ops b) { return a; }
        [BlackList] public int this[int i] { get { return i; } set { } }
        public int this[long i] { [BlackList] get { return 0; } set { } }
        [Obsolete] public int this[short i] { get { return i; } }
        public int this[byte i] { get { return i; } }
        [BlackList] public string this[string k] { get { return k; } set { } }
    }
    [LuaCallCSharp] public class ByName { public int this[int i] { get { return i; } } }
    [LuaCallCSharp] public class ByAccessorName { public string this[string k] { get { return k; } set { } } }
    [LuaCallCSharp]
    public class Lazy
    {
        [BlackList] public void Both() { }
        [BlackList] public void Mixed(string s) { }
        public void Mixed(int x) { }
        [BlackList] public int Field;
        [BlackList] public int Prop { get; set; }
        public int Getter { [BlackList] get { return 0; } set { } }
        [BlackList] public event System.Action Ev { add { } remove { } }
        [Obsolete] public void Old() { }
        public void Kept() { }
    }
}");
        // ByName's "Item" and ByAccessorName's "get_Item" name an indexer and an accessor: no XLSG003.
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        var ops = ManifestType(run, "Game.Ops");
        Assert.True(ops.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("name").GetString())
            .SequenceEqual(new[] { "op_Addition", "op_Multiply" }), "the [BlackList] and [Obsolete] operators are left out");
        var multiply = ops.GetProperty("operators").EnumerateArray().First(o => o.GetProperty("name").GetString() == "op_Multiply");
        Same(new[] { "Game.Ops::op_Multiply(Game.Ops,Game.Ops)" }, Strings(multiply.GetProperty("overloads")), "op_Multiply(Ops, int) is listed by its parameter types");
        Same(new[] { "Game.Ops::get_Item(System.Byte)" }, Strings(ops.GetProperty("indexers")), "indexers: [BlackList] on the indexer or its get accessor, [Obsolete]");
        Same(new[] { "Game.Ops::set_Item(System.Int64,System.Int32)" }, Strings(ops.GetProperty("newindexers")), "newindexers");
        Assert.True(Overloads(ops, "get_Item", false).Length == 0 && Overloads(ops, "set_Item", false).Length == 0,
            "the accessors of a string-keyed indexer go with it");
        Assert.True(Strings(ManifestType(run, "Game.ByName").GetProperty("indexers")).Length == 0, "an entry names an indexer Item");
        var byAccessor = ManifestType(run, "Game.ByAccessorName");
        Assert.True(Overloads(byAccessor, "get_Item", false).Length == 0, "an entry without parameter types names the accessor get_Item");
        Same(new[] { "Game.ByAccessorName::set_Item(System.String,System.String)" }, Overloads(byAccessor, "set_Item", false), "set_Item stays");
        Same(new[] { "SETTER_IDX Getter PropertySet", "METHOD_IDX Kept Method", "METHOD_IDX Mixed Method" },
            Strings(ManifestType(run, "Game.Lazy").GetProperty("lazy")), "lazy members without what [BlackList] or [Obsolete] leaves out");
    }

    // The wrappers are written again only for the type whose members changed.
    public static void WrapperOfAnEditedTypeIsWrittenAgain()
    {
        var input = Harness.Compile("Game", new[] { WrapperSource, "namespace Game { [XLua.LuaCallCSharp] public class Other { public int A; } }" });
        var first = Harness.Generate(input);
        var tree = input.SyntaxTrees.ElementAt(1);
        var edited = input.ReplaceSyntaxTree(tree, tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
            "namespace Game { [XLua.LuaCallCSharp] public class Other { public int A; public int B; } }")));
        var second = Harness.Generate(edited, first.Driver);
        var changed = second.Result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs)
            .Count(o => o.Reason != IncrementalStepRunReason.Cached && o.Reason != IncrementalStepRunReason.Unchanged);
        // Other's wrapper, and the registration with the manifest.
        Assert.Equal(2, changed, "outputs written again");
        Assert.True(second.Source(Wrappers.ClassName("Game.Other") + ".g.cs").Contains("_g_get_B"), "the new field is wrapped");
        var a = Harness.Generate("Game", WrapperSource);
        var b = Harness.Generate("Game", WrapperSource);
        Assert.True(a.Result.GeneratedSources.Select(s => s.HintName + s.SourceText).SequenceEqual(b.Result.GeneratedSources.Select(s => s.HintName + s.SourceText)),
            "two runs give the same files");
    }

    // XLSG008: a type a referenced assembly wraps (its GeneratedBindings list)
    // is not wrapped again; the assembly's own types are.
    public static void Xlsg008TypeBoundByAReference()
    {
        var lib = Harness.Generate("Lib", Usings + "[LuaCallCSharp] public class Player { public int Hp; }");
        Assert.Compiles(lib);
        Assert.True(lib.Source(Emitter.BindingsFile).Contains("new global::System.Type[] {\n    typeof(global::Player),"), "the GeneratedBindings list");
        var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib.Output));
        var input = Harness.Compile("Game", new[] { Usings + "[assembly: LuaCallCSharp(typeof(Player))]\n[LuaCallCSharp] public class Own { }" },
            new[] { Harness.Runtime, libRef });
        var run = Harness.Generate(input);
        Assert.Diagnostics(run, "XLSG008:Info");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("'Lib'"), run.Result.Diagnostics[0].GetMessage());
        Assert.Compiles(run);
        var wrappers = run.Result.GeneratedSources.Where(s => s.HintName.StartsWith("W_")).Select(s => s.HintName).ToArray();
        Assert.True(wrappers.Length == 1 && wrappers[0].StartsWith("W_Own_"), "wrappers: " + string.Join(", ", wrappers));
        Assert.True(run.Source(Emitter.BindingsFile).Contains("b.Name(typeof(global::Player), \"Player\")"), "Player stays in the name table");
    }

    const string SignatureSource = Usings + @"#nullable enable
namespace Game
{
    public delegate int IntA(int x);
    public delegate int IntB(int renamed);
    public delegate long LongA(int x);
    public delegate void RefA(ref int x);
    public delegate void OutA(out int x);
    public delegate void InA(in int x);
    public delegate void RefReadOnlyA([System.Runtime.CompilerServices.RequiresLocation] ref int x);
    public delegate void ParamsA(params int[] x);
    public delegate void ArrayA(int[] x);
    public delegate string? NullableA(string? s);
    public delegate string NotNullA(string s);
    public delegate (int a, int b) TupleA();
    public delegate (int, int) TupleB();
    public delegate dynamic DynamicA(dynamic d);
    public delegate object ObjectA(object d);
    public delegate void OrderA(int a, string b);
    public delegate void OrderB(string a, int b);
    public delegate List<int> ListA();
    public delegate List<long> ListB();
    public struct S { public int M(int a, string b, ref long c, params object[] d) { return 0; } }
    public class C { public C(int a) { } public static void N(int a, C c) { } }
}";

    static IMethodSymbol Invoke(Compilation c, string name)
    {
        return c.GetTypeByMetadataName("Game." + name).DelegateInvokeMethod;
    }

    // SignatureKey (section 5.4): which signatures share a bridge, rule by rule,
    // and the hotfix reduction (used from milestone M7).
    public static void SignatureKeyTable()
    {
        var c = Harness.Compile("Game", new[] { SignatureSource });
        Assert.True(!c.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "the signatures compile: "
            + string.Join("\n", c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)));
        var rows = new[]
        {
            // first, second, same bridge, the first's key
            Tuple.Create("IntA", "IntB", true, "System.Int32(System.Int32)"),          // parameter names do not count
            Tuple.Create("IntA", "LongA", false, "System.Int32(System.Int32)"),        // return type
            Tuple.Create("RefA", "OutA", false, "void(ref System.Int32)"),             // ref and out
            Tuple.Create("RefA", "InA", false, "void(ref System.Int32)"),              // ref and in: in writes nothing back
            Tuple.Create("InA", "RefReadOnlyA", true, "void(in System.Int32)"),        // ref readonly is in
            Tuple.Create("ParamsA", "ArrayA", false, "void(System.Int32[]...)"),       // the params flag
            Tuple.Create("NullableA", "NotNullA", true, "System.String(System.String)"),   // nullable annotations
            Tuple.Create("TupleA", "TupleB", true, "System.ValueTuple`2[System.Int32,System.Int32]()"),   // tuple element names
            Tuple.Create("DynamicA", "ObjectA", true, "System.Object(System.Object)"), // dynamic is object
            Tuple.Create("OrderA", "OrderB", false, "void(System.Int32,System.String)"),   // parameter order
            Tuple.Create("ListA", "ListB", false, "System.Collections.Generic.List`1[System.Int32]()"),   // type arguments
        };
        foreach (var row in rows)
        {
            string a = SignatureKey.Of(Invoke(c, row.Item1)), b = SignatureKey.Of(Invoke(c, row.Item2));
            Assert.Equal(row.Item4, a, "key of " + row.Item1);
            Assert.Equal(row.Item3, a == b, row.Item1 + " and " + row.Item2 + " share a bridge (" + a + " / " + b + ")");
        }

        var m = c.GetTypeByMetadataName("Game.S").GetMembers("M").OfType<IMethodSymbol>().Single();
        Assert.Equal("System.Int32(Game.S,System.Int32,System.Object,ref System.Int64,System.Object[]...)",
            SignatureKey.Of(m, KeyPurpose.Hotfix, 0), "hotfix: self and value types kept, reference types to object");
        Assert.Equal("System.Int32(System.Object,System.Object,System.Object,ref System.Int64,System.Object[]...)",
            SignatureKey.Of(m, KeyPurpose.Hotfix, 2), "hotfix with ValueTypeBoxing");
        var type = c.GetTypeByMetadataName("Game.C");
        Assert.Equal("void(System.Int32,System.Object)", SignatureKey.Of(type.GetMembers("N").OfType<IMethodSymbol>().Single(), KeyPurpose.Hotfix, 0),
            "hotfix: a static method has no self");
        Assert.Equal("void(System.Object,System.Int32)", SignatureKey.Of(type.InstanceConstructors.Single(), KeyPurpose.Hotfix, 0),
            "hotfix: a constructor returns void and has self");
        Assert.Equal("System.Int32(System.Int32,System.String,ref System.Int64,System.Object[]...)", SignatureKey.Of(m),
            "the delegate bridge key keeps every type and no self");
    }

    const string BridgeSource = Usings + @"
[assembly: CSharpCallLua(typeof(Action<int>))]
[assembly: CSharpCallLua(typeof(Func<int, int>))]
namespace Game
{
    public struct Pt { public int X; }
    [CSharpCallLua] public delegate int Twice(int x);
    [CSharpCallLua] public delegate void Results(int a, out string s, ref Pt p, in long l);
    [CSharpCallLua] public delegate int Many(string head, params object[] rest);
    [CSharpCallLua] public delegate void Notify(int code);
    [CSharpCallLua] public delegate short Narrow();
    [CSharpCallLua] public delegate Span<int> SpanResult();
    public interface IBase { int Count { get; } }
    [CSharpCallLua]
    public interface IScript : IBase, IDisposable
    {
        int Add(int a, int b);
        void Split(string s, out string head, ref int n);
        int Sum(params int[] values);
        string Name { get; set; }
        int this[int i] { get; set; }
        event Action Changed;
        int WithDefault() { return 1; }
        static int Shared() { return 2; }
    }
    [CSharpCallLua] public interface IGeneric { T Make<T>(); }
}";

    // Delegate and interface bridges (milestone M3): delegates with the same
    // SignatureKey share one bridge, every delegate type is registered with a
    // delegate closed over the bridge, and interfaces get a bridge class.
    public static void DelegateAndInterfaceBridges()
    {
        var run = Harness.Generate("Game", BridgeSource);
        Assert.Diagnostics(run, "XLSG004:Info", "XLSG004:Info");
        var messages = string.Join("\n", run.Result.Diagnostics.Select(d => d.GetMessage()));
        Assert.True(messages.Contains("Game.IGeneric") && messages.Contains("generic method"), messages);
        Assert.True(messages.Contains("Game.SpanResult") && messages.Contains("ref struct"), messages);
        Assert.Compiles(run);

        var root = ManifestType(run, null);
        var keys = root.GetProperty("delegateBridges").EnumerateArray()
            .ToDictionary(g => g.GetProperty("key").GetString(), g => Strings(g.GetProperty("delegates")));
        Same(new[] { "Game.Twice", "System.Func`2[System.Int32,System.Int32]" }, keys["System.Int32(System.Int32)"], "Twice and Func<int, int> share a bridge");
        Same(new[] { "Game.Notify", "System.Action`1[System.Int32]" }, keys["void(System.Int32)"], "Notify and Action<int>");
        Same(new[] { "Game.Results" }, keys["void(System.Int32,out System.String,ref Game.Pt,in System.Int64)"], "Results");
        Same(new[] { "Game.Many" }, keys["System.Int32(System.String,System.Object[]...)"], "Many");
        Assert.Equal(5, keys.Count, "bridges");
        var bridges = run.Source(Emitter.BridgesFile);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(bridges, @"internal static \S+ __Gen_Delegate_Imp\d+\(this global::XLua.DelegateBridge").Count,
            "bridge methods");
        // Results follow Lua's integer rule through the value readers, not the
        // argument checks, and are read inside try/finally, which restores the
        // Lua stack when a conversion throws.
        Assert.True(!bridges.Contains("xlua_check"), "results do not use the argument checks");
        Assert.True(bridges.Contains("__gen_ret = global::XLua.LuaDLL.Bridge.xlua_getint32(L, errFunc + 1);"), "an int result uses xlua_getint32");
        Assert.True(bridges.Contains("__gen_ret = global::XLua.LuaDLL.Bridge.xlua_getint16(L, errFunc + 1);"), "a short result uses xlua_getint16");
        Assert.True(!bridges.Contains("xlua_tointeger") && !bridges.Contains("lua_toint64"), "no lenient integer reader is left");
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(bridges, @"finally\s*\{\s*global::XLua\.LuaDLL\.Bridge\.lua_settop\(L, errFunc - 1\);").Count,
            "the four bridges with results restore the stack in finally");
        Assert.True(!System.Text.RegularExpressions.Regex.IsMatch(bridges, @"void __Gen_Delegate_Imp\d+\(this global::XLua.DelegateBridge __gen_bridge, int p0\)\s*\{[^}]*try"),
            "a bridge without results has no try");

        var itf = root.GetProperty("interfaceBridges").EnumerateArray().Single();
        Assert.Equal("Game.IScript", itf.GetProperty("type").GetString(), "the interface bridge");
        Same(new[] { "Game.IScript::Add(System.Int32,System.Int32)", "Game.IScript::Split(System.String,System.String&,System.Int32&)",
            "Game.IScript::Sum(System.Int32[])", "Game.IScript::WithDefault()", "System.IDisposable::Dispose()" },
            Strings(itf.GetProperty("methods")), "methods, with those of extended interfaces; no static ones");
        Same(new[] { "Game.IBase::Count get", "Game.IScript::Name get set" }, Strings(itf.GetProperty("properties")), "properties");
        Same(new[] { "Game.IScript::this[System.Int32] get set" }, Strings(itf.GetProperty("indexers")), "indexers");
        Same(new[] { "Game.IScript::Changed" }, Strings(itf.GetProperty("events")), "events");

        var bindings = run.Source(Emitter.BindingsFile);
        Assert.True(bindings.Contains("b.Delegate(typeof(global::Game.Twice), __gen_bridge => new global::Game.Twice(__gen_bridge.__Gen_Delegate_Imp"), "Twice is registered");
        Assert.True(bindings.Contains("b.InterfaceBridge(typeof(global::Game.IScript), " + Wrappers.BridgeClassName("Game.IScript") + ".__Create);"), "IScript is registered");

        // The registration fills the registry and sets Gen_Flag; the
        // delegates it makes are closed over the bridge.
        var context = new AssemblyLoadContext("bridges", isCollectible: true);
        try
        {
            var xlua = context.LoadFromStream(new MemoryStream(Harness.RuntimeImage));
            context.Resolving += (cx, name) => name.Name == "XLua.Mini" ? xlua : null;
            var game = context.LoadFromStream(new MemoryStream(Harness.Emit(run.Output)));
            game.GetType(Names.GeneratedNamespace("Game") + ".Bindings", true).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var registry = xlua.GetType("XLua.Binding.BindingRegistry", true);
            var delegates = (System.Collections.IDictionary)registry.GetField("delegates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var interfaces = (System.Collections.IDictionary)registry.GetField("interfaceBridges", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.Equal(7, delegates.Count, "registered delegate types");
            Assert.True(interfaces.Contains(game.GetType("Game.IScript", true)) && interfaces.Count == 1, "registered interface bridges");
            var bridgeType = xlua.GetType("XLua.DelegateBridge", true);
            var bridge = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(bridgeType);
            var made = (Delegate)((Delegate)delegates[game.GetType("Game.Twice", true)]).DynamicInvoke(bridge);
            Assert.True(made.Target == bridge && made.Method.IsStatic && made.Method.DeclaringType.FullName == Names.GeneratedNamespace("Game") + ".Bridges",
                "Twice is closed over the bridge: " + made.Method.DeclaringType + "." + made.Method.Name);
            Assert.Equal(true, (bool)bridgeType.GetField("Gen_Flag").GetValue(null), "Gen_Flag");
        }
        finally
        {
            context.Unload();
        }
    }

    const string StructSource = Usings + @"
[assembly: GCOptimize(typeof(Game.Ray), OptimizeFlag.Default)]
[assembly: AdditionalProperties(typeof(Game.Ray), ""Length"")]
namespace Game
{
    [GCOptimize] public struct V3 { public float x, y, z; }
    [GCOptimize] public struct Seven { public float a, b, c, d, e, f, g; }
    public struct Inner { public byte c; }
    [GCOptimize] public struct Mixed { public int i; public decimal m; public Inner inner; public double @class; static int ignored; int hidden; }
    public struct Ray { public V3 origin; public float Length { get; set; } }
    [GCOptimize(OptimizeFlag.PackAsTable)] public struct Tabled { public int a; public V3 v; }
    [GCOptimize] public struct Named { public string name; public int n; }
    [GCOptimize] public class Settings { public int level; public V3 at; }
    [LuaCallCSharp] public enum Mode { A, B }
    [GCOptimize] public enum Flags { X }
    [LuaCallCSharp] public class Player
    {
        public V3 Move(V3 v, ref Mixed m, Tabled t, Mode mode, Flags f, Lib.LibVec l, Lib.LibOther o) { return v; }
        public Ray ray;
        public Named Name(Named n) { return n; }
        public void Apply(Settings s) { }
    }
    [CSharpCallLua] public delegate V3 Mover(V3 v, Mode m);
}";

    const string StructLibrary = Usings + @"
[assembly: GCOptimize(typeof(Lib.LibOther))]
namespace Lib
{
    [GCOptimize] public struct LibVec { public float x, y; }
    public struct LibOther { public int a; }
    public struct NotConfigured { public int a; }
    [GCOptimize] public struct Unused { public int a; }   // configured here, not used by Game: not written there
}";

    // GCOptimize (milestone M4): the legacy selection of pushed, packed and
    // table-read types, the float grouping, [AdditionalProperties], the types
    // configured in a referenced assembly, and the registration.
    public static void StructsFollowTheLegacyModel()
    {
        var lib = Harness.Compile("Lib", new[] { StructLibrary });
        var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib));
        var run = Harness.Generate(Harness.Compile("Game", new[] { StructSource }, new[] { Harness.Runtime, libRef }));
        Assert.Diagnostics(run, "XLSG010:Warning");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("Game.Named"), run.Result.Diagnostics[0].GetMessage());
        Assert.Compiles(run);

        var root = ManifestType(run, null);
        var structs = root.GetProperty("structs").EnumerateArray().ToDictionary(s => s.GetProperty("type").GetString(), s => s);
        Same(new[] { "Game.Flags", "Game.Mixed", "Game.Mode", "Game.Named", "Game.Ray", "Game.Settings", "Game.Seven", "Game.Tabled", "Game.V3",
            "Lib.LibOther", "Lib.LibVec" }, structs.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(), "GCOptimize types, with those a reference configures and Game uses");
        Func<string, string> kind = n => structs[n].GetProperty("kind").GetString() + " " + structs[n].GetProperty("size").GetInt32()
            + (structs[n].GetProperty("pushed").GetBoolean() ? " pushed" : "");
        Assert.Equal("value 12 pushed", kind("Game.V3"), "V3");
        Assert.Equal("value 29 pushed", kind("Game.Mixed"), "Mixed: int, decimal, Inner (byte), double; no static or private fields");
        Assert.Equal("value 16 pushed", kind("Game.Ray"), "Ray with its additional property");
        Assert.Equal("table -1", kind("Game.Named"), "a struct with a string field is read from tables only");
        Assert.Equal("table -1", kind("Game.Settings"), "a class");
        Assert.Equal("enum 4 pushed", kind("Game.Mode"), "a [LuaCallCSharp] enum");
        Assert.Equal("enum 4", kind("Game.Flags"), "a GCOptimize enum has pushers but wrappers box it, as with the legacy generator");
        Assert.Equal("value 8 pushed", kind("Lib.LibVec"), "[GCOptimize] on a type of a reference");
        Assert.Equal("value 4 pushed", kind("Lib.LibOther"), "[assembly: GCOptimize] of a reference");
        Assert.Equal(1, structs["Game.Tabled"].GetProperty("flag").GetInt32(), "PackAsTable");
        Same(new[] { "6", "1" }, structs["Game.Seven"].GetProperty("groups").EnumerateArray().Select(x => x.GetInt32().ToString()).ToArray(), "floats packed six at a time");
        Same(new[] { "3" }, structs["Game.V3"].GetProperty("groups").EnumerateArray().Select(x => x.GetInt32().ToString()).ToArray(), "V3 floats");
        Same(new[] { "origin Game.V3", "Length System.Single property" }, Strings(structs["Game.Ray"].GetProperty("fields")), "Ray fields");
        Same(new[] { "Game.Inner", "Game.Mixed root", "Game.Ray root", "Game.Seven root", "Game.Tabled root", "Game.V3 root", "Lib.LibOther root", "Lib.LibVec root" },
            Strings(root.GetProperty("packUnpack")), "Pack/UnPack: the packable types and their struct fields");

        var player = run.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Game_Player_")).SourceText.ToString();
        foreach (var t in new[] { "Game_V3_", "Game_Mixed_", "Game_Tabled_", "Game_Mode_", "Lib_LibVec_", "Lib_LibOther_" })
            Assert.True(player.Contains("Structs.Get_" + t), "Player reads " + t + " through Structs");
        Assert.True(player.Contains("Structs.Push_Game_V3_") && player.Contains("Structs.Update_Game_Mixed_"), "V3 is pushed and Mixed updated without boxing");
        Assert.True(player.Contains("Structs.Get_Game_Named_") && player.Contains("translator.Push(L, gen_ret)"), "Named is read from a table and pushed boxed");
        Assert.True(player.Contains("BindingApi.GetStructArg(translator, L, 6, out _f)"),
            "a GCOptimize enum outside LuaCallCSharp has no generated reader: GetStructArg, which raises for nil (Q34)");
        Assert.True(run.Source(Emitter.BridgesFile).Contains("Structs.Push_Game_V3_"), "delegate bridges push without boxing too");
        var packing = run.Source(Emitter.PackFile);
        Assert.True(packing.Contains("xlua_pack_float6(buff, offset, field.a, field.b, field.c, field.d, field.e, field.f)")
            && packing.Contains("xlua_pack_float(buff, offset + 24, field.g)"), "float groups");
        Assert.True(packing.Contains("field.@class"), "keyword field names are escaped");
        var bindings = run.Source(Emitter.BindingsFile);
        Assert.True(bindings.Contains("b.Struct(typeof(global::Game.V3), typeof(global::Game.V3[]), Structs.Ops_Game_V3_")
            && bindings.Contains("b.Caster(typeof(global::Game.Settings), typeof(global::Game.Settings[]), Structs.Ops_Game_Settings_"), "registration");
        Assert.True(!bindings.Contains("b.Struct<") && !bindings.Contains("b.Caster<") && !bindings.Contains("b.ArrayAccessor<"),
            "no generic registration, which IL2CPP compiles once per value type");
        // The registered readers read results (result: true, owner decision Q35).
        var structSource = run.Source(Emitter.StructsFile);
        Assert.True(structSource.Contains("out val, true);"), "Value_ and StructOp.Get read results:\n" + structSource);

        var context = new AssemblyLoadContext("structs", isCollectible: true);
        try
        {
            var xlua = context.LoadFromStream(new MemoryStream(Harness.RuntimeImage));
            var libAssembly = context.LoadFromStream(new MemoryStream(Harness.Emit(lib)));
            context.Resolving += (cx, name) => name.Name == "XLua.Mini" ? xlua : name.Name == "Lib" ? libAssembly : null;
            var game = context.LoadFromStream(new MemoryStream(Harness.Emit(run.Output)));
            game.GetType(Names.GeneratedNamespace("Game") + ".Bindings", true).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var registry = xlua.GetType("XLua.Binding.BindingRegistry", true);
            var types = (ICollection<Type>)registry.GetField("structTypes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.Equal(11, types.Count, "registered GCOptimize types");
            var setters = (System.Collections.IDictionary)registry.GetField("arraySetters", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.True(setters.Contains(game.GetType("Game.Settings", true).MakeArrayType()), "the array setter of a table-read type");
        }
        finally
        {
            context.Unload();
        }
    }

    // link.xml for wrapped types (docs/design/source-generator.md, "link.xml entries
    // audited (2026-10-28)"): in strict minimal a [LuaCallCSharp] type gets no entry,
    // since the wrapper calls its members, and [ReflectionUse] on the same type keeps
    // it whole (preserve="all") and leaves its wrapper in place: the way back to
    // upstream's link.xml for one type that MIGRATING.md names. The legacy preset
    // keeps every [LuaCallCSharp] type whole (LinkXmlKeepsBoundTypesWholeByPreset).
    public static void ReflectionUseKeepsBoundTypeWhole()
    {
        var run = Harness.Generate(Harness.Compile("Game", new[] { Usings + @"
[assembly: LuaCallCSharp(typeof(Game.Bound), typeof(Game.Kept))]
[assembly: ReflectionUse(typeof(Game.Kept))]
namespace Game
{
    public class Bound { public void Call() { } int hidden; }
    public class Kept { public void Call() { } int hidden; }
}" }, new[] { Harness.RuntimeWith("XLUA_PRESET_MINIMAL") }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Compiles(run);
        Assert.True(run.Result.GeneratedSources.Any(x => x.HintName.StartsWith("W_Game_Kept_")), "[ReflectionUse] does not take the wrapper away");
        var linkXml = LinkXmlOf(run);
        Assert.True(linkXml != null && linkXml.Contains("<type fullname=\"Game.Kept\" preserve=\"all\"/>"), "link.xml does not keep Kept whole:\n" + linkXml);
        Assert.True(!linkXml.Contains("Game.Bound") && !linkXml.Contains("<method") && !linkXml.Contains("<field"),
            "a wrapped type needs no link.xml entry in strict minimal:\n" + linkXml);
    }

    // The link.xml fragment a run wrote (the LinkXml.Xml constant), or null.
    static string LinkXmlOf(Run run)
    {
        var source = run.Source(Emitter.LinkXmlFile);
        if (source == null) return null;
        const string Start = "Xml = @\"";
        int start = source.IndexOf(Start, StringComparison.Ordinal) + Start.Length;
        int end = source.IndexOf("\";\n", start, StringComparison.Ordinal);
        return source.Substring(start, end - start).Replace("\"\"", "\"");
    }

    // Each <type> of a fragment, once per assembly, as "assembly|type|preserve|
    // element:name,..." with its child elements.
    static string[] LinkXmlTypes(string xml)
    {
        var lines = new List<string>();
        foreach (var a in XDocument.Parse(xml).Root.Elements("assembly"))
        {
            var names = new HashSet<string>();
            foreach (var t in a.Elements("type"))
            {
                string name = (string)t.Attribute("fullname");
                Assert.True(names.Add(name), "link.xml lists " + name + " twice:\n" + xml);
                lines.Add((string)a.Attribute("fullname") + "|" + name + "|" + ((string)t.Attribute("preserve") ?? "") + "|"
                    + string.Join(",", t.Elements().Select(m => m.Name.LocalName + ":" + (string)m.Attribute("name"))));
            }
        }
        return lines.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    const string BoundTypesSource = Usings + @"
[assembly: LuaCallCSharp(typeof(List<int>), typeof(Game.Outer.Inner))]
[assembly: DoNotGen(typeof(Game.Bound), ""Slow"")]
[assembly: ReflectionUse(typeof(Game.Reflected))]
namespace Game
{
    [LuaCallCSharp] public class Bound { public void Slow() { } public void Fast() { } int hidden; void Secret() { } }
    public class Outer { public class Inner { public int Count; int hidden; } }
    [LuaCallCSharp] public enum Mode { A, B }
    public class Reflected { int hidden; }
    public class Unbound { int hidden; }
}";

    // Owner decision Q75 of 2026-10-29: where the build reaches members of a
    // wrapped type by reflection beyond its wrapper (xlua.private_accessible,
    // xlua.access, the reflection fallback, the project's own reflection), every
    // [LuaCallCSharp] type gets preserve="all", as upstream's link.xml gave it:
    // the legacy preset always, minimal with HOTFIX_ENABLE. Strict minimal, and
    // minimal with XLUA_REFLECTION alone (whose fallback wraps only types
    // without bindings), keep the narrow entries: the lazy members only.
    // [ReflectionUse] keeps its type whole everywhere, and a type kept whole
    // gets no member entries besides.
    public static void LinkXmlKeepsBoundTypesWholeByPreset()
    {
        var predicate = new (string[] Defines, bool Whole)[]
        {
            (new string[0], true),
            (new[] { "GEN_CODE_MINIMIZE" }, true),
            (new[] { "HOTFIX_ENABLE" }, true),
            (new[] { "XLUA_RESTRICT_PRIVATE_ACCESS" }, true),
            (new[] { "XLUA_NO_REFLECTION", "XLUA_RESTRICT_PRIVATE_ACCESS" }, true),
            (new[] { "XLUA_PRESET_MINIMAL" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "XLUA_NO_GCM" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "XLUA_UTIL" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "XLUA_REFLECTION" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "HOTFIX_ENABLE" }, true),
            (new[] { "XLUA_PRESET_MINIMAL", "XLUA_REFLECTION", "HOTFIX_ENABLE" }, true),
        };
        foreach (var c in predicate)
        {
            Assert.Equal(c.Whole, Wrappers.PreserveBoundTypes(c.Defines), "PreserveBoundTypes(" + string.Join(";", c.Defines) + ")");
        }

        string list = typeof(List<int>).Assembly.GetName().Name + "|System.Collections.Generic.List`1|all|";
        foreach (var c in new (string[] Defines, bool Whole)[]
        {
            (new string[0], true),
            (new[] { "XLUA_PRESET_MINIMAL" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "XLUA_REFLECTION" }, false),
            (new[] { "XLUA_PRESET_MINIMAL", "HOTFIX_ENABLE" }, true),
        })
        {
            string what = c.Defines.Length == 0 ? "legacy" : string.Join(";", c.Defines);
            var runtime = c.Defines.Length == 0 ? Harness.Runtime : Harness.RuntimeWith(c.Defines);
            var run = Harness.Generate(Harness.Compile("Game", new[] { BoundTypesSource }, new[] { runtime }, Harness.ParseWith(c.Defines)));
            Assert.Compiles(run);
            var xml = LinkXmlOf(run);
            Assert.True(xml != null, what + ": no " + Emitter.LinkXmlFile);
            var expected = c.Whole
                ? new[] { "Game|Game.Bound|all|", "Game|Game.Mode|all|", "Game|Game.Outer/Inner|all|", "Game|Game.Reflected|all|", list }
                : new[] { "Game|Game.Bound||method:Slow", "Game|Game.Reflected|all|" };
            Same(expected.OrderBy(x => x, StringComparer.Ordinal).ToArray(), LinkXmlTypes(xml), what + " link.xml types");
            Assert.True(!xml.Contains("Unbound"), what + ": a type without configuration in link.xml");
        }
    }

    const string FallbackSource = Usings + @"
[assembly: DoNotGen(typeof(Game.Outer.Inner), ""Slow"", ""count"")]
[assembly: ReflectionUse(typeof(Game.Reflected))]
namespace Game
{
    [LuaCallCSharp] public class Settings { public int Level { get; init; } public int Plain { get; set; } }
    public class Outer { [LuaCallCSharp] public class Inner { public void Slow() { } public int count; public void Fast() { } } }
    public class Reflected { int hidden; }
}";

    // Section 5.5 and 5.7 (milestone M5): a member the generator cannot wrap is
    // registered for reflection with its reason, and the link.xml fragment keeps
    // what Lua reaches by reflection; XLUA_SG_SKIP_REGISTER registers nothing.
    public static void FallbackAndLinkXml()
    {
        var run = Harness.Generate("Game", FallbackSource);
        Assert.Diagnostics(run, "XLSG004:Info");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("Level") && run.Result.Diagnostics[0].GetMessage().Contains("reflection"),
            run.Result.Diagnostics[0].GetMessage());
        Assert.Compiles(run);
        var settings = ManifestType(run, "Game.Settings");
        Same(new[] { "SETTER_IDX Level PropertySet" }, Strings(settings.GetProperty("fallback")), "the init-only setter falls back");
        Same(new[] { "Plain" }, Strings(settings.GetProperty("setters")), "the other setter is wrapped");
        Assert.True(Strings(settings.GetProperty("lazy")).Length == 0, "fallback members are not DoNotGen members");
        var wrapper = run.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Game_Settings_")).SourceText.ToString();
        Assert.True(wrapper.Contains("BindingApi.RegisterFallback(L, global::XLua.Utils.SETTER_IDX, \"Level\", type, global::XLua.LazyMemberTypes.PropertySet, false, \"its setter is init-only")
            && wrapper.Contains("#else") && wrapper.Contains("PropertySet, false, null);"), "the reason only where it is logged");

        // Legacy keeps the [LuaCallCSharp] types whole, so their lazy members get
        // no entries of their own; strict minimal lists the lazy members by name,
        // and wrapped members need no entry.
        var linkXml = LinkXmlOf(run);
        Assert.True(linkXml != null, "no " + Emitter.LinkXmlFile);
        Same(new[] { "Game|Game.Outer/Inner|all|", "Game|Game.Reflected|all|", "Game|Game.Settings|all|" }, LinkXmlTypes(linkXml), "legacy link.xml types");
        var strict = Harness.Generate(Harness.Compile("Game", new[] { FallbackSource }, new[] { Harness.RuntimeWith("XLUA_PRESET_MINIMAL") },
            Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Compiles(strict);
        Same(new[] { "Game|Game.Outer/Inner||field:count,method:Slow", "Game|Game.Reflected|all|", "Game|Game.Settings||property:Level" },
            LinkXmlTypes(LinkXmlOf(strict)), "strict minimal link.xml types");

        var skip = Harness.Generate(Harness.Compile("Game", new[] { FallbackSource }, null,
            Harness.Parse.WithPreprocessorSymbols("XLUA_SG_SKIP_REGISTER")));
        Assert.Compiles(skip);
        var context = new AssemblyLoadContext("skip", isCollectible: true);
        try
        {
            var xlua = context.LoadFromStream(new MemoryStream(Harness.RuntimeImage));
            context.Resolving += (cx, name) => name.Name == "XLua.Mini" ? xlua : null;
            var game = context.LoadFromStream(new MemoryStream(Harness.Emit(skip.Output)));
            game.GetType(Names.GeneratedNamespace("Game") + ".Bindings", true).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var registered = (ICollection<string>)xlua.GetType("XLua.Binding.BindingRegistry", true)
                .GetField("registered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.Equal(0, registered.Count, "units registered with XLUA_SG_SKIP_REGISTER");
        }
        finally
        {
            context.Unload();
        }
    }

    const string HotfixSource = Usings + @"
[assembly: Hotfix(typeof(Game.ByAssembly), HotfixFlag.ValueTypeBoxing)]
namespace Game
{
    public struct Pt { public int X; }
    class Hidden { }
    [Hotfix]
    public class Shop
    {
        public Shop(int gold) { }
        public int Buy(int id, string name) { return id; }
        public static long Price(long a) { return a; }
        public Pt Move(ref Pt p, out int moved) { moved = 0; return p; }
        public int Total(params int[] all) { return 0; }
        int Secret(Hidden h) { return 0; }
        public int Level { get; set; }
        public event Action Changed;
        public T Echo<T>(T value) { return value; }
    }
    [Hotfix(HotfixFlag.IgnoreProperty | HotfixFlag.IgnoreNotPublic | HotfixFlag.IgnoreCompilerGenerated)]
    public class Quiet
    {
        public double Rate { get; set; }
        public string Hand { get { return string.Empty; } }
        internal float Inner(float f) { return f; }
        public event Action Changed;
    }
    [Hotfix(HotfixFlag.Inline)] public class Inlined { public decimal Skip(decimal d) { return d; } }
    [Hotfix] public struct Vec { public int X; public int Sum(int y) { return X + y; } }
    public class ByAssembly { public byte Cut(byte b) { return b; } }
    [Hotfix] public class Box<T> { public T Get() { return default(T); } public static short Make(short s) { return s; } }
}";

    // Hotfix bridges per assembly (milestone M7): the methods Generator.cs
    // GenDelegateBridge selects, keyed by SignatureKey (KeyPurpose.Hotfix), and
    // the IntKey slots registered with the assembly's unit.
    public static void HotfixBridgesFollowTheLegacySelection()
    {
        var run = Harness.Generate("Game", HotfixSource);
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        var keys = Strings(ManifestType(run, null).GetProperty("hotfixBridges"));
        Assert.True(!keys.Contains("System.Double(System.Object)") && !keys.Any(k => k.Contains("Single")) && !keys.Any(k => k.Contains("Decimal")),
            "IgnoreProperty, IgnoreNotPublic and Inline leave Quiet's property, Inner and Inlined out: " + string.Join(", ", keys));
        Assert.True(!keys.Any(k => k.Contains("Hidden")), "a non-public reference type (Shop.Secret) becomes object in the key");
        foreach (var k in new[] { "Game.Pt(System.Object,ref Game.Pt,out System.Int32)", "System.Byte(System.Object,System.Object)",
            "System.Int16(System.Int16)", "System.Int32(System.Object)", "System.Int32(System.Object,System.Int32,System.Object)",
            "System.Int32(System.Object,System.Int32[]...)", "System.Int32(Game.Vec,System.Int32)", "System.Int64(System.Int64)",
            "void(System.Object)", "void(System.Object,System.Int32)", "void(System.Object,System.Object)",
            "System.Int32(System.Object,System.Object)" })
        {
            Assert.True(keys.Contains(k), "missing hotfix bridge " + k + " in " + string.Join(", ", keys));
        }
        Assert.Equal(12, keys.Length, "hotfix bridges: " + string.Join(", ", keys));
        var bridges = run.Source(Emitter.HotfixBridgesFile);
        Assert.True(bridges.Contains("internal static global::XLua.DelegateBridge Get(int id)") && bridges.Contains("internal static void Set(int id, global::XLua.DelegateBridge bridge)"),
            "the IntKey slots");
        Assert.True(run.Source(Emitter.BindingsFile).Contains("b.HotfixSlots(HotfixBridges.Set, typeof(HotfixBridges));"), "the slots are registered");
    }

    const string ObfuzSource = FallbackSource + @"
namespace Game
{
    [LuaCallCSharp] public enum Big { V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20 }
    [LuaCallCSharp] public enum Small { A, B }
}";

    // Milestone M8: the obfuz rule fragment keeps the names Lua reaches by
    // reflection, and Register, which hosts find by name, carries
    // [Obfuscation(Exclude = true)].
    public static void ObfuzRulesAndObfuscationExclude()
    {
        var run = Harness.Generate("Game", ObfuzSource);
        Assert.Compiles(run);
        var bindings = run.Source(Emitter.BindingsFile);
        Assert.True(bindings.Contains("[global::System.Reflection.Obfuscation(Exclude = true)]\n        internal static void Register()"),
            "Register is not excluded from obfuscation:\n" + bindings);
        var rules = run.Source(Emitter.ObfuzRulesFile);
        Assert.True(rules != null, "no " + Emitter.ObfuzRulesFile);
        Assert.True(rules.StartsWith("// <auto-generated/>") || rules.StartsWith("#if UNITY_EDITOR || XLUA_GENERAL"), "Editor and host builds only");
        foreach (var expected in new[] { "<obfuz>", "<assembly name=\"\"Game\"\">",
            "<type name=\"\"Game.Reflected\"\" obName=\"\"0\"\" applyToMembers=\"\"*\"\"/>",
            "<type name=\"\"Game.Outer/Inner\"\" obName=\"\"0\"\" applyToNestedTypes=\"\"0\"\">",
            "<method name=\"\"Slow\"\" obName=\"\"0\"\"/>", "<field name=\"\"count\"\" obName=\"\"0\"\"/>",
            "<type name=\"\"Game.Settings\"\" obName=\"\"0\"\" applyToNestedTypes=\"\"0\"\">", "<property name=\"\"Level\"\" obName=\"\"0\"\"/>",
            "<type name=\"\"Game.Big\"\">\n      <field name=\"\"*\"\" obName=\"\"0\"\"/>" })
        {
            Assert.True(rules.Contains(expected), "the obfuz rules lack " + expected + ":\n" + rules);
        }
        Assert.True(!rules.Contains("Fast") && !rules.Contains("Plain") && !rules.Contains("Small"),
            "wrapped members and enums with literal names need no rule:\n" + rules);

        // The fragment is a well-formed obfuz document once the verbatim string's quotes are undone.
        int start = rules.IndexOf("@\"") + 2;
        var xml = System.Xml.Linq.XDocument.Parse(rules.Substring(start, rules.IndexOf("\";", start) - start).Replace("\"\"", "\""));
        Assert.Equal("obfuz", xml.Root.Name.LocalName, "root element");

        var plain = Harness.Generate("Game", FallbackSource.Replace("[assembly: ReflectionUse(typeof(Game.Reflected))]", "")
            .Replace("[assembly: DoNotGen(typeof(Game.Outer.Inner), \"Slow\", \"count\")]", "").Replace("{ get; init; }", "{ get; set; }"));
        Assert.Compiles(plain);
        Assert.True(plain.Source(Emitter.ObfuzRulesFile) == null, "rules without anything reached by reflection");
    }

    // A generator that references a newer compiler than the host's does not load
    // (CS9057, SOURCEGEN_SPIKE.md note 2): Unity 2021.3 and later ship 4.3.1. Its
    // other references must be ones that Roslyn 4.3.1 loads itself, at most at the
    // versions Microsoft.CodeAnalysis.Common 4.3.1 depends on.
    // An extension method with a `ref readonly` parameter (from metadata, older
    // Roslyn sees `ref` with [RequiresLocation]): the delegate that takes its
    // MethodInfo declares the parameter with `ref`, so Roslyn 4.10 warned CS9198
    // in Unity 6000.6 (2026-10-18) until the registration disabled it.
    public static void RefReadonlyExtensionMethodsDisableCS9198()
    {
        const string source = Usings + @"
namespace System.Runtime.CompilerServices { public sealed class RequiresLocationAttribute : Attribute { } }
namespace Game
{
    public class Plain { }
    [LuaCallCSharp] public static class Ext
    {
        public static int Plus(this Plain self, [System.Runtime.CompilerServices.RequiresLocation] ref int x) { return x + 1; }
    }
    [LuaCallCSharp] public static class OtherExt
    {
        public static int Twice(this Plain self, int x) { return x * 2; }
    }
}";
        Func<Run, string> bindings = r => r.Source("XLua.Bindings.g.cs");
        var run = Harness.Generate("Game", source);
        Assert.Compiles(run);
        string text = bindings(run);
        Assert.True(text.Contains("delegate int __GEN_DELEGATE0(global::Game.Plain p0, ref int p1);"), "Plus by ref:\n" + text);
        Assert.True(text.Contains("#pragma warning disable CS9198"), "CS9198 disabled:\n" + text);

        var plain = Harness.Generate("Game", source.Replace("[System.Runtime.CompilerServices.RequiresLocation] ref int x", "int x"));
        Assert.Compiles(plain);
        Assert.True(!bindings(plain).Contains("CS9198"), "no pragma without ref readonly parameters");
    }

    // A [CSharpCallLua] delegate with a `ref readonly` parameter: its bridge
    // declares the parameter with `in`, and Register creates the delegate from
    // it, which Roslyn 4.10 reports as CS9198. The Bridges file disabled the
    // warning, but the conversion is in the Bindings file, so Unity 6000.6
    // warned in the editor suite (prepare.py --source-gen --roslyn-sdk,
    // 2026-10-18, XluaRefReadonlyLib.Measure).
    public static void RefReadonlyDelegatesDisableCS9198InTheRegistration()
    {
        const string source = Usings + @"
namespace System.Runtime.CompilerServices { public sealed class RequiresLocationAttribute : Attribute { } }
namespace Game
{
    [CSharpCallLua] public delegate float Measure([System.Runtime.CompilerServices.RequiresLocation] ref int x);
    [CSharpCallLua] public delegate float Plain(int x);
}";
        Func<Run, string> bindings = r => r.Source("XLua.Bindings.g.cs");
        // Not compiled: this harness's Roslyn 4.3.1 sees the source parameter as
        // plain `ref`, so the `in` bridge does not convert (CS0123); Roslyn 4.10
        // converts it with the warning. The text is what matters here.
        var run = Harness.Generate("Game", source);
        string text = bindings(run);
        Assert.True(text.Contains("new global::Game.Measure(__gen_bridge."), "Measure registered:\n" + text);
        Assert.True(text.Contains("#pragma warning disable CS9198"), "CS9198 disabled in the registration:\n" + text);

        var plain = Harness.Generate("Game", source.Replace("[CSharpCallLua] public delegate float Measure([System.Runtime.CompilerServices.RequiresLocation] ref int x);", ""));
        Assert.Compiles(plain);
        Assert.True(!bindings(plain).Contains("CS9198"), "no pragma without ref readonly parameters");
    }

    // Unity 6000.6's PhysicsScene2D: GetRayIntersection(Ray, float, int = -5)
    // next to (Ray, float, int = -5, Allocator = ...). A wrapper call that left
    // the optional parameter out was ambiguous (CS0121); the wrapper writes the
    // defaults out, and only for such calls.
    public static void AmbiguousDefaultsAreWrittenOut()
    {
        const string source = Usings + @"
namespace Game
{
    public enum Allocator { Temp = 2, Persistent = 4 }
    public struct Ray { public float x; }
    [LuaCallCSharp] public struct Scene
    {
        public int GetRayIntersection(Ray ray, float distance, int layerMask = -5) { return 1; }
        public int GetRayIntersection(Ray ray, float distance, int layerMask = -5, Allocator allocator = Allocator.Temp) { return 2; }
        public static string Mixed(int a, float f = 1.5f, double d = double.NegativeInfinity, string s = ""q\""\n"", Allocator e = Allocator.Persistent, int? n = null, Ray r = default, ulong u = ulong.MaxValue, long l = long.MinValue) { return """"; }
        public static string Mixed(int a, float f = 1.5f, double d = double.NegativeInfinity, string s = ""q\""\n"", Allocator e = Allocator.Persistent, int? n = null, Ray r = default, ulong u = ulong.MaxValue, long l = long.MinValue, bool extra = false) { return """"; }
        public int Plain(int a, int b = 3) { return a + b; }
    }
}";
        var run = Harness.Generate("Game", source);
        Assert.Compiles(run);
        string text = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.True(text.Contains("gen_to_be_invoked.GetRayIntersection(_ray, _distance, (int)(-5));"), "layerMask written out:\n" + text);
        Assert.True(text.Contains("gen_to_be_invoked.GetRayIntersection(_ray, _distance, (int)(-5), (global::Game.Allocator)(2));"), "both written out");
        Assert.True(text.Contains("global::Game.Scene.Mixed(_a, (float)(1.5F), (double)(double.NegativeInfinity), (string)(\"q\\\"\\n\"), "
            + "(global::Game.Allocator)(4), default(int?), default(global::Game.Ray), (ulong)(18446744073709551615), (long)(-9223372036854775808));"),
            "every constant written out");
        Assert.True(text.Contains("gen_to_be_invoked.Plain(_a);"), "an unambiguous call still leaves its default out");
    }

    // Overload selection (ObjectCasters.cs OverloadScore): a group whose
    // overloads can match the same Lua value scores every matching overload
    // and calls the cheapest; a group whose overloads never match the same
    // value keeps the plain checks in order. Constructors, indexers and
    // operators follow the same rule.
    public static void OverlappingOverloadsAreScored()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace Game
{
    public class Base { }
    public class Derived : Base { }
    public class Other { }
    [LuaCallCSharp] public class Picker
    {
        public Picker(double d) { }
        public Picker(int i) { }
        public static string Num(double d) { return """"; }
        public static string Num(int i) { return """"; }
        public static string Disjoint(int i) { return """"; }
        public static string Disjoint(string s) { return """"; }
        public static string Classes(Base b) { return """"; }
        public static string Classes(Derived d) { return """"; }
        public static string Unrelated(Derived d) { return """"; }
        public static string Unrelated(Other o) { return """"; }
        public static string Objects(object o) { return """"; }
        public static string Objects(Base b) { return """"; }
        public string this[int i] { get { return """"; } }
        public string this[double d] { get { return """"; } }
        public static string operator +(Picker a, int b) { return """"; }
        public static string operator +(Picker a, double b) { return """"; }
    }
}");
        Assert.Compiles(run);
        string text = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        string Function(string name)
        {
            int start = text.IndexOf(" " + name + "(");
            Assert.True(start >= 0, name + " written:\n" + text);
            int end = text.IndexOf("\n        }\n", start);
            return text.Substring(start, end - start);
        }
        foreach (var scored in new[] { "_m_Num_xlua_st_", "_m_Classes_xlua_st_", "_m_Objects_xlua_st_", "__CreateInstance", "__CSIndexer", "__AddMeta" })
        {
            Assert.True(Function(scored).Contains("gen_pick = -1"), scored + " scores:\n" + Function(scored));
        }
        // The checks of a scored group read what the costs need: a number's
        // kind once (gen_n1), a C# object's check and cost in one call.
        string num = Function("_m_Num_xlua_st_");
        Assert.True(num.Contains("NumberCost(gen_n1, 5)") && num.Contains("NumberCost(gen_n1, 1)"), "double and int kinds:\n" + num);
        Assert.True(num.Contains("(gen_n1 < 0 ? (gen_n1 = global::XLua.LuaDLL.Bridge.xlua_number_kind(L, 1)) : gen_n1) != 0")
            && num.Contains("(gen_n1 < 0 ? (gen_n1 = global::XLua.LuaDLL.Bridge.xlua_number_kind(L, 1)) : gen_n1) >= 2")
            && !num.Contains("xlua_isinteger_number") && !num.Contains("lua_type"), "number checks from the number kind:\n" + num);
        string classes = Function("_m_Classes_xlua_st_");
        Assert.True(classes.Contains("(gen_s1 = global::XLua.Binding.BindingApi.CheckedArgScore(translator, L, 1, typeof(global::Game.Derived), ref gen_t1)) >= 0")
            && !classes.Contains("Assignable"), "class check and cost in one call:\n" + classes);
        // object too, and both read the argument's lua_type once (gen_t1).
        string objects = Function("_m_Objects_xlua_st_");
        Assert.True(objects.Contains("CheckedArgScore(translator, L, 1, typeof(object), ref gen_t1)")
            && objects.Contains("CheckedArgScore(translator, L, 1, typeof(global::Game.Base), ref gen_t1)") && !objects.Contains("Assignable"),
            "object and class checks and costs in one call each:\n" + objects);
        // Outside a scored group the checks stay.
        Assert.True(Function("_m_Disjoint_xlua_st_").Contains("xlua_isinteger_number(L, 1)"), "plain int check");
        Assert.True(!Function("_m_Num_xlua_st_").Contains("ObjectTranslatorPool"), "numbers are scored without the translator");
        foreach (var plain in new[] { "_m_Disjoint_xlua_st_", "_m_Unrelated_xlua_st_" })
        {
            Assert.True(!Function(plain).Contains("gen_pick"), plain + " keeps the plain checks:\n" + Function(plain));
        }
    }

    // A generic method whose type parameter only the return type uses is left
    // out (XLSG004): the wrapper calls it without type arguments, which does
    // not compile (CS0411). Reflection (Utils.IsSupportedMethod) cannot call it
    // either.
    public static void GenericMethodsCSharpCannotInfer()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace Game
{
    public class Base { }
    [LuaCallCSharp] public class Player
    {
        public T Find<T>(T fallback) where T : Base { return fallback; }
        public U Convert<T, U>(T source) where T : Base where U : Base { return null; }
    }
}");
        Assert.Diagnostics(run, "XLSG004:Info");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("Convert") && run.Result.Diagnostics[0].GetMessage().Contains("CS0411"),
            run.Result.Diagnostics[0].GetMessage());
        Assert.Compiles(run);
        var player = ManifestType(run, "Game.Player");
        Same(new[] { "Game.Player::Find`1(T)" }, Overloads(player, "Find", false), "a type parameter used by a parameter");
        Assert.True(Overloads(player, "Convert", false).Length == 0, "Convert left out");
    }

    // What the LuaCATS definitions read from the manifest besides the wrapped
    // types (milestone M10): the [ReflectionUse] types by Type.ToString() name,
    // and the BlackList entries by type in the legacy list form (type, member,
    // parameter types).
    public static void ManifestForLuaCATS()
    {
        var run = Harness.Generate("Game", Usings + @"
[assembly: ReflectionUse(typeof(Game.Settings))]
[assembly: ReflectionUse(typeof(System.Collections.Generic.Dictionary<string, int>))]
[assembly: ReflectionUse(typeof(Game.Settings))]
[assembly: BlackList(typeof(Game.Player), ""Run"", typeof(int))]
[assembly: BlackList(typeof(Game.Player), ""speed"")]
[assembly: BlackList(""UnityEngine.MonoBehaviour"", ""runInEditMode"")]
namespace Game
{
    public class Settings { public int Volume; }
    [LuaCallCSharp] public class Player
    {
        public float speed;
        public void Run(int x) { }
        public void Run(string s) { }
    }
}");
        Assert.Diagnostics(run, "XLSG003:Info");
        Assert.Compiles(run);
        var root = ManifestType(run, null);
        Same(new[] { "Game.Settings", "System.Collections.Generic.Dictionary`2[System.String,System.Int32]" },
            Strings(root.GetProperty("reflectionUse")), "reflectionUse: sorted, once each");
        Same(new[] { "Game.Player|Run|System.Int32", "Game.Player|speed", "UnityEngine.MonoBehaviour|runInEditMode" },
            root.GetProperty("blackList").EnumerateArray().Select(r => string.Join("|", Strings(r))).ToArray(), "blackList");
        Same(new[] { "Game.Player::Run(System.String)" }, Overloads(ManifestType(run, "Game.Player"), "Run", false), "Run(int) blacklisted");
    }

    const string GcmParameters = "(this global::XLua.ObjectTranslator translator, global::System.IntPtr L, int gen_param_count)";

    static IEnumerable<string> WrapperFiles(Run run)
    {
        return run.Result.GeneratedSources.Where(s => s.HintName.StartsWith("W_")).Select(s => s.SourceText.ToString());
    }

    static int Count(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // GEN_CODE_MINIMIZE (design "GCM mode as implemented"), as
    // LuaClassWrapGCM.tpl.txt and LuaEnumWrapGCM.tpl.txt: every member is a
    // private extension method of the translator with the CSharpWrapper
    // signature, registered closed over the translator, without the callback
    // attribute, the translator lookup, lua_gettop and the try/catch that the
    // single C# entry (CSharpWrapperCallerImpl) does. The binding surface and
    // the registration are those of the plain mode.
    public static void GcmWrappers()
    {
        var plain = Harness.Generate("Game", WrapperSource);
        var gcm = Harness.Generate(Harness.Compile("Game", new[] { WrapperSource }, new[] { Harness.RuntimeGcm }, Harness.GcmParse));
        Assert.Diagnostics(gcm, "XLSG004:Info");
        Assert.Compiles(gcm);
        Assert.Equal(plain.Source(Emitter.ManifestFile), gcm.Source(Emitter.ManifestFile), "the binding surface");
        Assert.Equal(plain.Source(Emitter.BindingsFile), gcm.Source(Emitter.BindingsFile), "the registration");
        int callbacks = WrapperFiles(plain).Sum(s => Count(s, "[global::XLua.MonoPInvokeCallback("));
        int members = 0;
        foreach (var s in WrapperFiles(gcm))
        {
            Assert.True(!s.Contains("MonoPInvokeCallback") && !s.Contains("lua_gettop") && !s.Contains("catch (global::System.Exception gen_e)"),
                "a GCM wrapper with per-member boilerplate:\n" + s);
            Assert.Equal(1, Count(s, "ObjectTranslatorPool.Instance.Find(L)"), "translator lookups (only __Register's)");
            foreach (var line in s.Split('\n').Where(l => l.TrimStart().StartsWith("static int ") || l.TrimStart().StartsWith("internal static int ")))
            {
                Assert.True(line.TrimStart().StartsWith("static int ") && line.EndsWith(GcmParameters), "member function " + line.Trim());
                members++;
            }
        }
        Assert.Equal(callbacks, members, "one GCM function for each plain wrapper function");
        var player = gcm.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Game_Player_")).SourceText.ToString();
        foreach (var expected in new[] { "BeginClassRegister(type, L, translator.__CreateInstance, ", "EndObjectRegister(type, L, translator, translator.__CSIndexer, translator.__NewIndexer,",
            "OBJ_META_IDX, \"__add\", translator.__AddMeta);", "METHOD_IDX, \"Foo\", translator._m_Foo", "CLS_GETTER_IDX, \"Shared\", translator._g_get_Shared);",
            "METHOD_IDX, \"Changed\", translator._e_Changed);", "if (gen_param_count == 3)", "RegisterLazyFunc(L, global::XLua.Utils.METHOD_IDX, \"Lazy\"" })
        {
            Assert.True(player.Contains(expected), "Player's GCM wrapper has no " + expected + ":\n" + player);
        }
        // An enum of more than 20 values keeps the try/catch around
        // TranslateToEnumToTop, as LuaEnumWrapGCM.tpl.txt does.
        var big = gcm.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Game_Big_")).SourceText.ToString();
        Assert.True(big.Contains("\"__CastFrom\", translator.__CastFrom);") && big.Contains("catch (global::System.Exception e)"), big);
    }

    // Delegate, interface and hotfix bridges and the GCOptimize code do not
    // change with GEN_CODE_MINIMIZE (the legacy generator uses the same
    // templates for them in both modes); they compile against the GCM runtime.
    public static void GcmLeavesBridgesAndStructsAsTheyAre()
    {
        var lib = Harness.Compile("Lib", new[] { StructLibrary });
        var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib));
        var plain = Harness.Generate(Harness.Compile("Game", new[] { StructSource }, new[] { Harness.Runtime, libRef }));
        var gcm = Harness.Generate(Harness.Compile("Game", new[] { StructSource }, new[] { Harness.RuntimeGcm, libRef }, Harness.GcmParse));
        Assert.Compiles(gcm);
        foreach (var file in new[] { Emitter.StructsFile, Emitter.PackFile, Emitter.BridgesFile, Emitter.ManifestFile })
            Assert.Equal(plain.Source(file), gcm.Source(file), file);
        var player = gcm.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_Game_Player_")).SourceText.ToString();
        Assert.True(player.Contains("Structs.Get_Game_V3_") && player.Contains(GcmParameters), "GCM wrappers read GCOptimize structs through Structs");

        plain = Harness.Generate("Game", BridgeSource);
        gcm = Harness.Generate(Harness.Compile("Game", new[] { BridgeSource }, new[] { Harness.RuntimeGcm }, Harness.GcmParse));
        Assert.Compiles(gcm);
        Assert.Equal(plain.Source(Emitter.BridgesFile), gcm.Source(Emitter.BridgesFile), "delegate bridges");
        var bridges = gcm.Result.GeneratedSources.Where(s => s.HintName.StartsWith("B_")).ToArray();
        Assert.True(bridges.Length > 0, "no interface bridges");
        foreach (var s in bridges) Assert.Equal(plain.Source(s.HintName), s.SourceText.ToString(), s.HintName);

        const string hotfix = Usings + "[Hotfix] public class Shop { public int Price(int n) { return n; } }";
        plain = Harness.Generate("Game", hotfix);
        gcm = Harness.Generate(Harness.Compile("Game", new[] { hotfix }, new[] { Harness.RuntimeGcm }, Harness.GcmParse));
        Assert.Compiles(gcm);
        Assert.True(plain.Source(Emitter.HotfixBridgesFile) != null, "no hotfix bridges");
        Assert.Equal(plain.Source(Emitter.HotfixBridgesFile), gcm.Source(Emitter.HotfixBridgesFile), "hotfix bridges");
    }

    // XLSG013: the define and XLua's mode differ (a define for one assembly
    // only, or a precompiled XLua); the wrappers follow XLua, whose
    // RegisterFunc decides what compiles.
    public static void Xlsg013GcmDefineDiffersFromXLua()
    {
        const string source = Usings + "[LuaCallCSharp] public class Player { public int Hp; }";
        var defined = Harness.Generate(Harness.Compile("Game", new[] { source }, null, Harness.GcmParse));
        Assert.Diagnostics(defined, "XLSG013:Warning");
        Assert.Compiles(defined);
        var message = defined.Result.Diagnostics[0].GetMessage();
        Assert.True(message.Contains("is on in assembly 'Game'") && message.Contains("compiled without it"), message);
        Assert.True(WrapperFiles(defined).All(s => s.Contains("MonoPInvokeCallback")), "plain wrappers for a plain XLua");

        var undefined = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { Harness.RuntimeGcm }));
        Assert.Diagnostics(undefined, "XLSG013:Warning");
        Assert.Compiles(undefined);
        message = undefined.Result.Diagnostics[0].GetMessage();
        Assert.True(message.Contains("is off in assembly 'Game'") && message.Contains("compiled with it"), message);
        Assert.True(WrapperFiles(undefined).All(s => s.Contains(GcmParameters)), "GCM wrappers for a GCM XLua");

        // No configuration, nothing generated: no warning either.
        Assert.Diagnostics(Harness.Generate(Harness.Compile("Game", new[] { "public class Empty { }" }, null, Harness.GcmParse)));
    }

    // The minimal preset uses the GEN_CODE_MINIMIZE wrappers (owner decision
    // 2026-10-19): XLua compiled with XLUA_PRESET_MINIMAL gets GEN_CODE_MINIMIZE
    // from the <xlua-preset> blocks of its files, and the generator reads the
    // same rule from the user assembly's symbols, so the two agree without
    // XLSG013. XLUA_NO_GCM keeps the plain wrappers on both sides; an explicit
    // GEN_CODE_MINIMIZE wins over it, as in the blocks.
    public static void MinimalPresetImpliesGcm()
    {
        const string source = Usings + "[LuaCallCSharp] public class Player { public int Hp; public int Heal(int n) { return Hp += n; } }";
        var minimal = Harness.RuntimeWith("XLUA_PRESET_MINIMAL");
        var run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { minimal }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        Assert.True(run.Output.GetTypeByMetadataName("XLua.CSharpWrapper") != null, "XLua with XLUA_PRESET_MINIMAL has no CSharpWrapper");
        Assert.True(WrapperFiles(run).Any() && WrapperFiles(run).All(s => s.Contains(GcmParameters) && !s.Contains("MonoPInvokeCallback")),
            "plain wrappers with XLUA_PRESET_MINIMAL:\n" + string.Join("\n", WrapperFiles(run)));
        // The same wrappers as with GEN_CODE_MINIMIZE alone.
        var gcm = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { Harness.RuntimeGcm }, Harness.GcmParse));
        Assert.Equal(string.Join("\n", WrapperFiles(gcm)), string.Join("\n", WrapperFiles(run)), "the minimal preset's wrappers");

        var noGcm = Harness.RuntimeWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM");
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { noGcm }, Harness.ParseWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM")));
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        Assert.True(run.Output.GetTypeByMetadataName("XLua.CSharpWrapper") == null, "XLua with XLUA_NO_GCM declares CSharpWrapper");
        Assert.True(WrapperFiles(run).Any() && WrapperFiles(run).All(s => s.Contains("MonoPInvokeCallback") && !s.Contains(GcmParameters)),
            "GCM wrappers with XLUA_NO_GCM");
        var plain = Harness.Generate("Game", source);
        Assert.Equal(string.Join("\n", WrapperFiles(plain)), string.Join("\n", WrapperFiles(run)), "the opt-out's wrappers");

        // GEN_CODE_MINIMIZE defined as well: the mode is on on both sides.
        var both = Harness.RuntimeWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM", "GEN_CODE_MINIMIZE");
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { both },
            Harness.ParseWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM", "GEN_CODE_MINIMIZE")));
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        Assert.True(WrapperFiles(run).All(s => s.Contains(GcmParameters)), "plain wrappers with GEN_CODE_MINIMIZE and XLUA_NO_GCM");

        // XLUA_NO_GCM alone, without the preset, changes nothing.
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, null, Harness.ParseWith("XLUA_NO_GCM")));
        Assert.Diagnostics(run);
        Assert.True(WrapperFiles(run).All(s => s.Contains("MonoPInvokeCallback")), "GCM wrappers with XLUA_NO_GCM alone");
    }

    // XLSG013 follows the same rule: the preset in one assembly only, or the
    // opt-out in one assembly only, splits the modes, and the wrappers follow XLua.
    public static void Xlsg013FollowsTheMinimalPreset()
    {
        const string source = Usings + "[LuaCallCSharp] public class Player { public int Hp; }";
        var minimal = Harness.RuntimeWith("XLUA_PRESET_MINIMAL");
        var noGcm = Harness.RuntimeWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM");

        // The preset in the user assembly, a legacy XLua.
        var run = Harness.Generate(Harness.Compile("Game", new[] { source }, null, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Diagnostics(run, "XLSG013:Warning");
        Assert.Compiles(run);
        var message = run.Result.Diagnostics[0].GetMessage();
        Assert.True(message.Contains("is on in assembly 'Game'") && message.Contains("compiled without it") && message.Contains("XLUA_NO_GCM"), message);
        Assert.True(WrapperFiles(run).All(s => s.Contains("MonoPInvokeCallback")), "plain wrappers for a plain XLua");

        // A minimal XLua, a user assembly without the preset.
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { minimal }));
        Assert.Diagnostics(run, "XLSG013:Warning");
        Assert.Compiles(run);
        message = run.Result.Diagnostics[0].GetMessage();
        Assert.True(message.Contains("is off in assembly 'Game'") && message.Contains("compiled with it"), message);
        Assert.True(WrapperFiles(run).All(s => s.Contains(GcmParameters)), "GCM wrappers for a minimal XLua");

        // The opt-out in the user assembly only.
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { minimal }, Harness.ParseWith("XLUA_PRESET_MINIMAL", "XLUA_NO_GCM")));
        Assert.Diagnostics(run, "XLSG013:Warning");
        Assert.Compiles(run);
        Assert.True(WrapperFiles(run).All(s => s.Contains(GcmParameters)), "GCM wrappers for a minimal XLua");

        // The opt-out in XLua only.
        run = Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { noGcm }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Diagnostics(run, "XLSG013:Warning");
        Assert.Compiles(run);
        Assert.True(WrapperFiles(run).All(s => s.Contains("MonoPInvokeCallback")), "plain wrappers for an XLua with XLUA_NO_GCM");
    }

    // XLSG014 (owner decision 2026-10-28): in strict minimal (XLUA_NO_REFLECTION,
    // or XLUA_PRESET_MINIMAL without XLUA_REFLECTION) a [GCOptimize] struct
    // without [LuaCallCSharp] has a pusher but no wrapper, so Lua reading its
    // fields fails only at run time ("has no generated bindings").
    const string StrictStructSource = Usings + @"
[assembly: GCOptimize(typeof(Game.Ext))]
[assembly: LuaCallCSharp(typeof(Game.Listed))]
namespace Game
{
    [GCOptimize] public struct Point { public int X, Y; }
    public struct Ext { public float A; }
    [GCOptimize, LuaCallCSharp] public struct Bound { public int X; }
    [GCOptimize] public struct Listed { public int X; }
    [GCOptimize(OptimizeFlag.PackAsTable)] public struct AsTable { public int X; }
    [GCOptimize(OptimizeFlag.PackAsTable)] public struct Boxed { public int X; public string Name; }
    [GCOptimize] public enum Mode { A, B }
    [LuaCallCSharp] public class Api { public Point P; public Ext E; public AsTable T; public Boxed B; public Mode M; }
}";

    public static void Xlsg014GCOptimizeStructWithoutBindings()
    {
        Func<Run, string[]> unbound = r => r.Result.Diagnostics.Where(d => d.Id == "XLSG014")
            .Select(d => d.GetMessage()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        // XLUA_NO_REFLECTION alone, against a legacy XLua (no XLSG013).
        var run = Harness.Generate(Harness.Compile("Game", new[] { StrictStructSource }, null, Harness.ParseWith("XLUA_NO_REFLECTION")));
        Assert.Diagnostics(run, "XLSG010:Warning", "XLSG014:Warning", "XLSG014:Warning", "XLSG014:Warning");
        Assert.Compiles(run);
        var messages = unbound(run);
        Assert.True(messages[0].StartsWith("GCOptimize struct 'Game.Boxed' has no generated bindings") && messages[0].Contains("(X, Name)"), messages[0]);
        Assert.True(messages[1].StartsWith("GCOptimize struct 'Game.Ext'") && messages[1].Contains("(A)")
            && messages[1].Contains("[assembly: XLua.LuaCallCSharp(typeof(Game.Ext))]"), messages[1]);
        Assert.True(messages[2].StartsWith("GCOptimize struct 'Game.Point'") && messages[2].Contains("(X, Y)")
            && messages[2].Contains("Mark it [LuaCallCSharp]") && messages[2].Contains("XLUA_NO_REFLECTION"), messages[2]);

        // The minimal preset implies XLUA_NO_REFLECTION, as the <xlua-preset> blocks do.
        var minimal = Harness.RuntimeWith("XLUA_PRESET_MINIMAL");
        run = Harness.Generate(Harness.Compile("Game", new[] { StrictStructSource }, new[] { minimal }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.Diagnostics(run, "XLSG010:Warning", "XLSG014:Warning", "XLSG014:Warning", "XLSG014:Warning");
        Assert.Compiles(run);

        // XLUA_REFLECTION keeps the reflection fallback in the preset: no warning.
        var reflection = Harness.RuntimeWith("XLUA_PRESET_MINIMAL", "XLUA_REFLECTION");
        run = Harness.Generate(Harness.Compile("Game", new[] { StrictStructSource }, new[] { reflection },
            Harness.ParseWith("XLUA_PRESET_MINIMAL", "XLUA_REFLECTION")));
        Assert.Diagnostics(run, "XLSG010:Warning");

        // Legacy: reflection reads the fields.
        Assert.Diagnostics(Harness.Generate("Game", StrictStructSource), "XLSG010:Warning");

        // Bound by a referenced assembly's generated bindings: no warning there,
        // and the referenced assembly reports its own structs, not this one.
        var lib = Harness.Generate(Harness.Compile("Lib", new[] { Usings + "namespace Lib { [LuaCallCSharp] public struct Vec { public int X; } }" },
            null, Harness.ParseWith("XLUA_NO_REFLECTION")));
        Assert.Diagnostics(lib);
        var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib.Output));
        run = Harness.Generate(Harness.Compile("Game", new[] { Usings + "[assembly: GCOptimize(typeof(Lib.Vec))]\n[LuaCallCSharp] public class Own { public Lib.Vec V; }" },
            new[] { Harness.Runtime, libRef }, Harness.ParseWith("XLUA_NO_REFLECTION")));
        Assert.Diagnostics(run);
        Assert.Compiles(run);

        // Not in xLua's own assembly: its UnityEngine defaults (GenAttributes.cs) are
        // xLua's configuration, which the project cannot bind there.
        var project = Path.Combine(Program.Repo, "General/vs2013/XLua.Mini.csproj");
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var parse = Harness.ParseWith("XLUA_GENERAL", "NET5_0_OR_GREATER", "XLUA_NO_REFLECTION");
        var trees = XDocument.Load(project).Descendants(ns + "Compile")
            .Select(c => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), c.Attribute("Include").Value.Replace('\\', '/'))))
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f))
            .Append(CSharpSyntaxTree.ParseText("[assembly: XLua.GCOptimize(typeof(RuntimeVec))]\npublic struct RuntimeVec { public int X; }", parse, "/src/Defaults.cs"));
        var runtimeItself = CSharpCompilation.Create("XLua", trees, Harness.Bcl, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        run = Harness.Generate(runtimeItself);
        Assert.True(!run.Result.Diagnostics.Any(d => d.Id == "XLSG014"), "XLSG014 in xLua's own assembly: "
            + string.Join(", ", run.Result.Diagnostics.Select(d => d.GetMessage())));
    }

    // The model cache (types from metadata) keeps the two modes apart.
    public static void GcmModelsAreCachedApart()
    {
        const string source = Usings + "[assembly: LuaCallCSharp(typeof(System.Text.StringBuilder))]";
        Func<Run, string> builder = r => r.Result.GeneratedSources.Single(s => s.HintName.StartsWith("W_System_Text_StringBuilder_")).SourceText.ToString();
        var a = builder(Harness.Generate("Game", source));
        var b = builder(Harness.Generate(Harness.Compile("Game", new[] { source }, new[] { Harness.RuntimeGcm }, Harness.GcmParse)));
        var c = builder(Harness.Generate("Game", source));
        Assert.True(a.Contains("MonoPInvokeCallback") && !b.Contains("MonoPInvokeCallback") && b.Contains(GcmParameters), "the cached model of the other mode");
        Assert.Equal(a, c, "the plain model again");
    }

    public static void BuiltAgainstRoslyn431()
    {
        var allowed = new Dictionary<string, Version>
        {
            { "Microsoft.CodeAnalysis", new Version(4, 3, 0, 0) },
            { "Microsoft.CodeAnalysis.CSharp", new Version(4, 3, 0, 0) },
            { "System.Collections.Immutable", new Version(6, 0, 0, 0) },
            { "netstandard", new Version(2, 0, 0, 0) },
        };
        foreach (var reference in typeof(XLuaBindingGenerator).Assembly.GetReferencedAssemblies())
        {
            Assert.True(allowed.ContainsKey(reference.Name), "unexpected reference " + reference.FullName);
            Assert.True(reference.Version <= allowed[reference.Name], "too new: " + reference.FullName);
        }
    }

    // Tencent/xLua#918: parameters, fields and properties of a UnityEngine.Object
    // type that Lua sets are read with GetUnityObjectArg, which refuses a table
    // (ObjectCasters.RefusesTables); results C# reads from Lua and other classes
    // keep GetObject. Tencent/xLua#751: the struct table readers count the
    // fields a table sets and call Utils.StructTableSetNoField when it set none.
    public static void UnityObjectArgumentsAndStructTables()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace UnityEngine { public class Object { } public class GameObject : Object { } }
namespace Game
{
    [LuaCallCSharp] public class Spawner
    {
        public static void Place(UnityEngine.GameObject go) { }
        public static UnityEngine.GameObject Last;
        public static void Plain(Spawner s) { }
        public static void Hit(HitInfo h) { }
    }
    [CSharpCallLua] public delegate UnityEngine.GameObject MakeGameObject();
    [GCOptimize] public struct HitInfo { public float x, y; }
}");
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        string all = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        int refusing = all.Split(new[] { ".GetUnityObjectArg(L, " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(2, refusing, "GetUnityObjectArg for Place(go) and the Last setter:\n" + all);
        Assert.True(all.Contains("GetObject(L, 1, typeof(global::Game.Spawner))") || all.Contains("GetObject(L, 1, typeof(Game.Spawner))"),
            "Plain(Spawner) keeps GetObject");
        Assert.True(all.Contains("StructTableSetNoField(typeof("), "the HitInfo table reader checks the fields it set");
        Assert.True(all.Contains("__xlua_set++;"), "the HitInfo table reader counts the fields it set");
    }

    // A struct parameter or field that Lua sets is read with
    // BindingApi.GetStructArg, which raises for nil or a number as the emitted
    // and reflection wrappers do (owner decision of 2026-10-28); so is an enum
    // parameter without a generated reader since owner decision Q34.
    // Nullable<T>, decimal and the results C# reads keep translator.Get. The
    // packed GCOptimize reader raises BindingApi.StructExpected in its last
    // branch.
    public static void StructArgumentsRaiseForNil()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace Game
{
    public struct Plain { public int X; }
    [LuaCallCSharp] public class User
    {
        public static int Read(Plain p) { return p.X; }
        public static Plain Field;
        public static int Maybe(Plain? p) { return 0; }
        public static int Day(System.DayOfWeek d) { return 0; }
        public static int Money(decimal m) { return 0; }
        public static int Hit(HitInfo h) { return 0; }
    }
    [CSharpCallLua] public delegate Plain MakePlain();
    [GCOptimize] public struct HitInfo { public float x, y; }
}");
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        string all = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        int strict = all.Split(new[] { "BindingApi.GetStructArg(translator, L, " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(3, strict, "GetStructArg for Read(p), the Field setter and Day(d):\n" + all);
        Assert.True(all.Contains("BindingApi.StructExpected(L, index, typeof("), "the packed HitInfo reader raises StructExpected");
        Assert.True(all.Contains("Structs.Get_"), "Hit(h) keeps the generated HitInfo reader");
    }

    // Owner decisions Q59 and Q62 of 2026-10-28: a Nullable<S> argument of a
    // strict struct (a parameter or a field setter) is read with
    // BindingApi.GetNullableStructArg, which gives null for nil and raises
    // for a value that is no S, as the emitted and reflection wrappers read
    // it, also when S has a generated GCOptimize reader. Before Q59 it was
    // translator.Get, between Q59 and Q62 GetObject. A Nullable<S> result
    // keeps translator.Get (Q59), and a Nullable<int> argument too.
    public static void NullableStructArgumentsRaiseForOtherKinds()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace Game
{
    public struct Plain { public int X; }
    [LuaCallCSharp] public class User
    {
        public static int Maybe(Plain? p) { return 0; }
        public static int MaybeHit(HitInfo? h) { return 0; }
        public static int Count(int? n) { return 0; }
        public static Plain? Field;
    }
    [CSharpCallLua] public delegate Plain? MakeMaybe();
    [GCOptimize] public struct HitInfo { public float x, y; }
}");
        Assert.Diagnostics(run);
        Assert.Compiles(run);
        string all = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        int nullable = all.Split(new[] { "BindingApi.GetNullableStructArg(translator, L, " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(3, nullable, "GetNullableStructArg for Maybe(p), MaybeHit(h) and the Field setter:\n" + all);
        Assert.True(!all.Contains(")translator.GetObject(L, 1, typeof("), "no Nullable<S> argument is read with GetObject:\n" + all);
        int get = all.Split(new[] { "translator.Get(L, " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(2, get, "translator.Get for Count(n) and the MakeMaybe result:\n" + all);
    }

    // Owner decisions Q34 to Q36 of 2026-10-28. The generated reader of a
    // [LuaCallCSharp] enum raises StructExpected for an argument that is no
    // number or value name; the readers take `result` and give default(T) for
    // a struct result where Lua has no value: the bridges and the registration
    // (ObjectTranslator.Get<T>, GetObject's caster) pass true, arguments do not;
    // another type's userdata raises StructExpected instead of "invalid
    // userdata for T", and the table reader checks the C# object's type.
    public static void EnumArgumentsAndStructResults()
    {
        var run = Harness.Generate("Game", Usings + @"
namespace Game
{
    [LuaCallCSharp] public enum Mode { A, B }
    [LuaCallCSharp] public class User
    {
        public static int Pick(Mode m) { return 0; }
        public static int Hit(HitInfo h) { return 0; }
        public static int Named(Labeled l) { return 0; }
    }
    [CSharpCallLua] public delegate HitInfo MakeHit();
    [CSharpCallLua] public delegate Labeled MakeLabeled();
    [CSharpCallLua] public delegate Mode MakeMode();
    [GCOptimize] public struct HitInfo { public float x, y; }
    [GCOptimize] public struct Labeled { public int X; public string Name; }
}");
        Assert.Diagnostics(run, "XLSG010:Warning");
        Assert.Compiles(run);
        string all = string.Join("\n", run.Result.GeneratedSources.Select(s => s.SourceText.ToString()));
        int expected = all.Split(new[] { "throw global::XLua.Binding.BindingApi.StructExpected(L, index, typeof(" }, StringSplitOptions.None).Length - 1;
        Assert.True(expected >= 4, "StructExpected in the last branches and the table reader's type check (another type's packed userdata: BindingApi.StructBuffer):\n" + all);
        int readers = all.Split(new[] { ", bool result = false)" }, StringSplitOptions.None).Length - 1;
        Assert.Equal(3, readers, "Mode, HitInfo and Labeled readers take result:\n" + all);
        int results = all.Split(new[] { ", true);" }, StringSplitOptions.None).Length - 1;
        Assert.True(results >= 3, "the three bridges read their results with result: true:\n" + all);
        Assert.True(all.Contains("(translator, L, index, out val, true);"), "Value_ and StructOp.Get, which the registration uses, read results:\n" + all);
        Assert.True(all.Contains("if (!result && type != "), "the Mode reader raises for an argument that is no number or name");
        Assert.True(all.Contains("if (result && (type == "), "a struct result where Lua has no value is default(T)");
        Assert.True(all.Contains(" is global::Game.Labeled)) throw ") || all.Contains(" is Game.Labeled)) throw "),
            "the Labeled table reader checks the C# object's type:\n" + all);
        Assert.True(!all.Contains("BindingApi.GetStructArg("), "the three argument types have generated readers");
    }

    // XLSG014 for the GCOptimize structs of a referenced assembly (owner
    // decision Q41 of 2026-10-28), such as xLua's UnityEngine defaults
    // (GenAttributes.cs): in strict minimal, a member this assembly binds that
    // passes one warns, naming the member and the attribute to add; nothing
    // that exposes it, a [LuaCallCSharp] on it, or reflection keeps it quiet.
    public static void Xlsg014ReferencedDefaultStructs()
    {
        var defaults = Harness.Generate(Harness.Compile("Defaults", new[] { Usings + @"
[assembly: XLua.GCOptimize(typeof(UnityEngine.Vector3), typeof(UnityEngine.Color))]
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public struct Color { public float r, g, b, a; }
}" }));
        var defaultsRef = MetadataReference.CreateFromImage(Harness.Emit(defaults.Output));
        var noReflection = Harness.ParseWith("XLUA_NO_REFLECTION");
        Func<string, CSharpParseOptions, Run> game = (source, parse) => Harness.Generate(
            Harness.Compile("Game", new[] { Usings + source }, new[] { Harness.Runtime, defaultsRef }, parse));
        Func<Run, string[]> xlsg014 = r => r.Result.Diagnostics.Where(d => d.Id == "XLSG014")
            .Select(d => d.GetMessage()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        const string exposes = @"
namespace Game
{
    [LuaCallCSharp] public class Mover
    {
        public static UnityEngine.Vector3 Where() { return default(UnityEngine.Vector3); }
        public UnityEngine.Vector3 Target;
        public void Go(UnityEngine.Vector3[] path) { }
    }
    [LuaCallCSharp] public class Painter { public UnityEngine.Color? Tint { get; set; } }
}";
        var run = game(exposes, noReflection);
        Assert.Diagnostics(run, "XLSG014:Warning", "XLSG014:Warning");
        Assert.Compiles(run);
        var messages = xlsg014(run);
        Assert.True(messages[0].StartsWith("GCOptimize struct 'UnityEngine.Color', configured by assembly 'Defaults', has no generated bindings")
            && messages[0].Contains("'Game.Painter.Tint'") && messages[0].Contains("(r, g, b, a)")
            && messages[0].Contains("[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Color))]"), messages[0]);
        Assert.True(messages[1].StartsWith("GCOptimize struct 'UnityEngine.Vector3'") && messages[1].Contains("'Game.Mover.")
            && messages[1].Contains("(x, y, z)") && messages[1].Contains("[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Vector3))]"), messages[1]);

        // The minimal preset implies XLUA_NO_REFLECTION.
        var minimal = Harness.RuntimeWith("XLUA_PRESET_MINIMAL");
        run = Harness.Generate(Harness.Compile("Game", new[] { Usings + exposes }, new[] { minimal, defaultsRef }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.True(xlsg014(run).Length == 2, "minimal preset: " + string.Join(", ", xlsg014(run)));

        // Bound by this assembly: quiet for that struct.
        run = game("[assembly: LuaCallCSharp(typeof(UnityEngine.Vector3))]\n" + exposes, noReflection);
        messages = xlsg014(run);
        Assert.True(messages.Length == 1 && messages[0].Contains("'UnityEngine.Color'"), string.Join(", ", messages));
        Assert.Compiles(run);

        // Nothing exposes them: quiet.
        run = game("namespace Game { [LuaCallCSharp] public class Plain { public int N; public string Name(int x) { return null; } } }", noReflection);
        Assert.Diagnostics(run);

        // Reflection reads the fields: quiet.
        Assert.Diagnostics(game(exposes, Harness.Parse));
    }

    // XLSG014 for [CSharpCallLua] delegates and interfaces (owner decision Q58
    // of 2026-10-28): in strict minimal, a delegate's or interface's parameter
    // or result of a [GCOptimize] struct that nothing binds warns, naming the
    // delegate or interface member and the attribute to add, for a struct of a
    // referenced assembly (xLua's Vector3) and for the project's own struct;
    // quiet once everything is bound and with reflection.
    public static void Xlsg014CallLuaStructs()
    {
        var defaults = Harness.Generate(Harness.Compile("Defaults", new[] { Usings + @"
[assembly: XLua.GCOptimize(typeof(UnityEngine.Vector3), typeof(UnityEngine.Color), typeof(UnityEngine.Rect))]
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public struct Color { public float r, g, b, a; }
    public struct Rect { public float x, y, width, height; }
}" }));
        var defaultsRef = MetadataReference.CreateFromImage(Harness.Emit(defaults.Output));
        var noReflection = Harness.ParseWith("XLUA_NO_REFLECTION");
        Func<string, CSharpParseOptions, Run> game = (source, parse) => Harness.Generate(
            Harness.Compile("Game", new[] { Usings + source }, new[] { Harness.Runtime, defaultsRef }, parse));
        Func<Run, string[]> xlsg014 = r => r.Result.Diagnostics.Where(d => d.Id == "XLSG014")
            .Select(d => d.GetMessage()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        // A delegate result (Vector3), a delegate parameter array (Rect[]), an
        // interface property (Color?) and a project struct that an interface
        // method takes. Nothing binds any of them.
        const string passes = @"
namespace Game
{
    [GCOptimize] public struct Hit { public int Damage; }
    [CSharpCallLua] public delegate UnityEngine.Vector3 Aim(int target);
    [CSharpCallLua] public delegate void Layout(UnityEngine.Rect[] areas);
    [CSharpCallLua] public interface IShip
    {
        UnityEngine.Color? Tint { get; }
        void Take(Hit hit);
    }
}";
        var run = game(passes, noReflection);
        Assert.Diagnostics(run, "XLSG014:Warning", "XLSG014:Warning", "XLSG014:Warning", "XLSG014:Warning");
        Assert.Compiles(run);
        var messages = xlsg014(run);
        Assert.True(messages[0].StartsWith("GCOptimize struct 'Game.Hit' has no generated bindings")
            && messages[0].Contains("(the [CSharpCallLua] interface member 'Game.IShip.Take' passes them)")
            && messages[0].Contains("(Damage)") && messages[0].Contains("Mark it [LuaCallCSharp]")
            && messages[0].Contains("[assembly: XLua.LuaCallCSharp(typeof(Game.Hit))]"), messages[0]);
        Assert.True(messages[1].StartsWith("GCOptimize struct 'UnityEngine.Color', configured by assembly 'Defaults'")
            && messages[1].Contains("the [CSharpCallLua] interface member 'Game.IShip.Tint' passes such values")
            && messages[1].Contains("(r, g, b, a)") && messages[1].Contains("[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Color))]"), messages[1]);
        Assert.True(messages[2].StartsWith("GCOptimize struct 'UnityEngine.Rect'")
            && messages[2].Contains("the [CSharpCallLua] delegate 'Game.Layout' passes such values")
            && messages[2].Contains("[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Rect))]"), messages[2]);
        Assert.True(messages[3].StartsWith("GCOptimize struct 'UnityEngine.Vector3'")
            && messages[3].Contains("the [CSharpCallLua] delegate 'Game.Aim' passes such values")
            && messages[3].Contains("(x, y, z)") && messages[3].Contains("[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Vector3))]"), messages[3]);

        // The minimal preset implies XLUA_NO_REFLECTION.
        var minimal = Harness.RuntimeWith("XLUA_PRESET_MINIMAL");
        run = Harness.Generate(Harness.Compile("Game", new[] { Usings + passes }, new[] { minimal, defaultsRef }, Harness.ParseWith("XLUA_PRESET_MINIMAL")));
        Assert.True(xlsg014(run).Length == 4, "minimal preset: " + string.Join(", ", xlsg014(run)));

        // A member this assembly binds is named before a delegate.
        run = game("namespace Game { [LuaCallCSharp] public class Mover { public UnityEngine.Vector3 Target; } }\n" + passes, noReflection);
        messages = xlsg014(run);
        Assert.True(messages.Length == 4 && messages[3].Contains("'Game.Mover.Target' passes such values"), string.Join(", ", messages));

        // Everything bound: quiet.
        run = game("[assembly: LuaCallCSharp(typeof(UnityEngine.Vector3), typeof(UnityEngine.Color), typeof(UnityEngine.Rect), typeof(Game.Hit))]\n"
            + passes, noReflection);
        Assert.Diagnostics(run);
        Assert.Compiles(run);

        // An own struct that no delegate or interface passes keeps its message.
        run = game("namespace Game { [GCOptimize] public struct Lone { public int N; } [CSharpCallLua] public delegate int Count(string s); }", noReflection);
        messages = xlsg014(run);
        Assert.True(messages.Length == 1 && messages[0].Contains("without boxing, but"), string.Join(", ", messages));

        // Reflection reads the fields: quiet.
        Assert.Diagnostics(game(passes, Harness.Parse));
    }

    // The assembly-level configuration of NameTableSource with several types per
    // attribute (owner decision, 2026-10-18), in the same order.
    static readonly string NameTableSourceGrouped = Usings + @"
[assembly: LuaCallCSharp(typeof(List<int>), typeof(Dictionary<string, List<int>>), typeof(Game.Outer.Inner),
    typeof(Game.Outer<int>.Inner), typeof(Game.Outer<int>.Deep<string>), typeof(int[]), typeof(System.Text.StringBuilder))]
[assembly: CSharpCallLua(typeof(Action<float>), typeof(Func<int, (int, string)>))]
[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(Game.Pair))]
[assembly: ReflectionUse(new Type[] { typeof(Game.Settings) })]
[assembly: Hotfix(typeof(Game.Pool<>))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop))]
[assembly: LuaCallCSharp(typeof(Game.Player))]
" + NameTableSource.Substring(NameTableSource.IndexOf("namespace Game", StringComparison.Ordinal));

    static string AllSources(Run run)
    {
        return string.Join("\n", run.Result.GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal)
            .Select(s => "// " + s.HintName + "\n" + s.SourceText));
    }

    // [assembly: X(typeof(A), typeof(B))] configures what one attribute per type
    // does: the same sources (bindings, wrappers, bridges, pushers, hotfix
    // bridges, the manifest) and the same diagnostics.
    public static void MultiTypeAttributesEqualOneTypePerAttribute()
    {
        var one = Harness.Generate("Game", NameTableSource);
        var several = Harness.Generate("Game", NameTableSourceGrouped);
        Assert.Compiles(several);
        Assert.Diagnostics(several, "XLSG004:Info");
        Assert.Equal(AllSources(one), AllSources(several), "generated sources");

        // A problem of one type in the list points at its own typeof.
        var open = Harness.Generate("Game", Usings + "[assembly: LuaCallCSharp(typeof(Game.A), typeof(List<>), typeof(Game.B))]\nnamespace Game { public class A { } public class B { } }");
        Assert.Diagnostics(open, "XLSG001:Warning");
        var span = open.Result.Diagnostics[0].Location.SourceSpan;
        Assert.Equal("typeof(List<>)", open.Input.SyntaxTrees.First().GetText().ToString(span), "XLSG001 location");
        Assert.True(open.Source(Emitter.BindingsFile).Contains("Game.A") && open.Source(Emitter.BindingsFile).Contains("Game.B"), "the other types of the list are bound");
    }

    // Which constructor C# binds for each form: the ones without an array
    // whenever they fit, so code compiled before the array forms keeps its
    // meaning (and its constructor reference).
    public static void MultiTypeConstructorOverloads()
    {
        var c = Harness.Compile("Game", new[] { Usings + @"
[assembly: LuaCallCSharp(typeof(A))]
[assembly: LuaCallCSharp(typeof(A), typeof(B))]
[assembly: CSharpCallLua(typeof(D))]
[assembly: CSharpCallLua(typeof(D), typeof(I))]
[assembly: ReflectionUse(typeof(A))]
[assembly: ReflectionUse(typeof(A), typeof(B))]
[assembly: GCOptimize(typeof(S))]
[assembly: GCOptimize(typeof(S), OptimizeFlag.PackAsTable)]
[assembly: GCOptimize(typeof(S), typeof(T))]
[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(S))]
[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(S), typeof(T))]
[assembly: Hotfix(typeof(A))]
[assembly: Hotfix(typeof(A), HotfixFlag.IntKey)]
[assembly: Hotfix(typeof(A), typeof(B))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(A), typeof(B))]
[LuaCallCSharp] [CSharpCallLua] [ReflectionUse] [GCOptimize] [Hotfix] public class A { }
[LuaCallCSharp(GenFlag.No)] [GCOptimize(OptimizeFlag.PackAsTable)] [Hotfix(HotfixFlag.Inline)] public class B { }
public delegate void D(); public interface I { } public struct S { public int a; } public struct T { public int b; }
" });
        var errors = c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.ToString())));
        Func<AttributeData, string> describe = a => a.AttributeClass.Name.Replace("Attribute", "") + "("
            + string.Join(", ", a.AttributeConstructor.Parameters.Select(p => (p.IsParams ? "params " : "") + (p.Type is IArrayTypeSymbol arr ? arr.ElementType.Name + "[]" : p.Type.Name))) + ")";
        Same(new[]
        {
            "LuaCallCSharp(Type)", "LuaCallCSharp(params Type[])",
            "CSharpCallLua(Type)", "CSharpCallLua(params Type[])",
            "ReflectionUse(Type)", "ReflectionUse(params Type[])",
            "GCOptimize(Type, OptimizeFlag)", "GCOptimize(Type, OptimizeFlag)", "GCOptimize(params Type[])",
            "GCOptimize(OptimizeFlag, params Type[])", "GCOptimize(OptimizeFlag, params Type[])",
            "Hotfix(Type, HotfixFlag)", "Hotfix(Type, HotfixFlag)", "Hotfix(params Type[])", "Hotfix(HotfixFlag, params Type[])",
        }, c.Assembly.GetAttributes().Where(a => a.AttributeClass.ContainingNamespace.Name == "XLua").Select(describe).ToArray(), "assembly-level constructors");
        Same(new[] { "LuaCallCSharp(GenFlag)", "CSharpCallLua()", "ReflectionUse()", "GCOptimize(OptimizeFlag)", "Hotfix(HotfixFlag)" },
            c.GetTypeByMetadataName("A").GetAttributes().Select(describe).ToArray(), "[X] on a type");
        Same(new[] { "LuaCallCSharp(GenFlag)", "GCOptimize(OptimizeFlag)", "Hotfix(HotfixFlag)" },
            c.GetTypeByMetadataName("B").GetAttributes().Select(describe).ToArray(), "[X(flags)] on a type");
        // The one source form the array constructors break (MIGRATING.md): a
        // null literal fits Type and Type[] alike.
        var ambiguous = Harness.Compile("Game", new[] { Usings + "[LuaCallCSharp(null)] public class C { }" });
        Same(new[] { "CS0121" }, ambiguous.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray(),
            "[LuaCallCSharp(null)]");
    }

    // A referenced assembly's [assembly: GCOptimize(...)] in either form: the
    // referencing assembly writes the same pushers.
    public static void MultiTypeGCOptimizeOfAReference()
    {
        Func<string, string> generate = config =>
        {
            var lib = Harness.Compile("Lib", new[] { Usings + config + @"
namespace Lib { public struct P { public int a; } public struct Q { public float x, y; } public struct R { public int r; } }" });
            var libRef = MetadataReference.CreateFromImage(Harness.Emit(lib));
            var run = Harness.Generate(Harness.Compile("Game", new[] { Usings + @"
[LuaCallCSharp] public class User { public void Use(Lib.P p, Lib.Q q, Lib.R r) { } }" }, new[] { Harness.Runtime, libRef }));
            Assert.Compiles(run);
            return AllSources(run);
        };
        string one = generate("[assembly: GCOptimize(typeof(Lib.P))]\n[assembly: GCOptimize(typeof(Lib.Q))]\n[assembly: GCOptimize(typeof(Lib.R), OptimizeFlag.PackAsTable)]\n");
        string several = generate("[assembly: GCOptimize(typeof(Lib.P), typeof(Lib.Q))]\n[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(Lib.R))]\n");
        Assert.True(one.Contains("Push_Lib_P_") && one.Contains("Push_Lib_Q_") && one.Contains("Lib_R_"), "the reference's GCOptimize types are written:\n" + one);
        Assert.Equal(one, several, "generated sources");
    }
}

// Generator time and compile time by number of bound types (design section 7,
// "Measurement in M2"): XLua.SourceGenerator.Tests <repo> --timing 100 500 2000
// (--timing-gcm: the same with GEN_CODE_MINIMIZE).
// The types are public, non-generic, non-obsolete classes, structs, interfaces
// and enums of the .NET reference assemblies, in name order (a stand-in for a
// UnityEngine list, which needs a Unity install). It also shows whether the
// wrappers compile for that many types the suite does not configure.
static class Timing
{
    static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers()) yield return t;
        foreach (var n in ns.GetNamespaceMembers())
            foreach (var t in Types(n)) yield return t;
    }

    static double Ms(System.Diagnostics.Stopwatch w)
    {
        return w.Elapsed.TotalMilliseconds;
    }

    public static int Run(int[] counts, bool gcm)
    {
        var probe = Harness.Compile("Probe", new[] { "class P { }" });
        var candidates = probe.SourceModule.ReferencedAssemblySymbols
            .Where(a => a.Name.StartsWith("System.") || a.Name == "netstandard" || a.Name == "mscorlib")
            .SelectMany(a => Types(a.GlobalNamespace))
            .Where(t => t.DeclaredAccessibility == Accessibility.Public && !t.IsGenericType && t.TypeKind != TypeKind.Delegate
                && !t.IsRefLikeType && t.ContainingType == null && !t.GetAttributes().Any(x => x.AttributeClass.Name == "ObsoleteAttribute")
                && !t.ContainingNamespace.ToDisplayString().StartsWith("System.Runtime.Intrinsics")
                && (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct || t.TypeKind == TypeKind.Interface || t.TypeKind == TypeKind.Enum))
            .Select(t => t.ToDisplayString()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Console.WriteLine("candidate types: " + candidates.Length);
        int failed = 0;
        foreach (int n in counts)
        {
            var list = candidates.Take(n).ToArray();
            string config = "using XLua;\n" + string.Join("\n", list.Select(t => "[assembly: LuaCallCSharp(typeof(global::" + t + "))]"));
            var input = Harness.Compile("Timing", new[] { config, "public class Edited { public int a; }" },
                gcm ? new[] { Harness.RuntimeGcm } : null, gcm ? Harness.GcmParse : null);
            // The generator alone (RunGenerators, no binding of the output):
            // with an empty model cache, then a new driver with the cache
            // filled (a later compilation in Unity's compiler server), then
            // the same driver after an unrelated edit (an IDE).
            Wrappers.ClearCache();
            var w = System.Diagnostics.Stopwatch.StartNew();
            var driver = Harness.Driver().RunGenerators(input);
            double cold = Ms(w);
            w.Restart();
            var driver2 = Harness.Driver().RunGenerators(input);
            double warm = Ms(w);
            var tree = input.SyntaxTrees.ElementAt(1);
            var edited = input.ReplaceSyntaxTree(tree, tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From("public class Edited { public int b; }")));
            int hits = Wrappers.CacheHits;
            w.Restart();
            var driver3 = driver2.RunGenerators(edited);
            double edit = Ms(w);
            hits = Wrappers.CacheHits - hits;
            var steps = driver3.GetRunResult().Results.Single().TrackedSteps;
            double model = steps.ContainsKey("Wrappers") ? steps["Wrappers"].Sum(s => s.ElapsedTime.TotalMilliseconds) : -1;
            var first = Harness.Generate(input);
            int lines = first.Result.GeneratedSources.Sum(g => g.SourceText.Lines.Count);
            var errors = first.OutputDiagnostics.ToArray();
            // A whole compile of the output (bind, check and emit), as csc does after the generator.
            var fresh = CSharpCompilation.Create("Timing", first.Output.SyntaxTrees, first.Output.References, (CSharpCompilationOptions)first.Output.Options);
            w.Restart();
            using (var ms = new MemoryStream()) fresh.Emit(ms);
            double compile = Ms(w);
            var plain = CSharpCompilation.Create("Timing", input.SyntaxTrees, input.References, input.Options);
            w.Restart();
            using (var ms = new MemoryStream()) plain.Emit(ms);
            double without = Ms(w);
            Console.WriteLine(string.Format("XLUA_SOURCEGEN_TIMING types={0} wrappers={1} lines={2} generator_first_ms={3:F0} generator_fresh_driver_ms={4:F0} "
                + "generator_after_unrelated_edit_ms={5:F0} (model step {10:F0} ms, cached models {9}) compile_with_wrappers_ms={6:F0} compile_without_ms={8:F0} errors={7}",
                list.Length, first.Result.GeneratedSources.Length - 2, lines, cold, warm, edit, compile, errors.Length, without, hits, model));
            foreach (var e in errors.Take(15)) Console.WriteLine("  " + e);
            if (errors.Length > 0) failed++;
        }
        return failed == 0 ? 0 : 1;
    }
}
