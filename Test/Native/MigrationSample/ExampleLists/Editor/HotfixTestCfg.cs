// Fixture for Test/Native/migration_check.py: the examples' Editor [Hotfix] list
// (Assets/XLua/Examples/08_Hotfix/Editor/HotfixTestCfg.cs until source generator
// milestone M11). The examples' types carry [Hotfix] themselves.
using System;
using System.Collections.Generic;
using XLua;

namespace XLuaTest.Editor
{
    public static class HotfixTestCfg
    {
        [Hotfix]
        public static List<Type> by_field = new List<Type>()
        {
            typeof(HotfixTest),
            typeof(HotfixCalc),
            typeof(GenericClass<>),
            typeof(InnerTypeTest),
            typeof(BaseTest),
            typeof(StructTest),
            typeof(GenericStruct<>),
            typeof(StatefullTest)
        };

    }
}