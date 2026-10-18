using XLua;

// Assembly-CSharp's HotfixBridges, with IntKey slots of its own (milestone M7),
// whether xLua is in XLua.dll or in Assembly-CSharp. Until milestone M11 an
// Editor list (HotfixIntKeyConfig.cs) configured both fixture types for the
// bridges of Generate Code; the injector now refuses a type only a list names.
[assembly: Hotfix(typeof(HotfixIntKeyLocal), HotfixFlag.IntKey)]
