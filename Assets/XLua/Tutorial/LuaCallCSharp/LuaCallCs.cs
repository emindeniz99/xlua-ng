/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using UnityEngine;
using System.Collections;
using System;
using XLua;
using System.Collections.Generic;

namespace Tutorial
{
	[LuaCallCSharp]
	public class BaseClass
	{
		public static void BSFunc()
		{
			Debug.Log("Derived Static Func, BSF = " + BSF);
		}

		public static int BSF = 1;

		public void BMFunc()
		{
			Debug.Log("Derived Member Func, BMF = " + BMF);
		}

		public int BMF { get; set; }
	}

	public struct Param1
	{
		public int x;
		public string y;
	}

	[LuaCallCSharp]
	public enum TestEnum
	{
		E1,
		E2
	}


	[LuaCallCSharp]
	public class DerivedClass : BaseClass
	{
		[LuaCallCSharp]
		public enum TestEnumInner
		{
			E3,
			E4
		}

		public void DMFunc()
		{
			Debug.Log("Derived Member Func, DMF = " + DMF);
		}

		public int DMF { get; set; }

		public double ComplexFunc(Param1 p1, ref int p2, out string p3, Action luafunc, out Action csfunc)
		{
			Debug.Log("P1 = {x=" + p1.x + ",y=" + p1.y + "},p2 = " + p2);
			luafunc();
			p2 = p2 * p1.x;
			p3 = "hello " + p1.y;
			csfunc = () =>
			{
				Debug.Log("csharp callback invoked!");
			};
			return 1.23;
		}

		public void TestFunc(int i)
		{
			Debug.Log("TestFunc(int i)");
		}

		public void TestFunc(string i)
		{
			Debug.Log("TestFunc(string i)");
		}

		public static DerivedClass operator +(DerivedClass a, DerivedClass b)
		{
            DerivedClass ret = new DerivedClass();
			ret.DMF = a.DMF + b.DMF;
			return ret;
		}

		public void DefaultValueFunc(int a = 100, string b = "cccc", string c = null)
		{
			UnityEngine.Debug.Log("DefaultValueFunc: a=" + a + ",b=" + b + ",c=" + c);
		}

		public void VariableParamsFunc(int a, params string[] strs)
		{
			UnityEngine.Debug.Log("VariableParamsFunc: a =" + a);
			foreach (var str in strs)
			{
				UnityEngine.Debug.Log("str:" + str);
			}
		}

		public TestEnum EnumTestFunc(TestEnum e)
		{
			Debug.Log("EnumTestFunc: e=" + e);
			return TestEnum.E2;
		}

		public Action<string> TestDelegate = (param) =>
		{
			Debug.Log("TestDelegate in c#:" + param);
		};

		public event Action TestEvent;

		public void CallEvent()
		{
			TestEvent();
		}

		public ulong TestLong(long n)
		{
			return (ulong)(n + 1);
		}

		class InnerCalc : ICalc
		{
			public int add(int a, int b)
			{
				return a + b;
			}

			public int id = 100;
		}

		public ICalc GetCalc()
		{
			return new InnerCalc();
		}

		public void GenericMethod<T>()
		{
			Debug.Log("GenericMethod<" + typeof(T) + ">");
		}
	}

	[LuaCallCSharp]
	public interface ICalc
	{
		int add(int a, int b);
	}

	[LuaCallCSharp]
	public static class DerivedClassExtensions
    {
		public static int GetSomeData(this DerivedClass obj)
		{
			Debug.Log("GetSomeData ret = " + obj.DMF);
			return obj.DMF;
		}

		public static int GetSomeBaseData(this BaseClass obj)
		{
			Debug.Log("GetSomeBaseData ret = " + obj.BMF);
			return obj.BMF;
		}

		public static void GenericMethodOfString(this DerivedClass obj)
		{
			obj.GenericMethod<string>();
		}
	}
}

public class LuaCallCs : MonoBehaviour
{
	LuaEnv luaenv = null;
	string script = @"
        function demo()
            --Create C# objects
            local newGameObj = CS.UnityEngine.GameObject()
            local newGameObj2 = CS.UnityEngine.GameObject('helloworld')
            print(newGameObj, newGameObj2)
        
