// Regression tests for upstream Tencent/xLua issues (UPSTREAM.md).
// Lua cases: StreamingAssets/luaCallCsUpstream.lua. The types here are not in
// LuaCallCSharp: they use emitted wrappers in the reflection suite and
// reflection in the generated-code suites.
using System.Runtime.InteropServices;

namespace Upstream
{
    // Tencent/xLua#991: optional Nullable<T> parameters. Building the
    // reflection wrapper threw InvalidCastException (Convert.ChangeType to
    // int?), which broke every member of the type.
    public class NullableDefaults
    {
        public static int Plain()
        {
            return 1;
        }

        public static string Zero(int? x = 0)
        {
            return x.HasValue ? "v" + x.Value : "null";
        }

        public static string Null(int? y = null)
        {
            return y.HasValue ? "v" + y.Value : "null";
        }

        public static string Seven(string prefix, long? z = 7)
        {
            return prefix + (z.HasValue ? "v" + z.Value : "null");
        }
    }

    // Tencent/xLua#1105: `in int x = 5`. mcs compiles the `in` keyword as
    // by-value, so XLUA_GENERAL builds spell out what Roslyn emits: [In] ref
    // with IsReadOnlyAttribute and a default constant (see
    // InParamsForLuaCallCS.cs).
    public class InDefaults
    {
        public static int Plain()
        {
            return 1;
        }

#if XLUA_GENERAL
        public static int Five([In, System.Runtime.CompilerServices.IsReadOnly, Optional, DefaultParameterValue(5)] ref int x)
#else
        public static int Five(in int x = 5)
#endif
        {
            return x * 10;
        }
    }
}
