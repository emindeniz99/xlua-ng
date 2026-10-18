/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// A Unity script that uses LuaScript and nothing of the bridge: the sample of
// the editor-level core-only check (Test/Native/asmdef_compile_check.py
// --core-only, docs/design/core-luascript.md section 7.6). With XLUA_CORE_ONLY
// it must compile against XLua.Core alone.
using System;
using UnityEngine;
using XLua;

public class LuaScriptSample : MonoBehaviour
{
    public string code = "function calcReward(level, kind) return level * (kind == 'gold' and 10 or 1) end";
    LuaScript lua;

    void Start()
    {
        lua = new LuaScript(new LuaOptions
        {
            MemoryLimit = 4 << 20,
            InstructionLimit = 10000000,
            TimeLimit = TimeSpan.FromMilliseconds(50),
            Libraries = LuaLibs.String | LuaLibs.Table | LuaLibs.Math,
        });
        try
        {
            // Host functions: LuaCall without allocation, and a typed shorthand (IL2CPP sees both instantiations).
            lua.Register("log", (ref LuaCall c) => Debug.Log(c.GetString(0)));
            lua.Register<long, long, long>("bonus", (level, streak) => level * 2 + streak);
            lua.Load(code, "remote");
            long reward = lua.Call<long>("calcReward", 3, "gold");
            var results = new LuaValue[2];
            int n = lua.Call("calcReward", new LuaValue[] { 1, "wood" }, results);
            Debug.Log("reward " + reward + ", " + n + " result(s): " + results[0]);
        }
        catch (LuaLimitException e)
        {
            Debug.LogWarning("the script hit a limit: " + e.Limit + " " + e.Message);
        }
        catch (LuaException e)
        {
            Debug.LogError(e.Message + "\n" + e.LuaStackTrace);
        }
    }

    void OnDestroy()
    {
        if (lua != null)
        {
            lua.Dispose();
        }
    }
}
