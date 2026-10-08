# Unity examples

The upstream examples demonstrate different parts of the bridge. Generate the
required bindings before building an IL2CPP player. Hotfix examples additionally
require instrumentation; follow the [hotfix guide](../Assets/XLua/Doc/Hotfix_EN.md).

| Example | Demonstrates |
| --- | --- |
| [Hello world](../Assets/XLua/Examples/01_Helloworld/) | Running Lua from C# |
| [Unity scripting](../Assets/XLua/Examples/02_U3DScripting/) | Lua implementations of Unity lifecycle callbacks |
| [UI events](../Assets/XLua/Examples/03_UIEvent/) | Lua callbacks for Unity UI buttons |
| [Lua objects](../Assets/XLua/Examples/04_LuaObjectOrented/) | Lua objects accessed through C# interfaces |
| [No GC](../Assets/XLua/Examples/05_NoGc/) | Generated conversions for value types |
| [Coroutines](../Assets/XLua/Examples/06_Coroutine/) | Lua and Unity coroutine integration |
| [Async calls](../Assets/XLua/Examples/07_AsyncTest/) | Sequential Lua logic over asynchronous callbacks |
| [Hotfixing](../Assets/XLua/Examples/08_Hotfix/) | Replacing instrumented C# behavior with Lua |
| [Generic methods](../Assets/XLua/Examples/09_GenericMethod/) | Supported generic method signatures |
| [Signature loader](../Assets/XLua/Examples/10_SignatureLoader/) | Loading signed Lua scripts |
| [Raw objects](../Assets/XLua/Examples/11_RawObject/) | Choosing boxed C# types for object parameters |
| [Lua implementations](../Assets/XLua/Examples/12_ReImplementInLua/) | Replacing struct operations and mappings in Lua |
| [CLI build](../Assets/XLua/Examples/13_BuildFromCLI/) | Invoking Unity builds from the command line |
| [Async hotfixing](../Assets/XLua/Examples/14_HotfixAsyncAwait/) | Hotfixing async/task behavior |

The [tutorial source](../Assets/XLua/Tutorial/) also includes C#-to-Lua and
Lua-to-C# call examples. See [test coverage](../Test/README.md) for what has been
verified on devices; an example's presence is not proof of every platform/runtime
combination. These are development examples, not a list of commercial adopters.
