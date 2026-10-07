using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class ExampleRunner : MonoBehaviour {
 readonly List<string> messages=new List<string>(); bool failed;
 void Awake(){DontDestroyOnLoad(gameObject);Application.logMessageReceived+=Log;}
 void Log(string text,string stack,LogType type){messages.Add(text);if(type==LogType.Exception||type==LogType.Error){failed=true;}}
 bool Has(string part){return messages.Exists(x=>x.Contains(part));}
 void Check(bool ok,string name){if(!ok){failed=true;Debug.LogError("XLUA_EXAMPLE_FAIL: "+name);}else Debug.Log("XLUA_EXAMPLE_PASS: "+name);}
 IEnumerator Start(){
  yield return SceneManager.LoadSceneAsync("CSCallLua"); yield return null;
  Check(Has("ret.d =")&&Has("_G.d:add(1, 2)="),"CSharpCallLua delegate/interface/out values");
  yield return SceneManager.LoadSceneAsync("LuaCallCs");yield return null;
  Check(Has("lua_event_callback1")&&Has("cast to interface ICalc"),"LuaCallCSharp method/event/interface");
  yield return SceneManager.LoadSceneAsync("U3DScripting");yield return null;
  var behaviour=UnityEngine.Object.FindFirstObjectByType<XLuaTest.LuaBehaviour>();
  var rotation=behaviour.transform.rotation;
  yield return new WaitForSeconds(1);
  Check(Quaternion.Angle(rotation,behaviour.transform.rotation)>1,"U3DScripting Lua update rotates Unity transform");
  yield return SceneManager.LoadSceneAsync("UI");yield return null;
  var input=UnityEngine.Object.FindFirstObjectByType<InputField>();
  var button=UnityEngine.Object.FindFirstObjectByType<Button>();
  input.text="xlua-ng-bridge";button.onClick.Invoke();yield return null;
  Check(Has("clicked, you input is 'xlua-ng-bridge'"),"UIEvent Unity button invokes Lua callback");
  Debug.Log(failed?"XLUA_EXAMPLES_FAIL":"XLUA_EXAMPLES_PASS: Lua 5.5 Android IL2CPP, four upstream scenes");
 }
}
