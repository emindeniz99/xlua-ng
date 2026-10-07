using UnityEngine;
using System;
using XLua;

public class LuaMemoryLeakCheckerTest : MonoBehaviour
{
    LuaEnv luaenv = new LuaEnv();
    Action update;
    LuaMemoryLeakChecker.Data data = null;

    void Start()
    {
        luaenv.DoString(@"
           local local_leak = {}
           global_leak = { a = {}}
           --global_leak.a.b = global_leak

           local no_leak = {}
           
           function make_leak1()
               table.insert(local_leak, 1)
               table.insert(global_leak, {})
           end

           -- Keeps creating and retaining new tables, but this is not a leak.
           function innocent()
               no_leak.a = {x = 1}
               no_leak.b = {y = 1}
           end
        ", "@leak1.lua");

        luaenv.DoString(@"
           local anthor_leak = {a = {{ b = {}}}}

           function make_leak2()
               table.insert(anthor_leak.a[1].b, 1)
           end

           local t = 1

           slow_global_leak = {}

           debug.getregistry()['ref_anthor_leak'] = anthor_leak
           
           function slow_leak()
               if t == 40 then
                   t = 0
                   table.insert(slow_global_leak, {x = 0, y = 1})
               else
                   t = t + 1
               end
           end
 
        ", "@leak2.lua");

        luaenv.DoString(@"
            shutdown_fast_leak = false

            function update()
                 if not shutdown_fast_leak then
                     make_leak1()
                     make_leak2()
                 end
                 innocent()
                 slow_leak()
            end
        ", "@main.lua");

        luaenv.Global.Get("update", out update);

        data = luaenv.StartMemoryLeakCheck();
        Debug.Log("Start, PotentialLeakCount:" + data.PotentialLeakCount);
    }

    int tick = 0;

    bool finished = false;

    void Update()
    {
        if (!finished)
        {
            tick++;
            update();
            luaenv.Tick();

            if (tick % 30 == 0)
            {
                data = luaenv.MemoryLeakCheck(data);
                Debug.Log("Update, PotentialLeakCount:" + data.PotentialLeakCount);
            }

            if (tick % 180 == 0)
            {
                Debug.Log(luaenv.MemoryLeakReport(data));

                if (tick == 180)
                {
                    //Simulate fixing the fast memory leak.
                    luaenv.Global.Set("shutdown_fast_leak", true);
                    //Start a new leak detection pass.
                    data = luaenv.StartMemoryLeakCheck();
                }
                else
                {
                    finished = true;
                    Debug.Log("Finished");
                }
            }
        }
    }

    void OnDestroy()
    {
        update = null;
        luaenv.Dispose();
    }
}
