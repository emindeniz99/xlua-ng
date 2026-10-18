/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// "XLua/Project Layout": which assemblies xLua compiles into in the Assets
// layout (docs/MIGRATING.md, "xLua in its own assemblies").
//
// By default xLua ships four assembly definitions, so it compiles into its own
// assemblies:
//
//   <xLua>/Core/XLua.Core.asmdef                 XLua.Core
//   <xLua>/Core/Editor/XLua.Core.Editor.asmdef   XLua.Core.Editor (Editor only)
//   <xLua>/Src/XLua.asmdef                       XLua
//   <xLua>/Src/Editor/XLua.Editor.asmdef         XLua.Editor (Editor only)
//
// "Use Assembly-CSharp (no asmdefs)" moves the four files, with their .meta
// files, to <xLua>/Asmdef~/ (Unity does not import folders whose name ends in
// "~"), so xLua compiles into Assembly-CSharp and Assembly-CSharp-Editor as
// upstream xLua did. "Use asmdefs" moves them back. The .meta files travel with
// the asmdefs, so the GUIDs, and the references of other asmdefs to them, stay.
//
// The asmdefs are moved, not switched off with define constraints: an asmdef
// whose define constraints fail leaves its scripts out of every assembly, so
// xLua would not compile at all (XLUA_CORE_ONLY uses that on purpose).
//
// <xLua> is found by the GUIDs of the Src and Core folders, so a project that
// moved Assets/XLua elsewhere works too. A package install cannot be changed:
// the UPM package always has its asmdefs.

