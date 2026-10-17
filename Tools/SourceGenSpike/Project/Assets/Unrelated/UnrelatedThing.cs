// Scope check: this assembly does not reference XLuaTest.Runtime. It declares its own
// internal XLuaTest.ExportAttribute, so if the generator ran here it would find these
// exports and leave an XLuaSGMarker_Unrelated class behind.
[assembly: XLuaTest.Export(typeof(UnityEngine.Vector2))]

namespace XLuaTest
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
    internal sealed class ExportAttribute : System.Attribute
    {
        public ExportAttribute() { }
        public ExportAttribute(System.Type type) { }
    }
}

namespace Unrelated
{
    [XLuaTest.Export]
    public class UnrelatedThing
    {
        public static int Value => 1;
    }
}
