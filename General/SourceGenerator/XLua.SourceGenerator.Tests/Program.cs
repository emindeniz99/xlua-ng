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
                bcl = tpa.Where(p => { var n = Path.GetFileName(p); return n.StartsWith("System.") || n == "mscorlib.dll" || n == "netstandard.dll"; })
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
        Assert.Diagnostics(run);
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
            Assert.Equal(false, (bool)flag, "a unit without delegate bridges does not set DelegateBridge.Gen_Flag");
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
namespace XLua.Binding { public static class BindingApi { public const int Version = 2; } }";
        var xlua = Harness.Compile("XLua", new[] { stub }, new MetadataReference[0]);
        var input = Harness.Compile("Game", new[] { "[XLua.LuaCallCSharp] public class Player { }" },
            new[] { MetadataReference.CreateFromImage(Harness.Emit(xlua)) });
        var run = Harness.Generate(input);
        Assert.Diagnostics(run, "XLSG007:Error");
        Assert.Equal(0, run.Result.GeneratedSources.Length, "sources");
        Assert.True(run.Result.Diagnostics[0].GetMessage().Contains("version 1") && run.Result.Diagnostics[0].GetMessage().Contains("version 2"),
            run.Result.Diagnostics[0].GetMessage());
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

    // A generator that references a newer compiler than the host's does not load
    // (CS9057, SOURCEGEN_SPIKE.md note 2): Unity 2021.3 and later ship 4.3.1. Its
    // other references must be ones that Roslyn 4.3.1 loads itself, at most at the
    // versions Microsoft.CodeAnalysis.Common 4.3.1 depends on.
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
}
