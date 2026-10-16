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

    static string Definitions(string name)
    {
        string file = Path.Combine(GeneratorConfig.luacats_path, name);
        FileAssert.Exists(file);
        string text = File.ReadAllText(file).Replace("\r\n", "\n");
        StringAssert.StartsWith("---@meta\n", text);
        return text;
    }

    static void Has(string text, string file, params string[] lines)
    {
        foreach (string line in lines)
        {
            StringAssert.Contains(line, text, file + " lacks: " + line);
        }
    }

    // The types come from XluaEditorTest.Config in XluaEditorTestTarget.cs.
    // editor_tests.py then runs lua-language-server on these files.
    [Test]
    public void LuaCATSDescribesUnityAndTestTypes()
    {
        Generator.GenLuaCATS();

        Has(Definitions("UnityEngine.Vector3.lua"), "UnityEngine.Vector3.lua",
            "---@class CS.UnityEngine.Vector3 : CS.System.ValueType\n",
            "---@field x number\n",
            "---@field magnitude number read-only\n",
            "---@field up CS.UnityEngine.Vector3 static, read-only\n",
            "---@operator add(CS.UnityEngine.Vector3): CS.UnityEngine.Vector3\n",
            "---@overload fun(x: number, y: number, z: number): CS.UnityEngine.Vector3\n",
            "function CS.UnityEngine.Vector3.Cross(lhs, rhs) end\n");

        Has(Definitions("UnityEngine.GameObject.lua"), "UnityEngine.GameObject.lua",
            "---@class CS.UnityEngine.GameObject : CS.UnityEngine.Object\n",
            "---@field transform CS.UnityEngine.Transform read-only\n",
            "---@overload fun(name: string): CS.UnityEngine.GameObject\n");

        // Base classes are declared in full even though only GameObject is configured.
        Has(Definitions("UnityEngine.Object.lua"), "UnityEngine.Object.lua",
            "---@class CS.UnityEngine.Object : CS.System.Object\n",
            "function CS.UnityEngine.Object.DestroyImmediate(obj");

        Has(Definitions("UnityEngine.Transform.lua"), "UnityEngine.Transform.lua",
            "---@class CS.UnityEngine.Transform : CS.UnityEngine.Component",
            "---@field position CS.UnityEngine.Vector3\n",
            "function CS.UnityEngine.Transform:Find(n) end\n");

        Has(Definitions("XluaEditorTest.Target.lua"), "XluaEditorTest.Target.lua",
            "---@class CS.XluaEditorTest.Target : CS.System.Object\n",
            "---@field Value integer\n",
            "---@field Callback CS.XluaEditorTest.Combine\n",
            "---@field Calls integer static\n",
            "---@overload fun(value: integer): CS.XluaEditorTest.Target\n",
            "---@overload fun(self: CS.XluaEditorTest.Target, s: string): string\n",
            "---@overload fun(self: CS.XluaEditorTest.Target, x: integer, y: integer): string\n",
            "---@return integer rem\n",
            "function CS.XluaEditorTest.Target.Div(a, b) end\n",
            "---@return integer v\n",
            "function CS.XluaEditorTest.Target:Twice(v) end\n");

        Has(Definitions("XluaEditorTest.Mood.lua"), "XluaEditorTest.Mood.lua",
            "---@class CS.XluaEditorTest.Mood : CS.System.Enum\n",
            "---@field Calm CS.XluaEditorTest.Mood = 1\n",
            "---@field Lost CS.XluaEditorTest.Mood = 4\n");

        Has(Definitions("XluaEditorTest.Pair.lua"), "XluaEditorTest.Pair.lua",
            "---@class CS.XluaEditorTest.Pair : CS.System.ValueType\n",
            "---@field A integer\n",
            "---@field B number\n");

        Has(Definitions("CS.lua"), "CS.lua",
            "---@alias CS.XluaEditorTest.Combine fun(a: integer, b: string): integer\n");
    }
}
