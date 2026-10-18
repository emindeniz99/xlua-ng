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
        if (args.Length > 1 && args[1] == "--timing") return Timing.Run(args.Skip(2).Select(int.Parse).ToArray());
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
        Assert.True(!bridges.Contains("xlua_checkinteger"), "results use the lenient integer casters");

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
        Assert.True(player.Contains("translator.Get(L, 6, out _f)"), "a GCOptimize enum outside LuaCallCSharp goes through Get<T>");
        Assert.True(run.Source(Emitter.BridgesFile).Contains("Structs.Push_Game_V3_"), "delegate bridges push without boxing too");
        var packing = run.Source(Emitter.PackFile);
        Assert.True(packing.Contains("xlua_pack_float6(buff, offset, field.a, field.b, field.c, field.d, field.e, field.f)")
            && packing.Contains("xlua_pack_float(buff, offset + 24, field.g)"), "float groups");
        Assert.True(packing.Contains("field.@class"), "keyword field names are escaped");
        var bindings = run.Source(Emitter.BindingsFile);
        Assert.True(bindings.Contains("b.Struct<global::Game.V3>(Structs.Push_Game_V3_") && bindings.Contains("b.Caster<global::Game.Settings>(Structs.Get_Game_Settings_"),
            "registration");

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

        var linkXml = run.Source(Emitter.LinkXmlFile);
        Assert.True(linkXml != null, "no " + Emitter.LinkXmlFile);
        foreach (var expected in new[] { "<assembly fullname=\"\"Game\"\">", "<type fullname=\"\"Game.Outer/Inner\"\">", "<method name=\"\"Slow\"\"/>",
            "<field name=\"\"count\"\"/>", "<type fullname=\"\"Game.Reflected\"\" preserve=\"\"all\"\"/>", "<type fullname=\"\"Game.Settings\"\">",
            "<property name=\"\"Level\"\"/>" })
        {
            Assert.True(linkXml.Contains(expected), "link.xml lacks " + expected + ":\n" + linkXml);
        }
        Assert.True(!linkXml.Contains("Fast") && !linkXml.Contains("Plain"), "wrapped members need no link.xml entry");

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

// Generator time and compile time by number of bound types (design section 7,
// "Measurement in M2"): XLua.SourceGenerator.Tests <repo> --timing 100 500 2000.
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

    public static int Run(int[] counts)
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
            var input = Harness.Compile("Timing", new[] { config, "public class Edited { public int a; }" });
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
