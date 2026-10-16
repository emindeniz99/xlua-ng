using System.IO;
using System.Linq;
using CSObjectWrapEditor;
using NUnit.Framework;

// Runs the editor-side xLua tools inside a real Unity editor. Copied into the
// disposable project only when prepare.py is given --editor-tools.
public sealed class XluaEditorToolsTests
{
    [Test]
    public void LuaCATSDefinitionsAreGenerated()
    {
        Generator.GenLuaCATS();
        string dir = GeneratorConfig.luacats_path;
        foreach (string name in new[] { "CS.lua", "xlua.lua" })
        {
            string file = Path.Combine(dir, name);
            FileAssert.Exists(file);
            StringAssert.StartsWith("---@meta", File.ReadAllText(file));
        }
    }

    [Test]
    public void LuarcMatchesThePlugin()
    {
        Generator.GenLuaCATS();
        string luarc = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(GeneratorConfig.luacats_path).TrimEnd('/', '\\')), ".luarc.json");
        FileAssert.Exists(luarc);
        string text = File.ReadAllText(luarc);
#if XLUA_SMOKE_LUA55
        StringAssert.Contains("\"runtime.version\": \"Lua 5.5\"", text);
#else
        StringAssert.Contains("\"runtime.version\": ", text);
#endif
        StringAssert.Contains("\"LuaTypes\"", text);
    }

    [Test]
    public void ClearAndGenerateKeepsUserFiles()
    {
        Generator.GenLuaCATS();
        string dir = GeneratorConfig.luacats_path;
        string user = Path.Combine(dir, "user-notes.lua");
        string stale = Path.Combine(dir, "Stale.Removed.lua");
        File.WriteAllText(user, "-- mine\n");
        File.WriteAllText(stale, File.ReadAllText(Path.Combine(dir, "CS.lua")));
        try
        {
            Generator.ClearAndGenLuaCATS();
            FileAssert.Exists(user);
            FileAssert.DoesNotExist(stale);
            FileAssert.Exists(Path.Combine(dir, "CS.lua"));
        }
        finally
        {
            File.Delete(user);
        }
    }

    [Test]
    public void ValidateSetupAcceptsThePlugin()
    {
        var problems = SetupValidator.Validate();
        Assert.IsFalse(problems.Any(p => p.StartsWith("Native plugin") || p.StartsWith("LuaCATS")),
            string.Join("\n", problems.ToArray()));
    }
}
