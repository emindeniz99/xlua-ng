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
can improve performance:

1. Configure the types as described in the [configuration guide](../Assets/XLua/Doc/Configure_EN.md).
2. Build the tools with `python3 Tools/build_tools.py` from the repository root.
   Rebuild the application, then run the generator on the output:
   `XLuaGenerate xxx.exe` or `XLuaGenerate xxx.dll`. It writes generated code to
   a `Gen` directory in the current directory.
3. Create a project with the same settings and define `XLUA_GENERAL`.
4. Remove the `XLua.Mini.dll` reference. Add the xLua source files (the `Src`
   directory in upstream release packages) and the generated code from step 2.
5. Build this project to produce an executable or library using generated adapters.

The repository includes the general tool projects under `vs2013/`; the directory
name reflects their historical origin, not a guarantee that every modern toolchain
has been tested.

## Hotfixing

Run `XLuaHotfixInject` on the executable or library after generating bindings.
Follow the [hotfix guide](../Assets/XLua/Doc/Hotfix_EN.md) for configuration and
instrumentation requirements.

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
