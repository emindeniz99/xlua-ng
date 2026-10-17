using System;

namespace XLuaTest
{
    // Defined by the runtime assembly, not by the generator: the generator never calls
    // RegisterPostInitializationOutput.
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class ExportAttribute : Attribute
    {
        public ExportAttribute() { }
        public ExportAttribute(Type type) { Type = type; }
        public Type Type { get; }
    }
}
