using System.Linq;
using NUnit.Framework;
using UnityEditor;
using XLua;

// prepare.py --minimal-plugin: the minimal preset's Editor plugin is a variant
// (Plugins/<cpu>/xlua-variants/minimal/) next to the default one, with the import
// settings XLua/Plugin Variants/Copy Import Settings gives it. The Editor must keep
// loading the default plugin, and the selector (PluginVariants.cs) must pick the
// variant for XLUA_PRESET_MINIMAL and the default plugin otherwise. A player build
// with the define in Player Settings (SmokeBuild.BuildMac) checks the real selection.
public sealed class XluaPluginVariantTests
{
    static string DefaultPlugin()
    {
        return PluginImporter.GetAllImporters()
            .Select(i => i.assetPath)
            .Single(p => (p.EndsWith("/libxlua.dylib") || p.EndsWith("/libxlua.so")) && !p.Contains("/" + PluginVariants.Folder + "/"));
    }

    [Test]
    public void TheSelectorPicksTheMinimalVariantForThePreset()
    {
        var problems = PluginVariantSelector.Register();
        Assert.IsEmpty(problems, string.Join("; ", problems.ToArray()));
        string defaultPlugin = DefaultPlugin();
        var groups = PluginVariants.Groups(PluginImporter.GetAllImporters().Where(i => i.isNativePlugin).Select(i => i.assetPath), problems);
        var group = groups[defaultPlugin];
        string variant = group.Single(m => m.Key == "minimal").Value;
        StringAssert.Contains("/xlua-variants/minimal/", variant);

        string error;
        Assert.AreEqual(variant, PluginVariants.Choose(group, PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL" }, false, out error)));
        Assert.IsNull(error);
        // A Development Build without a "development" variant also takes the preset's.
        Assert.AreEqual(variant, PluginVariants.Choose(group, PluginVariants.Preference(new[] { "XLUA_PRESET_MINIMAL" }, true, out error)));
        Assert.AreEqual(defaultPlugin, PluginVariants.Choose(group, PluginVariants.Preference(new string[0], false, out error)));
        Assert.IsTrue(PluginVariantSelector.HasVariant("minimal"));
    }

    [Test]
    public void TheEditorLoadsTheDefaultPlugin()
    {
        var importer = (PluginImporter)AssetImporter.GetAtPath(PluginImporter.GetAllImporters()
            .Select(i => i.assetPath).Single(p => p.Contains("/xlua-variants/minimal/")));
        Assert.IsFalse(importer.GetCompatibleWithEditor(), "the variant must not be enabled for the Editor");
        Assert.IsTrue(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX) || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64));
        using (var env = new LuaEnv())
        {
            // The minimal library has no io and no LuaSocket; the default one has both.
            Assert.AreEqual(true, env.DoString("return io ~= nil and package.preload['socket.core'] ~= nil")[0]);
        }
    }
}
