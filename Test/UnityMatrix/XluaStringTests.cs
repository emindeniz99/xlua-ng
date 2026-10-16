using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XLua;

// Lua strings are bytes. xLua reads them as UTF-8 with U+FFFD for invalid
// bytes, so GBK text or binary data never makes LuaAPI.lua_tostring throw
// (https://github.com/Tencent/xLua/issues/1203). The Lua literals spell the
// bytes with escapes; the C# strings use \u escapes.
public sealed class XluaStringTests
{
    const string Turkish = "şeğiıİçöü";
    const string TurkishLua = @"'\197\159e\196\159i\196\177\196\176\195\167\195\182\195\188'";

    // print is StaticLuaCallbacks.Print, which logs "LUA: " and its arguments.
    [Test]
    public void PrintAcceptsInvalidUtf8()
    {
        using (var env = new LuaEnv())
        {
            LogAssert.Expect(LogType.Log, "LUA: a�b\t1\t" + Turkish);
            env.DoString(@"print('a\255b', 1, " + TurkishLua + ")");
        }
    }

    [Test]
    public void LuaStringsReadAsUtf8()
    {
        using (var env = new LuaEnv())
        {
            object[] r = env.DoString(@"return 'a\255b', " + TurkishLua + @", 'x\0y', '\240\159\152\128', '\228\184\173\230\150\135'");
            Assert.AreEqual("a�b", r[0]);
            Assert.AreEqual(Turkish, r[1]);
            Assert.AreEqual("x\0y", r[2]);
            Assert.AreEqual("\U0001F600", r[3]);
            Assert.AreEqual("中文", r[4]);
        }
    }

    [Test]
    public void LuaErrorWithInvalidUtf8BecomesLuaException()
    {
        using (var env = new LuaEnv())
        {
            var e = Assert.Throws<LuaException>(() => env.DoString(@"error('bad \255 byte', 0)"));
            StringAssert.StartsWith("bad � byte", e.Message);
        }
    }

    [Test]
    public void TurkishTextRoundTrips()
    {
        using (var env = new LuaEnv())
        {
            env.Global.Set("turkish", Turkish);
            object[] r = env.DoString(@"
                local T = CS.XluaEditorTest.Text
                local literal = " + TurkishLua + @"
                local long = string.rep(turkish, 30)
                return turkish == literal, #turkish, T.Echo(turkish), T.CodeUnits(literal), T.Echo(long) == long, T.Echo(long)");
            Assert.AreEqual(true, r[0], "C# string pushed as UTF-8");
            Assert.AreEqual(16, Convert.ToInt32(r[1]));
            Assert.AreEqual(Turkish, r[2]);
            Assert.AreEqual("015F 0065 011F 0069 0131 0130 00E7 00F6 00FC", r[3]);
            Assert.AreEqual(true, r[4]);
            Assert.AreEqual(string.Concat(Enumerable.Repeat(Turkish, 30).ToArray()), r[5]);
        }
    }
}
