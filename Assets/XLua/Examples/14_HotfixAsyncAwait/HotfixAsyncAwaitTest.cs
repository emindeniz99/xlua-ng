using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

namespace XLuaTest
{
    [Hotfix]
    public partial class HotfixAsyncAwaitTest : MonoBehaviour
    {
        [Hotfix]
        [ContextMenu("Method1")]
        public void Method1()
        {
            Debug.Log($"Method1 in C#!");
        }

        [Hotfix]
        [ContextMenu("AsyncMethod1")]
#pragma warning disable CS1998 // This async method has no await operator and runs synchronously.
        public async void AsyncMethod1()
#pragma warning restore CS1998 // This async method has no await operator and runs synchronously.
        {
            Debug.Log($"AsyncMethod1 in C#!");
        }

        [Hotfix]
        [ContextMenu("AsyncMethod2")]
        public async void AsyncMethod2()
        {
            Debug.Log($"AsyncMethod2 in C#!    001");
            await Task.Delay(1000);
            Debug.Log($"AsyncMethod2 in C#!    002");
        }

        [Hotfix]
        [ContextMenu("AsyncMethod3")]
        public async void AsyncMethod3()
        {
            Debug.Log($"AsyncMethod3 in C#!    001");
            await Task.Delay(1000);
            Debug.Log($"AsyncMethod3 in C#!    002");
            await MyTask();
            Debug.Log($"AsyncMethod3 in C#!    003");
        }

        [Hotfix]
        [ContextMenu("AsyncMethod4")]
        public async void AsyncMethod4()
        {
            Debug.Log($"AsyncMethod4 in C#!    001");
            var result = await MyTask1();
            Debug.Log($"AsyncMethod4 in C#!    002    MyTask1 Result:{result}");
        }

        [Hotfix]
        [ContextMenu("MyTask")]
        public async Task MyTask()
        {
            Debug.Log($"MyTask in C#!    001");
            await Task.Delay(1000);
            Debug.Log($"MyTask in C#!    002");
            return;
        }

        [Hotfix]
        [ContextMenu("MyTask1")]
        public async Task<int> MyTask1()
        {
            Debug.Log($"MyTask1 in C#!    001");
            await Task.Delay(1000);
            Debug.Log($"MyTask1 in C#!    002");
            return 9999;
        }

        [Hotfix]
        [ContextMenu("MyTask2")]
        public Task<int> MyTask2()
        {
            Debug.Log($"MyTask2 in C#!    001");
            return Task.FromResult(9998);
        }
    }

    public partial class HotfixAsyncAwaitTest
    {
        public TextAsset Method1Lua;
        public TextAsset AsyncMethod1Lua;
        public TextAsset AsyncMethod2Lua;
        public TextAsset AsyncMethod3Lua;
        public TextAsset AsyncMethod4Lua;
        public TextAsset MyTaskLua;
        public TextAsset MyTask1Lua;
        public TextAsset MyTask2Lua;

        internal static LuaEnv luaEnv = new LuaEnv(); //all lua behaviour shared one luaenv only!

        [ContextMenu("DoHotfix Method1")]
        public void DoHotfixMethod1()
        {
            // Execute the script
            luaEnv.DoString(Method1Lua.text);
        }

        [ContextMenu("DoHotfix AsyncMethod1")]
        public void DoHotfixAsyncMethod1()
        {
            // Execute the script
            luaEnv.DoString(AsyncMethod1Lua.text);
        }

        [ContextMenu("DoHotfix AsyncMethod2")]
        public void DoHotfixAsyncMethod2()
        {
            // Execute the script
            luaEnv.DoString(AsyncMethod2Lua.text);
        }

        [ContextMenu("DoHotfix AsyncMethod3")]
        public void DoHotfixAsyncMethod3()
        {
            // Execute the script
            luaEnv.DoString(AsyncMethod3Lua.text);
        }

        [ContextMenu("DoHotfix AsyncMethod4")]
        public void DoHotfixAsyncMethod4()
        {
            // Execute the script
            luaEnv.DoString(AsyncMethod4Lua.text);
        }

        [ContextMenu("DoHotfix MyTask")]
        public void DoHotfixMyTask()
        {
            // Execute the script
            luaEnv.DoString(MyTaskLua.text);
        }

        [ContextMenu("DoHotfix MyTask1")]
        public void DoHotfixMyTask1()
        {
            // Execute the script
            luaEnv.DoString(MyTask1Lua.text);
        }

        [ContextMenu("DoHotfix MyTask2")]
        public void DoHotfixMyTask2()
        {
            // Execute the script
            luaEnv.DoString(MyTask2Lua.text);
        }


        void OnGUI()
        {
            Vector2 size = new Vector2(200, 40);
            Vector2 position = new Vector2(600, 100);
            Vector2 rightPposition = position + new Vector2(240, 0);
            int inteval = 50;


            if (GUI.Button(new Rect(position, size), nameof(Method1)))
            {
                Method1();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixMethod1)))
            {
                DoHotfixMethod1();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(AsyncMethod1)))
            {
                AsyncMethod1();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixAsyncMethod1)))
            {
                DoHotfixAsyncMethod1();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(AsyncMethod2)))
            {
                AsyncMethod2();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixAsyncMethod2)))
            {
                DoHotfixAsyncMethod2();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(AsyncMethod3)))
            {
                AsyncMethod3();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixAsyncMethod3)))
            {
                DoHotfixAsyncMethod3();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(AsyncMethod4)))
            {
                AsyncMethod4();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixAsyncMethod4)))
            {
                DoHotfixAsyncMethod4();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(MyTask)))
            {
                MyTask();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixMyTask)))
            {
                DoHotfixMyTask();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(MyTask1)))
            {
                MyTask1();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixMyTask1)))
            {
                DoHotfixMyTask1();
            }

            position.y += inteval;
            rightPposition.y += inteval;

            if (GUI.Button(new Rect(position, size), nameof(MyTask2)))
            {
                MyTask2();
            }

            if (GUI.Button(new Rect(rightPposition, size), nameof(DoHotfixMyTask2)))
            {
                DoHotfixMyTask2();
            }


            string enHint = @"Before running this example, read the xLua documentation and follow these steps:

1. Define: Add HOTFIX_ENABLE to 'Edit > Project Settings > Player > Other Settings > Scripting Define Symbols'. Set it separately for each target platform.

2. Install the xLua source generator, which writes the hotfix bridges when Unity compiles the scripts: python3 Tools/build_source_generator.py --output <build folder> --install Assets/XLua/Src/Analyzers, then let Unity finish compiling.

3. Inject: Select 'XLua > Hotfix Inject In Editor'. Successful injection prints 'hotfix inject finish!' or 'had injected!' in the Console.";
            GUIStyle style = GUI.skin.textArea;
            style.normal.textColor = Color.red;
            style.fontSize = 16;
            GUI.TextArea(new Rect(10, 100, 500, 290), enHint, style);
        }
    }
}





