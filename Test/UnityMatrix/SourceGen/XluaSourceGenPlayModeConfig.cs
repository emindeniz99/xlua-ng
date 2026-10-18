using XLua;
using XluaEditorTest;

// prepare.py --source-gen with --fast-enter-play-mode: PlayModeConfig of
// XluaPlayModeHost.cs in the forms the source generator reads (see
// XluaSourceGenSuiteConfig.cs). The host runs a Lua coroutine through
// StartCoroutine, which needs the IEnumerator bridge.
[assembly: LuaCallCSharp(typeof(PlayModeHost))]
[assembly: LuaCallCSharp(typeof(PlayModeProbe))]
[assembly: CSharpCallLua(typeof(System.Collections.IEnumerator))]
