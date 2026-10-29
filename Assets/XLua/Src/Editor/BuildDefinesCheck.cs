/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// A player build fails when its scripting defines differ from the ones the
// Editor's scripts were compiled with in a define that changes what xLua's
// build steps take from those scripts (docs/design/source-generator.md,
// "Defines set in the build launch (2026-10-29)").
//
// The build steps read the assemblies loaded in the Editor: the source
// generator's link.xml (SourceGenLinkXml) and obfuz rules (ObfuzAdapter), the
// bindings check (SourceGenBuildCheck) and the hotfix injector, which exists
// only when the Editor was compiled with HOTFIX_ENABLE. A build script that
// sets the defines and builds in the same Editor launch, before Unity
// recompiles, gets all of them for the old defines. Switching from minimal to
// legacy that way gives a legacy player the narrow link.xml of minimal, and
// IL2CPP strips the private members that Lua then reads as nil on the device.
//
// What the Editor was compiled with is recorded after each domain reload, which
// follows each compilation: the Player Settings defines of the active build
// target (and, from Unity 6000.3, of the active build profile). The build's are
// those of its target, plus BuildPlayerOptions.extraScriptingDefines, which the
// Editor never compiles with. Defines from csc.rsp and asmdef version defines
// reach both compilations alike and are not compared.

#if !XLUA_GENERAL
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace XLua
{
    public static class BuildDefinesCheck
    {
        // The defines that change what xLua's build steps take from the Editor's
        // assemblies: the generator's link.xml and obfuz rules (LinkXml.cs
        // PreserveBoundTypes, Wrappers.cs GcmDefined and NoReflectionDefined)
        // and whether the hotfix injector exists (HOTFIX_ENABLE).
        public static readonly string[] Watched =
        {
            "XLUA_PRESET_MINIMAL", "HOTFIX_ENABLE", "XLUA_REFLECTION", "XLUA_NO_REFLECTION", "GEN_CODE_MINIMIZE", "XLUA_NO_GCM",
        };

        internal static string[] compiledWith;
        internal static string compiledFor;

        [InitializeOnLoadMethod]
        static void RecordCompiledDefines()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var named = Named(group, group == BuildTargetGroup.Standalone
                && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server);
            compiledFor = named.TargetName;
            compiledWith = Relevant(Defines(named));
        }

        internal static NamedBuildTarget Named(BuildTargetGroup group, bool server)
        {
            return server ? NamedBuildTarget.Server : NamedBuildTarget.FromBuildTargetGroup(group);
        }

        internal static IEnumerable<string> Defines(NamedBuildTarget named)
        {
            IEnumerable<string> defines = PlayerSettings.GetScriptingDefineSymbols(named).Split(';');
#if UNITY_6000_3_OR_NEWER
            var profile = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
            if (profile != null && profile.scriptingDefines != null) defines = defines.Concat(profile.scriptingDefines);
#endif
            return defines;
        }

        static string[] Relevant(IEnumerable<string> defines)
        {
            return defines.Where(d => d != null).Select(d => d.Trim()).Where(d => Watched.Contains(d))
                .Distinct().OrderBy(d => d, StringComparer.Ordinal).ToArray();
        }

        // Null when the build compiles the watched defines as the Editor did,
        // else the reason the build fails.
        public static string Problem(IEnumerable<string> compiled, string compiledTarget, IEnumerable<string> build, string buildTarget,
            IEnumerable<string> extra)
        {
            var before = Relevant(compiled);
            var after = Relevant(build.Concat(extra ?? new string[0]));
            var added = after.Except(before).ToArray();
            var removed = before.Except(after).ToArray();
            if (added.Length == 0 && removed.Length == 0) return null;
            var changes = added.Select(d => d + " added").Concat(removed.Select(d => d + " removed"));
            var extraWatched = Relevant(extra ?? new string[0]).Except(before).ToArray();
            return "xLua: this player build's scripting defines differ from the ones the Editor's scripts were compiled with: "
                + string.Join(", ", changes.ToArray())
                + " (the Editor: Player Settings of " + compiledTarget + "; the build: Player Settings of " + buildTarget
                + (extra != null && extra.Any() ? " and BuildPlayerOptions.extraScriptingDefines" : "") + "). "
                + "xLua's link.xml, obfuz rules, bindings check and hotfix injection come from the Editor's compiled scripts, so this "
                + "player would get those of the old defines; a legacy player built that way after minimal loses the private members "
                + "Lua reads, which are nil on the device. Let Unity recompile before building: set the defines and build in separate "
                + "Editor launches (or with -buildTarget for another platform), or in the Editor build after the compilation the "
                + "change starts (AssetDatabase.Refresh, then wait until EditorApplication.isCompiling is false and the scripts reload)."
                + (extraWatched.Length > 0 ? " BuildPlayerOptions.extraScriptingDefines cannot add " + string.Join(", ", extraWatched)
                    + ", since the Editor never compiles with it; put it in the Player Settings." : "");
        }
    }

    // The build layer: before Unity compiles the player's scripts.
    class BuildDefinesCheckProcessor : BuildPlayerProcessor
    {
        public override int callbackOrder { get { return 0; } }

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            if (BuildDefinesCheck.compiledWith == null) return;
            var options = context.BuildPlayerOptions;
            var group = options.targetGroup != BuildTargetGroup.Unknown ? options.targetGroup : BuildPipeline.GetBuildTargetGroup(options.target);
            // StandaloneBuildSubtarget: Player = 0 and Server = 1 in 2021.3; Default = 0 (the
            // Editor's setting), Player = 2 and Server = 1 in Unity 6.
            bool server = group == BuildTargetGroup.Standalone
                && (options.subtarget == (int)StandaloneBuildSubtarget.Server
                    || (Enum.GetName(typeof(StandaloneBuildSubtarget), options.subtarget) == "Default"
                        && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server));
            var named = BuildDefinesCheck.Named(group, server);
            string problem = BuildDefinesCheck.Problem(BuildDefinesCheck.compiledWith, BuildDefinesCheck.compiledFor, BuildDefinesCheck.Defines(named), named.TargetName, options.extraScriptingDefines);
            if (problem != null) throw new BuildFailedException(problem);
        }
    }
}
#endif
