// vs_bench.py copies this into the PuerTS and zlua projects as Assets/XluaSmoke.cs: SmokeBuild.cs builds a scene
// with an XluaSmoke component, as for xlua-ng's players, and this one starts the cross-library bench (VsBench.cs)
// with the project's adapter.
using UnityEngine;

public class XluaSmoke : MonoBehaviour {
#if VSBENCH_PUERTS
 const string Library = "puerts-lua";
 static VsBench.ILuaAdapter Create() { return new VsBench.PuertsAdapter(); }
#elif VSBENCH_ZLUA
 const string Library = "zlua";
 static VsBench.ILuaAdapter Create() { return new VsBench.ZLuaAdapter(); }
#endif
 public static string Run() { return "XLUA_VSBENCH_HOST " + Library; }
 void Start() {
  Debug.Log(Run());
  StartCoroutine(VsBench.Runner.Run(Create, Library, "default"));
 }
}
