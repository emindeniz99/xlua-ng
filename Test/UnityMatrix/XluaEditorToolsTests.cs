using System.IO;
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
}
