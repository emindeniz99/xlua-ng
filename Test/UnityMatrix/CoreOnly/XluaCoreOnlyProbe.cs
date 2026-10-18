/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The Editor side of Test/UnityMatrix/core_only_check.py: in a project that
// installs the xLua package with XLUA_CORE_ONLY in Player Settings, checks
// what the editor compiled and loaded, runs a LuaScript on the core library,
// asks the plugin variant selector for its choice, and exits with 0 or 1.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using XLua;

public static class XluaCoreOnlyProbe
{
    public static void Run()
    {
        var problems = new System.Collections.Generic.List<string>();
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
        foreach (var name in new[] { "XLua", "XLua.Editor" })
        {
            if (loaded.Contains(name)) problems.Add(name + " is loaded");
        }
        foreach (var name in new[] { "XLua.Core", "XLua.Core.Editor" })
        {
            if (!loaded.Contains(name)) problems.Add(name + " is not loaded");
        }
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (!defines.Split(';').Contains("XLUA_CORE_ONLY")) problems.Add("XLUA_CORE_ONLY is not in Player Settings: " + defines);
        string error;
        var preference = PluginVariantSelector.CurrentPreference(out error);
        if (error != null || preference.Count == 0 || preference[0] != PluginVariants.Core)
        {
            problems.Add("the plugin selector prefers " + string.Join(",", preference.Select(p => p ?? "default").ToArray()) + " " + error);
        }
        string result = null;
        try
        {
            using (var lua = new LuaScript(new LuaOptions { MemoryLimit = 4 << 20, InstructionLimit = 1000000 }))
            {
                lua.Load("function calcReward(level, kind) return level * (kind == 'gold' and 10 or 1) end", "probe");
                result = lua.Call<long>("calcReward", 3, "gold") + " " + lua.Load<string>("return _VERSION", "version");
                if (result != "30 Lua 5.5" && result != "30 Lua 5.4") problems.Add("LuaScript returned " + result);
                // Host functions (milestone 7): the dispatcher in the editor's Mono, and an exception back as a Lua error.
                lua.Register("host.bonus", (ref LuaCall c) => c.Return(c.GetLong(0) * 2 + (c.ArgEquals(1, "gold") ? 1 : 0)));
                lua.Register<string, string>("upper", s => s.ToUpperInvariant());
                lua.Register("fail", (ref LuaCall c) => { throw new InvalidOperationException("boom"); });
                string host = lua.Load<string>("return host.bonus(20, 'gold') .. upper('x') .. select(2, pcall(function() fail() end))", "host");
                if (host != "41Xhost:1: InvalidOperationException: boom") problems.Add("host functions returned " + host);
            }
        }
        catch (Exception e)
        {
            problems.Add("LuaScript failed: " + e);
        }
        if (problems.Count > 0)
        {
            Debug.LogError("XLUA_CORE_ONLY_PROBE_FAIL: " + string.Join("; ", problems.ToArray()));
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("XLUA_CORE_ONLY_PROBE_PASS: " + result + "; assemblies " + string.Join(",", loaded.Where(n => n.StartsWith("XLua")).ToArray())
            + "; plugin preference " + string.Join(",", preference.Select(p => p ?? "default").ToArray()));
        EditorApplication.Exit(0);
    }
}
