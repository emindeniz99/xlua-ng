using UnityEngine;
using System.Collections;
using XLua;
using System.IO;

namespace XLuaTest
{
    public class SignatureLoaderTest : MonoBehaviour
    {
        public static string PUBLIC_KEY = "BgIAAACkAABSU0ExAAwAAAEAAQAHtcljD8KtvHl37G5kokkI0tNDLpfP6TKCtsuD53mwnmj0+rj3NQRkIhPjvof/DtTCm1rwQjLXWBEJ/uBQEOQGuU8/TRcRb+c4lUw7EA92fdRRRbplXHVl0K1LOWiwC5GIoBrEY0O1NYn04ej7hpUSCtIF1zYAx+rm9fwua7kqzTtZDtfJyX4uil6fH9wa35yzvMhvbH5q0Ez9kI3M422Pz73rQx9CnvvrbykJRBB8ZFMagvJyUi59lGeWyYMaovEVa1IHF0/iwv1MypS7my29MiuFLtkmlXlpaU623LGw06/yvi/vOa21+v/m6fTUKHjyZAi54C1+xXHmDJZlhTwYyc9BGkLkM86ixGDB78eUqUKvTSqXn4aMIq799w0BOXx9qstc1K6Eqp4Uwj6AL4y4pWoXs/SNZmFDi/VbwEWVmKg3juU/80ANi+3NPmMak5M/gmgJV3EK6p03/cOgOXXbov5M3/3njtAZEA3xmeq6odfvnPTLPunBh94+ewSrApc=";

        // Use this for initialization
        void Start()
        {
            LuaEnv luaenv = new LuaEnv();
#if UNITY_EDITOR
            luaenv.AddLoader(new SignatureLoader(PUBLIC_KEY, (ref string filepath) =>
            {
                filepath = Application.dataPath + "/XLua/Examples/10_SignatureLoader/" + filepath.Replace('.', '/') + ".lua";
                if (File.Exists(filepath))
                {
                    return File.ReadAllBytes(filepath);
                }
                else
                {
                    return null;
                }
            }));
#else //Allow testing on mobile devices too
        luaenv.AddLoader(new SignatureLoader(PUBLIC_KEY, (ref string filepath) =>
        {
            filepath = filepath.Replace('.', '/') + ".lua";
            TextAsset file = (TextAsset)Resources.Load(filepath);
            if (file != null)
            {
                return file.bytes;
            }
            else
            {
                return null;
            }
        }));
#endif
            luaenv.DoString(@"
            require 'signatured1'
            require 'signatured2'
        ");
            luaenv.Dispose();
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
