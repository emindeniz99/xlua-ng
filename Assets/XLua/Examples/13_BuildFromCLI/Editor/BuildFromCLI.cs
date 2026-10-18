using System;
using UnityEditor;
using UnityEngine;
using XLua;

namespace XLuaTest
{
    public static class BuildFromCLI
    {
        /// <summary>
        /// Invoke this method from the Unity menu.
        /// </summary>
        [MenuItem("XLua/Examples/13_BuildFromCLI")]
        public static void BuildFromUnityMenu()
        {
            var outputDir = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length) + "/output";
            var packageName = "xLuaGame.exe";
            build(outputDir, packageName);
        }

        /// <summary>
        /// Invoke this method from the command line.
        /// </summary>
        public static void Build()
        {
            var outputDir = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length) + "/output";
            var packageName = "xLuaGame.exe";
            build(outputDir, packageName);
        }

        private static void build(string outputDir, string packageName)
        {
            // The bindings are generated while the scripts compile (the xLua
            // source generator), so there is nothing to generate first.
            Debug.Log("Build started; output directory: " + outputDir);

            var levels = new string[0];
            var locationPathName = string.Format("{0}/{1}", outputDir, packageName);
            var target = BuildTarget.StandaloneWindows64;
            var options = BuildOptions.None;
            BuildPipeline.BuildPlayer(levels, locationPathName, target, options);
            Debug.Log("Build completed");
        }
    }
}