#if UNITY_EDITOR && !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace XLua
{
    public static class ProjectLayout
    {
        public const string ParkingFolder = "Asmdef~";

        // Folder (relative to <xLua>) and file of each asmdef, the GUID of that
        // folder's .meta in the repository, and the asmdef's own GUID.
        struct Entry
        {
            public string Folder, File, FolderGuid, Guid;

            public Entry(string folder, string file, string folderGuid, string guid)
            {
                Folder = folder;
                File = file;
                FolderGuid = folderGuid;
                Guid = guid;
            }
        }

        static readonly Entry[] Entries =
        {
            new Entry("Core", "XLua.Core.asmdef", "dae77efb959f4806a24be5168678a5e9", "4b076258bb6e496aa6f3404327f20852"),
            new Entry("Core/Editor", "XLua.Core.Editor.asmdef", "738022cf553c40a49634f95d7804c2b6", "646ad94d870c4544bbd80a0b971efa07"),
            new Entry("Src", "XLua.asmdef", "ac2f50d02ed9ec24b8fcc1921bfb244c", "d67ecd89999f4eea8a9ab3c0ecd8057d"),
            new Entry("Src/Editor", "XLua.Editor.asmdef", "1e53aa922da0a00469e5760902833e71", "ab5755dc6ef2406fae4ec66b0178bc2d"),
        };

        public enum Layout
        {
            Asmdefs,        // every asmdef in its folder (the default)
            AssemblyCSharp, // every asmdef in Asmdef~
            Mixed,          // some here, some there
            Package,        // installed as a UPM package (read-only)
            Unknown,        // xLua's Src and Core folders not found
        }

        // <xLua>: the folder that holds Src and Core, from their GUIDs, or else
        // from the place of this script (<xLua>/Core/Editor/ProjectLayout.cs),
        // for a copy of xLua whose folders got new GUIDs.
        public static string Root()
        {
            foreach (var entry in Entries)
            {
                if (entry.Folder.Contains("/")) continue;
                string folder = AssetDatabase.GUIDToAssetPath(entry.FolderGuid);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    return Path.GetDirectoryName(folder).Replace('\\', '/');
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("ProjectLayout t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                const string suffix = "/Core/Editor/ProjectLayout.cs";
                if (path.EndsWith(suffix, StringComparison.Ordinal)) return path.Substring(0, path.Length - suffix.Length);
            }
            return null;
        }

        static string Placed(string root, Entry entry)
        {
            return root + "/" + entry.Folder + "/" + entry.File;
        }

        static string Parked(string root, Entry entry)
        {
            return root + "/" + ParkingFolder + "/" + entry.Folder + "/" + entry.File;
        }

        public static Layout Current()
        {
            string root = Root();
            if (root == null) return Layout.Unknown;
            if (root.StartsWith("Packages/", StringComparison.Ordinal)) return Layout.Package;
            int placed = 0, parked = 0;
            foreach (var entry in Entries)
            {
                if (File.Exists(Placed(root, entry))) placed++;
                if (File.Exists(Parked(root, entry))) parked++;
            }
            if (parked == 0 && placed > 0) return Layout.Asmdefs;
            if (placed == 0 && parked > 0) return Layout.AssemblyCSharp;
            return placed == 0 ? Layout.Unknown : Layout.Mixed;
        }

        // Moves every asmdef found in its folder to Asmdef~; throws when it cannot.
        // For -executeMethod XLua.ProjectLayout.UseAssemblyCSharp.
        public static void UseAssemblyCSharp()
        {
            string root = writableRoot();
            // The source generator is scoped by the XLua asmdef. Without it Unity
            // runs the generator in every assembly (build/SOURCEGEN_SPIKE.md,
            // "Shipping"), Assembly-CSharp among them, which then holds xLua:
            // the bindings stay generated, at the cost of the generator's check
            // in the assemblies that do not use xLua. Until the legacy generator
            // went (milestone M11) this refused, since Generate Code was there.
            var generators = AssetDatabase.FindAssets("XLua.SourceGenerator").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith("/XLua.SourceGenerator.dll", StringComparison.OrdinalIgnoreCase)).ToList();
            move(root, true);
            if (generators.Count > 0)
            {
                Debug.Log("xLua project layout: xLua's source generator (" + string.Join(", ", generators.ToArray())
                    + ") now runs in every assembly, as no asmdef scopes it; it generates bindings in Assembly-CSharp and in "
                    + "the assemblies that reference it.");
            }
        }

        // Moves every asmdef in Asmdef~ back to its folder; throws when it cannot.
        // For -executeMethod XLua.ProjectLayout.UseAsmdefs.
        public static void UseAsmdefs()
        {
            move(writableRoot(), false);
        }

        static string writableRoot()
        {
            string root = Root();
            if (root == null)
            {
                throw new InvalidOperationException("cannot find xLua's Src or Core folder (by the GUIDs of Src.meta and Core.meta)");
            }
            if (root.StartsWith("Packages/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("xLua is installed as a package (" + root + "), which always compiles into its own "
                    + "assemblies; copy xLua into Assets to choose the layout");
            }
            return root;
        }

        static void move(string root, bool park)
        {
            var moves = new List<KeyValuePair<string, string>>();
            foreach (var entry in Entries)
            {
                string from = park ? Placed(root, entry) : Parked(root, entry);
                string to = park ? Parked(root, entry) : Placed(root, entry);
                if (!File.Exists(from)) continue;
                // A folder deleted on purpose (Src in a core-only project) keeps its asmdef parked.
                if (!park && !Directory.Exists(root + "/" + entry.Folder)) continue;
                if (File.Exists(to) || File.Exists(to + ".meta"))
                {
                    throw new InvalidOperationException(to + " exists already; remove one of the two copies of " + entry.File
                        + " (keep the one whose .meta has guid " + entry.Guid + ")");
                }
                if (!File.Exists(from + ".meta"))
                {
                    throw new InvalidOperationException(from + ".meta is missing; without it the asmdef gets a new GUID and asmdefs "
                        + "that reference it by GUID break");
                }
                moves.Add(new KeyValuePair<string, string>(from, to));
            }
            if (moves.Count == 0)
            {
                Debug.Log("xLua project layout: nothing to move; xLua already compiles into "
                    + (park ? "Assembly-CSharp" : "its own assemblies") + ".");
                return;
            }
            foreach (var m in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m.Value));
                File.Move(m.Key, m.Value);
                File.Move(m.Key + ".meta", m.Value + ".meta");
                Debug.Log("xLua project layout: moved " + m.Key + " to " + m.Value);
            }
            if (!park) removeEmptyFolders(root + "/" + ParkingFolder);
            AssetDatabase.Refresh();
            Debug.Log(park
                ? "xLua project layout: xLua now compiles into Assembly-CSharp and Assembly-CSharp-Editor (upstream xLua's layout). "
                  + "Asmdefs of yours that referenced XLua or XLua.Core cannot see it any more; scripts without an asmdef can."
                : "xLua project layout: xLua now compiles into XLua.Core, XLua, XLua.Core.Editor and XLua.Editor. Asmdefs of yours "
                  + "that use xLua need \"XLua\" in their references (and \"XLua.Core\" with the source generator).");
        }

        static void removeEmptyFolders(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var sub in Directory.GetDirectories(folder)) removeEmptyFolders(sub);
            if (!Directory.GetFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }

        [MenuItem("XLua/Project Layout/Use Assembly-CSharp (no asmdefs)", false, 220)]
        static void UseAssemblyCSharpMenu()
        {
            try
            {
                UseAssemblyCSharp();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError("xLua project layout: " + e.Message);
            }
        }

        [MenuItem("XLua/Project Layout/Use Assembly-CSharp (no asmdefs)", true)]
        static bool CanUseAssemblyCSharp()
        {
            var layout = Current();
            return layout == Layout.Asmdefs || layout == Layout.Mixed;
        }

        [MenuItem("XLua/Project Layout/Use asmdefs", false, 221)]
        static void UseAsmdefsMenu()
        {
            try
            {
                UseAsmdefs();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError("xLua project layout: " + e.Message);
            }
        }

        [MenuItem("XLua/Project Layout/Use asmdefs", true)]
        static bool CanUseAsmdefs()
        {
            var layout = Current();
            return layout == Layout.AssemblyCSharp || layout == Layout.Mixed;
        }

        // For -executeMethod: logs XLUA_PROJECT_LAYOUT: <layout> <assembly of LuaDLL.Lua>.
        public static void Report()
        {
            Debug.Log("XLUA_PROJECT_LAYOUT: " + Current() + " " + typeof(XLua.LuaDLL.Lua).Assembly.GetName().Name);
        }
    }
}
#endif
