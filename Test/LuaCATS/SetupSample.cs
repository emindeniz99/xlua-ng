// Fixture for the "Validate Setup" check in Test/Native/luacats_check.py:
// configuration that names types and members that do not exist. Compiled
// against XLua.Mini.dll; never run.
using System;
using System.Collections.Generic;
using XLua;

namespace LuaCATSSetup
{
    public class Target
    {
        public int Kept;
        public void Run(int times) { }
    }

    public static class StaleConfig
    {
        [BlackList]
        public static List<List<string>> BlackList = new List<List<string>>()
        {
            new List<string>() { "LuaCATSSetup.Target", "Kept" },
            new List<string>() { "LuaCATSSetup.Target", "Run", "System.Int32" },
            new List<string>() { "LuaCATSSetup.Removed", "Field" },
            new List<string>() { "LuaCATSSetup.Target", "Renamed" },
            new List<string>() { "LuaCATSSetup.Target", "Run", "System.String" },
            new List<string>() { "LuaCATSSetup.Target" },
        };

        [DoNotGen]
        public static Dictionary<Type, List<string>> DoNotGen = new Dictionary<Type, List<string>>()
        {
            { typeof(Target), new List<string>() { "Kept", "Gone" } },
        };
    }
}
