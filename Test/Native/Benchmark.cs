// Bridge, runtime and GC micro-benchmarks for Test/Native/benchmark.py.
//
// Each case runs a fixed number of iterations: warm-up runs first, then timed
// runs (the driver reports min and median ns/op), then one allocation pass
// with the Lua collector stopped that measures managed and Lua bytes per op.
// Results are printed as "XLUA_BENCH {json}" lines; nothing is asserted.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using XLua;

namespace XLuaBench
{
    // With [GCOptimize] and generated code, xLua copies this struct into Lua
    // userdata instead of boxing it.
    [GCOptimize]
    [LuaCallCSharp]
    public struct BenchVec3
    {
        public float x, y, z;
        public BenchVec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    // The same layout without [GCOptimize]: passed to Lua as a boxed object.
    [LuaCallCSharp]
    public struct BenchVec3Boxed
    {
        public float x, y, z;
        public BenchVec3Boxed(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    // Called from Lua. The methods do almost no work, so the bridge dominates.
    [LuaCallCSharp]
    public class BenchTarget
    {
        public int Field;
        public int Prop { get; set; }
        public static long Seed64 = 0x0123456789ABCDEF;
        public static long Mul64 = 6364136223846793005;
        public static long Inc64 = 1442695040888963407;
        static readonly string name = "xlua-bench";

        public static int StaticAdd(int a, int b) { return a + b; }
        public int Add(int a) { return Field + a; }
        public static int StrLen(string s) { return s.Length; }
        public static string Name() { return name; }
        public static BenchVec3 ScaleVec(BenchVec3 v, float s) { v.x *= s; v.y *= s; v.z *= s; return v; }
        public static BenchVec3Boxed ScaleVecBoxed(BenchVec3Boxed v, float s) { v.x *= s; v.y *= s; v.z *= s; return v; }
    }

    [CSharpCallLua] public delegate void RunN(int n);
    [CSharpCallLua] public delegate double NumFn();
    [CSharpCallLua] public delegate int IntBinOp(int a, int b);
    [CSharpCallLua] public delegate int StrOp(string s);
    [CSharpCallLua] public delegate BenchVec3 Vec3Op(BenchVec3 v);
    [CSharpCallLua] public delegate BenchVec3Boxed Vec3BoxedOp(BenchVec3Boxed v);

    class Case
    {
        public string Group, Name;
        public int Iters;
        public Func<Action<int>> Make; // sets the case up and returns its loop
    }

    public static class Program
    {
        static LuaEnv env;
        static NumFn luaKb;
        static Func<long> allocatedBytes; // GC.GetAllocatedBytesForCurrentThread when Mono has it
        static int sink;

        static T Lua<T>(string chunk) where T : class
        {
            var fn = (LuaFunction)env.DoString(chunk)[0];
            T d = fn.Cast<T>();
            fn.Dispose();
            return d;
        }

        // A Lua function(n) that runs the benchmark loop itself.
        static Action<int> LuaLoop(string chunk)
        {
            RunN run = Lua<RunN>("local T = CS.XLuaBench.BenchTarget\nlocal V = CS.XLuaBench.BenchVec3\n"
                + "local VB = CS.XLuaBench.BenchVec3Boxed\n" + chunk);
            return n => run(n);
        }

        static List<Case> Cases(double scale)
        {
            var list = new List<Case>();
            Action<string, string, int, Func<Action<int>>> add = (group, name, iters, make) =>
                list.Add(new Case { Group = group, Name = name, Iters = Math.Max(1, (int)(iters * scale)), Make = make });
            const int B = 200000;

            // Lua -> C#
            add("lua_cs", "static_call", B, () => LuaLoop("local f = T.StaticAdd\nreturn function(n) for i = 1, n do f(i, 1) end end"));
            add("lua_cs", "instance_call", B, () => LuaLoop("local o = T()\nreturn function(n) for i = 1, n do o:Add(i) end end"));
            add("lua_cs", "property_get", B, () => LuaLoop("local o = T()\nreturn function(n) local s = 0 for i = 1, n do s = s + o.Prop end return s end"));
            add("lua_cs", "property_set", B, () => LuaLoop("local o = T()\nreturn function(n) for i = 1, n do o.Prop = i end end"));
            add("lua_cs", "field_get", B, () => LuaLoop("local o = T()\nreturn function(n) local s = 0 for i = 1, n do s = s + o.Field end return s end"));
            add("lua_cs", "field_set", B, () => LuaLoop("local o = T()\nreturn function(n) for i = 1, n do o.Field = i end end"));
            add("lua_cs", "string_arg", B, () => LuaLoop("local f, s = T.StrLen, 'hello xlua'\nreturn function(n) for i = 1, n do f(s) end end"));
            add("lua_cs", "string_return", B, () => LuaLoop("local f = T.Name\nreturn function(n) for i = 1, n do f() end end"));
            add("lua_cs", "struct_gcopt_pass", B, () => LuaLoop("local f, v = T.ScaleVec, V(1, 2, 3)\nreturn function(n) for i = 1, n do v = f(v, 1) end return v end"));
            add("lua_cs", "struct_boxed_pass", B, () => LuaLoop("local f, v = T.ScaleVecBoxed, VB(1, 2, 3)\nreturn function(n) for i = 1, n do v = f(v, 1) end return v end"));
            add("lua_cs", "struct_gcopt_new", B, () => LuaLoop("return function(n) for i = 1, n do local v = V(i, 2, 3) end end"));
            add("lua_cs", "struct_boxed_new", B, () => LuaLoop("return function(n) for i = 1, n do local v = VB(i, 2, 3) end end"));
            add("lua_cs", "struct_gcopt_field_get", B, () => LuaLoop("local v = V(1, 2, 3)\nreturn function(n) local s = 0 for i = 1, n do s = s + v.x end return s end"));
            add("lua_cs", "struct_boxed_field_get", B, () => LuaLoop("local v = VB(1, 2, 3)\nreturn function(n) local s = 0 for i = 1, n do s = s + v.x end return s end"));

            // C# -> Lua
            add("cs_lua", "delegate_int", B, () =>
            {
                IntBinOp f = Lua<IntBinOp>("return function(a, b) return a + b end");
                return n => { for (int i = 0; i < n; i++) sink += f(i, 1); };
            });
            add("cs_lua", "luafunction_call", B, () =>
            {
                var f = (LuaFunction)env.DoString("return function(a, b) return a + b end")[0];
                return n => { for (int i = 0; i < n; i++) sink += f.Call(i, 1).Length; };
            });
            add("cs_lua", "luafunction_func", B, () =>
            {
                var f = (LuaFunction)env.DoString("return function(a, b) return a + b end")[0];
                return n => { for (int i = 0; i < n; i++) sink += f.Func<int, int, int>(i, 1); };
            });
            add("cs_lua", "delegate_string_arg", B, () =>
            {
                StrOp f = Lua<StrOp>("return function(s) return #s end");
                return n => { for (int i = 0; i < n; i++) sink += f("hello xlua"); };
            });
            add("cs_lua", "delegate_struct_gcopt", B, () =>
            {
                Vec3Op f = Lua<Vec3Op>("return function(v) return v end");
                var v = new BenchVec3(1, 2, 3);
                return n => { for (int i = 0; i < n; i++) v = f(v); };
            });
            add("cs_lua", "delegate_struct_boxed", B, () =>
            {
                Vec3BoxedOp f = Lua<Vec3BoxedOp>("return function(v) return v end");
                var v = new BenchVec3Boxed(1, 2, 3);
                return n => { for (int i = 0; i < n; i++) v = f(v); };
            });

            // LuaTable access from C#
            Func<LuaTable> table = () => (LuaTable)env.DoString("local t = { value = 1 } for i = 1, 64 do t[i] = i end return t")[0];
            add("table", "get_int_key", B, () => { var t = table(); return n => { for (int i = 0; i < n; i++) sink += t.Get<int, int>((i & 63) + 1); }; });
            add("table", "set_int_key", B, () => { var t = table(); return n => { for (int i = 0; i < n; i++) t.Set((i & 63) + 1, i); }; });
            add("table", "get_string_key", B, () => { var t = table(); return n => { for (int i = 0; i < n; i++) sink += t.Get<int>("value"); }; });
            add("table", "set_string_key", B, () => { var t = table(); return n => { for (int i = 0; i < n; i++) t.Set("value", i); }; });

            // Pure Lua workloads, Lua 5.1 syntax so LuaJIT runs the same source. Each loop
            // returns or stores its result so that LuaJIT cannot drop the work.
            const int L = 1000000;
            add("lua", "empty_loop", L * 4, () => LuaLoop("return function(n) for i = 1, n do end end"));
            add("lua", "function_call", L, () => LuaLoop("local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end"));
            add("lua", "method_call", L, () => LuaLoop("local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\n"
                + "local o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end"));
            add("lua", "table_array_insert", L, () => LuaLoop("return function(n) local t = {} for i = 1, n do t[i] = i end return t end"));
            add("lua", "table_array_read", L, () => LuaLoop("local t = {} for i = 1, 1024 do t[i] = i end\n"
                + "return function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end"));
            add("lua", "table_hash_set_get", L, () => LuaLoop("local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\n"
                + "return function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end"));
            add("lua", "string_concat", L / 2, () => LuaLoop("return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = 'item' .. i j = j + 1 if j > 64 then j = 1 end end return buf end"));
            add("lua", "string_format", L / 2, () => LuaLoop("local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf end"));
            add("lua", "closure_create_call", L, () => LuaLoop("return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end"));
            add("lua", "int_math", L * 2, () => LuaLoop("return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end"));
            // Lua 5.3+ uses native 64-bit integers; LuaJIT uses xLua's int64 userdata (i64lib.c).
            add("lua", "int64_math", L / 2, () => LuaLoop("local seed, m, c = T.Seed64, T.Mul64, T.Inc64\n"
                + "return function(n) local x = seed for i = 1, n do x = x * m + c end return x end"));

            // Startup cost of a full LuaEnv (state, libraries, translator, init script).
            add("env", "create_dispose", 20, () => n => { for (int i = 0; i < n; i++) new LuaEnv().Dispose(); });
            return list;
        }

        static void Settle()
        {
            env.Tick();
            env.FullGc();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        static long ManagedBytes()
        {
            return allocatedBytes != null ? allocatedBytes() : GC.GetTotalMemory(false);
        }

        static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.AppendFormat("\\u{0:x4}", (int)ch);
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        static void Measure(Case c, string mode, int runs, int warmup)
        {
            Console.WriteLine("XLUA_BENCH_START " + c.Group + "." + c.Name);
            Console.Out.Flush();
            Action<int> run = c.Make();
            for (int w = 0; w < warmup; w++) run(c.Iters);

            var ns = new double[runs];
            int collections = 0;
            for (int r = 0; r < runs; r++)
            {
                Settle();
                int gc0 = GC.CollectionCount(0);
                long start = Stopwatch.GetTimestamp();
                run(c.Iters);
                long ticks = Stopwatch.GetTimestamp() - start;
                collections += GC.CollectionCount(0) - gc0;
                ns[r] = ticks * (1e9 / Stopwatch.Frequency) / c.Iters;
            }

            // Allocation pass. The Lua collector is stopped so collectgarbage("count")
            // grows by every byte Lua allocates; the C# side uses the per-thread
            // allocation counter, or the heap size when Mono does not provide it.
            // A first pass with the collector stopped grows the translator's object pool
            // and Lua's tables to their final size, so the measured pass does not count that.
            int k = c.Group == "env" ? c.Iters : Math.Min(c.Iters, 50000);
            Settle();
            env.StopGc();
            run(k);
            env.RestartGc();
            Settle();
            env.StopGc();
            double kb0 = luaKb();
            int g0 = GC.CollectionCount(0);
            long m0 = ManagedBytes();
            run(k);
            long m1 = ManagedBytes();
            int g1 = GC.CollectionCount(0);
            double kb1 = luaKb();
            env.RestartGc();

            var sorted = (double[])ns.Clone();
            Array.Sort(sorted);
            double median = runs % 2 == 1 ? sorted[runs / 2] : (sorted[runs / 2 - 1] + sorted[runs / 2]) / 2;
            var parts = new List<string>();
            foreach (double v in ns) parts.Add(Num(v));
            Console.WriteLine("XLUA_BENCH {" + string.Join(", ", new[] {
                "\"group\": " + Str(c.Group), "\"name\": " + Str(c.Name), "\"mode\": " + Str(c.Group == "lua" ? "any" : mode),
                "\"iterations\": " + c.Iters, "\"runs_ns_per_op\": [" + string.Join(", ", parts) + "]",
                "\"min_ns_per_op\": " + Num(sorted[0]), "\"median_ns_per_op\": " + Num(median),
                "\"ops_per_sec\": " + Num(Math.Round(1e9 / median)),
                "\"gen0_collections\": " + collections, "\"alloc_iterations\": " + k,
                "\"managed_bytes_per_op\": " + Num((double)(m1 - m0) / k),
                "\"managed_bytes_exact\": " + (allocatedBytes != null || g1 == g0 ? "true" : "false"),
                "\"lua_bytes_per_op\": " + Num((kb1 - kb0) * 1024 / k) }) + "}");
            Console.Out.Flush();
        }

        public static int Main(string[] args)
        {
            string mode = "unknown", groups = "lua_cs,cs_lua,table,lua,env", filter = null;
            int runs = 5, warmup = 1;
            double scale = 1;
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--mode": mode = args[i + 1]; break;
                    case "--runs": runs = int.Parse(args[i + 1]); break;
                    case "--warmup": warmup = int.Parse(args[i + 1]); break;
                    case "--scale": scale = double.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                    case "--groups": groups = args[i + 1]; break;
                    case "--filter": filter = args[i + 1]; break;
                    default: Console.Error.WriteLine("unknown option " + args[i]); return 2;
                }
            }
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            if (method != null) allocatedBytes = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);

            env = new LuaEnv();
            luaKb = Lua<NumFn>("return function() return collectgarbage('count') end");
            env.FullGc();
            object[] info = env.DoString("return _VERSION, jit and jit.version or ''");
            Console.WriteLine("XLUA_BENCH_META {" + string.Join(", ", new[] {
                "\"mode\": " + Str(mode), "\"lua_version\": " + Str((string)info[0]), "\"jit_version\": " + Str((string)info[1]),
                "\"init_lua_kb\": " + Num(luaKb()), "\"runtime\": " + Str(Environment.Version.ToString()),
                "\"managed_alloc_counter\": " + Str(allocatedBytes != null ? "GetAllocatedBytesForCurrentThread" : "GetTotalMemory"),
                "\"runs\": " + runs, "\"warmup\": " + warmup, "\"scale\": " + Num(scale) }) + "}");

            var wanted = new HashSet<string>(groups.Split(','));
            foreach (var c in Cases(scale))
            {
                if (!wanted.Contains(c.Group) || (filter != null && (c.Group + "." + c.Name).IndexOf(filter, StringComparison.Ordinal) < 0))
                    continue;
                try
                {
                    Measure(c, mode, runs, warmup);
                }
                catch (Exception e)
                {
                    env.RestartGc();
                    Console.WriteLine("XLUA_BENCH_ERROR {\"group\": " + Str(c.Group) + ", \"name\": " + Str(c.Name)
                        + ", \"error\": " + Str(e.GetType().Name + ": " + e.Message) + "}");
                }
            }
            Console.WriteLine("XLUA_BENCH_DONE " + sink);
            return 0;
        }
    }
}
