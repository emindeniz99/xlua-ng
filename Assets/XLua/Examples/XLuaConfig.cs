// xLua configuration of assembly Assembly-CSharp, in the attribute form
// that the xLua source generator reads, converted from the configuration
// lists named below, which the generator does not read. Written by the migration tool:
//
//     XLuaGenerate --migrate
//
// Delete those lists once this file is in place and edit it by hand from then
// on; the tool rewrites it only while the lists exist. See docs/MIGRATING.md,
// "Converting configuration lists".
//
// xlua-migrated-list: ExampleGenConfig.LuaCallCSharp
// xlua-migrated-list: XLuaTest.CoroutineConfig.LuaCallCSharp
// xlua-migrated-list: ExampleGenConfig.CSharpCallLua
// xlua-migrated-list: XLuaTest.MessageBoxConfig.CSharpCallLua
// xlua-migrated-list: ExampleGenConfig.BlackList

// ExampleGenConfig.LuaCallCSharp
[assembly: XLua.LuaCallCSharp(typeof(object))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Object))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Vector2))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Vector3))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Vector4))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Quaternion))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Color))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Ray))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Bounds))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Ray2D))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Time))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.GameObject))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Component))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Behaviour))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Transform))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Resources))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.TextAsset))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Keyframe))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.AnimationCurve))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.AnimationClip))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.MonoBehaviour))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.ParticleSystem))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.SkinnedMeshRenderer))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Renderer))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Light))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Mathf))]
[assembly: XLua.LuaCallCSharp(typeof(System.Collections.Generic.List<int>))]
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.Debug))]

// XLuaTest.CoroutineConfig.LuaCallCSharp
[assembly: XLua.LuaCallCSharp(typeof(UnityEngine.WaitForSeconds))]

// ExampleGenConfig.CSharpCallLua
[assembly: XLua.CSharpCallLua(typeof(System.Action))]
[assembly: XLua.CSharpCallLua(typeof(System.Func<double, double, double>))]
[assembly: XLua.CSharpCallLua(typeof(System.Action<string>))]
[assembly: XLua.CSharpCallLua(typeof(System.Action<double>))]
[assembly: XLua.CSharpCallLua(typeof(UnityEngine.Events.UnityAction))]
[assembly: XLua.CSharpCallLua(typeof(System.Collections.IEnumerator))]

// XLuaTest.MessageBoxConfig.CSharpCallLua
[assembly: XLua.CSharpCallLua(typeof(System.Action<bool>))]

// ExampleGenConfig.BlackList
[assembly: XLua.BlackList("System.Xml.XmlNodeList", "ItemOf")]
[assembly: XLua.BlackList("UnityEngine.WWW", "movie")]
[assembly: XLua.BlackList("UnityEngine.Texture2D", "alphaIsTransparency")]
[assembly: XLua.BlackList("UnityEngine.Security", "GetChainOfTrustValue")]
[assembly: XLua.BlackList("UnityEngine.CanvasRenderer", "onRequestRebuild")]
[assembly: XLua.BlackList("UnityEngine.Light", "areaSize")]
[assembly: XLua.BlackList("UnityEngine.Light", "lightmapBakeType")]
[assembly: XLua.BlackList("UnityEngine.Light", "SetLightDirty")]
[assembly: XLua.BlackList("UnityEngine.Light", "shadowRadius")]
[assembly: XLua.BlackList("UnityEngine.Light", "shadowAngle")]
[assembly: XLua.BlackList("UnityEngine.WWW", "MovieTexture")]
[assembly: XLua.BlackList("UnityEngine.WWW", "GetMovieTexture")]
[assembly: XLua.BlackList("UnityEngine.AnimatorOverrideController", "PerformOverrideClipListCleanup")]
[assembly: XLua.BlackList("UnityEngine.Application", "ExternalEval")]
[assembly: XLua.BlackList("UnityEngine.GameObject", "networkView")]
[assembly: XLua.BlackList("UnityEngine.Component", "networkView")]
[assembly: XLua.BlackList("System.IO.FileInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections")]
[assembly: XLua.BlackList("System.IO.FileInfo", "SetAccessControl", "System.Security.AccessControl.FileSecurity")]
[assembly: XLua.BlackList("System.IO.DirectoryInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections")]
[assembly: XLua.BlackList("System.IO.DirectoryInfo", "SetAccessControl", "System.Security.AccessControl.DirectorySecurity")]
[assembly: XLua.BlackList("System.IO.DirectoryInfo", "CreateSubdirectory", "System.String", "System.Security.AccessControl.DirectorySecurity")]
[assembly: XLua.BlackList("System.IO.DirectoryInfo", "Create", "System.Security.AccessControl.DirectorySecurity")]
[assembly: XLua.BlackList("UnityEngine.MonoBehaviour", "runInEditMode")]
