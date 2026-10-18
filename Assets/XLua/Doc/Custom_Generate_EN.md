## Custom code generation

Upstream xLua let Editor code write other files from xLua's configuration through the engine of **XLua > Generate Code**: `CSObjectWrapEditor.Generator.CustomGen(template_src, get_tasks)` filled a Lua template with the configured types, and methods marked `[GenCodeMenu]` ran each time the Generate Code menu ran. xLua's own `link.xml` was written that way (`LinkXmlGen`).

Generate Code, `CustomGen`, `[GenCodeMenu]`, `LinkXmlGen` and the templates under `Assets/XLua/Src/Editor/Template` were removed in xlua-ng (source generator milestone M11). They have no replacement ([decision 10](../../../docs/design/source-generator.md#decisions-owner-2026-10-17)): the source generator writes C# into the compilation and runs no user templates. A project that used them gets compile errors in that code; rewrite it as below. The other steps after the removal are in [MIGRATING.md](../../../docs/MIGRATING.md#generate-code-removed).

## What to do instead

* **`link.xml`** needs no custom step: the source generator lists `[ReflectionUse]` types and the members Lua reaches through reflection, and a player build passes them to UnityLinker (see [ReflectionUse](Configure_EN.md#xluareflectionuse)).
* **LuaLS definitions:** **XLua > Generate LuaCATS Definitions** writes them from the generator's manifests ([LuaCATS definitions](LuaCATS.md)).
* **Other files:** write an Editor script that runs from its own `[MenuItem]` or from a build callback, and writes the file with ordinary C#. It can read
  * the configuration by reflection: the attributes are metadata, so `Assembly.GetCustomAttributes` and `Type.GetCustomAttributes` return them; an assembly-level one names its configured types in `Types` (`BlackList` in `Type` or `TypeName`, `Member` and the parameter types);
  * what the bindings bind, from the generator's manifest (below).
* **Templates:** the template engine stays. In the Editor every `LuaEnv` opens it as the `template` Lua library, also with `XLUA_NO_TEMPLATE`, which leaves it out of players only.

## Template syntax

The template syntax is simple, with only three elements:

* eval: The syntax is <%=exp%>. Exp is an arbitrary expression that will calculate and output the value of exp as a string.
* Code: The syntax is <% if true then end%>. The text between <% and %> is arbitrary Lua code that will be executed.
* Literal: This contains the parts other than eval and code. Literal means output as it is.

`template.compile(text)` returns a function, and `template.execute(f, env)` runs it with `env` as its environment and returns the text. The compiled code calls `table.insert` and `tostring`, so give `env` a metatable whose `__index` is `_G`. From C#, `XLua.TemplateEngine.LuaTemplate.Compile(luaenv, text)` and `LuaTemplate.Execute(f, env)` do the same.

~~~lua
local f = template.compile([[
<linker>
<% for _, name in ipairs(assemblies) do %>  <assembly fullname="<%=name%>" preserve="all"/>
<% end %></linker>
]])
local text = template.execute(f, setmetatable({assemblies = {'Game', 'Game.UI'}}, {__index = _G}))
~~~

The helpers of `TemplateCommon.lua.txt`, such as `ForEachCsList`, went with the removed templates.

## Where the generator's output is

The source generator writes no files into the project: the compiler adds its sources to each compilation of an assembly that uses xLua. Outside Unity, `csc -generatedfilesout:<folder>` writes them to disk (see [General/README.md](../../../General/README.md#optional-code-generation)). For each assembly they are in the namespace `XLua.Generated.<assembly>_<hash>`:

| File | Contents |
|---|---|
| `XLua.Bindings.g.cs` | The registration of the assembly's wrappers, bridges and name table |
| One file per wrapped type | `W_<name>_<hash>`, the class or enum wrapper |
| `XLua.Bridges.g.cs` | The delegate bridges; interface bridges are `B_<name>_<hash>` classes |
| `XLua.Structs.g.cs`, `XLua.PackUnpack.g.cs` | The `GCOptimize` code |
| `XLua.HotfixBridges.g.cs` | The hotfix bridges and IntKey slots, with `[Hotfix]` types |
| `XLua.LinkXml.g.cs`, `XLua.ObfuzRules.g.cs` | `link.xml` entries and obfuz rules (Editor and `XLUA_GENERAL` only) |
| `XLua.Manifest.g.cs` | The manifest (Editor and `XLUA_GENERAL` only) |

The manifest is a JSON string, the constant `Json` of the internal class `Manifest` in that namespace. It lists the wrapped types with the members their wrappers bind, the extension methods, the `GCOptimize` structs, the delegate, interface and hotfix bridges, the `[ReflectionUse]` types and the `BlackList` entries. An Editor script finds it through the assembly's `[assembly: XLua.Binding.GeneratedBindings]` attribute, whose `Bindings` type is in the same namespace:

~~~csharp
foreach (var marker in assembly.GetCustomAttributes(typeof(XLua.Binding.GeneratedBindingsAttribute), false))
{
    var bindings = ((XLua.Binding.GeneratedBindingsAttribute)marker).Bindings;
    var manifest = assembly.GetType(bindings.Namespace + ".Manifest", false);
    var field = manifest == null ? null : manifest.GetField("Json", BindingFlags.Static | BindingFlags.NonPublic);
    string json = field == null ? null : (string)field.GetRawConstantValue();
}
~~~

LuaCATS reads it this way (`SourceGenManifests` in [LuaCATSGenerator.cs](../Src/Editor/LuaCATSGenerator.cs)).
