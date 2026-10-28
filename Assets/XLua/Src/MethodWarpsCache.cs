/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
#endif

using System.Collections.Generic;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace XLua
{
    // What a reflection wrapper of one method needs to know about it, apart
    // from the env's checkers and casters. It holds no translator or Lua
    // state and is not changed once made, so envs on several threads may
    // share it (Utils.ReflectionMembers keeps it for the process).
    internal sealed class OverloadSignature
    {
        internal Type targetType;
        internal MethodBase method;
        internal int luaStackPosStart = 1;
        internal bool targetNeeded;
        internal int parameterCount;
        internal int[] refPos;
        internal int[] inPosArray;
        internal int[] outPosArray;
        // The types the checkers and casters are made for, one per in position.
        internal Type[] argTypes;
        internal bool[] isOptionalArray;
        internal object[] defaultValueArray;
        internal Type paramsType;
        internal bool isVoid = true;
        internal bool hasDefaultValue;
        // Overload selection (OverloadScore): the kind and type of each in
        // position, and how many leading positions are not scored (the self
        // of an extension method in the extended type's wrapper).
        internal int[] scoreKinds;
        internal Type[] scoreTypes;
        internal int scoreSkip;

        internal static OverloadSignature Create(Type targetType, MethodBase method)
        {
            var s = new OverloadSignature();
            s.targetType = targetType;
            s.method = method;
            if ((typeof(Delegate) != targetType && typeof(Delegate).IsAssignableFrom(targetType)) ||
                !method.IsStatic || method.IsConstructor)
            {
                s.luaStackPosStart = 2;
                if (!method.IsConstructor)
                {
                    s.targetNeeded = true;
                }
            }

            var paramInfos = method.GetParameters();
            s.parameterCount = paramInfos.Length;
            s.refPos = new int[paramInfos.Length];

            List<int> inPosList = new List<int>();
            List<int> outPosList = new List<int>();

            List<Type> argTypeList = new List<Type>();
            List<bool> isOptionalList = new List<bool>();
            List<object> defaultValueList = new List<object>();

            for(int i = 0; i < paramInfos.Length; i++)
            {
                s.refPos[i] = -1;
                if (!paramInfos[i].IsIn && paramInfos[i].IsOut)  // out parameter
				{
					outPosList.Add(i);
				}
                else
                {
                    if(paramInfos[i].ParameterType.IsByRef && !Utils.IsInParameter(paramInfos[i]))
                    {
                        var ttype = paramInfos[i].ParameterType.GetElementType();
                        if(CopyByValue.IsStruct(ttype) && ttype != typeof(decimal))
                        {
                            s.refPos[i] = inPosList.Count;
                        }
                        outPosList.Add(i);
                    }

                    inPosList.Add(i);
                    var paramType = paramInfos[i].IsDefined(typeof(ParamArrayAttribute), false) || (!paramInfos[i].ParameterType.IsArray && paramInfos[i].ParameterType.IsByRef ) ?
                        paramInfos[i].ParameterType.GetElementType() : paramInfos[i].ParameterType;
                    argTypeList.Add(paramType);
                    isOptionalList.Add(paramInfos[i].IsOptional);
                    var defalutValue = paramInfos[i].DefaultValue;
                    if (paramInfos[i].IsOptional)
                    {
                        // The default of a Nullable<T> parameter is a boxed T, and of
                        // an `in` parameter a T, not a T& (Tencent/xLua#991, #1105).
                        var defaultType = paramInfos[i].ParameterType.IsByRef ? paramInfos[i].ParameterType.GetElementType() : paramInfos[i].ParameterType;
                        defaultType = Nullable.GetUnderlyingType(defaultType) ?? defaultType;
                        if (defalutValue != null && defalutValue.GetType() != defaultType)
                        {
                            defalutValue = defalutValue.GetType() == typeof(Missing) ? (paramInfos[i].ParameterType.IsValueType() ? Activator.CreateInstance(paramInfos[i].ParameterType) : Missing.Value)
                                : Convert.ChangeType(defalutValue, defaultType);
                        }
                        s.hasDefaultValue = true;
                    }
                    defaultValueList.Add(paramInfos[i].IsOptional ? defalutValue : null);
                }
            }
            s.argTypes = argTypeList.ToArray();
            s.inPosArray = inPosList.ToArray();
            s.outPosArray = outPosList.ToArray();
            s.isOptionalArray = isOptionalList.ToArray();
            s.defaultValueArray = defaultValueList.ToArray();

            if (paramInfos.Length > 0 && paramInfos[paramInfos.Length - 1].IsDefined(typeof(ParamArrayAttribute), false))
            {
                s.paramsType = paramInfos[paramInfos.Length - 1].ParameterType.GetElementType();
            }

            s.scoreKinds = new int[s.argTypes.Length];
            s.scoreTypes = new Type[s.argTypes.Length];
            for (int i = 0; i < s.argTypes.Length; i++)
            {
                s.scoreKinds[i] = OverloadScore.KindOf(s.argTypes[i]);
                s.scoreTypes[i] = OverloadScore.TypeFor(s.argTypes[i]);
            }
            if (method.IsStatic && method.DeclaringType != targetType && s.argTypes.Length > 0 && method.IsDefined(typeof(ExtensionAttribute), false))
            {
                s.scoreSkip = 1;
            }

            if (method is MethodInfo) //constructor is not MethodInfo?
            {
                s.isVoid = (method as MethodInfo).ReturnType == typeof(void);
            }
            else if(method is ConstructorInfo)
            {
                s.isVoid = false;
            }
            return s;
        }

        // This overload as OverloadScore.Needed sees it.
        internal OverloadScore.Shape Shape()
        {
            var types = (Type[])argTypes.Clone();
            for (int i = 0; i < scoreSkip; i++) types[i] = null;
            int required = 0;
            int fixedCount = argTypes.Length - (paramsType != null ? 1 : 0);
            for (int i = 0; i < fixedCount; i++)
            {
                if (!isOptionalArray[i]) required++;
            }
            return new OverloadScore.Shape { start = luaStackPosStart, types = types, required = required, hasParams = paramsType != null };
        }

        // The overloads _GenMethodWrap would wrap, in its order: methods with
        // unsupported types and generic methods that cannot be closed are
        // left out.
        internal static OverloadSignature[] Prepare(Type type, IEnumerable<MemberInfo> methodBases)
        {
            List<OverloadSignature> overloads = new List<OverloadSignature>();
            foreach(var methodBase in methodBases)
            {
                var mb = methodBase as MethodBase;
                if (mb == null || Utils.HasUnsupportedType(mb))
                    continue;

                if (mb.IsGenericMethodDefinition
#if !ENABLE_IL2CPP
                     && !MethodWrapsCache.tryMakeGenericMethod(ref mb)
#endif
                    )
                    continue;

                overloads.Add(Create(type, mb));
            }
            return overloads.ToArray();
        }
    }

    public class OverloadMethodWrap
    {
        ObjectTranslator translator;
        Type targetType;
        MethodBase method;

        ObjectCheck[] checkArray;
        ObjectCast[] castArray;

        int[] inPosArray;
        int[] outPosArray;

        bool[] isOptionalArray;

        object[] defaultValueArray;

        bool isVoid = true;

        int luaStackPosStart = 1;

        bool targetNeeded = false;

        object[] args;

        int[] refPos;

        Type paramsType = null;

        internal OverloadSignature signature;
        int[] scoreKinds;
        Type[] scoreTypes;
        int scoreSkip;
        int fixedCount;
        // The kind of each number parameter whose check Check(L, numbers) reads
        // from the number kind (OverloadScore.NumberMatches), 0 for the others.
        int[] numberChecks;

        public bool HasDefalutValue{ get; private set; }

        public OverloadMethodWrap(ObjectTranslator translator, Type targetType, MethodBase method)
        {
            this.translator = translator;
            this.targetType = targetType;
            this.method = method;
            HasDefalutValue = false;
        }

        internal OverloadMethodWrap(ObjectTranslator translator, OverloadSignature signature)
            : this(translator, signature.targetType, signature.method)
        {
        }

        public void Init(ObjectCheckers objCheckers, ObjectCasters objCasters)
        {
            Init(objCheckers, objCasters, OverloadSignature.Create(targetType, method));
        }

        // The checkers and casters belong to the env; the rest of the
        // signature may be shared by every env (OverloadSignature).
        internal void Init(ObjectCheckers objCheckers, ObjectCasters objCasters, OverloadSignature signature)
        {
            luaStackPosStart = signature.luaStackPosStart;
            targetNeeded = signature.targetNeeded;
            refPos = signature.refPos;
            inPosArray = signature.inPosArray;
            outPosArray = signature.outPosArray;
            isOptionalArray = signature.isOptionalArray;
            defaultValueArray = signature.defaultValueArray;
            paramsType = signature.paramsType;
            isVoid = signature.isVoid;
            HasDefalutValue = signature.hasDefaultValue;
            this.signature = signature;
            scoreKinds = signature.scoreKinds;
            scoreTypes = signature.scoreTypes;
            scoreSkip = signature.scoreSkip;
            fixedCount = signature.argTypes.Length - (paramsType != null ? 1 : 0);

            var argTypes = signature.argTypes;
            checkArray = new ObjectCheck[argTypes.Length];
            castArray = new ObjectCast[argTypes.Length];
            numberChecks = new int[argTypes.Length];
            for (int i = 0; i < argTypes.Length; i++)
            {
                checkArray[i] = objCheckers.GetChecker(argTypes[i]);
                castArray[i] = objCasters.GetArgCaster(argTypes[i]);
                int kind = scoreKinds[i];
                if (objCheckers.defaultNumberChecks && OverloadScore.IsNumeric(kind) && kind < OverloadScore.KindNullable)
                {
                    numberChecks[i] = kind;
                }
            }

            args = new object[signature.parameterCount];
        }

        public bool Check(RealStatePtr L)
        {
            return Check(L, null);
        }

        // numbers: while the overloads are scored, the number kind of each
        // argument (-1 until read, as for Score), from which the checks of the
        // number parameters are read.
        internal bool Check(RealStatePtr L, int[] numbers)
        {
            int luaTop = LuaAPI.lua_gettop(L);
            int luaStackPos = luaStackPosStart;

            for(int i = 0; i < checkArray.Length; i++)
            {
                if ((i == (checkArray.Length - 1)) && (paramsType != null))
                {
                    break;
                }
                if(luaStackPos > luaTop && !isOptionalArray[i])
                {
                    return false;
                }
                else if(luaStackPos <= luaTop && !(numbers != null && numberChecks[i] != 0 && luaStackPos < numbers.Length
                    ? NumberCheck(L, luaStackPos, i, numbers) : checkArray[i](L, luaStackPos)))
                {
                    return false;
                }

                if (luaStackPos <= luaTop || !isOptionalArray[i])
                {
                    luaStackPos++;
                }
            }

            return paramsType != null ? (luaStackPos < luaTop + 1 ? 
                checkArray[checkArray.Length - 1](L, luaStackPos) : true) : luaStackPos == luaTop + 1;
        }

        bool NumberCheck(RealStatePtr L, int pos, int i, int[] numbers)
        {
            if (numbers[pos] < 0) numbers[pos] = LuaAPI.xlua_number_kind(L, pos);
            int number = numbers[pos];
            return number != 0 ? OverloadScore.NumberMatches(number, numberChecks[i]) : checkArray[i](L, pos);
        }

        // The cost of the arguments for this overload, after Check passed
        // (OverloadScore), every element of a params array included. numbers
        // caches the number kind of each argument for the candidates of one
        // call (-1 until read).
        internal int Score(RealStatePtr L, int luaTop, int[] numbers)
        {
            int sum = 0;
            for (int i = scoreSkip; i < fixedCount; i++)
            {
                int pos = luaStackPosStart + i;
                if (pos > luaTop) break;
                if (pos < numbers.Length && OverloadScore.IsNumeric(scoreKinds[i]))
                {
                    sum += OverloadScore.NumberArg(L, pos, scoreKinds[i], ref numbers[pos]);
                }
                else
                {
                    sum += OverloadScore.Arg(translator, L, pos, scoreKinds[i], scoreTypes[i]);
                }
            }
            if (paramsType != null)
            {
                sum += OverloadScore.Args(translator, L, luaStackPosStart + fixedCount, luaTop, scoreKinds[fixedCount], scoreTypes[fixedCount]);
            }
            return OverloadScore.Total(sum, paramsType != null, luaTop - luaStackPosStart + 1 < fixedCount);
        }

        public int Call(RealStatePtr L)
        {
            try
            {
#if UNITY_EDITOR && !DISABLE_OBSOLETE_WARNING
                if (method.IsDefined(typeof(ObsoleteAttribute), true))
                {
                    ObsoleteAttribute info = Attribute.GetCustomAttribute(method, typeof(ObsoleteAttribute)) as ObsoleteAttribute;
                    UnityEngine.Debug.LogWarning("Obsolete Method [" + method.DeclaringType.ToString() + "." + method.Name + "]: " + info.Message);
                } 
#endif
                object target = null;
                MethodBase toInvoke = method;

                if (luaStackPosStart > 1)
                {
                    target = translator.FastGetCSObj(L, 1);
                    // An object of the class, as generated code casts it: no
                    // object (a Lua table, nil) or one of another class raises
                    // what that cast raises, not TargetException.
                    if (targetNeeded && !targetType.IsValueType() && !typeof(Delegate).IsAssignableFrom(targetType)
                        && !method.DeclaringType.IsInstanceOfType(target))
                    {
                        // The runtime's own exception, whose text differs from a new
                        // NullReferenceException's on some runtimes.
                        if (target == null) return target.GetHashCode();
                        throw new InvalidCastException();
                    }
                    if (target is Delegate)
                    {
                        Delegate delegateInvoke = (Delegate)target;
#if UNITY_WSA && !UNITY_EDITOR
                        toInvoke = delegateInvoke.GetMethodInfo();
#else
                        toInvoke = delegateInvoke.Method;
#endif
                    }
                }


                int luaTop = LuaAPI.lua_gettop(L);
                int luaStackPos = luaStackPosStart;

                for (int i = 0; i < castArray.Length; i++)
                {
                    //UnityEngine.Debug.Log("inPos:" + inPosArray[i]);
                    if (luaStackPos > luaTop) //after check
                    {
                        if (paramsType != null && i == castArray.Length - 1)
                        {
                            args[inPosArray[i]] = Array.CreateInstance(paramsType, 0);
                        }
                        else
                        {
                            args[inPosArray[i]] = defaultValueArray[i];
                        }
                    }
                    else
                    {
                        if (paramsType != null && i == castArray.Length - 1)
                        {
                            args[inPosArray[i]] = translator.GetParams(L, luaStackPos, paramsType);
                        }
                        else
                        {
                            args[inPosArray[i]] = castArray[i](L, luaStackPos, null);
                        }
                        luaStackPos++;
                    }
                    //UnityEngine.Debug.Log("value:" + args[inPosArray[i]]);
                }

                object ret = null;


                ret = toInvoke.IsConstructor ? ((ConstructorInfo)method).Invoke(args) : method.Invoke(targetNeeded ? target : null, args);

                if (targetNeeded && targetType.IsValueType())
                {
                    translator.Update(L, 1, target);
                }

                int nRet = 0;
                if (outPosArray.Length + 1 > Utils.UncheckedPushes)
                {
                    Utils.EnsureStack(L, outPosArray.Length + 1);
                }

                if (!isVoid)
                {
                    //UnityEngine.Debug.Log(toInvoke.ToString() + " ret:" + ret);
                    translator.PushAny(L, ret);
                    nRet++;
                }

                for (int i = 0; i < outPosArray.Length; i++)
                {
                    if (refPos[outPosArray[i]] != -1)
                    {
                        translator.Update(L, luaStackPosStart + refPos[outPosArray[i]], args[outPosArray[i]]);
                    }
                    translator.PushAny(L, args[outPosArray[i]]);
                    nRet++;
                }

                return nRet;
            }
            finally
            {
                for(int i = 0; i < args.Length; i++)
                {
                    args[i] = null;
                }
            }
        }
    }

    public class MethodWrap
    {
        private string methodName;
        // The error when no overload takes the arguments: "invalid arguments to
        // T.Method!" (or "T constructor!") as in generated code, when the
        // wrapper knows its type; operatorType is set for a binary operator
        // (BindingApi.InvalidOperatorArguments).
        internal string invalidArguments;
        internal Type operatorType;
        private List<OverloadMethodWrap> overloads = new List<OverloadMethodWrap>();
        private bool forceCheck;
        // Whether two overloads can match the same arguments with different
        // costs (OverloadScore.Needed); otherwise the first match is the best.
        private bool scored;
        // The number kind of each argument while the overloads are scored.
        private int[] numbers;

        public MethodWrap(string methodName, List<OverloadMethodWrap> overloads, bool forceCheck)
        {
            this.methodName = methodName;
            this.overloads = overloads;
            this.forceCheck = forceCheck;
            if (overloads.Count > 1)
            {
                var shapes = new OverloadScore.Shape[overloads.Count];
                bool known = true;
                for (int i = 0; i < overloads.Count; i++)
                {
                    if (overloads[i].signature == null)
                    {
                        known = false;
                        break;
                    }
                    shapes[i] = overloads[i].signature.Shape();
                }
                scored = known && OverloadScore.Needed(shapes);
                if (scored)
                {
                    int last = 0;
                    for (int i = 0; i < overloads.Count; i++)
                    {
                        var signature = overloads[i].signature;
                        if (signature != null) last = Math.Max(last, signature.luaStackPosStart + signature.argTypes.Length);
                    }
                    numbers = new int[last];
                }
            }
        }

        public int Call(RealStatePtr L)
        {
            try
            {
                if (overloads.Count == 1 && !overloads[0].HasDefalutValue && !forceCheck) return overloads[0].Call(L);

                if (!scored)
                {
                    for (int i = 0; i < overloads.Count; ++i)
                    {
                        var overload = overloads[i];
                        if (overload.Check(L))
                        {
                            return overload.Call(L);
                        }
                    }
                    return invalidArgumentsError(L);
                }

                // The best match: the lowest cost, the first one of equal cost.
                OverloadMethodWrap best = null;
                int bestScore = int.MaxValue;
                int luaTop = LuaAPI.lua_gettop(L);
                for (int i = 0; i < numbers.Length; ++i)
                {
                    numbers[i] = -1;
                }
                for (int i = 0; i < overloads.Count; ++i)
                {
                    var overload = overloads[i];
                    if (overload.Check(L, numbers))
                    {
                        int score = overload.Score(L, luaTop, numbers);
                        if (score < bestScore)
                        {
                            best = overload;
                            bestScore = score;
                            if (score == 0) break;
                        }
                    }
                }
                if (best != null)
                {
                    return best.Call(L);
                }
                return invalidArgumentsError(L);
            }
            // The exception as generated and emitted wrappers report it: its
            // type, message and stack trace.
            catch (System.Reflection.TargetInvocationException e)
            {
                return LuaAPI.luaL_error(L, "c# exception:" + e.InnerException);
            }
            catch (System.Exception e)
            {
                return LuaAPI.luaL_error(L, "c# exception:" + e);
            }
        }

        int invalidArgumentsError(RealStatePtr L)
        {
            if (operatorType != null)
            {
                return Binding.BindingApi.InvalidOperatorArguments(ObjectTranslatorPool.Instance.Find(L), L, operatorType, methodName);
            }
            return LuaAPI.luaL_error(L, invalidArguments ?? ("invalid arguments to " + methodName));
        }

        // The error text of generated code for a method or the constructors of type.
        internal static string InvalidArgumentsText(Type type, string methodName)
        {
            return "invalid arguments to " + type + (methodName == ".ctor" ? " constructor" : "." + methodName) + "!";
        }
    }

    public class MethodWrapsCache
    {
        ObjectTranslator translator;
        ObjectCheckers objCheckers;
        ObjectCasters objCasters;

        Dictionary<Type, LuaCSFunction> constructorCache = new Dictionary<Type, LuaCSFunction>();
        Dictionary<Type, Dictionary<string, LuaCSFunction>> methodsCache = new Dictionary<Type, Dictionary<string, LuaCSFunction>>();
        Dictionary<Type, LuaCSFunction> delegateCache = new Dictionary<Type, LuaCSFunction>();

        public MethodWrapsCache(ObjectTranslator translator, ObjectCheckers objCheckers, ObjectCasters objCasters)
        {
            this.translator = translator;
            this.objCheckers = objCheckers;
            this.objCasters = objCasters;
        }

        public LuaCSFunction GetConstructorWrap(Type type)
        {
            //UnityEngine.Debug.LogWarning("GetConstructor:" + type);
            if (!constructorCache.ContainsKey(type))
            {
                var constructors = type.GetConstructors();
                // Leave out the constructors that generated code leaves out.
                constructors = Array.FindAll(constructors, c => !BlackListRules.IsListedMethod(c));
                if (type.IsAbstract() || constructors == null || constructors.Length == 0)
                {
                    if (type.IsValueType())
                    {
                        constructorCache[type] = (L) =>
                        {
                            translator.PushAny(L, Activator.CreateInstance(type));
                            return 1;
                        };
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    var ctorWrap = _GenMethodWrap(type, ".ctor", constructors, true);
                    ctorWrap.invalidArguments = MethodWrap.InvalidArgumentsText(type, ".ctor");
                    LuaCSFunction ctor = ctorWrap.Call;
                    
                    if (type.IsValueType())
                    {
                        bool hasZeroParamsCtor = false;
                        for (int i = 0; i < constructors.Length; i++)
                        {
                            if (constructors[i].GetParameters().Length == 0)
                            {
                                hasZeroParamsCtor = true;
                                break;
                            }
                        }
                        if (hasZeroParamsCtor)
                        {
                            constructorCache[type] = ctor;
                        }
                        else
                        {
                            constructorCache[type] = (L) =>
                            {
                                if (LuaAPI.lua_gettop(L) == 1)
                                {
                                    translator.PushAny(L, Activator.CreateInstance(type));
                                    return 1;
                                }
                                else
                                {
                                    return ctor(L);
                                }
                            };
                        }
                    }
                    else
                    {
                        constructorCache[type] = ctor;
                    }
                }
            }
            return constructorCache[type];
        }

        public LuaCSFunction GetMethodWrap(Type type, string methodName)
        {
            //UnityEngine.Debug.LogWarning("GetMethodWrap:" + type + " " + methodName);
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }
            var methodsOfType = methodsCache[type];
            if (!methodsOfType.ContainsKey(methodName))
            {
                MemberInfo[] methods = type.GetMember(methodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (methods == null || methods.Length == 0 ||
#if UNITY_WSA && !UNITY_EDITOR
                    methods[0] is MethodBase
#else
                    methods[0].MemberType != MemberTypes.Method
#endif
                    )
                {
                    return null;
                }
                methodsOfType[methodName] = _GenMethodWrap(type, methodName, methods).Call;
            }
            return methodsOfType[methodName];
        }

        public LuaCSFunction GetMethodWrapInCache(Type type, string methodName)
        {
            //string retriKey = type.ToString() + "." + methodName;
            //return methodsCache.ContainsKey(retriKey) ? methodsCache[retriKey] : null;
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }
            var methodsOfType = methodsCache[type];
            return methodsOfType.ContainsKey(methodName) ? methodsOfType[methodName] : null;
        }

        public LuaCSFunction GetDelegateWrap(Type type)
        {
            //UnityEngine.Debug.LogWarning("GetDelegateWrap:" + type );
            if (!typeof(Delegate).IsAssignableFrom(type))
            {
                return null;
            }
            if (!delegateCache.ContainsKey(type))
            {
                delegateCache[type] = _GenMethodWrap(type, type.ToString(), new MethodBase[] { type.GetMethod("Invoke") }).Call;
            }
            return delegateCache[type];
        }

        public LuaCSFunction GetEventWrap(Type type, string eventName)
        {
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }

            var methodsOfType = methodsCache[type];
            if (!methodsOfType.ContainsKey(eventName))
            {
                {
                    EventInfo eventInfo = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic);
                    if (eventInfo == null)
                    {
                        throw new Exception(type.Name + " has no event named: " + eventName);
                    }
                    int start_idx = 0;

                    MethodInfo add = eventInfo.GetAddMethod(true);
                    MethodInfo remove = eventInfo.GetRemoveMethod(true);

                    if (add == null && remove == null)
                    {
                        throw new Exception(type.Name + "'s " + eventName + " has either add nor remove");
                    }

                    bool is_static = add != null ? add.IsStatic : remove.IsStatic;
                    if (!is_static) start_idx = 1;

                    // As the generated and emitted event wrappers: the handler is
                    // checked before anything else, and the errors read as theirs.
                    // A handler that was not a function (nil, a number) reached
                    // CreateDelegateBridge, which stored it as a registry key: nil
                    // raised a Lua error through the C# frames, and a number could
                    // name the bridge of another function.
                    string invalidArguments = "invalid arguments to " + type + "." + eventName + "!";
                    string needHandler = "#" + (start_idx + 2) + " need " + eventInfo.EventHandlerType + "!";
                    methodsOfType[eventName] = (L) =>
                    {
                        object obj = null;

                        if (!is_static)
                        {
                            obj = translator.GetObject(L, 1, type);
                            if (obj == null)
                            {
                                return LuaAPI.luaL_error(L, "invalid #1, needed:" + type);
                            }
                        }

                        try
                        {
                            object handlerDelegate = translator.GetObject(L, start_idx + 2, eventInfo.EventHandlerType);
                            if (handlerDelegate == null)
                            {
                                return LuaAPI.luaL_error(L, needHandler);
                            }
                            switch (LuaAPI.lua_tostring(L, start_idx + 1))
                            {
                                case "+":
                                    if (add == null)
                                    {
                                        return LuaAPI.luaL_error(L, invalidArguments);
                                    }
                                    add.Invoke(obj, new object[] { handlerDelegate });
                                    break;
                                case "-":
                                    if (remove == null)
                                    {
                                        return LuaAPI.luaL_error(L, invalidArguments);
                                    }
                                    remove.Invoke(obj, new object[] { handlerDelegate });
                                    break;
                                default:
                                    return LuaAPI.luaL_error(L, invalidArguments);
                            }
                            if (obj != null && type.IsValueType())
                            {
                                // As after a method call on a struct: the Lua value keeps the handler.
                                translator.Update(L, 1, obj);
                            }
                        }
                        catch (System.Reflection.TargetInvocationException e)
                        {
                            return LuaAPI.luaL_error(L, "c# exception:" + e.InnerException);
                        }
                        catch (System.Exception e)
                        {
                            return LuaAPI.luaL_error(L, "c# exception:" + e);
                        }

                        return 0;
                    };
                }
            }
            return methodsOfType[eventName];
        }

        public MethodWrap _GenMethodWrap(Type type, string methodName, IEnumerable<MemberInfo> methodBases, bool forceCheck = false)
        {
            return GenMethodWrap(methodName, OverloadSignature.Prepare(type, methodBases), forceCheck);
        }

        // A wrapper of this env over overloads that OverloadSignature.Prepare
        // made, possibly for another env.
        internal MethodWrap GenMethodWrap(string methodName, OverloadSignature[] signatures, bool forceCheck = false)
        {
            List<OverloadMethodWrap> overloads = new List<OverloadMethodWrap>(signatures.Length);
            for (int i = 0; i < signatures.Length; i++)
            {
                var overload = new OverloadMethodWrap(translator, signatures[i]);
                overload.Init(objCheckers, objCasters, signatures[i]);
                overloads.Add(overload);
            }
            return new MethodWrap(methodName, overloads, forceCheck);
        }

        internal static bool tryMakeGenericMethod(ref MethodBase method)
        {
            try
            {
                if (!(method is MethodInfo) || !Utils.IsSupportedMethod(method as MethodInfo) )
                {
                    return false;
                }
                var genericArguments = method.GetGenericArguments();
                var constraintedArgumentTypes = new Type[genericArguments.Length];
                for (var i = 0; i < genericArguments.Length; i++)
                {
                    var argumentType = genericArguments[i];
                    var parameterConstraints = argumentType.GetGenericParameterConstraints();
                    constraintedArgumentTypes[i] = parameterConstraints[0];
                }
                method = ((MethodInfo)method).MakeGenericMethod(constraintedArgumentTypes);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
