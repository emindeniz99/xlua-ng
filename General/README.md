# xLua for general C# applications

The general build provides Lua scripting in a C# environment. Compared with the
Unity integration, it omits Unity-specific behavior such as redirecting `print`
to the Unity Console and loading scripts from Unity resources. The other bridge
features remain available; see the [feature guide](../Assets/XLua/Doc/features.md).

## Installation

Reference `XLua.Mini.dll` from the application and place the matching native xLua
library where P/Invoke can load it, such as the application's executable directory.
Use a native library built for the selected runtime, operating system, and architecture.
See [native builds](../build/README.md) and [general test execution](../Test/README.md).

## Optional code generation

`XLua.Mini.dll` uses reflection for Lua/C# interoperability. Generated adapters
can improve performance. The xLua source generator writes them while the
application compiles; there is no separate generation step:

1. Configure the types with attributes, as described in the
   [configuration guide](../Assets/XLua/Doc/Configure_EN.md). Configuration
   lists are not read; `XLuaGenerate --migrate` converts them once
   ([Converting configuration lists](../Assets/XLua/Doc/Migrating_Configuration.md)).
2. From the repository root, build `XLua.Mini.dll` and the tools into `Tools/`
   with `python3 Tools/build_tools.py`, and the generator with
   `python3 Tools/build_source_generator.py --output <build folder>`, which
   writes `<build folder>/bin/XLua.SourceGenerator/XLua.SourceGenerator.dll`
   (it needs the .NET SDK pinned in `mise.toml`).
3. Compile the application with the .NET SDK's `csc`, with `XLUA_GENERAL`
   defined, the generator as an analyzer and a reference to `XLua.Mini.dll`.
   The generator needs Roslyn 4.3.1 or later, so Mono's `mcs` and the older
   `csc` that comes with Mono cannot run it. `Test/Native/run_managed.py`
   compiles its test application against Mono's reference assemblies this
   way:

   ```sh
   dotnet <dotnet folder>/sdk/<version>/Roslyn/bincore/csc.dll -nologo -noconfig -nostdlib -unsafe \
       -define:XLUA_GENERAL \
       -analyzer:<build folder>/bin/XLua.SourceGenerator/XLua.SourceGenerator.dll \
       -r:Tools/XLua.Mini.dll \
       -r:<mono>/lib/mono/4.5/mscorlib.dll -r:<mono>/lib/mono/4.5/System.dll \
       -r:<mono>/lib/mono/4.5/System.Core.dll -r:<mono>/lib/mono/4.5/Facades/netstandard.dll \
       -out:App.exe *.cs
   ```

   `-generatedfilesout:<folder>` also writes the generated sources to a
   folder. Warning CS8032 or CS9057 means the generator did not load; its own
   diagnostics start with `XLSG`.
4. Run the application with the native library next to it, as before.

For minimized wrappers, define `GEN_CODE_MINIMIZE` in the application and
build `XLua.Mini.dll` with it too (`python3 Tools/build_tools.py --define
GEN_CODE_MINIMIZE`); warning `XLSG013` reports a mismatch. Instead of
referencing `XLua.Mini.dll`, the application can also compile the xLua sources
that `vs2013/XLua.Mini.csproj` lists.

The repository includes the general tool projects under `vs2013/`; the directory
name reflects their historical origin, not a guarantee that every modern toolchain
has been tested.

## Hotfixing

The hotfix bridges also come from the source generator: compile each assembly
with `[Hotfix]` types as above, with `HOTFIX_ENABLE` defined, and xLua with
`HOTFIX_ENABLE` too (`python3 Tools/build_tools.py --define HOTFIX_ENABLE`
builds such an `XLua.Mini.dll`, or compile the xLua sources into the
application). Then run `XLuaHotfixInject <assembly> <xLua assembly> <id map>
[search paths...]` on the executable or library; when xLua is compiled into
the application, the assembly and the xLua assembly are the same file.
Follow the [hotfix guide](../Assets/XLua/Doc/Hotfix_EN.md) for configuration and
instrumentation requirements. After injecting several assemblies, each with its
own id map, `XLuaHotfixInject --merge-id-maps <output> <map>...` writes the
`hotfix_id_map` module that `xlua.util.auto_id_map()` loads (see
[Several hotfix assemblies](../Assets/XLua/Doc/Hotfix_EN.md#several-hotfix-assemblies)).

## Quick start

```csharp
using XLua;

public class XLuaTest
{
    public static void Main()
    {
        LuaEnv luaenv = new LuaEnv();
        luaenv.DoString("CS.System.Console.WriteLine('hello world')");
        luaenv.Dispose();
    }
}
```
