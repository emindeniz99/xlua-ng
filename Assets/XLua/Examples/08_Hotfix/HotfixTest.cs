using UnityEngine;
using XLua;

namespace XLuaTest
{
    [Hotfix]
    public class HotfixTest : MonoBehaviour
    {
        LuaEnv luaenv = new LuaEnv();

        private int tick = 0;

        // Use this for initialization
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
            if (++tick % 50 == 0)
            {
                Debug.Log(">>>>>>>>Update in C#, tick = " + tick);
            }
        }

        void OnGUI()
        {
            if (GUI.Button(new Rect(10, 10, 300, 80), "Hotfix"))
            {
                luaenv.DoString(@"
                xlua.hotfix(CS.XLuaTest.HotfixTest, 'Update', function(self)
                    self.tick = self.tick + 1
                    if (self.tick % 50) == 0 then
                        print('<<<<<<<<Update in lua, tick = ' .. self.tick)
                    end
                end)
            ");
            }

            string enHint = @"Before running this example, read the xLua documentation and follow these steps:

1. Define: Add HOTFIX_ENABLE to 'Edit > Project Settings > Player > Other Settings > Scripting Define Symbols'. Set it separately for each target platform.

2. Install the xLua source generator, which writes the hotfix bridges when Unity compiles the scripts (the UPM package brings it): python3 Tools/build_source_generator.py --output <build folder> --install Assets/XLua/Src/Analyzers, then let Unity finish compiling.

3. Inject: Select 'XLua > Hotfix Inject In Editor'. Successful injection prints 'hotfix inject finish!' or 'had injected!' in the Console.";
            GUIStyle style = GUI.skin.textArea;
            style.normal.textColor = Color.red;
            style.fontSize = 16;
            GUI.TextArea(new Rect(10, 100, 500, 290), enHint, style);
        }
    }
}
