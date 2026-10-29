// Fixture for hotfix_check.py, step 7: the process-wide IntKey id list. Mono's
// mcs compiles it with the xLua sources (XLUA_GENERAL, HOTFIX_ENABLE) and
// without the source generator, so the assembly that holds xLua has no
// HotfixBridges: XLuaHotfixInject then numbers its IntKey ids in the
// process-wide list (HotfixDelegateBridge.Set, and on iOS the native flags of
// Assets/Plugins/iOS/HotfixFlags.cpp), and only HotfixFlag.Inline types can be
// injected, since there are no hotfix bridges. This is the configuration that
// still reaches the list: xLua compiled into the game's assembly (the way back
// to Assembly-CSharp, docs/MIGRATING.md) with no [Hotfix] attribute there that
// would make the generator write HotfixBridges (no generator, or only Inline
// types named by [Hotfix] configuration lists).
//
//   HotfixFixtureShared.exe baseline        the original behaviour only
//   HotfixFixtureShared.exe patch <id map>  also patches Keyed through the ids of
//                                           the map (no "<assembly>" entry) and
//                                           removes the patches, then sets ids of
//                                           the list from four threads at once
//
// Ends with HOTFIX_SHARED_FIXTURE_PASS or exits 1.
using System;
using System.IO;
using System.Threading;
using XLua;

namespace HotfixShared
{
    [Hotfix(HotfixFlag.IntKey | HotfixFlag.Inline)]
    public class Keyed
    {
        public int Neg(int a) { return a; }

        public string Name(string s) { return s; }

        public string Echo<T>(T x) { return "echo " + x; }
    }

    public static class Program
    {
        static int failures = 0, checks = 0;

        static void Check<T>(string name, T actual, T expected)
        {
            checks++;
            if (!Equals(actual, expected))
            {
                failures++;
                Console.WriteLine("FAIL " + name + ": " + actual + ", expected " + expected);
            }
        }

        static void CheckOriginal(string phase)
        {
            var k = new Keyed();
            Check(phase + " Keyed.Neg", k.Neg(5), 5);
            Check(phase + " Keyed.Name", k.Name("x"), "x");
            Check(phase + " Keyed.Echo<int>", k.Echo(3), "echo 3");
        }

        // What a Lua function becomes when Lua passes it as a DelegateBridge.
        public static DelegateBridge Kept;

        public static void Keep(DelegateBridge bridge) { Kept = bridge; }

        // Sets the ids of Keyed's methods from the map, as xlua.util.auto_id_map()
        // does for an entry without "<assembly>".
        static void Patch(LuaEnv env, string mapPath)
        {
            env.Global.Set("id_map_source", File.ReadAllText(mapPath));
            env.DoString(@"
                local map = assert(load(id_map_source, '@hotfix_id_map'))()
                local info = assert(map['HotfixShared.Keyed'], 'HotfixShared.Keyed is not in the id map')
                assert(info['<assembly>'] == nil, 'the id map names an assembly: IntKey did not use the process-wide list')
                local function set(name, f)
                    for _, id in ipairs(assert(info[name], name .. ' has no id')) do
                        CS.XLua.HotfixDelegateBridge.Set(id, f)
                    end
                end
                set('Neg', function(self, a) return -a end)
                set('Name', function(self, s) return 'patched ' .. s end)
                set('Echo', function(self, x) return 'patched echo ' .. tostring(x) end)
                function unpatch_keyed()
                    set('Neg', nil)
                    set('Name', nil)
                    set('Echo', nil)
                end
            ");
        }

        // Four threads set ids of the process-wide list at once, each growing it.
        // Before 2026-10-29 Set replaced the list without a lock, so a thread could
        // publish a copy that lacked another thread's ids.
        static void SetFromThreads(LuaEnv env)
        {
            env.DoString("CS.HotfixShared.Program.Keep(function() end)");
            Check("a Lua function as a DelegateBridge", Kept != null, true);
            const int threads = 4, perThread = 1500, first = 1000;
            var workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                int start = first + t;
                workers[t] = new Thread(() =>
                {
                    for (int i = 0; i < perThread; i++) HotfixDelegateBridge.Set(start + i * threads, Kept);
                });
            }
            foreach (var w in workers) w.Start();
            foreach (var w in workers) w.Join();
            int lost = 0;
            for (int id = first; id < first + threads * perThread; id++)
            {
                if (HotfixDelegateBridge.Get(id) != Kept || !HotfixDelegateBridge.xlua_get_hotfix_flag(id)) lost++;
            }
            Check("ids set from four threads that were lost", lost, 0);
            for (int id = first; id < first + threads * perThread; id++) HotfixDelegateBridge.Set(id, null);
            Check("an id set to null reads unset", HotfixDelegateBridge.xlua_get_hotfix_flag(first), false);
            // The env refuses Dispose while a bridge to a Lua function is alive.
            Kept = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public static int Main(string[] args)
        {
            CheckOriginal("original");
            if (args.Length > 1 && args[0] == "patch")
            {
                var env = new LuaEnv();
                Patch(env, args[1]);
                var k = new Keyed();
                Check("patched Keyed.Neg", k.Neg(5), -5);
                Check("patched Keyed.Name", k.Name("x"), "patched x");
                Check("patched Keyed.Echo<int> (generic method)", k.Echo(3), "patched echo 3");
                env.DoString("unpatch_keyed()");
                CheckOriginal("restored");
                SetFromThreads(env);
                CheckOriginal("after the threads");
                env.Dispose();
            }
            if (failures > 0)
            {
                Console.WriteLine("HOTFIX_SHARED_FIXTURE_FAIL: " + failures + " of " + checks + " checks failed");
                return 1;
            }
            Console.WriteLine("HOTFIX_SHARED_FIXTURE_PASS: " + checks + " checks");
            return 0;
        }
    }
}
