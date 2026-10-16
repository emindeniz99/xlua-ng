using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

// A MonoBehaviour that runs Lua during a play session, for XluaPlayModeTests.
// prepare.py --fast-enter-play-mode copies this file into the runtime
// assembly. The tests enter and exit play mode several times with Unity's
// Enter Play Mode Options, where static fields outlive a play session.
namespace XluaEditorTest
{
    // What Lua writes during a session.
    public static class PlayModeProbe
    {
        public static int CoroutineSteps;
    }

    // What a session leaves behind for the next one, on purpose in statics:
    // with domain reload disabled they survive like a user's statics do.
    public static class PlayModeState
    {
        public static readonly List<string> Results = new List<string>();
        public static readonly List<Exception> Errors = new List<Exception>();

        // From an env that OnDestroy disposed.
        public static LuaFunction StaleFunction;
        public static LuaTable StaleTable;

        // Envs that OnDestroy left alive (a common user bug), and a delegate
        // into the last of them.
        public static readonly List<LuaEnv> Undisposed = new List<LuaEnv>();
        public static Combine UndisposedDelegate;

        // One env for every session, never disposed, like the static LuaEnv
        // of the LuaBehaviour example.
        public static LuaEnv Shared;

        public static void Reset()
        {
            Results.Clear();
            Errors.Clear();
            StaleFunction = null;
            StaleTable = null;
            UndisposedDelegate = null;
            PlayModeProbe.CoroutineSteps = 0;
        }
    }

    public class PlayModeHost : MonoBehaviour
    {
        public int Session;
        public bool DisposeOnDestroy = true;

        LuaEnv env;
        Combine combine;

        public bool Started { get; private set; }

        void Start()
        {
            try
            {
                Run();
            }
            catch (Exception e)
            {
                PlayModeState.Errors.Add(e);
            }
            Started = true;
        }

        void Run()
        {
            PlayModeProbe.CoroutineSteps = 0;
            env = new LuaEnv();
            env.Global.Set("host", this);
            object[] r = env.DoString(@"
                local T, Vector3 = CS.XluaEditorTest.Target, CS.UnityEngine.Vector3
                local t = T(" + Session + @")
                local go = CS.UnityEngine.GameObject('xlua-playmode-lua-object')
                go.transform.position = Vector3(1, 2, 3)
                local p = t:MakePair(2, 0.5)
                function combine(a, b) return a * 10 + #b end
                state = { session = " + Session + @" }

                -- util.cs_generator, inlined: a Lua table as an IEnumerator.
                local move_end = {}
                local gen = setmetatable({}, { __index = {
                    MoveNext = function(self)
                        self.Current = self.co()
                        if self.Current == move_end then
                            self.Current = nil
                            return false
                        end
                        return true
                    end,
                    Reset = function(self) end,
                } })
                gen.co = coroutine.wrap(function()
                    for i = 1, 3 do
                        coroutine.yield(nil)
                        CS.XluaEditorTest.PlayModeProbe.CoroutineSteps = i
                    end
                    return move_end
                end)
                host:StartCoroutine(gen)

                return t:Describe(7), p:Sum(), T.Length(p), t:Apply(combine, 3, 'abcd'), go.transform.position.y, t.Value");
            combine = env.Global.Get<Combine>("combine");
            PlayModeState.Results.Add(string.Format("session={0} describe={1} sum={2} length={3} apply={4} y={5} value={6} delegate={7}",
                Session, r[0], r[1], r[2], r[3], r[4], r[5], combine(4, "abc")));

            if (PlayModeState.Shared == null)
            {
                PlayModeState.Shared = new LuaEnv();
                PlayModeState.Shared.DoString("sessions = 0");
            }
            object[] shared = PlayModeState.Shared.DoString(@"
                sessions = sessions + 1
                last = CS.UnityEngine.GameObject('xlua-playmode-shared-object')
                return sessions, CS.XluaEditorTest.Target(" + Session + @").Value");
            PlayModeState.Results.Add("shared sessions=" + shared[0] + " value=" + shared[1]);
        }

        void OnDestroy()
        {
            if (env == null) return;
            try
            {
                if (DisposeOnDestroy)
                {
                    PlayModeState.StaleFunction = env.Global.Get<LuaFunction>("combine");
                    PlayModeState.StaleTable = env.Global.Get<LuaTable>("state");
                    combine = null;
                    env.Dispose();
                }
                else
                {
                    PlayModeState.Undisposed.Add(env);
                    PlayModeState.UndisposedDelegate = combine;
                }
            }
            catch (Exception e)
            {
                PlayModeState.Errors.Add(e);
            }
            env = null;
        }
    }

    public static class PlayModeConfig
    {
        [LuaCallCSharp]
        public static List<Type> LuaCallCSharp = new List<Type>()
        {
            typeof(PlayModeHost),
            typeof(PlayModeProbe),
        };

        [CSharpCallLua]
        public static List<Type> CSharpCallLua = new List<Type>()
        {
            typeof(IEnumerator),
        };
    }
}
