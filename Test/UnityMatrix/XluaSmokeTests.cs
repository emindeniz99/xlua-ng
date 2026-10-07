using NUnit.Framework;

public sealed class XluaSmokeTests
{
    [Test]
    public void Lua55BridgeRunsInTheEditor()
    {
        var result = XluaSmoke.Run();
        StringAssert.StartsWith("XLUA_NG_SMOKE_PASS: Lua 5.5", result);
    }
}
