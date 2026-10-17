using UnityEngine;

namespace Game
{
    // Runs in the player after the generated BeforeSceneLoad registrations. Uses only the
    // registry the generated code filled, no reflection, so stripping is not hidden.
    public static class SpikePlayerCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Check()
        {
            if (Application.isEditor)
                return;
            bool game = false, user = false;
            foreach (XLuaTest.Registry.Entry e in XLuaTest.Registry.Entries)
            {
                string r;
                try { r = e.Invoke(); }
                catch (System.Exception ex) { r = "EXCEPTION " + ex; }
                Debug.Log("XLUA_SG_INVOKE " + e.Assembly + ": " + r);
                if (e.Assembly == "Assembly-CSharp" && r.Contains("Game.UserThing.Answer()=42") && r.Contains("UnityEngine.Vector3.back="))
                    game = true;
                if (e.Assembly == "UserGame" && r.Contains("UserGame.UserGameThing.Hello=hello"))
                    user = true;
            }
            // Reflection-only member. Type and member names are built at run time, as in
            // xLua's reflection mode (names come from Lua): UnityLinker keeps members it can
            // see through typeof(T).GetMethod(...), so a literal would hide the effect.
            string typeName = new string("Game.UserThing".ToCharArray());
            string name = "Only" + new string(new[] { 'R', 'e', 'f', 'l', 'e', 'c', 't', 'e', 'd' });
            System.Type type = System.Type.GetType(typeName + ", Assembly-CSharp");
            bool reflected = type != null && type.GetMethod(name) != null;
            Debug.Log("XLUA_SG_REFLECTED_ONLY_MEMBER present=" + reflected);
            Debug.Log("XLUA_SG_BACKEND " + (System.Type.GetType("Mono.Runtime") != null ? "mono" : "il2cpp")
                + " entries=" + XLuaTest.Registry.Entries.Count);
            Debug.Log(game && user ? "XLUA_SG_PLAYER_PASS" : "XLUA_SG_PLAYER_FAIL game=" + game + " user=" + user);
            Application.Quit(game && user ? 0 : 1);
        }
    }
}
