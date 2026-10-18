/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#if !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XLua;

namespace CSObjectWrapEditor
{
    // "XLua/Lua Memory": takes memory snapshots of a running LuaEnv in Play Mode and shows one
    // snapshot or what changed between two (LuaMemoryDiagnostics, Assets/XLua/Src/LuaMemorySnapshot.cs).
    // The usual check for a leak: snapshot A, open and close the UI or scene, snapshot B, then read
    // the diff for grown tables, new objects and the C# objects Lua started holding.
    public class LuaMemoryWindow : EditorWindow
    {
        LuaMemorySnapshot snapshotA, snapshotB;
        LuaMemoryDiff diff;
        int envIndex;
        int tab;
        int top = 50;
        bool fullGc = true;
        bool csharpGc;
        string filter = "";
        string error;
        Vector2 scroll;
        string[] lines = new string[0];
        List<LuaMemoryCSharpObject> unityObjects = new List<LuaMemoryCSharpObject>();

        [MenuItem("XLua/Lua Memory", false, 200)]
        public static void Open()
        {
            GetWindow<LuaMemoryWindow>(false, "Lua Memory", true);
        }

        void OnGUI()
        {
            IList<LuaEnv> envs = LuaMemoryDiagnostics.LiveEnvironments();
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to take snapshots of the running LuaEnv.", MessageType.Info);
            }
            if (envs.Count == 0)
            {
                EditorGUILayout.HelpBox("No LuaEnv is alive.", MessageType.None);
            }
            else
            {
                if (envs.Count > 1)
                {
                    var names = new string[envs.Count];
                    for (int i = 0; i < names.Length; i++) names[i] = "LuaEnv " + (i + 1);
                    envIndex = EditorGUILayout.Popup("LuaEnv", Mathf.Clamp(envIndex, 0, envs.Count - 1), names);
                }
                else
                {
                    envIndex = 0;
                }
                fullGc = EditorGUILayout.Toggle(new GUIContent("Full Lua GC first",
                    "collectgarbage(\"collect\") before the walk, so that only live objects are listed"), fullGc);
                csharpGc = EditorGUILayout.Toggle(new GUIContent("C# GC first",
                    "GC.Collect, finalizers and LuaEnv.Tick before the walk, so that LuaTable, LuaFunction and "
                    + "delegates C# no longer uses release their Lua side"), csharpGc);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Take snapshot A"))
                {
                    snapshotA = Take(envs[envIndex]);
                    snapshotB = null;
                    diff = null;
                    Show(0);
                }
                GUI.enabled = snapshotA != null;
                if (GUILayout.Button("Take snapshot B and compare"))
                {
                    snapshotB = Take(envs[envIndex]);
                    diff = null;
                    Show(2);
                }
                GUI.enabled = true;
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    snapshotA = snapshotB = null;
                    diff = null;
                    error = null;
                    Show(0);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            int newTab = GUILayout.Toolbar(tab, new[] { "Snapshot A", "Snapshot B", "Diff A to B" });
            EditorGUILayout.BeginHorizontal();
            string newFilter = EditorGUILayout.TextField("Filter", filter);
            int newTop = EditorGUILayout.IntField("Lines per section", top, GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();
            if (newTab != tab || newFilter != filter || newTop != top)
            {
                filter = newFilter;
                top = Mathf.Max(1, newTop);
                Show(newTab);
            }
            DrawReport();
        }

        LuaMemorySnapshot Take(LuaEnv env)
        {
            try
            {
                var options = new LuaMemorySnapshotOptions();
                options.FullGc = fullGc;
                options.CollectCSharpGarbage = csharpGc;
                error = null;
                return env.TakeMemorySnapshot(options);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        // Builds the lines of the selected tab.
        void Show(int newTab)
        {
            tab = newTab;
            string text = null;
            unityObjects.Clear();
            if (tab == 0 && snapshotA != null) text = snapshotA.Report(top);
            if (tab == 1 && snapshotB != null) text = snapshotB.Report(top);
            if (tab == 2 && snapshotA != null && snapshotB != null)
            {
                if (diff == null) diff = LuaMemoryDiagnostics.Diff(snapshotA, snapshotB);
                text = diff.Report(top);
                foreach (var c in diff.AddedCSharpObjects)
                {
                    if (c.Target is UnityEngine.Object && unityObjects.Count < top) unityObjects.Add(c);
                }
            }
            if (text == null)
            {
                text = tab == 2 ? "Take snapshots A and B to compare them." : "No snapshot yet.";
            }
            var kept = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (filter.Length == 0 || l.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) kept.Add(l);
            }
            lines = kept.ToArray();
            scroll = Vector2.zero;
        }

        void DrawReport()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy report", GUILayout.Width(110)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", lines);
            }
            if (GUILayout.Button("Save report...", GUILayout.Width(110)))
            {
                string path = EditorUtility.SaveFilePanel("Save Lua memory report", "", "lua-memory.txt", "txt");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, string.Join("\n", lines));
            }
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var style = EditorStyles.label;
            foreach (string line in lines)
            {
                EditorGUILayout.SelectableLabel(line, style, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            if (unityObjects.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Unity objects Lua started holding", EditorStyles.boldLabel);
                foreach (var c in unityObjects)
                {
                    var target = c.Target as UnityEngine.Object;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(target, typeof(UnityEngine.Object), true, GUILayout.Width(200));
                    EditorGUILayout.SelectableLabel(c.Path ?? "(not reachable)", GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void OnDisable()
        {
            snapshotA = snapshotB = null;
            diff = null;
        }
    }
}
#endif
