using System;
using System.Collections.Generic;
using XLua;
public static class DeviceExampleConfig {
 [BlackList] public static Func<System.Reflection.MemberInfo,bool> Filter = m => {
 if(m.Name=="networkView") return true;
 if(m.DeclaringType==typeof(UnityEngine.Light) && new List<string>{"areaSize","lightmapBakeType","SetLightDirty","shadowRadius","shadowAngle"}.Contains(m.Name)) return true;
 var method=m as System.Reflection.MethodBase; if(method!=null) {foreach(var param in method.GetParameters()) if(param.ParameterType.IsByRefLike) return true;}
 var mi=m as System.Reflection.MethodInfo; if(mi!=null && mi.ReturnType.IsByRefLike) return true;
 var prop=m as System.Reflection.PropertyInfo; return prop!=null && prop.PropertyType.IsByRefLike; };
 [CSharpCallLua] public static List<Type> Callbacks=new List<Type>{typeof(Action),typeof(Action<string>),typeof(UnityEngine.Events.UnityAction)};
 [LuaCallCSharp] public static List<Type> Types=new List<Type>{typeof(UnityEngine.GameObject),typeof(UnityEngine.Component),typeof(UnityEngine.Object),typeof(UnityEngine.Vector3),typeof(UnityEngine.Color),typeof(UnityEngine.Mathf),typeof(UnityEngine.Time),typeof(UnityEngine.Light),typeof(UnityEngine.Transform),typeof(UnityEngine.UI.Button),typeof(UnityEngine.UI.InputField)};
}
