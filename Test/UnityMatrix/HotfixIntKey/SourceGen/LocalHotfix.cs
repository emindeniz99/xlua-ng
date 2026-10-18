using XLua;

// --source-gen: Assembly-CSharp's HotfixBridges, with IntKey slots of its own,
// although xLua is in XLua.dll (milestone M7). Replaces the Editor list of
// HotfixIntKeyConfig.cs, which the source generator does not read.
[assembly: Hotfix(typeof(HotfixIntKeyLocal), HotfixFlag.IntKey)]
