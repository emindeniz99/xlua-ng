using System;
using System.Collections.Generic;

namespace XLuaTest
{
    // Generated classes call Register from [RuntimeInitializeOnLoadMethod], the way
    // generated xLua wrappers would hook into LuaEnv without reflection.
    public static class Registry
    {
        public struct Entry
        {
            public string Assembly;
            public string Exports;
            public Func<string> Invoke;
        }

        static readonly List<Entry> entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => entries;

        public static void Register(string assembly, string exports, Func<string> invoke)
        {
            entries.Add(new Entry { Assembly = assembly, Exports = exports, Invoke = invoke });
            UnityEngine.Debug.Log("XLUA_SG_REGISTERED " + assembly + " exports=" + exports);
        }
    }
}
