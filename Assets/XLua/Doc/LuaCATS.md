# LuaCATS definitions for editors

xLua can write [LuaCATS](https://luals.github.io/wiki/annotations/) definition
files for the C# API your Lua code calls. Editors that use
[lua-language-server](https://github.com/LuaLS/lua-language-server) (LuaLS),
such as VS Code with the Lua extension, read them to complete `CS.*` names,
show signatures and report wrong argument types or misspelled members.

## Generate the files

In the Unity editor, run **XLua > Generate LuaCATS Definitions**. The files go
to `<project>/LuaTypes/`, next to `Assets/`. That folder sits outside `Assets`,
so Unity never imports the files and no xLua loader picks them up as scripts.

To choose another folder, mark a static string field or property in an editor
class with `CSObjectWrapEditor.LuaCATSPath`, the same way `GenPath` works:

```csharp
public static class LuaCATSConfig
{
    [CSObjectWrapEditor.LuaCATSPath]
    public static string Path = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../Tools/LuaTypes");
}
```

Outside Unity, `XLuaGenerate.exe` (built by `Tools/build_tools.py`) takes
`--luacats` before its usual arguments:

```sh
mono Tools/XLuaGenerate.exe --luacats Game.dll Game.Editor.dll LuaTypes/ [search paths...]
```

The generator loads the native xLua plugin, as code generation does, and asks
it whether Lua has an integer subtype. With Lua 5.3 or later, C# integral types
become `integer`; with LuaJIT they become `number`.

Generate again after you change the `LuaCallCSharp` or `ReflectionUse`
configuration. The generator deletes files it wrote earlier for types that are
gone and leaves other files in the folder alone.

## Point the editor at them

Add the folder to `workspace.library` in a `.luarc.json` at the root of your
Lua sources, and set the runtime that matches your plugin:

```json
{
  "runtime.version": "Lua 5.4",
  "workspace.library": ["../LuaTypes"]
}
```

LuaLS treats library files as read-only definitions; it does not report
problems in them. To check scripts from the command line, run
`lua-language-server --check=<lua folder> --checklevel=Warning`.

## What gets declared

The generator uses the same type set as **XLua > Generate Code**: every type in
the `LuaCallCSharp` and `ReflectionUse` configuration, with the same
`BlackList` and `[Obsolete]` filtering. It adds the base classes of those types
in full, since Lua reaches their members through every derived object. Members
listed in `DoNotGen` stay, because xLua still reaches them through reflection.

| File | Contents |
|-|-|
| `<Namespace>.<Type>.lua` | One file per C# type, like the generated `*Wrap.cs` files. |
| `CS.lua` | The `CS` root and namespace tables, delegate aliases, and empty classes for types that only appear in signatures. |
| `xlua.lua` | `xlua.*` helpers, `typeof`, `cast`, `base`, `uint64` and the array type. |
| `xlua/util.lua` | The module returned by `require 'xlua.util'`. |

One file per type keeps each file well under the 500 KB LuaLS
`workspace.preloadFileSize` limit, even for large namespaces such as
`UnityEngine`.

### Type mapping

| C# | LuaCATS |
|-|-|
| `bool` | `boolean` |
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char` | `integer` (Lua 5.3+) or `number` (LuaJIT) |
| `float`, `double` | `number` |
| `string`, `byte[]` | `string` |
| `object` | `any` |
| `void` return | no `@return` |
| `T?` (`Nullable<T>`) | `T?` |
| `LuaTable`, `LuaFunction` | `table`, `function` |
| `IntPtr` | `lightuserdata` |
| class, struct, interface | `CS.Namespace.Type`; parents are the base class and the implemented interfaces that appear in the files |
| nested type `Outer.Inner` | `CS.Namespace.Outer.Inner` |
| constructed generic, such as `List<int>` | `CS.System.Collections.Generic.List_System_Int32` |
| `T[]` | `xlua.Array<T>`: zero-based `[integer]` access and `Length` |
| delegate | `---@alias CS.Namespace.Delegate fun(...)` |
| enum | a class whose values are fields of the enum type, plus `__CastFrom` |

A parameter that xLua converts from a Lua table also accepts `table`: structs,
classes with a public parameterless constructor, interfaces and arrays (`T[]`).

Members follow the Lua call syntax:

- Fields and properties are `---@field` entries, marked `static` or `read-only`
  in their description.
- Instance methods use `:` and static methods use `.`. Overloads become
  `---@overload` entries in xLua's resolution order. Optional parameters end in
  `?`, and `params T[]` becomes `...: T`.
- `out` parameters leave the argument list. The return value comes first,
  followed by every `out` and `ref` parameter in order, as xLua returns them.
- Constructors are `---@overload fun(...): CS.Type` on the class, so
  `CS.UnityEngine.GameObject('name')` type-checks. Structs also get `fun()`.
- An event is a method that takes `"+"` or `"-"` and a handler:
  `obj:Clicked('+', handler)`.
- Indexers with a non-string key become `---@field [integer] T`, and `+`, `-`,
  `*`, `/`, `%` and the bitwise operators become `---@operator` entries.
- Extension methods from configured static classes appear as instance methods
  of the type they extend.

## Limitations

- LuaCATS has one class per name, so instances also show static members, and
  the class table also shows instance members and the call signature.
- The namespace tables accept any key (`[string] any`). xLua reaches every C#
  type through reflection, so `CS.System.Console` is valid even when it is not
  configured. A misspelled type name is therefore not reported, and LuaLS
  also skips undefined-member checks on a chain read straight through a
  namespace, such as `CS.Game.Color.Purple`. Keep the class in a local
  (`local Color = CS.Game.Color`) and `Color.Purple` is reported.
- LuaLS picks an overload by argument count before it checks argument types.
  When two C# overloads take the same number of arguments, or none takes the
  number you pass, it reports nothing.
- Helper parameters that take a C# class table, such as the one for
  `xlua.private_accessible`, have type `any`, so LuaLS does not report a
  missing argument there.
- Generic type definitions are not declared. `CS.System.Collections.Generic.List(CS.System.Int32)`
  has type `any`; annotate the result with `---@type` to get the generated class.
- Operators whose left operand is not the declaring type, such as
  `2 * vector`, and comparison operators have no LuaCATS form.
- Types that appear only in signatures are empty classes, so their members are
  reported as undefined. Add those types to `LuaCallCSharp` or `ReflectionUse`
  to declare them.
- LuaCATS has no read-only fields; assigning a read-only property is not
  reported.
- `xlua.cast(obj, typeof(T))` changes the object in place and returns nothing.
  Use `---@cast obj CS.T` to tell the editor about the new type.
