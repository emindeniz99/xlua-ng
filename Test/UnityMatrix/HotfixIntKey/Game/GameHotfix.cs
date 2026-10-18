using XLua;

// The asmdef layout: Game.dll references XLua, so the source generator runs in it and
// writes its HotfixBridges, with IntKey slots of its own (milestone M7). The
// configuration is the assembly-level form in the assembly that defines the type.
[assembly: Hotfix(typeof(Game.Shop), HotfixFlag.IntKey)]