            --Access static properties and methods
            local GameObject = CS.UnityEngine.GameObject
            print('UnityEngine.Time.deltaTime:', CS.UnityEngine.Time.deltaTime) --Read a static property
            CS.UnityEngine.Time.timeScale = 0.5 --Write a static property
            print('helloworld', GameObject.Find('helloworld')) --Call a static method

            --Access instance properties and methods
            local DerivedClass = CS.Tutorial.DerivedClass
            local testobj = DerivedClass()
            testobj.DMF = 1024--Set an instance property
            print(testobj.DMF)--Read an instance property
            testobj:DMFunc()--Instance method

            --Base class properties and methods
            print(DerivedClass.BSF)--Read a base class static property
            DerivedClass.BSF = 2048--Write a base class static property
            DerivedClass.BSFunc();--Base class static method
            print(testobj.BMF)--Read a base class instance property
            testobj.BMF = 4096--Write a base class instance property
            testobj:BMFunc()--Call a base class method

            --Call methods with complex signatures
            local ret, p2, p3, csfunc = testobj:ComplexFunc({x=3, y = 'john'}, 100, function()
               print('i am lua callback')
            end)
            print('ComplexFunc ret:', ret, p2, p3, csfunc)
            csfunc()

            --Call overloaded methods
            testobj:TestFunc(100)
            testobj:TestFunc('hello')

            --Operators
            local testobj2 = DerivedClass()
            testobj2.DMF = 2048
            print('(testobj + testobj2).DMF = ', (testobj + testobj2).DMF)

            --Default parameter values
            testobj:DefaultValueFunc(1)
            testobj:DefaultValueFunc(3, 'hello', 'john')

            --Variable argument lists
            testobj:VariableParamsFunc(5, 'hello', 'john')

            --Extension methods
            print(testobj:GetSomeData()) 
            print(testobj:GetSomeBaseData()) --Call extension methods on the base class
            testobj:GenericMethodOfString()  --Use extension methods to expose generic methods

            --Enum types
            local e = testobj:EnumTestFunc(CS.Tutorial.TestEnum.E1)
            print(e, e == CS.Tutorial.TestEnum.E2)
            print(CS.Tutorial.TestEnum.__CastFrom(1), CS.Tutorial.TestEnum.__CastFrom('E1'))
            print(CS.Tutorial.DerivedClass.TestEnumInner.E3)
            assert(CS.Tutorial.BaseClass.TestEnumInner == nil)

            --Delegate
            testobj.TestDelegate('hello') --Invoke directly
            local function lua_delegate(str)
                print('TestDelegate in lua:', str)
            end
            testobj.TestDelegate = lua_delegate + testobj.TestDelegate --Combine delegates. Here the C# delegate is the right operand; the left operand is also supported.
            testobj.TestDelegate('hello')
            testobj.TestDelegate = testobj.TestDelegate - lua_delegate --remove
            testobj.TestDelegate('hello')

            --Events
            local function lua_event_callback1() print('lua_event_callback1') end
            local function lua_event_callback2() print('lua_event_callback2') end
            testobj:TestEvent('+', lua_event_callback1)
            testobj:CallEvent()
            testobj:TestEvent('+', lua_event_callback2)
            testobj:CallEvent()
            testobj:TestEvent('-', lua_event_callback1)
            testobj:CallEvent()
            testobj:TestEvent('-', lua_event_callback2)

            --64-bit integer support
            local l = testobj:TestLong(11)
            print(type(l), l, l + 100, 10000 + l)

            --typeof
            newGameObj:AddComponent(typeof(CS.UnityEngine.ParticleSystem))

            --cast
            local calc = testobj:GetCalc()
            print('assess instance of InnerCalc via reflection', calc:add(1, 2))
            assert(calc.id == 100)
            cast(calc, typeof(CS.Tutorial.ICalc))
            print('cast to interface ICalc', calc:add(1, 2))
            assert(calc.id == nil)
       end

       demo()

       --Use from a coroutine
       local co = coroutine.create(function()
           print('------------------------------------------------------')
           demo()
       end)
       assert(coroutine.resume(co))
    ";

	// Use this for initialization
	void Start()
	{
		luaenv = new LuaEnv();
		luaenv.DoString(script);
	}

	// Update is called once per frame
	void Update()
	{
		if (luaenv != null)
		{
			luaenv.Tick();
		}
	}

	void OnDestroy()
	{
		luaenv.Dispose();
	}
}
